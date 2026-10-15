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

	/// <summary>
	/// 内置备用清单源（R03）。默认主源是 Gitee 仓库里的固定 update.json，本轮独立复核时它返回 404；
	/// 旧实现只认这一个地址，清单取不到就直接报错——而资产级的 fallbackUrl 救不了这一层，
	/// 因为 fallbackUrl 只有在**已经拿到清单**之后才有意义。
	///
	/// 备用源刻意选 GitHub Release 的固定 asset 直链：
	/// <c>/releases/latest/download/manifest.json</c> 由 release.yml 每次发版上传，
	/// 地址不随 tag 变化，也不需要额外维护仓库文件。
	/// </summary>
	public static readonly string[] DefaultUpdateMirrorUrls = new string[]
	{
		"https://github.com/zhangtanze1359/Pineglow-Launcher/releases/latest/download/manifest.json"
	};

	private static readonly JsonSerializerOptions Options = new JsonSerializerOptions
	{
		WriteIndented = true,
		PropertyNameCaseInsensitive = true
	};

	// 跨进程互斥 + 原子写，避免 GUI/API 多份内存副本写盘互相覆盖
	private static readonly object ConfigGate = new object();
	private const int MaxWriteAttempts = 3;

	/// <summary>
	/// 测试缝（缺口登记表 A15）：把 config.json 的路径重定向到临时目录。
	///
	/// 存在的理由：本类的读/写都走 <see cref="PlatformPaths.GetConfigPath"/>，而那条路径由
	/// 「可执行文件旁的 <c>portable.flag</c>」这个**全局**开关决定 —— 在测试进程里动它会波及并行用例。
	/// 有了这个缝，就可以对**真实的 <see cref="LoadAsync"/> 归一化逻辑**做验证（例如"旧配置不被重置"），
	/// 而完全不碰真实 <c>%APPDATA%</c>。
	///
	/// **只重定向 config.json 本身**：<see cref="SaveAsync"/> 还会写 <c>SecretStore</c>（真实凭据存储），
	/// 因此**不要**在测试里调用 <see cref="SaveAsync"/>（见 ConfigPreservationTests 的说明）。
	/// 生产路径上恒为 <c>null</c>。
	/// </summary>
	internal static string? ConfigPathOverrideForTest { get; set; }

	private static string ResolveConfigPath() =>
		string.IsNullOrWhiteSpace(ConfigPathOverrideForTest) ? PlatformPaths.GetConfigPath() : ConfigPathOverrideForTest!;

	public static async Task<AppConfig> LoadAsync()
	{
		string configPath = ResolveConfigPath();
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
				config.ProxyPassword = SecretStore.Load(SecretStore.ProxyPassword);
				foreach (AiProviderProfile aiProvider in config.AiProviders)
				{
					aiProvider.ApiKey = SecretStore.Load(AiKey(aiProvider.Id));
				}
			}
			catch
			{
				config.MicrosoftRefreshToken = string.Empty;
				config.CurseForgeApiKey = string.Empty;
				foreach (AiProviderProfile aiProvider2 in config.AiProviders)
				{
					aiProvider2.ApiKey = string.Empty;
				}
			}
			// 一次性迁移：旧单套 AI 配置（AiProviderUrl/AiModel + SecretStore "AiApiKey"）→ 默认 profile
			bool flag = false;
			if (config.AiProviders.Count == 0)
			{
				string legacyAiUrl = ReadLegacySecret(legacyDocument.RootElement, "AiProviderUrl");
				if (!string.IsNullOrWhiteSpace(legacyAiUrl))
				{
					string legacyAiModel = ReadLegacySecret(legacyDocument.RootElement, "AiModel");
					AiProviderProfile migrated = new AiProviderProfile
					{
						Id = Guid.NewGuid().ToString("N"),
						Name = "DeepSeek",
						BaseUrl = legacyAiUrl,
						Model = string.IsNullOrWhiteSpace(legacyAiModel) ? "deepseek-chat" : legacyAiModel,
						ApiKey = SecretStore.Load("AiApiKey")
					};
					config.AiProviders.Add(migrated);
					config.SelectedAiProviderId = migrated.Id;
					flag = true;
				}
			}
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
		lock (ConfigGate)
		{
			SaveCore(config);
		}
		await Task.CompletedTask;
	}

	private static void SaveCore(AppConfig config)
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
		SecretStore.Save(SecretStore.ProxyPassword, config.ProxyPassword);
		SecretStore.Save("AiApiKey", string.Empty);
		foreach (AiProviderProfile aiProvider in config.AiProviders)
		{
			SecretStore.Save(AiKey(aiProvider.Id), aiProvider.ApiKey);
		}

		string configPath = ResolveConfigPath();
		string tmp = configPath + ".tmp";
		byte[] payload = JsonSerializer.SerializeToUtf8Bytes(config, Options);
		for (int attempt = 1; attempt <= MaxWriteAttempts; attempt++)
		{
			try
			{
				using (FileStream fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
				{
					fs.Write(payload, 0, payload.Length);
					fs.Flush(flushToDisk: true);
				}
				RotateBackupBeforeWrite(configPath);
				File.Move(tmp, configPath, overwrite: true);
				return;
			}
			catch (IOException) when (attempt < MaxWriteAttempts)
			{
				Thread.Sleep(100 * attempt);
			}
		}
		throw new IOException("配置写入失败（文件被占用）：" + configPath);
	}

	private static void RotateBackupBeforeWrite(string configPath)
	{
		try
		{
			string bak = configPath + ".bak";
			if (File.Exists(bak))
			{
				string old = configPath + ".prev";
				File.Copy(bak, old, true);
			}
			if (File.Exists(configPath))
			{
				File.Copy(configPath, bak, true);
			}
		}
		catch
		{
			// 备份失败不影响主写入
		}
	}

	/// <summary>
	/// 测试缝（缺口登记表 A15 的剩余项）：直接调用**备份轮转**，不经过 <see cref="SaveAsync"/>。
	///
	/// 为什么需要它：轮转本身是**纯文件操作**（现 config → <c>.bak</c>，旧 <c>.bak</c> → <c>.prev</c>），
	/// 但它在 <c>SaveCore</c> 里、而 <c>SaveCore</c> 还会写 <c>SecretStore</c>（**真实凭据存储**，无测试缝）。
	/// 有了这个缝，就能验证"主备切换留证"这一半，而**完全不碰凭据** ✓。
	/// </summary>
	internal static void RotateBackupBeforeWriteForTest(string configPath) => RotateBackupBeforeWrite(configPath);

	private static string AiKey(string id) => "AiKey_" + id;

	private static string ReadLegacySecret(JsonElement root, string propertyName)
	{
		if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty(propertyName, out var value) || value.ValueKind != JsonValueKind.String)
		{
			return string.Empty;
		}
		return value.GetString() ?? string.Empty;
	}
}
