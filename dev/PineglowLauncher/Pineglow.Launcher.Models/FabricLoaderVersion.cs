using System.Text.Json.Serialization;

namespace Pineglow.Launcher.Models;

public sealed class FabricLoaderVersion
{
	[JsonPropertyName("separator")]
	public string Separator { get; set; } = ".";

	[JsonPropertyName("build")]
	public int Build { get; set; }

	[JsonPropertyName("maven")]
	public string Maven { get; set; } = string.Empty;

	[JsonPropertyName("version")]
	public string Version { get; set; } = string.Empty;

	[JsonPropertyName("stable")]
	public bool Stable { get; set; }

	public override string ToString()
	{
		if (!Stable)
		{
			return Version;
		}
		return Version + "  稳定";
	}
}
