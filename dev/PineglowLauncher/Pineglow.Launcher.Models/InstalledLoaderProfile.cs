namespace Pineglow.Launcher.Models;

public sealed class InstalledLoaderProfile
{
	public required string VersionId { get; init; }

	public required string MinecraftVersion { get; init; }

	public required ModLoaderKind LoaderKind { get; init; }

	public required string LoaderVersion { get; init; }
}
