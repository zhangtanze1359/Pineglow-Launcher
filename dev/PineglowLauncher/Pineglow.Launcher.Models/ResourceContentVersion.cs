namespace Pineglow.Launcher.Models;

public sealed class ResourceContentVersion
{
	public string VersionId { get; set; } = string.Empty;

	public string VersionNumber { get; set; } = string.Empty;

	public string FileName { get; set; } = string.Empty;

	public string DownloadUrl { get; set; } = string.Empty;

	public string Sha1 { get; set; } = string.Empty;

	public long FileSize { get; set; }
}
