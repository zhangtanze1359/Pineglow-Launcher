using System.Collections.Generic;
using System.Text.Json;

namespace Pineglow.Launcher.Services;

public sealed class MinecraftVersion
{
	public string Id { get; set; } = string.Empty;

	public string? Type { get; set; }

	public string? MainClass { get; set; }

	public string? Assets { get; set; }

	public string? MinecraftArguments { get; set; }

	public string? InheritsFrom { get; set; }

	public JsonElement? Arguments { get; set; }

	public JsonElement? Downloads { get; set; }

	public JsonElement? AssetIndex { get; set; }

	public List<JsonElement> Libraries { get; } = new List<JsonElement>();
}
