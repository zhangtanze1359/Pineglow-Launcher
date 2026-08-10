using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Pineglow.Launcher.Models;

namespace Pineglow.Launcher.Services;

public static class ConfigStore
{
	/// <summary>
	/// 内置默认自更新源：Gitee 仓库固定 update.json（每次发版由 CI 更新该文件）。
	/// 用户 config 未显式配置 updateUrl 时使用。
	/// </summary>
	public const string DefaultUpdateUrl = "https://gitee.com/ztz1359/pineglow-launcher/raw/master/update.json";

	private static readonly JsonSerializerOptions Options = new JsonSerializerOptions
	{
		WriteIndented = true,
		PropertyNameCaseInsensitive = true
	};

	public static async Task<AppConfig> LoadAsync()
	{
		string configPath = PlatformPaths.GetConfigPath();
		if (!File.Exists(configPath))
		{
			AppConfig obj = new AppConfig
			{
				GameDirectory = PlatformPaths.GetDefaultMinecraftDirectory(),
				UseAutoMemory = true,
				UpdateUrl = DefaultUpdateUrl
			};
			InstanceService.EnsureDefaultInstance(obj);
			AccountService.EnsureAccounts(obj);
			return obj;
		}
		try
		{
			string json = await File.ReadAllTextAsync(configPath);
			AppConfig config = JsonSerializer.Deserialize<AppConfig>(json, Options);
			if (config == null)
			{
				config = new AppConfig
				{
					GameDirectory = PlatformPaths.GetDefaultMinecraftDirectory()
				};
			}
			using JsonDocument legacyDocument = JsonDocument.Parse(json);
			string text = ReadLegacySecret(legacyDocument.RootElement, "MicrosoftRefreshToken");
			string text2 = ReadLegacySecret(legacyDocument.RootElement, "CurseForgeApiKey");
			try
			{
				config.MicrosoftRefreshToken = SecretStore.Load("MicrosoftRefreshToken");
				config.CurseForgeApiKey = SecretStore.Load("CurseForgeApiKey");
			}
			catch
			{
				config.MicrosoftRefreshToken = string.Empty;
				config.CurseForgeApiKey = string.Empty;
			}
			bool flag = false;
			if (string.IsNullOrWhiteSpace(config.MicrosoftRefreshToken) && !string.IsNullOrWhiteSpace(text))
			{
				config.MicrosoftRefreshToken = text;
				flag = true;
			}
			if (string.IsNullOrWhiteSpace(config.CurseForgeApiKey) && !string.IsNullOrWhiteSpace(text2))
			{
				config.CurseForgeApiKey = text2;
				flag = true;
			}
			config.MicrosoftAccessToken = string.Empty;
			config.MicrosoftAccessTokenExpiresAt = DateTimeOffset.MinValue;
			bool flag2 = AccountService.EnsureAccounts(config, config.MicrosoftRefreshToken);
			try
			{
				foreach (LauncherAccount item in config.Accounts.Where((LauncherAccount account) => account.IsMicrosoft || account.IsThirdParty))
				{
					string text3 = SecretStore.Load(SecretStore.AccountToken(item.Id));
					if (item.IsMicrosoft && string.IsNullOrWhiteSpace(text3))
					{
						text3 = SecretStore.Load(SecretStore.MicrosoftAccountToken(item.Id));
						if (!string.IsNullOrWhiteSpace(text3))
						{
							flag = true;
						}
					}
					if (item.IsMicrosoft)
					{
						if (!string.IsNullOrWhiteSpace(text3))
						{
							item.RefreshToken = text3;
						}
						item.AccessToken = string.Empty;
						item.AccessTokenExpiresAt = DateTimeOffset.MinValue;
					}
					else if (item.IsThirdParty)
					{
						item.AccessToken = text3;
					}
				}
			}
			catch
			{
				foreach (LauncherAccount item2 in config.Accounts.Where((LauncherAccount account) => account.IsMicrosoft || account.IsThirdParty))
				{
					item2.AccessToken = string.Empty;
					item2.AccessTokenExpiresAt = DateTimeOffset.MinValue;
				}
			}
			AccountService.ApplySelectedToLegacy(config);
			flag = flag || flag2;
			if (string.IsNullOrWhiteSpace(config.GameDirectory))
			{
				config.GameDirectory = PlatformPaths.GetDefaultMinecraftDirectory();
			}
			InstanceService.EnsureDefaultInstance(config);
			if (string.IsNullOrWhiteSpace(config.UpdateUrl))
			{
				config.UpdateUrl = DefaultUpdateUrl;
			}
			if (flag)
			{
				try
				{
					await SaveAsync(config);
				}
				catch
				{
				}
			}
			return config;
		}
		catch
		{
			AppConfig obj6 = new AppConfig
			{
				GameDirectory = PlatformPaths.GetDefaultMinecraftDirectory(),
				UseAutoMemory = true,
				UpdateUrl = DefaultUpdateUrl
			};
			InstanceService.EnsureDefaultInstance(obj6);
			AccountService.EnsureAccounts(obj6);
			return obj6;
		}
	}

	public static async Task SaveAsync(AppConfig config)
	{
		Directory.CreateDirectory(PlatformPaths.GetConfigDirectory());
		AccountService.ApplySelectedToLegacy(config);
		foreach (LauncherAccount item in config.Accounts.Where((LauncherAccount account) => account.IsMicrosoft || account.IsThirdParty))
		{
			string value = (item.IsMicrosoft ? item.RefreshToken : item.AccessToken);
			SecretStore.Save(SecretStore.AccountToken(item.Id), value);
			SecretStore.Save(SecretStore.MicrosoftAccountToken(item.Id), string.Empty);
		}
		SecretStore.Save("MicrosoftRefreshToken", string.Empty);
		SecretStore.Save("CurseForgeApiKey", config.CurseForgeApiKey);
		await using FileStream stream = File.Create(PlatformPaths.GetConfigPath());
		await JsonSerializer.SerializeAsync((Stream)stream, config, Options, default(CancellationToken));
	}

	private static string ReadLegacySecret(JsonElement root, string propertyName)
	{
		if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty(propertyName, out var value) || value.ValueKind != JsonValueKind.String)
		{
			return string.Empty;
		}
		return value.GetString() ?? string.Empty;
	}
}
