namespace Pineglow.Launcher.Models;

public sealed class ResourcePackInfo
{
	public string FileName { get; set; } = string.Empty;

	public string FullPath { get; set; } = string.Empty;

	public string DisplayName { get; set; } = string.Empty;

	public string Description { get; set; } = string.Empty;

	public int PackFormat { get; set; }

	public string SizeText { get; set; } = string.Empty;

	public bool HasCover { get; set; }

	public byte[]? CoverBytes { get; set; }
}
