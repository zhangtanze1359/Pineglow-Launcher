using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Pineglow.Launcher.Models;

namespace Pineglow.Launcher.Services;

public sealed class ShaderPackService
{
	private readonly string _defaultGameDirectory;

	public ShaderPackService(string defaultGameDirectory)
	{
		_defaultGameDirectory = (string.IsNullOrWhiteSpace(defaultGameDirectory) ? PlatformPaths.GetDefaultMinecraftDirectory() : defaultGameDirectory);
	}

	public string GetDirectory(GameInstance instance)
	{
		string obj = (string.IsNullOrWhiteSpace(instance.GameDirectory) ? _defaultGameDirectory : instance.GameDirectory);
		if (string.IsNullOrWhiteSpace(obj))
		{
			throw new InvalidOperationException("当前实例游戏目录为空。");
		}
		return Path.Combine(obj, "shaderpacks");
	}

	public List<ShaderPackInfo> GetPacks(GameInstance instance)
	{
		string directory = GetDirectory(instance);
		Directory.CreateDirectory(directory);
		return (from path in Directory.EnumerateFiles(directory, "*.zip")
			select new FileInfo(path) into file
			select new ShaderPackInfo
			{
				FileName = file.Name,
				FullPath = file.FullName,
				SizeText = FormatSize(file.Length)
			}).OrderBy<ShaderPackInfo, string>((ShaderPackInfo pack) => pack.FileName, StringComparer.OrdinalIgnoreCase).ToList();
	}

	public string Import(GameInstance instance, string sourcePath)
	{
		if (!File.Exists(sourcePath))
		{
			throw new FileNotFoundException("Shader 文件不存在。", sourcePath);
		}
		if (!sourcePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
		{
			throw new InvalidOperationException("Shader 必须是 .zip 文件。");
		}
		string directory = GetDirectory(instance);
		Directory.CreateDirectory(directory);
		string text = AvailablePath(directory, Path.GetFileName(sourcePath));
		File.Copy(sourcePath, text);
		return text;
	}

	public void Delete(ShaderPackInfo pack)
	{
		if (!pack.FullPath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
		{
			throw new InvalidOperationException("只能删除 Shader zip 文件。");
		}
		File.Delete(pack.FullPath);
	}

	public void OpenDirectory(GameInstance instance)
	{
		string directory = GetDirectory(instance);
		Directory.CreateDirectory(directory);
		if (OperatingSystem.IsWindows())
		{
			Process.Start("explorer.exe", directory);
		}
		else if (OperatingSystem.IsMacOS())
		{
			Process.Start("open", directory);
		}
		else
		{
			Process.Start("xdg-open", directory);
		}
	}

	private static string AvailablePath(string directory, string fileName)
	{
		string text = Path.Combine(directory, fileName);
		if (!File.Exists(text))
		{
			return text;
		}
		string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(fileName);
		for (int i = 1; i < 1000; i++)
		{
			text = Path.Combine(directory, $"{fileNameWithoutExtension} ({i}).zip");
			if (!File.Exists(text))
			{
				return text;
			}
		}
		throw new IOException("同名 Shader 文件过多。");
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
