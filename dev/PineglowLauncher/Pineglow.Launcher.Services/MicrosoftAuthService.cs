using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Pineglow.Launcher.Models;

namespace Pineglow.Launcher.Services;

public sealed class MicrosoftAuthService
{
	private sealed class XboxTokenResponse
	{
		public string Token { get; set; } = string.Empty;

		public XboxDisplayClaims DisplayClaims { get; set; } = new XboxDisplayClaims();
	}

	private sealed class XboxDisplayClaims
	{
		public List<XboxXui> Xui { get; set; } = new List<XboxXui>();
	}

	private sealed class XboxXui
	{
		public string Uhs { get; set; } = string.Empty;

		public string Xid { get; set; } = string.Empty;
	}

	private sealed class MinecraftTokenResponse
	{
		[JsonPropertyName("access_token")]
		public string AccessToken { get; set; } = string.Empty;

		[JsonPropertyName("expires_in")]
		public int ExpiresIn { get; set; }
	}

	private sealed class MinecraftEntitlementsResponse
	{
		public List<JsonElement> Items { get; set; } = new List<JsonElement>();
	}

	private sealed class MinecraftProfileResponse
	{
		public string Id { get; set; } = string.Empty;

		public string Name { get; set; } = string.Empty;

		public List<MinecraftProfileTexture> Skins { get; set; } = new List<MinecraftProfileTexture>();

		public List<MinecraftProfileTexture> Capes { get; set; } = new List<MinecraftProfileTexture>();
	}

	private sealed class MinecraftProfileTexture
	{
		public string Id { get; set; } = string.Empty;

		public string Url { get; set; } = string.Empty;

		public string State { get; set; } = string.Empty;

		public string Alias { get; set; } = string.Empty;
	}

	private const string Scope = "XboxLive.signin offline_access";

	private readonly HttpClient _http;

	private readonly JsonSerializerOptions _json = new JsonSerializerOptions(JsonSerializerDefaults.Web)
	{
		PropertyNameCaseInsensitive = true
	};

	public MicrosoftAuthService(HttpClient http)
	{
		_http = http;
	}

	public async Task<DeviceCodeInfo> CreateDeviceCodeAsync(string clientId, CancellationToken ct)
	{
		EnsureClientId(clientId);
		using HttpResponseMessage response = await PostFormAsync("https://login.microsoftonline.com/consumers/oauth2/v2.0/devicecode", new Dictionary<string, string>
		{
			["client_id"] = clientId.Trim(),
			["scope"] = "XboxLive.signin offline_access"
		}, ct);
		string text = await response.Content.ReadAsStringAsync(ct);
		if (!response.IsSuccessStatusCode)
		{
			if (TryReadOAuthError(text).Equals("unauthorized_client", StringComparison.OrdinalIgnoreCase) || text.Contains("AADSTS700016", StringComparison.OrdinalIgnoreCase))
			{
				throw new InvalidOperationException("Microsoft Client ID 无法用于个人 Microsoft 账号。请在 Entra 应用注册中，将“支持的帐户类型”改为包含个人 Microsoft 帐户，并启用公共客户端流。");
			}
			throw new InvalidOperationException($"无法创建 Microsoft 设备登录码（HTTP {(int)response.StatusCode}）：{CompactError(text)}");
		}
		return JsonSerializer.Deserialize<DeviceCodeInfo>(text, _json) ?? throw new InvalidOperationException("Microsoft 设备登录码响应为空。");
	}

	public async Task<MicrosoftOAuthToken> PollForMicrosoftTokenAsync(string clientId, DeviceCodeInfo deviceCode, Action<string> log, CancellationToken ct)
	{
		EnsureClientId(clientId);
		int interval = Math.Max(3, deviceCode.Interval);
		DateTimeOffset expiresAt = DateTimeOffset.UtcNow.AddSeconds(Math.Max(60, deviceCode.ExpiresIn));
		while (DateTimeOffset.UtcNow < expiresAt)
		{
			ct.ThrowIfCancellationRequested();
			await Task.Delay(TimeSpan.FromSeconds(interval), ct);
			using HttpResponseMessage response = await PostFormAsync("https://login.microsoftonline.com/consumers/oauth2/v2.0/token", new Dictionary<string, string>
			{
				["grant_type"] = "urn:ietf:params:oauth:grant-type:device_code",
				["client_id"] = clientId.Trim(),
				["device_code"] = deviceCode.DeviceCode
			}, ct);
			string text = await response.Content.ReadAsStringAsync(ct);
			if (response.IsSuccessStatusCode)
			{
				return JsonSerializer.Deserialize<MicrosoftOAuthToken>(text, _json) ?? throw new InvalidOperationException("Microsoft Token 响应为空。");
			}
			switch (TryReadOAuthError(text))
			{
			case "authorization_pending":
				log("等待你在浏览器中完成 Microsoft 登录...");
				break;
			case "slow_down":
				interval += 5;
				log("Microsoft 要求降低轮询频率，继续等待...");
				break;
			case "authorization_declined":
				throw new InvalidOperationException("你取消了 Microsoft 登录授权。");
			case "expired_token":
				throw new InvalidOperationException("设备登录码已过期，请重新点击正版登录。");
			default:
				throw new InvalidOperationException("Microsoft 登录失败：" + CompactError(text));
			}
		}
		throw new InvalidOperationException("设备登录码已过期，请重新点击正版登录。");
	}

	public async Task<MicrosoftOAuthToken> RefreshMicrosoftTokenAsync(string clientId, string refreshToken, CancellationToken ct)
	{
		EnsureClientId(clientId);
		if (string.IsNullOrWhiteSpace(refreshToken))
		{
			throw new InvalidOperationException("没有可用的 Microsoft refresh_token，请重新正版登录。");
		}
		using HttpResponseMessage response = await PostFormAsync("https://login.microsoftonline.com/consumers/oauth2/v2.0/token", new Dictionary<string, string>
		{
			["grant_type"] = "refresh_token",
			["client_id"] = clientId.Trim(),
			["refresh_token"] = refreshToken.Trim(),
			["scope"] = "XboxLive.signin offline_access"
		}, ct);
		string text = await response.Content.ReadAsStringAsync(ct);
		if (!response.IsSuccessStatusCode)
		{
			if (RequiresInteractiveLogin(TryReadOAuthError(text), text))
			{
				throw new MicrosoftReauthenticationRequiredException("Microsoft 登录凭据已失效，需要重新完成网页授权。");
			}
			throw new InvalidOperationException($"Microsoft 令牌刷新失败（HTTP {(int)response.StatusCode}）：{CompactError(text)}");
		}
		return JsonSerializer.Deserialize<MicrosoftOAuthToken>(text, _json) ?? throw new InvalidOperationException("Microsoft Token 响应为空。");
	}

	public async Task<MinecraftAccount> LoginMinecraftAsync(MicrosoftOAuthToken oauth, CancellationToken ct)
	{
		if (string.IsNullOrWhiteSpace(oauth.AccessToken))
		{
			throw new InvalidOperationException("Microsoft access_token 为空。");
		}
		XboxTokenResponse xbl = await AuthenticateXboxLiveAsync(oauth.AccessToken, ct);
		XboxTokenResponse xsts = await AuthorizeXstsAsync(xbl.Token, ct);
		string text = xsts.DisplayClaims.Xui.FirstOrDefault()?.Uhs ?? xbl.DisplayClaims.Xui.FirstOrDefault()?.Uhs ?? string.Empty;
		if (string.IsNullOrWhiteSpace(text))
		{
			throw new InvalidOperationException("Xbox 登录成功，但未返回 user hash。 ");
		}
		MinecraftTokenResponse mcToken = await LoginWithXboxAsync(text, xsts.Token, ct);
		await EnsureMinecraftOwnershipAsync(mcToken.AccessToken, ct);
		MinecraftProfileResponse minecraftProfileResponse = await GetMinecraftProfileAsync(mcToken.AccessToken, ct);
		MinecraftAppearance minecraftAppearance = MapAppearance(minecraftProfileResponse);
		return new MinecraftAccount
		{
			UserName = minecraftProfileResponse.Name,
			Uuid = minecraftProfileResponse.Id,
			AccessToken = mcToken.AccessToken,
			AccessTokenExpiresAt = DateTimeOffset.UtcNow.AddSeconds(Math.Max(60, mcToken.ExpiresIn - 60)),
			Xuid = (xsts.DisplayClaims.Xui.FirstOrDefault()?.Xid ?? xbl.DisplayClaims.Xui.FirstOrDefault()?.Xid ?? string.Empty),
			RefreshToken = (string.IsNullOrWhiteSpace(oauth.RefreshToken) ? string.Empty : oauth.RefreshToken),
			SkinUrl = minecraftAppearance.SkinUrl,
			SkinCount = minecraftAppearance.SkinCount,
			CapeCount = minecraftAppearance.CapeCount,
			Capes = minecraftAppearance.Capes
		};
	}

	public async Task<MinecraftAccount> LoginWithDeviceCodeAsync(string clientId, Action<DeviceCodeInfo> onCode, Action<string> log, CancellationToken ct)
	{
		DeviceCodeInfo deviceCodeInfo = await CreateDeviceCodeAsync(clientId, ct);
		onCode(deviceCodeInfo);
		return await LoginMinecraftAsync(await PollForMicrosoftTokenAsync(clientId, deviceCodeInfo, log, ct), ct);
	}

	public async Task<MinecraftAccount> LoginWithRefreshTokenAsync(string clientId, string refreshToken, CancellationToken ct)
	{
		MinecraftAccount minecraftAccount = await LoginMinecraftAsync(await RefreshMicrosoftTokenAsync(clientId, refreshToken, ct), ct);
		if (!string.IsNullOrWhiteSpace(minecraftAccount.RefreshToken))
		{
			return minecraftAccount;
		}
		return new MinecraftAccount
		{
			UserName = minecraftAccount.UserName,
			Uuid = minecraftAccount.Uuid,
			AccessToken = minecraftAccount.AccessToken,
			AccessTokenExpiresAt = minecraftAccount.AccessTokenExpiresAt,
			Xuid = minecraftAccount.Xuid,
			RefreshToken = refreshToken,
			SkinUrl = minecraftAccount.SkinUrl,
			SkinCount = minecraftAccount.SkinCount,
			CapeCount = minecraftAccount.CapeCount,
			Capes = minecraftAccount.Capes
		};
	}

	public async Task<MinecraftAppearance> GetMinecraftAppearanceAsync(string minecraftAccessToken, CancellationToken ct)
	{
		EnsureMinecraftAccessToken(minecraftAccessToken);
		return MapAppearance(await GetMinecraftProfileAsync(minecraftAccessToken, ct));
	}

	public async Task<MinecraftAppearance> UploadSkinAsync(string minecraftAccessToken, string pngPath, bool slim, CancellationToken ct)
	{
		EnsureMinecraftAccessToken(minecraftAccessToken);
		ValidateSkinPng(pngPath);
		MinecraftAppearance result;
		await using (FileStream stream = File.OpenRead(pngPath))
		{
			using MultipartFormDataContent form = new MultipartFormDataContent();
			form.Add(new StringContent(slim ? "slim" : "classic"), "variant");
			StreamContent streamContent = new StreamContent(stream);
			streamContent.Headers.ContentType = new MediaTypeHeaderValue("image/png");
			form.Add(streamContent, "file", Path.GetFileName(pngPath));
			using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, "https://api.minecraftservices.com/minecraft/profile/skins")
			{
				Content = form
			};
			request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", minecraftAccessToken);
			using HttpResponseMessage response = await _http.SendAsync(request, ct);
			string text = await response.Content.ReadAsStringAsync(ct);
			ThrowAppearanceMutationError(response, text, "上传皮肤");
			if (string.IsNullOrWhiteSpace(text))
			{
				goto IL_02b2;
			}
			MinecraftProfileResponse minecraftProfileResponse = JsonSerializer.Deserialize<MinecraftProfileResponse>(text, _json);
			if (minecraftProfileResponse == null)
			{
				goto IL_02b2;
			}
			result = MapAppearance(minecraftProfileResponse);
			goto end_IL_0206;
			IL_02b2:
			result = await GetMinecraftAppearanceAsync(minecraftAccessToken, ct);
			end_IL_0206:;
		}
		return result;
	}

	public async Task<MinecraftAppearance> SetActiveCapeAsync(string minecraftAccessToken, string? capeId, CancellationToken ct)
	{
		EnsureMinecraftAccessToken(minecraftAccessToken);
		bool hideCape = string.IsNullOrWhiteSpace(capeId);
		using HttpRequestMessage request = new HttpRequestMessage(hideCape ? HttpMethod.Delete : HttpMethod.Put, "https://api.minecraftservices.com/minecraft/profile/capes/active");
		request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", minecraftAccessToken);
		if (!hideCape)
		{
			request.Content = new StringContent(JsonSerializer.Serialize(new
			{
				capeId = capeId.Trim()
			}, _json), Encoding.UTF8, "application/json");
		}
		using HttpResponseMessage response = await _http.SendAsync(request, ct);
		ThrowAppearanceMutationError(response, await response.Content.ReadAsStringAsync(ct), hideCape ? "隐藏披风" : "切换披风");
		return await GetMinecraftAppearanceAsync(minecraftAccessToken, ct);
	}

	private async Task<XboxTokenResponse> AuthenticateXboxLiveAsync(string microsoftAccessToken, CancellationToken ct)
	{
		var payload = new
		{
			Properties = new
			{
				AuthMethod = "RPS",
				SiteName = "user.auth.xboxlive.com",
				RpsTicket = (microsoftAccessToken.StartsWith("d=", StringComparison.Ordinal) ? microsoftAccessToken : ("d=" + microsoftAccessToken))
			},
			RelyingParty = "http://auth.xboxlive.com",
			TokenType = "JWT"
		};
		using HttpResponseMessage response = await PostJsonAsync("https://user.auth.xboxlive.com/user/authenticate", payload, ct, xboxContract: true);
		if (response.IsSuccessStatusCode)
		{
			return await ReadOrThrowAsync<XboxTokenResponse>(response, ct);
		}
		string text = await response.Content.ReadAsStringAsync(ct);
		throw new InvalidOperationException($"Xbox Live 用户认证失败（HTTP {(int)response.StatusCode}）：{CompactError(text)}");
	}

	private async Task<XboxTokenResponse> AuthorizeXstsAsync(string xboxToken, CancellationToken ct)
	{
		var payload = new
		{
			Properties = new
			{
				SandboxId = "RETAIL",
				UserTokens = new string[1] { xboxToken }
			},
			RelyingParty = "rp://api.minecraftservices.com/",
			TokenType = "JWT"
		};
		using HttpResponseMessage response = await PostJsonAsync("https://xsts.auth.xboxlive.com/xsts/authorize", payload, ct, xboxContract: true);
		if (response.IsSuccessStatusCode)
		{
			return await ReadOrThrowAsync<XboxTokenResponse>(response, ct);
		}
		string text = await response.Content.ReadAsStringAsync(ct);
		string text2 = TryReadXstsError(text);
		throw new InvalidOperationException(string.IsNullOrWhiteSpace(text2) ? ("XSTS 授权失败：" + CompactError(text)) : text2);
	}

	private async Task<MinecraftTokenResponse> LoginWithXboxAsync(string userHash, string xstsToken, CancellationToken ct)
	{
		var payload = new
		{
			identityToken = "XBL3.0 x=" + userHash + ";" + xstsToken
		};
		using HttpResponseMessage response = await PostJsonAsync("https://api.minecraftservices.com/authentication/login_with_xbox", payload, ct);
		if (response.IsSuccessStatusCode)
		{
			return await ReadOrThrowAsync<MinecraftTokenResponse>(response, ct);
		}
		string text = await response.Content.ReadAsStringAsync(ct);
		if (response.StatusCode == HttpStatusCode.TooManyRequests)
		{
			throw new InvalidOperationException("Minecraft 登录请求过于频繁，请等待几分钟后再试。");
		}
		if (response.StatusCode == HttpStatusCode.Forbidden)
		{
			if (text.Contains("ACCOUNT_SUSPENDED", StringComparison.OrdinalIgnoreCase))
			{
				throw new InvalidOperationException("该 Microsoft/Xbox 账号已被暂停，暂时无法登录 Minecraft。");
			}
			if (text.Contains("Invalid app registration", StringComparison.OrdinalIgnoreCase) || text.Contains("AppRegInfo", StringComparison.OrdinalIgnoreCase))
			{
				throw new MinecraftAppRegistrationRequiredException();
			}
			if (string.IsNullOrWhiteSpace(text))
			{
				throw new InvalidOperationException("Minecraft 服务暂时拒绝了登录请求，请稍后重试。若问题持续存在，请在启动器中导出诊断信息。");
			}
			throw new InvalidOperationException("Minecraft Services 拒绝了登录请求（HTTP 403）：" + CompactError(text));
		}
		if (response.StatusCode == HttpStatusCode.ServiceUnavailable)
		{
			throw new InvalidOperationException("Minecraft 登录服务暂时不可用，请稍后重试。");
		}
		throw new InvalidOperationException($"Minecraft 令牌获取失败（HTTP {(int)response.StatusCode}）：{CompactError(text)}");
	}

	private async Task EnsureMinecraftOwnershipAsync(string minecraftAccessToken, CancellationToken ct)
	{
		using HttpResponseMessage response = await SendWithTransientRetryAsync(() => CreateBearerRequest("https://api.minecraftservices.com/entitlements/mcstore", minecraftAccessToken), ct);
		if (response.StatusCode == HttpStatusCode.TooManyRequests)
		{
			throw new InvalidOperationException("Minecraft 所有权校验请求过于频繁，请稍后重试。");
		}
		if ((await ReadOrThrowAsync<MinecraftEntitlementsResponse>(response, ct)).Items.Count == 0)
		{
			throw new InvalidOperationException("该账号未拥有 Minecraft Java 版，或 Xbox Game Pass 已到期。");
		}
	}

	private async Task<MinecraftProfileResponse> GetMinecraftProfileAsync(string minecraftAccessToken, CancellationToken ct)
	{
		using HttpResponseMessage response = await SendWithTransientRetryAsync(() => CreateBearerRequest("https://api.minecraftservices.com/minecraft/profile", minecraftAccessToken), ct);
		if (response.StatusCode == HttpStatusCode.NotFound)
		{
			throw new InvalidOperationException("该账号已拥有 Minecraft，但尚未创建 Java 版玩家档案。请先前往 minecraft.net 创建玩家名称。");
		}
		if (response.StatusCode == HttpStatusCode.TooManyRequests)
		{
			throw new InvalidOperationException("Minecraft 玩家档案请求过于频繁，请稍后重试。");
		}
		return await ReadOrThrowAsync<MinecraftProfileResponse>(response, ct);
	}

	private async Task<HttpResponseMessage> PostJsonAsync(string url, object payload, CancellationToken ct, bool xboxContract = false)
	{
		string json = JsonSerializer.Serialize(payload);
		return await SendWithTransientRetryAsync(delegate
		{
			HttpRequestMessage httpRequestMessage = new HttpRequestMessage(HttpMethod.Post, url)
			{
				Content = new StringContent(json, Encoding.UTF8, "application/json")
			};
			httpRequestMessage.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
			if (xboxContract)
			{
				httpRequestMessage.Headers.TryAddWithoutValidation("x-xbl-contract-version", "1");
			}
			return httpRequestMessage;
		}, ct);
	}

	private Task<HttpResponseMessage> PostFormAsync(string url, IReadOnlyDictionary<string, string> values, CancellationToken ct)
	{
		return SendWithTransientRetryAsync(() => new HttpRequestMessage(HttpMethod.Post, url)
		{
			Content = new FormUrlEncodedContent(values)
		}, ct);
	}

	private async Task<HttpResponseMessage> SendWithTransientRetryAsync(Func<HttpRequestMessage> requestFactory, CancellationToken ct)
	{
		for (int attempt = 1; attempt <= 3; attempt++)
		{
			using HttpRequestMessage request = requestFactory();
			try
			{
				HttpResponseMessage httpResponseMessage = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
				if (attempt == 3 || !IsTransient(httpResponseMessage.StatusCode))
				{
					return httpResponseMessage;
				}
				httpResponseMessage.Dispose();
			}
			catch (HttpRequestException) when (attempt < 3)
			{
			}
			await Task.Delay(TimeSpan.FromMilliseconds(200 * attempt), ct);
		}
		throw new InvalidOperationException("认证请求重试结束但没有返回结果。");
	}

	private static HttpRequestMessage CreateBearerRequest(string url, string accessToken)
	{
		HttpRequestMessage httpRequestMessage = new HttpRequestMessage(HttpMethod.Get, url);
		httpRequestMessage.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
		return httpRequestMessage;
	}

	private static bool IsTransient(HttpStatusCode statusCode)
	{
		if (statusCode == HttpStatusCode.RequestTimeout || (uint)(statusCode - 502) <= 2u)
		{
			return true;
		}
		return false;
	}

	private static MinecraftAppearance MapAppearance(MinecraftProfileResponse profile)
	{
		return new MinecraftAppearance
		{
			SkinUrl = (profile.Skins.FirstOrDefault((MinecraftProfileTexture skin) => skin.State.Equals("ACTIVE", StringComparison.OrdinalIgnoreCase))?.Url ?? profile.Skins.FirstOrDefault()?.Url ?? string.Empty),
			SkinCount = profile.Skins.Count,
			CapeCount = profile.Capes.Count,
			Capes = MapCapes(profile.Capes)
		};
	}

	private static List<MinecraftCapeInfo> MapCapes(IEnumerable<MinecraftProfileTexture> capes)
	{
		return capes.Select((MinecraftProfileTexture cape) => new MinecraftCapeInfo
		{
			Id = cape.Id,
			Alias = cape.Alias,
			Url = cape.Url,
			IsActive = cape.State.Equals("ACTIVE", StringComparison.OrdinalIgnoreCase)
		}).ToList();
	}

	private static void EnsureMinecraftAccessToken(string minecraftAccessToken)
	{
		if (string.IsNullOrWhiteSpace(minecraftAccessToken))
		{
			throw new InvalidOperationException("没有可用的 Minecraft Access Token，请先刷新正版账号。");
		}
	}

	private static void ThrowAppearanceMutationError(HttpResponseMessage response, string text, string action)
	{
		if (response.IsSuccessStatusCode)
		{
			return;
		}
		throw new InvalidOperationException(response.StatusCode switch
		{
			HttpStatusCode.BadRequest => action + "失败：文件或披风参数不符合 Minecraft 要求。", 
			HttpStatusCode.Unauthorized => action + "失败：Minecraft 登录凭据已过期，请刷新账号后重试。", 
			HttpStatusCode.Forbidden => action + "被 Minecraft 服务拒绝，请确认账号状态正常。", 
			HttpStatusCode.TooManyRequests => action + "请求过于频繁，请稍后重试。", 
			_ => $"{action}失败（HTTP {(int)response.StatusCode}）：{CompactError(text)}", 
		});
	}

	private static void ValidateSkinPng(string pngPath)
	{
		if (string.IsNullOrWhiteSpace(pngPath) || !File.Exists(pngPath))
		{
			throw new FileNotFoundException("找不到要上传的皮肤文件。", pngPath);
		}
		if (!Path.GetExtension(pngPath).Equals(".png", StringComparison.OrdinalIgnoreCase))
		{
			throw new InvalidOperationException("皮肤文件必须是 PNG 格式。");
		}
		FileInfo fileInfo = new FileInfo(pngPath);
		if (fileInfo.Length > 1048576)
		{
			throw new InvalidOperationException("皮肤 PNG 不能超过 1 MB。");
		}
		if (fileInfo.Length < 24)
		{
			throw new InvalidOperationException("皮肤 PNG 文件不完整。");
		}
		Span<byte> buffer = stackalloc byte[24];
		using FileStream fileStream = File.OpenRead(pngPath);
		fileStream.ReadExactly(buffer);
		ReadOnlySpan<byte> other = new byte[8] { 137, 80, 78, 71, 13, 10, 26, 10 };
		if (!((ReadOnlySpan<byte>)buffer.Slice(0, 8)).SequenceEqual(other) || !((ReadOnlySpan<byte>)buffer.Slice(12, 4)).SequenceEqual("IHDR"u8))
		{
			throw new InvalidOperationException("所选文件不是有效的 PNG 图片。");
		}
		int num = BinaryPrimitives.ReadInt32BigEndian(buffer.Slice(16, 4));
		int num2 = BinaryPrimitives.ReadInt32BigEndian(buffer.Slice(20, 4));
		bool flag = num != 64;
		if (!flag)
		{
			bool flag2 = ((num2 == 32 || num2 == 64) ? true : false);
			flag = !flag2;
		}
		if (flag)
		{
			throw new InvalidOperationException("Minecraft 皮肤尺寸必须是 64×64 或兼容旧格式的 64×32。");
		}
	}

	private async Task<T> ReadOrThrowAsync<T>(HttpResponseMessage response, CancellationToken ct)
	{
		string text = await response.Content.ReadAsStringAsync(ct);
		if (!response.IsSuccessStatusCode)
		{
			throw new InvalidOperationException($"HTTP {(int)response.StatusCode}：{CompactError(text)}");
		}
		T val = JsonSerializer.Deserialize<T>(text, _json);
		if (val == null)
		{
			throw new InvalidOperationException("接口响应为空。 ");
		}
		return val;
	}

	private static void EnsureClientId(string clientId)
	{
		if (string.IsNullOrWhiteSpace(clientId))
		{
			throw new InvalidOperationException("当前程序包还没有配置 Microsoft OAuth Client ID。开发者只需在 Services/AuthClientOptions.cs 中填入自己的 Microsoft Entra 应用 Client ID；正式发布后普通用户点击正版登录即可，不需要自己设置。 ");
		}
	}

	private static string TryReadOAuthError(string text)
	{
		try
		{
			using JsonDocument jsonDocument = JsonDocument.Parse(text);
			JsonElement value;
			return jsonDocument.RootElement.TryGetProperty("error", out value) ? (value.GetString() ?? string.Empty) : string.Empty;
		}
		catch
		{
			return string.Empty;
		}
	}

	private static bool RequiresInteractiveLogin(string error, string response)
	{
		if (error.Equals("invalid_grant", StringComparison.OrdinalIgnoreCase) || error.Equals("interaction_required", StringComparison.OrdinalIgnoreCase) || error.Equals("login_required", StringComparison.OrdinalIgnoreCase) || error.Equals("consent_required", StringComparison.OrdinalIgnoreCase))
		{
			return true;
		}
		if (response.Contains("refresh token", StringComparison.OrdinalIgnoreCase))
		{
			if (!response.Contains("expired", StringComparison.OrdinalIgnoreCase) && !response.Contains("revoked", StringComparison.OrdinalIgnoreCase))
			{
				return response.Contains("invalid", StringComparison.OrdinalIgnoreCase);
			}
			return true;
		}
		return false;
	}

	private static string TryReadXstsError(string text)
	{
		try
		{
			using JsonDocument jsonDocument = JsonDocument.Parse(text);
			if (!jsonDocument.RootElement.TryGetProperty("XErr", out var value))
			{
				return string.Empty;
			}
			long @int = value.GetInt64();
			long num = @int - 2148916233u;
			if ((ulong)num <= 5uL)
			{
				switch ((int)num)
				{
				case 0:
					goto IL_0075;
				case 2:
					goto IL_007e;
				case 3:
					goto IL_0087;
				case 4:
					goto IL_0090;
				case 5:
					goto IL_0099;
				case 1:
					goto IL_00ab;
				}
			}
			if (@int != 2148916262u)
			{
				goto IL_00ab;
			}
			string result = "XSTS 授权失败：该账号年龄设置或 Xbox 档案状态不满足要求。";
			goto IL_00be;
			IL_00ab:
			result = "XSTS 授权失败，错误码：" + @int;
			goto IL_00be;
			IL_00be:
			return result;
			IL_0075:
			result = "XSTS 授权失败：该 Microsoft 账号没有 Xbox 档案，请先登录 xbox.com 完成档案创建。";
			goto IL_00be;
			IL_007e:
			result = "XSTS 授权失败：该账号所在地区不支持 Xbox Live。";
			goto IL_00be;
			IL_0087:
			result = "XSTS 授权失败：该账号需要成年人验证。";
			goto IL_00be;
			IL_0090:
			result = "XSTS 授权失败：该账号需要成年人验证。";
			goto IL_00be;
			IL_0099:
			result = "XSTS 授权失败：该账号是儿童账号，需要加入家庭组并由成年人授权。";
			goto IL_00be;
		}
		catch
		{
			return string.Empty;
		}
	}

	private static string CompactError(string text)
	{
		if (string.IsNullOrWhiteSpace(text))
		{
			return "空响应";
		}
		string text2 = text.Replace("\r", " ", StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal).Trim();
		if (text2.Length > 500)
		{
			return text2.Substring(0, 500) + "...";
		}
		return text2;
	}
}
