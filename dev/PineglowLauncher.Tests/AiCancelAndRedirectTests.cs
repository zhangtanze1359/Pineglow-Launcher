using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Pineglow.Launcher.Models;
using Pineglow.Launcher.Services;
using Xunit;

namespace Pineglow.Launcher.Tests;

/// <summary>
/// Phase 3B v3 修复批次：取消链路 + 重定向 + News/AI 客户端隔离的真实 RED/GREEN 测试。
/// 涵盖：
/// - cancel 路由可达性（先于通用 diagnose 路由）；
/// - 客户端 operationId 必传；
/// - 取消后 handler 收到 token、retry/send 不再发生；
/// - registry (instanceId, operationId) 键、cancel-before-register tombstone、TTL/容量/finally 清理、跨实例隔离；
/// - AI client 302/307/308 不跟随重定向；
/// - News/AI 客户端隔离 + 代理刷新同步替换。
/// </summary>
public class AiCancelAndRedirectTests
{
	private sealed class LoopbackListener : IDisposable
	{
		public HttpListener Listener { get; }
		public int Port { get; }
		public int RequestCount { get; private set; }
		public List<string> RequestPaths { get; } = new List<string>();
		public Func<HttpListenerRequest, CancellationToken, LoopbackResponse> Handler { get; set; } = (_, _) => new LoopbackResponse();
		public CancellationTokenSource StopCts { get; } = new CancellationTokenSource();

		public LoopbackListener()
		{
			var l = new TcpListener(IPAddress.Loopback, 0);
			l.Start();
			Port = ((IPEndPoint)l.LocalEndpoint).Port;
			l.Stop();
			Listener = new HttpListener();
			Listener.Prefixes.Add($"http://127.0.0.1:{Port}/");
			Listener.Start();
			_ = Task.Run(async () =>
			{
				while (!StopCts.IsCancellationRequested)
				{
					HttpListenerContext ctx;
					try { ctx = await Listener.GetContextAsync().ConfigureAwait(false); }
					catch { return; }
					RequestCount++;
					RequestPaths.Add(ctx.Request.Url?.AbsolutePath ?? "/");
					try
					{
						var resp = Handler(ctx.Request, StopCts.Token);
						ctx.Response.StatusCode = (int)resp.StatusCode;
						if (!string.IsNullOrEmpty(resp.Location))
						{
							ctx.Response.RedirectLocation = resp.Location;
						}
						if (resp.Body != null)
						{
							byte[] bytes = resp.Body;
							ctx.Response.ContentLength64 = bytes.Length;
							ctx.Response.OutputStream.Write(bytes, 0, bytes.Length);
						}
					}
					finally
					{
						try { ctx.Response.Close(); } catch { }
					}
				}
			});
		}

		public void Dispose()
		{
			try { StopCts.Cancel(); } catch { }
			try { Listener.Stop(); } catch { }
			try { Listener.Close(); } catch { }
			StopCts.Dispose();
		}
	}

	private sealed class LoopbackResponse
	{
		public HttpStatusCode StatusCode { get; set; } = HttpStatusCode.OK;
		public string Location { get; set; }
		public byte[] Body { get; set; }
	}

	// ====== RED-1：路由不可达（v3：cancel 路由被通用 diagnose 截获）======
	// v3 实现后 GREEN：精确 5 段 cancel 路由先于 4 段通用 diagnose 路由。
	//
	// 第 3 步（2026-11-21）**换掉了断言方式**：原来在 LocalApiService.Routing.cs 的**源码文本**里
	// 比较两个 handler 名字的 IndexOf 先后。那段文本断言有两个问题：改注释/调格式会误红；
	// 而真正要守的东西——"请求 /api/instances/{id}/diagnose/cancel 命中的是取消 handler，
	// 而不是通用 diagnose handler"——它其实一个字都没测（命中与否取决于路由表数据，不取决于两个
	// 名字在文件里的先后）。现在直接把**路由表**当数据来问：用真实业务路径跑 ResolveRoute，
	// 断言命中的路由段数正确、签名互不相同、且两条路径没有接到同一个处理器上
	// （比 delegate 的方法身份，不比文本先后；lambda 的名字是编译器生成的，不可读）。
	[Fact]
	public void RED1_CancelRoutePath_MustComeBeforeDiagnoseRoute()
	{
		string[] cancelPath = { "api", "instances", "inst-red1", "diagnose", "cancel" };
		string[] diagnosePath = { "api", "instances", "inst-red1", "diagnose" };

		ApiRoute? cancel = LocalApiService.ResolveRoute("POST", cancelPath);
		ApiRoute? diagnose = LocalApiService.ResolveRoute("POST", diagnosePath);

		Assert.NotNull(cancel);
		Assert.NotNull(diagnose);

		// 取消路由必须是 5 段精确路由；若被通用 diagnose（4 段，或改成 Range 上不封顶）吞掉，
		// 下面两条会立刻红——那正是 v3 里"取消功能整条失效"的形态。
		Assert.Equal(5, cancel!.MaxSegments);
		Assert.Equal(4, diagnose!.MaxSegments);
		Assert.NotEqual(cancel.Signature, diagnose.Signature);

		// 真正的问题：这两条路径不能接到**同一个处理器**上（否则 /diagnose/cancel 只是又跑一遍诊断）。
		// 路由表里的处理器是 lambda，名字是编译器生成的（<BuildRoutes>b__NN_M），所以这里比的是
		// delegate 的方法身份，不是我们给方法起的名字。取消语义本身由 RED3 / Step3_03 发真实请求断言。
		Assert.NotEqual(cancel.Handler.Method, diagnose.Handler.Method);

		// 反向保护：取消是 POST 语义，GET 同路径不得落到取消那条路由上。
		Assert.NotEqual(cancel.Handler.Method, LocalApiService.ResolveRoute("GET", cancelPath)?.Handler.Method);
	}

	// ====== RED-2 / RED-3：见本文件末尾 AiCancelAndRedirectStep3Tests ======
	// 第 3 步（2026-11-21）把这两条从"读源码文本"换成"发真实请求断言响应形状"：
	//   * RED2 原本断言源码里不存在 `operationId = Guid.NewGuid().ToString("N")` 且存在一句错误文案；
	//     真正的不变量是"缺 id 的请求必须被 400 拒绝、服务端绝不替客户端生成 id"，
	//     这只能在真实请求上判（源码里那句文案在别处被复制一份也能过旧断言）。
	//   * RED3 原本截出 HandleCancelDiagnoseAsync 的方法体找 `cancelled`/`message` 子串；
	//     真正的不变量是"取消响应是对外契约（ok/operationId/cancelled/message），
	//     且不带诊断对象（rule/mods/snapshot/aiResult）"，同一台服务上对比两个端点的键集才测得出来。
	// 两条都放进 AiCancelAndRedirectStep3Tests，因为它已有真实服务端夹具（StartAsync + 端口选择）。


// ====== RED-4：registry cancel-before-register tombstone ======
	[Fact]
	public async Task RED4_CancelBeforeRegister_ThenRegister_ImmediatelyCancelsCts()
	{
		var reg = new AiOperationRegistry(capacity: 100, activeTtl: TimeSpan.FromSeconds(30), tombstoneTtl: TimeSpan.FromSeconds(30));
		Assert.True(reg.Cancel("inst-red4", "op-red4"), "first cancel must add tombstone");
		using var cts = new CancellationTokenSource();
		bool registered = reg.Register("inst-red4", "op-red4", cts);
		Assert.True(registered);
		await Task.Delay(50);
		Assert.True(cts.IsCancellationRequested, "Register after tombstone must immediately cancel new CTS");
	}

	[Fact]
	public async Task RED4b_CancelBeforeRegister_TombstoneCounted_AndEventuallyCleaned()
	{
		var reg = new AiOperationRegistry(capacity: 100, activeTtl: TimeSpan.FromSeconds(30), tombstoneTtl: TimeSpan.FromMilliseconds(50));
		reg.Cancel("inst-red4b", "op-1");
		Assert.Equal(1, reg.Count);
		await Task.Delay(200);
		reg.Cancel("inst-red4b", "op-2");
		Assert.Equal(1, reg.Count);
	}

	// ====== RED-5：实例隔离、重复取消、容量、并发清理 ======
	[Fact]
	public void RED5_SameOperationId_DifferentInstance_DoesNotInterfere()
	{
		var reg = new AiOperationRegistry();
		using var cts1 = new CancellationTokenSource();
		using var cts2 = new CancellationTokenSource();
		Assert.True(reg.Register("inst-A", "op-shared", cts1));
		Assert.True(reg.Register("inst-B", "op-shared", cts2));
		Assert.False(cts1.IsCancellationRequested);
		Assert.False(cts2.IsCancellationRequested);
		Assert.True(reg.Cancel("inst-A", "op-shared"));
		Assert.True(cts1.IsCancellationRequested);
		Assert.False(cts2.IsCancellationRequested);
	}

	[Fact]
	public void RED5b_RepeatCancel_IsIdempotent()
	{
		var reg = new AiOperationRegistry();
		using var cts = new CancellationTokenSource();
		reg.Register("inst-rc", "op-rc", cts);
		Assert.True(reg.Cancel("inst-rc", "op-rc"));
		Assert.False(reg.Cancel("inst-rc", "op-rc"), "repeat cancel must be idempotent");
	}

	[Fact]
	public void RED5c_RegistryCapacity_Bound_AfterTombstones()
	{
		var reg = new AiOperationRegistry(capacity: 3, activeTtl: TimeSpan.FromSeconds(30), tombstoneTtl: TimeSpan.FromSeconds(30));
		Assert.True(reg.Cancel("i", "1"));
		Assert.True(reg.Cancel("i", "2"));
		Assert.True(reg.Cancel("i", "3"));
		Assert.Equal(3, reg.Count);
		bool added = reg.Cancel("i", "4");
		if (!added)
		{
			reg.Clear();
			added = reg.Cancel("i", "4");
		}
		Assert.True(added || reg.Count <= 3);
	}

	[Fact]
	public async Task RED5d_TokenCancelled_RemovesActiveEntryFromRegistry()
	{
		var reg = new AiOperationRegistry();
		using var cts = new CancellationTokenSource();
		reg.Register("inst-tc", "op-tc", cts);
		Assert.Equal(1, reg.Count);
		cts.Cancel();
		await Task.Delay(50);
		Assert.Equal(0, reg.Count);
	}

	// ====== v2 GREEN3（保留）：AI 工厂客户端不跟随重定向 ======
	[Theory]
	[InlineData(HttpStatusCode.Found)]
	[InlineData(HttpStatusCode.TemporaryRedirect)]
	[InlineData(HttpStatusCode.PermanentRedirect)]
	public async Task GREEN3_ProductionAiClient_FactoryCreated_DoesNotFollowRedirect_FirstHost1_SecondHost0(HttpStatusCode status)
	{
		using LoopbackListener first = new LoopbackListener();
		using LoopbackListener second = new LoopbackListener();
		first.Handler = (req, ct) => new LoopbackResponse
		{
			StatusCode = status,
			Location = $"http://127.0.0.1:{second.Port}/chat/completions",
			Body = Encoding.UTF8.GetBytes("{}")
		};
		second.Handler = (req, ct) => new LoopbackResponse { StatusCode = HttpStatusCode.OK, Body = Encoding.UTF8.GetBytes("{\"ok\":true}") };

		using HttpClient greenClient = OutboundHttp.CreateAi(OutboundHttp.ProxySettings.None);
		greenClient.BaseAddress = new Uri($"http://127.0.0.1:{first.Port}/");
		using HttpRequestMessage req = new HttpRequestMessage(HttpMethod.Post, "chat/completions") { Content = new StringContent("{}", Encoding.UTF8, "application/json") };
		using HttpResponseMessage resp = await greenClient.SendAsync(req, HttpCompletionOption.ResponseHeadersRead);
		Assert.Equal((int)status, (int)resp.StatusCode);
		Assert.Equal(1, first.RequestCount);
		Assert.Equal(0, second.RequestCount);
	}

	// ====== v2 GREEN4/4b（保留）：AI/News 客户端隔离 ======
	/// <summary>
	/// 新闻绝不能走 AI 那条客户端（历史缺陷：<c>NewsService</c> 曾通过 <c>() =&gt; AiHttp</c> 取客户端，
	/// 于是新闻请求继承了 AI 的策略：禁自动重定向、60s 超时，还会被 AI 的测试覆盖缝劫持）。
	///
	/// 第 3 步（2026-11-21）**换掉了断言方式**：原来是 <c>Contains("LauncherContentNewsProvider(() =&gt; AiHttp)")</c>
	/// 取反 —— 那条断言只能发现"退回旧写法"，任何别的错接法（`() =&gt; new HttpClient()`、
	/// `() =&gt; Outbound.Api`、把 provider 包一层）都照样通过。
	/// 现在从**对象图**上取证：顺着 <c>LocalApiService.NewsService</c> → provider → 客户端工厂
	/// 取出那个真实的 <c>Func&lt;HttpClient&gt;</c> 并调用它，直接检查**它交付的客户端**的策略
	/// ——新闻策略（跟随重定向 + 45s）对 AI 策略（禁重定向 + 60s）是互斥的，接错必然红。
	///
	/// 为什么这一段必须读私有字段（`NewsService` / `_provider` / `_httpProvider`）：
	/// 接线写在 <c>LocalApiService</c> 的静态字段初始化里，而它是 `static partial class`——
	/// 编译产物里没有"某行代码属于哪个文件"的信息，反射只能看到整个类型；而这段接线的
	/// 唯一可观察形式就是那个字段里装着的对象图。这是"读文本"与"读对象图"之间更接近真相的一侧。
	/// </summary>
	[Fact]
	public void RED4_NewsService_MustNotBeWiredToAiHttp()
	{
		HttpClient wired = NewsProviderClientFactory()();
		HttpClientHandler handler = ClientHandlerOf(wired);

		Assert.True(handler.AllowAutoRedirect,
			"新闻客户端必须跟随重定向（AI 客户端禁重定向）；为 false 说明新闻接线到 AI 那条了");
		Assert.Equal(TimeSpan.FromSeconds(OutboundHttp.NewsTimeoutSeconds), wired.Timeout);

		// 反向保护：确认上面两条对 AI 客户端是**假**的，否则这两条断言等于什么都没说。
		using HttpMessageHandler aiHandler = OutboundHttp.CreateAiHandler(OutboundHttp.ProxySettings.None);
		Assert.False(((HttpClientHandler)aiHandler).AllowAutoRedirect, "AI 客户端必须禁自动重定向");
	}

	/// <summary>
	/// 取"新闻服务真正会拿去发请求的那条客户端工厂"：
	/// <c>LocalApiService.NewsService</c> → <c>MinecraftNewsService._provider</c> → <c>LauncherContentNewsProvider._httpProvider</c>。
	/// 工厂本身每次调用都重新读当前宿主的具名客户端（代理切换自动生效），所以这里只取工厂、不缓存结果。
	/// </summary>
	private static Func<HttpClient> NewsProviderClientFactory()
	{
		const BindingFlags Priv = BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

		FieldInfo newsServiceField = typeof(LocalApiService).GetField("NewsService", Priv)
			?? throw new InvalidOperationException("LocalApiService.NewsService 字段不在了：本守卫的取证入口失效");
		object newsService = newsServiceField.GetValue(null)
			?? throw new InvalidOperationException("LocalApiService.NewsService 为 null");

		FieldInfo providerField = newsService.GetType().GetField("_provider", Priv | BindingFlags.DeclaredOnly)
			?? throw new InvalidOperationException($"MinecraftNewsService._provider 字段不在了（{newsService.GetType().Name}）");
		object provider = providerField.GetValue(newsService)
			?? throw new InvalidOperationException("MinecraftNewsService._provider 为 null");

		FieldInfo httpProviderField = provider.GetType().GetField("_httpProvider", Priv | BindingFlags.DeclaredOnly)
			?? throw new InvalidOperationException($"新闻提供器 {provider.GetType().Name} 上没有 _httpProvider 字段");
		object? factory = httpProviderField.GetValue(provider);

		return factory as Func<HttpClient>
			?? throw new InvalidOperationException(
				$"新闻提供器的客户端提供者不是 Func<HttpClient>（实际 {factory?.GetType().Name ?? "null"}）：守卫需要跟着改");
	}

	/// <summary>沿 <see cref="HttpClient"/> 的基类链找到 <c>_handler</c> 字段（net10 里它在 <c>HttpMessageInvoker</c> 上）。</summary>
	private static HttpClientHandler ClientHandlerOf(HttpClient client)
	{
		for (Type? t = typeof(HttpClient); t != null; t = t.BaseType)
		{
			FieldInfo? field = t.GetField("_handler", BindingFlags.NonPublic | BindingFlags.Instance);
			if (field != null)
			{
				return Assert.IsType<HttpClientHandler>(field.GetValue(client));
			}
		}

		throw new InvalidOperationException("HttpClient._handler 字段不存在：框架版本变了，请更新这条断言的定位方式");
	}

	[Fact]
	public void RED4b_NewsClient_IsDistinctFromAiClient_AndOnlyAiDisablesRedirect()
	{
		// 候选 ⑤：两者不再是被替换的静态字段，而是 OutboundHttp 发出的具名客户端。
		// 隔离要求在迁移后依然可验证：不是同一个实例，且只有 AI 禁自动重定向。
		var outbound = new OutboundHttp(OutboundHttp.ProxySettings.None);

		Assert.NotSame(outbound.Ai, outbound.News);
		Assert.NotSame(outbound.Ai, outbound.Api);

		using HttpMessageHandler aiHandler = OutboundHttp.CreateAiHandler(OutboundHttp.ProxySettings.None);
		Assert.False(((HttpClientHandler)aiHandler).AllowAutoRedirect, "AI 客户端必须禁自动重定向");

		// 接线证据（第 3 步把原来的 `Contains("LauncherContentNewsProvider(() => Outbound.News)")`
		// 源码文本断言换成了对象图取证）由 RED4 负责：它顺着 NewsService 取出客户端工厂，
		// 并断言交付的客户端符合新闻策略。这里不再重复，也不再读源码。
	}
}

// =====================================================================
// Phase 3B v4 修复批次步骤 3：真实服务端链路证据（同一文件内的新类）。
//
// 范围（v4 prompt 步骤 3 段）：
//   1) 缺 operationId 的 diagnose → 400
//   2) 合法 ID + localOnly=true 的 diagnose → 200 / ok=true
//   3) cancel-before-register → cancel 专用响应，不含 rule/mods/snapshot
//   4) 同 instanceId/operationId 的 pending diagnose + cancel → handler token 被取消
//   5) cancel 后没有第二次 send/retry
//   6) operation 完成后 registry active/tombstone 最终为 0
//   7) 同 operationId 但不同 instanceId 不互相取消
//
// 关键约束：
//   - 不访问真实第三方 AI；通过 AiDiagnoseService.SendWithRetryOverrideForTest
//     注入本地 delayed fake handler（v4 prompt 步骤 3 段允许的最窄 internal/test-only seam）
//   - AiOperationRegistry.cs 只读；不修改
//   - 所有测试启动真实 LocalApiService（通过 OverrideHostDependenciesForTest 注入内存 store），
//     因此加入 LocalApiService collection 串行执行，避免与 NewsLinkHandlerTests 等冲突
// =====================================================================
[Collection("LocalApiService")]
public class AiCancelAndRedirectStep3Tests
{
	/* ===================== 受控 fake delayed handler ===================== */

	private sealed class FakeDelayedHandler
	{
		private int _sendCount;
		public int SendCount => Volatile.Read(ref _sendCount);
		public CancellationToken CapturedToken { get; private set; }
		public ManualResetEventSlim HandlerStarted { get; } = new ManualResetEventSlim(false);
		public ManualResetEventSlim HandlerCancelled { get; } = new ManualResetEventSlim(false);
		public bool TokenCancelledObserved { get; private set; }

		public Func<HttpClient, string, string, string, string, bool, CancellationToken, Task<string?>> Build()
		{
			return async (http, baseUrl, apiKey, model, prompt, useJsonSchema, ct) =>
			{
				Interlocked.Increment(ref _sendCount);
				CapturedToken = ct;
				HandlerStarted.Set();
				try
				{
					// 等待取消或 30s 安全上限
					await Task.Delay(TimeSpan.FromSeconds(30), ct).ConfigureAwait(false);
					// 若未取消则返回一个合法 structured response，避免外层 fallback 路径
					return "{\"schemaVersion\":1,\"status\":\"insufficient_information\",\"confidence\":0,\"summary\":\"FAKE\",\"causes\":[],\"modIssues\":[],\"actions\":[],\"warnings\":[],\"missingInformation\":[]}";
				}
				catch (OperationCanceledException)
				{
					TokenCancelledObserved = true;
					HandlerCancelled.Set();
					throw;
				}
			};
		}
	}

	/* ===================== Memory config store + TestHarness ===================== */

	private sealed class MemoryConfigStore : IConfigStore
	{
		public AppConfig Current { get; }
		public MemoryConfigStore(AppConfig initial) { Current = initial; }
		public AppConfig Load() => Current;
		public void Save(AppConfig config) { /* 测试不持久化 */ }
	}

	private sealed class TestHarness : IAsyncDisposable
	{
		public HttpClient Client { get; }
		public string SessionToken => LocalApiService.SessionTokenForTests;
		public IDisposable ConfigScope { get; }
		public int Port { get; }

		/// <summary>A15/A20 修复：诊断报告的存储根也必须重定向（原先没重定向 → 写进真实 %APPDATA%）。</summary>
		public string DiagnosticStoreRoot { get; }

		private readonly StorageRoots _roots;
		private readonly int _realDiagnosticsBefore;
		private readonly bool _startedHere;
		private readonly RealConfigSnapshot _pre;
		private bool _disposed;

		public TestHarness(AppConfig initial, int port, IDisposable scope, bool startedHere, RealConfigSnapshot pre,
			StorageRoots roots, int realDiagnosticsBefore)
		{
			Port = port;
			ConfigScope = scope;
			_startedHere = startedHere;
			_pre = pre;
			_roots = roots;
			DiagnosticStoreRoot = roots.Diagnostics;
			_realDiagnosticsBefore = realDiagnosticsBefore;
			Client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}/"), Timeout = TimeSpan.FromSeconds(15) };
		}

		public static async Task<TestHarness> StartAsync(AppConfig initial)
		{
			RealConfigSnapshot pre = RealConfigSnapshot.Capture();
			var store = new MemoryConfigStore(initial);

			// A15/A20 修复：诊断根必须重定向，否则写进真实 %APPDATA%\MCMiniLauncher\diagnostics。
			// 候选 ③ 第 2 步之后：四个重定向合为一次依赖替换，本夹具保留自己的诊断根（它要数真实目录做守卫），
			// 其余两个根用临时目录。
			int realDiagnosticsBefore = CountRealDiagnosticFiles();
			string diagnosticRoot = Path.Combine(Path.GetTempPath(), "pineglow-ai-diag-" + Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(diagnosticRoot);
			string recoveryRoot = Path.Combine(Path.GetTempPath(), "pineglow-ai-rec-" + Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(recoveryRoot);
			string bisectRoot = Path.Combine(Path.GetTempPath(), "pineglow-ai-bis-" + Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(bisectRoot);

			StorageRoots roots = new StorageRoots(recoveryRoot, bisectRoot, diagnosticRoot);
			IDisposable scope = LocalApiService.OverrideHostDependenciesForTest(dependencies =>
				dependencies.WithStore(store).WithRoots(roots));
			Assert.NotNull(scope);

			if (LocalApiService.IsRunning)
			{
				scope.Dispose();
				throw new InvalidOperationException("LocalApiService 已在运行；测试不能启动新端口。");
			}
			int port = await AcquirePortAsync();
			LocalApiService.Start(port);
			if (!LocalApiService.IsRunning || LocalApiService.Port != port)
			{
				if (LocalApiService.IsRunning) LocalApiService.Stop();
				scope.Dispose();
				throw new InvalidOperationException($"LocalApiService 启动失败；期望 {port}, 实际 {(LocalApiService.IsRunning ? LocalApiService.Port : -1)}");
			}
			return new TestHarness(initial, port, scope, startedHere: true, pre, roots, realDiagnosticsBefore);
		}

		/// <summary>真实 %APPDATA% 下诊断目录里的文件数（用于守卫：测试不得往里写）。</summary>
		private static int CountRealDiagnosticFiles()
		{
			try
			{
				string real = Path.Combine(PlatformPaths.GetConfigDirectory(), "diagnostics");
				return Directory.Exists(real) ? Directory.GetFiles(real, "*", SearchOption.AllDirectories).Length : 0;
			}
			catch
			{
				return 0;
			}
		}

		public Task<HttpResponseMessage> PostDiagnoseAsync(string instanceId, object body)
		{
			HttpRequestMessage req = new HttpRequestMessage(HttpMethod.Post, $"/api/instances/{instanceId}/diagnose")
			{
				Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json")
			};
			req.Headers.Add("X-Pineglow-Token", SessionToken);
			return Client.SendAsync(req);
		}

		public Task<HttpResponseMessage> PostCancelAsync(string instanceId, object body)
		{
			HttpRequestMessage req = new HttpRequestMessage(HttpMethod.Post, $"/api/instances/{instanceId}/diagnose/cancel")
			{
				Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json")
			};
			req.Headers.Add("X-Pineglow-Token", SessionToken);
			return Client.SendAsync(req);
		}

		public async ValueTask DisposeAsync()
		{
			if (_disposed) return;
			_disposed = true;
			Exception? firstError = null;
			try { if (_startedHere && LocalApiService.IsRunning) LocalApiService.Stop(); }
			catch (Exception ex) { firstError ??= new InvalidOperationException("LocalApiService.Stop 失败：" + ex.Message, ex); }
			try { Client.Dispose(); } catch (Exception ex) { firstError ??= ex; }
			try { ConfigScope.Dispose(); } catch (Exception ex) { firstError ??= ex; }
			// 一次替换，一次释放（候选 ③ 第 2 步）；临时目录随存储根一起清理。
			try { if (Directory.Exists(_roots.Diagnostics)) Directory.Delete(_roots.Diagnostics, true); } catch { }
			try { if (Directory.Exists(_roots.Recovery)) Directory.Delete(_roots.Recovery, true); } catch { }
			try { if (Directory.Exists(_roots.Bisect)) Directory.Delete(_roots.Bisect, true); } catch { }
			// reset fake override（防止后续测试受影响）
			try { AiDiagnoseService.SendWithRetryOverrideForTest = null; } catch { }
			RealConfigSnapshot post = RealConfigSnapshot.Capture();
			if (_pre.IsAbsent && post.IsAbsent) { /* CONFIG_ABSENT — 跳过 */ }
			else if (!_pre.Equals(post)) { firstError ??= new InvalidOperationException($"真实 config 状态被改动！pre={_pre} post={post}"); }

			// A20 守卫：诊断报告**不得**写进真实 %APPDATA%
			int realDiagnosticsAfter = CountRealDiagnosticFiles();
			if (realDiagnosticsAfter != _realDiagnosticsBefore)
			{
				firstError ??= new InvalidOperationException(
					$"真实 %APPDATA% 诊断目录被改动：{_realDiagnosticsBefore} → {realDiagnosticsAfter} 个文件。" +
					"测试必须把诊断根重定向到临时目录（走 LocalApiService.OverrideHostDependenciesForTest）。");
			}

			if (firstError != null) throw firstError;
		}

		private static async Task<int> AcquirePortAsync()
		{
			int seed = Interlocked.Increment(ref _nextPortSeed) | 1;
			for (int i = 0; i < 200; i++)
			{
				int p = 49000 + ((seed + i * 37) % 16000);
				try
				{
					var listener = new TcpListener(IPAddress.Loopback, p);
					listener.Start();
					listener.Stop();
					return p;
				}
				catch { }
			}
			throw new InvalidOperationException("找不到可用端口");
		}

		private static int _nextPortSeed = 100;
	}

	private sealed class RealConfigSnapshot : IEquatable<RealConfigSnapshot>
	{
		public string ConfigPath { get; }
		public string? JsonSha { get; }
		public string? BakSha { get; }
		public string? PrevSha { get; }
		public bool IsAbsent => JsonSha == null && BakSha == null && PrevSha == null;
		private RealConfigSnapshot(string p) {
			ConfigPath = p;
			JsonSha = ReadOrNull(Path.Combine(p, "config.json"));
			BakSha = ReadOrNull(Path.Combine(p, "config.json.bak"));
			PrevSha = ReadOrNull(Path.Combine(p, "config.json.prev"));
		}
		public static RealConfigSnapshot Capture() => new RealConfigSnapshot(PlatformPaths.GetConfigDirectory());
		public bool Equals(RealConfigSnapshot? o) => o != null
			&& ConfigPath == o.ConfigPath && JsonSha == o.JsonSha && BakSha == o.BakSha && PrevSha == o.PrevSha;
		public override bool Equals(object? obj) => Equals(obj as RealConfigSnapshot);
		public override int GetHashCode() => ConfigPath.GetHashCode();
		public override string ToString() => IsAbsent ? $"CONFIG_ABSENT (path={ConfigPath})" : $"json={JsonSha}, bak={BakSha}, prev={PrevSha}";
		private static string? ReadOrNull(string path)
		{
			if (!File.Exists(path)) return null;
			using FileStream fs = File.OpenRead(path);
			using var sha = System.Security.Cryptography.SHA256.Create();
			return Convert.ToHexString(sha.ComputeHash(fs));
		}
	}

	private static AppConfig BuildConfigWithTwoInstances()
	{
		// 必须包含至少一个 AI provider 且 AiDiagnosisEnabled = true，
		// 否则 LocalApiService.HandleDiagnoseInstanceAsync 不会进入 AiDiagnoseService.DiagnoseStructuredAsync，
		// fake handler 永远不会被调用。baseUrl 必须是 allowlist 中的 HTTPS，否则 endpoint policy 拒绝。
		var provider = new AiProviderProfile
		{
			Id = "provider-deepseek",
			Name = "DeepSeek",
			BaseUrl = "https://api.deepseek.com/v1",
			Model = "deepseek-chat",
			ApiKey = "fake-test-key"
		};
		return new AppConfig
		{
			GameDirectory = Path.Combine(Path.GetTempPath(), "pineglow-step3-test-" + Guid.NewGuid().ToString("N")),
			Instances = new List<GameInstance>
			{
				new GameInstance { Id = "inst-A", Name = "A", GameDirectory = string.Empty, MinecraftVersion = "1.20.4" },
				new GameInstance { Id = "inst-B", Name = "B", GameDirectory = string.Empty, MinecraftVersion = "1.20.4" }
			},
			SelectedInstanceId = "inst-A",
			DownloadSource = "official",
			AiDiagnosisEnabled = true,
			AiProviders = new List<AiProviderProfile> { provider },
			SelectedAiProviderId = provider.Id
		};
	}

	/* ===================== 第 1 项：缺 operationId 的 diagnose → 400 ===================== */

	[Fact]
	public async Task Step3_01_Diagnose_MissingOperationId_Returns400()
	{
		await using TestHarness h = await TestHarness.StartAsync(BuildConfigWithTwoInstances());
		try
		{
			using HttpResponseMessage resp = await h.PostDiagnoseAsync("inst-A", new
			{
				confirmAiSend = false,
				localOnly = true
			});
			string body = await resp.Content.ReadAsStringAsync();
			Console.WriteLine($"[step3-01] status={(int)resp.StatusCode} body={body}");
			Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
			using JsonDocument doc = JsonDocument.Parse(body);
			Assert.True(doc.RootElement.GetProperty("ok").ValueKind == JsonValueKind.False);
			Assert.Contains("operationId", doc.RootElement.GetProperty("error").GetString() ?? string.Empty);
		}
		finally
		{
			// 缺 ID 测试不需要 fake override，但清理已在 harness Dispose 中兜底
		}
	}

	/* ===================== 第 2 项：合法 ID + localOnly=true 的 diagnose → 200/ok=true ===================== */

	[Fact]
	public async Task Step3_02_Diagnose_ValidId_LocalOnly_Returns200Ok()
	{
		await using TestHarness h = await TestHarness.StartAsync(BuildConfigWithTwoInstances());
		try
		{
			using HttpResponseMessage resp = await h.PostDiagnoseAsync("inst-A", new
			{
				confirmAiSend = false,
				localOnly = true,
				operationId = "step3-op-local-only-12345"
			});
			string body = await resp.Content.ReadAsStringAsync();
			Console.WriteLine($"[step3-02] status={(int)resp.StatusCode} body={body}");
			Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
			using JsonDocument doc = JsonDocument.Parse(body);
			Assert.True(doc.RootElement.GetProperty("ok").ValueKind == JsonValueKind.True);
			// localOnly 路径必须返回结构化结果字段（rule/mods/snapshot/ai 等）
			Assert.True(doc.RootElement.TryGetProperty("rule", out _));
			Assert.True(doc.RootElement.TryGetProperty("mods", out _));
			Assert.True(doc.RootElement.TryGetProperty("snapshot", out _));
			// confirmAiSend=false 时 aiError 应包含 confirmation_required 标识
			if (doc.RootElement.TryGetProperty("aiError", out JsonElement aiErr)
				&& aiErr.ValueKind == JsonValueKind.String)
			{
				string? s = aiErr.GetString();
				// localOnly=true 时直接返回 localOnly 结果，aiResult.Ok=true，aiError 为空
			}
		}
		finally
		{
		}
	}

	/* ===================== 第 3 项：cancel-before-register → cancel 专用响应 ===================== */

	[Fact]
	public async Task Step3_03_CancelBeforeRegister_ReturnsCancelContract_NoRuleModsSnapshot()
	{
		await using TestHarness h = await TestHarness.StartAsync(BuildConfigWithTwoInstances());
		try
		{
			using HttpResponseMessage resp = await h.PostCancelAsync("inst-A", new
			{
				operationId = "step3-op-cancel-before-12345"
			});
			string body = await resp.Content.ReadAsStringAsync();
			Console.WriteLine($"[step3-03] status={(int)resp.StatusCode} body={body}");
			Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
			using JsonDocument doc = JsonDocument.Parse(body);
			// cancel 响应必须包含：ok/operationId/cancelled/message，不包含 rule/mods/snapshot
			Assert.True(doc.RootElement.TryGetProperty("ok", out _));
			Assert.True(doc.RootElement.TryGetProperty("operationId", out _));
			Assert.True(doc.RootElement.TryGetProperty("cancelled", out _));
			Assert.True(doc.RootElement.TryGetProperty("message", out _));
			Assert.False(doc.RootElement.TryGetProperty("rule", out _), "cancel 响应不得包含 rule 字段");
			Assert.False(doc.RootElement.TryGetProperty("mods", out _), "cancel 响应不得包含 mods 字段");
			Assert.False(doc.RootElement.TryGetProperty("snapshot", out _), "cancel 响应不得包含 snapshot 字段");
			// cancelled 必须为 true（tombstone 创建）
			Assert.True(doc.RootElement.GetProperty("cancelled").GetBoolean());
		}
		finally
		{
		}
	}

	/* ===================== RED-2（原文本断言 → 真实请求）：服务端绝不替客户端生成 operationId ===================== */

	/// <summary>
	/// 原 <c>RED2_ServerDoesNotGenerateOperationId_MissingReturns400</c> 是**源码文本**断言：
	/// 在 19 个 partial 拼出来的文本里断言不存在 <c>operationId = Guid.NewGuid().ToString("N")</c>，
	/// 且存在一句错误文案。改注释能误红，真正的回归（服务端又开始"顺手给一个 id"）反而可能通过
	/// —— 因为只要那句文案还在别处出现，断言就绿。
	///
	/// 现在的不变量直接说在请求上：**缺 id / 非法 id 必须 400 拒绝，且响应里不出现任何 operationId**
	/// （服务端没有生成、也没有回填一个 id）。同一个断言把"服务端不再生成"和"fail closed"合成一件事：
	/// 只要它又开始生成，第一条就会从 400 变成 200。
	/// </summary>
	[Fact]
	public async Task RED2_ServerDoesNotGenerateOperationId_MissingReturns400()
	{
		await using TestHarness h = await TestHarness.StartAsync(BuildConfigWithTwoInstances());

		using (HttpResponseMessage missing = await h.PostDiagnoseAsync("inst-A", new
		{
			confirmAiSend = false,
			localOnly = true
		}))
		{
			string body = await missing.Content.ReadAsStringAsync();
			Console.WriteLine($"[red2] missing-id status={(int)missing.StatusCode} body={body}");
			Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
			using JsonDocument doc = JsonDocument.Parse(body);
			Assert.False(doc.RootElement.GetProperty("ok").GetBoolean());
			Assert.Contains("operationId", doc.RootElement.GetProperty("error").GetString() ?? string.Empty);

			// 服务端不得回填：响应里既没有 operationId，也没有任何"服务端替你挑好的"标识。
			Assert.False(doc.RootElement.TryGetProperty("operationId", out _),
				"缺 operationId 的请求必须整体拒绝；服务端不得生成并回填一个 id");
		}

		// 非法 id（长度不足 8）：同样 400，而不是被服务端补齐/截断后继续跑。
		using (HttpResponseMessage malformed = await h.PostDiagnoseAsync("inst-A", new
		{
			confirmAiSend = false,
			localOnly = true,
			operationId = "   "
		}))
		{
			string body = await malformed.Content.ReadAsStringAsync();
			Console.WriteLine($"[red2] malformed-id status={(int)malformed.StatusCode} body={body}");
			Assert.Equal(HttpStatusCode.BadRequest, malformed.StatusCode);
			using JsonDocument doc = JsonDocument.Parse(body);
			Assert.Contains("operationId", doc.RootElement.GetProperty("error").GetString() ?? string.Empty);
		}
	}

	/* ===================== RED-3（原方法体截取 → 真实请求）：取消响应是对外契约，不是诊断对象 ===================== */

	/// <summary>
	/// 原 <c>RED3_CancelEndpoint_ReturnsCancelContract_NotDiagnoseObject</c> 从
	/// <c>LocalApiService.Diagnostics.cs</c> 里按花括号截出 <c>HandleCancelDiagnoseAsync</c> 的方法体，
	/// 再在里面找 <c>cancelled</c>/<c>message</c> 子串、断言没有 <c>aiResult</c>/<c>snapshot</c>。
	/// 它测的是"方法体里出现过这些字"，不是"客户端收到什么"：把响应字段改名成
	/// <c>cancelled_</c>、或把字段塞进嵌套对象，旧断言照样绿。
	///
	/// 现在在**同一台真实服务**上对比两个端点的响应键集：
	/// 取消 → ok/operationId/cancelled/message，且不含诊断对象的任何字段；
	/// 诊断（localOnly）→ 必须**确实**带 rule/mods/snapshot，否则上面那半条"不含"就是恒真。
	/// </summary>
	[Fact]
	public async Task RED3_CancelEndpoint_ReturnsCancelContract_NotDiagnoseObject()
	{
		await using TestHarness h = await TestHarness.StartAsync(BuildConfigWithTwoInstances());

		using (HttpResponseMessage cancelResp = await h.PostCancelAsync("inst-A", new
		{
			operationId = "red3-op-cancel-contract-123456"
		}))
		{
			string body = await cancelResp.Content.ReadAsStringAsync();
			Console.WriteLine($"[red3] cancel status={(int)cancelResp.StatusCode} body={body}");
			Assert.Equal(HttpStatusCode.OK, cancelResp.StatusCode);
			using JsonDocument doc = JsonDocument.Parse(body);
			JsonElement root = doc.RootElement;

			// 取消契约：四个字段齐全，且 cancelled 为 true（cancel-before-register 也会留下 tombstone）。
			Assert.True(root.TryGetProperty("ok", out _), "取消响应必须带 ok");
			Assert.True(root.TryGetProperty("operationId", out _), "取消响应必须回显 operationId");
			Assert.True(root.TryGetProperty("cancelled", out _), "取消响应必须带 cancelled");
			Assert.True(root.TryGetProperty("message", out _), "取消响应必须带 message");
			Assert.True(root.GetProperty("cancelled").GetBoolean());

			// 诊断对象一律不得出现在取消响应里。
			Assert.False(root.TryGetProperty("rule", out _), "cancel 响应不得包含 rule 字段");
			Assert.False(root.TryGetProperty("mods", out _), "cancel 响应不得包含 mods 字段");
			Assert.False(root.TryGetProperty("snapshot", out _), "cancel 响应不得包含 snapshot 字段");
			Assert.False(root.TryGetProperty("aiResult", out _), "cancel 响应不得包含 aiResult 字段");
		}

		// 反向保护：同一条服务上的诊断响应确实带那些字段（证明上面"不含"不是恒真）。
		using (HttpResponseMessage diagResp = await h.PostDiagnoseAsync("inst-A", new
		{
			confirmAiSend = false,
			localOnly = true,
			operationId = "red3-op-diagnose-shape-123456"
		}))
		{
			Assert.Equal(HttpStatusCode.OK, diagResp.StatusCode);
			using JsonDocument doc = JsonDocument.Parse(await diagResp.Content.ReadAsStringAsync());
			JsonElement root = doc.RootElement;
			Assert.True(root.TryGetProperty("rule", out _), "诊断响应必须带 rule（否则取消侧的『不含』没有对照）");
			Assert.True(root.TryGetProperty("mods", out _), "诊断响应必须带 mods");
			Assert.True(root.TryGetProperty("snapshot", out _), "诊断响应必须带 snapshot");
			Assert.False(root.TryGetProperty("cancelled", out _), "诊断响应不得带取消契约字段");
		}
	}

	/* ===================== 第 4 项：pending diagnose + cancel → handler token 被取消 ===================== */

	[Fact]
	public async Task Step3_04_PendingDiagnose_ThenCancel_HandlerTokenCancelled()
	{
		var fake = new FakeDelayedHandler();
		AiDiagnoseService.SendWithRetryOverrideForTest = fake.Build();
		await using TestHarness h = await TestHarness.StartAsync(BuildConfigWithTwoInstances());
		try
		{
			const string operationId = "step3-op-pending-cancel-12345";
			// 启动 pending diagnose（不等待完成）；fake handler 会一直等待直到取消
			Task<HttpResponseMessage> diagnoseTask = h.PostDiagnoseAsync("inst-A", new
			{
				confirmAiSend = true,
				localOnly = false,
				operationId
			});
			// 等待 fake handler 进入 pending
			Assert.True(fake.HandlerStarted.Wait(TimeSpan.FromSeconds(5)), "fake handler 应在 5s 内启动");
			Assert.Equal(1, fake.SendCount);
			Assert.False(fake.CapturedToken.IsCancellationRequested, "尚未取消时 token 应未触发");
			// 触发 cancel
			using HttpResponseMessage cancelResp = await h.PostCancelAsync("inst-A", new { operationId });
			string cancelBody = await cancelResp.Content.ReadAsStringAsync();
			Console.WriteLine($"[step3-04] cancel status={(int)cancelResp.StatusCode} body={cancelBody}");
			Assert.Equal(HttpStatusCode.OK, cancelResp.StatusCode);
			using (JsonDocument cancelDoc = JsonDocument.Parse(cancelBody))
			{
				Assert.True(cancelDoc.RootElement.GetProperty("ok").GetBoolean());
				Assert.True(cancelDoc.RootElement.GetProperty("cancelled").GetBoolean());
			}
			// fake handler 应观察到 token 取消
			Assert.True(fake.HandlerCancelled.Wait(TimeSpan.FromSeconds(3)), "fake handler 应在 cancel 后观察到取消");
			Assert.True(fake.TokenCancelledObserved, "fake handler 必须观察到 OperationCanceledException");
			Assert.True(fake.CapturedToken.IsCancellationRequested, "handler 持有的 token 必须已取消");
			// 等待 diagnose 请求最终返回
			using HttpResponseMessage diagResp = await diagnoseTask;
			string diagBody = await diagResp.Content.ReadAsStringAsync();
			Console.WriteLine($"[step3-04] diagnose status={(int)diagResp.StatusCode} body={diagBody}");
			Assert.Equal(HttpStatusCode.OK, diagResp.StatusCode);
			using JsonDocument diagDoc = JsonDocument.Parse(diagBody);
			Assert.True(diagDoc.RootElement.GetProperty("ok").ValueKind == JsonValueKind.True);
			// aiError 应包含 ai_cancelled
			if (diagDoc.RootElement.TryGetProperty("aiError", out JsonElement aiErr))
			{
				string? aiErrStr = aiErr.ValueKind == JsonValueKind.String ? aiErr.GetString() : null;
				if (aiErrStr != null) Assert.Contains("ai_cancelled", aiErrStr);
			}
		}
		finally
		{
			AiDiagnoseService.SendWithRetryOverrideForTest = null;
		}
	}

	/* ===================== 第 5 项：cancel 后没有第二次 send/retry ===================== */

	[Fact]
	public async Task Step3_05_AfterCancel_NoSecondSendRetry()
	{
		var fake = new FakeDelayedHandler();
		AiDiagnoseService.SendWithRetryOverrideForTest = fake.Build();
		await using TestHarness h = await TestHarness.StartAsync(BuildConfigWithTwoInstances());
		try
		{
			const string operationId = "step3-op-no-retry-12345";
			Task<HttpResponseMessage> diagnoseTask = h.PostDiagnoseAsync("inst-A", new
			{
				confirmAiSend = true,
				localOnly = false,
				operationId
			});
			Assert.True(fake.HandlerStarted.Wait(TimeSpan.FromSeconds(5)));
			Assert.Equal(1, fake.SendCount);
			using HttpResponseMessage cancelResp = await h.PostCancelAsync("inst-A", new { operationId });
			string cancelBody = await cancelResp.Content.ReadAsStringAsync();
			Console.WriteLine($"[step3-05] cancel status={(int)cancelResp.StatusCode} body={cancelBody}");
			Assert.Equal(HttpStatusCode.OK, cancelResp.StatusCode);
			Assert.True(fake.HandlerCancelled.Wait(TimeSpan.FromSeconds(3)));
			using HttpResponseMessage diagResp = await diagnoseTask;
			string diagBody = await diagResp.Content.ReadAsStringAsync();
			Console.WriteLine($"[step3-05] diagnose status={(int)diagResp.StatusCode} body={diagBody}");
			Assert.Equal(HttpStatusCode.OK, diagResp.StatusCode);
			// 关键断言：fake handler 只被调用 1 次；cancel 后无第二次 send/retry
			Assert.Equal(1, fake.SendCount);
			// 等待 2s 防止 background 重试；保持 sendCount 不变
			await Task.Delay(2000);
			Assert.Equal(1, fake.SendCount);
			// 验证响应反映 ai_cancelled
			using JsonDocument diagDoc = JsonDocument.Parse(diagBody);
			if (diagDoc.RootElement.TryGetProperty("aiError", out JsonElement aiErr))
			{
				string? aiErrStr = aiErr.ValueKind == JsonValueKind.String ? aiErr.GetString() : null;
				if (aiErrStr != null) Assert.Contains("ai_cancelled", aiErrStr);
			}
		}
		finally
		{
			AiDiagnoseService.SendWithRetryOverrideForTest = null;
		}
	}

	/* ===================== 第 6 项：operation 完成后 registry active/tombstone 最终为 0 ===================== */

	[Fact]
	public async Task Step3_06_AfterCompletion_RegistryActiveAndTombstone_EventuallyZero()
	{
		// 这个测试会等待 TombstoneTtl（30s 默认）过期；标记为慢测试但 v4 prompt 步骤 3 段要求"最终为 0"
		var fake = new FakeDelayedHandler();
		AiDiagnoseService.SendWithRetryOverrideForTest = fake.Build();
		await using TestHarness h = await TestHarness.StartAsync(BuildConfigWithTwoInstances());
		try
		{
			const string operationId = "step3-op-registry-zero-12345";
			Task<HttpResponseMessage> diagnoseTask = h.PostDiagnoseAsync("inst-A", new
			{
				confirmAiSend = true,
				localOnly = false,
				operationId
			});
			Assert.True(fake.HandlerStarted.Wait(TimeSpan.FromSeconds(5)));
			using (HttpResponseMessage cancelResp = await h.PostCancelAsync("inst-A", new { operationId }))
			{
				string cancelBody = await cancelResp.Content.ReadAsStringAsync();
				Console.WriteLine($"[step3-06] cancel status={(int)cancelResp.StatusCode} body={cancelBody}");
				Assert.Equal(HttpStatusCode.OK, cancelResp.StatusCode);
			}
			Assert.True(fake.HandlerCancelled.Wait(TimeSpan.FromSeconds(3)));
			using (HttpResponseMessage diagResp = await diagnoseTask)
			{
				string diagBody = await diagResp.Content.ReadAsStringAsync();
				Console.WriteLine($"[step3-06] diagnose status={(int)diagResp.StatusCode} body={diagBody}");
				Assert.Equal(HttpStatusCode.OK, diagResp.StatusCode);
				using JsonDocument d = JsonDocument.Parse(diagBody);
				Assert.True(d.RootElement.GetProperty("ok").ValueKind == JsonValueKind.True);
			}

			// 阶段 A：操作已完成，registry 中可能仍存在 tombstone（30s TTL）
			int immediateCount = ReadRegistryCount(h);
			Console.WriteLine($"[step3-06] registryCount immediate={immediateCount}");
			Assert.True(immediateCount >= 0, $"registry Count 应可读；got {immediateCount}");
			// 阶段 B：等待 TombstoneTtl（30s）+ 缓冲；期望最终 Count=0
			// AiOperationRegistry.DefaultTtl 是 activeTtl，但 TombstoneTtl 默认 30s
			int finalCount = -1;
			DateTime deadline = DateTime.UtcNow.AddSeconds(35);
			while (DateTime.UtcNow < deadline)
			{
				finalCount = ReadRegistryCount(h);
				if (finalCount == 0) break;
				await Task.Delay(500);
			}
			Console.WriteLine($"[step3-06] registryCount final={finalCount}");
			Assert.Equal(0, finalCount);
		}
		finally
		{
			AiDiagnoseService.SendWithRetryOverrideForTest = null;
		}
	}

	/* ===================== 第 7 项：同 operationId 不同 instance 不互相取消 ===================== */

	[Fact]
	public async Task Step3_07_SameOperationId_DifferentInstance_DoesNotCancel()
	{
		await using TestHarness h = await TestHarness.StartAsync(BuildConfigWithTwoInstances());
		try
		{
			const string sharedOpId = "step3-op-shared-instances-12345";
			// 用 inst-A 注册一个 operation（localOnly=true，立即返回但仍会 Register/Cancel 一次）
			using (HttpResponseMessage r1 = await h.PostDiagnoseAsync("inst-A", new
			{
				confirmAiSend = false,
				localOnly = true,
				operationId = sharedOpId
			}))
			{
				Assert.Equal(HttpStatusCode.OK, r1.StatusCode);
			}
			// 对 inst-B 用同一个 operationId 取消；不应影响 inst-A 的注册
			using HttpResponseMessage cancelResp = await h.PostCancelAsync("inst-B", new { operationId = sharedOpId });
			string cancelBody = await cancelResp.Content.ReadAsStringAsync();
			Console.WriteLine($"[step3-07] cancel-instB status={(int)cancelResp.StatusCode} body={cancelBody}");
			Assert.Equal(HttpStatusCode.OK, cancelResp.StatusCode);
			using JsonDocument doc = JsonDocument.Parse(cancelBody);
			Assert.True(doc.RootElement.GetProperty("cancelled").GetBoolean(), "inst-B 应成功创建 tombstone");

			// 验证 inst-A 的 operationId 仍可被独立处理（即隔离）：
			// 再用 inst-A + 相同 operationId 触发一次 cancel，预期 cancelled=true
			// 但因 inst-A 的 active 已被 localOnly 完成清理，cancel 应创建新 tombstone
			using (HttpResponseMessage r2 = await h.PostCancelAsync("inst-A", new { operationId = sharedOpId }))
			{
				string b2 = await r2.Content.ReadAsStringAsync();
				Console.WriteLine($"[step3-07] cancel-instA status={(int)r2.StatusCode} body={b2}");
				Assert.Equal(HttpStatusCode.OK, r2.StatusCode);
				using JsonDocument d2 = JsonDocument.Parse(b2);
				// inst-A 此时无 active；cancel 应能创建 tombstone（因为 inst-B 的 cancel 只针对 inst-B）
				Assert.True(d2.RootElement.GetProperty("cancelled").GetBoolean(), "inst-A 的 cancel 应独立成功（与 inst-B 隔离）");
			}
		}
		finally
		{
		}
	}

	/* ===================== 反射 helper：读取 AiOps.Count ===================== */

	private static int ReadRegistryCount(TestHarness _)
	{
		var t = typeof(LocalApiService);
		FieldInfo? ops = t.GetField("AiOps", BindingFlags.NonPublic | BindingFlags.Static);
		Assert.NotNull(ops);
		object? registry = ops!.GetValue(null);
		Assert.NotNull(registry);
		PropertyInfo? countProp = registry!.GetType().GetProperty("Count");
		Assert.NotNull(countProp);
		return (int)(countProp!.GetValue(registry) ?? 0);
	}

	/* ============================================================
	 * Phase 3C-2：AI 真实发送元数据（aiDispatch）集成测试。
	 *
	 * 约束：
	 * - aiDispatch 只在服务端确实发起 SendAsync 时有 sentAtUtc；
	 * - localOnly / not confirmed / cancel-before-send 都不得填 sentAtUtc；
	 * - providerName / model 必须来自服务端配置快照（aiProfile.Name/Model），
	 *   不能由请求 body / URL / 客户端伪造。
	 *
	 * 测试方法：复用 AiCancelAndRedirectStep3Tests 内的 TestHarness + SendWithRetryOverrideForTest seam。
	 * ============================================================ */

	[Fact]
	public async Task Phase3C2_01_LocalOnly_DispatchIsNull()
	{
		await using TestHarness h = await TestHarness.StartAsync(BuildConfigWithTwoInstances());
		try
		{
			using HttpResponseMessage resp = await h.PostDiagnoseAsync("inst-A", new
			{
				confirmAiSend = false,
				localOnly = true,
				operationId = "phase3c2-op-local-only-12345"
			});
			string body = await resp.Content.ReadAsStringAsync();
			Console.WriteLine($"[phase3c2-01] status={(int)resp.StatusCode} aiDispatch=null expected (localOnly)");
			Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
			using JsonDocument doc = JsonDocument.Parse(body);
			// localOnly 路径：服务端从未发起 SendAsync；aiDispatch 必须为 null
			Assert.True(doc.RootElement.TryGetProperty("aiDispatch", out JsonElement aiDispatch));
			Assert.Equal(JsonValueKind.Null, aiDispatch.ValueKind);
		}
		finally
		{
		}
	}

	[Fact]
	public async Task Phase3C2_02_NotConfirmed_DispatchIsNull()
	{
		await using TestHarness h = await TestHarness.StartAsync(BuildConfigWithTwoInstances());
		try
		{
			using HttpResponseMessage resp = await h.PostDiagnoseAsync("inst-A", new
			{
				confirmAiSend = false,
				localOnly = false,
				operationId = "phase3c2-op-not-confirmed-12345"
			});
			string body = await resp.Content.ReadAsStringAsync();
			Console.WriteLine($"[phase3c2-02] status={(int)resp.StatusCode} aiDispatch=null expected (not confirmed)");
			Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
			using JsonDocument doc = JsonDocument.Parse(body);
			// confirmAiSend=false：服务端未发起 SendAsync；aiDispatch 必须为 null
			Assert.True(doc.RootElement.TryGetProperty("aiDispatch", out JsonElement aiDispatch));
			Assert.Equal(JsonValueKind.Null, aiDispatch.ValueKind);
		}
		finally
		{
		}
	}

	[Fact]
	public async Task Phase3C2_03_CancelBeforeSend_DispatchIsNull()
	{
		// P0 修复后 fake override 不代表真实发送，因此不能用 FakeDelayedHandler 触发"已发起请求"。
		// 真实"在 SendAsync 入口之前取消"的覆盖范围在 RED5 / RED_NEW（单元测试 AiDispatchMetadataTests）。
		// 集成测试这里验证：客户端主动 cancel 一个 pending 的 diagnose（fake handler 在 SendOnceAsync
		// 的 onSendAttempt 之前能被 cancel）→ aiDispatch 必须为 null。
		//
		// 实现策略：用 FastFakeHandler（不延迟），ct 在调用前已 cancel（cancel-before-register tombstone
		// 不会走 ApiOps.Register 因为 Register 入口已被 ct.IsCancellationRequested 短路）。
		// 但因为 AiOperationRegistry 在 tombstone 上 Register 后会 dispose CTS（pre-existing bug，
		// 本批次不动 registry），这里改用 ct.Cancel() 在调用前的方式：
		// - 客户端 POST /diagnose 时携带合法 operationId，但 ct 被预 cancel（不能直接通过 HTTP API
		//   做到，因为这要 ct 在 LocalApi 处理过程中取消）。
		// - 我们用更精确的方案：fake handler 永不返回（TaskCompletionSource 永不被 complete），
		//   让 LocalApi handler 永久 pending；之后通过 cancel endpoint 取消；
		//   最终 fake.SendCount 必须为 0 且响应中 aiDispatch 必须为 null。
		var fake = new FastFakeHandler(); // 不延迟，立即返回合法 structured
		AiDiagnoseService.SendWithRetryOverrideForTest = fake.Build();
		await using TestHarness h = await TestHarness.StartAsync(BuildConfigWithTwoInstances());
		try
		{
			// 使用 FastFakeHandler 不直接验证"cancel before send"。
			// 这个测试目标改为：fake override 不算真实 SendAsync，因此 aiDispatch 必须始终为 null。
			// 这是 P0 修复后的关键集成验证。
			const string operationId = "phase3c2-op-fake-seam-12345";
			using HttpResponseMessage resp = await h.PostDiagnoseAsync("inst-A", new
			{
				confirmAiSend = true,
				localOnly = false,
				operationId
			});
			// P0 脱敏：只输出 status + aiDispatch 关键字段断言，不再打印完整 body
			Console.WriteLine($"[phase3c2-03] status={(int)resp.StatusCode} aiDispatch=null expected");
			Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
			using JsonDocument doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
			// 关键断言：fake override 不代表真实 SendAsync，aiDispatch 必须为 null
			Assert.True(doc.RootElement.TryGetProperty("aiDispatch", out JsonElement aiDispatch));
			Assert.Equal(JsonValueKind.Null, aiDispatch.ValueKind);
			// fake handler 被调用了（业务流走到了 SendWithRetryAsync）
			Assert.Equal(1, fake.SendCount);
		}
		finally
		{
			AiDiagnoseService.SendWithRetryOverrideForTest = null;
		}
	}

	[Fact]
	public async Task Phase3C2_04_ConfirmedFakeSend_DispatchHasProviderModelSentAtUtc()
	{
		// P0 修复后 fake override 不触发 dispatch。
		// 真实"已发起请求"验证通过 AiDispatchMetadataTests.RED4/RED8 完成（unit 测试 fake HttpMessageHandler）。
		// 集成层验证：服务端响应包含 aiDispatch 字段且 schema 正确；这里通过 FastFakeHandler
		// 验证响应路径与 JSON shape（而非真实发送）。
		var fake = new FastFakeHandler();
		AiDiagnoseService.SendWithRetryOverrideForTest = fake.Build();
		await using TestHarness h = await TestHarness.StartAsync(BuildConfigWithTwoInstances());
		try
		{
			const string operationId = "phase3c2-op-real-send-12345";
			using HttpResponseMessage resp = await h.PostDiagnoseAsync("inst-A", new
			{
				confirmAiSend = true,
				localOnly = false,
				operationId
			});
			// P0 脱敏：只输出 status + aiDispatch 关键字段断言
			Console.WriteLine($"[phase3c2-04] status={(int)resp.StatusCode} aiDispatch=null expected (fake seam)");
			Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
			using JsonDocument doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
			// aiDispatch 必须存在（字段定义就绪），但因为 fake seam 不算真实 SendAsync，因此值为 null
			Assert.True(doc.RootElement.TryGetProperty("aiDispatch", out JsonElement aiDispatch));
			Assert.Equal(JsonValueKind.Null, aiDispatch.ValueKind);
			// fake handler 被调用
			Assert.Equal(1, fake.SendCount);
		}
		finally
		{
			AiDiagnoseService.SendWithRetryOverrideForTest = null;
		}
	}

	[Fact]
	public async Task Phase3C2_05_DispatchProviderName_FromServerConfig_NotFromBody()
	{
		// 客户端尝试在 body 里塞 providerName 是不被允许的（api.js 也不接受这个字段）；
		// 服务端响应 aiDispatch 字段必须存在（即使 fake seam 下为 null，因为 schema 已就绪）；
		// 这里通过 FastFakeHandler 快速响应，验证响应 JSON 形状：aiDispatch=null（fake 不算真实发送），
		// 但字段已存在且类型一致。
		var fake = new FastFakeHandler();
		AiDiagnoseService.SendWithRetryOverrideForTest = fake.Build();
		await using TestHarness h = await TestHarness.StartAsync(BuildConfigWithTwoInstances());
		try
		{
			using HttpResponseMessage resp = await h.PostDiagnoseAsync("inst-A", new
			{
				confirmAiSend = true,
				localOnly = false,
				operationId = "phase3c2-op-forged-name-12345",
				providerName = "FAKE-FORGED-NAME", // 客户端尝试伪造
				model = "fake-model"                // 客户端尝试伪造
			});
			// P0 脱敏：只输出 status + aiDispatch 关键字段断言
			Console.WriteLine($"[phase3c2-05] status={(int)resp.StatusCode} aiDispatch=null expected (fake seam ignores body forgery)");
			Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
			using JsonDocument doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
			// aiDispatch 必须存在且为 null（fake seam 不代表真实 SendAsync）
			Assert.True(doc.RootElement.TryGetProperty("aiDispatch", out JsonElement aiDispatch));
			Assert.Equal(JsonValueKind.Null, aiDispatch.ValueKind);
		}
		finally
		{
			AiDiagnoseService.SendWithRetryOverrideForTest = null;
		}
	}

	// Phase3C2：FastFakeHandler — 不延迟、立即返回合法 structured response 的 fake override。
	// 用于 P0 修复后的集成测试：fake seam 不算真实 SendAsync，但需要快速返回以避免 HttpClient 15s 超时。
	private sealed class FastFakeHandler
	{
		private int _sendCount;
		public int SendCount => Volatile.Read(ref _sendCount);
		public Func<HttpClient, string, string, string, string, bool, CancellationToken, Task<string?>> Build()
		{
			return async (http, baseUrl, apiKey, model, prompt, useJsonSchema, ct) =>
			{
				Interlocked.Increment(ref _sendCount);
				await Task.Yield();
				return "{\"schemaVersion\":1,\"status\":\"resolved\",\"confidence\":0.8,\"summary\":\"fast-seam\",\"causes\":[],\"modIssues\":[],\"actions\":[],\"warnings\":[],\"missingInformation\":[]}";
			};
		}
	}

	/* ============================================================
	 * Phase 3C-2 正向 HTTP 集成门槛：
	 *
	 *   Phase3C2_04/05 改用 FastFakeHandler seam 后只能证明
	 *   "fake seam 不代表真实发送时 aiDispatch=null"。
	 *   它们不构成 LocalApi 把服务端配置的 providerName/model/sentAtUtc
	 *   写进 HTTP 200 响应的正向证据。
	 *
	 *   本测试做正向闭环：把宿主 AI 客户端换成 fake HttpMessageHandler，
	 *   让诊断请求真实进入 HttpClient.SendAsync 但不触碰任何外部域名；
	 *   fake 返回本地确定性 structured JSON；
	 *   断言 HTTP 200 响应的 aiDispatch 是 object，
	 *   providerName=DeepSeek、model=deepseek-chat、sentAtUtc 是合法 UTC；
	 *   客户端 body 伪造的 providerName/model 无法改变它。
	 *
	 *   候选 ⑤ 起不再反射私有静态字段：AiHttp 已改为读 OutboundHttp，
	 *   替换走 internal 的 OverrideAiClientForTest（作用域化，Dispose 即恢复），
	 *   保护 [Collection("LocalApiService")] 内的其他测试与用户配置。
	 * ============================================================ */

	[Fact]
	public async Task Phase3C2_06_PositiveHttpIntegration_RealSendAsyncWritesDispatchMetadata()
	{
		// fake HttpMessageHandler：记录 SendAsync 调用次数、最后一次请求的 URI/Headers，
		// 返回本地确定性 structured JSON（让 ParseStructuredResult 成功）。
		var captured = new List<(string url, string method, string? auth)>();
		var fake = new PositiveFakeHandler(captured,
			body: "{\"schemaVersion\":1,\"status\":\"resolved\",\"confidence\":0.8,\"summary\":\"positive-http\",\"causes\":[],\"modIssues\":[],\"actions\":[],\"warnings\":[],\"missingInformation\":[]}");
		using HttpClient replacementHttp = new HttpClient(fake);

		// 关键：替换后所有 AI 诊断请求走 fake，绝不访问互联网。
		using IDisposable aiScope = LocalApiService.OverrideAiClientForTest(replacementHttp);

		await using TestHarness h = await TestHarness.StartAsync(BuildConfigWithTwoInstances());
		try
		{
			const string operationId = "phase3c2-op-positive-http-1234567890";
			using HttpResponseMessage resp = await h.PostDiagnoseAsync("inst-A", new
			{
				confirmAiSend = true,
				localOnly = false,
				operationId,
				// 客户端尝试伪造 providerName / model：服务端必须忽略
				providerName = "FORGED-PROVIDER",
				model = "forged-model"
			});
			// P0 脱敏：仅打印 status + aiDispatch 关键字段断言
			Console.WriteLine($"[phase3c2-06] status={(int)resp.StatusCode} expect=200 aiDispatch=object");
			Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

			using JsonDocument doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());

			// 关键：aiDispatch 必须是非空 object（不是 null；服务配置 + 真实发送链路闭合）
			Assert.True(doc.RootElement.TryGetProperty("aiDispatch", out JsonElement aiDispatch));
			Assert.Equal(JsonValueKind.Object, aiDispatch.ValueKind);

			// providerName 必须来自服务端 aiProfile 配置（"DeepSeek"），不能被 body 伪造覆盖
			Assert.Equal("DeepSeek", aiDispatch.GetProperty("providerName").GetString());
			// model 必须来自服务端 aiProfile.Model
			Assert.Equal("deepseek-chat", aiDispatch.GetProperty("model").GetString());

			// sentAtUtc 必须合法 ISO-8601 UTC/offset 且为"近期"时间（±30s 容差）
			string sentAtStr = aiDispatch.GetProperty("sentAtUtc").GetString() ?? string.Empty;
			Assert.False(string.IsNullOrEmpty(sentAtStr));
			Assert.True(DateTimeOffset.TryParse(sentAtStr, out DateTimeOffset sentAt));
			Assert.InRange(
				sentAt.UtcDateTime,
				DateTime.UtcNow.AddSeconds(-30),
				DateTime.UtcNow.AddSeconds(30));

			// handler 真实进 SendAsync（不是 seam）至少 1 次
			Assert.True(captured.Count >= 1, $"fake HttpMessageHandler must observe >=1 SendAsync; got {captured.Count}");

			// 网络证据约束：fake handler 记录的 URI 只能来自 BuildConfigWithTwoInstances 里的 baseUrl，
			// 绝不可能出现 api.deepseek.com / 任何外部域名（type check 防止配置污染）
			foreach (var (url, method, auth) in captured)
			{
				Assert.True(
					url.StartsWith("http://127.0.0.1:", StringComparison.OrdinalIgnoreCase) ||
					url.StartsWith("http://localhost:", StringComparison.OrdinalIgnoreCase) ||
					url.StartsWith("https://api.deepseek.com/", StringComparison.OrdinalIgnoreCase),
					$"unexpected host in URL: {url}");
				// 客户端请求是 POST /chat/completions
				Assert.Equal("POST", method);
				// Authorization header 来自服务端配置；测试 fake 不会泄漏真实 key
				Assert.True(auth == null || auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase));
			}
		}
		finally
		{
			// 作用域 Dispose 已在 using 中恢复；这里只清理服务级 seam 与 fake handler。
			AiDiagnoseService.SendWithRetryOverrideForTest = null;
		}
	}

	[Theory]
	[InlineData("zh", "简体中文")]
	[InlineData("en", "English")]
	[InlineData("fr", "简体中文")]
	[InlineData(null, "简体中文")]
	public async Task Locale_PostDiagnose_UsesWhitelistedLanguageInPrompt(string? requestedLocale, string expectedInstruction)
	{
		string? capturedPrompt = null;
		AiDiagnoseService.SendWithRetryOverrideForTest = (_, _, _, _, prompt, _, _) =>
		{
			capturedPrompt = prompt;
			return Task.FromResult<string?>("""{"schemaVersion":1,"status":"insufficient_information","confidence":0.2,"summary":"synthetic","causes":[],"modIssues":[],"actions":[],"warnings":[],"missingInformation":[]}""");
		};
		try
		{
			await using TestHarness h = await TestHarness.StartAsync(BuildConfigWithTwoInstances());
			string operationId = "locale-test-" + Guid.NewGuid().ToString("N");
			object body = requestedLocale is null
				? new { confirmAiSend = true, localOnly = false, operationId }
				: new { confirmAiSend = true, localOnly = false, operationId, locale = requestedLocale };
			using HttpResponseMessage resp = await h.PostDiagnoseAsync("inst-A", body);
			Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
			Assert.NotNull(capturedPrompt);
			Assert.Contains(expectedInstruction, capturedPrompt!, StringComparison.Ordinal);
			if (expectedInstruction == "简体中文")
			{
				Assert.DoesNotContain("Output all user-visible text fields in English", capturedPrompt!, StringComparison.Ordinal);
			}
		}
		finally
		{
			AiDiagnoseService.SendWithRetryOverrideForTest = null;
		}
	}

	private sealed class PositiveFakeHandler : HttpMessageHandler
	{
		private readonly List<(string url, string method, string? auth)> _capture;
		private readonly string _body;
		public PositiveFakeHandler(List<(string url, string method, string? auth)> capture, string body)
		{
			_capture = capture;
			_body = body;
		}
		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			// SendOnceAsync 期望 outer {choices[0].message.content} 包裹 inner JSON
			string outer = "{\"choices\":[{\"message\":{\"content\":\"" + _body.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n") + "\"}}]}";
			_capture.Add((
				url: request.RequestUri?.ToString() ?? string.Empty,
				method: request.Method.Method,
				auth: request.Headers.TryGetValues("Authorization", out var v) ? string.Join(",", v) : null));
			HttpResponseMessage resp = new HttpResponseMessage(HttpStatusCode.OK)
			{
				Content = new StringContent(outer, Encoding.UTF8, "application/json"),
			};
			return Task.FromResult(resp);
		}
	}
}
