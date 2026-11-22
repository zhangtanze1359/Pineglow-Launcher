using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Pineglow.Launcher.Models;
using Pineglow.Launcher.Services;
using Xunit;

namespace Pineglow.Launcher.Tests;

/// <summary>真实 HTTP 入口必须接通迁移，且迁移结果在重新加载后仍有效。</summary>
[Collection("LocalApiService")]
public sealed class DownloadSourceHttpMigrationTests
{
	[Theory]
	[InlineData("mcbbs", "bmclapi", 1)]
	[InlineData("MCBBS", "bmclapi", 1)]
	[InlineData(" mcbbs ", "bmclapi", 1)]
	[InlineData("official", "official", 0)]
	[InlineData("bmclapi", "bmclapi", 0)]
	[InlineData("Official", "official", 1)]
	[InlineData("BMCLAPI", "bmclapi", 1)]
	[InlineData(null, "official", 1)]
	[InlineData("", "official", 1)]
	[InlineData("   ", "official", 1)]
	public async Task State_MigratesSourceOnce_AndSurvivesReload(string? original, string expected, int expectedWrites)
	{
		string root = Path.Combine(Path.GetTempPath(), "pg-source-http-" + Guid.NewGuid().ToString("N"));
		string previousSource = MirrorHelper.SelectedKey;
		Directory.CreateDirectory(root);
		try
		{
			var initial = new AppConfig { GameDirectory = root, DownloadSource = original! };
			var store = new RecordingFileStore(Path.Combine(root, "config.json"), initial);
			await using (LocalApiTestHarness harness = await LocalApiTestHarness.StartAsync(store, initial))
			{
				await AssertStateAsync(harness, expected);
				Assert.Equal(expectedWrites, store.ConfigOnlySaveCount);
				Assert.Equal(0, store.SaveCount);
				Assert.Equal(expected, store.Load().DownloadSource);
				await AssertStateAsync(harness, expected);
				Assert.Equal(expectedWrites, store.ConfigOnlySaveCount);
				Assert.Equal(0, store.SaveCount);
			}

			// 新 store 从实际写回的 JSON 重载，不复用原 AppConfig 引用。
			var reloaded = new RecordingFileStore(store.Path);
			await using (LocalApiTestHarness harness = await LocalApiTestHarness.StartAsync(reloaded, reloaded.Load()))
			{
				await AssertStateAsync(harness, expected);
				Assert.Equal(0, reloaded.ConfigOnlySaveCount);
				Assert.Equal(0, reloaded.SaveCount);
			}
		}
		finally
		{
			MirrorHelper.SelectedKey = previousSource;
			Directory.Delete(root, recursive: true);
		}
	}

	/// <summary>
	/// 并发读取探针：迁移必须与读取在同一把 ConfigGate 锁内完成，
	/// 否则多个 /api/state 会各自看到旧值、各写一次（重复写回）。
	/// </summary>
	[Fact]
	public async Task State_ConcurrentReads_WriteBackOnlyOnce()
	{
		string root = Path.Combine(Path.GetTempPath(), "pg-source-http-conc-" + Guid.NewGuid().ToString("N"));
		string previousSource = MirrorHelper.SelectedKey;
		Directory.CreateDirectory(root);
		try
		{
			var initial = new AppConfig { GameDirectory = root, DownloadSource = "mcbbs" };
			var store = new RecordingFileStore(Path.Combine(root, "config.json"), initial);
			await using (LocalApiTestHarness harness = await LocalApiTestHarness.StartAsync(store, initial))
			{
				Task[] reads = Enumerable.Range(0, 16)
					.Select(_ => AssertStateAsync(harness, "bmclapi"))
					.ToArray();
				await Task.WhenAll(reads);
				Assert.Equal(1, store.ConfigOnlySaveCount);
				Assert.Equal(0, store.SaveCount);
				Assert.Equal("bmclapi", store.Load().DownloadSource);
			}
		}
		finally
		{
			MirrorHelper.SelectedKey = previousSource;
			Directory.Delete(root, recursive: true);
		}
	}

	/// <summary>
	/// 写回失败探针：迁移写盘失败只影响持久化，不得把原本可用的读取入口变成 500。
	/// 磁盘保持旧值，本次响应与实际生效源仍必须是规范化后的 bmclapi。
	/// </summary>
	[Fact]
	public async Task State_WriteBackFailure_KeepsReadPathWorking()
	{
		string root = Path.Combine(Path.GetTempPath(), "pg-source-http-ro-" + Guid.NewGuid().ToString("N"));
		string previousSource = MirrorHelper.SelectedKey;
		Directory.CreateDirectory(root);
		try
		{
			var initial = new AppConfig { GameDirectory = root, DownloadSource = "mcbbs" };
			var store = new ThrowingSaveStore(Path.Combine(root, "config.json"), initial);
			await using (LocalApiTestHarness harness = await LocalApiTestHarness.StartAsync(store, initial))
			{
				await AssertStateAsync(harness, "bmclapi");
				Assert.Equal(1, store.SaveAttempts);
			}

			// 写回失败后磁盘上仍是旧值；读取路径已按新值工作，后续配置保存会再落一次。
			Assert.Equal("mcbbs", new RecordingFileStore(store.Path).Load().DownloadSource);
		}
		finally
		{
			MirrorHelper.SelectedKey = previousSource;
			Directory.Delete(root, recursive: true);
		}
	}

	/// <summary>
	/// 生产存储回归（2026-11-22 独立复验阻断项）：任一 AI 凭据读取失败时，ConfigStore.LoadAsync
	/// 会清空内存里的 CurseForge 密钥；旧下载源迁移若走全量 Save，SaveCore 会把空值写回
	/// SecretStore（空值 = 删除），误删与迁移无关的凭据。迁移必须走 SaveConfigOnly。
	/// 末尾用同一对象走一次全量 Save 作反证，确认这条断言不是空洞的。
	/// </summary>
	[Fact]
	public void ProductionStore_ConfigOnlyWrite_PreservesUnrelatedCredential()
	{
		string root = Path.Combine(Path.GetTempPath(), "pg-source-prod-" + Guid.NewGuid().ToString("N"));
		string? previousPath = ConfigStore.ConfigPathOverrideForTest;
		Directory.CreateDirectory(root);
		ConfigStore.ConfigPathOverrideForTest = Path.Combine(root, "config.json");
		var backend = new FailingAiReadSecretBackend();
		backend.Seed(SecretStore.CurseForgeApiKey, "synthetic-curseforge");
		using IDisposable scope = SecretStore.OverrideBackendForTest(backend);
		try
		{
			var initial = new AppConfig
			{
				GameDirectory = root,
				DownloadSource = "mcbbs",
				AiProviders = new List<AiProviderProfile> { new AiProviderProfile { Id = "probe" } }
			};
			InstanceService.EnsureDefaultInstance(initial);
			AccountService.EnsureAccounts(initial);
			File.WriteAllText(ConfigStore.ConfigPathOverrideForTest!, JsonSerializer.Serialize(initial));

			var store = new ProductionConfigStore();
			AppConfig config = store.Load();
			Assert.True(backend.AiReadAttempts >= 1, "注入的 AI 凭据读取失败没有发生，测试前提不成立");
			Assert.Equal(string.Empty, config.CurseForgeApiKey); // 生产读路径的既有行为
			Assert.True(backend.Contains(SecretStore.CurseForgeApiKey));

			int operationsBefore = backend.Operations.Count;
			config.DownloadSource = MirrorHelper.Normalize(config.DownloadSource);
			store.SaveConfigOnly(config);

			Assert.Equal(operationsBefore, backend.Operations.Count); // 迁移写回完全不碰凭据后端
			Assert.True(backend.Contains(SecretStore.CurseForgeApiKey), "迁移写回删除了无关凭据");
			Assert.Equal("bmclapi", JsonSerializer.Deserialize<AppConfig>(
				File.ReadAllText(ConfigStore.ConfigPathOverrideForTest!))!.DownloadSource);

			// 反证：同一内存对象走全量 Save 才会删除凭据 —— 证明上面的断言确实守住了问题。
			store.Save(config);
			Assert.False(backend.Contains(SecretStore.CurseForgeApiKey));
			Assert.Equal(1, backend.DeleteCount(SecretStore.CurseForgeApiKey));
		}
		finally
		{
			ConfigStore.ConfigPathOverrideForTest = previousPath;
			Directory.Delete(root, recursive: true);
		}
	}

	/// <summary>
	/// 第二处生产回归（2026-11-22 第二轮独立复验）：旧 JSON 里只存在明文 CurseForge 密钥，
	/// 转存到 SecretStore 失败时，主配置必须保留该字段供后续重试；迁移的整包写回不得把它抹掉。
	/// 后端恢复后，下一次 Load 应完成转存，随后 config-only 写回持久化下载源。
	/// </summary>
	[Fact]
	public void ProductionStore_LegacyTransferFailure_KeepsConfigForRetry()
	{
		string root = Path.Combine(Path.GetTempPath(), "pg-source-legacy-" + Guid.NewGuid().ToString("N"));
		string configPath = Path.Combine(root, "config.json");
		string? previousPath = ConfigStore.ConfigPathOverrideForTest;
		ISecretStoreBackend previousBackend = SecretStore.CurrentBackend;
		var backend = new ToggleWriteSecretBackend(failCurseForgeWrites: true);
		Directory.CreateDirectory(root);
		ConfigStore.ConfigPathOverrideForTest = configPath;
		SecretStore.InstallProductionBackendForTest(backend);
		try
		{
			var initial = new AppConfig { GameDirectory = root, DownloadSource = "mcbbs" };
			InstanceService.EnsureDefaultInstance(initial);
			AccountService.EnsureAccounts(initial);
			WriteLegacyConfig(configPath, initial, "legacy-curseforge");

			var store = new ProductionConfigStore();
			AppConfig first = store.Load();
			Assert.True(first.SecretMigrationPending, "旧密钥转存失败没有置位待转存标志");
			Assert.Equal("legacy-curseforge", first.CurseForgeApiKey);
			Assert.True(ConfigFileHasLegacyKey(configPath, "legacy-curseforge"));
			Assert.False(backend.Contains(SecretStore.CurseForgeApiKey));

			first.DownloadSource = MirrorHelper.Normalize(first.DownloadSource);
			store.SaveConfigOnly(first);

			// 待转存期间不得整包重写：旧字段和旧下载源都还在。
			Assert.True(ConfigFileHasLegacyKey(configPath, "legacy-curseforge"));
			Assert.Equal("mcbbs", ReadDiskSource(configPath));

			// 后端恢复：下一次 Load 完成转存，随后 config-only 写回持久化 bmclapi。
			backend.FailCurseForgeWrites = false;
			AppConfig second = store.Load();
			Assert.False(second.SecretMigrationPending);
			Assert.True(backend.Contains(SecretStore.CurseForgeApiKey));
			second.DownloadSource = MirrorHelper.Normalize(second.DownloadSource);
			store.SaveConfigOnly(second);
			Assert.Equal("bmclapi", ReadDiskSource(configPath));
			Assert.False(ConfigFileHasLegacyKey(configPath, "legacy-curseforge"));
		}
		finally
		{
			SecretStore.InstallProductionBackendForTest(previousBackend);
			ConfigStore.ConfigPathOverrideForTest = previousPath;
			Directory.Delete(root, recursive: true);
		}
	}

	/// <summary>
	/// 同一回归的真实 HTTP 入口：首次 /api/state 转存失败时，响应仍按 bmclapi 生效，
	/// 但主配置保持原样；后端恢复后第二次请求完成转存并落盘 bmclapi。
	/// </summary>
	[Fact]
	public async Task State_LegacyCredentialTransferFailure_PreservesConfigAndRetries()
	{
		string root = Path.Combine(Path.GetTempPath(), "pg-source-legacy-http-" + Guid.NewGuid().ToString("N"));
		string configPath = Path.Combine(root, "config.json");
		string previousSource = MirrorHelper.SelectedKey;
		string? previousPath = ConfigStore.ConfigPathOverrideForTest;
		ISecretStoreBackend previousBackend = SecretStore.CurrentBackend;
		var backend = new ToggleWriteSecretBackend(failCurseForgeWrites: true);
		Directory.CreateDirectory(root);
		ConfigStore.ConfigPathOverrideForTest = configPath;
		SecretStore.InstallProductionBackendForTest(backend);
		try
		{
			var initial = new AppConfig { GameDirectory = root, DownloadSource = "mcbbs" };
			InstanceService.EnsureDefaultInstance(initial);
			AccountService.EnsureAccounts(initial);
			WriteLegacyConfig(configPath, initial, "legacy-curseforge");
			var store = new ProductionConfigStore();
			await using (LocalApiTestHarness harness = await LocalApiTestHarness.StartAsync(store, initial))
			{
				await AssertStateAsync(harness, "bmclapi");
				Assert.True(ConfigFileHasLegacyKey(configPath, "legacy-curseforge"));
				Assert.Equal("mcbbs", ReadDiskSource(configPath));
				Assert.False(backend.Contains(SecretStore.CurseForgeApiKey));

				backend.FailCurseForgeWrites = false;
				await AssertStateAsync(harness, "bmclapi");
				Assert.True(backend.Contains(SecretStore.CurseForgeApiKey));
				Assert.Equal("bmclapi", ReadDiskSource(configPath));
				Assert.False(ConfigFileHasLegacyKey(configPath, "legacy-curseforge"));
			}
		}
		finally
		{
			SecretStore.InstallProductionBackendForTest(previousBackend);
			ConfigStore.ConfigPathOverrideForTest = previousPath;
			MirrorHelper.SelectedKey = previousSource;
			Directory.Delete(root, recursive: true);
		}
	}

	/// <summary>
	/// 第三处生产回归（2026-11-22 第三轮独立复验）：缺少账户列表时 EnsureAccounts 会触发全量保存；
	/// 若该保存因无关凭据操作失败，不得把“旧凭据待转存”标志置位，否则下载源迁移会被误拦住。
	/// 配置里没有旧明文凭据，迁移应照常落盘 bmclapi。
	/// </summary>
	[Fact]
	public void ProductionStore_AccountOnlySaveFailure_DoesNotBlockMigration()
	{
		string root = Path.Combine(Path.GetTempPath(), "pg-source-account-" + Guid.NewGuid().ToString("N"));
		string configPath = Path.Combine(root, "config.json");
		string? previousPath = ConfigStore.ConfigPathOverrideForTest;
		ISecretStoreBackend previousBackend = SecretStore.CurrentBackend;
		var backend = new FailingAiDeleteSecretBackend();
		Directory.CreateDirectory(root);
		ConfigStore.ConfigPathOverrideForTest = configPath;
		SecretStore.InstallProductionBackendForTest(backend);
		try
		{
			var initial = new AppConfig
			{
				GameDirectory = root,
				DownloadSource = "mcbbs",
				AiProviders = new List<AiProviderProfile> { new AiProviderProfile { Id = "probe" } }
			};
			InstanceService.EnsureDefaultInstance(initial);
			// 不调用 EnsureAccounts：让 LoadAsync 的账户补全触发 flag2 / 全量保存。
			File.WriteAllText(configPath, JsonSerializer.Serialize(initial));

			var store = new ProductionConfigStore();
			AppConfig config = store.Load();
			Assert.Equal(1, backend.AiDeleteAttempts); // 无关的保存故障确实发生
			Assert.Equal(1, config.Accounts.Count);    // 账户补全确实发生
			Assert.False(config.SecretMigrationPending, "没有旧明文凭据，却置位了待转存标志");

			config.DownloadSource = MirrorHelper.Normalize(config.DownloadSource);
			store.SaveConfigOnly(config);
			Assert.Equal("bmclapi", ReadDiskSource(configPath));
		}
		finally
		{
			SecretStore.InstallProductionBackendForTest(previousBackend);
			ConfigStore.ConfigPathOverrideForTest = previousPath;
			Directory.Delete(root, recursive: true);
		}
	}

	/// <summary>同一场景的真实 HTTP 入口：账户补全的无关保存失败不得阻止下载源落盘。</summary>
	[Fact]
	public async Task State_AccountOnlySaveFailure_StillPersistsDownloadSource()
	{
		string root = Path.Combine(Path.GetTempPath(), "pg-source-account-http-" + Guid.NewGuid().ToString("N"));
		string configPath = Path.Combine(root, "config.json");
		string previousSource = MirrorHelper.SelectedKey;
		string? previousPath = ConfigStore.ConfigPathOverrideForTest;
		ISecretStoreBackend previousBackend = SecretStore.CurrentBackend;
		var backend = new FailingAiDeleteSecretBackend();
		Directory.CreateDirectory(root);
		ConfigStore.ConfigPathOverrideForTest = configPath;
		SecretStore.InstallProductionBackendForTest(backend);
		try
		{
			var initial = new AppConfig
			{
				GameDirectory = root,
				DownloadSource = "mcbbs",
				AiProviders = new List<AiProviderProfile> { new AiProviderProfile { Id = "probe" } }
			};
			InstanceService.EnsureDefaultInstance(initial);
			File.WriteAllText(configPath, JsonSerializer.Serialize(initial));
			var store = new ProductionConfigStore();
			await using (LocalApiTestHarness harness = await LocalApiTestHarness.StartAsync(store, initial))
			{
				await AssertStateAsync(harness, "bmclapi");
				Assert.Equal(1, backend.AiDeleteAttempts);
				Assert.Equal("bmclapi", ReadDiskSource(configPath));

				await AssertStateAsync(harness, "bmclapi");
				Assert.Equal(1, backend.AiDeleteAttempts); // 账户已补全，第二次不再触发全量保存
			}
		}
		finally
		{
			SecretStore.InstallProductionBackendForTest(previousBackend);
			ConfigStore.ConfigPathOverrideForTest = previousPath;
			MirrorHelper.SelectedKey = previousSource;
			Directory.Delete(root, recursive: true);
		}
	}

	private static void WriteLegacyConfig(string path, AppConfig config, string legacyCurseForgeKey)
	{
		JsonNode node = JsonNode.Parse(JsonSerializer.Serialize(config))!.AsObject();
		node["CurseForgeApiKey"] = legacyCurseForgeKey;
		File.WriteAllText(path, node.ToJsonString());
	}

	private static bool ConfigFileHasLegacyKey(string path, string expected)
	{
		using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
		return document.RootElement.TryGetProperty("CurseForgeApiKey", out JsonElement element)
			&& element.GetString() == expected;
	}

	private static string ReadDiskSource(string path) =>
		JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(path))!.DownloadSource;

	private static async Task AssertStateAsync(LocalApiTestHarness harness, string expected)
	{
		(int status, string body) = await harness.SendAsync(HttpMethod.Get, "/api/state");
		Assert.Equal(200, status);
		using JsonDocument document = JsonDocument.Parse(body);
		Assert.Equal(expected, document.RootElement.GetProperty("config").GetProperty("downloadSource").GetString());
		Assert.Equal(expected, MirrorHelper.SelectedKey);
		const string url = "https://piston-meta.mojang.com/mc/game/version_manifest_v2.json";
		Assert.Equal(expected == "bmclapi"
			? "https://bmclapi2.bangbang93.com/mc/game/version_manifest_v2.json"
			: url, MirrorHelper.Rewrite(url));
	}

	private sealed class RecordingFileStore : IConfigStore
	{
		public string Path { get; }
		public int SaveCount { get; private set; }
		public int ConfigOnlySaveCount { get; private set; }

		public RecordingFileStore(string path) { Path = path; }
		public RecordingFileStore(string path, AppConfig initial) : this(path)
		{
			File.WriteAllText(Path, JsonSerializer.Serialize(initial));
		}

		public AppConfig Load() => JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(Path))
			?? throw new InvalidOperationException("隔离测试配置为空");

		public void Save(AppConfig config)
		{
			Write(config);
			SaveCount++;
		}

		public void SaveConfigOnly(AppConfig config)
		{
			Write(config);
			ConfigOnlySaveCount++;
		}

		private void Write(AppConfig config) => File.WriteAllText(Path, JsonSerializer.Serialize(config));
	}

	private sealed class ThrowingSaveStore : IConfigStore
	{
		public string Path { get; }
		public int SaveAttempts { get; private set; }

		public ThrowingSaveStore(string path, AppConfig initial)
		{
			Path = path;
			File.WriteAllText(Path, JsonSerializer.Serialize(initial));
		}

		public AppConfig Load() => JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(Path))
			?? throw new InvalidOperationException("隔离测试配置为空");

		public void Save(AppConfig config)
		{
			SaveAttempts++;
			throw new IOException("模拟写回失败（只读磁盘）");
		}

		public void SaveConfigOnly(AppConfig config)
		{
			SaveAttempts++;
			throw new IOException("模拟写回失败（只读磁盘）");
		}
	}

	/// <summary>合成凭据后端：读取 AiKey_probe 时抛错，并记录 CurseForge 的删除次数。</summary>
	private sealed class FailingAiReadSecretBackend : ISecretStoreBackend
	{
		private readonly InMemorySecretStoreBackend _inner = new("open02-production-regression");

		public int AiReadAttempts { get; private set; }

		public void Seed(string name, string value) => _inner.Write(name, value);
		public bool Contains(string name) => _inner.Contains(name);
		public int DeleteCount(string name) =>
			_inner.DeletedNames.Count(n => string.Equals(n, name, StringComparison.Ordinal));
		public IReadOnlyList<string> Operations => _inner.Operations;

		public string? Read(string name)
		{
			if (name == "AiKey_probe")
			{
				AiReadAttempts++;
				throw new IOException("合成 AI 凭据读取失败");
			}
			return _inner.Read(name);
		}

		public void Write(string name, string value) => _inner.Write(name, value);
		public void Delete(string name) => _inner.Delete(name);
	}

	/// <summary>合成凭据后端：拒绝旧 AI 凭据删除，用于模拟账户补全触发的无关保存失败。</summary>
	private sealed class FailingAiDeleteSecretBackend : ISecretStoreBackend
	{
		private readonly InMemorySecretStoreBackend _inner = new("open02-account-only-fault");

		public int AiDeleteAttempts { get; private set; }

		public bool Contains(string name) => _inner.Contains(name);
		public string? Read(string name) => _inner.Read(name);
		public void Write(string name, string value) => _inner.Write(name, value);

		public void Delete(string name)
		{
			if (name == "AiApiKey")
			{
				AiDeleteAttempts++;
				throw new IOException("合成旧 AI 凭据删除失败");
			}
			_inner.Delete(name);
		}
	}

	/// <summary>合成凭据后端：可按需拒绝 CurseForge 写入，用于模拟转存失败/恢复。</summary>
	private sealed class ToggleWriteSecretBackend : ISecretStoreBackend
	{
		private readonly InMemorySecretStoreBackend _inner = new("open02-legacy-transfer");

		public ToggleWriteSecretBackend(bool failCurseForgeWrites) => FailCurseForgeWrites = failCurseForgeWrites;

		public bool FailCurseForgeWrites { get; set; }

		public bool Contains(string name) => _inner.Contains(name);
		public string? Read(string name) => _inner.Read(name);

		public void Write(string name, string value)
		{
			if (FailCurseForgeWrites && name == SecretStore.CurseForgeApiKey)
			{
				throw new IOException("合成凭据写入失败");
			}
			_inner.Write(name, value);
		}

		public void Delete(string name) => _inner.Delete(name);
	}
}
