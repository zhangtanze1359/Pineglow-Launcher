using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Pineglow.Launcher.Models;
using Pineglow.Launcher.Services;
using Xunit;
using Xunit.Abstractions;

namespace Pineglow.Launcher.Tests;

/// <summary>
/// R03 聚焦测试：**更新清单入口的可用性与降级**。
///
/// 原始故障（docs/release-readiness-review-2026-10-05.md R03）：
///   默认主源（Gitee 仓库固定 update.json）实测返回 404；而旧实现只认这一条地址，
///   清单取不到就直接失败——资产级 fallbackUrl 救不了这一层，
///   因为 fallbackUrl 只有在**已经拿到清单**之后才有意义。
///
/// 本类用本地假 HTTP 服务器驱动地址链，覆盖：
///   ① 主源成功、不降级；
///   ② 主源 404 / 坏 JSON / 字段非法 / 超时 → 逐类降级到备用源，并给出可读原因；
///   ③ 全部失败 → 明确失败摘要（主源 + 各备用源原因），不假装成功；
///   ④ 地址链组成：用户自定义源优先、内置主源与备用源在后、去重、丢弃非 http(s)；
///   ⑤ 清单字段校验（version / HTTPS url / SHA256 / size）与日志脱敏；
///   ⑥ 调用方取消不触发降级。
/// </summary>
public class UpdateManifestFallbackTests
{
	private readonly ITestOutputHelper _out;

	public UpdateManifestFallbackTests(ITestOutputHelper output)
	{
		_out = output;
	}

	private const string ValidAssetUrl = "https://example.com/Pineglow-Launcher-win-x64-single-file.exe";

	private sealed class ManifestHandler : HttpMessageHandler
	{
		public readonly List<string> Requests = new();
		public Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> Respond = (_, _) =>
			Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
		{
			lock (Requests) Requests.Add(request.RequestUri!.AbsolutePath);
			return Respond(request, ct);
		}
	}

	[Fact]
	public async Task StalePrimary_NewerMirrorWinsWithItsReleaseNotes()
	{
		using ManifestHandler handler = new();
		handler.Respond = (r, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
		{
			Content = new StringContent(ValidManifestJson(r.RequestUri!.AbsolutePath == "/old" ? "1.0.0" : "9.0.0")
				.Replace("test", "新版介绍\\n修复更新提醒"))
		});
		using HttpClient http = new(handler);
		var result = await UpdateService.FetchManifestChainAsync(http,
			new[] { "https://example.com/old", "https://example.com/new" }, CancellationToken.None);
		Assert.Equal("9.0.0", result.Manifest!.Version);
		Assert.Equal("新版介绍\n修复更新提醒", result.Manifest.Notes);
		Assert.Equal("https://example.com/new", result.SourceUrl);
		Assert.True(result.UsedFallback);
	}

	[Theory]
	[InlineData("2.0.0", "1.0.0")]
	[InlineData("2.0.0", "2.0.0")]
	public async Task ManifestSelection_PrimaryNewerOrEqualKeepsPriority(string primary, string mirror)
	{
		using ManifestHandler handler = new();
		handler.Respond = (r, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
		{ Content = new StringContent(ValidManifestJson(r.RequestUri!.AbsolutePath == "/primary" ? primary : mirror)) });
		using HttpClient http = new(handler);
		var result = await UpdateService.FetchManifestChainAsync(http,
			new[] { "https://example.com/primary", "https://example.com/mirror" }, CancellationToken.None);
		Assert.Equal("https://example.com/primary", result.SourceUrl);
		Assert.False(result.UsedFallback);
		Assert.Equal(2, result.Attempts.Count);
	}

	[Theory]
	[InlineData(404, "")]
	[InlineData(200, "not-json")]
	[InlineData(200, "{}")]
	public async Task ManifestSelection_BrokenPrimaryDoesNotDiscardMirror(int status, string body)
	{
		using ManifestHandler handler = new();
		handler.Respond = (r, _) => Task.FromResult(r.RequestUri!.AbsolutePath == "/primary"
			? new HttpResponseMessage((HttpStatusCode)status) { Content = new StringContent(body) }
			: new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(ValidManifestJson("3.0.0")) });
		using HttpClient http = new(handler);
		var result = await UpdateService.FetchManifestChainAsync(http,
			new[] { "https://example.com/primary", "https://example.com/mirror" }, CancellationToken.None);
		Assert.Equal("3.0.0", result.Manifest!.Version);
		Assert.False(result.Attempts[0].Ok);
		Assert.True(result.Attempts[1].Ok);
	}

	[Fact]
	public async Task ManifestSelection_SourcesRunConcurrentlyAndTimeoutIndependently()
	{
		using ManifestHandler handler = new();
		var mirrorStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		handler.Respond = async (r, ct) =>
		{
			if (r.RequestUri!.AbsolutePath == "/primary")
			{
				await mirrorStarted.Task.WaitAsync(ct);
				await Task.Delay(Timeout.Infinite, ct);
			}
			mirrorStarted.TrySetResult();
			return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(ValidManifestJson("3.0.0")) };
		};
		using HttpClient http = new(handler);
		var result = await UpdateService.FetchManifestChainAsync(http,
			new[] { "https://example.com/primary", "https://example.com/mirror" }, CancellationToken.None, TimeSpan.FromMilliseconds(150));
		Assert.True(mirrorStarted.Task.IsCompletedSuccessfully);
		Assert.Equal("3.0.0", result.Manifest!.Version);
		Assert.Contains("超时", result.Attempts[0].Reason);
	}

	[Fact]
	public async Task ManifestSelection_AllFailReportsBothAndCancellationPropagates()
	{
		using ManifestHandler handler = new();
		using HttpClient http = new(handler);
		string[] chain = { "https://example.com/primary", "https://example.com/mirror" };
		var result = await UpdateService.FetchManifestChainAsync(http, chain, CancellationToken.None);
		Assert.False(result.Ok);
		Assert.Equal(2, result.Attempts.Count);
		Assert.Contains("404", result.FailureSummary);
		using var cts = new CancellationTokenSource();
		cts.Cancel();
		await Assert.ThrowsAnyAsync<OperationCanceledException>(() => UpdateService.FetchManifestChainAsync(http, chain, cts.Token));
	}

	[Theory]
	[InlineData("v2.0.0", "新增功能\n修复问题", "新增功能\n修复问题")]
	[InlineData("v1.0.0", "其他版本说明", "manifest notes")]
	[InlineData("v2.0.0", "", "manifest notes")]
	public async Task ReleaseNotes_OnlyMatchingTagBodyOverridesManifest(string tag, string body, string expected)
	{
		using ManifestHandler handler = new();
		handler.Respond = (r, _) =>
		{
			Assert.Equal("api.github.com", r.RequestUri!.Host);
			Assert.EndsWith("/releases/tags/v2.0.0", r.RequestUri.AbsolutePath);
			Assert.NotEmpty(r.Headers.UserAgent);
			return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
			{ Content = new StringContent(JsonSerializer.Serialize(new { tag_name = tag, body })) });
		};
		using HttpClient http = new(handler);
		var manifest = new UpdateManifest { Version = "2.0.0", Notes = "manifest notes",
			Url = "https://gitee.com/example.exe",
			FallbackUrl = "https://github.com/zhangtanze1359/Pineglow-Launcher/releases/download/v2.0.0/Pineglow-Launcher-win-x64-single-file.exe" };
		await UpdateService.EnrichReleaseNotesAsync(http, manifest, CancellationToken.None);
		Assert.Equal(expected, manifest.Notes);
	}

	[Fact]
	public async Task ReleaseNotes_OfflineKeepsPackagedNotesAndOtherRepositoriesAreIgnored()
	{
		using ManifestHandler handler = new();
		using HttpClient http = new(handler);
		var manifest = new UpdateManifest { Version = "2.0.0", Notes = "离线可读介绍",
			Url = "https://github.com/zhangtanze1359/Pineglow-Launcher/releases/download/v2.0.0/Pineglow-Launcher-win-x64-single-file.exe" };
		await UpdateService.EnrichReleaseNotesAsync(http, manifest, CancellationToken.None);
		Assert.Equal("离线可读介绍", manifest.Notes);
		Assert.Single(handler.Requests);
		manifest.Url = manifest.Url.Replace("zhangtanze1359", "other");
		await UpdateService.EnrichReleaseNotesAsync(http, manifest, CancellationToken.None);
		Assert.Single(handler.Requests);
	}

	private static string ValidManifestJson(string version = "1.2.3")
	{
		return JsonSerializer.Serialize(new
		{
			version,
			url = ValidAssetUrl,
			fallbackUrl = "https://example.com/fallback.exe",
			size = 123456L,
			sha256 = new string('a', 64),
			notes = "test",
			mandatory = false
		});
	}

	private static int FreeLoopbackPort()
	{
		TcpListener listener = new TcpListener(IPAddress.Loopback, 0);
		listener.Start();
		int port = ((IPEndPoint)listener.LocalEndpoint).Port;
		listener.Stop();
		return port;
	}

	/* ===================== 本地假清单服务器 ===================== */

	private sealed class FakeServer : IDisposable
	{
		private readonly HttpListener _listener = new HttpListener();
		private readonly Dictionary<string, Func<HttpListenerContext, Task>> _routes =
			new Dictionary<string, Func<HttpListenerContext, Task>>(StringComparer.OrdinalIgnoreCase);
		private readonly CancellationTokenSource _cts = new CancellationTokenSource();

		public string BaseUrl { get; }

		public FakeServer()
		{
			int port = FreeLoopbackPort();
			BaseUrl = $"http://127.0.0.1:{port}/";
			_listener.Prefixes.Add(BaseUrl);
			_listener.Start();
			_ = Task.Run(LoopAsync);
		}

		public string Url(string path) => BaseUrl.TrimEnd('/') + path;

		public void Map(string path, Func<HttpListenerContext, Task> handler) => _routes[path] = handler;

		public void MapJson(string path, string json) => Map(path, async ctx =>
		{
			byte[] bytes = Encoding.UTF8.GetBytes(json);
			ctx.Response.StatusCode = 200;
			ctx.Response.ContentType = "application/json; charset=utf-8";
			ctx.Response.ContentLength64 = bytes.Length;
			await ctx.Response.OutputStream.WriteAsync(bytes, 0, bytes.Length);
		});

		public void MapStatus(string path, int status) => Map(path, ctx =>
		{
			ctx.Response.StatusCode = status;
			return Task.CompletedTask;
		});

		public void MapDelay(string path, TimeSpan delay) => Map(path, async ctx =>
		{
			await Task.Delay(delay, _cts.Token);
			byte[] bytes = Encoding.UTF8.GetBytes(ValidManifestJson());
			ctx.Response.StatusCode = 200;
			ctx.Response.ContentType = "application/json; charset=utf-8";
			ctx.Response.ContentLength64 = bytes.Length;
			await ctx.Response.OutputStream.WriteAsync(bytes, 0, bytes.Length);
		});

		private async Task LoopAsync()
		{
			while (!_cts.IsCancellationRequested)
			{
				HttpListenerContext? ctx = null;
				try
				{
					ctx = await _listener.GetContextAsync();
				}
				catch
				{
					return;
				}

				_ = Task.Run(async () =>
				{
					try
					{
						string path = ctx.Request.Url?.AbsolutePath ?? "/";
						if (_routes.TryGetValue(path, out Func<HttpListenerContext, Task>? handler))
						{
							await handler(ctx);
						}
						else
						{
							ctx.Response.StatusCode = 404;
						}
					}
					catch
					{
						// 客户端提前中止（超时用例）会在这里抛，忽略即可
					}
					finally
					{
						try { ctx.Response.Close(); } catch { }
					}
				});
			}
		}

		public void Dispose()
		{
			try { _cts.Cancel(); } catch { }
			try { _listener.Stop(); _listener.Close(); } catch { }
			_cts.Dispose();
		}
	}

	/* ===================== ① 主源成功 ===================== */

	[Fact]
	public async Task PrimaryNewer_RemainsSelectedAfterCheckingMirror()
	{
		using FakeServer server = new FakeServer();
		server.MapJson("/primary.json", ValidManifestJson("2.0.0"));
		server.MapJson("/mirror.json", ValidManifestJson("1.0.0"));
		using HttpClient http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };

		UpdateService.ManifestFetchResult result = await UpdateService.FetchManifestChainAsync(
			http, new[] { server.Url("/primary.json"), server.Url("/mirror.json") }, CancellationToken.None);

		Assert.True(result.Ok);
		Assert.False(result.UsedFallback);
		Assert.Equal("2.0.0", result.Manifest!.Version);
		Assert.Equal(2, result.Attempts.Count);
		Assert.Equal(server.Url("/primary.json"), result.SourceUrl);
	}

	/* ===================== ② 四类主源故障都能降级 ===================== */

	[Fact]
	public async Task Primary404_FallsBackToMirror()
	{
		using FakeServer server = new FakeServer();
		server.MapStatus("/primary.json", 404);
		server.MapJson("/mirror.json", ValidManifestJson("2.0.0"));
		using HttpClient http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };

		UpdateService.ManifestFetchResult result = await UpdateService.FetchManifestChainAsync(
			http, new[] { server.Url("/primary.json"), server.Url("/mirror.json") }, CancellationToken.None);

		Assert.True(result.Ok);
		Assert.True(result.UsedFallback);
		Assert.Equal(server.Url("/mirror.json"), result.SourceUrl);
		Assert.Equal(2, result.Attempts.Count);
		Assert.Contains("404", result.Attempts[0].Reason);
		_out.WriteLine($"[R03] 主源 404 → 降级成功，原因='{result.Attempts[0].Reason}'");
	}

	[Fact]
	public async Task PrimaryBadJson_FallsBackToMirror()
	{
		using FakeServer server = new FakeServer();
		server.MapJson("/primary.json", "{ this is not json");
		server.MapJson("/mirror.json", ValidManifestJson("2.0.0"));
		using HttpClient http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };

		UpdateService.ManifestFetchResult result = await UpdateService.FetchManifestChainAsync(
			http, new[] { server.Url("/primary.json"), server.Url("/mirror.json") }, CancellationToken.None);

		Assert.True(result.Ok);
		Assert.True(result.UsedFallback);
		Assert.Contains("JSON", result.Attempts[0].Reason);
	}

	[Fact]
	public async Task PrimaryMissingSha256_FallsBackToMirror()
	{
		using FakeServer server = new FakeServer();
		server.MapJson("/primary.json", JsonSerializer.Serialize(new
		{
			version = "2.0.0",
			url = ValidAssetUrl,
			size = 10L,
			sha256 = string.Empty
		}));
		server.MapJson("/mirror.json", ValidManifestJson("2.0.0"));
		using HttpClient http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };

		UpdateService.ManifestFetchResult result = await UpdateService.FetchManifestChainAsync(
			http, new[] { server.Url("/primary.json"), server.Url("/mirror.json") }, CancellationToken.None);

		Assert.True(result.Ok);
		Assert.True(result.UsedFallback);
		Assert.Contains("SHA256", result.Attempts[0].Reason);
	}

	[Fact]
	public async Task PrimaryTimeout_FallsBackToMirror()
	{
		using FakeServer server = new FakeServer();
		server.MapDelay("/primary.json", TimeSpan.FromSeconds(10));
		server.MapJson("/mirror.json", ValidManifestJson("2.0.0"));
		using HttpClient http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };

		Stopwatch sw = Stopwatch.StartNew();
		UpdateService.ManifestFetchResult result = await UpdateService.FetchManifestChainAsync(
			http,
			new[] { server.Url("/primary.json"), server.Url("/mirror.json") },
			CancellationToken.None,
			perAttemptTimeout: TimeSpan.FromSeconds(1));
		sw.Stop();

		Assert.True(result.Ok);
		Assert.True(result.UsedFallback);
		Assert.Contains("超时", result.Attempts[0].Reason);
		Assert.True(sw.Elapsed < TimeSpan.FromSeconds(8), $"单次尝试超时必须生效，实测 {sw.Elapsed.TotalSeconds:N1}s");
		_out.WriteLine($"[R03] 主源超时 → 降级成功，耗时 {sw.Elapsed.TotalSeconds:N1}s，原因='{result.Attempts[0].Reason}'");
	}

	/* ===================== ③ 全失败 ===================== */

	[Fact]
	public async Task AllSourcesFail_ReportsEveryReason()
	{
		using FakeServer server = new FakeServer();
		server.MapStatus("/primary.json", 404);
		server.MapStatus("/mirror.json", 500);
		using HttpClient http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };

		UpdateService.ManifestFetchResult result = await UpdateService.FetchManifestChainAsync(
			http, new[] { server.Url("/primary.json"), server.Url("/mirror.json") }, CancellationToken.None);

		Assert.False(result.Ok);
		Assert.Null(result.Manifest);
		Assert.Equal(2, result.Attempts.Count);
		Assert.All(result.Attempts, a => Assert.False(a.Ok));
		Assert.Contains("主源", result.FailureSummary);
		Assert.Contains("备用源", result.FailureSummary);
		Assert.Contains("404", result.FailureSummary);
		Assert.DoesNotContain("OK", result.FailureSummary);
		_out.WriteLine("[R03] 全失败摘要: " + result.FailureSummary);
	}

	/* ===================== ④ 地址链 ===================== */

	[Fact]
	public void UrlChain_CustomFirstThenBuiltIns_Deduped()
	{
		IReadOnlyList<string> chain = UpdateService.BuildManifestUrlChain("https://example.com/custom.json");

		Assert.Equal("https://example.com/custom.json", chain[0]);
		Assert.Contains(ConfigStore.DefaultUpdateUrl, chain);
		Assert.Contains(ConfigStore.DefaultUpdateMirrorUrls[0], chain);
		Assert.Equal(chain.Count, chain.Distinct(StringComparer.OrdinalIgnoreCase).Count());

		// 用户配置就是内置主源时不应重复
		IReadOnlyList<string> same = UpdateService.BuildManifestUrlChain(ConfigStore.DefaultUpdateUrl);
		Assert.Equal(1, same.Count(u => string.Equals(u, ConfigStore.DefaultUpdateUrl, StringComparison.OrdinalIgnoreCase)));
	}

	[Fact]
	public void UrlChain_RejectsNonHttpAndBlank()
	{
		IReadOnlyList<string> chain = UpdateService.BuildManifestUrlChain("   ");
		Assert.DoesNotContain(chain, u => string.IsNullOrWhiteSpace(u));
		Assert.All(chain, u => Assert.StartsWith("https://", u, StringComparison.OrdinalIgnoreCase));

		IReadOnlyList<string> fileChain = UpdateService.BuildManifestUrlChain("file:///C:/temp/update.json");
		Assert.DoesNotContain(fileChain, u => u.StartsWith("file:", StringComparison.OrdinalIgnoreCase));
	}

	[Fact]
	public void DefaultMirrors_ArePresentAndHttps()
	{
		Assert.NotEmpty(ConfigStore.DefaultUpdateMirrorUrls);
		Assert.All(ConfigStore.DefaultUpdateMirrorUrls, u =>
		{
			Assert.True(Uri.TryCreate(u, UriKind.Absolute, out Uri? uri));
			Assert.Equal(Uri.UriSchemeHttps, uri!.Scheme);
		});
		Assert.DoesNotContain(ConfigStore.DefaultUpdateUrl, ConfigStore.DefaultUpdateMirrorUrls);
	}

	/* ===================== ⑤ 字段校验与脱敏 ===================== */

	[Theory]
	[InlineData("{\"url\":\"https://e.com/a.exe\",\"sha256\":\"" + "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa" + "\",\"size\":1}")]
	[InlineData("{\"version\":\"1.0.0\",\"url\":\"http://e.com/a.exe\",\"sha256\":\"" + "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa" + "\",\"size\":1}")]
	[InlineData("{\"version\":\"1.0.0\",\"url\":\"https://e.com/a.exe\",\"sha256\":\"short\",\"size\":1}")]
	[InlineData("{\"version\":\"1.0.0\",\"url\":\"https://e.com/a.exe\",\"sha256\":\"" + "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa" + "\",\"size\":-5}")]
	public void ValidateManifest_RejectsInvalidPayloads(string json)
	{
		UpdateManifest? manifest = JsonSerializer.Deserialize<UpdateManifest>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
		InvalidDataException ex = Assert.Throws<InvalidDataException>(() => UpdateService.ValidateManifest(manifest));
		Assert.False(string.IsNullOrWhiteSpace(ex.Message));
		_out.WriteLine("[R03] 拒绝原因: " + ex.Message);
	}

	[Fact]
	public void ValidateManifest_AcceptsReleaseShape()
	{
		UpdateManifest manifest = new UpdateManifest
		{
			Version = "1.0.1",
			Url = ValidAssetUrl,
			Size = 53048626,
			Sha256 = new string('b', 64)
		};
		UpdateService.ValidateManifest(manifest);
	}

	[Fact]
	public void Redact_RemovesQueryButKeepsHostAndPath()
	{
		Assert.Equal(
			"https://example.com/update.json",
			UpdateService.Redact("https://example.com/update.json?access_token=secret"));
		Assert.Equal("(invalid-url)", UpdateService.Redact("not a url"));
	}

	/* ===================== ⑥ 取消不降级 ===================== */

	[Fact]
	public async Task CallerCancellation_DoesNotFallBack()
	{
		using FakeServer server = new FakeServer();
		server.MapDelay("/primary.json", TimeSpan.FromSeconds(10));
		server.MapJson("/mirror.json", ValidManifestJson("2.0.0"));
		using HttpClient http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
		using CancellationTokenSource cts = new CancellationTokenSource();
		cts.CancelAfter(TimeSpan.FromMilliseconds(300));
		Stopwatch sw = Stopwatch.StartNew();

		await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
			await UpdateService.FetchManifestChainAsync(
				http,
				new[] { server.Url("/primary.json"), server.Url("/mirror.json") },
				cts.Token,
				perAttemptTimeout: TimeSpan.FromSeconds(30)));
		sw.Stop();
		Assert.True(sw.Elapsed < TimeSpan.FromSeconds(8), "用户取消必须立刻结束，不应继续尝试备用源");
	}
}

/// <summary>
/// R03 API 级契约测试：<c>/api/update/check</c> 的响应必须让界面能判断「用了哪个源、每个源为什么失败」。
///
/// 与 <see cref="UpdateManifestFallbackTests"/> 的分工：
///   - 那一类直接驱动服务层降级逻辑；
///   - 这一类验证 HTTP 层把降级结果如实映射成响应字段（manifestSource / usedFallback / manifestSources），
///     并用 LocalApiService 的测试缝把地址链换成本地假服务器，**不打真实外网**。
/// </summary>
[Collection("LocalApiService")]
public class UpdateManifestApiTests
{
	private readonly ITestOutputHelper _out;

	public UpdateManifestApiTests(ITestOutputHelper output)
	{
		_out = output;
	}

	private static string ValidManifestJson(string version)
	{
		return JsonSerializer.Serialize(new
		{
			version,
			url = "https://example.com/Pineglow-Launcher-win-x64-single-file.exe",
			size = 53048626L,
			sha256 = new string('c', 64),
			notes = "api-test",
			mandatory = false
		});
	}

	private static int FreeLoopbackPort()
	{
		TcpListener listener = new TcpListener(IPAddress.Loopback, 0);
		listener.Start();
		int port = ((IPEndPoint)listener.LocalEndpoint).Port;
		listener.Stop();
		return port;
	}

	private sealed class TinyServer : IDisposable
	{
		private readonly HttpListener _listener = new HttpListener();
		private readonly Dictionary<string, (int Status, string Body)> _routes = new Dictionary<string, (int, string)>(StringComparer.OrdinalIgnoreCase);
		private readonly CancellationTokenSource _cts = new CancellationTokenSource();

		public string BaseUrl { get; }

		public TinyServer()
		{
			int port = FreeLoopbackPort();
			BaseUrl = $"http://127.0.0.1:{port}/";
			_listener.Prefixes.Add(BaseUrl);
			_listener.Start();
			_ = Task.Run(LoopAsync);
		}

		public string Url(string path) => BaseUrl.TrimEnd('/') + path;

		public void Map(string path, int status, string body) => _routes[path] = (status, body);

		private async Task LoopAsync()
		{
			while (!_cts.IsCancellationRequested)
			{
				HttpListenerContext ctx;
				try { ctx = await _listener.GetContextAsync(); }
				catch { return; }

				_ = Task.Run(async () =>
				{
					try
					{
						string path = ctx.Request.Url?.AbsolutePath ?? "/";
						if (_routes.TryGetValue(path, out (int Status, string Body) route))
						{
							byte[] bytes = Encoding.UTF8.GetBytes(route.Body);
							ctx.Response.StatusCode = route.Status;
							ctx.Response.ContentType = "application/json; charset=utf-8";
							ctx.Response.ContentLength64 = bytes.Length;
							await ctx.Response.OutputStream.WriteAsync(bytes, 0, bytes.Length);
						}
						else
						{
							ctx.Response.StatusCode = 404;
						}
					}
					catch { }
					finally { try { ctx.Response.Close(); } catch { } }
				});
			}
		}

		public void Dispose()
		{
			try { _cts.Cancel(); } catch { }
			try { _listener.Stop(); _listener.Close(); } catch { }
			_cts.Dispose();
		}
	}

	private static string UpdateUrlTempDir()
	{
		string dir = Path.Combine(Path.GetTempPath(), "pineglow-update-api-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(dir);
		return dir;
	}

	[Fact]
	public async Task CheckUpdate_StalePrimary_UsesNewerMirrorAndNotes()
	{
		using TinyServer server = new TinyServer();
		server.Map("/primary.json", 200, ValidManifestJson("1.0.0"));
		server.Map("/mirror.json", 200, ValidManifestJson("9.9.9").Replace("api-test", "新增自动提醒\\n修复更新发现"));
		string gameDir = UpdateUrlTempDir();
		var original = LocalApiService.UpdateUrlChainOverrideForTest;
		try
		{
			LocalApiService.UpdateUrlChainOverrideForTest = _ => new[] { server.Url("/primary.json"), server.Url("/mirror.json") };
			await using LocalApiTestHarness h = await LocalApiTestHarness.StartAsync(new AppConfig { GameDirectory = gameDir });
			var (status, body) = await h.SendAsync(HttpMethod.Get, "/api/update/check");
			_out.WriteLine(body);
			Assert.Equal(200, status);
			using JsonDocument doc = JsonDocument.Parse(body);
			Assert.True(doc.RootElement.GetProperty("updateAvailable").GetBoolean());
			Assert.Equal("9.9.9", doc.RootElement.GetProperty("latestVersion").GetString());
			Assert.Equal("新增自动提醒\n修复更新发现", doc.RootElement.GetProperty("notes").GetString());
			Assert.Equal(server.Url("/mirror.json"), doc.RootElement.GetProperty("manifestSource").GetString());
		}
		finally
		{
			LocalApiService.UpdateUrlChainOverrideForTest = original;
			try { Directory.Delete(gameDir, true); } catch { }
		}
	}

	[Fact]
	public async Task CheckUpdate_PrimaryFails_FallsBackAndReportsSource()
	{
		using TinyServer server = new TinyServer();
		server.Map("/primary.json", 404, string.Empty);
		server.Map("/mirror.json", 200, ValidManifestJson("9.9.9"));
		string gameDir = UpdateUrlTempDir();
		Func<string?, IReadOnlyList<string>>? original = LocalApiService.UpdateUrlChainOverrideForTest;
		try
		{
			LocalApiService.UpdateUrlChainOverrideForTest = _ => new[] { server.Url("/primary.json"), server.Url("/mirror.json") };
			AppConfig config = new AppConfig { GameDirectory = gameDir, UpdateUrl = server.Url("/primary.json") };
			await using LocalApiTestHarness h = await LocalApiTestHarness.StartAsync(config);

			(int status, string body) = await h.SendAsync(HttpMethod.Get, "/api/update/check");
			_out.WriteLine("[R03][api] " + body);
			Assert.Equal(200, status);
			using JsonDocument doc = JsonDocument.Parse(body);
			JsonElement root = doc.RootElement;
			Assert.True(root.GetProperty("updateAvailable").GetBoolean());
			Assert.Equal("9.9.9", root.GetProperty("latestVersion").GetString());
			Assert.True(root.GetProperty("usedFallback").GetBoolean());
			Assert.Equal(server.Url("/mirror.json"), root.GetProperty("manifestSource").GetString());
			Assert.False(root.TryGetProperty("error", out _), "降级成功时不应报错");
		}
		finally
		{
			LocalApiService.UpdateUrlChainOverrideForTest = original;
			try { if (Directory.Exists(gameDir)) Directory.Delete(gameDir, true); } catch { }
		}
	}

	[Fact]
	public async Task CheckUpdate_AllSourcesFail_ReportsPerSourceReasons()
	{
		using TinyServer server = new TinyServer();
		server.Map("/primary.json", 404, string.Empty);
		server.Map("/mirror.json", 500, string.Empty);
		string gameDir = UpdateUrlTempDir();
		Func<string?, IReadOnlyList<string>>? original = LocalApiService.UpdateUrlChainOverrideForTest;
		try
		{
			LocalApiService.UpdateUrlChainOverrideForTest = _ => new[] { server.Url("/primary.json"), server.Url("/mirror.json") };
			AppConfig config = new AppConfig { GameDirectory = gameDir, UpdateUrl = server.Url("/primary.json") };
			await using LocalApiTestHarness h = await LocalApiTestHarness.StartAsync(config);

			(int status, string body) = await h.SendAsync(HttpMethod.Get, "/api/update/check");
			_out.WriteLine("[R03][api] " + body);
			Assert.Equal(200, status);
			using JsonDocument doc = JsonDocument.Parse(body);
			JsonElement root = doc.RootElement;
			Assert.True(root.GetProperty("enabled").GetBoolean(), "仍应报告更新功能是开启的（只是源不可用）");
			Assert.True(root.TryGetProperty("error", out JsonElement error));
			Assert.Contains("404", error.GetString());
			Assert.Contains("500", error.GetString());
			JsonElement sources = root.GetProperty("manifestSources");
			Assert.Equal(2, sources.GetArrayLength());
			Assert.False(sources[0].GetProperty("ok").GetBoolean());
			Assert.False(sources[1].GetProperty("ok").GetBoolean());
		}
		finally
		{
			LocalApiService.UpdateUrlChainOverrideForTest = original;
			try { if (Directory.Exists(gameDir)) Directory.Delete(gameDir, true); } catch { }
		}
	}
}
