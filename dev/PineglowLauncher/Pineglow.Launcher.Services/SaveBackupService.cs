using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using Pineglow.Launcher.Models;

namespace Pineglow.Launcher.Services;

public sealed class SaveBackupService
{
	private readonly SaveService _saves;

	public SaveBackupService(string gameDirectory)
	{
		_saves = new SaveService(gameDirectory);
	}

	public string CreateBackup(GameInstance instance, SaveInfo save)
	{
		if (!Directory.Exists(save.FullPath))
		{
			throw new DirectoryNotFoundException("要备份的存档不存在。");
		}
		string text = Path.Combine(Path.GetDirectoryName(_saves.GetSavesDirectory(instance)), "backups", Sanitize(save.FolderName));
		Directory.CreateDirectory(text);
		string text2 = Path.Combine(text, $"{Sanitize(save.FolderName)}-{DateTime.Now:yyyyMMdd-HHmmss}.zip");
		ZipFile.CreateFromDirectory(save.FullPath, text2, CompressionLevel.Fastest, includeBaseDirectory: true);
		return text2;
	}

	public string RestoreBackup(GameInstance instance, string backupPath)
	{
		if (!File.Exists(backupPath))
		{
			throw new FileNotFoundException("备份文件不存在。", backupPath);
		}
		string text = Path.Combine(Path.GetTempPath(), "pineglow-save-restore-" + Guid.NewGuid().ToString("N"));
		try
		{
			ExtractSafely(backupPath, text);
			string text2 = Directory.EnumerateDirectories(text).FirstOrDefault((string dir) => File.Exists(Path.Combine(dir, "level.dat")));
			if (text2 == null)
			{
				throw new InvalidDataException("备份文件中没有找到有效的 Minecraft 存档。");
			}
			return _saves.ImportSave(instance, text2);
		}
		finally
		{
			if (Directory.Exists(text))
			{
				Directory.Delete(text, recursive: true);
			}
		}
	}

	public List<BackupFileInfo> GetBackups(GameInstance instance)
	{
		string root = GetBackupsDirectory(instance);
		if (!Directory.Exists(root))
		{
			return new List<BackupFileInfo>();
		}
		return (from path in Directory.EnumerateFiles(root, "*.zip", SearchOption.AllDirectories)
			select ToBackupInfo(path, root) into item
			orderby item.CreatedAt descending
			select item).ToList();
	}

	public string GetBackupsDirectory(GameInstance instance)
	{
		return Path.Combine(Path.GetDirectoryName(_saves.GetSavesDirectory(instance)), "backups");
	}

	public void DeleteBackup(GameInstance instance, BackupFileInfo backup)
	{
		string value = Path.GetFullPath(GetBackupsDirectory(instance)) + Path.DirectorySeparatorChar;
		string fullPath = Path.GetFullPath(backup.FullPath);
		StringComparison comparisonType = (OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
		if (!fullPath.StartsWith(value, comparisonType) || !fullPath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
		{
			throw new InvalidOperationException("只能删除当前游戏的存档备份文件。");
		}
		if (!File.Exists(fullPath))
		{
			throw new FileNotFoundException("备份文件不存在。", fullPath);
		}
		File.Delete(fullPath);
	}

	public void OpenBackupsDirectory(GameInstance instance)
	{
		string backupsDirectory = GetBackupsDirectory(instance);
		Directory.CreateDirectory(backupsDirectory);
		if (OperatingSystem.IsWindows())
		{
			Process.Start("explorer.exe", backupsDirectory);
		}
		else if (OperatingSystem.IsMacOS())
		{
			Process.Start("open", backupsDirectory);
		}
		else
		{
			Process.Start("xdg-open", backupsDirectory);
		}
	}

	private static void ExtractSafely(string archivePath, string destination)
	{
		using ZipArchive zipArchive = ZipFile.OpenRead(archivePath);
		string value = Path.GetFullPath(destination) + Path.DirectorySeparatorChar;
		foreach (ZipArchiveEntry entry in zipArchive.Entries)
		{
			if (!string.IsNullOrEmpty(entry.Name))
			{
				string fullPath = Path.GetFullPath(Path.Combine(destination, entry.FullName));
				if (!fullPath.StartsWith(value, StringComparison.OrdinalIgnoreCase))
				{
					throw new InvalidDataException("备份包含不安全的文件路径。");
				}
				Directory.CreateDirectory(Path.GetDirectoryName(fullPath));
				entry.ExtractToFile(fullPath, overwrite: false);
			}
		}
	}

	private static string Sanitize(string value)
	{
		char[] invalid = Path.GetInvalidFileNameChars();
		string text = new string(value.Where((char ch) => !invalid.Contains(ch)).ToArray()).Trim();
		if (!string.IsNullOrWhiteSpace(text))
		{
			return text;
		}
		return "save";
	}

	private static BackupFileInfo ToBackupInfo(string path, string root)
	{
		FileInfo fileInfo = new FileInfo(path);
		string text = Path.GetDirectoryName(Path.GetRelativePath(root, path)) ?? string.Empty;
		return new BackupFileInfo
		{
			FileName = fileInfo.Name,
			FullPath = fileInfo.FullName,
			GroupName = (string.IsNullOrWhiteSpace(text) ? "存档" : text),
			CreatedAt = fileInfo.LastWriteTimeUtc,
			SizeBytes = fileInfo.Length,
			SizeText = FormatSize(fileInfo.Length)
		};
	}

	private static string FormatSize(long bytes)
	{
		string[] array = new string[4] { "B", "KB", "MB", "GB" };
		double num = bytes;
		int num2 = 0;
		while (num >= 1024.0 && num2 < array.Length - 1)
		{
			num /= 1024.0;
			num2++;
		}
		if (num2 == 0)
		{
			return $"{bytes} B";
		}
		return $"{num:0.##} {array[num2]}";
	}
}
