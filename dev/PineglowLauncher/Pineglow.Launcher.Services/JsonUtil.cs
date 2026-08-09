using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace Pineglow.Launcher.Services;

public static class JsonUtil
{
	public static readonly JsonSerializerOptions Options = new JsonSerializerOptions
	{
		PropertyNameCaseInsensitive = true,
		WriteIndented = true
	};

	public static string? GetString(JsonElement element, string property)
	{
		if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var value))
		{
			if (value.ValueKind != JsonValueKind.String)
			{
				return value.ToString();
			}
			return value.GetString();
		}
		return null;
	}

	public static JsonElement? GetProperty(JsonElement element, string property)
	{
		if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var value))
		{
			return value;
		}
		return null;
	}

	public static Dictionary<string, string> BuildReplacements(Dictionary<string, string> values)
	{
		return values.ToDictionary<KeyValuePair<string, string>, string, string>((KeyValuePair<string, string> kv) => "${" + kv.Key + "}", (KeyValuePair<string, string> kv) => kv.Value);
	}

	public static string ReplacePlaceholders(string input, Dictionary<string, string> replacements)
	{
		string text = input;
		foreach (KeyValuePair<string, string> replacement in replacements)
		{
			text = text.Replace(replacement.Key, replacement.Value, StringComparison.Ordinal);
		}
		return text;
	}
}
