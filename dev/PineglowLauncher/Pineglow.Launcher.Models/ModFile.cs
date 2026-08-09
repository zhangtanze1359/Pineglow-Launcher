using System;
using System.Collections.Generic;

namespace Pineglow.Launcher.Models;

public sealed class ModFile
{
	public string FileName { get; set; } = string.Empty;

	public string FullPath { get; set; } = string.Empty;

	public long SizeBytes { get; set; }

	public string SizeText { get; set; } = string.Empty;

	public bool IsEnabled { get; set; }

	public DateTime LastModifiedTime { get; set; }

	public string DisplayName { get; set; } = string.Empty;

	public string ModId { get; set; } = string.Empty;

	public string Version { get; set; } = string.Empty;

	public string LoaderHint { get; set; } = string.Empty;

	public string Summary { get; set; } = string.Empty;

	public string OnlineSource { get; set; } = string.Empty;

	public string ProjectId { get; set; } = string.Empty;

	public string VersionId { get; set; } = string.Empty;

	public string OnlineVersionNumber { get; set; } = string.Empty;

	public List<ModManifestDependency> Dependencies { get; set; } = new List<ModManifestDependency>();

	public bool HasOnlineMetadata
	{
		get
		{
			if (!string.IsNullOrWhiteSpace(OnlineSource) && !string.IsNullOrWhiteSpace(ProjectId))
			{
				return !string.IsNullOrWhiteSpace(VersionId);
			}
			return false;
		}
	}
}
