using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Pineglow.Launcher.Models;

namespace Pineglow.Launcher.Services;

/// <summary>
/// 宿主生命周期、安全边界与共用工具。
/// <para>
/// 本地 HTTP API 与静态 UI 托管的**宿主层**：端口/令牌/Origin 校验、listener 启停、
/// 请求入口的安全前置、以及各域共用的 JSON 与路径工具。
/// <para>
/// M1-2（2026-11-06）按域拆分的入口文件。**所有静态字段与静态初始化都留在这里**，
/// 因为字段初始化顺序是按声明文本顺序决定的，散到多个 partial 文件会让
/// "谁先初始化" 变成依赖文件名排序的隐式契约。
/// </para>
/// </para>
/// </summary>
public static partial class LocalApiService
{
	private const int DefaultPort = 48123;

	private const long MaxJsonBodyBytes = 32L * 1024 * 1024;

	private const int MaxImportBytes = 24 * 1024 * 1024;

	private static readonly string SessionToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));

	private static readonly object ConfigGate = new object();

	private static readonly JsonSerializerOptions JsonOpts = new JsonSerializerOptions
	{
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
		WriteIndented = false
	};

	// 宿主（候选 ③）：listener / 端口 / UI 根 / 运行标志 + 全部外部依赖
	// （config store / 三个存储根 / 出站客户端）都在这里。
	// 本类不再有任何可被"半改"的静态依赖字段：默认值只有 HostDependencies.Production() 一份，
	// 替换只有 OverrideHostDependenciesForTest 一个入口。
	private static readonly LocalApiHost _host = new LocalApiHost(HostDependencies.Production());

	/// <summary>出站客户端（供调用点读具名客户端的便捷入口，真身归 `<see cref="LocalApiHost"/>.Dependencies.Http`）。</summary>
	private static OutboundHttp Outbound => _host.Dependencies.Http;

	/// <summary>
	/// 测试缝（R03）：替换更新清单的地址链。生产路径恒为 null。
	///
	/// 存在的理由：生产地址链里包含真实的 Gitee/GitHub 地址，若不能在测试里替换，
	/// API 级降级用例就只能去打真实网络——那是"依赖外网的假通过/假失败"，不是可复现验证。
	/// 服务级降级逻辑另外由 UpdateManifestFallbackTests 直接驱动。
	/// </summary>
	internal static Func<string?, IReadOnlyList<string>>? UpdateUrlChainOverrideForTest { get; set; }

	private static IReadOnlyList<string> ResolveUpdateUrlChain(string? configuredUrl)
	{
		Func<string?, IReadOnlyList<string>>? over = UpdateUrlChainOverrideForTest;
		return over != null ? over(configuredUrl) : UpdateService.BuildManifestUrlChain(configuredUrl);
	}

	// AI 专用客户端：每小时重新读 OutboundHttp —— AllowAutoRedirect=false（防固定首跳 allowlist 被 302/307/308 绕过）。
	// 新闻运行时（Phase 3A）：跨请求共享内存缓存与图片 ID 映射；http 提供器按调用读当前客户端（代理切换自动生效）。
	private static readonly MinecraftNewsService NewsService = new MinecraftNewsService(new LauncherContentNewsProvider(() => Outbound.News));

	// R2-03：新闻外链 linkId 注册表（服务端持有 URL，客户端永不提交 URL）
	private static readonly NewsLinkService NewsLinks = new NewsLinkService();

	// R-02：fake-ip（198.18/15）兼容默认 fail closed。
	// R2-02 最小实现：每次图片请求读取当前已持久化配置 NewsFakeIpCompat 的窄委托，
	// 同一进程 POST /api/config 后立即生效，不依赖重启。LoadConfig 内部 lock(ConfigGate) 保护。
	// 任意异常一律按 false 处理（fail closed），不放大 SSRF 风险面。
	private static readonly NewsImageProxyService NewsImages = new NewsImageProxyService(
		fakeIpCompat: () =>
		{
			try { return LoadConfig().NewsFakeIpCompat; }
			catch { return false; }
		});

	/// <summary>
	/// 测试缝：把 AI 客户端换成假的，让诊断请求真实走 <c>SendAsync</c> 却不触碰外网。
	/// 生产路径不调用；Dispose 即恢复。
	/// </summary>
	internal static IDisposable OverrideAiClientForTest(HttpClient client) => Outbound.OverrideAiClient(client);

	private sealed class DownloadTaskEntry
	{
		public string Id;
		public string VersionId;
		public string Status; // queued / downloading / completed / failed
		public double Progress;
		public string Message = string.Empty;
		public DateTimeOffset StartedAt;
	}

	private static readonly object DownloadGate = new object();

	private static readonly List<DownloadTaskEntry> DownloadTasks = new List<DownloadTaskEntry>();

	private sealed class ContentTaskEntry
	{
		public string Id;
		public string Category;      // mod / modpack / shader / resourcepack
		public string ProjectId;
		public string InstanceId;    // 安装目标实例（modpack 创建新实例，忽略此字段）
		public string Source = "modrinth"; // modrinth / curseforge
		public string Title = string.Empty;
		public string Status;        // queued / running / completed / failed
		public double Progress;
		public string Message = string.Empty;
		public string ResultInstanceId = string.Empty;
		public DateTimeOffset StartedAt;
	}

	private static readonly object ContentGate = new object();

	private static readonly List<ContentTaskEntry> ContentTasks = new List<ContentTaskEntry>();

	// 自更新状态（单任务）
	private static readonly object UpdateGate = new object();

	private static string UpdateState = "idle"; // idle / downloading / completed / failed / applying
	private static double UpdateProgress;

	private static string UpdateVersion = string.Empty;

	private static string UpdatePath = string.Empty;

	private static string UpdateError = string.Empty;

	private static bool UpdateMandatory;

	public static int Port => _host.Port;

	public static bool IsRunning => _host.IsRunning;

	internal static string SessionTokenForTests => SessionToken;

	public static void Start(int? portOverride = null)
	{
		if (_host.IsRunning)
		{
			return;
		}

		int port = portOverride ?? DefaultPort;
		string envPort = Environment.GetEnvironmentVariable("PINEGLOW_API_PORT");
		if (portOverride == null && int.TryParse(envPort, out int envParsed))
		{
			port = envParsed;
		}

		string uiRoot = ResolveUiRoot();
		_host.Start(port, uiRoot);
		_ = Task.Run(() => LoopAsync(_host));
		LauncherLog.Info($"[LocalApi] 监听 127.0.0.1:{port}, ui={uiRoot}");
		Debug.WriteLine($"[LocalApi] listening on 127.0.0.1:{port}, ui={uiRoot}");
	}

	public static void Stop()
	{
		// 返修五：无论当前是否运行，都必须释放 ContentIconService.Default。
		// listener 停止/关闭抛异常时，通过 finally 保证图标服务释放；可重复调用。
		try
		{
			_host.Stop();
		}
		finally
		{
			// 释放 ContentIconService 默认实例（线程安全、幂等）
			ContentIconService.DisposeDefault();
		}
	}

	private static string ResolveUiRoot()
	{
		return ResolveUiRootCore(
			Environment.GetEnvironmentVariable("PINEGLOW_UI_DIR"),
			UiBundle.IsSingleFileDeployment(),
			Path.Combine(AppContext.BaseDirectory, "UI"),
			Path.Combine(PlatformPaths.GetConfigDirectory(), "UI"),
			UiBundle.EnsureExtracted);
	}

	/// <summary>
	/// UI 根目录选择（纯决策；IO 只做存在性判断，便于聚焦测试）。
	///
	/// R02：正式**单文件发行**必须让「当前 EXE 自带的内嵌界面」优先。
	/// 原实现把 EXE 旁的 UI/ 排在内嵌解包之前，于是目录式旧安装原地换 EXE 后，
	/// 安装目录里残留的旧 UI/ 会继续被服务，形成「新后端 + 旧界面」——
	/// 全新安装的冷启动通过并不能覆盖这条升级路径。
	///
	/// 现在的次序：
	///   1. PINEGLOW_UI_DIR 显式覆盖（开发/测试用的既有接缝，行为不变）；
	///   2. 单文件部署：先内嵌解包（版本 + 内容指纹，天然不会复用到旧界面）；
	///   3. EXE 旁 UI/（开发树、目录包的有意外置 UI，行为不变）；
	///   4. 内嵌解包的兜底尝试；
	///   5. 配置目录 UI/；
	///   6. 都没有时返回外置路径（与既有回退语义一致，启动不因缺 UI 而中断）。
	/// </summary>
	internal static string ResolveUiRootCore(
		string? explicitUiDir,
		bool singleFileDeployment,
		string externalUiDir,
		string configUiDir,
		Func<string?> extractEmbedded)
	{
		if (!string.IsNullOrWhiteSpace(explicitUiDir) && Directory.Exists(explicitUiDir))
		{
			return explicitUiDir!;
		}

		if (singleFileDeployment)
		{
			string? bundled = extractEmbedded();
			if (!string.IsNullOrWhiteSpace(bundled) && Directory.Exists(bundled))
			{
				return bundled!;
			}
		}

		if (Directory.Exists(externalUiDir))
		{
			return externalUiDir;
		}

		string? extracted = extractEmbedded();
		if (!string.IsNullOrWhiteSpace(extracted) && Directory.Exists(extracted))
		{
			return extracted!;
		}

		if (Directory.Exists(configUiDir))
		{
			return configUiDir;
		}

		return externalUiDir;
	}

	/// <summary>
	/// 当前实际服务的 UI 根目录（未启动时为 null）。
	/// WebShell 的静态回退路径也用它，避免「API 没起来时去看 EXE 旁的 UI/」这种
	/// 在单文件发行下必然落空的判断。
	/// </summary>
	public static string? UiRoot => _host.UiRoot;

	private static async Task LoopAsync(LocalApiHost host)
	{
		while (host.IsRunning && host.Listener != null)
		{
			try
			{
				HttpListenerContext context = await host.Listener.GetContextAsync().ConfigureAwait(false);
				_ = Task.Run(() => HandleAsync(context));
			}
			catch (Exception ex)
			{
				LauncherLog.Error("[LocalApi] loop error: " + ex.Message, ex);
				Debug.WriteLine($"[LocalApi] loop error: {ex.Message}");
				if (host.IsRunning)
				{
					await Task.Delay(200).ConfigureAwait(false);
				}
			}
		}
	}

	private static async Task HandleAsync(HttpListenerContext context)
	{
		HttpListenerRequest request = context.Request;
		HttpListenerResponse response = context.Response;
		try
		{
			if (!ValidateOrigin(request, out string? allowedOrigin))
			{
				await WriteJsonAsync(response, 403, new { error = "不允许的请求来源" });
				return;
			}
			SetCors(response, allowedOrigin);
			if (request.HttpMethod == "OPTIONS")
			{
				response.StatusCode = 204;
				response.Close();
				return;
			}

			string path = request.Url.AbsolutePath;
			if (path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase) || path.Equals("/api", StringComparison.OrdinalIgnoreCase))
			{
				if (!path.Equals("/api/health", StringComparison.OrdinalIgnoreCase) && !HasValidSessionToken(request))
				{
					await WriteJsonAsync(response, 401, new { error = "缺少或无效的本地 API 会话令牌" });
					return;
				}
				await HandleApiAsync(context, path);
				return;
			}

			await ServeStaticAsync(context, path);
		}
		catch (CurseForgeKeyRequiredException ex)
		{
			// 可预期的"用户没配 Key"，不是服务端故障：400 + 稳定 code，让界面能引导。
			LauncherLog.Info("[LocalApi] 需要 CurseForge Key: " + request.RawUrl);
			await WriteJsonAsync(response, 400, new
			{
				ok = false,
				code = CurseForgeKeyRequiredException.ErrorCode,
				error = ex.Message
			});
		}
		catch (Exception ex)
		{
			LauncherLog.Error("[LocalApi] handler error: " + request.RawUrl + " | " + ex.Message, ex);
			Debug.WriteLine($"[LocalApi] handler error: {ex}");
			await WriteJsonAsync(response, 500, new { error = ex.Message });
		}
		finally
		{
			try
			{
				response.Close();
			}
			catch
			{
			}
		}
	}

	internal static bool IsAllowedOrigin(string? origin, int port)
	{
		if (string.IsNullOrWhiteSpace(origin)) return true;
		return Uri.TryCreate(origin, UriKind.Absolute, out Uri? uri)
			&& uri.Scheme == Uri.UriSchemeHttp
			&& IPAddress.TryParse(uri.Host, out IPAddress? address)
			&& IPAddress.IsLoopback(address)
			&& uri.Port == port;
	}

	private static bool ValidateOrigin(HttpListenerRequest request, out string? allowedOrigin)
	{
		allowedOrigin = request.Headers["Origin"];
		return IsAllowedOrigin(allowedOrigin, Port);
	}

	private static bool HasValidSessionToken(HttpListenerRequest request)
	{
		string? supplied = request.Headers["X-Pineglow-Token"];
		if (string.IsNullOrEmpty(supplied) && request.Url?.AbsolutePath is string absPath
			&& (absPath.EndsWith("/skin-image", StringComparison.OrdinalIgnoreCase)
				// Phase 2：/api/content/icon 由 <img src> 加载，无法携带自定义 header，
				// 与 /skin-image 同样接受 token= 查询参数（会话令牌等价校验）。
				|| absPath.EndsWith("/api/content/icon", StringComparison.OrdinalIgnoreCase)))
		{
			supplied = request.QueryString["token"];
		}
		return IsValidSessionToken(supplied, SessionToken);
	}

	internal static bool IsValidSessionToken(string? supplied, string expected)
	{
		if (supplied is null || supplied.Length != expected.Length) return false;
		return CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(supplied), Encoding.ASCII.GetBytes(expected));
	}

	private static void SetCors(HttpListenerResponse response, string? allowedOrigin)
	{
		if (!string.IsNullOrWhiteSpace(allowedOrigin)) response.Headers["Access-Control-Allow-Origin"] = allowedOrigin;
		response.Headers["Access-Control-Allow-Methods"] = "GET, POST, DELETE, OPTIONS";
		response.Headers["Access-Control-Allow-Headers"] = "Content-Type, X-Pineglow-Token";
		response.Headers["Cache-Control"] = "no-store";
	}

	private static async Task WriteJsonErrorAsync(HttpListenerResponse response, int status, string message)
	{
		try
		{
			await WriteJsonAsync(response, status, new { error = message });
		}
		catch
		{
			try { response.StatusCode = status; response.Close(); } catch { }
		}
	}

	/* ================= 工具 ================= */

	internal static bool IsPathWithinRoot(string root, string candidate)
	{
		string relative = Path.GetRelativePath(Path.GetFullPath(root), Path.GetFullPath(candidate));
		return !Path.IsPathRooted(relative) && relative != ".." && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal);
	}

	internal static string CreateSafeTemporaryUploadPath(string root, string clientFileName)
	{
		string extension = Path.GetExtension(Path.GetFileName(clientFileName));
		string result = Path.Combine(root, Guid.NewGuid().ToString("N") + extension);
		if (!IsPathWithinRoot(root, result)) throw new InvalidOperationException("临时上传路径越界");
		return result;
	}

	internal static bool ExceedsJsonBodyLimit(long contentLength) => contentLength > MaxJsonBodyBytes;

	private static async Task<JsonDocument> ReadBodyAsync(HttpListenerRequest request)
	{
		if (ExceedsJsonBodyLimit(request.ContentLength64)) throw new InvalidOperationException("请求体超过 32 MiB 上限");
		using MemoryStream ms = new MemoryStream();
		byte[] buffer = new byte[81920];
		int read;
		while ((read = await request.InputStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
		{
			if (ms.Length + read > MaxJsonBodyBytes) throw new InvalidOperationException("请求体超过 32 MiB 上限");
			await ms.WriteAsync(buffer, 0, read);
		}
		return JsonDocument.Parse(ms.ToArray());
	}

	private static async Task WriteJsonAsync(HttpListenerResponse response, int status, object payload)
	{
		byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(payload, JsonOpts);
		response.StatusCode = status;
		response.ContentType = "application/json; charset=utf-8";
		response.ContentLength64 = bytes.Length;
		await response.OutputStream.WriteAsync(bytes, 0, bytes.Length);
	}
}
