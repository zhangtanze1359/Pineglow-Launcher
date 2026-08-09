namespace Pineglow.Launcher.Models;

public sealed class JavaInstallation
{
	public string JavaPath { get; set; } = string.Empty;

	public int VersionMajor { get; set; }

	public string VersionText { get; set; } = string.Empty;

	public string Vendor { get; set; } = string.Empty;

	public bool IsValid { get; set; }
}
