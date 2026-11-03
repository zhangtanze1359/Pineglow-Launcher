using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using Pineglow.Launcher.Models;
using Pineglow.Launcher.Services;
using Xunit;
using Xunit.Abstractions;

namespace Pineglow.Launcher.Tests;

/// <summary>
/// MR03：实例"从列表移除"的准确语义。
///
/// 被测契约（必须与 UI 文案一致）：
///   1. `POST /api/instances/{id}/delete` **只改配置**，不删除任何游戏文件；
///      游戏目录、存档、Mod、资源包在移除前后逐字节不变。
///   2. 正在进行 Mod 二分定位的实例必须被拒绝移除（否则会留下无主的 Bisect 会话）。
///   3. `POST /api/instances/register` 能把已存在的目录挂回列表，同样零文件写入。
///   4. 共享目录、最后一个实例、目录不可读、重复点击、重复注册都有确定结果。
///
/// 所有样本都在合成临时目录内；真实配置由 <see cref="LocalApiTestHarness"/> 的内存 store
/// 与 RealConfigGuard 双重隔离。
/// </summary>
[Collection("LocalApiService")]
public class InstanceRemovalTests
{
	private readonly ITestOutputHelper _out;

	public InstanceRemovalTests(ITestOutputHelper output) => _out = output;

	/* ===================== 合成夹具 ===================== */

	private sealed class Fixture : IDisposable
	{
		public string Root { get; }
		public string ModsDir => Path.Combine(Root, "mods");
		public List<GameInstance> Instances { get; } = new List<GameInstance>();
		public AppConfig Config { get; }
		/// <summary>构造时固化的 id 列表：后端会就地修改 config.Instances，不能在删除后再去读它。</summary>
		private readonly List<string> _allIds = new List<string>();
		public IReadOnlyList<string> AllIds => _allIds;
		public string FirstInstanceId => _allIds[0];

		public Fixture(int instanceCount = 1)
		{
			Root = Path.Combine(Path.GetTempPath(), "pineglow-inst-removal-" + Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(ModsDir);
			File.WriteAllText(Path.Combine(ModsDir, "a.jar"), "fake mod a");
			File.WriteAllText(Path.Combine(ModsDir, "b.jar"), "fake mod b");
			Directory.CreateDirectory(Path.Combine(Root, "saves", "world1"));
			File.WriteAllText(Path.Combine(Root, "saves", "world1", "level.dat"), "fake level");
			Directory.CreateDirectory(Path.Combine(Root, "resourcepacks"));
			File.WriteAllText(Path.Combine(Root, "resourcepacks", "pack.zip"), "fake pack");

			for (int i = 0; i < instanceCount; i++)
			{
				GameInstance created = new GameInstance
				{
					Id = "inst-" + Guid.NewGuid().ToString("N").Substring(0, 8),
					Name = "instance-" + i,
					GameDirectory = Root,
					MinecraftVersion = "1.20.4"
				};
				Instances.Add(created);
				_allIds.Add(created.Id);
			}

			Config = new AppConfig
			{
				GameDirectory = Root,
				SelectedInstanceId = Instances[0].Id,
				Instances = Instances
			};
		}

		public string Api(string suffix) => "/api/instances" + suffix;

		/// <summary>目录下所有文件（相对路径 → 内容 SHA256），用于证明"零文件改动"。</summary>
		public Dictionary<string, string> SnapshotFiles()
		{
			var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
			if (!Directory.Exists(Root)) return map;
			foreach (string file in Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories))
			{
				using FileStream fs = File.OpenRead(file);
				map[Path.GetRelativePath(Root, file)] = Convert.ToHexString(SHA256.HashData(fs));
			}
			return map;
		}

		public static string DescribeDiff(Dictionary<string, string> before, Dictionary<string, string> after)
		{
			var lines = new List<string>();
			foreach (var kv in before)
			{
				if (!after.TryGetValue(kv.Key, out string? now)) lines.Add("  消失: " + kv.Key);
				else if (!string.Equals(now, kv.Value, StringComparison.Ordinal)) lines.Add("  被改写: " + kv.Key);
			}
			foreach (var kv in after)
			{
				if (!before.ContainsKey(kv.Key)) lines.Add("  新增: " + kv.Key);
			}
			return lines.Count == 0 ? "  （无差异）" : string.Join("\n", lines);
		}

		public void Dispose()
		{
			foreach (string id in _allIds)
			{
				try { ModBisectService.Remove(id); } catch { }
			}
			try { if (Directory.Exists(Root)) Directory.Delete(Root, true); } catch { }
		}
	}

	/* ===================== 断言辅助 ===================== */

	private static string? ExtractError(string body)
	{
		try
		{
			using JsonDocument doc = JsonDocument.Parse(body);
			if (doc.RootElement.ValueKind == JsonValueKind.Object
				&& doc.RootElement.TryGetProperty("error", out JsonElement el)
				&& el.ValueKind == JsonValueKind.String)
			{
				return el.GetString();
			}
		}
		catch { }
		return null;
	}

	private static bool ListContains(string listBody, string instanceId)
	{
		using JsonDocument doc = JsonDocument.Parse(listBody);
		return doc.RootElement.GetProperty("instances").EnumerateArray()
			.Any(x => x.GetProperty("id").GetString() == instanceId);
	}

	private static int ListCount(string listBody)
	{
		using JsonDocument doc = JsonDocument.Parse(listBody);
		return doc.RootElement.GetProperty("instances").GetArrayLength();
	}

	private static string SelectedId(string listBody)
	{
		using JsonDocument doc = JsonDocument.Parse(listBody);
		return doc.RootElement.GetProperty("selectedInstanceId").GetString() ?? string.Empty;
	}

	/* ===================== 1. 移除 = 只改配置，零文件改动 ===================== */

	[Fact]
	public async Task Remove_FromList_KeepsEveryGameFileByteIdentical()
	{
		using var fx = new Fixture();
		await using var h = await LocalApiTestHarness.StartAsync(fx.Config);

		Dictionary<string, string> before = fx.SnapshotFiles();
		Assert.True(before.Count >= 4, "合成夹具应至少有 4 个文件");

		(int status, string body) = await h.SendAsync(HttpMethod.Post, fx.Api("/" + fx.FirstInstanceId + "/delete"), new { });
		_out.WriteLine($"[remove] -> {status} body={body}");

		Assert.Equal(200, status);
		using (JsonDocument doc = JsonDocument.Parse(body))
		{
			Assert.True(doc.RootElement.GetProperty("ok").GetBoolean());
			Assert.True(doc.RootElement.GetProperty("keptFiles").GetBoolean(), "响应必须声明文件被保留");
			Assert.Equal(fx.Root, doc.RootElement.GetProperty("gameDirectory").GetString());
			Assert.True(doc.RootElement.GetProperty("savesCount").GetInt32() >= 1, "移除前应识别到至少 1 个存档");
		}

		// 核心断言：文件系统逐字节没有变化
		Dictionary<string, string> after = fx.SnapshotFiles();
		Assert.True(before.Count == after.Count, "移除实例不得增删任何文件：\n" + Fixture.DescribeDiff(before, after));
		Assert.True(before.All(kv => after.TryGetValue(kv.Key, out string? now) && now == kv.Value),
			"移除实例不得改写任何文件：\n" + Fixture.DescribeDiff(before, after));
		Assert.True(Directory.Exists(Path.Combine(fx.Root, "saves", "world1")), "存档目录必须仍然存在");
		Assert.True(Directory.Exists(fx.ModsDir), "mods 目录必须仍然存在");

		(int listStatus, string listBody) = await h.SendAsync(HttpMethod.Get, "/api/instances");
		Assert.Equal(200, listStatus);
		Assert.False(ListContains(listBody, fx.FirstInstanceId), "移除后列表不应再包含该实例");
		Assert.Equal(string.Empty, SelectedId(listBody));
	}

	/* ===================== 2. 活动状态保护 ===================== */

	[Fact]
	public async Task Remove_RejectedWhileBisectActive_AndRecordStays()
	{
		using var fx = new Fixture();
		await using var h = await LocalApiTestHarness.StartAsync(fx.Config);

		(int startStatus, _) = await h.SendAsync(HttpMethod.Post, fx.Api("/" + fx.FirstInstanceId + "/bisect/start"), new { });
		Assert.Equal(200, startStatus);

		(int status, string body) = await h.SendAsync(HttpMethod.Post, fx.Api("/" + fx.FirstInstanceId + "/delete"), new { });
		_out.WriteLine($"[remove-while-bisect] -> {status} error={ExtractError(body)}");

		Assert.Equal(400, status);
		Assert.Contains("二分定位", ExtractError(body) ?? string.Empty);

		(int listStatus, string listBody) = await h.SendAsync(HttpMethod.Get, "/api/instances");
		Assert.Equal(200, listStatus);
		Assert.True(ListContains(listBody, fx.FirstInstanceId), "被拒绝时不能移除列表记录");

		await h.SendAsync(HttpMethod.Post, fx.Api("/" + fx.FirstInstanceId + "/bisect/giveup"), new { });
	}

	/* ===================== 3. 重新挂回 ===================== */

	[Fact]
	public async Task Register_ReAddsExistingDirectory_WithoutTouchingFiles()
	{
		using var fx = new Fixture();
		await using var h = await LocalApiTestHarness.StartAsync(fx.Config);

		(int delStatus, _) = await h.SendAsync(HttpMethod.Post, fx.Api("/" + fx.FirstInstanceId + "/delete"), new { });
		Assert.Equal(200, delStatus);
		Dictionary<string, string> beforeRegister = fx.SnapshotFiles();

		(int status, string body) = await h.SendAsync(HttpMethod.Post, fx.Api("/register"),
			new { gameDirectory = fx.Root, name = "挂回的实例" });
		_out.WriteLine($"[register] -> {status} body={body}");

		Assert.Equal(200, status);
		string newId;
		using (JsonDocument doc = JsonDocument.Parse(body))
		{
			Assert.True(doc.RootElement.GetProperty("ok").GetBoolean());
			Assert.True(doc.RootElement.GetProperty("registeredExistingDirectory").GetBoolean());
			JsonElement instance = doc.RootElement.GetProperty("instance");
			Assert.Equal("挂回的实例", instance.GetProperty("name").GetString());
			Assert.True(instance.GetProperty("savesCount").GetInt32() >= 1, "挂回后应重新看到原有存档");
			Assert.True(instance.GetProperty("modsCount").GetInt32() >= 2, "挂回后应重新看到原有 Mod");
			newId = instance.GetProperty("id").GetString()!;
		}

		Dictionary<string, string> after = fx.SnapshotFiles();
		Assert.True(beforeRegister.Count == after.Count && beforeRegister.All(kv => after[kv.Key] == kv.Value),
			"挂回目录不得产生任何文件改动：\n" + Fixture.DescribeDiff(beforeRegister, after));

		(int listStatus, string listBody) = await h.SendAsync(HttpMethod.Get, "/api/instances");
		Assert.Equal(200, listStatus);
		Assert.True(ListContains(listBody, newId), "挂回后列表应包含新记录");
	}

	/* ===================== 4. 挂回的失败路径 ===================== */

	[Fact]
	public async Task Register_RejectsMissingDirectory_AndDuplicateEntry()
	{
		using var fx = new Fixture();
		await using var h = await LocalApiTestHarness.StartAsync(fx.Config);

		string missing = Path.Combine(fx.Root, "does-not-exist");
		(int s1, string b1) = await h.SendAsync(HttpMethod.Post, fx.Api("/register"), new { gameDirectory = missing });
		_out.WriteLine($"[register-missing] -> {s1} error={ExtractError(b1)}");
		Assert.Equal(400, s1);
		Assert.Contains("不存在", ExtractError(b1) ?? string.Empty);

		(int s2, string b2) = await h.SendAsync(HttpMethod.Post, fx.Api("/register"), new { gameDirectory = "" });
		Assert.Equal(400, s2);
		Assert.Contains("请填写", ExtractError(b2) ?? string.Empty);

		(int s3, string b3) = await h.SendAsync(HttpMethod.Post, fx.Api("/register"), new { gameDirectory = fx.Root });
		_out.WriteLine($"[register-duplicate] -> {s3} error={ExtractError(b3)}");
		Assert.Equal(400, s3);
		Assert.Contains("已在实例列表", ExtractError(b3) ?? string.Empty);

		(int listStatus, string listBody) = await h.SendAsync(HttpMethod.Get, "/api/instances");
		Assert.Equal(200, listStatus);
		Assert.Equal(1, ListCount(listBody));
	}

	/* ===================== 5. 共享目录 ===================== */

	[Fact]
	public async Task Remove_SharedDirectory_ReportsSharing_AndOtherInstanceSurvives()
	{
		using var fx = new Fixture(instanceCount: 2);
		string otherId = fx.AllIds[1];

		await using var h = await LocalApiTestHarness.StartAsync(fx.Config);

		(int detailStatus, string detailBody) = await h.SendAsync(HttpMethod.Get, fx.Api("/" + fx.FirstInstanceId));
		Assert.Equal(200, detailStatus);
		using (JsonDocument detail = JsonDocument.Parse(detailBody))
		{
			Assert.True(detail.RootElement.GetProperty("instance").GetProperty("sharedGameDirectory").GetBoolean(),
				"共用同一游戏目录时，详情必须标出 sharedGameDirectory=true，供 UI 提示");
		}

		Dictionary<string, string> before = fx.SnapshotFiles();
		(int status, string body) = await h.SendAsync(HttpMethod.Post, fx.Api("/" + fx.FirstInstanceId + "/delete"), new { });
		_out.WriteLine($"[remove-shared] -> {status} body={body}");

		Assert.Equal(200, status);
		using (JsonDocument doc = JsonDocument.Parse(body))
		{
			Assert.True(doc.RootElement.GetProperty("sharedDirectory").GetBoolean(), "共享目录必须如实回报");
		}

		Dictionary<string, string> after = fx.SnapshotFiles();
		Assert.True(before.Count == after.Count && before.All(kv => after[kv.Key] == kv.Value),
			"移除共享目录的实例不得改动任何文件：\n" + Fixture.DescribeDiff(before, after));

		(int listStatus, string listBody) = await h.SendAsync(HttpMethod.Get, "/api/instances");
		Assert.Equal(200, listStatus);
		Assert.True(ListContains(listBody, otherId), "另一个共享目录的实例必须原样保留");
		Assert.Equal(otherId, SelectedId(listBody));
	}

	/* ===================== 6. 最后一个实例 ===================== */

	[Fact]
	public async Task Remove_LastInstance_LeavesEmptyListAndBlankSelection()
	{
		using var fx = new Fixture();
		await using var h = await LocalApiTestHarness.StartAsync(fx.Config);

		(int status, _) = await h.SendAsync(HttpMethod.Post, fx.Api("/" + fx.FirstInstanceId + "/delete"), new { });
		Assert.Equal(200, status);

		(int listStatus, string listBody) = await h.SendAsync(HttpMethod.Get, "/api/instances");
		Assert.Equal(200, listStatus);
		Assert.Equal(0, ListCount(listBody));
		Assert.Equal(string.Empty, SelectedId(listBody));
	}

	/* ===================== 7. 目录不可读 / 重复点击 ===================== */

	[Fact]
	public async Task Remove_WithMissingGameDirectory_Succeeds_AndSecondCallReturns400()
	{
		using var fx = new Fixture();
		foreach (GameInstance i in fx.Instances)
		{
			i.GameDirectory = Path.Combine(fx.Root, "never-created");
		}

		await using var h = await LocalApiTestHarness.StartAsync(fx.Config);

		(int s1, string b1) = await h.SendAsync(HttpMethod.Post, fx.Api("/" + fx.FirstInstanceId + "/delete"), new { });
		_out.WriteLine($"[remove-missing-dir] -> {s1} body={b1}");
		Assert.Equal(200, s1);
		using (JsonDocument doc = JsonDocument.Parse(b1))
		{
			Assert.Equal(0, doc.RootElement.GetProperty("savesCount").GetInt32());
		}

		(int s2, string b2) = await h.SendAsync(HttpMethod.Post, fx.Api("/" + fx.FirstInstanceId + "/delete"), new { });
		Assert.Equal(400, s2);
		Assert.Contains("实例不存在", ExtractError(b2) ?? string.Empty);
	}
}
