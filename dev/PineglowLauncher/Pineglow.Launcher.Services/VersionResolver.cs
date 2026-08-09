using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace Pineglow.Launcher.Services;

public static class VersionResolver
{
	public static async Task<MinecraftVersion> ResolveAsync(string gameDirectory, string versionId)
	{
		HashSet<string> visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		return await ResolveInternalAsync(gameDirectory, versionId, visited);
	}

	private static async Task<MinecraftVersion> ResolveInternalAsync(string gameDirectory, string versionId, HashSet<string> visited)
	{
		if (!visited.Add(versionId))
		{
			throw new InvalidOperationException("版本继承出现循环：" + versionId);
		}
		string versionJsonPath = GetVersionJsonPath(gameDirectory, versionId);
		if (!File.Exists(versionJsonPath))
		{
			throw new FileNotFoundException("未找到版本 JSON：" + versionJsonPath);
		}
		MinecraftVersion result;
		await using (FileStream stream = File.OpenRead(versionJsonPath))
		{
			using JsonDocument doc = await JsonDocument.ParseAsync(stream);
			JsonElement root = doc.RootElement.Clone();
			string parentId = JsonUtil.GetString(root, "inheritsFrom");
			MinecraftVersion minecraftVersion = (string.IsNullOrWhiteSpace(parentId) ? new MinecraftVersion() : (await ResolveInternalAsync(gameDirectory, parentId, visited)));
			minecraftVersion.Id = JsonUtil.GetString(root, "id") ?? versionId;
			minecraftVersion.InheritsFrom = parentId ?? minecraftVersion.InheritsFrom;
			minecraftVersion.Type = JsonUtil.GetString(root, "type") ?? minecraftVersion.Type;
			minecraftVersion.MainClass = JsonUtil.GetString(root, "mainClass") ?? minecraftVersion.MainClass;
			minecraftVersion.Assets = JsonUtil.GetString(root, "assets") ?? minecraftVersion.Assets;
			minecraftVersion.MinecraftArguments = JsonUtil.GetString(root, "minecraftArguments") ?? minecraftVersion.MinecraftArguments;
			if (root.TryGetProperty("arguments", out var value))
			{
				minecraftVersion.Arguments = value.Clone();
			}
			if (root.TryGetProperty("downloads", out var value2))
			{
				minecraftVersion.Downloads = value2.Clone();
			}
			if (root.TryGetProperty("assetIndex", out var value3))
			{
				minecraftVersion.AssetIndex = value3.Clone();
			}
			if (root.TryGetProperty("libraries", out var value4) && value4.ValueKind == JsonValueKind.Array)
			{
				foreach (JsonElement item in value4.EnumerateArray())
				{
					minecraftVersion.Libraries.Add(item.Clone());
				}
			}
			result = minecraftVersion;
		}
		return result;
	}

	public static string GetVersionJsonPath(string gameDirectory, string versionId)
	{
		return Path.Combine(gameDirectory, "versions", versionId, versionId + ".json");
	}

	public static string GetVersionJarPath(string gameDirectory, string versionId)
	{
		return Path.Combine(gameDirectory, "versions", versionId, versionId + ".jar");
	}

	public static List<string> ListLocalVersions(string gameDirectory)
	{
		string path = Path.Combine(gameDirectory, "versions");
		if (!Directory.Exists(path))
		{
			return new List<string>();
		}
		return (from x in Directory.EnumerateDirectories(path).Select(Path.GetFileName)
			where !string.IsNullOrWhiteSpace(x)
			where File.Exists(GetVersionJsonPath(gameDirectory, x))
			select x).OrderByDescending<string, string>((string x) => x, StringComparer.OrdinalIgnoreCase).Cast<string>().ToList();
	}
}
