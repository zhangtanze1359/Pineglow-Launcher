using System;
using System.Collections.Generic;

namespace Pineglow.Launcher.Models;

public sealed class ModInstallResult
{
	public string MainFilePath { get; set; } = string.Empty;

	public List<string> InstalledFiles { get; set; } = new List<string>();

	public int DependencyCount => Math.Max(0, InstalledFiles.Count - 1);
}
