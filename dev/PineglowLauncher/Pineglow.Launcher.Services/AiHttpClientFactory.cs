using System;
using System.Net;
using System.Net.Http;

namespace Pineglow.Launcher.Services;

/// <summary>
/// Phase 3B 修复批次：AI 专用 HttpClient 工厂。
///
/// 与 News / Content / Update / Download 共享的 client 行为隔离；仅控制 AI 请求
/// 路径，禁止自动跟随 302/307/308，避免固定首跳 allowlist 被绕过。
///
/// 保留现有代理能力、60 秒超时与连接释放行为；proxy 刷新后必须重建并继续保持
/// AllowAutoRedirect=false。
/// </summary>
public static class AiHttpClientFactory
{
	public const int DefaultTimeoutSeconds = 60;

	/// <summary>构造一个 AI 专用 HttpMessageHandler：禁止自动重定向、可选代理。</summary>
	public static HttpMessageHandler CreateAiHandler(
		bool proxyEnabled,
		string proxyHost,
		int proxyPort,
		string proxyUser,
		string proxyPass)
	{
		var handler = new HttpClientHandler
		{
			AllowAutoRedirect = false,
			UseCookies = false,
		};
		if (proxyEnabled && !string.IsNullOrWhiteSpace(proxyHost) && proxyPort > 0)
		{
			try
			{
				var proxy = new WebProxy($"{proxyHost}:{proxyPort}");
				if (!string.IsNullOrWhiteSpace(proxyUser))
				{
					proxy.Credentials = new NetworkCredential(proxyUser, proxyPass);
				}
				handler.Proxy = proxy;
				handler.UseProxy = true;
			}
			catch
			{
				// 代理地址非法时忽略，保持直连
			}
		}
		else
		{
			handler.UseProxy = false;
		}
		return handler;
	}

	/// <summary>构造一个 AI 专用 HttpClient：禁止自动重定向、可选代理、默认 60s 超时。</summary>
	public static HttpClient CreateAiHttp(
		bool proxyEnabled,
		string proxyHost,
		int proxyPort,
		string proxyUser,
		string proxyPass,
		int timeoutSeconds = DefaultTimeoutSeconds)
	{
		var handler = CreateAiHandler(proxyEnabled, proxyHost, proxyPort, proxyUser, proxyPass);
		return new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(timeoutSeconds) };
	}
}