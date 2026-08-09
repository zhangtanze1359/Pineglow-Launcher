using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Pineglow.Launcher.Services;

public sealed class MicrosoftBrowserAuthorizationService
{
	public sealed class MicrosoftBrowserAuthorizationSession : IAsyncDisposable
	{
		private readonly HttpClient _http;

		private readonly HttpListener _listener;

		private readonly string _clientId;

		private readonly string _verifier;

		private bool _completed;

		public string AuthorizationUri { get; }

		public string RedirectUri { get; }

		public string State { get; }

		internal MicrosoftBrowserAuthorizationSession(HttpClient http, HttpListener listener, string clientId, string redirectUri, string authorizationUri, string state, string verifier)
		{
			_http = http;
			_listener = listener;
			_clientId = clientId;
			RedirectUri = redirectUri;
			AuthorizationUri = authorizationUri;
			State = state;
			_verifier = verifier;
		}

		public async Task<MicrosoftOAuthToken> WaitForTokenAsync(CancellationToken ct)
		{
			if (_completed)
			{
				throw new InvalidOperationException("该网页登录会话已经结束。");
			}
			_completed = true;
			using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
			timeout.CancelAfter(TimeSpan.FromMinutes(5L));
			try
			{
				HttpListenerContext context = await _listener.GetContextAsync().WaitAsync(timeout.Token);
				string b = context.Request.QueryString["state"];
				string value = context.Request.QueryString["code"];
				string value2 = context.Request.QueryString["error"];
				if (!string.Equals(State, b, StringComparison.Ordinal))
				{
					await WritePageAsync(context.Response, HttpStatusCode.BadRequest, "登录结果验证失败，请返回启动器重新登录。");
					throw new MicrosoftBrowserAuthorizationException("登录结果验证失败，请重新点击正版登录。", "callback-state");
				}
				if (!string.IsNullOrWhiteSpace(value2))
				{
					await WritePageAsync(context.Response, HttpStatusCode.BadRequest, "Microsoft 登录未完成，可以返回启动器重试。");
					throw new MicrosoftBrowserAuthorizationException("Microsoft 登录未完成，请重新登录或改用登录码。", "provider-response");
				}
				if (string.IsNullOrWhiteSpace(value))
				{
					await WritePageAsync(context.Response, HttpStatusCode.BadRequest, "未收到登录授权，请返回启动器重试。");
					throw new MicrosoftBrowserAuthorizationException("未收到 Microsoft 授权结果，请重新登录。", "callback-code");
				}
				using HttpResponseMessage tokenResponse = await _http.PostAsync("https://login.microsoftonline.com/consumers/oauth2/v2.0/token", new FormUrlEncodedContent(new Dictionary<string, string>
				{
					["client_id"] = _clientId,
					["grant_type"] = "authorization_code",
					["code"] = value,
					["redirect_uri"] = RedirectUri,
					["code_verifier"] = _verifier,
					["scope"] = "XboxLive.signin offline_access"
				}), timeout.Token);
				string json = await tokenResponse.Content.ReadAsStringAsync(timeout.Token);
				if (!tokenResponse.IsSuccessStatusCode)
				{
					await WritePageAsync(context.Response, HttpStatusCode.BadGateway, "登录凭据获取失败，请返回启动器重试。");
					throw new MicrosoftBrowserAuthorizationException("Microsoft 登录凭据获取失败，请重新登录或改用登录码。", "token-exchange", (int)tokenResponse.StatusCode);
				}
				MicrosoftOAuthToken token = JsonSerializer.Deserialize<MicrosoftOAuthToken>(json);
				if (token == null)
				{
					await WritePageAsync(context.Response, HttpStatusCode.BadGateway, "登录响应无效，请返回启动器重试。");
					throw new MicrosoftBrowserAuthorizationException("Microsoft 登录响应为空，请重新登录。", "token-response");
				}
				await WritePageAsync(context.Response, HttpStatusCode.OK, "登录已完成，可以返回 Pineglow Launcher。");
				return token;
			}
			catch (OperationCanceledException) when (ct.IsCancellationRequested)
			{
				throw;
			}
			catch (OperationCanceledException)
			{
				throw new MicrosoftBrowserAuthorizationException("网页登录已超时，可以重新登录或改用登录码。", "callback-timeout");
			}
			finally
			{
				_listener.Close();
			}
		}

		public ValueTask DisposeAsync()
		{
			_listener.Close();
			return ValueTask.CompletedTask;
		}

		private static async Task WritePageAsync(HttpListenerResponse response, HttpStatusCode status, string message)
		{
			string s = "<!doctype html><html><head><meta charset=\"utf-8\"><title>Pineglow Launcher</title></head><body><p>" + WebUtility.HtmlEncode(message) + "</p></body></html>";
			byte[] bytes = Encoding.UTF8.GetBytes(s);
			response.StatusCode = (int)status;
			response.ContentType = "text/html; charset=utf-8";
			response.ContentLength64 = bytes.Length;
			await response.OutputStream.WriteAsync(bytes);
			response.Close();
		}
	}

	private const string Scope = "XboxLive.signin offline_access";

	private const string AuthorizeEndpoint = "https://login.microsoftonline.com/consumers/oauth2/v2.0/authorize";

	private const string TokenEndpoint = "https://login.microsoftonline.com/consumers/oauth2/v2.0/token";

	private readonly HttpClient _http;

	public MicrosoftBrowserAuthorizationService(HttpClient http)
	{
		_http = http;
	}

	public Task<MicrosoftBrowserAuthorizationSession> StartAsync(string clientId, CancellationToken ct)
	{
		ct.ThrowIfCancellationRequested();
		if (!Guid.TryParse(clientId?.Trim(), out var _))
		{
			throw new MicrosoftBrowserAuthorizationException("Microsoft Client ID 无效，无法开始正版登录。", "client-id");
		}
		HttpListener httpListener = new HttpListener();
		try
		{
			string redirectUri = StartLoopbackListener(httpListener);
			string text = RandomUrlSafeValue(32);
			string state = RandomUrlSafeValue(32);
			string challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(text)));
			string authorizationUri = BuildAuthorizationUri(clientId.Trim(), redirectUri, state, challenge);
			return Task.FromResult(new MicrosoftBrowserAuthorizationSession(_http, httpListener, clientId.Trim(), redirectUri, authorizationUri, state, text));
		}
		catch
		{
			httpListener.Close();
			throw;
		}
	}

	private static string StartLoopbackListener(HttpListener listener)
	{
		for (int i = 0; i < 5; i++)
		{
			int availableLoopbackPort = GetAvailableLoopbackPort();
			string text = $"http://localhost:{availableLoopbackPort}/";
			listener.Prefixes.Clear();
			listener.Prefixes.Add(text);
			try
			{
				listener.Start();
				return text;
			}
			catch (HttpListenerException) when (i < 4)
			{
			}
			catch (HttpListenerException)
			{
				throw new MicrosoftBrowserAuthorizationException("无法启动本机登录回调，请改用登录码。", "loopback-listener");
			}
		}
		throw new MicrosoftBrowserAuthorizationException("无法启动本机登录回调，请改用登录码。", "loopback-listener");
	}

	private static int GetAvailableLoopbackPort()
	{
		TcpListener tcpListener = new TcpListener(IPAddress.Loopback, 0);
		try
		{
			tcpListener.Start();
			return ((IPEndPoint)tcpListener.LocalEndpoint).Port;
		}
		finally
		{
			tcpListener.Stop();
		}
	}

	private static string BuildAuthorizationUri(string clientId, string redirectUri, string state, string challenge)
	{
		Dictionary<string, string> source = new Dictionary<string, string>
		{
			["client_id"] = clientId,
			["response_type"] = "code",
			["redirect_uri"] = redirectUri,
			["response_mode"] = "query",
			["scope"] = "XboxLive.signin offline_access",
			["state"] = state,
			["code_challenge"] = challenge,
			["code_challenge_method"] = "S256"
		};
		string text = string.Join("&", source.Select((KeyValuePair<string, string> pair) => Uri.EscapeDataString(pair.Key) + "=" + Uri.EscapeDataString(pair.Value)));
		return "https://login.microsoftonline.com/consumers/oauth2/v2.0/authorize?" + text;
	}

	private static string RandomUrlSafeValue(int length)
	{
		return Base64Url(RandomNumberGenerator.GetBytes(length));
	}

	internal static string Base64Url(ReadOnlySpan<byte> data)
	{
		return Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-')
			.Replace('/', '_');
	}
}
