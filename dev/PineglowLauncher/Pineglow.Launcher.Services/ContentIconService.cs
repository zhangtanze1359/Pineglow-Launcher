using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SkiaSharp;

namespace Pineglow.Launcher.Services;

/// <summary>
/// 内容资源图标（Mod / Modpack / Shader / ResourcePack）的下载、校验、缓存与并发控制。
/// 设计目标：
///   1. LocalApiService 只接受 (source, category, projectId) 三元组，绝不允许客户端提交任意 URL。
///   2. 上游主机走精确字符串白名单（不依赖 EndsWith）。仅含已实测的 Modrinth CDN；
///      CurseForge 暂不加入白名单，等真实 API Key 验证后另起一轮。
///   3. 每一跳重定向都重新解析 + 校验 + 绑定：DNS 解析结果必须全部为公网 IP
///      （拒绝 IPv4-mapped 私网），并把验证通过的 IP 通过请求选项传入共享 handler 的
///      ConnectCallback，使实际 TCP 连接目标就是该已验证 IP（fail-closed）；TLS 仍按
///      原始 CDN host 做 SNI 与证书校验（不关闭校验、不删除 IP 检查、不放宽白名单）。
///   4. 可复用并可释放的 transport：服务懒创建单个 SocketsHttpHandler + HttpClient，
///      所有请求复用同一连接池；IDisposable/IAsyncDisposable 释放全部资源。
///   5. 可信 URL TTL：注入时钟计算 TrustedUrlTtl，过期即移除映射返回 NotFound；
///      新的搜索结果无图标时用空 URL 清除旧映射。
///   6. Content-Type 规范化后与 PNG / JPEG / WebP 魔数 + 有限结构校验结果必须一致；
///      任何不一致、空文件、截断、结构损坏都拒绝且不落盘。
///   7. 缓存采用 manifest 式一致提交：图片写版本化文件，元数据写 <key>.json，
///      File.Move 原子切换 manifest 指针；崩溃后孤立版本下次读取时清理，
///      绝不出现"新元数据配旧图片 / 孤立文件被当作命中"。
///   8. 同键并发去重 + 全局并发上限 4；共享 in-flight 下载不归属任一调用者的
///      CancellationToken，每个等待者独立取消互不影响。
/// </summary>
public sealed class ContentIconService : IDisposable, IAsyncDisposable
{
	public const int MaxBytes = 2 * 1024 * 1024;
	public const int MaxRedirects = 3;
	public const int MaxCacheBytes = 256 * 1024 * 1024;
	public const int MaxConcurrent = 4;
	public const int FormatVersion = 2;
	public const int MaxProjectIdLength = 80;
	/// <summary>断网冒烟注入开关（收口九）：仅当该环境变量显式为 "1" 时，Default 注入不可达图标 transport。</summary>
	internal const string IconOfflineEnvVar = "PINEGLOW_ICON_OFFLINE";
	public const int MaxSourceLength = 16;
	public const int MaxCategoryLength = 16;
	public const int ConnectTimeoutSeconds = 5;
	public const int TotalTimeoutSeconds = 12;

	public static readonly TimeSpan TrustedUrlTtl = TimeSpan.FromMinutes(5);
	public static readonly TimeSpan CacheFreshTtl = TimeSpan.FromDays(7);
	public static readonly TimeSpan StaleAfterTtl = TimeSpan.FromDays(30);
	/// <summary>manifest writtenAtTicks 允许的最大未来时钟偏差。</summary>
	internal static readonly long ManifestMaxFutureSkewTicks = TimeSpan.FromDays(1).Ticks;

	/// <summary>
	/// 经实测（2026-09-04 13:08+08:00）确认的 HTTPS 主机白名单（精确字符串比较）。
	/// CurseForge 主机（疑似 media.forgecdn.net）尚无 API Key 验证证据，按"安全降级"暂不加入。
	/// </summary>
	public static readonly IReadOnlyList<string> AllowedHosts = new[]
	{
		"cdn-raw.modrinth.com",
		"cdn.modrinth.com",
	};

	private static readonly byte[] PngSignature = new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 };
	private static readonly byte[] JpegSignature = new byte[] { 0xFF, 0xD8, 0xFF };
	private static readonly byte[] WebpRiff = new byte[] { (byte)'R', (byte)'I', (byte)'F', (byte)'F' };
	private static readonly byte[] WebpWebp = new byte[] { (byte)'W', (byte)'E', (byte)'B', (byte)'P' };

	private static readonly HttpRequestOptionsKey<IPAddress> ValidatedIpKey = new("Pineglow.ValidatedIp");
	private static readonly HttpRequestOptionsKey<string> ValidatedHostKey = new("Pineglow.ValidatedHost");
	private static readonly Dictionary<string, string> EmptyConditional = new(StringComparer.OrdinalIgnoreCase);

	private static readonly object _defaultLock = new();
	private static ContentIconService? _default;
	public static ContentIconService Default
	{
		get
		{
			if (_default != null) return _default;
			lock (_defaultLock)
			{
				_default ??= CreateDefault();
				return _default;
			}
		}
	}

	private static ContentIconService CreateDefault()
	{
		ContentIconService svc = new ContentIconService();
		if (Environment.GetEnvironmentVariable(IconOfflineEnvVar) == "1")
		{
			// 断网冒烟注入（收口九）：仅当显式设置环境变量时启用，使图标下载不可达；
			// 缓存读取不受影响（TryReadCache 先于下载），绝不触碰系统网络配置。
			svc.HttpFetcher = (_, _) => Task.FromException<HttpResponseMessage>(
				new HttpRequestException($"simulated offline ({IconOfflineEnvVar}=1)"));
		}
		return svc;
	}

	/// <summary>
	/// 线程安全、幂等的默认实例关闭/重置入口。
	/// 1. 在锁内将 _default 原子交换为 null
	/// 2. 在锁外释放旧实例，避免 Dispose 阻塞锁
	/// 3. 多次调用不抛异常
	/// 4. 后续访问 Default 时会创建新的可用实例
	/// </summary>
	public static void DisposeDefault()
	{
		ContentIconService? toDispose;
		lock (_defaultLock)
		{
			toDispose = _default;
			_default = null;
		}
		toDispose?.Dispose();
	}

	// ----- 可注入点（测试覆盖） -----
	/// <summary>注入式抓取函数；为 null 时走内置可复用 transport（DNS 绑定 + TLS SNI 保留）。</summary>
	public Func<Uri, CancellationToken, Task<HttpResponseMessage>> HttpFetcher { get; set; }
	public Func<string, CancellationToken, Task<IPAddress[]>> DnsResolver { get; set; }
	public Func<long> UtcNowTicks { get; set; }
	/// <summary>实际 TCP 连接工厂（默认 Socket.ConnectAsync）。测试注入用于观测"验证后的 IP 就是连接目标"。</summary>
	internal Func<IPAddress, int, CancellationToken, Task<Stream>> ConnectFactory { get; set; }
	/// <summary>缓存提交故障点（测试注入）：图片落盘前调用，抛出即模拟"图片提交失败"。</summary>
	internal Action<string, string>? BeforeImageCommit { get; set; }
	/// <summary>缓存提交故障点（测试注入）：manifest 原子切换前调用，抛出即模拟"元数据提交失败"。</summary>
	internal Action<string, string>? BeforeManifestCommit { get; set; }
	/// <summary>孤立文件清理/缓存预算使用的挂钟（测试注入实现确定性宽限期判断）。</summary>
	internal Func<DateTime> WallClock { get; set; }
	/// <summary>缓存预算上限覆盖（测试注入；null = MaxCacheBytes）。</summary>
	internal long? CacheBudgetBytesOverride { get; set; }
	/// <summary>预算淘汰故障点（测试注入）：删除 manifest 前调用（用于断言删除顺序）。</summary>
	internal Action<string>? BeforeBudgetManifestDelete { get; set; }
	/// <summary>预算淘汰故障点（测试注入）：删除图片前调用（用于断言删除顺序）。</summary>
	internal Action<string>? BeforeBudgetImageDelete { get; set; }
	public string CacheRoot { get; private set; }

	// ----- 生命周期观测（测试） -----
	internal int HandlerBuildCount;
	internal int ConnectCallCount;
	internal bool TransportDisposed;

	private readonly Dictionary<string, TrustEntry> _trust = new Dictionary<string, TrustEntry>(StringComparer.Ordinal);
	private readonly object _trustGate = new object();
	private readonly Dictionary<string, Task<IconResult>> _inFlight = new Dictionary<string, Task<IconResult>>(StringComparer.Ordinal);
	private readonly object _inFlightGate = new object();
	private readonly SemaphoreSlim _globalSem;
	private readonly object _transportGate = new object();
	private SocketsHttpHandler? _handler;
	private HttpClient? _http;
	private bool _disposed;

	public ContentIconService()
		: this(cacheRoot: null, httpFetcher: null, clock: null)
	{
	}

	/// <summary>测试用构造器。允许注入缓存根、HTTP 抓取实现与时钟。</summary>
	public ContentIconService(string? cacheRoot, Func<Uri, CancellationToken, Task<HttpResponseMessage>>? httpFetcher, Func<long>? clock)
		: this(cacheRoot, httpFetcher, clock, null)
	{
	}

	/// <summary>完整测试用构造器。</summary>
	public ContentIconService(string? cacheRoot, Func<Uri, CancellationToken, Task<HttpResponseMessage>>? httpFetcher, Func<long>? clock, Func<string, CancellationToken, Task<IPAddress[]>>? dnsResolver)
	{
		CacheRoot = string.IsNullOrWhiteSpace(cacheRoot)
			? Path.Combine(PlatformPaths.GetConfigDirectory(), "content-icons")
			: cacheRoot!;
		// null 表示使用内置可复用 transport（真实连接）；测试注入则完全绕过 HTTP 栈。
		HttpFetcher = httpFetcher!;
		DnsResolver = dnsResolver ?? ((host, ct) => Dns.GetHostAddressesAsync(host, ct));
		UtcNowTicks = clock ?? (() => DateTimeOffset.UtcNow.UtcTicks);
		ConnectFactory = DefaultConnectAsync;
		WallClock = static () => DateTime.UtcNow;
		_globalSem = new SemaphoreSlim(MaxConcurrent, MaxConcurrent);
	}

	/// <summary>
	/// 构造 MIME 错标结构化警告（收口五-15）：只含 host/声明/检测/大小，
	/// 不含完整 URL、token 或凭据。
	/// </summary>
	internal static string BuildMimeMismatchWarning(string host, string declared, string detected, long size)
		=> $"mime-mismatch host={host} declared={declared} detected={detected} size={size} action=accept-verified";

	// =========================================================================
	//  可复用、可释放的 transport 生命周期
	// =========================================================================

	private static async Task<Stream> DefaultConnectAsync(IPAddress ip, int port, CancellationToken ct)
	{
		Socket socket = new Socket(ip.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
		try
		{
			socket.NoDelay = true;
			await socket.ConnectAsync(new IPEndPoint(ip, port), ct).ConfigureAwait(false);
			return new NetworkStream(socket, ownsSocket: true);
		}
		catch
		{
			socket.Dispose();
			throw;
		}
	}

	private HttpMessageHandler GetHandler()
	{
		if (_handler != null) return _handler;
		lock (_transportGate)
		{
			if (_handler == null && !_disposed)
			{
				SocketsHttpHandler h = new SocketsHttpHandler
				{
					AllowAutoRedirect = false,
					UseProxy = false,
					ConnectTimeout = TimeSpan.FromSeconds(ConnectTimeoutSeconds),
				};
				// 连接回调：只能连到请求选项中已验证的公网 IP；host 不匹配即失败（fail-closed）。
				// TLS SNI / 证书校验仍由 SocketsHttpHandler 按请求 URL 的原始 CDN host 执行（默认 SslOptions，不关闭校验）。
				h.ConnectCallback = BindAndConnectAsync;
				_handler = h;
				_http = new HttpClient(h) { Timeout = TimeSpan.FromSeconds(TotalTimeoutSeconds) };
				HandlerBuildCount++;
			}
			return _handler;
		}
	}

	private HttpClient GetHttp()
	{
		GetHandler();
		return _http!;
	}

	private async ValueTask<Stream> BindAndConnectAsync(SocketsHttpConnectionContext context, CancellationToken ct)
	{
		HttpRequestMessage req = context.InitialRequestMessage;
		if (!req.Options.TryGetValue(ValidatedIpKey, out IPAddress? ip) || ip is null
			|| !req.Options.TryGetValue(ValidatedHostKey, out string? host) || string.IsNullOrEmpty(host)
			|| !string.Equals(host, context.DnsEndPoint.Host, StringComparison.OrdinalIgnoreCase))
		{
			// 缺少已验证 IP 或主机不一致 → 拒绝连接（安全失败），绝不回退到系统解析。
			throw new InvalidOperationException("缺少已验证的解析 IP 或连接主机不一致");
		}
		Interlocked.Increment(ref ConnectCallCount);
		return await ConnectFactory(ip, context.DnsEndPoint.Port, ct).ConfigureAwait(false);
	}

	private async Task<HttpResponseMessage> FetchWithRealClientAsync(
		Uri uri, IPAddress validatedIp, Dictionary<string, string> conditionalHeaders, CancellationToken ct)
	{
		HttpClient http = GetHttp();
		using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, uri);
		request.Headers.UserAgent.ParseAdd("Pineglow-Launcher/1.0");
		// 把"已验证 IP + 原始 host"写入请求选项，ConnectCallback 据此绑定实际 TCP 连接地址。
		request.Options.Set(ValidatedIpKey, validatedIp);
		request.Options.Set(ValidatedHostKey, uri.Host);
		if (conditionalHeaders.TryGetValue("If-None-Match", out string? etag) && !string.IsNullOrEmpty(etag))
			request.Headers.TryAddWithoutValidation("If-None-Match", etag);
		if (conditionalHeaders.TryGetValue("If-Modified-Since", out string? ims) && !string.IsNullOrEmpty(ims))
			request.Headers.TryAddWithoutValidation("If-Modified-Since", ims);
		return await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
	}

	public void Dispose()
	{
		DisposeTransport();
		try { _globalSem.Dispose(); } catch { }
	}

	public ValueTask DisposeAsync()
	{
		DisposeTransport();
		try { _globalSem.Dispose(); } catch { }
		return ValueTask.CompletedTask;
	}

	private void DisposeTransport()
	{
		lock (_transportGate)
		{
			if (_disposed) return;
			_disposed = true;
			TransportDisposed = true;
			try { _http?.Dispose(); } catch { }
			try { _handler?.Dispose(); } catch { }
			_http = null;
			_handler = null;
		}
	}

	// =========================================================================
	//  身份 & 主机 & URL 校验
	// =========================================================================

	/// <summary>身份三元组白名单：仅 modrinth（已实测）+ curseforge（降级：图标端点 404，但 install 仍可工作）。</summary>
	public static bool IsValidSource(string? source)
		=> source is not null && (source.Equals("modrinth", StringComparison.OrdinalIgnoreCase) || source.Equals("curseforge", StringComparison.OrdinalIgnoreCase));

	/// <summary>类别白名单：mod / modpack / shader / resourcepack。</summary>
	public static bool IsValidCategory(string? category)
	{
		if (string.IsNullOrEmpty(category)) return false;
		switch (category.ToLowerInvariant())
		{
			case "mod":
			case "modpack":
			case "shader":
			case "resourcepack":
				return true;
			default:
				return false;
		}
	}

	/// <summary>项目 ID 合法性：长度 ≤ 80，仅 [a-zA-Z0-9_-]。空字符串不接受。</summary>
	public static bool IsValidProjectId(string? projectId)
	{
		if (string.IsNullOrEmpty(projectId)) return false;
		if (projectId.Length > MaxProjectIdLength) return false;
		for (int i = 0; i < projectId.Length; i++)
		{
			char c = projectId[i];
			bool ok = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '_' || c == '-';
			if (!ok) return false;
		}
		return true;
	}

	/// <summary>白名单主机精确匹配（不允许 EvilModrinth 之类前缀冒名）。</summary>
	public static bool IsAllowedHost(string? host)
	{
		if (string.IsNullOrWhiteSpace(host)) return false;
		foreach (string allowed in AllowedHosts)
		{
			if (string.Equals(host, allowed, StringComparison.OrdinalIgnoreCase)) return true;
		}
		return false;
	}

	/// <summary>HTTPS + 非空 host + 无用户信息（user:pass@host）。</summary>
	public static bool IsAllowedUri(Uri? uri)
	{
		if (uri is null) return false;
		if (uri.Scheme != Uri.UriSchemeHttps) return false;
		if (string.IsNullOrWhiteSpace(uri.Host)) return false;
		if (!string.IsNullOrEmpty(uri.UserInfo)) return false;
		return true;
	}

	/// <summary>解析后的所有地址必须为公网 IP。任一非公网（含 IPv4-mapped-IPv6）即拒绝。</summary>
	public static bool IsPublicAddress(IPAddress address) => SkinImageProxyService.IsPublicAddress(address);

	/// <summary>缓存键：(source, category, projectId, normalizedUrl) 的 SHA-256 hex（无任何特殊字符）。</summary>
	public static string ComputeCacheKey(string source, string category, string projectId, string normalizedUrl)
	{
		using SHA256 sha = SHA256.Create();
		// 把四元组编码成可读 ASCII（用 NUL 作分隔符避免与文件名/路径冲突）
		string input = source.ToLowerInvariant() + "\0" + category.ToLowerInvariant() + "\0" + projectId + "\0" + normalizedUrl;
		byte[] hash = sha.ComputeHash(Encoding.ASCII.GetBytes(input));
		StringBuilder sb = new StringBuilder(hash.Length * 2);
		for (int i = 0; i < hash.Length; i++) sb.Append(hash[i].ToString("x2"));
		return sb.ToString();
	}

	// =========================================================================
	//  可信 URL 注册（TTL + 无图标清除）
	// =========================================================================

	/// <summary>
	/// 注册可信 URL。url 为空/空白表示"本次搜索结果无图标"，会清除该身份的旧映射，
	/// 避免旧图标继续可访问。
	/// </summary>
	public void RegisterTrustedUrl(string source, string category, string projectId, string url)
	{
		if (!IsValidSource(source) || !IsValidCategory(category) || !IsValidProjectId(projectId)) return;
		string key = TrustKey(source, category, projectId);
		if (string.IsNullOrWhiteSpace(url))
		{
			// 无图标 → 清除旧映射
			lock (_trustGate)
			{
				_trust.Remove(key);
			}
			return;
		}
		if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) || !IsAllowedUri(uri) || !IsAllowedHost(uri.Host)) return;
		// 拒绝路径中含 .. 或被规范化为路径穿越的 URL
		string absPath = Uri.UnescapeDataString(uri.AbsolutePath);
		if (absPath.Contains("..", StringComparison.Ordinal)) return;
		lock (_trustGate)
		{
			_trust[key] = new TrustEntry { Url = uri.ToString(), RegisteredAtTicks = UtcNowTicks() };
		}
	}

	/// <summary>读取可信 URL；超过 TrustedUrlTtl 的映射视为不存在（并移除），调用方得到 NotFound。</summary>
	public bool TryGetTrustedUrl(string source, string category, string projectId, out string url)
	{
		url = string.Empty;
		if (!IsValidSource(source) || !IsValidCategory(category) || !IsValidProjectId(projectId)) return false;
		string key = TrustKey(source, category, projectId);
		long now = UtcNowTicks();
		lock (_trustGate)
		{
			if (!_trust.TryGetValue(key, out TrustEntry? entry)) return false;
			// 真正执行可信 URL TTL：过期即视为不可信，且不再联网。
			if (now - entry.RegisteredAtTicks > TrustedUrlTtl.Ticks)
			{
				_trust.Remove(key);
				return false;
			}
			url = entry.Url;
			return true;
		}
	}

	private static string TrustKey(string source, string category, string projectId)
		=> source.ToLowerInvariant() + "|" + category.ToLowerInvariant() + "|" + projectId;

	private sealed class TrustEntry
	{
		public string Url = string.Empty;
		public long RegisteredAtTicks;
	}

	// =========================================================================
	//  主入口
	// =========================================================================

	/// <summary>
	/// 获取经过验证的图标字节。调用方负责传入合法的 source/category/projectId 三元组。
	/// 失败时返回的 IconResult.Status 表明根因；不向上抛网络异常。
	/// </summary>
	public async Task<IconResult> GetAsync(string source, string category, string projectId, CancellationToken ct = default)
	{
		if (!IsValidSource(source) || !IsValidCategory(category) || !IsValidProjectId(projectId))
		{
			return IconResult.NotFound();
		}

		if (!TryGetTrustedUrl(source, category, projectId, out string trustedUrl))
		{
			return IconResult.NotFound();
		}

		if (!Uri.TryCreate(trustedUrl, UriKind.Absolute, out Uri? firstUri) || !IsAllowedUri(firstUri))
		{
			return IconResult.NotFound();
		}

		IconResult? cached = TryReadCache(source, category, projectId, trustedUrl);
		if (cached != null && cached.Status == IconStatus.FromCache)
		{
			return cached;
		}

		return await DedupedFetchAsync(source, category, projectId, firstUri, trustedUrl, cached, ct).ConfigureAwait(false);
	}

	private async Task<IconResult> DedupedFetchAsync(string source, string category, string projectId, Uri firstUri, string trustedUrl, IconResult? staleCache, CancellationToken ct)
	{
		string cacheKey = ComputeCacheKey(source, category, projectId, trustedUrl);
		Task<IconResult> task;
		lock (_inFlightGate)
		{
			if (_inFlight.TryGetValue(cacheKey, out Task<IconResult>? existing))
			{
				task = existing;
			}
			else
			{
				// 共享 in-flight 下载不归属任一调用者的 CancellationToken（使用 CancellationToken.None，
				// 由 transport 的 ConnectTimeout / HttpClient.Timeout 做服务级超时兜底）。
				// 这样第一个等待者取消不会连带取消其他等待者。
				task = FetchAndCacheAsync(source, category, projectId, firstUri, trustedUrl, staleCache, CancellationToken.None);
				_inFlight[cacheKey] = task;
				_ = task.ContinueWith(_ => { lock (_inFlightGate) { _inFlight.Remove(cacheKey); } }, TaskScheduler.Default);
			}
		}
		return await AwaitWithCancellationAsync(task, ct).ConfigureAwait(false);
	}

	private static async Task<IconResult> AwaitWithCancellationAsync(Task<IconResult> task, CancellationToken ct)
	{
		if (!ct.CanBeCanceled || task.IsCompleted)
		{
			return await task.ConfigureAwait(false);
		}
		TaskCompletionSource<bool> tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		using (ct.Register(() => tcs.TrySetResult(true)))
		{
			Task winner = await Task.WhenAny(task, tcs.Task).ConfigureAwait(false);
			if (ReferenceEquals(winner, task) || task.IsCompleted)
			{
				return await task.ConfigureAwait(false);
			}
		}
		// 仅此等待者被取消；共享下载继续在后台完成，不影响其他等待者。
		return IconResult.Of(IconStatus.NetworkError);
	}

	private async Task<IconResult> FetchAndCacheAsync(string source, string category, string projectId, Uri firstUri, string trustedUrl, IconResult? staleCache, CancellationToken ct)
	{
		try
		{
			await _globalSem.WaitAsync(ct).ConfigureAwait(false);
		}
		catch (OperationCanceledException)
		{
			return IconResult.Of(IconStatus.NetworkError);
		}

		try
		{
			Dictionary<string, string> conditionalHeaders = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
			if (staleCache != null)
			{
				if (!string.IsNullOrEmpty(staleCache.ETag)) conditionalHeaders["If-None-Match"] = staleCache.ETag!;
				if (!string.IsNullOrEmpty(staleCache.LastModified)) conditionalHeaders["If-Modified-Since"] = staleCache.LastModified!;
			}

			DownloadOutcome outcome = await DownloadWithRedirectsAsync(firstUri, conditionalHeaders, ct).ConfigureAwait(false);

			// 304 → 复用 stale 缓存，刷新 manifest 元数据时间
			if (outcome.StatusCode == 304 && staleCache != null && staleCache.Bytes != null && !string.IsNullOrEmpty(staleCache.CachePath))
			{
				try
				{
					RefreshCacheMetadata(source, category, projectId, trustedUrl, staleCache.CachePath, outcome.ETag, outcome.LastModified, staleCache.Mime ?? "image/png");
				}
				catch { /* 元数据刷新失败不致命 */ }
				return IconResult.CacheHit(staleCache.Bytes, staleCache.Mime ?? "image/png", staleCache.CachePath, outcome.ETag, outcome.LastModified, stale: false);
			}

			if (outcome.StatusCode == 413)
			{
				TryDeleteTemp(outcome.TempPath);
				return IconResult.Of(IconStatus.TooLarge);
			}
			if (outcome.StatusCode >= 400)
			{
				TryDeleteTemp(outcome.TempPath);
				if (staleCache != null && staleCache.Bytes != null && !string.IsNullOrEmpty(staleCache.CachePath) && File.Exists(staleCache.CachePath))
				{
					return IconResult.CacheHit(staleCache.Bytes, staleCache.Mime ?? "image/png", staleCache.CachePath, outcome.ETag, outcome.LastModified, stale: true);
				}
				return IconResult.Of(IconStatus.UpstreamError);
			}

			// 1) 响应 MIME 规范化后必须在白名单内。
			string? normalizedMime = NormalizeMime(outcome.Mime);
			if (normalizedMime is null || !IsAllowedMime(normalizedMime))
			{
				TryDeleteTemp(outcome.TempPath);
				return IconResult.Of(IconStatus.BadContentType);
			}

			// 2) 魔数检测实际格式。
			if (!TryDetectImageFormat(outcome.Head, out string detectedExt, out string detectedMime))
			{
				TryDeleteTemp(outcome.TempPath);
				return IconResult.Of(IconStatus.BadImage);
			}

			// 3) 验证内容优先（收口四）：上游声明为允许的图片 MIME 但与真实检测格式不一致时，
			//    记录结构化警告并接受内容——最终格式以魔数 + 结构 + 完整解码确认的检测格式为准
			//    （manifest 与响应均使用验证后的 MIME）。非图片 MIME 已在第 1 步被拒绝，
			//    错标接受的前提是全部来源安全条件（HTTPS/白名单/公网 DNS/IP 绑定/TLS/大小）已满足。
			if (!string.Equals(normalizedMime, detectedMime, StringComparison.Ordinal))
			{
				LauncherLog.Warn("[ContentIcon] " + BuildMimeMismatchWarning(firstUri.Host, normalizedMime, detectedMime, outcome.Written));
			}

			if (outcome.ContentLength > MaxBytes || outcome.Written > MaxBytes)
			{
				TryDeleteTemp(outcome.TempPath);
				return IconResult.Of(IconStatus.TooLarge);
			}
			if (outcome.Written <= 0)
			{
				TryDeleteTemp(outcome.TempPath);
				return IconResult.Of(IconStatus.BadImage);
			}

			// 4) 有限结构校验（截断 / chunk 越界 / 长度不一致 → BadImage）。
			byte[] fullBytes;
			try
			{
				fullBytes = File.ReadAllBytes(outcome.TempPath);
			}
			catch
			{
				TryDeleteTemp(outcome.TempPath);
				return IconResult.Of(IconStatus.BadImage);
			}
			if (!TryValidateImageStructure(fullBytes, detectedExt))
			{
				TryDeleteTemp(outcome.TempPath);
				return IconResult.Of(IconStatus.BadImage);
			}

			// 4b) 真实解码器验证：必须能被 SkiaSharp 完整解码且宽高 > 0。
			if (!TryValidateImageWithDecoder(fullBytes, detectedExt))
			{
				TryDeleteTemp(outcome.TempPath);
				return IconResult.Of(IconStatus.BadImage);
			}

			// 5) manifest 式一致缓存提交：图片版本化落盘，manifest 原子切换。
			try
			{
				string finalPath = CommitCache(source, category, projectId, trustedUrl, outcome.TempPath, detectedExt, detectedMime, fullBytes.LongLength, outcome.ETag, outcome.LastModified);
				EnforceCacheBudget();
				return IconResult.Success(fullBytes, detectedMime, finalPath, outcome.ETag, outcome.LastModified);
			}
			catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
			{
				LauncherLog.Error($"[ContentIcon] cache write failed: {ex.GetType().Name}: {ex.Message}");
				TryDeleteTemp(outcome.TempPath);
				return IconResult.Of(IconStatus.NetworkError);
			}
		}
		catch (OperationCanceledException)
		{
			return IconResult.Of(IconStatus.NetworkError);
		}
		catch (Exception ex)
		{
			// 将异常信息写入日志；返回 NetworkError（不向上抛）
			LauncherLog.Error($"[ContentIcon] fetch failed: {ex.GetType().FullName}: {ex.Message}");
			if (staleCache != null && staleCache.Bytes != null && !string.IsNullOrEmpty(staleCache.CachePath) && File.Exists(staleCache.CachePath))
			{
				return IconResult.CacheHit(staleCache.Bytes, staleCache.Mime ?? "image/png", staleCache.CachePath, staleCache.ETag, staleCache.LastModified, stale: true);
			}
			return IconResult.Of(IconStatus.NetworkError);
		}
		finally
		{
			try { _globalSem.Release(); } catch { /* disposed */ }
		}
	}

	// =========================================================================
	//  下载核心：逐跳重定向 + DNS 解析校验绑定 + 流式 + 大小
	// =========================================================================

	private sealed class DownloadOutcome
	{
		public int StatusCode;
		public string Mime = string.Empty;
		public long ContentLength;
		public long Written;
		public string TempPath = string.Empty;
		public byte[] Head = Array.Empty<byte>();
		public string? ETag;
		public string? LastModified;
	}

	/// <summary>在所有解析结果均为公网的前提下，优先选 IPv4（多数网络无 IPv6 直达）。</summary>
	private static IPAddress ChooseConnectionAddress(IPAddress[] addresses)
	{
		foreach (IPAddress a in addresses)
		{
			if (a.AddressFamily == AddressFamily.InterNetwork) return a;
		}
		return addresses[0];
	}

	private async Task<DownloadOutcome> DownloadWithRedirectsAsync(Uri startUri, Dictionary<string, string> conditionalHeaders, CancellationToken ct)
	{
		Uri current = startUri;
		DownloadOutcome outcome = new DownloadOutcome();
		for (int hop = 0; hop <= MaxRedirects; hop++)
		{
			if (!IsAllowedUri(current) || !IsAllowedHost(current.Host))
			{
				outcome.StatusCode = 502;
				return outcome;
			}
			// 路径穿越也拒绝
			string absPath = Uri.UnescapeDataString(current.AbsolutePath);
			if (absPath.Contains("..", StringComparison.Ordinal))
			{
				outcome.StatusCode = 502;
				return outcome;
			}

			// 每一跳都重新解析并校验：所有解析结果必须为公网 IP，任一非公网即拒绝。
			IPAddress validatedIp;
			try
			{
				IPAddress[] addresses = await DnsResolver(current.DnsSafeHost, ct).ConfigureAwait(false);
				if (addresses.Length == 0 || addresses.Any(a => !IsPublicAddress(a)))
				{
					outcome.StatusCode = 502;
					return outcome;
				}
				validatedIp = ChooseConnectionAddress(addresses);
			}
			catch
			{
				outcome.StatusCode = 504;
				return outcome;
			}

			HttpResponseMessage response;
			try
			{
				if (HttpFetcher != null)
				{
					// 测试注入路径：仍先完成 DNS 校验，再交给注入函数。
					response = await HttpFetcher(current, ct).ConfigureAwait(false);
				}
				else
				{
					// 生产路径：把"已验证 IP + 原始 host"写入请求选项，由 ConnectCallback 绑定实际 TCP 连接，
					// TLS SNI/证书校验针对原始 CDN host 保留。条件请求头只随第一跳发送。
					Dictionary<string, string> hopHeaders = hop == 0 ? conditionalHeaders : EmptyConditional;
					response = await FetchWithRealClientAsync(current, validatedIp, hopHeaders, ct).ConfigureAwait(false);
				}
			}
			catch (OperationCanceledException)
			{
				throw;
			}
			catch
			{
				outcome.StatusCode = 504;
				return outcome;
			}

			try
			{
				int status = (int)response.StatusCode;
				outcome.StatusCode = status;
				outcome.Mime = response.Content.Headers.ContentType?.MediaType ?? string.Empty;
				outcome.ContentLength = response.Content.Headers.ContentLength ?? 0L;
				outcome.ETag = response.Headers.ETag?.Tag;
				// Last-Modified 是实体头：真实 CDN 放在 Content.Headers，回退到 Response.Headers
				string? lastModified = response.Content.Headers.TryGetValues("Last-Modified", out IEnumerable<string>? lmContent)
					? string.Join(",", lmContent)
					: (response.Headers.TryGetValues("Last-Modified", out IEnumerable<string>? lmResp)
						? string.Join(",", lmResp)
						: null);
				outcome.LastModified = lastModified;

				if (status >= 300 && status < 400)
				{
					if (response.Headers.Location is null)
					{
						return outcome;
					}
					if (hop == MaxRedirects)
					{
						// 重定向链超过上限 → 视为上游异常（508 Loop Detected）
						outcome.StatusCode = 508;
						return outcome;
					}
					Uri? next = response.Headers.Location.IsAbsoluteUri
						? response.Headers.Location
						: new Uri(current, response.Headers.Location);
					// 下一跳会重新解析、校验并绑定；对不安全的 Location 在循环开头即拒绝。
					current = next;
					continue;
				}

				if (status < 200 || status >= 300)
				{
					return outcome;
				}

				if (status == 304)
				{
					return outcome;
				}

				EnsureCacheRoot();
				string tempPath = Path.Combine(CacheRoot, ".tmp-" + Guid.NewGuid().ToString("N"));
				long written = 0;
				byte[] head = Array.Empty<byte>();
				using (FileStream output = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
				{
					using Stream input = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
					byte[] buffer = new byte[16384];
					int read;
					while ((read = await input.ReadAsync(buffer.AsMemory(0, buffer.Length), ct).ConfigureAwait(false)) > 0)
					{
						written += read;
						if (written > MaxBytes)
						{
							try { output.Close(); } catch { }
							try { File.Delete(tempPath); } catch { }
							outcome.StatusCode = 413;
							return outcome;
						}
						if (head.Length < 16)
						{
							int take = Math.Min(read, 16 - head.Length);
							byte[] chunk = new byte[head.Length + take];
							Array.Copy(head, chunk, head.Length);
							Array.Copy(buffer, 0, chunk, head.Length, take);
							head = chunk;
						}
						await output.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
					}
				}

				outcome.Written = written;
				outcome.TempPath = tempPath;
				outcome.Head = head;
				return outcome;
			}
			finally
			{
				response.Dispose();
			}
		}
		outcome.StatusCode = 508;
		return outcome;
	}

	// =========================================================================
	//  缓存层（manifest 式一致提交 + 崩溃恢复）
	// =========================================================================

	/// <summary>
	/// 一致缓存提交：
	///   1) 下载临时文件移动为版本化图片 <urlHash>.<ext>.v<ticks>（先于 manifest 落盘）；
	///   2) 元数据写入 <urlHash>.json.tmp-*；
	///   3) File.Move(tmp → <urlHash>.json) 原子切换 manifest 指针（唯一提交点）。
	/// 崩溃发生在 1) 之后、3) 之前 → 新图片是孤立版本，下次读取清理；旧 manifest/旧版本仍一致。
	/// </summary>
	private string CommitCache(string source, string category, string projectId, string trustedUrl, string tempPath, string ext, string mime, long length, string? etag, string? lastModified)
	{
		EnsureCacheRoot();
		string urlHash = ComputeCacheKey(source, category, projectId, trustedUrl);
		string dir = Path.Combine(CacheRoot, source.ToLowerInvariant(), category.ToLowerInvariant());
		Directory.CreateDirectory(dir);
		long now = UtcNowTicks();
		string imageName = urlHash + "." + SanitizeExt(ext) + ".v" + now.ToString(CultureInfo.InvariantCulture);
		string imagePath = Path.Combine(dir, imageName);
		// 故障点：图片提交失败模拟（测试）
		BeforeImageCommit?.Invoke(tempPath, imagePath);
		// 1) 图片版本化落盘
		File.Move(tempPath, imagePath, overwrite: false);
		// 2) manifest 临时文件
		string manifestPath = Path.Combine(dir, urlHash + ".json");
		string manifestTmp = manifestPath + ".tmp-" + Guid.NewGuid().ToString("N");
		File.WriteAllText(manifestTmp, BuildManifestJson(urlHash, imageName, mime, length, now, etag, lastModified), new UTF8Encoding(false));
		// 故障点：元数据提交失败模拟（测试）
		BeforeManifestCommit?.Invoke(manifestTmp, manifestPath);
		// 3) 原子提交
		File.Move(manifestTmp, manifestPath, overwrite: true);
		// 4) 清理旧版本（best-effort；崩溃恢复在读取时也会做）
		CleanupOrphans(dir, urlHash, keepImage: imageName);
		return imagePath;
	}

	private void RefreshCacheMetadata(string source, string category, string projectId, string trustedUrl, string imagePath, string? etag, string? lastModified, string mime)
	{
		EnsureCacheRoot();
		string urlHash = ComputeCacheKey(source, category, projectId, trustedUrl);
		string dir = Path.Combine(CacheRoot, source.ToLowerInvariant(), category.ToLowerInvariant());
		Directory.CreateDirectory(dir);
		string imageName = Path.GetFileName(imagePath);
		long length = new FileInfo(imagePath).Length;
		long now = UtcNowTicks();
		string manifestPath = Path.Combine(dir, urlHash + ".json");
		string manifestTmp = manifestPath + ".tmp-" + Guid.NewGuid().ToString("N");
		File.WriteAllText(manifestTmp, BuildManifestJson(urlHash, imageName, mime, length, now, etag, lastModified), new UTF8Encoding(false));
		File.Move(manifestTmp, manifestPath, overwrite: true);
	}

	private IconResult? TryReadCache(string source, string category, string projectId, string trustedUrl)
	{
		try
		{
			EnsureCacheRoot();
			string dir = Path.Combine(CacheRoot, source.ToLowerInvariant(), category.ToLowerInvariant());
			if (!Directory.Exists(dir)) return null;
			string urlHash = ComputeCacheKey(source, category, projectId, trustedUrl);
			string manifestPath = Path.Combine(dir, urlHash + ".json");
			if (!File.Exists(manifestPath))
			{
				// 无 manifest：清理崩溃遗留的孤立版本/临时文件。
				CleanupOrphans(dir, urlHash, keepImage: null);
				return null;
			}

			CacheMetadata meta;
			try { meta = ReadManifest(manifestPath); }
			catch { TryQuarantine(manifestPath); CleanupOrphans(dir, urlHash, keepImage: null); return null; }
			if (meta.FormatVersion != FormatVersion || !string.Equals(meta.UrlHash, urlHash, StringComparison.OrdinalIgnoreCase))
			{
				TryQuarantine(manifestPath);
				CleanupOrphans(dir, urlHash, keepImage: null);
				return null;
			}
			if (string.IsNullOrEmpty(meta.ImageName) || !IsSafeImageName(meta.ImageName, urlHash))
			{
				TryQuarantine(manifestPath);
				CleanupOrphans(dir, urlHash, keepImage: null);
				return null;
			}

			// 以 manifest 引用的图片为准清理其它孤立版本（崩溃恢复）。
			CleanupOrphans(dir, urlHash, keepImage: meta.ImageName);
			string imagePath = Path.Combine(dir, meta.ImageName);
			if (!File.Exists(imagePath))
			{
				TryQuarantine(manifestPath);
				return null;
			}

			FileInfo fi = new FileInfo(imagePath);
			if (fi.Length != meta.Length) { TryQuarantine(imagePath); TryQuarantine(manifestPath); return null; }
			byte[] bytes = File.ReadAllBytes(imagePath);
			if (!TryDetectImageFormat(bytes, out string detectedExt, out string detectedMime))
			{
				TryQuarantine(imagePath);
				TryQuarantine(manifestPath);
				return null;
			}
			if (!string.Equals(detectedMime, meta.Mime, StringComparison.OrdinalIgnoreCase))
			{
				TryQuarantine(imagePath);
				TryQuarantine(manifestPath);
				return null;
			}
			if (!TryValidateImageStructure(bytes, detectedExt))
			{
				TryQuarantine(imagePath);
				TryQuarantine(manifestPath);
				return null;
			}

			// 真实解码器验证缓存图片
			if (!TryValidateImageWithDecoder(bytes, detectedExt))
			{
				TryQuarantine(imagePath);
				TryQuarantine(manifestPath);
				return null;
			}

			// writtenAtTicks 合法性：非正数或位于不合理的未来（> 1 天时钟偏差）→ 损坏 manifest，隔离
			if (meta.WrittenAtTicks <= 0 || meta.WrittenAtTicks > UtcNowTicks() + ManifestMaxFutureSkewTicks)
			{
				TryQuarantine(imagePath);
				TryQuarantine(manifestPath);
				return null;
			}

			long ageTicks = UtcNowTicks() - meta.WrittenAtTicks;
			TimeSpan age = TimeSpan.FromTicks(ageTicks);
			if (age > StaleAfterTtl)
			{
				try { File.Delete(imagePath); } catch { }
				try { File.Delete(manifestPath); } catch { }
				return null;
			}
			bool stale = age > CacheFreshTtl;
			return IconResult.CacheHit(bytes, meta.Mime, imagePath, meta.ETag, meta.LastModified, stale);
		}
		catch
		{
			// IO 异常 → 当作未命中
		}
		return null;
	}

	/// <summary>manifest 引用的图片名必须符合 <urlHash>.<ext>.v<digits> 规则（防路径穿越/任意文件引用）。</summary>
	private static bool IsSafeImageName(string name, string urlHash)
	{
		foreach (string ext in new[] { "png", "jpg", "webp" })
		{
			string prefix = urlHash + "." + ext + ".v";
			if (name.StartsWith(prefix, StringComparison.Ordinal))
			{
				string rest = name.Substring(prefix.Length);
				if (rest.Length == 0) return false;
				foreach (char c in rest) if (!char.IsDigit(c)) return false;
				return true;
			}
		}
		return false;
	}

	/// <summary>
	/// 清理该 urlHash 的崩溃残留：manifest 临时文件、图片临时文件、旧格式 .meta.json、
	/// 未被当前 manifest 引用的旧版本图片。keepImage 非空时保留它。
	/// </summary>
	private void CleanupOrphans(string dir, string urlHash, string? keepImage)
	{
		try
		{
			if (!Directory.Exists(dir)) return;
			foreach (string f in Directory.GetFiles(dir, urlHash + "*"))
			{
				string name = Path.GetFileName(f);
				if (name == urlHash + ".json") continue;
				if (keepImage != null && name == keepImage) continue;
				if (name.StartsWith(urlHash + ".json.tmp-", StringComparison.Ordinal)) { TryDeleteFile(f); continue; }
				if (name.EndsWith(".tmp", StringComparison.Ordinal)) { TryDeleteFile(f); continue; }
				if (name.EndsWith(".meta.json", StringComparison.Ordinal)) { TryDeleteFile(f); continue; }
				if (name.StartsWith(urlHash + ".", StringComparison.Ordinal) && name.Contains(".v", StringComparison.Ordinal))
				{
					TryDeleteFile(f);
				}
			}
		}
		catch { }
	}

	private static string SanitizeExt(string ext)
	{
		if (string.IsNullOrEmpty(ext)) return "bin";
		foreach (char c in ext)
		{
			if (!(char.IsLetterOrDigit(c) || c == '_' || c == '-')) return "bin";
		}
		return ext.ToLowerInvariant();
	}

	/// <summary>manifest DTO for System.Text.Json serialization</summary>
	private sealed class ManifestDto
	{
		public int FormatVersion { get; set; }
		public string UrlHash { get; set; } = string.Empty;
		public string Image { get; set; } = string.Empty;
		public string Mime { get; set; } = string.Empty;
		public long Length { get; set; }
		public long WrittenAtTicks { get; set; }
		public string? ETag { get; set; }
		public string? LastModified { get; set; }
	}

	private static readonly JsonSerializerOptions JsonOpts = new JsonSerializerOptions
	{
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
		WriteIndented = false
	};

	private static string BuildManifestJson(string urlHash, string imageName, string mime, long length, long writtenAtTicks, string? etag, string? lastModified)
	{
		var dto = new ManifestDto
		{
			FormatVersion = FormatVersion,
			UrlHash = urlHash,
			Image = imageName,
			Mime = mime,
			Length = length,
			WrittenAtTicks = writtenAtTicks,
			ETag = etag,
			LastModified = lastModified
		};
		return JsonSerializer.Serialize(dto, JsonOpts);
	}

	private sealed class CacheMetadata
	{
		public int FormatVersion;
		public string Mime = string.Empty;
		public long Length;
		public long WrittenAtTicks;
		public string UrlHash = string.Empty;
		public string ImageName = string.Empty;
		public string? ETag;
		public string? LastModified;
	}

	private static CacheMetadata ReadManifest(string path)
	{
		string json = File.ReadAllText(path);
		ManifestDto? dto;
		try
		{
			dto = JsonSerializer.Deserialize<ManifestDto>(json, JsonOpts);
		}
		catch (JsonException)
		{
			throw; // 让调用方捕获并处理为损坏缓存
		}
		if (dto == null) throw new JsonException("Manifest deserialization returned null");

		var m = new CacheMetadata
		{
			FormatVersion = dto.FormatVersion,
			Mime = dto.Mime ?? string.Empty,
			Length = dto.Length,
			WrittenAtTicks = dto.WrittenAtTicks,
			UrlHash = dto.UrlHash ?? string.Empty,
			ImageName = dto.Image ?? string.Empty,
			ETag = dto.ETag,
			LastModified = dto.LastModified
		};
		return m;
	}

	private void TryQuarantine(string path)
	{
		try
		{
			if (!File.Exists(path)) return;
			string dir = Path.GetDirectoryName(path) ?? CacheRoot;
			string name = Path.GetFileName(path);
			string corrupt = Path.Combine(dir, name + ".corrupt-" + DateTimeOffset.UtcNow.ToUnixTimeSeconds());
			File.Move(path, corrupt, overwrite: false);
		}
		catch
		{
			try { File.Delete(path); } catch { }
		}
	}

	private static void TryDeleteFile(string path)
	{
		try { if (File.Exists(path)) File.Delete(path); } catch { }
	}

	private void TryDeleteTemp(string path)
	{
		if (string.IsNullOrEmpty(path)) return;
		TryDeleteFile(path);
	}

	internal void EnforceCacheBudget()
	{
		try
		{
			if (!Directory.Exists(CacheRoot)) return;

			// 1. 枚举并解析有效 manifest，组装逻辑缓存条目
			var entries = new List<CacheEntry>();
			foreach (string dir in Directory.GetDirectories(CacheRoot, "*", SearchOption.AllDirectories))
			{
				foreach (string manifestPath in Directory.GetFiles(dir, "*.json"))
				{
					// 严格文件名解析：只有 <64hex>.json 形态的 manifest 才参与预算
					if (!TryClassifyCacheFileName(Path.GetFileName(manifestPath), out CacheFileNameInfo mfi)
						|| mfi.Kind != CacheFileKind.Manifest)
					{
						continue;
					}

					CacheMetadata meta;
					try { meta = ReadManifest(manifestPath); }
					catch { continue; } // 损坏 manifest 不计入预算

					if (meta.FormatVersion != FormatVersion) continue;
					if (string.IsNullOrEmpty(meta.ImageName)) continue;
					if (!IsSafeImageName(meta.ImageName, meta.UrlHash)) continue;

					string imagePath = Path.Combine(dir, meta.ImageName);
					if (!File.Exists(imagePath)) continue;

					long totalSize = new FileInfo(manifestPath).Length + new FileInfo(imagePath).Length;
					DateTime lastAccess = File.GetLastWriteTimeUtc(manifestPath);
					if (File.GetLastWriteTimeUtc(imagePath) < lastAccess) lastAccess = File.GetLastWriteTimeUtc(imagePath);

					entries.Add(new CacheEntry
					{
						ManifestPath = manifestPath,
						ImagePath = imagePath,
						TotalSize = totalSize,
						LastAccessUtc = lastAccess,
						UrlHash = meta.UrlHash
					});
				}
			}

			// 2. 按最后访问时间从旧到新排序
			entries.Sort((a, b) => a.LastAccessUtc.CompareTo(b.LastAccessUtc));

			// 3. 计算总大小并淘汰最旧的完整条目
			long total = entries.Sum(e => e.TotalSize);
			long budget = CacheBudgetBytesOverride ?? MaxCacheBytes;
			foreach (var entry in entries)
			{
				if (total <= budget) break;

				// 先撤销 manifest 可见性，再删除版本图片
				try { BeforeBudgetManifestDelete?.Invoke(entry.ManifestPath); } catch { }
				try { File.Delete(entry.ManifestPath); } catch { }
				try { BeforeBudgetImageDelete?.Invoke(entry.ImagePath); } catch { }
				try { File.Delete(entry.ImagePath); } catch { }
				total -= entry.TotalSize;
			}

			// 4. 孤立临时文件、损坏文件、旧版本使用独立清理规则（宽限期 1 小时）
			CleanupOrphanedFiles(OrphanGracePeriod);
		}
		catch { /* 清理失败不影响主流程 */ }
	}

	/// <summary>孤立文件清理宽限期（避免误删正在提交的文件）。</summary>
	internal static readonly TimeSpan OrphanGracePeriod = TimeSpan.FromHours(1);

	/// <summary>
	/// 清理孤立文件：下载临时文件、manifest 临时文件、隔离文件、未被任何 manifest 引用的旧版本图片。
	/// 文件身份通过 TryClassifyCacheFileName 严格解析（真实命名协议）：
	///   下载临时    .tmp-&lt;32hex GUID&gt;
	///   manifest    &lt;hash&gt;.json
	///   manifest 临时 &lt;hash&gt;.json.tmp-&lt;hex&gt;
	///   隔离        &lt;原文件名&gt;.corrupt-&lt;unix 秒&gt;
	///   版本图片    &lt;hash&gt;.&lt;png|jpg|webp&gt;.v&lt;ticks&gt;
	/// 解析失败（含名称偶然含 .v 的无关文件）一律不动。宽限期内的文件不清理。
	/// </summary>
	internal void CleanupOrphanedFiles(TimeSpan gracePeriod)
	{
		try
		{
			if (!Directory.Exists(CacheRoot)) return;
			DateTime nowUtc = WallClock();
			DateTime cutoff = nowUtc - gracePeriod;

			foreach (string file in Directory.GetFiles(CacheRoot, "*", SearchOption.AllDirectories))
			{
				string name = Path.GetFileName(file);
				// 严格解析：非缓存命名协议的文件（含偶然包含 ".v" 的无关文件）绝不删除
				if (!TryClassifyCacheFileName(name, out CacheFileNameInfo info)) continue;

				FileInfo fi = new FileInfo(file);
				// 宽限期内的文件不清理（可能正在写入/提交）
				if (fi.LastWriteTimeUtc > cutoff) continue;

				bool isOrphan = info.Kind switch
				{
					CacheFileKind.DownloadTemp => true,
					CacheFileKind.ManifestTemp => true,
					CacheFileKind.QuarantinedManifest => true,
					CacheFileKind.QuarantinedImage => true,
					CacheFileKind.VersionedImage => !IsImageReferencedByAnyManifest(Path.GetDirectoryName(file)!, name),
					_ => false, // Manifest / Unrelated
				};

				if (isOrphan)
				{
					try { File.Delete(file); } catch { }
				}
			}
		}
		catch { }
	}

	/// <summary>检查目录内是否有任何有效 manifest 引用该图片文件名。</summary>
	private bool IsImageReferencedByAnyManifest(string dir, string imageName)
	{
		try
		{
			foreach (string mf in Directory.GetFiles(dir, "*.json"))
			{
				if (!TryClassifyCacheFileName(Path.GetFileName(mf), out CacheFileNameInfo mfi)
					|| mfi.Kind != CacheFileKind.Manifest)
				{
					continue;
				}
				CacheMetadata meta;
				try { meta = ReadManifest(mf); }
				catch { continue; }
				if (string.Equals(meta.ImageName, imageName, StringComparison.Ordinal)) return true;
			}
		}
		catch { }
		return false;
	}

	// =========================================================================
	//  缓存文件名严格解析（真实命名协议）
	// =========================================================================

	public enum CacheFileKind
	{
		Manifest,          // <hash>.json
		ManifestTemp,      // <hash>.json.tmp-<hex>
		QuarantinedManifest, // <hash>.json.corrupt-<digits>
		QuarantinedImage,  // <hash>.<ext>.v<ticks>.corrupt-<digits>
		DownloadTemp,      // .tmp-<32hex>
		VersionedImage,    // <hash>.<ext>.v<ticks>
		Unrelated,         // 其他（不参与任何清理）
	}

	public readonly record struct CacheFileNameInfo(CacheFileKind Kind, string Hash, string Ext, long VersionTicks, string Suffix);

	private const int CacheKeyHexLength = 64; // SHA-256 hex

	/// <summary>
	/// 按真实命名协议严格解析缓存文件名。解析失败返回 false（调用方必须当作 Unrelated 不删除）。
	/// </summary>
	internal static bool TryClassifyCacheFileName(string? name, out CacheFileNameInfo info)
	{
		info = default;
		if (string.IsNullOrEmpty(name) || name.IndexOfAny(new[] { '/', '\\' }) >= 0) return false;

		// 隔离文件：<原文件名>.corrupt-<unix 秒数字>
		int ci = name.LastIndexOf(".corrupt-", StringComparison.Ordinal);
		if (ci >= 0)
		{
			string baseName = name[..ci];
			string suffix = name[(ci + ".corrupt-".Length)..];
			if (suffix.Length == 0 || !suffix.All(char.IsAsciiDigit)) return false;
			if (baseName.EndsWith(".json", StringComparison.Ordinal)
				&& TryParseCacheHash(baseName[..^5], out string? hashM))
			{
				info = new CacheFileNameInfo(CacheFileKind.QuarantinedManifest, hashM, "json", 0, suffix);
				return true;
			}
			if (TryClassifyVersionedImageName(baseName, out string hashI, out string extI, out long ticksI))
			{
				info = new CacheFileNameInfo(CacheFileKind.QuarantinedImage, hashI, extI, ticksI, suffix);
				return true;
			}
			return false;
		}

		// manifest 临时文件：<hash>.json.tmp-<非空 hex 后缀（GUID N 形态或等价）>
		int ti = name.IndexOf(".json.tmp-", StringComparison.Ordinal);
		if (ti >= 0)
		{
			string hashPart = name[..ti];
			string suffix = name[(ti + ".json.tmp-".Length)..];
			if (suffix.Length > 0 && IsHexLowerOrUpper(suffix) && TryParseCacheHash(hashPart, out string? hashT))
			{
				info = new CacheFileNameInfo(CacheFileKind.ManifestTemp, hashT, "json", 0, suffix);
				return true;
			}
			return false;
		}

		// manifest：<hash>.json
		if (name.EndsWith(".json", StringComparison.Ordinal))
		{
			if (TryParseCacheHash(name[..^5], out string? hashJ))
			{
				info = new CacheFileNameInfo(CacheFileKind.Manifest, hashJ, "json", 0, string.Empty);
				return true;
			}
			return false;
		}

		// 版本化图片：<hash>.<png|jpg|webp>.v<digits>
		if (TryClassifyVersionedImageName(name, out string hash, out string ext, out long ticks))
		{
			info = new CacheFileNameInfo(CacheFileKind.VersionedImage, hash, ext, ticks, string.Empty);
			return true;
		}

		// 下载临时文件：.tmp-<hex GUID>（放在最后，避免与 .json.tmp- 冲突——后者已提前返回）
		if (name.StartsWith(".tmp-", StringComparison.Ordinal))
		{
			string rest = name[".tmp-".Length..];
			if (rest.Length >= 8 && IsHexLowerOrUpper(rest))
			{
				info = new CacheFileNameInfo(CacheFileKind.DownloadTemp, string.Empty, "tmp", 0, rest);
				return true;
			}
			return false;
		}

		return false;
	}

	/// <summary>解析 &lt;hash&gt;.&lt;ext&gt;.v&lt;digits&gt;，验证 hash（64 hex）、扩展名白名单与版本数字。</summary>
	private static bool TryClassifyVersionedImageName(string name, out string hash, out string ext, out long ticks)
	{
		hash = string.Empty; ext = string.Empty; ticks = 0;
		int vIdx = name.LastIndexOf(".v", StringComparison.Ordinal);
		if (vIdx < 0) return false;
		string digits = name[(vIdx + 2)..];
		if (digits.Length == 0 || !digits.All(char.IsAsciiDigit)) return false;
		string head = name[..vIdx];
		int dot = head.LastIndexOf('.');
		if (dot <= 0) return false;
		ext = head[(dot + 1)..];
		hash = head[..dot];
		if (ext != "png" && ext != "jpg" && ext != "webp") return false;
		if (!TryParseCacheHash(hash, out _)) return false;
		if (!long.TryParse(digits, out ticks)) return false;
		return true;
	}

	private static bool TryParseCacheHash(string? s, out string hash)
	{
		hash = s ?? string.Empty;
		return s is not null && s.Length == CacheKeyHexLength && IsHexLowerOrUpper(s);
	}

	private static bool IsHexLowerOrUpper(string s)
		=> s.All(c => (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F'));

	private sealed class CacheEntry
	{
		public string ManifestPath = string.Empty;
		public string ImagePath = string.Empty;
		public long TotalSize;
		public DateTime LastAccessUtc;
		public string UrlHash = string.Empty;
	}

	private void EnsureCacheRoot()
	{
		string fullRoot = Path.GetFullPath(CacheRoot);
		if (fullRoot.Contains("..", StringComparison.Ordinal))
		{
			throw new InvalidOperationException("缓存根目录路径非法");
		}
		Directory.CreateDirectory(fullRoot);
		CacheRoot = fullRoot;
	}

	// =========================================================================
	//  格式校验（MIME 规范化 + 魔数 + 有限结构）
	// =========================================================================

	/// <summary>规范化响应 MIME：去参数、去空白、小写；image/jpg 显式映射为 image/jpeg。</summary>
	public static string? NormalizeMime(string? mime)
	{
		if (string.IsNullOrWhiteSpace(mime)) return null;
		int semi = mime.IndexOf(';');
		string m = (semi >= 0 ? mime[..semi] : mime).Trim().ToLowerInvariant();
		if (m == "image/jpg") m = "image/jpeg";
		return m.Length == 0 ? null : m;
	}

	public static bool IsAllowedMime(string? mime)
	{
		string? m = NormalizeMime(mime);
		return m == "image/png" || m == "image/jpeg" || m == "image/webp";
	}

	/// <summary>魔数检测：仅判断文件签名对应的格式，不做结构校验。</summary>
	public static bool TryDetectImageFormat(ReadOnlySpan<byte> head, out string ext, out string mime)
	{
		ext = string.Empty; mime = string.Empty;
		if (head.Length >= 8 && head[..8].SequenceEqual(PngSignature))
		{
			ext = "png"; mime = "image/png"; return true;
		}
		if (head.Length >= 3 && head[..3].SequenceEqual(JpegSignature))
		{
			ext = "jpg"; mime = "image/jpeg"; return true;
		}
		if (head.Length >= 12 && head[..4].SequenceEqual(WebpRiff) && head.Slice(8, 4).SequenceEqual(WebpWebp))
		{
			ext = "webp"; mime = "image/webp"; return true;
		}
		return false;
	}

	/// <summary>
	/// 有限结构校验（PNG/JPEG/WebP），截断或结构损坏返回 false：
	///   PNG —— 签名 + IHDR(13) 为首 chunk + 至少一个 IDAT + IEND(0) 为最后一个 chunk，chunk 边界合法；
	///   JPEG —— SOI + 合法段序列 + 必须出现 EOI（容忍熵编码 FF00 填充），段长度越界即截断；
	///   WebP —— RIFF/WEBP 头 + RIFF 声明长度必须等于文件长度 - 8 + 合法 chunk。
	/// </summary>
	public static bool TryValidateImageStructure(ReadOnlySpan<byte> data, string format)
	{
		if (data.Length == 0) return false;
		switch (format)
		{
			case "png": return ValidatePngStructure(data);
			case "jpg": return ValidateJpegStructure(data);
			case "webp": return ValidateWebpStructure(data);
			default: return false;
		}
	}

	private static bool ValidatePngStructure(ReadOnlySpan<byte> d)
	{
		if (d.Length < 8 || !d[..8].SequenceEqual(PngSignature)) return false;
		int i = 8;
		bool sawIhdr = false;
		bool sawIdat = false;
		while (i < d.Length)
		{
			if (d.Length - i < 12) return false;                 // 至少 length(4)+type(4)+crc(4)
			long len = ((long)d[i] << 24) | ((long)d[i + 1] << 16) | ((long)d[i + 2] << 8) | d[i + 3];
			if (i + 12 + len > d.Length) return false;           // chunk 越界 → 截断
			string type = string.Concat((char)d[i + 4], (char)d[i + 5], (char)d[i + 6], (char)d[i + 7]);
			if (type == "IHDR")
			{
				if (sawIhdr || len != 13) return false;
				sawIhdr = true;
				// 宽高必须 > 0（IHDR 数据在 i+8 起）
				int w = (int)(((long)d[i + 8] << 24) | ((long)d[i + 9] << 16) | ((long)d[i + 10] << 8) | d[i + 11]);
				int h = (int)(((long)d[i + 12] << 24) | ((long)d[i + 13] << 16) | ((long)d[i + 14] << 8) | d[i + 15]);
				if (w <= 0 || h <= 0) return false;
			}
			else if (type == "IDAT")
			{
				sawIdat = true;
			}
			else if (type == "IEND")
			{
				if (len != 0) return false;
				// IEND 必须是最后一个 chunk
				return i + 12 == d.Length && sawIhdr && sawIdat;
			}
			i += 12 + (int)len;
		}
		return false;
	}

	private static bool ValidateJpegStructure(ReadOnlySpan<byte> d)
	{
		if (d.Length < 4 || d[0] != 0xFF || d[1] != 0xD8) return false;
		int i = 2;
		while (i < d.Length)
		{
			if (d[i] != 0xFF) return false;          // 段之间出现非 0xFF → 结构损坏
			if (i + 1 >= d.Length) return false;     // 末尾悬挂 FF → 截断
			byte marker = d[i + 1];
			if (marker == 0xD9) return true;         // EOI → 结构完整
			if (marker == 0x00) { i += 2; continue; }// 熵编码数据中的填充 FF
			if (marker == 0xD8) return false;        // 重复 SOI
			if (marker == 0x01 || (marker >= 0xD0 && marker <= 0xD7)) { i += 2; continue; } // 独立段
			if (i + 3 >= d.Length) return false;     // 需要 2 字节长度字段
			if (marker == 0xDA)                      // SOS：段后是熵编码数据，跳到 EOI
			{
				int sosLen = (d[i + 2] << 8) | d[i + 3];
				if (sosLen < 2) return false;
				int j = i + 2 + sosLen;
				while (j + 1 < d.Length)
				{
					if (d[j] == 0xFF)
					{
						if (d[j + 1] == 0xD9) return true;
						if (d[j + 1] == 0x00) { j += 2; continue; }
						j += 2; continue;
					}
					j++;
				}
				return false;
			}
			int segLen = (d[i + 2] << 8) | d[i + 3];
			if (segLen < 2) return false;
			if (i + 2 + segLen > d.Length) return false;   // 段越界 → 截断
			i += 2 + segLen;
		}
		return false;
	}

private static bool ValidateWebpStructure(ReadOnlySpan<byte> d)
	{
		if (d.Length < 12) return false;
		if (!d[..4].SequenceEqual(WebpRiff)) return false;
		if (!d.Slice(8, 4).SequenceEqual(WebpWebp)) return false;
		// RIFF little-endian：声明长度必须与实际文件长度一致（d[4..7] 小端）
		long riffSize = (long)(uint)(d[4] | (d[5] << 8) | (d[6] << 16) | (d[7] << 24));
		if (riffSize != d.Length - 8) return false;        // 声明的 RIFF 长度与实际不一致 → 截断/损坏
		long end = 8 + riffSize;                            // RIFF 数据区结束（含 chunk padding）
		long i = 12;
		bool sawImageChunk = false;
		while (i + 8 <= end)
		{
			string type = string.Concat((char)d[(int)i], (char)d[(int)i + 1], (char)d[(int)i + 2], (char)d[(int)i + 3]);
			// chunk size 必须从当前 chunk 起点读取（d[i+4..i+7] 小端）。
			// 全程 long 运算：恶意长度最大 0xFFFFFFFF 不会与 int 溢出混淆。
			long size = (long)(uint)(d[(int)i + 4] | (d[(int)i + 5] << 8) | (d[(int)i + 6] << 16) | (d[(int)i + 7] << 24));
			if (i + 8 + size > end) return false;          // chunk 越界 → 截断/损坏
			if (type == "VP8 " || type == "VP8L")
			{
				sawImageChunk = true;
			}
			else if (type != "VP8X" && type != "ALPH" && type != "ANIM" && type != "ANMF"
				  && type != "ICCP" && type != "EXIF" && type != "XMP ")
			{
				return false;                              // 未知 chunk 类型 → 拒绝
			}
			i += 8 + size;
			if ((size & 1) == 1) i++;                      // chunk 按 2 字节对齐的 padding
		}
		return sawImageChunk;                               // 必须包含至少一个真实图像 chunk（VP8/VP8L）
	}

	// 完整像素解码的防滥用上限（图标 ≤ 2 MiB，但 2 MiB PNG 可声明超大尺寸）
	internal const int MaxDecodeDimension = 4096;
	internal const long MaxDecodePixels = 4096L * 4096L;
	internal const long MaxDecodeMemoryBytes = 64L * 1024 * 1024; // 4 字节/像素

	/// <summary>
	/// 使用 SkiaSharp 进行完整像素解码验证（不是只识别图片头）。
	/// 步骤：创建 SKCodec → 校验编码器识别格式与魔数检测一致 → 宽高 > 0 且在上限内
	/// → checked 计算缓冲区大小 → 分配受限像素缓冲 → SKCodec.GetPixels 完整解码。
	/// 只有明确的 SKCodecResult.Success 才返回 true；IncompleteInput / InvalidInput /
	/// ErrorInInput / CouldNotRewind / InternalError 等一律返回 false。
	/// 所有资源（stream/codec/bitmap）在 finally 中释放。
	/// </summary>
	public static bool TryValidateImageWithDecoder(ReadOnlySpan<byte> data, string format)
	{
		if (data.Length == 0) return false;
		MemoryStream? stream = null;
		SKCodec? codec = null;
		SKBitmap? bitmap = null;
		try
		{
			stream = new MemoryStream(data.ToArray(), writable: false);
			codec = SKCodec.Create(stream);
			if (codec == null) return false;

			// 编码器识别的格式必须与魔数检测的格式一致（防止魔数与真实编码不符）
			SKEncodedImageFormat encoded = codec.EncodedFormat;
			bool formatMatches = format switch
			{
				"png" => encoded == SKEncodedImageFormat.Png,
				"jpg" => encoded == SKEncodedImageFormat.Jpeg,
				"webp" => encoded == SKEncodedImageFormat.Webp,
				_ => false,
			};
			if (!formatMatches) return false;

			int width = codec.Info.Width;
			int height = codec.Info.Height;
			if (width <= 0 || height <= 0) return false;
			if (width > MaxDecodeDimension || height > MaxDecodeDimension) return false;
			long pixels = checked((long)width * height);
			if (pixels > MaxDecodePixels) return false;
			long decodeBytes = checked(pixels * 4); // Bgra8888 = 4 字节/像素
			if (decodeBytes > MaxDecodeMemoryBytes) return false;

			bitmap = new SKBitmap(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);
			SKImageInfo target = new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);
			// 完整像素解码：只有明确成功才接受
			return codec.GetPixels(target, bitmap.GetPixels()) == SKCodecResult.Success;
		}
		catch
		{
			return false;
		}
		finally
		{
			bitmap?.Dispose();
			codec?.Dispose();
			stream?.Dispose();
		}
	}
}
