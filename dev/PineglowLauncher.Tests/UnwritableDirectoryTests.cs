using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using Pineglow.Launcher.Models;
using Xunit;
using Xunit.Abstractions;

namespace Pineglow.Launcher.Tests;

/// <summary>
/// MR21 补测（缺口登记表 A12 的剩余项）：**目录不可写 / 路径无效时的行为**。
///
/// MR21 报告 §六 原话："目录不可写 / 端口冲突 | 未在本包重新测量"。
/// 端口冲突已在 `LauncherStartupBoundaryTests` 关闭；本类关闭**目录不可写**这一半。
///
/// 做法（确定性、不动 ACL）：把本该是**目录**的路径做成**文件** ——
///   * `mods` 是文件 → 列 Mod 时的 `Directory.CreateDirectory` 必然失败；
///   * 游戏目录位于**文件之下**（`<file>\sub`）→ `Directory.Exists` 为 false，注册应被拒。
/// 两者都等价于"这个位置写不了"，且**不会**留下任何 ACL 改动。
///
/// **未覆盖**：真正的 ACL 拒绝（`icacls /deny`）—— 见本包 deliverable §五。
/// </summary>
[Collection("LocalApiService")]
public class UnwritableDirectoryTests
{
	private readonly ITestOutputHelper _out;

	public UnwritableDirectoryTests(ITestOutputHelper output) => _out = output;

	private sealed class Fixture : IDisposable
	{
		public string Root { get; }
		public string InstanceId { get; } = "unwritable-inst";
		public AppConfig Config { get; }

		public Fixture()
		{
			Root = Path.Combine(Path.GetTempPath(), "pineglow-a12b-" + Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(Root);

			// 关键：`mods` 是一个**文件**而不是目录 → 任何"创建 mods 目录"的写操作都会失败
			File.WriteAllText(Path.Combine(Root, "mods"), "NOT-A-DIRECTORY");

			// 一些正常文件，用于证明失败路径不会破坏既有数据
			File.WriteAllText(Path.Combine(Root, "options.txt"), "fov:70");
			Directory.CreateDirectory(Path.Combine(Root, "saves", "world1"));
			File.WriteAllText(Path.Combine(Root, "saves", "world1", "level.dat"), "LEVEL-DATA");

			Config = new AppConfig
			{
				GameDirectory = Root,
				SelectedInstanceId = InstanceId,
				Instances = new List<GameInstance>
				{
					new GameInstance { Id = InstanceId, Name = "unwritable", GameDirectory = Root, MinecraftVersion = "1.20.4" }
				}
			};
		}

		public Dictionary<string, string> Snapshot()
		{
			var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
			foreach (string file in Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories))
			{
				map[Path.GetRelativePath(Root, file)] = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file)));
			}

			return map;
		}

		public void Dispose()
		{
			try { if (Directory.Exists(Root)) Directory.Delete(Root, true); } catch { }
		}
	}

	private static string? ErrorOf(string body)
	{
		try
		{
			using JsonDocument doc = JsonDocument.Parse(body);
			return doc.RootElement.TryGetProperty("error", out JsonElement e) && e.ValueKind == JsonValueKind.String
				? e.GetString()
				: null;
		}
		catch
		{
			return null;
		}
	}

	/* ===================== ① mods 是文件 → 列表必须给可读错误，且文件不被破坏 ===================== */

	[Fact]
	public async Task ListMods_WhenModsPathIsAFile_ReturnsReadableError_AndLeavesThatFileUntouched()
	{
		using var fx = new Fixture();
		string modsFile = Path.Combine(fx.Root, "mods");
		byte[] before = File.ReadAllBytes(modsFile);

		await using var h = await LocalApiTestHarness.StartAsync(fx.Config);
		(int status, string body) = await h.SendAsync(HttpMethod.Get, "/api/instances/" + fx.InstanceId + "/mods");

		string? error = ErrorOf(body);
		_out.WriteLine($"[A12b] mods 为文件时 GET mods -> {status} error={error}");

		Assert.True(status != 200, "mods 路径是文件时不得假装成功返回 200");
		Assert.False(string.IsNullOrWhiteSpace(error), "失败必须带可读的 error（不能是空体/裸 500）");

		// 数据安全：那个文件必须原样
		Assert.True(File.Exists(modsFile), "那个文件必须仍然存在");
		Assert.True(File.ReadAllBytes(modsFile).SequenceEqual(before), "失败路径不得改动该文件内容");
	}

	/* ===================== ② 游戏目录位于文件之下 → 注册应给可读 400 ===================== */

	[Fact]
	public async Task Register_WhenGameDirectoryIsUnderAFile_IsRejectedWithReadableError()
	{
		using var fx = new Fixture();
		await using var h = await LocalApiTestHarness.StartAsync(fx.Config);

		// `<file>\sub` 这种路径的父级是文件 → 目录不可能存在
		string bogus = Path.Combine(fx.Root, "mods", "sub");
		(int status, string body) = await h.SendAsync(HttpMethod.Post, "/api/instances/register",
			new { gameDirectory = bogus, name = "bogus", minecraftVersion = "1.20.4" });

		string? error = ErrorOf(body);
		_out.WriteLine($"[A12b] 注册 <file>\\sub -> {status} error={error}");

		Assert.True(status == 400, $"路径位于文件之下时应返回 400，实际 {status}");
		Assert.False(string.IsNullOrWhiteSpace(error), "必须带可读 error");
		Assert.True(error!.Contains("目录不存在", StringComparison.Ordinal) || error.Contains("无效", StringComparison.Ordinal),
			"错误文案应指出目录问题，实际：" + error);
	}

	/* ===================== ③ 两次写失败之后，整棵合成目录必须逐字节不变 ===================== */

	[Fact]
	public async Task AfterBothWriteFailures_TheWholeSyntheticTreeIsByteIdentical()
	{
		using var fx = new Fixture();
		Dictionary<string, string> before = fx.Snapshot();
		// 夹具固定 3 个文件：mods（文件）、options.txt、saves/world1/level.dat
		Assert.True(before.Count == 3, "夹具应为 3 个文件，实际 " + before.Count + "：" + string.Join(", ", before.Keys));

		await using (var h = await LocalApiTestHarness.StartAsync(fx.Config))
		{
			await h.SendAsync(HttpMethod.Get, "/api/instances/" + fx.InstanceId + "/mods");
			await h.SendAsync(HttpMethod.Post, "/api/instances/register",
				new { gameDirectory = Path.Combine(fx.Root, "mods", "sub"), name = "bogus", minecraftVersion = "1.20.4" });
		}

		Dictionary<string, string> after = fx.Snapshot();
		Assert.True(before.Count == after.Count,
			"写失败路径不得增删任何文件：\n" + DescribeDiff(before, after));
		Assert.True(before.All(kv => after.TryGetValue(kv.Key, out string? now) && now == kv.Value),
			"写失败路径不得改写任何文件：\n" + DescribeDiff(before, after));
		_out.WriteLine($"[A12b] 两次写失败后，{after.Count} 个文件全部逐字节不变");
	}

	private static string DescribeDiff(Dictionary<string, string> before, Dictionary<string, string> after)
	{
		var lines = new List<string>();
		foreach (var kv in before)
		{
			if (!after.TryGetValue(kv.Key, out string? now))
			{
				lines.Add("  消失: " + kv.Key);
			}
			else if (!string.Equals(now, kv.Value, StringComparison.Ordinal))
			{
				lines.Add("  被改写: " + kv.Key);
			}
		}

		foreach (var kv in after.Where(kv => !before.ContainsKey(kv.Key)))
		{
			lines.Add("  新增: " + kv.Key);
		}

		return lines.Count == 0 ? "  （无差异）" : string.Join("\n", lines);
	}
}
