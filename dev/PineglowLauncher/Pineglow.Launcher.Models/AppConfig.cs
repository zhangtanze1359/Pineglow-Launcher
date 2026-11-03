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

	/// <summary>AI 诊断提供商列表（多提供商预设，Key 经 SecretStore 持久化）。</summary>
	public List<AiProviderProfile> AiProviders { get; set; } = new List<AiProviderProfile>();

	/// <summary>当前选中的 AI 提供商 Id（对应 AiProviders 中一项）。</summary>
	public string SelectedAiProviderId { get; set; } = string.Empty;

	/// <summary>AI 诊断功能开关</summary>
	public bool AiDiagnosisEnabled { get; set; }

	/// <summary>
	/// MR08：诊断时是否保留**脱敏后**的证据摘录。
	/// 默认 false —— 历史只保存脱敏摘要，不落原始日志正文；开启后导出才带证据摘录。
	/// </summary>
	public bool DiagnosticKeepEvidence { get; set; }

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
	[JsonIgnore]
	public string ProxyPassword { get; set; } = string.Empty;

	/// <summary>游戏启动后自动最小化启动器</summary>
	public bool MinimizeOnLaunch { get; set; }

	/// <summary>游戏退出后自动关闭启动器</summary>
	public bool AutoCloseOnExit { get; set; }

	/// <summary>
	/// R2-02：是否启用"Clash 类 TUN/fake-ip 新闻图片兼容"。
	/// 默认 false（fail closed）；仅在用户显式开启后，才允许 NewsImageProxyService 选择 198.18/15 fake-ip 地址。
	/// 序列化/反序列化与 PatchConfig 必须保留精确布尔语义；缺字段、字符串或数字均视为缺省。
	/// </summary>
	[JsonPropertyName("newsFakeIpCompat")]
	public bool NewsFakeIpCompat { get; set; }
}
