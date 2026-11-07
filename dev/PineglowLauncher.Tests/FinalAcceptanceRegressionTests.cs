using System;
using System.Collections.Generic;
using System.Diagnostics;
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
/// 最终验收阻断项 F-01 / F-03 / F-04 的**常驻回归**（本轮 R1–R3）。
///
/// 本文件把独立验收时写在一次性副本里的探针搬进正式测试工程，并补上边界矩阵：
///   * F-01（P1）：恢复路径穿过目录联接（junction）时，文件会被写到游戏目录之外，
///     而接口仍返回 <c>200 / ok=true / restored</c>；
///   * F-03（P2）：Mod 替换回滚失败时仍提示"已回滚到旧版本"；
///   * F-04（P2）：只读的 Mod 列表接口被"并发启动守卫"拦住，游戏运行时返回 400。
///
/// 全部使用合成临时目录与合成字节；HTTP 走真实 <see cref="LocalApiService"/> 与真实
/// <c>HttpClient</c>。**不启动真实游戏、不触碰真实配置/凭据/存档。**
/// </summary>
[Collection("LocalApiService")]
public sealed class FinalAcceptanceRegressionTests
{
	private readonly ITestOutputHelper _out;

	public FinalAcceptanceRegressionTests(ITestOutputHelper output) => _out = output;

	/* ===================== 夹具 ===================== */

	private sealed class Fixture : IDisposable
	{
		public string Root { get; } = Path.Combine(Path.GetTempPath(), "pineglow-far-" + Guid.NewGuid().ToString("N"));
		public string Game => Path.Combine(Root, "game");
		public string Mods => Path.Combine(Game, "mods");
		public string Recovery { get; } = Path.Combine(Path.GetTempPath(), "pineglow-far-rec-" + Guid.NewGuid().ToString("N"));
		public string Outside => Path.Combine(Root, "outside");
		public string Outside2 => Path.Combine(Root, "outside2");
		public GameInstance Instance { get; }
		public AppConfig Config { get; }

		public Fixture()
		{
			Directory.CreateDirectory(Mods);
			Directory.CreateDirectory(Outside);
			Directory.CreateDirectory(Outside2);
			Directory.CreateDirectory(Recovery);
			Instance = new GameInstance
			{
				Id = "far-" + Guid.NewGuid().ToString("N"),
				Name = "far",
				GameDirectory = Game,
				MinecraftVersion = "1.20.4"
			};
			Config = new AppConfig
			{
				GameDirectory = Game,
				Instances = { Instance },
				SelectedInstanceId = Instance.Id
			};
			File.WriteAllText(Path.Combine(Mods, "a.jar"), "old-A");
			File.WriteAllText(Path.Combine(Mods, "b.jar"), "old-B");
		}

		public string Api(string suffix) => "/api/instances/" + Instance.Id + suffix;

		public void Dispose()
		{
			try { RunningInstanceRegistry.MarkExited(Instance.Id); } catch { }
			try { ModBisectService.Remove(Instance.Id); } catch { }
			BreakJunction(Mods);
			BreakJunction(Game);
			foreach (string dir in new[] { Root, Recovery })
			{
				try
				{
					if (!Directory.Exists(dir))
					{
						continue;
					}

					foreach (string file in Directory.GetFiles(dir, "*", SearchOption.AllDirectories))
					{
						try { File.SetAttributes(file, FileAttributes.Normal); } catch { }
					}

					Directory.Delete(dir, true);
				}
				catch
				{
				}
			}
		}
	}

	/// <summary>只删除联接本身（绝不递归进目标）。</summary>
	private static void BreakJunction(string path)
	{
		try
		{
			if (Directory.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
			{
				Directory.Delete(path, false);
			}
		}
		catch
		{
		}
	}

	/// <summary>
	/// 在 <paramref name="link"/> 位置造一个重解析点（产品判定口径见 FileAttributes.ReparsePoint）。
	/// Windows 用目录联接（New-Item -ItemType Junction，无需特权）；
	/// POSIX 用目录符号链接（Directory.CreateSymbolicLink）—— .NET 在 Unix 上对符号链接
	/// 置 ReparsePoint 位，因此对产品是同一个"链上有重解析点"场景。
	/// </summary>
	private static async Task<bool> TryCreateJunctionAsync(string link, string target)
	{
		if (Directory.Exists(link))
		{
			Directory.Delete(link, true);
		}

		if (!OperatingSystem.IsWindows())
		{
			try
			{
				Directory.CreateSymbolicLink(link, target);
			}
			catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
			{
				Console.WriteLine("symlink setup failed: " + ex.Message);
				return false;
			}

			return (File.GetAttributes(link) & FileAttributes.ReparsePoint) != 0;
		}

		var psi = new ProcessStartInfo("powershell.exe")
		{
			UseShellExecute = false,
			CreateNoWindow = true,
			RedirectStandardOutput = true,
			RedirectStandardError = true
		};
		psi.ArgumentList.Add("-NoProfile");
		psi.ArgumentList.Add("-NonInteractive");
		psi.ArgumentList.Add("-Command");
		psi.ArgumentList.Add($"New-Item -ItemType Junction -Path '{link}' -Target '{target}' -ErrorAction Stop | Out-Null");
		using Process p = Process.Start(psi)!;
		string stdout = await p.StandardOutput.ReadToEndAsync();
		string stderr = await p.StandardError.ReadToEndAsync();
		await p.WaitForExitAsync();
		if (p.ExitCode != 0)
		{
			Console.WriteLine("junction setup failed: " + stdout + stderr);
			return false;
		}

		return (File.GetAttributes(link) & FileAttributes.ReparsePoint) != 0;
	}

	private static string Sha(string path)
	{
		using FileStream fs = File.OpenRead(path);
		return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(fs));
	}

	private static string EntryState(string body, string fileName)
	{
		foreach (JsonElement f in JsonDocument.Parse(body).RootElement.GetProperty("files").EnumerateArray())
		{
			string? relative = f.GetProperty("relativePath").GetString();
			if (relative != null
				&& (string.Equals(relative, fileName, StringComparison.OrdinalIgnoreCase)
					|| relative.EndsWith(fileName, StringComparison.OrdinalIgnoreCase)))
			{
				return f.GetProperty("state").GetString()!;
			}
		}

		throw new Xunit.Sdk.XunitException("响应里找不到文件项：" + fileName + " / " + body);
	}

	/// <summary>HTTP 错误体里的中文会被 JSON 转义，断言前先解码。</summary>
	private static string DecodeError(string body)
	{
		try
		{
			JsonElement root = JsonDocument.Parse(body).RootElement;
			foreach (string name in new[] { "error", "message" })
			{
				if (root.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String)
				{
					return value.GetString() ?? string.Empty;
				}
			}
		}
		catch (JsonException)
		{
		}

		return body;
	}

	private static void RewriteRecord(FileRecoveryService service, FileRecoveryRecord record)
	{
		string dir = service.GetOperationDirectory(record.InstanceId, record.OperationId);
		string path = Path.Combine(dir, "record.json");
		var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };
		File.WriteAllText(path, JsonSerializer.Serialize(record, options));
	}

	/* ===================== F-01 / R1：服务级 junction ===================== */

	[Fact]
	public async Task Restore_ServiceLevel_RejectsJunctionAncestor_AndKeepsIsolatedFile()
	{
		using var fx = new Fixture();
		string jar = Path.Combine(fx.Mods, "a.jar");
		string originalSha = Sha(jar);
		File.Delete(Path.Combine(fx.Mods, "b.jar"));

		var service = new FileRecoveryService(fx.Recovery);
		FileRecoveryRecord record = await service.QuarantineAsync(fx.Instance.Id, fx.Game, "acceptance", new[] { jar });
		Assert.Equal("quarantined", record.Status);
		string storedPath = record.Files[0].StoredPath!;

		// 把 game\mods 换成指向游戏目录之外的联接
		Directory.Delete(fx.Mods);
		Assert.True(await TryCreateJunctionAsync(fx.Mods, fx.Outside), "本机应能创建目录联接");

		FileRecoveryRecord restored = await service.RestoreAsync(record);

		_out.WriteLine($"[F-01] state={restored.Status} / {restored.Files[0].State} / {restored.Files[0].Error}；" +
			$"outsideExists={File.Exists(Path.Combine(fx.Outside, "a.jar"))}；storedExists={File.Exists(storedPath)}");

		Assert.False(File.Exists(Path.Combine(fx.Outside, "a.jar")), "恢复不得穿过联接写到游戏目录之外");
		Assert.Equal("failed", restored.Files[0].State);
		Assert.Equal("failed", restored.Status);
		Assert.Contains("重解析点", restored.Files[0].Error ?? string.Empty);
		Assert.True(File.Exists(storedPath), "被拒绝时隔离副本必须原样保留，供人工再次恢复");
		Assert.Equal(originalSha, Sha(storedPath));
	}

	/* ===================== F-01 / R1：HTTP 级 junction ===================== */

	[Fact]
	public async Task Restore_HttpLevel_RejectsJunctionAncestor_AndReportsOkFalse()
	{
		using var fx = new Fixture();
		await using LocalApiTestHarness h = await LocalApiTestHarness.StartAsync(fx.Config);

		(int status, string body) = await h.SendAsync(HttpMethod.Post, fx.Api("/mods/a.jar/delete"));
		Assert.Equal(200, status);
		string operationId = JsonDocument.Parse(body).RootElement.GetProperty("operationId").GetString()!;

		File.Delete(Path.Combine(fx.Mods, "b.jar"));
		Directory.Delete(fx.Mods);
		Assert.True(await TryCreateJunctionAsync(fx.Mods, fx.Outside), "本机应能创建目录联接");

		(status, body) = await h.SendAsync(HttpMethod.Post, fx.Api("/recovery/" + operationId + "/restore"));
		_out.WriteLine($"[F-01/api] restore => {status}: {body}；outsideExists={File.Exists(Path.Combine(fx.Outside, "a.jar"))}");

		Assert.False(File.Exists(Path.Combine(fx.Outside, "a.jar")), "HTTP 恢复不得穿过联接写到游戏目录之外");
		JsonElement root = JsonDocument.Parse(body).RootElement;
		Assert.False(root.GetProperty("ok").GetBoolean(), "越界写入被拒绝时必须 ok=false");
		Assert.Equal("failed", root.GetProperty("status").GetString());
		Assert.Equal("failed", EntryState(body, "a.jar"));
	}

	/* ===================== F-01 / R1：伪造记录（恢复区之外 / 游戏目录之外） ===================== */

	[Fact]
	public async Task Restore_RejectsStoredPathOutsideRecoveryRoot()
	{
		using var fx = new Fixture();
		string jar = Path.Combine(fx.Mods, "a.jar");
		File.Delete(Path.Combine(fx.Mods, "b.jar"));

		var service = new FileRecoveryService(fx.Recovery);
		FileRecoveryRecord record = await service.QuarantineAsync(fx.Instance.Id, fx.Game, "mod-delete", new[] { jar });

		// 伪造：把隔离副本指到恢复区之外的任意文件
		string foreign = Path.Combine(fx.Outside, "foreign.jar");
		File.WriteAllText(foreign, "FOREIGN-BYTES");
		File.Delete(record.Files[0].StoredPath!);
		record.Files[0].StoredPath = foreign;
		RewriteRecord(service, record);

		FileRecoveryRecord? reloaded = await service.LoadAsync(fx.Instance.Id, record.OperationId);
		Assert.NotNull(reloaded);
		FileRecoveryRecord restored = await service.RestoreAsync(reloaded!);

		_out.WriteLine($"[F-01/forged-stored] state={restored.Files[0].State} error={restored.Files[0].Error}");
		Assert.Equal("failed", restored.Files[0].State);
		Assert.Contains("隔离副本", restored.Files[0].Error ?? string.Empty);
		Assert.False(File.Exists(jar), "拒绝时必须什么都不写");
		Assert.True(File.Exists(foreign), "恢复区之外的文件不得被搬走");
		Assert.Equal("FOREIGN-BYTES", File.ReadAllText(foreign));
	}

	[Fact]
	public async Task Restore_RejectsForgedOriginalPathOutsideGameRoot()
	{
		using var fx = new Fixture();
		string jar = Path.Combine(fx.Mods, "a.jar");
		File.Delete(Path.Combine(fx.Mods, "b.jar"));

		var service = new FileRecoveryService(fx.Recovery);
		FileRecoveryRecord record = await service.QuarantineAsync(fx.Instance.Id, fx.Game, "mod-delete", new[] { jar });

		string forged = Path.Combine(fx.Outside, "forged-target.jar");
		record.Files[0].OriginalPath = forged;
		RewriteRecord(service, record);

		FileRecoveryRecord? reloaded = await service.LoadAsync(fx.Instance.Id, record.OperationId);
		FileRecoveryRecord restored = await service.RestoreAsync(reloaded!);

		_out.WriteLine($"[F-01/forged-original] state={restored.Files[0].State} error={restored.Files[0].Error}");
		Assert.Equal("failed", restored.Files[0].State);
		Assert.Contains("越界", restored.Files[0].Error ?? string.Empty);
		Assert.False(File.Exists(forged));
	}

	[Fact]
	public async Task Restore_RejectsWhenGameRootItselfIsAReparsePoint()
	{
		using var fx = new Fixture();
		string jar = Path.Combine(fx.Mods, "a.jar");
		string originalSha = Sha(jar);
		File.Delete(Path.Combine(fx.Mods, "b.jar"));

		var service = new FileRecoveryService(fx.Recovery);
		FileRecoveryRecord record = await service.QuarantineAsync(fx.Instance.Id, fx.Game, "mod-delete", new[] { jar });
		string storedPath = record.Files[0].StoredPath!;

		Directory.Delete(fx.Game, true);
		Assert.True(await TryCreateJunctionAsync(fx.Game, fx.Outside2), "本机应能创建目录联接");

		FileRecoveryRecord restored = await service.RestoreAsync(record);

		_out.WriteLine($"[F-01/root-reparse] state={restored.Files[0].State} error={restored.Files[0].Error}");
		Assert.Equal("failed", restored.Files[0].State);
		Assert.Contains("重解析点", restored.Files[0].Error ?? string.Empty);
		Assert.False(File.Exists(Path.Combine(fx.Outside2, "mods", "a.jar")), "不得穿过游戏根联接写入");
		Assert.True(File.Exists(storedPath));
		Assert.Equal(originalSha, Sha(storedPath));
	}

	/* ===================== R1：正常路径不得退化 ===================== */

	[Fact]
	public async Task Restore_DefaultDoesNotOverwrite_ButExplicitOverwriteRestoresByteIdenticalContent()
	{
		using var fx = new Fixture();
		string jar = Path.Combine(fx.Mods, "a.jar");
		string originalSha = Sha(jar);
		File.Delete(Path.Combine(fx.Mods, "b.jar"));

		var service = new FileRecoveryService(fx.Recovery);
		FileRecoveryRecord record = await service.QuarantineAsync(fx.Instance.Id, fx.Game, "mod-delete", new[] { jar });

		// 目标位置被写入不同内容
		File.WriteAllText(jar, "SOMETHING-ELSE");
		FileRecoveryRecord conflicted = await service.RestoreAsync(record);
		Assert.Equal("conflict", conflicted.Files[0].State);
		Assert.Equal("SOMETHING-ELSE", File.ReadAllText(jar));
		Assert.True(File.Exists(record.Files[0].StoredPath!), "冲突时隔离副本必须保留");

		FileRecoveryRecord overwritten = await service.RestoreAsync(conflicted, overwrite: true);
		_out.WriteLine($"[R1/overwrite] state={overwritten.Files[0].State} status={overwritten.Status}");
		Assert.Equal("restored", overwritten.Files[0].State);
		Assert.Equal("restored", overwritten.Status);
		Assert.Equal(originalSha, Sha(jar));
		Assert.False(File.Exists(record.Files[0].StoredPath!), "恢复成功后隔离副本被消费");
	}

	[Fact]
	public async Task Restore_RecreatesMissingParentDirectories_InsideGameRoot()
	{
		using var fx = new Fixture();
		string jar = Path.Combine(fx.Mods, "a.jar");
		string originalSha = Sha(jar);
		File.Delete(Path.Combine(fx.Mods, "b.jar"));

		var service = new FileRecoveryService(fx.Recovery);
		FileRecoveryRecord record = await service.QuarantineAsync(fx.Instance.Id, fx.Game, "mod-delete", new[] { jar });
		Directory.Delete(fx.Mods);

		FileRecoveryRecord restored = await service.RestoreAsync(record);

		_out.WriteLine($"[R1/missing-parent] state={restored.Files[0].State}");
		Assert.Equal("restored", restored.Files[0].State);
		Assert.True(File.Exists(jar), "缺失的父目录应被重建在游戏目录之内");
		Assert.Equal(originalSha, Sha(jar));
	}

	[Fact]
	public async Task Restore_HandlesChineseSpaceHyphenNames_ForJarAndSidecar()
	{
		using var fx = new Fixture();
		string name = "世界 A-测试.jar";
		string jar = Path.Combine(fx.Mods, name);
		string sidecar = jar + ".mcmini.json";
		File.WriteAllText(jar, "chinese-jar-bytes");
		File.WriteAllText(sidecar, "{\"versionNumber\":\"1.0.0\"}");
		File.Delete(Path.Combine(fx.Mods, "b.jar"));
		string jarSha = Sha(jar);
		string sidecarSha = Sha(sidecar);

		var service = new FileRecoveryService(fx.Recovery);
		FileRecoveryRecord record = await service.QuarantineAsync(fx.Instance.Id, fx.Game, "mod-delete", new[] { jar, sidecar });
		Assert.Equal("quarantined", record.Status);
		Assert.False(File.Exists(jar));

		FileRecoveryRecord restored = await service.RestoreAsync(record);

		_out.WriteLine($"[R1/unicode] status={restored.Status} files={string.Join(",", restored.Files.Select(x => x.State))}");
		Assert.Equal("restored", restored.Status);
		Assert.Equal(jarSha, Sha(jar));
		Assert.Equal(sidecarSha, Sha(sidecar));
	}

	/* ===================== F-03 / R2：回滚真实结果 ===================== */

	[Fact]
	public async Task Replace_WhenRollbackCannotRestore_ReportsNeedsRecovery_AndNeverSaysRolledBack()
	{
		using var fx = new Fixture();
		string target = Path.Combine(fx.Mods, "a.jar");
		string staged = Path.Combine(fx.Root, "staged-new.jar");
		File.WriteAllText(staged, "new-staged-bytes");
		var recovery = new FileRecoveryService(fx.Recovery);

		ModReplacementService.BeforeCommitForTest = path =>
		{
			// 让"回滚放不回旧文件"在 Windows 与 POSIX 上都成立：用**同名目录**占住目标路径。
			//
			// 旧写法（写一个只读的 foreign 文件）只在 Windows 上成立：
			//   - Windows：ReadOnly 属性让删除/覆盖失败，回滚真的失败；
			//   - POSIX：unlink 只看**父目录**写权限，只读文件照样被删 → 回滚成功、
			//     needsRecovery=false、错误信息真的可以说"已回滚"（CI 实测 recoveryStatus=restored），
			//     于是本条用例在 Linux 上不但失败，而且什么都没验到。
			// 文件名被目录占用是每种文件系统都拒绝写入的情况，两边行为一致。
			File.Delete(path);
			Directory.CreateDirectory(path);
			throw new IOException("injected commit failure");
		};
		ModReplacementResult result;
		try
		{
			result = await new ModReplacementService(recovery)
				.ReplaceAsync(fx.Instance, new ModService(fx.Game), "a.jar", staged);
		}
		finally
		{
			ModReplacementService.BeforeCommitForTest = null;
		}

		FileRecoveryRecord? record = await recovery.LoadAsync(fx.Instance.Id, result.OperationId!);
		_out.WriteLine($"[F-03] status={result.Status} ok={result.Ok} needsRecovery={result.NeedsRecovery} " +
			$"recoveryStatus={result.RecoveryStatus} error={result.Error}");
		_out.WriteLine($"[F-03] targetOccupiedByDir={Directory.Exists(target)} recoveryStatus={record?.Status} " +
			$"storedStillThere={record?.Files.Any(x => x.StoredPath != null && File.Exists(x.StoredPath))}");

		Assert.False(result.Ok);
		Assert.Equal("commit_failed", result.Status);
		Assert.DoesNotContain("已回滚", result.Error ?? string.Empty);
		Assert.True(result.NeedsRecovery, "回滚没能把旧文件放回去时必须标记 needsRecovery");
		Assert.False(string.IsNullOrWhiteSpace(result.OperationId));
		Assert.NotEmpty(result.FailedFiles);
		Assert.Contains("未能完全回滚", result.Error ?? string.Empty);
		Assert.Contains("人工恢复", result.Error ?? string.Empty);
		Assert.NotNull(record);
		Assert.True(record!.Files.Any(x => x.StoredPath != null && File.Exists(x.StoredPath)), "旧文件必须仍留在恢复区");
		Assert.True(Directory.Exists(target), "占位的外来内容必须原样留在原位置（回滚没能覆盖它）");

		// 清掉占位目录后，必须仍然可以再次恢复（隔离副本没被消耗）
		Directory.Delete(target, true);
		FileRecoveryRecord retry = await recovery.RestoreAsync(record, overwrite: true);
		_out.WriteLine($"[R2/retry] status={retry.Status} states={string.Join(",", retry.Files.Select(x => x.State))}");
		Assert.Equal("restored", retry.Status);
		Assert.Equal("old-A", File.ReadAllText(target));
	}

	[Fact]
	public async Task Replace_WhenRollbackSucceeds_StillReportsTheRealRollback()
	{
		using var fx = new Fixture();
		string target = Path.Combine(fx.Mods, "a.jar");
		string sidecar = target + ".mcmini.json";
		File.WriteAllText(sidecar, "{\"versionNumber\":\"0.9.0\"}");
		string jarSha = Sha(target);
		string sidecarSha = Sha(sidecar);
		string staged = Path.Combine(fx.Root, "staged-new.jar");
		File.WriteAllText(staged, "new-staged-bytes");
		var recovery = new FileRecoveryService(fx.Recovery);

		ModReplacementService.BeforeCommitForTest = _ => throw new IOException("injected commit failure");
		ModReplacementResult result;
		try
		{
			result = await new ModReplacementService(recovery)
				.ReplaceAsync(fx.Instance, new ModService(fx.Game), "a.jar", staged);
		}
		finally
		{
			ModReplacementService.BeforeCommitForTest = null;
		}

		_out.WriteLine($"[R2/success-rollback] status={result.Status} needsRecovery={result.NeedsRecovery} " +
			$"recoveryStatus={result.RecoveryStatus} error={result.Error}");

		Assert.False(result.Ok);
		Assert.Equal("commit_failed", result.Status);
		Assert.False(result.NeedsRecovery, "逐项都恢复成功时不得报 needsRecovery");
		Assert.Equal("restored", result.RecoveryStatus);
		Assert.Empty(result.FailedFiles);
		Assert.Contains("已回滚", result.Error ?? string.Empty);
		Assert.Equal(jarSha, Sha(target));
		Assert.Equal(sidecarSha, Sha(sidecar));
	}

	/* ===================== F-04 / R3：只读列表 vs 危险变更 ===================== */

	[Fact]
	public async Task ListMods_IsAllowedWhileInstanceRuns_ButDangerousMutationsAreStillRejected()
	{
		using var fx = new Fixture();
		await using LocalApiTestHarness h = await LocalApiTestHarness.StartAsync(fx.Config);

		RunningInstanceRegistry.MarkRunningPid(fx.Instance.Id, Environment.ProcessId);
		try
		{
			(int status, string body) = await h.SendAsync(HttpMethod.Get, fx.Api("/mods"));
			_out.WriteLine($"[F-04] running GET mods => {status}: {body}");
			Assert.Equal(200, status);
			Assert.Contains("a.jar", body);
			Assert.Equal("old-A", File.ReadAllText(Path.Combine(fx.Mods, "a.jar")));

			// 危险变更必须仍然被拒（守卫只能收窄到只读路由，不能整体删除）
			(int toggleStatus, string toggleBody) = await h.SendAsync(HttpMethod.Post, fx.Api("/mods/a.jar/toggle"));
			_out.WriteLine($"[R3] running toggle => {toggleStatus}: {DecodeError(toggleBody)}");
			Assert.Equal(400, toggleStatus);
			Assert.Contains("请勿重复启动", DecodeError(toggleBody));

			(int deleteStatus, string deleteBody) = await h.SendAsync(HttpMethod.Post, fx.Api("/mods/a.jar/delete"));
			_out.WriteLine($"[R3] running delete => {deleteStatus}: {DecodeError(deleteBody)}");
			Assert.Equal(400, deleteStatus);
			Assert.Contains("请勿重复启动", DecodeError(deleteBody));

			(int replaceStatus, string replaceBody) = await h.SendAsync(HttpMethod.Post, fx.Api("/mods/a.jar/replace"),
				new { newFileBase64 = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("new")) });
			_out.WriteLine($"[R3] running replace => {replaceStatus}: {DecodeError(replaceBody)}");
			Assert.Equal(400, replaceStatus);
			Assert.Contains("请勿重复启动", DecodeError(replaceBody));

			(int removeStatus, string removeBody) = await h.SendAsync(HttpMethod.Post, fx.Api("/delete"));
			_out.WriteLine($"[R3] running instance delete => {removeStatus}: {DecodeError(removeBody)}");
			Assert.Equal(400, removeStatus);
			Assert.Contains("正在运行", DecodeError(removeBody));

			Assert.Equal("old-A", File.ReadAllText(Path.Combine(fx.Mods, "a.jar")));
			Assert.Equal("old-B", File.ReadAllText(Path.Combine(fx.Mods, "b.jar")));
		}
		finally
		{
			RunningInstanceRegistry.MarkExited(fx.Instance.Id);
		}
	}

	/* ===================== R0：静态守卫（原生调用只允许存在于后端文件里） ===================== */

	[Fact]
	public void SecretStoreSource_ContainsNoInlineNativeCredentialCalls()
	{
		string services = FindServicesDirectory();
		string secretStore = File.ReadAllText(Path.Combine(services, "SecretStore.cs"));
		string backend = File.ReadAllText(Path.Combine(services, "SecretStoreBackend.cs"));

		foreach (string native in new[] { "CredReadW", "CredWriteW", "CredDeleteW", "DllImport" })
		{
			Assert.DoesNotContain(native, secretStore, StringComparison.Ordinal);
		}

		// 反向证明：这不是"两边都没有"的空守卫
		Assert.Contains("CredReadW", backend, StringComparison.Ordinal);
		Assert.Contains("CredWriteW", backend, StringComparison.Ordinal);
		Assert.Contains("CredDeleteW", backend, StringComparison.Ordinal);

		// 目标前缀与持久化常量属于落盘格式，必须仍在
		Assert.Contains("MCMiniLauncher/", backend, StringComparison.Ordinal);
		_out.WriteLine("[R0] SecretStore.cs 无内联原生调用；SecretStoreBackend.cs 保留原生实现与目标前缀");
	}

	private static string FindServicesDirectory()
	{
		string dir = AppContext.BaseDirectory;
		for (int i = 0; i < 8 && dir != null; i++)
		{
			string candidate = Path.Combine(dir, "dev", "PineglowLauncher", "Pineglow.Launcher.Services");
			if (Directory.Exists(candidate))
			{
				return candidate;
			}

			dir = Path.GetDirectoryName(dir)!;
		}

		throw new DirectoryNotFoundException("找不到 Pineglow.Launcher.Services 源码目录");
	}
}
