using System;
using System.Collections.Generic;
using System.Linq;
using Pineglow.Launcher.Models;

namespace Pineglow.Launcher.Services;

public static class AccountService
{
	public static bool EnsureAccounts(AppConfig config, string legacyRefreshToken = "")
	{
		bool result = false;
		foreach (LauncherAccount account in config.Accounts)
		{
			if (account.Capes == null)
			{
				List<MinecraftCapeInfo> list = (account.Capes = new List<MinecraftCapeInfo>());
			}
		}
		if (config.Accounts.Count == 0)
		{
			LauncherAccount launcherAccount = new LauncherAccount
			{
				Type = "offline",
				UserName = SafeName(config.PlayerName)
			};
			config.Accounts.Add(launcherAccount);
			if (!string.IsNullOrWhiteSpace(config.MicrosoftUserName) && !string.IsNullOrWhiteSpace(config.MicrosoftUuid))
			{
				LauncherAccount launcherAccount2 = new LauncherAccount
				{
					Type = "microsoft",
					UserName = config.MicrosoftUserName,
					Uuid = config.MicrosoftUuid,
					Xuid = config.MicrosoftXuid,
					ClientId = config.MicrosoftClientId,
					RefreshToken = legacyRefreshToken,
					AccessToken = config.MicrosoftAccessToken,
					AccessTokenExpiresAt = config.MicrosoftAccessTokenExpiresAt
				};
				config.Accounts.Add(launcherAccount2);
				config.SelectedAccountId = (config.AccountMode.Equals("microsoft", StringComparison.OrdinalIgnoreCase) ? launcherAccount2.Id : launcherAccount.Id);
			}
			else
			{
				config.SelectedAccountId = launcherAccount.Id;
			}
			result = true;
		}
		if (GetSelected(config) == null)
		{
			config.SelectedAccountId = config.Accounts[0].Id;
			result = true;
		}
		ApplySelectedToLegacy(config);
		return result;
	}

	public static LauncherAccount? GetSelected(AppConfig config)
	{
		return config.Accounts.FirstOrDefault((LauncherAccount account) => account.Id == config.SelectedAccountId) ?? config.Accounts.FirstOrDefault();
	}

	public static LauncherAccount AddOffline(AppConfig config, string userName)
	{
		string safeName = SafeName(userName);
		LauncherAccount launcherAccount = config.Accounts.FirstOrDefault((LauncherAccount account) => !account.IsMicrosoft && !account.IsThirdParty && account.UserName.Equals(safeName, StringComparison.OrdinalIgnoreCase));
		if (launcherAccount != null)
		{
			Select(config, launcherAccount.Id);
			return launcherAccount;
		}
		LauncherAccount launcherAccount2 = new LauncherAccount
		{
			Type = "offline",
			UserName = safeName
		};
		config.Accounts.Add(launcherAccount2);
		Select(config, launcherAccount2.Id);
		return launcherAccount2;
	}

	public static LauncherAccount UpsertMicrosoft(AppConfig config, MinecraftAccount value, string clientId)
	{
		LauncherAccount launcherAccount = config.Accounts.FirstOrDefault((LauncherAccount item) => item.IsMicrosoft && item.Uuid.Equals(value.Uuid, StringComparison.OrdinalIgnoreCase));
		if (launcherAccount == null)
		{
			launcherAccount = new LauncherAccount
			{
				Type = "microsoft",
				Uuid = value.Uuid
			};
			config.Accounts.Add(launcherAccount);
		}
		launcherAccount.UserName = value.UserName;
		launcherAccount.Uuid = value.Uuid;
		launcherAccount.Xuid = value.Xuid;
		launcherAccount.ClientId = clientId;
		launcherAccount.RefreshToken = value.RefreshToken;
		launcherAccount.AccessToken = value.AccessToken;
		launcherAccount.AccessTokenExpiresAt = value.AccessTokenExpiresAt;
		launcherAccount.SkinUrl = value.SkinUrl;
		launcherAccount.SkinCount = value.SkinCount;
		launcherAccount.CapeCount = value.CapeCount;
		launcherAccount.Capes = value.Capes.Select((MinecraftCapeInfo cape) => new MinecraftCapeInfo
		{
			Id = cape.Id,
			Alias = cape.Alias,
			Url = cape.Url,
			IsActive = cape.IsActive
		}).ToList();
		Select(config, launcherAccount.Id);
		return launcherAccount;
	}

	public static LauncherAccount UpsertThirdParty(AppConfig config, AuthlibAccount value)
	{
		LauncherAccount launcherAccount = config.Accounts.FirstOrDefault((LauncherAccount item) => item.IsThirdParty && item.Uuid.Equals(value.Uuid, StringComparison.OrdinalIgnoreCase) && item.AuthServerUrl.Equals(value.ServerUrl, StringComparison.OrdinalIgnoreCase));
		if (launcherAccount == null)
		{
			launcherAccount = new LauncherAccount
			{
				Type = "authlib"
			};
			config.Accounts.Add(launcherAccount);
		}
		launcherAccount.UserName = value.UserName;
		launcherAccount.Uuid = value.Uuid;
		launcherAccount.AccessToken = value.AccessToken;
		launcherAccount.AuthServerUrl = value.ServerUrl;
		launcherAccount.RefreshToken = string.Empty;
		launcherAccount.Xuid = string.Empty;
		launcherAccount.ClientId = string.Empty;
		launcherAccount.AccessTokenExpiresAt = DateTimeOffset.MinValue;
		Select(config, launcherAccount.Id);
		return launcherAccount;
	}

	public static void Select(AppConfig config, string accountId)
	{
		LauncherAccount launcherAccount = config.Accounts.FirstOrDefault((LauncherAccount item) => item.Id == accountId) ?? throw new InvalidOperationException("选择的账号不存在。");
		config.SelectedAccountId = launcherAccount.Id;
		ApplySelectedToLegacy(config);
	}

	public static void UpdateSelectedOfflineName(AppConfig config, string userName)
	{
		LauncherAccount selected = GetSelected(config);
		if (selected != null && !selected.IsMicrosoft && !selected.IsThirdParty)
		{
			selected.UserName = SafeName(userName);
			ApplySelectedToLegacy(config);
		}
	}

	public static LauncherAccount? RemoveSelected(AppConfig config)
	{
		LauncherAccount selected = GetSelected(config);
		if (selected == null || config.Accounts.Count <= 1)
		{
			return null;
		}
		config.Accounts.Remove(selected);
		config.SelectedAccountId = config.Accounts[0].Id;
		ApplySelectedToLegacy(config);
		return selected;
	}

	public static void ApplySelectedToLegacy(AppConfig config)
	{
		LauncherAccount selected = GetSelected(config);
		if (selected != null)
		{
			config.AccountMode = (selected.IsMicrosoft ? "microsoft" : (selected.IsThirdParty ? "authlib" : "offline"));
			config.PlayerName = selected.UserName;
			if (selected.IsMicrosoft)
			{
				config.MicrosoftClientId = selected.ClientId;
				config.MicrosoftRefreshToken = selected.RefreshToken;
				config.MicrosoftAccessToken = selected.AccessToken;
				config.MicrosoftAccessTokenExpiresAt = selected.AccessTokenExpiresAt;
				config.MicrosoftUserName = selected.UserName;
				config.MicrosoftUuid = selected.Uuid;
				config.MicrosoftXuid = selected.Xuid;
			}
			else
			{
				config.MicrosoftRefreshToken = string.Empty;
				config.MicrosoftAccessToken = string.Empty;
				config.MicrosoftAccessTokenExpiresAt = DateTimeOffset.MinValue;
				config.MicrosoftUserName = string.Empty;
				config.MicrosoftUuid = string.Empty;
				config.MicrosoftXuid = string.Empty;
			}
		}
	}

	private static string SafeName(string? value)
	{
		string text = new string((string.IsNullOrWhiteSpace(value) ? "Player" : value.Trim()).Where((char ch) => char.IsLetterOrDigit(ch) || ch == '_').ToArray());
		if (!string.IsNullOrWhiteSpace(text))
		{
			return text.Substring(0, Math.Min(16, text.Length));
		}
		return "Player";
	}
}
