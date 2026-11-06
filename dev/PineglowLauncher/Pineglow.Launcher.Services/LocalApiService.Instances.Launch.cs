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
		AppConfig config = LoadConfig();
		GameInstance instance = RequireInstance(config, instanceId);
		string gameDir = ResolveGameDirectory(config, instance);
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
			MinecraftAccount refreshed = await new MicrosoftAuthService(ApiHttp).LoginWithRefreshTokenAsync(config.MicrosoftClientId, refreshToken, refreshCts.Token);
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

			string agentPath = await new AuthlibInjectorService(new HttpClient()).EnsureAgentAsync(CancellationToken.None);
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

		string javaPath = ResolveJavaPath(config, instance, instance.MinecraftVersion);
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
				if (autoCloseOnExit)
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
			catch
			{
			}
			finally
			{
				// MR03 收尾：无论退出监听是否成功，都要清掉运行态登记。
				RunningInstanceRegistry.MarkExited(instanceId);
			}
		});
		await WriteJsonAsync(response, 200, new { ok = true, pid = process.Id, minimizeOnLaunch });
	}

	private static string ResolveJavaPath(AppConfig config, GameInstance instance, string minecraftVersion)
	{
		if (!string.IsNullOrWhiteSpace(instance.JavaPath))
		{
			return instance.JavaPath;
		}

		if (!string.IsNullOrWhiteSpace(config.JavaPath))
		{
			return config.JavaPath;
		}

		List<JavaInstallation> found = JavaLocator.ScanAsync(null).GetAwaiter().GetResult();
		// 关键：把**游戏目录**一起传进去，让匹配走版本 JSON 里的 `javaVersion.majorVersion`（并可沿 inheritsFrom 上溯）。
		// 只传版本字符串时，`neoforge-21.1.230` / `1.20.1-forge-47.4.3` 这类**加载器版本 id** 解析不出主版本，
		// 会默认成 Java 17 ⇒ MC 1.21+ 实例的 JVM 立刻退出：
		//   java.lang.module.InvalidModuleDescriptorException: Unsupported major.minor version 65.0
		string javaGameDir = ResolveGameDirectory(config, instance);
		JavaInstallation match = JavaLocator.MatchForMinecraftVersion(found, minecraftVersion, javaGameDir);
		if (match != null)
		{
			return match.JavaPath;
		}

		int requiredMajor = JavaVersionMatcher.GetRequiredJavaMajor(minecraftVersion, javaGameDir);
		JavaInfo any = JavaLocator.FindAsync().GetAwaiter().GetResult();
		if (any != null)
		{
			// 只有"任意 Java"确实满足要求时才用它；主版本未知（<=0）也**不**视为兼容。
			if (any.VersionMajor > 0 && JavaVersionMatcher.IsCompatible(any.VersionMajor, requiredMajor))
			{
				return any.Path;
			}

			throw new InvalidOperationException(
				$"该版本需要 Java {requiredMajor}，但本机只找到 Java {(any.VersionMajor > 0 ? any.VersionMajor.ToString() : "未知版本")}（{any.Path}）。"
				+ $"请在设置中指定 Java {requiredMajor} 的路径（整合包导入会自动安装所需 Java）。");
		}

		throw new InvalidOperationException($"未找到可用的 Java 运行时（该版本需要 Java {requiredMajor}），请先在设置中指定 Java 路径");
	}

	private static string AppendJvmArgument(string existing, string argument)
	{
		return string.IsNullOrWhiteSpace(existing) ? argument.Trim() : existing.Trim() + " " + argument.Trim();
	}
}
