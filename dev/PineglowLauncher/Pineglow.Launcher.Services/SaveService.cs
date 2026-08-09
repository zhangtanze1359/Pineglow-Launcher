using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Pineglow.Launcher.Models;

namespace Pineglow.Launcher.Services;

public sealed class SaveService
{
	private readonly string _defaultGameDirectory;

	public SaveService(string defaultGameDirectory)
	{
		_defaultGameDirectory = (string.IsNullOrWhiteSpace(defaultGameDirectory) ? PlatformPaths.GetDefaultMinecraftDirectory() : defaultGameDirectory);
	}

	public string GetSavesDirectory(GameInstance instance)
	{
		string obj = (string.IsNullOrWhiteSpace(instance.GameDirectory) ? _defaultGameDirectory : instance.GameDirectory);
		if (string.IsNullOrWhiteSpace(obj))
		{
			throw new InvalidOperationException("当前实例的游戏目录为空，请先在设置页选择 .minecraft 目录。");
		}
		return Path.Combine(obj, "saves");
	}

	public List<SaveInfo> GetSaves(GameInstance instance)
	{
		string savesDirectory = GetSavesDirectory(instance);
		Directory.CreateDirectory(savesDirectory);
		return (from x in (from dir in Directory.EnumerateDirectories(savesDirectory)
				where File.Exists(Path.Combine(dir, "level.dat"))
				select dir).Select(CreateSaveInfo)
			orderby x.LastPlayed descending
			select x).ToList();
	}

	public string ImportSave(GameInstance instance, string sourceFolderPath)
	{
		if (string.IsNullOrWhiteSpace(sourceFolderPath) || !Directory.Exists(sourceFolderPath))
		{
			throw new DirectoryNotFoundException("导入的存档文件夹不存在。");
		}
		if (!File.Exists(Path.Combine(sourceFolderPath, "level.dat")))
		{
			throw new InvalidOperationException("选中的文件夹不是 Minecraft 存档（缺少 level.dat）。");
		}
		string savesDirectory = GetSavesDirectory(instance);
		Directory.CreateDirectory(savesDirectory);
		string fileName = Path.GetFileName(sourceFolderPath);
		string availableTargetPath = GetAvailableTargetPath(savesDirectory, fileName);
		CopyDirectory(sourceFolderPath, availableTargetPath);
		return availableTargetPath;
	}

	public void DeleteSave(SaveInfo save)
	{
		if (!Directory.Exists(save.FullPath))
		{
			throw new DirectoryNotFoundException("要删除的存档文件夹不存在。");
		}
		Directory.Delete(save.FullPath, recursive: true);
	}

	public void OpenSavesDirectory(GameInstance instance)
	{
		string savesDirectory = GetSavesDirectory(instance);
		Directory.CreateDirectory(savesDirectory);
		try
		{
			if (OperatingSystem.IsWindows())
			{
				Process.Start("explorer.exe", savesDirectory);
			}
			else if (OperatingSystem.IsMacOS())
			{
				Process.Start("open", savesDirectory);
			}
			else
			{
				Process.Start("xdg-open", savesDirectory);
			}
		}
		catch (Exception ex)
		{
			throw new InvalidOperationException("无法打开存档文件夹。" + ex.Message, ex);
		}
	}

	private static SaveInfo CreateSaveInfo(string folderPath)
	{
		DirectoryInfo directoryInfo = new DirectoryInfo(folderPath);
		FileInfo fileInfo = new FileInfo(Path.Combine(folderPath, "level.dat"));
		SaveInfo saveInfo = new SaveInfo
		{
			FolderName = directoryInfo.Name,
			FullPath = directoryInfo.FullName,
			DisplayName = directoryInfo.Name,
			LastPlayed = (fileInfo.Exists ? fileInfo.LastWriteTime : directoryInfo.LastWriteTime),
			SizeText = FormatSize(GetDirectorySize(folderPath))
		};
		string path = Path.Combine(folderPath, "icon.png");
		if (File.Exists(path))
		{
			try
			{
				saveInfo.IconBytes = File.ReadAllBytes(path);
				saveInfo.HasIcon = true;
			}
			catch
			{
			}
		}
		return saveInfo;
	}

	private static long GetDirectorySize(string path)
	{
		try
		{
			return Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories).Sum(delegate(string f)
			{
				try
				{
					return new FileInfo(f).Length;
				}
				catch
				{
					return 0L;
				}
			});
		}
		catch
		{
			return 0L;
		}
	}

	private static string GetAvailableTargetPath(string directory, string folderName)
	{
		string text = Path.Combine(directory, folderName);
		if (!Directory.Exists(text))
		{
			return text;
		}
		for (int i = 1; i < 1000; i++)
		{
			text = Path.Combine(directory, $"{folderName} ({i})");
			if (!Directory.Exists(text))
			{
				return text;
			}
		}
		throw new IOException("同名存档过多，无法自动生成新文件夹名。");
	}

	private static void CopyDirectory(string sourceDir, string targetDir)
	{
		Directory.CreateDirectory(targetDir);
		foreach (string item in Directory.EnumerateFiles(sourceDir))
		{
			string destFileName = Path.Combine(targetDir, Path.GetFileName(item));
			File.Copy(item, destFileName, overwrite: false);
		}
		foreach (string item2 in Directory.EnumerateDirectories(sourceDir))
		{
			CopyDirectory(item2, Path.Combine(targetDir, Path.GetFileName(item2)));
		}
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
