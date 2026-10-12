using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Pineglow.Launcher.Models;
using Xunit;
using Xunit.Abstractions;

namespace Pineglow.Launcher.Tests;

/// <summary>
/// 第 63 轮：修复 **Codex 独立探针发现的 2 个缺陷**，并补上常驻覆盖。
///
/// 来源（`docs/market-acceptance/20261003-W1-W3/`）：
///   * `IndependentRouteProbes.BackupsListIsScopedToRequestedWorld` → **FAIL**
///     "请求 worldB 的备份列表，却返回了 worldA 的备份"
///   * `IndependentRouteProbes.RestoreRejectsBackupFromDifferentWorld` → **FAIL**
///     "跨世界恢复被接受：200 {ok:true, path:…\\saves\\worldA (1)}"
///
/// 这两个缺陷**我自己的测试没抓到** —— 它们由**独立探针**发现。
/// 本类把两个行为**永久钉住**（不依赖外部探针文件）。
/// </summary>
[Collection("LocalApiService")]
public class WorldBackupScopeTests
{
	private readonly ITestOutputHelper _out;

	public WorldBackupScopeTests(ITestOutputHelper output) => _out = output;

	private sealed class Fixture : IDisposable
	{
		public string Root { get; }
		public string InstanceId { get; } = "wb-" + Guid.NewGuid().ToString("N").Substring(0, 8);
		public AppConfig Config { get; }

		public string Api(string suffix) => "/api/instances/" + InstanceId + suffix;

		public Fixture()
		{
			Root = Path.Combine(Path.GetTempPath(), "pineglow-wb-" + Guid.NewGuid().ToString("N"));
			foreach (string w in new[] { "worldA", "worldB" })
			{
				Directory.CreateDirectory(Path.Combine(Root, "saves", w));
				File.WriteAllText(Path.Combine(Root, "saves", w, "level.dat"), "original-" + w);
			}

			Config = new AppConfig
			{
				GameDirectory = Root,
				SelectedInstanceId = InstanceId,
				Instances = new List<GameInstance>
				{
					new GameInstance { Id = InstanceId, Name = "wb", GameDirectory = Root, MinecraftVersion = "1.20.4" }
				}
			};
		}

		/// <summary>目录内全部文件的 (相对路径, SHA256) 有序快照 —— 用于"跨世界恢复被拒后什么都没变"。</summary>
		public string[] Snapshot() => Directory.GetFiles(Root, "*", SearchOption.AllDirectories)
			.Select(p => Path.GetRelativePath(Root, p) + ":" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(p))))
			.OrderBy(x => x, StringComparer.Ordinal)
			.ToArray();

		public void Dispose()
		{
			try { if (Directory.Exists(Root)) Directory.Delete(Root, true); } catch { }
		}
	}

	private static async Task<string> MakeBackupOfWorldA(LocalApiTestHarness h, Fixture fx)
	{
		(int status, string body) = await h.SendAsync(HttpMethod.Post, fx.Api("/saves/worldA/backup"));
		Assert.Equal(200, status);
		using JsonDocument doc = JsonDocument.Parse(body);
		return Path.GetFileName(doc.RootElement.GetProperty("path").GetString()!);
	}

	/* ===================== 缺陷 1：列表必须按请求的世界过滤 ===================== */

	[Fact]
	public async Task BackupsList_IsScopedToTheRequestedWorld()
	{
		using var fx = new Fixture();
		await using var h = await LocalApiTestHarness.StartAsync(fx.Config);

		string worldABackup = await MakeBackupOfWorldA(h, fx);
		_out.WriteLine("[A23] worldA 的备份文件名 = " + worldABackup);

		// worldA 的列表：应恰好包含那一个备份
		(int aStatus, string aBody) = await h.SendAsync(HttpMethod.Get, fx.Api("/saves/worldA/backups"));
		Assert.Equal(200, aStatus);
		using (JsonDocument doc = JsonDocument.Parse(aBody))
		{
			var names = doc.RootElement.GetProperty("backups").EnumerateArray()
				.Select(x => x.GetProperty("fileName").GetString()).ToList();
			Assert.Contains(worldABackup, names);
		}

		// worldB 的列表：**必须为空**（修复前会返回 worldA 的备份）
		(int bStatus, string bBody) = await h.SendAsync(HttpMethod.Get, fx.Api("/saves/worldB/backups"));
		Assert.Equal(200, bStatus);
		_out.WriteLine("[A23] worldB 的列表 = " + bBody);
		using (JsonDocument doc = JsonDocument.Parse(bBody))
		{
			Assert.Empty(doc.RootElement.GetProperty("backups").EnumerateArray());
		}
	}

	/* ===================== 缺陷 2：跨世界恢复必须被拒绝（数据安全） ===================== */

	[Fact]
	public async Task Restore_RejectsABackupFromAnotherWorld_AndChangesNothing()
	{
		using var fx = new Fixture();
		await using var h = await LocalApiTestHarness.StartAsync(fx.Config);

		string worldABackup = await MakeBackupOfWorldA(h, fx);
		string[] before = fx.Snapshot();

		// 用 worldA 的备份去恢复 worldB → 必须 4xx/5xx
		(int status, string body) = await h.SendAsync(HttpMethod.Post,
			fx.Api("/saves/worldB/backups/" + worldABackup + "/restore"));
		_out.WriteLine($"[A23] worldB 用 worldA 的备份恢复 -> {status} {body}");

		Assert.True(status >= 400, $"跨世界恢复必须被拒绝，实际 {status}：{body}");
		// 注意（本会话反复踩的坑）：JSON 响应里的中文是 \uXXXX 转义的 —— **必须先解析**再断言，
		// 直接对 body 做字符串匹配会永远失败。
		string? error = null;
		try
		{
			using JsonDocument errDoc = JsonDocument.Parse(body);
			if (errDoc.RootElement.TryGetProperty("error", out JsonElement el) && el.ValueKind == JsonValueKind.String)
			{
				error = el.GetString();
			}
		}
		catch { }
		_out.WriteLine("[A23] 解析后的错误信息 = " + error);
		Assert.False(string.IsNullOrWhiteSpace(error), "拒绝时必须带可读 error");
		Assert.Contains("属于", error!);          // 错误信息要能说清"该备份属于哪个世界"
		Assert.Contains("worldA", error!);

		// **数据安全**：一个文件都不能变（修复前会写出 saves\worldA (1)）
		string[] after = fx.Snapshot();
		Assert.Equal(before, after);
		Assert.False(Directory.Exists(Path.Combine(fx.Root, "saves", "worldA (1)")),
			"跨世界恢复被拒后不得留下任何新目录");
	}

	/* ===================== 正面：同世界恢复仍然可用（别把功能改坏） ===================== */

	[Fact]
	public async Task Restore_OfTheSameWorld_StillWorks()
	{
		using var fx = new Fixture();
		await using var h = await LocalApiTestHarness.StartAsync(fx.Config);

		string worldABackup = await MakeBackupOfWorldA(h, fx);
		File.WriteAllText(Path.Combine(fx.Root, "saves", "worldA", "level.dat"), "changed-A");

		(int status, string body) = await h.SendAsync(HttpMethod.Post,
			fx.Api("/saves/worldA/backups/" + worldABackup + "/restore"));
		_out.WriteLine($"[A23] worldA 用 worldA 的备份恢复 -> {status}");
		Assert.Equal(200, status);

		using JsonDocument doc = JsonDocument.Parse(body);
		string target = doc.RootElement.GetProperty("path").GetString()!;
		Assert.Equal("original-worldA", File.ReadAllText(Path.Combine(target, "level.dat")));
	}
}
