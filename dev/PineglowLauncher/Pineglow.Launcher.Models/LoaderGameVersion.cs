namespace Pineglow.Launcher.Models;

public sealed class LoaderGameVersion
{
	public string Version { get; set; } = string.Empty;

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
