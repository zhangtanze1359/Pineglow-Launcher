namespace Pineglow.Launcher.Models;

public sealed class LoaderVersionEntry
{
	public string Version { get; set; } = string.Empty;

	public bool Stable { get; set; }

	public string? DownloadUrl { get; set; }

	public string? MavenVersion { get; set; }

	public override string ToString()
	{
		if (!Stable)
		{
			return Version;
		}
		return Version + "  稳定";
	}
}
