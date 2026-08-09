using System;

namespace Pineglow.Launcher.Models;

public sealed class ModpackInstallation
{
	public string Source { get; set; } = string.Empty;

	public string ProjectId { get; set; } = string.Empty;

	public string ProjectName { get; set; } = string.Empty;

	public string VersionId { get; set; } = string.Empty;

	public string VersionName { get; set; } = string.Empty;

	public string WebsiteUrl { get; set; } = string.Empty;

	public DateTimeOffset InstalledAt { get; set; } = DateTimeOffset.UtcNow;
}
