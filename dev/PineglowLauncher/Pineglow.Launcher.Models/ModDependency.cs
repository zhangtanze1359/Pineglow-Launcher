namespace Pineglow.Launcher.Models;

public sealed class ModDependency
{
	public ModSource Source { get; set; }

	public string ProjectId { get; set; } = string.Empty;

	public string VersionId { get; set; } = string.Empty;

	public bool Required { get; set; }
}
