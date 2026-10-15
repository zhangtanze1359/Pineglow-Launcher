using System;
using System.Collections.Generic;

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

	/// <summary>累计游玩时长（秒）</summary>
	public long TotalPlaySeconds { get; set; }

	/// <summary>游玩次数</summary>
	public int PlayCount { get; set; }

	/// <summary>真实完成的游玩记录。旧累计时长不回填为虚构会话。</summary>
	public List<GamePlaySession> PlaySessions { get; set; } = new List<GamePlaySession>();

	public void RecordPlaySession(DateTimeOffset startedAt, DateTimeOffset endedAt)
	{
		if (endedAt <= startedAt) return;
		long seconds = (long)(endedAt - startedAt).TotalSeconds;
		if (seconds <= 0) return;
		PlaySessions ??= new List<GamePlaySession>();
		PlaySessions.Add(new GamePlaySession { StartedAt = startedAt, EndedAt = endedAt });
		TotalPlaySeconds += seconds;
	}

	public string Notes { get; set; } = string.Empty;

	public string CustomJvmArgs { get; set; } = string.Empty;

	public string CustomGameArgs { get; set; } = string.Empty;

	public ModpackInstallation? Modpack { get; set; }
}

public sealed class GamePlaySession
{
	public DateTimeOffset StartedAt { get; set; }
	public DateTimeOffset EndedAt { get; set; }
}
