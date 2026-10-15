param(
  [Parameter(Mandatory)][string]$Root,
  [Parameter(Mandatory)][string]$OutDir
)
# Round-89: defect #1 -- an independent loader-install entry point.
#
# Verified in record 55/82: POST /api/versions/download installs VANILLA only, and no route installs a
# loader for an existing instance. The only way to get Forge/Fabric/NeoForge/Quilt today is to import a
# modpack. Everything the fix needs already exists (record 88):
#   - InstancePackService.InstallLoaderAsync(ModLoaderKind, GameInstance, mcVersion, loaderVersion,
#     javaPath, progress) at :475 -- private, needs to become public;
#   - ModLoaderInfo.TryParse(string, out ModLoaderKind) at :326 -- the string->enum entry point;
#   - the four services are dispatched inside InstallLoaderAsync already;
#   - the InstancePackService ctor's four arguments are all available (P-IMPORTLOG / P-PROXYJVM).
#
# Scope note: this patch is the BACKEND half only (visibility + route + handler). A UI button is still
# needed for a user to reach it; that is deliberately left out so the patch stays verifiable on its own.
$ErrorActionPreference = 'Stop'
[void](New-Item -ItemType Directory -Path $OutDir -Force)

$selfBytes = [IO.File]::ReadAllBytes($PSCommandPath)
if (-not ($selfBytes[0] -eq 0xEF -and $selfBytes[1] -eq 0xBB -and $selfBytes[2] -eq 0xBF)) {
  throw "本脚本缺少 UTF-8 BOM：PowerShell 5.1 会按 GBK 读取它，脚本里的中文锚点会静默失配。请先补回 BOM 再运行。"
}

$report = New-Object System.Collections.Generic.List[object]
$S = Join-Path $Root 'dev\PineglowLauncher\Pineglow.Launcher.Services'
$ips = Join-Path $S 'InstancePackService.cs'
$las = Join-Path $S 'LocalApiService.cs'
$L = [char]10

function Write-PreservingEncoding([string]$file, [string]$text) {
  $bytes = [IO.File]::ReadAllBytes($file)
  $hasBom = ($bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF)
  [IO.File]::WriteAllText($file, $text, (New-Object Text.UTF8Encoding($hasBom)))
}

function Patch([string]$file, [string]$old, [string]$new, [string]$label) {
  $text = [IO.File]::ReadAllText($file)
  $usesCrlf = $text.Contains("`r`n")
  if ($usesCrlf) { $old = $old -replace "`r?`n", "`r`n"; $new = $new -replace "`r?`n", "`r`n" }
  else { $old = $old -replace "`r?`n", "`n"; $new = $new -replace "`r?`n", "`n" }
  $count = ([regex]::Matches($text, [regex]::Escape($old))).Count
  if ($count -ne 1) { throw "[$label] expected exactly 1 match in $file but found $count" }
  Write-PreservingEncoding $file $text.Replace($old, $new)
  $report.Add([pscustomobject]@{ label = $label; ok = $true })
  Write-Output "[OK] $label"
}

# ---- 1. make the dispatcher reachable
$old1 = @(
'	private async Task<InstalledLoaderProfile> InstallLoaderAsync(ModLoaderKind kind, GameInstance instance, string mcVersion, string loaderVersion, string javaPath, Action<string> progress)'
) -join $L
$new1 = @(
'	// FIX-LOADERENTRY：改为 public —— 此前它只服务于整合包导入，于是"给已存在的实例装加载器"没有入口',
'	// （缺陷 1）。公开它是这个功能唯一的结构性改动；四个分支的分派逻辑本身不用动。',
'	public async Task<InstalledLoaderProfile> InstallLoaderAsync(ModLoaderKind kind, GameInstance instance, string mcVersion, string loaderVersion, string javaPath, Action<string> progress)'
) -join $L
Patch $ips $old1 $new1 'P-LOADERENTRY 1/3 InstallLoaderAsync becomes public'

# ---- 2. register the route, right after the import route
$old2 = @(
'			if (seg.Length == 3 && seg[1].Equals("instances", StringComparison.OrdinalIgnoreCase) && seg[2].Equals("import", StringComparison.OrdinalIgnoreCase) && method == "POST")',
'			{',
'				await HandleImportInstanceAsync(request, response);',
'				return;',
'			}'
) -join $L
$new2 = @(
'			if (seg.Length == 3 && seg[1].Equals("instances", StringComparison.OrdinalIgnoreCase) && seg[2].Equals("import", StringComparison.OrdinalIgnoreCase) && method == "POST")',
'			{',
'				await HandleImportInstanceAsync(request, response);',
'				return;',
'			}',
'',
'			// FIX-LOADERENTRY：为**已存在的实例**安装加载器（缺陷 1）。此前只有整合包导入能装。',
'			// URL 形状：/api/instances/{id}/loader → seg[2] 是实例 id、seg[3] 是 "loader"。',
'			if (seg.Length == 4 && seg[1].Equals("instances", StringComparison.OrdinalIgnoreCase) && seg[3].Equals("loader", StringComparison.OrdinalIgnoreCase) && method == "POST")',
'			{',
'				await HandleInstallLoaderAsync(request, response, seg[2]);',
'				return;',
'			}'
) -join $L
Patch $las $old2 $new2 'P-LOADERENTRY 2/3 route POST /api/instances/{id}/loader'

# ---- 3. the handler, placed just before the import handler
$old3 = @(
'	private static async Task HandleImportInstanceAsync(HttpListenerRequest request, HttpListenerResponse response)'
) -join $L
$new3 = @(
'	/// <summary>',
'	/// FIX-LOADERENTRY：给**已存在的实例**安装加载器（缺陷 1）。',
'	/// 走的是整合包导入内部同一套分派（<see cref="InstancePackService.InstallLoaderAsync"/>），',
'	/// 所以四种加载器的行为与导入时完全一致；OptiFine 不在四种之内，会被 TryParse 明确拒绝。',
'	/// </summary>',
'	private static async Task HandleInstallLoaderAsync(HttpListenerRequest request, HttpListenerResponse response, string instanceId)',
'	{',
'		JsonDocument body = await ReadBodyAsync(request);',
'		string loaderKind = JsonUtil.GetString(body.RootElement, "loaderType")?.Trim() ?? string.Empty;',
'		string loaderVersion = JsonUtil.GetString(body.RootElement, "loaderVersion")?.Trim() ?? string.Empty;',
'		if (string.IsNullOrWhiteSpace(loaderKind))',
'		{',
'			throw new InvalidOperationException("loaderType 不能为空");',
'		}',
'		if (string.IsNullOrWhiteSpace(loaderVersion))',
'		{',
'			throw new InvalidOperationException("loaderVersion 不能为空");',
'		}',
'		if (!ModLoaderInfo.TryParse(loaderKind, out ModLoaderKind kind))',
'		{',
'			throw new InvalidOperationException("不支持的加载器：" + loaderKind + "（支持 Fabric / Quilt / Forge / NeoForge）");',
'		}',
'',
'		AppConfig config = LoadConfig();',
'		GameInstance instance = RequireInstance(config, instanceId);',
'		string mcVersion = string.IsNullOrWhiteSpace(instance.BaseMinecraftVersion)',
'			? instance.MinecraftVersion',
'			: instance.BaseMinecraftVersion;',
'		if (string.IsNullOrWhiteSpace(mcVersion))',
'		{',
'			throw new InvalidOperationException("实例未关联游戏版本，请先安装原版版本");',
'		}',
'		string defaultGameDir = string.IsNullOrWhiteSpace(config.GameDirectory)',
'			? PlatformPaths.GetDefaultMinecraftDirectory()',
'			: config.GameDirectory;',
'		string javaPath = ResolveJavaPath(config, instance, mcVersion);',
'',
'		InstancePackService packService = new InstancePackService(',
'			ApiHttp, defaultGameDir, CreateImportLogger(), BuildLoaderInstallerJvmArgs(config));',
'		InstalledLoaderProfile profile = await packService.InstallLoaderAsync(',
'			kind, instance, mcVersion, loaderVersion, javaPath, _ => { });',
'',
'		// 回写实例字段：与整合包导入之后的处理保持一致（版本 id 换成加载器 profile 的版本 id）。',
'		AppConfig latest = LoadConfig();',
'		GameInstance target = RequireInstance(latest, instanceId);',
'		target.MinecraftVersion = profile.VersionId;',
'		target.LoaderType = kind.ToString();',
'		target.LoaderVersion = loaderVersion;',
'		target.BaseMinecraftVersion = mcVersion;',
'		SaveConfig(latest);',
'',
'		await WriteJsonAsync(response, 200, new',
'		{',
'			ok = true,',
'			versionId = profile.VersionId,',
'			loaderType = kind.ToString(),',
'			loaderVersion,',
'			minecraftVersion = mcVersion',
'		});',
'	}',
'',
'	private static async Task HandleImportInstanceAsync(HttpListenerRequest request, HttpListenerResponse response)'
) -join $L
Patch $las $old3 $new3 'P-LOADERENTRY 3/3 the handler'

$report | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $OutDir 'loaderentry-patch-report.json') -Encoding utf8
Write-Output ("steps applied = {0}" -f $report.Count)
