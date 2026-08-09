using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using Pineglow.Launcher.Models;

namespace Pineglow.Launcher.Services;

/// <summary>
/// 新界面（XAML + MVVM）启动链路服务：从 GameInstance + AppConfig 构建 LaunchProfile 并启动游戏。
/// 逻辑对齐经典界面 MainWindow.OnLaunchClick / BuildProfileAsync / EnsureJavaForVersionCoreAsync，
/// 但不再依赖任何 UI 控件。
/// </summary>
public sealed class GameLaunchService
{
	private readonly HttpClient _http;
	private readonly Action<string> _log;
	private readonly MicrosoftAuthService _microsoftAuthService;
	private readonly AuthlibInjectorService _authlibInjectorService;

	private Process? _gameProcess;
	private string _runningGameDirectory = string.Empty;

	/// <summary>游戏进程退出时触发（exitCode 为实际退出码）。</summary>
	public event Action<int>? GameExited;

	public GameLaunchService(HttpClient http, Action<string> log)
	{
		_http = http;
		_log = log;
		_microsoftAuthService = new MicrosoftAuthService(http);
		_authlibInjectorService = new AuthlibInjectorService(http);
	}

	public bool IsGameRunning()
	{
		try
		{
			Process? process = _gameProcess;
			return process != null && !process.HasExited;
		}
		catch
		{
			return false;
		}
	}

	public void StopGame()
	{
		Process? process = _gameProcess;
		if (process == null || process.HasExited)
		{
			return;
		}
		try
		{
			process.Kill(entireProcessTree: true);
			_log("正在停止 Minecraft。");
		}
		catch (Exception ex)
		{
			_log("停止游戏失败：" + ex.Message);
		}
	}

	/// <summary>完整启动链路：Mod 健康检查 → 构建 LaunchProfile → Java 校验 → 启动 → 记录游玩时间。</summary>
	public async Task<Process> LaunchAsync(AppConfig config, GameInstance instance, CancellationToken ct)
	{
		if (IsGameRunning())
		{
			throw new InvalidOperationException("游戏已经在运行，请先停止当前游戏。");
		}
		if (string.IsNullOrWhiteSpace(instance.MinecraftVersion))
		{
			throw new InvalidOperationException("该实例尚未安装游戏版本，请先安装或选择一个版本。");
		}

		await EnsureModHealthForLaunchAsync(instance, config, ct);
		LaunchProfile profile = await BuildProfileAsync(config, instance, ct);

		string minecraftVersion = string.IsNullOrWhiteSpace(instance.BaseMinecraftVersion) ? profile.VersionId : instance.BaseMinecraftVersion;
		int required = JavaVersionMatcher.GetRequiredJavaMajor(minecraftVersion, profile.GameDirectory, _log);
		_log($"当前 Minecraft 版本需要 Java {required}");

		JavaInfo javaInfo = await JavaLocator.ValidateAsync(profile.JavaPath);
		if (javaInfo == null)
		{
			throw new InvalidOperationException($"这个游戏版本需要 Java {required}，但启动器没有找到。请打开“设置 → Java 管理”，点击“自动扫描 / 下载”。");
		}
		if (!JavaVersionMatcher.IsCompatible(javaInfo.VersionMajor, required))
		{
			throw new InvalidOperationException($"Java 版本不合适：当前是 Java {javaInfo.VersionMajor}，这个游戏需要 Java {required}。请让启动器重新自动选择 Java。");
		}
		_log("Java：" + javaInfo.VersionText);

		Process process = await new LauncherService(_log).LaunchAsync(profile, ct);
		TrackGameProcess(process, profile.GameDirectory);

		instance.LastLaunchTime = DateTimeOffset.Now;
		await ConfigStore.SaveAsync(config);
		return process;
	}

	private async Task<LaunchProfile> BuildProfileAsync(AppConfig config, GameInstance instance, CancellationToken ct)
	{
		string versionId = instance.MinecraftVersion;
		string gameDirectory = ResolveGameDir(config, instance);
		LauncherAccount? selected = AccountService.GetSelected(config);

		string mode = instance.AccountMode;
		if (string.IsNullOrWhiteSpace(mode))
		{
			mode = selected?.IsMicrosoft == true ? "microsoft" : selected?.IsThirdParty == true ? "authlib" : "offline";
		}

		string playerName;
		string playerUuid;
		string accessToken;
		string userType;
		string xuid;
		string clientId;
		string customJvmArgs;

		if (mode.Equals("microsoft", StringComparison.OrdinalIgnoreCase))
		{
			LauncherAccount account = await EnsureMicrosoftAccountAsync(config, ct);
			playerName = account.UserName;
			playerUuid = account.Uuid;
			accessToken = account.AccessToken;
			userType = "msa";
			xuid = account.Xuid;
			clientId = AuthClientOptions.ResolveMicrosoftClientId(account.ClientId);
			customJvmArgs = instance.CustomJvmArgs ?? string.Empty;
		}
		else if (mode.Equals("authlib", StringComparison.OrdinalIgnoreCase))
		{
			LauncherAccount account = selected;
			if (account == null || !account.IsThirdParty || string.IsNullOrWhiteSpace(account.UserName) || string.IsNullOrWhiteSpace(account.Uuid) || string.IsNullOrWhiteSpace(account.AccessToken) || string.IsNullOrWhiteSpace(account.AuthServerUrl))
			{
				throw new InvalidOperationException("第三方账号登录信息不完整，请重新添加该账号。");
			}
			string agent = await _authlibInjectorService.EnsureAgentAsync(ct);
			playerName = account.UserName;
			playerUuid = account.Uuid;
			accessToken = account.AccessToken;
			userType = "mojang";
			xuid = string.Empty;
			clientId = string.Empty;
			customJvmArgs = AppendJvmArgument(instance.CustomJvmArgs, "-javaagent:\"" + agent + "\"=" + account.AuthServerUrl);
		}
		else
		{
			playerName = SafePlayerName(config.PlayerName);
			playerUuid = string.Empty;
			accessToken = "0";
			userType = "legacy";
			xuid = string.Empty;
			clientId = string.Empty;
			customJvmArgs = instance.CustomJvmArgs ?? string.Empty;
		}

		return new LaunchProfile
		{
			GameDirectory = gameDirectory,
			JavaPath = await ResolveJavaPathForLaunchAsync(config, instance, versionId, ct),
			VersionId = versionId,
			PlayerName = playerName,
			MaxMemoryMb = EffectiveMemoryMb(config, instance),
			WindowWidth = instance.WindowWidth > 0 ? instance.WindowWidth : 1280,
			WindowHeight = instance.WindowHeight > 0 ? instance.WindowHeight : 720,
			ServerHost = instance.ServerAddress?.Trim() ?? string.Empty,
			ServerPort = instance.ServerPort > 0 ? instance.ServerPort.ToString() : "25565",
			AccountMode = mode,
			PlayerUuid = playerUuid,
			AccessToken = accessToken,
			UserType = userType,
			ClientId = clientId,
			Xuid = xuid,
			CustomJvmArgs = customJvmArgs,
			CustomGameArgs = instance.CustomGameArgs ?? string.Empty
		};
	}

	private async Task<LauncherAccount> EnsureMicrosoftAccountAsync(AppConfig config, CancellationToken ct)
	{
		LauncherAccount selected = AccountService.GetSelected(config);
		if (selected == null || !selected.IsMicrosoft || string.IsNullOrWhiteSpace(selected.RefreshToken))
		{
			throw new InvalidOperationException("没有可用的正版账号，请先在设置中完成 Microsoft 登录。");
		}

		DateTimeOffset utcNow = DateTimeOffset.UtcNow;
		if (string.IsNullOrWhiteSpace(selected.AccessToken) || !(selected.AccessTokenExpiresAt > utcNow.AddMinutes(5.0)))
		{
			_log("正在刷新 Microsoft / Minecraft 登录状态...");
			string clientId = AuthClientOptions.ResolveMicrosoftClientId(selected.ClientId);
			try
			{
				MinecraftAccount account = await _microsoftAuthService.LoginWithRefreshTokenAsync(clientId, selected.RefreshToken, ct);
				AccountService.UpsertMicrosoft(config, account, clientId);
				await ConfigStore.SaveAsync(config);
				_log("正版账号已刷新：" + account.UserName);
			}
			catch (MicrosoftReauthenticationRequiredException ex)
			{
				throw new InvalidOperationException("正版账号需要重新登录，请先在设置页完成 Microsoft 登录后再启动。", ex);
			}
		}

		LauncherAccount refreshed = AccountService.GetSelected(config);
		if (refreshed == null || string.IsNullOrWhiteSpace(refreshed.AccessToken))
		{
			throw new InvalidOperationException("没有可用的正版账号，请先完成 Microsoft 登录。");
		}
		return refreshed;
	}

	private async Task<string> ResolveJavaPathForLaunchAsync(AppConfig config, GameInstance instance, string versionId, CancellationToken ct)
	{
		if (!string.IsNullOrWhiteSpace(instance.JavaPath))
		{
			_log("使用当前游戏手动选择的 Java：" + instance.JavaPath);
			return instance.JavaPath;
		}
		string minecraftVersion = string.IsNullOrWhiteSpace(instance.BaseMinecraftVersion) ? versionId : instance.BaseMinecraftVersion;
		return (await EnsureJavaForVersionCoreAsync(config, minecraftVersion, ct, downloadIfMissing: true)).JavaPath;
	}

	private async Task<JavaInstallation> EnsureJavaForVersionCoreAsync(AppConfig config, string minecraftVersion, CancellationToken ct, bool downloadIfMissing)
	{
		string gameDirectory = ResolveGameDir(config, null);
		int required = JavaVersionMatcher.GetRequiredJavaMajor(minecraftVersion, gameDirectory, _log);
		JavaInstallation? matched = JavaLocator.MatchForMinecraftVersion(config.JavaInstallations, minecraftVersion, gameDirectory, _log);
		if (matched != null && await JavaLocator.ValidateInstallationAsync(matched.JavaPath, _log) == null)
		{
			config.JavaInstallations.Remove(matched);
			matched = null;
		}

		if (matched == null)
		{
			foreach (JavaInstallation installation in await JavaLocator.ScanAsync(_log))
			{
				config.JavaInstallations.RemoveAll((JavaInstallation existing) => existing.JavaPath.Equals(installation.JavaPath, StringComparison.OrdinalIgnoreCase));
				config.JavaInstallations.Add(installation);
			}
			matched = JavaLocator.MatchForMinecraftVersion(config.JavaInstallations, minecraftVersion, gameDirectory, _log);
		}

		if (matched == null && downloadIfMissing)
		{
			_log($"未找到 Java {required}，开始从 Eclipse Adoptium 自动下载...");
			matched = await new JavaRuntimeService(_http, _log).InstallAsync(required, new Progress<JavaRuntimeProgress>(delegate (JavaRuntimeProgress value)
			{
				_log(value.Message);
			}), ct);
			config.JavaInstallations.RemoveAll((JavaInstallation existing) => existing.JavaPath.Equals(matched.JavaPath, StringComparison.OrdinalIgnoreCase));
			config.JavaInstallations.Add(matched);
		}

		if (matched == null)
		{
			throw new InvalidOperationException($"未找到兼容的 Java {required}，自动下载也未能完成。");
		}
		await ConfigStore.SaveAsync(config);
		return matched;
	}

	private async Task EnsureModHealthForLaunchAsync(GameInstance instance, AppConfig config, CancellationToken ct)
	{
		ct.ThrowIfCancellationRequested();
		string gameDirectory = ResolveGameDir(config, instance);
		List<ModFile> mods = await new ModService(gameDirectory).GetModsAsync(instance);
		ct.ThrowIfCancellationRequested();

		ModHealthReport report = ModHealthService.Analyze(instance, mods);
		ModHealthIssue? error = report.Issues.FirstOrDefault((ModHealthIssue issue) => issue.Severity == ModHealthSeverity.Error);
		if (error != null)
		{
			throw new InvalidOperationException("Mod 检查未通过：" + error.Description);
		}
		foreach (ModHealthIssue warning in report.Issues.Where((ModHealthIssue issue) => issue.Severity == ModHealthSeverity.Warning))
		{
			_log("Mod 检查提示：" + warning.Title + " - " + warning.Description);
		}
	}

	private int EffectiveMemoryMb(AppConfig config, GameInstance instance)
	{
		string version = string.IsNullOrWhiteSpace(instance.BaseMinecraftVersion) ? instance.MinecraftVersion : instance.BaseMinecraftVersion;
		string loaderType = instance.LoaderType ?? "Vanilla";
		int enabledModCount = MemoryAllocationService.CountEnabledMods(ResolveGameDir(config, instance));
		MemoryRecommendation recommendation = MemoryAllocationService.Recommend(version, loaderType, enabledModCount);
		if (instance.UseAutoMemory)
		{
			return recommendation.RecommendedMb;
		}
		return MemoryAllocationService.NormalizeManualMemory(instance.MaxMemoryMb, recommendation.MaximumMb);
	}

	private static string ResolveGameDir(AppConfig config, GameInstance? instance)
	{
		string? directory = instance?.GameDirectory;
		if (string.IsNullOrWhiteSpace(directory))
		{
			directory = config.GameDirectory;
		}
		if (string.IsNullOrWhiteSpace(directory))
		{
			return PlatformPaths.GetDefaultMinecraftDirectory();
		}
		return directory.Trim();
	}

	private static string AppendJvmArgument(string? existing, string argument)
	{
		if (!string.IsNullOrWhiteSpace(existing))
		{
			return existing.Trim() + " " + argument;
		}
		return argument;
	}

	private static string SafePlayerName(string? text)
	{
		string result = new string((string.IsNullOrWhiteSpace(text) ? "Player" : text.Trim()).Where((char ch) => char.IsLetterOrDigit(ch) || ch == '_').ToArray());
		if (!string.IsNullOrWhiteSpace(result))
		{
			return result.Substring(0, Math.Min(16, result.Length));
		}
		return "Player";
	}

	private void TrackGameProcess(Process process, string gameDirectory)
	{
		_gameProcess = process;
		_runningGameDirectory = gameDirectory;
		process.EnableRaisingEvents = true;
		process.Exited += (_, _) => OnGameProcessExited(process, gameDirectory);
		_log("Minecraft 已启动。");
		if (process.HasExited)
		{
			OnGameProcessExited(process, gameDirectory);
		}
	}

	private void OnGameProcessExited(Process process, string gameDirectory)
	{
		int exitCode;
		try
		{
			exitCode = process.ExitCode;
		}
		catch
		{
			exitCode = -1;
		}

		Dispatcher.UIThread.Post(delegate
		{
			if (_gameProcess != process)
			{
				return;
			}
			_gameProcess = null;
			if (exitCode == 0)
			{
				_log("Minecraft 已正常退出。");
			}
			else
			{
				CrashAnalysis crashAnalysis = CrashAnalyzerService.Analyze(gameDirectory, exitCode);
				_log("游戏异常退出：" + crashAnalysis.Summary);
				foreach (string suggestion in crashAnalysis.Suggestions)
				{
					_log("建议：" + suggestion);
				}
				if (!string.IsNullOrWhiteSpace(crashAnalysis.SourceFile))
				{
					_log("错误详情文件：" + crashAnalysis.SourceFile);
				}
			}
			GameExited?.Invoke(exitCode);
		});
	}
}
