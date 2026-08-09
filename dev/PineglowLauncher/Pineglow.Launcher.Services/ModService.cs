using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Pineglow.Launcher.Models;

namespace Pineglow.Launcher.Services;

public sealed class ModService
{
	private readonly string _defaultGameDirectory;

	public ModService(string defaultGameDirectory)
	{
		_defaultGameDirectory = (string.IsNullOrWhiteSpace(defaultGameDirectory) ? PlatformPaths.GetDefaultMinecraftDirectory() : defaultGameDirectory);
	}

	public async Task<List<ModFile>> GetModsAsync(GameInstance instance)
	{
		string modsDirectory = GetModsDirectory(instance);
		Directory.CreateDirectory(modsDirectory);
		List<string> source = Directory.EnumerateFiles(modsDirectory).Where(IsModFilePath).ToList();
		List<ModFile> mods = new List<ModFile>();
		SemaphoreSlim semaphore = new SemaphoreSlim(4);
		await Parallel.ForEachAsync(source, async delegate(string path, CancellationToken ct)
		{
			await semaphore.WaitAsync(ct);
			try
			{
				ModFile item = await Task.Run(() => CreateModFile(path), ct);
				lock (mods)
				{
					mods.Add(item);
				}
			}
			finally
			{
				semaphore.Release();
			}
		});
		return mods.OrderByDescending((ModFile x) => x.IsEnabled).ThenBy<ModFile, string>((ModFile x) => x.FileName, StringComparer.OrdinalIgnoreCase).ToList();
	}

	public string GetModsDirectory(GameInstance instance)
	{
		string obj = (string.IsNullOrWhiteSpace(instance.GameDirectory) ? _defaultGameDirectory : instance.GameDirectory);
		if (string.IsNullOrWhiteSpace(obj))
		{
			throw new InvalidOperationException("当前实例的游戏目录为空，请先在设置页选择 .minecraft 目录。");
		}
		return Path.Combine(obj, "mods");
	}

	public void EnableMod(ModFile mod)
	{
		if (!mod.IsEnabled)
		{
			if (!mod.FullPath.EndsWith(".jar.disabled", StringComparison.OrdinalIgnoreCase))
			{
				throw new InvalidOperationException("只能启用 .jar.disabled 文件。");
			}
			string fullPath = mod.FullPath;
			int length = ".disabled".Length;
			string text = fullPath.Substring(0, fullPath.Length - length);
			if (File.Exists(text))
			{
				throw new IOException("目标文件已存在：" + Path.GetFileName(text) + "。请先删除或重命名同名文件。");
			}
			File.Move(mod.FullPath, text);
			MoveOnlineMetadata(mod.FullPath, text);
		}
	}

	public void DisableMod(ModFile mod)
	{
		if (mod.IsEnabled)
		{
			if (!mod.FullPath.EndsWith(".jar", StringComparison.OrdinalIgnoreCase) || mod.FullPath.EndsWith(".jar.disabled", StringComparison.OrdinalIgnoreCase))
			{
				throw new InvalidOperationException("只能禁用 .jar 文件。");
			}
			string text = mod.FullPath + ".disabled";
			if (File.Exists(text))
			{
				throw new IOException("目标文件已存在：" + Path.GetFileName(text) + "。请先删除或重命名同名文件。");
			}
			File.Move(mod.FullPath, text);
			MoveOnlineMetadata(mod.FullPath, text);
		}
	}

	public void DeleteMod(ModFile mod)
	{
		if (!IsModFilePath(mod.FullPath))
		{
			throw new InvalidOperationException("只能删除 .jar 或 .jar.disabled 文件。为了安全，不会删除其他文件。");
		}
		if (!File.Exists(mod.FullPath))
		{
			throw new FileNotFoundException("要删除的 Mod 文件不存在。", mod.FullPath);
		}
		File.Delete(mod.FullPath);
		string metadataPath = GetMetadataPath(mod.FullPath);
		if (File.Exists(metadataPath))
		{
			File.Delete(metadataPath);
		}
	}

	public string ImportMod(GameInstance instance, string sourceFilePath)
	{
		if (string.IsNullOrWhiteSpace(sourceFilePath) || !File.Exists(sourceFilePath))
		{
			throw new FileNotFoundException("导入的 Mod 文件不存在。", sourceFilePath);
		}
		if (!sourceFilePath.EndsWith(".jar", StringComparison.OrdinalIgnoreCase))
		{
			throw new InvalidOperationException("只能导入 .jar 文件。请确认下载的是 Minecraft Mod 的 jar 文件。");
		}
		string modsDirectory = GetModsDirectory(instance);
		Directory.CreateDirectory(modsDirectory);
		string availableTargetPath = GetAvailableTargetPath(modsDirectory, Path.GetFileName(sourceFilePath));
		File.Copy(sourceFilePath, availableTargetPath, overwrite: false);
		return availableTargetPath;
	}

	public void WriteOnlineMetadata(string modPath, ModVersion version)
	{
		if (!File.Exists(modPath))
		{
			throw new FileNotFoundException("无法为不存在的 Mod 写入来源信息。", modPath);
		}
		ModInstallMetadata value = new ModInstallMetadata
		{
			Source = version.Source.ToString(),
			ProjectId = version.ProjectId,
			VersionId = version.VersionId,
			VersionNumber = version.VersionNumber,
			InstalledAt = DateTimeOffset.UtcNow
		};
		File.WriteAllText(GetMetadataPath(modPath), JsonSerializer.Serialize(value, JsonUtil.Options));
	}

	public void OpenModsDirectory(GameInstance instance)
	{
		string modsDirectory = GetModsDirectory(instance);
		Directory.CreateDirectory(modsDirectory);
		try
		{
			if (OperatingSystem.IsWindows())
			{
				Process.Start("explorer.exe", modsDirectory);
			}
			else if (OperatingSystem.IsMacOS())
			{
				Process.Start("open", modsDirectory);
			}
			else
			{
				Process.Start("xdg-open", modsDirectory);
			}
		}
		catch (Exception ex)
		{
			throw new InvalidOperationException("无法打开 mods 文件夹，请检查系统文件管理器是否可用。" + ex.Message, ex);
		}
	}

	private static bool IsModFilePath(string path)
	{
		if (!path.EndsWith(".jar", StringComparison.OrdinalIgnoreCase))
		{
			return path.EndsWith(".jar.disabled", StringComparison.OrdinalIgnoreCase);
		}
		return true;
	}

	private static ModFile CreateModFile(string path)
	{
		FileInfo fileInfo = new FileInfo(path);
		bool isEnabled = path.EndsWith(".jar", StringComparison.OrdinalIgnoreCase) && !path.EndsWith(".jar.disabled", StringComparison.OrdinalIgnoreCase);
		ModFile obj = new ModFile
		{
			FileName = fileInfo.Name,
			FullPath = fileInfo.FullName,
			SizeBytes = fileInfo.Length,
			SizeText = FormatSize(fileInfo.Length),
			IsEnabled = isEnabled,
			LastModifiedTime = fileInfo.LastWriteTime
		};
		ApplyLocalMetadata(obj);
		ApplyOnlineMetadata(obj);
		return obj;
	}

	private static void ApplyOnlineMetadata(ModFile mod)
	{
		string metadataPath = GetMetadataPath(mod.FullPath);
		if (!File.Exists(metadataPath))
		{
			return;
		}
		try
		{
			ModInstallMetadata modInstallMetadata = JsonSerializer.Deserialize<ModInstallMetadata>(File.ReadAllText(metadataPath), JsonUtil.Options);
			if (modInstallMetadata != null)
			{
				mod.OnlineSource = modInstallMetadata.Source;
				mod.ProjectId = modInstallMetadata.ProjectId;
				mod.VersionId = modInstallMetadata.VersionId;
				mod.OnlineVersionNumber = modInstallMetadata.VersionNumber;
			}
		}
		catch
		{
		}
	}

	private static void MoveOnlineMetadata(string sourceModPath, string targetModPath)
	{
		string metadataPath = GetMetadataPath(sourceModPath);
		if (File.Exists(metadataPath))
		{
			string metadataPath2 = GetMetadataPath(targetModPath);
			if (File.Exists(metadataPath2))
			{
				File.Delete(metadataPath2);
			}
			File.Move(metadataPath, metadataPath2);
		}
	}

	private static string GetMetadataPath(string modPath)
	{
		return modPath + ".mcmini.json";
	}

	private static void ApplyLocalMetadata(ModFile mod)
	{
		try
		{
			using ZipArchive archive = ZipFile.OpenRead(mod.FullPath);
			if (!TryApplyJsonMetadata(archive, "fabric.mod.json", "Fabric", mod) && !TryApplyJsonMetadata(archive, "quilt.mod.json", "Quilt", mod) && !TryApplyForgeTomlMetadata(archive, mod))
			{
				TryApplyMcModInfoMetadata(archive, mod);
			}
		}
		catch
		{
		}
	}

	private static bool TryApplyJsonMetadata(ZipArchive archive, string entryName, string loaderHint, ModFile mod)
	{
		ZipArchiveEntry entry = archive.GetEntry(entryName);
		if (entry == null)
		{
			return false;
		}
		using Stream utf8Json = entry.Open();
		using JsonDocument jsonDocument = JsonDocument.Parse(utf8Json);
		JsonElement rootElement = jsonDocument.RootElement;
		mod.LoaderHint = loaderHint;
		if (loaderHint == "Quilt" && rootElement.TryGetProperty("quilt_loader", out var value) && value.ValueKind == JsonValueKind.Object)
		{
			mod.ModId = ReadString(value, "id") ?? mod.ModId;
			mod.Version = ReadString(value, "version") ?? mod.Version;
			if (value.TryGetProperty("metadata", out var value2) && value2.ValueKind == JsonValueKind.Object)
			{
				mod.DisplayName = ReadString(value2, "name") ?? mod.ModId ?? mod.DisplayName;
				mod.Summary = ReadString(value2, "description") ?? mod.Summary;
			}
			AddJsonDependencies(value, mod);
			return true;
		}
		mod.ModId = ReadString(rootElement, "id") ?? mod.ModId;
		mod.DisplayName = ReadString(rootElement, "name") ?? ReadString(rootElement, "id") ?? mod.DisplayName;
		mod.Version = ReadString(rootElement, "version") ?? mod.Version;
		mod.Summary = ReadString(rootElement, "description") ?? mod.Summary;
		AddJsonDependencies(rootElement, mod);
		return true;
	}

	private static bool TryApplyForgeTomlMetadata(ZipArchive archive, ModFile mod)
	{
		ZipArchiveEntry entry = archive.GetEntry("META-INF/mods.toml");
		if (entry == null)
		{
			return false;
		}
		using StreamReader streamReader = new StreamReader(entry.Open());
		string text = streamReader.ReadToEnd();
		mod.LoaderHint = (text.Contains("modLoader=\"javafml\"", StringComparison.OrdinalIgnoreCase) ? "Forge / NeoForge" : "Forge");
		mod.ModId = ReadTomlString(text, "modId") ?? mod.ModId;
		mod.DisplayName = ReadTomlString(text, "displayName") ?? ReadTomlString(text, "modId") ?? mod.DisplayName;
		mod.Version = ReadTomlString(text, "version") ?? mod.Version;
		mod.Summary = ReadTomlString(text, "description") ?? mod.Summary;
		AddForgeDependencies(text, mod);
		return true;
	}

	private static bool TryApplyMcModInfoMetadata(ZipArchive archive, ModFile mod)
	{
		ZipArchiveEntry entry = archive.GetEntry("mcmod.info");
		if (entry == null)
		{
			return false;
		}
		using Stream utf8Json = entry.Open();
		using JsonDocument jsonDocument = JsonDocument.Parse(utf8Json);
		JsonElement element = ((jsonDocument.RootElement.ValueKind == JsonValueKind.Array) ? jsonDocument.RootElement.EnumerateArray().FirstOrDefault() : jsonDocument.RootElement);
		if (element.ValueKind != JsonValueKind.Object)
		{
			return false;
		}
		mod.LoaderHint = "Forge";
		mod.ModId = ReadString(element, "modid") ?? mod.ModId;
		mod.DisplayName = ReadString(element, "name") ?? ReadString(element, "modid") ?? mod.DisplayName;
		mod.Version = ReadString(element, "version") ?? mod.Version;
		mod.Summary = ReadString(element, "description") ?? mod.Summary;
		return true;
	}

	private static string? ReadString(JsonElement element, string name)
	{
		if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String)
		{
			return null;
		}
		return value.GetString();
	}

	private static void AddJsonDependencies(JsonElement root, ModFile mod)
	{
		if (!root.TryGetProperty("depends", out var value))
		{
			return;
		}
		if (value.ValueKind == JsonValueKind.Object)
		{
			foreach (JsonProperty item in value.EnumerateObject())
			{
				string versionRange = ((item.Value.ValueKind == JsonValueKind.String) ? (item.Value.GetString() ?? string.Empty) : ((item.Value.ValueKind == JsonValueKind.Array) ? string.Join(" ", from j in item.Value.EnumerateArray()
					where j.ValueKind == JsonValueKind.String
					select j.GetString()) : string.Empty));
				AddDependency(mod, item.Name, versionRange);
			}
			return;
		}
		if (value.ValueKind != JsonValueKind.Array)
		{
			return;
		}
		foreach (JsonElement item2 in value.EnumerateArray())
		{
			if (item2.ValueKind == JsonValueKind.String)
			{
				AddDependency(mod, item2.GetString() ?? string.Empty, string.Empty);
			}
			else if (item2.ValueKind == JsonValueKind.Object)
			{
				AddDependency(mod, ReadString(item2, "id") ?? string.Empty, ReadString(item2, "versions") ?? ReadString(item2, "version") ?? string.Empty);
			}
		}
	}

	private static void AddForgeDependencies(string text, ModFile mod)
	{
		bool inDependency = false;
		string dependencyId = string.Empty;
		string versionRange = string.Empty;
		bool required = true;
		string[] array = text.Split(new char[2] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
		for (int i = 0; i < array.Length; i++)
		{
			string text2 = array[i].Trim();
			if (text2.StartsWith("[[", StringComparison.Ordinal))
			{
				Commit();
				inDependency = text2.StartsWith("[[dependencies.", StringComparison.OrdinalIgnoreCase);
			}
			else if (inDependency)
			{
				if (text2.StartsWith("modId=", StringComparison.OrdinalIgnoreCase))
				{
					dependencyId = ReadTomlValue(text2);
				}
				else if (text2.StartsWith("versionRange=", StringComparison.OrdinalIgnoreCase))
				{
					versionRange = ReadTomlValue(text2);
				}
				else if (text2.StartsWith("mandatory=", StringComparison.OrdinalIgnoreCase))
				{
					required = !ReadTomlValue(text2).Equals("false", StringComparison.OrdinalIgnoreCase);
				}
			}
		}
		Commit();
		void Commit()
		{
			if (inDependency)
			{
				AddDependency(mod, dependencyId, versionRange, required);
			}
			dependencyId = string.Empty;
			versionRange = string.Empty;
			required = true;
		}
	}

	private static void AddDependency(ModFile mod, string modId, string versionRange, bool isRequired = true)
	{
		if (!string.IsNullOrWhiteSpace(modId) && !IsBuiltinDependency(modId) && !mod.Dependencies.Any((ModManifestDependency item) => item.ModId.Equals(modId, StringComparison.OrdinalIgnoreCase)))
		{
			mod.Dependencies.Add(new ModManifestDependency
			{
				ModId = modId.Trim(),
				VersionRange = versionRange.Trim(),
				IsRequired = isRequired
			});
		}
	}

	private static bool IsBuiltinDependency(string modId)
	{
		if (!modId.Equals("minecraft", StringComparison.OrdinalIgnoreCase) && !modId.Equals("java", StringComparison.OrdinalIgnoreCase) && !modId.Equals("fabricloader", StringComparison.OrdinalIgnoreCase) && !modId.Equals("quilt_loader", StringComparison.OrdinalIgnoreCase) && !modId.Equals("neoforge", StringComparison.OrdinalIgnoreCase))
		{
			return modId.Equals("forge", StringComparison.OrdinalIgnoreCase);
		}
		return true;
	}

	private static string ReadTomlValue(string line)
	{
		int num = line.IndexOf('=');
		if (num < 0)
		{
			return string.Empty;
		}
		string text = line.Substring(num + 1).Trim();
		if (text.Length < 2 || !text.StartsWith('"') || !text.EndsWith('"'))
		{
			return text;
		}
		string text2 = text;
		return text2.Substring(1, text2.Length - 1 - 1).Trim();
	}

	private static string? ReadTomlString(string text, string key)
	{
		string[] array = text.Split(new char[2] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
		for (int i = 0; i < array.Length; i++)
		{
			string text2 = array[i].Trim();
			if (text2.StartsWith(key + "=", StringComparison.OrdinalIgnoreCase))
			{
				string text3 = text2.Substring(key.Length + 1).Trim();
				if (text3.StartsWith("\"\"\"", StringComparison.Ordinal) && text3.EndsWith("\"\"\"", StringComparison.Ordinal) && text3.Length >= 6)
				{
					string text4 = text3;
					return text4.Substring(3, text4.Length - 3 - 3).Trim();
				}
				if (text3.StartsWith('"') && text3.EndsWith('"') && text3.Length >= 2)
				{
					string text4 = text3;
					return text4.Substring(1, text4.Length - 1 - 1).Trim();
				}
				return text3;
			}
		}
		return null;
	}

	private static string GetAvailableTargetPath(string directory, string fileName)
	{
		string text = Path.Combine(directory, fileName);
		if (!File.Exists(text))
		{
			return text;
		}
		string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(fileName);
		string extension = Path.GetExtension(fileName);
		for (int i = 1; i < 1000; i++)
		{
			text = Path.Combine(directory, $"{fileNameWithoutExtension} ({i}){extension}");
			if (!File.Exists(text))
			{
				return text;
			}
		}
		throw new IOException("同名 Mod 文件过多，无法自动生成新文件名。");
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
