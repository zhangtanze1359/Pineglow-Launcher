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

	/// <summary>下载源：official / bmclapi / mcbbs</summary>
	public string DownloadSource { get; set; } = "official";

	/// <summary>自更新 manifest 地址；留空表示不检查更新。</summary>
	public string UpdateUrl { get; set; } = string.Empty;

	/// <summary>HTTP 代理开关</summary>
	public bool ProxyEnabled { get; set; }

	/// <summary>HTTP 代理地址</summary>
	public string ProxyHost { get; set; } = string.Empty;

	/// <summary>HTTP 代理端口</summary>
	public int ProxyPort { get; set; }

	/// <summary>HTTP 代理用户名（可选）</summary>
	public string ProxyUsername { get; set; } = string.Empty;

	/// <summary>HTTP 代理密码（可选）</summary>
	public string ProxyPassword { get; set; } = string.Empty;

	/// <summary>游戏启动后自动最小化启动器</summary>
	public bool MinimizeOnLaunch { get; set; }

	/// <summary>游戏退出后自动关闭启动器</summary>
	public bool AutoCloseOnExit { get; set; }
}
