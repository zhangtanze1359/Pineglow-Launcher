using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Pineglow.Launcher.Models;
using Pineglow.Launcher.Services;
using Xunit;

namespace Pineglow.Launcher.Tests;

/// <summary>
/// Phase 3C-2 真实发送元数据 RED/GREEN 测试。
///
/// 约束：
/// - aiDispatch 必须只在服务端确实开始向已允许的 AI endpoint 发起 HTTP SendAsync 时才有 sentAtUtc；
///   本地模式 / 未确认 / 未配置 / endpoint 被拒 / 发送前取消 都不得产生 sentAtUtc。
/// - providerName / model 必须来自本次请求所使用的服务端配置快照（通过 AiDiagnoseService
///   的 per-operation onFirstSendAttempt 回调回传 ServerConfig.ProviderName/Model），
///   不能被调用方（URL 字符串 / 客户端伪造值）替换。
/// - 取消传播语义保持不变；AiHttpException("ai_cancelled", ...) 仍由 SendWithRetryAsync 抛出。
///
/// 测试 seam 策略：使用 AiDiagnoseService.DiagnoseStructuredAsync 的新可选参数
/// `Action<DateTimeOffset>? onFirstSendAttempt`；测试断言回调被调用的次数与时机。
/// 该回调是 per-operation 闭包（不是 static 全局），不会跨请求泄漏。
/// </summary>
public class AiDispatchMetadataTests
{
	private const string OkJson = """{"choices":[{"message":{"content":"ok"}}]}""";
	private const string StructuredInner = """{"schemaVersion":1,"status":"resolved","confidence":0.8,"summary":"test","causes":[],"modIssues":[],"actions":[],"warnings":[],"missingInformation":[]}""";
	// SendOnceAsync 解析 outer {choices[0].message.content} → 这里 outer 套 inner
	private static string StructuredJson => "{\"choices\":[{\"message\":{\"content\":\"" + StructuredInner.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n") + "\"}}]}";

	private sealed class FakeHandler : HttpMessageHandler
	{
		private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;
		public int CallCount { get; private set; }
		public HttpRequestMessage? LastRequest { get; private set; }
		public CancellationToken CapturedToken { get; private set; }
		public FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond)
		{
			_respond = respond;
		}
		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			CallCount++;
			LastRequest = request;
			CapturedToken = cancellationToken;
			return Task.FromResult(_respond(request));
		}
	}

	private static HttpClient NewClient(FakeHandler h) => new HttpClient(h);

	private static HttpResponseMessage JsonOk(string body = OkJson) =>
		new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

	private static DiagnosticContext NewContext() =>
		new("win-x64", "1.0.0", "1.20.1", "fabric", "0.14.0", "17", 4096,
			string.Empty, 1, "logs/latest.log", DateTimeOffset.UtcNow,
			new[] { "sodium-0.6.8.jar" }, Array.Empty<string>());

	private static DiagnosticSnapshot NewSnapshot() =>
		new() { Context = NewContext() };

	private static CrashAnalysis NewRule() =>
		new("RULE-SUMMARY", new[] { "RULE-SUGGESTION" }, null);

	private static ModHealthReport NewMods() => new ModHealthReport
	{
		Issues = new[] { new ModHealthIssue(ModHealthSeverity.Warning, "X", "Y") }
	};

	private static (int calls, List<AiDispatchAttempt> events) NewDispatchObserver()
	{
		return (0, new List<AiDispatchAttempt>());
	}

	// ==== RED-1: localOnly 必须不触发 dispatch 回调（无 aiSend） ====
	[Fact]
	public async Task RED1_LocalOnly_DoesNotFireDispatch()
	{
		var handler = new FakeHandler(_ => JsonOk());
		using HttpClient http = NewClient(handler);
		var events = new List<AiDispatchAttempt>();
		Action<AiDispatchAttempt> observer = a => events.Add(a);

		AiDiagnoseResult result = await AiDiagnoseService.DiagnoseStructuredAsync(
			http, "https://api.deepseek.com/v1", "sk", "deepseek-chat",
			NewSnapshot(), NewRule(), NewMods(), CancellationToken.None,
			confirmAiSend: true, localOnly: true,
			onFirstSendAttempt: observer);

		Assert.Empty(events);
		Assert.Equal(0, handler.CallCount);
		Assert.True(result.Ok);
	}

	// ==== RED-2: confirmAiSend=false 必须不触发 dispatch 回调 ====
	[Fact]
	public async Task RED2_NotConfirmed_DoesNotFireDispatch()
	{
		var handler = new FakeHandler(_ => JsonOk());
		using HttpClient http = NewClient(handler);
		var events = new List<AiDispatchAttempt>();
		Action<AiDispatchAttempt> observer = a => events.Add(a);

		AiDiagnoseResult result = await AiDiagnoseService.DiagnoseStructuredAsync(
			http, "https://api.deepseek.com/v1", "sk", "deepseek-chat",
			NewSnapshot(), NewRule(), NewMods(), CancellationToken.None,
			confirmAiSend: false, localOnly: false,
			onFirstSendAttempt: observer);

		Assert.Empty(events);
		Assert.Equal(0, handler.CallCount);
		Assert.False(result.Ok);
		Assert.Equal("confirmation_required", result.ErrorCode);
	}

	// ==== RED-3: endpoint 被 AiEndpointPolicy 拒绝（不在 allowlist）必须不触发 dispatch ====
	[Fact]
	public async Task RED3_EndpointRejected_DoesNotFireDispatch()
	{
		var handler = new FakeHandler(_ => JsonOk());
		using HttpClient http = NewClient(handler);
		var events = new List<AiDispatchAttempt>();
		Action<AiDispatchAttempt> observer = a => events.Add(a);

		AiDiagnoseResult result = await AiDiagnoseService.DiagnoseStructuredAsync(
			http, "http://attacker.example/v1", "sk", "x",
			NewSnapshot(), NewRule(), NewMods(), CancellationToken.None,
			confirmAiSend: true, localOnly: false,
			onFirstSendAttempt: observer);

		Assert.Empty(events);
		Assert.Equal(0, handler.CallCount);
		Assert.False(result.Ok);
		// 失败原因必须是 endpoint 拒绝，绝不能是 ai_timeout/ai_unavailable 等其它错误码
		Assert.NotNull(result.ErrorCode);
		Assert.Contains("endpoint", result.ErrorCode, StringComparison.OrdinalIgnoreCase);
	}

	// ==== RED-4: SendAsync 被 fake handler 调用时必须触发 dispatch，且 timestamp 合理 ====
	[Fact]
	public async Task RED4_FakeHandlerSeesSendAsync_FiresDispatch_Once()
	{
		var handler = new FakeHandler(_ => JsonOk(StructuredJson));
		using HttpClient http = NewClient(handler);
		var events = new List<AiDispatchAttempt>();
		Action<AiDispatchAttempt> observer = a => events.Add(a);

		DateTimeOffset before = DateTimeOffset.UtcNow.AddSeconds(-1);
		AiDiagnoseResult result = await AiDiagnoseService.DiagnoseStructuredAsync(
			http, "https://api.deepseek.com/v1", "sk", "deepseek-chat",
			NewSnapshot(), NewRule(), NewMods(), CancellationToken.None,
			confirmAiSend: true, localOnly: false,
			onFirstSendAttempt: observer);
		DateTimeOffset after = DateTimeOffset.UtcNow.AddSeconds(1);

		Assert.True(result.Ok);
		Assert.Equal(1, handler.CallCount);
		// 单一回调：first-send 仅 1 次，不重复
		Assert.Single(events);
		// timestamp 必须在 before..after 区间内
		Assert.InRange(events[0].SentAtUtc.UtcDateTime, before.UtcDateTime, after.UtcDateTime);
	}

	// ==== RED-5: 在 SendAsync 之前取消（linked token 已经被 cancel）必须不触发 dispatch ====
	[Fact]
	public async Task RED5_CancelledBeforeSend_DoesNotFireDispatch()
	{
		var handler = new FakeHandler(_ => JsonOk());
		using HttpClient http = NewClient(handler);
		var events = new List<AiDispatchAttempt>();
		Action<AiDispatchAttempt> observer = a => events.Add(a);

		using CancellationTokenSource cts = new CancellationTokenSource();
		cts.Cancel(); // 关键：在调用前取消

		AiDiagnoseResult result = await AiDiagnoseService.DiagnoseStructuredAsync(
			http, "https://api.deepseek.com/v1", "sk", "deepseek-chat",
			NewSnapshot(), NewRule(), NewMods(), cts.Token,
			confirmAiSend: true, localOnly: false,
			onFirstSendAttempt: observer);

		// 关键断言：SendAsync 必须没被调用，dispatch 回调必须没触发
		Assert.Equal(0, handler.CallCount);
		Assert.Empty(events);
		// 结果是 ai_cancelled
		Assert.False(result.Ok);
		Assert.Equal("ai_cancelled", result.ErrorCode);
	}

	// ==== RED-6: 重试 / fallback schema 必须只触发一次 dispatch（首次实际发送） ====
	[Fact]
	public async Task RED6_RetryAndFallback_FiresDispatchOnce()
	{
		int sendAttempt = 0;
		// 第一次返回 500 → 触发 ai_service_error → 重试
		// 重试再 500 → ai_service_error → fallback schema
		// fallback schema 返回 200 但 body 不含 choices[0] → invalid_response
		var handler = new FakeHandler(_ =>
		{
			sendAttempt++;
			if (sendAttempt <= 2) return new HttpResponseMessage(HttpStatusCode.InternalServerError);
			return new HttpResponseMessage(HttpStatusCode.OK)
			{
				Content = new StringContent("""{"choices":[{"message":{"content":""}}]}""", Encoding.UTF8, "application/json")
			};
		});
		using HttpClient http = NewClient(handler);
		var events = new List<AiDispatchAttempt>();
		Action<AiDispatchAttempt> observer = a => events.Add(a);

		AiDiagnoseResult result = await AiDiagnoseService.DiagnoseStructuredAsync(
			http, "https://api.deepseek.com/v1", "sk", "deepseek-chat",
			NewSnapshot(), NewRule(), NewMods(), CancellationToken.None,
			confirmAiSend: true, localOnly: false,
			onFirstSendAttempt: observer);

		// 至少 2 次 SendAsync（retry 至少 1 次；可能还有 fallback schema）
		Assert.True(sendAttempt >= 2, $"retry 应触发多次 SendAsync，但实际 {sendAttempt}");
		// 关键：dispatch 回调只触发一次
		Assert.Single(events);
	}

	// ==== RED-7: aiDispatch 元数据本身 —— P0 修复：service-layer 只含 SentAtUtc；ProviderName/Model 由 LocalApi 注入 ====
	// 这是编译期断言 + 运行时 shape 验证：
	// AiDispatchAttempt 是 readonly record struct (DateTimeOffset SentAtUtc)
	// 反射验证字段名/类型，确保不会被人悄悄加回 ProviderName/Model（URL 泄露风险）。
	[Fact]
	public void RED7_AiDispatchAttempt_RecordShape_OnlySentAtUtc()
	{
		Type t = typeof(AiDispatchAttempt);
		System.Reflection.PropertyInfo[] props = t.GetProperties();
		Assert.Equal(1, props.Length);
		System.Reflection.PropertyInfo sent = t.GetProperty("SentAtUtc")!;
		Assert.NotNull(sent);
		Assert.Equal(typeof(DateTimeOffset), sent!.PropertyType);
	}

	// ==== RED-8: 真实 SendAsync 链上的 timestamp 容差断言 ====
	// 服务端记录的 sentAtUtc 必须来自 fake handler 入口附近；不能早于 handler 调用 1 秒、
	// 也不能晚于 handler 返回 1 秒。这证明 timestamp 来源是真实 SendAsync，不是更早或更晚。
	[Fact]
	public async Task RED8_SentAtUtc_TimestampWithinFakeHandlerEntryTolerance()
	{
		var handler = new RecordingHandler();
		using HttpClient http = new HttpClient(handler);
		var events = new List<AiDispatchAttempt>();
		Action<AiDispatchAttempt> observer = a => events.Add(a);

		AiDiagnoseResult result = await AiDiagnoseService.DiagnoseStructuredAsync(
			http, "https://api.deepseek.com/v1", "sk", "deepseek-chat",
			NewSnapshot(), NewRule(), NewMods(), CancellationToken.None,
			confirmAiSend: true, localOnly: false,
			onFirstSendAttempt: observer);

		Assert.True(result.Ok);
		Assert.Equal(1, handler.CallCount);
		Assert.Single(events);
		// 容差断言：sentAtUtc - handlerEntryUtc ∈ [-1s, +1s]
		// （实际差距为同步纳秒级；±1s 给 wall clock 抖动与机器调度足够裕度）
		double deltaMs = Math.Abs((events[0].SentAtUtc - handler.EntryUtc).TotalMilliseconds);
		Assert.True(deltaMs < 1000, $"sentAtUtc vs fake handler entry delta = {deltaMs}ms (must be < 1000ms)");
	}

	// ==== RED-8b: 请求构造边界断言 ——
	// 关键：dispatch 必须晚于 request.Content 构造完成、早于 http.SendAsync 入口。
	// 我们让 fake handler 在 SendAsync 入口抛 TaskCanceledException（不是 ct 取消，被 SendWithRetryAsync
	// 视为 ai_timeout）；dispatch 此时必须已经 fire（因为 send 真的即将发生），但结果 ai_timeout。
	// 同时 dispatch.SentAtUtc 必须 ≤ handler.EntryUtc（dispatch 严格早于 handler 入口）。
	[Fact]
	public async Task RED8b_SentAtUtc_FiresAfterRequestConstruction()
	{
		DateTimeOffset requestConstructedAt = DateTimeOffset.UtcNow;
		var handler = new CancelAtEntryHandler();
		using HttpClient http = new HttpClient(handler);
		var events = new List<AiDispatchAttempt>();
		Action<AiDispatchAttempt> observer = a => events.Add(a);

		AiDiagnoseResult result = await AiDiagnoseService.DiagnoseStructuredAsync(
			http, "https://api.deepseek.com/v1", "sk", "deepseek-chat",
			NewSnapshot(), NewRule(), NewMods(), CancellationToken.None,
			confirmAiSend: true, localOnly: false,
			onFirstSendAttempt: observer);
		// handler 入口抛 TaskCanceledException → SendWithRetryAsync 视为 ai_timeout
		Assert.False(result.Ok);
		Assert.Equal("ai_timeout", result.ErrorCode);
		Assert.True(handler.EntryCount >= 1, $"handler must be entered at least once (was {handler.EntryCount})");
		Assert.Single(events);
		// 关键断言 1：dispatch.SentAtUtc ≤ handler.EntryUtc + 小幅 skew（dispatch 严格早于 handler 入口）
		Assert.True(events[0].SentAtUtc <= handler.EntryUtc.AddMilliseconds(50),
			$"dispatch.SentAtUtc={events[0].SentAtUtc:O} must be <= handler.EntryUtc={handler.EntryUtc:O} + 50ms skew");
		// 关键断言 2：dispatch 必须晚于"调用起点"。给一个 ±2s 的容差：
		double deltaMs = Math.Abs((events[0].SentAtUtc - requestConstructedAt).TotalMilliseconds);
		Assert.True(deltaMs < 2000, $"dispatch.SentAtUtc must be within ±2s of request construction start (got {deltaMs}ms)");
	}

	private sealed class CancelAtEntryHandler : HttpMessageHandler
	{
		public int EntryCount { get; private set; }
		public DateTimeOffset EntryUtc { get; private set; }
		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			EntryCount++;
			EntryUtc = DateTimeOffset.UtcNow;
			// 立刻抛 TaskCanceledException（不是 ct 取消），被 SendWithRetryAsync 视为 ai_timeout
			throw new TaskCanceledException("timeout-at-handler-entry");
		}
	}

	// ==== RED-9: Fake 测试 seam (SendWithRetryOverrideForTest) 不算真实发送，不应触发 dispatch ====
	// P0 修复强调：seam 走短路绕过 SendOnceAsync 的真实 http.SendAsync(...)，绝不能误报"已发送"。
	[Fact]
	public async Task RED9_SendWithRetryOverrideForTest_DoesNotFireDispatch()
	{
		int seamCallCount = 0;
		Func<HttpClient, string, string, string, string, bool, CancellationToken, Task<string?>> seam =
			async (http, baseUrl, apiKey, model, prompt, useJsonSchema, ct) =>
			{
				Interlocked.Increment(ref seamCallCount);
				// 模拟"成功返回"，但走的是 seam 不是真实 SendAsync
				await Task.Yield();
				return "{\"schemaVersion\":1,\"status\":\"resolved\",\"confidence\":0.8,\"summary\":\"seam\",\"causes\":[],\"modIssues\":[],\"actions\":[],\"warnings\":[],\"missingInformation\":[]}";
			};
		AiDiagnoseService.SendWithRetryOverrideForTest = seam;

		var realHandler = new RecordingHandler();
		using HttpClient http = new HttpClient(realHandler);
		var events = new List<AiDispatchAttempt>();
		Action<AiDispatchAttempt> observer = a => events.Add(a);

		try
		{
			AiDiagnoseResult result = await AiDiagnoseService.DiagnoseStructuredAsync(
				http, "https://api.deepseek.com/v1", "sk", "deepseek-chat",
				NewSnapshot(), NewRule(), NewMods(), CancellationToken.None,
				confirmAiSend: true, localOnly: false,
				onFirstSendAttempt: observer);

			Assert.True(result.Ok);
			// seam 被调用
			Assert.Equal(1, Volatile.Read(ref seamCallCount));
			// 真实 handler 永远不应被调用（被 seam 短路）
			Assert.Equal(0, realHandler.CallCount);
			// 关键：dispatch 不应触发 —— seam 不代表真实发送
			Assert.Empty(events);
		}
		finally
		{
			AiDiagnoseService.SendWithRetryOverrideForTest = null;
		}
	}

	private sealed class RecordingHandler : HttpMessageHandler
	{
		public int CallCount { get; private set; }
		public DateTimeOffset EntryUtc { get; private set; }
		public HttpRequestMessage? LastRequest { get; private set; }
		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			CallCount++;
			EntryUtc = DateTimeOffset.UtcNow;
			LastRequest = request;
			const string inner = "{\"schemaVersion\":1,\"status\":\"resolved\",\"confidence\":0.8,\"summary\":\"x\",\"causes\":[],\"modIssues\":[],\"actions\":[],\"warnings\":[],\"missingInformation\":[]}";
			string body = "{\"choices\":[{\"message\":{\"content\":\"" + inner.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n") + "\"}}]}";
			return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
			{
				Content = new StringContent(body, Encoding.UTF8, "application/json")
			});
		}
	}
}