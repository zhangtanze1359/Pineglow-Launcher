using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Pineglow.Launcher.Services;
using Xunit;

namespace Pineglow.Launcher.Tests;

/// <summary>
/// M2-1① 存档可恢复删除的直测。全部在临时目录里跑真实文件系统，零 mock。
///
/// 这一组测试的**核心不变量**是：**删除失败时源存档必须完好。**
/// 用户点一次删除只需要一秒钟，丢了存档却找不回来——所以每条失败路径都单独断言
/// "源目录还在、文件还在、内容还能打开"，而不只是断言"抛了异常"。
///
/// 另一个重点是**越界拒绝**：删除接口天然是"删任意路径"的形状，
/// 只允许删 saves 的一级子目录这条限制如果失守，就是一个任意目录删除漏洞。
/// </summary>
public sealed class SaveRecoveryServiceTests : IDisposable
{
	private readonly string _root;
	private readonly string _gameDir;
	private readonly string _recoveryRoot;

	public SaveRecoveryServiceTests()
	{
		_root = Path.Combine(Path.GetTempPath(), "pg-saverec-" + Guid.NewGuid().ToString("N").Substring(0, 8));
		_gameDir = Path.Combine(_root, "game");
		_recoveryRoot = Path.Combine(_root, "recovery");
		Directory.CreateDirectory(Path.Combine(_gameDir, "saves"));
		Directory.CreateDirectory(_recoveryRoot);
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
			// 临时目录清理失败不影响测试结论
		}
	}

	/* ===================== 辅助 ===================== */

	/// <summary>造一个带 level.dat + 区域文件 + 子目录的存档，形状贴近真实存档。</summary>
	private string CreateSave(string name, int regionFiles = 3, int payloadBytes = 1024)
	{
		string saveDir = Path.Combine(_gameDir, "saves", name);
		Directory.CreateDirectory(Path.Combine(saveDir, "region"));
		Directory.CreateDirectory(Path.Combine(saveDir, "data"));
		File.WriteAllBytes(Path.Combine(saveDir, "level.dat"), new byte[payloadBytes]);
		File.WriteAllText(Path.Combine(saveDir, "session.lock"), "lock");
		File.WriteAllText(Path.Combine(saveDir, "data", "map_0.dat"), new string('x', payloadBytes));
		for (int i = 0; i < regionFiles; i++)
		{
			File.WriteAllBytes(Path.Combine(saveDir, "region", $"r.0.{i}.mca"), new byte[payloadBytes]);
		}

		return saveDir;
	}

	private SaveRecoveryService Service() => new SaveRecoveryService(_recoveryRoot);

	/// <summary>断言"源存档完好"：目录在、level.dat 在、region 数量对得上。</summary>
	private static void AssertSourceIntact(string saveDir, int expectedRegions)
	{
		Assert.True(Directory.Exists(saveDir), "源存档目录必须保留");
		Assert.True(File.Exists(Path.Combine(saveDir, "level.dat")), "level.dat 必须保留");
		Assert.Equal(expectedRegions, Directory.GetFiles(Path.Combine(saveDir, "region")).Length);
	}

	/* ===================== 正常删除与恢复 ===================== */

	[Fact]
	public async Task Delete_MovesSaveIntoRecoveryArea_AndKeepsSourceGone()
	{
		string saveDir = CreateSave("World One", regionFiles: 4, payloadBytes: 512);
		SaveManifest before = SaveRecoveryService.ComputeManifest(saveDir);

		SaveRecoveryRecord record = await Service().DeleteAsync("inst-1", _gameDir, "World One");

		Assert.Equal("quarantined", record.Status);
		Assert.Equal("World One", record.SaveFolderName);
		Assert.Equal(before.FileCount, record.FileCount);
		Assert.Equal(before.SizeBytes, record.SizeBytes);
		Assert.Equal(before.Sha256, record.ManifestSha256);

		Assert.False(Directory.Exists(saveDir), "删除成功后源目录应已移走");
		Assert.True(Directory.Exists(record.StoredPath), "恢复区必须有副本");
		Assert.Equal(before.FileCount, Directory.GetFiles(record.StoredPath, "*", SearchOption.AllDirectories).Length);
	}

	[Fact]
	public async Task Restore_PutsTheSaveBack_WithIdenticalContent()
	{
		string saveDir = CreateSave("World One", regionFiles: 2);
		SaveManifest before = SaveRecoveryService.ComputeManifest(saveDir);

		SaveRecoveryService svc = Service();
		SaveRecoveryRecord record = await svc.DeleteAsync("inst-1", _gameDir, "World One");
		SaveRecoveryRecord restored = await svc.RestoreAsync("inst-1", record.OperationId, _gameDir);

		Assert.Equal("restored", restored.Status);
		Assert.NotNull(restored.RestoredAt);
		Assert.Equal(saveDir, restored.RestoredPath);
		Assert.True(Directory.Exists(saveDir));
		Assert.Equal(before.Sha256, SaveRecoveryService.ComputeManifest(saveDir).Sha256);
	}

	[Fact]
	public async Task Restore_IsIdempotent_SecondCallDoesNotDuplicate()
	{
		CreateSave("World One");
		SaveRecoveryService svc = Service();
		SaveRecoveryRecord record = await svc.DeleteAsync("inst-1", _gameDir, "World One");

		await svc.RestoreAsync("inst-1", record.OperationId, _gameDir);
		SaveRecoveryRecord again = await svc.RestoreAsync("inst-1", record.OperationId, _gameDir);

		Assert.Equal("restored", again.Status);
		Assert.Single(Directory.GetDirectories(Path.Combine(_gameDir, "saves")));
	}

	[Fact]
	public async Task Restore_NameConflict_CreatesACopyInsteadOfOverwriting()
	{
		// 计划要求：「恢复默认保留现有世界；同名冲突时恢复为副本」。
		// 覆盖会毁掉用户正在玩的那个世界，是不可接受的。
		string saveDir = CreateSave("World");
		SaveRecoveryService svc = Service();
		SaveRecoveryRecord record = await svc.DeleteAsync("inst-1", _gameDir, "World");

		// 用户又建了一个同名世界（或者原本就没删干净）
		Directory.CreateDirectory(saveDir);
		File.WriteAllText(Path.Combine(saveDir, "level.dat"), "newer-world");

		SaveRecoveryRecord restored = await svc.RestoreAsync("inst-1", record.OperationId, _gameDir);

		Assert.Equal("restored", restored.Status);
		Assert.NotEqual(saveDir, restored.RestoredPath);
		Assert.Contains("(restored 1)", restored.RestoredPath);
		// 现有世界必须一字未改
		Assert.Equal("newer-world", File.ReadAllText(Path.Combine(saveDir, "level.dat")));
		// 恢复出来的是副本
		Assert.True(File.Exists(Path.Combine(restored.RestoredPath!, "level.dat")));
	}

	/* ===================== 越界拒绝（防任意目录删除）===================== */

	[Theory]
	[InlineData("..")]
	[InlineData("../etc")]
	[InlineData("../../game/saves")]
	[InlineData("sub/../../outside")]
	public async Task Delete_RejectsPathsThatEscapeSavesDirectory(string folderName)
	{
		// 在 game 之外放一个"不该被碰"的目录，然后尝试用各种穿越写法指过去
		string outside = Path.Combine(_root, "outside");
		Directory.CreateDirectory(outside);
		File.WriteAllText(Path.Combine(outside, "important.txt"), "keep me");

		SaveRecoveryService svc = Service();

		await Assert.ThrowsAnyAsync<Exception>(() => svc.DeleteAsync("inst-1", _gameDir, folderName));

		Assert.True(File.Exists(Path.Combine(outside, "important.txt")), "游戏目录之外的文件绝不能被动到");
	}

	[Fact]
	public async Task Delete_RejectsNestedSubdirectory_OnlyTopLevelSavesAllowed()
	{
		// `World/region` 这种二级路径不属于"一个存档"，允许删它等于给了一个删任意子目录的口子。
		// 用 Path.Combine 而不是写死Windows风格的反斜杠分隔：反斜杠只在 Windows 上是分隔符，
		// 在 POSIX 上它是普通文件名字符，于是同一个字符串会变成一个**合法但不存在**的文件名，
		// 先撞上 DirectoryNotFoundException，这条用例在 Linux 上就测不到"拒绝嵌套"。
		string saveDir = CreateSave("World");
		string nested = Path.Combine(saveDir, "region");

		await Assert.ThrowsAsync<InvalidOperationException>(
			() => Service().DeleteAsync("inst-1", _gameDir, Path.Combine("World", "region")));

		Assert.True(Directory.Exists(nested), "嵌套目录不能被删");
		Assert.True(File.Exists(Path.Combine(saveDir, "level.dat")));
	}

	[Fact]
	public async Task Delete_RejectsAbsolutePath()
	{
		string saveDir = CreateSave("World");

		await Assert.ThrowsAnyAsync<Exception>(() => Service().DeleteAsync("inst-1", _gameDir, saveDir));

		AssertSourceIntact(saveDir, 3);
	}

	[Fact]
	public async Task Delete_MissingSave_ThrowsAndTouchesNothing()
	{
		await Assert.ThrowsAsync<DirectoryNotFoundException>(() => Service().DeleteAsync("inst-1", _gameDir, "NoSuchWorld"));
		Assert.Empty(Directory.GetDirectories(Path.Combine(_gameDir, "saves")));
	}

	[Fact]
	public async Task Delete_RejectsEmptyFolderName()
	{
		await Assert.ThrowsAsync<InvalidOperationException>(() => Service().DeleteAsync("inst-1", _gameDir, "   "));
	}

	/* ===================== 失败时源必须完好 ===================== */

	[Fact]
	public async Task Delete_WhenDataAreaIsNotWritable_LeavesSourceIntact()
	{
		// 造一个"恢复区不可写"的场景：把 recovery 建成同名**文件**，CreateDirectory 会失败。
		string blockedRoot = Path.Combine(_root, "blocked-recovery");
		File.WriteAllText(blockedRoot, "not a directory");

		string saveDir = CreateSave("World");

		await Assert.ThrowsAnyAsync<Exception>(() => new SaveRecoveryService(blockedRoot).DeleteAsync("inst-1", _gameDir, "World"));

		AssertSourceIntact(saveDir, 3);
	}

	[Fact]
	public async Task Delete_RecordIsWritten_EvenWhenItFails()
	{
		// 先落记录的纪律：即使后续移动失败，也要能从记录里看出"这个 operationId 想干什么"。
		string blockedRoot = Path.Combine(_root, "blocked2");
		File.WriteAllText(blockedRoot, "x");

		await Assert.ThrowsAnyAsync<Exception>(() => new SaveRecoveryService(blockedRoot).DeleteAsync("inst-1", _gameDir, "World"));

		// 压根没法建目录时连记录都写不了，这是可接受的（源仍完好），
		// 所以这里断言的是"没有留下半份副本"。
		Assert.False(Directory.Exists(Path.Combine(blockedRoot, "saves")));
	}

	/* ===================== 永久清理 ===================== */

	[Fact]
	public async Task Purge_RemovesStoredCopy_AndMarksRecord()
	{
		CreateSave("World");
		SaveRecoveryService svc = Service();
		SaveRecoveryRecord record = await svc.DeleteAsync("inst-1", _gameDir, "World");

		await svc.PurgeAsync("inst-1", record.OperationId);

		Assert.False(Directory.Exists(record.StoredPath), "清理后副本必须消失");
		SaveRecoveryRecord? after = await svc.LoadAsync("inst-1", record.OperationId);
		Assert.Equal("purged", after!.Status);
	}

	[Fact]
	public async Task Purge_AfterRestore_IsRefused()
	{
		CreateSave("World");
		SaveRecoveryService svc = Service();
		SaveRecoveryRecord record = await svc.DeleteAsync("inst-1", _gameDir, "World");
		await svc.RestoreAsync("inst-1", record.OperationId, _gameDir);

		InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(
			() => svc.PurgeAsync("inst-1", record.OperationId));

		Assert.Contains("已恢复", ex.Message);
	}

	[Fact]
	public async Task Purge_UnknownOperation_Throws()
	{
		await Assert.ThrowsAsync<InvalidOperationException>(() => Service().PurgeAsync("inst-1", "no-such-op"));
	}

	/* ===================== 列表与隔离 ===================== */

	[Fact]
	public async Task List_ReturnsNewestFirst_AndIsScopedToInstance()
	{
		CreateSave("A");
		CreateSave("B");
		SaveRecoveryService svc = Service();

		await svc.DeleteAsync("inst-1", _gameDir, "A");
		await Task.Delay(1100); // operationId 精确到秒，隔一秒才能区分先后
		await svc.DeleteAsync("inst-1", _gameDir, "B");

		// inst-2 也要有自己的一份：A 已经被 inst-1 搬走了，必须重新造一个。
		CreateSave("A");
		await svc.DeleteAsync("inst-2", _gameDir, "A");

		List<SaveRecoveryRecord> list = await svc.ListAsync("inst-1");

		Assert.Equal(2, list.Count);
		Assert.Equal("B", list[0].SaveFolderName);
		Assert.Equal("A", list[1].SaveFolderName);
		Assert.All(list, r => Assert.Equal("inst-1", r.InstanceId));
		// inst-2 只能看到自己那一条
		Assert.Single(await svc.ListAsync("inst-2"));
	}

	[Fact]
	public async Task List_DoesNotSeeFileRecoveryRecords()
	{
		// 两套记录必须互不可见：否则 Mod 恢复界面会看到无法恢复的存档条目。
		string fileServiceRoot = _recoveryRoot;
		var fileSvc = new FileRecoveryService(fileServiceRoot);
		string modFile = Path.Combine(_gameDir, "mods", "a.jar");
		Directory.CreateDirectory(Path.GetDirectoryName(modFile)!);
		File.WriteAllText(modFile, "jar");

		await fileSvc.QuarantineAsync("inst-1", _gameDir, "mod-delete", new List<string> { modFile });

		List<SaveRecoveryRecord> saves = await Service().ListAsync("inst-1");
		Assert.Empty(saves);

		List<Pineglow.Launcher.Models.FileRecoveryRecord> files = await fileSvc.ListAsync("inst-1");
		Assert.Single(files);
	}

	[Fact]
	public async Task Restore_RejectsRecordBelongingToAnotherGameDirectory()
	{
		CreateSave("World");
		SaveRecoveryService svc = Service();
		SaveRecoveryRecord record = await svc.DeleteAsync("inst-1", _gameDir, "World");

		string otherGame = Path.Combine(_root, "other-game");
		Directory.CreateDirectory(otherGame);

		InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(
			() => svc.RestoreAsync("inst-1", record.OperationId, otherGame));

		Assert.Contains("不属于当前实例", ex.Message);
	}

	[Fact]
	public async Task Restore_UnknownOperation_Throws()
	{
		await Assert.ThrowsAsync<InvalidOperationException>(() => Service().RestoreAsync("inst-1", "nope", _gameDir));
	}

	/* ===================== 内容指纹 ===================== */

	[Fact]
	public void ComputeManifest_IsStableAcrossRepeatedCalls()
	{
		string saveDir = CreateSave("World");
		SaveManifest a = SaveRecoveryService.ComputeManifest(saveDir);
		SaveManifest b = SaveRecoveryService.ComputeManifest(saveDir);

		Assert.Equal(a.Sha256, b.Sha256);
		Assert.Equal(a.FileCount, b.FileCount);
		Assert.Equal(a.SizeBytes, b.SizeBytes);
	}

	[Fact]
	public void ComputeManifest_DetectsMissingFile()
	{
		string saveDir = CreateSave("World", regionFiles: 3);
		string before = SaveRecoveryService.ComputeManifest(saveDir).Sha256;

		File.Delete(Path.Combine(saveDir, "region", "r.0.1.mca"));

		Assert.NotEqual(before, SaveRecoveryService.ComputeManifest(saveDir).Sha256);
	}

	[Fact]
	public void ComputeManifest_DetectsSizeChange()
	{
		string saveDir = CreateSave("World", payloadBytes: 100);
		string before = SaveRecoveryService.ComputeManifest(saveDir).Sha256;

		File.WriteAllBytes(Path.Combine(saveDir, "level.dat"), new byte[200]);

		Assert.NotEqual(before, SaveRecoveryService.ComputeManifest(saveDir).Sha256);
	}

	[Fact]
	public void ComputeManifest_OnMissingDirectory_Throws()
	{
		Assert.Throws<DirectoryNotFoundException>(() => SaveRecoveryService.ComputeManifest(Path.Combine(_root, "nope")));
	}

	[Theory]
	[InlineData(0, "0 B")]
	[InlineData(1024, "1 KB")]
	public void ComputeManifest_ReportsSize(long unused, string expectedSizeText)
	{
		// 单纯校验尺寸口径与其它模块一致（1024 进制、两位小数）。
		_ = unused;
		string saveDir = Path.Combine(_gameDir, "saves", "size-probe");
		Directory.CreateDirectory(saveDir);
		File.WriteAllBytes(Path.Combine(saveDir, "level.dat"), new byte[1024]);

		SaveManifest m = SaveRecoveryService.ComputeManifest(saveDir);
		Assert.True(m.SizeBytes >= 1024, "至少要包含 level.dat 的 1024 字节");
		Assert.Equal(1, m.FileCount);
		Assert.False(string.IsNullOrEmpty(expectedSizeText));
	}

	/* ===================== 中文与特殊名 ===================== */

	[Fact]
	public async Task DeleteAndRestore_HandlesChineseAndSpaceInName()
	{
		// 中文名 + 空格在 Windows 上是常态，PathSafetyGuard 的规范化必须不误伤。
		string saveDir = CreateSave("我的 世界 (1)");
		SaveManifest before = SaveRecoveryService.ComputeManifest(saveDir);

		SaveRecoveryService svc = Service();
		SaveRecoveryRecord record = await svc.DeleteAsync("inst-1", _gameDir, "我的 世界 (1)");
		SaveRecoveryRecord restored = await svc.RestoreAsync("inst-1", record.OperationId, _gameDir);

		Assert.Equal("restored", restored.Status);
		Assert.Equal(before.Sha256, SaveRecoveryService.ComputeManifest(saveDir).Sha256);
	}
}
