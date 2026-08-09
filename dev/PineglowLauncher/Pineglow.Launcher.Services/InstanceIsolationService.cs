using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Pineglow.Launcher.Services;

public static class InstanceIsolationService
{
	private static readonly string[] UserDirectories = new string[6] { "mods", "config", "saves", "resourcepacks", "shaderpacks", "screenshots" };

	private static readonly string[] UserFiles = new string[4] { "options.txt", "optionsof.txt", "servers.dat", "servers.dat_old" };

	public static string GetMinecraftRoot(string path)
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

	public static string GetAvailableInstanceDirectory(string minecraftRoot, string name, string fallbackId)
	{
		string text = new string(name.Select((char ch) => (!Path.GetInvalidFileNameChars().Contains(ch)) ? ch : '_').ToArray()).Trim();
		if (string.IsNullOrWhiteSpace(text))
		{
			text = fallbackId;
		}
		string text2 = Path.Combine(GetMinecraftRoot(minecraftRoot), "instances");
		Directory.CreateDirectory(text2);
		string text3 = Path.Combine(text2, text);
		int num = 1;
		while (Directory.Exists(text3))
		{
			text3 = Path.Combine(text2, $"{text} ({num})");
			num++;
		}
		return text3;
	}

	public static async Task CloneAsync(string sourceRoot, string targetRoot, string versionId, bool includeUserData, IProgress<string>? progress = null, CancellationToken ct = default(CancellationToken))
	{
		sourceRoot = Path.GetFullPath(sourceRoot);
		targetRoot = Path.GetFullPath(targetRoot);
		if (sourceRoot.Equals(targetRoot, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
		{
			throw new InvalidOperationException("独立实例目标目录不能与源目录相同。");
		}
		if (Directory.Exists(targetRoot))
		{
			throw new IOException("实例目标目录已存在：" + targetRoot);
		}
		Directory.CreateDirectory(targetRoot);
		try
		{
			progress?.Report("正在分析版本运行文件...");
			HashSet<string> relativeFiles = new HashSet<string>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
			await AddVersionChainAsync(sourceRoot, versionId, relativeFiles, ct);
			MinecraftVersion version = await VersionResolver.ResolveAsync(sourceRoot, versionId);
			AddLibraries(sourceRoot, version, relativeFiles);
			await AddAssetsAsync(sourceRoot, version, relativeFiles, ct);
			if (includeUserData)
			{
				AddUserData(sourceRoot, relativeFiles);
			}
			List<string> files = relativeFiles.Where((string relative) => File.Exists(Path.Combine(sourceRoot, relative))).ToList();
			int completed = 0;
			await Parallel.ForEachAsync(files, new ParallelOptions
			{
				CancellationToken = ct,
				MaxDegreeOfParallelism = 8
			}, async delegate(string relative, CancellationToken token)
			{
				string path = Path.Combine(sourceRoot, relative);
				string path2 = Path.Combine(targetRoot, relative);
				Directory.CreateDirectory(Path.GetDirectoryName(path2));
				await using FileStream input = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read);
				await using FileStream output = File.Create(path2);
				await input.CopyToAsync(output, token);
				int num = Interlocked.Increment(ref completed);
				if (num % 100 == 0 || num == files.Count)
				{
					progress?.Report($"正在复制实例文件：{num}/{files.Count}");
				}
			});
			progress?.Report($"独立实例准备完成，共复制 {files.Count} 个文件。");
		}
		catch
		{
			if (Directory.Exists(targetRoot))
			{
				Directory.Delete(targetRoot, recursive: true);
			}
			throw;
		}
	}

	private static async Task AddVersionChainAsync(string root, string versionId, HashSet<string> files, CancellationToken ct)
	{
		string text = versionId;
		HashSet<string> visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		while (!string.IsNullOrWhiteSpace(text) && visited.Add(text))
		{
			ct.ThrowIfCancellationRequested();
			string text2 = Path.Combine(root, "versions", text);
			if (!Directory.Exists(text2))
			{
				throw new DirectoryNotFoundException("源版本目录不存在：" + text);
			}
			AddDirectory(root, text2, files);
			using JsonDocument jsonDocument = JsonDocument.Parse(await File.ReadAllTextAsync(VersionResolver.GetVersionJsonPath(root, text), ct));
			text = JsonUtil.GetString(jsonDocument.RootElement, "inheritsFrom") ?? string.Empty;
		}
	}

	private static void AddLibraries(string root, MinecraftVersion version, HashSet<string> files)
	{
		foreach (JsonElement library in version.Libraries)
		{
			if (!RulesEvaluator.IsAllowed(library))
			{
				continue;
			}
			if (library.TryGetProperty("downloads", out var value) && value.ValueKind == JsonValueKind.Object)
			{
				if (value.TryGetProperty("artifact", out var value2))
				{
					AddArtifact(root, value2, files);
				}
				string text = MinecraftUtils.SelectNativeClassifier(library);
				if (!string.IsNullOrWhiteSpace(text) && value.TryGetProperty("classifiers", out var value3) && value3.TryGetProperty(text, out var value4))
				{
					AddArtifact(root, value4, files);
				}
			}
			else
			{
				string text2 = JsonUtil.GetString(library, "name");
				if (!string.IsNullOrWhiteSpace(text2))
				{
					files.Add(Path.Combine("libraries", MinecraftUtils.MavenPath(text2)));
				}
			}
		}
	}

	private static void AddArtifact(string root, JsonElement artifact, HashSet<string> files)
	{
		string text = JsonUtil.GetString(artifact, "path");
		if (!string.IsNullOrWhiteSpace(text))
		{
			files.Add(Path.Combine("libraries", text.Replace('/', Path.DirectorySeparatorChar)));
		}
	}

	private static async Task AddAssetsAsync(string root, MinecraftVersion version, HashSet<string> files, CancellationToken ct)
	{
		JsonElement? assetIndex = version.AssetIndex;
		object obj;
		if (assetIndex.HasValue)
		{
			JsonElement valueOrDefault = assetIndex.GetValueOrDefault();
			if (valueOrDefault.ValueKind == JsonValueKind.Object)
			{
				obj = JsonUtil.GetString(valueOrDefault, "id");
				goto IL_004a;
			}
		}
		obj = null;
		goto IL_004a;
		IL_004a:
		string text = (string)obj;
		if (text == null)
		{
			text = version.Assets ?? version.Id;
		}
		string text2 = Path.Combine("assets", "indexes", text + ".json");
		string text3 = Path.Combine(root, text2);
		if (!File.Exists(text3))
		{
			throw new FileNotFoundException("资源索引不存在，无法创建完整独立实例。", text3);
		}
		files.Add(text2);
		await using FileStream stream = File.OpenRead(text3);
		CancellationToken cancellationToken = ct;
		using JsonDocument jsonDocument = await JsonDocument.ParseAsync(stream, default(JsonDocumentOptions), cancellationToken);
		if (!jsonDocument.RootElement.TryGetProperty("objects", out var value))
		{
			return;
		}
		foreach (JsonProperty item in value.EnumerateObject())
		{
			ct.ThrowIfCancellationRequested();
			string text4 = JsonUtil.GetString(item.Value, "hash");
			if (!string.IsNullOrWhiteSpace(text4) && text4.Length >= 2)
			{
				files.Add(Path.Combine("assets", "objects", text4.Substring(0, 2), text4));
			}
		}
	}

	private static void AddUserData(string root, HashSet<string> files)
	{
		string[] userDirectories = UserDirectories;
		foreach (string path in userDirectories)
		{
			string text = Path.Combine(root, path);
			if (Directory.Exists(text))
			{
				AddDirectory(root, text, files);
			}
		}
		userDirectories = UserFiles;
		foreach (string text2 in userDirectories)
		{
			if (File.Exists(Path.Combine(root, text2)))
			{
				files.Add(text2);
			}
		}
	}

	private static void AddDirectory(string root, string directory, HashSet<string> files)
	{
		foreach (string item in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
		{
			files.Add(Path.GetRelativePath(root, item));
		}
	}
}
