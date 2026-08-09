using System;
using System.Text.Json.Serialization;

namespace Pineglow.Launcher.Models;

public sealed class FavoriteServer
{
	public string Id { get; set; } = Guid.NewGuid().ToString("N");

	public string Name { get; set; } = string.Empty;

	public string Host { get; set; } = string.Empty;

	public int Port { get; set; } = 25565;

	public string StatusText { get; set; } = "尚未检测";

	public string Motd { get; set; } = string.Empty;

	public string VersionName { get; set; } = string.Empty;

	public int OnlinePlayers { get; set; } = -1;

	public int MaxPlayers { get; set; } = -1;

	public long LatencyMs { get; set; } = -1L;

	public DateTimeOffset? LastCheckedAt { get; set; }

	[JsonIgnore]
	public byte[]? FaviconBytes { get; set; }
}
