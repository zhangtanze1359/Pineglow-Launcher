using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Pineglow.Launcher.Services;

public static class RulesEvaluator
{
	public static bool IsAllowed(JsonElement element, IReadOnlyDictionary<string, bool>? features = null, Version? osVersion = null)
	{
		if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty("rules", out var value))
		{
			return true;
		}
		if (value.ValueKind != JsonValueKind.Array)
		{
			return true;
		}
		bool result = false;
		foreach (JsonElement item in value.EnumerateArray())
		{
			string text = JsonUtil.GetString(item, "action") ?? "allow";
			if (RuleMatches(item, features, osVersion ?? Environment.OSVersion.Version))
			{
				result = text.Equals("allow", StringComparison.OrdinalIgnoreCase);
			}
		}
		return result;
	}

	private static bool RuleMatches(JsonElement rule, IReadOnlyDictionary<string, bool>? features, Version osVersion)
	{
		if (rule.TryGetProperty("os", out var value) && value.ValueKind == JsonValueKind.Object)
		{
			string osRuleName = PlatformPaths.GetOsRuleName();
			if (value.TryGetProperty("name", out var value2))
			{
				string text = value2.GetString();
				if (!string.IsNullOrWhiteSpace(text) && !text.Equals(osRuleName, StringComparison.OrdinalIgnoreCase))
				{
					return false;
				}
			}
			if (value.TryGetProperty("arch", out var value3))
			{
				string text2 = NormalizeArchitecture(value3.GetString());
				if (!string.IsNullOrWhiteSpace(text2) && !text2.Equals(CurrentArchitecture(), StringComparison.OrdinalIgnoreCase))
				{
					return false;
				}
			}
			if (value.TryGetProperty("version", out var value4))
			{
				string text3 = value4.GetString();
				if (!string.IsNullOrWhiteSpace(text3))
				{
					try
					{
						if (!Regex.IsMatch(Environment.OSVersion.VersionString, text3, RegexOptions.IgnoreCase))
						{
							return false;
						}
					}
					catch (ArgumentException)
					{
						return false;
					}
				}
			}
			if (value.TryGetProperty("versionRange", out var value5) && value5.ValueKind == JsonValueKind.Object)
			{
				string text4 = JsonUtil.GetString(value5, "min");
				if (!string.IsNullOrWhiteSpace(text4) && Version.TryParse(text4, out Version result) && osVersion.CompareTo(result) < 0)
				{
					return false;
				}
				string text5 = JsonUtil.GetString(value5, "max");
				if (!string.IsNullOrWhiteSpace(text5) && Version.TryParse(text5, out Version result2) && osVersion.CompareTo(result2) >= 0)
				{
					return false;
				}
			}
		}
		if (rule.TryGetProperty("features", out var value6) && value6.ValueKind == JsonValueKind.Object)
		{
			bool value7 = default(bool);
			foreach (JsonProperty item in value6.EnumerateObject())
			{
				JsonValueKind valueKind = item.Value.ValueKind;
				if (valueKind - 5 > JsonValueKind.Object)
				{
					return false;
				}
				bool boolean = item.Value.GetBoolean();
				if (((features?.TryGetValue(item.Name, out value7) ?? false) && value7) != boolean)
				{
					return false;
				}
			}
		}
		return true;
	}

	private static string CurrentArchitecture()
	{
		return RuntimeInformation.OSArchitecture switch
		{
			Architecture.X86 => "x86", 
			Architecture.X64 => "x86_64", 
			Architecture.Arm => "arm", 
			Architecture.Arm64 => "arm64", 
			_ => RuntimeInformation.OSArchitecture.ToString().ToLowerInvariant(), 
		};
	}

	private static string NormalizeArchitecture(string? architecture)
	{
		string text = architecture?.Trim().ToLowerInvariant();
		switch (text)
		{
		case "x64":
		case "amd64":
		case "x86-64":
			return "x86_64";
		case "aarch64":
			return "arm64";
		default:
			return text ?? string.Empty;
		}
	}
}
