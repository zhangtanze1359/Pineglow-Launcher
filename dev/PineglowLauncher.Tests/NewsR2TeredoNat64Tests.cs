using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Pineglow.Launcher.Services;
using SkiaSharp;
using Xunit;

namespace Pineglow.Launcher.Tests;

/// <summary>
/// R2-01：修复 SSRF 守卫对真正 Teredo、标准 NAT64（含 well-known 与 local 前缀）、6to4 的错误分类。
///
/// 已知真实缺陷（旧实现）：
///  1. 真正 Teredo 2001:0000::/32（旧代码仅拦截 2001:db8::/32 文档前缀）
///  2. 标准 NAT64 well-known prefix 64:ff9b::/96（旧代码字节偏移错位 1 字节）
///  3. NAT64 local prefix 64:ff9b:1::/48（旧实现完全未覆盖）
///  4. 6to4 2002::/16 命名/注释错误（旧代码注释写为 Teredo）
///
/// RED 目标：旧实现对真正 Teredo 与 NAT64 真实漏判；6to4 已有整体拦截（既有安全回归），
/// 本次只改正命名错误，验证 6to4 IPv4 嵌入位置规范 + 防隧道绕过不退化。
/// </summary>
public class NewsR2TeredoNat64Tests
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

	// ---------- 真正 Teredo 2001:0000::/32（旧实现漏判）----------

	[Fact]
	public void R201_RealTeredoPrefix_DefaultForbidden()
	{
		// 文档 §3.1 给出：2001:0000:4136:e378:8000:63bf:3fff:fdd2（Microsoft 真实 Teredo）
		Assert.True(NewsSsrfGuard.IsForbiddenAddress(IPAddress.Parse("2001:0000:4136:e378:8000:63bf:3fff:fdd2")),
			"真实 Teredo 2001:0000::/32 必须被 IsForbiddenAddress 判为受限");
	}

	[Fact]
	public void R201_RealTeredoShortForm_DefaultForbidden()
	{
		Assert.True(NewsSsrfGuard.IsForbiddenAddress(IPAddress.Parse("2001::1")),
			"2001::1 属于 2001:0000::/32 Teredo 段");
	}

	[Fact]
	public void R201_RealTeredoZeroTail_DefaultForbidden()
	{
		Assert.True(NewsSsrfGuard.IsForbiddenAddress(IPAddress.Parse("2001:0:0:0:0:0:0:1")),
			"2001:0:0:0:0:0:0:1 属于 2001:0000::/32 Teredo 段");
	}

	// ---------- 标准 NAT64 well-known prefix 64:ff9b::/96（旧实现字节偏移错位）----------

	[Fact]
	public void R201_Nat64WellKnown_EmbedsPrivate_DefaultForbidden()
	{
		// 64:ff9b::a00:1 嵌入 10.0.0.1
		Assert.True(NewsSsrfGuard.IsForbiddenAddress(IPAddress.Parse("64:ff9b::a00:1")),
			"NAT64 well-known prefix 嵌入私网必须被拒");
	}

	[Fact]
	public void R201_Nat64WellKnown_EmbedsLoopback_DefaultForbidden()
	{
		// 64:ff9b::7f00:1 嵌入 127.0.0.1
		Assert.True(NewsSsrfGuard.IsForbiddenAddress(IPAddress.Parse("64:ff9b::7f00:1")),
			"NAT64 well-known prefix 嵌入环回必须被拒");
	}

	[Fact]
	public void R201_Nat64WellKnown_EmbedsLinkLocal_DefaultForbidden()
	{
		// 64:ff9b::a9fe:a9fe 嵌入 169.254.169.254
		Assert.True(NewsSsrfGuard.IsForbiddenAddress(IPAddress.Parse("64:ff9b::a9fe:a9fe")),
			"NAT64 well-known prefix 嵌入链路本地/云元数据必须被拒");
	}

	[Fact]
	public void R201_Nat64WellKnown_EmbedsRfc1918_DefaultForbidden()
	{
		// 64:ff9b::c0a8:101 嵌入 192.168.1.1
		Assert.True(NewsSsrfGuard.IsForbiddenAddress(IPAddress.Parse("64:ff9b::c0a8:101")),
			"NAT64 well-known prefix 嵌入 RFC1918 必须被拒");
	}

	// ---------- NAT64 local prefix 64:ff9b:1::/48（RFC 8215，旧实现完全未覆盖）----------

	[Fact]
	public void R201_Nat64Local_EmbedsPrivate_DefaultForbidden()
	{
		// 64:ff9b:1::a00:1 嵌入 10.0.0.1
		Assert.True(NewsSsrfGuard.IsForbiddenAddress(IPAddress.Parse("64:ff9b:1::a00:1")),
			"NAT64 local prefix 嵌入私网必须被拒");
	}

	[Fact]
	public void R201_Nat64Local_EmbedsLoopback_DefaultForbidden()
	{
		// 64:ff9b:1::7f00:1 嵌入 127.0.0.1
		Assert.True(NewsSsrfGuard.IsForbiddenAddress(IPAddress.Parse("64:ff9b:1::7f00:1")),
			"NAT64 local prefix 嵌入环回必须被拒");
	}

	[Fact]
	public void R201_Nat64Local_EmbedsLinkLocal_DefaultForbidden()
	{
		// 64:ff9b:1::a9fe:a9fe 嵌入 169.254.169.254
		Assert.True(NewsSsrfGuard.IsForbiddenAddress(IPAddress.Parse("64:ff9b:1::a9fe:a9fe")),
			"NAT64 local prefix 嵌入链路本地/云元数据必须被拒");
	}

	// ---------- 6to4 2002:WWXX:YYZZ::/48 整体拒绝（防隧道绕过，IPv4 嵌入位置正确）----------

	[Fact]
	public void R201_6to4_EmbedsPrivate_DefaultForbidden()
	{
		// 2002:0a00:0001::/48 嵌入 10.0.0.1
		Assert.True(NewsSsrfGuard.IsForbiddenAddress(IPAddress.Parse("2002:0a00:0001::")),
			"6to4 嵌入私网必须被拒");
	}

	[Fact]
	public void R201_6to4_EmbedsLoopback_DefaultForbidden()
	{
		// 2002:7f00:0001::/48 嵌入 127.0.0.1
		Assert.True(NewsSsrfGuard.IsForbiddenAddress(IPAddress.Parse("2002:7f00:0001::")),
			"6to4 嵌入环回必须被拒");
	}

	[Fact]
	public void R201_6to4_EmbedsLinkLocal_DefaultForbidden()
	{
		// 2002:a9fe:a9fe::/48 嵌入 169.254.169.254
		Assert.True(NewsSsrfGuard.IsForbiddenAddress(IPAddress.Parse("2002:a9fe:a9fe::")),
			"6to4 嵌入链路本地必须被拒");
	}

	[Fact]
	public void R201_6to4_EmbedsPublic_DefaultForbidden()
	{
		// 2002:5db8:d877::/48 嵌入 93.184.216.119 —— 整体 6to4 段必须被拒，不依赖嵌入地址
		Assert.True(NewsSsrfGuard.IsForbiddenAddress(IPAddress.Parse("2002:5db8:d877::")),
			"6to4 2002::/16 即使嵌入公网 IPv4 仍必须被拒（防 6to4 隧道绕过）");
	}

	// ---------- 普通公网 IPv6 边界反例（不应被误杀）----------

	[Fact]
	public void R201_PublicIpv6GoogleDns_Allowed()
	{
		// Google 公共 DNS 2001:4860:4860::8888
		Assert.False(NewsSsrfGuard.IsForbiddenAddress(IPAddress.Parse("2001:4860:4860::8888")),
			"普通公网 IPv6 (Google DNS) 不应被误杀");
	}

	[Fact]
	public void R201_PublicIpv6CloudflareDns_Allowed()
	{
		// Cloudflare 公共 DNS 2606:4700:4700::1111
		Assert.False(NewsSsrfGuard.IsForbiddenAddress(IPAddress.Parse("2606:4700:4700::1111")),
			"普通公网 IPv6 (Cloudflare DNS) 不应被误杀");
	}

	[Fact]
	public void R201_PublicIpv6Select_Allowed()
	{
		// 生产入口：SelectConnectableAddress 应返回该地址，不抛异常
		IPAddress selected = NewsSsrfGuard.SelectConnectableAddress(new[] { IPAddress.Parse("2001:4860:4860::8888") });
		Assert.Equal(IPAddress.Parse("2001:4860:4860::8888"), selected);
	}

	// ---------- SelectConnectableAddress 生产入口（不只是 IsForbiddenAddress）----------

	[Fact]
	public void R201_Teredo_Only_SelectRejected_AsDnsPrivate()
	{
		var ex = Assert.Throws<NewsImageException>(() =>
			NewsSsrfGuard.SelectConnectableAddress(new[] { IPAddress.Parse("2001:0000:4136:e378:8000:63bf:3fff:fdd2") }));
		Assert.Equal("dns_private", ex.Kind);
	}

	[Fact]
	public void R201_Nat64_Only_SelectRejected_AsDnsPrivate()
	{
		var ex = Assert.Throws<NewsImageException>(() =>
			NewsSsrfGuard.SelectConnectableAddress(new[] { IPAddress.Parse("64:ff9b::a00:1") }));
		Assert.Equal("dns_private", ex.Kind);
	}

	[Fact]
	public void R201_Nat64Local_Only_SelectRejected_AsDnsPrivate()
	{
		var ex = Assert.Throws<NewsImageException>(() =>
			NewsSsrfGuard.SelectConnectableAddress(new[] { IPAddress.Parse("64:ff9b:1::a00:1") }));
		Assert.Equal("dns_private", ex.Kind);
	}

	[Fact]
	public void R201_Teredo_MixedWithPublic_SelectRejected_AsDnsMixed()
	{
		// Teredo + 公网混合：必须 dns_mixed，不得挑选其中一个继续
		var ex = Assert.Throws<NewsImageException>(() =>
			NewsSsrfGuard.SelectConnectableAddress(new[] {
				IPAddress.Parse("2001:0000:4136:e378:8000:63bf:3fff:fdd2"),
				PublicIp,
			}));
		Assert.Equal("dns_mixed", ex.Kind);
	}

	[Fact]
	public void R201_Nat64_MixedWithPublic_SelectRejected_AsDnsMixed()
	{
		var ex = Assert.Throws<NewsImageException>(() =>
			NewsSsrfGuard.SelectConnectableAddress(new[] {
				IPAddress.Parse("64:ff9b::a00:1"),
				PublicIp,
			}));
		Assert.Equal("dns_mixed", ex.Kind);
	}

	[Fact]
	public void R201_Teredo_MixedWithPrivate_SelectRejected_AsDnsPrivate()
	{
		// Teredo (forbidden) + Private (forbidden) → 全部受限，整体 dns_private
		var ex = Assert.Throws<NewsImageException>(() =>
			NewsSsrfGuard.SelectConnectableAddress(new[] {
				IPAddress.Parse("2001::1"),
				IPAddress.Parse("10.0.0.5"),
			}));
		Assert.Equal("dns_private", ex.Kind);
	}

	// ---------- 真实服务入口：DNS 解析到 Teredo/NAT64 时 FetchImageAsync 必须拒绝 ----------

	[Fact]
	public async Task R201_Service_RealTeredoDns_Rejected_DnsPrivate()
	{
		var transport = new NewsImageProxyService.NewsImageTransport((_, _, _) => Task.FromResult(ImageResponse(RealPng())));
		var service = new NewsImageProxyService(
			cacheDirectory: System.IO.Path.Combine(System.IO.Path.GetTempPath(), "pineglow-r2-" + Guid.NewGuid().ToString("N")),
			dnsResolver: Dns(IPAddress.Parse("2001:0000:4136:e378:8000:63bf:3fff:fdd2")),
			transport: transport);
		string id = service.RegisterImageUrl(OfficialImage)!;

		var ex = await Assert.ThrowsAsync<NewsImageException>(() => service.FetchImageAsync(id, CancellationToken.None));
		Assert.Equal("dns_private", ex.Kind);
	}

	[Fact]
	public async Task R201_Service_Nat64WellKnownDns_Rejected_DnsPrivate()
	{
		var transport = new NewsImageProxyService.NewsImageTransport((_, _, _) => Task.FromResult(ImageResponse(RealPng())));
		var service = new NewsImageProxyService(
			cacheDirectory: System.IO.Path.Combine(System.IO.Path.GetTempPath(), "pineglow-r2-" + Guid.NewGuid().ToString("N")),
			dnsResolver: Dns(IPAddress.Parse("64:ff9b::a00:1")),
			transport: transport);
		string id = service.RegisterImageUrl(OfficialImage)!;

		var ex = await Assert.ThrowsAsync<NewsImageException>(() => service.FetchImageAsync(id, CancellationToken.None));
		Assert.Equal("dns_private", ex.Kind);
	}

	[Fact]
	public async Task R201_Service_Nat64LocalDns_Rejected_DnsPrivate()
	{
		var transport = new NewsImageProxyService.NewsImageTransport((_, _, _) => Task.FromResult(ImageResponse(RealPng())));
		var service = new NewsImageProxyService(
			cacheDirectory: System.IO.Path.Combine(System.IO.Path.GetTempPath(), "pineglow-r2-" + Guid.NewGuid().ToString("N")),
			dnsResolver: Dns(IPAddress.Parse("64:ff9b:1::a00:1")),
			transport: transport);
		string id = service.RegisterImageUrl(OfficialImage)!;

		var ex = await Assert.ThrowsAsync<NewsImageException>(() => service.FetchImageAsync(id, CancellationToken.None));
		Assert.Equal("dns_private", ex.Kind);
	}

	// ============== R2-01b: NAT64 精确边界（二次返工）==============
	// 当前实现用 b[0..4]==00 64 FF 9B 共同前缀，等同 64:ff9b::/32，
	// 实际应只拒 64:ff9b::/96 (well-known) + 64:ff9b:1::/48 (local) 的并集。
	// §4.1 前缀内部：必须继续拒绝（既有安全回归）
	// §4.2 前缀外部、共同 /32 内：当前应 RED（旧实现过度拦截）
	// §4.3 前缀两侧边界：必须保持允许（防偏移再次出错）

	// ---------- §4.1 前缀内部安全回归（应继续 GREEN）----------

	[Fact]
	public void R201b_Nat64_WellKnown_Start_DefaultForbidden()
	{
		// 64:ff9b::/96 起点
		Assert.True(NewsSsrfGuard.IsForbiddenAddress(IPAddress.Parse("64:ff9b::")));
	}

	[Fact]
	public void R201b_Nat64_WellKnown_Inside_DefaultForbidden()
	{
		// 64:ff9b::/96 内
		Assert.True(NewsSsrfGuard.IsForbiddenAddress(IPAddress.Parse("64:ff9b::1")));
	}

	[Fact]
	public void R201b_Nat64_WellKnown_End_DefaultForbidden()
	{
		// 64:ff9b::/96 末端
		Assert.True(NewsSsrfGuard.IsForbiddenAddress(IPAddress.Parse("64:ff9b::ffff:ffff")));
	}

	[Fact]
	public void R201b_Nat64_Local_Start_DefaultForbidden()
	{
		// 64:ff9b:1::/48 起点
		Assert.True(NewsSsrfGuard.IsForbiddenAddress(IPAddress.Parse("64:ff9b:1::")));
	}

	[Fact]
	public void R201b_Nat64_Local_End_DefaultForbidden()
	{
		// 64:ff9b:1::/48 末端：64:ff9b:1:ffff:ffff:ffff:ffff:ffff（8 组）
		Assert.True(NewsSsrfGuard.IsForbiddenAddress(IPAddress.Parse("64:ff9b:1:ffff:ffff:ffff:ffff:ffff")));
	}

	[Fact]
	public void R201b_Nat64_WellKnown_Start_Select_AsDnsPrivate()
	{
		// 代表地址：SelectConnectableAddress 必须 dns_private
		var ex = Assert.Throws<NewsImageException>(() =>
			NewsSsrfGuard.SelectConnectableAddress(new[] { IPAddress.Parse("64:ff9b::") }));
		Assert.Equal("dns_private", ex.Kind);
	}

	// ---------- §4.2 前缀外部、共同 /32 内（旧实现 RED）----------

	[Fact]
	public void R201b_Nat64_OutOfPrefix_64ff9b_0_1_Allowed()
	{
		// 64:ff9b:0:1::1 —— 共同 /32 内但不在 /96 或 /48 内，必须允许
		Assert.False(NewsSsrfGuard.IsForbiddenAddress(IPAddress.Parse("64:ff9b:0:1::1")),
			"64:ff9b:0:1::1 共同前 32 位但不在 64:ff9b::/96 也不在 64:ff9b:1::/48，必须允许");
	}

	[Fact]
	public void R201b_Nat64_OutOfPrefix_64ff9b_0_ffff_Allowed()
	{
		// 64:ff9b:0:ffff::1
		Assert.False(NewsSsrfGuard.IsForbiddenAddress(IPAddress.Parse("64:ff9b:0:ffff::1")),
			"64:ff9b:0:ffff::1 共同前 32 位但不在 64:ff9b::/96 也不在 64:ff9b:1::/48，必须允许");
	}

	[Fact]
	public void R201b_Nat64_OutOfPrefix_64ff9b_2_Allowed()
	{
		// 64:ff9b:2::1
		Assert.False(NewsSsrfGuard.IsForbiddenAddress(IPAddress.Parse("64:ff9b:2::1")),
			"64:ff9b:2::1 共同前 32 位但不在两个目标前缀内，必须允许");
	}

	[Fact]
	public void R201b_Nat64_OutOfPrefix_64ff9b_ffff_Allowed()
	{
		// 64:ff9b:ffff::1
		Assert.False(NewsSsrfGuard.IsForbiddenAddress(IPAddress.Parse("64:ff9b:ffff::1")),
			"64:ff9b:ffff::1 共同前 32 位但不在两个目标前缀内，必须允许");
	}

	[Fact]
	public void R201b_Nat64_OutOfPrefix_SelectReturns()
	{
		// SelectConnectableAddress 生产入口：必须返回原地址，不抛异常
		IPAddress selected = NewsSsrfGuard.SelectConnectableAddress(new[] { IPAddress.Parse("64:ff9b:2::1") });
		Assert.Equal(IPAddress.Parse("64:ff9b:2::1"), selected);
	}

	[Fact]
	public void R201b_Nat64_OutOfPrefix_MixedWithPublic_SelectReturns()
	{
		// 共同 /32 内相邻外部地址 + 公网混合：
		// 旧实现误判 64:ff9b:2::1 为 forbidden，会抛 dns_mixed；新实现两地址都合法，应正常返回。
		IPAddress selected = NewsSsrfGuard.SelectConnectableAddress(new[] {
			IPAddress.Parse("64:ff9b:2::1"),
			PublicIp,
		});
		Assert.NotNull(selected);
	}

	// ---------- §4.3 前缀两侧边界（防偏移再次出错）----------

	[Fact]
	public void R201b_Nat64_LeftBoundary_64ff9a_ffff_ffff_Allowed()
	{
		// 64:ff9a:ffff:ffff::1 —— 共同前 32 位之前
		Assert.False(NewsSsrfGuard.IsForbiddenAddress(IPAddress.Parse("64:ff9a:ffff:ffff::1")),
			"64:ff9a:ffff:ffff::1 在 64:ff9b::/96 之前，必须允许");
	}

	[Fact]
	public void R201b_Nat64_RightBoundary_64ff9c_Allowed()
	{
		// 64:ff9c::1 —— 共同前 32 位之后
		Assert.False(NewsSsrfGuard.IsForbiddenAddress(IPAddress.Parse("64:ff9c::1")),
			"64:ff9c::1 在 64:ff9b::/96 之后，必须允许");
	}

	// ---------- 目标前后矩阵（按工作流 §6 用当前编译产物直接观察）----------

	[Fact]
	public void R201b_Matrix_Output()
	{
		var matrix = new (string Addr, bool Expected)[]
		{
			("64:ff9b::a00:1", true),       // 内部 64:ff9b::/96
			("64:ff9b:1::a00:1", true),      // 内部 64:ff9b:1::/48
			("64:ff9b:0:1::1", false),       // 共同 /32 内、目标外
			("64:ff9b:2::1", false),         // 共同 /32 内、目标外
			("64:ff9b:ffff::1", false),      // 共同 /32 内、目标外
		};

		var lines = new System.Text.StringBuilder();
		lines.AppendLine("=== R2-01b target-prefix matrix (current compiled binary) ===");
		foreach (var (addr, expected) in matrix)
		{
			IPAddress ip = IPAddress.Parse(addr);
			bool actual = NewsSsrfGuard.IsForbiddenAddress(ip);
			string verdict = actual == expected ? "OK" : "WRONG";
			lines.AppendLine($"{addr,-22} forbidden={actual,-5} expected={expected,-5} {verdict}");
			Assert.True(actual == expected, $"matrix mismatch: {addr} expected={expected} actual={actual}");
		}

		// 输出到 xUnit 控制台，dotnet test 会保留 stdout
		Console.Write(lines.ToString());
	}
}
