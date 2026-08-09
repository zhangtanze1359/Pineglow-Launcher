namespace Pineglow.Launcher.Services;

public sealed class MinecraftServerStatus
{
	public string Motd { get; init; } = string.Empty;

	public string VersionName { get; init; } = string.Empty;

	public int OnlinePlayers { get; init; } = -1;

	public int MaxPlayers { get; init; } = -1;

	public long LatencyMs { get; init; } = -1L;

	public byte[]? FaviconBytes { get; init; }
}
