using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Pineglow.Launcher.Models;

public sealed class AppConfig
{
	public string GameDirectory { get; set; } = string.Empty;

	public string JavaPath { get; set; } = string.Empty;

	public List<GameInstance> Instances { get; set; } = new List<GameInstance>();

	public string SelectedInstanceId { get; set; } = string.Empty;

	public List<JavaInstallation> JavaInstallations { get; set; } = new List<JavaInstallation>();

	public List<LauncherAccount> Accounts { get; set; } = new List<LauncherAccount>();

	public string SelectedAccountId { get; set; } = string.Empty;

	public string PlayerName { get; set; } = "Player";

	public string SelectedVersion { get; set; } = string.Empty;

	public int MaxMemoryMb { get; set; } = 4096;

	public bool UseAutoMemory { get; set; }

	public int WindowWidth { get; set; } = 1280;

	public int WindowHeight { get; set; } = 720;

	public string ServerHost { get; set; } = string.Empty;

	public string ServerPort { get; set; } = "25565";

	public List<FavoriteServer> FavoriteServers { get; set; } = new List<FavoriteServer>();

	public List<VersionDownloadQueueItem> DownloadQueue { get; set; } = new List<VersionDownloadQueueItem>();

	public List<ResumableTaskItem> ResumableTasks { get; set; } = new List<ResumableTaskItem>();

	public string AccountMode { get; set; } = "offline";

	public string MicrosoftClientId { get; set; } = string.Empty;

	[JsonIgnore]
	public string MicrosoftRefreshToken { get; set; } = string.Empty;

	[JsonIgnore]
	public string MicrosoftAccessToken { get; set; } = string.Empty;

	public DateTimeOffset MicrosoftAccessTokenExpiresAt { get; set; } = DateTimeOffset.MinValue;

	public string MicrosoftUserName { get; set; } = string.Empty;

	public string MicrosoftUuid { get; set; } = string.Empty;

	public string MicrosoftXuid { get; set; } = string.Empty;

	[JsonIgnore]
	public string CurseForgeApiKey { get; set; } = string.Empty;

	public bool UseMirror { get; set; }

	/// <summary>自更新 manifest 地址；留空表示不检查更新。</summary>
	public string UpdateUrl { get; set; } = string.Empty;
}
