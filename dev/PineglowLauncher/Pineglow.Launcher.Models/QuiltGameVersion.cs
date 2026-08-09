using System.Text.Json.Serialization;

namespace Pineglow.Launcher.Models;

public sealed class QuiltGameVersion
{
	[JsonPropertyName("version")]
	public string Version { get; set; } = string.Empty;

	[JsonPropertyName("stable")]
	public bool Stable { get; set; }
}
