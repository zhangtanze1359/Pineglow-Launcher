using System;

namespace Pineglow.Launcher.Models;

public sealed class GameInstance
{
	public string Id { get; set; } = Guid.NewGuid().ToString("N");

	public string Name { get; set; } = "默认实例";

	public string MinecraftVersion { get; set; } = string.Empty;

	public string GameDirectory { get; set; } = string.Empty;

	public string JavaPath { get; set; } = string.Empty;

	public int MaxMemoryMb { get; set; } = 4096;

	public bool UseAutoMemory { get; set; }

	public int WindowWidth { get; set; } = 1280;

	public int WindowHeight { get; set; } = 720;

	public string ServerAddress { get; set; } = string.Empty;

	public int ServerPort { get; set; } = 25565;

	public string AccountMode { get; set; } = "offline";

	public string LoaderType { get; set; } = "Vanilla";

	public string LoaderVersion { get; set; } = string.Empty;

	public string BaseMinecraftVersion { get; set; } = string.Empty;

	public DateTimeOffset? LastLaunchTime { get; set; }

	public string Notes { get; set; } = string.Empty;

	public string CustomJvmArgs { get; set; } = string.Empty;

	public string CustomGameArgs { get; set; } = string.Empty;

	public ModpackInstallation? Modpack { get; set; }
}
