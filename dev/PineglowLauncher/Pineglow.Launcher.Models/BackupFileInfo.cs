using System;

namespace Pineglow.Launcher.Models;

public sealed class BackupFileInfo
{
	public string FileName { get; set; } = string.Empty;

	public string FullPath { get; set; } = string.Empty;

	public string GroupName { get; set; } = string.Empty;

	public DateTimeOffset CreatedAt { get; set; }

	public long SizeBytes { get; set; }

	public string SizeText { get; set; } = string.Empty;
}
