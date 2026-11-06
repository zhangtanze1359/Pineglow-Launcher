using System;
using System.Collections.Generic;
using System.Linq;
using Pineglow.Launcher.Models;
using Pineglow.Launcher.Services;
using Xunit;

namespace Pineglow.Launcher.Tests;

/// <summary>
/// <see cref="AccountService"/> 的直测（此前 64/276 行 = 23.2%，只有 1 处测试调用点）。
///
/// 这个类决定"当前用哪个账号、账号列表怎么维护、以及账号如何投影到 legacy 配置字段"。
/// 判错不会抛异常，只会**静默把用户登到别的账号上**，或者把 refreshToken 写进
/// 不该有的字段——属于"出错时看起来一切正常"的那一类。
///
/// 本文件全部零 IO：只构造 <see cref="AppConfig"/> 与账号对象，断言内存状态。
/// 与 SecretStore / ConfigStore 的磁盘交互不在本文件范围（那部分由
/// SecretPersistenceContractTests 与 ConfigPreservationTests 覆盖）。
/// </summary>
public sealed class AccountServiceTests
{
	/* ===================== 辅助 ===================== */

	private static AppConfig EmptyConfig() => new AppConfig();

	private static LauncherAccount Offline(string name) => new LauncherAccount { Type = "offline", UserName = name };

	private static LauncherAccount Microsoft(string name, string uuid, string clientId = "cid", string refresh = "rt", string access = "at") =>
		new LauncherAccount
		{
			Type = "microsoft",
			UserName = name,
			Uuid = uuid,
			ClientId = clientId,
			RefreshToken = refresh,
			AccessToken = access,
			AccessTokenExpiresAt = new DateTimeOffset(2026, 11, 6, 12, 0, 0, TimeSpan.Zero),
		};

	private static LauncherAccount ThirdParty(string name, string uuid, string server) =>
		new LauncherAccount { Type = "authlib", UserName = name, Uuid = uuid, AuthServerUrl = server };

	/* ===================== EnsureAccounts：首次生成 ===================== */

	[Fact]
	public void EnsureAccounts_EmptyConfig_CreatesSingleOfflineAccountAndSelectsIt()
	{
		AppConfig config = EmptyConfig();
		config.PlayerName = "Steve";

		bool changed = AccountService.EnsureAccounts(config);

		Assert.True(changed, "首次生成账号必须返回 true，否则调用方不会落盘");
		LauncherAccount account = Assert.Single(config.Accounts);
		Assert.Equal("offline", account.Type);
		Assert.Equal("Steve", account.UserName);
		Assert.Equal(account.Id, config.SelectedAccountId);
		Assert.Equal("offline", config.AccountMode);
		Assert.Equal("Steve", config.PlayerName);
	}

	[Fact]
	public void EnsureAccounts_EmptyPlayerName_FallsBackToPlayer()
	{
		AppConfig config = EmptyConfig();
		config.PlayerName = "";

		AccountService.EnsureAccounts(config);

		Assert.Equal("Player", Assert.Single(config.Accounts).UserName);
	}

	[Fact]
	public void EnsureAccounts_LegacyMicrosoftPair_CreatesBothAccountsAndHonoursAccountMode()
	{
		// 老配置（只有 MicrosoftUserName/MicrosoftUuid 这类 legacy 字段，没有 Accounts 列表）
		// 必须能被迁移成两条账号，且 AccountMode=microsoft 时选中微软那条。
		AppConfig config = EmptyConfig();
		config.PlayerName = "Legacy";
		config.MicrosoftUserName = "MSUser";
		config.MicrosoftUuid = "uuid-ms";
		config.MicrosoftXuid = "xuid-ms";
		config.MicrosoftClientId = "cid-ms";
		config.MicrosoftAccessToken = "at-ms";
		config.AccountMode = "microsoft";

		AccountService.EnsureAccounts(config, legacyRefreshToken: "rt-legacy");

		Assert.Equal(2, config.Accounts.Count);
		LauncherAccount ms = config.Accounts.Single(a => a.IsMicrosoft);
		Assert.Equal("MSUser", ms.UserName);
		Assert.Equal("uuid-ms", ms.Uuid);
		Assert.Equal("xuid-ms", ms.Xuid);
		Assert.Equal("cid-ms", ms.ClientId);
		Assert.Equal("rt-legacy", ms.RefreshToken);
		Assert.Equal("at-ms", ms.AccessToken);
		Assert.Equal(ms.Id, config.SelectedAccountId);
	}

	[Fact]
	public void EnsureAccounts_LegacyMicrosoftFieldsWithoutMicrosoftMode_SelectsOfflineAccount()
	{
		AppConfig config = EmptyConfig();
		config.MicrosoftUserName = "MSUser";
		config.MicrosoftUuid = "uuid-ms";
		config.AccountMode = "offline";

		AccountService.EnsureAccounts(config);

		Assert.Equal(2, config.Accounts.Count);
		LauncherAccount offline = config.Accounts.Single(a => !a.IsMicrosoft && !a.IsThirdParty);
		Assert.Equal(offline.Id, config.SelectedAccountId);
	}

	[Fact]
	public void EnsureAccounts_OnlyUsernameWithoutUuid_DoesNotInventMicrosoftAccount()
	{
		// 只有 UserName 没有 Uuid 时无法构成可用账号，不得凭空造一条（会造成"看似已登录"）。
		AppConfig config = EmptyConfig();
		config.MicrosoftUserName = "MSUser";
		config.MicrosoftUuid = "";
		config.AccountMode = "microsoft";

		AccountService.EnsureAccounts(config);

		Assert.Single(config.Accounts);
		Assert.False(config.Accounts[0].IsMicrosoft);
	}

	[Fact]
	public void EnsureAccounts_NullCapesList_IsRepairedForEveryAccount()
	{
		// Capes 为 null 时前端读取会抛 NRE，这里必须补齐成空列表。
		AppConfig config = EmptyConfig();
		LauncherAccount a = Offline("A");
		a.Capes = null!;
		LauncherAccount b = Offline("B");
		b.Capes = null!;
		config.Accounts.Add(a);
		config.Accounts.Add(b);
		config.SelectedAccountId = a.Id;

		AccountService.EnsureAccounts(config);

		Assert.All(config.Accounts, x => Assert.NotNull(x.Capes));
	}

	[Fact]
	public void EnsureAccounts_DanglingSelectedId_IsNotRepaired_ButLegacyFollowsFirstAccount()
	{
		// **实测行为（值得记录）**：
		// 修复分支写的是 `if (GetSelected(config) == null)`，而 GetSelected 在没有 id 命中时会
		// 回退到 Accounts[0]，因此只要列表非空它就**永远不返回 null** —— 这个修复分支实际上
		// 只在列表为空时可达（而列表刚被填满，所以不可达）。
		//
		// 后果：SelectedAccountId 指向一个不存在的 id 时不会被纠正，配置里会带着这个悬空 id
		// 继续落盘；但 ApplySelectedToLegacy 也走 GetSelected，所以实际投影用的是 Accounts[0]。
		// 即"界面/启动行为正确，配置里留了一个脏 id"。不是功能性缺陷，但是一处不一致，
		// 这里把它钉住：将来若有人改了 GetSelected 的回退语义，这条会红。
		AppConfig config = EmptyConfig();
		config.Accounts.Add(Offline("Real"));
		config.SelectedAccountId = "no-such-account-id";

		bool changed = AccountService.EnsureAccounts(config);

		Assert.False(changed, "列表非空且无可修内容时应返回 false（不触发多余落盘）");
		Assert.Equal("no-such-account-id", config.SelectedAccountId);
		Assert.Equal("Real", config.PlayerName); // legacy 投影来自 Accounts[0]
		Assert.Equal("offline", config.AccountMode);
	}

	/* ===================== GetSelected ===================== */

	[Fact]
	public void GetSelected_UnknownId_FallsBackToFirstAccount()
	{
		AppConfig config = EmptyConfig();
		config.Accounts.Add(Offline("First"));
		config.Accounts.Add(Offline("Second"));
		config.SelectedAccountId = "missing";

		Assert.Equal("First", AccountService.GetSelected(config)!.UserName);
	}

	[Fact]
	public void GetSelected_EmptyAccounts_ReturnsNull()
	{
		Assert.Null(AccountService.GetSelected(EmptyConfig()));
	}

	/* ===================== AddOffline ===================== */

	[Fact]
	public void AddOffline_NewName_AppendsAndSelects()
	{
		AppConfig config = EmptyConfig();
		config.Accounts.Add(Offline("Existing"));
		config.SelectedAccountId = config.Accounts[0].Id;

		LauncherAccount added = AccountService.AddOffline(config, "Newbie");

		Assert.Equal(2, config.Accounts.Count);
		Assert.Equal("Newbie", added.UserName);
		Assert.Equal(added.Id, config.SelectedAccountId);
		Assert.Equal("Newbie", config.PlayerName);
	}

	[Fact]
	public void AddOffline_SameNameIgnoringCase_ReusesInsteadOfDuplicating()
	{
		AppConfig config = EmptyConfig();
		LauncherAccount first = Offline("Steve");
		config.Accounts.Add(first);
		config.SelectedAccountId = "something-else";

		LauncherAccount again = AccountService.AddOffline(config, "sTeVe");

		Assert.Single(config.Accounts);
		Assert.Same(first, again);
		Assert.Equal(first.Id, config.SelectedAccountId);
	}

	[Fact]
	public void AddOffline_DoesNotDedupeAgainstMicrosoftOrThirdPartyAccounts()
	{
		// 同名但类型不同的账号是两个不同身份（离线 Steve 与正版 Steve），不能合并。
		AppConfig config = EmptyConfig();
		config.Accounts.Add(Microsoft("Steve", "uuid-ms"));
		config.SelectedAccountId = config.Accounts[0].Id;

		AccountService.AddOffline(config, "Steve");

		Assert.Equal(2, config.Accounts.Count);
		Assert.Contains(config.Accounts, a => !a.IsMicrosoft && a.UserName == "Steve");
	}

	[Theory]
	[InlineData("", "Player")]
	[InlineData("   ", "Player")]
	[InlineData("bad name!", "badname")]
	[InlineData("a-b.c/d", "abcd")]
	[InlineData("___", "___")]
	public void AddOffline_NameIsSanitised(string input, string expected)
	{
		AppConfig config = EmptyConfig();

		LauncherAccount account = AccountService.AddOffline(config, input);

		Assert.Equal(expected, account.UserName);
	}

	[Fact]
	public void SafeName_TruncatesToSixteenCharacters()
	{
		AppConfig config = EmptyConfig();

		LauncherAccount account = AccountService.AddOffline(config, new string('x', 40));

		Assert.Equal(16, account.UserName.Length);
		Assert.Equal(new string('x', 16), account.UserName);
	}

	[Fact]
	public void SafeName_KeepsUnicodeLetters_BecauseValidationIsCharIsLetterOrDigit()
	{
		// **实测口径**：SafeName 用的是 char.IsLetterOrDigit，按 Unicode 判定，
		// 所以中文昵称会被完整保留（Minecraft 本身也允许非 ASCII 用户名）。
		// 这不是缺陷，但此前没有文档写过，钉住以免后人误以为中文名会被清空成 Player。
		AppConfig config = EmptyConfig();

		LauncherAccount account = AccountService.AddOffline(config, "松光玩家");

		Assert.Equal("松光玩家", account.UserName);
	}

	/* ===================== UpsertMicrosoft ===================== */

	[Fact]
	public void UpsertMicrosoft_NewUuid_AppendsWithAllFieldsCopied()
	{
		AppConfig config = EmptyConfig();
		MinecraftAccount value = new MinecraftAccount
		{
			UserName = "MSUser",
			Uuid = "UUID-1",
			Xuid = "xuid-1",
			AccessToken = "at",
			RefreshToken = "rt",
			AccessTokenExpiresAt = new DateTimeOffset(2026, 11, 6, 12, 0, 0, TimeSpan.Zero),
			SkinUrl = "https://example.com/skin.png",
			IsSlim = true,
			SkinCount = 2,
			CapeCount = 1,
			Capes = new List<MinecraftCapeInfo>
			{
				new MinecraftCapeInfo { Id = "c1", Alias = "Mojang", Url = "https://example.com/c1.png", IsActive = true },
			},
		};

		LauncherAccount account = AccountService.UpsertMicrosoft(config, value, "client-1");

		Assert.Same(account, Assert.Single(config.Accounts));
		Assert.Equal("microsoft", account.Type);
		Assert.Equal("MSUser", account.UserName);
		Assert.Equal("UUID-1", account.Uuid);
		Assert.Equal("xuid-1", account.Xuid);
		Assert.Equal("client-1", account.ClientId);
		Assert.Equal("rt", account.RefreshToken);
		Assert.Equal("at", account.AccessToken);
		Assert.Equal("slim", account.SkinModel);
		Assert.Equal(2, account.SkinCount);
		Assert.Equal(1, account.CapeCount);
		Assert.Single(account.Capes);
		Assert.True(account.Capes[0].IsActive);
		Assert.Equal(account.Id, config.SelectedAccountId);
	}

	[Fact]
	public void UpsertMicrosoft_SameUuidDifferentCase_UpdatesInPlace()
	{
		// 微软返回的 uuid 大小写不固定，必须按忽略大小写去重，否则每登录一次多一条账号。
		AppConfig config = EmptyConfig();
		LauncherAccount existing = Microsoft("OldName", "abcdef");
		config.Accounts.Add(existing);
		config.SelectedAccountId = existing.Id;

		MinecraftAccount value = new MinecraftAccount { UserName = "NewName", Uuid = "ABCDEF" };
		LauncherAccount result = AccountService.UpsertMicrosoft(config, value, "cid");

		Assert.Single(config.Accounts);
		Assert.Same(existing, result);
		Assert.Equal("NewName", existing.UserName);
	}

	[Fact]
	public void UpsertMicrosoft_SwitchingCapeList_ReplacesRatherThanAccumulates()
	{
		AppConfig config = EmptyConfig();
		config.Accounts.Add(Microsoft("MSUser", "uuid-1"));

		AccountService.UpsertMicrosoft(config, new MinecraftAccount
		{
			UserName = "MSUser",
			Uuid = "uuid-1",
			Capes = new List<MinecraftCapeInfo> { new MinecraftCapeInfo { Id = "a" }, new MinecraftCapeInfo { Id = "b" } },
		}, "cid");

		AccountService.UpsertMicrosoft(config, new MinecraftAccount
		{
			UserName = "MSUser",
			Uuid = "uuid-1",
			Capes = new List<MinecraftCapeInfo> { new MinecraftCapeInfo { Id = "c" } },
		}, "cid");

		Assert.Equal(new[] { "c" }, config.Accounts[0].Capes.Select(c => c.Id));
	}

	/* ===================== UpsertThirdParty ===================== */

	[Fact]
	public void UpsertThirdParty_NewAccount_ClearsMicrosoftOnlyFields()
	{
		// 外置登录没有 refreshToken / xuid / clientId，这些字段必须清空，
		// 否则后续"续期"逻辑会拿着空/错的值去访问微软接口。
		AppConfig config = EmptyConfig();

		LauncherAccount account = AccountService.UpsertThirdParty(config,
			new AuthlibAccount("Li", "uuid-li", "at-li", "https://auth.example.com"));

		Assert.Equal("authlib", account.Type);
		Assert.Equal("Li", account.UserName);
		Assert.Equal("https://auth.example.com", account.AuthServerUrl);
		Assert.Equal("at-li", account.AccessToken);
		Assert.Equal(string.Empty, account.RefreshToken);
		Assert.Equal(string.Empty, account.Xuid);
		Assert.Equal(string.Empty, account.ClientId);
		Assert.Equal(DateTimeOffset.MinValue, account.AccessTokenExpiresAt);
	}

	[Fact]
	public void UpsertThirdParty_SameUuidDifferentServer_CreatesSecondAccount()
	{
		// 同一个 uuid 在不同认证服务器上是不同的人，不能合并。
		AppConfig config = EmptyConfig();
		config.Accounts.Add(ThirdParty("Li", "uuid-li", "https://a.example.com"));
		config.SelectedAccountId = config.Accounts[0].Id;

		AccountService.UpsertThirdParty(config, new AuthlibAccount("Li", "uuid-li", "at", "https://b.example.com"));

		Assert.Equal(2, config.Accounts.Count);
	}

	[Fact]
	public void UpsertThirdParty_SameUuidAndServer_UpdatesInPlace()
	{
		AppConfig config = EmptyConfig();
		LauncherAccount existing = ThirdParty("Li", "uuid-li", "https://a.example.com");
		config.Accounts.Add(existing);

		LauncherAccount result = AccountService.UpsertThirdParty(config,
			new AuthlibAccount("Li2", "UUID-LI", "at2", "https://A.example.com"));

		Assert.Single(config.Accounts);
		Assert.Same(existing, result);
		Assert.Equal("Li2", existing.UserName);
		Assert.Equal("at2", existing.AccessToken);
	}

	[Theory]
	[InlineData("", "", 0, 0)]
	[InlineData("https://x/skin.png", "", 1, 0)]
	[InlineData("", "https://x/cape.png", 0, 1)]
	[InlineData("https://x/skin.png", "https://x/cape.png", 1, 1)]
	public void UpsertThirdParty_CountsFollowUrlPresence(string skinUrl, string capeUrl, int expectedSkin, int expectedCape)
	{
		AppConfig config = EmptyConfig();

		LauncherAccount account = AccountService.UpsertThirdParty(config,
			new AuthlibAccount("Li", "uuid-li", "at", "https://auth.example.com", skinUrl, capeUrl));

		Assert.Equal(expectedSkin, account.SkinCount);
		Assert.Equal(expectedCape, account.CapeCount);
	}

	[Fact]
	public void UpsertThirdParty_WithoutCapeUrl_LeavesPreviousCapesUntouched()
	{
		// **实测行为（已知不一致，钉住以便将来修）**：
		// CapeCount 被重置为 0，但 Capes 列表只在 CapeUrl 非空时才被覆盖 ——
		// 因此"曾经有披风、这次没有"的账号会得到 CapeCount=0 却仍保留旧 Capes 条目。
		// 界面按 CapeCount 判断不会显示披风，所以不构成可见缺陷；
		// 但两个字段语义不一致，后续如果改成读 Capes 就会露出来。
		AppConfig config = EmptyConfig();
		config.Accounts.Add(ThirdParty("Li", "uuid-li", "https://auth.example.com"));

		AccountService.UpsertThirdParty(config,
			new AuthlibAccount("Li", "uuid-li", "at", "https://auth.example.com", "", "https://x/cape.png"));
		Assert.Single(config.Accounts[0].Capes);

		LauncherAccount after = AccountService.UpsertThirdParty(config,
			new AuthlibAccount("Li", "uuid-li", "at", "https://auth.example.com"));

		Assert.Equal(0, after.CapeCount);
		Assert.Single(after.Capes); // 旧条目仍在（与 CapeCount 不一致）
	}

	/* ===================== Select / UpdateSelectedOfflineName ===================== */

	[Fact]
	public void Select_UnknownId_Throws()
	{
		AppConfig config = EmptyConfig();
		config.Accounts.Add(Offline("A"));

		InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => AccountService.Select(config, "nope"));
		Assert.Contains("不存在", ex.Message);
	}

	[Fact]
	public void Select_KnownId_UpdatesSelectionAndLegacyProjection()
	{
		AppConfig config = EmptyConfig();
		LauncherAccount a = Offline("A");
		LauncherAccount b = Microsoft("B", "uuid-b", clientId: "cid-b", refresh: "rt-b", access: "at-b");
		config.Accounts.Add(a);
		config.Accounts.Add(b);

		AccountService.Select(config, b.Id);

		Assert.Equal(b.Id, config.SelectedAccountId);
		Assert.Equal("microsoft", config.AccountMode);
		Assert.Equal("B", config.PlayerName);
		Assert.Equal("cid-b", config.MicrosoftClientId);
		Assert.Equal("rt-b", config.MicrosoftRefreshToken);
		Assert.Equal("uuid-b", config.MicrosoftUuid);
	}

	[Fact]
	public void UpdateSelectedOfflineName_OnlyAffectsOfflineAccounts()
	{
		AppConfig config = EmptyConfig();
		LauncherAccount ms = Microsoft("MSUser", "uuid-ms");
		config.Accounts.Add(ms);
		config.SelectedAccountId = ms.Id;
		AccountService.ApplySelectedToLegacy(config);
		Assert.Equal("MSUser", config.PlayerName);

		AccountService.UpdateSelectedOfflineName(config, "Renamed");

		Assert.Equal("MSUser", ms.UserName); // 微软账号改名必须走微软接口，不能本地改
		Assert.Equal("MSUser", config.PlayerName); // legacy 投影也不得被顺手改掉
	}

	[Fact]
	public void UpdateSelectedOfflineName_OfflineAccount_RenamesAndReprojects()
	{
		AppConfig config = EmptyConfig();
		LauncherAccount offline = Offline("Old");
		config.Accounts.Add(offline);
		config.SelectedAccountId = offline.Id;

		AccountService.UpdateSelectedOfflineName(config, "New Name!");

		Assert.Equal("NewName", offline.UserName);
		Assert.Equal("NewName", config.PlayerName);
	}

	[Fact]
	public void UpdateSelectedOfflineName_EmptyConfig_DoesNotThrow()
	{
		// Selected 为 null 时必须安静返回，不能空引用崩掉设置页。
		AppConfig config = EmptyConfig();

		AccountService.UpdateSelectedOfflineName(config, "X");

		Assert.Equal("Player", config.PlayerName);
	}

	/* ===================== RemoveSelected ===================== */

	[Fact]
	public void RemoveSelected_LastAccount_IsRefused()
	{
		// 不允许删到零个账号：启动链路假定至少有一个可用身份。
		AppConfig config = EmptyConfig();
		config.Accounts.Add(Offline("Only"));
		config.SelectedAccountId = config.Accounts[0].Id;

		Assert.Null(AccountService.RemoveSelected(config));
		Assert.Single(config.Accounts);
	}

	[Fact]
	public void RemoveSelected_WithMultipleAccounts_RemovesAndSelectsFirstRemaining()
	{
		AppConfig config = EmptyConfig();
		LauncherAccount a = Offline("A");
		LauncherAccount b = Microsoft("B", "uuid-b");
		config.Accounts.Add(a);
		config.Accounts.Add(b);
		config.SelectedAccountId = b.Id;

		LauncherAccount? removed = AccountService.RemoveSelected(config);

		Assert.Same(b, removed);
		Assert.Single(config.Accounts);
		Assert.Equal(a.Id, config.SelectedAccountId);
		Assert.Equal("offline", config.AccountMode);
	}

	[Fact]
	public void RemoveSelected_EmptyConfig_ReturnsNull()
	{
		Assert.Null(AccountService.RemoveSelected(EmptyConfig()));
	}

	/* ===================== ApplySelectedToLegacy ===================== */

	[Fact]
	public void ApplySelectedToLegacy_OfflineSelection_ClearsEveryMicrosoftField()
	{
		// 从微软账号切到离线账号后，legacy 字段里绝不能残留上一个人的 refreshToken，
		// 否则下次启动会拿别人的凭据去续期。
		AppConfig config = EmptyConfig();
		config.MicrosoftRefreshToken = "leaked-rt";
		config.MicrosoftAccessToken = "leaked-at";
		config.MicrosoftAccessTokenExpiresAt = DateTimeOffset.MaxValue;
		config.MicrosoftUserName = "SomeoneElse";
		config.MicrosoftUuid = "uuid-someone";
		config.MicrosoftXuid = "xuid-someone";
		LauncherAccount offline = Offline("Me");
		config.Accounts.Add(offline);
		config.SelectedAccountId = offline.Id;

		AccountService.ApplySelectedToLegacy(config);

		Assert.Equal("offline", config.AccountMode);
		Assert.Equal("Me", config.PlayerName);
		Assert.Equal(string.Empty, config.MicrosoftRefreshToken);
		Assert.Equal(string.Empty, config.MicrosoftAccessToken);
		Assert.Equal(DateTimeOffset.MinValue, config.MicrosoftAccessTokenExpiresAt);
		Assert.Equal(string.Empty, config.MicrosoftUserName);
		Assert.Equal(string.Empty, config.MicrosoftUuid);
		Assert.Equal(string.Empty, config.MicrosoftXuid);
	}

	[Fact]
	public void ApplySelectedToLegacy_ThirdPartySelection_ReportsAuthlibModeAndClearsMicrosoftFields()
	{
		AppConfig config = EmptyConfig();
		config.MicrosoftRefreshToken = "leaked-rt";
		LauncherAccount third = ThirdParty("Li", "uuid-li", "https://auth.example.com");
		config.Accounts.Add(third);
		config.SelectedAccountId = third.Id;

		AccountService.ApplySelectedToLegacy(config);

		Assert.Equal("authlib", config.AccountMode);
		Assert.Equal("Li", config.PlayerName);
		Assert.Equal(string.Empty, config.MicrosoftRefreshToken);
		Assert.Equal(string.Empty, config.MicrosoftUserName);
	}

	[Fact]
	public void ApplySelectedToLegacy_EmptyAccounts_ChangesNothing()
	{
		AppConfig config = EmptyConfig();
		config.AccountMode = "microsoft";
		config.PlayerName = "Untouched";

		AccountService.ApplySelectedToLegacy(config);

		Assert.Equal("microsoft", config.AccountMode);
		Assert.Equal("Untouched", config.PlayerName);
	}

	/* ===================== 往返一致性 ===================== */

	[Fact]
	public void AddSelectRemoveSequence_KeepsLegacyProjectionConsistentAtEveryStep()
	{
		AppConfig config = EmptyConfig();
		config.PlayerName = "First";
		AccountService.EnsureAccounts(config);

		LauncherAccount microsoft = AccountService.UpsertMicrosoft(config, new MinecraftAccount
		{
			UserName = "MS",
			Uuid = "uuid-ms",
			RefreshToken = "rt",
			AccessToken = "at",
			AccessTokenExpiresAt = new DateTimeOffset(2026, 11, 6, 12, 0, 0, TimeSpan.Zero),
		}, "cid");
		Assert.Equal("microsoft", config.AccountMode);
		Assert.Equal("MS", config.PlayerName);

		LauncherAccount third = AccountService.UpsertThirdParty(config, new AuthlibAccount("Li", "uuid-li", "at", "https://auth.example.com"));
		Assert.Equal("authlib", config.AccountMode);
		Assert.Equal("Li", config.PlayerName);

		config.SelectedAccountId = microsoft.Id;
		AccountService.ApplySelectedToLegacy(config);
		Assert.Equal("microsoft", config.AccountMode);
		Assert.Equal("rt", config.MicrosoftRefreshToken);

		AccountService.RemoveSelected(config);
		Assert.Equal("offline", config.AccountMode);
		Assert.Equal("First", config.PlayerName);
		Assert.Equal(string.Empty, config.MicrosoftRefreshToken);
	}

	[Fact]
	public void EnsureAccounts_IsIdempotentOnAlreadyHealthyConfig()
	{
		AppConfig config = EmptyConfig();
		AccountService.EnsureAccounts(config);
		string selected = config.SelectedAccountId;
		int count = config.Accounts.Count;

		bool changed = AccountService.EnsureAccounts(config);

		Assert.False(changed, "健康配置再跑一次不应报告变化，避免无意义落盘");
		Assert.Equal(count, config.Accounts.Count);
		Assert.Equal(selected, config.SelectedAccountId);
	}
}
