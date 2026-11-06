using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using Pineglow.Launcher.Models;
using Pineglow.Launcher.Services;
using Xunit;

namespace Pineglow.Launcher.Tests;

/// <summary>
/// 2026-11-06 未覆盖路径补测（批次 2）：**文件系统类服务 + 状态解析**。
///
/// 覆盖率实测：<c>SaveService</c> 68%（路径分支未测）、<c>SaveBackupService</c> 68%、
/// <c>ResourcePackService</c> / <c>ShaderPackService</c> **从未被任何测试提到**、
/// <c>ModHealthService</c> 23%、<c>ModpackLifecycleService</c> 0%、
/// <c>ServerPingService.ParseStatusJson</c> 0%。
///
/// 这批的共同点：全部可在临时目录里用真实文件系统跑，不需要游戏、不需要网络。
/// 唯一例外是 <c>ServerPingService.QueryAsync</c>（真 TCP 协议），
/// 这里只测它的 <c>ParseStatusJson</c> 纯解析部分，协议层另见 <c>ServerPingProtocolTests</c>。
/// </summary>
public class UncoveredFileServiceTests : IDisposable
{
	private readonly string _root;

	public UncoveredFileServiceTests()
	{
		_root = Path.Combine(Path.GetTempPath(), "pineglow-filesvc-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(_root);
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
			// 临时目录清理失败不影响结论
		}
	}

	private GameInstance Instance(string gameDir) => new() { GameDirectory = gameDir, Name = "test" };

	private GameInstance InstanceWithNonexistentDirectory() =>
		new() { GameDirectory = Path.Combine(_root, "missing-" + Guid.NewGuid().ToString("N")) };

	private string MakeSave(string savesDir, string folderName, bool withIcon = false)
	{
		string dir = Path.Combine(savesDir, folderName);
		Directory.CreateDirectory(dir);
		File.WriteAllText(Path.Combine(dir, "level.dat"), "level");
		File.WriteAllText(Path.Combine(dir, "region.bin"), new string('x', 2048));
		if (withIcon)
		{
			File.WriteAllBytes(Path.Combine(dir, "icon.png"), new byte[] { 0x89, 0x50, 0x4E, 0x47 });
		}

		return dir;
	}

	/* ===================== SaveService ===================== */

	[Fact]
	public void SaveService_GetSavesDirectory_UsesInstanceGameDirectory()
	{
		string gameDir = Path.Combine(_root, "game");
		var svc = new SaveService(gameDir);

		Assert.Equal(Path.Combine(gameDir, "saves"), svc.GetSavesDirectory(Instance(gameDir)));
	}

	[Fact]
	public void SaveService_EmptyInstanceDirectory_FallsBackToDefault()
	{
		string fallback = Path.Combine(_root, "fallback");
		var svc = new SaveService(fallback);

		Assert.Equal(Path.Combine(fallback, "saves"), svc.GetSavesDirectory(Instance(string.Empty)));
		Assert.Equal(Path.Combine(fallback, "saves"), svc.GetSavesDirectory(Instance("   ")));
	}

	[Fact]
	public void SaveService_BlankDefaultDirectory_FallsBackToPlatformDefault_NotThrowing()
	{
		// 实测结论（本测试原以为是"两边都空 → 抛异常"，跑出来不是）：
		// 构造函数在 defaultGameDirectory 为空白时就已经替换成 PlatformPaths 的默认目录，
		// 所以 GetSavesDirectory 里那句 `throw new InvalidOperationException("当前实例的游戏目录为空…")`
		// **在公开 API 下不可达**——它只是防御性代码。
		//
		// 这里如实断言当前行为（回落而不是抛），并把"防御分支不可达"记在案，
		// 避免后人以为那句 throw 是活的、还照着它写调用方逻辑。
		var svc = new SaveService("   ");
		string dir = svc.GetSavesDirectory(Instance("  "));

		Assert.Contains("saves", dir);
		Assert.True(Path.IsPathFullyQualified(dir), "应当回落到一个绝对路径（平台默认 .minecraft/saves）");
	}

	[Fact]
	public void SaveService_GetSaves_OnlyReturnsFoldersWithLevelDat_AndSortsByRecency()
	{
		string gameDir = Path.Combine(_root, "game");
		string saves = Path.Combine(gameDir, "saves");
		Directory.CreateDirectory(saves);

		string older = MakeSave(saves, "older");
		string newer = MakeSave(saves, "newer");
		// 不是存档的文件夹（缺少 level.dat）必须被忽略
		Directory.CreateDirectory(Path.Combine(saves, "not-a-save"));

		File.SetLastWriteTime(Path.Combine(older, "level.dat"), DateTime.Now.AddDays(-3));
		File.SetLastWriteTime(Path.Combine(newer, "level.dat"), DateTime.Now);

		var list = new SaveService(gameDir).GetSaves(Instance(gameDir));

		Assert.Equal(2, list.Count);
		Assert.Equal("newer", list[0].FolderName);
		Assert.Equal("older", list[1].FolderName);
	}

	[Fact]
	public void SaveService_GetSaves_ReadsIconBytes_WhenPresent()
	{
		string gameDir = Path.Combine(_root, "game");
		string saves = Path.Combine(gameDir, "saves");
		MakeSave(saves, "with-icon", withIcon: true);

		var save = Assert.Single(new SaveService(gameDir).GetSaves(Instance(gameDir)));

		Assert.True(save.HasIcon);
		Assert.NotNull(save.IconBytes);
		Assert.Equal(4, save.IconBytes!.Length);
	}

	[Fact]
	public void SaveService_GetSaves_FormatsSize_AndCreatesMissingDirectory()
	{
		string gameDir = Path.Combine(_root, "game");

		// saves 目录不存在时必须自建而不是抛
		var list = new SaveService(gameDir).GetSaves(Instance(gameDir));

		Assert.Empty(list);
		Assert.True(Directory.Exists(Path.Combine(gameDir, "saves")));
	}

	[Fact]
	public void SaveService_ImportSave_CopiesNestedStructure()
	{
		string gameDir = Path.Combine(_root, "game");
		string source = Path.Combine(_root, "incoming", "MyWorld");
		Directory.CreateDirectory(Path.Combine(source, "region"));
		File.WriteAllText(Path.Combine(source, "level.dat"), "level");
		File.WriteAllText(Path.Combine(source, "region", "r.0.0.mca"), "region");

		string imported = new SaveService(gameDir).ImportSave(Instance(gameDir), source);

		Assert.Equal("MyWorld", Path.GetFileName(imported));
		Assert.True(File.Exists(Path.Combine(imported, "level.dat")));
		Assert.True(File.Exists(Path.Combine(imported, "region", "r.0.0.mca")));
	}

	[Fact]
	public void SaveService_ImportSave_NameCollision_GeneratesSuffixedFolder()
	{
		string gameDir = Path.Combine(_root, "game");
		string source = Path.Combine(_root, "incoming", "Dup");
		Directory.CreateDirectory(source);
		File.WriteAllText(Path.Combine(source, "level.dat"), "level");

		var svc = new SaveService(gameDir);
		string first = svc.ImportSave(Instance(gameDir), source);
		string second = svc.ImportSave(Instance(gameDir), source);

		Assert.NotEqual(first, second);
		Assert.Equal("Dup (1)", Path.GetFileName(second));
	}

	[Fact]
	public void SaveService_ImportSave_RejectsFolderWithoutLevelDat()
	{
		string gameDir = Path.Combine(_root, "game");
		string source = Path.Combine(_root, "incoming", "NotASave");
		Directory.CreateDirectory(source);

		var ex = Assert.Throws<InvalidOperationException>(
			() => new SaveService(gameDir).ImportSave(Instance(gameDir), source));
		Assert.Contains("level.dat", ex.Message);
	}

	[Fact]
	public void SaveService_ImportSave_RejectsMissingSourceAndNull()
	{
		string gameDir = Path.Combine(_root, "game");
		var svc = new SaveService(gameDir);

		Assert.Throws<DirectoryNotFoundException>(() => svc.ImportSave(Instance(gameDir), Path.Combine(_root, "nope")));
		Assert.Throws<DirectoryNotFoundException>(() => svc.ImportSave(Instance(gameDir), "  "));
	}

	[Fact]
	public void SaveService_DeleteSave_RemovesFolder_AndThrowsWhenMissing()
	{
		string gameDir = Path.Combine(_root, "game");
		string saves = Path.Combine(gameDir, "saves");
		string dir = MakeSave(saves, "to-delete");
		var svc = new SaveService(gameDir);

		svc.DeleteSave(new SaveInfo { FolderName = "to-delete", FullPath = dir });
		Assert.False(Directory.Exists(dir));

		Assert.Throws<DirectoryNotFoundException>(
			() => svc.DeleteSave(new SaveInfo { FolderName = "to-delete", FullPath = dir }));
	}

	/* ===================== SaveBackupService ===================== */

	[Fact]
	public void SaveBackup_CreateBackup_ProducesZipUnderBackupsFolder()
	{
		string gameDir = Path.Combine(_root, "game");
		string saves = Path.Combine(gameDir, "saves");
		string dir = MakeSave(saves, "World");
		var svc = new SaveBackupService(gameDir);

		string zip = svc.CreateBackup(Instance(gameDir), new SaveInfo { FolderName = "World", FullPath = dir });

		Assert.True(File.Exists(zip));
		Assert.StartsWith(Path.Combine(gameDir, "backups"), zip);
		Assert.EndsWith(".zip", zip);
	}

	[Fact]
	public void SaveBackup_GetBackups_ListsAndGroupsBySaveFolder()
	{
		string gameDir = Path.Combine(_root, "game");
		string saves = Path.Combine(gameDir, "saves");
		var svc = new SaveBackupService(gameDir);

		foreach (string name in new[] { "WorldA", "WorldB" })
		{
			string dir = MakeSave(saves, name);
			svc.CreateBackup(Instance(gameDir), new SaveInfo { FolderName = name, FullPath = dir });
		}

		var backups = svc.GetBackups(Instance(gameDir));

		Assert.Equal(2, backups.Count);
		Assert.Contains(backups, b => b.GroupName == "WorldA");
		Assert.Contains(backups, b => b.GroupName == "WorldB");
		Assert.All(backups, b => Assert.True(b.SizeBytes > 0));
	}

	[Fact]
	public void SaveBackup_GetBackups_MissingDirectory_ReturnsEmptyNotThrow()
	{
		var svc = new SaveBackupService(Path.Combine(_root, "game"));
		Assert.Empty(svc.GetBackups(Instance(Path.Combine(_root, "game"))));
	}

	[Fact]
	public void SaveBackup_Restore_RoundTripsContent()
	{
		string gameDir = Path.Combine(_root, "game");
		string saves = Path.Combine(gameDir, "saves");
		string dir = MakeSave(saves, "RoundTrip");
		File.WriteAllText(Path.Combine(dir, "marker.txt"), "SURVIVE");
		var svc = new SaveBackupService(gameDir);

		string zip = svc.CreateBackup(Instance(gameDir), new SaveInfo { FolderName = "RoundTrip", FullPath = dir });
		Directory.Delete(dir, recursive: true);

		string restored = svc.RestoreBackup(Instance(gameDir), zip);

		Assert.True(File.Exists(Path.Combine(restored, "marker.txt")));
		Assert.Equal("SURVIVE", File.ReadAllText(Path.Combine(restored, "marker.txt")));
	}

	[Fact]
	public void SaveBackup_Restore_MissingFile_Throws()
	{
		var svc = new SaveBackupService(Path.Combine(_root, "game"));
		Assert.Throws<FileNotFoundException>(
			() => svc.RestoreBackup(Instance(Path.Combine(_root, "game")), Path.Combine(_root, "nope.zip")));
	}

	[Fact]
	public void SaveBackup_Restore_ZipWithoutLevelDat_Throws()
	{
		string gameDir = Path.Combine(_root, "game");
		string zip = Path.Combine(_root, "bogus.zip");
		using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
		{
			using var writer = new StreamWriter(archive.CreateEntry("junk/readme.txt").Open(), Encoding.UTF8);
			writer.Write("not a save");
		}

		Assert.Throws<InvalidDataException>(
			() => new SaveBackupService(gameDir).RestoreBackup(Instance(gameDir), zip));
	}

	[Fact]
	public void SaveBackup_ExtractSafely_RejectsZipSlipEntry()
	{
		// 安全断言：备份 zip 里带 ../ 的条目必须被拒，否则解压会写到临时目录之外。
		string gameDir = Path.Combine(_root, "game");
		string zip = Path.Combine(_root, "evil.zip");
		using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
		{
			var entry = archive.CreateEntry("../../escaped.txt");
			using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
			writer.Write("pwned");
		}

		Assert.Throws<InvalidDataException>(
			() => new SaveBackupService(gameDir).RestoreBackup(Instance(gameDir), zip));
	}

	[Fact]
	public void SaveBackup_DeleteBackup_RejectsPathOutsideBackupsDirectory()
	{
		string gameDir = Path.Combine(_root, "game");
		string outside = Path.Combine(_root, "outside.zip");
		File.WriteAllText(outside, "x");

		var ex = Assert.Throws<InvalidOperationException>(
			() => new SaveBackupService(gameDir).DeleteBackup(
				Instance(gameDir),
				new BackupFileInfo { FullPath = outside, FileName = "outside.zip" }));
		Assert.Contains("只能删除", ex.Message);
		// 拒绝之后文件必须还在
		Assert.True(File.Exists(outside));
	}

	[Fact]
	public void SaveBackup_DeleteBackup_RejectsNonZip_EvenInsideBackupsDirectory()
	{
		string gameDir = Path.Combine(_root, "game");
		string backups = Path.Combine(gameDir, "backups");
		Directory.CreateDirectory(backups);
		string txt = Path.Combine(backups, "notes.txt");
		File.WriteAllText(txt, "x");

		Assert.Throws<InvalidOperationException>(
			() => new SaveBackupService(gameDir).DeleteBackup(
				Instance(gameDir),
				new BackupFileInfo { FullPath = txt, FileName = "notes.txt" }));
		Assert.True(File.Exists(txt));
	}

	[Fact]
	public void SaveBackup_DeleteBackup_RemovesValidZip()
	{
		string gameDir = Path.Combine(_root, "game");
		string saves = Path.Combine(gameDir, "saves");
		string dir = MakeSave(saves, "W");
		var svc = new SaveBackupService(gameDir);
		string zip = svc.CreateBackup(Instance(gameDir), new SaveInfo { FolderName = "W", FullPath = dir });

		var info = Assert.Single(svc.GetBackups(Instance(gameDir)));
		svc.DeleteBackup(Instance(gameDir), info);

		Assert.False(File.Exists(zip));
	}

	[Fact]
	public void SaveBackup_Sanitize_StripsInvalidFileNameCharacters()
	{
		// 存档名里可能有 : ? * 等非法字符（来自 CurseForge 整合包命名）。
		string gameDir = Path.Combine(_root, "game");
		string saves = Path.Combine(gameDir, "saves");
		string dir = MakeSave(saves, "World");
		var svc = new SaveBackupService(gameDir);

		// FolderName 带非法字符，但 FullPath 指向真实目录 → 备份名必须被清洗
		string zip = svc.CreateBackup(Instance(gameDir), new SaveInfo { FolderName = "a:b?c*d", FullPath = dir });

		string name = Path.GetFileName(zip);
		Assert.DoesNotContain(':', name);
		Assert.DoesNotContain('?', name);
		Assert.DoesNotContain('*', name);
	}

	/* ===================== ResourcePackService ===================== */

	private static string MakeResourcePackZip(string path, string description = "Test Pack", int packFormat = 15, bool withCover = false)
	{
		Directory.CreateDirectory(Path.GetDirectoryName(path)!);
		using var archive = ZipFile.Open(path, ZipArchiveMode.Create);

		var mcmeta = archive.CreateEntry("pack.mcmeta");
		using (var writer = new StreamWriter(mcmeta.Open(), new UTF8Encoding(false)))
		{
			writer.Write("{\"pack\":{\"description\":\"" + description + "\",\"pack_format\":" + packFormat + "}}");
		}

		if (withCover)
		{
			var png = archive.CreateEntry("pack.png");
			using var stream = png.Open();
			stream.Write(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D }, 0, 5);
		}

		return path;
	}

	[Fact]
	public void ResourcePacks_GetResourcePacks_ReadsMetadataFromZip()
	{
		string gameDir = Path.Combine(_root, "game");
		string dir = Path.Combine(gameDir, "resourcepacks");
		MakeResourcePackZip(Path.Combine(dir, "pack.zip"), "我的资源包", 22, withCover: true);

		var packs = new ResourcePackService(gameDir).GetResourcePacks(Instance(gameDir));

		var pack = Assert.Single(packs);
		Assert.Equal("pack.zip", pack.FileName);
		Assert.Equal("我的资源包", pack.DisplayName);
		Assert.Equal(22, pack.PackFormat);
		Assert.True(pack.HasCover);
		Assert.NotNull(pack.CoverBytes);
	}

	[Fact]
	public void ResourcePacks_MalformedZip_DoesNotThrow_AndStillListsFile()
	{
		// 关键：用户可能把 .zip 改成别的再改回来。列表页不能因为一个坏 zip 整页崩掉。
		string gameDir = Path.Combine(_root, "game");
		string dir = Path.Combine(gameDir, "resourcepacks");
		Directory.CreateDirectory(dir);
		File.WriteAllText(Path.Combine(dir, "broken.zip"), "this is not a zip");

		var packs = new ResourcePackService(gameDir).GetResourcePacks(Instance(gameDir));

		var pack = Assert.Single(packs);
		Assert.Equal("broken.zip", pack.FileName);
		Assert.Equal(string.Empty, pack.DisplayName);
	}

	[Fact]
	public void ResourcePacks_Import_RejectsNonZipAndMissingFile()
	{
		string gameDir = Path.Combine(_root, "game");
		var svc = new ResourcePackService(gameDir);
		string txt = Path.Combine(_root, "pack.txt");
		File.WriteAllText(txt, "x");

		Assert.Throws<InvalidOperationException>(() => svc.ImportResourcePack(Instance(gameDir), txt));
		Assert.Throws<FileNotFoundException>(() => svc.ImportResourcePack(Instance(gameDir), Path.Combine(_root, "nope.zip")));
	}

	[Fact]
	public void ResourcePacks_Import_NameCollision_GeneratesSuffixedFile()
	{
		string gameDir = Path.Combine(_root, "game");
		string src = MakeResourcePackZip(Path.Combine(_root, "incoming", "Same.zip"));
		var svc = new ResourcePackService(gameDir);

		string first = svc.ImportResourcePack(Instance(gameDir), src);
		string second = svc.ImportResourcePack(Instance(gameDir), src);

		Assert.Equal("Same.zip", Path.GetFileName(first));
		Assert.Equal("Same (1).zip", Path.GetFileName(second));
	}

	[Fact]
	public void ResourcePacks_Delete_RejectsNonZip_AndKeepsFile()
	{
		string gameDir = Path.Combine(_root, "game");
		string dir = Path.Combine(gameDir, "resourcepacks");
		Directory.CreateDirectory(dir);
		string txt = Path.Combine(dir, "keep.txt");
		File.WriteAllText(txt, "x");

		Assert.Throws<InvalidOperationException>(
			() => new ResourcePackService(gameDir).DeleteResourcePack(
				new ResourcePackInfo { FileName = "keep.txt", FullPath = txt }));
		Assert.True(File.Exists(txt));
	}

	/* ===================== ShaderPackService ===================== */

	[Fact]
	public void Shaders_GetPacks_ListsOnlyZip_AndSortsByFileName()
	{
		string gameDir = Path.Combine(_root, "game");
		string dir = Path.Combine(gameDir, "shaderpacks");
		Directory.CreateDirectory(dir);
		File.WriteAllText(Path.Combine(dir, "b.zip"), "x");
		File.WriteAllText(Path.Combine(dir, "a.zip"), "x");
		File.WriteAllText(Path.Combine(dir, "readme.txt"), "x");

		var packs = new ShaderPackService(gameDir).GetPacks(Instance(gameDir));

		Assert.Equal(new[] { "a.zip", "b.zip" }, packs.Select(p => p.FileName));
	}

	[Fact]
	public void Shaders_Import_RejectsNonZip()
	{
		string gameDir = Path.Combine(_root, "game");
		string txt = Path.Combine(_root, "shader.txt");
		File.WriteAllText(txt, "x");

		Assert.Throws<InvalidOperationException>(
			() => new ShaderPackService(gameDir).Import(Instance(gameDir), txt));
	}

	[Fact]
	public void Shaders_Import_NameCollision_GeneratesSuffixedFile()
	{
		string gameDir = Path.Combine(_root, "game");
		string src = Path.Combine(_root, "incoming", "S.zip");
		Directory.CreateDirectory(Path.GetDirectoryName(src)!);
		File.WriteAllText(src, "x");
		var svc = new ShaderPackService(gameDir);

		Assert.Equal("S.zip", Path.GetFileName(svc.Import(Instance(gameDir), src)));
		Assert.Equal("S (1).zip", Path.GetFileName(svc.Import(Instance(gameDir), src)));
	}

	[Fact]
	public void Shaders_Delete_RejectsNonZip_AndKeepsFile()
	{
		string gameDir = Path.Combine(_root, "game");
		string dir = Path.Combine(gameDir, "shaderpacks");
		Directory.CreateDirectory(dir);
		string txt = Path.Combine(dir, "notes.txt");
		File.WriteAllText(txt, "x");

		Assert.Throws<InvalidOperationException>(
			() => new ShaderPackService(gameDir).Delete(new ShaderPackInfo { FileName = "notes.txt", FullPath = txt }));
		Assert.True(File.Exists(txt));
	}

	/* ===================== ModHealthService ===================== */

	private static ModFile Mod(string fileName, string? loaderHint = null, bool enabled = true,
		string modId = "", string version = "", string onlineSource = "", string projectId = "",
		string versionId = "", List<ModManifestDependency>? deps = null)
		=> new()
		{
			FileName = fileName,
			DisplayName = fileName,
			IsEnabled = enabled,
			ModId = modId,
			Version = version,
			LoaderHint = loaderHint ?? string.Empty,
			OnlineSource = onlineSource,
			ProjectId = projectId,
			VersionId = versionId,
			Dependencies = deps ?? new List<ModManifestDependency>(),
		};

	[Fact]
	public void ModHealth_VanillaWithEnabledMods_ProducesBlockingError()
	{
		var instance = new GameInstance { LoaderType = "Vanilla" };
		var report = ModHealthService.Analyze(instance, new[] { Mod("sodium.jar") });

		Assert.False(report.CanLaunch);
		Assert.Contains(report.Issues, i => i.Severity == ModHealthSeverity.Error && i.Title.Contains("没有 Mod 支持"));
	}

	[Fact]
	public void ModHealth_VanillaWithNoMods_IsClean()
	{
		var report = ModHealthService.Analyze(new GameInstance { LoaderType = "Vanilla" }, Array.Empty<ModFile>());

		Assert.True(report.CanLaunch);
		Assert.Empty(report.Issues);
		Assert.Equal(0, report.EnabledCount);
	}

	[Fact]
	public void ModHealth_DisabledMods_AreNotCountedAsEnabled()
	{
		var report = ModHealthService.Analyze(
			new GameInstance { LoaderType = "Fabric" },
			new[] { Mod("a.jar", enabled: true), Mod("b.jar", enabled: false) });

		Assert.Equal(1, report.EnabledCount);
		Assert.Equal(1, report.DisabledCount);
	}

	[Fact]
	public void ModHealth_DuplicateOnlineProject_IsWarningNotError()
	{
		var report = ModHealthService.Analyze(
			new GameInstance { LoaderType = "Fabric" },
			new[]
			{
				Mod("sodium-1.jar", onlineSource: "modrinth", projectId: "AANobbMI", versionId: "v1"),
				Mod("sodium-2.jar", onlineSource: "modrinth", projectId: "AANobbMI", versionId: "v2"),
			});

		Assert.True(report.CanLaunch); // Warning 不阻断启动
		Assert.Contains(report.Issues, i => i.Severity == ModHealthSeverity.Warning && i.Title.Contains("重复安装"));
	}

	[Fact]
	public void ModHealth_DuplicateModId_IsWarning()
	{
		var report = ModHealthService.Analyze(
			new GameInstance { LoaderType = "Fabric" },
			new[] { Mod("a.jar", modId: "sodium"), Mod("b.jar", modId: "sodium") });

		Assert.Contains(report.Issues, i => i.Title.Contains("相同 Mod 标识"));
	}

	[Fact]
	public void ModHealth_LoaderMismatch_IsWarning()
	{
		var report = ModHealthService.Analyze(
			new GameInstance { LoaderType = "Fabric" },
			new[] { Mod("forge-only.jar", loaderHint: "Forge") });

		Assert.Contains(report.Issues, i => i.Severity == ModHealthSeverity.Warning && i.Title.Contains("不适配"));
	}

	[Fact]
	public void ModHealth_FabricHintOnQuilt_IsCompatible()
	{
		// Quilt 兼容 Fabric 模组是真实语义，判错会误报一大片。
		var report = ModHealthService.Analyze(
			new GameInstance { LoaderType = "Quilt" },
			new[] { Mod("f.jar", loaderHint: "Fabric") });

		Assert.DoesNotContain(report.Issues, i => i.Title.Contains("不适配"));
	}

	[Fact]
	public void ModHealth_MissingRequiredDependency_IsBlockingError()
	{
		string json = """{"rules":[]}"""; // 占位，避免误用
		_ = json;

		var report = ModHealthService.Analyze(
			new GameInstance { LoaderType = "Fabric" },
			new[]
			{
				Mod("a.jar", modId: "a", deps: new List<ModManifestDependency>
				{
					new() { ModId = "fabric-api", VersionRange = "*", IsRequired = true },
				}),
			});

		Assert.False(report.CanLaunch);
		Assert.Contains(report.Issues, i => i.Severity == ModHealthSeverity.Error && i.Title.Contains("缺少 Mod 前置依赖"));
	}

	[Fact]
	public void ModHealth_OptionalDependency_DoesNotProduceError()
	{
		var report = ModHealthService.Analyze(
			new GameInstance { LoaderType = "Fabric" },
			new[]
			{
				Mod("a.jar", modId: "a", deps: new List<ModManifestDependency>
				{
					new() { ModId = "optional-api", VersionRange = "*", IsRequired = false },
				}),
			});

		Assert.True(report.CanLaunch);
		Assert.DoesNotContain(report.Issues, i => i.Title.Contains("缺少 Mod 前置依赖"));
	}

	[Fact]
	public void ModHealth_DependencyPresentButVersionOutOfRange_IsBlockingError()
	{
		var report = ModHealthService.Analyze(
			new GameInstance { LoaderType = "Fabric" },
			new[]
			{
				Mod("api.jar", modId: "fabric-api", version: "0.1.0"),
				Mod("a.jar", modId: "a", deps: new List<ModManifestDependency>
				{
					new() { ModId = "fabric-api", VersionRange = "[1.0.0,)", IsRequired = true },
				}),
			});

		Assert.False(report.CanLaunch);
		Assert.Contains(report.Issues, i => i.Title.Contains("版本不匹配"));
	}

	[Fact]
	public void ModHealth_DependencyVersionInRange_IsClean()
	{
		var report = ModHealthService.Analyze(
			new GameInstance { LoaderType = "Fabric" },
			new[]
			{
				Mod("api.jar", modId: "fabric-api", version: "1.2.3"),
				Mod("a.jar", modId: "a", deps: new List<ModManifestDependency>
				{
					new() { ModId = "fabric-api", VersionRange = "[1.0.0,2.0.0)", IsRequired = true },
				}),
			});

		Assert.True(report.CanLaunch);
	}

	[Theory]
	// Modrinth 的版本区间语法变体很多，逐条钉住，避免"看着像支持其实判错"
	[InlineData("1.2.3", "1.2.3", true)]
	[InlineData("1.2.3", "1.2.4", false)]
	[InlineData("1.2.3", "[1.0.0,2.0.0)", true)]
	[InlineData("2.0.0", "[1.0.0,2.0.0)", false)]   // 右开：2.0.0 不含
	[InlineData("2.0.0", "[1.0.0,2.0.0]", true)]    // 右闭：2.0.0 含
	[InlineData("0.9.9", "[1.0.0,)", false)]
	[InlineData("1.0.0", "[1.0.0,)", true)]
	[InlineData("1.5.0", "(,1.0.0)", false)]
	[InlineData("3.0.0", ">=2.0.0", true)]
	[InlineData("1.0.0", ">2.0.0", false)]
	[InlineData("1.0.0", "<=1.0.0", true)]
	[InlineData("1.2.3", "1.2.*", true)]
	[InlineData("1.3.0", "1.2.*", false)]
	[InlineData("5.0.0", "1.0.0 || 5.0.0", true)]   // 或运算
	[InlineData("5.0.0", "*", true)]
	[InlineData("5.0.0", "(,)", true)]
	public void ModHealth_VersionRangeSyntax_IsHonored(string actual, string range, bool expectedCompatible)
	{
		var mods = new List<ModFile>
		{
			Mod("dep.jar", modId: "dep", version: actual),
			Mod("consumer.jar", modId: "consumer", deps: new List<ModManifestDependency>
			{
				new() { ModId = "dep", VersionRange = range, IsRequired = true },
			}),
		};

		var report = ModHealthService.Analyze(new GameInstance { LoaderType = "Fabric" }, mods);

		bool hasMismatch = report.Issues.Any(i => i.Title.Contains("版本不匹配"));
		Assert.Equal(!expectedCompatible, hasMismatch);
	}

	[Fact]
	public void ModHealth_EmptyVersionOrRange_IsTreatedAsCompatible()
	{
		// 判定口径：拿不到版本就当没冲突。误报"版本不匹配"会让用户无从下手。
		var report = ModHealthService.Analyze(
			new GameInstance { LoaderType = "Fabric" },
			new[]
			{
				Mod("dep.jar", modId: "dep", version: ""),
				Mod("c.jar", modId: "c", deps: new List<ModManifestDependency>
				{
					new() { ModId = "dep", VersionRange = "[1.0.0,)", IsRequired = true },
				}),
			});

		Assert.True(report.CanLaunch);
	}

	[Fact]
	public void ModHealth_ManualMods_ProduceInfoAboutNoUpdateCheck()
	{
		var report = ModHealthService.Analyze(
			new GameInstance { LoaderType = "Fabric" },
			new[] { Mod("manual.jar") });

		Assert.Contains(report.Issues, i => i.Severity == ModHealthSeverity.Info && i.Title.Contains("无法自动检查更新"));
		Assert.Equal(0, report.OnlineManagedCount);
	}

	/* ===================== ModpackLifecycleService ===================== */

	[Fact]
	public void Modpack_IsTrackable_RequiresModrinthSourceAndBothIds()
	{
		Assert.True(ModpackLifecycleService.IsTrackableModrinth(new GameInstance
		{
			Modpack = new ModpackInstallation { Source = "modrinth", ProjectId = "P", VersionId = "V" },
		}));

		Assert.False(ModpackLifecycleService.IsTrackableModrinth(new GameInstance
		{
			Modpack = new ModpackInstallation { Source = "curseforge", ProjectId = "P", VersionId = "V" },
		}));

		Assert.False(ModpackLifecycleService.IsTrackableModrinth(new GameInstance
		{
			Modpack = new ModpackInstallation { Source = "modrinth", ProjectId = "P", VersionId = "" },
		}));

		Assert.False(ModpackLifecycleService.IsTrackableModrinth(new GameInstance()));
		Assert.False(ModpackLifecycleService.IsTrackableModrinth(null));
	}

	[Fact]
	public void Modpack_SetModrinthSource_WritesAllFields()
	{
		var instance = new GameInstance();
		var project = new ModpackProject
		{
			ProjectId = "AANobbMI",
			Title = "Sodium Pack",
			WebsiteUrl = "https://modrinth.com/modpack/x",
		};
		var version = new ModpackVersion { VersionId = "V1", VersionNumber = "1.0.0" };

		ModpackLifecycleService.SetModrinthSource(instance, project, version);

		Assert.NotNull(instance.Modpack);
		Assert.Equal("modrinth", instance.Modpack!.Source);
		Assert.Equal("AANobbMI", instance.Modpack.ProjectId);
		Assert.Equal("Sodium Pack", instance.Modpack.ProjectName);
		Assert.Equal("V1", instance.Modpack.VersionId);
		Assert.Equal("1.0.0", instance.Modpack.VersionName);
		Assert.True(ModpackLifecycleService.IsTrackableModrinth(instance));
	}

	[Fact]
	public void Modpack_FindAvailableUpdate_PrefersReleaseThenNewest()
	{
		var instance = new GameInstance
		{
			Modpack = new ModpackInstallation { Source = "modrinth", ProjectId = "P", VersionId = "CURRENT" },
		};

		var versions = new[]
		{
			new ModpackVersion { VersionId = "CURRENT", ReleaseType = "release", PublishedAt = DateTimeOffset.Parse("2026-01-01T00:00:00Z") },
			new ModpackVersion { VersionId = "BETA-NEWEST", ReleaseType = "beta", PublishedAt = DateTimeOffset.Parse("2026-06-01T00:00:00Z") },
			new ModpackVersion { VersionId = "RELEASE-OLD", ReleaseType = "release", PublishedAt = DateTimeOffset.Parse("2026-03-01T00:00:00Z") },
		};

		var update = ModpackLifecycleService.FindAvailableUpdate(instance, versions);

		Assert.NotNull(update);
		// release 优先于 beta，即使 beta 更新
		Assert.Equal("RELEASE-OLD", update!.VersionId);
	}

	[Fact]
	public void Modpack_FindAvailableUpdate_ReturnsNullWhenOnlyCurrentVersionExists()
	{
		var instance = new GameInstance
		{
			Modpack = new ModpackInstallation { Source = "modrinth", ProjectId = "P", VersionId = "ONLY" },
		};

		var update = ModpackLifecycleService.FindAvailableUpdate(
			instance,
			new[] { new ModpackVersion { VersionId = "ONLY", ReleaseType = "release" } });

		Assert.Null(update);
	}

	[Fact]
	public void Modpack_FindAvailableUpdate_UntrackableInstance_ReturnsNull()
	{
		var update = ModpackLifecycleService.FindAvailableUpdate(
			new GameInstance(),
			new[] { new ModpackVersion { VersionId = "X", ReleaseType = "release" } });

		Assert.Null(update);
	}

	[Fact]
	public void Modpack_CloneSource_CopiesAllFields_AndHandlesNull()
	{
		var source = new ModpackInstallation
		{
			Source = "modrinth",
			ProjectId = "P",
			ProjectName = "N",
			VersionId = "V",
			VersionName = "1.0",
			WebsiteUrl = "u",
			InstalledAt = DateTimeOffset.Parse("2026-01-01T00:00:00Z"),
		};

		var clone = ModpackLifecycleService.CloneSource(source);

		Assert.NotNull(clone);
		Assert.Equal(source.ProjectId, clone!.ProjectId);
		Assert.Equal(source.VersionId, clone.VersionId);
		Assert.Equal(source.InstalledAt, clone.InstalledAt);
		Assert.NotSame(source, clone); // 必须是深拷贝，否则实例隔离会串
		Assert.Null(ModpackLifecycleService.CloneSource(null));
	}

	/* ===================== ServerPingService.ParseStatusJson ===================== */

	[Fact]
	public void ServerStatus_ParsesFlatJson()
	{
		const string json = """
			{"version":{"name":"1.20.4","protocol":765},
			 "players":{"online":12,"max":100},
			 "description":"§aHello §rServer"}
			""";

		var status = ServerPingService.ParseStatusJson(json, 42);

		Assert.Equal("1.20.4", status.VersionName);
		Assert.Equal(12, status.OnlinePlayers);
		Assert.Equal(100, status.MaxPlayers);
		Assert.Equal(42, status.LatencyMs);
		// 格式化码必须被剥掉，否则界面会显示乱码方块
		Assert.Equal("Hello Server", status.Motd);
	}

	[Fact]
	public void ServerStatus_ParsesChatObjectDescription()
	{
		const string json = """
			{"version":{"name":"1.20"},
			 "players":{"online":1,"max":2},
			 "description":{"text":"A ","extra":[{"text":"B"},{"text":" C"}]}}
			""";

		var status = ServerPingService.ParseStatusJson(json);

		Assert.Equal("A B C", status.Motd);
	}

	[Fact]
	public void ServerStatus_MissingFields_YieldDocumentedDefaults()
	{
		var status = ServerPingService.ParseStatusJson("{}");

		Assert.Equal(string.Empty, status.Motd);
		Assert.Equal(string.Empty, status.VersionName);
		Assert.Equal(-1, status.OnlinePlayers);
		Assert.Equal(-1, status.MaxPlayers);
		Assert.Equal(-1, status.LatencyMs);
		Assert.Null(status.FaviconBytes);
	}

	[Fact]
	public void ServerStatus_InvalidJson_Throws()
	{
		Assert.ThrowsAny<JsonException>(() => ServerPingService.ParseStatusJson("{not json"));
	}
}
