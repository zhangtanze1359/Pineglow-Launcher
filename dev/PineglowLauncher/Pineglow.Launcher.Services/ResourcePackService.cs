using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using Pineglow.Launcher.Models;

namespace Pineglow.Launcher.Services;

public sealed class ResourcePackService
{
	private readonly string _defaultGameDirectory;

	public ResourcePackService(string defaultGameDirectory)
	{
		_defaultGameDirectory = (string.IsNullOrWhiteSpace(defaultGameDirectory) ? PlatformPaths.GetDefaultMinecraftDirectory() : defaultGameDirectory);
	}

	public string GetResourcePacksDirectory(GameInstance instance)
	{
		string obj = (string.IsNullOrWhiteSpace(instance.GameDirectory) ? _defaultGameDirectory : instance.GameDirectory);
		if (string.IsNullOrWhiteSpace(obj))
		{
			throw new InvalidOperationException("当前实例的游戏目录为空，请先在设置页选择 .minecraft 目录。");
		}
		return Path.Combine(obj, "resourcepacks");
	}

	public List<ResourcePackInfo> GetResourcePacks(GameInstance instance)
	{
		string resourcePacksDirectory = GetResourcePacksDirectory(instance);
		Directory.CreateDirectory(resourcePacksDirectory);
		return Directory.EnumerateFiles(resourcePacksDirectory, "*.zip").Select(CreatePackInfo).OrderBy<ResourcePackInfo, string>((ResourcePackInfo x) => (x.DisplayName.Length <= 0) ? x.FileName : x.DisplayName, StringComparer.OrdinalIgnoreCase)
			.ToList();
	}

	public string ImportResourcePack(GameInstance instance, string sourceZipPath)
	{
		if (string.IsNullOrWhiteSpace(sourceZipPath) || !File.Exists(sourceZipPath))
		{
			throw new FileNotFoundException("导入的资源包文件不存在。", sourceZipPath);
		}
		if (!sourceZipPath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
		{
			throw new InvalidOperationException("只能导入 .zip 格式的资源包文件。");
		}
		string resourcePacksDirectory = GetResourcePacksDirectory(instance);
		Directory.CreateDirectory(resourcePacksDirectory);
		string fileName = Path.GetFileName(sourceZipPath);
		string availableTargetPath = GetAvailableTargetPath(resourcePacksDirectory, fileName);
		File.Copy(sourceZipPath, availableTargetPath, overwrite: false);
		return availableTargetPath;
	}

	public void DeleteResourcePack(ResourcePackInfo pack)
	{
		if (!pack.FullPath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
		{
			throw new InvalidOperationException("只能删除 .zip 格式的资源包文件。");
		}
		if (!File.Exists(pack.FullPath))
		{
			throw new FileNotFoundException("要删除的资源包文件不存在。", pack.FullPath);
		}
		File.Delete(pack.FullPath);
	}

	public void OpenResourcePacksDirectory(GameInstance instance)
	{
		string resourcePacksDirectory = GetResourcePacksDirectory(instance);
		Directory.CreateDirectory(resourcePacksDirectory);
		try
		{
			if (OperatingSystem.IsWindows())
			{
				Process.Start("explorer.exe", resourcePacksDirectory);
			}
			else if (OperatingSystem.IsMacOS())
			{
				Process.Start("open", resourcePacksDirectory);
			}
			else
			{
				Process.Start("xdg-open", resourcePacksDirectory);
			}
		}
		catch (Exception ex)
		{
			throw new InvalidOperationException("无法打开资源包文件夹。" + ex.Message, ex);
		}
	}

	private static ResourcePackInfo CreatePackInfo(string path)
	{
		FileInfo fileInfo = new FileInfo(path);
		ResourcePackInfo obj = new ResourcePackInfo
		{
			FileName = fileInfo.Name,
			FullPath = fileInfo.FullName,
			SizeText = FormatSize(fileInfo.Length)
		};
		ReadPackMetadata(obj);
		return obj;
	}

	private static void ReadPackMetadata(ResourcePackInfo pack)
	{
		try
		{
			using ZipArchive zipArchive = ZipFile.OpenRead(pack.FullPath);
			ZipArchiveEntry entry = zipArchive.GetEntry("pack.mcmeta");
			if (entry != null)
			{
				using Stream utf8Json = entry.Open();
				using JsonDocument jsonDocument = JsonDocument.Parse(utf8Json);
				if (jsonDocument.RootElement.TryGetProperty("pack", out var value))
				{
					pack.DisplayName = ReadString(value, "description") ?? string.Empty;
					if (value.TryGetProperty("pack_format", out var value2) && value2.TryGetInt32(out var value3))
					{
						pack.PackFormat = value3;
					}
				}
			}
			ZipArchiveEntry entry2 = zipArchive.GetEntry("pack.png");
			if (entry2 == null)
			{
				return;
			}
			using Stream stream = entry2.Open();
			using MemoryStream memoryStream = new MemoryStream();
			stream.CopyTo(memoryStream);
			pack.CoverBytes = memoryStream.ToArray();
			pack.HasCover = true;
		}
		catch
		{
		}
	}

	private static string? ReadString(JsonElement element, string name)
	{
		if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String)
		{
			return null;
		}
		return value.GetString();
	}

	private static string GetAvailableTargetPath(string directory, string fileName)
	{
		string text = Path.Combine(directory, fileName);
		if (!File.Exists(text))
		{
			return text;
		}
		string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(fileName);
		string value = ".zip";
		for (int i = 1; i < 1000; i++)
		{
			text = Path.Combine(directory, $"{fileNameWithoutExtension} ({i}){value}");
			if (!File.Exists(text))
			{
				return text;
			}
		}
		throw new IOException("同名资源包文件过多，无法自动生成新文件名。");
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
