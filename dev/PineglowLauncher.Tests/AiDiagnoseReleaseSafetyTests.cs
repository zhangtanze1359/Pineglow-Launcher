using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Pineglow.Launcher.Models;
using Pineglow.Launcher.Services;

namespace Pineglow.Launcher.Tests;

/// <summary>
/// Phase 3B 发布安全切片 RED 测试（2026-09-18）。
/// 这些测试断言 prompt 第 五 节最小实现要求的可观察行为。当前代码应 RED；
/// GREEN 实施后必须全部通过。
/// 测试组织：A-01..A-09 + A-14；每条 RED 期望写在测试方法上方的注释中。
/// </summary>
public sealed class AiDiagnoseReleaseSafetyTests
{
	/// <summary>跟踪调用次数的 FakeHandler，便于断言"是否真正联网"。
	/// <para>同步在 SendAsync 内读取请求 body 缓存到 <see cref="LastRequestBody"/>，
	/// 避免 HttpClient 释放 request 后 <c>LastRequest.Content.ReadAsStringAsync()</c> 抛
	/// <see cref="ObjectDisposedException"/>。</para></summary>
	private sealed class CountingHandler : HttpMessageHandler
	{
		private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;

		public int CallCount { get; private set; }

		public HttpRequestMessage? LastRequest { get; private set; }

		public string? LastRequestBody { get; private set; }

		public CountingHandler(Func<HttpRequestMessage, HttpResponseMessage> respond)
		{
			_respond = respond;
		}

		protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			CallCount++;
			LastRequest = request;
			if (request.Content != null)
			{
				LastRequestBody = await request.Content.ReadAsStringAsync().ConfigureAwait(false);
			}
			return _respond(request);
		}
	}

	private static HttpClient NewClient(CountingHandler h) => new HttpClient(h);

	private const string OkJson = """{"choices":[{"message":{"content":"ok"}}]}""";

	/// <summary>合法 schemaVersion=1 完整 JSON（最小字段，引用已有 ParseStructuredResult 测试样例）。
	/// 供 fake AI 响应嵌入到 <c>choices[0].message.content</c> 中，使 <see cref="AiDiagnoseService.ParseStructuredResult"/>
	/// 解析为 <c>Ok=true</c>。</summary>
	private const string StructuredSample = """{"schemaVersion":1,"status":"resolved","confidence":0.5,"summary":"ok","causes":[],"modIssues":[],"actions":[],"warnings":[],"missingInformation":[]}""";

	private static HttpResponseMessage JsonOk(string body = OkJson) =>
		new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

	/// <summary>包装 schemaVersion=1 完整 JSON 到 <c>choices[0].message.content</c>，构造合法 fake AI 响应。
	/// 使用 JSON 序列化器正确转义内层 JSON 中的引号与控制字符，
	/// 保证外层响应 JSON 合法、内层 content 字符串等价于传入的 schemaVersion=1 JSON。</summary>
	private static HttpResponseMessage JsonOkStructured(string content = StructuredSample)
	{
		string body = System.Text.Json.JsonSerializer.Serialize(new
		{
			choices = new[] { new { message = new { content } } }
		});
		return new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
	}

	private static DiagnosticContext NewContext(string customJvmArgs = "") =>
		new("win-x64", "1.0.0", "1.20.1", "fabric", "0.14.0", "17", 4096,
			customJvmArgs, 1, "logs/latest.log", DateTimeOffset.UtcNow,
			new[] { "sodium-0.6.8.jar" }, Array.Empty<string>());

	private static DiagnosticSnapshot NewSnapshot(string customJvmArgs = "") =>
		new() { Context = NewContext(customJvmArgs) };

	private static CrashAnalysis NewRule() =>
		new("RULE-SUMMARY", new[] { "RULE-SUGGESTION" }, null);

	private static ModHealthReport NewMods() => new ModHealthReport
	{
		Issues = new[] { new ModHealthIssue(ModHealthSeverity.Warning, "X", "Y") }
	};

	// ============ A-03 RED 1: 未确认不得外发 ============
	[Fact]
	public async Task A03_Red_DiagnoseStructuredAsync_WithoutConfirmAiSend_DoesNotInvokeHandler()
	{
		// RED 期望：当前 API 无 confirmAiSend；handler 总是被调用 1 次。
		// GREEN 期望：无 confirmAiSend=true 时 handler.CallCount == 0；result.ErrorCode == "confirmation_required"。
		var handler = new CountingHandler(_ => JsonOk());
		using HttpClient http = NewClient(handler);
		DiagnosticSnapshot snapshot = NewSnapshot();

		AiDiagnoseResult result = await AiDiagnoseService.DiagnoseStructuredAsync(
			http, "https://api.deepseek.com/v1", "sk", "deepseek-chat",
			snapshot, NewRule(), NewMods(), CancellationToken.None);

		Assert.Equal(0, handler.CallCount);
		Assert.False(result.Ok);
		Assert.Equal("confirmation_required", result.ErrorCode);
	}

	// ============ A-03 GREEN: 仅本地分析 ============
	[Fact]
	public async Task A03_Green_DiagnoseStructuredAsync_LocalOnly_ReturnsLocalResultWithoutHttpCall()
	{
		var handler = new CountingHandler(_ => JsonOk());
		using HttpClient http = NewClient(handler);
		DiagnosticSnapshot snapshot = NewSnapshot();

		AiDiagnoseResult result = await AiDiagnoseService.DiagnoseStructuredAsync(
			http, "https://api.deepseek.com/v1", "sk", "deepseek-chat",
			snapshot, NewRule(), NewMods(), CancellationToken.None,
			confirmAiSend: true, localOnly: true);

		Assert.Equal(0, handler.CallCount);
		Assert.True(result.Ok);
		Assert.Equal("insufficient_information", result.Status);
		Assert.NotEmpty(result.Warnings);
	}

	// ============ A-02 GREEN: customJvmArgs 脱敏 ============
	[Fact]
	public void A02_Green_ComposeStructuredPrompt_RedactsJvmArgsSecrets()
	{
		DiagnosticSnapshot snapshot = NewSnapshot("-DaccessToken=SECRET_TOKEN_VALUE -Dpassword=SECRET_PASSWORD_VALUE");
		string prompt = AiDiagnoseService.ComposeStructuredPrompt(
			snapshot, "(no evidence)", NewRule(), NewMods(),
			snapshot.Context.ModFiles, useJsonSchema: false);

		Assert.DoesNotContain("SECRET_TOKEN_VALUE", prompt);
		Assert.DoesNotContain("SECRET_PASSWORD_VALUE", prompt);
	}

	// ============ A-01 GREEN: endpoint 策略拒绝 ============
	public static IEnumerable<object[]> DisallowedEndpoints => new[]
	{
		new object[] { "http://api.deepseek.com/v1" },                  // HTTP
		new object[] { "https://api.example.com/v1" },                  // 未在 allowlist 的 host
		new object[] { "https://api.deepseek.com.evil.example/v1" },    // 相似后缀域名
		new object[] { "https://user:pass@api.deepseek.com/v1" },       // userinfo
		new object[] { "https://127.0.0.1:11434/v1" },                  // IP literal
		new object[] { "https://localhost:11434/v1" },                  // localhost
		new object[] { "file:///etc/passwd" }                           // 未知 scheme
	};

	[Theory]
	[MemberData(nameof(DisallowedEndpoints))]
	public async Task A01_Green_DiagnoseStructuredAsync_RejectsDisallowedEndpoints(string baseUrl)
	{
		var handler = new CountingHandler(_ => JsonOk());
		using HttpClient http = NewClient(handler);
		DiagnosticSnapshot snapshot = NewSnapshot();

		AiDiagnoseResult result = await AiDiagnoseService.DiagnoseStructuredAsync(
			http, baseUrl, "sk", "deepseek-chat",
			snapshot, NewRule(), NewMods(), CancellationToken.None,
			confirmAiSend: true, localOnly: false);

		Assert.Equal(0, handler.CallCount);
		Assert.False(result.Ok);
		Assert.NotNull(result.ErrorCode);
		Assert.StartsWith("endpoint_", result.ErrorCode);
	}

	// ============ A-01 GREEN: endpoint 策略放行（需 confirmAiSend 才真正发出）============
	[Fact]
	public async Task A01_Green_DiagnoseStructuredAsync_ApprovedEndpoint_CallsHandlerWhenConfirmed()
	{
		var handler = new CountingHandler(_ => JsonOk());
		using HttpClient http = NewClient(handler);
		DiagnosticSnapshot snapshot = NewSnapshot();

		AiDiagnoseResult result = await AiDiagnoseService.DiagnoseStructuredAsync(
			http, "https://api.deepseek.com/v1", "sk", "deepseek-chat",
			snapshot, NewRule(), NewMods(), CancellationToken.None,
			confirmAiSend: true, localOnly: false);

		Assert.Equal(1, handler.CallCount);
	}

	// ============ A-04 GREEN: 响应 MIME 门禁 ============
	[Fact]
	public async Task A04_Green_DiagnoseStructuredAsync_RejectsNonJsonContentType()
	{
		var handler = new CountingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
		{
			Content = new StringContent("<html>oops</html>", Encoding.UTF8, "text/html")
		});
		using HttpClient http = NewClient(handler);
		DiagnosticSnapshot snapshot = NewSnapshot();

		AiDiagnoseResult result = await AiDiagnoseService.DiagnoseStructuredAsync(
			http, "https://api.deepseek.com/v1", "sk", "deepseek-chat",
			snapshot, NewRule(), NewMods(), CancellationToken.None,
			confirmAiSend: true, localOnly: false);

		Assert.False(result.Ok);
		Assert.Equal("ai_invalid_content_type", result.ErrorCode);
	}

	// ============ A-04 GREEN: 响应 Content-Length 超限 ============
	[Fact]
	public async Task A04_Green_DiagnoseStructuredAsync_RejectsContentLengthOverLimit()
	{
		// Handler 在 Content-Length 阶段直接拒收；body 永远不被完整读取。
		var handler = new CountingHandler(_ =>
		{
			HttpResponseMessage resp = new HttpResponseMessage(HttpStatusCode.OK);
			byte[] huge = new byte[AiEndpointPolicy.MaxResponseBytes + 1024];
			ByteArrayContent content = new ByteArrayContent(huge);
			content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
			content.Headers.ContentLength = huge.Length;
			resp.Content = content;
			return resp;
		});
		using HttpClient http = NewClient(handler);
		DiagnosticSnapshot snapshot = NewSnapshot();

		AiDiagnoseResult result = await AiDiagnoseService.DiagnoseStructuredAsync(
			http, "https://api.deepseek.com/v1", "sk", "deepseek-chat",
			snapshot, NewRule(), NewMods(), CancellationToken.None,
			confirmAiSend: true, localOnly: false);

		Assert.False(result.Ok);
		Assert.Equal("ai_response_too_large", result.ErrorCode);
		Assert.Equal(1, handler.CallCount);  // 仍然发起一次请求，但在 Content-Length 阶段拒收
	}

	// ============ A-06 GREEN: 解析围栏剥离 ============
	[Fact]
	public void A06_Green_ParseStructuredResult_StripsJsonMarkdownFence()
	{
		const string fenced = "```json\n{\"schemaVersion\":1,\"status\":\"resolved\",\"confidence\":0.5,\"summary\":\"x\",\"causes\":[],\"modIssues\":[],\"actions\":[],\"warnings\":[],\"missingInformation\":[]}\n```";
		AiDiagnoseResult result = AiDiagnoseService.ParseStructuredResult(fenced, Array.Empty<string>());
		Assert.True(result.Ok);
	}

	// ============ A-06 GREEN: confidence 钳位 ============
	[Fact]
	public void A06_Green_ParseStructuredResult_ClampsConfidenceToUnitInterval()
	{
		const string high = """{"schemaVersion":1,"status":"resolved","confidence":1.7,"summary":"x","causes":[],"modIssues":[],"actions":[],"warnings":[],"missingInformation":[]}""";
		const string neg = """{"schemaVersion":1,"status":"resolved","confidence":-0.5,"summary":"x","causes":[],"modIssues":[],"actions":[],"warnings":[],"missingInformation":[]}""";
		AiDiagnoseResult highResult = AiDiagnoseService.ParseStructuredResult(high, Array.Empty<string>());
		AiDiagnoseResult negResult = AiDiagnoseService.ParseStructuredResult(neg, Array.Empty<string>());
		Assert.Equal(1.0, highResult.Confidence, 3);
		Assert.Equal(0.0, negResult.Confidence, 3);
	}

	// ============ A-06 GREEN: 超大数组截断 ============
	[Fact]
	public void A06_Green_ParseStructuredResult_TruncatesOversizedArrays()
	{
		string causes = string.Join(",", Enumerable.Range(0, 1000).Select(i => $"\"c{i}\""));
		string raw = $$"""{"schemaVersion":1,"status":"resolved","confidence":0.5,"summary":"x","causes":[{{causes}}],"modIssues":[],"actions":[],"warnings":[],"missingInformation":[]}""";
		AiDiagnoseResult result = AiDiagnoseService.ParseStructuredResult(raw, Array.Empty<string>());
		Assert.True(result.Causes.Count <= AiEndpointPolicy.MaxCauses, $"causes={result.Causes.Count}");
	}

	// ============ A-06 GREEN: 超长字符串截断 ============
	[Fact]
	public void A06_Green_ParseStructuredResult_TruncatesLongStringsAndAddsWarning()
	{
		string longText = new string('x', AiEndpointPolicy.MaxStringFieldLength + 500);
		string raw = $$"""{"schemaVersion":1,"status":"resolved","confidence":0.5,"summary":"{{longText}}","causes":[],"modIssues":[],"actions":[],"warnings":[],"missingInformation":[]}""";
		AiDiagnoseResult result = AiDiagnoseService.ParseStructuredResult(raw, Array.Empty<string>());
		Assert.True(result.Ok);
		Assert.True(result.Summary.Length <= AiEndpointPolicy.MaxStringFieldLength);
		Assert.Contains(result.Warnings, w => w.Contains("truncated", StringComparison.OrdinalIgnoreCase));
	}

	// ============ A-08 GREEN: 稳定错误分类 ============
	public static IEnumerable<object[]> StatusErrorCodes => new[]
	{
		new object[] { HttpStatusCode.Unauthorized, "ai_auth_failed" },
		new object[] { HttpStatusCode.Forbidden, "ai_auth_failed" },
		new object[] { HttpStatusCode.TooManyRequests, "ai_rate_limited" },
		new object[] { HttpStatusCode.InternalServerError, "ai_service_error" },
		new object[] { HttpStatusCode.BadGateway, "ai_service_error" },
		new object[] { HttpStatusCode.ServiceUnavailable, "ai_service_error" }
	};

	[Theory]
	[MemberData(nameof(StatusErrorCodes))]
	public async Task A08_Green_DiagnoseStructuredAsync_ReturnsStableErrorCode_ByHttpStatus(HttpStatusCode status, string expectedErrorCode)
	{
		var handler = new CountingHandler(_ => new HttpResponseMessage(status));
		using HttpClient http = NewClient(handler);
		DiagnosticSnapshot snapshot = NewSnapshot();

		AiDiagnoseResult result = await AiDiagnoseService.DiagnoseStructuredAsync(
			http, "https://api.deepseek.com/v1", "sk", "deepseek-chat",
			snapshot, NewRule(), NewMods(), CancellationToken.None,
			confirmAiSend: true, localOnly: false);

		Assert.False(result.Ok);
		Assert.Equal(expectedErrorCode, result.ErrorCode);
	}

	// ============ A-08 GREEN: 非法 JSON 错误分类 ============
	[Fact]
	public async Task A08_Green_DiagnoseStructuredAsync_ReturnsAiInvalidResponse_OnInvalidJson()
	{
		var handler = new CountingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
		{
			Content = new StringContent("not-json-at-all", Encoding.UTF8, "application/json")
		});
		using HttpClient http = NewClient(handler);
		DiagnosticSnapshot snapshot = NewSnapshot();

		AiDiagnoseResult result = await AiDiagnoseService.DiagnoseStructuredAsync(
			http, "https://api.deepseek.com/v1", "sk", "deepseek-chat",
			snapshot, NewRule(), NewMods(), CancellationToken.None,
			confirmAiSend: true, localOnly: false);

		Assert.False(result.Ok);
		Assert.Equal("ai_invalid_response", result.ErrorCode);
		// 锁住 DeepSeek 路径对非法 JSON 不重试：handler 只被调用一次，避免「其他 provider 跟着 MiniMax CN 一起重试」的回归。
		Assert.Equal(1, handler.CallCount);
	}

	// ============ A-07 GREEN: 取消传播 ============
	[Fact]
	public async Task A07_Green_DiagnoseStructuredAsync_CancelledToken_ReturnsAiCancelledInsteadOfThrowing()
	{
		using CancellationTokenSource cts = new CancellationTokenSource();
		cts.Cancel(); // 预取消，确保 ct.IsCancellationRequested == true 在 await 之前
		var handler = new CountingHandler(_ =>
		{
			throw new OperationCanceledException(cts.Token);
		});
		using HttpClient http = NewClient(handler);
		DiagnosticSnapshot snapshot = NewSnapshot();

		Task<AiDiagnoseResult> task = AiDiagnoseService.DiagnoseStructuredAsync(
			http, "https://api.deepseek.com/v1", "sk", "deepseek-chat",
			snapshot, NewRule(), NewMods(), cts.Token,
			confirmAiSend: true, localOnly: false);

		AiDiagnoseResult result;
		try
		{
			result = await task;
		}
		catch (OperationCanceledException)
		{
			Assert.Fail("GREEN expected: should not throw OCE; should return AiDiagnoseResult with ErrorCode=ai_cancelled");
			return;
		}
		Assert.False(result.Ok);
		Assert.Equal("ai_cancelled", result.ErrorCode);
	}

	// ============ Gate R1 RED：localOnly 必须在 endpoint 与 confirm 之前 ============

	[Fact]
	public async Task A03_Red_LocalOnly_AllowedEndpoint_BypassesConfirmGate()
	{
		// RED：当前代码先校验 endpoint 通过后再看 confirmAiSend；confirmAiSend=false 时直接返回 confirmation_required，
		// 永远不会走 LocalOnly 路径。GREEN 期望：localOnly=true 时无论 confirmAiSend，都直接返回本地结果。
		var handler = new CountingHandler(_ => JsonOk());
		using HttpClient http = NewClient(handler);
		DiagnosticSnapshot snapshot = NewSnapshot();

		AiDiagnoseResult result = await AiDiagnoseService.DiagnoseStructuredAsync(
			http, "https://api.deepseek.com/v1", "sk", "deepseek-chat",
			snapshot, NewRule(), NewMods(), CancellationToken.None,
			confirmAiSend: false, localOnly: true);

		Assert.Equal(0, handler.CallCount);
		Assert.True(result.Ok);
		Assert.Equal("insufficient_information", result.Status);
	}

	[Fact]
	public async Task A03_Red_LocalOnly_DisallowedEndpoint_StillReturnsLocalResult()
	{
		// RED：当前代码先校验 endpoint，对不允许的 host 直接返回 endpoint_not_allowed，
		// 完全没机会走 LocalOnly。GREEN 期望：localOnly=true 时完全跳过 endpoint 校验。
		var handler = new CountingHandler(_ => JsonOk());
		using HttpClient http = NewClient(handler);
		DiagnosticSnapshot snapshot = NewSnapshot();

		AiDiagnoseResult result = await AiDiagnoseService.DiagnoseStructuredAsync(
			http, "http://api.deepseek.com/v1", "sk", "deepseek-chat",
			snapshot, NewRule(), NewMods(), CancellationToken.None,
			confirmAiSend: false, localOnly: true);

		Assert.Equal(0, handler.CallCount);
		Assert.True(result.Ok);
		Assert.Equal("insufficient_information", result.Status);
	}

	[Fact]
	public async Task A03_Red_MissingConfirmAiSend_WithoutLocalOnly_StillReturnsConfirmationRequired()
	{
		// 当前已 GREEN（confirmAiSend 默认 false → confirmation_required）。
		// 保留为回归锁，确保未来重构不会让「缺失 confirm」静默外发。
		var handler = new CountingHandler(_ => JsonOk());
		using HttpClient http = NewClient(handler);
		DiagnosticSnapshot snapshot = NewSnapshot();

		AiDiagnoseResult result = await AiDiagnoseService.DiagnoseStructuredAsync(
			http, "https://api.deepseek.com/v1", "sk", "deepseek-chat",
			snapshot, NewRule(), NewMods(), CancellationToken.None);

		Assert.Equal(0, handler.CallCount);
		Assert.False(result.Ok);
		Assert.Equal("confirmation_required", result.ErrorCode);
	}

	// ============ Gate R1 RED：AI 专用 client 必须禁自动重定向 ============

	[Fact]
	public void A01_Green_AiHttpClientFactory_DisablesAutoRedirect()
	{
		// 命中生产 AI client 创建路径（AiHttpClientFactory.CreateAiHandler）。
		// 验证返回的 HttpMessageHandler 是 HttpClientHandler 且 AllowAutoRedirect=false。
		using HttpMessageHandler handler = AiHttpClientFactory.CreateAiHandler(false, string.Empty, 0, string.Empty, string.Empty);
		HttpClientHandler? hch = handler as HttpClientHandler;
		Assert.NotNull(hch);
		Assert.False(hch!.AllowAutoRedirect, "AiHttpClientFactory must set AllowAutoRedirect=false on inner HttpClientHandler");
	}

	// ============ MiniMax China region (2026-09-24)：精确放行 + 跳过 response_format + thinking.type=disabled ============

	[Fact]
	public async Task MiniMax_CN_ApprovedEndpoint_CallsHandlerWhenConfirmed()
	{
		// fake 响应给合法 schemaVersion=1 完整 JSON，使 ParseStructuredResult 解析为 Ok=true。
		var handler = new CountingHandler(_ => JsonOkStructured());
		using HttpClient http = NewClient(handler);
		DiagnosticSnapshot snapshot = NewSnapshot();

		AiDiagnoseResult result = await AiDiagnoseService.DiagnoseStructuredAsync(
			http, "https://api.minimaxi.com/v1", "sk", "MiniMax-M3",
			snapshot, NewRule(), NewMods(), CancellationToken.None,
			confirmAiSend: true, localOnly: false);

		Assert.True(result.Ok, $"MiniMax CN 路径在合法 schemaVersion=1 响应下应解析成功；ErrorCode={result.ErrorCode} Status={result.Status}");
		Assert.Equal(1, handler.CallCount);
	}

	[Fact]
	public async Task MiniMax_CN_RequestPayload_DoesNotIncludeResponseFormat_AndIncludesThinkingDisabled()
	{
		// fake 响应给合法 schemaVersion=1 完整 JSON；handler 同步捕获 body 字符串避免 ObjectDisposedException。
		var handler = new CountingHandler(_ => JsonOkStructured());
		using HttpClient http = NewClient(handler);
		DiagnosticSnapshot snapshot = NewSnapshot();

		AiDiagnoseResult result = await AiDiagnoseService.DiagnoseStructuredAsync(
			http, "https://api.minimaxi.com/v1", "sk", "MiniMax-M3",
			snapshot, NewRule(), NewMods(), CancellationToken.None,
			confirmAiSend: true, localOnly: false);

		Assert.True(result.Ok, $"请求 payload 测试前置：响应必须可解析；ErrorCode={result.ErrorCode}");
		Assert.NotNull(handler.LastRequestBody);
		using System.Text.Json.JsonDocument doc = System.Text.Json.JsonDocument.Parse(handler.LastRequestBody!);
		System.Text.Json.JsonElement root = doc.RootElement;
		// Codex 实测：同端点带 response_format=json_schema → HTTP 400。MiniMax 路径必须不带此字段。
		Assert.False(root.TryGetProperty("response_format", out _), "MiniMax CN must not send response_format (empirical 400 on the real endpoint)");
		// thinking.type=disabled：避免 <think> 污染 JSON content。
		Assert.True(root.TryGetProperty("thinking", out System.Text.Json.JsonElement thinking), "MiniMax CN must send thinking object");
		Assert.Equal("disabled", thinking.GetProperty("type").GetString());
		Assert.Equal("user", root.GetProperty("messages")[0].GetProperty("role").GetString());
	}

	[Fact]
	public async Task MiniMax_CN_WithoutConfirmAiSend_DoesNotInvokeHandler()
	{
		var handler = new CountingHandler(_ => JsonOk());
		using HttpClient http = NewClient(handler);
		DiagnosticSnapshot snapshot = NewSnapshot();

		AiDiagnoseResult result = await AiDiagnoseService.DiagnoseStructuredAsync(
			http, "https://api.minimaxi.com/v1", "sk", "MiniMax-M3",
			snapshot, NewRule(), NewMods(), CancellationToken.None);

		Assert.Equal(0, handler.CallCount);
		Assert.False(result.Ok);
		Assert.Equal("confirmation_required", result.ErrorCode);
	}

	[Fact]
	public async Task MiniMax_CN_LocalOnly_ReturnsLocalResultWithoutHttpCall()
	{
		var handler = new CountingHandler(_ => JsonOk());
		using HttpClient http = NewClient(handler);
		DiagnosticSnapshot snapshot = NewSnapshot();

		AiDiagnoseResult result = await AiDiagnoseService.DiagnoseStructuredAsync(
			http, "https://api.minimaxi.com/v1", "sk", "MiniMax-M3",
			snapshot, NewRule(), NewMods(), CancellationToken.None,
			confirmAiSend: true, localOnly: true);

		Assert.Equal(0, handler.CallCount);
		Assert.True(result.Ok);
		Assert.Equal("insufficient_information", result.Status);
	}

	// ============ MiniMax CN（2026-09-25）：外层 JSON 合法但内层 content 非法时只重试一次 ============

	[Fact]
	public async Task MiniMax_CN_InvalidStructuredContent_RetriesOnce()
	{
		// RED 期望：当前实现对内层 content 解析失败直接返回 ErrorCode=ai_invalid_response / ai_invalid_content_type
		//              等错误（不重试）；GREEN 期望：当外层 JSON 合法但 choices[0].message.content 是无效内层 JSON 时，
		//              服务在第一次响应解析失败后自动重试一次；第二次响应合法 → result.Ok=true，handler.CallCount=2。
		const string invalidInnerJson = "this-is-not-valid-inner-json{{{";
		string outerInvalidBody = System.Text.Json.JsonSerializer.Serialize(new
		{
			choices = new[] { new { message = new { content = invalidInnerJson } } }
		});
		int call = 0;
		var handler = new CountingHandler(_ =>
		{
			call++;
			if (call == 1)
			{
				return new HttpResponseMessage(HttpStatusCode.OK)
				{
					Content = new StringContent(outerInvalidBody, Encoding.UTF8, "application/json")
				};
			}
			return JsonOkStructured();
		});
		using HttpClient http = NewClient(handler);
		DiagnosticSnapshot snapshot = NewSnapshot();

		AiDiagnoseResult result = await AiDiagnoseService.DiagnoseStructuredAsync(
			http, "https://api.minimaxi.com/v1", "sk", "MiniMax-M3",
			snapshot, NewRule(), NewMods(), CancellationToken.None,
			confirmAiSend: true, localOnly: false);

		Assert.True(result.Ok, $"第二次响应合法时 MiniMax CN 路径应解析成功；ErrorCode={result.ErrorCode} Status={result.Status}");
		Assert.Equal(2, handler.CallCount);
	}

	[Fact]
	public async Task MiniMax_CN_InvalidOuterJson_RetriesOnce()
	{
		// RED 期望：当前实现对外层响应 body 直接调 JsonSerializer.Deserialize 解析，
		//              解析失败时直接返回 ErrorCode=ai_invalid_response（不重试）；
		//              GREEN 期望：当外层响应 body 不是合法 JSON 时，服务在第一次响应解析失败后自动重试一次；
		//              第二次响应合法 → result.Ok=true，handler.CallCount=2。
		int call = 0;
		var handler = new CountingHandler(_ =>
		{
			call++;
			if (call == 1)
			{
				return new HttpResponseMessage(HttpStatusCode.OK)
				{
					Content = new StringContent("not-json-at-all", Encoding.UTF8, "application/json")
				};
			}
			return JsonOkStructured();
		});
		using HttpClient http = NewClient(handler);
		DiagnosticSnapshot snapshot = NewSnapshot();

		AiDiagnoseResult result = await AiDiagnoseService.DiagnoseStructuredAsync(
			http, "https://api.minimaxi.com/v1", "sk", "MiniMax-M3",
			snapshot, NewRule(), NewMods(), CancellationToken.None,
			confirmAiSend: true, localOnly: false);

		Assert.True(result.Ok, $"第二次响应合法时 MiniMax CN 路径应解析成功；ErrorCode={result.ErrorCode} Status={result.Status}");
		Assert.Equal(2, handler.CallCount);
	}

	// ============ MiniMax CN（2026-09-25）：边界——两次响应都非法时重试一次后返回 ai_invalid_response，不再继续 ============

	[Fact]
	public async Task MiniMax_CN_BothResponsesHaveInvalidInnerContent_ReturnsAiInvalidResponse_WithCallCountTwo()
	{
		// 边界：两次响应均「外层 JSON 合法但 choices[0].message.content 是非法内层 JSON」时，
		//              服务应在第一次解析失败后重试一次；第二次仍非法 → 返回 ErrorCode=ai_invalid_response，
		//              不无限重试、不抛异常，handler.CallCount=2。
		var handler = new CountingHandler(_ => JsonOkStructured("bad-inner-json{{{"));
		using HttpClient http = NewClient(handler);
		DiagnosticSnapshot snapshot = NewSnapshot();

		AiDiagnoseResult result = await AiDiagnoseService.DiagnoseStructuredAsync(
			http, "https://api.minimaxi.com/v1", "sk", "MiniMax-M3",
			snapshot, NewRule(), NewMods(), CancellationToken.None,
			confirmAiSend: true, localOnly: false);

		Assert.False(result.Ok);
		Assert.Equal("ai_invalid_response", result.ErrorCode);
		Assert.Equal(2, handler.CallCount);
	}

	[Fact]
	public async Task MiniMax_CN_BothResponsesAreInvalidOuterJson_ReturnsAiInvalidResponse_WithCallCountTwo()
	{
		// 边界：两次响应均「HTTP 200 application/json 但 body 不是合法 JSON」时，
		//              服务应在第一次解析失败后重试一次；第二次仍非法 → 返回 ErrorCode=ai_invalid_response，
		//              不无限重试、不抛异常，handler.CallCount=2。
		var handler = new CountingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
		{
			Content = new StringContent("not-json-at-all", Encoding.UTF8, "application/json")
		});
		using HttpClient http = NewClient(handler);
		DiagnosticSnapshot snapshot = NewSnapshot();

		AiDiagnoseResult result = await AiDiagnoseService.DiagnoseStructuredAsync(
			http, "https://api.minimaxi.com/v1", "sk", "MiniMax-M3",
			snapshot, NewRule(), NewMods(), CancellationToken.None,
			confirmAiSend: true, localOnly: false);

		Assert.False(result.Ok);
		Assert.Equal("ai_invalid_response", result.ErrorCode);
		Assert.Equal(2, handler.CallCount);
	}

	[Fact]
	public async Task DeepSeek_RequestPayload_StillIncludesResponseFormat_AndNoThinking()
	{
		// 其它提供商路径保持原样：response_format + 无 thinking。
		// fake 响应给合法 schemaVersion=1 完整 JSON；handler 同步捕获 body 字符串避免 ObjectDisposedException。
		var handler = new CountingHandler(_ => JsonOkStructured());
		using HttpClient http = NewClient(handler);
		DiagnosticSnapshot snapshot = NewSnapshot();

		AiDiagnoseResult result = await AiDiagnoseService.DiagnoseStructuredAsync(
			http, "https://api.deepseek.com/v1", "sk", "deepseek-chat",
			snapshot, NewRule(), NewMods(), CancellationToken.None,
			confirmAiSend: true, localOnly: false);

		Assert.True(result.Ok, $"请求 payload 测试前置：响应必须可解析；ErrorCode={result.ErrorCode}");
		Assert.NotNull(handler.LastRequestBody);
		using System.Text.Json.JsonDocument doc = System.Text.Json.JsonDocument.Parse(handler.LastRequestBody!);
		System.Text.Json.JsonElement root = doc.RootElement;
		Assert.True(root.TryGetProperty("response_format", out _), "DeepSeek path must keep response_format (unchanged behavior)");
		Assert.False(root.TryGetProperty("thinking", out _), "DeepSeek path must NOT send thinking");
	}

	// ============ MiniMax CN：相似后缀 / HTTP / userinfo / IP / localhost 仍必须拒绝 ============

	public static IEnumerable<object[]> MiniMaxDisallowedEndpoints => new[]
	{
		new object[] { "http://api.minimaxi.com/v1" },                            // HTTP
		new object[] { "https://api.minimaxi.com.evil.example/v1" },              // 相似后缀域名
		new object[] { "https://user:pass@api.minimaxi.com/v1" },                 // userinfo
		new object[] { "https://127.0.0.1:11434/v1" },                            // IP literal
		new object[] { "https://localhost:11434/v1" },                            // localhost
		new object[] { "https://api.minimax.com/v1" },                            // 相似前缀（.com vs .com.cn 风格；非 minimaxi）
		new object[] { "https://api.minimax.cn/v1" }                              // 另一个相似后缀
	};

	[Theory]
	[MemberData(nameof(MiniMaxDisallowedEndpoints))]
	public async Task MiniMax_CN_RejectsDisallowedVariants(string baseUrl)
	{
		var handler = new CountingHandler(_ => JsonOk());
		using HttpClient http = NewClient(handler);
		DiagnosticSnapshot snapshot = NewSnapshot();

		AiDiagnoseResult result = await AiDiagnoseService.DiagnoseStructuredAsync(
			http, baseUrl, "sk", "MiniMax-M3",
			snapshot, NewRule(), NewMods(), CancellationToken.None,
			confirmAiSend: true, localOnly: false);

		Assert.Equal(0, handler.CallCount);
		Assert.False(result.Ok);
		Assert.NotNull(result.ErrorCode);
		Assert.StartsWith("endpoint_", result.ErrorCode);
	}

	// ============ Locale 节点 RED（2026-09-25）：prompt 必须按 locale 指示模型按语言输出 ============
	//
	// 服务端合同：
	//   - ComposeStructuredPrompt 接受可选 locale 参数（默认 "zh"）；
	//   - 非法/缺失 locale 在服务端安全回退为 "zh"；
	//   - prompt 中包含明确的语言指令段，指示模型按 locale 输出所有用户可见 JSON 字段，
	//     同时保留 JSON 字段名、技术标识、日志原文不翻译；
	//   - 首次发送与 fallback 两条 prompt 必须使用同一 locale；
	//   - DiagnoseStructuredAsync 接受可选 locale 参数（默认 "zh"）并透传到上述两条 prompt；
	//   - LocalApiService 从 POST body 读取 locale，仅接受精确 "zh"/"en"，其它值回退为 "zh"。
	//
	// RED 期望（当前实现无 locale 参数）：以下测试断言 prompt 含目标语言关键词 / 字段名保留，
	// 在 GREEN 实施前必然失败（无语言指令、首次与 fallback 难以保证同 locale）。

	/// <summary>RED：ComposeStructuredPrompt(locale="zh") 必须包含简体中文语言指令。</summary>
	[Fact]
	public void Locale_Red_ComposeStructuredPrompt_Zh_ContainsChineseLanguageInstruction()
	{
		DiagnosticSnapshot snapshot = NewSnapshot();
		string prompt = AiDiagnoseService.ComposeStructuredPrompt(
			snapshot, "(no evidence)", NewRule(), NewMods(),
			snapshot.Context.ModFiles, useJsonSchema: false, locale: "zh");

		Assert.Contains("简体中文", prompt, StringComparison.Ordinal);
		Assert.Contains("summary", prompt, StringComparison.Ordinal);
		Assert.Contains("causes", prompt, StringComparison.Ordinal);
	}

	/// <summary>RED：ComposeStructuredPrompt(locale="en") 必须包含英文语言指令。</summary>
	[Fact]
	public void Locale_Red_ComposeStructuredPrompt_En_ContainsEnglishLanguageInstruction()
	{
		DiagnosticSnapshot snapshot = NewSnapshot();
		string prompt = AiDiagnoseService.ComposeStructuredPrompt(
			snapshot, "(no evidence)", NewRule(), NewMods(),
			snapshot.Context.ModFiles, useJsonSchema: false, locale: "en");

		Assert.Contains("English", prompt, StringComparison.Ordinal);
		Assert.Contains("summary", prompt, StringComparison.Ordinal);
		Assert.Contains("causes", prompt, StringComparison.Ordinal);
	}

	/// <summary>RED：非法 locale（"fr"）必须在服务端安全回退为 zh，包含简体中文指令。</summary>
	[Fact]
	public void Locale_Red_ComposeStructuredPrompt_InvalidLocale_FallsBackToZh()
	{
		DiagnosticSnapshot snapshot = NewSnapshot();
		string prompt = AiDiagnoseService.ComposeStructuredPrompt(
			snapshot, "(no evidence)", NewRule(), NewMods(),
			snapshot.Context.ModFiles, useJsonSchema: false, locale: "fr");

		Assert.Contains("简体中文", prompt, StringComparison.Ordinal);
		Assert.DoesNotContain("Français", prompt, StringComparison.Ordinal);
	}

	/// <summary>RED：缺失 locale（默认参数）必须回退为 zh，包含简体中文指令。</summary>
	[Fact]
	public void Locale_Red_ComposeStructuredPrompt_DefaultLocale_IsZh()
	{
		DiagnosticSnapshot snapshot = NewSnapshot();
		string prompt = AiDiagnoseService.ComposeStructuredPrompt(
			snapshot, "(no evidence)", NewRule(), NewMods(),
			snapshot.Context.ModFiles, useJsonSchema: false);

		Assert.Contains("简体中文", prompt, StringComparison.Ordinal);
	}

	/// <summary>RED：DiagnoseStructuredAsync(locale="zh") 首次发送的 prompt 必须含简体中文指令。</summary>
	[Fact]
	public async Task Locale_Red_DiagnoseStructuredAsync_Zh_FirstSendPromptContainsChinese()
	{
		string? capturedPrompt = null;
		AiDiagnoseService.SendWithRetryOverrideForTest = (_, _, _, _, prompt, useJsonSchema, _) =>
		{
			if (useJsonSchema)
			{
				capturedPrompt = prompt;
			}
			return Task.FromResult<string?>(StructuredSample);
		};
		try
		{
			var handler = new CountingHandler(_ => JsonOk());
			using HttpClient http = NewClient(handler);
			DiagnosticSnapshot snapshot = NewSnapshot();

			AiDiagnoseResult result = await AiDiagnoseService.DiagnoseStructuredAsync(
				http, "https://api.deepseek.com/v1", "sk", "deepseek-chat",
				snapshot, NewRule(), NewMods(), CancellationToken.None,
				confirmAiSend: true, localOnly: false, locale: "zh");

			Assert.True(result.Ok, $"RED 前置：响应必须可解析；ErrorCode={result.ErrorCode}");
			Assert.NotNull(capturedPrompt);
			Assert.Contains("简体中文", capturedPrompt!, StringComparison.Ordinal);
		}
		finally
		{
			AiDiagnoseService.SendWithRetryOverrideForTest = null;
		}
	}

	/// <summary>RED：DiagnoseStructuredAsync(locale="en") 首次发送的 prompt 必须含英文指令。</summary>
	[Fact]
	public async Task Locale_Red_DiagnoseStructuredAsync_En_FirstSendPromptContainsEnglish()
	{
		string? capturedPrompt = null;
		AiDiagnoseService.SendWithRetryOverrideForTest = (_, _, _, _, prompt, useJsonSchema, _) =>
		{
			if (useJsonSchema)
			{
				capturedPrompt = prompt;
			}
			return Task.FromResult<string?>(StructuredSample);
		};
		try
		{
			var handler = new CountingHandler(_ => JsonOk());
			using HttpClient http = NewClient(handler);
			DiagnosticSnapshot snapshot = NewSnapshot();

			AiDiagnoseResult result = await AiDiagnoseService.DiagnoseStructuredAsync(
				http, "https://api.deepseek.com/v1", "sk", "deepseek-chat",
				snapshot, NewRule(), NewMods(), CancellationToken.None,
				confirmAiSend: true, localOnly: false, locale: "en");

			Assert.True(result.Ok, $"RED 前置：响应必须可解析；ErrorCode={result.ErrorCode}");
			Assert.NotNull(capturedPrompt);
			Assert.Contains("English", capturedPrompt!, StringComparison.Ordinal);
		}
		finally
		{
			AiDiagnoseService.SendWithRetryOverrideForTest = null;
		}
	}

	/// <summary>RED：首次发送返回空时触发 fallback，两条 prompt（首次+fallback）必须使用同一 locale（zh）。
	/// 通过 SendWithRetryOverrideForTest seam 捕获每次调用的 prompt 与 useJsonSchema 标记。</summary>
	[Fact]
	public async Task Locale_Red_DiagnoseStructuredAsync_Zh_FirstAndFallbackPromptsUseSameLocale()
	{
		var captured = new List<(bool useJsonSchema, string prompt)>();
		AiDiagnoseService.SendWithRetryOverrideForTest = (_, _, _, _, prompt, useJsonSchema, _) =>
		{
			lock (captured)
			{
				captured.Add((useJsonSchema, prompt));
			}
			// 首次 useJsonSchema=true 返回空 → 触发 fallback；fallback useJsonSchema=false 返回合法响应。
			return Task.FromResult<string?>(useJsonSchema ? null : StructuredSample);
		};
		try
		{
			var handler = new CountingHandler(_ => JsonOk());
			using HttpClient http = NewClient(handler);
			DiagnosticSnapshot snapshot = NewSnapshot();

			AiDiagnoseResult result = await AiDiagnoseService.DiagnoseStructuredAsync(
				http, "https://api.deepseek.com/v1", "sk", "deepseek-chat",
				snapshot, NewRule(), NewMods(), CancellationToken.None,
				confirmAiSend: true, localOnly: false, locale: "zh");

			Assert.True(result.Ok, $"RED 前置：fallback 响应必须可解析；ErrorCode={result.ErrorCode}");
			Assert.Equal(2, captured.Count);
			Assert.True(captured[0].useJsonSchema, "首次调用 useJsonSchema 必须为 true");
			Assert.False(captured[1].useJsonSchema, "fallback 调用 useJsonSchema 必须为 false");
			Assert.Contains("简体中文", captured[0].prompt, StringComparison.Ordinal);
			Assert.Contains("简体中文", captured[1].prompt, StringComparison.Ordinal);
		}
		finally
		{
			AiDiagnoseService.SendWithRetryOverrideForTest = null;
		}
	}

	/// <summary>RED：locale="en" 时首次与 fallback 两条 prompt 都必须含英文指令。</summary>
	[Fact]
	public async Task Locale_Red_DiagnoseStructuredAsync_En_FirstAndFallbackPromptsUseSameLocale()
	{
		var captured = new List<(bool useJsonSchema, string prompt)>();
		AiDiagnoseService.SendWithRetryOverrideForTest = (_, _, _, _, prompt, useJsonSchema, _) =>
		{
			lock (captured)
			{
				captured.Add((useJsonSchema, prompt));
			}
			return Task.FromResult<string?>(useJsonSchema ? null : StructuredSample);
		};
		try
		{
			var handler = new CountingHandler(_ => JsonOk());
			using HttpClient http = NewClient(handler);
			DiagnosticSnapshot snapshot = NewSnapshot();

			AiDiagnoseResult result = await AiDiagnoseService.DiagnoseStructuredAsync(
				http, "https://api.deepseek.com/v1", "sk", "deepseek-chat",
				snapshot, NewRule(), NewMods(), CancellationToken.None,
				confirmAiSend: true, localOnly: false, locale: "en");

			Assert.True(result.Ok, $"RED 前置：fallback 响应必须可解析；ErrorCode={result.ErrorCode}");
			Assert.Equal(2, captured.Count);
			Assert.Contains("English", captured[0].prompt, StringComparison.Ordinal);
			Assert.Contains("English", captured[1].prompt, StringComparison.Ordinal);
		}
		finally
		{
			AiDiagnoseService.SendWithRetryOverrideForTest = null;
		}
	}
}
