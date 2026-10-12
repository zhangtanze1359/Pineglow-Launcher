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
/// MR04：Mod 删除改为可恢复隔离。
///
/// 被测契约：
///   1. `POST /api/instances/{id}/mods/{file}/delete` 不再物理删除：
///      jar 与 `.mcmini.json` 侧车作为**同一次操作**移入恢复区，原位置消失、恢复区出现同 SHA 副本。
///   2. `GET  /api/instances/{id}/recovery` 能列出记录（含过期时间与逐文件状态）。
///   3. `POST /api/instances/{id}/recovery/{op}/restore` 逐字节还原；
///      重复恢复幂等；原位置有同名且内容不同的文件时**默认不覆盖**，报 conflict。
///   4. 失败必须如实报告：隔离不了就不宣称删除成功；越界路径与重解析点一律拒绝。
///
/// 恢复区由 <see cref="LocalApiTestHarness"/> 重定向到临时目录，绝不写真实 %APPDATA%。
/// </summary>
[Collection("LocalApiService")]
public class FileRecoveryTests
{
	private const string JarContent = "AAA-jar-content-v1";
	private const string MetaContent = "{\"modId\":\"demo\",\"version\":\"1.0.0\"}";
	private const string OtherContent = "BBB-untouched";

	private readonly ITestOutputHelper _out;

	public FileRecoveryTests(ITestOutputHelper output) => _out = output;

	/* ===================== 合成夹具 ===================== */

	private sealed class Fixture : IDisposable
	{
		public string Root { get; }
		public string ModsDir => Path.Combine(Root, "mods");
		public string InstanceId { get; } = "inst-" + Guid.NewGuid().ToString("N").Substring(0, 8);
		public AppConfig Config { get; }
		public string JarPath => Path.Combine(ModsDir, "a.jar");
		public string MetaPath => JarPath + ".mcmini.json";
		public string OtherPath => Path.Combine(ModsDir, "b.jar");

		public Fixture()
		{
			Root = Path.Combine(Path.GetTempPath(), "pineglow-file-recovery-" + Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(ModsDir);
			File.WriteAllText(JarPath, JarContent);
			File.WriteAllText(MetaPath, MetaContent);
			File.WriteAllText(OtherPath, OtherContent);

			Config = new AppConfig
			{
				GameDirectory = Root,
				SelectedInstanceId = InstanceId,
				Instances = new List<GameInstance>
				{
					new GameInstance
					{
						Id = InstanceId,
						Name = "recovery",
						GameDirectory = Root,
						MinecraftVersion = "1.20.4"
					}
				}
			};
		}

		public string Api(string suffix) => "/api/instances/" + InstanceId + suffix;

		public Dictionary<string, string> SnapshotFiles()
		{
			var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
			if (!Directory.Exists(Root)) return map;
			foreach (string file in Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories))
			{
				map[Path.GetRelativePath(Root, file)] = Sha(file);
			}
			return map;
		}

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

	private static string? FindUnder(string root, string fileName) =>
		Directory.Exists(root)
			? Directory.EnumerateFiles(root, fileName, SearchOption.AllDirectories).FirstOrDefault()
			: null;

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

	private static JsonElement FileEntry(string body, string relativePathSuffix)
	{
		using JsonDocument doc = JsonDocument.Parse(body);
		foreach (JsonElement f in doc.RootElement.GetProperty("files").EnumerateArray())
		{
			if ((f.GetProperty("relativePath").GetString() ?? string.Empty).EndsWith(relativePathSuffix, StringComparison.OrdinalIgnoreCase))
			{
				return f.Clone();
			}
		}
		throw new Xunit.Sdk.XunitException($"响应里没有以 {relativePathSuffix} 结尾的文件项：{body}");
	}

	/* ===================== 1. 删除 = 隔离 jar + 侧车 ===================== */

	[Fact]
	public async Task DeleteMod_QuarantinesJarAndSidecar_KeepsByteIdenticalCopies()
	{
		using var fx = new Fixture();
		await using var h = await LocalApiTestHarness.StartAsync(fx.Config);

		string jarSha = Sha(fx.JarPath);
		string metaSha = Sha(fx.MetaPath);
		string otherSha = Sha(fx.OtherPath);

		(int status, string body) = await h.SendAsync(HttpMethod.Post, fx.Api("/mods/a.jar/delete"), new { });
		_out.WriteLine($"[delete-mod] -> {status} body={body}");

		Assert.Equal(200, status);
		string operationId;
		using (JsonDocument doc = JsonDocument.Parse(body))
		{
			Assert.True(doc.RootElement.GetProperty("ok").GetBoolean());
			Assert.True(doc.RootElement.GetProperty("recoverable").GetBoolean());
			operationId = doc.RootElement.GetProperty("operationId").GetString()!;
		}

		// 原位置应当消失
		Assert.False(File.Exists(fx.JarPath), "删除后 jar 不应留在原位置");
		Assert.False(File.Exists(fx.MetaPath), "删除后侧车不应留在原位置");
		Assert.True(File.Exists(fx.OtherPath), "同目录的其它 Mod 必须原样保留");
		Assert.Equal(otherSha, Sha(fx.OtherPath));

		// 恢复区里应当有逐字节一致的副本
		string? storedJar = FindUnder(h.RecoveryRoot, "a.jar");
		string? storedMeta = FindUnder(h.RecoveryRoot, "a.jar.mcmini.json");
		Assert.NotNull(storedJar);
		Assert.NotNull(storedMeta);
		Assert.Equal(jarSha, Sha(storedJar!));
		Assert.Equal(metaSha, Sha(storedMeta!));
		Assert.Contains(operationId, storedJar!);

		// 记录必须可列出，并带过期时间
		(int listStatus, string listBody) = await h.SendAsync(HttpMethod.Get, fx.Api("/recovery"));
		Assert.Equal(200, listStatus);
		using (JsonDocument list = JsonDocument.Parse(listBody))
		{
			Assert.True(list.RootElement.GetProperty("retentionDays").GetInt32() == 30, "V1 保留期应为 30 天");
			JsonElement recoveries = list.RootElement.GetProperty("recoveries");
			Assert.Equal(1, recoveries.GetArrayLength());
			JsonElement rec = recoveries[0];
			Assert.Equal(operationId, rec.GetProperty("operationId").GetString());
			Assert.Equal("quarantined", rec.GetProperty("status").GetString());
			Assert.Equal(2, rec.GetProperty("files").GetArrayLength());
		}

		Assert.Equal(jarSha, FileEntry(body, "a.jar").GetProperty("sha256").GetString());
	}

	/* ===================== 2. 禁用态文件名 ===================== */

	[Fact]
	public async Task DeleteMod_DisabledMod_QuarantinesDotDisabledName()
	{
		using var fx = new Fixture();
		await using var h = await LocalApiTestHarness.StartAsync(fx.Config);

		(int toggleStatus, _) = await h.SendAsync(HttpMethod.Post, fx.Api("/mods/a.jar/toggle"), new { });
		Assert.Equal(200, toggleStatus);
		Assert.True(File.Exists(fx.JarPath + ".disabled"));

		(int status, string body) = await h.SendAsync(HttpMethod.Post, fx.Api("/mods/a.jar.disabled/delete"), new { });
		_out.WriteLine($"[delete-disabled] -> {status} body={body}");

		Assert.Equal(200, status);
		Assert.False(File.Exists(fx.JarPath + ".disabled"), "禁用态文件也应从原位置消失");
		string? stored = FindUnder(h.RecoveryRoot, "a.jar.disabled");
		Assert.NotNull(stored);
		Assert.Equal(JarContent, File.ReadAllText(stored!));
	}

	/* ===================== 3. 恢复 = 逐字节还原 ===================== */

	[Fact]
	public async Task Restore_PutsJarAndSidecarBackByteIdentical()
	{
		using var fx = new Fixture();
		await using var h = await LocalApiTestHarness.StartAsync(fx.Config);

		string jarSha = Sha(fx.JarPath);
		string metaSha = Sha(fx.MetaPath);

		(int delStatus, string delBody) = await h.SendAsync(HttpMethod.Post, fx.Api("/mods/a.jar/delete"), new { });
		Assert.Equal(200, delStatus);
		string operationId = JsonDocument.Parse(delBody).RootElement.GetProperty("operationId").GetString()!;

		(int status, string body) = await h.SendAsync(
			HttpMethod.Post, fx.Api("/recovery/" + operationId + "/restore"), new { });
		_out.WriteLine($"[restore] -> {status} body={body}");

		Assert.Equal(200, status);
		using (JsonDocument doc = JsonDocument.Parse(body))
		{
			Assert.True(doc.RootElement.GetProperty("ok").GetBoolean(), "全部文件都应恢复成功");
			Assert.Equal(0, doc.RootElement.GetProperty("conflicts").GetInt32());
			Assert.Equal("restored", doc.RootElement.GetProperty("status").GetString());
		}

		Assert.True(File.Exists(fx.JarPath), "jar 应回到原位置");
		Assert.True(File.Exists(fx.MetaPath), "侧车应回到原位置");
		Assert.Equal(jarSha, Sha(fx.JarPath));
		Assert.Equal(metaSha, Sha(fx.MetaPath));
		Assert.Equal(JarContent, File.ReadAllText(fx.JarPath));
	}

	/* ===================== 4. 重复恢复幂等 ===================== */

	[Fact]
	public async Task Restore_Twice_IsIdempotent_AndKeepsFileContent()
	{
		using var fx = new Fixture();
		await using var h = await LocalApiTestHarness.StartAsync(fx.Config);

		(int _, string delBody) = await h.SendAsync(HttpMethod.Post, fx.Api("/mods/a.jar/delete"), new { });
		string operationId = JsonDocument.Parse(delBody).RootElement.GetProperty("operationId").GetString()!;

		(int first, string firstBody) = await h.SendAsync(HttpMethod.Post, fx.Api("/recovery/" + operationId + "/restore"), new { });
		Assert.Equal(200, first);
		string shaAfterFirst = Sha(fx.JarPath);

		(int second, string secondBody) = await h.SendAsync(HttpMethod.Post, fx.Api("/recovery/" + operationId + "/restore"), new { });
		_out.WriteLine($"[restore-2nd] -> {second} body={secondBody}");

		Assert.Equal(200, second);
		using (JsonDocument doc = JsonDocument.Parse(secondBody))
		{
			Assert.True(doc.RootElement.GetProperty("ok").GetBoolean(), "重复恢复应当幂等成功");
			Assert.Equal("already_restored", FileEntry(secondBody, "a.jar").GetProperty("state").GetString());
		}
		Assert.Equal(shaAfterFirst, Sha(fx.JarPath));
		Assert.Equal(JarContent, File.ReadAllText(fx.JarPath));

		using JsonDocument firstDoc = JsonDocument.Parse(firstBody);
		Assert.True(firstDoc.RootElement.GetProperty("ok").GetBoolean());
	}

	/* ===================== 5. 同名冲突：默认不覆盖 ===================== */

	[Fact]
	public async Task Restore_WithDifferentFileAtOriginalPath_ReportsConflictAndDoesNotOverwrite()
	{
		using var fx = new Fixture();
		await using var h = await LocalApiTestHarness.StartAsync(fx.Config);

		(int _, string delBody) = await h.SendAsync(HttpMethod.Post, fx.Api("/mods/a.jar/delete"), new { });
		string operationId = JsonDocument.Parse(delBody).RootElement.GetProperty("operationId").GetString()!;

		// 用户在原位置放了一个内容不同的新文件
		const string replacement = "CCC-a-brand-new-mod";
		File.WriteAllText(fx.JarPath, replacement);
		string replacementSha = Sha(fx.JarPath);

		(int status, string body) = await h.SendAsync(HttpMethod.Post, fx.Api("/recovery/" + operationId + "/restore"), new { });
		_out.WriteLine($"[restore-conflict] -> {status} body={body}");

		Assert.Equal(200, status);
		using (JsonDocument doc = JsonDocument.Parse(body))
		{
			Assert.False(doc.RootElement.GetProperty("ok").GetBoolean(), "存在冲突时不能报成功");
			Assert.True(doc.RootElement.GetProperty("conflicts").GetInt32() >= 1);
		}
		Assert.Equal("conflict", FileEntry(body, "a.jar").GetProperty("state").GetString());

		// 关键：既不覆盖新文件，也不丢掉隔离副本
		Assert.Equal(replacement, File.ReadAllText(fx.JarPath));
		Assert.Equal(replacementSha, Sha(fx.JarPath));
		Assert.NotNull(FindUnder(h.RecoveryRoot, "a.jar"));
	}

	/* ===================== 6. 未知文件不被移除 ===================== */

	[Fact]
	public async Task DeleteMod_LeavesEveryOtherFileUntouched()
	{
		using var fx = new Fixture();
		File.WriteAllText(Path.Combine(fx.ModsDir, "c.jar"), "CCC");
		Directory.CreateDirectory(Path.Combine(fx.Root, "saves", "w"));
		File.WriteAllText(Path.Combine(fx.Root, "saves", "w", "level.dat"), "LEVEL");

		await using var h = await LocalApiTestHarness.StartAsync(fx.Config);
		Dictionary<string, string> before = fx.SnapshotFiles();

		(int status, _) = await h.SendAsync(HttpMethod.Post, fx.Api("/mods/a.jar/delete"), new { });
		Assert.Equal(200, status);

		Dictionary<string, string> after = fx.SnapshotFiles();
		var disappeared = before.Keys.Where(k => !after.ContainsKey(k)).OrderBy(x => x).ToArray();
		Assert.Equal(new[] { Path.Combine("mods", "a.jar"), Path.Combine("mods", "a.jar.mcmini.json") }, disappeared);
		Assert.Equal(before.Count - 2, after.Count);
		foreach (var kv in after)
		{
			Assert.Equal(before[kv.Key], kv.Value);
		}
	}

	/* ===================== 7. 越界路径被拒绝 ===================== */

	[Fact]
	public async Task Quarantine_RejectsPathOutsideGameDirectory()
	{
		string gameRoot = Path.Combine(Path.GetTempPath(), "pineglow-guard-game-" + Guid.NewGuid().ToString("N"));
		string outsideRoot = Path.Combine(Path.GetTempPath(), "pineglow-guard-outside-" + Guid.NewGuid().ToString("N"));
		string recoveryRoot = Path.Combine(Path.GetTempPath(), "pineglow-guard-recovery-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(Path.Combine(gameRoot, "mods"));
		Directory.CreateDirectory(outsideRoot);
		string outsideFile = Path.Combine(outsideRoot, "evil.jar");
		File.WriteAllText(outsideFile, "SHOULD-NOT-MOVE");

		try
		{
			var service = new FileRecoveryService(recoveryRoot);
			FileRecoveryRecord record = await service.QuarantineAsync("inst-x", gameRoot, "mod-delete", new[] { outsideFile });

			Assert.Single(record.Files);
			Assert.Equal("failed", record.Files[0].State);
			Assert.Contains("游戏目录之外", record.Files[0].Error ?? string.Empty);
			Assert.True(File.Exists(outsideFile), "越界文件必须原样留在原处");
			Assert.Equal("SHOULD-NOT-MOVE", File.ReadAllText(outsideFile));
		}
		finally
		{
			try { Directory.Delete(gameRoot, true); } catch { }
			try { Directory.Delete(outsideRoot, true); } catch { }
			try { Directory.Delete(recoveryRoot, true); } catch { }
		}
	}

	/* ===================== 8. 部分失败必须如实报告 ===================== */

	[Fact]
	public async Task Quarantine_PartialFailure_RecordsMissingFileWithoutClaimingSuccess()
	{
		string gameRoot = Path.Combine(Path.GetTempPath(), "pineglow-partial-game-" + Guid.NewGuid().ToString("N"));
		string recoveryRoot = Path.Combine(Path.GetTempPath(), "pineglow-partial-recovery-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(Path.Combine(gameRoot, "mods"));
		string existing = Path.Combine(gameRoot, "mods", "ok.jar");
		File.WriteAllText(existing, "OK");
		string missing = Path.Combine(gameRoot, "mods", "gone.jar");

		try
		{
			var service = new FileRecoveryService(recoveryRoot);
			FileRecoveryRecord record = await service.QuarantineAsync("inst-y", gameRoot, "mod-delete", new[] { existing, missing });

			Assert.Equal("partial", record.Status);
			Assert.False(File.Exists(existing), "能隔离的文件仍应被隔离");
			Assert.Equal("quarantined", record.Files[0].State);
			Assert.Equal("missing", record.Files[1].State);

			// 记录本身必须可读回，且含隔离位置——中断后靠它恢复
			FileRecoveryRecord? reloaded = await service.LoadAsync("inst-y", record.OperationId);
			Assert.NotNull(reloaded);
			Assert.Equal("partial", reloaded!.Status);
			Assert.NotNull(reloaded.Files[0].StoredPath);
			Assert.True(File.Exists(reloaded.Files[0].StoredPath!));
		}
		finally
		{
			try { Directory.Delete(gameRoot, true); } catch { }
			try { Directory.Delete(recoveryRoot, true); } catch { }
		}
	}

	/* ===================== 9. 路径包含判断的边界 ===================== */

	[Fact]
	public void IsPathInside_RejectsSiblingPrefix_AndTheRootItself()
	{
		string root = Path.Combine(Path.GetTempPath(), "pineglow-inside");

		Assert.True(FileRecoveryService.IsPathInside(root, Path.Combine(root, "mods", "a.jar")));
		Assert.False(FileRecoveryService.IsPathInside(root, root), "根目录自身不算内部");
		Assert.False(FileRecoveryService.IsPathInside(root, root + "-evil"), "同前缀的兄弟目录不算内部");
		Assert.False(FileRecoveryService.IsPathInside(root, Path.Combine(root, "..", "escape.jar")), "上跳路径必须被拒绝");
		Assert.False(FileRecoveryService.IsPathInside(root, ""));
	}

	/* ===================== 11. 跨卷：复制 → 校验 → 删除源 ===================== */

	[Fact]
	public async Task Quarantine_AcrossVolumes_CopiesVerifiesThenRemovesSource()
	{
		// 恢复根在 C:（%TEMP%），游戏目录放到另一个卷 D:，迫使 File.Move 跨卷失败，
		// 从而真正走到「复制 → SHA256 校验 → 删除源」的回退路径。
		string gameRoot = "D:\\pineglow-crossvol-" + Guid.NewGuid().ToString("N");
		try
		{
			Directory.CreateDirectory(Path.Combine(gameRoot, "mods"));
		}
		catch (Exception ex)
		{
			// 没有第二卷或不可写时明确跳过，并在输出里说明（跨卷路径在本机记为未验证）。
			_out.WriteLine($"[cross-volume] 无法在 D: 卷创建目录，跳过该用例：{ex.Message}");
			return;
		}

		string recoveryRoot = Path.Combine(Path.GetTempPath(), "pineglow-crossvol-recovery-" + Guid.NewGuid().ToString("N"));
		string jar = Path.Combine(gameRoot, "mods", "x.jar");
		File.WriteAllText(jar, "CROSS-VOLUME-PAYLOAD");
		string sha = Sha(jar);

		try
		{
			var service = new FileRecoveryService(recoveryRoot);
			FileRecoveryRecord record = await service.QuarantineAsync("inst-cv", gameRoot, "mod-delete", new[] { jar });

			Assert.Equal("quarantined", record.Files[0].State);
			Assert.False(File.Exists(jar), "跨卷隔离成功后源文件才应被移除");
			Assert.NotNull(record.Files[0].StoredPath);
			Assert.True(File.Exists(record.Files[0].StoredPath!), "恢复区必须有副本");
			Assert.Equal(sha, Sha(record.Files[0].StoredPath!));

			// 再从 C: 恢复到 D:，同样跨卷，必须逐字节一致
			FileRecoveryRecord restored = await service.RestoreAsync(record);
			Assert.Equal("restored", restored.Files[0].State);
			Assert.True(File.Exists(jar), "恢复后应回到原位置");
			Assert.Equal(sha, Sha(jar));
		}
		finally
		{
			try { if (Directory.Exists(gameRoot)) Directory.Delete(gameRoot, true); } catch { }
			try { if (Directory.Exists(recoveryRoot)) Directory.Delete(recoveryRoot, true); } catch { }
		}
	}

	/* ===================== 10. 恢复记录不存在时给出明确错误 ===================== */

	[Fact]
	public async Task Restore_UnknownOperation_Returns400()
	{
		using var fx = new Fixture();
		await using var h = await LocalApiTestHarness.StartAsync(fx.Config);

		(int status, string body) = await h.SendAsync(
			HttpMethod.Post, fx.Api("/recovery/20200101-000000-deadbeef/restore"), new { });
		_out.WriteLine($"[restore-unknown] -> {status} error={ExtractError(body)}");

		Assert.Equal(400, status);
		Assert.Contains("恢复记录不存在", ExtractError(body) ?? string.Empty);
	}
}
