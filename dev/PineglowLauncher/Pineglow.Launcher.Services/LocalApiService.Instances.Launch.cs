using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Pineglow.Launcher.Models;

namespace Pineglow.Launcher.Services;

/// <summary>
/// 实例启动与 Java 解析。
/// <para>
/// 启动链：Java 路径解析、参数拼装、进程拉起与结果回报。
/// </para>
/// </summary>
public static partial class LocalApiService
{
	private static async Task HandleLaunchAsync(HttpListenerResponse response, string instanceId)
	{
		// MR03 收尾之三（A7 剩余项：**严格原子性**）：
		// 把「检查是否在运行」与「登记启动中占位」合并为一次原子预约（TryBeginLaunch 内部用 TryAdd）。
		// 修复前是「先 IsRunning 再 MarkRunning」，两次并发请求之间存在窗口，可能**都**通过检查
		// → 同一实例起两个游戏进程同时写同一份存档。
		// 位置刻意放在最前：在任何网络调用/进程派发**之前**就拒绝。
		if (!RunningInstanceRegistry.TryBeginLaunch(instanceId, out int alreadyRunningPid))
		{
			throw new InvalidOperationException(alreadyRunningPid > 0
				? $"该实例已在运行（进程 {alreadyRunningPid}），请勿重复启动。"
				: "该实例正在启动中，请勿重复启动。");
		}

		try
		{
			await LaunchBodyAsync(response, instanceId);
		}
		finally
		{
			// 无条件调用即可：AbortLaunch 只删「预约中」的条目；
			// 启动成功后 MarkRunning 已把它升级为真实进程登记（Launching=false），这里是 no-op。
			// 而**任何失败路径**（抛异常、提前返回）都会释放预约，不会永久挡住该实例。
			RunningInstanceRegistry.AbortLaunch(instanceId);
		}
	}

	private static async Task LaunchBodyAsync(HttpListenerResponse response, string instanceId)
	{
		// N09-09（退出归属，第六轮修复）：在进程拉起之前先读一次退出事件号，
		// 随成功响应回传给界面作为「历史分界」——分界之后的退出才可能属于本次启动。
		// 归属由服务端时序决定，前端不做任何历史读取（第四~六轮的漏报都来自读取时序）。
		long launchExitBase = RunningInstanceRegistry.CurrentExitEventSeq;
		RequestScope scope = BeginReadScope(instanceId);
		AppConfig config = scope.Config;
		GameInstance instance = scope.Instance!;
		string gameDir = scope.GameDirectory;
		if (string.IsNullOrWhiteSpace(instance.MinecraftVersion))
		{
			throw new InvalidOperationException("实例未关联游戏版本，请先在设置中安装版本");
		}

		LauncherAccount account = config.Accounts.FirstOrDefault(x => x.Id == config.SelectedAccountId);
		string mode = account?.Type?.Trim().ToLowerInvariant() ?? "offline";
		string playerName = account?.UserName ?? config.PlayerName;
		string playerUuid = string.Empty;
		string accessToken = "0";
		string userType = "legacy";
		string xuid = string.Empty;
		string clientId = config.MicrosoftClientId;
		string customJvmArgs = instance.CustomJvmArgs ?? string.Empty;

		if (mode == "microsoft")
		{
			string refreshToken = account?.RefreshToken;
			if (string.IsNullOrWhiteSpace(refreshToken) && account is not null) refreshToken = SecretStore.Load(SecretStore.AccountToken(account.Id));
			if (string.IsNullOrWhiteSpace(config.MicrosoftClientId) || string.IsNullOrWhiteSpace(refreshToken))
			{
				throw new InvalidOperationException("正版账号刷新凭据不完整，请重新登录");
			}
			using CancellationTokenSource refreshCts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
			MinecraftAccount refreshed = await new MicrosoftAuthService(Outbound.Api).LoginWithRefreshTokenAsync(config.MicrosoftClientId, refreshToken, refreshCts.Token);
			playerName = refreshed.UserName;
			playerUuid = refreshed.Uuid;
			accessToken = refreshed.AccessToken;
			userType = "msa";
			xuid = refreshed.Xuid;
		}
		else if (mode == "authlib")
		{
			if (account == null || !account.IsThirdParty || string.IsNullOrWhiteSpace(account.UserName) || string.IsNullOrWhiteSpace(account.Uuid) || string.IsNullOrWhiteSpace(account.AccessToken) || string.IsNullOrWhiteSpace(account.AuthServerUrl))
			{
				throw new InvalidOperationException("第三方账号登录信息不完整，请重新添加该账号");
			}

			// 修复（候选 ⑤ 顺带）：这里原本 「自建裸客户端」——既不跟随用户代理设置，也不释放。
			// 同一个 handler 明明把代理写进了游戏 JVM 参数，交付物组件下载却裸连；
			// 现在走宿主拥有的 download 具名客户端（跟随代理、长超时、不自建）。
			string agentPath = await new AuthlibInjectorService(Outbound.Download).EnsureAgentAsync(CancellationToken.None);
			playerName = account.UserName;
			playerUuid = account.Uuid;
			accessToken = account.AccessToken;
			userType = "mojang";
			clientId = string.Empty;
			customJvmArgs = AppendJvmArgument(customJvmArgs, $" -javaagent:\"{agentPath}\"={account.AuthServerUrl}");
		}

		// 代理参数传递给游戏进程
		if (config.ProxyEnabled && !string.IsNullOrWhiteSpace(config.ProxyHost) && config.ProxyPort > 0)
		{
			customJvmArgs = AppendJvmArgument(customJvmArgs, $"-Dhttp.proxyHost={config.ProxyHost} -Dhttp.proxyPort={config.ProxyPort} -Dhttps.proxyHost={config.ProxyHost} -Dhttps.proxyPort={config.ProxyPort}");
		}

		string javaPath = await ResolveJavaPathAsync(config, instance, instance.MinecraftVersion);
		LaunchProfile profile = new LaunchProfile
		{
			GameDirectory = gameDir,
			JavaPath = javaPath,
			VersionId = instance.MinecraftVersion,
			PlayerName = playerName,
			MaxMemoryMb = instance.MaxMemoryMb > 0 ? instance.MaxMemoryMb : config.MaxMemoryMb,
			WindowWidth = instance.WindowWidth,
			WindowHeight = instance.WindowHeight,
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

		Process process = await new LauncherService(msg => Debug.WriteLine("[launch] " + msg)).LaunchAsync(profile, CancellationToken.None);
		// MR03 收尾：启动成功即登记运行态；退出监听结束时清除（注册表本身也会自愈）。
		RunningInstanceRegistry.MarkRunning(instanceId, process);
		DateTimeOffset playStartedAt = DateTimeOffset.Now;
		instance.LastLaunchTime = playStartedAt;
		instance.PlayCount++;
		SaveConfig(config);
		bool minimizeOnLaunch = config.MinimizeOnLaunch;
		bool autoCloseOnExit = config.AutoCloseOnExit;
		// 挂进程退出事件：累加游玩时长；若开启"退出后自动关闭"，关闭启动器窗口
		_ = Task.Run(async () =>
		{
			try
			{
				await process.WaitForExitAsync();
				// N09-09 补充（游戏退出显式提示）：登记本次退出（退出码 + 运行时长）。
				// 界面观察窗口按事件号去重后，对「秒退 / 退出码非 0」给一条持久提示。
				int? gameExitCode;
				try { gameExitCode = process.ExitCode; } catch { gameExitCode = null; }
				RunningInstanceRegistry.RecordGameExit(instanceId, gameExitCode, playStartedAt, DateTimeOffset.Now);
				lock (ConfigGate)
				{
					AppConfig latest = LoadConfig();
					GameInstance? target = latest.Instances.FirstOrDefault(x => x.Id == instanceId);
					if (target != null)
					{
						target.RecordPlaySession(playStartedAt, DateTimeOffset.Now);
						SaveConfig(latest);
					}
				}
				// D3②「退出后自动备份」：实例级开关，判定在 AutoBackupPlanner（纯函数），
				// 执行在 AutoBackupService（只读源存档、单条失败不中断）。
				//
				// 与"退出后自动关闭"的时序：**先备份，再关窗**，备份给 20 秒上限，超时放弃本次备份照常退出。
				// 反过来（先关窗再备份）会被下面的 Environment.Exit(0) 直接截断，开关就等于形同虚设。
				AutoBackupExitResult backup = await TryRunAutoBackupOnExitAsync(instanceId);
				if (autoCloseOnExit)
				{
					if (!backup.ShouldClose)
					{
						// S2：备份失败/超时 → 保留窗口，让用户看到原因、手动决定。
						// （写一条能被用户看到的日志；界面提示交由后续版本接入状态条。）
						Debug.WriteLine("[auto-close] 备份未成功完成，已按设置保留窗口：" + backup.Summary);
					}
					else
					{
						Avalonia.Threading.Dispatcher.UIThread.Post(() =>
						{
							try
							{
								Environment.Exit(0);
							}
							catch
							{
							}
						});
					}
				}
			}
			catch
			{
			}
			finally
			{
				// MR03 收尾：无论退出监听是否成功，都要清掉运行态登记。
				RunningInstanceRegistry.MarkExited(instanceId);
			}
		});
		await WriteJsonAsync(response, 200, new { ok = true, pid = process.Id, minimizeOnLaunch, exitEventBase = launchExitBase });
	}

	/// <summary>
	/// N09-09 补充（游戏退出显式提示）：轻量游戏状态查询。
	/// 只读运行注册表——不加载配置、不扫描 Java / 存档 / Mod，
	/// 供前端在启动后的观察窗口里轮询「最近一次退出」，不给首屏性能添负担。
	/// </summary>
	private static async Task HandleGameStatusAsync(HttpListenerRequest request, HttpListenerResponse response)
	{
		long since = 0;
		string? sinceRaw = request.QueryString["since"];
		if (!string.IsNullOrEmpty(sinceRaw) && long.TryParse(sinceRaw, out long parsed))
		{
			since = parsed;
		}

		List<object> exits = new List<object>();
		foreach (RunningInstanceRegistry.GameExitInfo exit in RunningInstanceRegistry.GetExitsAfter(since))
		{
			exits.Add(new
			{
				eventId = exit.EventId,
				instanceId = exit.InstanceId,
				exitCode = exit.ExitCode,
				durationMs = exit.DurationMs,
				quickExit = exit.QuickExit,
				needsNotice = exit.NeedsNotice
			});
		}

		RunningInstanceRegistry.GameExitInfo? last = RunningInstanceRegistry.LastGameExit;
		object? lastGameExit = last == null
			? null
			: new
			{
				eventId = last.EventId,
				instanceId = last.InstanceId,
				exitCode = last.ExitCode,
				durationMs = last.DurationMs,
				quickExit = last.QuickExit,
				needsNotice = last.NeedsNotice
			};

		await WriteJsonAsync(response, 200, new
		{
			lastGameExit,
			exits,
			lastEventId = RunningInstanceRegistry.CurrentExitEventSeq,
			// 第九轮（架构审查反例）：观察窗口按「是否还有游戏在跑」收摊——
			// 双开时 A 的退出提示后不能停表，B 的秒退还要接着报。
			runningCount = RunningInstanceRegistry.Count
		});
	}

	/// <summary>
	/// D3②：游戏进程退出后跑一次自动备份。**永不抛异常**——它跑在退出监听里，
	/// 一旦抛出去会让"记录游玩时长 / 清理运行态 / 自动关窗"全部跳过。
	/// </summary>
	/// <summary>退出后备份的结果摘要，供「自动关窗」判断要不要留下窗口。</summary>
	private sealed record AutoBackupExitResult(bool ShouldClose, string Summary);

	/// <summary>
	/// D3② + 2026-11-24（S2，用户数据保护）：
	/// 此版本前，备份失败/超时**照样自动关窗**——用户以为数据已备份，实际可能没有。
	/// 现在：失败 / 部分失败 / 超时 / 扫描异常都会**保留窗口**并把原因写进日志，
	/// 只有「备份成功」或「本来就没开启/没有存档」才按设置关窗。
	/// </summary>
	private static async Task<AutoBackupExitResult> TryRunAutoBackupOnExitAsync(string instanceId)
	{
		try
		{
			AppConfig latest = LoadConfig();
			GameInstance? instance = latest.Instances.FirstOrDefault(x => x.Id == instanceId);
			if (instance == null || !instance.AutoBackupOnExit)
			{
				return new AutoBackupExitResult(true, "备份未开启或实例不存在");
			}

			string gameDirectory = ResolveGameDirectory(latest, instance);
			Task<AutoBackupOutcome> run = Task.Run(() => AutoBackupService.TryBackupOnExit(
				latest, instanceId, gameDirectory, DateTimeOffset.Now, message => Debug.WriteLine(message)));

			Task completed = await Task.WhenAny(run, Task.Delay(TimeSpan.FromSeconds(20)));
			if (completed != run)
			{
				// 超时：**不能**确定备份已完成。关窗会截断仍在写的备份任务。
				// 保留窗口，让后台任务自然收尾；用户可以看到并自行决定。
				Debug.WriteLine("[auto-backup] 超过 20 秒未完成：保留窗口等待备份收尾（不再自动关窗）");
				return new AutoBackupExitResult(false, "自动备份超过 20 秒未完成，已保留窗口等待其结束");
			}

			AutoBackupOutcome outcome = await run;
			if (outcome.FailedFolders.Count > 0)
			{
				Debug.WriteLine($"[auto-backup] 部分存档备份失败（{outcome.FailedFolders.Count} 个）：保留窗口");
				return new AutoBackupExitResult(false,
					"自动备份有 " + outcome.FailedFolders.Count + " 个存档失败，已保留窗口："
					+ string.Join(", ", outcome.FailedFolders.Take(3)));
			}

			Debug.WriteLine("[auto-backup] 完成：已备份 " + outcome.BackedUpPaths.Count + " 个存档");
			return new AutoBackupExitResult(true, "备份完成（" + outcome.BackedUpPaths.Count + " 个存档）");
		}
		catch (Exception ex)
		{
			// 连"结果"都拿不到时，同样不能假装成功去关窗。
			Debug.WriteLine("[auto-backup] 未预期异常：保留窗口。" + ex.Message);
			return new AutoBackupExitResult(false, "自动备份出现未预期异常，已保留窗口：" + ex.Message);
		}
	}

	private static async Task<(string? Path, int RequiredMajor)> FindLaunchJavaAsync(AppConfig config, GameInstance instance, string minecraftVersion)
	{
		string gameDir = ResolveGameDirectory(config, instance);
		int requiredMajor = JavaVersionMatcher.GetRequiredJavaMajor(minecraftVersion, gameDir);
		string configured = !string.IsNullOrWhiteSpace(instance.JavaPath) ? instance.JavaPath : config.JavaPath;
		if (!string.IsNullOrWhiteSpace(configured))
		{
			// javaw.exe can omit console version output. Verify its sibling from the same installation.
			string probePath = configured;
			if (OperatingSystem.IsWindows() && File.Exists(configured)
				&& Path.GetFileName(configured).Equals("javaw.exe", StringComparison.OrdinalIgnoreCase))
			{
				string consoleJava = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(configured))!, "java.exe");
				if (File.Exists(consoleJava)) probePath = consoleJava;
			}
			JavaInstallation? pinned = await JavaLocator.ValidateInstallationAsync(probePath);
			if (pinned == null || !JavaVersionMatcher.IsCompatible(pinned.VersionMajor, requiredMajor))
			{
				string actual = pinned == null ? "无法运行或识别版本" : $"实际为 Java {pinned.VersionMajor}";
				throw new InvalidOperationException($"已指定的 Java {actual}。该实例需要 Java {requiredMajor}，请在设置中重新选择或清除该路径以启用自动安装。");
			}
			return (probePath == configured ? pinned.JavaPath : configured, requiredMajor);
		}

		List<JavaInstallation> found = await JavaLocator.ScanAsync();
		JavaInstallation? match = JavaLocator.MatchForMinecraftVersion(found, minecraftVersion, gameDir);
		return (match?.JavaPath, requiredMajor);
	}

	private static async Task<string> ResolveJavaPathAsync(AppConfig config, GameInstance instance, string minecraftVersion)
	{
		var (path, requiredMajor) = await FindLaunchJavaAsync(config, instance, minecraftVersion);
		if (path != null) return path;
		try
		{
			JavaInstallation installed = await new JavaRuntimeService(Outbound.Api, _ => { }).InstallAsync(requiredMajor);
			return installed.JavaPath;
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			throw new InvalidOperationException(
				$"该实例需要 Java {requiredMajor}，自动安装失败：{ex.Message}。请重试或在设置中选择兼容的 Java。", ex);
		}
	}

	private static string AppendJvmArgument(string existing, string argument)
	{
		return string.IsNullOrWhiteSpace(existing) ? argument.Trim() : existing.Trim() + " " + argument.Trim();
	}
}
