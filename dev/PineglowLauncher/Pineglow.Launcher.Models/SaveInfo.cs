using System;

namespace Pineglow.Launcher.Models;

public sealed class SaveInfo
{
	public string FolderName { get; set; } = string.Empty;

	public string FullPath { get; set; } = string.Empty;

	public string DisplayName { get; set; } = string.Empty;

	public DateTime LastPlayed { get; set; }

	public string SizeText { get; set; } = string.Empty;

	public bool HasIcon { get; set; }

	public byte[]? IconBytes { get; set; }
}
