using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Pineglow.Launcher.Services;
using SkiaSharp;
using Xunit;

namespace Pineglow.Launcher.Tests;

/// <summary>
/// Phase 3A 新闻图片代理安全测试（先红后绿）。
/// 模型：服务端从已验证官方 feed 注册图片 URL → 不可篡改 ID；前端只提交 ID。
/// 网络层：精确官方 host + DNS/IP 校验（私网/环回/链路本地/保留/映射地址/混合 DNS）+
/// 绑定已验证地址防 rebinding + 重定向重验证限跳数 + 超时/3MB/MIME/魔数/结构解码。
/// DNS 与传输层可注入，测试执行生产行为路径（非源码字符串断言）。
/// </summary>
public class NewsImageProxyServiceTests
{
	private const string OfficialImage = "https://launchercontent.mojang.com/images/cover.png";
	private static readonly IPAddress PublicIp = IPAddress.Parse("93.184.216.34");

	// ---- 真实可解码 fixture（R-01）：必须能被 SkiaSharp 完整解码 ----
	private static byte[] EncodeRealImage(SKEncodedImageFormat fmt, int width = 8, int height = 8)
	{
		using var bitmap = new SkiaSharp.SKBitmap(width, height);
		using var canvas = new SkiaSharp.SKCanvas(bitmap);
		canvas.Clear(SkiaSharp.SKColors.CornflowerBlue);
		using var image = SkiaSharp.SKImage.FromBitmap(bitmap);
		using var data = image.Encode(fmt, 90);
		if (data == null) throw new InvalidOperationException("encoder failed for " + fmt);
		return data.ToArray();
	}

	private static byte[] PngBytes(int width = 8, int height = 8) => EncodeRealImage(SKEncodedImageFormat.Png, width, height);
	private static byte[] JpegBytes(int width = 8, int height = 8) => EncodeRealImage(SKEncodedImageFormat.Jpeg, width, height);
	private static byte[] WebpBytes(int width = 8, int height = 8) => EncodeRealImage(SKEncodedImageFormat.Webp, width, height);

	// 最小可解码 GIF89a（1x1）
	private static byte[] GifBytes() => new byte[]
	{
		0x47, 0x49, 0x46, 0x38, 0x39, 0x61, 0x01, 0x00, 0x01, 0x00, 0x80, 0x00, 0x00,
		0xFF, 0xFF, 0xFF, 0x00, 0x00, 0x00, 0x2C, 0x00, 0x00, 0x00, 0x00, 0x01, 0x00,
		0x01, 0x00, 0x00, 0x02, 0x02, 0x44, 0x01, 0x00, 0x3B
	};

	// ---- 头部合法但像素数据损坏/缺失的 fixture（R-01 RED） ----
	private static byte[] PngWithoutIdat(byte[] realPng)
	{
		// 签名(8) + IHDR 块(25) + IEND(12)：结构合法但无像素数据
		using var ms = new System.IO.MemoryStream();
		ms.Write(realPng, 0, 8);
		ms.Write(realPng, 8, 25);
		ms.Write(realPng, realPng.Length - 12, 12);
		return ms.ToArray();
	}

	private static byte[] Truncate(byte[] bytes, int ratioPercent)
	{
		int len = Math.Max(1, bytes.Length * ratioPercent / 100);
		byte[] cut = new byte[len];
		Array.Copy(bytes, cut, len);
		return cut;
	}

	// WebP：RIFF/WEBP/VP8 头 + chunk 声明长度与实际 payload 不一致（仅 30 字节头部）
	private static byte[] WebpHeaderOnly()
	{
		byte[] b = new byte[30];
		b[0] = (byte)'R'; b[1] = (byte)'I'; b[2] = (byte)'F'; b[3] = (byte)'F';
		b[4] = 0x40; b[5] = 0x00; b[6] = 0x00; b[7] = 0x00; // riffSize 声明 64，实际 0
		b[8] = (byte)'W'; b[9] = (byte)'E'; b[10] = (byte)'B'; b[11] = (byte)'P';
		b[12] = (byte)'V'; b[13] = (byte)'P'; b[14] = (byte)'8'; b[15] = (byte)' ';
		b[16] = 0x20; b[17] = 0x00; b[18] = 0x00; b[19] = 0x00; // chunkSize 声明 32，实际 10
		return b;
	}

	// PNG 维度炸弹：签名 + IHDR(100000x100000)，像素量必须在分配前被拒绝
	private static byte[] PngDimensionBomb()
	{
		byte[] png = PngBytes();
		byte[] bomb = (byte[])png.Clone();
		// IHDR width/height 位于偏移 16..23
		bomb[16] = 0x00; bomb[17] = 0x01; bomb[18] = 0x86; bomb[19] = 0xA0; // 100000
		bomb[20] = 0x00; bomb[21] = 0x01; bomb[22] = 0x86; bomb[23] = 0xA0;
		return bomb;
	}

	private static NewsImageResponse ImageResponse(byte[] bytes, string contentType = "image/png", int status = 200, string? location = null) =>
		new NewsImageResponse(status, location, contentType, bytes?.LongLength, _ => Task.FromResult(bytes!));

	private static NewsImageProxyService CreateService(
		Func<string, CancellationToken, Task<IPAddress[]>>? dns = null,
		NewsImageProxyService.NewsImageTransport? transport = null,
		string? cacheDirectory = null,
		TimeSpan? timeout = null)
	{
		return new NewsImageProxyService(
			cacheDirectory: cacheDirectory ?? System.IO.Path.Combine(System.IO.Path.GetTempPath(), "pineglow-newsimg-" + Guid.NewGuid().ToString("N")),
			dnsResolver: dns,
			transport: transport,
			imageTimeout: timeout);
	}

	private static Func<string, CancellationToken, Task<IPAddress[]>> Dns(params IPAddress[] addresses) =>
		(_, _) => Task.FromResult(addresses);

	/* ---------------- 注册与 ID 映射 ---------------- */

	[Fact]
	public void RegisterImageUrl_AcceptsOfficial_ReturnsStableDeterministicId()
	{
		var service = CreateService();
		string? id = service.RegisterImageUrl(OfficialImage);
		Assert.NotNull(id);
		Assert.Equal(64, id!.Length); // SHA-256 hex
		Assert.Equal(id, service.RegisterImageUrl(OfficialImage));
	}

	[Theory]
	[InlineData("http://launchercontent.mojang.com/images/a.png")]
	[InlineData("https://evil.com/images/a.png")]
	[InlineData("javascript:alert(1)")]
	[InlineData("")]
	[InlineData("https://169.254.169.254/latest/meta-data")]
	public void RegisterImageUrl_RejectsNonOfficialOrUnsafe(string url)
	{
		var service = CreateService();
		Assert.Null(service.RegisterImageUrl(url));
	}

	[Fact]
	public async Task FetchImageAsync_UnknownId_FailsWithUnknownImage()
	{
		var service = CreateService();
		var exception = await Assert.ThrowsAsync<NewsImageException>(() => service.FetchImageAsync("deadbeef", CancellationToken.None));
		Assert.Equal("unknown_image", exception.Kind);
	}

	/* ---------------- DNS/IP 校验 ---------------- */

	[Fact]
	public async Task FetchImageAsync_LoopbackDns_Rejected()
	{
		var service = CreateService(dns: Dns(IPAddress.Loopback), transport: (_, _, _) => throw new InvalidOperationException("must not connect"));
		string id = service.RegisterImageUrl(OfficialImage)!;
		var exception = await Assert.ThrowsAsync<NewsImageException>(() => service.FetchImageAsync(id, CancellationToken.None));
		Assert.Equal("dns_private", exception.Kind);
	}

	[Fact]
	public async Task FetchImageAsync_PrivateDns_Rejected()
	{
		var service = CreateService(dns: Dns(IPAddress.Parse("10.1.2.3")), transport: (_, _, _) => throw new InvalidOperationException("must not connect"));
		string id = service.RegisterImageUrl(OfficialImage)!;
		var exception = await Assert.ThrowsAsync<NewsImageException>(() => service.FetchImageAsync(id, CancellationToken.None));
		Assert.Equal("dns_private", exception.Kind);
	}

	[Fact]
	public async Task FetchImageAsync_LinkLocalMetadataDns_Rejected()
	{
		var service = CreateService(dns: Dns(IPAddress.Parse("169.254.169.254")), transport: (_, _, _) => throw new InvalidOperationException("must not connect"));
		string id = service.RegisterImageUrl(OfficialImage)!;
		var exception = await Assert.ThrowsAsync<NewsImageException>(() => service.FetchImageAsync(id, CancellationToken.None));
		Assert.Equal("dns_private", exception.Kind);
	}

	[Fact]
	public async Task FetchImageAsync_MappedIpv6Loopback_Rejected()
	{
		var service = CreateService(dns: Dns(IPAddress.Parse("::ffff:127.0.0.1")), transport: (_, _, _) => throw new InvalidOperationException("must not connect"));
		string id = service.RegisterImageUrl(OfficialImage)!;
		var exception = await Assert.ThrowsAsync<NewsImageException>(() => service.FetchImageAsync(id, CancellationToken.None));
		Assert.Equal("dns_private", exception.Kind);
	}

	[Fact]
	public async Task FetchImageAsync_MixedPublicPrivateDns_Rejected()
	{
		var service = CreateService(dns: Dns(PublicIp, IPAddress.Parse("192.168.1.10")), transport: (_, _, _) => throw new InvalidOperationException("must not connect"));
		string id = service.RegisterImageUrl(OfficialImage)!;
		var exception = await Assert.ThrowsAsync<NewsImageException>(() => service.FetchImageAsync(id, CancellationToken.None));
		Assert.Equal("dns_mixed", exception.Kind);
	}

	[Fact]
	public async Task FetchImageAsync_EmptyDns_Rejected()
	{
		var service = CreateService(dns: Dns(), transport: (_, _, _) => throw new InvalidOperationException("must not connect"));
		string id = service.RegisterImageUrl(OfficialImage)!;
		var exception = await Assert.ThrowsAsync<NewsImageException>(() => service.FetchImageAsync(id, CancellationToken.None));
		Assert.Equal("dns_failed", exception.Kind);
	}

	[Fact]
	public void SsrfGuard_FakeIpBenchmarkRange_DefaultForbidden_ExplicitOptInAllowed()
	{
		// R-02 终态：默认拒绝；显式兼容模式下允许选择并仍由 pinned 连接 + TLS 主机名校验兜底
		Assert.Throws<NewsImageException>(() => NewsSsrfGuard.SelectConnectableAddress(new[] { IPAddress.Parse("198.18.0.7") }));
		Assert.Equal(IPAddress.Parse("198.18.0.7"),
			NewsSsrfGuard.SelectConnectableAddress(new[] { IPAddress.Parse("198.18.0.7") }, allowFakeIpRanges: true));
		Assert.Throws<NewsImageException>(() => NewsSsrfGuard.SelectConnectableAddress(new[] { IPAddress.Parse("192.168.1.1") }));
		Assert.Throws<NewsImageException>(() => NewsSsrfGuard.SelectConnectableAddress(new[] { IPAddress.Parse("169.254.169.254") }));
		Assert.Throws<NewsImageException>(() => NewsSsrfGuard.SelectConnectableAddress(new[] { IPAddress.Parse("10.0.0.5") }));
		Assert.Throws<NewsImageException>(() => NewsSsrfGuard.SelectConnectableAddress(new[] { IPAddress.Parse("100.64.0.1") }));
		Assert.Throws<NewsImageException>(() => NewsSsrfGuard.SelectConnectableAddress(new[] { IPAddress.Parse("::ffff:10.0.0.5") }));
	}

	/* ---------------- 真实 TCP 绑定（防 rebinding 的连接层证据） ---------------- */

	[Fact]
	public async Task ConnectPinnedAsync_ConnectsToPinnedAddressWithoutSecondResolution()
	{
		var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
		listener.Start();
		try
		{
			int port = ((IPEndPoint)listener.LocalEndpoint).Port;
			Task acceptTask = listener.AcceptTcpClientAsync().ContinueWith(_ => { });

			using var stream = await NewsImageProxyService.ConnectPinnedAsync("localhost", IPAddress.Loopback, port, useTls: false, CancellationToken.None);

			await acceptTask.WaitAsync(TimeSpan.FromSeconds(5));
			Assert.True(stream.CanWrite);
		}
		finally
		{
			listener.Stop();
		}
	}

	/* ================= R-01 RED：完整像素解码（旧实现只查结构头，必然错误接受损坏载荷） ================= */

	[Fact]
	public async Task R01_Png_HeaderValid_MissingIdat_Rejected()
	{
		byte[] corrupt = PngWithoutIdat(PngBytes());
		var transport = new NewsImageProxyService.NewsImageTransport((_, _, _) => Task.FromResult(ImageResponse(corrupt)));
		var service = CreateService(dns: Dns(PublicIp), transport: transport);
		string id = service.RegisterImageUrl(OfficialImage)!;

		var exception = await Assert.ThrowsAsync<NewsImageException>(() => service.FetchImageAsync(id, CancellationToken.None));

		Assert.Equal("decode_rejected", exception.Kind);
	}

	[Fact]
	public async Task R01_Png_HeaderValid_TruncatedIdat_Rejected()
	{
		byte[] corrupt = Truncate(PngBytes(), 60);
		var transport = new NewsImageProxyService.NewsImageTransport((_, _, _) => Task.FromResult(ImageResponse(corrupt)));
		var service = CreateService(dns: Dns(PublicIp), transport: transport);
		string id = service.RegisterImageUrl(OfficialImage)!;

		var exception = await Assert.ThrowsAsync<NewsImageException>(() => service.FetchImageAsync(id, CancellationToken.None));

		Assert.Equal("decode_rejected", exception.Kind);
	}

	[Fact]
	public async Task R01_Jpeg_HeaderValid_Truncated_Rejected()
	{
		byte[] corrupt = Truncate(JpegBytes(), 50);
		var transport = new NewsImageProxyService.NewsImageTransport((_, _, _) => Task.FromResult(ImageResponse(corrupt, "image/jpeg")));
		var service = CreateService(dns: Dns(PublicIp), transport: transport);
		string id = service.RegisterImageUrl(OfficialImage)!;

		var exception = await Assert.ThrowsAsync<NewsImageException>(() => service.FetchImageAsync(id, CancellationToken.None));

		Assert.Equal("decode_rejected", exception.Kind);
	}

	[Fact]
	public async Task R01_Gif_HeaderValid_Truncated_Rejected()
	{
		byte[] corrupt = Truncate(GifBytes(), 45);
		var transport = new NewsImageProxyService.NewsImageTransport((_, _, _) => Task.FromResult(ImageResponse(corrupt, "image/gif")));
		var service = CreateService(dns: Dns(PublicIp), transport: transport);
		string id = service.RegisterImageUrl(OfficialImage)!;

		var exception = await Assert.ThrowsAsync<NewsImageException>(() => service.FetchImageAsync(id, CancellationToken.None));

		Assert.Equal("decode_rejected", exception.Kind);
	}

	[Fact]
	public async Task R01_Webp_HeaderOnly_ChunkLengthMismatch_Rejected()
	{
		byte[] corrupt = WebpHeaderOnly();
		var transport = new NewsImageProxyService.NewsImageTransport((_, _, _) => Task.FromResult(ImageResponse(corrupt, "image/webp")));
		var service = CreateService(dns: Dns(PublicIp), transport: transport);
		string id = service.RegisterImageUrl(OfficialImage)!;

		var exception = await Assert.ThrowsAsync<NewsImageException>(() => service.FetchImageAsync(id, CancellationToken.None));

		Assert.Equal("decode_rejected", exception.Kind);
	}

	[Fact]
	public async Task R01_Png_DimensionBomb_RejectedBeforeAllocation()
	{
		byte[] bomb = PngDimensionBomb();
		var transport = new NewsImageProxyService.NewsImageTransport((_, _, _) => Task.FromResult(ImageResponse(bomb)));
		var service = CreateService(dns: Dns(PublicIp), transport: transport);
		string id = service.RegisterImageUrl(OfficialImage)!;

		var exception = await Assert.ThrowsAsync<NewsImageException>(() => service.FetchImageAsync(id, CancellationToken.None));

		Assert.Equal("decode_rejected", exception.Kind);
	}

	[Theory]
	[InlineData("png")]
	[InlineData("jpeg")]
	[InlineData("webp")]
	[InlineData("gif")]
	public async Task R01_RealDecodableFixture_Succeeds_DownloadThenCacheHit(string format)
	{
		byte[] bytes = format switch
		{
			"png" => PngBytes(),
			"jpeg" => JpegBytes(),
			"webp" => WebpBytes(),
			_ => GifBytes(),
		};
		string contentType = "image/" + format;
		string dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "pineglow-newsimg-" + Guid.NewGuid().ToString("N"));
		int calls = 0;
		var transport = new NewsImageProxyService.NewsImageTransport((_, _, _) => { calls++; return Task.FromResult(ImageResponse(bytes, contentType)); });
		var service = CreateService(dns: Dns(PublicIp), transport: transport, cacheDirectory: dir);
		string id = service.RegisterImageUrl(OfficialImage)!;

		byte[] first = await service.FetchImageAsync(id, CancellationToken.None);
		byte[] second = await service.FetchImageAsync(id, CancellationToken.None);

		Assert.NotEmpty(first);
		Assert.Equal(first, second);
		Assert.Equal(1, calls); // 第二次命中磁盘缓存，不再走网络
		Assert.NotEmpty(System.IO.Directory.GetFiles(dir));
	}

	[Fact]
	public async Task R01_FailedDecode_NotCached()
	{
		string dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "pineglow-newsimg-" + Guid.NewGuid().ToString("N"));
		byte[] corrupt = PngWithoutIdat(PngBytes());
		var transport = new NewsImageProxyService.NewsImageTransport((_, _, _) => Task.FromResult(ImageResponse(corrupt)));
		var service = CreateService(dns: Dns(PublicIp), transport: transport, cacheDirectory: dir);
		string id = service.RegisterImageUrl(OfficialImage)!;

		await Assert.ThrowsAsync<NewsImageException>(() => service.FetchImageAsync(id, CancellationToken.None));
		await Assert.ThrowsAsync<NewsImageException>(() => service.FetchImageAsync(id, CancellationToken.None));

		Assert.True(!System.IO.Directory.Exists(dir) || System.IO.Directory.GetFiles(dir).Length == 0, "失败载荷不落盘");
	}
}
