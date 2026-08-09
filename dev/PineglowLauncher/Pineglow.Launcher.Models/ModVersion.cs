using System.Collections.Generic;

namespace Pineglow.Launcher.Models;

public sealed class ModVersion
{
	public ModSource Source { get; set; }

	public string ProjectId { get; set; } = string.Empty;

	public string VersionId { get; set; } = string.Empty;

	public string DisplayName { get; set; } = string.Empty;

	public string VersionNumber { get; set; } = string.Empty;

	public string ReleaseType { get; set; } = string.Empty;

	public string FileName { get; set; } = string.Empty;

	public string DownloadUrl { get; set; } = string.Empty;

	public string Sha1 { get; set; } = string.Empty;

	public long FileSize { get; set; }

	public List<string> GameVersions { get; set; } = new List<string>();

	public List<string> Loaders { get; set; } = new List<string>();

	public List<ModDependency> Dependencies { get; set; } = new List<ModDependency>();
}
