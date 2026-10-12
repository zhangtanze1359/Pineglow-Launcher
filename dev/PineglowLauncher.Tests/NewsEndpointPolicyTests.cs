using System;
using Pineglow.Launcher.Services;
using Xunit;

namespace Pineglow.Launcher.Tests;

/// <summary>
/// Phase 3A 新闻 URL 策略测试（先红后绿）。
/// 主机清单来自真实官方 feed 证据（docs/phase3a-news-evidence-2026-09-05/baseline/real-feed-snapshot.json）：
/// 链接 host = www.minecraft.net / minecraft.net / aka.ms / www.youtube.com（全部 https、无非默认端口）；
/// feed 与图片 host = launchercontent.mojang.com。
/// 仅允许精确主机匹配，禁止 Contains/后缀匹配。
/// </summary>
public class NewsEndpointPolicyTests
{
	/* ---------------- 新闻外链 ---------------- */

	[Theory]
	[InlineData("https://www.minecraft.net/article/planet-earth-iii")]
	[InlineData("https://minecraft.net/article/new-realms?ref=launcher")]
	[InlineData("https://aka.ms/mce-ListingBlogPost")]
	[InlineData("https://www.youtube.com/watch?v=8fRLEJvtvMA")]
	[InlineData("HTTPS://WWW.MINECRAFT.NET/Article/X")] // 大小写规范化
	[InlineData("https://www.minecraft.net./article/x")] // 单个末尾点规范化
	public void IsAllowedLink_AcceptsExactOfficialHosts(string url)
	{
		Assert.True(NewsEndpointPolicy.IsAllowedLink(url));
	}

	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData("   ")]
	[InlineData("http://www.minecraft.net/article/x")] // 非 HTTPS
	[InlineData("ftp://www.minecraft.net/article/x")]
	[InlineData("javascript:alert(1)")] // 协议注入
	[InlineData("data:text/html,<script>")]
	[InlineData("//www.minecraft.net/article/x")] // 相对地址
	[InlineData("https://user:pass@www.minecraft.net/article/x")] // userinfo
	[InlineData("https://www.minecraft.net:8443/article/x")] // 非默认端口
	[InlineData("https://evil.www.minecraft.net/article/x")] // 子域伪装
	[InlineData("https://www.minecraft.net.evil.com/article/x")] // 恶意后缀
	[InlineData("https://minecraft-net.com/article/x")] // 相似域
	[InlineData("https://www.minecraftnet.com/article/x")]
	[InlineData("https://93.184.216.34/article/x")] // IP literal
	[InlineData("https://127.0.0.1/article/x")]
	[InlineData("https://www.minecraft.net../article/x")] // 双末尾点
	[InlineData("https://xn--mcraft-9ua.com/article/x")] // punycode IDN
	[InlineData("https://www.míncraft.net/article/x")] // Unicode IDN
	[InlineData("https://launchercontent.mojang.com/news.json")] // 官方 feed 域不在链接白名单
	[InlineData("https://www.google.com/search")]
	public void IsAllowedLink_RejectsNonOfficialOrObfuscated(string? url)
	{
		Assert.False(NewsEndpointPolicy.IsAllowedLink(url!));
	}

	/* ---------------- Feed endpoint ---------------- */

	[Fact]
	public void IsAllowedFeedEndpoint_AcceptsOfficialFeed()
	{
		Assert.True(NewsEndpointPolicy.IsAllowedFeedEndpoint(LauncherContentNewsProvider.DefaultEndpoint));
	}

	[Theory]
	[InlineData("http://launchercontent.mojang.com/news.json")]
	[InlineData("https://launchercontent.mojang.com:8080/news.json")]
	[InlineData("https://evil.com/news.json")]
	[InlineData("https://launchercontent.mojang.com.evil.com/news.json")]
	[InlineData("https://www.mojang.com/news.json")]
	[InlineData("https://127.0.0.1/news.json")]
	public void IsAllowedFeedEndpoint_RejectsNonOfficial(string url)
	{
		Assert.False(NewsEndpointPolicy.IsAllowedFeedEndpoint(url));
	}

	/* ---------------- 图片 URL ---------------- */

	[Theory]
	[InlineData("https://launchercontent.mojang.com/images/abc.jpg")]
	[InlineData("https://launchercontent.mojang.com/v1/images/xyz.webp")]
	public void IsAllowedImageUrl_AcceptsOfficialImageHost(string url)
	{
		Assert.True(NewsEndpointPolicy.IsAllowedImageUrl(url));
	}

	[Theory]
	[InlineData("http://launchercontent.mojang.com/images/abc.jpg")]
	[InlineData("https://evil.com/images/abc.png")]
	[InlineData("https://images.mojang.com/x.png")] // 近似官方域
	[InlineData("https://launchercontent.mojang.com:8443/x.png")] // 非默认端口（:443 等价默认端口，Uri 规范化后允许）
	[InlineData("https://169.254.169.254/latest/meta-data")]
	[InlineData("https://user@launchercontent.mojang.com/x.png")]
	public void IsAllowedImageUrl_RejectsNonOfficial(string url)
	{
		Assert.False(NewsEndpointPolicy.IsAllowedImageUrl(url));
	}
}
