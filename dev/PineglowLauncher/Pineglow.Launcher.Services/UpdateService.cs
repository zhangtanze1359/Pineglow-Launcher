using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Pineglow.Launcher.Models;

namespace Pineglow.Launcher.Services;

/// <summary>
/// 自更新：从 manifest（GET updateUrl 返回 JSON）检查版本 → 下载新 exe 到 updates 目录 →
/// 校验 SHA256 → 生成替换脚本，待主进程退出后替换并重启。
/// </summary>
public static class UpdateService
{
	public static bool IsNewerVersion(string latest, string current)
	{
		if (string.IsNullOrWhiteSpace(latest) || string.IsNullOrWhiteSpace(current))
		{
			return false;
		}
		string[] a = latest.Trim().Split('.');
		string[] b = current.Trim().Split('.');
		int len = Math.Max(a.Length, b.Length);
		for (int i = 0; i < len; i++)
		{
			int x = i < a.Length && int.TryParse(a[i], out int av) ? av : 0;
			int y = i < b.Length && int.TryParse(b[i], out int bv) ? bv : 0;
			if (x != y)
			{
				return x > y;
			}
		}
		return false;
	}

	/// <summary>
	/// 单次清单拉取的结果（R03）：成功携带清单，失败携带**可读的分类原因**。
	/// 分类而不是只抛异常，是因为用户看到的必须是"主源 404 / 备用源超时"这类可判断的信息。
	/// </summary>
	public sealed record ManifestAttempt(string Url, bool Ok, string Reason);

	public sealed record ManifestFetchResult(
		UpdateManifest? Manifest,
		string SourceUrl,
		bool UsedFallback,
		IReadOnlyList<ManifestAttempt> Attempts)
	{
		public bool Ok => Manifest != null;

		/// <summary>给用户/API 的一句话失败摘要（只含分类原因，不含凭据或完整 URL 参数）。</summary>
		public string FailureSummary
		{
			get
			{
				if (Ok)
				{
					return string.Empty;
				}

				if (Attempts.Count == 0)
				{
					return "没有可用的更新清单地址";
				}

				System.Text.StringBuilder sb = new System.Text.StringBuilder("更新清单不可用：");
				for (int i = 0; i < Attempts.Count; i++)
				{
					if (i > 0)
					{
						sb.Append("；");
					}

					sb.Append(i == 0 ? "主源" : $"备用源{i}");
					sb.Append(' ').Append(Attempts[i].Reason);
				}

				return sb.ToString();
			}
		}
	}

	/// <summary>
	/// 默认的「单次尝试」超时。主源卡住时不能让备用源的预算被一起吃掉。
	/// </summary>
	public static readonly TimeSpan DefaultManifestAttemptTimeout = TimeSpan.FromSeconds(15);

	/// <summary>
	/// 组装清单地址链（R03）：用户自定义源优先（保留用户意图），其后是内置主源与备用源，按序去重。
	/// 只接受绝对 http/https 地址，其它一律丢弃（防止把本地路径或空串当成更新源）。
	/// </summary>
	public static IReadOnlyList<string> BuildManifestUrlChain(string? configuredUrl)
	{
		List<string> chain = new List<string>();
		AddCandidate(chain, configuredUrl);
		AddCandidate(chain, ConfigStore.DefaultUpdateUrl);
		foreach (string mirror in ConfigStore.DefaultUpdateMirrorUrls)
		{
			AddCandidate(chain, mirror);
		}

		return chain;
	}

	private static void AddCandidate(List<string> chain, string? candidate)
	{
		string value = candidate?.Trim() ?? string.Empty;
		if (value.Length == 0)
		{
			return;
		}

		if (!Uri.TryCreate(value, UriKind.Absolute, out Uri? uri))
		{
			return;
		}

		if (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp)
		{
			return;
		}

		if (!chain.Any(existing => string.Equals(existing, value, StringComparison.OrdinalIgnoreCase)))
		{
			chain.Add(value);
		}
	}

	/// <summary>
	/// 按地址链依次尝试取清单：主源失败（404 / 超时 / 坏 JSON / 字段不合法）时降级到备用源。
	/// 调用方自身的取消令牌被触发时**不**继续降级（那是用户主动取消，不是源故障）。
	/// </summary>
	public static Task<ManifestFetchResult> FetchManifestWithFallbackAsync(
		HttpClient http,
		string? configuredUrl,
		CancellationToken ct,
		TimeSpan? perAttemptTimeout = null)
	{
		return FetchManifestChainAsync(http, BuildManifestUrlChain(configuredUrl), ct, perAttemptTimeout);
	}

	/// <summary>
	/// 显式指定地址链的版本：生产路径由 <see cref="BuildManifestUrlChain"/> 组装，
	/// 测试用本地假服务器提供的地址链直接驱动降级逻辑（不去打真实 Gitee/GitHub）。
	/// </summary>
	public static async Task<ManifestFetchResult> FetchManifestChainAsync(
		HttpClient http,
		IReadOnlyList<string> chain,
		CancellationToken ct,
		TimeSpan? perAttemptTimeout = null)
	{
		List<ManifestAttempt> attempts = new List<ManifestAttempt>();
		TimeSpan timeout = perAttemptTimeout ?? DefaultManifestAttemptTimeout;

		for (int i = 0; i < chain.Count; i++)
		{
			string url = chain[i];
			CancellationTokenSource? attemptCts = null;
			try
			{
				attemptCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
				attemptCts.CancelAfter(timeout);
				UpdateManifest? manifest = await FetchManifestAsync(http, url, attemptCts.Token).ConfigureAwait(false);
				attempts.Add(new ManifestAttempt(url, true, "OK"));
				return new ManifestFetchResult(manifest, url, i > 0, attempts);
			}
			catch (OperationCanceledException) when (ct.IsCancellationRequested)
			{
				// 调用方取消：保留已尝试记录后向上传播，不再打备用源
				throw;
			}
			catch (Exception ex)
			{
				string reason = DescribeManifestFailure(ex, timeout);
				attempts.Add(new ManifestAttempt(url, false, reason));
				LauncherLog.Info($"[update] 清单源失败（{reason}）: {Redact(url)}");
			}
			finally
			{
				attemptCts?.Dispose();
			}
		}

		return new ManifestFetchResult(null, string.Empty, attempts.Count > 1, attempts);
	}

	/// <summary>把取清单的失败归类成可展示原因（不猜测平台侧隐藏原因）。</summary>
	internal static string DescribeManifestFailure(Exception ex, TimeSpan timeout)
	{
		if (ex is OperationCanceledException)
		{
			int seconds = (int)Math.Round(timeout.TotalSeconds);
			return $"超时（超过 {seconds}s 未响应）";
		}

		if (ex is HttpRequestException httpEx)
		{
			if (httpEx.StatusCode.HasValue)
			{
				int code = (int)httpEx.StatusCode.Value;
				string hint = code switch
				{
					404 => "（清单不存在）",
					403 => "（被拒绝或需要鉴权）",
					429 => "（请求过于频繁）",
					_ => string.Empty
				};
				return $"HTTP {code}{hint}";
			}

			return "网络错误: " + httpEx.Message;
		}

		if (ex is InvalidDataException)
		{
			return ex.Message;
		}

		if (ex is JsonException)
		{
			return "响应不是有效 JSON";
		}

		return ex.Message;
	}

	/// <summary>日志里不打印 query（可能含 token），只保留 scheme+host+path。</summary>
	internal static string Redact(string url)
	{
		return Uri.TryCreate(url, UriKind.Absolute, out Uri? uri)
			? uri.GetLeftPart(UriPartial.Path)
			: "(invalid-url)";
	}

	/// <summary>
	/// 从清单地址取清单并**校验字段**（R03 第 5 步：hash/size/version/资产类型必须对应最终包）。
	/// 校验放在取清单这一层，坏清单才能被当成"这个源坏了"而降级，而不是等下载阶段才失败。
	/// </summary>
	public static async Task<UpdateManifest?> FetchManifestAsync(HttpClient http, string updateUrl, CancellationToken ct)
	{
		using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, updateUrl);
		request.Headers.UserAgent.ParseAdd("PineglowLauncher/" + LauncherService.LauncherVersion);
		using HttpResponseMessage response = await http.SendAsync(request, ct).ConfigureAwait(false);
		response.EnsureSuccessStatusCode();
		string json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
		UpdateManifest? manifest;
		try
		{
			// manifest 协议为 camelCase，需忽略大小写映射到 PascalCase 模型
			manifest = JsonSerializer.Deserialize<UpdateManifest>(json, JsonOpts);
		}
		catch (JsonException ex)
		{
			throw new InvalidDataException("响应不是有效 JSON", ex);
		}

		ValidateManifest(manifest);
		return manifest;
	}

	/// <summary>清单字段校验：缺 version / 非 HTTPS url / 无效 SHA256 / 负 size 都判为坏清单。</summary>
	internal static void ValidateManifest(UpdateManifest? manifest)
	{
		if (manifest == null)
		{
			throw new InvalidDataException("清单为空");
		}

		if (string.IsNullOrWhiteSpace(manifest.Version))
		{
			throw new InvalidDataException("清单缺少 version");
		}

		if (!Uri.TryCreate(manifest.Url, UriKind.Absolute, out Uri? assetUri) || assetUri.Scheme != Uri.UriSchemeHttps)
		{
			throw new InvalidDataException("清单的 url 不是有效 HTTPS 地址");
		}

		if (!IsValidSha256(manifest.Sha256))
		{
			throw new InvalidDataException("清单缺少有效的 SHA256");
		}

		if (manifest.Size < 0)
		{
			throw new InvalidDataException("清单的 size 非法");
		}
	}

	private static readonly JsonSerializerOptions JsonOpts = new JsonSerializerOptions
	{
		PropertyNameCaseInsensitive = true
	};

	public static async Task<string> DownloadUpdateAsync(HttpClient http, UpdateManifest manifest, IProgress<double>? progress, CancellationToken ct)
	{
		if (!IsValidSha256(manifest.Sha256))
		{
			throw new InvalidOperationException("更新清单缺少有效的 SHA256，已拒绝下载。");
		}
		if (!Uri.TryCreate(manifest.Url, UriKind.Absolute, out Uri? primaryUri) || primaryUri.Scheme != Uri.UriSchemeHttps)
		{
			throw new InvalidOperationException("更新地址必须是有效的 HTTPS URL。");
		}

		// MR20：平台资产匹配——先拒绝"别的平台"的资产，再谈下载。
		string currentRid = CurrentRid();
		if (!IsAssetCompatible(manifest.Url, currentRid, out string assetReason))
		{
			throw new InvalidOperationException(assetReason);
		}

		if (!string.IsNullOrWhiteSpace(assetReason))
		{
			LauncherLog.Info("[update] 平台匹配提示：" + assetReason);
		}
		string updatesDir = Path.Combine(PlatformPaths.GetConfigDirectory(), "updates");
		Directory.CreateDirectory(updatesDir);
		string dest = Path.Combine(updatesDir, "Pineglow-Launcher-" + manifest.Version + "-win-x64-single-file.exe");
		if (File.Exists(dest) && Sha256Matches(dest, manifest.Sha256))
		{
			progress?.Report(1.0);
			return dest;
		}

		// 主地址 + 备用地址依次尝试：主源（Gitee 国内快）失败后回退备用源（GitHub）
		string[] urls = new string[]
		{
			manifest.Url,
			string.IsNullOrWhiteSpace(manifest.FallbackUrl) ? string.Empty : manifest.FallbackUrl
		};
		List<string> errors = new List<string>();
		foreach (string url in urls)
		{
			if (string.IsNullOrWhiteSpace(url))
			{
				continue;
			}
			try
			{
				string part = dest + ".part";
				long received = await TryDownloadFromAsync(http, url, part, manifest.Size, progress, ct);

				// MR20：先按清单声明的大小判截断，给出比"校验失败"更精确的原因。
				if (manifest.Size > 0 && received != manifest.Size)
				{
					File.Delete(part);
					errors.Add($"下载不完整：{url}（声明 {manifest.Size} 字节，实际 {received} 字节）");
					continue;
				}

				if (Sha256Matches(part, manifest.Sha256))
				{
					if (File.Exists(dest))
					{
						File.Delete(dest);
					}
					File.Move(part, dest);
					progress?.Report(1.0);
					return dest;
				}
				File.Delete(part);
				errors.Add("校验失败：" + url);
			}
			catch (Exception ex)
			{
				errors.Add(url + " - " + ex.Message);
			}
		}
		throw new InvalidOperationException("更新包下载失败：" + string.Join("；", errors));
	}

	private static async Task<long> TryDownloadFromAsync(HttpClient http, string url, string part, long expectedSize, IProgress<double>? progress, CancellationToken ct)
	{
		using HttpResponseMessage response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
		response.EnsureSuccessStatusCode();
		long total = expectedSize > 0 ? expectedSize : (response.Content.Headers.ContentLength ?? 0L);
		await using FileStream fs = new FileStream(part, FileMode.Create, FileAccess.Write, FileShare.None);
		await using System.IO.Stream stream = await response.Content.ReadAsStreamAsync(ct);
		byte[] buffer = new byte[81920];
		long received = 0;
		int read;
		while ((read = await stream.ReadAsync(buffer, ct)) > 0)
		{
			await fs.WriteAsync(buffer.AsMemory(0, read), ct);
			received += read;
			progress?.Report(total > 0 ? (double)received / total : 0.0);
		}
		await fs.FlushAsync(ct);
		fs.Close();
		// MR20：返回实际字节数，供调用方判定"下载截断"。
		return received;
	}

	/// <summary>
	/// MR20：信任链声明（必须准确，不能含糊）。
	/// SHA256 只能证明"下载内容与清单里写的一致"，即**检出损坏/截断**；
	/// 如果清单本身来自同源（或可被篡改的）feed，它**不提供身份认证** ——
	/// 攻击者同时改 exe 与清单里的 SHA256，校验照样通过。
	/// 因此本包不声称"校验通过 = 可信来源"；正式发布应使用签名清单或已审查的信任源。
	/// </summary>
	public const string TrustModelNote =
		"SHA256 校验用于发现下载损坏或截断，不提供发布者身份认证；"
		+ "清单与产物同源时，两者可被同时篡改。正式分发应使用签名清单或已审查的信任源。";

	/// <summary>MR20：当前平台对应的 RID 记号（用于平台资产匹配）。</summary>
	internal static string CurrentRid()
	{
		if (OperatingSystem.IsWindows())
		{
			return Environment.Is64BitOperatingSystem ? "win-x64" : "win-x86";
		}

		if (OperatingSystem.IsMacOS())
		{
			return "osx-x64";
		}

		return "linux-x64";
	}

	private static readonly string[] KnownRidTokens =
	{
		"win-x64", "win-x86", "win-arm64", "linux-x64", "linux-arm64", "osx-x64", "osx-arm64"
	};

	/// <summary>
	/// MR20：平台资产匹配。规则：
	///   - 资产名里出现了**别的**平台记号 → 拒绝（防止把 linux 包装到 Windows 上）；
	///   - 出现当前平台记号，或完全没出现任何记号 → 接受（后者在 reason 里说明未做平台断言）。
	/// </summary>
	internal static bool IsAssetCompatible(string? assetUrlOrName, string currentRid, out string reason)
	{
		reason = string.Empty;
		if (string.IsNullOrWhiteSpace(assetUrlOrName))
		{
			reason = "更新清单没有可用的资产地址。";
			return false;
		}

		string lower = assetUrlOrName.ToLowerInvariant();
		List<string> foreign = KnownRidTokens
			.Where(t => !string.Equals(t, currentRid, StringComparison.OrdinalIgnoreCase) && lower.Contains(t))
			.ToList();
		if (foreign.Count > 0)
		{
			reason = $"更新资产与当前平台不匹配：资产名包含 {string.Join("/", foreign)}，当前平台是 {currentRid}。";
			return false;
		}

		if (!lower.Contains(currentRid, StringComparison.OrdinalIgnoreCase))
		{
			reason = $"资产名未包含平台记号（当前 {currentRid}），无法断言平台匹配；按兼容处理。";
		}

		return true;
	}

	/// <summary>MR20：apply 结果记录（由替换脚本写出，下次启动可读）。</summary>
	internal sealed record ApplyResult(string Result, string Version, DateTimeOffset? WrittenAtUtc)
	{
		/// <summary>applied 表示"替换并逐字节校验都通过"；其余值都表示已回滚或未替换。</summary>
		public bool Applied => string.Equals(Result, "applied", StringComparison.OrdinalIgnoreCase);
	}

	internal static string GetApplyStatePath(string updatesDir) => Path.Combine(updatesDir, "apply-state.json");

	/// <summary>MR20：读 apply 结果记录。文件不存在/损坏 → 返回 null（不猜）。</summary>
	internal static ApplyResult? ReadApplyResult(string statePath)
	{
		try
		{
			if (!File.Exists(statePath))
			{
				return null;
			}

			using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(statePath));
			JsonElement root = doc.RootElement;
			string result = root.TryGetProperty("result", out JsonElement r) && r.ValueKind == JsonValueKind.String
				? r.GetString() ?? string.Empty
				: string.Empty;
			string version = root.TryGetProperty("version", out JsonElement v) && v.ValueKind == JsonValueKind.String
				? v.GetString() ?? string.Empty
				: string.Empty;
			DateTimeOffset? writtenAt = root.TryGetProperty("writtenAtUtc", out JsonElement w)
				&& w.ValueKind == JsonValueKind.String
				&& DateTimeOffset.TryParse(w.GetString(), out DateTimeOffset parsed)
				? parsed
				: null;
			return string.IsNullOrEmpty(result) ? null : new ApplyResult(result, version, writtenAt);
		}
		catch
		{
			return null;
		}
	}

	/// <summary>
	/// MR20：生成替换脚本（**纯函数**，便于被真实执行验证）。
	///
	/// 关键行为（这是本包补上的最大缺口：原实现直接 `copy /Y` 覆盖，失败后**无法回退**）：
	///   1. 先备份当前 exe；备份失败就什么都不做，并记 `backup_failed`；
	///   2. 再替换；替换失败 → 用备份放回，记 `replace_failed`；
	///   3. 替换后**逐字节校验**（Windows `fc /b`、Unix `cmp -s`），不一致 → 回滚，记 `verify_failed`；
	///   4. 只有全部通过才记 `applied` 并启动新版本；
	///   5. **任何失败路径都会启动旧版本** —— 保证"异常时仍有可运行旧版"。
	///
	/// <paramref name="restartCommand"/> 由调用方给出（真实场景是启动目标 exe）；
	/// 测试会把它换成空操作，以便真实执行脚本而不会真的启动进程。
	/// </summary>
	internal static string BuildApplyScript(
		string scriptExt,
		string targetExe,
		string downloadedPath,
		string backupPath,
		string statePath,
		string version,
		string restartCommand)
	{
		string exeName = Path.GetFileName(targetExe);
		if (scriptExt == ".bat")
		{
			return "@echo off\r\n"
				+ "chcp 65001 >nul\r\n"
				+ "set \"NEW=" + downloadedPath + "\"\r\n"
				+ "set \"TARGET=" + targetExe + "\"\r\n"
				+ "set \"BACKUP=" + backupPath + "\"\r\n"
				+ "set \"STATE=" + statePath + "\"\r\n"
				// R06 实测缺陷修复：这里原本用 `timeout /t 1 /nobreak` 做等待间隔。
				// timeout 依赖控制台输入，而替换脚本是被**无控制台的 GUI 启动器**拉起的
				// （UseShellExecute=false + CreateNoWindow）；实测在 stdin 被接成管道时
				// `timeout` 会永久阻塞，循环再也走不到替换那一步（更新原地卡死）。
				// 改用不依赖控制台的 1 秒等待：ping 回环。
				// 另外加一个上界：目标进程若始终不退（异常占用），也必须在有限时间内进入
				// 备份→替换→校验流程；替换失败会走回滚并记录 replace_failed，
				// 从而保证"异常时仍有可运行旧版本"，而不是无限等待。
				+ "set /a WAITS=0\r\n"
				+ ":wait\r\n"
				+ "tasklist /FI \"IMAGENAME eq " + exeName + "\" 2>nul | find /I \"" + exeName + "\" >nul\r\n"
				+ "if not %errorlevel%==0 goto ready\r\n"
				+ "set /a WAITS+=1\r\n"
				+ "if %WAITS% GTR " + MaxRunningProcessWaitIterations + " goto ready\r\n"
				+ "ping -n 2 127.0.0.1 >nul\r\n"
				+ "goto wait\r\n"
				+ ":ready\r\n"
				+ "copy /Y \"%TARGET%\" \"%BACKUP%\" >nul\r\n"
				+ "set \"RC=%errorlevel%\"\r\n"
				+ "if not \"%RC%\"==\"0\" ( echo {\"result\":\"backup_failed\",\"version\":\"" + version + "\"} > \"%STATE%\" & goto abort )\r\n"
				+ "copy /Y \"%NEW%\" \"%TARGET%\" >nul\r\n"
				+ "set \"RC=%errorlevel%\"\r\n"
				+ "if not \"%RC%\"==\"0\" ( copy /Y \"%BACKUP%\" \"%TARGET%\" >nul & echo {\"result\":\"replace_failed\",\"version\":\"" + version + "\"} > \"%STATE%\" & goto abort )\r\n"
				+ "fc /b \"%NEW%\" \"%TARGET%\" >nul\r\n"
				+ "set \"RC=%errorlevel%\"\r\n"
				+ "if not \"%RC%\"==\"0\" ( copy /Y \"%BACKUP%\" \"%TARGET%\" >nul & echo {\"result\":\"verify_failed\",\"version\":\"" + version + "\"} > \"%STATE%\" & goto abort )\r\n"
				+ "echo {\"result\":\"applied\",\"version\":\"" + version + "\"} > \"%STATE%\"\r\n"
				+ restartCommand + "\r\n"
				+ "goto done\r\n"
				+ ":abort\r\n"
				+ "rem after rollback we still start the old binary, so a broken update cannot leave the user without a runnable launcher\r\n"
				+ restartCommand + "\r\n"
				+ ":done\r\n"
				+ "del \"%~f0\"\r\n";
		}

		return "#!/bin/sh\n"
			+ "NEW=\"" + downloadedPath + "\"\n"
			+ "TARGET=\"" + targetExe + "\"\n"
			+ "BACKUP=\"" + backupPath + "\"\n"
			+ "STATE=\"" + statePath + "\"\n"
			+ "while pgrep -f \"" + exeName + "\" >/dev/null 2>&1; do sleep 1; done\n"
			+ "cp \"$TARGET\" \"$BACKUP\" || { echo '{\"result\":\"backup_failed\",\"version\":\"" + version + "\"}' > \"$STATE\"; " + restartCommand + "; exit 0; }\n"
			+ "cp \"$NEW\" \"$TARGET\" || { cp \"$BACKUP\" \"$TARGET\"; echo '{\"result\":\"replace_failed\",\"version\":\"" + version + "\"}' > \"$STATE\"; " + restartCommand + "; exit 0; }\n"
			+ "chmod +x \"$TARGET\"\n"
			+ "cmp -s \"$NEW\" \"$TARGET\" || { cp \"$BACKUP\" \"$TARGET\"; echo '{\"result\":\"verify_failed\",\"version\":\"" + version + "\"}' > \"$STATE\"; " + restartCommand + "; exit 0; }\n"
			+ "echo '{\"result\":\"applied\",\"version\":\"" + version + "\"}' > \"$STATE\"\n"
			+ restartCommand + "\n"
			+ "rm -f \"$0\"\n";
	}

	/// <summary>
	/// 等待「目标 exe 还在运行」的最大轮次（每轮约 1 秒）。上界的存在是为了避免
	/// 异常占用把更新永久卡在等待循环里：到点即进入备份/替换/校验，失败就走回滚。
	/// </summary>
	internal const int MaxRunningProcessWaitIterations = 300;

	/// <summary>生成替换脚本并启动；脚本等待当前进程退出后备份 → 替换 → 校验 → （失败则回滚）→ 重启。</summary>
	public static void ApplyUpdate(string downloadedPath, string scriptExt, string version = "")
	{
		string targetExe = Environment.ProcessPath ?? throw new InvalidOperationException("无法确定当前可执行文件路径");
		string dir = Path.GetDirectoryName(downloadedPath) ?? PlatformPaths.GetConfigDirectory();
		string scriptPath = Path.Combine(dir, "apply-update" + scriptExt);
		string backupPath = targetExe + ".bak" + (string.IsNullOrWhiteSpace(version) ? string.Empty : "-" + version);
		string statePath = GetApplyStatePath(dir);
		string restart = scriptExt == ".bat" ? "start \"\" \"%TARGET%\"" : "nohup \"$TARGET\" >/dev/null 2>&1 &";

		string script = BuildApplyScript(scriptExt, targetExe, downloadedPath, backupPath, statePath, version, restart);
		File.WriteAllText(scriptPath, script);

		if (scriptExt == ".bat")
		{
			Process.Start(new ProcessStartInfo
			{
				FileName = "cmd.exe",
				Arguments = "/c \"" + scriptPath + "\"",
				UseShellExecute = false,
				CreateNoWindow = true,
				WorkingDirectory = AppContext.BaseDirectory
			});
		}
		else
		{
			Process.Start(new ProcessStartInfo
			{
				FileName = "/bin/sh",
				Arguments = "\"" + scriptPath + "\"",
				UseShellExecute = false,
				CreateNoWindow = true,
				WorkingDirectory = AppContext.BaseDirectory
			});
		}
	}

	internal static bool IsValidSha256(string? expected)
	{
		return expected is not null && expected.Length == 64 && expected.All(Uri.IsHexDigit);
	}

	internal static bool Sha256Matches(string file, string expected)
	{
		if (!IsValidSha256(expected)) return false;
		string actual = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file))).ToLowerInvariant();
		return string.Equals(actual, expected.Trim(), StringComparison.OrdinalIgnoreCase);
	}
}
