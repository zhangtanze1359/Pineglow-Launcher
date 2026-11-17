using System;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using Pineglow.Launcher.Models;

namespace Pineglow.Launcher.Services;

/// <summary>
/// 出站 HTTP 客户端的**唯一所有者**（架构评审候选 ⑤）。
///
/// <para>
/// 改造前，<c>LocalApiService</c> 有 3 个私有静态客户端（ApiHttp / AiHttp / NewsHttp），
/// 26 处生产代码围着它们造服务，代理切换靠"换字段 + 延迟 2 分钟销毁旧的"，正确性依赖
/// "没有别人还握着引用"；同时启动路径（Authlib Injector 组件下载）在 handler 里
/// <c>new HttpClient()</c>——既不跟随用户代理，也不释放。整个进程没有一个地方能回答
/// "我们的出站策略是什么"。
/// </para>
/// <para>
/// 现在策略集中在这里：代理、超时、是否跟随重定向只在这一个类型里决定，调用方只接收客户端。
/// </para>
///
/// <para>
/// **为什么旧客户端不 Dispose**（2026-11-17 决策）：<c>HttpClient</c> 没有终结器，句柄在
/// 底层 handler 上；handler 回收时连接池随之释放，空闲连接另有空闲超时兜底。
/// 代理配置是用户手动改的频次，泄漏面是有界的一小组连接池，换来的是"换配置时绝不会打断
/// 正在进行的请求"——这正是原先"延迟 2 分钟再销毁"想达到却没能证明的效果。真要精确到
/// "引用归零才销毁"，需要给调用方引入借用语义，那是单独一步（见架构文档的遗留项）。
/// </para>
///
/// <para>
/// **不是全局唯一的 new HttpClient 地点**：三处安全钉死型客户端（<c>SkinImageProxyService</c>、
/// <c>NewsImageProxyService</c>、<c>ContentIconService</c>）刻意 <c>UseProxy = false</c> 并用
/// <c>ConnectCallback</c> 把连接钉在已校验的 IP 上——它们必须绕开用户代理，否则 SSRF 防护
/// 失效。壳层健康检查（<c>WebShellWindow</c>）打的是 127.0.0.1。两者的豁免理由写在
/// <c>OutboundHttpTests</c> 的白名单里，加新条目必须先写理由。
/// </para>
/// </summary>
internal sealed class OutboundHttp
{
	/// <summary>API 调用超时：内容站、账号、更新清单、游戏版本元数据。</summary>
	public const int ApiTimeoutSeconds = 45;

	/// <summary>AI 诊断请求超时。前端等待上界与之一致（由 DiagnoseProgressGateTests 断言）。</summary>
	public const int AiTimeoutSeconds = 60;

	/// <summary>新闻与图片代理超时。</summary>
	public const int NewsTimeoutSeconds = 45;

	/// <summary>
	/// 大文件下载超时（Authlib Injector 组件等）。刻意远大于 API 超时：
	/// 45 秒是"接口多久不回就算挂"，不是"文件多大就算失败"。
	/// </summary>
	public const int DownloadTimeoutSeconds = 600;

	private readonly object _gate = new object();

	/// <summary>当前这一组客户端。整组替换（不是逐个字段改），读侧永远看不到半配置状态。</summary>
	private volatile NamedClients _clients;

	private ProxySettings _proxy;

	private string _signature;

	/// <summary>测试缝：非 null 时压过按代理配置构造出来的 AI 客户端。</summary>
	private HttpClient? _aiOverride;

	public OutboundHttp() : this(ProxySettings.None)
	{
	}

	public OutboundHttp(ProxySettings proxy)
	{
		_proxy = proxy;
		_signature = proxy.Signature;
		_clients = Build();
	}

	public HttpClient Api => _clients.Api;

	public HttpClient Ai => _aiOverride ?? _clients.Ai;

	public HttpClient News => _clients.News;

	public HttpClient Download => _clients.Download;

	/// <summary>
	/// 按配置换一组客户端。签名未变时**不重建**（保存配置是很频繁的操作）。
	/// 旧的这一组交给 GC，不 Dispose——理由见类型注释。
	/// </summary>
	public void ApplyProxy(ProxySettings proxy)
	{
		lock (_gate)
		{
			if (SameSignature(proxy.Signature, _signature))
			{
				return;
			}

			_proxy = proxy;
			_signature = proxy.Signature;
			_clients = Build();
		}
	}

	/// <summary>
	/// 测试缝：替换 AI 客户端，让诊断请求真实走 <c>SendAsync</c> 但不触碰外网。
	/// 只影响 AI 这一条——不重建其余客户端，也不必担心代理切换把假客户端换掉。Dispose 即恢复。
	/// </summary>
	internal IDisposable OverrideAiClient(HttpClient client)
	{
		if (client == null) throw new ArgumentNullException(nameof(client));
		lock (_gate)
		{
			_aiOverride = client;
		}

		return new AiOverrideScope(this);
	}

	private void ReleaseAiOverride()
	{
		lock (_gate)
		{
			_aiOverride = null;
		}
	}

	private sealed class AiOverrideScope(OutboundHttp owner) : IDisposable
	{
		private bool _disposed;

		public void Dispose()
		{
			if (_disposed) return;
			_disposed = true;
			owner.ReleaseAiOverride();
		}
	}

	private NamedClients Build() => new(
		CreateClient(CreateHandler(_proxy, allowAutoRedirect: true, useCookies: true), ApiTimeoutSeconds),
		CreateAi(_proxy),
		CreateClient(CreateHandler(_proxy, allowAutoRedirect: true, useCookies: true), NewsTimeoutSeconds),
		CreateClient(CreateHandler(_proxy, allowAutoRedirect: true, useCookies: true), DownloadTimeoutSeconds));

	/// <summary>AI 专用客户端：禁止自动跟随 302/307/308，避免固定首跳 allowlist 被绕过。</summary>
	internal static HttpClient CreateAi(ProxySettings proxy) =>
		CreateClient(CreateAiHandler(proxy), AiTimeoutSeconds);

	/// <summary>AI 专用 handler：禁重定向、不带 Cookie、可选代理。</summary>
	internal static HttpMessageHandler CreateAiHandler(ProxySettings proxy) =>
		CreateHandler(proxy, allowAutoRedirect: false, useCookies: false);

	private static HttpClient CreateClient(HttpMessageHandler handler, int timeoutSeconds) =>
		new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(timeoutSeconds) };

	private static HttpClientHandler CreateHandler(ProxySettings proxy, bool allowAutoRedirect, bool useCookies)
	{
		var handler = new HttpClientHandler
		{
			AllowAutoRedirect = allowAutoRedirect,
			UseCookies = useCookies,
		};

		if (proxy.Enabled && !string.IsNullOrWhiteSpace(proxy.Host) && proxy.Port > 0)
		{
			try
			{
				var web = new WebProxy($"{proxy.Host}:{proxy.Port}");
				if (!string.IsNullOrWhiteSpace(proxy.User))
				{
					web.Credentials = new NetworkCredential(proxy.User, proxy.Password);
				}

				handler.Proxy = web;
				handler.UseProxy = true;
			}
			catch
			{
				// 代理地址非法时忽略，保持直连（与改造前一致）
			}
		}
		else
		{
			handler.UseProxy = false;
		}

		return handler;
	}

	/// <summary>
	/// 签名含代理密码，沿用改造前的定长比较。对"进程内两个字符串自己比"没有实际防护意义，
	/// 但它是既有行为——迁移时不顺手改安全相关的比较方式。
	/// </summary>
	private static bool SameSignature(string a, string b) =>
		CryptographicOperations.FixedTimeEquals(
			SHA256.HashData(Encoding.UTF8.GetBytes(a)),
			SHA256.HashData(Encoding.UTF8.GetBytes(b)));

	private sealed class NamedClients(HttpClient api, HttpClient ai, HttpClient news, HttpClient download)
	{
		public HttpClient Api { get; } = api;

		public HttpClient Ai { get; } = ai;

		public HttpClient News { get; } = news;

		public HttpClient Download { get; } = download;
	}

	/// <summary>
	/// 一次代理配置的不可变快照。与 <c>AppConfig</c> 解耦，测试不必造完整配置对象。
	/// 刻意**不是 record**：合成 <c>ToString</c> 会把代理密码打进任何日志/断言输出。
	/// </summary>
	public readonly struct ProxySettings : IEquatable<ProxySettings>
	{
		public ProxySettings(bool enabled, string host, int port, string user, string password)
		{
			Enabled = enabled;
			Host = host ?? string.Empty;
			Port = port;
			User = user ?? string.Empty;
			Password = password ?? string.Empty;
		}

		public static ProxySettings None => new(false, string.Empty, 0, string.Empty, string.Empty);

		public static ProxySettings From(AppConfig config) => new(
			config.ProxyEnabled,
			config.ProxyHost,
			config.ProxyPort,
			config.ProxyUsername,
			config.ProxyPassword);

		public bool Enabled { get; }

		public string Host { get; }

		public int Port { get; }

		public string User { get; }

		public string Password { get; }

		/// <summary>用于判断"配置有没有变"的签名。**含密码**，因此它只能留在内存里。</summary>
		public string Signature => $"{Enabled}|{Host}|{Port}|{User}|{Password}";

		public bool Equals(ProxySettings other) =>
			Enabled == other.Enabled && Host == other.Host && Port == other.Port
			&& string.Equals(User, other.User, StringComparison.Ordinal)
			&& string.Equals(Password, other.Password, StringComparison.Ordinal);

		public override bool Equals(object? obj) => obj is ProxySettings other && Equals(other);

		public override int GetHashCode() => HashCode.Combine(Enabled, Host, Port, User);

		public override string ToString() =>
			$"enabled={Enabled}, host={(string.IsNullOrEmpty(Host) ? "(none)" : Host)}, port={Port}, user={(string.IsNullOrEmpty(User) ? "(none)" : "(set)")}, password={(string.IsNullOrEmpty(Password) ? "(none)" : "(set)")}";

		public static bool operator ==(ProxySettings left, ProxySettings right) => left.Equals(right);

		public static bool operator !=(ProxySettings left, ProxySettings right) => !left.Equals(right);
	}
}
