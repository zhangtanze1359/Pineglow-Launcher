using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Pineglow.Launcher.Models;
using Pineglow.Launcher.Services;
using Xunit;
using Xunit.Abstractions;

namespace Pineglow.Launcher.Tests;

/// <summary>
/// MR03 剩余项的补测（缺口登记表 A7）：**"移除后的默认实例补建"** 到底会不会发生。
///
/// MR03 报告 §六 原话：
///   "移除后的默认实例补建 | 观察到一个未验证点：`InstanceService.EnsureDefaultInstance`
///    会在实例数为 0 时补建默认实例，但本包测试路径**未触发它**；'移除最后一个实例后 UI 会看到什么'
///    需要在真实 GUI 验证"
///
/// **本包查清了，并发现一处夹具与生产的分叉**（见 §一、§二）：
///   * 生产路径：`LocalApiService.LoadConfig()` → `ConfigStore.LoadAsync()` → **会调用** `EnsureDefaultInstance`；
///   * 测试夹具：`RouteTestMemoryConfigStore.Load()` 直接返回内存对象，**绕过** `EnsureDefaultInstance`。
///   因此既有用例 `Remove_LastInstance_LeavesEmptyListAndBlankSelection`（断言 count==0）
///   **反映的是夹具行为，不是生产行为**。
/// </summary>
/// <summary>必须串行：本类会启动进程级单例 LocalApiService（见 LocalApiServiceTestCollection 的说明）。</summary>
[Collection("LocalApiService")]
public class InstanceDefaultInstanceBoundaryTests
{
	private readonly ITestOutputHelper _out;

	public InstanceDefaultInstanceBoundaryTests(ITestOutputHelper output) => _out = output;

	/* ===================== 一、纯函数边界（决定生产行为的就是它） ===================== */

	[Fact]
	public void EnsureDefaultInstance_OnEmptyList_CreatesOneAndSelectsIt()
	{
		var config = new AppConfig
		{
			GameDirectory = @"C:\synthetic-game",
			SelectedInstanceId = string.Empty,
			Instances = new List<GameInstance>()
		};

		InstanceService.EnsureDefaultInstance(config);

		Assert.True(config.Instances.Count == 1, "空列表必须补建 1 个默认实例（这正是 MR03 说未触发的那条路径）");
		Assert.True(config.SelectedInstanceId == config.Instances[0].Id, "补建后必须把选中项指向它");
		Assert.True(config.Instances[0].GameDirectory == config.GameDirectory, "默认实例应使用配置里的游戏目录");
		_out.WriteLine($"[A7] 空列表 → 补建 {config.Instances.Count} 个，selected={config.SelectedInstanceId}");
	}

	[Fact]
	public void EnsureDefaultInstance_OnNonEmptyList_DoesNotAddAnother()
	{
		var existing = new GameInstance { Id = "keep-me", Name = "keep", GameDirectory = @"C:\g" };
		var config = new AppConfig
		{
			GameDirectory = @"C:\g",
			SelectedInstanceId = existing.Id,
			Instances = new List<GameInstance> { existing }
		};

		InstanceService.EnsureDefaultInstance(config);

		Assert.True(config.Instances.Count == 1, "非空列表不得补建");
		Assert.True(config.SelectedInstanceId == "keep-me", "非空列表且选中有效时不得改选中");
	}

	/// <summary>
	/// **本包实测出的行为（不是缺陷修复，只钉住现状）**：`EnsureDefaultInstance`
	/// **不会**修复悬空的 `SelectedInstanceId`。
	///
	/// 原因：第 35 行的判断是 `if (GetSelectedInstance(config) == null)`，而
	/// `GetSelectedInstance` 在 id 不匹配时会**兜底返回第一个实例**，因此它永远不为 null
	/// （列表非空时），这个分支不会执行 —— 配置里的 `SelectedInstanceId` 保持悬空。
	///
	/// **具体后果**：`GET /api/instances` 是**原样**返回 `config.SelectedInstanceId` 的
	/// （`LocalApiService` 第 1117 行），因此一个"由别的路径写坏"的配置会让界面收到一个
	/// **指向不存在实例的 id**。注意：删除路径自己会重指向（`HandleDeleteInstanceAsync` 第 1264–1266 行），
	/// 所以该后果只在**其它来源**（手改配置、旧版本写入）下出现。
	///
	/// 是否要把这里改成"顺手修好悬空选中项"，属于**行为决策**，本包不擅自改（已登记为 A17）。
	/// </summary>
	[Fact]
	public void EnsureDefaultInstance_DoesNotRepairDanglingSelection_AndEffectiveSelectionFallsBack()
	{
		var a = new GameInstance { Id = "a", Name = "a", GameDirectory = @"C:\g" };
		var b = new GameInstance { Id = "b", Name = "b", GameDirectory = @"C:\g" };
		var config = new AppConfig
		{
			GameDirectory = @"C:\g",
			SelectedInstanceId = "已被删除的-id",
			Instances = new List<GameInstance> { a, b }
		};

		InstanceService.EnsureDefaultInstance(config);

		Assert.True(config.Instances.Count == 2, "不得因为选中项失效而增删实例");
		_out.WriteLine("[A7] 悬空选中项处理后的 SelectedInstanceId = " + config.SelectedInstanceId);

		// 现状：字段保持悬空
		Assert.True(config.SelectedInstanceId == "已被删除的-id",
			"实测：EnsureDefaultInstance 不修复悬空 SelectedInstanceId（字段保持原样），实际 " + config.SelectedInstanceId);

		// 但"有效选中项"通过兜底仍然可用 —— 这正是它没有立刻炸掉的原因
		GameInstance? effective = InstanceService.GetSelectedInstance(config);
		Assert.NotNull(effective);
		Assert.True(effective!.Id == "a", "GetSelectedInstance 应兜底到第一个实例");
	}

	/* ===================== 二、夹具分叉：同一个动作，两种配置存储给出不同结果 ===================== */

	/// <summary>
	/// **生产语义**的配置存储（用于对照）：Load 时按 `ConfigStore` 的做法调用 `EnsureDefaultInstance`，
	/// 并且真的落盘到**临时目录**（不碰真实 %APPDATA%）。
	/// 它复刻的是"加载路径会补建默认实例"这一条语义，而不是复制整个 ConfigStore。
	/// </summary>
	private sealed class ProductionLikeFileConfigStore : LocalApiService.IConfigStore
	{
		private readonly string _path;
		public AppConfig Current { get; private set; }

		public ProductionLikeFileConfigStore(string path, AppConfig initial)
		{
			_path = path;
			Current = initial;
			Persist();
		}

		public AppConfig Load()
		{
			if (File.Exists(_path))
			{
				string json = File.ReadAllText(_path);
				Current = JsonSerializer.Deserialize<AppConfig>(json) ?? Current;
			}
			// 与 ConfigStore.LoadAsync 一致：加载后补齐默认实例
			InstanceService.EnsureDefaultInstance(Current);
			return Current;
		}

		public void Save(AppConfig config)
		{
			Current = config;
			Persist();
		}

		private void Persist()
		{
			Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
			File.WriteAllText(_path, JsonSerializer.Serialize(Current));
		}
	}

	private sealed class Fixture : IDisposable
	{
		public string Root { get; }
		public string ConfigPath { get; }
		public AppConfig Config { get; }
		public string InstanceId { get; }

		public Fixture()
		{
			Root = Path.Combine(Path.GetTempPath(), "pineglow-a7-" + Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(Path.Combine(Root, "mods"));
			ConfigPath = Path.Combine(Root, "cfg", "config.json");
			InstanceId = "only-one";
			Config = new AppConfig
			{
				GameDirectory = Root,
				SelectedInstanceId = InstanceId,
				Instances = new List<GameInstance>
				{
					new GameInstance { Id = InstanceId, Name = "only", GameDirectory = Root, MinecraftVersion = "1.20.4" }
				}
			};
		}

		public void Dispose()
		{
			try { if (Directory.Exists(Root)) Directory.Delete(Root, true); } catch { }
		}
	}

	private static int ListCount(string body)
	{
		using JsonDocument doc = JsonDocument.Parse(body);
		return doc.RootElement.GetProperty("instances").GetArrayLength();
	}

	private static string SelectedId(string body)
	{
		using JsonDocument doc = JsonDocument.Parse(body);
		return doc.RootElement.GetProperty("selectedInstanceId").GetString() ?? string.Empty;
	}

	[Fact]
	public async Task Remove_LastInstance_BehaviorDiffersBetweenTestHarnessAndProductionLikeStore()
	{
		using var fx = new Fixture();

		// ① 夹具存储（现状）：Load 不补建 → 移除最后一个后列表为 0
		await using (var harness = await LocalApiTestHarness.StartAsync(fx.Config))
		{
			(int delStatus, _) = await harness.SendAsync(HttpMethod.Post, "/api/instances/" + fx.InstanceId + "/delete", new { });
			Assert.Equal(200, delStatus);
			(int listStatus, string listBody) = await harness.SendAsync(HttpMethod.Get, "/api/instances");
			Assert.Equal(200, listStatus);
			int countUnderHarness = ListCount(listBody);
			_out.WriteLine($"[A7] 夹具存储下：count={countUnderHarness} selected='{SelectedId(listBody)}'");
			Assert.True(countUnderHarness == 0, "夹具存储下应为 0（这与既有用例 Remove_LastInstance_… 一致）");
		}

		// ② 生产语义存储：Load 会补建默认实例 → 下一次请求列表里应出现 1 个默认实例
		using var fx2 = new Fixture();
		var store = new ProductionLikeFileConfigStore(fx2.ConfigPath, fx2.Config);
		{
			await using var harness = await LocalApiTestHarness.StartAsync(store, fx2.Config);
			(int delStatus, _) = await harness.SendAsync(HttpMethod.Post, "/api/instances/" + fx2.InstanceId + "/delete", new { });
			Assert.Equal(200, delStatus);

			// 关键：**下一次请求**会重新 LoadConfig，从而触发 EnsureDefaultInstance
			(int listStatus, string listBody) = await harness.SendAsync(HttpMethod.Get, "/api/instances");
			Assert.Equal(200, listStatus);
			int countUnderProduction = ListCount(listBody);
			string selected = SelectedId(listBody);
			_out.WriteLine($"[A7] 生产语义存储下：count={countUnderProduction} selected='{selected}'");

			Assert.True(countUnderProduction == 1,
				$"生产语义下（Load 会补建）移除最后一个实例后，下一次请求应看到 1 个默认实例，实际 {countUnderProduction}");
			Assert.False(string.IsNullOrWhiteSpace(selected), "补建后选中项不应为空");
		}
	}
}
