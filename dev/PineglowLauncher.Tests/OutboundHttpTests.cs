using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Pineglow.Launcher.Services;
using Xunit;

namespace Pineglow.Launcher.Tests;

/// <summary>
/// 候选 ⑤「没人拥有 HTTP 客户端」的结构与行为守卫。
///
/// <para>要守三件事：</para>
/// <list type="number">
/// <item>
/// <b>出站策略有归宿</b>：<c>LocalApiService</c> 不得再持有 <c>HttpClient</c> 成员
/// （代理 / 超时 / 是否跟随重定向只有 <see cref="OutboundHttp"/> 一处决定）。
/// </item>
/// <item>
/// <b>策略真的作用到请求上</b>：让假代理实际收到请求，而不是只断言"字段上挂了 WebProxy"。
/// </item>
/// <item>
/// <b>自建 HttpClient 的名单不能再长</b>：仍允许的几处是安全钉死型（SSRF：无视用户代理、
/// 按已校验 IP 连）或本机自检，理由写在白名单里；其他地方出现 <c>new HttpClient</c> 判红。
/// </item>
/// </list>
///
/// <para>
/// **关于"启动路径修好了"的证据口径**（避免把它说大）：
/// 旧实现在 <c>Instances.Launch.cs</c> 里 <c>new HttpClient()</c> 下载 Authlib Injector 组件，
/// 既不认代理也不释放。修复由两条互补断言覆盖——
/// (a) 行为：<see cref="DownloadClient_SendsThroughConfiguredProxy"/> 证明 Download 具名客户端确实走代理；
/// (b) 接线：<see cref="LaunchDomain_DelegatesToHostedDownloadClient"/> 证明启动域把这个客户端传进去、
/// 且该文件里不再有自建客户端。
/// 故意不做端到端用例：<c>EnsureAgentAsync</c> 在本地已有 jar 时会**短路**（不联网），
/// 而镜像地址是 HTTPS（要么让假代理支持 CONNECT+TLS，要么让用例依赖真外网）——
/// 两条路都会造出"环境一变就假绿"的测试，正是本仓库反复踩过的坑。
/// </para>
/// </summary>
public class OutboundHttpTests
{
	/// <summary>
	/// 允许在生产代码里自建 HttpClient 的豁免名单。每条都要写明**为什么必须绕开宿主客户端**。
	///
	/// 加新条目之前先问：这个客户端发的是"用户看得见的出站请求"（→ 用 OutboundHttp），
	/// 还是安全上**必须**无视用户代理、把连接钉在已校验的 IP 上（→ 才能进这份名单）？
	/// </summary>
	private static readonly (string File, string Reason)[] SelfBuiltClientAllowList =
	[
		("SkinImageProxyService.cs", "SSRF 防护：UseProxy=false，每跳 DNS 校验后按 IP 连接；走用户代理等于放弃钉死"),
		("NewsImageProxyService.cs", "SSRF 防护：同上，且 fake-ip 兼容要求直连已验证地址"),
		("ContentIconService.cs", "SSRF 防护：ConnectCallback 只连已校验公网 IP，代理会让校验形同虚设"),
		("WebShellWindow.axaml.cs", "壳层自检：只打 127.0.0.1:<本机端口>/api/health，必须绕过任何代理"),
		("VerifyHost/BisectSmoke.cs", "离线验证宿主：只打 127.0.0.1，且不属于交付主程序"),
	];

	private static string RepoRoot => Path.GetFullPath(Path.Combine(
		AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

	// ===================== 1. 出站策略有归宿 =====================

	[Fact]
	public void LocalApiService_OwnsNoHttpClientMember()
	{
		// 反射看的是**编译后的类型**：partial 里怎么写都骗不过它，
		// 「字段改属性」「塞进静态构造函数」这类搬家也会被抓到。
		const BindingFlags flags = BindingFlags.Static | BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

		string[] clientFields = Array.FindAll(typeof(LocalApiService).GetFields(flags),
				f => typeof(HttpClient).IsAssignableFrom(f.FieldType))
			.Select(f => f.Name).ToArray();
		Assert.True(clientFields.Length == 0,
			$"LocalApiService 又自己持有了 HttpClient 字段：{string.Join("、", clientFields)}。" +
			"代理/超时/重定向策略只能由 OutboundHttp 决定；新增具名客户端请加在那里。");

		string[] clientProps = Array.FindAll(typeof(LocalApiService).GetProperties(flags),
				p => typeof(HttpClient).IsAssignableFrom(p.PropertyType))
			.Select(p => p.Name).ToArray();
		Assert.True(clientProps.Length == 0,
			$"LocalApiService 有 HttpClient 属性：{string.Join("、", clientProps)}；应改为读 OutboundHttp。");
	}

	[Fact]
	public void NamedClients_HaveTheContract_TheirNameImplies()
	{
		var outbound = new OutboundHttp(OutboundHttp.ProxySettings.None);

		Assert.Equal(OutboundHttp.AiTimeoutSeconds, (int)outbound.Ai.Timeout.TotalSeconds);
		Assert.Equal(OutboundHttp.ApiTimeoutSeconds, (int)outbound.Api.Timeout.TotalSeconds);
		Assert.Equal(OutboundHttp.NewsTimeoutSeconds, (int)outbound.News.Timeout.TotalSeconds);
		Assert.Equal(OutboundHttp.DownloadTimeoutSeconds, (int)outbound.Download.Timeout.TotalSeconds);

		// AI 禁自动重定向是首跳 allowlist 的前提（原来由 AiHttpClientFactory 保证，现在由 OutboundHttp 保证）。
		HttpMessageHandler aiHandler = OutboundHttp.CreateAiHandler(OutboundHttp.ProxySettings.None);
		Assert.False(Assert.IsType<HttpClientHandler>(aiHandler).AllowAutoRedirect, "AI 客户端必须禁自动重定向");

		// 其余三条保留改造前的默认重定向行为。
		Assert.True(HandlerOf(outbound.Api).AllowAutoRedirect, "API/News/Download 必须保留默认重定向（改造前行为）");
		Assert.True(HandlerOf(outbound.News).AllowAutoRedirect, "News 必须保留默认重定向");
		Assert.True(HandlerOf(outbound.Download).AllowAutoRedirect, "Download 必须保留默认重定向");

		// 「文件大」不能被误判成「接口挂了」。
		Assert.True(outbound.Download.Timeout > outbound.Api.Timeout, "Download 超时必须长于 Api 超时");
	}

	[Fact]
	public void ApplyProxy_RebuildsOnlyWhenSettingsChange()
	{
		var outbound = new OutboundHttp(OutboundHttp.ProxySettings.None);
		HttpClient before = outbound.Api;

		// 配置页每改一个字段都会 SaveConfig：同样内容重复应用不得重建。
		outbound.ApplyProxy(OutboundHttp.ProxySettings.None);
		Assert.Same(before, outbound.Api);

		outbound.ApplyProxy(new OutboundHttp.ProxySettings(true, "127.0.0.1", 7890, string.Empty, string.Empty));
		Assert.NotSame(before, outbound.Api);

		// 端口、用户名、密码任一变化都必须重建（否则用户改了代理还在走旧的）。
		HttpClient hostAndPort = outbound.Api;
		outbound.ApplyProxy(new OutboundHttp.ProxySettings(true, "127.0.0.1", 7891, string.Empty, string.Empty));
		Assert.NotSame(hostAndPort, outbound.Api);

		HttpClient noCreds = outbound.Api;
		outbound.ApplyProxy(new OutboundHttp.ProxySettings(true, "127.0.0.1", 7891, "user", "pass"));
		Assert.NotSame(noCreds, outbound.Api);

		// 关掉代理也得重建（否则用户关了代理还在走代理）。
		HttpClient withCreds = outbound.Api;
		outbound.ApplyProxy(OutboundHttp.ProxySettings.None);
		Assert.NotSame(withCreds, outbound.Api);
		Assert.True(HandlerOf(outbound.Api).UseProxy == false, "代理关闭后 handler.UseProxy 必须为 false");
	}

	[Fact]
	public void ProxySettings_ToString_DoesNotLeakPassword()
	{
		var settings = new OutboundHttp.ProxySettings(true, "127.0.0.1", 7890, "user", "s3cr3t-pw");
		string text = settings.ToString();
		Assert.DoesNotContain("s3cr3t-pw", text, StringComparison.Ordinal);
	}

	[Fact]
	public void AiOverride_TouchesOnlyAi_AndIsScoped()
	{
		var outbound = new OutboundHttp(OutboundHttp.ProxySettings.None);
		HttpClient realAi = outbound.Ai;
		HttpClient realApi = outbound.Api;

		using var fake = new HttpClient(new NeverUsedHandler());
		using (outbound.OverrideAiClient(fake))
		{
			Assert.Same(fake, outbound.Ai);
			Assert.Same(realApi, outbound.Api);

			// 替换期间代理变化不重建任何客户端（AI 走覆盖，其余只在签名变化时重建）。
			outbound.ApplyProxy(new OutboundHttp.ProxySettings(true, "127.0.0.1", 7892, string.Empty, string.Empty));
			Assert.Same(fake, outbound.Ai);
		}

		// 作用域结束后回到**当前代理配置**下的真客户端（不是被覆盖前那个旧的）。
		Assert.NotSame(fake, outbound.Ai);
		Assert.NotSame(realAi, outbound.Ai);
	}

	[Fact]
	public void AiOverride_WithoutProxyChange_RestoresTheSameInstance()
	{
		var outbound = new OutboundHttp(OutboundHttp.ProxySettings.None);
		HttpClient realAi = outbound.Ai;

		using (outbound.OverrideAiClient(new HttpClient(new NeverUsedHandler())))
		{
			Assert.NotSame(realAi, outbound.Ai);
		}

		Assert.Same(realAi, outbound.Ai);
	}

	// ===================== 2. 策略真的作用到请求上（行为证据） =====================

	/// <summary>
	/// 代理必须真的收到请求：起一个记录请求行的假代理 + 一个"源站"（不会被访问），
	/// 让 Download 具名客户端请求源站地址，断言假代理收到的是**绝对 URI**（代理请求行的形态）。
	///
	/// 为什么测 Download 而不是 Api：启动路径用的就是 Download，而它是本次唯一一个
	/// 从"自建裸客户端"改成"宿主客户端"的调用点。
	/// </summary>
	[Fact]
	public async Task DownloadClient_SendsThroughConfiguredProxy()
	{
		using var proxy = new RecordingProxyServer();
		int originPort = FreePort(); // 只借端口号，不监听：证明请求没有直连源站
		var outbound = new OutboundHttp(new OutboundHttp.ProxySettings(true, "127.0.0.1", proxy.Port, string.Empty, string.Empty));

		using HttpResponseMessage resp = await outbound.Download.GetAsync($"http://127.0.0.1:{originPort}/probe.bin");

		Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
		Assert.Equal(1, proxy.RequestCount);
		Assert.Contains($":{originPort}/probe.bin", proxy.LastRequestLine ?? string.Empty, StringComparison.Ordinal);
	}

	/// <summary>反向证据：未配置代理时，客户端不得把请求交给那个"恰好开着"的代理端口。</summary>
	[Fact]
	public async Task DownloadClient_DoesNotUseProxy_WhenProxyIsOff()
	{
		using var proxy = new RecordingProxyServer();
		var outbound = new OutboundHttp(OutboundHttp.ProxySettings.None);

		// 目标是这台"代理"自己：直连应当成功，但它收到的请求行是**相对路径**而非绝对 URI。
		using HttpResponseMessage resp = await outbound.Download.GetAsync($"http://127.0.0.1:{proxy.Port}/direct");

		Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
		Assert.Equal(1, proxy.RequestCount);
		Assert.DoesNotContain("http://", proxy.LastRequestLine ?? string.Empty, StringComparison.OrdinalIgnoreCase);
	}

	// ===================== 3. 自建 HttpClient 的名单不能再长 =====================

	[Fact]
	public void SelfBuiltHttpClient_SitesAreExactlyTheDocumentedAllowList()
	{
		var unexpected = new List<string>();
		string[] allowed = SelfBuiltClientAllowList.Select(x => x.File).ToArray();

		foreach (string file in ProductionCsFiles())
		{
			if (!File.ReadAllText(file).Contains("new HttpClient", StringComparison.Ordinal))
			{
				continue;
			}

			// OutboundHttp 自己就是那个"唯一允许的地方"。
			if (Path.GetFileName(file) == "OutboundHttp.cs")
			{
				continue;
			}

			string rel = Relative(file);
			if (!allowed.Any(a => rel.EndsWith(a, StringComparison.OrdinalIgnoreCase)))
			{
				unexpected.Add(rel);
			}
		}

		Assert.True(unexpected.Count == 0,
			"这些生产文件自建了 HttpClient，但没有登记在 SelfBuiltClientAllowList 里：" +
			string.Join("；", unexpected) +
			"。发用户可见的出站请求请用 OutboundHttp；只有安全上必须无视用户代理（SSRF 钉 IP）才允许自建，并写明理由。");
	}

	/// <summary>
	/// 启动链必须用宿主的 <c>Download</c> 具名客户端（候选 ⑤ 修掉的真缺陷：
	/// <c>Instances.Launch.cs</c> 里曾有一个 <c>new HttpClient()</c>——忽略用户代理、不释放、600s 下载超时也没生效）。
	///
	/// **为什么这条仍读源码文本（第 3 步唯一保留下来的两条之一）**：
	/// 它守的是"**某个文件里有没有这句接线**"。<c>LocalApiService</c> 是 <c>static partial class</c>，
	/// 编译产物里没有"成员/语句属于哪个文件"的信息，反射只能看到整个类型 —— 所以"接线在这个文件里"
	/// 这件事除了读文件没有第二种判法。换成行为测试也不划算：跑通 authlib 启动链要真 Java + 真实例目录。
	///
	/// 已经有的等效/更强证据：<see cref="SelfBuiltHttpClient_SitesAreExactlyTheDocumentedAllowList"/>
	/// 扫描**全部**生产文件，谁自建 <c>HttpClient</c> 又不登记就判红（Launch.cs 不在白名单里）；
	/// <see cref="DownloadClient_SendsThroughConfiguredProxy"/> 证明 Download 客户端确实跟随代理。
	/// 这条文本断言只剩下"启动链有没有把它用上"这一句接线事实。
	/// </summary>
	[Fact]
	public void LaunchDomain_DelegatesToHostedDownloadClient()
	{
		string launch = ProductionServicesSource.Read("LocalApiService.Instances.Launch.cs");
		Assert.DoesNotContain("new HttpClient", launch, StringComparison.Ordinal);
		Assert.Contains("Outbound.Download", launch, StringComparison.Ordinal);
	}

	[Fact]
	public void AiHttpClientFactory_IsGone_BecauseStrategyLivesInOutboundHttp()
	{
		// 它曾经只是一个 new：策略本身（禁重定向 + 代理 + 超时）现在由 OutboundHttp 一处决定。
		// 留两份实现等于又回到"没人能回答我们的出站策略是什么"。
		string path = Path.Combine(ProductionServicesSource.Root, "AiHttpClientFactory.cs");
		Assert.False(File.Exists(path), "AiHttpClientFactory 已被 OutboundHttp 取代；不要留下第二份 AI 客户端策略。");
	}

	// ===================== 辅助 =====================

	private static HttpClientHandler HandlerOf(HttpClient client)
	{
		// 字段在基类 HttpMessageInvoker 上（net10）：先找本类型，再沿基类链往上找。
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

	private static IEnumerable<string> ProductionCsFiles()
	{
		string projectDir = Path.Combine(RepoRoot, "dev", "PineglowLauncher");
		Assert.True(Directory.Exists(projectDir), $"找不到生产源码目录：{projectDir}");

		string sep = Path.DirectorySeparatorChar.ToString();
		foreach (string file in Directory.EnumerateFiles(projectDir, "*.cs", SearchOption.AllDirectories))
		{
			if (file.Contains(sep + "bin" + sep, StringComparison.OrdinalIgnoreCase) ||
				file.Contains(sep + "obj" + sep, StringComparison.OrdinalIgnoreCase))
			{
				continue;
			}

			yield return file;
		}
	}

	private static string Relative(string absolute) =>
		Path.GetRelativePath(RepoRoot, absolute).Replace(Path.DirectorySeparatorChar, '/');

	private static int FreePort()
	{
		var probe = new TcpListener(IPAddress.Loopback, 0);
		probe.Start();
		int port = ((IPEndPoint)probe.LocalEndpoint).Port;
		probe.Stop();
		return port;
	}

	/// <summary>
	/// 最小假代理：接受连接、读请求头、回 200 空体，并记录**请求行原文**。
	///
	/// 刻意用裸 TcpListener 而不是 HttpListener：代理形态的请求行是绝对 URI
	/// （<c>GET http://host:port/path HTTP/1.1</c>），http.sys 对绝对形式 URI 的处理
	/// 是"看情况"，用它做断言会把"平台行为"混进"我们的代理配置是否生效"。
	/// </summary>
	private sealed class RecordingProxyServer : IDisposable
	{
		private readonly TcpListener _listener;
		private readonly CancellationTokenSource _stop = new CancellationTokenSource();
		private int _requestCount;

		public RecordingProxyServer()
		{
			_listener = new TcpListener(IPAddress.Loopback, 0);
			_listener.Start();
			Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
			_ = Task.Run(AcceptLoopAsync);
		}

		public int Port { get; }

		public int RequestCount => Volatile.Read(ref _requestCount);

		public string? LastRequestLine { get; private set; }

		private async Task AcceptLoopAsync()
		{
			while (!_stop.IsCancellationRequested)
			{
				TcpClient client;
				try
				{
					client = await _listener.AcceptTcpClientAsync(_stop.Token).ConfigureAwait(false);
				}
				catch
				{
					return;
				}

				_ = Task.Run(() => ServeAsync(client));
			}
		}

		private async Task ServeAsync(TcpClient client)
		{
			using (client)
			{
				try
				{
					NetworkStream stream = client.GetStream();
					string head = await ReadHeadAsync(stream).ConfigureAwait(false);
					if (head.Length == 0)
					{
						return;
					}

					Interlocked.Increment(ref _requestCount);
					LastRequestLine = head.Split("\r\n", StringSplitOptions.None)[0];

					byte[] body = "{}"u8.ToArray();
					byte[] response = Encoding.ASCII.GetBytes(
						"HTTP/1.1 200 OK\r\n" +
						"Content-Type: application/json\r\n" +
						"Content-Length: " + body.Length + "\r\n" +
						"Connection: close\r\n\r\n");
					await stream.WriteAsync(response).ConfigureAwait(false);
					await stream.WriteAsync(body).ConfigureAwait(false);
					await stream.FlushAsync().ConfigureAwait(false);
				}
				catch
				{
					// 假服务器不关心失败细节
				}
			}
		}

		private static async Task<string> ReadHeadAsync(NetworkStream stream)
		{
			var buffer = new byte[4096];
			var text = new StringBuilder();
			while (text.ToString().IndexOf("\r\n\r\n", StringComparison.Ordinal) < 0)
			{
				int read = await stream.ReadAsync(buffer).ConfigureAwait(false);
				if (read <= 0)
				{
					break;
				}

				text.Append(Encoding.ASCII.GetString(buffer, 0, read));
				if (text.Length > 64 * 1024)
				{
					break;
				}
			}

			return text.ToString();
		}

		public void Dispose()
		{
			try { _stop.Cancel(); } catch { }
			try { _listener.Stop(); } catch { }
			_stop.Dispose();
		}
	}

	private sealed class NeverUsedHandler : HttpMessageHandler
	{
		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
			throw new InvalidOperationException("这个 handler 不该被调用");
	}
}
