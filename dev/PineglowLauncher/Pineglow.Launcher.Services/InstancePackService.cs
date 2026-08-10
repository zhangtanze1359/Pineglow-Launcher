using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Pineglow.Launcher.Models;

namespace Pineglow.Launcher.Services;

public sealed class InstancePackService
{
	private sealed record ModrinthPackFile(string Path, List<string> Downloads, string Sha1, string Sha512);

	private const int MaxConcurrentPackDownloads = 6;

	private readonly HttpClient _http;

	private readonly Action<string> _log;

	private readonly string _defaultGameDir;

	public InstancePackService(HttpClient http, string defaultGameDir, Action<string> log)
	{
		_http = http;
		_defaultGameDir = defaultGameDir;
		_log = log;
	}

	public static ModpackArchiveFormat DetectModpackFormat(string zipPath)
	{
		using ZipArchive zipArchive = ZipFile.OpenRead(zipPath);
		if (zipArchive.Entries.Any((ZipArchiveEntry entry) => entry.FullName.Equals("modrinth.index.json", StringComparison.OrdinalIgnoreCase)))
		{
			return ModpackArchiveFormat.Modrinth;
		}
		if (zipArchive.Entries.Any((ZipArchiveEntry entry) => entry.FullName.Equals("manifest.json", StringComparison.OrdinalIgnoreCase)))
		{
			return ModpackArchiveFormat.CurseForge;
		}
		if (zipArchive.Entries.Any((ZipArchiveEntry entry) => entry.FullName.Equals("instance.json", StringComparison.OrdinalIgnoreCase)))
		{
			return ModpackArchiveFormat.Mcm;
		}
		return ModpackArchiveFormat.Unknown;
	}

	public async Task ExportInstanceAsync(GameInstance instance, string outputPath)
	{
		using ZipArchive zipArchive = ZipFile.Open(outputPath, ZipArchiveMode.Create);
		string value = JsonSerializer.Serialize(new
		{
			instance.Name, instance.MinecraftVersion, instance.BaseMinecraftVersion, instance.LoaderType, instance.LoaderVersion, instance.MaxMemoryMb, instance.UseAutoMemory, instance.WindowWidth, instance.WindowHeight, instance.AccountMode,
			instance.CustomJvmArgs, instance.CustomGameArgs, instance.Modpack
		}, JsonUtil.Options);
		using (StreamWriter streamWriter = new StreamWriter(zipArchive.CreateEntry("instance.json").Open()))
		{
			streamWriter.Write(value);
		}
		string gameDir = GetGameDir(instance);
		AddFolderToArchive(zipArchive, gameDir, "mods", "mods");
		AddFolderToArchive(zipArchive, gameDir, "config", "config");
		AddFolderToArchive(zipArchive, gameDir, "resourcepacks", "resourcepacks");
		AddFolderToArchive(zipArchive, gameDir, "shaderpacks", "shaderpacks");
		AddFolderToArchive(zipArchive, gameDir, "saves", "saves");
		AddFileToArchive(zipArchive, gameDir, "options.txt");
		AddFileToArchive(zipArchive, gameDir, "optionsof.txt");
		AddFileToArchive(zipArchive, gameDir, "servers.dat");
		AddFileToArchive(zipArchive, gameDir, "servers.dat_old");
	}

	public async Task<string> CreateUpgradeBackupAsync(GameInstance instance)
	{
		string text = Path.Combine(InstanceIsolationService.GetMinecraftRoot(GetGameDir(instance)), "backups", "modpack-upgrades");
		Directory.CreateDirectory(text);
		string value = SanitizeFileName(instance.Name);
		string output = Path.Combine(text, $"{value}-{DateTime.Now:yyyyMMdd-HHmmss}.mcm");
		await ExportInstanceAsync(instance, output);
		_log("已创建整合包更新备份：" + output);
		return output;
	}

	public List<BackupFileInfo> GetUpgradeBackups(GameInstance instance)
	{
		string upgradeBackupDirectory = GetUpgradeBackupDirectory(instance);
		if (!Directory.Exists(upgradeBackupDirectory))
		{
			return new List<BackupFileInfo>();
		}
		return (from item in Directory.EnumerateFiles(upgradeBackupDirectory, "*.mcm", SearchOption.TopDirectoryOnly).Select(delegate(string path)
			{
				FileInfo fileInfo = new FileInfo(path);
				return new BackupFileInfo
				{
					FileName = fileInfo.Name,
					FullPath = fileInfo.FullName,
					GroupName = "整合包更新",
					CreatedAt = fileInfo.LastWriteTimeUtc,
					SizeBytes = fileInfo.Length,
					SizeText = FormatSize(fileInfo.Length)
				};
			})
			orderby item.CreatedAt descending
			select item).ToList();
	}

	public void DeleteUpgradeBackup(GameInstance instance, BackupFileInfo backup)
	{
		string value = Path.GetFullPath(GetUpgradeBackupDirectory(instance)) + Path.DirectorySeparatorChar;
		string fullPath = Path.GetFullPath(backup.FullPath);
		StringComparison comparisonType = (OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
		if (!fullPath.StartsWith(value, comparisonType) || !fullPath.EndsWith(".mcm", StringComparison.OrdinalIgnoreCase))
		{
			throw new InvalidOperationException("只能删除当前游戏的整合包更新备份。");
		}
		if (!File.Exists(fullPath))
		{
			throw new FileNotFoundException("备份文件不存在。", fullPath);
		}
		File.Delete(fullPath);
	}

	public void OpenUpgradeBackupsDirectory(GameInstance instance)
	{
		string upgradeBackupDirectory = GetUpgradeBackupDirectory(instance);
		Directory.CreateDirectory(upgradeBackupDirectory);
		if (OperatingSystem.IsWindows())
		{
			Process.Start("explorer.exe", upgradeBackupDirectory);
		}
		else if (OperatingSystem.IsMacOS())
		{
			Process.Start("open", upgradeBackupDirectory);
		}
		else
		{
			Process.Start("xdg-open", upgradeBackupDirectory);
		}
	}

	public GameInstance ImportMcm(string zipPath)
	{
		using ZipArchive zipArchive = ZipFile.OpenRead(zipPath);
		GameInstance gameInstance;
		using (StreamReader streamReader = new StreamReader((zipArchive.GetEntry("instance.json") ?? throw new InvalidOperationException("导入文件不是有效的 Pineglow 实例包（缺少 instance.json）。")).Open()))
		{
			using JsonDocument jsonDocument = JsonDocument.Parse(streamReader.ReadToEnd());
			JsonElement rootElement = jsonDocument.RootElement;
			gameInstance = new GameInstance
			{
				Name = ReadString(rootElement, "Name") + " 导入",
				MinecraftVersion = ReadString(rootElement, "MinecraftVersion"),
				BaseMinecraftVersion = ReadString(rootElement, "BaseMinecraftVersion"),
				LoaderType = ReadString(rootElement, "LoaderType"),
				LoaderVersion = ReadString(rootElement, "LoaderVersion"),
				MaxMemoryMb = ReadInt(rootElement, "MaxMemoryMb", 4096),
				UseAutoMemory = (rootElement.TryGetProperty("UseAutoMemory", out var value) && value.ValueKind == JsonValueKind.True),
				WindowWidth = ReadInt(rootElement, "WindowWidth", 1280),
				WindowHeight = ReadInt(rootElement, "WindowHeight", 720),
				AccountMode = ReadString(rootElement, "AccountMode"),
				CustomJvmArgs = ReadString(rootElement, "CustomJvmArgs"),
				CustomGameArgs = ReadString(rootElement, "CustomGameArgs"),
				Modpack = ReadModpack(rootElement)
			};
		}
		string gameDir = GetGameDir(gameInstance);
		ExtractFolderFromArchive(zipArchive, "mods", Path.Combine(gameDir, "mods"));
		ExtractFolderFromArchive(zipArchive, "config", Path.Combine(gameDir, "config"));
		ExtractFolderFromArchive(zipArchive, "resourcepacks", Path.Combine(gameDir, "resourcepacks"));
		ExtractFolderFromArchive(zipArchive, "shaderpacks", Path.Combine(gameDir, "shaderpacks"));
		ExtractFolderFromArchive(zipArchive, "saves", Path.Combine(gameDir, "saves"));
		ExtractFileFromArchive(zipArchive, "options.txt", Path.Combine(gameDir, "options.txt"));
		ExtractFileFromArchive(zipArchive, "optionsof.txt", Path.Combine(gameDir, "optionsof.txt"));
		ExtractFileFromArchive(zipArchive, "servers.dat", Path.Combine(gameDir, "servers.dat"));
		ExtractFileFromArchive(zipArchive, "servers.dat_old", Path.Combine(gameDir, "servers.dat_old"));
		_log("Pineglow 实例导入完成：" + gameInstance.Name);
		return gameInstance;
	}

	public async Task<GameInstance> ImportCurseForgePackAsync(string zipPath, string curseForgeApiKey, Action<string> progress, CancellationToken ct)
	{
		using ZipArchive archive = ZipFile.OpenRead(zipPath);
		string minecraftVersion;
		string text;
		ModLoaderKind loaderKind;
		string loaderVersion;
		string overridesFolder;
		List<(int projectId, int fileId)> mods;
		using (StreamReader streamReader = new StreamReader((archive.GetEntry("manifest.json") ?? throw new InvalidOperationException("不是有效的 CurseForge 整合包（缺少 manifest.json）。")).Open()))
		{
			using JsonDocument jsonDocument = JsonDocument.Parse(streamReader.ReadToEnd());
			JsonElement rootElement = jsonDocument.RootElement;
			minecraftVersion = rootElement.GetProperty("minecraft").GetProperty("version").GetString() ?? "1.20.1";
			text = (rootElement.TryGetProperty("name", out var value) ? (value.GetString() ?? "CurseForge 整合包") : "CurseForge 整合包");
			JsonElement value2;
			(ModLoaderKind, string) tuple = ParseCurseForgeLoader(rootElement.GetProperty("modLoaders").EnumerateArray().First()
				.TryGetProperty("id", out value2) ? (value2.GetString() ?? string.Empty) : string.Empty);
			loaderKind = tuple.Item1;
			loaderVersion = tuple.Item2;
			overridesFolder = ReadString(rootElement, "overrides");
			if (string.IsNullOrWhiteSpace(overridesFolder))
			{
				overridesFolder = "overrides";
			}
			mods = new List<(int, int)>();
			foreach (JsonElement item in rootElement.GetProperty("files").EnumerateArray())
			{
				mods.Add(((int)item.GetProperty("projectID").GetInt64(), (int)item.GetProperty("fileID").GetInt64()));
			}
		}
		progress($"识别整合包：{text}，Minecraft {minecraftVersion}，{ModLoaderInfo.DisplayName(loaderKind)} {loaderVersion}，{mods.Count} 个 Mod");
		new MinecraftDownloader(_http, _log);
		MemoryRecommendation memoryRecommendation = MemoryAllocationService.Recommend(minecraftVersion, ModLoaderInfo.DisplayName(loaderKind), mods.Count);
		GameInstance instance = new GameInstance
		{
			Name = text,
			MinecraftVersion = minecraftVersion,
			BaseMinecraftVersion = minecraftVersion,
			LoaderType = ModLoaderInfo.DisplayName(loaderKind),
			LoaderVersion = loaderVersion,
			GameDirectory = CreateIsolatedGameDirectory(text ?? "CurseForge 整合包"),
			MaxMemoryMb = memoryRecommendation.RecommendedMb,
			UseAutoMemory = true
		};
		string javaPath = await ResolveJavaForVersionAsync(minecraftVersion, progress, ct);
		progress("安装 " + ModLoaderInfo.DisplayName(loaderKind) + " 加载器...");
		InstalledLoaderProfile installedLoaderProfile = await InstallLoaderAsync(loaderKind, instance, minecraftVersion, loaderVersion, javaPath, progress);
		instance.MinecraftVersion = installedLoaderProfile.VersionId;
		ModContentService modService = new ModContentService(_http);
		ConcurrentBag<string> failures = new ConcurrentBag<string>();
		int completed = 0;
		progress($"正在并行下载 CurseForge Mod：0/{mods.Count}，最多同时下载 4 个。");
		await Parallel.ForEachAsync(mods, new ParallelOptions
		{
			CancellationToken = ct,
			MaxDegreeOfParallelism = 4
		}, async delegate((int projectId, int fileId) mod, CancellationToken token)
		{
			var (projectId, fileId) = mod;
			try
			{
				ModVersion version = new ModVersion
				{
					Source = ModSource.CurseForge,
					ProjectId = projectId.ToString(),
					VersionId = fileId.ToString(),
					FileName = $"curseforge-{projectId}-{fileId}.jar"
				};
				await modService.DownloadAndInstallAsync(instance, version, new ModService(_defaultGameDir), curseForgeApiKey, null, token);
				int value3 = Interlocked.Increment(ref completed);
				progress($"正在下载 CurseForge Mod：{value3}/{mods.Count}（最近完成：{projectId}/{fileId}）");
			}
			catch (Exception ex) when (!(ex is OperationCanceledException))
			{
				_log($"  Mod {projectId} 下载失败：{ex.Message}");
				failures.Add($"{projectId}/{fileId}: {ex.Message}");
			}
		});
		ExtractFolderFromArchive(archive, overridesFolder.TrimEnd('/'), instance.GameDirectory);
		if (failures.Count > 0)
		{
			throw new InvalidOperationException($"整合包有 {failures.Count} 个 Mod 下载失败。第一个错误：{failures.First()}");
		}
		_log("CurseForge 整合包导入完成：" + instance.Name);
		return instance;
	}

	public async Task<GameInstance> ImportModrinthPackAsync(string zipPath, Action<string> progress, CancellationToken ct)
	{
		using ZipArchive archive = ZipFile.OpenRead(zipPath);
		ZipArchiveEntry? obj = archive.GetEntry("modrinth.index.json") ?? throw new InvalidOperationException("不是有效的 Modrinth 整合包（缺少 modrinth.index.json）。");
		string minecraftVersion = "1.21";
		string loaderKind = "Vanilla";
		string loaderVersion = "";
		string text;
		List<ModrinthPackFile> files;
		using (StreamReader streamReader = new StreamReader(obj.Open()))
		{
			using JsonDocument jsonDocument = JsonDocument.Parse(streamReader.ReadToEnd());
			JsonElement rootElement = jsonDocument.RootElement;
			text = ReadString(rootElement, "name") + " 导入";
			foreach (JsonProperty item in rootElement.GetProperty("dependencies").EnumerateObject())
			{
				if (item.NameEquals("minecraft"))
				{
					minecraftVersion = item.Value.GetString() ?? "1.21";
				}
				else if (item.NameEquals("fabric-loader"))
				{
					loaderKind = "Fabric";
					loaderVersion = item.Value.GetString() ?? "";
				}
				else if (item.NameEquals("quilt-loader"))
				{
					loaderKind = "Quilt";
					loaderVersion = item.Value.GetString() ?? "";
				}
				else if (item.NameEquals("neoforge"))
				{
					loaderKind = "NeoForge";
					loaderVersion = item.Value.GetString() ?? "";
				}
				else if (item.NameEquals("forge"))
				{
					loaderKind = "Forge";
					loaderVersion = item.Value.GetString() ?? "";
				}
			}
			files = new List<ModrinthPackFile>();
			foreach (JsonElement item2 in rootElement.GetProperty("files").EnumerateArray())
			{
				string text2 = ReadString(item2, "path");
				JsonElement value;
				List<string> list = ((item2.TryGetProperty("downloads", out value) && value.ValueKind == JsonValueKind.Array) ? (from x in value.EnumerateArray()
					where x.ValueKind == JsonValueKind.String
					select x.GetString() ?? string.Empty into x
					where x.Length > 0
					select x).ToList() : new List<string>());
				JsonElement value2;
				JsonElement element = (item2.TryGetProperty("hashes", out value2) ? value2 : default(JsonElement));
				string sha = ((element.ValueKind == JsonValueKind.Object) ? ReadString(element, "sha1") : string.Empty);
				string sha2 = ((element.ValueKind == JsonValueKind.Object) ? ReadString(element, "sha512") : string.Empty);
				bool flag = true;
				if (item2.TryGetProperty("env", out var value3) && value3.ValueKind == JsonValueKind.Object)
				{
					flag = !ReadString(value3, "client").Equals("unsupported", StringComparison.OrdinalIgnoreCase);
				}
				if (flag && !string.IsNullOrWhiteSpace(text2) && list.Count > 0)
				{
					files.Add(new ModrinthPackFile(text2, list, sha, sha2));
				}
			}
		}
		progress($"识别整合包：{text}，Minecraft {minecraftVersion}，{loaderKind} {loaderVersion}，{files.Count} 个客户端文件");
		GameInstance instance = null;
		try
		{
			MinecraftDownloader downloader = new MinecraftDownloader(_http, _log);
			MemoryRecommendation memoryRecommendation = MemoryAllocationService.Recommend(minecraftVersion, loaderKind, files.Count);
			instance = new GameInstance
			{
				Name = text,
				MinecraftVersion = minecraftVersion,
				BaseMinecraftVersion = minecraftVersion,
				LoaderType = loaderKind,
				LoaderVersion = loaderVersion,
				GameDirectory = CreateIsolatedGameDirectory(text),
				MaxMemoryMb = memoryRecommendation.RecommendedMb,
				UseAutoMemory = true
			};
			string javaPath = await ResolveJavaForVersionAsync(minecraftVersion, progress, ct);
			if (ModLoaderInfo.TryParse(loaderKind, out var kind))
			{
				progress("安装 " + loaderKind + " 加载器...");
				instance.MinecraftVersion = (await InstallLoaderAsync(kind, instance, minecraftVersion, loaderVersion, javaPath, progress)).VersionId;
			}
			else
			{
				progress("安装原版 Minecraft " + minecraftVersion + "...");
				await downloader.InstallVersionAsync(instance.GameDirectory, minecraftVersion, ct);
			}
			await DownloadModrinthPackFilesAsync(instance.GameDirectory, files, progress, ct);
			ExtractFolderFromArchive(archive, "overrides", instance.GameDirectory);
			ExtractFolderFromArchive(archive, "client-overrides", instance.GameDirectory);
			_log("Modrinth 整合包导入完成：" + instance.Name);
			return instance;
		}
		catch
		{
			if (instance != null && Directory.Exists(instance.GameDirectory))
			{
				Directory.Delete(instance.GameDirectory, recursive: true);
			}
			throw;
		}
	}

	private async Task<InstalledLoaderProfile> InstallLoaderAsync(ModLoaderKind kind, GameInstance instance, string mcVersion, string loaderVersion, string javaPath, Action<string> progress)
	{
		MinecraftDownloader downloader = new MinecraftDownloader(_http, _log);
		string gameDir = GetGameDir(instance);
		return kind switch
		{
			ModLoaderKind.Fabric => await new FabricService(_http, gameDir, downloader, _log).InstallFabricProfileAsync(instance, mcVersion, loaderVersion, new Progress<string>(delegate(string msg)
			{
				progress(msg);
			})), 
			ModLoaderKind.Quilt => await new QuiltService(_http, gameDir, downloader, _log).InstallQuiltProfileAsync(instance, mcVersion, loaderVersion, new Progress<string>(delegate(string msg)
			{
				progress(msg);
			})), 
			ModLoaderKind.Forge => await new ForgeService(_http, gameDir, downloader, _log).InstallProfileAsync(instance, mcVersion, new LoaderVersionEntry
			{
				Version = loaderVersion,
				MavenVersion = mcVersion + "-" + loaderVersion
			}, javaPath, new Progress<string>(delegate(string msg)
			{
				progress(msg);
			})), 
			ModLoaderKind.NeoForge => await new NeoForgeService(_http, gameDir, downloader, _log).InstallProfileAsync(instance, mcVersion, new LoaderVersionEntry
			{
				Version = loaderVersion,
				MavenVersion = loaderVersion
			}, javaPath, new Progress<string>(delegate(string msg)
			{
				progress(msg);
			})), 
			_ => throw new InvalidOperationException($"不支持的加载器：{kind}"), 
		};
	}

	private async Task DownloadModrinthPackFileAsync(string gameDirectory, ModrinthPackFile file, CancellationToken ct)
	{
		string target = GetSafeTargetPath(gameDirectory, file.Path);
		Directory.CreateDirectory(Path.GetDirectoryName(target));
		string temp = target + ".download";
		try
		{
			Exception lastError = null;
			foreach (string url in file.Downloads)
			{
				for (int attempt = 1; attempt <= 2; attempt++)
				{
					try
					{
						using HttpResponseMessage response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
						response.EnsureSuccessStatusCode();
						await using Stream input = await response.Content.ReadAsStreamAsync(ct);
						await using FileStream output = File.Create(temp);
						await input.CopyToAsync(output, ct);
						lastError = null;
					}
					catch (Exception ex) when (((ex is HttpRequestException || ex is IOException || ex is TaskCanceledException) ? 1 : 0) != 0)
					{
						lastError = ex;
						if (File.Exists(temp))
						{
							File.Delete(temp);
						}
						if (attempt < 2)
						{
							await Task.Delay(TimeSpan.FromSeconds(attempt), ct);
						}
						continue;
					}
					break;
				}
				if (lastError == null && File.Exists(temp))
				{
					break;
				}
			}
			if (lastError != null || !File.Exists(temp))
			{
				throw new InvalidOperationException("所有下载地址均失败：" + file.Path, lastError);
			}
			if (!string.IsNullOrWhiteSpace(file.Sha512) && !HashMatches(temp, file.Sha512, SHA512.Create()))
			{
				throw new InvalidDataException("SHA512 校验失败：" + file.Path);
			}
			if (!string.IsNullOrWhiteSpace(file.Sha1) && !HashMatches(temp, file.Sha1, SHA1.Create()))
			{
				throw new InvalidDataException("SHA1 校验失败：" + file.Path);
			}
			File.Move(temp, target, overwrite: true);
		}
		finally
		{
			if (File.Exists(temp))
			{
				File.Delete(temp);
			}
		}
	}

	private async Task DownloadModrinthPackFilesAsync(string gameDirectory, IReadOnlyList<ModrinthPackFile> files, Action<string> progress, CancellationToken ct)
	{
		if (files.Count != 0)
		{
			int completed = 0;
			progress($"正在并行下载整合包文件：0/{files.Count}，最多同时下载 {6} 个。");
			await Parallel.ForEachAsync(files, new ParallelOptions
			{
				CancellationToken = ct,
				MaxDegreeOfParallelism = 6
			}, async delegate(ModrinthPackFile file, CancellationToken token)
			{
				await DownloadModrinthPackFileAsync(gameDirectory, file, token);
				int value = Interlocked.Increment(ref completed);
				progress($"正在下载整合包文件：{value}/{files.Count}（最近完成：{file.Path}）");
			});
		}
	}

	private string CreateIsolatedGameDirectory(string name)
	{
		string text = new string(name.Select((char ch) => (!Path.GetInvalidFileNameChars().Contains(ch)) ? ch : '_').ToArray()).Trim();
		if (string.IsNullOrWhiteSpace(text))
		{
			text = "imported-pack";
		}
		string text2 = Path.Combine(FindMinecraftRoot(_defaultGameDir), "instances");
		Directory.CreateDirectory(text2);
		string text3 = Path.Combine(text2, text);
		int num = 1;
		while (Directory.Exists(text3))
		{
			text3 = Path.Combine(text2, $"{text} ({num})");
			num++;
		}
		Directory.CreateDirectory(text3);
		return text3;
	}

	private static string FindMinecraftRoot(string path)
	{
		DirectoryInfo directoryInfo = new DirectoryInfo(path);
		while (directoryInfo?.Parent != null)
		{
			if (directoryInfo.Name.Equals("instances", StringComparison.OrdinalIgnoreCase))
			{
				return directoryInfo.Parent.FullName;
			}
			directoryInfo = directoryInfo.Parent;
		}
		return path;
	}

	private static string GetUpgradeBackupDirectory(GameInstance instance)
	{
		return Path.Combine(InstanceIsolationService.GetMinecraftRoot(instance.GameDirectory), "backups", "modpack-upgrades");
	}

	private static (ModLoaderKind Kind, string Version) ParseCurseForgeLoader(string loaderId)
	{
		int num = loaderId.IndexOf('-');
		if (num <= 0 || num == loaderId.Length - 1)
		{
			throw new InvalidDataException("无法识别 CurseForge 加载器：" + loaderId);
		}
		string value = loaderId.Substring(0, num);
		string item = loaderId.Substring(num + 1);
		if (!ModLoaderInfo.TryParse(value, out var kind) || kind == ModLoaderKind.OptiFine)
		{
			throw new InvalidDataException("不支持的 CurseForge 加载器：" + loaderId);
		}
		return (Kind: kind, Version: item);
	}

	private static string GetSafeTargetPath(string targetDirectory, string relativePath)
	{
		string fullPath = Path.GetFullPath(targetDirectory);
		string fullPath2 = Path.GetFullPath(Path.Combine(fullPath, relativePath.Replace('/', Path.DirectorySeparatorChar)));
		string value = (fullPath.EndsWith(Path.DirectorySeparatorChar) ? fullPath : (fullPath + Path.DirectorySeparatorChar));
		StringComparison comparisonType = (OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
		if (!fullPath2.StartsWith(value, comparisonType))
		{
			throw new InvalidDataException("整合包包含不安全的文件路径：" + relativePath);
		}
		return fullPath2;
	}

	private static bool HashMatches(string path, string expected, HashAlgorithm algorithm)
	{
		using (algorithm)
		{
			using FileStream inputStream = File.OpenRead(path);
			return Convert.ToHexString(algorithm.ComputeHash(inputStream)).Equals(expected, StringComparison.OrdinalIgnoreCase);
		}
	}

	private string GetGameDir(GameInstance instance)
	{
		if (!string.IsNullOrWhiteSpace(instance.GameDirectory))
		{
			return instance.GameDirectory;
		}
		return _defaultGameDir;
	}

	private static void AddFolderToArchive(ZipArchive archive, string baseDir, string folderName, string entryPrefix)
	{
		string text = Path.Combine(baseDir, folderName);
		if (!Directory.Exists(text))
		{
			return;
		}
		foreach (string item in Directory.EnumerateFiles(text, "*", SearchOption.AllDirectories))
		{
			string entryName = entryPrefix + "/" + Path.GetRelativePath(text, item).Replace('\\', '/');
			archive.CreateEntryFromFile(item, entryName);
		}
	}

	private static void ExtractFolderFromArchive(ZipArchive archive, string entryPrefix, string targetDir)
	{
		string fullPath = Path.GetFullPath(targetDir);
		string value = (fullPath.EndsWith(Path.DirectorySeparatorChar) ? fullPath : (fullPath + Path.DirectorySeparatorChar));
		StringComparison comparisonType = (OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
		foreach (ZipArchiveEntry entry in archive.Entries)
		{
			if (!entry.FullName.StartsWith(entryPrefix + "/", StringComparison.OrdinalIgnoreCase))
			{
				continue;
			}
			string text = entry.FullName.Substring(entryPrefix.Length + 1);
			if (!string.IsNullOrWhiteSpace(text) && !text.EndsWith('/'))
			{
				string fullPath2 = Path.GetFullPath(Path.Combine(fullPath, text.Replace('/', Path.DirectorySeparatorChar)));
				if (!fullPath2.StartsWith(value, comparisonType))
				{
					throw new InvalidDataException("实例包包含不安全的文件路径：" + entry.FullName);
				}
				Directory.CreateDirectory(Path.GetDirectoryName(fullPath2));
				entry.ExtractToFile(fullPath2, overwrite: true);
			}
		}
	}

	private async Task<string> ResolveJavaForVersionAsync(string minecraftVersion, Action<string> progress, CancellationToken ct)
	{
		try
		{
			JavaInstallation javaInstallation = JavaLocator.MatchForMinecraftVersion(await JavaLocator.ScanAsync(_log), minecraftVersion, _log);
			if (javaInstallation != null)
			{
				return javaInstallation.JavaPath;
			}
			int requiredJavaMajor = JavaVersionMatcher.GetRequiredJavaMajor(minecraftVersion, _log);
			progress($"未找到 Java {requiredJavaMajor}，正在自动下载...");
			return (await new JavaRuntimeService(_http, _log).InstallAsync(requiredJavaMajor, new Progress<JavaRuntimeProgress>(delegate(JavaRuntimeProgress value)
			{
				progress(value.Message);
			}), ct)).JavaPath;
		}
		catch (Exception ex) when (!(ex is OperationCanceledException))
		{
			throw new InvalidOperationException("无法准备整合包所需 Java：" + ex.Message, ex);
		}
	}

	private static string ReadString(JsonElement element, string name)
	{
		if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String)
		{
			return string.Empty;
		}
		return value.GetString() ?? string.Empty;
	}

	private static int ReadInt(JsonElement element, string name, int fallback)
	{
		if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(name, out var value) || !value.TryGetInt32(out var value2))
		{
			return fallback;
		}
		return value2;
	}

	private static ModpackInstallation? ReadModpack(JsonElement root)
	{
		if (!root.TryGetProperty("Modpack", out var value) || value.ValueKind != JsonValueKind.Object)
		{
			return null;
		}
		try
		{
			return JsonSerializer.Deserialize<ModpackInstallation>(value.GetRawText(), JsonUtil.Options);
		}
		catch (JsonException)
		{
			return null;
		}
	}

	private static void AddFileToArchive(ZipArchive archive, string root, string relativePath)
	{
		string text = Path.Combine(root, relativePath);
		if (File.Exists(text))
		{
			archive.CreateEntryFromFile(text, relativePath, CompressionLevel.Fastest);
		}
	}

	private static void ExtractFileFromArchive(ZipArchive archive, string entryName, string outputPath)
	{
		ZipArchiveEntry entry = archive.GetEntry(entryName);
		if (entry != null)
		{
			Directory.CreateDirectory(Path.GetDirectoryName(outputPath));
			entry.ExtractToFile(outputPath, overwrite: true);
		}
	}

	private static string SanitizeFileName(string value)
	{
		char[] invalid = Path.GetInvalidFileNameChars();
		string text = new string(value.Where((char ch) => !invalid.Contains(ch)).ToArray()).Trim();
		if (!string.IsNullOrWhiteSpace(text))
		{
			return text;
		}
		return "modpack";
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
