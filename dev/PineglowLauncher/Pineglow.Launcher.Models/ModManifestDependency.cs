namespace Pineglow.Launcher.Models;

public sealed class ModManifestDependency
{
	public string ModId { get; set; } = string.Empty;

	public string VersionRange { get; set; } = string.Empty;

	public bool IsRequired { get; set; } = true;
}
