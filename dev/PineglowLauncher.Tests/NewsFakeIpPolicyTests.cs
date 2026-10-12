using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Pineglow.Launcher.Services;
using SkiaSharp;
using Xunit;

namespace Pineglow.Launcher.Tests;

/// <summary>
/// R-02：198.18.0.0/15（fake-ip 基准段）默认归类为受限地址；
/// 仅通过显式注入的窄接口（构造参数 allowFakeIpRanges / TestFakeIpCompat）放行，
/// 且 fake-ip 与公网/私网混合解析必须整体拒绝；重定向逐跳重新判定。
/// </summary>
public class NewsFakeIpPolicyTests
{
	private const string OfficialImage = "https://launchercontent.mojang.com/images/cover.png";
	private static readonly IPAddress PublicIp = IPAddress.Parse("93.184.216.34");

	private static byte[] RealPng()
	{
		using var bitmap = new SKBitmap(8, 8);
		using var canvas = new SKCanvas(bitmap);
		canvas.Clear(SKColors.CornflowerBlue);
		using var image = SKImage.FromBitmap(bitmap);
		using var data = image.Encode(SKEncodedImageFormat.Png, 90);
		return data.ToArray();
	}

	private static NewsImageResponse ImageResponse(byte[] bytes, string contentType = "image/png", int status = 200, string? location = null) =>
		new NewsImageResponse(status, location, contentType, bytes?.LongLength, _ => Task.FromResult(bytes!));

	private static Func<string, CancellationToken, Task<IPAddress[]>> Dns(params IPAddress[] addresses) =>
		(_, _) => Task.FromResult(addresses);

	private static NewsImageProxyService CreateService(
		Func<string, CancellationToken, Task<IPAddress[]>>? dns = null,
		NewsImageProxyService.NewsImageTransport? transport = null,
		bool? fakeIpCompat = null)
	{
		var service = new NewsImageProxyService(
			cacheDirectory: System.IO.Path.Combine(System.IO.Path.GetTempPath(), "pineglow-newsimg-" + Guid.NewGuid().ToString("N")),
			dnsResolver: dns,
			transport: transport);
		if (fakeIpCompat.HasValue) service.TestFakeIpCompat = fakeIpCompat.Value;
		return service;
	}

	[Fact]
	public void Guard_Default_RejectsFakeIpBenchmarkRange()
	{
		Assert.Throws<NewsImageException>(() => NewsSsrfGuard.SelectConnectableAddress(new[] { IPAddress.Parse("198.18.0.7") }));
	}

	[Fact]
	public void Guard_Default_RejectsMappedFakeIp()
	{
		Assert.Throws<NewsImageException>(() => NewsSsrfGuard.SelectConnectableAddress(new[] { IPAddress.Parse("::ffff:198.18.0.7") }));
	}

	[Fact]
	public void Guard_FakeIpMixedWithPublic_Rejected()
	{
		Assert.Throws<NewsImageException>(() => NewsSsrfGuard.SelectConnectableAddress(new[] { PublicIp, IPAddress.Parse("198.18.0.7") }));
	}

	[Fact]
	public void Guard_FakeIpCompat_ExplicitOptIn_Selects()
	{
		IPAddress selected = NewsSsrfGuard.SelectConnectableAddress(new[] { IPAddress.Parse("198.18.0.7") }, allowFakeIpRanges: true);
		Assert.Equal(IPAddress.Parse("198.18.0.7"), selected);
	}

	[Fact]
	public void Guard_FakeIpCompat_MixedWithPrivateOrPublic_StillRejected()
	{
		Assert.Throws<NewsImageException>(() => NewsSsrfGuard.SelectConnectableAddress(
			new[] { IPAddress.Parse("198.18.0.7"), IPAddress.Parse("10.0.0.5") }, allowFakeIpRanges: true));
		Assert.Throws<NewsImageException>(() => NewsSsrfGuard.SelectConnectableAddress(
			new[] { IPAddress.Parse("198.18.0.7"), PublicIp }, allowFakeIpRanges: true));
	}

	[Fact]
	public async Task Service_FakeIpDns_RejectedByDefault()
	{
		var transport = new NewsImageProxyService.NewsImageTransport((_, _, _) => Task.FromResult(ImageResponse(RealPng())));
		var service = CreateService(dns: Dns(IPAddress.Parse("198.18.0.7")), transport: transport, fakeIpCompat: false);
		string id = service.RegisterImageUrl(OfficialImage)!;

		var exception = await Assert.ThrowsAsync<NewsImageException>(() => service.FetchImageAsync(id, CancellationToken.None));

		Assert.Equal("dns_private", exception.Kind);
	}

	[Fact]
	public async Task Service_FakeIpCompat_ExplicitOptIn_FetchesWithPinnedAddress()
	{
		IPAddress? pinned = null;
		var transport = new NewsImageProxyService.NewsImageTransport((_, address, _) => { pinned = address; return Task.FromResult(ImageResponse(RealPng())); });
		var service = CreateService(dns: Dns(IPAddress.Parse("198.18.0.7")), transport: transport, fakeIpCompat: true);
		string id = service.RegisterImageUrl(OfficialImage)!;

		byte[] bytes = await service.FetchImageAsync(id, CancellationToken.None);

		Assert.Equal(IPAddress.Parse("198.18.0.7"), pinned);
		Assert.NotEmpty(bytes);
	}

	[Fact]
	public async Task Service_Redirect_ReevaluatesFakeIpPerHop()
	{
		// 第一跳（公网）返回重定向；重定向目标（官方图片 host）解析为 fake-ip → 默认拒绝，不继承首跳
		var transport = new NewsImageProxyService.NewsImageTransport((_, _, _) =>
			Task.FromResult(ImageResponse(Array.Empty<byte>(), "text/html", 302, "https://launchercontent.mojang.com/images/next.png")));
		var service = CreateService(
			dns: Dns(IPAddress.Parse("198.18.0.7")),
			transport: transport,
			fakeIpCompat: false);
		string id = service.RegisterImageUrl(OfficialImage)!;

		var exception = await Assert.ThrowsAsync<NewsImageException>(() => service.FetchImageAsync(id, CancellationToken.None));

		Assert.Equal("dns_private", exception.Kind);
	}

	// ============== R2-02: fake-ip + 各类 IPv6 混合 ==============

	[Fact]
	public void Guard_FakeIpCompat_MixedWithTeredo_StillRejected()
	{
		// 2001:0000::/32（真正 Teredo）属于默认受限；与 fake-ip 混合时整体 dns_mixed
		Assert.Throws<NewsImageException>(() => NewsSsrfGuard.SelectConnectableAddress(
			new[] { IPAddress.Parse("198.18.0.7"), IPAddress.Parse("2001:0000:4136:e378:8000:63bf:3fff:fdd2") },
			allowFakeIpRanges: true));
	}

	[Fact]
	public void Guard_FakeIpCompat_MixedWith6to4_StillRejected()
	{
		// 2002::/16 6to4 属于默认受限；与 fake-ip 混合时整体 dns_mixed
		Assert.Throws<NewsImageException>(() => NewsSsrfGuard.SelectConnectableAddress(
			new[] { IPAddress.Parse("198.18.0.7"), IPAddress.Parse("2002::1") },
			allowFakeIpRanges: true));
	}

	[Fact]
	public void Guard_FakeIpCompat_MixedWithNat64_StillRejected()
	{
		// 64:ff9b::/96（NAT64 well-known prefix）属于默认受限；与 fake-ip 混合时整体 dns_mixed
		Assert.Throws<NewsImageException>(() => NewsSsrfGuard.SelectConnectableAddress(
			new[] { IPAddress.Parse("198.18.0.7"), IPAddress.Parse("64:ff9b::1") },
			allowFakeIpRanges: true));
	}

	// ============== R2-02: 重定向逐跳重验（true 分支） ==============

	[Fact]
	public async Task Service_Redirect_ReevaluatesFakeIpPerHop_TrueBranch()
	{
		// 显式 fake-ip 兼容开启时：第一跳（公网）返回重定向 → 重定向目标解析为纯 fake-ip
		// 仍然按新一 hop 评估：纯 198.18/15 → 放行；transport 收到精确 pinned + 原始 host
		IPAddress? pinned = null;
		string? lastHost = null;
		var transport = new NewsImageProxyService.NewsImageTransport((uri, address, _) =>
		{
			pinned = address;
			lastHost = uri.Host;
			if (uri.AbsolutePath.EndsWith("/cover.png", StringComparison.OrdinalIgnoreCase))
			{
				// 第一跳：公网地址 → 返回重定向
				return Task.FromResult(ImageResponse(Array.Empty<byte>(), "text/html", 302,
					"https://launchercontent.mojang.com/images/next.png"));
			}
			// 第二跳：fake-ip → 成功
			return Task.FromResult(ImageResponse(RealPng()));
		});
		var service = CreateService(dns: Dns(IPAddress.Parse("198.18.0.7")), transport: transport, fakeIpCompat: true);
		string id = service.RegisterImageUrl(OfficialImage)!;

		byte[] bytes = await service.FetchImageAsync(id, CancellationToken.None);

		Assert.NotEmpty(bytes);
		Assert.Equal(IPAddress.Parse("198.18.0.7"), pinned);
		Assert.Equal("launchercontent.mojang.com", lastHost);
	}

	// ============== R2-02: 非白名单 host 在 true 时仍不能注册 ==============

	[Fact]
	public void RegisterImageUrl_NonOfficialHost_ReturnsNull_EvenUnderTrue()
	{
		var service = CreateService(dns: Dns(IPAddress.Parse("198.18.0.7")), fakeIpCompat: true);

		// 非白名单 host（含攻击者控制的 fake-ip 域）即使注入 true 也必须被白名单拒绝
		Assert.Null(service.RegisterImageUrl("https://attacker.example.com/198.18.0.7.png"));
		Assert.Null(service.RegisterImageUrl("https://198.18.0.7/direct.png"));
		Assert.Null(service.RegisterImageUrl("http://launchercontent.mojang.com/insecure.png"));
	}
}
