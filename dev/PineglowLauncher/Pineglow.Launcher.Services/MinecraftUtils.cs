using System;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;

namespace Pineglow.Launcher.Services;

public static class MinecraftUtils
{
	public static string? SelectNativeClassifier(JsonElement library)
	{
		if (!library.TryGetProperty("natives", out var value) || value.ValueKind != JsonValueKind.Object)
		{
			return null;
		}
		string osRuleName = PlatformPaths.GetOsRuleName();
		if (!value.TryGetProperty(osRuleName, out var value2))
		{
			return null;
		}
		string text = value2.GetString();
		if (string.IsNullOrWhiteSpace(text))
		{
			return null;
		}
		return text.Replace("${arch}", Environment.Is64BitOperatingSystem ? "64" : "32", StringComparison.Ordinal);
	}

	public static string MavenPath(string name)
	{
		string[] array = name.Split(':');
		if (array.Length < 3)
		{
			return name.Replace(':', Path.DirectorySeparatorChar);
		}
		string path = array[0].Replace('.', Path.DirectorySeparatorChar);
		string text = array[1];
		string text2 = array[2];
		string value = ((array.Length >= 4) ? ("-" + array[3]) : string.Empty);
		string path2 = $"{text}-{text2}{value}.jar";
		return Path.Combine(path, text, text2, path2);
	}

	public static bool Sha1Matches(string path, string sha1)
	{
		try
		{
			using FileStream source = File.OpenRead(path);
			return Convert.ToHexString(SHA1.HashData(source)).ToLowerInvariant().Equals(sha1, StringComparison.OrdinalIgnoreCase);
		}
		catch
		{
			return false;
		}
	}
}
