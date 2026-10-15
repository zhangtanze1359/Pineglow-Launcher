using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Pineglow.Launcher.Services;

[Collection("LocalApiService")]
public sealed class ContentIconServiceTests : IDisposable
{
	private readonly string _cacheRoot;
	private readonly List<string> _tempRoots = new();

	// 默认 DNS 解析器：返回已知公网 IP（Cloudflare 1.1.1.1），避免依赖真实 DNS 与环境代理劫持
	private static readonly IPAddress[] FakePublicIps = new[] { IPAddress.Parse("1.1.1.1") };
	private static Func<string, CancellationToken, Task<IPAddress[]>> FakeDns => (_, __) => Task.FromResult(FakePublicIps);

	public ContentIconServiceTests()
	{
		_cacheRoot = Path.Combine(Path.GetTempPath(), "pineglow-icon-tests-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(_cacheRoot);
	}

	public void Dispose()
	{
		try { if (Directory.Exists(_cacheRoot)) Directory.Delete(_cacheRoot, recursive: true); } catch { }
	}

	// 帮助：构造一个带默认 fake DNS + 默认 cacheRoot 的服务
	private ContentIconService NewService(Func<Uri, CancellationToken, Task<HttpResponseMessage>>? fetcher = null, Func<long>? clock = null)
		=> new ContentIconService(_cacheRoot, fetcher, clock, FakeDns);

	// -------------------------------------------------------------------------
	//  身份与 URL 校验
	// -------------------------------------------------------------------------

	[Theory]
	[InlineData("modrinth", true)]
	[InlineData("curseforge", true)]
	[InlineData("MODRINTH", true)]
	[InlineData("evil", false)]
	[InlineData(null, false)]
	[InlineData("", false)]
	public void IsValidSource_OnlyAcceptsWhitelistedSource(string? value, bool expected)
	{
		Assert.Equal(expected, ContentIconService.IsValidSource(value));
	}

	[Theory]
	[InlineData("mod", true)]
	[InlineData("modpack", true)]
	[InlineData("shader", true)]
	[InlineData("resourcepack", true)]
	[InlineData("RESOURCEPACK", true)]
	[InlineData("evil", false)]
	[InlineData(null, false)]
	[InlineData("", false)]
	public void IsValidCategory_OnlyAcceptsWhitelistedCategory(string? value, bool expected)
	{
		Assert.Equal(expected, ContentIconService.IsValidCategory(value));
	}

	[Theory]
	[InlineData("AANobbMI", true)]
	[InlineData("Pjtdummy-1_2", true)]
	[InlineData("", false)]
	[InlineData("with space", false)]
	[InlineData("../evil", false)]
	[InlineData("a/b", false)]
	[InlineData("a%2Fb", false)]
	[InlineData("a.b", false)]
	public void IsValidProjectId_EnforcesLengthAndCharset(string? value, bool expected)
	{
		Assert.Equal(expected, ContentIconService.IsValidProjectId(value));
	}

	[Fact]
	public void IsValidProjectId_RejectsOver80Chars()
	{
		Assert.False(ContentIconService.IsValidProjectId(new string('a', 81)));
	}

	[Theory]
	[InlineData("cdn-raw.modrinth.com", true)]
	[InlineData("cdn.modrinth.com", true)]
	[InlineData("CDN.MODRINTH.COM", true)]
	[InlineData("evil-modrinth.com", false)]
	[InlineData("modrinth.com", false)]
	[InlineData("media.forgecdn.net", false)]
	[InlineData("example.com", false)]
	[InlineData(null, false)]
	[InlineData("", false)]
	public void IsAllowedHost_OnlyAcceptsExactWhitelisted(string? host, bool expected)
	{
		Assert.Equal(expected, ContentIconService.IsAllowedHost(host));
	}

	[Theory]
	[InlineData("https://cdn.modrinth.com/data/A/icon.png", true)]
	[InlineData("http://cdn.modrinth.com/data/A/icon.png", false)]
	[InlineData("file:///etc/passwd", false)]
	[InlineData("https://user:pass@cdn.modrinth.com/icon.png", false)]
	public void IsAllowedUri_OnlyHttpsAndNoUserInfo(string url, bool expected)
	{
		Assert.Equal(expected, ContentIconService.IsAllowedUri(new Uri(url)));
	}

	[Theory]
	[InlineData("image/png", true)]
	[InlineData("image/jpeg", true)]
	[InlineData("image/jpg", true)]
	[InlineData("image/webp", true)]
	[InlineData("image/svg+xml", false)]
	[InlineData("image/gif", false)]
	[InlineData("text/html", false)]
	[InlineData("application/json", false)]
	[InlineData(null, false)]
	[InlineData("", false)]
	public void IsAllowedMime_OnlyImageFormats(string? value, bool expected)
	{
		Assert.Equal(expected, ContentIconService.IsAllowedMime(value));
	}

	[Fact]
	public void TryDetectImageFormat_AcceptsPngJpegWebpOnly()
	{
		// PNG 8-byte signature
		Assert.True(ContentIconService.TryDetectImageFormat(new byte[] { 137,80,78,71,13,10,26,10,0,0,0,0 }, out string pe, out string pm));
		Assert.Equal("png", pe); Assert.Equal("image/png", pm);

		// JPEG
		Assert.True(ContentIconService.TryDetectImageFormat(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0,0,0,0,0,0,0,0 }, out string je, out string jm));
		Assert.Equal("jpg", je); Assert.Equal("image/jpeg", jm);

		// WebP RIFF + WEBP at offset 8
		byte[] webp = new byte[] { (byte)'R',(byte)'I',(byte)'F',(byte)'F', 0,0,0,0, (byte)'W',(byte)'E',(byte)'B',(byte)'P' };
		Assert.True(ContentIconService.TryDetectImageFormat(webp, out string we, out string wm));
		Assert.Equal("webp", we); Assert.Equal("image/webp", wm);

		// HTML
		Assert.False(ContentIconService.TryDetectImageFormat(new byte[] { 0x3C, 0x68, 0x74, 0x6D, 0x6C }, out _, out _));
		// SVG
		Assert.False(ContentIconService.TryDetectImageFormat(new byte[] { 0x3C, 0x73, 0x76, 0x67 }, out _, out _));
		// Empty
		Assert.False(ContentIconService.TryDetectImageFormat(Array.Empty<byte>(), out _, out _));
	}

	[Fact]
	public void IsPublicAddress_RejectsLoopbackPrivateAndLinkLocal()
	{
		Assert.False(ContentIconService.IsPublicAddress(IPAddress.Parse("127.0.0.1")));
		Assert.False(ContentIconService.IsPublicAddress(IPAddress.Parse("10.0.0.1")));
		Assert.False(ContentIconService.IsPublicAddress(IPAddress.Parse("172.16.0.1")));
		Assert.False(ContentIconService.IsPublicAddress(IPAddress.Parse("192.168.1.1")));
		Assert.False(ContentIconService.IsPublicAddress(IPAddress.Parse("169.254.1.1")));
		Assert.False(ContentIconService.IsPublicAddress(IPAddress.Parse("::1")));
		Assert.False(ContentIconService.IsPublicAddress(IPAddress.Parse("fc00::1")));
		// IPv4-mapped IPv6：内嵌私网/回环地址必须按公网性判定，防止绕过检查
		Assert.False(ContentIconService.IsPublicAddress(IPAddress.Parse("::ffff:10.0.0.1")));
		Assert.False(ContentIconService.IsPublicAddress(IPAddress.Parse("::ffff:192.168.1.1")));
		Assert.False(ContentIconService.IsPublicAddress(IPAddress.Parse("::ffff:127.0.0.1")));
		Assert.True(ContentIconService.IsPublicAddress(IPAddress.Parse("::ffff:8.8.8.8")));
	}

	[Fact]
	public void ComputeCacheKey_DependsOnUrlAndIdentity()
	{
		string a = ContentIconService.ComputeCacheKey("modrinth", "mod", "AANobbMI", "https://cdn.modrinth.com/data/AANobbMI/icon.png");
		string b = ContentIconService.ComputeCacheKey("modrinth", "mod", "AANobbMI", "https://cdn.modrinth.com/data/AANobbMI/icon.png");
		string c = ContentIconService.ComputeCacheKey("modrinth", "mod", "AANobbMI", "https://cdn.modrinth.com/data/AANobbMI/other.png");
		string d = ContentIconService.ComputeCacheKey("modrinth", "modpack", "AANobbMI", "https://cdn.modrinth.com/data/AANobbMI/icon.png");
		Assert.Equal(a, b);
		Assert.NotEqual(a, c);
		Assert.NotEqual(a, d);
	}

	// -------------------------------------------------------------------------
	//  GetAsync：身份边界 / 未知项目 / 缺失图标
	// -------------------------------------------------------------------------

	[Fact]
	public async Task GetAsync_UnknownIdentityReturns404_NoNetwork()
	{
		ContentIconService svc = NewService();
		// 替换 fetcher 为捕获器：若被调用则抛错
		int fetchCount = 0;
		svc.HttpFetcher = (u, ct) => { fetchCount++; throw new InvalidOperationException("should not fetch"); };
		IconResult r = await svc.GetAsync("modrinth", "mod", "UNKNOWN", CancellationToken.None);
		Assert.Equal(IconStatus.NotFound, r.Status);
		Assert.Equal(0, fetchCount);
	}

	[Fact]
	public async Task GetAsync_InvalidSourceCategoryProjectId_Returns404()
	{
		ContentIconService svc = NewService((_, _) => Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)));
		Assert.Equal(IconStatus.NotFound, (await svc.GetAsync("evil", "mod", "abc", CancellationToken.None)).Status);
		Assert.Equal(IconStatus.NotFound, (await svc.GetAsync("modrinth", "evil", "abc", CancellationToken.None)).Status);
		Assert.Equal(IconStatus.NotFound, (await svc.GetAsync("modrinth", "mod", "", CancellationToken.None)).Status);
	}

	[Fact]
	public async Task GetAsync_NoTrustedUrlRegistered_Returns404_NoNetwork()
	{
		ContentIconService svc = NewService((_, _) => throw new InvalidOperationException("should not fetch"));
		// 没注册任何可信 URL → 必须 NotFound，绝不访问网络
		IconResult r = await svc.GetAsync("modrinth", "mod", "AANobbMI", CancellationToken.None);
		Assert.Equal(IconStatus.NotFound, r.Status);
	}

	[Fact]
	public async Task GetAsync_RejectsUntrustedUrlEvenIfWhitelistedHost()
	{
		// 任何 RegisterTrustedUrl 调用如果 URL 主机不在白名单，都被静默忽略。
		ContentIconService svc = NewService((_, _) => throw new InvalidOperationException("should not fetch"));
		svc.RegisterTrustedUrl("modrinth", "mod", "X", "https://evil.example.com/x.png");
		Assert.Equal(IconStatus.NotFound, (await svc.GetAsync("modrinth", "mod", "X", CancellationToken.None)).Status);
	}

	// -------------------------------------------------------------------------
	//  下载：成功路径（PNG / JPEG / WebP）
	// -------------------------------------------------------------------------

	[Fact]
	public async Task GetAsync_DownloadsValidPngAndCaches()
	{
		byte[] png = LoadTestPng();
		ContentIconService svc = NewService(MakeOkFetcher("image/png", png));
		svc.RegisterTrustedUrl("modrinth", "mod", "PNG1", "https://cdn.modrinth.com/data/PNG1/icon.png");
		IconResult r = await svc.GetAsync("modrinth", "mod", "PNG1", CancellationToken.None);
		Assert.Equal(IconStatus.Ok, r.Status);
		Assert.NotNull(r.Bytes);
		Assert.Equal("image/png", r.Mime);
		Assert.NotNull(r.CachePath);
		Assert.True(File.Exists(r.CachePath!));
	}

	[Fact]
	public async Task GetAsync_DownloadsValidJpegAndCaches()
	{
		byte[] jpeg = LoadTestJpeg();
		ContentIconService svc = NewService(MakeOkFetcher("image/jpeg", jpeg));
		svc.RegisterTrustedUrl("modrinth", "mod", "JPG1", "https://cdn.modrinth.com/data/JPG1/icon.jpg");
		IconResult r = await svc.GetAsync("modrinth", "mod", "JPG1", CancellationToken.None);
		Assert.Equal(IconStatus.Ok, r.Status);
		Assert.Equal("image/jpeg", r.Mime);
	}

	[Fact]
	public async Task GetAsync_DownloadsValidWebpAndCaches()
	{
		byte[] webp = LoadTestWebp();
		ContentIconService svc = NewService(MakeOkFetcher("image/webp", webp));
		svc.RegisterTrustedUrl("modrinth", "mod", "WP1", "https://cdn.modrinth.com/data/WP1/icon.webp");
		IconResult r = await svc.GetAsync("modrinth", "mod", "WP1", CancellationToken.None);
		Assert.Equal(IconStatus.Ok, r.Status);
		Assert.Equal("image/webp", r.Mime);
	}

	// -------------------------------------------------------------------------
	//  类型 / 大小 / 魔数 / 重定向 / SSRF
	// -------------------------------------------------------------------------

	[Fact]
	public async Task GetAsync_RejectsSvgEvenIfContentTypeSpoofed()
	{
		byte[] svg = new byte[] { 0x3C, 0x73, 0x76, 0x67, 0x20, 0x2F, 0x3E };
		ContentIconService svc = NewService(MakeOkFetcher("image/svg+xml", svg));
		svc.RegisterTrustedUrl("modrinth", "mod", "SVG", "https://cdn.modrinth.com/data/SVG/icon.svg");
		IconResult r = await svc.GetAsync("modrinth", "mod", "SVG", CancellationToken.None);
		Assert.Equal(IconStatus.BadContentType, r.Status);
	}

	[Fact]
	public async Task GetAsync_RejectsHtmlDisguisedAsImage()
	{
		byte[] html = new byte[] { 0x3C, 0x68, 0x74, 0x6D, 0x6C, 0x3E, 0x3C, 0x2F, 0x68, 0x74, 0x6D, 0x6C, 0x3E };
		ContentIconService svc = NewService(MakeOkFetcher("image/png", html));
		svc.RegisterTrustedUrl("modrinth", "mod", "HTML", "https://cdn.modrinth.com/data/HTML/icon.png");
		IconResult r = await svc.GetAsync("modrinth", "mod", "HTML", CancellationToken.None);
		Assert.Equal(IconStatus.BadImage, r.Status);
	}

	[Fact]
	public async Task GetAsync_RejectsContentLengthOversize()
	{
		byte[] png = LoadTestPng();
		ContentIconService svc = NewService((_, __) =>
		{
			HttpResponseMessage m = new HttpResponseMessage(System.Net.HttpStatusCode.OK);
			m.Content = new StreamContent(new MemoryStream(png));
			m.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
			m.Content.Headers.ContentLength = ContentIconService.MaxBytes + 1L;
			return Task.FromResult(m);
		});
		svc.RegisterTrustedUrl("modrinth", "mod", "OV1", "https://cdn.modrinth.com/data/OV1/icon.png");
		IconResult r = await svc.GetAsync("modrinth", "mod", "OV1", CancellationToken.None);
		Assert.Equal(IconStatus.TooLarge, r.Status);
	}

	[Fact]
	public async Task GetAsync_RejectsStreamingOversize()
	{
		// Content-Length 缺失；流式输出超过 2 MiB
		byte[] overlong = new byte[ContentIconService.MaxBytes + 4096];
		overlong[0] = 137; overlong[1] = 80; overlong[2] = 78; overlong[3] = 71; overlong[4] = 13; overlong[5] = 10; overlong[6] = 26; overlong[7] = 10;
		ContentIconService svc = NewService((_, __) =>
		{
			HttpResponseMessage m = new HttpResponseMessage(System.Net.HttpStatusCode.OK);
			m.Content = new StreamContent(new MemoryStream(overlong));
			m.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
			// 故意不设 ContentLength
			return Task.FromResult(m);
		});
		svc.RegisterTrustedUrl("modrinth", "mod", "OV2", "https://cdn.modrinth.com/data/OV2/icon.png");
		IconResult r = await svc.GetAsync("modrinth", "mod", "OV2", CancellationToken.None);
		Assert.Equal(IconStatus.TooLarge, r.Status);
	}

	[Fact]
	public async Task GetAsync_RejectsRedirectToNonWhitelistedHost()
	{
		byte[] png = LoadTestPng();
		// 第一次返回 302 跳到 evil.example.com
		Dictionary<string, Func<HttpResponseMessage>> byUrl = new Dictionary<string, Func<HttpResponseMessage>>(StringComparer.OrdinalIgnoreCase)
		{
			["https://cdn.modrinth.com/data/REDIR/icon.png"] = () =>
			{
				HttpResponseMessage m = new HttpResponseMessage(System.Net.HttpStatusCode.Found);
				m.Headers.Location = new Uri("https://evil.example.com/icon.png");
				return m;
			},
			["https://evil.example.com/icon.png"] = () =>
			{
				HttpResponseMessage m = new HttpResponseMessage(System.Net.HttpStatusCode.OK);
				m.Content = new StreamContent(new MemoryStream(png));
				m.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
				return m;
			},
		};
		ContentIconService svc = NewService((u, ct) => Task.FromResult(byUrl[u.ToString()]()));
		svc.RegisterTrustedUrl("modrinth", "mod", "REDIR", "https://cdn.modrinth.com/data/REDIR/icon.png");
		IconResult r = await svc.GetAsync("modrinth", "mod", "REDIR", CancellationToken.None);
		Assert.Equal(IconStatus.UpstreamError, r.Status);
	}

	[Fact]
	public async Task GetAsync_RejectsRedirectLoop()
	{
		Dictionary<string, Func<HttpResponseMessage>> byUrl = new Dictionary<string, Func<HttpResponseMessage>>(StringComparer.OrdinalIgnoreCase)
		{
			["https://cdn.modrinth.com/data/LOOP/icon.png"] = () =>
			{
				HttpResponseMessage m = new HttpResponseMessage(System.Net.HttpStatusCode.Found);
				m.Headers.Location = new Uri("https://cdn-raw.modrinth.com/data/LOOP/icon.png");
				return m;
			},
			["https://cdn-raw.modrinth.com/data/LOOP/icon.png"] = () =>
			{
				HttpResponseMessage m = new HttpResponseMessage(System.Net.HttpStatusCode.Found);
				m.Headers.Location = new Uri("https://cdn.modrinth.com/data/LOOP/icon.png");
				return m;
			},
		};
		ContentIconService svc = NewService((u, ct) => Task.FromResult(byUrl[u.ToString()]()));
		svc.RegisterTrustedUrl("modrinth", "mod", "LOOP", "https://cdn.modrinth.com/data/LOOP/icon.png");
		IconResult r = await svc.GetAsync("modrinth", "mod", "LOOP", CancellationToken.None);
		// 跳到第 4 次（超过 MaxRedirects=3）应失败
		Assert.Equal(IconStatus.UpstreamError, r.Status);
	}

	[Fact]
	public async Task GetAsync_RejectsHttpNonHttpsEvenIfWhitelistedHost()
	{
		// 即便 host 在白名单，scheme 不是 https 也必须拒绝
		ContentIconService svc = NewService((_, __) => throw new InvalidOperationException("should not fetch"));
		svc.RegisterTrustedUrl("modrinth", "mod", "HTTP", "http://cdn.modrinth.com/data/HTTP/icon.png");
		IconResult r = await svc.GetAsync("modrinth", "mod", "HTTP", CancellationToken.None);
		Assert.Equal(IconStatus.NotFound, r.Status);
	}

	[Fact]
	public async Task GetAsync_Upstream4xxReturnsUpstreamError()
	{
		ContentIconService svc = NewService((_, __) =>
		{
			HttpResponseMessage m = new HttpResponseMessage(System.Net.HttpStatusCode.NotFound);
			return Task.FromResult(m);
		});
		svc.RegisterTrustedUrl("modrinth", "mod", "NF", "https://cdn.modrinth.com/data/NF/icon.png");
		IconResult r = await svc.GetAsync("modrinth", "mod", "NF", CancellationToken.None);
		Assert.Equal(IconStatus.UpstreamError, r.Status);
	}

	// -------------------------------------------------------------------------
	//  缓存：命中 / 原子 / 损坏 / 路径
	// -------------------------------------------------------------------------

	[Fact]
	public async Task GetAsync_CacheHitDoesNotFetchAgain()
	{
		byte[] png = LoadTestPng();
		int fetchCount = 0;
		ContentIconService svc = NewService((_, __) =>
		{
			Interlocked.Increment(ref fetchCount);
			HttpResponseMessage m = new HttpResponseMessage(System.Net.HttpStatusCode.OK);
			m.Content = new StreamContent(new MemoryStream(png));
			m.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
			m.Headers.ETag = new System.Net.Http.Headers.EntityTagHeaderValue("\"v1\"");
			return Task.FromResult(m);
		});
		svc.RegisterTrustedUrl("modrinth", "mod", "HIT", "https://cdn.modrinth.com/data/HIT/icon.png");
		IconResult r1 = await svc.GetAsync("modrinth", "mod", "HIT", CancellationToken.None);
		Assert.Equal(IconStatus.Ok, r1.Status);
		// 让第一次的 in-flight ContinueWith 有机会运行（清空 in-flight 字典）。
		// 真实场景下用户在 UI 上点击一次刷新后会有时间间隔。
		await Task.Delay(50);
		IconResult r2 = await svc.GetAsync("modrinth", "mod", "HIT", CancellationToken.None);
		Assert.Equal(IconStatus.FromCache, r2.Status);
		Assert.Equal(1, fetchCount);
	}

	[Fact]
	public async Task GetAsync_StaleCacheWith304ReusesAndDoesNotRefetch()
	{
		// 第一次写入正常 PNG；构造一个 fake clock 让缓存变成 stale
		byte[] png = LoadTestPng();
		long fakeNow = DateTimeOffset.UtcNow.UtcTicks;
		Func<long> clock = () => fakeNow;
		int fetchCount = 0;
		ContentIconService svc = NewService((_, __) =>
		{
			Interlocked.Increment(ref fetchCount);
			HttpResponseMessage m = new HttpResponseMessage(System.Net.HttpStatusCode.OK);
			m.Content = new StreamContent(new MemoryStream(png));
			m.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
			m.Headers.ETag = new System.Net.Http.Headers.EntityTagHeaderValue("\"v1\"");
			return Task.FromResult(m);
		}, clock);
		svc.RegisterTrustedUrl("modrinth", "mod", "S304", "https://cdn.modrinth.com/data/S304/icon.png");
		IconResult r1 = await svc.GetAsync("modrinth", "mod", "S304", CancellationToken.None);
		Assert.Equal(IconStatus.Ok, r1.Status);

		// 推进时钟 8 天，缓存变 stale
		fakeNow += TimeSpan.FromDays(8).Ticks;
		// 让第一次的 in-flight ContinueWith 有机会运行
		await Task.Delay(50);
		// 重新注册可信 URL 刷新 TTL（否则可信映射过期 → NotFound；本用例只验证缓存 stale 语义）
		svc.RegisterTrustedUrl("modrinth", "mod", "S304", "https://cdn.modrinth.com/data/S304/icon.png");
		// 替换 fetcher：304
		svc.HttpFetcher = (_, __) =>
		{
			Interlocked.Increment(ref fetchCount);
			HttpResponseMessage m = new HttpResponseMessage(System.Net.HttpStatusCode.NotModified);
			m.Headers.ETag = new System.Net.Http.Headers.EntityTagHeaderValue("\"v1\"");
			return Task.FromResult(m);
		};
		IconResult r2 = await svc.GetAsync("modrinth", "mod", "S304", CancellationToken.None);
		Assert.Equal(IconStatus.FromCache, r2.Status);
		Assert.Equal(2, fetchCount); // 1 写入 + 1 条件请求
	}

	[Fact]
	public async Task GetAsync_UpstreamErrorWithStaleCacheReturnsStale()
	{
		byte[] png = LoadTestPng();
		long fakeNow = DateTimeOffset.UtcNow.UtcTicks;
		Func<long> clock = () => fakeNow;
		ContentIconService svc = NewService((_, __) =>
		{
			HttpResponseMessage m = new HttpResponseMessage(System.Net.HttpStatusCode.OK);
			m.Content = new StreamContent(new MemoryStream(png));
			m.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
			return Task.FromResult(m);
		}, clock);
		svc.RegisterTrustedUrl("modrinth", "mod", "STL", "https://cdn.modrinth.com/data/STL/icon.png");
		await svc.GetAsync("modrinth", "mod", "STL", CancellationToken.None);

		// 推进 8 天 → stale
		fakeNow += TimeSpan.FromDays(8).Ticks;
		// 让第一次的 in-flight ContinueWith 有机会运行
		await Task.Delay(50);
		// 重新注册可信 URL 刷新 TTL（否则可信映射过期 → NotFound；本用例只验证 stale 回退语义）
		svc.RegisterTrustedUrl("modrinth", "mod", "STL", "https://cdn.modrinth.com/data/STL/icon.png");
		// 上游 502
		svc.HttpFetcher = (_, __) =>
		{
			HttpResponseMessage m = new HttpResponseMessage(System.Net.HttpStatusCode.BadGateway);
			return Task.FromResult(m);
		};
		IconResult r = await svc.GetAsync("modrinth", "mod", "STL", CancellationToken.None);
		Assert.Equal(IconStatus.Stale, r.Status);
	}

	[Fact]
	public async Task GetAsync_CorruptCacheIsQuarantinedAndRefetched()
	{
		// 先写一次正常
		byte[] png = LoadTestPng();
		int fetchCount = 0;
		ContentIconService svc = NewService((_, __) =>
		{
			Interlocked.Increment(ref fetchCount);
			HttpResponseMessage m = new HttpResponseMessage(System.Net.HttpStatusCode.OK);
			m.Content = new StreamContent(new MemoryStream(png));
			m.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
			return Task.FromResult(m);
		});
		svc.RegisterTrustedUrl("modrinth", "mod", "COR", "https://cdn.modrinth.com/data/COR/icon.png");
		IconResult r1 = await svc.GetAsync("modrinth", "mod", "COR", CancellationToken.None);
		Assert.Equal(IconStatus.Ok, r1.Status);

		// 人为损坏缓存
		string cacheDir = Path.Combine(_cacheRoot, "modrinth", "mod");
		foreach (string f in Directory.GetFiles(cacheDir, "*"))
		{
			if (f.EndsWith(".meta.json", StringComparison.OrdinalIgnoreCase)) continue;
			File.WriteAllBytes(f, new byte[] { 0xFF, 0xFF, 0xFF, 0xFF });
		}

		IconResult r2 = await svc.GetAsync("modrinth", "mod", "COR", CancellationToken.None);
		Assert.Equal(IconStatus.Ok, r2.Status);
		Assert.Equal(2, fetchCount);
		// 至少存在一个 .corrupt-* 隔离文件
		Assert.NotEmpty(Directory.GetFiles(cacheDir, "*.corrupt-*"));
	}

	[Fact]
	public async Task GetAsync_CacheRootCannotEscapeViaRegisteredUrl()
	{
		// 注册 URL 含 .. 段。.NET Uri 会自动归一化（/../../etc/passwd → /etc/passwd），
		// 归一化后 host 仍是 cdn.modrinth.com（白名单）。
		// 关键安全不变量：缓存键是 SHA-256 hex 字符串，路径段在归一化后只参与哈希，
		// 不会以任何形式写入磁盘文件名或缓存目录。
		ContentIconService svc = NewService((_, __) => throw new InvalidOperationException("should not fetch"));
		svc.RegisterTrustedUrl("modrinth", "mod", "ESC", "https://cdn.modrinth.com/../../etc/passwd");
		IconResult r = await svc.GetAsync("modrinth", "mod", "ESC", CancellationToken.None);
		// fetcher 抛错 → 上游错误。这条用例主要保护"路径不会逃逸出缓存根目录"。
		Assert.Equal(IconStatus.UpstreamError, r.Status);
		// 进一步：验证缓存目录里没有任何以 .. 形式存在的文件（防止路径穿越）
		string cacheDir = Path.Combine(_cacheRoot, "modrinth", "mod");
		if (Directory.Exists(cacheDir))
		{
			foreach (string f in Directory.GetFiles(cacheDir))
			{
				Assert.False(f.Contains("..", StringComparison.Ordinal), $"缓存文件 {f} 含路径穿越段");
			}
		}
	}

	// -------------------------------------------------------------------------
	//  并发：同键去重 + 全局上限
	// -------------------------------------------------------------------------

	[Fact]
	public async Task GetAsync_ConcurrentSameKeyResultsInSingleFetch()
	{
		byte[] png = LoadTestPng();
		int fetchCount = 0;
		ContentIconService svc = NewService(async (_, ct) =>
		{
			Interlocked.Increment(ref fetchCount);
			// 用 Task.Delay 真正让出执行权，让 8 个并发调用能同时进入 in-flight 逻辑
			await Task.Delay(100, ct);
			HttpResponseMessage m = new HttpResponseMessage(System.Net.HttpStatusCode.OK);
			m.Content = new StreamContent(new MemoryStream(png));
			m.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
			return m;
		});
		svc.RegisterTrustedUrl("modrinth", "mod", "CONC", "https://cdn.modrinth.com/data/CONC/icon.png");
		// 8 个并发调用必须用 Task.Run 推到线程池上，否则单线程会串行执行，in-flight 永远为空
		Task<IconResult>[] tasks = new Task<IconResult>[8];
		Parallel.For(0, 8, i => tasks[i] = Task.Run(async () => await svc.GetAsync("modrinth", "mod", "CONC", CancellationToken.None)));
		IconResult[] results = await Task.WhenAll(tasks);
		Assert.All(results, r => Assert.Equal(IconStatus.Ok, r.Status));
		Assert.Equal(1, fetchCount);
	}

	[Fact]
	public async Task GetAsync_FailedFetchAllowsRetry()
	{
		int fetchCount = 0;
		ContentIconService svc = NewService((_, __) =>
		{
			int n = Interlocked.Increment(ref fetchCount);
			if (n == 1)
			{
				HttpResponseMessage m = new HttpResponseMessage(System.Net.HttpStatusCode.BadGateway);
				return Task.FromResult(m);
			}
			HttpResponseMessage ok = new HttpResponseMessage(System.Net.HttpStatusCode.OK);
			ok.Content = new StreamContent(new MemoryStream(LoadTestPng()));
			ok.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
			return Task.FromResult(ok);
		});
		svc.RegisterTrustedUrl("modrinth", "mod", "RETRY", "https://cdn.modrinth.com/data/RETRY/icon.png");
		IconResult r1 = await svc.GetAsync("modrinth", "mod", "RETRY", CancellationToken.None);
		Assert.Equal(IconStatus.UpstreamError, r1.Status);
		IconResult r2 = await svc.GetAsync("modrinth", "mod", "RETRY", CancellationToken.None);
		Assert.Equal(IconStatus.Ok, r2.Status);
		Assert.Equal(2, fetchCount);
	}

	[Fact]
	public async Task GetAsync_GlobalConcurrencyLimitEnforced()
	{
		// 8 个不同 key 并发请求，每个请求耗时 100ms；全局信号量 4，
		// 总耗时应 ≥ 2 * 100ms = 200ms（两组串行）
		byte[] png = LoadTestPng();
		ContentIconService svc = NewService((_, __) =>
		{
			Thread.Sleep(80);
			HttpResponseMessage m = new HttpResponseMessage(System.Net.HttpStatusCode.OK);
			m.Content = new StreamContent(new MemoryStream(png));
			m.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
			return Task.FromResult(m);
		});
		// 8 个不同项目
		string[] ids = new[] { "P1","P2","P3","P4","P5","P6","P7","P8" };
		foreach (string id in ids)
		{
			svc.RegisterTrustedUrl("modrinth", "mod", id, "https://cdn.modrinth.com/data/" + id + "/icon.png");
		}
		DateTimeOffset start = DateTimeOffset.UtcNow;
		Task<IconResult>[] tasks = ids.Select(id => svc.GetAsync("modrinth", "mod", id, CancellationToken.None)).ToArray();
		await Task.WhenAll(tasks);
		long elapsedMs = (long)(DateTimeOffset.UtcNow - start).TotalMilliseconds;
		// 全局 4 + 每请求 ~80ms 至少要 2 轮 → ≥ 160ms；给一定抖动
		Assert.True(elapsedMs >= 130, $"期望 ≥ 130ms 实际 {elapsedMs}ms");
	}

	// -------------------------------------------------------------------------
	//  安全返修 v2 —— 先补修复前失败测试（S2 / P2R，针对当前生产实现必须真实失败）
	// -------------------------------------------------------------------------

	// -------------------------------------------------------------------------
	//  放行收口 —— "验证内容优先"MIME 策略（先红后绿）
	//  原"MIME 必须完全一致"用例（M1/M2/M3）按收口工作流五拆分：
	//   a) 受信任图片 CDN 的图片 MIME 错标 → 接受，最终 MIME 取验证格式；
	//   b) 非图片 MIME（HTML/JSON/JS/XML/空/其他）→ 仍拒绝（GetAsync_RejectsNonImageMime_*）；
	//   c) 只有魔数/结构不完整/GetPixels 失败 → 仍拒绝（截断与伪图片用例）。
	// -------------------------------------------------------------------------

	[Fact]
	public async Task GetAsync_MimePngDeclaredWithRealJpegBody_AcceptsVerifiedJpeg()
	{
		// 收口五-2：声明 image/png + 完整可解码 JPEG 正文 → 接受，最终 MIME=检测格式 image/jpeg。
		byte[] jpeg = LoadTestJpeg64();
		ContentIconService svc = NewService(MakeOkFetcher("image/png", jpeg));
		svc.RegisterTrustedUrl("modrinth", "mod", "M1", "https://cdn.modrinth.com/data/M1/icon.png");
		IconResult r = await svc.GetAsync("modrinth", "mod", "M1", CancellationToken.None);
		Assert.Equal(IconStatus.Ok, r.Status);
		Assert.Equal("image/jpeg", r.Mime); // 最终 MIME 取验证后的检测格式，不是上游错误声明
		Assert.NotNull(r.CachePath);
	}

	[Fact]
	public async Task GetAsync_MimeJpegDeclaredWithRealPngBody_AcceptsVerifiedPng()
	{
		// 收口五-2（原 M2 拆分）：声明 image/jpeg + 完整可解码 PNG 正文 → 接受，MIME=image/png。
		byte[] png = LoadTestPng64();
		ContentIconService svc = NewService(MakeOkFetcher("image/jpeg", png));
		svc.RegisterTrustedUrl("modrinth", "mod", "M2", "https://cdn.modrinth.com/data/M2/icon.jpg");
		IconResult r = await svc.GetAsync("modrinth", "mod", "M2", CancellationToken.None);
		Assert.Equal(IconStatus.Ok, r.Status);
		Assert.Equal("image/png", r.Mime);
	}

	[Fact]
	public async Task GetAsync_MimeWebpDeclaredWithRealPngBody_AcceptsVerifiedPng()
	{
		// 收口五-2（原 M3 拆分）：声明 image/webp + 完整可解码 PNG 正文 → 接受，MIME=image/png。
		byte[] png = LoadTestPng64();
		ContentIconService svc = NewService(MakeOkFetcher("image/webp", png));
		svc.RegisterTrustedUrl("modrinth", "mod", "M3", "https://cdn.modrinth.com/data/M3/icon.webp");
		IconResult r = await svc.GetAsync("modrinth", "mod", "M3", CancellationToken.None);
		Assert.Equal(IconStatus.Ok, r.Status);
		Assert.Equal("image/png", r.Mime);
	}

	[Fact]
	public async Task GetAsync_MimePngDeclaredWithRealWebpBody_AcceptsVerifiedWebp()
	{
		// 收口五-1/4：真实场景（Modrinth CDN 把 .webp 标成 image/png）：
		// 声明 image/png + 2,570B 完整可解码 WebP 正文 → 接受，最终 MIME=image/webp。
		byte[] webp = LoadTestWebp64();
		ContentIconService svc = NewService(MakeOkFetcher("image/png", webp));
		svc.RegisterTrustedUrl("modrinth", "mod", "MIMEFIX", "https://cdn.modrinth.com/data/MIMEFIX/icon.png");
		IconResult r = await svc.GetAsync("modrinth", "mod", "MIMEFIX", CancellationToken.None);
		Assert.Equal(IconStatus.Ok, r.Status);
		Assert.Equal("image/webp", r.Mime); // 检测格式为准
		Assert.NotNull(r.CachePath);
	}

	[Fact]
	public async Task GetAsync_MimeMismatch_CacheHit_UsesVerifiedMime()
	{
		// 收口五-5：MIME 错标图片缓存命中成功且 MIME 仍为验证格式。
		byte[] webp = LoadTestWebp64();
		ContentIconService svc = NewService(MakeOkFetcher("image/png", webp));
		svc.RegisterTrustedUrl("modrinth", "mod", "MMHIT", "https://cdn.modrinth.com/data/MMHIT/icon.png");
		IconResult r1 = await svc.GetAsync("modrinth", "mod", "MMHIT", CancellationToken.None);
		Assert.Equal(IconStatus.Ok, r1.Status);
		await Task.Delay(50);
		IconResult r2 = await svc.GetAsync("modrinth", "mod", "MMHIT", CancellationToken.None);
		Assert.Equal(IconStatus.FromCache, r2.Status);
		Assert.Equal("image/webp", r2.Mime);
		Assert.Equal(webp, r2.Bytes);
	}

	[Fact]
	public async Task GetAsync_MimeMismatch_CacheSurvivesServiceRestart()
	{
		// 收口五-6：应用重启（新服务实例、同一缓存根）后错标图片缓存仍能读取。
		byte[] webp = LoadTestWebp64();
		string cacheRoot = Path.Combine(_cacheRoot, "restart-" + Guid.NewGuid().ToString("N"));
		ContentIconService svc1 = new ContentIconService(cacheRoot, MakeOkFetcher("image/png", webp), null, FakeDns);
		svc1.RegisterTrustedUrl("modrinth", "mod", "MMRST", "https://cdn.modrinth.com/data/MMRST/icon.png");
		IconResult r1 = await svc1.GetAsync("modrinth", "mod", "MMRST", CancellationToken.None);
		Assert.Equal(IconStatus.Ok, r1.Status);
		svc1.Dispose();

		ContentIconService svc2 = new ContentIconService(cacheRoot, (_, _) => throw new InvalidOperationException("must not fetch"), null, FakeDns);
		try
		{
			svc2.RegisterTrustedUrl("modrinth", "mod", "MMRST", "https://cdn.modrinth.com/data/MMRST/icon.png");
			IconResult r2 = await svc2.GetAsync("modrinth", "mod", "MMRST", CancellationToken.None);
			Assert.Equal(IconStatus.FromCache, r2.Status);
			Assert.Equal("image/webp", r2.Mime); // 重启后缓存命中仍返回验证后的 MIME
		}
		finally { svc2.Dispose(); }
	}

	[Fact]
	public async Task GetAsync_MimeMismatch_ManifestStoresVerifiedMime()
	{
		// 收口五-7：manifest 必须保存验证后的 MIME（image/webp），不是上游错误声明。
		byte[] webp = LoadTestWebp64();
		ContentIconService svc = NewService(MakeOkFetcher("image/png", webp));
		svc.RegisterTrustedUrl("modrinth", "mod", "MMMF", "https://cdn.modrinth.com/data/MMMF/icon.png");
		IconResult r = await svc.GetAsync("modrinth", "mod", "MMMF", CancellationToken.None);
		Assert.Equal(IconStatus.Ok, r.Status);
		string dir = Path.Combine(_cacheRoot, "modrinth", "mod");
		string urlHash = ContentIconService.ComputeCacheKey("modrinth", "mod", "MMMF", "https://cdn.modrinth.com/data/MMMF/icon.png");
		string json = File.ReadAllText(Path.Combine(dir, urlHash + ".json"));
		Assert.Contains("\"mime\":\"image/webp\"", json);
	}

	[Fact]
	public async Task GetAsync_MimeMismatch_Stale304_RetainsVerifiedMime()
	{
		// 收口五-8：条件请求 304 后继续使用验证后的 MIME。
		byte[] webp = LoadTestWebp64();
		long fakeNow = DateTimeOffset.UtcNow.UtcTicks;
		ContentIconService svc = NewService((_, __) =>
		{
			HttpResponseMessage m = MakeOk(webp, "image/png"); // 错标：png 声明 + webp 正文
			m.Headers.ETag = new System.Net.Http.Headers.EntityTagHeaderValue("\"v1\"");
			return Task.FromResult(m);
		}, () => fakeNow);
		svc.RegisterTrustedUrl("modrinth", "mod", "MM304", "https://cdn.modrinth.com/data/MM304/icon.png");
		IconResult r1 = await svc.GetAsync("modrinth", "mod", "MM304", CancellationToken.None);
		Assert.Equal(IconStatus.Ok, r1.Status);
		Assert.Equal("image/webp", r1.Mime);

		fakeNow += TimeSpan.FromDays(8).Ticks;
		await Task.Delay(50);
		svc.RegisterTrustedUrl("modrinth", "mod", "MM304", "https://cdn.modrinth.com/data/MM304/icon.png");
		svc.HttpFetcher = (_, __) =>
		{
			HttpResponseMessage m = new HttpResponseMessage(HttpStatusCode.NotModified);
			m.Headers.ETag = new System.Net.Http.Headers.EntityTagHeaderValue("\"v1\"");
			return Task.FromResult(m);
		};
		IconResult r2 = await svc.GetAsync("modrinth", "mod", "MM304", CancellationToken.None);
		Assert.Equal(IconStatus.FromCache, r2.Status);
		Assert.Equal("image/webp", r2.Mime); // 304 复用后仍为验证后的 MIME
	}

	[Theory]
	[InlineData("application/json")]
	[InlineData("text/javascript")]
	[InlineData("application/xml")]
	[InlineData("text/plain")]
	[InlineData("")]
	public async Task GetAsync_RejectsNonImageMime_EvenIfBodyIsImage(string declared)
	{
		// 收口五-9 拆分 b：非图片 MIME（JSON/JS/XML/文本/空）即使正文是合法图片也必须拒绝。
		byte[] png = LoadTestPng64();
		ContentIconService svc = NewService((_, _) =>
		{
			// 注意：不用 MakeOk（其默认声明 image/png）；空 declared 时不设置任何 Content-Type。
			HttpResponseMessage m = new HttpResponseMessage(HttpStatusCode.OK);
			m.Content = new StreamContent(new MemoryStream(png));
			if (declared.Length > 0) m.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(declared);
			return Task.FromResult(m);
		});
		svc.RegisterTrustedUrl("modrinth", "mod", "NIM", "https://cdn.modrinth.com/data/NIM/icon.png");
		IconResult r = await svc.GetAsync("modrinth", "mod", "NIM", CancellationToken.None);
		Assert.Equal(IconStatus.BadContentType, r.Status);
		AssertEmptyCacheDir("modrinth", "mod");
	}

	[Fact]
	public void BuildMimeMismatchWarning_LogsNoUrlOrTokenOrCredentials()
	{
		// 收口五-15：结构化警告不得包含完整 URL、token 或凭据。
		string msg = ContentIconService.BuildMimeMismatchWarning("cdn.modrinth.com", "image/png", "image/webp", 2570);
		Assert.DoesNotContain("://", msg);
		Assert.DoesNotContain("token", msg, StringComparison.OrdinalIgnoreCase);
		Assert.DoesNotContain("key", msg, StringComparison.OrdinalIgnoreCase);
		Assert.DoesNotContain("signature", msg, StringComparison.OrdinalIgnoreCase);
		Assert.Contains("cdn.modrinth.com", msg);
		Assert.Contains("image/webp", msg);
	}

	[Fact]
	public async Task GetAsync_TruncatedPngSignatureOnly_IsRejectedAndNotCached()
	{
		// S2-5 / P2R-10：只有 PNG 签名（无 IHDR / chunk / IEND）→ BadImage，不缓存。
		byte[] onlySig = new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 };
		ContentIconService svc = NewService(MakeOkFetcher("image/png", onlySig));
		svc.RegisterTrustedUrl("modrinth", "mod", "T10", "https://cdn.modrinth.com/data/T10/icon.png");
		IconResult r = await svc.GetAsync("modrinth", "mod", "T10", CancellationToken.None);
		Assert.Equal(IconStatus.BadImage, r.Status);
		AssertEmptyCacheDir("modrinth", "mod");
	}

	[Fact]
	public async Task GetAsync_TruncatedJpegNoEoi_IsRejectedAndNotCached()
	{
		// S2-5 / P2R-11：只有 SOI + 短头，无 EOI → BadImage，不缓存。
		byte[] truncatedJpeg = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46, 0x00 };
		ContentIconService svc = NewService(MakeOkFetcher("image/jpeg", truncatedJpeg));
		svc.RegisterTrustedUrl("modrinth", "mod", "T11", "https://cdn.modrinth.com/data/T11/icon.jpg");
		IconResult r = await svc.GetAsync("modrinth", "mod", "T11", CancellationToken.None);
		Assert.Equal(IconStatus.BadImage, r.Status);
		AssertEmptyCacheDir("modrinth", "mod");
	}

	[Fact]
	public async Task GetAsync_WebpRiffLengthMismatch_IsRejectedAndNotCached()
	{
		// S2-5 / P2R-12：RIFF 声明的长度与实际文件长度不一致 → BadImage，不缓存。
		byte[] webp = new byte[] {
			(byte)'R',(byte)'I',(byte)'F',(byte)'F', 0,0,0,0, (byte)'W',(byte)'E',(byte)'B',(byte)'P',
			(byte)'V',(byte)'P',(byte)'8',(byte)' ', 0x0C,0,0,0, 0x9D,0x01,0x2A, 0x10,0x00, 0x10,0x00, 1,2,3,4,5
		};
		ContentIconService svc = NewService(MakeOkFetcher("image/webp", webp));
		svc.RegisterTrustedUrl("modrinth", "mod", "T12", "https://cdn.modrinth.com/data/T12/icon.webp");
		IconResult r = await svc.GetAsync("modrinth", "mod", "T12", CancellationToken.None);
		Assert.Equal(IconStatus.BadImage, r.Status);
		AssertEmptyCacheDir("modrinth", "mod");
	}

	// -------------------------------------------------------------------------
	//  新增：必须在旧实现上真实失败的伪图片测试（返修前红、返修后绿）
	// -------------------------------------------------------------------------

	[Fact]
	public async Task GetAsync_PngEmptyIdat_IsRejectedAndNotCached()
	{
		// PNG: 签名 + IHDR + 空 IDAT (CRC 忽略) + IEND —— 当前有限结构校验会通过，但实际解码器会失败
		byte[] png = new byte[] {
			137, 80, 78, 71, 13, 10, 26, 10,  // PNG signature
			0, 0, 0, 13,  // IHDR length
			0x49, 0x48, 0x44, 0x52,  // "IHDR"
			0, 0, 0, 1,  // width = 1
			0, 0, 0, 1,  // height = 1
			8, 2, 0, 0, 0,  // bit depth, color type, compression, filter, interlace
			0, 0, 0, 0,  // CRC (placeholder)
			0, 0, 0, 0,  // IDAT length = 0 (empty!)
			0x49, 0x44, 0x41, 0x54,  // "IDAT"
			// no data
			0, 0, 0, 0,  // CRC (placeholder)
			0, 0, 0, 0,  // IEND length = 0
			0x49, 0x45, 0x4E, 0x44,  // "IEND"
			0xAE, 0x42, 0x60, 0x82  // IEND CRC (correct)
		};
		ContentIconService svc = NewService(MakeOkFetcher("image/png", png));
		svc.RegisterTrustedUrl("modrinth", "mod", "PNGEMPTY", "https://cdn.modrinth.com/data/PNGEMPTY/icon.png");
		IconResult r = await svc.GetAsync("modrinth", "mod", "PNGEMPTY", CancellationToken.None);
		Assert.Equal(IconStatus.BadImage, r.Status);
		AssertEmptyCacheDir("modrinth", "mod");

		// Second request must not hit cache with bad image
		IconResult r2 = await svc.GetAsync("modrinth", "mod", "PNGEMPTY", CancellationToken.None);
		Assert.Equal(IconStatus.BadImage, r2.Status);
	}

	[Fact]
	public async Task GetAsync_PngInvalidCrc_IsRejectedAndNotCached()
	{
		// PNG with valid structure but invalid CRC in IDAT - limited parser ignores CRC, real decoder fails
		byte[] png = new byte[] {
			137, 80, 78, 71, 13, 10, 26, 10,
			0, 0, 0, 13,
			0x49, 0x48, 0x44, 0x52,
			0, 0, 0, 1,
			0, 0, 0, 1,
			8, 2, 0, 0, 0,
			0, 0, 0, 0,
			0, 0, 0, 4,  // IDAT length = 4
			0x49, 0x44, 0x41, 0x54,
			0x08, 0xD7, 0x63, 0xF8,  // some compressed data
			0xFF, 0xFF, 0xFF, 0xFF,  // WRONG CRC
			0, 0, 0, 0,
			0x49, 0x45, 0x4E, 0x44,
			0xAE, 0x42, 0x60, 0x82
		};
		ContentIconService svc = NewService(MakeOkFetcher("image/png", png));
		svc.RegisterTrustedUrl("modrinth", "mod", "PNGBADCRC", "https://cdn.modrinth.com/data/PNGBADCRC/icon.png");
		IconResult r = await svc.GetAsync("modrinth", "mod", "PNGBADCRC", CancellationToken.None);
		Assert.Equal(IconStatus.BadImage, r.Status);
		AssertEmptyCacheDir("modrinth", "mod");
	}

	[Fact]
	public async Task GetAsync_JpegNoSofSos_IsRejectedAndNotCached()
	{
		// JPEG: SOI + APP0 + EOI only — no SOF (frame) and no SOS (scan)
		// Current limited parser accepts this because it has SOI and EOI with valid segments
		byte[] jpeg = new byte[] {
			0xFF, 0xD8,  // SOI
			0xFF, 0xE0, 0x00, 0x10,  // APP0 segment (JFIF)
			0x4A, 0x46, 0x49, 0x46, 0x00, 0x01, 0x01, 0x00, 0x00, 0x01, 0x00, 0x01, 0x00, 0x00,
			0xFF, 0xD9  // EOI
		};
		ContentIconService svc = NewService(MakeOkFetcher("image/jpeg", jpeg));
		svc.RegisterTrustedUrl("modrinth", "mod", "JPGNOSOF", "https://cdn.modrinth.com/data/JPGNOSOF/icon.jpg");
		IconResult r = await svc.GetAsync("modrinth", "mod", "JPGNOSOF", CancellationToken.None);
		Assert.Equal(IconStatus.BadImage, r.Status);
		AssertEmptyCacheDir("modrinth", "mod");

		// Second request must not hit cache with bad image
		IconResult r2 = await svc.GetAsync("modrinth", "mod", "JPGNOSOF", CancellationToken.None);
		Assert.Equal(IconStatus.BadImage, r2.Status);
	}

	[Fact]
	public async Task GetAsync_WebpVp8ZeroPayload_IsRejectedAndNotCached()
	{
		// WebP: RIFF/WEBP + VP8 chunk with zero payload length
		byte[] webp = new byte[] {
			(byte)'R',(byte)'I',(byte)'F',(byte)'F',
			0x10, 0x00, 0x00, 0x00,  // RIFF size = 16 (total - 8)
			(byte)'W',(byte)'E',(byte)'B',(byte)'P',
			(byte)'V',(byte)'P',(byte)'8',(byte)' ',
			0x00, 0x00, 0x00, 0x00,  // VP8 chunk size = 0 (ZERO PAYLOAD!)
		};
		ContentIconService svc = NewService(MakeOkFetcher("image/webp", webp));
		svc.RegisterTrustedUrl("modrinth", "mod", "WPVP8ZERO", "https://cdn.modrinth.com/data/WPVP8ZERO/icon.webp");
		IconResult r = await svc.GetAsync("modrinth", "mod", "WPVP8ZERO", CancellationToken.None);
		Assert.Equal(IconStatus.BadImage, r.Status);
		AssertEmptyCacheDir("modrinth", "mod");
	}

	[Fact]
	public async Task GetAsync_WebpVp8xOnlyNoImageData_IsRejectedAndNotCached()
	{
		// WebP: Only VP8X header (extended format), no actual VP8/VP8L/ANMF image data chunks
		// Current limited parser sees VP8X and returns true (sawChunk = true)
		byte[] webp = new byte[] {
			(byte)'R',(byte)'I',(byte)'F',(byte)'F',
			0x20, 0x00, 0x00, 0x00,  // RIFF size
			(byte)'W',(byte)'E',(byte)'B',(byte)'P',
			(byte)'V',(byte)'P',(byte)'8',(byte)'X',
			0x10, 0x00, 0x00, 0x00,  // VP8X chunk size = 16
			0x00, 0x00, 0x00,        // flags (no animation, no alpha, etc.)
			0x00, 0x00, 0x01,        // canvas width = 1 (24-bit little endian)
			0x00, 0x00, 0x01,        // canvas height = 1
			0x00, 0x00, 0x00, 0x00, 0x00, 0x00  // reserved
			// NO VP8, VP8L, or ANMF chunks follow!
		};
		ContentIconService svc = NewService(MakeOkFetcher("image/webp", webp));
		svc.RegisterTrustedUrl("modrinth", "mod", "WPVP8XONLY", "https://cdn.modrinth.com/data/WPVP8XONLY/icon.webp");
		IconResult r = await svc.GetAsync("modrinth", "mod", "WPVP8XONLY", CancellationToken.None);
		Assert.Equal(IconStatus.BadImage, r.Status);
		AssertEmptyCacheDir("modrinth", "mod");
	}

	[Fact]
	public async Task GetAsync_TrustedUrlExpiredAfterTtl_ReturnsNotFound_NoNetwork()
	{
		// S2-4 / P2R-8：注入时钟。TTL 内可获取；超过 TTL 必须 NotFound 且不再联网。
		long fakeNow = DateTimeOffset.UtcNow.UtcTicks;
		int fetchCount = 0;
		ContentIconService svc = NewService((_, __) => { fetchCount++; throw new InvalidOperationException("must not fetch"); }, () => fakeNow);
		svc.RegisterTrustedUrl("modrinth", "mod", "TTL", "https://cdn.modrinth.com/data/TTL/icon.png");
		// TTL 内（还差 1 tick）→ 映射有效，会尝试联网（fetcher 抛错 → UpstreamError）
		fakeNow += ContentIconService.TrustedUrlTtl.Ticks - 1;
		IconResult inside = await svc.GetAsync("modrinth", "mod", "TTL", CancellationToken.None);
		Assert.Equal(IconStatus.UpstreamError, inside.Status);
		Assert.Equal(1, fetchCount);
		// 达到/超过 TTL → NotFound 且不联网
		fakeNow += 2;
		IconResult expired = await svc.GetAsync("modrinth", "mod", "TTL", CancellationToken.None);
		Assert.Equal(IconStatus.NotFound, expired.Status);
		Assert.Equal(1, fetchCount);
	}

	[Fact]
	public async Task GetAsync_EmptyReRegisterClearsOldMapping()
	{
		// S2-4 / P2R-9：新的搜索结果无图标（空 URL 重注册）必须清除旧映射，旧图标不再可访问。
		int fetchCount = 0;
		ContentIconService svc = NewService((_, __) => { fetchCount++; throw new InvalidOperationException("must not fetch"); });
		svc.RegisterTrustedUrl("modrinth", "mod", "CLR", "https://cdn.modrinth.com/data/CLR/icon.png");
		IconResult before = await svc.GetAsync("modrinth", "mod", "CLR", CancellationToken.None);
		Assert.Equal(IconStatus.UpstreamError, before.Status); // 旧映射存在 → 会尝试联网
		Assert.Equal(1, fetchCount);
		// 新搜索结果无图标 → 清空映射
		svc.RegisterTrustedUrl("modrinth", "mod", "CLR", "");
		IconResult after = await svc.GetAsync("modrinth", "mod", "CLR", CancellationToken.None);
		Assert.Equal(IconStatus.NotFound, after.Status);
		Assert.Equal(1, fetchCount); // 不再联网
	}

	[Fact]
	public async Task GetAsync_FirstWaiterCancelDoesNotBreakSecondWaiter()
	{
		// S2-7 / P2R-17：共享 in-flight 下载不应归属第一个调用者的 CancellationToken。
		// 第一个等待者取消后，第二个等待者仍必须取得结果。
		byte[] png = LoadTestPng();
		TaskCompletionSource entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		ContentIconService svc = NewService(async (_, ct) =>
		{
			entered.TrySetResult();
			await Task.Delay(2000, ct);
			HttpResponseMessage m = new HttpResponseMessage(System.Net.HttpStatusCode.OK);
			m.Content = new StreamContent(new MemoryStream(png));
			m.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
			return m;
		});
		svc.RegisterTrustedUrl("modrinth", "mod", "SHARE", "https://cdn.modrinth.com/data/SHARE/icon.png");
		using CancellationTokenSource cts1 = new CancellationTokenSource();
		using CancellationTokenSource cts2 = new CancellationTokenSource();
		Task<IconResult> t1 = svc.GetAsync("modrinth", "mod", "SHARE", cts1.Token);
		await entered.Task; // 共享任务已注册并进入下载
		Task<IconResult> t2 = svc.GetAsync("modrinth", "mod", "SHARE", cts2.Token);
		cts1.Cancel();
		IconResult r2 = await t2;
		Assert.Equal(IconStatus.Ok, r2.Status);
		IconResult r1 = await t1;
		Assert.Equal(IconStatus.NetworkError, r1.Status); // 第一个等待者取消 → 仅自身返回 NetworkError
	}

	// -------------------------------------------------------------------------
	//  安全返修 v2 —— P2R-4/5/6/7/13/14/15/16/18 补充（DNS 绑定 / 生命周期 / 故障注入）
	// -------------------------------------------------------------------------

	[Fact]
	public async Task GetAsync_DnsRebindingAttempt_ConnectsOnlyToValidatedPublicIp()
	{
		// P2R-4：首次解析返回公网 1.1.1.1；若代码在连接前二次解析会得到私网 10.0.0.1。
		// 真实 transport 路径（HttpFetcher=null）：ConnectFactory 观测实际连接目标，
		// 必须只用已验证公网 IP，且不发生二次解析（无 TOCTOU 窗口）。
		int dnsCalls = 0;
		ContentIconService svc = NewService();
		svc.DnsResolver = (_, __) =>
		{
			Interlocked.Increment(ref dnsCalls);
			return Task.FromResult(dnsCalls == 1
				? new[] { IPAddress.Parse("1.1.1.1") }
				: new[] { IPAddress.Parse("10.0.0.1") });
		};
		List<IPAddress> connected = new List<IPAddress>();
		svc.ConnectFactory = (ip, _, _) =>
		{
			lock (connected) connected.Add(ip);
			throw new InvalidOperationException("simulated connect failure");
		};
		svc.RegisterTrustedUrl("modrinth", "mod", "RB1", "https://cdn.modrinth.com/data/RB1/icon.png");
		IconResult r = await svc.GetAsync("modrinth", "mod", "RB1", CancellationToken.None);
		Assert.Equal(IconStatus.UpstreamError, r.Status);
		Assert.Equal(1, dnsCalls);                                      // 未发生二次解析
		Assert.Contains(IPAddress.Parse("1.1.1.1"), connected);         // 实际连接目标 = 已验证公网 IP
		Assert.DoesNotContain(IPAddress.Parse("10.0.0.1"), connected);  // 绝不连接私网
	}

	[Fact]
	public async Task GetAsync_RedirectReResolvesAndValidatesEachHop()
	{
		// P2R-5：重定向每一跳都重新解析并校验地址（本跳解析记录必须包含两个 host）。
		byte[] png = LoadTestPng();
		Dictionary<string, IPAddress[]> dns = new(StringComparer.OrdinalIgnoreCase)
		{
			["cdn.modrinth.com"] = new[] { IPAddress.Parse("1.1.1.1") },
			["cdn-raw.modrinth.com"] = new[] { IPAddress.Parse("1.0.0.1") },
		};
		List<string> resolvedHosts = new List<string>();
		ContentIconService svc = NewService((u, _) =>
		{
			if (u.Host.Equals("cdn.modrinth.com", StringComparison.OrdinalIgnoreCase))
			{
				HttpResponseMessage m = new HttpResponseMessage(HttpStatusCode.Found);
				m.Headers.Location = new Uri("https://cdn-raw.modrinth.com/data/RB2/icon.png");
				return Task.FromResult(m);
			}
			return Task.FromResult(MakeOk(png));
		});
		svc.DnsResolver = (host, ct) =>
		{
			lock (resolvedHosts) resolvedHosts.Add(host);
			return Task.FromResult(dns[host]);
		};
		svc.RegisterTrustedUrl("modrinth", "mod", "RB2", "https://cdn.modrinth.com/data/RB2/icon.png");
		IconResult r = await svc.GetAsync("modrinth", "mod", "RB2", CancellationToken.None);
		Assert.Equal(IconStatus.Ok, r.Status);
		lock (resolvedHosts)
		{
			Assert.Contains("cdn.modrinth.com", resolvedHosts);
			Assert.Contains("cdn-raw.modrinth.com", resolvedHosts); // 第二跳重新解析
		}
	}

	[Fact]
	public async Task GetAsync_RedirectHopResolvingPrivate_IsRejected()
	{
		// P2R-5：重定向到第二跳时解析出私网 → 整体拒绝（每跳都重新校验，不放过后续跳）。
		byte[] png = LoadTestPng();
		Dictionary<string, IPAddress[]> dns = new(StringComparer.OrdinalIgnoreCase)
		{
			["cdn.modrinth.com"] = new[] { IPAddress.Parse("1.1.1.1") },
			["cdn-raw.modrinth.com"] = new[] { IPAddress.Parse("10.0.0.1") },
		};
		ContentIconService svc = NewService((u, _) =>
		{
			if (u.Host.Equals("cdn.modrinth.com", StringComparison.OrdinalIgnoreCase))
			{
				HttpResponseMessage m = new HttpResponseMessage(HttpStatusCode.Found);
				m.Headers.Location = new Uri("https://cdn-raw.modrinth.com/data/RB3/icon.png");
				return Task.FromResult(m);
			}
			return Task.FromResult(MakeOk(png));
		});
		svc.DnsResolver = (host, ct) => Task.FromResult(dns[host]);
		svc.RegisterTrustedUrl("modrinth", "mod", "RB3", "https://cdn.modrinth.com/data/RB3/icon.png");
		IconResult r = await svc.GetAsync("modrinth", "mod", "RB3", CancellationToken.None);
		Assert.Equal(IconStatus.UpstreamError, r.Status);
	}

	[Fact]
	public async Task GetAsync_MixedPublicPrivateDnsResults_IsRejected()
	{
		// S2-2：任一解析结果非公网 → 整体拒绝（维持现有保守策略）。
		ContentIconService svc = NewService((_, __) => Task.FromResult(MakeOk(LoadTestPng())));
		svc.DnsResolver = (_, __) => Task.FromResult(new[]
		{
			IPAddress.Parse("1.1.1.1"),
			IPAddress.Parse("10.0.0.1"),
		});
		svc.RegisterTrustedUrl("modrinth", "mod", "MIX", "https://cdn.modrinth.com/data/MIX/icon.png");
		IconResult r = await svc.GetAsync("modrinth", "mod", "MIX", CancellationToken.None);
		Assert.Equal(IconStatus.UpstreamError, r.Status);
	}

	[Fact]
	public async Task GetAsync_MultipleRequestsReuseSingleHandler()
	{
		// P2R-6：不同图标的多次请求复用同一 handler/连接池，不每请求新建并遗失。
		ContentIconService svc = NewService();
		svc.ConnectFactory = (_, _, _) => throw new InvalidOperationException("simulated connect failure");
		svc.RegisterTrustedUrl("modrinth", "mod", "R1", "https://cdn.modrinth.com/data/R1/icon.png");
		svc.RegisterTrustedUrl("modrinth", "mod", "R2", "https://cdn.modrinth.com/data/R2/icon.png");
		await svc.GetAsync("modrinth", "mod", "R1", CancellationToken.None);
		await svc.GetAsync("modrinth", "mod", "R2", CancellationToken.None);
		Assert.Equal(1, svc.HandlerBuildCount);   // 只构建一个 handler
		Assert.True(svc.ConnectCallCount >= 2);   // 两个不同 key 各自连接，复用同一 handler
	}

	[Fact]
	public async Task Dispose_ReleasesTransportResources()
	{
		// P2R-7：服务 Dispose 后 transport 资源被释放，且可重复调用（幂等）。
		ContentIconService svc = NewService();
		svc.ConnectFactory = (_, _, _) => throw new InvalidOperationException("simulated connect failure");
		svc.RegisterTrustedUrl("modrinth", "mod", "D7", "https://cdn.modrinth.com/data/D7/icon.png");
		await svc.GetAsync("modrinth", "mod", "D7", CancellationToken.None);
		Assert.Equal(1, svc.HandlerBuildCount);
		Assert.False(svc.TransportDisposed);
		svc.Dispose();
		Assert.True(svc.TransportDisposed);
		svc.Dispose(); // 幂等
		Assert.True(svc.TransportDisposed);
	}

	[Fact]
	public async Task DisposeAsync_ReleasesTransportResources()
	{
		ContentIconService svc = NewService();
		svc.ConnectFactory = (_, _, _) => throw new InvalidOperationException("simulated connect failure");
		svc.RegisterTrustedUrl("modrinth", "mod", "DA7", "https://cdn.modrinth.com/data/DA7/icon.png");
		await svc.GetAsync("modrinth", "mod", "DA7", CancellationToken.None);
		await svc.DisposeAsync();
		Assert.True(svc.TransportDisposed);
	}

	// -------------------------------------------------------------------------
	//  生产单例生命周期：DisposeDefault / ResetDefaultForShutdown 测试
	// -------------------------------------------------------------------------

	[Fact]
	public void DisposeDefault_ReleasesDefaultInstanceTransport()
	{
		// 创建默认实例并使用它
		ContentIconService svc = ContentIconService.Default;
		try
		{
			svc.ConnectFactory = (_, _, _) => throw new InvalidOperationException("simulated connect failure");
			svc.DnsResolver = FakeDns; // 注入受控 DNS,消除对真实 DNS / TUN / fake-ip 的依赖
			svc.RegisterTrustedUrl("modrinth", "mod", "DISPDEF1", "https://cdn.modrinth.com/data/DISPDEF1/icon.png");
			// 触发 handler 创建
			_ = svc.GetAsync("modrinth", "mod", "DISPDEF1", CancellationToken.None).GetAwaiter().GetResult();
			Assert.Equal(1, svc.HandlerBuildCount);
			Assert.False(svc.TransportDisposed);

			// 调用静态释放方法
			ContentIconService.DisposeDefault();

			// 旧实例应被释放
			Assert.True(svc.TransportDisposed);
		}
		finally
		{
			// 任一断言失败时仍清理静态默认实例,避免污染后续测试
			ContentIconService.DisposeDefault();
		}
	}

	[Fact]
	public void DisposeDefault_Idempotent_NoThrow()
	{
		// 确保有默认实例
		var _ = ContentIconService.Default;
		// 第一次调用
		ContentIconService.DisposeDefault();
		// 第二次调用不应抛异常
		ContentIconService.DisposeDefault();
		// 第三次调用也不应抛异常
		ContentIconService.DisposeDefault();
		// 通过即成功
		Assert.True(true);
	}

	[Fact]
	public void DisposeDefault_AfterDispose_NewDefaultInstanceCreated()
	{
		// 创建并释放默认实例
		var svc1 = ContentIconService.Default;
		try
		{
			svc1.ConnectFactory = (_, _, _) => throw new InvalidOperationException("simulated connect failure");
			svc1.DnsResolver = FakeDns; // 注入受控 DNS,消除对真实 DNS / TUN / fake-ip 的依赖
			svc1.RegisterTrustedUrl("modrinth", "mod", "DISPDEF2", "https://cdn.modrinth.com/data/DISPDEF2/icon.png");
			_ = svc1.GetAsync("modrinth", "mod", "DISPDEF2", CancellationToken.None).GetAwaiter().GetResult();
			int handlerCount1 = svc1.HandlerBuildCount;
			Assert.Equal(1, handlerCount1);

			ContentIconService.DisposeDefault();

			// 再次访问 Default 应创建新实例
			var svc2 = ContentIconService.Default;
			svc2.ConnectFactory = (_, _, _) => throw new InvalidOperationException("simulated connect failure 2");
			svc2.DnsResolver = FakeDns; // 注入受控 DNS,svc2 不能继承 svc1 的注入
			svc2.RegisterTrustedUrl("modrinth", "mod", "DISPDEF3", "https://cdn.modrinth.com/data/DISPDEF3/icon.png");
			_ = svc2.GetAsync("modrinth", "mod", "DISPDEF3", CancellationToken.None).GetAwaiter().GetResult();
			// 新实例应有独立的 handler
			Assert.NotSame(svc1, svc2);
			Assert.Equal(1, svc2.HandlerBuildCount);
			Assert.False(svc2.TransportDisposed);
		}
		finally
		{
			// 任一断言失败时仍清理静态默认实例,避免污染后续测试
			ContentIconService.DisposeDefault();
		}
	}

	[Fact]
	public async Task GetAsync_ActiveRequestSuspendedAtSignal_DisposeDefault_SafeCompletion()
	{
		// 返修六：请求必须真实 await 一个 TaskCompletionSource，并用第二个信号确认
		// "已进入悬挂点"，禁止用 Task.Delay 充当进入关键点的证据。
		// 设计语义：共享 in-flight 下载使用 CancellationToken.None（不归属调用者），
		// 因此 DisposeDefault 不触发服务级取消，请求按明确设计安全结束。
		TaskCompletionSource enteredSuspension = new(TaskCreationOptions.RunContinuationsAsynchronously);
		TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
		ContentIconService svc = ContentIconService.Default;
		svc.DnsResolver = FakeDns;
		svc.HttpFetcher = async (_, ct) =>
		{
			enteredSuspension.TrySetResult(); // 信号 1：请求已进入悬挂点（等待释放）
			await release.Task.WaitAsync(TimeSpan.FromSeconds(15), ct);
			HttpResponseMessage m = new HttpResponseMessage(HttpStatusCode.BadGateway);
			return m;
		};
		svc.RegisterTrustedUrl("modrinth", "mod", "DISPDL", "https://cdn.modrinth.com/data/DISPDL/icon.png");

		Task<IconResult> t = svc.GetAsync("modrinth", "mod", "DISPDL", CancellationToken.None);
		// 限定超时等待"已进入悬挂点"信号，防止测试永久挂起
		await enteredSuspension.Task.WaitAsync(TimeSpan.FromSeconds(10));

		// 请求确实未完成时释放默认实例
		Assert.False(t.IsCompleted, "请求在释放前不应完成");
		ContentIconService.DisposeDefault();

		release.TrySetResult();
		IconResult r = await t.WaitAsync(TimeSpan.FromSeconds(15)); // 不抛、不挂起、无未观察异常
		Assert.Equal(TaskStatus.RanToCompletion, t.Status);
		Assert.Equal(IconStatus.UpstreamError, r.Status);

		// 新 Default 实例不受旧请求影响
		ContentIconService svc2 = ContentIconService.Default;
		Assert.NotSame(svc, svc2);
		Assert.False(svc2.TransportDisposed);
		svc2.DnsResolver = FakeDns;
		svc2.ConnectFactory = (_, _, _) => throw new InvalidOperationException("simulated connect failure");
		svc2.RegisterTrustedUrl("modrinth", "mod", "DISPDL2", "https://cdn.modrinth.com/data/DISPDL2/icon.png");
		IconResult r2 = await svc2.GetAsync("modrinth", "mod", "DISPDL2", CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(15));
		Assert.Equal(IconStatus.UpstreamError, r2.Status);
		Assert.Equal(1, svc2.HandlerBuildCount); // 新实例不复用已释放 handler
		ContentIconService.DisposeDefault(); // 清理，避免影响其他测试
	}

	[Fact]
	public async Task GetAsync_AfterDisposeDefault_NewRequestsUseNewInstance()
	{
		// 释放默认实例后，新请求应使用新实例，不得 NullReference
		var svc1 = ContentIconService.Default;
		svc1.ConnectFactory = (_, _, _) => throw new InvalidOperationException("simulated connect failure");
		svc1.RegisterTrustedUrl("modrinth", "mod", "AFTER1", "https://cdn.modrinth.com/data/AFTER1/icon.png");
		await svc1.GetAsync("modrinth", "mod", "AFTER1", CancellationToken.None);
		// 返修六：删除恒真断言（TransportDisposed || !TransportDisposed），
		// 改为明确断言：释放前 transport 必然未释放。
		Assert.False(svc1.TransportDisposed);

		ContentIconService.DisposeDefault();

		// 新请求
		var svc2 = ContentIconService.Default;
		svc2.ConnectFactory = (_, _, _) => throw new InvalidOperationException("simulated connect failure 2");
		svc2.RegisterTrustedUrl("modrinth", "mod", "AFTER2", "https://cdn.modrinth.com/data/AFTER2/icon.png");
		IconResult r = await svc2.GetAsync("modrinth", "mod", "AFTER2", CancellationToken.None);
		Assert.Equal(IconStatus.UpstreamError, r.Status);
		Assert.False(svc2.TransportDisposed);
		Assert.NotSame(svc1, svc2);
	}

	[Fact]
	public async Task GetAsync_ImageCommitFailure_FreshFetchLeavesNoMixedCache()
	{
		// P2R-14：全新抓取时图片提交失败 → 不落任何混合/半成品缓存（目录为空）。
		byte[] png = LoadTestPng();
		ContentIconService svc = NewService((_, __) => Task.FromResult(MakeOk(png)));
		svc.BeforeImageCommit = (_, __) => throw new IOException("simulated image commit failure");
		svc.RegisterTrustedUrl("modrinth", "mod", "ICF", "https://cdn.modrinth.com/data/ICF/icon.png");
		IconResult r = await svc.GetAsync("modrinth", "mod", "ICF", CancellationToken.None);
		Assert.Equal(IconStatus.NetworkError, r.Status);
		AssertEmptyCacheDir("modrinth", "mod");
	}

	[Fact]
	public async Task GetAsync_ManifestCommitFailureOnRefresh_KeepsOldCacheValid()
	{
		// P2R-13：已有有效缓存时刷新，元数据提交失败 → 旧 manifest+旧图片仍一致可用。
		byte[] png = LoadTestPng();
		long fakeNow = DateTimeOffset.UtcNow.UtcTicks;
		Func<long> clock = () => fakeNow;
		ContentIconService svc = NewService((_, __) =>
		{
			HttpResponseMessage m = MakeOk(png);
			m.Headers.ETag = new System.Net.Http.Headers.EntityTagHeaderValue("\"v1\"");
			return Task.FromResult(m);
		}, clock);
		svc.RegisterTrustedUrl("modrinth", "mod", "MCF", "https://cdn.modrinth.com/data/MCF/icon.png");
		IconResult r1 = await svc.GetAsync("modrinth", "mod", "MCF", CancellationToken.None);
		Assert.Equal(IconStatus.Ok, r1.Status);
		Assert.NotNull(r1.CachePath);

		// 推进时钟让缓存 stale，并重新注册刷新 TTL（本用例只验证元数据提交失败语义）
		fakeNow += TimeSpan.FromDays(8).Ticks;
		await Task.Delay(50);
		svc.RegisterTrustedUrl("modrinth", "mod", "MCF", "https://cdn.modrinth.com/data/MCF/icon.png");
		svc.BeforeManifestCommit = (_, __) => throw new IOException("simulated meta commit failure");
		IconResult r2 = await svc.GetAsync("modrinth", "mod", "MCF", CancellationToken.None);
		Assert.Equal(IconStatus.NetworkError, r2.Status);

		// 旧 manifest 仍指向旧图片，且旧图片文件仍在
		string dir = Path.Combine(_cacheRoot, "modrinth", "mod");
		string urlHash = ContentIconService.ComputeCacheKey("modrinth", "mod", "MCF", "https://cdn.modrinth.com/data/MCF/icon.png");
		string manifestPath = Path.Combine(dir, urlHash + ".json");
		Assert.True(File.Exists(manifestPath), "旧 manifest 必须仍然存在");
		string imageRef = ReadManifestField(File.ReadAllText(manifestPath), "image");
		Assert.Equal(Path.GetFileName(r1.CachePath!), imageRef);
		Assert.True(File.Exists(Path.Combine(dir, imageRef)), "旧图片必须仍然存在");
	}

	[Fact]
	public async Task GetAsync_ImageCommitFailureOnRefresh_KeepsOldCacheValid()
	{
		// P2R-15：刷新时新图片提交失败 → 旧 manifest+旧图片仍一致可用（无混合版本）。
		byte[] png = LoadTestPng();
		long fakeNow = DateTimeOffset.UtcNow.UtcTicks;
		Func<long> clock = () => fakeNow;
		ContentIconService svc = NewService((_, __) =>
		{
			HttpResponseMessage m = MakeOk(png);
			m.Headers.ETag = new System.Net.Http.Headers.EntityTagHeaderValue("\"v1\"");
			return Task.FromResult(m);
		}, clock);
		svc.RegisterTrustedUrl("modrinth", "mod", "ICF2", "https://cdn.modrinth.com/data/ICF2/icon.png");
		IconResult r1 = await svc.GetAsync("modrinth", "mod", "ICF2", CancellationToken.None);
		Assert.Equal(IconStatus.Ok, r1.Status);
		Assert.NotNull(r1.CachePath);

		fakeNow += TimeSpan.FromDays(8).Ticks;
		await Task.Delay(50);
		svc.RegisterTrustedUrl("modrinth", "mod", "ICF2", "https://cdn.modrinth.com/data/ICF2/icon.png");
		svc.BeforeImageCommit = (_, __) => throw new IOException("simulated image commit failure");
		IconResult r2 = await svc.GetAsync("modrinth", "mod", "ICF2", CancellationToken.None);
		Assert.Equal(IconStatus.NetworkError, r2.Status);

		string dir = Path.Combine(_cacheRoot, "modrinth", "mod");
		string urlHash = ContentIconService.ComputeCacheKey("modrinth", "mod", "ICF2", "https://cdn.modrinth.com/data/ICF2/icon.png");
		string manifestPath = Path.Combine(dir, urlHash + ".json");
		Assert.True(File.Exists(manifestPath), "旧 manifest 必须仍然存在");
		string imageRef = ReadManifestField(File.ReadAllText(manifestPath), "image");
		Assert.Equal(Path.GetFileName(r1.CachePath!), imageRef);
		Assert.True(File.Exists(Path.Combine(dir, imageRef)), "旧图片必须仍然存在");
	}

	[Fact]
	public async Task GetAsync_StartupOrphanTempFiles_IgnoredAndCleaned()
	{
		// P2R-16：进程启动遇到孤立临时/版本文件 → 不误命中，读取时清理。
		byte[] png = LoadTestPng();
		ContentIconService svc = NewService((_, __) => Task.FromResult(MakeOk(png)));
		svc.RegisterTrustedUrl("modrinth", "mod", "ORPH", "https://cdn.modrinth.com/data/ORPH/icon.png");
		await svc.GetAsync("modrinth", "mod", "ORPH", CancellationToken.None); // 建立有效缓存

		string dir = Path.Combine(_cacheRoot, "modrinth", "mod");
		string urlHash = ContentIconService.ComputeCacheKey("modrinth", "mod", "ORPH", "https://cdn.modrinth.com/data/ORPH/icon.png");
		string orphanTmp = Path.Combine(dir, urlHash + ".json.tmp-deadbeef");
		string orphanImage = Path.Combine(dir, urlHash + ".png.v99999999999999");
		File.WriteAllText(orphanTmp, "{\"formatVersion\":2}");
		File.WriteAllBytes(orphanImage, new byte[] { 1, 2, 3, 4 });

		IconResult r = await svc.GetAsync("modrinth", "mod", "ORPH", CancellationToken.None);
		Assert.Equal(IconStatus.FromCache, r.Status);
		Assert.False(File.Exists(orphanTmp), "孤立 manifest 临时文件应被清理");
		Assert.False(File.Exists(orphanImage), "孤立版本图片应被清理");
	}

	[Fact]
	public async Task GetAsync_DisposeWithSuspendedInflightDownload_SafeCompletion()
	{
		// 返修六：注入 fetcher 实际 await TaskCompletionSource，用第二个信号确认
		// 请求已进入悬挂点后再 Dispose；禁止用 Task.Delay(50) 充当进入关键点的证据。
		byte[] png = LoadTestPng64();
		TaskCompletionSource enteredSuspension = new(TaskCreationOptions.RunContinuationsAsynchronously);
		TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
		ContentIconService svc = NewService(async (_, ct) =>
		{
			enteredSuspension.TrySetResult(); // 信号：请求已进入悬挂点
			await release.Task.WaitAsync(TimeSpan.FromSeconds(15), ct);
			return MakeOk(png);
		});
		svc.RegisterTrustedUrl("modrinth", "mod", "DISP", "https://cdn.modrinth.com/data/DISP/icon.png");
		Task<IconResult> t = svc.GetAsync("modrinth", "mod", "DISP", CancellationToken.None);
		await enteredSuspension.Task.WaitAsync(TimeSpan.FromSeconds(10)); // 限定超时，防永久挂起
		Assert.False(t.IsCompleted, "请求在 Dispose 前不应完成");

		svc.Dispose();
		Assert.True(svc.TransportDisposed);

		release.TrySetResult();
		IconResult r = await t.WaitAsync(TimeSpan.FromSeconds(15)); // 不抛、不挂起、无未观察异常
		Assert.Equal(TaskStatus.RanToCompletion, t.Status);
		Assert.Equal(IconStatus.Ok, r.Status); // 按明确设计安全结束（共享下载不归属调用者取消）
	}

	// -------------------------------------------------------------------------
	//  取消 / 超时
	// -------------------------------------------------------------------------

	[Fact]
	public async Task GetAsync_CancellationIsReportedAsNetworkError()
	{
		ContentIconService svc = NewService(async (_, ct) =>
		{
			await Task.Delay(2000, ct);
			return new HttpResponseMessage(System.Net.HttpStatusCode.OK);
		});
		svc.RegisterTrustedUrl("modrinth", "mod", "CXL", "https://cdn.modrinth.com/data/CXL/icon.png");
		using CancellationTokenSource cts = new CancellationTokenSource(50);
		IconResult r = await svc.GetAsync("modrinth", "mod", "CXL", cts.Token);
		Assert.Equal(IconStatus.NetworkError, r.Status);
	}

	// -------------------------------------------------------------------------
	//  第二轮最终返修 —— 三：WebP chunk 长度索引（先红后绿）
	// -------------------------------------------------------------------------

	[Fact]
	public void TryValidateImageStructure_RealWebpLargePayload_Succeeds()
	{
		// 返修三：真实可解码、VP8 payload > 255 字节的 WebP。
		// 当前实现把 chunk size 高 3 字节读成 RIFF 头 d[5..7]，对 >263 字节的真实 WebP
		// 会错读 size 并在 payload 中间错误推进 → 误判为损坏。必须在当前实现上失败。
		byte[] webp = LoadTestWebp64(); // 2570 字节，VP8 payload 2550 字节
		Assert.True(ContentIconService.TryValidateImageStructure(webp, "webp"));
	}

	[Fact]
	public void TryValidateImageStructure_WebpChunkLengthBeyondData_Fails()
	{
		// chunk 声明长度大于实际数据 → BadImage 且不缓存（端到端见 GetAsync 用例）。
		// 构造：真实 VP8 payload + 把 chunk size 字段改大 + riffSize 同步改大（保持自洽外表）。
		byte[] webp = LoadTestWebp64();
		byte[] evil = (byte[])webp.Clone();
		long realSize = evil[16] | (evil[17] << 8) | (evil[18] << 16) | (evil[19] << 24);
		long evilSize = realSize + 1000;
		evil[16] = (byte)evilSize; evil[17] = (byte)(evilSize >> 8); evil[18] = (byte)(evilSize >> 16); evil[19] = (byte)(evilSize >> 24);
		long riff = evil.Length - 8 + 1000;
		evil[4] = (byte)riff; evil[5] = (byte)(riff >> 8); evil[6] = (byte)(riff >> 16); evil[7] = (byte)(riff >> 24);
		Assert.False(ContentIconService.TryValidateImageStructure(evil, "webp"));
	}

	[Fact]
	public void TryValidateImageStructure_WebpChunkSizeConsistentWithRiffButOversized_Fails()
	{
		// riffSize 与文件一致，但 chunk size 超出 RIFF 数据区 → 拒绝。
		byte[] webp = LoadTestWebp64();
		byte[] evil = (byte[])webp.Clone();
		long evilSize = evil.Length; // 远超剩余数据
		evil[16] = (byte)evilSize; evil[17] = (byte)(evilSize >> 8); evil[18] = (byte)(evilSize >> 16); evil[19] = (byte)(evilSize >> 24);
		Assert.False(ContentIconService.TryValidateImageStructure(evil, "webp"));
	}

	[Fact]
	public void TryValidateImageStructure_WebpMaliciousHugeChunkLength_NoOverflow()
	{
		// 大于 int 范围的恶意 chunk 长度：必须返回 false 且不抛溢出异常。
		byte[] webp = LoadTestWebp64();
		foreach (long evilSize in new long[] { 0x7FFFFFFFL, 0xFFFFFFFFL })
		{
			byte[] evil = (byte[])webp.Clone();
			evil[16] = (byte)evilSize; evil[17] = (byte)(evilSize >> 8); evil[18] = (byte)(evilSize >> 16); evil[19] = (byte)(evilSize >> 24);
			Assert.False(ContentIconService.TryValidateImageStructure(evil, "webp"));
		}
	}

	[Fact]
	public void TryValidateImageStructure_WebpOddChunkSizeWithPadding_ParsesAllChunks()
	{
		// RIFF little-endian + chunk padding（奇数长度补 1 字节）：后续 chunk 必须能正确解析。
		// 构造：RIFF + "ICCP" 奇数长度 chunk(padding) + "VP8 " chunk(size=0)。
		byte[] payload = { 1, 2, 3 }; // 奇数长度
		using MemoryStream ms = new MemoryStream();
		ms.Write(new byte[] { (byte)'R', (byte)'I', (byte)'F', (byte)'F', 0, 0, 0, 0, (byte)'W', (byte)'E', (byte)'B', (byte)'P' });
		ms.Write(new byte[] { (byte)'I', (byte)'C', (byte)'C', (byte)'P', 3, 0, 0, 0 });
		ms.Write(payload);
		ms.Write(new byte[] { 0 }); // padding
		ms.Write(new byte[] { (byte)'V', (byte)'P', (byte)'8', (byte)' ', 0, 0, 0, 0 });
		byte[] webp = ms.ToArray();
		long riff = webp.Length - 8;
		webp[4] = (byte)riff; webp[5] = (byte)(riff >> 8); webp[6] = (byte)(riff >> 16); webp[7] = (byte)(riff >> 24);
		// 结构解析必须成功（ICCP 允许，VP8 图像 chunk 存在）；真实拒绝交给解码器验证
		Assert.True(ContentIconService.TryValidateImageStructure(webp, "webp"));
	}

	[Fact]
	public void TryValidateImageStructure_WebpUnknownChunkType_Fails()
	{
		// 未知 chunk 类型必须拒绝（防止带外数据绕过）。
		byte[] payload = { 1, 2, 3, 4 };
		using MemoryStream ms = new MemoryStream();
		ms.Write(new byte[] { (byte)'R', (byte)'I', (byte)'F', (byte)'F', 0, 0, 0, 0, (byte)'W', (byte)'E', (byte)'B', (byte)'P' });
		ms.Write(new byte[] { (byte)'E', (byte)'V', (byte)'I', (byte)'L', 4, 0, 0, 0 });
		ms.Write(payload);
		byte[] webp = ms.ToArray();
		long riff = webp.Length - 8;
		webp[4] = (byte)riff; webp[5] = (byte)(riff >> 8); webp[6] = (byte)(riff >> 16); webp[7] = (byte)(riff >> 24);
		Assert.False(ContentIconService.TryValidateImageStructure(webp, "webp"));
	}

	[Fact]
	public async Task GetAsync_LargeWebp_FullPipeline_MimeDecodeAndCache()
	{
		// 返修三端到端：真实大 WebP（payload > 255 字节）必须：
		// 首次响应成功且 MIME=image/webp；完整像素解码成功；第二次缓存命中且字节一致；
		// 缓存文件与返回字节一致。
		byte[] webp = LoadTestWebp64();
		ContentIconService svc = NewService(MakeOkFetcher("image/webp", webp));
		svc.RegisterTrustedUrl("modrinth", "mod", "WPBIG", "https://cdn.modrinth.com/data/WPBIG/icon.webp");
		IconResult r1 = await svc.GetAsync("modrinth", "mod", "WPBIG", CancellationToken.None);
		Assert.Equal(IconStatus.Ok, r1.Status);
		Assert.Equal("image/webp", r1.Mime);
		Assert.NotNull(r1.CachePath);
		Assert.True(File.Exists(r1.CachePath!));
		Assert.Equal(webp, File.ReadAllBytes(r1.CachePath!));

		await Task.Delay(50); // 让 in-flight ContinueWith 清理字典
		IconResult r2 = await svc.GetAsync("modrinth", "mod", "WPBIG", CancellationToken.None);
		Assert.Equal(IconStatus.FromCache, r2.Status);
		Assert.Equal(r1.Bytes, r2.Bytes);
		Assert.Equal("image/webp", r2.Mime);
	}

	[Fact]
	public async Task GetAsync_WebpChunkLengthExceedsActualData_BadImage_NotCached()
	{
		// chunk 声明长度大于实际数据 → BadImage、不生成 manifest、不生成版本图片、不缓存。
		byte[] webp = LoadTestWebp64();
		byte[] evil = (byte[])webp.Clone();
		long realSize = evil[16] | (evil[17] << 8) | (evil[18] << 16) | (evil[19] << 24);
		long evilSize = realSize + 1000;
		evil[16] = (byte)evilSize; evil[17] = (byte)(evilSize >> 8); evil[18] = (byte)(evilSize >> 16); evil[19] = (byte)(evilSize >> 24);
		long riff = evil.Length - 8 + 1000;
		evil[4] = (byte)riff; evil[5] = (byte)(riff >> 8); evil[6] = (byte)(riff >> 16); evil[7] = (byte)(riff >> 24);

		ContentIconService svc = NewService(MakeOkFetcher("image/webp", evil));
		svc.RegisterTrustedUrl("modrinth", "mod", "WPOVR", "https://cdn.modrinth.com/data/WPOVR/icon.webp");
		IconResult r = await svc.GetAsync("modrinth", "mod", "WPOVR", CancellationToken.None);
		Assert.Equal(IconStatus.BadImage, r.Status);
		AssertEmptyCacheDir("modrinth", "mod");

		await Task.Delay(50);
		IconResult r2 = await svc.GetAsync("modrinth", "mod", "WPOVR", CancellationToken.None);
		Assert.Equal(IconStatus.BadImage, r2.Status); // 不会命中损坏缓存
	}

	// -------------------------------------------------------------------------
	//  第二轮最终返修 —— 四：完整像素解码（先红后绿）
	// -------------------------------------------------------------------------

	[Fact]
	public void TryValidateImageWithDecoder_RealPngJpegWebp_FullDecodeSucceeds()
	{
		// 真实成功样本：完整像素解码成功（不是只看头部）。
		Assert.True(ContentIconService.TryValidateImageWithDecoder(LoadTestPng64(), "png"));
		Assert.True(ContentIconService.TryValidateImageWithDecoder(LoadTestJpeg64(), "jpg"));
		Assert.True(ContentIconService.TryValidateImageWithDecoder(LoadTestWebp64(), "webp"));
		Assert.True(ContentIconService.TryValidateImageWithDecoder(LoadTestPng(), "png"));
		Assert.True(ContentIconService.TryValidateImageWithDecoder(LoadTestJpeg(), "jpg"));
		Assert.True(ContentIconService.TryValidateImageWithDecoder(LoadTestWebp(), "webp"));
	}

	[Fact]
	public void TryValidateImageWithDecoder_FormatMismatch_Fails()
	{
		// 编码器识别的格式必须与魔数检测一致。
		Assert.False(ContentIconService.TryValidateImageWithDecoder(LoadTestPng64(), "webp"));
		Assert.False(ContentIconService.TryValidateImageWithDecoder(LoadTestWebp64(), "png"));
		Assert.False(ContentIconService.TryValidateImageWithDecoder(LoadTestJpeg64(), "png"));
		Assert.False(ContentIconService.TryValidateImageWithDecoder(new byte[] { 1, 2, 3 }, "png"));
	}

	[Fact]
	public void TryValidateImageWithDecoder_TruncatedPngPixelStream_ReturnsFalse()
	{
		// 返修四：PNG 合法头与尺寸（codec 可创建），但像素流（IDAT zlib 数据）截断。
		// 当前实现只看 codec.Info 宽高 → 错误接受；修复后必须返回 false。
		byte[] truncated = MakePngTruncatedIdat(LoadTestPng64());
		Assert.True(ContentIconService.TryValidateImageStructure(truncated, "png")); // 结构自洽
		Assert.False(ContentIconService.TryValidateImageWithDecoder(truncated, "png"));
	}

	[Fact]
	public void TryValidateImageWithDecoder_JpegCodecCreatableButPixelsUndecodable_ReturnsFalse()
	{
		// 返修四：JPEG 能创建 codec（SOF 头完整），但缺 DQT 量化表 → 像素不可解码。
		// 说明：libjpeg 对"扫描数据截断但 EOI 存在"会以灰色填充并返回 Success（探针实测），
		// 因此 JPEG 的字面截断用例由结构校验（EOI 必须存在）兜底，本用例覆盖解码器层。
		byte[] noDqt = MakeJpegWithoutSegments(LoadTestJpeg64(), 0xDB);
		Assert.True(ContentIconService.TryValidateImageStructure(noDqt, "jpg")); // 结构自洽
		Assert.False(ContentIconService.TryValidateImageWithDecoder(noDqt, "jpg"));
	}

	[Fact]
	public void TryValidateImageStructure_JpegScanTruncatedWithoutEoi_Fails()
	{
		// JPEG 扫描数据截断（无 EOI）→ 结构校验必须拒绝（libjpeg 会灰填，结构层兜底）。
		byte[] truncated = MakeJpegScanTruncatedNoEoi(LoadTestJpeg64());
		Assert.False(ContentIconService.TryValidateImageStructure(truncated, "jpg"));
	}

	[Fact]
	public void TryValidateImageWithDecoder_TruncatedWebpVp8_ReturnsFalse()
	{
		// 返修四：WebP 能创建 codec（RIFF/VP8X 自洽），但 VP8 像素数据截断。
		byte[] truncated = MakeWebpTruncatedVp8(LoadTestWebp64());
		Assert.True(ContentIconService.TryValidateImageStructure(truncated, "webp")); // 结构自洽
		Assert.False(ContentIconService.TryValidateImageWithDecoder(truncated, "webp"));
	}

	[Fact]
	public async Task GetAsync_TruncatedPngPixels_BadImage_NotCached_NoManifest()
	{
		byte[] truncated = MakePngTruncatedIdat(LoadTestPng64());
		await AssertTruncatedRejectedAndNotCached("PNGTR", truncated, "image/png", ".png");
	}

	[Fact]
	public async Task GetAsync_TruncatedJpegPixels_BadImage_NotCached_NoManifest()
	{
		byte[] noDqt = MakeJpegWithoutSegments(LoadTestJpeg64(), 0xDB);
		await AssertTruncatedRejectedAndNotCached("JPGTR", noDqt, "image/jpeg", ".jpg");
	}

	[Fact]
	public async Task GetAsync_TruncatedWebpPixels_BadImage_NotCached_NoManifest()
	{
		byte[] truncated = MakeWebpTruncatedVp8(LoadTestWebp64());
		await AssertTruncatedRejectedAndNotCached("WPTR", truncated, "image/webp", ".webp");
	}

	[Fact]
	public async Task GetAsync_TruncatedNewResponse_DoesNotOverwriteHealthyStaleCache()
	{
		// 已有健康 stale 缓存时，损坏的新响应必须：返回失败/保留旧缓存文件，绝不覆盖。
		byte[] png = LoadTestPng64();
		long fakeNow = DateTimeOffset.UtcNow.UtcTicks;
		ContentIconService svc = NewService((_, __) => Task.FromResult(MakeOk(png)), () => fakeNow);
		svc.RegisterTrustedUrl("modrinth", "mod", "STLV", "https://cdn.modrinth.com/data/STLV/icon.png");
		IconResult r1 = await svc.GetAsync("modrinth", "mod", "STLV", CancellationToken.None);
		Assert.Equal(IconStatus.Ok, r1.Status);
		string dir = Path.Combine(_cacheRoot, "modrinth", "mod");
		string urlHash = ContentIconService.ComputeCacheKey("modrinth", "mod", "STLV", "https://cdn.modrinth.com/data/STLV/icon.png");
		string oldManifest = Path.Combine(dir, urlHash + ".json");
		string oldImage = File.ReadAllText(oldManifest) is string mfJson ? Path.Combine(dir, ExtractJsonString(mfJson, "image")) : throw new InvalidOperationException();
		byte[] oldBytes = File.ReadAllBytes(oldImage);

		// 推进 8 天 → stale；重新注册刷新 TTL
		fakeNow += TimeSpan.FromDays(8).Ticks;
		await Task.Delay(50);
		svc.RegisterTrustedUrl("modrinth", "mod", "STLV", "https://cdn.modrinth.com/data/STLV/icon.png");
		// 新响应：截断 PNG（能过结构校验 → 若无完整解码会污染缓存）
		byte[] truncated = MakePngTruncatedIdat(png);
		svc.HttpFetcher = MakeOkFetcher("image/png", truncated);
		IconResult r2 = await svc.GetAsync("modrinth", "mod", "STLV", CancellationToken.None);
		Assert.NotEqual(IconStatus.Ok, r2.Status);

		// 损坏的新响应不能覆盖已有健康 stale 缓存
		Assert.True(File.Exists(oldManifest), "旧 manifest 必须保留");
		Assert.Equal(oldBytes, File.ReadAllBytes(oldImage));
		// 第二次请求不命中损坏数据：命中的仍是旧健康缓存（stale）
		await Task.Delay(50);
		IconResult r3 = await svc.GetAsync("modrinth", "mod", "STLV", CancellationToken.None);
		Assert.NotEqual(IconStatus.Ok, r3.Status); // 仍走网络（未缓存损坏数据）；旧缓存文件原样保留
		Assert.Equal(oldBytes, File.ReadAllBytes(oldImage));
	}

	[Fact]
	public async Task GetAsync_CacheHitBytesStillFullyDecode_AndFileMatchesBytes()
	{
		// 缓存命中路径同样经过完整像素解码（TryReadCache 内调用解码器验证）。
		byte[] webp = LoadTestWebp64();
		ContentIconService svc = NewService(MakeOkFetcher("image/webp", webp));
		svc.RegisterTrustedUrl("modrinth", "mod", "CHIT", "https://cdn.modrinth.com/data/CHIT/icon.webp");
		IconResult r1 = await svc.GetAsync("modrinth", "mod", "CHIT", CancellationToken.None);
		Assert.Equal(IconStatus.Ok, r1.Status);
		byte[] cacheFile = File.ReadAllBytes(r1.CachePath!);
		Assert.Equal(webp, cacheFile);

		await Task.Delay(50);
		IconResult r2 = await svc.GetAsync("modrinth", "mod", "CHIT", CancellationToken.None);
		Assert.Equal(IconStatus.FromCache, r2.Status);
		Assert.Equal(webp, r2.Bytes); // 缓存命中后字节一致且可完整解码
	}

	// -------------------------------------------------------------------------
	//  第二轮最终返修 —— 五：LocalApiService.Stop 生命周期（先红后绿）
	// -------------------------------------------------------------------------

	[Fact]
	public void LocalApiService_Stop_WhenNotRunning_StillDisposesContentIconDefault()
	{
		// 返修五：LocalApiService 未运行时 Stop() 也必须释放 ContentIconService.Default。
		// 当前实现在 _running == false 时直接 return，DisposeDefault 永远不执行 → 必须失败。
		LocalApiService.Stop(); // 基线：确保处于未运行状态（幂等）
		Assert.False(LocalApiService.IsRunning);

		ContentIconService svc = ContentIconService.Default;
		svc.DnsResolver = FakeDns;
		svc.ConnectFactory = (_, _, _) => throw new InvalidOperationException("simulated connect failure");
		svc.RegisterTrustedUrl("modrinth", "mod", "LSTP", "https://cdn.modrinth.com/data/LSTP/icon.png");
		IconResult before = svc.GetAsync("modrinth", "mod", "LSTP", CancellationToken.None).GetAwaiter().GetResult();
		Assert.Equal(IconStatus.UpstreamError, before.Status);
		Assert.Equal(1, svc.HandlerBuildCount); // handler 已创建
		Assert.False(svc.TransportDisposed);

		// 未运行状态下调用 Stop：旧实例必须被释放
		LocalApiService.Stop();
		Assert.True(svc.TransportDisposed);

		// 连续调用不抛异常
		LocalApiService.Stop();
		LocalApiService.Stop();

		// 再次访问 Default 得到不同的新实例，且可正常工作、不复用已释放 handler
		ContentIconService svc2 = ContentIconService.Default;
		Assert.NotSame(svc, svc2);
		Assert.False(svc2.TransportDisposed);
		svc2.DnsResolver = FakeDns;
		svc2.ConnectFactory = (_, _, _) => throw new InvalidOperationException("simulated connect failure 2");
		svc2.RegisterTrustedUrl("modrinth", "mod", "LSTP2", "https://cdn.modrinth.com/data/LSTP2/icon.png");
		IconResult after = svc2.GetAsync("modrinth", "mod", "LSTP2", CancellationToken.None).GetAwaiter().GetResult();
		Assert.Equal(IconStatus.UpstreamError, after.Status);
		Assert.Equal(1, svc2.HandlerBuildCount);
		ContentIconService.DisposeDefault(); // 清理，避免影响其他测试
	}

	[Fact]
	public void LocalApiService_Stop_AfterStart_StillDisposesDefault_AndIdempotent()
	{
		// 运行状态下 Stop：listener 停止 + Default 释放；重复 Stop 不抛异常。
		LocalApiService.Stop(); // 基线
		ContentIconService svc = ContentIconService.Default;
		svc.DnsResolver = FakeDns;
		svc.ConnectFactory = (_, _, _) => throw new InvalidOperationException("cf");
		// A21（第 49 轮）：本用例直接 Start 服务，必须自己隔离三个存储根。
		Pineglow.Launcher.Tests.TestIsolationRoots.InstallCurrent(includeConfigStore: true);
		LocalApiService.Start(48999);
		try
		{
			Assert.True(LocalApiService.IsRunning);
			LocalApiService.Stop();
			Assert.False(LocalApiService.IsRunning);
			Assert.True(svc.TransportDisposed);
		}
		finally
		{
			Pineglow.Launcher.Tests.TestIsolationRoots.ReleaseCurrent();
			LocalApiService.Stop();
			LocalApiService.Stop();
			ContentIconService.DisposeDefault();
		}
	}

	// -------------------------------------------------------------------------
	//  截断样本构造（基于真实可解码资源做字节手术，保持结构自洽）
	// -------------------------------------------------------------------------

	private static readonly uint[] CrcTable = BuildCrcTable();
	private static uint[] BuildCrcTable()
	{
		uint[] table = new uint[256];
		for (uint n = 0; n < 256; n++)
		{
			uint c = n;
			for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
			table[n] = c;
		}
		return table;
	}

	private static uint Crc32(byte[] data, int offset, int count)
	{
		uint c = 0xFFFFFFFFu;
		for (int i = 0; i < count; i++) c = CrcTable[(c ^ data[offset + i]) & 0xFF] ^ (c >> 8);
		return c ^ 0xFFFFFFFFu;
	}

	/// <summary>PNG：IDAT payload 截断一半 + 重算 chunk CRC → 结构自洽、codec 可创建、像素流截断。</summary>
	private static byte[] MakePngTruncatedIdat(byte[] png)
	{
		using MemoryStream ms = new MemoryStream();
		ms.Write(png, 0, 8);
		int i = 8;
		bool truncatedIdat = false;
		while (i + 12 <= png.Length)
		{
			uint len = (uint)(png[i] << 24 | png[i + 1] << 16 | png[i + 2] << 8 | png[i + 3]);
			string type = Encoding.ASCII.GetString(png, i + 4, 4);
			byte[] payload = png[(i + 8)..(int)(i + 8 + len)];
			if (type == "IDAT" && !truncatedIdat)
			{
				payload = payload[..(payload.Length / 2)];
				truncatedIdat = true;
			}
			byte[] chunk = new byte[12 + payload.Length];
			chunk[0] = (byte)(payload.Length >> 24); chunk[1] = (byte)(payload.Length >> 16);
			chunk[2] = (byte)(payload.Length >> 8); chunk[3] = (byte)payload.Length;
			Encoding.ASCII.GetBytes(type).CopyTo(chunk, 4);
			payload.CopyTo(chunk, 8);
			uint crc = Crc32(chunk, 4, 4 + payload.Length);
			chunk[^4] = (byte)(crc >> 24); chunk[^3] = (byte)(crc >> 16); chunk[^2] = (byte)(crc >> 8); chunk[^1] = (byte)crc;
			ms.Write(chunk);
			if (type == "IEND") break;
			i += 12 + (int)len;
		}
		return ms.ToArray();
	}

	/// <summary>JPEG：删除所有指定 marker 段（如 DQT=0xDB）→ 结构自洽、codec 可创建、解码失败。</summary>
	private static byte[] MakeJpegWithoutSegments(byte[] jpg, byte markerByte)
	{
		using MemoryStream ms = new MemoryStream();
		ms.Write(jpg, 0, 2); // SOI
		int i = 2;
		bool removed = false;
		while (i + 4 <= jpg.Length)
		{
			if (jpg[i] != 0xFF) break;
			byte m = jpg[i + 1];
			if (m == 0xDA)
			{
				ms.Write(jpg, i, jpg.Length - i);
				break;
			}
			int segLen = (jpg[i + 2] << 8) | jpg[i + 3];
			if (m != markerByte) ms.Write(jpg, i, 2 + segLen);
			else removed = true;
			i += 2 + segLen;
		}
		Assert.True(removed, "样本缺少待删除段");
		return ms.ToArray();
	}

	/// <summary>JPEG：扫描数据（熵编码段）截断一半且不补 EOI → 结构校验必须拒绝。</summary>
	private static byte[] MakeJpegScanTruncatedNoEoi(byte[] jpg)
	{
		int i = 2;
		int sosHeaderEnd = -1;
		while (i + 4 <= jpg.Length)
		{
			if (jpg[i] != 0xFF) break;
			if (jpg[i + 1] == 0xDA)
			{
				int sosLen = (jpg[i + 2] << 8) | jpg[i + 3];
				sosHeaderEnd = i + 2 + sosLen;
				break;
			}
			int segLen = (jpg[i + 2] << 8) | jpg[i + 3];
			i += 2 + segLen;
		}
		Assert.True(sosHeaderEnd > 0, "样本缺少 SOS");
		int end = jpg.Length;
		for (int k = jpg.Length - 2; k > sosHeaderEnd; k--)
			if (jpg[k] == 0xFF && jpg[k + 1] == 0xD9) { end = k; break; }
		byte[] scan = jpg[sosHeaderEnd..end];
		using MemoryStream ms = new MemoryStream();
		ms.Write(jpg, 0, sosHeaderEnd);
		ms.Write(scan, 0, scan.Length / 2); // 不补 EOI
		return ms.ToArray();
	}

	/// <summary>WebP：VP8 payload 截断一半，重写 chunk size 与 riffSize 保持自洽。</summary>
	private static byte[] MakeWebpTruncatedVp8(byte[] webp)
	{
		long size = (long)(uint)(webp[16] | (webp[17] << 8) | (webp[18] << 16) | (webp[19] << 24));
		int keep = (int)(size / 2) & ~1;
		using MemoryStream ms = new MemoryStream();
		ms.Write(new byte[] { (byte)'R', (byte)'I', (byte)'F', (byte)'F' });
		long riff = 4 + 8 + keep;
		ms.Write(new byte[] { (byte)riff, (byte)(riff >> 8), (byte)(riff >> 16), (byte)(riff >> 24) });
		ms.Write(new byte[] { (byte)'W', (byte)'E', (byte)'B', (byte)'P' });
		ms.Write(new byte[] { (byte)'V', (byte)'P', (byte)'8', (byte)' ' });
		ms.Write(new byte[] { (byte)keep, (byte)(keep >> 8), (byte)(keep >> 16), (byte)(keep >> 24) });
		ms.Write(webp, 20, keep);
		return ms.ToArray();
	}

	private static string ExtractJsonString(string json, string key)
	{
		string pat = "\"" + key + "\":\"";
		int i = json.IndexOf(pat, StringComparison.Ordinal);
		if (i < 0) return string.Empty;
		int s = i + pat.Length;
		int e = json.IndexOf('"', s);
		return e > s ? json.Substring(s, e - s) : string.Empty;
	}

	private async Task AssertTruncatedRejectedAndNotCached(string pid, byte[] body, string mime, string ext)
	{
		// 截断样本必须：当前结构+解码双层校验下 BadImage；不生成 manifest；不生成版本图片；
		// 第二次请求不命中损坏缓存。
		Assert.False(ContentIconService.TryValidateImageWithDecoder(body, ext == ".png" ? "png" : ext == ".jpg" ? "jpg" : "webp"));
		ContentIconService svc = NewService(MakeOkFetcher(mime, body));
		svc.RegisterTrustedUrl("modrinth", "mod", pid, "https://cdn.modrinth.com/data/" + pid + "/icon" + ext);
		IconResult r = await svc.GetAsync("modrinth", "mod", pid, CancellationToken.None);
		Assert.Equal(IconStatus.BadImage, r.Status);
		AssertEmptyCacheDir("modrinth", "mod");

		await Task.Delay(50);
		IconResult r2 = await svc.GetAsync("modrinth", "mod", pid, CancellationToken.None);
		Assert.Equal(IconStatus.BadImage, r2.Status);
	}

	// -------------------------------------------------------------------------
	//  冒烟返修：/api/content/icon 必须接受 token 查询参数（<img> 无法携带 header）
	// -------------------------------------------------------------------------

	[Fact]
	public void RegisterTrustedUrl_CurseForgeForgecdnHost_NotRegistered_DeterministicDegradation()
	{
		// 收口八：确定性降级证据 —— 有搜索结果但图标 host 未授权：
		// forgecdn 未经验证（无真实 API 返回佐证），RegisterTrustedUrl 必须拒绝注册，
		// GetAsync 得到 NotFound（不联网），图标端点呈 404 → 前端占位。
		ContentIconService svc = NewService((_, _) => throw new InvalidOperationException("must not fetch"));
		svc.RegisterTrustedUrl("curseforge", "mod", "CF1", "https://media.forgecdn.net/files/1234/icon.png");
		Assert.False(svc.TryGetTrustedUrl("curseforge", "mod", "CF1", out string url));
		Assert.Equal(string.Empty, url);
		IconResult r = svc.GetAsync("curseforge", "mod", "CF1", CancellationToken.None).GetAwaiter().GetResult();
		Assert.Equal(IconStatus.NotFound, r.Status);
	}

	[Fact]
	public void Default_IconOfflineEnv_InjectsFailingTransport()
	{
		// 收口九（断网冒烟）：PINEGLOW_ICON_OFFLINE=1 时 Default 必须注入不可达 transport，
		// 仅影响图标下载（缓存仍可命中），绝不触碰系统网络配置；未设置时保持真实 transport。
		try
		{
			LocalApiService.Stop();
			ContentIconService.DisposeDefault();
			Environment.SetEnvironmentVariable(ContentIconService.IconOfflineEnvVar, "1");
			ContentIconService svc = ContentIconService.Default;
			Assert.NotNull(svc.HttpFetcher); // 红：当前实现忽略该环境变量
			ContentIconService.DisposeDefault();

			Environment.SetEnvironmentVariable(ContentIconService.IconOfflineEnvVar, null);
			ContentIconService svc2 = ContentIconService.Default;
			Assert.Null(svc2.HttpFetcher); // 未设置时保持真实 transport
		}
		finally
		{
			Environment.SetEnvironmentVariable(ContentIconService.IconOfflineEnvVar, null);
			ContentIconService.DisposeDefault();
		}
	}

	/// <summary>
	/// MG2 复验新增：让操作系统分配一个空闲端口。
	/// 不用"固定端口 + 线程 Id 派生"——那会在并行测试下撞端口（实测复现过）。
	/// </summary>
	private static int AcquireFreePortForTest()
	{
		for (int attempt = 0; attempt < 20; attempt++)
		{
			var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
			try
			{
				listener.Start();
				int port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
				listener.Stop();
				return port;
			}
			catch
			{
				try { listener.Stop(); } catch { }
			}
		}

		throw new InvalidOperationException("找不到可用端口");
	}

	[Fact]
	public async Task LocalApi_ContentIcon_AcceptsSessionTokenViaQuery_ForImgRequests()
	{
		// 真实入口：启动 LocalApiService 监听。<img src> 无法设置 X-Pineglow-Token 头，
		// UI 侧 buildContentIconUrl 按设计附带 token= 查询参数；端点必须接受之。
		// 当前实现仅对 /skin-image 接受查询参数 token → 图标请求恒 401（先红）。
		LocalApiService.Stop(); // 基线：确保未运行
		// MG2 复验发现并修掉：原写法 `49777 + (Environment.CurrentManagedThreadId % 100)` 会撞——
		// 线程池里 id 相差 100 的两个线程会派生出**同一个端口**，实测全量跑时
		// HttpListener.Start 抛"另一个程序正在使用此文件"。改为让操作系统分配空闲端口，
		// 与 AiCancelAndRedirectTests / NewsLinkHandlerTests / NewsFakeIpConfigTests 的做法一致。
		// A21（第 49 轮）：同上。
		Pineglow.Launcher.Tests.TestIsolationRoots.InstallCurrent(includeConfigStore: true);
		int port = AcquireFreePortForTest();
		LocalApiService.Start(port);
		try
		{
			Assert.True(LocalApiService.IsRunning);
			string token = LocalApiService.SessionTokenForTests;
			using HttpClient http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };

			// 错误 token → 401（认证仍然把守）
			HttpResponseMessage bad = await http.GetAsync(
				$"http://127.0.0.1:{port}/api/content/icon?source=modrinth&category=mod&projectId=AANobbMI&token=0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef");
			Assert.Equal(HttpStatusCode.Unauthorized, bad.StatusCode);

			// 正确 query token → 认证必须通过（未缓存图标为 NotFound 语义，而不是 401）
			HttpResponseMessage good = await http.GetAsync(
				$"http://127.0.0.1:{port}/api/content/icon?source=modrinth&category=mod&projectId=AANobbMI&token={token}");
			Assert.NotEqual(HttpStatusCode.Unauthorized, good.StatusCode);

			// header 路径依旧有效
			using HttpRequestMessage byHeader = new HttpRequestMessage(HttpMethod.Get,
				$"http://127.0.0.1:{port}/api/content/icon?source=modrinth&category=mod&projectId=AANobbMI");
			byHeader.Headers.Add("X-Pineglow-Token", token);
			HttpResponseMessage hdrResp = await http.SendAsync(byHeader);
			Assert.NotEqual(HttpStatusCode.Unauthorized, hdrResp.StatusCode);
		}
		finally
		{
			Pineglow.Launcher.Tests.TestIsolationRoots.ReleaseCurrent();
			LocalApiService.Stop();
			ContentIconService.DisposeDefault();
		}
	}

	// -------------------------------------------------------------------------
	//  第二轮最终返修 —— 七：孤立文件识别（真实命名协议）
	// -------------------------------------------------------------------------

	private const string Hash64 = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"; // 64 hex

	[Theory]
	[InlineData(Hash64 + ".json", ContentIconService.CacheFileKind.Manifest)]
	[InlineData(Hash64 + ".json.tmp-deadbeef", ContentIconService.CacheFileKind.ManifestTemp)]
	[InlineData(Hash64 + ".json.tmp-0123abcdef4567890123abcdef45678901", ContentIconService.CacheFileKind.ManifestTemp)]
	[InlineData(Hash64 + ".json.corrupt-1234567890", ContentIconService.CacheFileKind.QuarantinedManifest)]
	[InlineData(Hash64 + ".png.v638123456789012345", ContentIconService.CacheFileKind.VersionedImage)]
	[InlineData(Hash64 + ".webp.v638123456789012345", ContentIconService.CacheFileKind.VersionedImage)]
	[InlineData(Hash64 + ".jpg.v638123456789012345", ContentIconService.CacheFileKind.VersionedImage)]
	[InlineData(Hash64 + ".png.v638123456789012345.corrupt-1234567890", ContentIconService.CacheFileKind.QuarantinedImage)]
	[InlineData(".tmp-0123abcdef4567890123abcdef45678901", ContentIconService.CacheFileKind.DownloadTemp)]
	public void TryClassifyCacheFileName_ParsesRealNamingProtocol(string name, ContentIconService.CacheFileKind kind)
	{
		Assert.True(ContentIconService.TryClassifyCacheFileName(name, out ContentIconService.CacheFileNameInfo info), name);
		Assert.Equal(kind, info.Kind);
	}

	[Theory]
	[InlineData("notes.v2.txt")]                       // 偶然包含 .v 的无关文件
	[InlineData("abc.v123")]
	[InlineData(Hash64 + ".png.v")]                    // 空版本数字
	[InlineData(Hash64 + ".png.v12a")]                 // 版本数字含非数字
	[InlineData(Hash64 + ".png.v-5")]                  // 负版本
	[InlineData(Hash64 + ".jpeg.v123")]                // 扩展名不在白名单
	[InlineData(Hash64 + ".json.tmp-")]                // 空后缀
	[InlineData(Hash64 + ".json.tmp-dead beef")]       // 后缀含非 hex
	[InlineData("short.json")]                         // hash 不是 64 hex
	[InlineData(".tmp-xyz")]                           // 下载临时后缀过短
	[InlineData("")]
	[InlineData("randomfile")]
	public void TryClassifyCacheFileName_RejectsNonProtocolNames(string name)
	{
		Assert.False(ContentIconService.TryClassifyCacheFileName(name, out _), name);
	}

	[Fact]
	public void TryClassifyCacheFileName_ValidatesHashExtVersionAndSuffix()
	{
		Assert.True(ContentIconService.TryClassifyCacheFileName(Hash64 + ".png.v638123456789012345", out ContentIconService.CacheFileNameInfo info));
		Assert.Equal(Hash64, info.Hash);
		Assert.Equal("png", info.Ext);
		Assert.Equal(638123456789012345L, info.VersionTicks);
		Assert.True(ContentIconService.TryClassifyCacheFileName(Hash64 + ".json.corrupt-1720000000", out ContentIconService.CacheFileNameInfo q));
		Assert.Equal("1720000000", q.Suffix);
	}

	[Fact]
	public void EnforceCacheBudget_RemovesAgedManifestTempFile()
	{
		CacheEntryInfo e = WriteCacheEntry("OR1", LoadTestPng64());
		string tmp = Path.Combine(e.Dir, e.UrlHash + ".json.tmp-deadbeef");
		File.WriteAllText(tmp, "{\"formatVersion\":2}");
		ContentIconService svc = NewService();
		svc.WallClock = () => DateTime.UtcNow + TimeSpan.FromMinutes(90); // 超过 1 小时宽限期
		svc.EnforceCacheBudget();
		Assert.False(File.Exists(tmp), "超过宽限期的 manifest 临时文件应删除");
		Assert.True(File.Exists(e.ManifestPath), "健康 manifest 不应删除");
	}

	[Fact]
	public void EnforceCacheBudget_RemovesAgedQuarantinedFiles()
	{
		CacheEntryInfo e = WriteCacheEntry("OR2", LoadTestPng64());
		string qm = Path.Combine(e.Dir, e.UrlHash + ".json.corrupt-1234567890");
		string qi = Path.Combine(e.Dir, e.UrlHash + ".png.v111.corrupt-1234567890");
		File.WriteAllText(qm, "junk");
		File.WriteAllBytes(qi, new byte[] { 1 });
		ContentIconService svc = NewService();
		svc.WallClock = () => DateTime.UtcNow + TimeSpan.FromMinutes(90);
		svc.EnforceCacheBudget();
		Assert.False(File.Exists(qm), "超过宽限期的隔离 manifest 应删除");
		Assert.False(File.Exists(qi), "超过宽限期的隔离图片应删除");
	}

	[Fact]
	public void EnforceCacheBudget_KeepsFilesWithinGracePeriod()
	{
		CacheEntryInfo e = WriteCacheEntry("OR3", LoadTestPng64());
		string tmp = Path.Combine(e.Dir, e.UrlHash + ".json.tmp-deadbeef");
		File.WriteAllText(tmp, "x");
		ContentIconService svc = NewService();
		svc.WallClock = () => DateTime.UtcNow; // 宽限期内
		svc.EnforceCacheBudget();
		Assert.True(File.Exists(tmp), "宽限期内文件不应删除");
	}

	[Fact]
	public void EnforceCacheBudget_KeepsManifestReferencedVersionImage()
	{
		// manifest 当前引用的版本图片即使超过宽限期也不删除
		CacheEntryInfo e = WriteCacheEntry("OR4", LoadTestPng64());
		ContentIconService svc = NewService();
		svc.WallClock = () => DateTime.UtcNow + TimeSpan.FromMinutes(90);
		svc.EnforceCacheBudget();
		Assert.True(File.Exists(e.ImagePath), "manifest 引用的图片不应删除");
		Assert.True(File.Exists(e.ManifestPath));
	}

	[Fact]
	public void EnforceCacheBudget_DoesNotTouchOtherSourceCategoryCaches()
	{
		CacheEntryInfo a = WriteCacheEntry("OR5", LoadTestPng64());
		CacheEntryInfo b = WriteCacheEntry("OR5B", LoadTestPng64(), source: "curseforge");
		CacheEntryInfo c = WriteCacheEntry("OR5C", LoadTestPng64(), category: "shader");
		ContentIconService svc = NewService();
		svc.WallClock = () => DateTime.UtcNow + TimeSpan.FromMinutes(90);
		svc.EnforceCacheBudget();
		Assert.True(File.Exists(a.ImagePath) && File.Exists(a.ManifestPath));
		Assert.True(File.Exists(b.ImagePath) && File.Exists(b.ManifestPath));
		Assert.True(File.Exists(c.ImagePath) && File.Exists(c.ManifestPath));
	}

	[Fact]
	public void EnforceCacheBudget_DoesNotDeleteUnrelatedDotVFiles()
	{
		CacheEntryInfo e = WriteCacheEntry("OR6", LoadTestPng64());
		string unrelated1 = Path.Combine(e.Dir, "notes.v2.txt");
		string unrelated2 = Path.Combine(e.Dir, e.UrlHash + ".png.v9x"); // 版本数字非法 → Unrelated
		File.WriteAllText(unrelated1, "x");
		File.WriteAllText(unrelated2, "x");
		ContentIconService svc = NewService();
		svc.WallClock = () => DateTime.UtcNow + TimeSpan.FromMinutes(90);
		svc.EnforceCacheBudget();
		Assert.True(File.Exists(unrelated1), "名称偶然包含 .v 的无关文件不应误删");
		Assert.True(File.Exists(unrelated2), "不合法版本数字的文件不应误删");
	}

	[Fact]
	public void EnforceCacheBudget_RemovesAgedDownloadTempFiles()
	{
		string tmp = Path.Combine(_cacheRoot, ".tmp-" + Guid.NewGuid().ToString("N"));
		File.WriteAllText(tmp, "partial download");
		ContentIconService svc = NewService();
		svc.WallClock = () => DateTime.UtcNow + TimeSpan.FromMinutes(90);
		svc.EnforceCacheBudget();
		Assert.False(File.Exists(tmp), "超过宽限期的下载临时文件应删除");
	}

	[Fact]
	public async Task CleanupDuringCommit_DeletingAgedManifestTmp_DoesNotBreakHealthyCache()
	{
		// 清理与 CommitCache 并发：正在提交的 manifest 临时文件被（回溯时间的）清理删除
		// → 提交安全失败（NetworkError），旧健康缓存保持一致，绝不出现 manifest 指向缺失图片。
		byte[] png = LoadTestPng64();
		byte[] png2 = LoadTestWebp64(); // 第二次用不同字节触发新提交
		long fakeNow = DateTimeOffset.UtcNow.UtcTicks;
		ContentIconService svc = NewService((_, __) => Task.FromResult(MakeOk(png)), () => fakeNow);
		svc.RegisterTrustedUrl("modrinth", "mod", "CNC1", "https://cdn.modrinth.com/data/CNC1/icon.png");
		IconResult r1 = await svc.GetAsync("modrinth", "mod", "CNC1", CancellationToken.None);
		Assert.Equal(IconStatus.Ok, r1.Status);
		string dir = Path.Combine(_cacheRoot, "modrinth", "mod");
		string urlHash = ContentIconService.ComputeCacheKey("modrinth", "mod", "CNC1", "https://cdn.modrinth.com/data/CNC1/icon.png");
		string oldImageName = ExtractJsonString(File.ReadAllText(Path.Combine(dir, urlHash + ".json")), "image");

		// 第二次提交：暂停在 manifest 原子切换前。
		// Task.Yield 强制提交流程在线程池上继续，回调阻塞不会占用测试线程。
		TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
		TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
		string? committedTmpPath = null;
		svc.HttpFetcher = async (_, __) =>
		{
			await Task.Yield();
			return MakeOk(png2, "image/webp");
		};
		svc.BeforeManifestCommit = (tmpPath, _) =>
		{
			committedTmpPath = tmpPath;
			entered.TrySetResult(); // 信号：请求已进入 manifest 原子切换前的悬挂点
			release.Task.Wait(TimeSpan.FromSeconds(15));
		};
		fakeNow += TimeSpan.FromDays(8).Ticks;
		await Task.Delay(50);
		svc.RegisterTrustedUrl("modrinth", "mod", "CNC1", "https://cdn.modrinth.com/data/CNC1/icon.png");
		Task<IconResult> commit = svc.GetAsync("modrinth", "mod", "CNC1", CancellationToken.None);
		await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));

		// 并发清理：把正在提交的 manifest 临时文件时间回溯到宽限期之外
		Assert.NotNull(committedTmpPath);
		Assert.True(File.Exists(committedTmpPath!), "悬挂点处 manifest 临时文件必须已写入");
		string committedTmp = committedTmpPath!;
		File.SetLastWriteTimeUtc(committedTmp, DateTime.UtcNow - TimeSpan.FromHours(2));
		svc.WallClock = () => DateTime.UtcNow;
		svc.EnforceCacheBudget();
		Assert.False(File.Exists(committedTmp));

		release.TrySetResult();
		IconResult r2 = await commit.WaitAsync(TimeSpan.FromSeconds(15));
		Assert.Equal(IconStatus.NetworkError, r2.Status); // 提交安全失败

		// 旧 manifest 仍指向旧图片，目录内不存在指向缺失图片的 manifest
		string manifestPath = Path.Combine(dir, urlHash + ".json");
		Assert.True(File.Exists(manifestPath));
		Assert.Equal(oldImageName, ExtractJsonString(File.ReadAllText(manifestPath), "image"));
		Assert.True(File.Exists(Path.Combine(dir, oldImageName)));
		foreach (string mf in Directory.GetFiles(dir, "*.json"))
		{
			string img = ExtractJsonString(File.ReadAllText(mf), "image");
			Assert.True(File.Exists(Path.Combine(dir, img)), $"manifest {mf} 指向缺失图片 {img}");
		}
		// 旧缓存仍可访问（fakeNow 已 +8 天 → stale → 触发重抓，此时故障点已解除、提交成功 → Ok）
		await Task.Delay(50);
		svc.RegisterTrustedUrl("modrinth", "mod", "CNC1", "https://cdn.modrinth.com/data/CNC1/icon.png");
		IconResult r3 = await svc.GetAsync("modrinth", "mod", "CNC1", CancellationToken.None);
		Assert.Equal(IconStatus.Ok, r3.Status);
		foreach (string mf in Directory.GetFiles(dir, "*.json"))
		{
			string img = ExtractJsonString(File.ReadAllText(mf), "image");
			Assert.True(File.Exists(Path.Combine(dir, img)), $"最终 manifest {mf} 指向缺失图片 {img}");
		}
	}

	// -------------------------------------------------------------------------
	//  第二轮最终返修 —— 八：manifest 行为测试矩阵（真实缓存读取路径）
	// -------------------------------------------------------------------------

	private static string BuildManifestJsonForTest(string urlHash, string image, string mime, long length, long ticks,
		string? etag = null, string? lastModified = null)
	{
		string json = "{\"formatVersion\":2,\"urlHash\":\"" + urlHash + "\",\"image\":\"" + image
			+ "\",\"mime\":\"" + mime + "\",\"length\":" + length + ",\"writtenAtTicks\":" + ticks;
		if (etag != null) json += ",\"etag\":" + JsonSerializer.Serialize(etag);
		if (lastModified != null) json += ",\"lastModified\":" + JsonSerializer.Serialize(lastModified);
		return json + "}";
	}

	private sealed record CacheEntryInfo(string Dir, string UrlHash, string ManifestPath, string ImagePath, string Url, string ProjectId);

	private CacheEntryInfo WriteCacheEntry(string pid, byte[] png, string? manifestJson = null,
		long? writtenAtTicks = null, string source = "modrinth", string category = "mod")
	{
		string url = $"https://cdn.modrinth.com/data/{pid}/icon.png";
		string urlHash = ContentIconService.ComputeCacheKey(source, category, pid, url);
		string dir = Path.Combine(_cacheRoot, source.ToLowerInvariant(), category.ToLowerInvariant());
		Directory.CreateDirectory(dir);
		long ticks = writtenAtTicks ?? DateTimeOffset.UtcNow.UtcTicks;
		string imageName = urlHash + ".png.v" + ticks;
		string imagePath = Path.Combine(dir, imageName);
		File.WriteAllBytes(imagePath, png);
		string manifestPath = Path.Combine(dir, urlHash + ".json");
		File.WriteAllText(manifestPath,
			manifestJson ?? BuildManifestJsonForTest(urlHash, imageName, "image/png", png.LongLength, ticks),
			new UTF8Encoding(false));
		return new CacheEntryInfo(dir, urlHash, manifestPath, imagePath, url, pid);
	}

	/// <summary>写入缓存条目，manifest JSON 由工厂基于 (urlHash, imageName, ticks) 生成（避免初始化器自引用）。</summary>
	private CacheEntryInfo WriteCacheEntryFactory(string pid, byte[] png,
		Func<string, string, long, string> jsonFactory, string source = "modrinth", string category = "mod")
	{
		string url = $"https://cdn.modrinth.com/data/{pid}/icon.png";
		string urlHash = ContentIconService.ComputeCacheKey(source, category, pid, url);
		string dir = Path.Combine(_cacheRoot, source.ToLowerInvariant(), category.ToLowerInvariant());
		Directory.CreateDirectory(dir);
		long ticks = DateTimeOffset.UtcNow.UtcTicks;
		string imageName = urlHash + ".png.v" + ticks;
		string imagePath = Path.Combine(dir, imageName);
		File.WriteAllBytes(imagePath, png);
		string manifestPath = Path.Combine(dir, urlHash + ".json");
		File.WriteAllText(manifestPath, jsonFactory(urlHash, imageName, ticks), new UTF8Encoding(false));
		return new CacheEntryInfo(dir, urlHash, manifestPath, imagePath, url, pid);
	}

	/// <summary>损坏 manifest：GetAsync 必须重新抓取成功（网络计数），且旧 manifest 被隔离为 .corrupt-*。</summary>
	private async Task AssertCorruptManifestRefetchedAndQuarantined(CacheEntryInfo e, byte[] png)
	{
		int fetchCount = 0;
		ContentIconService svc = NewService((_, __) =>
		{
			Interlocked.Increment(ref fetchCount);
			return Task.FromResult(MakeOk(png));
		});
		svc.RegisterTrustedUrl("modrinth", "mod", e.ProjectId, e.Url);
		IconResult r = await svc.GetAsync("modrinth", "mod", e.ProjectId, CancellationToken.None);
		Assert.Equal(IconStatus.Ok, r.Status);
		Assert.Equal(1, fetchCount); // 损坏 manifest 不命中缓存，必须真实联网重抓
		Assert.Contains(Directory.GetFiles(e.Dir, e.UrlHash + ".json.corrupt-*"), _ => true); // 旧 manifest 被隔离
	}

	[Fact]
	public async Task Manifest_FieldOrderChanged_StillCacheHit()
	{
		byte[] png01 = LoadTestPng64();
		CacheEntryInfo e = WriteCacheEntryFactory("MF01", png01, (urlHash, imageName, ticks) =>
			"{\"writtenAtTicks\":" + ticks + ",\"length\":" + png01.LongLength
			+ ",\"mime\":\"image/png\",\"image\":\"" + imageName
			+ "\",\"urlHash\":\"" + urlHash
			+ "\",\"formatVersion\":2}");
		ContentIconService svc = NewService((_, __) => throw new InvalidOperationException("should not fetch"));
		svc.RegisterTrustedUrl("modrinth", "mod", e.ProjectId, e.Url);
		IconResult r = await svc.GetAsync("modrinth", "mod", e.ProjectId, CancellationToken.None);
		Assert.Equal(IconStatus.FromCache, r.Status);
	}

	[Fact]
	public async Task Manifest_UnknownField_StillCacheHit()
	{
		byte[] png = LoadTestPng64();
		CacheEntryInfo e = WriteCacheEntry("MF02", png);
		string json = File.ReadAllText(e.ManifestPath);
		File.WriteAllText(e.ManifestPath, json[..^1] + ",\"futureField\":{\"nested\":[1,2,3]}}");
		ContentIconService svc = NewService((_, __) => throw new InvalidOperationException("should not fetch"));
		svc.RegisterTrustedUrl("modrinth", "mod", e.ProjectId, e.Url);
		IconResult r = await svc.GetAsync("modrinth", "mod", e.ProjectId, CancellationToken.None);
		Assert.Equal(IconStatus.FromCache, r.Status);
	}

	[Fact]
	public async Task Manifest_MissingFormatVersion_RefetchAndQuarantine()
	{
		byte[] png = LoadTestPng64();
		CacheEntryInfo e = WriteCacheEntryFactory("MF03", png, (urlHash, imageName, ticks) =>
			BuildManifestJsonForTest(urlHash, imageName, "image/png", png.LongLength, ticks)
			.Replace("\"formatVersion\":2,", ""));
		await AssertCorruptManifestRefetchedAndQuarantined(e, png);
	}

	[Fact]
	public async Task Manifest_MissingUrlHash_RefetchAndQuarantine()
	{
		byte[] png = LoadTestPng64();
		CacheEntryInfo e = WriteCacheEntryFactory("MF04", png, (urlHash, imageName, ticks) =>
			BuildManifestJsonForTest(urlHash, imageName, "image/png", png.LongLength, ticks)
			.Replace("\"urlHash\":\"" + urlHash + "\",", ""));
		await AssertCorruptManifestRefetchedAndQuarantined(e, png);
	}

	[Fact]
	public async Task Manifest_MissingImage_RefetchAndQuarantine()
	{
		byte[] png = LoadTestPng64();
		CacheEntryInfo e = WriteCacheEntryFactory("MF05", png, (urlHash, imageName, ticks) =>
			BuildManifestJsonForTest(urlHash, "", "image/png", png.LongLength, ticks));
		await AssertCorruptManifestRefetchedAndQuarantined(e, png);
	}

	[Fact]
	public async Task Manifest_MissingMime_RefetchAndQuarantine()
	{
		byte[] png = LoadTestPng64();
		CacheEntryInfo e = WriteCacheEntryFactory("MF06", png, (urlHash, imageName, ticks) =>
			BuildManifestJsonForTest(urlHash, imageName, "", png.LongLength, ticks));
		await AssertCorruptManifestRefetchedAndQuarantined(e, png);
	}

	[Fact]
	public async Task Manifest_MissingLength_RefetchAndQuarantine()
	{
		byte[] png = LoadTestPng64();
		CacheEntryInfo e = WriteCacheEntryFactory("MF07", png, (urlHash, imageName, ticks) =>
			BuildManifestJsonForTest(urlHash, imageName, "image/png", 0, DateTimeOffset.UtcNow.UtcTicks));
		await AssertCorruptManifestRefetchedAndQuarantined(e, png);
	}

	[Fact]
	public async Task Manifest_MissingWrittenAtTicks_RefetchAndQuarantine()
	{
		byte[] png = LoadTestPng64();
		CacheEntryInfo e = WriteCacheEntryFactory("MF08", png, (urlHash, imageName, ticks) =>
			BuildManifestJsonForTest(urlHash, imageName, "image/png", png.LongLength, 0));
		await AssertCorruptManifestRefetchedAndQuarantined(e, png);
	}

	[Fact]
	public async Task Manifest_FieldTypeMismatch_RefetchAndQuarantine()
	{
		byte[] png = LoadTestPng64();
		CacheEntryInfo e = WriteCacheEntryFactory("MF09", png, (urlHash, imageName, ticks) =>
			BuildManifestJsonForTest(urlHash, imageName, "image/png", png.LongLength, ticks)
			.Replace("\"length\":" + png.LongLength, "\"length\":\"not-a-number\""));
		await AssertCorruptManifestRefetchedAndQuarantined(e, png);
	}

	[Fact]
	public async Task Manifest_TruncatedJson_RefetchAndQuarantine()
	{
		byte[] png = LoadTestPng64();
		CacheEntryInfo e = WriteCacheEntry("MF10", png);
		string json = File.ReadAllText(e.ManifestPath);
		File.WriteAllText(e.ManifestPath, json[..(json.Length / 2)]);
		await AssertCorruptManifestRefetchedAndQuarantined(e, png);
	}

	[Fact]
	public async Task Manifest_ImageContainsDotDot_RefetchAndQuarantine()
	{
		byte[] png = LoadTestPng64();
		CacheEntryInfo e = WriteCacheEntryFactory("MF11", png, (urlHash, imageName, ticks) =>
			BuildManifestJsonForTest(urlHash, "..\\evil.png", "image/png", png.LongLength, ticks));
		await AssertCorruptManifestRefetchedAndQuarantined(e, png);
	}

	[Fact]
	public async Task Manifest_ImageContainsDirectorySeparator_RefetchAndQuarantine()
	{
		byte[] png = LoadTestPng64();
		CacheEntryInfo e = WriteCacheEntryFactory("MF12", png, (urlHash, imageName, ticks) =>
			BuildManifestJsonForTest(urlHash, "sub\\dir\\evil.png", "image/png", png.LongLength, ticks));
		await AssertCorruptManifestRefetchedAndQuarantined(e, png);
	}

	[Fact]
	public async Task Manifest_ImageAbsolutePath_RefetchAndQuarantine()
	{
		byte[] png = LoadTestPng64();
		CacheEntryInfo e = WriteCacheEntryFactory("MF13", png, (urlHash, imageName, ticks) =>
			BuildManifestJsonForTest(urlHash, "C:\\windows\\evil.png", "image/png", png.LongLength, ticks));
		await AssertCorruptManifestRefetchedAndQuarantined(e, png);
	}

	[Fact]
	public async Task Manifest_ImageHashMismatch_RefetchAndQuarantine()
	{
		byte[] png = LoadTestPng64();
		string otherHash = ContentIconService.ComputeCacheKey("modrinth", "mod", "OTHER", "https://cdn.modrinth.com/data/OTHER/icon.png");
		CacheEntryInfo e2 = WriteCacheEntryFactory("MF14B", png, (urlHash, imageName, ticks) =>
			BuildManifestJsonForTest(urlHash, otherHash + ".png.v123", "image/png", png.LongLength, ticks));
		await AssertCorruptManifestRefetchedAndQuarantined(e2, png);
	}

	[Fact]
	public async Task Manifest_NegativeLength_RefetchAndQuarantine()
	{
		byte[] png = LoadTestPng64();
		CacheEntryInfo e = WriteCacheEntryFactory("MF15", png, (urlHash, imageName, ticks) =>
			BuildManifestJsonForTest(urlHash, imageName, "image/png", -1, DateTimeOffset.UtcNow.UtcTicks));
		await AssertCorruptManifestRefetchedAndQuarantined(e, png);
	}

	[Fact]
	public async Task Manifest_LengthOverflow_RefetchAndQuarantine()
	{
		byte[] png = LoadTestPng64();
		CacheEntryInfo e = WriteCacheEntryFactory("MF16", png, (urlHash, imageName, ticks) =>
			BuildManifestJsonForTest(urlHash, imageName, "image/png", long.MaxValue, DateTimeOffset.UtcNow.UtcTicks));
		await AssertCorruptManifestRefetchedAndQuarantined(e, png);
	}

	[Fact]
	public async Task Manifest_LengthMismatchWithRealFile_RefetchAndQuarantine()
	{
		byte[] png = LoadTestPng64();
		CacheEntryInfo e = WriteCacheEntryFactory("MF17", png, (urlHash, imageName, ticks) =>
			BuildManifestJsonForTest(urlHash, imageName, "image/png", png.LongLength + 1, DateTimeOffset.UtcNow.UtcTicks));
		await AssertCorruptManifestRefetchedAndQuarantined(e, png);
	}

	[Fact]
	public async Task Manifest_NegativeWrittenAtTicks_RefetchAndQuarantine()
	{
		byte[] png = LoadTestPng64();
		CacheEntryInfo e = WriteCacheEntryFactory("MF18", png, (urlHash, imageName, ticks) =>
			BuildManifestJsonForTest(urlHash, imageName, "image/png", png.LongLength, -1000));
		await AssertCorruptManifestRefetchedAndQuarantined(e, png);
	}

	[Fact]
	public async Task Manifest_WrittenAtTicksUnreasonableFuture_RefetchAndQuarantine()
	{
		byte[] png = LoadTestPng64();
		long future = DateTimeOffset.UtcNow.UtcTicks + TimeSpan.FromDays(30).Ticks;
		CacheEntryInfo e = WriteCacheEntryFactory("MF19", png, (urlHash, imageName, ticks) =>
			BuildManifestJsonForTest(urlHash, imageName, "image/png", png.LongLength, future));
		await AssertCorruptManifestRefetchedAndQuarantined(e, png);
	}

	[Theory]
	[InlineData(1)]
	[InlineData(3)]
	public async Task Manifest_WrongFormatVersion_RefetchAndQuarantine(int version)
	{
		byte[] png = LoadTestPng64();
		CacheEntryInfo e = WriteCacheEntryFactory("MF20", png, (urlHash, imageName, ticks) =>
			BuildManifestJsonForTest(urlHash, imageName, "image/png", png.LongLength, ticks)
			.Replace("\"formatVersion\":2", "\"formatVersion\":" + version));
		await AssertCorruptManifestRefetchedAndQuarantined(e, png);
	}

	[Fact]
	public async Task Manifest_EtagAndLastModifiedWithEscapableChars_RoundTripThroughRealPath()
	{
		// ETag / Last-Modified 含引号、反斜杠、Unicode → 写入与读取都必须正确转义
		string etag = "w/\"q\\b--é中\"";
		string lastModified = "Wed, 21 Oct 2015 07:28:00 GMT; tag=\"a\\b\"";
		byte[] png = LoadTestPng64();
		ContentIconService svc = NewService((_, __) =>
		{
			HttpResponseMessage m = MakeOk(png);
			m.Headers.ETag = new System.Net.Http.Headers.EntityTagHeaderValue("\"" + etag.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"");
			// Last-Modified 是实体头：模拟真实 CDN 放在 Content.Headers
			m.Content.Headers.TryAddWithoutValidation("Last-Modified", lastModified);
			return Task.FromResult(m);
		});
		svc.RegisterTrustedUrl("modrinth", "mod", "MF21", "https://cdn.modrinth.com/data/MF21/icon.png");
		IconResult r1 = await svc.GetAsync("modrinth", "mod", "MF21", CancellationToken.None);
		Assert.Equal(IconStatus.Ok, r1.Status);
		string storedEtag = r1.ETag!;
		Assert.Contains("\"", storedEtag);
		Assert.Contains("\\", storedEtag);

		string dir = Path.Combine(_cacheRoot, "modrinth", "mod");
		string urlHash = ContentIconService.ComputeCacheKey("modrinth", "mod", "MF21", "https://cdn.modrinth.com/data/MF21/icon.png");
		string rawJson = File.ReadAllText(Path.Combine(dir, urlHash + ".json"));
		// 默认编码器把值内引号/反斜杠转义（" / \ 形式）；用 JsonDocument 解析验证往返
		using JsonDocument doc = JsonDocument.Parse(rawJson); // 必须仍是合法 JSON
		string parsedEtag = doc.RootElement.GetProperty("eTag").GetString()!;
		string parsedLastModified = doc.RootElement.TryGetProperty("lastModified", out JsonElement lmEl) && lmEl.ValueKind != JsonValueKind.Null
			? lmEl.GetString()! : string.Empty;

		await Task.Delay(50);
		svc.RegisterTrustedUrl("modrinth", "mod", "MF21", "https://cdn.modrinth.com/data/MF21/icon.png");
		IconResult r2 = await svc.GetAsync("modrinth", "mod", "MF21", CancellationToken.None);
		Assert.Equal(IconStatus.FromCache, r2.Status);
		Assert.Equal(storedEtag, r2.ETag); // 转义往返一致
		Assert.Equal(storedEtag, parsedEtag);
		Assert.Equal(lastModified, parsedLastModified);
		Assert.Equal(lastModified, r2.LastModified);
	}

	[Fact]
	public async Task Manifest_OneCorruptEntry_IsolatesItselfOnly()
	{
		byte[] png = LoadTestPng64();
		CacheEntryInfo healthy = WriteCacheEntry("MF22A", png);
		CacheEntryInfo corrupt = WriteCacheEntry("MF22B", png, manifestJson: "{not-json");
		ContentIconService svc = NewService(MakeOkFetcher("image/png", png));
		svc.RegisterTrustedUrl("modrinth", "mod", healthy.ProjectId, healthy.Url);
		svc.RegisterTrustedUrl("modrinth", "mod", corrupt.ProjectId, corrupt.Url);

		IconResult rHealthy = await svc.GetAsync("modrinth", "mod", healthy.ProjectId, CancellationToken.None);
		Assert.Equal(IconStatus.FromCache, rHealthy.Status); // 健康条目不受影响
		IconResult rCorrupt = await svc.GetAsync("modrinth", "mod", corrupt.ProjectId, CancellationToken.None);
		Assert.Equal(IconStatus.Ok, rCorrupt.Status);        // 损坏条目重新抓取

		Assert.True(File.Exists(healthy.ManifestPath) && File.Exists(healthy.ImagePath), "健康条目必须原样保留");
		// 损坏条目重抓后 manifest 被重建（指向新图片）；旧损坏 manifest 已被隔离
		Assert.Contains(Directory.GetFiles(corrupt.Dir, corrupt.UrlHash + ".json.corrupt-*"), _ => true);
		string rebuiltImage = ExtractJsonString(File.ReadAllText(corrupt.ManifestPath), "image");
		Assert.True(File.Exists(Path.Combine(corrupt.Dir, rebuiltImage)), "重建后的 manifest 必须指向存在的图片");
	}

	// -------------------------------------------------------------------------
	//  第二轮最终返修 —— 九：缓存预算逻辑条目测试
	// -------------------------------------------------------------------------

	private void SetEntryTimestamps(CacheEntryInfo e, DateTime utc)
	{
		File.SetLastWriteTimeUtc(e.ManifestPath, utc);
		File.SetLastWriteTimeUtc(e.ImagePath, utc);
	}

	[Fact]
	public void EnforceCacheBudget_EvictsOldestLogicalEntryWhenOverBudget()
	{
		byte[] png = LoadTestPng64();
		CacheEntryInfo oldest = WriteCacheEntry("BG01A", png);
		CacheEntryInfo middle = WriteCacheEntry("BG01B", png);
		CacheEntryInfo newest = WriteCacheEntry("BG01C", png);
		DateTime baseTime = DateTime.UtcNow - TimeSpan.FromDays(3);
		SetEntryTimestamps(oldest, baseTime);
		SetEntryTimestamps(middle, baseTime + TimeSpan.FromDays(1));
		SetEntryTimestamps(newest, baseTime + TimeSpan.FromDays(2));

		ContentIconService svc = NewService();
		long budget = new FileInfo(oldest.ManifestPath).Length + new FileInfo(oldest.ImagePath).Length
			+ new FileInfo(middle.ManifestPath).Length + new FileInfo(middle.ImagePath).Length
			+ new FileInfo(newest.ManifestPath).Length + new FileInfo(newest.ImagePath).Length - 1;
		svc.CacheBudgetBytesOverride = budget; // 超 1 字节 → 必须淘汰最旧
		svc.EnforceCacheBudget();

		Assert.False(File.Exists(oldest.ManifestPath), "最旧逻辑条目的 manifest 应删除");
		Assert.False(File.Exists(oldest.ImagePath), "最旧逻辑条目的图片应删除");
		Assert.True(File.Exists(middle.ManifestPath) && File.Exists(middle.ImagePath));
		Assert.True(File.Exists(newest.ManifestPath) && File.Exists(newest.ImagePath));
	}

	[Fact]
	public void EnforceCacheBudget_DeletesManifestBeforeImage()
	{
		// 通过删除故障点观测顺序：删除图片时 manifest 必须已不存在（先撤销可见性）。
		byte[] png = LoadTestPng64();
		CacheEntryInfo oldest = WriteCacheEntry("BG03", png);
		SetEntryTimestamps(oldest, DateTime.UtcNow - TimeSpan.FromDays(3));
		ContentIconService svc = NewService();
		svc.CacheBudgetBytesOverride = 0; // 强制淘汰
		bool observedOrder = false;
		svc.BeforeBudgetImageDelete = imagePath =>
		{
			if (imagePath == oldest.ImagePath)
				observedOrder = !File.Exists(oldest.ManifestPath);
		};
		svc.EnforceCacheBudget();
		Assert.True(observedOrder, "必须先删除 manifest 再删除图片");
	}

	[Fact]
	public void EnforceCacheBudget_NoManifestPointsToMissingImage()
	{
		byte[] png = LoadTestPng64();
		CacheEntryInfo a = WriteCacheEntry("BG04A", png);
		CacheEntryInfo b = WriteCacheEntry("BG04B", png);
		SetEntryTimestamps(a, DateTime.UtcNow - TimeSpan.FromDays(3));
		ContentIconService svc = NewService();
		long sizeA = new FileInfo(a.ManifestPath).Length + new FileInfo(a.ImagePath).Length;
		svc.CacheBudgetBytesOverride = sizeA - 1; // 只淘汰最旧一条
		svc.EnforceCacheBudget();
		foreach (string mf in Directory.GetFiles(Path.Combine(_cacheRoot, "modrinth", "mod"), "*.json"))
		{
			string img = ExtractJsonString(File.ReadAllText(mf), "image");
			Assert.True(File.Exists(Path.Combine(Path.GetDirectoryName(mf)!, img)), $"manifest {mf} 指向缺失图片");
		}
	}

	[Fact]
	public async Task EnforceCacheBudget_SurvivorsStillCacheHitAndFullyDecode()
	{
		byte[] png = LoadTestPng64();
		CacheEntryInfo oldest = WriteCacheEntry("BG05A", png);
		CacheEntryInfo survivor = WriteCacheEntry("BG05B", png);
		SetEntryTimestamps(oldest, DateTime.UtcNow - TimeSpan.FromDays(3));
		ContentIconService svc = NewService();
		long survivorSize = new FileInfo(survivor.ManifestPath).Length + new FileInfo(survivor.ImagePath).Length;
		svc.CacheBudgetBytesOverride = survivorSize; // 总量 2×条目，预算 = 1×条目 → 恰好淘汰最旧一条
		svc.EnforceCacheBudget();
		Assert.False(File.Exists(oldest.ManifestPath));

		svc.CacheBudgetBytesOverride = null;
		svc.RegisterTrustedUrl("modrinth", "mod", survivor.ProjectId, survivor.Url);
		IconResult r = await svc.GetAsync("modrinth", "mod", survivor.ProjectId, CancellationToken.None);
		Assert.Equal(IconStatus.FromCache, r.Status);
		Assert.Equal(png, r.Bytes); // 保留条目仍能完整解码
	}

	[Fact]
	public void EnforceCacheBudget_CorruptEntriesNotCounted()
	{
		// 损坏条目（JSON 乱码 + 大图片）不计入健康缓存预算：不触发健康条目淘汰。
		byte[] png = LoadTestPng64();
		CacheEntryInfo healthy = WriteCacheEntry("BG06H", png);
		CacheEntryInfo corrupt = WriteCacheEntry("BG06C", png, manifestJson: "{not-json");
		byte[] big = new byte[2 * 1024 * 1024];
		File.WriteAllBytes(corrupt.ImagePath, big);
		SetEntryTimestamps(healthy, DateTime.UtcNow - TimeSpan.FromDays(3));

		ContentIconService svc = NewService();
		long healthySize = new FileInfo(healthy.ManifestPath).Length + png.LongLength;
		long corruptSize = new FileInfo(corrupt.ManifestPath).Length + big.LongLength;
		// 若损坏条目被计入，healthy 必然被淘汰（总超预算）；assert 保留 = 损坏未计数
		svc.CacheBudgetBytesOverride = healthySize + corruptSize - 1;
		svc.EnforceCacheBudget();
		Assert.True(File.Exists(healthy.ManifestPath) && File.Exists(healthy.ImagePath), "损坏条目不应计入预算导致健康条目被淘汰");
	}

	[Fact]
	public void EnforceCacheBudget_SingleEntryOverBudget_EvictedEntirely()
	{
		// 单个条目超过总预算：行为明确 —— 完整淘汰该条目（不留半提交状态）。
		byte[] png = LoadTestPng64();
		CacheEntryInfo e = WriteCacheEntry("BG07", png);
		ContentIconService svc = NewService();
		svc.CacheBudgetBytesOverride = png.LongLength / 2;
		svc.EnforceCacheBudget();
		Assert.False(File.Exists(e.ManifestPath));
		Assert.False(File.Exists(e.ImagePath));
	}

	[Fact]
	public void EnforceCacheBudget_ImageDeleteFailure_AccountingNotCorrupted()
	{
		// manifest 删除成功、图片删除失败（真实文件锁）：预算计算不得死循环/错账，
		// 后续条目不因此多删。
		byte[] png = LoadTestPng64();
		CacheEntryInfo a = WriteCacheEntry("BG08A", png);
		CacheEntryInfo b = WriteCacheEntry("BG08B", png);
		SetEntryTimestamps(a, DateTime.UtcNow - TimeSpan.FromDays(3));
		SetEntryTimestamps(b, DateTime.UtcNow - TimeSpan.FromDays(2));
		ContentIconService svc = NewService();
		long sizeA = new FileInfo(a.ManifestPath).Length + new FileInfo(a.ImagePath).Length;
		long sizeB = new FileInfo(b.ManifestPath).Length + new FileInfo(b.ImagePath).Length;
		svc.CacheBudgetBytesOverride = (sizeA + sizeB) - 1; // 只需淘汰 A
		using (FileStream lockStream = File.Open(a.ImagePath, FileMode.Open, FileAccess.Read, FileShare.None))
		{
			svc.EnforceCacheBudget(); // 图片删除失败被吞掉，函数必须返回
		}
		Assert.False(File.Exists(a.ManifestPath), "manifest 已删除（可见性已撤销）");
		Assert.True(File.Exists(a.ImagePath), "图片删除失败应保留");
		Assert.True(File.Exists(b.ManifestPath) && File.Exists(b.ImagePath), "后续条目不应多删");
		// 第二次运行：A 无 manifest → 不计入预算，B 保留；
		// A 图片（时间戳已被回溯 3 天）成为无 manifest 引用的孤立版本，被宽限期清理移除
		svc.EnforceCacheBudget();
		Assert.True(File.Exists(b.ManifestPath) && File.Exists(b.ImagePath));
		Assert.False(File.Exists(a.ImagePath), "无 manifest 引用且超过宽限期的旧图片应被孤立清理");
	}

	[Fact]
	public async Task EnforceCacheBudget_ConcurrentWithCommit_NoHalfCommit()
	{
		// CommitCache 与 EnforceCacheBudget 并发：结束态不存在"manifest 指向不存在图片"。
		byte[] png = LoadTestPng64();
		ContentIconService svc = NewService((_, __) => Task.FromResult(MakeOk(png)));
		svc.RegisterTrustedUrl("modrinth", "mod", "BG09", "https://cdn.modrinth.com/data/BG09/icon.png");
		TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
		TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
		svc.BeforeImageCommit = (_, _) =>
		{
			entered.TrySetResult(); // 请求已到达图片提交点（真实信号，非延时）
			release.Task.Wait(TimeSpan.FromSeconds(15));
		};
		Task<IconResult> commit = svc.GetAsync("modrinth", "mod", "BG09", CancellationToken.None);
		await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));

		string dir = Path.Combine(_cacheRoot, "modrinth", "mod");
		Task budget = Task.Run(() => svc.EnforceCacheBudget());
		release.TrySetResult();
		await Task.WhenAll(commit.WaitAsync(TimeSpan.FromSeconds(15)), budget.WaitAsync(TimeSpan.FromSeconds(15)));

		Assert.Equal(IconStatus.Ok, (await commit).Status);
		foreach (string mf in Directory.GetFiles(dir, "*.json"))
		{
			string img = ExtractJsonString(File.ReadAllText(mf), "image");
			Assert.True(File.Exists(Path.Combine(dir, img)), $"并发结束后 manifest {mf} 指向缺失图片 {img}");
		}
	}

	[Fact]
	public async Task EnforceCacheBudget_ConcurrentWithCacheRead_NoExceptions()
	{
		// 正在被读取的缓存不会因清理产生未处理异常；最终状态健康。
		byte[] png = LoadTestPng64();
		CacheEntryInfo e = WriteCacheEntry("BG10", png);
		ContentIconService svc = NewService((_, __) => Task.FromResult(MakeOk(png)));
		svc.RegisterTrustedUrl("modrinth", "mod", e.ProjectId, e.Url);
		svc.CacheBudgetBytesOverride = 0; // 读取的同时持续淘汰该条目（淘汰后由读取路径重抓）

		Task reader = Task.Run(async () =>
		{
			for (int i = 0; i < 20; i++)
			{
				IconResult r = await svc.GetAsync("modrinth", "mod", e.ProjectId, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));
				Assert.True(r.Status is IconStatus.Ok or IconStatus.FromCache, $"意外状态 {r.Status}");
			}
		});
		Task budget = Task.Run(async () =>
		{
			for (int i = 0; i < 20; i++) svc.EnforceCacheBudget();
		});
		await Task.WhenAll(reader.WaitAsync(TimeSpan.FromSeconds(60)), budget.WaitAsync(TimeSpan.FromSeconds(60)));
	}

	[Fact]
	public void EnforceCacheBudget_OverBudgetOnlyEvictsInTargetCategory()
	{
		// 预算超限淘汰最旧条目：其他 source/category 的较新条目不被误删。
		byte[] png = LoadTestPng64();
		CacheEntryInfo victim = WriteCacheEntry("BG11A", png); // modrinth/mod，最旧
		CacheEntryInfo cf = WriteCacheEntry("BG11B", png, source: "curseforge");
		CacheEntryInfo shader = WriteCacheEntry("BG11C", png, category: "shader");
		DateTime baseTime = DateTime.UtcNow - TimeSpan.FromDays(3);
		SetEntryTimestamps(victim, baseTime);
		SetEntryTimestamps(cf, baseTime + TimeSpan.FromDays(1));
		SetEntryTimestamps(shader, baseTime + TimeSpan.FromDays(2));
		ContentIconService svc = NewService();
		long total = Directory.GetFiles(_cacheRoot, "*", SearchOption.AllDirectories).Sum(f => new FileInfo(f).Length);
		svc.CacheBudgetBytesOverride = total - 1;
		svc.EnforceCacheBudget();
		Assert.False(File.Exists(victim.ManifestPath));
		Assert.True(File.Exists(cf.ManifestPath) && File.Exists(cf.ImagePath), "其他 source 的健康缓存不应被误删");
		Assert.True(File.Exists(shader.ManifestPath) && File.Exists(shader.ImagePath), "其他 category 的健康缓存不应被误删");
	}

	[Fact]
	public void EnforceCacheBudget_ManifestDeleteFailure_KeepsHealthyEntries()
	{
		// 清理失败（manifest 删除失败：真实文件锁）不破坏健康条目、预算不错账（不无限多删）。
		byte[] png = LoadTestPng64();
		CacheEntryInfo a = WriteCacheEntry("BG12A", png);
		CacheEntryInfo b = WriteCacheEntry("BG12B", png);
		SetEntryTimestamps(a, DateTime.UtcNow - TimeSpan.FromDays(3));
		SetEntryTimestamps(b, DateTime.UtcNow - TimeSpan.FromDays(2));
		ContentIconService svc = NewService();
		long sizeA = new FileInfo(a.ManifestPath).Length + new FileInfo(a.ImagePath).Length;
		long sizeB = new FileInfo(b.ManifestPath).Length + new FileInfo(b.ImagePath).Length;
		svc.CacheBudgetBytesOverride = (sizeA + sizeB) - 1; // 只需淘汰 A
		using (FileStream lockStream = File.Open(a.ManifestPath, FileMode.Open, FileAccess.Read, FileShare.None))
		{
			svc.EnforceCacheBudget();
		}
		Assert.True(File.Exists(a.ManifestPath), "锁定的 manifest 删除失败应保留");
		Assert.True(File.Exists(b.ManifestPath) && File.Exists(b.ImagePath), "B 不应被多删");
	}

	// -------------------------------------------------------------------------
	//  工具
	// -------------------------------------------------------------------------

	private static Func<Uri, CancellationToken, Task<HttpResponseMessage>> MakeOkFetcher(string mime, byte[] body)
	{
		return (_, __) =>
		{
			HttpResponseMessage m = new HttpResponseMessage(System.Net.HttpStatusCode.OK);
			m.Content = new StreamContent(new MemoryStream(body));
			m.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(mime);
			return Task.FromResult(m);
		};
	}

	private static HttpResponseMessage MakeOk(byte[] body, string mime = "image/png")
	{
		HttpResponseMessage m = new HttpResponseMessage(HttpStatusCode.OK);
		m.Content = new StreamContent(new MemoryStream(body));
		m.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(mime);
		return m;
	}

	private static string ReadManifestField(string json, string key)
	{
		string pat = "\"" + key + "\":\"";
		int i = json.IndexOf(pat, StringComparison.Ordinal);
		if (i < 0) return string.Empty;
		int s = i + pat.Length;
		int e = json.IndexOf('"', s);
		return e > s ? json.Substring(s, e - s) : string.Empty;
	}

	private static byte[] MakePng(int width, int height)
	{
		// 构造一个通过有限结构校验的最小合法 PNG：签名 + IHDR(13) + IDAT + IEND。
		MemoryStream ms = new MemoryStream();
		ms.Write(new byte[] { 137,80,78,71,13,10,26,10 }, 0, 8);
		WritePngChunk(ms, "IHDR", new byte[]
		{
			(byte)(width >> 24), (byte)(width >> 16), (byte)(width >> 8), (byte)width,
			(byte)(height >> 24), (byte)(height >> 16), (byte)(height >> 8), (byte)height,
			8, 2, 0, 0, 0
		});
		WritePngChunk(ms, "IDAT", Array.Empty<byte>());
		WritePngChunk(ms, "IEND", Array.Empty<byte>());
		return ms.ToArray();
	}

	private static void WritePngChunk(Stream s, string type, byte[] data)
	{
		s.Write(new byte[] {
			(byte)(data.Length >> 24), (byte)(data.Length >> 16), (byte)(data.Length >> 8), (byte)data.Length
		}, 0, 4);
		byte[] t = Encoding.ASCII.GetBytes(type);
		s.Write(t, 0, 4);
		s.Write(data, 0, data.Length);
		s.Write(new byte[4], 0, 4); // CRC（结构校验不检查 CRC）
	}

	private static byte[] MakeJpeg()
	{
		// SOI + APP0(JFIF) + EOI：通过 JPEG 有限结构校验（段长度合法、以 EOI 结束）。
		return new byte[]
		{
			0xFF, 0xD8,
			0xFF, 0xE0, 0x00, 0x10,
			0x4A, 0x46, 0x49, 0x46, 0x00, 0x01, 0x01, 0x00, 0x00, 0x01, 0x00, 0x01, 0x00, 0x00,
			0xFF, 0xD9
		};
	}

	private static byte[] MakeWebp()
	{
		// RIFF + 声明长度=文件长度-8 + WEBP + 单个 VP8 chunk（size=0）：通过 WebP 有限结构校验。
		return new byte[]
		{
			(byte)'R',(byte)'I',(byte)'F',(byte)'F', 0x0C,0x00,0x00,0x00,
			(byte)'W',(byte)'E',(byte)'B',(byte)'P',
			(byte)'V',(byte)'P',(byte)'8',(byte)' ', 0x00,0x00,0x00,0x00
		};
	}

	// -------------------------------------------------------------------------
	//  测试资源：真实可解码的 PNG / JPEG / WebP（从 TestAssets 目录加载）
	// -------------------------------------------------------------------------

	private static string TestAssetsRoot => Path.Combine(AppContext.BaseDirectory, "TestAssets", "ContentIcons");

	private static byte[] LoadTestPng() => File.ReadAllBytes(Path.Combine(TestAssetsRoot, "valid-1x1.png"));
	private static byte[] LoadTestJpeg() => File.ReadAllBytes(Path.Combine(TestAssetsRoot, "valid-1x1.jpg"));
	private static byte[] LoadTestWebp() => File.ReadAllBytes(Path.Combine(TestAssetsRoot, "valid-1x1.webp"));
	private static byte[] LoadTestPng64() => File.ReadAllBytes(Path.Combine(TestAssetsRoot, "valid-64.png"));
	private static byte[] LoadTestJpeg64() => File.ReadAllBytes(Path.Combine(TestAssetsRoot, "valid-64.jpg"));
	private static byte[] LoadTestWebp64() => File.ReadAllBytes(Path.Combine(TestAssetsRoot, "valid-64.webp"));

	private void AssertEmptyCacheDir(string source, string category)
	{
		string dir = Path.Combine(_cacheRoot, source.ToLowerInvariant(), category.ToLowerInvariant());
		if (Directory.Exists(dir))
		{
			Assert.Empty(Directory.GetFiles(dir, "*"));
			Assert.Empty(Directory.GetDirectories(dir));
		}
	}
}
