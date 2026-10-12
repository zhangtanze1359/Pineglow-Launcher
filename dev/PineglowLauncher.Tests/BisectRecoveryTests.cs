using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
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
/// MR07：Bisect 重启恢复、依赖与存档安全。
///
/// 改动前：会话只存在于内存 <c>ConcurrentDictionary</c>，进程一重启，
/// "哪些文件是本次会话禁用的、原始状态是什么"就全丢了——用户只能靠文件名猜。
///
/// 本文件验证的契约：
///   1. 会话与基线（含 SHA）落盘在游戏目录之外，进程中断后仍能查到；
///   2. `pending` 逐项比对磁盘与记录，冲突时明确列出而**不靠文件名猜**；
///   3. `resume restore` 只把本会话开始时启用的文件恢复为启用态——
///      不启用用户原本禁用的 Mod、不删除后来加入的文件；
///   4. `resume continue` 只在磁盘与记录完全一致时重建会话；
///   5. 有未完成记录时不允许直接开新一轮；
///   6. 禁用集合会破坏依赖（禁用了仍然启用的 Mod 的前置）时，该轮标为无效测试并拒绝反馈。
/// </summary>
[Collection("LocalApiService")]
public class BisectRecoveryTests
{
	private readonly ITestOutputHelper _out;

	public BisectRecoveryTests(ITestOutputHelper output) => _out = output;

	/* ===================== 夹具 ===================== */

	private sealed class Fixture : IDisposable
	{
		public string Root { get; }
		public string ModsDir => Path.Combine(Root, "mods");
		public string InstanceId { get; } = "inst-" + Guid.NewGuid().ToString("N").Substring(0, 8);
		public AppConfig Config { get; }

		public Fixture(params string[] modFileNames)
		{
			Root = Path.Combine(Path.GetTempPath(), "pineglow-bisect-rec-" + Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(ModsDir);
			foreach (string name in modFileNames)
			{
				File.WriteAllText(Path.Combine(ModsDir, name), "PLAIN:" + name);
			}

			Config = new AppConfig
			{
				GameDirectory = Root,
				SelectedInstanceId = InstanceId,
				Instances = new List<GameInstance>
				{
					new GameInstance { Id = InstanceId, Name = "rec", GameDirectory = Root, MinecraftVersion = "1.20.4" }
				}
			};
		}

		/// <summary>写一个带 fabric.mod.json 的合法 jar，用于依赖校验。</summary>
		public void WriteFabricJar(string fileName, string modId, params string[] requiredDepends)
		{
			string path = Path.Combine(ModsDir, fileName);
			using FileStream fs = File.Create(path);
			using var zip = new ZipArchive(fs, ZipArchiveMode.Create);
			ZipArchiveEntry entry = zip.CreateEntry("fabric.mod.json");
			using var writer = new StreamWriter(entry.Open());
			string depends = "{}";
			if (requiredDepends.Length > 0)
			{
				depends = "{" + string.Join(",", requiredDepends.Select(d => $"\"{d}\":\"*\"") ) + "}";
			}

			writer.Write($"{{\"schemaVersion\":1,\"id\":\"{modId}\",\"version\":\"1.0.0\",\"name\":\"{modId}\",\"depends\":{depends}}}");
		}

		public string Api(string suffix) => "/api/instances/" + InstanceId + suffix;
		public string Mod(string name) => Path.Combine(ModsDir, name);

		/// <summary>模拟"进程被杀 / 应用重启"：内存会话清空，落盘记录保留。</summary>
		public void SimulateRestart() => ModBisectService.Remove(InstanceId);

		public void Dispose()
		{
			try { ModBisectService.Remove(InstanceId); } catch { }
			try { if (Directory.Exists(Root)) Directory.Delete(Root, true); } catch { }
		}
	}

	private static string Sha(string path)
	{
		using FileStream fs = File.OpenRead(path);
		return Convert.ToHexString(SHA256.HashData(fs));
	}

	private static string? Error(string body)
	{
		try
		{
			using JsonDocument doc = JsonDocument.Parse(body);
			return doc.RootElement.TryGetProperty("error", out JsonElement el) ? el.GetString() : null;
		}
		catch { }
		return null;
	}

	private static bool Flag(string body, string name)
	{
		using JsonDocument doc = JsonDocument.Parse(body);
		return doc.RootElement.TryGetProperty(name, out JsonElement el) && el.ValueKind == JsonValueKind.True;
	}

	private static JsonElement Root(string body) => JsonDocument.Parse(body).RootElement.Clone();

	private static int ConflictCount(string body)
	{
		JsonElement r = Root(body);
		return r.TryGetProperty("conflicts", out JsonElement c) && c.ValueKind == JsonValueKind.Array ? c.GetArrayLength() : 0;
	}

	/* ===================== 1. 中断后能查到未完成会话并继续 ===================== */

	[Fact]
	public async Task InterruptedAfterStart_PendingIsFound_AndContinueRebuildsSession()
	{
		using var fx = new Fixture("a.jar", "b.jar", "c.jar", "d.jar");
		await using var h = await LocalApiTestHarness.StartAsync(fx.Config);

		(int startStatus, string startBody) = await h.SendAsync(HttpMethod.Post, fx.Api("/bisect/start"), new { });
		Assert.Equal(200, startStatus);
		int step = Root(startBody).GetProperty("step").GetInt32();
		Assert.True(File.Exists(fx.Mod("a.jar.disabled")), "start 应当禁用一半");

		fx.SimulateRestart(); // 进程被中断

		(int pendingStatus, string pendingBody) = await h.SendAsync(HttpMethod.Get, fx.Api("/bisect"));
		_out.WriteLine($"[pending] -> {pendingStatus} body={pendingBody}");
		Assert.Equal(200, pendingStatus);
		Assert.True(Flag(pendingBody, "pending"), "重启后必须能从落盘记录发现未完成会话");
		Assert.True(Flag(pendingBody, "canContinue"), "磁盘与记录一致时应当可以继续");
		Assert.Equal(0, ConflictCount(pendingBody));
		Assert.Equal(step, Root(pendingBody).GetProperty("step").GetInt32());

		(int resumeStatus, string resumeBody) = await h.SendAsync(HttpMethod.Post, fx.Api("/bisect/resume"),
			new { action = "continue" });
		_out.WriteLine($"[resume-continue] -> {resumeStatus} body={resumeBody}");
		Assert.Equal(200, resumeStatus);
		Assert.True(Root(resumeBody).GetProperty("waitingForReport").GetBoolean(), "继续后应当回到「等待反馈」状态");

		// 会话真的回来了：可以继续上报
		(int testStatus, _) = await h.SendAsync(HttpMethod.Post, fx.Api("/bisect/test"),
			new { crashed = false, step = Root(resumeBody).GetProperty("step").GetInt32() });
		Assert.Equal(200, testStatus);
	}

	/* ===================== 2. 中断后恢复原状态并清空记录 ===================== */

	[Fact]
	public async Task InterruptedBeforeRestore_ResumeRestorePutsEveryManagedFileBack()
	{
		using var fx = new Fixture("a.jar", "b.jar");
		string shaA = Sha(fx.Mod("a.jar"));
		string shaB = Sha(fx.Mod("b.jar"));

		await using var h = await LocalApiTestHarness.StartAsync(fx.Config);
		(int startStatus, _) = await h.SendAsync(HttpMethod.Post, fx.Api("/bisect/start"), new { });
		Assert.Equal(200, startStatus);

		fx.SimulateRestart();

		(int status, string body) = await h.SendAsync(HttpMethod.Post, fx.Api("/bisect/resume"), new { action = "restore" });
		_out.WriteLine($"[resume-restore] -> {status} body={body}");

		Assert.Equal(200, status);
		Assert.True(Flag(body, "ok"), "恢复应当成功");
		Assert.True(File.Exists(fx.Mod("a.jar")) && File.Exists(fx.Mod("b.jar")));
		Assert.False(File.Exists(fx.Mod("a.jar.disabled")));
		Assert.Equal(shaA, Sha(fx.Mod("a.jar")));
		Assert.Equal(shaB, Sha(fx.Mod("b.jar")));

		// 记录被清空
		(int pendingStatus, string pendingBody) = await h.SendAsync(HttpMethod.Get, fx.Api("/bisect"));
		Assert.Equal(200, pendingStatus);
		Assert.False(Flag(pendingBody, "pending"), "恢复完成后不应再有未完成记录");
	}

	/* ===================== 3. 恢复只碰本会话管理的文件 ===================== */

	[Fact]
	public async Task ResumeRestore_DoesNotEnableUserDisabledMods_AndDoesNotDeleteNewFiles()
	{
		using var fx = new Fixture("a.jar", "b.jar.disabled");
		string userDisabledSha = Sha(fx.Mod("b.jar.disabled"));

		await using var h = await LocalApiTestHarness.StartAsync(fx.Config);
		(int startStatus, _) = await h.SendAsync(HttpMethod.Post, fx.Api("/bisect/start"), new { });
		Assert.Equal(200, startStatus);

		// 会话开始**之后**用户又加入的文件
		File.WriteAllText(fx.Mod("later.jar"), "NEW-LATER");
		fx.SimulateRestart();

		(int status, _) = await h.SendAsync(HttpMethod.Post, fx.Api("/bisect/resume"), new { action = "restore" });
		Assert.Equal(200, status);

		Assert.True(File.Exists(fx.Mod("a.jar")), "会话管理的文件应恢复为启用态");
		Assert.True(File.Exists(fx.Mod("b.jar.disabled")), "用户原本禁用的 Mod 必须保持禁用");
		Assert.False(File.Exists(fx.Mod("b.jar")));
		Assert.Equal(userDisabledSha, Sha(fx.Mod("b.jar.disabled")));
		Assert.True(File.Exists(fx.Mod("later.jar")), "后来加入的文件不得被删除");
		Assert.Equal("NEW-LATER", File.ReadAllText(fx.Mod("later.jar")));
	}

	/* ===================== 4. 磁盘与记录不一致时明确列出冲突 ===================== */

	[Fact]
	public async Task Continue_WithDiskMismatch_ListsConflictsAndRefuses()
	{
		using var fx = new Fixture("a.jar", "b.jar");
		await using var h = await LocalApiTestHarness.StartAsync(fx.Config);

		(int startStatus, _) = await h.SendAsync(HttpMethod.Post, fx.Api("/bisect/start"), new { });
		Assert.Equal(200, startStatus);
		Assert.True(File.Exists(fx.Mod("a.jar.disabled")));

		fx.SimulateRestart();

		// 有人（或另一个进程）在会话之外改动了被禁用的文件
		File.Move(fx.Mod("a.jar.disabled"), fx.Mod("a.jar"));

		(int pendingStatus, string pendingBody) = await h.SendAsync(HttpMethod.Get, fx.Api("/bisect"));
		Assert.Equal(200, pendingStatus);
		Assert.True(ConflictCount(pendingBody) >= 1, "磁盘与记录不一致时必须列出冲突，而不是靠文件名猜");
		Assert.False(Flag(pendingBody, "canContinue"));

		(int resumeStatus, string resumeBody) = await h.SendAsync(HttpMethod.Post, fx.Api("/bisect/resume"),
			new { action = "continue" });
		_out.WriteLine($"[resume-conflict] -> {resumeStatus} error={Error(resumeBody)}");
		Assert.Equal(409, resumeStatus);
		Assert.Contains("不一致", Error(resumeBody) ?? string.Empty);
		Assert.True(ConflictCount(resumeBody) >= 1);

		// 文件没有被"顺手覆盖"
		Assert.True(File.Exists(fx.Mod("a.jar")));
	}

	/* ===================== 5. 有未完成记录时不允许直接开新一轮 ===================== */

	[Fact]
	public async Task Start_WithPendingRecord_IsRefusedUntilResolved()
	{
		using var fx = new Fixture("a.jar", "b.jar");
		await using var h = await LocalApiTestHarness.StartAsync(fx.Config);

		(int first, _) = await h.SendAsync(HttpMethod.Post, fx.Api("/bisect/start"), new { });
		Assert.Equal(200, first);

		fx.SimulateRestart();

		(int second, string secondBody) = await h.SendAsync(HttpMethod.Post, fx.Api("/bisect/start"), new { });
		_out.WriteLine($"[start-with-pending] -> {second} error={Error(secondBody)}");

		Assert.Equal(409, second);
		Assert.Contains("未完成", Error(secondBody) ?? string.Empty);
		Assert.True(Flag(secondBody, "pending"));
	}

	/* ===================== 6. 依赖被破坏的一轮是无效测试 ===================== */

	[Fact]
	public async Task DependencyBreak_MarksRoundInvalid_AndTouchesNoFile()
	{
		using var fx = new Fixture();
		fx.WriteFabricJar("dep.jar", "depmod");                       // 前置
		fx.WriteFabricJar("user.jar", "usermod", "depmod");            // 依赖 depmod

		await using var h = await LocalApiTestHarness.StartAsync(fx.Config);

		(int status, string body) = await h.SendAsync(HttpMethod.Post, fx.Api("/bisect/start"), new { });
		_out.WriteLine($"[dep-break] -> {status} body={body}");

		// 2 个 Mod 时第一半就是 [dep.jar]，禁用它会破坏仍然启用的 user.jar 的前置
		Assert.Equal(409, status);
		Assert.True(Flag(body, "roundInvalid"), "依赖被破坏的一轮必须标为无效");
		Assert.Contains("依赖前置", string.Join(" ", Root(body).GetProperty("issues").EnumerateArray().Select(x => x.GetString())));

		// 无效的一轮不得改动任何文件
		Assert.True(File.Exists(fx.Mod("dep.jar")), "判定无效后不得落盘禁用");
		Assert.False(File.Exists(fx.Mod("dep.jar.disabled")));
		Assert.True(File.Exists(fx.Mod("user.jar")));
	}

	/* ===================== 7. 缺少 manifest 元数据 → limited（可继续但要提示） ===================== */

	[Fact]
	public async Task MissingManifestMetadata_IsReportedAsLimited_NotInvalid()
	{
		using var fx = new Fixture("a.jar", "b.jar"); // 纯文本 jar，没有 manifest
		await using var h = await LocalApiTestHarness.StartAsync(fx.Config);

		(int status, string body) = await h.SendAsync(HttpMethod.Post, fx.Api("/bisect/start"), new { });
		_out.WriteLine($"[metadata-limited] -> {status} body={body}");

		Assert.Equal(200, status);
		Assert.Equal("limited", Root(body).GetProperty("roundValidity").GetString());
		Assert.False(Flag(body, "roundInvalid"));
		Assert.True(File.Exists(fx.Mod("a.jar.disabled")), "limited 轮次仍然照常执行");
	}

	/* ===================== 8. 记录损坏时按"无记录"处理，不半途而废 ===================== */

	[Fact]
	public async Task CorruptRecord_IsTreatedAsNoPendingSession()
	{
		using var fx = new Fixture("a.jar", "b.jar");
		await using var h = await LocalApiTestHarness.StartAsync(fx.Config);

		(int startStatus, _) = await h.SendAsync(HttpMethod.Post, fx.Api("/bisect/start"), new { });
		Assert.Equal(200, startStatus);
		fx.SimulateRestart();

		string recordPath = Path.Combine(h.BisectStoreRoot, fx.InstanceId + ".json");
		Assert.True(File.Exists(recordPath), "记录应当落在测试专用的临时根目录里");
		File.WriteAllText(recordPath, "{ this is not json");

		(int pendingStatus, string pendingBody) = await h.SendAsync(HttpMethod.Get, fx.Api("/bisect"));
		Assert.Equal(200, pendingStatus);
		Assert.False(Flag(pendingBody, "pending"), "损坏的记录不能伪装成一条可用会话");
	}
}
