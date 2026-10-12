using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using Pineglow.Launcher.Services;
using Xunit;
using Xunit.Abstractions;

namespace Pineglow.Launcher.Tests;

/// <summary>
/// MR20 补测（缺口登记表 A15）＋ 最终验收阻断项 F-02（R0）修复：**旧配置/凭据不被重置**，
/// 且**测试绝不触碰真实凭据**。
///
/// F-02 实测（独立验收 <c>credential-intents.jsonl</c>）：修复前这 5 个用例会真实进入
/// Windows 凭据管理器 —— 5 次运行共产生 **20 次 delete-intent**（外加 15 次 read-stub）。
/// 原因是 <c>ConfigStore.SaveCore</c> 会把凭据写回 <see cref="SecretStore"/>，而
/// <c>ConfigStore.ConfigPathOverrideForTest</c> 只重定向了 config.json，
/// 原来的 A15 守卫又只看 <c>secrets.json</c>（那不是 Windows 凭据管理器）。
///
/// 本轮修复：<see cref="SecretStore"/> 的读/写/删统一走可注入的 <c>ISecretStoreBackend</c>。
/// 本类每个用例都在**内存后端作用域**内执行（构造函数建立、Dispose 释放），
/// 并在守卫用例中断言"生产（原生）后端调用次数 = 0"。
/// </summary>
public class ConfigPreservationTests : IDisposable
{
	private readonly ITestOutputHelper _out;
	private readonly string _root;

	/// <summary>R0：本用例的凭据作用域（AsyncLocal，按异步流隔离；Dispose 时还原）。</summary>
	private readonly IDisposable _secretScope;

	/// <summary>R0：本用例实际使用的内存后端 —— 用于证明"调用确实被它接住了"。</summary>
	private readonly InMemorySecretStoreBackend _secrets;

	public ConfigPreservationTests(ITestOutputHelper output)
	{
		_out = output;
		_root = Path.Combine(Path.GetTempPath(), "pineglow-a15-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(_root);

		// F-02 修复：先把凭据后端换成内存实现，再让任何 LoadAsync 发生。
		// 没有这一步，SaveCore 会真的调用 CredDeleteW/CredWriteW。
		_secrets = new InMemorySecretStoreBackend("a15-config-preservation");
		_secretScope = SecretStore.OverrideBackendForTest(_secrets);
	}

	public void Dispose()
	{
		_secretScope.Dispose();
		ConfigStore.ConfigPathOverrideForTest = null;
		try { if (Directory.Exists(_root)) Directory.Delete(_root, true); } catch { }
	}

	private string ConfigPath => Path.Combine(_root, "config.json");

	private void WriteConfig(string json)
	{
		File.WriteAllText(ConfigPath, json);
		ConfigStore.ConfigPathOverrideForTest = ConfigPath;
	}

	/* ===================== ① 既有非凭据字段必须原样保留 ===================== */

	[Fact]
	public async Task LoadAsync_PreservesEveryNonSecretField_FromAnExistingConfig()
	{
		WriteConfig("""
		{
		  "gameDirectory": "D:\\synthetic\\game",
		  "updateUrl": "https://example.invalid/custom-update.json",
		  "downloadSource": "bmclapi",
		  "playerName": "PreservedPlayer",
		  "javaPath": "D:\\synthetic\\java\\bin\\javaw.exe",
		  "maxMemoryMb": 6144,
		  "windowWidth": 1600,
		  "windowHeight": 900,
		  "selectedInstanceId": "inst-keep-2",
		  "instances": [
		    { "id": "inst-keep-1", "name": "one", "gameDirectory": "D:\\synthetic\\game", "minecraftVersion": "1.20.4" },
		    { "id": "inst-keep-2", "name": "two", "gameDirectory": "D:\\synthetic\\game", "minecraftVersion": "1.21" }
		  ]
		}
		""");

		Pineglow.Launcher.Models.AppConfig config = await ConfigStore.LoadAsync();

		_out.WriteLine($"[A15] updateUrl={config.UpdateUrl}  downloadSource={config.DownloadSource}  player={config.PlayerName}");
		Assert.Equal(@"D:\synthetic\game", config.GameDirectory);
		Assert.Equal("https://example.invalid/custom-update.json", config.UpdateUrl);
		Assert.Equal("bmclapi", config.DownloadSource);
		Assert.Equal("PreservedPlayer", config.PlayerName);
		Assert.Equal(@"D:\synthetic\java\bin\javaw.exe", config.JavaPath);
		Assert.Equal(6144, config.MaxMemoryMb);
		Assert.Equal(1600, config.WindowWidth);
		Assert.Equal(900, config.WindowHeight);
		Assert.Equal("inst-keep-2", config.SelectedInstanceId);
		Assert.Equal(2, config.Instances.Count);
		Assert.Contains(config.Instances, x => x.Id == "inst-keep-1");
		Assert.Contains(config.Instances, x => x.Id == "inst-keep-2");
	}

	/* ===================== ② 只有"空 updateUrl"才会被补默认值 ===================== */

	[Fact]
	public async Task LoadAsync_FillsUpdateUrl_OnlyWhenItIsBlank()
	{
		WriteConfig("""
		{
		  "gameDirectory": "D:\\synthetic\\game",
		  "updateUrl": "",
		  "instances": [ { "id": "a", "name": "a", "gameDirectory": "D:\\synthetic\\game", "minecraftVersion": "1.20.4" } ]
		}
		""");

		Pineglow.Launcher.Models.AppConfig config = await ConfigStore.LoadAsync();

		Assert.Equal(ConfigStore.DefaultUpdateUrl, config.UpdateUrl);
		_out.WriteLine("[A15] 空 updateUrl 被补为默认值（这是**唯一**被允许的覆盖之一）");
	}

	/* ===================== ③ 已有实例时不得再注入默认实例 ===================== */

	[Fact]
	public async Task LoadAsync_DoesNotInjectADefaultInstance_WhenInstancesExist()
	{
		WriteConfig("""
		{
		  "gameDirectory": "D:\\synthetic\\game",
		  "selectedInstanceId": "only-one",
		  "instances": [ { "id": "only-one", "name": "only", "gameDirectory": "D:\\synthetic\\game", "minecraftVersion": "1.20.4" } ]
		}
		""");

		Pineglow.Launcher.Models.AppConfig config = await ConfigStore.LoadAsync();

		Assert.Single(config.Instances);
		Assert.Equal("only-one", config.Instances[0].Id);
		Assert.Equal("only-one", config.SelectedInstanceId);
	}

	/* ===================== ④ 回写（若发生）也必须保留既有字段 ===================== */

	[Fact]
	public async Task LoadAsync_IfItWritesBack_StillPreservesExistingFields()
	{
		// 第 46 轮实测：**即使没有旧版凭据迁移**，`LoadAsync` 也可能回写 ——
		// 因为 `AccountService.EnsureAccounts` 在"没有账号"时会补一个默认账号，
		// 令迁移标志为真 → 走到 `await SaveAsync(config)`。
		//
		// 因此本用例**不假设"不回写"**，而是断言：**无论回不回写，既有字段都必须原样保留**。
		WriteConfig("""
		{
		  "gameDirectory": "D:\\synthetic\\game",
		  "updateUrl": "https://example.invalid/keep.json",
		  "playerName": "KeepMe",
		  "maxMemoryMb": 3072,
		  "selectedInstanceId": "a",
		  "instances": [ { "id": "a", "name": "a", "gameDirectory": "D:\\synthetic\\game", "minecraftVersion": "1.20.4" } ]
		}
		""");

		Pineglow.Launcher.Models.AppConfig config = await ConfigStore.LoadAsync();
		Assert.Equal("https://example.invalid/keep.json", config.UpdateUrl);
		Assert.Equal("KeepMe", config.PlayerName);
		Assert.Equal(3072, config.MaxMemoryMb);

		// 盘上的内容也必须仍然保留这些字段（允许新增字段，但不允许被重置成默认值）
		// 注意：**`ConfigStore.SaveAsync` 写的是 PascalCase**（`ConfigStore.Options` 只设了
		// `WriteIndented` + `PropertyNameCaseInsensitive`，**没有** CamelCase 策略），
		// 而读入是大小写不敏感的 —— 所以这里必须大小写不敏感地取值。
		using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(ConfigPath));
		JsonElement root = doc.RootElement;
		JsonElement Get(string name)
		{
			foreach (JsonProperty p in root.EnumerateObject())
			{
				if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))
				{
					return p.Value;
				}
			}

			throw new Xunit.Sdk.XunitException($"盘上 JSON 找不到字段 {name}（实际字段：" +
				string.Join(", ", root.EnumerateObject().Select(p => p.Name)) + "）");
		}

		_out.WriteLine($"[A15] 回写后盘上 updateUrl={Get("updateUrl").GetString()}  player={Get("playerName").GetString()}");
		Assert.Equal("https://example.invalid/keep.json", Get("updateUrl").GetString());
		Assert.Equal("KeepMe", Get("playerName").GetString());
		Assert.Equal(3072, Get("maxMemoryMb").GetInt32());
		Assert.False(File.Exists(ConfigPath + ".tmp"), "不应留下 .tmp 临时文件");
	}

	/* ===================== ⑤ 安全守卫：不得碰真实凭据存储 ===================== */

	[Fact]
	public async Task LoadAsync_MustNotCreateOrModifyTheRealCredentialStore()
	{
		// 这条守卫是 F-02 的**直接证明**（修复前它只看 secrets.json，因此在 Windows 上
		// 是一道假守卫 —— 真正的写入目标是 Windows 凭据管理器）。
		//
		// 现在三件事一起断言：
		//   ① 真实 secrets.json 不得被创建/改动；
		//   ② **生产（原生）后端调用次数必须为 0**；
		//   ③ 这些读/删**确实**发生在了本用例注入的内存后端上（否则 ② 就是空话）。
		string realSecrets = Path.Combine(PlatformPaths.GetConfigDirectory(), "secrets.json");
		bool existedBefore = File.Exists(realSecrets);
		DateTime? mtimeBefore = existedBefore ? File.GetLastWriteTimeUtc(realSecrets) : null;

		long nativeBefore = SecretStore.ProductionBackendCallCount;
		int isolatedOpsBefore = _secrets.Operations.Count;

		WriteConfig("""
		{
		  "gameDirectory": "D:\\synthetic\\game",
		  "instances": [ { "id": "a", "name": "a", "gameDirectory": "D:\\synthetic\\game", "minecraftVersion": "1.20.4" } ]
		}
		""");
		await ConfigStore.LoadAsync();

		bool existedAfter = File.Exists(realSecrets);
		long nativeAfter = SecretStore.ProductionBackendCallCount;
		int isolatedOps = _secrets.Operations.Count - isolatedOpsBefore;
		_out.WriteLine($"[A15] 真实 secrets.json：存在 {existedBefore} → {existedAfter}；路径 = {realSecrets}");
		_out.WriteLine($"[A15] 生产后端调用 delta={nativeAfter - nativeBefore}；内存后端受理操作={isolatedOps}；" +
			"其中 delete=" + _secrets.CountOf("delete") + " read=" + _secrets.CountOf("read") + " write=" + _secrets.CountOf("write"));

		Assert.False(existedAfter && !existedBefore,
			"测试**创建了真实凭据存储** —— 这是不可接受的：测试绝不能碰用户凭据。路径：" + realSecrets);
		if (existedBefore && existedAfter)
		{
			Assert.Equal(mtimeBefore, File.GetLastWriteTimeUtc(realSecrets));
		}

		Assert.Equal(0, nativeAfter - nativeBefore);
		Assert.True(isolatedOps > 0, "内存后端一次调用都没有被受理 —— 说明注入没生效，② 的 0 次就成了空断言");
	}
}
