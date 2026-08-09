using System;

namespace Pineglow.Launcher.Models;

public sealed class ModInstallMetadata
{
	public string Source { get; set; } = string.Empty;

	public string ProjectId { get; set; } = string.Empty;

	public string VersionId { get; set; } = string.Empty;

	public string VersionNumber { get; set; } = string.Empty;

	public DateTimeOffset InstalledAt { get; set; } = DateTimeOffset.UtcNow;
}
