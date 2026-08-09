namespace Pineglow.Launcher.Services;

public sealed class JavaInfo
{
	public string Path { get; init; } = string.Empty;

	public string VersionText { get; init; } = string.Empty;

	public int VersionMajor { get; init; }
}
