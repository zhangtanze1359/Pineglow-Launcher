using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Pineglow.Launcher.Services;

public static class JavaVersionMatcher
{
	public static int GetRequiredJavaMajor(string? minecraftVersion, Action<string>? log = null)
	{
		if (string.IsNullOrWhiteSpace(minecraftVersion))
		{
			log?.Invoke("无法识别版本，默认使用 Java 17");
			return 17;
		}
		string input = minecraftVersion.Trim();
		if (Regex.IsMatch(input, "^\\d{2}w\\d{2}[a-z]$", RegexOptions.IgnoreCase))
		{
			return 21;
		}
		Match match = Regex.Match(input, "^(\\d+)\\.(\\d+)(?:\\.(\\d+))?");
		if (!match.Success)
		{
			log?.Invoke("无法识别版本，默认使用 Java 17");
			return 17;
		}
		int num = int.Parse(match.Groups[1].Value);
		int num2 = int.Parse(match.Groups[2].Value);
		int num3 = (match.Groups[3].Success ? int.Parse(match.Groups[3].Value) : 0);
		if (num >= 26)
		{
			return 25;
		}
		if (num > 1)
		{
			return 21;
		}
		if (num2 <= 16)
		{
			return 8;
		}
		if (num2 == 17)
		{
			return 16;
		}
		if (num2 >= 21)
		{
			return 21;
		}
		if (num2 == 20 && num3 >= 5)
		{
			return 21;
		}
		return 17;
	}

	public static int GetRequiredJavaMajor(string? minecraftVersion, string? gameDirectory, Action<string>? log = null)
	{
		int num = ReadRequiredMajorFromVersionMetadata(minecraftVersion, gameDirectory, log);
		if (num <= 0)
		{
			return GetRequiredJavaMajor(minecraftVersion, log);
		}
		return num;
	}

	public static bool IsCompatible(int actualMajor, int requiredMajor)
	{
		return actualMajor == requiredMajor;
	}

	private static int ReadRequiredMajorFromVersionMetadata(string? minecraftVersion, string? gameDirectory, Action<string>? log)
	{
		if (string.IsNullOrWhiteSpace(minecraftVersion) || string.IsNullOrWhiteSpace(gameDirectory))
		{
			return 0;
		}
		string text = minecraftVersion.Trim();
		HashSet<string> hashSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		while (hashSet.Add(text))
		{
			string versionJsonPath = VersionResolver.GetVersionJsonPath(gameDirectory, text);
			if (!File.Exists(versionJsonPath))
			{
				return 0;
			}
			try
			{
				using JsonDocument jsonDocument = JsonDocument.Parse(File.ReadAllText(versionJsonPath));
				JsonElement rootElement = jsonDocument.RootElement;
				if (rootElement.TryGetProperty("javaVersion", out var value) && value.ValueKind == JsonValueKind.Object && value.TryGetProperty("majorVersion", out var value2) && value2.TryGetInt32(out var value3) && value3 > 0)
				{
					log?.Invoke($"从版本元数据读取到 Java {value3}：{text}");
					return value3;
				}
				string text2 = JsonUtil.GetString(rootElement, "inheritsFrom");
				if (string.IsNullOrWhiteSpace(text2))
				{
					return 0;
				}
				text = text2;
			}
			catch (Exception ex)
			{
				log?.Invoke("读取版本 Java 要求失败：" + text + " - " + ex.Message);
				return 0;
			}
		}
		return 0;
	}
}
