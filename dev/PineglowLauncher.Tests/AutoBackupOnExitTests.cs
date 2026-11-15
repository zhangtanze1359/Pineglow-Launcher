using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Pineglow.Launcher.Models;
using Pineglow.Launcher.Services;
using Xunit;

namespace Pineglow.Launcher.Tests;

/// <summary>
/// D3②「退出后自动备份」的直测。全部在临时目录里跑真实文件系统，零 mock。
///
/// 三条不变量（失败时比成功时更重要）：
///   1. 开关关闭 → 一条备份都不产生；
///   2. 备份失败 → **源存档内容与 mtime 不变**（备份永远不许动源）；
///   3. 备份失败 → **backups/ 下不留半成品**（`.partial` / 空 zip 都不许留）。
///
/// 判定与 IO 分离：`AutoBackupPlanner` 是纯的，所以前三条用例不用碰磁盘；
/// 后三条通过 `AutoBackupService` 走真实备份链路。
/// </summary>
public sealed class AutoBackupOnExitTests : IDisposable
{
	private readonly string _root;
	private readonly string _gameDir;
	private readonly GameInstance _instance;

	public AutoBackupOnExitTests()
	{
		_root = Path.Combine(Path.GetTempPath(), "pg-autobackup-" + Guid.NewGuid().ToString("N").Substring(0, 8));
		_gameDir = Path.Combine(_root, "game");
		Directory.CreateDirectory(Path.Combine(_gameDir, "saves"));
		_instance = new GameInstance
		{
			Id = "inst-autobackup",
			Name = "autobackup",
			GameDirectory = _gameDir,
			MinecraftVersion = "1.20.4"
		};
	}

	public void Dispose()
	{
		try
		{
			if (Directory.Exists(_root))
			{
				Directory.Delete(_root, recursive: true);
			}
		}
		catch
		{
		}
	}

	private string CreateSave(string folderName, string levelDatContent, int extraFiles = 0)
	{
		string dir = Path.Combine(_gameDir, "saves", folderName);
		Directory.CreateDirectory(dir);
		File.WriteAllText(Path.Combine(dir, "level.dat"), levelDatContent);
		for (int i = 0; i < extraFiles; i++)
		{
			File.WriteAllText(Path.Combine(dir, $"region-{i}.dat"), new string('x', 64));
		}
		return dir;
	}

	private AppConfig ConfigWith(bool autoBackupOnExit)
	{
		_instance.AutoBackupOnExit = autoBackupOnExit;
		return new AppConfig
		{
			Instances = new List<GameInstance> { _instance },
			SelectedInstanceId = _instance.Id
		};
	}

	private string BackupsDir => Path.Combine(_gameDir, "backups");

	/* ===================== 判定面（纯函数，不碰磁盘） ===================== */

	[Fact]
	public void Plan_SwitchOff_DoesNotRun()
	{
		List<SaveInfo> saves = new List<SaveInfo>
		{
			new SaveInfo { FolderName = "worldA", SizeBytes = 1024 },
			new SaveInfo { FolderName = "worldB", SizeBytes = 2048 }
		};

		AutoBackupPlan plan = AutoBackupPlanner.Plan(false, saves, DateTimeOffset.Now);

		Assert.False(plan.ShouldRun);
		Assert.Equal("disabled", plan.Reason);
		Assert.Empty(plan.TargetFolders);
	}

	[Fact]
	public void Plan_SwitchOn_SkipsEmptySave_AndKeepsTheRest()
	{
		List<SaveInfo> saves = new List<SaveInfo>
		{
			new SaveInfo { FolderName = "worldA", SizeBytes = 1024 },
			new SaveInfo { FolderName = "worldEmpty", SizeBytes = 0 },
			new SaveInfo { FolderName = "worldB", SizeBytes = 2048 }
		};

		AutoBackupPlan plan = AutoBackupPlanner.Plan(true, saves, DateTimeOffset.Now);

		Assert.True(plan.ShouldRun);
		Assert.Equal("planned", plan.Reason);
		Assert.Equal(new[] { "worldA", "worldB" }, plan.TargetFolders);
	}

	[Fact]
	public void Plan_SwitchOn_ButNoSaves_DoesNotRun()
	{
		AutoBackupPlan plan = AutoBackupPlanner.Plan(true, Array.Empty<SaveInfo>(), DateTimeOffset.Now);

		Assert.False(plan.ShouldRun);
		Assert.Equal("no-saves", plan.Reason);
	}

	/* ===================== IO 面（真实备份链路） ===================== */

	[Fact]
	public void Service_SwitchOn_BacksUpEveryNonEmptySave()
	{
		CreateSave("worldA", "level-a", extraFiles: 2);
		CreateSave("worldB", "level-b", extraFiles: 1);
		CreateSave("worldEmpty", string.Empty);

		AutoBackupOutcome outcome = AutoBackupService.TryBackupOnExit(
			ConfigWith(true), _instance.Id, _gameDir, DateTimeOffset.Now);

		Assert.True(outcome.Ran);
		Assert.Equal("ok", outcome.Reason);
		Assert.Empty(outcome.FailedFolders);
		Assert.Equal(2, outcome.BackedUpPaths.Count);
		Assert.All(outcome.BackedUpPaths, path => Assert.True(File.Exists(path)));

		List<string> zips = Directory.Exists(BackupsDir)
			? Directory.EnumerateFiles(BackupsDir, "*.zip", SearchOption.AllDirectories).ToList()
			: new List<string>();
		Assert.Equal(2, zips.Count);
		Assert.DoesNotContain(zips, z => z.Contains("worldEmpty", StringComparison.Ordinal));
	}

	[Fact]
	public void Service_SwitchOff_ProducesNoBackupFile()
	{
		CreateSave("worldA", "level-a", extraFiles: 1);

		AutoBackupOutcome outcome = AutoBackupService.TryBackupOnExit(
			ConfigWith(false), _instance.Id, _gameDir, DateTimeOffset.Now);

		Assert.False(outcome.Ran);
		Assert.Equal("disabled", outcome.Reason);
		Assert.False(Directory.Exists(BackupsDir) && Directory.EnumerateFiles(BackupsDir, "*.zip", SearchOption.AllDirectories).Any());
	}

	/// <summary>
	/// 失败路径 A —— **源未损坏**。
	/// 让 worldB 的 level.dat 被独占锁定（zip 读到一半会失败），然后断言：
	/// worldA 仍然被备份、worldB 记入失败，且 worldB 的内容与 mtime **一个字节都没变**。
	/// </summary>
	[Fact]
	public void Service_OneSaveFails_SourceUntouched_AndOthersStillBackedUp()
	{
		CreateSave("worldA", "level-a", extraFiles: 2);
		string worldB = CreateSave("worldB", "level-b", extraFiles: 1);
		string levelB = Path.Combine(worldB, "level.dat");
		DateTime mtimeBefore = File.GetLastWriteTimeUtc(levelB);
		byte[] bytesBefore = File.ReadAllBytes(levelB);

		AutoBackupOutcome outcome;
		using (FileStream hold = new FileStream(levelB, FileMode.Open, FileAccess.Read, FileShare.None))
		{
			outcome = AutoBackupService.TryBackupOnExit(
				ConfigWith(true), _instance.Id, _gameDir, DateTimeOffset.Now);
		}

		Assert.True(outcome.Ran);
		Assert.Equal("partial", outcome.Reason);
		Assert.Contains("worldB", outcome.FailedFolders);
		Assert.DoesNotContain("worldB", outcome.FailedFolders.Where(f => f == "worldA").ToList());
		Assert.Single(outcome.BackedUpPaths);

		Assert.True(File.Exists(levelB), "备份失败后源存档必须还在");
		Assert.Equal(bytesBefore, File.ReadAllBytes(levelB));
		Assert.Equal(mtimeBefore, File.GetLastWriteTimeUtc(levelB));
	}

	/// <summary>
	/// 失败路径 B —— **失败可回滚**：失败项不许在 backups/ 下留下 `.partial` 或空 zip；
	/// 已存在的旧备份仍然可枚举、可恢复。
	/// </summary>
	[Fact]
	public void Service_OneSaveFails_LeavesNoPartialArtifact_AndOldBackupStillUsable()
	{
		CreateSave("worldA", "level-a", extraFiles: 1);
		string worldB = CreateSave("worldB", "level-b", extraFiles: 1);
		string levelB = Path.Combine(worldB, "level.dat");

		// 先造一个"昨天的备份"，用来证明失败不会破坏既有备份
		SaveBackupService backups = new SaveBackupService(_gameDir);
		SaveInfo saveB = new SaveInfo { FolderName = "worldB", FullPath = worldB };
		string oldBackup = backups.CreateBackup(_instance, saveB);

		AutoBackupOutcome outcome;
		using (FileStream hold = new FileStream(levelB, FileMode.Open, FileAccess.Read, FileShare.None))
		{
			outcome = AutoBackupService.TryBackupOnExit(
				ConfigWith(true), _instance.Id, _gameDir, DateTimeOffset.Now);
		}

		Assert.Contains("worldB", outcome.FailedFolders);

		List<string> partials = Directory.EnumerateFiles(BackupsDir, "*.partial", SearchOption.AllDirectories).ToList();
		Assert.Empty(partials);

		List<string> zipsInWorldB = Directory
			.EnumerateFiles(Path.Combine(BackupsDir, "worldB"), "*.zip", SearchOption.AllDirectories)
			.ToList();
		Assert.Single(zipsInWorldB);
		Assert.Equal(Path.GetFullPath(oldBackup), Path.GetFullPath(zipsInWorldB[0]));

		// 失败不会破坏既有备份：旧备份仍在列表里、且文件仍非空
		List<BackupFileInfo> all = backups.GetBackups(_instance);
		Assert.Contains(all, b => string.Equals(
			Path.GetFullPath(b.FullPath), Path.GetFullPath(oldBackup), StringComparison.OrdinalIgnoreCase));
		Assert.True(new FileInfo(oldBackup).Length > 0);
	}
}
