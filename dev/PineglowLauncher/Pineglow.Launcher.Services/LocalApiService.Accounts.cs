using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Pineglow.Launcher.Models;

namespace Pineglow.Launcher.Services;

/// <summary>
/// 账号与登录。
/// <para>
/// 离线账号、Microsoft 设备码登录、authlib 登录、皮肤刷新与选择。
/// </para>
/// </summary>
public static partial class LocalApiService
{
	private static object BuildAccountSummary(LauncherAccount a)
	{
		return new
		{
			a.Id,
			a.Type,
			a.UserName,
			a.Uuid,
			a.Xuid,
			a.AuthServerUrl,
			a.SkinUrl,
			a.SkinModel,
			a.SkinCount,
			a.CapeCount,
			a.Capes,
			a.AccessTokenExpiresAt,
			a.IsMicrosoft,
			a.IsThirdParty
		};
	}

	private static async Task HandleListAccountsAsync(HttpListenerResponse response)
	{
		AppConfig config = LoadConfig();
		await WriteJsonAsync(response, 200, new { accounts = config.Accounts.Select(BuildAccountSummary), selectedAccountId = config.SelectedAccountId });
	}

	private static async Task HandleAddOfflineAccountAsync(HttpListenerRequest request, HttpListenerResponse response)
	{
		JsonDocument body = await ReadBodyAsync(request);
		string userName = JsonUtil.GetString(body.RootElement, "userName")?.Trim();
		if (string.IsNullOrWhiteSpace(userName))
		{
			throw new InvalidOperationException("玩家名不能为空");
		}

		AppConfig config = LoadConfig();
		LauncherAccount account = AccountService.AddOffline(config, userName);
		AccountService.Select(config, account.Id);
		SaveConfig(config);
		await WriteJsonAsync(response, 200, new { ok = true, account = BuildAccountSummary(account) });
	}

	private static async Task HandleDeleteAccountAsync(HttpListenerResponse response, string accountId)
	{
		AppConfig config = LoadConfig();
		if (config.Accounts.Count <= 1)
		{
			throw new InvalidOperationException("至少保留一个账号。");
		}
		LauncherAccount account = config.Accounts.FirstOrDefault(x => x.Id == accountId);
		if (account == null)
		{
			throw new InvalidOperationException("账号不存在：" + accountId);
		}
		config.Accounts.Remove(account);
		if (config.SelectedAccountId == accountId)
		{
			config.SelectedAccountId = config.Accounts[0].Id;
		}
		SecretStore.Save(SecretStore.AccountToken(accountId), null);
		SecretStore.Save(SecretStore.MicrosoftAccountToken(accountId), null);
		SaveConfig(config);
		await WriteJsonAsync(response, 200, new { ok = true, selectedAccountId = config.SelectedAccountId, accounts = config.Accounts.Select(BuildAccountSummary) });
	}

	private static async Task HandleAccountSkinImageAsync(HttpListenerResponse response, string accountId)
	{
		AppConfig config = LoadConfig();
		LauncherAccount account = config.Accounts.FirstOrDefault(x => x.Id == accountId);
		if (account == null)
		{
			await WriteJsonAsync(response, 404, new { error = "账号不存在" });
			return;
		}

		string skinUrl = (account.SkinUrl ?? string.Empty).Trim();
		if (string.IsNullOrWhiteSpace(skinUrl))
		{
			await WriteJsonAsync(response, 404, new { error = "该账号没有皮肤" });
			return;
		}

		using CancellationTokenSource cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
		byte[] bytes = await SkinImageProxyService.FetchAsync(skinUrl, cts.Token);
		response.Headers["X-Content-Type-Options"] = "nosniff";
		response.Headers["Content-Security-Policy"] = "default-src 'none'";
		response.Headers["Cache-Control"] = "private, max-age=300";
		response.Headers["Referrer-Policy"] = "no-referrer";
		response.ContentType = "image/png";
		response.ContentLength64 = bytes.Length;
		await response.OutputStream.WriteAsync(bytes, 0, bytes.Length);
	}

	private static async Task HandleRefreshSkinAsync(HttpListenerRequest request, HttpListenerResponse response)
	{
		JsonDocument body = await ReadBodyAsync(request);
		string accountId = JsonUtil.GetString(body.RootElement, "accountId")?.Trim() ?? string.Empty;
		if (string.IsNullOrWhiteSpace(accountId))
		{
			throw new InvalidOperationException("accountId 不能为空");
		}

		AppConfig config = LoadConfig();
		LauncherAccount account = config.Accounts.FirstOrDefault(x => x.Id == accountId);
		if (account == null)
		{
			throw new InvalidOperationException("账号不存在：" + accountId);
		}
		if (!account.IsThirdParty)
		{
			throw new InvalidOperationException("仅外置登录（皮肤站）账号支持刷新皮肤");
		}
		if (string.IsNullOrWhiteSpace(account.AuthServerUrl) || string.IsNullOrWhiteSpace(account.Uuid))
		{
			throw new InvalidOperationException("该账号缺少认证服务地址或角色 UUID，无法刷新皮肤");
		}

		string accessToken = account.AccessToken;
		if (string.IsNullOrWhiteSpace(accessToken))
		{
			accessToken = SecretStore.Load(SecretStore.AccountToken(account.Id));
		}
		if (string.IsNullOrWhiteSpace(accessToken))
		{
			throw new InvalidOperationException("缺少外置登录令牌，请重新登录该账号");
		}

		// 从认证根地址推导 sessionserver profile 地址
		string baseUrl = account.AuthServerUrl.Trim().TrimEnd('/');
		string profileUrl = baseUrl + "/sessionserver/session/minecraft/profile/" + account.Uuid;

		string skinUrl = string.Empty;
		string capeUrl = string.Empty;
		bool isSlim = false;
		using (CancellationTokenSource cts = new CancellationTokenSource(TimeSpan.FromSeconds(30)))
		using (HttpRequestMessage req = new HttpRequestMessage(HttpMethod.Get, profileUrl))
		{
			req.Headers.TryAddWithoutValidation("Authorization", "Bearer " + accessToken);
			using (HttpResponseMessage resp = await ApiHttp.SendAsync(req, cts.Token))
			{
				string text = await resp.Content.ReadAsStringAsync(cts.Token);
				if (!resp.IsSuccessStatusCode)
				{
					throw new InvalidOperationException("拉取皮肤站 profile 失败：" + resp.StatusCode + " " + text.Substring(0, Math.Min(200, text.Length)));
				}
				using (JsonDocument doc = JsonDocument.Parse(text))
				{
					JsonElement root = doc.RootElement;
					if (root.TryGetProperty("properties", out JsonElement properties) && properties.ValueKind == JsonValueKind.Array)
					{
						foreach (JsonElement p in properties.EnumerateArray())
						{
							if (p.TryGetProperty("name", out JsonElement pName) && pName.GetString() == "textures" &&
								p.TryGetProperty("value", out JsonElement pValue))
							{
								try
								{
									string texturesJson = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(pValue.GetString() ?? string.Empty));
									using (JsonDocument texDoc = JsonDocument.Parse(texturesJson))
									{
										if (texDoc.RootElement.TryGetProperty("textures", out JsonElement texRoot))
										{
											if (texRoot.TryGetProperty("SKIN", out JsonElement skin) && skin.TryGetProperty("url", out JsonElement skinUrlEl))
											{
												skinUrl = skinUrlEl.GetString() ?? string.Empty;
											isSlim = skin.TryGetProperty("metadata", out JsonElement meta) && meta.TryGetProperty("model", out JsonElement modelEl) && modelEl.GetString() == "slim";
											}
											if (texRoot.TryGetProperty("CAPE", out JsonElement cape) && cape.TryGetProperty("url", out JsonElement capeUrlEl))
											{
												capeUrl = capeUrlEl.GetString() ?? string.Empty;
											}
										}
									}
								}
								catch
								{
									// textures 解析失败不影响整体刷新
								}
							}
						}
					}
				}
			}
		}

		account.SkinUrl = skinUrl;
		account.SkinModel = isSlim ? "slim" : "classic";
		account.SkinCount = string.IsNullOrWhiteSpace(skinUrl) ? 0 : 1;
		account.CapeCount = string.IsNullOrWhiteSpace(capeUrl) ? 0 : 1;
		account.Capes = new List<MinecraftCapeInfo>();
		if (!string.IsNullOrWhiteSpace(capeUrl))
		{
			account.Capes.Add(new MinecraftCapeInfo { Url = capeUrl, IsActive = false });
		}
		SaveConfig(config);
		// 回写令牌，避免下次启动丢失
		if (!string.IsNullOrWhiteSpace(accessToken))
		{
			SecretStore.Save(SecretStore.AccountToken(account.Id), accessToken);
		}
		await WriteJsonAsync(response, 200, new { ok = true, account = BuildAccountSummary(account) });
	}

	private static async Task HandleSelectAccountAsync(HttpListenerRequest request, HttpListenerResponse response)
	{
		JsonDocument body = await ReadBodyAsync(request);
		string accountId = JsonUtil.GetString(body.RootElement, "accountId");
		AppConfig config = LoadConfig();
		if (config.Accounts.All(x => x.Id != accountId))
		{
			throw new InvalidOperationException("账号不存在：" + accountId);
		}

		AccountService.Select(config, accountId);
		SaveConfig(config);
		await WriteJsonAsync(response, 200, new { ok = true, selectedAccountId = accountId });
	}

	private sealed class MicrosoftDeviceSession
	{
		public string ClientId = string.Empty;
		public DeviceCodeInfo DeviceCode = new DeviceCodeInfo();
		public DateTimeOffset CreatedAt = DateTimeOffset.Now;
		public bool Completed;
	}

	private static readonly object MicrosoftDeviceGate = new object();

	private static readonly Dictionary<string, MicrosoftDeviceSession> MicrosoftDeviceSessions = new Dictionary<string, MicrosoftDeviceSession>();

	private static async Task HandleMicrosoftLoginAsync(HttpListenerResponse response)
	{
		string clientId = AuthClientOptions.ResolveMicrosoftClientId();
		if (!AuthClientOptions.HasUsableMicrosoftClientId(clientId))
		{
			await WriteJsonAsync(response, 400, new { ok = false, message = "未配置 Microsoft Client ID，请设置环境变量 " + AuthClientOptions.ClientIdEnvironmentVariable + " 或配置文件 MicrosoftClientId。" });
			return;
		}

		MicrosoftAuthService authService = new MicrosoftAuthService(ApiHttp);
		try
		{
			// 兼容旧调用：直接创建设备码会话，返回用户代码
			DeviceCodeInfo deviceCode = await authService.CreateDeviceCodeAsync(clientId, CancellationToken.None);
			MicrosoftDeviceSession session = new MicrosoftDeviceSession
			{
				ClientId = clientId,
				DeviceCode = deviceCode,
				CreatedAt = DateTimeOffset.Now
			};
			lock (MicrosoftDeviceGate)
			{
				MicrosoftDeviceSessions[clientId] = session;
			}
			await WriteJsonAsync(response, 200, new
			{
				ok = true,
				userCode = deviceCode.UserCode,
				verificationUri = deviceCode.VerificationUri,
				verificationUriComplete = deviceCode.VerificationUriComplete,
				expiresIn = deviceCode.ExpiresIn,
				interval = deviceCode.Interval
			});
		}
		catch (MinecraftAppRegistrationRequiredException ex)
		{
			await WriteJsonAsync(response, 400, new { ok = false, message = ex.Message });
		}
		catch (OperationCanceledException)
		{
			await WriteJsonAsync(response, 400, new { ok = false, message = "登录已取消或超时，请重试。" });
		}
		catch (Exception ex)
		{
			await WriteJsonAsync(response, 500, new { ok = false, message = "微软登录失败：" + ex.Message });
		}
	}

	/// <summary>创建设备码登录会话：返回 user_code 供用户到 microsoft.com/link 输入。</summary>
	private static async Task HandleMicrosoftDeviceStartAsync(HttpListenerResponse response)
	{
		string clientId = AuthClientOptions.ResolveMicrosoftClientId();
		if (!AuthClientOptions.HasUsableMicrosoftClientId(clientId))
		{
			await WriteJsonAsync(response, 400, new { ok = false, message = "未配置 Microsoft Client ID，请设置环境变量 " + AuthClientOptions.ClientIdEnvironmentVariable + " 或配置文件 MicrosoftClientId。" });
			return;
		}

		try
		{
			MicrosoftAuthService authService = new MicrosoftAuthService(ApiHttp);
			DeviceCodeInfo deviceCode = await authService.CreateDeviceCodeAsync(clientId, CancellationToken.None);
			MicrosoftDeviceSession session = new MicrosoftDeviceSession
			{
				ClientId = clientId,
				DeviceCode = deviceCode,
				CreatedAt = DateTimeOffset.Now
			};
			lock (MicrosoftDeviceGate)
			{
				MicrosoftDeviceSessions[clientId] = session;
			}
			await WriteJsonAsync(response, 200, new
			{
				ok = true,
				userCode = deviceCode.UserCode,
				verificationUri = deviceCode.VerificationUri,
				verificationUriComplete = deviceCode.VerificationUriComplete,
				expiresIn = deviceCode.ExpiresIn,
				interval = deviceCode.Interval
			});
		}
		catch (Exception ex)
		{
			await WriteJsonAsync(response, 500, new { ok = false, message = "无法创建微软登录码：" + ex.Message });
		}
	}

	/// <summary>轮询设备码登录结果：pending 表示继续等待，成功返回账号信息。</summary>
	private static async Task HandleMicrosoftDevicePollAsync(HttpListenerResponse response)
	{
		string clientId = AuthClientOptions.ResolveMicrosoftClientId();
		if (!AuthClientOptions.HasUsableMicrosoftClientId(clientId))
		{
			await WriteJsonAsync(response, 400, new { ok = false, message = "未配置 Microsoft Client ID。" });
			return;
		}

		MicrosoftDeviceSession session;
		lock (MicrosoftDeviceGate)
		{
			MicrosoftDeviceSessions.TryGetValue(clientId, out session);
		}
		if (session == null)
		{
			await WriteJsonAsync(response, 400, new { ok = false, error = "no_session", message = "登录会话不存在或已过期，请重新开始登录。" });
			return;
		}
		if (session.Completed)
		{
			await WriteJsonAsync(response, 200, new { ok = true, status = "completed", account = (object?)null });
			return;
		}

		try
		{
			MicrosoftAuthService authService = new MicrosoftAuthService(ApiHttp);
			MicrosoftOAuthToken oauth = await authService.PollDeviceCodeOnceAsync(clientId, session.DeviceCode, CancellationToken.None);
			session.Completed = true;
			MinecraftAccount account = await authService.LoginMinecraftAsync(oauth, CancellationToken.None);

			AppConfig config = LoadConfig();
			LauncherAccount launcherAccount = AccountService.UpsertMicrosoft(config, account, clientId);
			AccountService.Select(config, launcherAccount.Id);
			SaveConfig(config);
			await WriteJsonAsync(response, 200, new { ok = true, status = "completed", account = BuildAccountSummary(launcherAccount) });
		}
		catch (OperationCanceledException)
		{
			await WriteJsonAsync(response, 200, new { ok = false, status = "pending" });
		}
		catch (Exception ex)
		{
			await WriteJsonAsync(response, 200, new { ok = false, status = "failed", message = ex.Message });
		}
	}

	private static async Task HandleAuthlibLoginAsync(HttpListenerRequest request, HttpListenerResponse response)
	{
		JsonDocument body = await ReadBodyAsync(request);
		string serverUrl = JsonUtil.GetString(body.RootElement, "serverUrl")?.Trim() ?? string.Empty;
		string userName = JsonUtil.GetString(body.RootElement, "userName")?.Trim() ?? string.Empty;
		string password = JsonUtil.GetString(body.RootElement, "password") ?? string.Empty;
		if (string.IsNullOrWhiteSpace(serverUrl) || string.IsNullOrWhiteSpace(userName) || string.IsNullOrWhiteSpace(password))
		{
			throw new InvalidOperationException("请填写认证服务地址、账号和密码。");
		}

		using CancellationTokenSource cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
		AuthlibInjectorService authService = new AuthlibInjectorService(ApiHttp);
		AuthlibAccount authAccount = await authService.LoginAsync(serverUrl, userName, password, cts.Token);

		AppConfig config = LoadConfig();
		LauncherAccount account = AccountService.UpsertThirdParty(config, authAccount);
		AccountService.Select(config, account.Id);
		SaveConfig(config);
		await WriteJsonAsync(response, 200, new { ok = true, account = BuildAccountSummary(account) });
	}
}
