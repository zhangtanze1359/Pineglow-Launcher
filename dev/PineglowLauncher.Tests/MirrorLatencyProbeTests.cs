using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Pineglow.Launcher.Services;
using Xunit;

namespace Pineglow.Launcher.Tests;

/// <summary>
/// 2026-11-06 未覆盖路径补测（批次 4）：<c>MirrorHelper.MeasureLatencyAsync</c> 的**探测口径**。
///
/// 为什么单独拿它出来：覆盖率实测该类 **2/144 = 1.4%**，而它决定"下载源"页上每个源
/// 显示"可用/不可用"和延迟数字。用户据此切换镜像——判错会直接导致下载全失败。
///
/// 这批测试不联外网，用一个记录请求的假 <see cref="HttpMessageHandler"/> 断言：
///   * 探测用的 URL 是怎么拼的；
///   * 是否检查 HTTP 状态码（**不检查**——这是本次实测到的口径缺陷，见测试注释）；
///   * 异常/取消如何降级为 -1。
///
/// 与之对照的真实网络事实（2026-11-06 用 curl 独立核实，见 <c>UnverifiedItemsReport</c> 文档）：
///   GET  https://piston-meta.mojang.com/mc/game/version_manifest_v2.json          → 200
///   HEAD https://piston-meta.mojang.com/version_manifest_v2.json                  → 404
///   GET  https://bmclapi2.bangbang93.com/mc/game/version_manifest_v2.json         → 200
///   HEAD https://bmclapi2.bangbang93.com/version_manifest_v2.json                 → 404
///   GET  https://download.mcbbs.net/...                                           → DNS 不存在
/// 即：<b>三个源的探测路径都是错的</b>（少了 <c>/mc/game/</c> 这一段），
/// 而 <c>MinecraftDownloader.ManifestUrl</c> 用的是**正确**的带 <c>/mc/game/</c> 路径。
/// </summary>
public class MirrorLatencyProbeTests
{
	/// <summary>记录请求并按预设返回的假 handler。</summary>
	private sealed class RecordingHandler : HttpMessageHandler
	{
		public List<Uri> Requests { get; } = new();
		public List<HttpMethod> Methods { get; } = new();
		public Func<HttpRequestMessage, HttpResponseMessage> Responder { get; set; } =
			_ => new HttpResponseMessage(HttpStatusCode.OK);

		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			Requests.Add(request.RequestUri!);
			Methods.Add(request.Method);
			return Task.FromResult(Responder(request));
		}
	}

	[Fact]
	public async Task MeasureLatency_UsesHeadOnVersionManifestPath_UnderBaseUrl()
	{
		var handler = new RecordingHandler();
		using var http = new HttpClient(handler);

		await MirrorHelper.MeasureLatencyAsync(http, "https://bmclapi2.bangbang93.com");

		var uri = Assert.Single(handler.Requests);
		Assert.Equal(HttpMethod.Head, handler.Methods[0]);
		Assert.Equal("https://bmclapi2.bangbang93.com/version_manifest_v2.json", uri.ToString());
	}

	[Fact]
	public async Task MeasureLatency_TrimsTrailingSlash_SoDoubleSlashNeverAppears()
	{
		var handler = new RecordingHandler();
		using var http = new HttpClient(handler);

		await MirrorHelper.MeasureLatencyAsync(http, "https://piston-meta.mojang.com///");

		var uri = Assert.Single(handler.Requests);
		Assert.DoesNotContain("//version_manifest", uri.ToString());
		Assert.EndsWith("/version_manifest_v2.json", uri.ToString());
	}

	[Fact]
	public async Task MeasureLatency_ProbeUrl_DoesNotMatchTheRealManifestPath()
	{
		// 这条是**缺陷固化测试**：把"探测路径与真实使用路径不一致"钉成可执行的断言，
		// 而不是只在报告里写一句。一旦有人修好，这条测试会红，逼他回来改注释与文档。
		//
		// 真实清单地址（MinecraftDownloader.ManifestUrl）：
		//   https://piston-meta.mojang.com/mc/game/version_manifest_v2.json
		// 探测地址（本类构造）：
		//   {baseUrl}/version_manifest_v2.json
		// 两者相差 "/mc/game" 一段。
		var handler = new RecordingHandler();
		using var http = new HttpClient(handler);

		await MirrorHelper.MeasureLatencyAsync(http, "https://piston-meta.mojang.com");

		string probe = Assert.Single(handler.Requests).ToString();
		const string realManifest = "https://piston-meta.mojang.com/mc/game/version_manifest_v2.json";

		Assert.NotEqual(realManifest, probe);
		Assert.DoesNotContain("/mc/game/", probe);

		// 注释里记录这个不一致的后果：HEAD 该路径真实返回 404，但见下一条测试——
		// 实现并不检查状态码，所以"404 仍被判为可用"。
	}

	[Fact]
	public async Task MeasureLatency_IgnoresHttpStatus_BecauseItOnlyMeasuresTransport()
	{
		// 把"不检查状态码"这个行为钉住：返回 404 也算成功，只要连接建立起来。
		// 后果：下载源页的 available=true 只证明"能连上"，不证明"能取到清单"。
		var handler = new RecordingHandler { Responder = _ => new HttpResponseMessage(HttpStatusCode.NotFound) };
		using var http = new HttpClient(handler);

		long latency = await MirrorHelper.MeasureLatencyAsync(http, "https://piston-meta.mojang.com");

		Assert.True(latency >= 0, "404 也算连通，返回了非负延迟 → 上层会把 available 判为 true");
	}

	[Fact]
	public async Task MeasureLatency_ServerError_AlsoReportedAsAvailable()
	{
		var handler = new RecordingHandler { Responder = _ => new HttpResponseMessage(HttpStatusCode.InternalServerError) };
		using var http = new HttpClient(handler);

		long latency = await MirrorHelper.MeasureLatencyAsync(http, "https://example.invalid");

		Assert.True(latency >= 0);
	}

	[Fact]
	public async Task MeasureLatency_TransportFailure_ReturnsMinusOne()
	{
		var handler = new RecordingHandler
		{
			Responder = _ => throw new HttpRequestException("boom"),
		};
		using var http = new HttpClient(handler);

		long latency = await MirrorHelper.MeasureLatencyAsync(http, "https://example.invalid");

		Assert.Equal(-1, latency);
	}

	[Fact]
	public async Task MeasureLatency_BlankBaseUrl_SkipsRequestEntirely()
	{
		var handler = new RecordingHandler();
		using var http = new HttpClient(handler);

		Assert.Equal(-1, await MirrorHelper.MeasureLatencyAsync(http, ""));
		Assert.Equal(-1, await MirrorHelper.MeasureLatencyAsync(http, "   "));
		Assert.Empty(handler.Requests);
	}

	[Fact]
	public async Task MeasureLatency_Timeout_LandsAsMinusOne_NotAsFasterNumber()
	{
		// 超时不能被记成一个"很快的延迟"——那会让慢源看起来最优。
		var handler = new RecordingHandler
		{
			Responder = _ => throw new TaskCanceledException("timeout"),
		};
		using var http = new HttpClient(handler);

		Assert.Equal(-1, await MirrorHelper.MeasureLatencyAsync(http, "https://example.invalid"));
	}

	[Fact]
	public void ListSources_ThreeKeys_AllHaveHttpsBaseUrl_AndLatencyStartsUnmeasured()
	{
		foreach (var src in MirrorHelper.ListSources())
		{
			Assert.StartsWith("https://", src.BaseUrl);
			// LatencyMs 初值就是 -1，含义是"尚未测量"。上层 HandleListSourcesAsync
			// 会立刻覆盖它；如果哪个调用方拿 ListSources() 直接展示，就会看到"未测"而不是假的 0ms。
			Assert.Equal(-1, src.LatencyMs);
			Assert.True(src.Available, "初值视为可用，测速失败后才降级");
		}
	}
}
