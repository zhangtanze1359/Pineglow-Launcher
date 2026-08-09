using System.Text.Json.Serialization;

namespace Pineglow.Launcher.Models;

public sealed class FabricCompatibleLoaderVersion
{
	[JsonPropertyName("loader")]
	public FabricLoaderVersion Loader { get; set; } = new FabricLoaderVersion();

	public string Version => Loader.Version;

	public bool Stable => Loader.Stable;

	public override string ToString()
	{
		return Loader.ToString();
	}
}
