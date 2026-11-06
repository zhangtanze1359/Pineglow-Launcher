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
/// 实例生命周期。
/// <para>
/// 实例的读、建、删、重新挂载，以及实例摘要的组装。
/// </para>
/// </summary>
public static partial class LocalApiService
{
	/* ================= API handlers ================= */

	private static async Task HandleGetStateAsync(HttpListenerResponse response)
	{
		AppConfig config = LoadConfig();
		List<object> instances = config.Instances.Select(x => BuildInstanceSummary(config, x).GetAwaiter().GetResult()).ToList();
		await WriteJsonAsync(response, 200, new
		{
			version = LauncherService.LauncherVersion,
			pid = Environment.ProcessId,
			config = new
			{
				config.GameDirectory,
				config.JavaPath,
				config.PlayerName,
				config.MaxMemoryMb,
				config.UseAutoMemory,
				config.WindowWidth,
				config.WindowHeight,
				config.ServerHost,
				config.ServerPort,
				config.UseMirror,
				downloadSource = string.IsNullOrWhiteSpace(config.DownloadSource) ? "official" : config.DownloadSource,
				config.ProxyEnabled,
				config.ProxyHost,
				config.ProxyPort,
				config.ProxyUsername,
				proxyPasswordConfigured = !string.IsNullOrEmpty(config.ProxyPassword),
				config.MinimizeOnLaunch,
				config.AutoCloseOnExit,
				// R2-02：新闻图片 fake-ip 兼容开关（默认 false，fail closed）
				config.NewsFakeIpCompat,
				config.SelectedVersion,
				curseForgeApiKeyConfigured = !string.IsNullOrEmpty(config.CurseForgeApiKey),
				config.SelectedInstanceId,
				config.SelectedAccountId,
				config.AccountMode,
				config.AiDiagnosisEnabled,
				selectedAiProviderId = config.SelectedAiProviderId,
				aiProviders = config.AiProviders.Select(p => new
				{
					p.Id,
					p.Name,
					p.BaseUrl,
					p.Model,
					keyConfigured = !string.IsNullOrEmpty(p.ApiKey)
				})
			},
			instances,
			accounts = config.Accounts.Select(BuildAccountSummary),
			java = JavaLocator.ScanAsync(null).GetAwaiter().GetResult()
		});
	}

	private static async Task HandleListInstancesAsync(HttpListenerResponse response)
	{
		AppConfig config = LoadConfig();
		List<object> instances = config.Instances.Select(x => BuildInstanceSummary(config, x).GetAwaiter().GetResult()).ToList();
		await WriteJsonAsync(response, 200, new { instances, selectedInstanceId = config.SelectedInstanceId });
	}

	private static async Task<object> BuildInstanceSummary(AppConfig config, GameInstance instance)
	{
		string gameDir = ResolveGameDirectory(config, instance);
		int savesCount = 0;
		int modsCount = 0;
		int resourcePacksCount = 0;
		int shaderPacksCount = 0;
		try
		{
			savesCount = new SaveService(gameDir).GetSaves(instance).Count;
		}
		catch
		{
		}

		try
		{
			modsCount = (await new ModService(gameDir).GetModsAsync(instance)).Count;
		}
		catch
		{
		}

		try
		{
			resourcePacksCount = new ResourcePackService(gameDir).GetResourcePacks(instance).Count;
		}
		catch
		{
		}

		try
		{
			shaderPacksCount = new ShaderPackService(gameDir).GetPacks(instance).Count;
		}
		catch
		{
		}

		// MR03：同一游戏目录被多个实例共用时，移除确认必须提示"数据仍被其他实例使用"。
		bool sharedGameDirectory = config.Instances.Any(x => !ReferenceEquals(x, instance)
			&& string.Equals(ResolveGameDirectory(config, x), gameDir, StringComparison.OrdinalIgnoreCase));

		return new
		{
			instance.Id,
			instance.Name,
			instance.MinecraftVersion,
			gameDirectory = gameDir,
			sharedGameDirectory,
			instance.JavaPath,
			instance.MaxMemoryMb,
			instance.UseAutoMemory,
			instance.WindowWidth,
			instance.WindowHeight,
			instance.ServerAddress,
			instance.ServerPort,
			instance.AccountMode,
			instance.LoaderType,
			instance.LoaderVersion,
			instance.BaseMinecraftVersion,
			instance.LastLaunchTime,
			instance.TotalPlaySeconds,
			instance.PlayCount,
			instance.Notes,
			instance.CustomJvmArgs,
			instance.CustomGameArgs,
			instance.Modpack,
			savesCount,
			modsCount,
			resourcePacksCount,
			shaderPacksCount,
			versions = VersionResolver.ListLocalVersions(gameDir)
		};
	}

	private static async Task HandleInstanceDetailAsync(HttpListenerResponse response, string instanceId)
	{
		AppConfig config = LoadConfig();
		GameInstance instance = RequireInstance(config, instanceId);
		object summary = await BuildInstanceSummary(config, instance);
		await WriteJsonAsync(response, 200, new { instance = summary, selectedInstanceId = config.SelectedInstanceId });
	}

	private static async Task HandleCreateInstanceAsync(HttpListenerRequest request, HttpListenerResponse response)
	{
		JsonDocument body = await ReadBodyAsync(request);
		string name = JsonUtil.GetString(body.RootElement, "name")?.Trim();
		string version = JsonUtil.GetString(body.RootElement, "version")?.Trim();
		if (string.IsNullOrWhiteSpace(name))
		{
			throw new InvalidOperationException("实例名称不能为空");
		}

		AppConfig config = LoadConfig();
		GameInstance instance = InstanceService.CreateDefault(string.IsNullOrWhiteSpace(version) ? null : version, PlatformPaths.GetDefaultMinecraftDirectory());
		instance.Name = name;
		config.Instances.Add(instance);
		config.SelectedInstanceId = instance.Id;
		SaveConfig(config);
		object summary = await BuildInstanceSummary(config, instance);
		await WriteJsonAsync(response, 200, new { ok = true, instance = summary });
	}

	/// <summary>
	/// MR03：仅把实例"从列表移除" —— 只删除配置记录，**不删除任何游戏文件**。
	/// 游戏目录、存档、Mod、资源包、光影全部原样保留，之后可用 <c>/api/instances/register</c> 挂回。
	/// 活动状态保护：正在进行 Mod 二分定位的实例必须先结束定位，否则会留下无主的 Bisect 会话
	/// （文件半禁用、恢复依据随后丢失，见 MR06/MR07）。
	/// </summary>
	private static async Task HandleDeleteInstanceAsync(HttpListenerResponse response, string instanceId)
	{
		AppConfig config = LoadConfig();
		GameInstance instance = RequireInstance(config, instanceId);

		ModBisectSession? activeBisect = ModBisectService.Get(instanceId);
		if (activeBisect != null && !activeBisect.Finished)
		{
			throw new InvalidOperationException("该实例正在进行 Mod 二分定位，请先「放弃」或结束定位，再把它从列表移除。");
		}

		// MR03 收尾：补上"实例正在运行不得移除"的守卫。
		// 与上面的 Bisect 守卫同一错误通道（400），保持这个 handler 的行为一致。
		if (RunningInstanceRegistry.IsRunning(instanceId, out int runningPid))
		{
			throw new InvalidOperationException(
				$"该实例正在运行（进程 {runningPid}），请先退出游戏，再把它从列表移除。");
		}

		string gameDir = ResolveGameDirectory(config, instance);
		int savesCount = 0;
		try
		{
			savesCount = new SaveService(gameDir).GetSaves(instance).Count;
		}
		catch
		{
		}

		bool sharedDirectory = config.Instances.Any(x => x.Id != instanceId
			&& string.Equals(ResolveGameDirectory(config, x), gameDir, StringComparison.OrdinalIgnoreCase));

		// 只改配置：此处不存在任何 File/Directory 写操作。
		config.Instances.RemoveAll(x => x.Id == instanceId);
		if (config.SelectedInstanceId == instanceId)
		{
			config.SelectedInstanceId = config.Instances.FirstOrDefault()?.Id ?? string.Empty;
		}

		SaveConfig(config);
		await WriteJsonAsync(response, 200, new
		{
			ok = true,
			removedId = instance.Id,
			removedName = instance.Name,
			gameDirectory = gameDir,
			savesCount,
			sharedDirectory,
			keptFiles = true,
			selectedInstanceId = config.SelectedInstanceId
		});
	}

	/// <summary>
	/// MR03：把磁盘上已存在的游戏目录重新挂回实例列表。
	/// 只新增配置记录，**不创建、不移动、不删除任何文件**；已存在的存档与 Mod 原样可见。
	/// </summary>
	private static async Task HandleRegisterInstanceAsync(HttpListenerRequest request, HttpListenerResponse response)
	{
		JsonDocument body = await ReadBodyAsync(request);
		string rawDir = JsonUtil.GetString(body.RootElement, "gameDirectory")?.Trim() ?? string.Empty;
		string name = JsonUtil.GetString(body.RootElement, "name")?.Trim() ?? string.Empty;
		string version = JsonUtil.GetString(body.RootElement, "minecraftVersion")?.Trim() ?? string.Empty;

		if (string.IsNullOrWhiteSpace(rawDir))
		{
			throw new InvalidOperationException("请填写要挂回的游戏目录");
		}

		string fullDir;
		try
		{
			fullDir = Path.GetFullPath(rawDir);
		}
		catch (Exception)
		{
			throw new InvalidOperationException("游戏目录路径无效：" + rawDir);
		}

		if (!Directory.Exists(fullDir))
		{
			throw new InvalidOperationException("目录不存在，无法挂回：" + fullDir);
		}

		AppConfig config = LoadConfig();
		bool duplicate = config.Instances.Any(x => string.Equals(
			SafeFullPath(x.GameDirectory), fullDir, StringComparison.OrdinalIgnoreCase));
		if (duplicate)
		{
			throw new InvalidOperationException("该目录已在实例列表中，无需重复添加：" + fullDir);
		}

		GameInstance instance = InstanceService.CreateDefault(
			string.IsNullOrWhiteSpace(version) ? null : version, fullDir);
		instance.GameDirectory = fullDir;
		if (!string.IsNullOrWhiteSpace(name))
		{
			instance.Name = name;
		}

		// 只改配置：此处不存在任何 File/Directory 写操作。
		config.Instances.Add(instance);
		config.SelectedInstanceId = instance.Id;
		SaveConfig(config);

		object summary = await BuildInstanceSummary(config, instance);
		await WriteJsonAsync(response, 200, new { ok = true, instance = summary, registeredExistingDirectory = true });
	}

	private static string SafeFullPath(string? path)
	{
		if (string.IsNullOrWhiteSpace(path))
		{
			return string.Empty;
		}

		try
		{
			return Path.GetFullPath(path);
		}
		catch
		{
			return path;
		}
	}
}
