using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Pineglow.Launcher.Services;
using Xunit;
using Xunit.Abstractions;

namespace Pineglow.Launcher.Tests;

/// <summary>MR12：CF 策略测试会改静态测试缝，必须与其它类串行。</summary>
[CollectionDefinition("CurseForgePolicy", DisableParallelization = true)]
public sealed class CurseForgePolicyCollection
{
}

/// <summary>
/// MR12：CurseForge 限流闭环。
///
/// 改动前：CF 的三处请求都只调用 <c>EnsureSuccessStatusCode()</c> ——
/// 429 被当成普通失败直接抛出，不读 <c>Retry-After</c>、不退避，
/// 也分不清"限流（稍后重试就好）"与"Key 无效 / 真的没有结果"。
///
/// 本文件用**合成响应**驱动（计划明确：真实限流短时间内无法可靠复现）：
///   - 遵守 Retry-After（秒数与 HTTP 日期两种形式）；
///   - 无 Retry-After 时用指数退避 + 抖动；
///   - 限流持续 → 抛出带"稍后重试"提示的分类异常；
///   - 401/403、404、500 **不重试**（重试只会浪费配额），且给出可区分的提示；
///   - 瞬时网络错误会重试；
///   - 所有 CF 请求共享并发上限，不绕过官方 API 限制。
///
/// 明确不覆盖：真实 CurseForge 服务的限流行为（需要真实 Key 与配额）。
/// </summary>
[Collection("CurseForgePolicy")]
public class CurseForgeRateLimitTests
{
	private readonly ITestOutputHelper _out;

	public CurseForgeRateLimitTests(ITestOutputHelper output) => _out = output;

	/// <summary>按脚本逐次返回响应的假 handler。</summary>
	private sealed class ScriptedHandler : HttpMessageHandler
	{
		private readonly Queue<Func<HttpResponseMessage>> _script;
		private int _calls;
		private int _current;
		private int _maxConcurrent;

		/// <summary>必须用 Interlocked：并发用例下 `Calls++` 会丢计数（全量跑时实测丢过 1 次）。</summary>
		public int Calls => Volatile.Read(ref _calls);

		public int MaxConcurrent => Volatile.Read(ref _maxConcurrent);

		public TimeSpan Latency { get; set; } = TimeSpan.Zero;

		public ScriptedHandler(params Func<HttpResponseMessage>[] script) => _script = new Queue<Func<HttpResponseMessage>>(script);

		protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			Interlocked.Increment(ref _calls);
			int now = Interlocked.Increment(ref _current);
			// 记录峰值并发：用 CAS 循环，避免"读-改-写"竞争
			int seen = Volatile.Read(ref _maxConcurrent);
			while (now > seen)
			{
				int prior = Interlocked.CompareExchange(ref _maxConcurrent, now, seen);
				if (prior == seen)
				{
					break;
				}

				seen = prior;
			}

			try
			{
				if (Latency > TimeSpan.Zero)
				{
					await Task.Delay(Latency, cancellationToken).ConfigureAwait(false);
				}

				// 脚本用完后一直重复最后一条
				Func<HttpResponseMessage> factory = _script.Count > 1 ? _script.Dequeue() : _script.Peek();
				return factory();
			}
			finally
			{
				Interlocked.Decrement(ref _current);
			}
		}
	}

	private static Func<HttpResponseMessage> Ok(string body = "{\"data\":[]}") =>
		() => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) };

	private static Func<HttpResponseMessage> Status(HttpStatusCode code, TimeSpan? retryAfter = null, string? retryAfterDate = null)
	{
		return () =>
		{
			var response = new HttpResponseMessage(code) { Content = new StringContent("{}") };
			if (retryAfter.HasValue)
			{
				response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(retryAfter.Value);
			}
			else if (retryAfterDate != null)
			{
				response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(DateTimeOffset.Parse(retryAfterDate));
			}
			return response;
		};
	}

	/// <summary>把真实等待换成记录，测试才能秒级跑完。</summary>
	private sealed class DelayRecorder : IDisposable
	{
		public List<TimeSpan> Waits { get; } = new List<TimeSpan>();

		public DelayRecorder(double jitter = 1.0)
		{
			CurseForgeRequestPolicy.DelayForTest = (wait, ct) =>
			{
				Waits.Add(wait);
				return Task.CompletedTask;
			};
			CurseForgeRequestPolicy.JitterForTest = () => jitter;
		}

		public void Dispose()
		{
			CurseForgeRequestPolicy.DelayForTest = null;
			CurseForgeRequestPolicy.JitterForTest = null;
		}
	}

	private static HttpClient NewClient(HttpMessageHandler handler) => new HttpClient(handler);

	/* ===================== 1. 遵守 Retry-After ===================== */

	[Fact]
	public async Task RetryAfterDelta_IsHonored_ThenSucceeds()
	{
		using var delays = new DelayRecorder();
		var handler = new ScriptedHandler(Status(HttpStatusCode.TooManyRequests, retryAfter: TimeSpan.FromSeconds(3)), Ok());
		using HttpClient http = NewClient(handler);

		using HttpResponseMessage response = await CurseForgeRequestPolicy.SendAsync(
			http, () => new HttpRequestMessage(HttpMethod.Get, "https://api.curseforge.com/v1/mods/search"), CancellationToken.None);

		_out.WriteLine($"[retry-after delta] calls={handler.Calls} waits={string.Join(",", delays.Waits)}");
		Assert.Equal(HttpStatusCode.OK, response.StatusCode);
		Assert.Equal(2, handler.Calls);
		Assert.Single(delays.Waits);
		Assert.True(delays.Waits[0] >= TimeSpan.FromSeconds(3), $"必须至少等 Retry-After 指定的时长，实际 {delays.Waits[0]}");
	}

	[Fact]
	public async Task RetryAfterHttpDate_IsAlsoHonored()
	{
		using var delays = new DelayRecorder();
		string when = DateTimeOffset.UtcNow.AddSeconds(4).ToString("R");
		var handler = new ScriptedHandler(Status(HttpStatusCode.TooManyRequests, retryAfterDate: when), Ok());
		using HttpClient http = NewClient(handler);

		using HttpResponseMessage response = await CurseForgeRequestPolicy.SendAsync(
			http, () => new HttpRequestMessage(HttpMethod.Get, "https://api.curseforge.com/v1/mods/search"), CancellationToken.None);

		_out.WriteLine($"[retry-after date] waits={string.Join(",", delays.Waits)}");
		Assert.Equal(HttpStatusCode.OK, response.StatusCode);
		Assert.Single(delays.Waits);
		// HTTP 日期只有秒级精度，允许 ±1s
		Assert.True(delays.Waits[0] >= TimeSpan.FromSeconds(2), $"按 HTTP 日期解析出的等待过短：{delays.Waits[0]}");
	}

	/* ===================== 2. 无 Retry-After 时的退避 ===================== */

	[Fact]
	public async Task WithoutRetryAfter_UsesExponentialBackoff()
	{
		using var delays = new DelayRecorder(jitter: 1.0);
		var handler = new ScriptedHandler(Status(HttpStatusCode.TooManyRequests));
		using HttpClient http = NewClient(handler);

		CurseForgeRequestException ex = await Assert.ThrowsAsync<CurseForgeRequestException>(
			() => CurseForgeRequestPolicy.SendAsync(
				http, () => new HttpRequestMessage(HttpMethod.Get, "https://api.curseforge.com/v1/mods/search"), CancellationToken.None));

		_out.WriteLine($"[backoff] waits={string.Join(",", delays.Waits)} attempts={ex.Attempts}");
		Assert.Equal(CurseForgeOutcome.RateLimited, ex.Outcome);
		Assert.Equal(CurseForgeRequestPolicy.MaxAttempts, handler.Calls);
		Assert.Equal(CurseForgeRequestPolicy.MaxAttempts - 1, delays.Waits.Count);

		// 抖动固定为 1.0 时退避应当严格递增：400ms → 800ms → 1600ms
		for (int i = 1; i < delays.Waits.Count; i++)
		{
			Assert.True(delays.Waits[i] > delays.Waits[i - 1], $"退避应当递增：{string.Join(",", delays.Waits)}");
		}

		Assert.Equal(TimeSpan.FromMilliseconds(400), delays.Waits[0]);
	}

	[Fact]
	public void Backoff_IsBoundedAndJittered()
	{
		CurseForgeRequestPolicy.JitterForTest = () => 0.0;
		try
		{
			TimeSpan low = CurseForgeRequestPolicy.Backoff(1);
			Assert.Equal(TimeSpan.FromMilliseconds(200), low); // 400 * 0.5
		}
		finally
		{
			CurseForgeRequestPolicy.JitterForTest = null;
		}

		// 很大的 attempt 也要被 MaxDelay 封顶
		Assert.True(CurseForgeRequestPolicy.Backoff(20) <= CurseForgeRequestPolicy.MaxDelay);
	}

	/* ===================== 3. 分类：限流 vs 鉴权 vs 未找到 ===================== */

	[Fact]
	public async Task PersistentRateLimit_ReportsRetryAfterAndSaysTryLater()
	{
		using var delays = new DelayRecorder();
		var handler = new ScriptedHandler(Status(HttpStatusCode.TooManyRequests, retryAfter: TimeSpan.FromSeconds(7)));
		using HttpClient http = NewClient(handler);

		CurseForgeRequestException ex = await Assert.ThrowsAsync<CurseForgeRequestException>(
			() => CurseForgeRequestPolicy.SendAsync(
				http, () => new HttpRequestMessage(HttpMethod.Get, "https://api.curseforge.com/v1/mods/search"), CancellationToken.None));

		_out.WriteLine($"[rate limited] outcome={ex.Outcome} retryAfter={ex.RetryAfter} hint={ex.Hint}");
		Assert.Equal(CurseForgeOutcome.RateLimited, ex.Outcome);
		Assert.NotNull(ex.RetryAfter);
		Assert.True(ex.RetryAfter!.Value >= TimeSpan.FromSeconds(7));
		Assert.Contains("过于频繁", ex.Hint);
		Assert.Contains("重试", ex.Hint);
		Assert.Contains("7", ex.Hint);
		// 路由把 ex.Message 直接回给界面：可执行的提示必须在消息里，不能只放在 Hint 属性上。
		Assert.Contains("过于频繁", ex.Message);
		Assert.Contains("重试", ex.Message);
	}

	[Fact]
	public async Task Unauthorized_IsNotRetried_AndPointsAtTheApiKey()
	{
		using var delays = new DelayRecorder();
		var handler = new ScriptedHandler(Status(HttpStatusCode.Forbidden));
		using HttpClient http = NewClient(handler);

		CurseForgeRequestException ex = await Assert.ThrowsAsync<CurseForgeRequestException>(
			() => CurseForgeRequestPolicy.SendAsync(
				http, () => new HttpRequestMessage(HttpMethod.Get, "https://api.curseforge.com/v1/mods/search"), CancellationToken.None));

		_out.WriteLine($"[unauthorized] calls={handler.Calls} hint={ex.Hint}");
		Assert.Equal(CurseForgeOutcome.Unauthorized, ex.Outcome);
		Assert.Equal(1, handler.Calls);
		Assert.Empty(delays.Waits);
		Assert.Contains("Key", ex.Hint);
		Assert.Contains("检查 Key", ex.Message);
	}

	[Fact]
	public async Task NotFound_IsNotRetried_AndDoesNotClaimRateLimit()
	{
		using var delays = new DelayRecorder();
		var handler = new ScriptedHandler(Status(HttpStatusCode.NotFound));
		using HttpClient http = NewClient(handler);

		CurseForgeRequestException ex = await Assert.ThrowsAsync<CurseForgeRequestException>(
			() => CurseForgeRequestPolicy.SendAsync(
				http, () => new HttpRequestMessage(HttpMethod.Get, "https://api.curseforge.com/v1/mods/1/files"), CancellationToken.None));

		_out.WriteLine($"[not found] calls={handler.Calls} hint={ex.Hint}");
		Assert.Equal(CurseForgeOutcome.NotFound, ex.Outcome);
		Assert.Equal(1, handler.Calls);
		Assert.DoesNotContain("稍后重试", ex.Hint);
		Assert.Contains("找不到", ex.Hint);
	}

	[Fact]
	public async Task ServerError_IsNotRetried()
	{
		using var delays = new DelayRecorder();
		var handler = new ScriptedHandler(Status(HttpStatusCode.InternalServerError));
		using HttpClient http = NewClient(handler);

		CurseForgeRequestException ex = await Assert.ThrowsAsync<CurseForgeRequestException>(
			() => CurseForgeRequestPolicy.SendAsync(
				http, () => new HttpRequestMessage(HttpMethod.Get, "https://api.curseforge.com/v1/mods/1/files"), CancellationToken.None));

		Assert.Equal(CurseForgeOutcome.ServerError, ex.Outcome);
		Assert.Equal(1, handler.Calls);
	}

	/* ===================== 4. 瞬时网络错误会重试 ===================== */

	[Fact]
	public async Task TransientNetworkError_IsRetried_ThenSucceeds()
	{
		using var delays = new DelayRecorder();
		int calls = 0;
		var handler = new DelegateHandler(_ =>
		{
			calls++;
			if (calls == 1)
			{
				throw new HttpRequestException("connection reset");
			}

			return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"data\":[]}") };
		});
		using HttpClient http = NewClient(handler);

		using HttpResponseMessage response = await CurseForgeRequestPolicy.SendAsync(
			http, () => new HttpRequestMessage(HttpMethod.Get, "https://api.curseforge.com/v1/mods/search"), CancellationToken.None);

		_out.WriteLine($"[transient] calls={calls} waits={string.Join(",", delays.Waits)}");
		Assert.Equal(HttpStatusCode.OK, response.StatusCode);
		Assert.Equal(2, calls);
		Assert.Single(delays.Waits);
	}

	private sealed class DelegateHandler : HttpMessageHandler
	{
		private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;

		public DelegateHandler(Func<HttpRequestMessage, HttpResponseMessage> handler) => _handler = handler;

		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
			Task.FromResult(_handler(request));
	}

	/* ===================== 5. 并发上限（不绕过官方限制） ===================== */

	[Fact]
	public async Task ConcurrentRequests_AreCappedAtTheConfiguredLimit()
	{
		using var delays = new DelayRecorder();
		var handler = new ScriptedHandler(Ok()) { Latency = TimeSpan.FromMilliseconds(40) };
		using HttpClient http = NewClient(handler);

		Task<HttpResponseMessage>[] tasks = Enumerable.Range(0, CurseForgeRequestPolicy.MaxConcurrentRequests * 3)
			.Select(_ => CurseForgeRequestPolicy.SendAsync(
				http, () => new HttpRequestMessage(HttpMethod.Get, "https://api.curseforge.com/v1/mods/search"), CancellationToken.None))
			.ToArray();

		HttpResponseMessage[] responses = await Task.WhenAll(tasks);
		foreach (HttpResponseMessage response in responses)
		{
			response.Dispose();
		}

		_out.WriteLine($"[concurrency] calls={handler.Calls} maxConcurrent={handler.MaxConcurrent} limit={CurseForgeRequestPolicy.MaxConcurrentRequests}");
		Assert.Equal(CurseForgeRequestPolicy.MaxConcurrentRequests * 3, handler.Calls);
		Assert.True(handler.MaxConcurrent <= CurseForgeRequestPolicy.MaxConcurrentRequests,
			$"同时最多只能有 {CurseForgeRequestPolicy.MaxConcurrentRequests} 条 CF 请求，实测 {handler.MaxConcurrent}");
	}
}
