using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Pineglow.Launcher.Services;
using Xunit;

namespace Pineglow.Launcher.Tests;

/// <summary>
/// Phase 3A 新闻服务行为测试（先红后绿）。
/// 覆盖：200/304/validators、30 分钟内存新鲜期、6 小时磁盘可展示期、
/// 强刷、错误分类、原子写、并发共享、取消隔离、乱序提交保护、User-Agent。
/// 时间与 HTTP 全部可注入，测试不等待真实 TTL。
/// </summary>
public class MinecraftNewsServiceTests
{
	private sealed class FakeHandler : HttpMessageHandler
	{
		private readonly Queue<Func<HttpRequestMessage, HttpResponseMessage>> _responders = new();
		public List<HttpRequestMessage> Requests { get; } = new();

		public FakeHandler(params Func<HttpRequestMessage, HttpResponseMessage>[] responders)
		{
			foreach (var r in responders) _responders.Enqueue(r);
		}

		public void Enqueue(Func<HttpRequestMessage, HttpResponseMessage> responder) => _responders.Enqueue(responder);

		protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			Requests.Add(request);
			var responder = _responders.Count > 0 ? _responders.Dequeue() : throw new InvalidOperationException("no responder scripted");
			return responder(request);
		}
	}

	private static HttpResponseMessage Json(string body, string? etag = null, string? lastModified = null, HttpStatusCode status = HttpStatusCode.OK)
	{
		var message = new HttpResponseMessage(status)
		{
			Content = new StringContent(body, Encoding.UTF8, "application/json")
		};
		if (etag != null) message.Headers.TryAddWithoutValidation("ETag", etag);
		if (lastModified != null) message.Content.Headers.TryAddWithoutValidation("Last-Modified", lastModified);
		return message;
	}

	private const string FeedBody = """{"entries":[{"id":"a","title":"Alpha","date":"2026-08-01T00:00:00Z"}]}""";
	private const string FeedBodyB = """{"entries":[{"id":"b","title":"Bravo","date":"2026-08-02T00:00:00Z"}]}""";
	private const string ValidatorsEtag = @"""etag-1""";
	private const string ValidatorsLastModified = "Wed, 01 Jul 2026 00:00:00 GMT";

	private static DateTimeOffset _now = new(2026, 9, 5, 12, 0, 0, TimeSpan.Zero);

	private static void TryCleanup(string directory)
	{
		try
		{
			if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
		}
		catch { /* 清理失败不影响测试结论 */ }
	}

	private DateTimeOffset Now() => _now;
	private void Advance(TimeSpan t) => _now = _now.Add(t);

	private MinecraftNewsService CreateService(FakeHandler handler, out FakeHandler captured, TimeSpan? fetchTimeout = null, string? cacheDirectory = null)
	{
		captured = handler;
		string dir = cacheDirectory ?? Path.Combine(Path.GetTempPath(), "pineglow-news3a-" + Guid.NewGuid().ToString("N"));
		return new MinecraftNewsService(
			new LauncherContentNewsProvider(new Func<HttpClient>(() => new HttpClient(handler))),
			cacheDirectory: dir,
			clock: Now,
			fetchTimeout: fetchTimeout ?? TimeSpan.FromSeconds(15));
	}

	private string CacheDir(MinecraftNewsService service) => service.TestCacheDirectory;

	/* ---------------- Provider：排序/去重/UA/条件头 ---------------- */

	[Fact]
	public async Task Provider_SortsAndDeduplicatesByDate_Preserved()
	{
		const string body = """
			{"entries":[
			{"id":"old","title":"Old","date":"2024-01-16T00:00:00Z"},
			{"id":"new","title":"New","date":"2026-08-01T00:00:00Z"},
			{"id":"new-copy","title":"New","date":"2026-08-01T00:00:00Z"},
			{"id":"none","title":"No date","date":"bad-date"}]}
			""";
		var handler = new FakeHandler(_ => Json(body));
		using HttpClient http = new(handler);
		var provider = new LauncherContentNewsProvider(http);

		var result = await provider.FetchAsync(null, null, CancellationToken.None);

		Assert.NotNull(result.Items);
		Assert.Equal(new[] { "New", "Old", "No date" }, result.Items!.Select(i => i.Title).ToArray());
	}

	[Fact]
	public async Task Provider_SendsUserAgent_OnEveryRequest()
	{
		var handler = new FakeHandler(_ => Json(FeedBody, ValidatorsEtag, ValidatorsLastModified));
		using HttpClient http = new(handler);
		var provider = new LauncherContentNewsProvider(http);

		await provider.FetchAsync(null, null, CancellationToken.None);

		Assert.NotEmpty(handler.Requests);
		Assert.True(handler.Requests[0].Headers.TryGetValues("User-Agent", out var uaValues), "User-Agent header must be present");
		Assert.StartsWith("PineglowLauncher/", uaValues.First());
	}

	[Fact]
	public async Task Provider_SecondRequest_CarriesConditionalHeaders()
	{
		var handler = new FakeHandler(
			_ => Json(FeedBody, ValidatorsEtag, ValidatorsLastModified),
			_ => Json(FeedBodyB));
		using HttpClient http = new(handler);
		var provider = new LauncherContentNewsProvider(http);

		var first = await provider.FetchAsync(null, null, CancellationToken.None);
		Assert.NotNull(first.Items);
		Assert.Equal(ValidatorsEtag, first.ETag);
		Assert.Equal(ValidatorsLastModified, first.LastModified);

		var second = await provider.FetchAsync(first.ETag, first.LastModified, CancellationToken.None);

		Assert.Equal(2, handler.Requests.Count);
		Assert.True(handler.Requests[1].Headers.TryGetValues("If-None-Match", out var inm), "If-None-Match header must be present");
		Assert.Equal(ValidatorsEtag, inm.First());
		Assert.NotNull(handler.Requests[1].Headers.IfModifiedSince);
	}

	[Fact]
	public async Task Provider_NotModified304_IsReportedWithoutItems()
	{
		Func<HttpRequestMessage, HttpResponseMessage> respond = request =>
			request.Headers.Contains("If-None-Match")
				? new HttpResponseMessage(HttpStatusCode.NotModified)
				: Json(FeedBody, ValidatorsEtag, ValidatorsLastModified);
		var handler = new FakeHandler(respond, respond);
		using HttpClient http = new(handler);
		var provider = new LauncherContentNewsProvider(http);

		await provider.FetchAsync(null, null, CancellationToken.None);
		var second = await provider.FetchAsync(ValidatorsEtag, ValidatorsLastModified, CancellationToken.None);

		Assert.True(second.NotModified);
		Assert.Null(second.Items);
	}

	/* ---------------- 服务：状态机与缓存信封 ---------------- */

	[Fact]
	public async Task Service_FirstFetch_StateIsLive_WithValidators()
	{
		var service = CreateService(new FakeHandler(_ => Json(FeedBody, ValidatorsEtag, ValidatorsLastModified)), out _);
		try
		{
			var result = await service.FetchAsync();

			Assert.Equal(NewsContentState.Live, result.State);
			Assert.Equal("Alpha", result.Items.Single().Title);
			Assert.Equal(ValidatorsEtag, result.ETag);
			Assert.False(result.IsStale);
			Assert.False(result.CanRetry);
			Assert.Null(result.ErrorKind);
			Assert.Equal(_now, result.ValidatedAt);
		}
		finally { TryCleanup(CacheDir(service)); }
	}

	[Fact]
	public async Task Service_MemoryWithin30Minutes_SkipsNetwork_ReportsMemoryState()
	{
		var service = CreateService(new FakeHandler(_ => Json(FeedBody, ValidatorsEtag, ValidatorsLastModified)), out var handler);
		try
		{
			await service.FetchAsync();
			Advance(TimeSpan.FromMinutes(29));

			var second = await service.FetchAsync();

			Assert.Equal(1, handler.Requests.Count);
			Assert.Equal(NewsContentState.Memory, second.State);
			Assert.Equal("Alpha", second.Items.Single().Title);
		}
		finally { TryCleanup(CacheDir(service)); }
	}

	[Fact]
	public async Task Service_AtExactly30Minutes_SendsConditionalRequest()
	{
		var handler = new FakeHandler(
			_ => Json(FeedBody, ValidatorsEtag, ValidatorsLastModified),
			req => req.Headers.Contains("If-None-Match")
				? new HttpResponseMessage(HttpStatusCode.NotModified)
				: Json(FeedBodyB));
		var service = CreateService(handler, out _);
		try
		{
			await service.FetchAsync();
			Advance(TimeSpan.FromMinutes(30));

			var second = await service.FetchAsync();

			Assert.Equal(2, handler.Requests.Count);
			Assert.True(handler.Requests[1].Headers.Contains("If-None-Match"), "到达 30 分钟必须发送条件请求");
			Assert.Equal(NewsContentState.Revalidated, second.State);
		}
		finally { TryCleanup(CacheDir(service)); }
	}

	[Fact]
	public async Task Service_Revalidated304_KeepsItems_UpdatesValidatedAt_PersistsEnvelope()
	{
		var handler = new FakeHandler(
			_ => Json(FeedBody, ValidatorsEtag, ValidatorsLastModified),
			req => req.Headers.Contains("If-None-Match")
				? new HttpResponseMessage(HttpStatusCode.NotModified)
				: Json(FeedBodyB));
		var service = CreateService(handler, out _);
		try
		{
			var first = await service.FetchAsync();
			Advance(TimeSpan.FromMinutes(40));

			var second = await service.FetchAsync();

			Assert.Equal(NewsContentState.Revalidated, second.State);
			Assert.Equal(first.Items.Select(i => i.Title), second.Items.Select(i => i.Title));
			Assert.Equal(_now, second.ValidatedAt);
			Assert.False(second.IsStale);
			// 信封持久化：新服务实例读到 6 小时内的 validatedAt
			Advance(TimeSpan.FromMinutes(31));
			var thirdService = CreateService(new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)), out _, cacheDirectory: CacheDir(service));
			var cached = await thirdService.FetchAsync();
			Assert.Equal(NewsContentState.Cached, cached.State);
			Assert.Equal("Alpha", cached.Items.Single().Title);
		}
		finally { TryCleanup(CacheDir(service)); }
	}

	[Fact]
	public async Task Service_ForceRefresh_BypassesMemory_ButSendsValidators()
	{
		var handler = new FakeHandler(
			_ => Json(FeedBody, ValidatorsEtag, ValidatorsLastModified),
			req => Json(FeedBodyB, ValidatorsEtag, ValidatorsLastModified));
		var service = CreateService(handler, out _);
		try
		{
			await service.FetchAsync();

			var forced = await service.FetchAsync(forceRefresh: true);

			Assert.Equal(2, handler.Requests.Count);
			Assert.True(handler.Requests[1].Headers.Contains("If-None-Match"), "强刷绕过内存但仍携带 validators");
			Assert.Equal(NewsContentState.Live, forced.State);
			Assert.Equal("Bravo", forced.Items.Single().Title);
		}
		finally { TryCleanup(CacheDir(service)); }
	}

	[Fact]
	public async Task Service_UpstreamFailure_Within6Hours_ServesCachedState()
	{
		var service = CreateService(new FakeHandler(
			_ => Json(FeedBody, ValidatorsEtag, ValidatorsLastModified),
			_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)), out _);
		try
		{
			await service.FetchAsync();
			Advance(TimeSpan.FromHours(1));

			var failed = await service.FetchAsync();

			Assert.Equal(NewsContentState.Cached, failed.State);
			Assert.True(failed.IsStale);
			Assert.True(failed.CanRetry);
			Assert.Equal("Alpha", failed.Items.Single().Title);
			Assert.NotNull(failed.ErrorKind);
		}
		finally { TryCleanup(CacheDir(service)); }
	}

	[Fact]
	public async Task Service_CacheAtExactly6Hours_NotDisplayed()
	{
		var service = CreateService(new FakeHandler(
			_ => Json(FeedBody, ValidatorsEtag, ValidatorsLastModified),
			_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)), out _);
		try
		{
			await service.FetchAsync();
			Advance(TimeSpan.FromHours(6));

			var expired = await service.FetchAsync();

			Assert.NotEqual(NewsContentState.Cached, expired.State);
			Assert.Empty(expired.Items);
			Assert.True(expired.IsStale);
			Assert.True(expired.CanRetry);
		}
		finally { TryCleanup(CacheDir(service)); }
	}

	[Fact]
	public async Task Service_UpstreamFailure_NoCache_ErrorStateWithKind()
	{
		var service = CreateService(new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)), out _);
		try
		{
			var result = await service.FetchAsync();

			Assert.Equal(NewsContentState.Error, result.State);
			Assert.Empty(result.Items);
			Assert.True(result.CanRetry);
			Assert.NotNull(result.ErrorKind);
		}
		finally { TryCleanup(CacheDir(service)); }
	}

	[Fact]
	public async Task Service_Upstream200Empty_EmptyStateWithKind()
	{
		var service = CreateService(new FakeHandler(_ => Json("""{"entries":[]}""", ValidatorsEtag)), out _);
		try
		{
			var result = await service.FetchAsync();

			Assert.Equal(NewsContentState.Empty, result.State);
			Assert.Empty(result.Items);
			Assert.True(result.CanRetry);
			Assert.Equal("empty_response", result.ErrorKind);
		}
		finally { TryCleanup(CacheDir(service)); }
	}

	[Theory]
	[InlineData(429, "rate_limited")]
	[InlineData(500, "http_server")]
	[InlineData(403, "http_client")]
	public async Task Service_HttpStatusClassifiedIntoErrorKind(int status, string expectedKind)
	{
		var service = CreateService(new FakeHandler(_ => new HttpResponseMessage((HttpStatusCode)status)), out _);
		try
		{
			var result = await service.FetchAsync();

			Assert.Equal(NewsContentState.Error, result.State);
			Assert.Equal(expectedKind, result.ErrorKind);
		}
		finally { TryCleanup(CacheDir(service)); }
	}

	private sealed class SlowContent : HttpContent
	{
		private readonly TimeSpan _delay;
		private readonly byte[] _payload;
		public SlowContent(TimeSpan delay, string body) { _delay = delay; _payload = Encoding.UTF8.GetBytes(body); }
		protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context)
		{
			await Task.Delay(_delay);
			await stream.WriteAsync(_payload);
		}
		protected override bool TryComputeLength(out long length) { length = _payload.Length; return true; }
	}

	[Fact]
	public async Task Service_TimeoutClassifiedAsTimeout()
	{
		var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new SlowContent(TimeSpan.FromMilliseconds(500), FeedBody) });
		var service = CreateService(handler, out _, fetchTimeout: TimeSpan.FromMilliseconds(50));
		try
		{
			var result = await service.FetchAsync();

			Assert.Equal(NewsContentState.Error, result.State);
			Assert.Equal("timeout", result.ErrorKind);
		}
		finally { TryCleanup(CacheDir(service)); }
	}

	[Fact]
	public async Task Service_NetworkExceptionClassifiedAsNetwork()
	{
		var service = CreateService(new FakeHandler(_ => throw new HttpRequestException("dns down")), out _);
		try
		{
			var result = await service.FetchAsync();

			Assert.Equal(NewsContentState.Error, result.State);
			Assert.Equal("network", result.ErrorKind);
		}
		finally { TryCleanup(CacheDir(service)); }
	}

	[Fact]
	public async Task Service_MalformedJsonClassifiedAsParse()
	{
		var service = CreateService(new FakeHandler(_ => Json("not-json{")), out _);
		try
		{
			var result = await service.FetchAsync();

			Assert.Equal(NewsContentState.Error, result.State);
			Assert.Equal("parse", result.ErrorKind);
		}
		finally { TryCleanup(CacheDir(service)); }
	}

	/* ---------------- 磁盘缓存健壮性 ---------------- */

	[Fact]
	public async Task Service_CorruptCache_SafelyIgnored()
	{
		string dir = Path.Combine(Path.GetTempPath(), "pineglow-news3a-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(dir);
		await File.WriteAllTextAsync(Path.Combine(dir, "news-cache.json"), "{corrupt!!!");
		try
		{
			var service = CreateService(new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)), out _, cacheDirectory: dir);

			var result = await service.FetchAsync();

			Assert.Equal(NewsContentState.Error, result.State);
			Assert.Empty(result.Items);
		}
		finally { TryCleanup(dir); }
	}

	[Fact]
	public async Task Service_UnknownSchemaVersion_Ignored()
	{
		string dir = Path.Combine(Path.GetTempPath(), "pineglow-news3a-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(dir);
		await File.WriteAllTextAsync(Path.Combine(dir, "news-cache.json"),
			"""{"schemaVersion":1,"source":"launchercontent.mojang.com","items":[{"id":"x","title":"Old","date":"2024-01-01"}],"validatedAt":"2026-09-05T00:00:00Z"}""");
		try
		{
			var service = CreateService(new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)), out _, cacheDirectory: dir);

			var result = await service.FetchAsync();

			Assert.NotEqual(NewsContentState.Cached, result.State);
			Assert.Empty(result.Items);
		}
		finally { TryCleanup(dir); }
	}

	[Fact]
	public async Task Service_SourceMismatch_Ignored()
	{
		string dir = Path.Combine(Path.GetTempPath(), "pineglow-news3a-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(dir);
		await File.WriteAllTextAsync(Path.Combine(dir, "news-cache.json"),
			$$"""{"schemaVersion":2,"source":"evil.example.com","items":[{"id":"x","title":"Injected","date":"2026-09-01"}],"validatedAt":"{{_now:O}}"}""");
		try
		{
			var service = CreateService(new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)), out _, cacheDirectory: dir);

			var result = await service.FetchAsync();

			Assert.NotEqual(NewsContentState.Cached, result.State);
			Assert.Empty(result.Items);
		}
		finally { TryCleanup(dir); }
	}

	[Fact]
	public async Task Service_AtomicWriteFailure_KeepsOldCache()
	{
		var service = CreateService(new FakeHandler(
			_ => Json(FeedBody, ValidatorsEtag, ValidatorsLastModified),
			_ => Json(FeedBodyB, ValidatorsEtag, ValidatorsLastModified),
			_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)), out _);
		try
		{
			await service.FetchAsync();
			// 占用 tmp 路径，使下一次原子写失败
			Directory.CreateDirectory(Path.Combine(CacheDir(service), "news-cache.json.tmp"));
			Advance(TimeSpan.FromMinutes(31));

			var refreshed = await service.FetchAsync();
			Assert.Equal("Bravo", refreshed.Items.Single().Title); // 内存提交成功

			var failure = new MinecraftNewsService(
				new LauncherContentNewsProvider(new Func<HttpClient>(() => new HttpClient(new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable))))),
				cacheDirectory: CacheDir(service), clock: Now);
			var cached = await failure.FetchAsync();

			Assert.Equal(NewsContentState.Cached, cached.State);
			Assert.Equal("Alpha", cached.Items.Single().Title); // 旧缓存未被破坏
		}
		finally { TryCleanup(CacheDir(service)); }
	}

	/* ---------------- 并发：共享/取消隔离/乱序提交 ---------------- */

	private sealed class BlockingContent : HttpContent
	{
		private readonly TaskCompletionSource _gate;
		private readonly byte[] _payload;
		public BlockingContent(TaskCompletionSource gate, string body) { _gate = gate; _payload = Encoding.UTF8.GetBytes(body); }
		protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context)
		{
			await _gate.Task;
			await stream.WriteAsync(_payload);
		}
		protected override bool TryComputeLength(out long length) { length = _payload.Length; return true; }
	}

	[Fact]
	public async Task Service_ConcurrentRequests_ShareSingleUpstreamFetch()
	{
		var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new BlockingContent(gate, FeedBody) });
		var service = CreateService(handler, out _);
		try
		{
			var t1 = service.FetchAsync();
			var t2 = service.FetchAsync();
			var t3 = service.FetchAsync();
			gate.TrySetResult();

			var results = await Task.WhenAll(t1, t2, t3);

			Assert.Equal(1, handler.Requests.Count);
			Assert.All(results, r => Assert.Equal("Alpha", r.Items.Single().Title));
		}
		finally { TryCleanup(CacheDir(service)); }
	}

	[Fact]
	public async Task Service_WaiterCancellation_DoesNotBreakSharedFetch()
	{
		var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new BlockingContent(gate, FeedBody) });
		var service = CreateService(handler, out _);
		try
		{
			var leader = service.FetchAsync();
			using var cts = new CancellationTokenSource();
			cts.Cancel();
			await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.FetchAsync(12, cts.Token));

			gate.TrySetResult();
			await leader;

			// 共享 fetch 完成并提交：磁盘信封存在，后续请求不再发网络
			Advance(TimeSpan.FromMinutes(31));
			var next = new MinecraftNewsService(
				new LauncherContentNewsProvider(new Func<HttpClient>(() => new HttpClient(new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable))))),
				cacheDirectory: CacheDir(service), clock: Now);
			var cached = await next.FetchAsync();
			Assert.Equal(NewsContentState.Cached, cached.State);
			Assert.Equal("Alpha", cached.Items.Single().Title);
		}
		finally { TryCleanup(CacheDir(service)); }
	}

	[Fact]
	public async Task Service_StaleForceCompletion_DoesNotOverwriteNewerCommit()
	{
		// 两个强刷并发生命：先启动者 gen 小，后启动者 gen 大。
		// 完成顺序由门控制为"先小 gen 后大 gen"；提交保护保证内存 = 大 gen 的内容。
		// 到达序与内容配对在并行线程池下不确定，因此断言为属性式：
		//   调用方各自拿到自己的响应；内存/磁盘必然等于 gen 较新者（fast）的提交内容。
		var gate1 = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var gate2 = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var handler = new FakeHandler(
			_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new BlockingContent(gate1, FeedBody) },
			_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new BlockingContent(gate2, FeedBodyB) });
		var service = CreateService(handler, out _);
		try
		{
			var slow = service.FetchAsync(forceRefresh: true);   // gen1
			var fast = service.FetchAsync(forceRefresh: true);   // gen2
			for (int i = 0; i < 600 && handler.Requests.Count < 2; i++) await Task.Delay(50);
			Assert.Equal(2, handler.Requests.Count);
			gate2.TrySetResult();
			gate1.TrySetResult();
			var results = await Task.WhenAll(slow, fast);
			Assert.All(results, r => Assert.NotEmpty(r.Items)); // 调用方各自拿到自己的响应

			// 内存必须保留 gen 较新者（fast）的提交：后续请求（内存新鲜期）与其一致
			var memory = await service.FetchAsync();
			Assert.Equal(NewsContentState.Memory, memory.State);
			Assert.Equal(results[1].Items.Single().Title, memory.Items.Single().Title);
		}
		finally { TryCleanup(CacheDir(service)); }
	}

	[Fact]
	public async Task Service_FailureResult_DoesNotPolluteMemory()
	{
		var handler = new FakeHandler(
			_ => Json(FeedBody, ValidatorsEtag, ValidatorsLastModified),
			_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable),
			_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
		var service = CreateService(handler, out _);
		try
		{
			await service.FetchAsync();
			Advance(TimeSpan.FromMinutes(31));

			var first = await service.FetchAsync();
			Assert.Equal(NewsContentState.Cached, first.State);

			Advance(TimeSpan.FromMinutes(1));
			var second = await service.FetchAsync();

			// 失败结果未写入内存：仍走上游（第 3 个请求），而不是把 cached 当 memory 返回
			Assert.Equal(3, handler.Requests.Count);
			Assert.Equal(NewsContentState.Cached, second.State);
		}
		finally { TryCleanup(CacheDir(service)); }
	}
}
