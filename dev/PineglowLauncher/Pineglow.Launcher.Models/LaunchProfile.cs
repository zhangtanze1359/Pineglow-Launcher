namespace Pineglow.Launcher.Models;

public sealed class LaunchProfile
{
	public string GameDirectory { get; set; }

	public string JavaPath { get; set; }

	public string VersionId { get; set; }

	public string PlayerName { get; set; }

	public int MaxMemoryMb { get; set; }

	public int WindowWidth { get; set; }

	public int WindowHeight { get; set; }

	public string ServerHost { get; set; } = string.Empty;

	public string ServerPort { get; set; } = "25565";

	public string AccountMode { get; set; } = "offline";

	public string PlayerUuid { get; set; } = string.Empty;

	public string AccessToken { get; set; } = "0";

	public string UserType { get; set; } = "legacy";

	public string ClientId { get; set; } = string.Empty;

	public string Xuid { get; set; } = string.Empty;

	public string CustomJvmArgs { get; set; } = string.Empty;

	public string CustomGameArgs { get; set; } = string.Empty;
}
