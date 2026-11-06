using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
/// <see cref="MicrosoftAuthService"/> 完整登录链的可执行验证（此前 38/230 行 = 16.5%）。
///
/// 既有 <see cref="MicrosoftAuthServiceTests"/> 只覆盖了"单次设备码轮询"的 6 条分支，
/// 真正决定用户能不能登进去的是后面那串**五跳链路**：
///   微软 OAuth → XBL 用户认证 → XSTS 授权 → Minecraft login_with_xbox → 所有权校验 → 玩家档案
/// 每一跳都可能失败，而失败必须被翻译成"用户能照做"的中文原因。判错的表现是
/// "无限转圈"或"报了个 500 什么都没说"，不是崩溃，所以特别容易漏。
///
/// 本文件用按 URL 分派的假处理器把整条链跑通，**零真实网络**：
///   * 每一跳的请求体/请求头都被断言（XBL 必须带 x-xbl-contract-version，RpsTicket 必须补 d= 前缀）；
///   * 每一跳的错误码都断言到具体文案（XErr 5 种、403 的 3 种、404 无档案）；
///   * 瞬态重试（408/502/503/504）必须真的重试，非瞬态必须不重试。
/// </summary>
public sealed class MicrosoftAuthLoginChainTests
{
	/* ===================== 假处理器 ===================== */

	private sealed class RoutingHandler : HttpMessageHandler
	{
		private readonly Dictionary<string, Func<HttpRequestMessage, HttpResponseMessage>> _routes = new(StringComparer.OrdinalIgnoreCase);

		public List<(string Url, string? Body, string? Auth, string? Contract)> Calls { get; } = new();

		public RoutingHandler On(string urlFragment, Func<HttpRequestMessage, HttpResponseMessage> respond)
		{
			_routes[urlFragment] = respond;
			return this;
		}

		public RoutingHandler On(string urlFragment, HttpStatusCode status, string body) =>
			On(urlFragment, _ => Json(status, body));

		protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			string url = request.RequestUri?.ToString() ?? string.Empty;
			string? body = request.Content == null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
			string? auth = request.Headers.Authorization?.ToString();
			string? contract = request.Headers.TryGetValues("x-xbl-contract-version", out IEnumerable<string>? values) ? values.FirstOrDefault() : null;
			Calls.Add((url, body, auth, contract));

			foreach (KeyValuePair<string, Func<HttpRequestMessage, HttpResponseMessage>> route in _routes)
			{
				if (url.Contains(route.Key, StringComparison.OrdinalIgnoreCase))
				{
					return route.Value(request);
				}
			}
			return new HttpResponseMessage(HttpStatusCode.NotImplemented)
			{
				Content = new StringContent($"未登记的 URL：{url}"),
			};
		}
	}

	private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
		new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

	private static MicrosoftAuthService Create(RoutingHandler handler) => new MicrosoftAuthService(new HttpClient(handler));

	private static MicrosoftOAuthToken OAuth(string access = "MS-AT", string refresh = "MS-RT") =>
		new MicrosoftOAuthToken { AccessToken = access, RefreshToken = refresh, ExpiresIn = 3600 };

	/* ===================== 成功链路 ===================== */

	[Fact]
	public async Task LoginMinecraft_HappyPath_ProducesAccountWithProfileAndCapes()
	{
		RoutingHandler handler = new RoutingHandler()
			.On("user.auth.xboxlive.com/user/authenticate", HttpStatusCode.OK,
				"""{"Token":"XBL-TOKEN","DisplayClaims":{"xui":[{"uhs":"UHS-1","xid":"XID-1"}]}}""")
			.On("xsts.auth.xboxlive.com/xsts/authorize", HttpStatusCode.OK,
				"""{"Token":"XSTS-TOKEN","DisplayClaims":{"xui":[{"uhs":"UHS-XSTS","xid":"XID-XSTS"}]}}""")
			.On("authentication/login_with_xbox", HttpStatusCode.OK,
				"""{"access_token":"MC-AT","expires_in":86400}""")
			.On("entitlements/mcstore", HttpStatusCode.OK,
				"""{"items":[{"name":"product_minecraft"}]}""")
			.On("minecraft/profile", HttpStatusCode.OK,
				"""{"id":"uuid-mc","name":"Notch","skins":[{"id":"s1","state":"ACTIVE","url":"https://textures/skin.png","variant":"SLIM"}],"capes":[{"id":"c1","state":"ACTIVE","url":"https://textures/cape.png","alias":"Migrator"},{"id":"c2","state":"INACTIVE","url":"https://textures/c2.png","alias":"Other"}]}""");

		MinecraftAccount account = await Create(handler).LoginMinecraftAsync(OAuth(), CancellationToken.None);

		Assert.Equal("Notch", account.UserName);
		Assert.Equal("uuid-mc", account.Uuid);
		Assert.Equal("MC-AT", account.AccessToken);
		Assert.Equal("MS-RT", account.RefreshToken);
		// Xuid 取 XSTS 的 xid（不是 uhs）：uhs 只用于拼 identityToken
		Assert.Equal("XID-XSTS", account.Xuid);
		Assert.Equal("https://textures/skin.png", account.SkinUrl);
		Assert.True(account.IsSlim);
		Assert.Equal(1, account.SkinCount);
		Assert.Equal(2, account.CapeCount);
		Assert.Equal(2, account.Capes.Count);
		Assert.True(account.Capes.Single(c => c.Id == "c1").IsActive);
		Assert.False(account.Capes.Single(c => c.Id == "c2").IsActive);

		// 令牌过期时间必须比服务端给的早 60 秒，避免"刚好过期"边界
		DateTimeOffset expected = DateTimeOffset.UtcNow.AddSeconds(86400 - 60);
		Assert.InRange(account.AccessTokenExpiresAt, expected.AddSeconds(-30), expected.AddSeconds(30));
	}

	[Fact]
	public async Task LoginMinecraft_XblRequestShape_MustCarryContractHeaderAndDPrefix()
	{
		RoutingHandler handler = new RoutingHandler()
			.On("user.auth.xboxlive.com/user/authenticate", HttpStatusCode.OK,
				"""{"Token":"XBL","DisplayClaims":{"xui":[{"uhs":"U","xid":"X"}]}}""")
			.On("xsts.auth.xboxlive.com/xsts/authorize", HttpStatusCode.OK,
				"""{"Token":"XSTS","DisplayClaims":{"xui":[{"uhs":"U","xid":"X"}]}}""")
			.On("authentication/login_with_xbox", HttpStatusCode.OK, """{"access_token":"MC","expires_in":3600}""")
			.On("entitlements/mcstore", HttpStatusCode.OK, """{"items":[{"name":"p"}]}""")
			.On("minecraft/profile", HttpStatusCode.OK, """{"id":"u","name":"N","skins":[],"capes":[]}""");

		await Create(handler).LoginMinecraftAsync(OAuth("RAW-TOKEN"), CancellationToken.None);

		var xbl = handler.Calls.Single(c => c.Url.Contains("user/authenticate"));
		Assert.Equal("1", xbl.Contract); // 缺这个头 XBL 会返回 400 而不是可读错误
		Assert.Contains("\"RpsTicket\":\"d=RAW-TOKEN\"", xbl.Body); // 必须补 d= 前缀
		Assert.Contains("\"SiteName\":\"user.auth.xboxlive.com\"", xbl.Body);
		Assert.Contains("\"RelyingParty\":\"http://auth.xboxlive.com\"", xbl.Body);

		var xsts = handler.Calls.Single(c => c.Url.Contains("xsts/authorize"));
		Assert.Equal("1", xsts.Contract);
		Assert.Contains("\"RelyingParty\":\"rp://api.minecraftservices.com/\"", xsts.Body);
		Assert.Contains("\"SandboxId\":\"RETAIL\"", xsts.Body);

		var login = handler.Calls.Single(c => c.Url.Contains("login_with_xbox"));
		Assert.Contains("XBL3.0 x=U;XSTS", login.Body);
	}

	[Fact]
	public async Task LoginMinecraft_AlreadyPrefixedRpsTicket_IsNotDoublePrefixed()
	{
		RoutingHandler handler = new RoutingHandler()
			.On("user.auth.xboxlive.com/user/authenticate", HttpStatusCode.OK,
				"""{"Token":"XBL","DisplayClaims":{"xui":[{"uhs":"U","xid":"X"}]}}""")
			.On("xsts.auth.xboxlive.com/xsts/authorize", HttpStatusCode.OK,
				"""{"Token":"XSTS","DisplayClaims":{"xui":[{"uhs":"U","xid":"X"}]}}""")
			.On("authentication/login_with_xbox", HttpStatusCode.OK, """{"access_token":"MC","expires_in":3600}""")
			.On("entitlements/mcstore", HttpStatusCode.OK, """{"items":[{"name":"p"}]}""")
			.On("minecraft/profile", HttpStatusCode.OK, """{"id":"u","name":"N","skins":[],"capes":[]}""");

		await Create(handler).LoginMinecraftAsync(OAuth("d=ALREADY"), CancellationToken.None);

		string body = handler.Calls.Single(c => c.Url.Contains("user/authenticate")).Body ?? string.Empty;
		Assert.Contains("\"RpsTicket\":\"d=ALREADY\"", body);
		Assert.DoesNotContain("d=d=", body);
	}

	[Fact]
	public async Task LoginMinecraft_SkinWithoutActiveState_FallsBackToFirstSkin()
	{
		RoutingHandler handler = HappyChain()
			.On("minecraft/profile", HttpStatusCode.OK,
				"""{"id":"u","name":"N","skins":[{"id":"s1","state":"INACTIVE","url":"https://x/1.png","variant":"CLASSIC"},{"id":"s2","state":"INACTIVE","url":"https://x/2.png","variant":"CLASSIC"}],"capes":[]}""");

		MinecraftAccount account = await Create(handler).LoginMinecraftAsync(OAuth(), CancellationToken.None);

		Assert.Equal("https://x/1.png", account.SkinUrl);
		Assert.False(account.IsSlim);
		Assert.Equal(2, account.SkinCount);
	}

	[Fact]
	public async Task LoginMinecraft_EmptySkinList_LeavesSkinUrlEmptyRatherThanThrowing()
	{
		RoutingHandler handler = HappyChain()
			.On("minecraft/profile", HttpStatusCode.OK, """{"id":"u","name":"N","skins":[],"capes":[]}""");

		MinecraftAccount account = await Create(handler).LoginMinecraftAsync(OAuth(), CancellationToken.None);

		Assert.Equal(string.Empty, account.SkinUrl);
		Assert.Equal(0, account.SkinCount);
		Assert.Equal(0, account.CapeCount);
	}

	/* ===================== 每一跳的失败语义 ===================== */

	[Fact]
	public async Task LoginMinecraft_EmptyMicrosoftAccessToken_ThrowsBeforeAnyHttp()
	{
		RoutingHandler handler = new RoutingHandler();

		InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(
			() => Create(handler).LoginMinecraftAsync(OAuth(access: "  "), CancellationToken.None));

		Assert.Contains("access_token", ex.Message);
		Assert.Empty(handler.Calls);
	}

	[Fact]
	public async Task LoginMinecraft_MissingUserHash_ThrowsActionableError()
	{
		RoutingHandler handler = new RoutingHandler()
			.On("user.auth.xboxlive.com/user/authenticate", HttpStatusCode.OK, """{"Token":"XBL","DisplayClaims":{"xui":[{"xid":"X"}]}}""")
			.On("xsts.auth.xboxlive.com/xsts/authorize", HttpStatusCode.OK, """{"Token":"XSTS","DisplayClaims":{"xui":[{"xid":"X"}]}}""");

		InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(
			() => Create(handler).LoginMinecraftAsync(OAuth(), CancellationToken.None));

		Assert.Contains("user hash", ex.Message);
	}

	[Theory]
	[InlineData(2148916233L, "没有 Xbox 档案")]
	[InlineData(2148916235L, "所在地区不支持 Xbox Live")]
	[InlineData(2148916236L, "成年人验证")]
	[InlineData(2148916237L, "成年人验证")]
	[InlineData(2148916238L, "儿童账号")]
	[InlineData(2148916262L, "年龄设置或 Xbox 档案状态")]
	public async Task LoginMinecraft_XstsErrorCodes_AreTranslatedToActionableChinese(long xerr, string expectedFragment)
	{
		// XErr 是一组固定错误码，用户看不懂；翻译错会让人去改地区/改年龄，白折腾。
		RoutingHandler handler = new RoutingHandler()
			.On("user.auth.xboxlive.com/user/authenticate", HttpStatusCode.OK,
				"""{"Token":"XBL","DisplayClaims":{"xui":[{"uhs":"U","xid":"X"}]}}""")
			.On("xsts.auth.xboxlive.com/xsts/authorize", HttpStatusCode.Unauthorized,
				$$"""{"XErr":{{xerr}},"Message":"bad"}""");

		InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(
			() => Create(handler).LoginMinecraftAsync(OAuth(), CancellationToken.None));

		Assert.Contains(expectedFragment, ex.Message);
	}

	[Fact]
	public async Task LoginMinecraft_UnknownXstsErrorCode_IncludesTheRawCode()
	{
		RoutingHandler handler = new RoutingHandler()
			.On("user.auth.xboxlive.com/user/authenticate", HttpStatusCode.OK,
				"""{"Token":"XBL","DisplayClaims":{"xui":[{"uhs":"U","xid":"X"}]}}""")
			.On("xsts.auth.xboxlive.com/xsts/authorize", HttpStatusCode.Unauthorized, """{"XErr":9999999999}""");

		InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(
			() => Create(handler).LoginMinecraftAsync(OAuth(), CancellationToken.None));

		Assert.Contains("9999999999", ex.Message);
	}

	[Fact]
	public async Task LoginMinecraft_OwnershipEmpty_ReportsNoJavaEditionInsteadOfGenericError()
	{
		RoutingHandler handler = HappyChain()
			.On("entitlements/mcstore", HttpStatusCode.OK, """{"items":[]}""");

		InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(
			() => Create(handler).LoginMinecraftAsync(OAuth(), CancellationToken.None));

		Assert.Contains("未拥有 Minecraft Java 版", ex.Message);
	}

	[Fact]
	public async Task LoginMinecraft_ProfileNotFound_ExplainsHowToCreateProfile()
	{
		RoutingHandler handler = HappyChain()
			.On("minecraft/profile", HttpStatusCode.NotFound, """{"error":"NOT_FOUND"}""");

		InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(
			() => Create(handler).LoginMinecraftAsync(OAuth(), CancellationToken.None));

		Assert.Contains("尚未创建 Java 版玩家档案", ex.Message);
		Assert.Contains("minecraft.net", ex.Message);
	}

	[Theory]
	[InlineData(HttpStatusCode.Forbidden, "ACCOUNT_SUSPENDED", "已被暂停")]
	[InlineData(HttpStatusCode.Forbidden, "Invalid app registration", "")]
	[InlineData(HttpStatusCode.TooManyRequests, "{}", "过于频繁")]
	[InlineData(HttpStatusCode.ServiceUnavailable, "{}", "暂时不可用")]
	public async Task LoginWithXbox_FailureStatuses_AreMappedToDistinctGuidance(HttpStatusCode status, string body, string expectedFragment)
	{
		RoutingHandler handler = new RoutingHandler()
			.On("user.auth.xboxlive.com/user/authenticate", HttpStatusCode.OK,
				"""{"Token":"XBL","DisplayClaims":{"xui":[{"uhs":"U","xid":"X"}]}}""")
			.On("xsts.auth.xboxlive.com/xsts/authorize", HttpStatusCode.OK,
				"""{"Token":"XSTS","DisplayClaims":{"xui":[{"uhs":"U","xid":"X"}]}}""")
			.On("authentication/login_with_xbox", status, body);

		if (status == HttpStatusCode.Forbidden && expectedFragment.Length == 0)
		{
			// 「Invalid app registration」走的是专用异常类型，调用方据此提示"需要重新注册应用"
			await Assert.ThrowsAsync<MinecraftAppRegistrationRequiredException>(
				() => Create(handler).LoginMinecraftAsync(OAuth(), CancellationToken.None));
			return;
		}

		InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(
			() => Create(handler).LoginMinecraftAsync(OAuth(), CancellationToken.None));
		Assert.Contains(expectedFragment, ex.Message);
	}

	[Fact]
	public async Task LoginWithXbox_ForbiddenWithEmptyBody_GivesRetryGuidanceNotRawJson()
	{
		RoutingHandler handler = new RoutingHandler()
			.On("user.auth.xboxlive.com/user/authenticate", HttpStatusCode.OK,
				"""{"Token":"XBL","DisplayClaims":{"xui":[{"uhs":"U","xid":"X"}]}}""")
			.On("xsts.auth.xboxlive.com/xsts/authorize", HttpStatusCode.OK,
				"""{"Token":"XSTS","DisplayClaims":{"xui":[{"uhs":"U","xid":"X"}]}}""")
			.On("authentication/login_with_xbox", HttpStatusCode.Forbidden, string.Empty);

		InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(
			() => Create(handler).LoginMinecraftAsync(OAuth(), CancellationToken.None));

		Assert.Contains("暂时拒绝", ex.Message);
		Assert.Contains("诊断信息", ex.Message);
	}

	/* ===================== 瞬态重试 ===================== */

	[Fact]
	public async Task TransientRetry_503ThenSuccess_RetriesAndSucceeds()
	{
		// mcstore 第一次 503、第二次 200：必须重试，不能把一次抖动报成"登录失败"。
		int attempts = 0;
		RoutingHandler handler = HappyChain()
			.On("entitlements/mcstore", _ =>
			{
				attempts++;
				return attempts == 1
					? Json(HttpStatusCode.ServiceUnavailable, "{}")
					: Json(HttpStatusCode.OK, """{"items":[{"name":"p"}]}""");
			});

		MinecraftAccount account = await Create(handler).LoginMinecraftAsync(OAuth(), CancellationToken.None);

		Assert.Equal("Notch", account.UserName);
		Assert.Equal(2, attempts);
	}

	[Fact]
	public async Task TransientRetry_NonTransientStatus_IsNotRetried()
	{
		// 401 不是瞬态，重试只会拖慢失败反馈并可能触发风控。
		int attempts = 0;
		RoutingHandler handler = HappyChain()
			.On("entitlements/mcstore", _ =>
			{
				attempts++;
				return Json(HttpStatusCode.Unauthorized, """{"error":"UNAUTHORIZED"}""");
			});

		await Assert.ThrowsAsync<InvalidOperationException>(
			() => Create(handler).LoginMinecraftAsync(OAuth(), CancellationToken.None));

		Assert.Equal(1, attempts);
	}

	[Fact]
	public async Task TransientRetry_GivesUpAfterThreeAttempts()
	{
		int attempts = 0;
		RoutingHandler handler = HappyChain()
			.On("entitlements/mcstore", _ =>
			{
				attempts++;
				return Json(HttpStatusCode.BadGateway, "{}");
			});

		await Assert.ThrowsAsync<InvalidOperationException>(
			() => Create(handler).LoginMinecraftAsync(OAuth(), CancellationToken.None));

		Assert.Equal(3, attempts);
	}

	/* ===================== 刷新链路 ===================== */

	[Fact]
	public async Task LoginWithRefreshToken_WhenOauthOmitsRefreshToken_KeepsTheProvidedOne()
	{
		// 微软刷新接口有时不回 refresh_token，此时必须沿用旧的 —— 丢掉就等于强制用户重新登录。
		RoutingHandler handler = HappyChain()
			.On("oauth2/v2.0/token", HttpStatusCode.OK, """{"access_token":"MS-AT-2","expires_in":3600}""");

		MinecraftAccount account = await Create(handler).LoginWithRefreshTokenAsync("cid", "OLD-RT", CancellationToken.None);

		Assert.Equal("OLD-RT", account.RefreshToken);
		Assert.Equal("Notch", account.UserName);
	}

	[Fact]
	public async Task LoginWithRefreshToken_WhenOauthRotatesRefreshToken_UsesTheNewOne()
	{
		RoutingHandler handler = HappyChain()
			.On("oauth2/v2.0/token", HttpStatusCode.OK, """{"access_token":"MS-AT-2","refresh_token":"NEW-RT","expires_in":3600}""");

		MinecraftAccount account = await Create(handler).LoginWithRefreshTokenAsync("cid", "OLD-RT", CancellationToken.None);

		Assert.Equal("NEW-RT", account.RefreshToken);
	}

	[Theory]
	[InlineData("invalid_grant")]
	[InlineData("interaction_required")]
	[InlineData("login_required")]
	[InlineData("consent_required")]
	public async Task RefreshMicrosoftToken_InteractiveErrors_RaiseReauthenticationRequired(string error)
	{
		// 这四种错误是"凭据本身废了"，必须走交互式重登；当成普通失败重试会无限循环。
		RoutingHandler handler = new RoutingHandler()
			.On("oauth2/v2.0/token", HttpStatusCode.BadRequest, $$"""{"error":"{{error}}"}""");

		await Assert.ThrowsAsync<MicrosoftReauthenticationRequiredException>(
			() => Create(handler).RefreshMicrosoftTokenAsync("cid", "rt", CancellationToken.None));
	}

	[Fact]
	public async Task RefreshMicrosoftToken_EmptyRefreshToken_ThrowsWithoutHttp()
	{
		RoutingHandler handler = new RoutingHandler();

		InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(
			() => Create(handler).RefreshMicrosoftTokenAsync("cid", "  ", CancellationToken.None));

		Assert.Contains("refresh_token", ex.Message);
		Assert.Empty(handler.Calls);
	}

	[Fact]
	public async Task CreateDeviceCode_UnauthorizedClient_GivesEntraConfigurationGuidance()
	{
		// 这是最常见的自建 Client ID 错误（应用没勾"个人 Microsoft 帐户"），
		// 报错必须直接指向 Entra 设置项，否则用户完全无从下手。
		RoutingHandler handler = new RoutingHandler()
			.On("devicecode", HttpStatusCode.BadRequest, """{"error":"unauthorized_client","error_description":"AADSTS700016"}""");

		InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(
			() => Create(handler).CreateDeviceCodeAsync("cid", CancellationToken.None));

		Assert.Contains("Client ID", ex.Message);
		Assert.Contains("个人 Microsoft 帐户", ex.Message);
	}

	[Fact]
	public async Task CreateDeviceCode_OtherFailure_IncludesStatusAndBody()
	{
		RoutingHandler handler = new RoutingHandler()
			.On("devicecode", HttpStatusCode.InternalServerError, """{"error":"server_error"}""");

		InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(
			() => Create(handler).CreateDeviceCodeAsync("cid", CancellationToken.None));

		Assert.Contains("500", ex.Message);
		Assert.Contains("server_error", ex.Message);
	}

	[Fact]
	public async Task CreateDeviceCode_EmptyClientId_ThrowsWithoutHttp()
	{
		RoutingHandler handler = new RoutingHandler();

		await Assert.ThrowsAsync<InvalidOperationException>(
			() => Create(handler).CreateDeviceCodeAsync("", CancellationToken.None));

		Assert.Empty(handler.Calls);
	}

	/* ===================== 外观：上传皮肤 / 切披风 ===================== */

	[Fact]
	public async Task GetMinecraftAppearance_EmptyToken_ThrowsWithoutHttp()
	{
		RoutingHandler handler = new RoutingHandler();

		await Assert.ThrowsAsync<InvalidOperationException>(
			() => Create(handler).GetMinecraftAppearanceAsync("  ", CancellationToken.None));

		Assert.Empty(handler.Calls);
	}

	[Fact]
	public async Task UploadSkin_NonPngExtension_IsRejectedBeforeHttp()
	{
		RoutingHandler handler = new RoutingHandler();
		string path = Path.Combine(Path.GetTempPath(), $"skin-{Guid.NewGuid():N}.jpg");
		File.WriteAllBytes(path, new byte[64]);
		try
		{
			InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(
				() => Create(handler).UploadSkinAsync("mc-at", path, slim: true, CancellationToken.None));

			Assert.Contains("PNG", ex.Message);
			Assert.Empty(handler.Calls);
		}
		finally
		{
			File.Delete(path);
		}
	}

	[Fact]
	public async Task UploadSkin_FileTooLarge_IsRejectedBeforeHttp()
	{
		RoutingHandler handler = new RoutingHandler();
		string path = Path.Combine(Path.GetTempPath(), $"skin-{Guid.NewGuid():N}.png");
		byte[] buffer = new byte[1048577];
		buffer[0] = 137; buffer[1] = 80; buffer[2] = 78; buffer[3] = 71;
		File.WriteAllBytes(path, buffer);
		try
		{
			InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(
				() => Create(handler).UploadSkinAsync("mc-at", path, slim: false, CancellationToken.None));

			Assert.Contains("1 MB", ex.Message);
			Assert.Empty(handler.Calls);
		}
		finally
		{
			File.Delete(path);
		}
	}

	[Theory]
	[InlineData(64, 64, true)]
	[InlineData(64, 32, true)]
	[InlineData(64, 128, false)]
	[InlineData(32, 32, false)]
	[InlineData(128, 64, false)]
	public async Task UploadSkin_OnlyAcceptsMinecraftSkinDimensions(int width, int height, bool accepted)
	{
		// 尺寸不对时 Minecraft 会返回 400「文件不符合要求」，用户完全不知道错在哪，
		// 所以在本地就先挡掉并说清楚。
		RoutingHandler handler = new RoutingHandler()
			.On("profile/skins", HttpStatusCode.OK, """{"id":"u","name":"N","skins":[],"capes":[]}""")
			.On("minecraft/profile", HttpStatusCode.OK, """{"id":"u","name":"N","skins":[],"capes":[]}""");

		string path = Path.Combine(Path.GetTempPath(), $"skin-{Guid.NewGuid():N}.png");
		File.WriteAllBytes(path, BuildPngHeader(width, height));
		try
		{
			if (accepted)
			{
				await Create(handler).UploadSkinAsync("mc-at", path, slim: true, CancellationToken.None);
				Assert.NotEmpty(handler.Calls);
			}
			else
			{
				InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(
					() => Create(handler).UploadSkinAsync("mc-at", path, slim: true, CancellationToken.None));
				Assert.Contains("64×64", ex.Message);
				Assert.Empty(handler.Calls);
			}
		}
		finally
		{
			File.Delete(path);
		}
	}

	[Fact]
	public async Task UploadSkin_NotAPngMagic_IsRejected()
	{
		RoutingHandler handler = new RoutingHandler();
		string path = Path.Combine(Path.GetTempPath(), $"skin-{Guid.NewGuid():N}.png");
		File.WriteAllBytes(path, new byte[64]); // 全零，不是 PNG 魔数
		try
		{
			InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(
				() => Create(handler).UploadSkinAsync("mc-at", path, slim: true, CancellationToken.None));

			Assert.Contains("有效的 PNG", ex.Message);
			Assert.Empty(handler.Calls);
		}
		finally
		{
			File.Delete(path);
		}
	}

	[Fact]
	public async Task SetActiveCape_NullCapeId_UsesDeleteVerbToHideCape()
	{
		RoutingHandler handler = new RoutingHandler()
			.On("capes/active", HttpStatusCode.NoContent, string.Empty)
			.On("minecraft/profile", HttpStatusCode.OK, """{"id":"u","name":"N","skins":[],"capes":[]}""");

		await Create(handler).SetActiveCapeAsync("mc-at", null, CancellationToken.None);

		var capeCall = handler.Calls.Single(c => c.Url.Contains("capes/active"));
		Assert.Equal("Bearer mc-at", capeCall.Auth);
	}

	[Fact]
	public async Task SetActiveCape_WithCapeId_PutsJsonBody()
	{
		RoutingHandler handler = new RoutingHandler()
			.On("capes/active", HttpStatusCode.NoContent, string.Empty)
			.On("minecraft/profile", HttpStatusCode.OK, """{"id":"u","name":"N","skins":[],"capes":[{"id":"c1","state":"ACTIVE","url":"https://x/c.png"}]}""");

		MinecraftAppearance appearance = await Create(handler).SetActiveCapeAsync("mc-at", " c1 ", CancellationToken.None);

		var capeCall = handler.Calls.Single(c => c.Url.Contains("capes/active"));
		Assert.Contains("c1", capeCall.Body);
		Assert.DoesNotContain("  ", capeCall.Body);
		Assert.Equal(1, appearance.CapeCount);
	}

	[Theory]
	[InlineData(HttpStatusCode.BadRequest, "不符合 Minecraft 要求")]
	[InlineData(HttpStatusCode.Unauthorized, "已过期")]
	[InlineData(HttpStatusCode.Forbidden, "被 Minecraft 服务拒绝")]
	[InlineData(HttpStatusCode.TooManyRequests, "过于频繁")]
	public async Task SetActiveCape_FailureStatuses_AreMapped(HttpStatusCode status, string fragment)
	{
		RoutingHandler handler = new RoutingHandler()
			.On("capes/active", status, "{}");

		InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(
			() => Create(handler).SetActiveCapeAsync("mc-at", "c1", CancellationToken.None));

		Assert.Contains(fragment, ex.Message);
	}

	/* ===================== 辅助 ===================== */

	/// <summary>前四跳都成功的处理器，具体某一跳的错误由调用方覆盖。</summary>
	private static RoutingHandler HappyChain() =>
		new RoutingHandler()
			.On("user.auth.xboxlive.com/user/authenticate", HttpStatusCode.OK,
				"""{"Token":"XBL","DisplayClaims":{"xui":[{"uhs":"U","xid":"X"}]}}""")
			.On("xsts.auth.xboxlive.com/xsts/authorize", HttpStatusCode.OK,
				"""{"Token":"XSTS","DisplayClaims":{"xui":[{"uhs":"U","xid":"X"}]}}""")
			.On("authentication/login_with_xbox", HttpStatusCode.OK, """{"access_token":"MC","expires_in":3600}""")
			.On("entitlements/mcstore", HttpStatusCode.OK, """{"items":[{"name":"p"}]}""")
			.On("minecraft/profile", HttpStatusCode.OK,
				"""{"id":"uuid-mc","name":"Notch","skins":[{"id":"s1","state":"ACTIVE","url":"https://textures/skin.png","variant":"CLASSIC"}],"capes":[]}""");

	/// <summary>造一个只有 PNG 签名 + IHDR 宽高的最小文件（24 字节），够 ValidateSkinPng 判完。</summary>
	private static byte[] BuildPngHeader(int width, int height)
	{
		byte[] bytes = new byte[64];
		byte[] magic = { 137, 80, 78, 71, 13, 10, 26, 10 };
		Array.Copy(magic, bytes, 8);
		bytes[8] = 0; bytes[9] = 0; bytes[10] = 0; bytes[11] = 13; // chunk length
		Encoding.ASCII.GetBytes("IHDR").CopyTo(bytes, 12);
		bytes[16] = (byte)(width >> 24); bytes[17] = (byte)(width >> 16); bytes[18] = (byte)(width >> 8); bytes[19] = (byte)width;
		bytes[20] = (byte)(height >> 24); bytes[21] = (byte)(height >> 16); bytes[22] = (byte)(height >> 8); bytes[23] = (byte)height;
		return bytes;
	}
}
