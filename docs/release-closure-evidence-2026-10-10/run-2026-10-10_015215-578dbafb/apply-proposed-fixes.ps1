param(
  [Parameter(Mandatory)][string]$Root,
  [Parameter(Mandatory)][string]$OutDir
)
# Round-20: apply the three proposed fixes to an ISOLATED copy and validate them.
# $Root is a throwaway copy (dev/ + ui/ + global.json). The real product tree is never touched.
$ErrorActionPreference = 'Stop'
[void](New-Item -ItemType Directory -Path $OutDir -Force)

# Recurring failure mode in this repo (hit five times now): a non-ASCII .ps1 without a UTF-8 BOM is
# read as GBK by Windows PowerShell 5.1, so the Chinese anchors below silently stop matching while
# pure-ASCII anchors keep working. Fail loudly instead of producing a half-applied patch set.
$selfBytes = [IO.File]::ReadAllBytes($PSCommandPath)
if (-not ($selfBytes[0] -eq 0xEF -and $selfBytes[1] -eq 0xBB -and $selfBytes[2] -eq 0xBF)) {
  throw "本脚本缺少 UTF-8 BOM：PowerShell 5.1 会按 GBK 读取它，脚本里的中文锚点会静默失配。请先补回 BOM 再运行。"
}

$S = Join-Path $Root 'dev\PineglowLauncher\Pineglow.Launcher.Services'
$T = Join-Path $Root 'dev\PineglowLauncher.Tests'
$report = New-Object System.Collections.Generic.List[object]

function Write-PreservingEncoding([string]$file, [string]$text) {
  # Keep the file's original encoding exactly: these .cs files carry no BOM, and adding one
  # would show up as an unrelated change in the patch.
  $bytes = [IO.File]::ReadAllBytes($file)
  $hasBom = ($bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF)
  [IO.File]::WriteAllText($file, $text, (New-Object Text.UTF8Encoding($hasBom)))
}

function Patch([string]$file, [string]$old, [string]$new, [string]$label) {
  $text = [IO.File]::ReadAllText($file)
  # The here-strings in this script may carry either LF or CRLF depending on how the script was
  # edited; normalise them to whatever the TARGET file actually uses before matching.
  $usesCrlf = $text.Contains("`r`n")
  if ($usesCrlf) {
    $old = $old -replace "`r?`n", "`r`n"
    $new = $new -replace "`r?`n", "`r`n"
  } else {
    $old = $old -replace "`r?`n", "`n"
    $new = $new -replace "`r?`n", "`n"
  }
  $count = ([regex]::Matches($text, [regex]::Escape($old))).Count
  if ($count -ne 1) { throw "[$label] expected exactly 1 match in $file but found $count" }
  $text = $text.Replace($old, $new)
  Write-PreservingEncoding $file $text
  $report.Add([pscustomobject]@{ label = $label; file = $file.Substring($Root.Length + 1); ok = $true })
  Write-Output "[OK] $label  ->  $($file.Substring($Root.Length + 1))"
}

# ---------------------------------------------------------------- P-UA
$uaOld = "`t`treturn new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(timeoutSeconds) };"
$uaNew = @'
		HttpClient client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(timeoutSeconds) };
		// FIX-UA: mirror redirects land on academic mirrors that answer 403 to a request with no
		// User-Agent (and also to browser-looking ones). A tool-style UA is accepted, and this is the
		// convention already used elsewhere in this codebase (MinecraftNewsService / UpdateService).
		client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "PineglowLauncher/" + LauncherService.LauncherVersion);
		return client;
'@
Patch (Join-Path $S 'LocalApiService.cs') $uaOld $uaNew 'P-UA download/metadata client User-Agent'

# ---------------------------------------------------------------- P-FABRICMETA
$fabricOld = @'
		if (url.Contains("meta.fabricmc.net", StringComparison.Ordinal))
		{
			return ReplaceHost(url, baseUrl);
		}
'@
$fabricNew = @'
		if (url.Contains("meta.fabricmc.net", StringComparison.Ordinal))
		{
			// FIX-FABRICMETA: BMCLAPI serves Fabric meta under /fabric-meta/v2/...; the bare
			// "<base> + path" form answers 404 (verified with curl). Quilt's mapping below
			// already carries its own prefix, which is why only Fabric was broken.
			return ReplaceHost(url, baseUrl + "/fabric-meta");
		}
'@
Patch (Join-Path $S 'MirrorHelper.cs') $fabricOld $fabricNew 'P-FABRICMETA Fabric meta mirror prefix'

# ---------------------------------------------------------------- P-PROXYJVM (1/6)
$helperOld = @'
	private readonly Action<string> _log;

	public string GameDirectory => _gameDirectory;

	public LoaderInstallHelper(HttpClient http, string gameDirectory, MinecraftDownloader downloader, Action<string> log)
	{
		_http = http;
'@
$helperNew = @'
	private readonly Action<string> _log;

	// FIX-PROXYJVM: JVM options handed to the loader INSTALLER process (e.g. -Dhttps.proxyHost=...).
	// The installer is a separate JVM and does NOT inherit the launcher's HttpClient proxy settings.
	private readonly IReadOnlyList<string> _extraJvmArgs;

	public string GameDirectory => _gameDirectory;

	public LoaderInstallHelper(HttpClient http, string gameDirectory, MinecraftDownloader downloader, Action<string> log, IReadOnlyList<string>? extraJvmArgs = null)
	{
		_http = http;
		_extraJvmArgs = extraJvmArgs ?? Array.Empty<string>();
'@
Patch (Join-Path $S 'LoaderInstallHelper.cs') $helperOld $helperNew 'P-PROXYJVM 1/6 helper ctor'

$argOld = @'
		processStartInfo.ArgumentList.Add("-jar");
		processStartInfo.ArgumentList.Add(installerPath);
'@
$argNew = @'
		// FIX-PROXYJVM: JVM options must precede -jar. Without a proxy the installer tries to download
		// the vanilla client directly and fails ("Downloading minecraft client failed, invalid checksum").
		foreach (string jvmArg in _extraJvmArgs)
		{
			processStartInfo.ArgumentList.Add(jvmArg);
		}
		processStartInfo.ArgumentList.Add("-jar");
		processStartInfo.ArgumentList.Add(installerPath);
'@
Patch (Join-Path $S 'LoaderInstallHelper.cs') $argOld $argNew 'P-PROXYJVM 2/6 helper before -jar'

# ---------------------------------------------------------------- P-PROXYJVM (3/6)
$ilsOld = @'
	protected InstallerLoaderService(HttpClient http, string gameDirectory, MinecraftDownloader downloader, Action<string> log, string metadataUrl, string artifactBaseUrl, string groupPath, string artifactId, ModLoaderKind kind, string label)
	{
		_http = http;
		_helper = new LoaderInstallHelper(http, gameDirectory, downloader, log);
'@
$ilsNew = @'
	protected InstallerLoaderService(HttpClient http, string gameDirectory, MinecraftDownloader downloader, Action<string> log, string metadataUrl, string artifactBaseUrl, string groupPath, string artifactId, ModLoaderKind kind, string label, IReadOnlyList<string>? installerJvmArgs = null)
	{
		_http = http;
		_helper = new LoaderInstallHelper(http, gameDirectory, downloader, log, installerJvmArgs);
'@
Patch (Join-Path $S 'InstallerLoaderService.cs') $ilsOld $ilsNew 'P-PROXYJVM 3/6 installer base ctor'

# ---------------------------------------------------------------- P-PROXYJVM (4/6, 5/6)
foreach ($pair in @(
    @{ file = 'ForgeService.cs';    kind = 'Forge' },
    @{ file = 'NeoForgeService.cs'; kind = 'NeoForge' }
  )) {
  $text = [IO.File]::ReadAllText((Join-Path $S $pair.file))
  $old = "`tpublic $($pair.kind)Service(HttpClient http, string gameDirectory, MinecraftDownloader downloader, Action<string> log)`r`n`t`t: base(http, gameDirectory, downloader, log,"
  $new = "`tpublic $($pair.kind)Service(HttpClient http, string gameDirectory, MinecraftDownloader downloader, Action<string> log, IReadOnlyList<string>? installerJvmArgs = null)`r`n`t`t: base(http, gameDirectory, downloader, log,"
  $c = ([regex]::Matches($text, [regex]::Escape($old))).Count
  if ($c -eq 0) {
    # file may use LF line endings
    $old = $old.Replace("`r`n", "`n"); $new = $new.Replace("`r`n", "`n")
    $c = ([regex]::Matches($text, [regex]::Escape($old))).Count
  }
  if ($c -ne 1) { throw "[P-PROXYJVM $($pair.file)] ctor signature not found exactly once (found $c)" }
  $text = $text.Replace($old, $new)
  $tailOld = "ModLoaderKind.$($pair.kind), `"$($pair.kind)`")"
  $tailNew = "ModLoaderKind.$($pair.kind), `"$($pair.kind)`", installerJvmArgs)"
  if (([regex]::Matches($text, [regex]::Escape($tailOld))).Count -ne 1) { throw "[P-PROXYJVM $($pair.file)] base call tail not found once" }
  $text = $text.Replace($tailOld, $tailNew)
  # add the using for IReadOnlyList
  if ($text -notmatch 'using System\.Collections\.Generic;') {
    $text = $text.Replace("using System.Net.Http;", "using System.Collections.Generic;`r`nusing System.Net.Http;")
  }
  Write-PreservingEncoding (Join-Path $S $pair.file) $text
  $report.Add([pscustomobject]@{ label = "P-PROXYJVM $($pair.file)"; file = $pair.file; ok = $true })
  Write-Output "[OK] P-PROXYJVM $($pair.file)"
}

# ---------------------------------------------------------------- P-PROXYJVM (6/6) InstancePackService
$ips = Join-Path $S 'InstancePackService.cs'
$ipsOld = @'
	public InstancePackService(HttpClient http, string defaultGameDir, Action<string> log)
'@
$ipsNew = @'
	// FIX-PROXYJVM: JVM options forwarded to the Forge/NeoForge installer process.
	private readonly IReadOnlyList<string> _installerJvmArgs;

	public InstancePackService(HttpClient http, string defaultGameDir, Action<string> log, IReadOnlyList<string>? installerJvmArgs = null)
'@
Patch $ips $ipsOld $ipsNew 'P-PROXYJVM 6a/8 InstancePackService ctor'
$ipsBodyOld = @'
		_log = log;
'@
$ipsBodyNew = @'
		_log = log;
		_installerJvmArgs = installerJvmArgs ?? Array.Empty<string>();
'@
Patch $ips $ipsBodyOld $ipsBodyNew 'P-PROXYJVM 6b/8 InstancePackService field init'
Patch $ips 'new ForgeService(_http, gameDir, downloader, _log)' 'new ForgeService(_http, gameDir, downloader, _log, _installerJvmArgs)' 'P-PROXYJVM 7/8 ForgeService call'
Patch $ips 'new NeoForgeService(_http, gameDir, downloader, _log)' 'new NeoForgeService(_http, gameDir, downloader, _log, _installerJvmArgs)' 'P-PROXYJVM 8/8 NeoForgeService call'

# ---------------------------------------------------------------- P-PROXYJVM LocalApiService call sites
$las = Join-Path $S 'LocalApiService.cs'
$siteOld1 = @'
			string defaultGameDir = string.IsNullOrWhiteSpace(config.GameDirectory) ? PlatformPaths.GetDefaultMinecraftDirectory() : config.GameDirectory;
			InstancePackService packService = new InstancePackService(ApiHttp, defaultGameDir, _ => { });
'@
$siteNew1 = @'
			string defaultGameDir = string.IsNullOrWhiteSpace(config.GameDirectory) ? PlatformPaths.GetDefaultMinecraftDirectory() : config.GameDirectory;
			InstancePackService packService = new InstancePackService(ApiHttp, defaultGameDir, _ => { }, BuildLoaderInstallerJvmArgs(config));
'@
Patch $las $siteOld1 $siteNew1 'P-PROXYJVM site A (instance pack import)'

$siteOld2 = @'
			string defaultGameDir = string.IsNullOrWhiteSpace(config.GameDirectory) ? PlatformPaths.GetDefaultMinecraftDirectory() : config.GameDirectory;
			InstancePackService packService = new InstancePackService(ApiHttp, defaultGameDir, msg => UpdateContentTask(entry, msg));
'@
$siteNew2 = @'
			string defaultGameDir = string.IsNullOrWhiteSpace(config.GameDirectory) ? PlatformPaths.GetDefaultMinecraftDirectory() : config.GameDirectory;
			InstancePackService packService = new InstancePackService(ApiHttp, defaultGameDir, msg => UpdateContentTask(entry, msg), BuildLoaderInstallerJvmArgs(config));
'@
Patch $las $siteOld2 $siteNew2 'P-PROXYJVM site B (modrinth modpack import)'

$helperMethodOld = @'
	private static void RefreshApiHttpProxy(AppConfig config)
'@
$helperMethodNew = @'
	// FIX-PROXYJVM: the loader installer is a separate JVM; it only sees a proxy if we pass the
	// standard -Dhttp.proxyHost/-Dhttps.proxyHost options on its command line. Same values the game
	// launch already uses for its own JVM (see the launch path below).
	private static IReadOnlyList<string> BuildLoaderInstallerJvmArgs(AppConfig config)
	{
		if (!config.ProxyEnabled || string.IsNullOrWhiteSpace(config.ProxyHost) || config.ProxyPort <= 0)
		{
			return Array.Empty<string>();
		}
		return new string[]
		{
			$"-Dhttp.proxyHost={config.ProxyHost}",
			$"-Dhttp.proxyPort={config.ProxyPort}",
			$"-Dhttps.proxyHost={config.ProxyHost}",
			$"-Dhttps.proxyPort={config.ProxyPort}"
		};
	}

	private static void RefreshApiHttpProxy(AppConfig config)
'@
Patch $las $helperMethodOld $helperMethodNew 'P-PROXYJVM helper method'

# ---------------------------------------------------------------- regression test for P-FABRICMETA
$testFile = Join-Path $T 'MirrorFabricMetaMappingTests.cs'
$testSrc = @'
using Pineglow.Launcher.Services;

namespace Pineglow.Launcher.Tests;

/// <summary>
/// Regression test for the Fabric meta mirror mapping. Nothing in the suite covered
/// MirrorHelper.Rewrite at all, which is why "<base> + path" (404) survived unnoticed.
/// </summary>
public sealed class MirrorFabricMetaMappingTests
{
    [Fact]
    public void Rewrite_MapsFabricMetaUnderFabricMetaPrefix()
    {
        string previous = MirrorHelper.SelectedKey;
        try
        {
            MirrorHelper.SelectedKey = "bmclapi";
            string rewritten = MirrorHelper.Rewrite("https://meta.fabricmc.net/v2/versions/loader");
            Assert.Equal("https://bmclapi2.bangbang93.com/fabric-meta/v2/versions/loader", rewritten);
        }
        finally
        {
            MirrorHelper.SelectedKey = previous;
        }
    }

    [Fact]
    public void Rewrite_QuiltMetaKeepsItsOwnPrefix()
    {
        string previous = MirrorHelper.SelectedKey;
        try
        {
            MirrorHelper.SelectedKey = "bmclapi";
            string rewritten = MirrorHelper.Rewrite("https://meta.quiltmc.org/v3/versions");
            Assert.Equal("https://bmclapi2.bangbang93.com/quilt-meta/v3/versions", rewritten);
        }
        finally
        {
            MirrorHelper.SelectedKey = previous;
        }
    }
}
'@
[IO.File]::WriteAllText($testFile, $testSrc, (New-Object Text.UTF8Encoding($false)))
$report.Add([pscustomobject]@{ label = 'P-TEST Fabric meta mapping'; file = 'MirrorFabricMetaMappingTests.cs'; ok = $true })
Write-Output "[OK] P-TEST added"

# ---------------------------------------------------------------- P-FAKEIP
$nip = Join-Path $S 'NewsImageProxyService.cs'
$fakeOld = @'
    /// <summary>198.18.0.0/15（含 IPv4-mapped 形式）：基准测试保留段，默认拒绝。</summary>
    public static bool IsFakeIpBenchmarkAddress(IPAddress address)
    {
        IPAddress candidate = address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;
        if (candidate.AddressFamily != AddressFamily.InterNetwork)
        {
            return false;
        }

        byte[] b = candidate.GetAddressBytes();
        return b[0] == 198 && (b[1] & 0xFE) == 18;
    }
'@
$fakeNew = @'
    /// <summary>
    /// 基准测试保留段：198.18.0.0/15（含 IPv4-mapped 形式）与 2001:2::/48（RFC 5180，IPv6 对应段）。
    /// 两者都是 Clash 类 TUN/fake-ip 模式在双栈机器上会返回的地址，默认拒绝。
    /// </summary>
    public static bool IsFakeIpBenchmarkAddress(IPAddress address)
    {
        IPAddress candidate = address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;
        byte[] b = candidate.GetAddressBytes();
        if (candidate.AddressFamily == AddressFamily.InterNetwork)
        {
            return b[0] == 198 && (b[1] & 0xFE) == 18;
        }
        if (candidate.AddressFamily == AddressFamily.InterNetworkV6)
        {
            // FIX-FAKEIP: IPv6 基准测试段 2001:2::/48。以前只认 IPv4，于是双栈机器上
            // 「198.18.x.x + 2001:2::x」被当成"fake-ip 与其他类别混合"，兼容开关永远救不回来。
            return b[0] == 0x20 && b[1] == 0x01 && b[2] == 0x00 && b[3] == 0x02;
        }
        return false;
    }
'@
Patch $nip $fakeOld $fakeNew 'P-FAKEIP 1/2 recognise 2001:2::/48'

$pickOld = @'
            return fakeIp[0];
'@
$pickNew = @'
            // FIX-FAKEIP: a dual-stack fake-ip pair is [IPv4, IPv6]; pin the IPv4 one, which is the
            // address the TUN/fake-ip proxy actually accepts.
            return fakeIp.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork) ?? fakeIp[0];
'@
Patch $nip $pickOld $pickNew 'P-FAKEIP 2/2 prefer the IPv4 fake-ip address'

# ---------------------------------------------------------------- P-IMPORTLOG
$las2 = Join-Path $S 'LocalApiService.cs'
Patch $las2 'new InstancePackService(ApiHttp, defaultGameDir, _ => { }, BuildLoaderInstallerJvmArgs(config));' 'new InstancePackService(ApiHttp, defaultGameDir, CreateImportLogger(), BuildLoaderInstallerJvmArgs(config));' 'P-IMPORTLOG 1/3 logger for instance-pack import'
Patch $las2 'newInstance = await packService.ImportModrinthPackAsync(zipPath, _ => { }, ct);' 'newInstance = await packService.ImportModrinthPackAsync(zipPath, CreateImportLogger(), ct);' 'P-IMPORTLOG 2/3 logger for uploaded-pack progress'

# The first cut of this patch wrote EVERY progress tick to the durable log; an in-situ measurement
# showed ~3200 lines in 150 seconds (~21/second). Throttle byte-level ticks to one per second and
# always let step messages through.
$impAnchor = @'
	// FIX-PROXYJVM: the loader installer is a separate JVM; it only sees a proxy if we pass the
'@
$impInsert = @'
	// FIX-IMPORTLOG: route the import's log/progress stream into the durable launcher log. The
	// /api/instances/import path used to pass a no-op delegate, so a 40-minute import left the log
	// completely empty. Byte-level download ticks arrive ~20x/second, so they are throttled to at
	// most one per second; step messages always go through.
	private static readonly Regex ImportProgressTick = new Regex(
		@"\d+(\.\d+)?\s*(B|KB|MB|GB)\s*/\s*\d+(\.\d+)?\s*(B|KB|MB|GB)",
		RegexOptions.Compiled);

	private static Action<string> CreateImportLogger()
	{
		object gate = new object();
		DateTime lastTickUtc = DateTime.MinValue;
		return delegate(string message)
		{
			if (string.IsNullOrWhiteSpace(message))
			{
				return;
			}

			if (ImportProgressTick.IsMatch(message))
			{
				lock (gate)
				{
					if ((DateTime.UtcNow - lastTickUtc).TotalMilliseconds < 1000.0)
					{
						return;
					}

					lastTickUtc = DateTime.UtcNow;
				}
			}

			LauncherLog.Info("[Import] " + message);
		};
	}

	// FIX-PROXYJVM: the loader installer is a separate JVM; it only sees a proxy if we pass the
'@
Patch $las2 $impAnchor $impInsert 'P-IMPORTLOG 3/3 throttled logger factory'

# ---------------------------------------------------------------- fake-ip regression tests
$testFile2 = Join-Path $T 'NewsFakeIpDualStackTests.cs'
$testSrc2 = @'
using System.Net;
using Pineglow.Launcher.Services;

namespace Pineglow.Launcher.Tests;

/// <summary>
/// Regression tests for the Clash-style TUN/fake-ip compatibility switch on a DUAL-STACK machine.
/// The resolver returns one IPv4 from 198.18.0.0/15 and one IPv6 from 2001:2::/48 (RFC 5180);
/// before the fix only the IPv4 one counted as "fake-ip", so the opt-in switch could never help.
/// </summary>
public sealed class NewsFakeIpDualStackTests
{
    private static IPAddress[] DualStack() => new[]
    {
        IPAddress.Parse("198.18.0.33"),
        IPAddress.Parse("2001:2::21"),
    };

    [Fact]
    public void DualStackFakeIp_IsRejectedWhenCompatIsOff()
    {
        // After the fix BOTH addresses count as benchmark ranges, so the "every address is a
        // benchmark range" branch fires and the kind becomes dns_private. Before the fix only the
        // IPv4 one was recognised, so the same input produced dns_mixed ("mixed with other
        // classes"). Either way the request is REJECTED while the opt-in switch is off.
        NewsImageException ex = Assert.Throws<NewsImageException>(
            () => NewsSsrfGuard.SelectConnectableAddress(DualStack(), allowFakeIpRanges: false));
        Assert.Equal("dns_private", ex.Kind);
    }

    [Fact]
    public void DualStackFakeIp_ResolvesToTheIpv4AddressWhenCompatIsOn()
    {
        IPAddress picked = NewsSsrfGuard.SelectConnectableAddress(DualStack(), allowFakeIpRanges: true);
        Assert.Equal(IPAddress.Parse("198.18.0.33"), picked);
    }

    [Fact]
    public void RealPrivateAddressMixedWithFakeIp_IsStillRejected()
    {
        IPAddress[] mixed = new[] { IPAddress.Parse("192.168.1.10"), IPAddress.Parse("198.18.0.33") };
        NewsImageException ex = Assert.Throws<NewsImageException>(
            () => NewsSsrfGuard.SelectConnectableAddress(mixed, allowFakeIpRanges: true));
        Assert.Equal("dns_mixed", ex.Kind);
    }

    [Fact]
    public void PublicAddressMixedWithFakeIp_IsStillRejected()
    {
        IPAddress[] mixed = new[] { IPAddress.Parse("1.1.1.1"), IPAddress.Parse("198.18.0.33") };
        NewsImageException ex = Assert.Throws<NewsImageException>(
            () => NewsSsrfGuard.SelectConnectableAddress(mixed, allowFakeIpRanges: true));
        Assert.Equal("dns_mixed", ex.Kind);
    }
}
'@
[IO.File]::WriteAllText($testFile2, $testSrc2, (New-Object Text.UTF8Encoding($false)))
$report.Add([pscustomobject]@{ label = 'P-TEST fake-ip dual stack'; file = 'NewsFakeIpDualStackTests.cs'; ok = $true })
Write-Output "[OK] P-TEST fake-ip added"

$report | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $OutDir 'patch-report.json') -Encoding utf8
Write-Output ("patches applied = {0}" -f $report.Count)
