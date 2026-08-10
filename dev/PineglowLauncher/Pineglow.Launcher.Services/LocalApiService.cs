using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Pineglow.Launcher.Models;

namespace Pineglow.Launcher.Services;

/// <summary>
/// 本地 HTTP API + 静态 UI 托管服务。
/// 暴露 127.0.0.1:{port} 的 JSON 接口供 Web UI（ui/ 目录）调用。
/// </summary>
public static class LocalApiService
{
	private const int DefaultPort = 48123;

	private static readonly object ConfigGate = new object();
	private static readonly JsonSerializerOptions JsonOpts = new JsonSerializerOptions
	{
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
		WriteIndented = false
	};

	private static HttpListener _listener;
	private static string _uiRoot;
	private static bool _running;

	private static readonly HttpClient ApiHttp = new HttpClient { Timeout = TimeSpan.FromSeconds(45) };

	private sealed class DownloadTaskEntry
	{
		public string Id;
		public string VersionId;
		public string Status; // queued / downloading / completed / failed
		public double Progress;
		public string Message = string.Empty;
		public DateTimeOffset StartedAt;
	}

	private static readonly object DownloadGate = new object();
	private static readonly List<DownloadTaskEntry> DownloadTasks = new List<DownloadTaskEntry>();

	private sealed class ContentTaskEntry
	{
		public string Id;
		public string Category;      // mod / modpack / shader / resourcepack
		public string ProjectId;
		public string InstanceId;    // 安装目标实例（modpack 创建新实例，忽略此字段）
		public string Title = string.Empty;
		public string Status;        // queued / running / completed / failed
		public double Progress;
		public string Message = string.Empty;
		public string ResultInstanceId = string.Empty;
		public DateTimeOffset StartedAt;
	}

	private static readonly object ContentGate = new object();
	private static readonly List<ContentTaskEntry> ContentTasks = new List<ContentTaskEntry>();

	// 自更新状态（单任务）
	private static readonly object UpdateGate = new object();
	private static string UpdateState = "idle"; // idle / downloading / completed / failed / applying
	private static double UpdateProgress;
	private static string UpdateVersion = string.Empty;
	private static string UpdatePath = string.Empty;
	private static string UpdateError = string.Empty;
	private static bool UpdateMandatory;

	public static int Port { get; private set; } = DefaultPort;
	public static bool IsRunning => _running;

	public static void Start(int? portOverride = null)
	{
		if (_running)
		{
			return;
		}

		int port = portOverride ?? DefaultPort;
		string envPort = Environment.GetEnvironmentVariable("PINEGLOW_API_PORT");
		if (portOverride == null && int.TryParse(envPort, out int envParsed))
		{
			port = envParsed;
		}

		_uiRoot = ResolveUiRoot();
		_listener = new HttpListener();
		_listener.Prefixes.Add($"http://127.0.0.1:{port}/");
		_listener.Start();
		Port = port;
		_running = true;
		_ = Task.Run(LoopAsync);
		LauncherLog.Info($"[LocalApi] 监听 127.0.0.1:{port}, ui={_uiRoot}");
		Debug.WriteLine($"[LocalApi] listening on 127.0.0.1:{port}, ui={_uiRoot}");
	}

	public static void Stop()
	{
		if (!_running)
		{
			return;
		}

		_running = false;
		try
		{
			_listener?.Stop();
			_listener?.Close();
		}
		catch
		{
		}

		_listener = null;
	}

	private static string ResolveUiRoot()
	{
		string env = Environment.GetEnvironmentVariable("PINEGLOW_UI_DIR");
		if (!string.IsNullOrWhiteSpace(env) && Directory.Exists(env))
		{
			return env;
		}

		string baseDir = Path.Combine(AppContext.BaseDirectory, "UI");
		if (Directory.Exists(baseDir))
		{
			return baseDir;
		}

		string configDir = PlatformPaths.GetConfigDirectory();
		string cfgUi = Path.Combine(configDir, "UI");
		if (Directory.Exists(cfgUi))
		{
			return cfgUi;
		}

		return baseDir;
	}

	private static async Task LoopAsync()
	{
		while (_running)
		{
			try
			{
				HttpListenerContext context = await _listener.GetContextAsync().ConfigureAwait(false);
				_ = Task.Run(() => HandleAsync(context));
			}
			catch (Exception ex)
			{
				LauncherLog.Error("[LocalApi] loop error: " + ex.Message, ex);
				Debug.WriteLine($"[LocalApi] loop error: {ex.Message}");
				if (_running)
				{
					await Task.Delay(200).ConfigureAwait(false);
				}
			}
		}
	}

	private static async Task HandleAsync(HttpListenerContext context)
	{
		HttpListenerRequest request = context.Request;
		HttpListenerResponse response = context.Response;
		try
		{
			SetCors(response);
			if (request.HttpMethod == "OPTIONS")
			{
				response.StatusCode = 204;
				response.Close();
				return;
			}

			string path = request.Url.AbsolutePath;
			if (path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase) || path.Equals("/api", StringComparison.OrdinalIgnoreCase))
			{
				await HandleApiAsync(context, path);
				return;
			}

			await ServeStaticAsync(context, path);
		}
		catch (Exception ex)
		{
			LauncherLog.Error("[LocalApi] handler error: " + request.RawUrl + " | " + ex.Message, ex);
			Debug.WriteLine($"[LocalApi] handler error: {ex}");
			await WriteJsonAsync(response, 500, new { error = ex.Message });
		}
		finally
		{
			try
			{
				response.Close();
			}
			catch
			{
			}
		}
	}

	private static void SetCors(HttpListenerResponse response)
	{
		response.Headers["Access-Control-Allow-Origin"] = "*";
		response.Headers["Access-Control-Allow-Methods"] = "GET, POST, DELETE, OPTIONS";
		response.Headers["Access-Control-Allow-Headers"] = "Content-Type";
		response.Headers["Cache-Control"] = "no-store";
	}

	private static async Task HandleApiAsync(HttpListenerContext context, string path)
	{
		HttpListenerRequest request = context.Request;
		HttpListenerResponse response = context.Response;
		string method = request.HttpMethod.ToUpperInvariant();
		string[] seg = path.Trim('/').Split('/').Select(s => Uri.UnescapeDataString(s)).ToArray();
		// seg = ["api", ...]

		try
		{
			if (seg.Length >= 2 && seg[1].Equals("health", StringComparison.OrdinalIgnoreCase) && method == "GET")
			{
				await WriteJsonAsync(response, 200, new { ok = true, pid = Environment.ProcessId, port = Port });
				return;
			}

			if (seg.Length == 2 && seg[1].Equals("state", StringComparison.OrdinalIgnoreCase) && method == "GET")
			{
				await HandleGetStateAsync(response);
				return;
			}

			if (seg.Length == 2 && seg[1].Equals("logs", StringComparison.OrdinalIgnoreCase) && method == "POST")
			{
				HandleOpenLogDirectory(response);
				return;
			}

			if (seg.Length == 2 && seg[1].Equals("instances", StringComparison.OrdinalIgnoreCase))
			{
				if (method == "GET")
				{
					await HandleListInstancesAsync(response);
					return;
				}

				if (method == "POST")
				{
					await HandleCreateInstanceAsync(request, response);
					return;
				}
			}

			if (seg.Length == 3 && seg[1].Equals("instances", StringComparison.OrdinalIgnoreCase) && method == "GET")
			{
				await HandleInstanceDetailAsync(response, seg[2]);
				return;
			}

			if (seg.Length >= 4 && seg[1].Equals("instances", StringComparison.OrdinalIgnoreCase))
			{
				string instanceId = seg[2];
				string action = seg[3];
				if (action.Equals("delete", StringComparison.OrdinalIgnoreCase) && method == "POST")
				{
					await HandleDeleteInstanceAsync(response, instanceId);
					return;
				}

				if (action.Equals("launch", StringComparison.OrdinalIgnoreCase) && method == "POST")
				{
					await HandleLaunchAsync(response, instanceId);
					return;
				}

				if (action.Equals("saves", StringComparison.OrdinalIgnoreCase) && method == "GET")
				{
					await HandleListSavesAsync(response, instanceId);
					return;
				}

				if (action.Equals("mods", StringComparison.OrdinalIgnoreCase) && method == "GET")
				{
					await HandleListModsAsync(response, instanceId);
					return;
				}

				if (action.Equals("resourcepacks", StringComparison.OrdinalIgnoreCase) && method == "GET")
				{
					await HandleListResourcePacksAsync(response, instanceId);
					return;
				}

				if (action.Equals("shaderpacks", StringComparison.OrdinalIgnoreCase) && method == "GET")
				{
					await HandleListShaderPacksAsync(response, instanceId);
					return;
				}

				if (seg.Length == 6 && action.Equals("mods", StringComparison.OrdinalIgnoreCase))
				{
					string fileName = seg[4];
					string modAction = seg[5];
					if (modAction.Equals("toggle", StringComparison.OrdinalIgnoreCase) && method == "POST")
					{
						await HandleToggleModAsync(response, instanceId, fileName);
						return;
					}

					if (modAction.Equals("delete", StringComparison.OrdinalIgnoreCase) && method == "POST")
					{
						await HandleDeleteModAsync(response, instanceId, fileName);
						return;
					}
				}
			}

			if (seg.Length == 2 && seg[1].Equals("accounts", StringComparison.OrdinalIgnoreCase) && method == "GET")
			{
				await HandleListAccountsAsync(response);
				return;
			}

			if (seg.Length == 3 && seg[1].Equals("accounts", StringComparison.OrdinalIgnoreCase) && method == "POST")
			{
				if (seg[2].Equals("offline", StringComparison.OrdinalIgnoreCase))
				{
					await HandleAddOfflineAccountAsync(request, response);
					return;
				}

				if (seg[2].Equals("select", StringComparison.OrdinalIgnoreCase))
				{
					await HandleSelectAccountAsync(request, response);
					return;
				}

				if (seg[2].Equals("microsoft", StringComparison.OrdinalIgnoreCase))
				{
					await HandleMicrosoftLoginAsync(response);
					return;
				}
			}

			if (seg.Length == 2 && seg[1].Equals("versions", StringComparison.OrdinalIgnoreCase) && method == "GET")
			{
				await HandleListVersionsAsync(response);
				return;
			}

			if (seg.Length == 3 && seg[1].Equals("versions", StringComparison.OrdinalIgnoreCase))
			{
				if (seg[2].Equals("remote", StringComparison.OrdinalIgnoreCase) && method == "GET")
				{
					await HandleListRemoteVersionsAsync(response);
					return;
				}

				if (seg[2].Equals("download", StringComparison.OrdinalIgnoreCase) && method == "POST")
				{
					await HandleDownloadVersionAsync(request, response);
					return;
				}
			}

			if (seg.Length == 2 && seg[1].Equals("downloads", StringComparison.OrdinalIgnoreCase) && method == "GET")
			{
				await HandleListDownloadsAsync(response);
				return;
			}

			if (seg.Length == 2 && seg[1].Equals("update", StringComparison.OrdinalIgnoreCase) && method == "GET")
			{
				await HandleUpdateStatusAsync(response);
				return;
			}

			if (seg.Length == 3 && seg[1].Equals("update", StringComparison.OrdinalIgnoreCase))
			{
				if (seg[2].Equals("check", StringComparison.OrdinalIgnoreCase) && method == "GET")
				{
					await HandleCheckUpdateAsync(response);
					return;
				}

				if (seg[2].Equals("download", StringComparison.OrdinalIgnoreCase) && method == "POST")
				{
					await HandleStartUpdateDownloadAsync(response);
					return;
				}

				if (seg[2].Equals("apply", StringComparison.OrdinalIgnoreCase) && method == "POST")
				{
					await HandleApplyUpdateAsync(response);
					return;
				}
			}

			if (seg.Length == 2 && seg[1].Equals("config", StringComparison.OrdinalIgnoreCase) && method == "POST")
			{
				await HandleUpdateConfigAsync(request, response);
				return;
			}

			if (seg.Length == 2 && seg[1].Equals("worlds", StringComparison.OrdinalIgnoreCase) && method == "GET")
			{
				await HandleListWorldsAsync(response);
				return;
			}

			if (seg.Length >= 2 && seg[1].Equals("content", StringComparison.OrdinalIgnoreCase) && method == "GET")
			{
				if (seg.Length == 2)
				{
					await HandleSearchContentAsync(request, response);
					return;
				}

				if (seg.Length == 3 && seg[2].Equals("search", StringComparison.OrdinalIgnoreCase))
				{
					await HandleSearchContentAsync(request, response);
					return;
				}

				if (seg.Length == 3 && seg[2].Equals("tasks", StringComparison.OrdinalIgnoreCase))
				{
					await HandleListContentTasksAsync(response);
					return;
				}
			}

			if (seg.Length == 3 && seg[1].Equals("content", StringComparison.OrdinalIgnoreCase) && seg[2].Equals("install", StringComparison.OrdinalIgnoreCase) && method == "POST")
			{
				await HandleContentInstallAsync(request, response);
				return;
			}

			if (seg.Length == 2 && seg[1].Equals("java", StringComparison.OrdinalIgnoreCase) && method == "GET")
			{
				await HandleListJavaAsync(response);
				return;
			}

			if (seg.Length == 2 && seg[1].Equals("servers", StringComparison.OrdinalIgnoreCase))
			{
				if (method == "GET")
				{
					await HandleListServersAsync(response);
					return;
				}

				if (method == "POST")
				{
					await HandleAddServerAsync(request, response);
					return;
				}
			}

			if (seg.Length == 4 && seg[1].Equals("servers", StringComparison.OrdinalIgnoreCase) && method == "POST")
			{
				string serverId = seg[2];
				string action = seg[3];
				if (action.Equals("ping", StringComparison.OrdinalIgnoreCase))
				{
					await HandlePingServerAsync(response, serverId);
					return;
				}

				if (action.Equals("delete", StringComparison.OrdinalIgnoreCase))
				{
					await HandleDeleteServerAsync(response, serverId);
					return;
				}
			}

			await WriteJsonAsync(response, 404, new { error = $"未找到接口：{method} {path}" });
		}
		catch (InvalidOperationException ex)
		{
			await WriteJsonAsync(response, 400, new { error = ex.Message });
		}
		catch (Exception ex)
		{
			Debug.WriteLine($"[LocalApi] api error: {ex}");
			await WriteJsonAsync(response, 500, new { error = ex.Message });
		}
	}

	/* ================= 配置读写（全量落盘，与桌面端共用 config.json） ================= */

	private static AppConfig LoadConfig()
	{
		lock (ConfigGate)
		{
			return ConfigStore.LoadAsync().GetAwaiter().GetResult();
		}
	}

	private static void SaveConfig(AppConfig config)
	{
		lock (ConfigGate)
		{
			MirrorHelper.Enabled = config.UseMirror;
			ConfigStore.SaveAsync(config).GetAwaiter().GetResult();
		}
	}

	private static string ResolveGameDirectory(AppConfig config, GameInstance instance)
	{
		string dir = instance?.GameDirectory;
		if (string.IsNullOrWhiteSpace(dir))
		{
			dir = config.GameDirectory;
		}

		return string.IsNullOrWhiteSpace(dir) ? PlatformPaths.GetDefaultMinecraftDirectory() : dir;
	}

	private static GameInstance RequireInstance(AppConfig config, string instanceId)
	{
		return config.Instances.FirstOrDefault(x => x.Id == instanceId)
			?? throw new InvalidOperationException("实例不存在：" + instanceId);
	}

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
				config.SelectedVersion,
				curseForgeApiKeyConfigured = !string.IsNullOrEmpty(config.CurseForgeApiKey),
				config.SelectedInstanceId,
				config.SelectedAccountId,
				config.AccountMode
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

		return new
		{
			instance.Id,
			instance.Name,
			instance.MinecraftVersion,
			gameDirectory = gameDir,
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

	private static async Task HandleDeleteInstanceAsync(HttpListenerResponse response, string instanceId)
	{
		AppConfig config = LoadConfig();
		RequireInstance(config, instanceId);
		config.Instances.RemoveAll(x => x.Id == instanceId);
		if (config.SelectedInstanceId == instanceId)
		{
			config.SelectedInstanceId = config.Instances.FirstOrDefault()?.Id ?? string.Empty;
		}

		SaveConfig(config);
		await WriteJsonAsync(response, 200, new { ok = true });
	}

	private static async Task HandleLaunchAsync(HttpListenerResponse response, string instanceId)
	{
		AppConfig config = LoadConfig();
		GameInstance instance = RequireInstance(config, instanceId);
		string gameDir = ResolveGameDirectory(config, instance);
		if (string.IsNullOrWhiteSpace(instance.MinecraftVersion))
		{
			throw new InvalidOperationException("实例未关联游戏版本，请先在设置中安装版本");
		}

		LauncherAccount account = config.Accounts.FirstOrDefault(x => x.Id == config.SelectedAccountId);
		string mode = instance.AccountMode;
		string playerName = account?.UserName ?? config.PlayerName;
		string playerUuid = string.Empty;
		string accessToken = "0";
		string userType = "legacy";
		string xuid = string.Empty;
		string clientId = config.MicrosoftClientId;
		string customJvmArgs = instance.CustomJvmArgs ?? string.Empty;

		if (mode == "microsoft")
		{
			if (string.IsNullOrWhiteSpace(config.MicrosoftUserName) || string.IsNullOrWhiteSpace(config.MicrosoftUuid) || string.IsNullOrWhiteSpace(config.MicrosoftAccessToken))
			{
				throw new InvalidOperationException("正版账号信息不完整，请在桌面端完成正版登录后重试");
			}

			playerName = config.MicrosoftUserName;
			playerUuid = config.MicrosoftUuid;
			accessToken = config.MicrosoftAccessToken;
			userType = "msa";
			xuid = config.MicrosoftXuid;
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
		instance.LastLaunchTime = DateTimeOffset.Now;
		SaveConfig(config);
		await WriteJsonAsync(response, 200, new { ok = true, pid = process.Id });
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
		JavaInstallation match = JavaLocator.MatchForMinecraftVersion(found, minecraftVersion);
		if (match != null)
		{
			return match.JavaPath;
		}

		JavaInfo any = JavaLocator.FindAsync().GetAwaiter().GetResult();
		if (any != null)
		{
			return any.Path;
		}

		throw new InvalidOperationException("未找到可用的 Java 运行时，请先在设置中指定 Java 路径");
	}

	private static string AppendJvmArgument(string existing, string argument)
	{
		return string.IsNullOrWhiteSpace(existing) ? argument.Trim() : existing.Trim() + " " + argument.Trim();
	}

	private static async Task HandleListSavesAsync(HttpListenerResponse response, string instanceId)
	{
		AppConfig config = LoadConfig();
		GameInstance instance = RequireInstance(config, instanceId);
		string gameDir = ResolveGameDirectory(config, instance);
		List<SaveInfo> saves = new SaveService(gameDir).GetSaves(instance);
		await WriteJsonAsync(response, 200, new
		{
			instanceId,
			gameDir,
			saves = saves.Select(s => new
			{
				s.FolderName,
				s.FullPath,
				s.DisplayName,
				s.LastPlayed,
				s.SizeText,
				s.HasIcon,
				icon = s.HasIcon && s.IconBytes != null ? "data:image/png;base64," + Convert.ToBase64String(s.IconBytes) : null
			})
		});
	}

	private static async Task HandleListModsAsync(HttpListenerResponse response, string instanceId)
	{
		AppConfig config = LoadConfig();
		GameInstance instance = RequireInstance(config, instanceId);
		string gameDir = ResolveGameDirectory(config, instance);
		List<ModFile> mods = await new ModService(gameDir).GetModsAsync(instance);
		await WriteJsonAsync(response, 200, new { instanceId, mods = mods.Select(BuildModSummary) });
	}

	private static object BuildModSummary(ModFile m)
	{
		return new
		{
			m.FileName,
			m.FullPath,
			m.SizeBytes,
			m.SizeText,
			m.IsEnabled,
			m.LastModifiedTime,
			m.DisplayName,
			m.ModId,
			m.Version,
			m.LoaderHint,
			m.Summary,
			m.OnlineSource,
			m.ProjectId,
			m.VersionId,
			m.OnlineVersionNumber,
			m.HasOnlineMetadata,
			dependencies = m.Dependencies?.Select(d => new { d.ModId, d.VersionRange, isRequired = d.IsRequired })
		};
	}

	private static async Task HandleToggleModAsync(HttpListenerResponse response, string instanceId, string fileName)
	{
		AppConfig config = LoadConfig();
		GameInstance instance = RequireInstance(config, instanceId);
		string gameDir = ResolveGameDirectory(config, instance);
		ModService service = new ModService(gameDir);
		ModFile mod = (await service.GetModsAsync(instance)).FirstOrDefault(x => x.FileName == fileName)
			?? throw new InvalidOperationException("Mod 不存在：" + fileName);
		if (mod.IsEnabled)
		{
			service.DisableMod(mod);
		}
		else
		{
			service.EnableMod(mod);
		}

		await WriteJsonAsync(response, 200, new { ok = true, fileName, isEnabled = !mod.IsEnabled });
	}

	private static async Task HandleDeleteModAsync(HttpListenerResponse response, string instanceId, string fileName)
	{
		AppConfig config = LoadConfig();
		GameInstance instance = RequireInstance(config, instanceId);
		string gameDir = ResolveGameDirectory(config, instance);
		ModService service = new ModService(gameDir);
		ModFile mod = (await service.GetModsAsync(instance)).FirstOrDefault(x => x.FileName == fileName)
			?? throw new InvalidOperationException("Mod 不存在：" + fileName);
		service.DeleteMod(mod);
		await WriteJsonAsync(response, 200, new { ok = true, fileName });
	}

	private static async Task HandleListResourcePacksAsync(HttpListenerResponse response, string instanceId)
	{
		AppConfig config = LoadConfig();
		GameInstance instance = RequireInstance(config, instanceId);
		string gameDir = ResolveGameDirectory(config, instance);
		List<ResourcePackInfo> packs = new ResourcePackService(gameDir).GetResourcePacks(instance);
		await WriteJsonAsync(response, 200, new
		{
			instanceId,
			packs = packs.Select(p => new
			{
				p.FileName,
				p.FullPath,
				p.DisplayName,
				p.Description,
				p.PackFormat,
				p.SizeText,
				p.HasCover,
				cover = p.HasCover && p.CoverBytes != null ? "data:image/png;base64," + Convert.ToBase64String(p.CoverBytes) : null
			})
		});
	}

	private static async Task HandleListShaderPacksAsync(HttpListenerResponse response, string instanceId)
	{
		AppConfig config = LoadConfig();
		GameInstance instance = RequireInstance(config, instanceId);
		string gameDir = ResolveGameDirectory(config, instance);
		List<ShaderPackInfo> packs = new ShaderPackService(gameDir).GetPacks(instance);
		await WriteJsonAsync(response, 200, new { instanceId, packs = packs.Select(p => new { p.FileName, p.FullPath, p.SizeText }) });
	}

	private static object BuildAccountSummary(LauncherAccount a)
	{
		return new
		{
			a.Id,
			a.Type,
			a.UserName,
			a.Uuid,
			a.Xuid,
			a.AuthServerUrl,
			a.SkinUrl,
			a.SkinCount,
			a.CapeCount,
			a.Capes,
			a.AccessTokenExpiresAt,
			a.IsMicrosoft,
			a.IsThirdParty
		};
	}

	private static async Task HandleListAccountsAsync(HttpListenerResponse response)
	{
		AppConfig config = LoadConfig();
		await WriteJsonAsync(response, 200, new { accounts = config.Accounts.Select(BuildAccountSummary), selectedAccountId = config.SelectedAccountId });
	}

	private static async Task HandleAddOfflineAccountAsync(HttpListenerRequest request, HttpListenerResponse response)
	{
		JsonDocument body = await ReadBodyAsync(request);
		string userName = JsonUtil.GetString(body.RootElement, "userName")?.Trim();
		if (string.IsNullOrWhiteSpace(userName))
		{
			throw new InvalidOperationException("玩家名不能为空");
		}

		AppConfig config = LoadConfig();
		LauncherAccount account = AccountService.AddOffline(config, userName);
		AccountService.Select(config, account.Id);
		SaveConfig(config);
		await WriteJsonAsync(response, 200, new { ok = true, account = BuildAccountSummary(account) });
	}

	private static async Task HandleSelectAccountAsync(HttpListenerRequest request, HttpListenerResponse response)
	{
		JsonDocument body = await ReadBodyAsync(request);
		string accountId = JsonUtil.GetString(body.RootElement, "accountId");
		AppConfig config = LoadConfig();
		if (config.Accounts.All(x => x.Id != accountId))
		{
			throw new InvalidOperationException("账号不存在：" + accountId);
		}

		AccountService.Select(config, accountId);
		SaveConfig(config);
		await WriteJsonAsync(response, 200, new { ok = true, selectedAccountId = accountId });
	}

	private static async Task HandleMicrosoftLoginAsync(HttpListenerResponse response)
	{
		string clientId = AuthClientOptions.ResolveMicrosoftClientId();
		if (!AuthClientOptions.HasUsableMicrosoftClientId(clientId))
		{
			await WriteJsonAsync(response, 400, new { ok = false, message = "未配置 Microsoft Client ID，请设置环境变量 " + AuthClientOptions.ClientIdEnvironmentVariable + " 或配置文件 MicrosoftClientId。" });
			return;
		}

		MicrosoftAuthService authService = new MicrosoftAuthService(ApiHttp);
		MicrosoftBrowserAuthorizationService browserService = new MicrosoftBrowserAuthorizationService(ApiHttp);
		using CancellationTokenSource cts = new CancellationTokenSource(TimeSpan.FromMinutes(6));
		try
		{
			MicrosoftBrowserAuthorizationService.MicrosoftBrowserAuthorizationSession session;
			try
			{
				session = await browserService.StartAsync(clientId, cts.Token);
			}
			catch (MicrosoftBrowserAuthorizationException ex) when (ex.Diagnostic.StartsWith("stage=loopback-listener", StringComparison.Ordinal))
			{
				// 本机回调不可用：登录码流程暂未接入，先返回明确错误
				await WriteJsonAsync(response, 400, new { ok = false, message = ex.Message, diagnostic = ex.Diagnostic });
				return;
			}

			await using (session)
			{
				// 打开系统默认浏览器完成授权
				Process.Start(new ProcessStartInfo { UseShellExecute = true, FileName = session.AuthorizationUri });
				MicrosoftOAuthToken oauth = await session.WaitForTokenAsync(cts.Token);
				MinecraftAccount account = await authService.LoginMinecraftAsync(oauth, cts.Token);

				AppConfig config = LoadConfig();
				LauncherAccount launcherAccount = AccountService.UpsertMicrosoft(config, account, clientId);
				AccountService.Select(config, launcherAccount.Id);
				SaveConfig(config);
				await WriteJsonAsync(response, 200, new { ok = true, message = "微软账号登录成功", account = BuildAccountSummary(launcherAccount) });
			}
		}
		catch (MicrosoftBrowserAuthorizationException ex)
		{
			await WriteJsonAsync(response, 400, new { ok = false, message = ex.Message, diagnostic = ex.Diagnostic });
		}
		catch (MinecraftAppRegistrationRequiredException ex)
		{
			await WriteJsonAsync(response, 400, new { ok = false, message = ex.Message });
		}
		catch (OperationCanceledException)
		{
			await WriteJsonAsync(response, 400, new { ok = false, message = "登录已取消或超时，请重试。" });
		}
		catch (Exception ex)
		{
			await WriteJsonAsync(response, 500, new { ok = false, message = "微软登录失败：" + ex.Message });
		}
	}

	private static void HandleOpenLogDirectory(HttpListenerResponse response)
	{
		string dir = LauncherLog.GetLogDirectory();
		if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir))
		{
			WriteJsonAsync(response, 400, new { ok = false, message = "日志目录不存在" }).GetAwaiter().GetResult();
			return;
		}

		try
		{
			if (OperatingSystem.IsWindows())
			{
				Process.Start(new ProcessStartInfo { FileName = "explorer.exe", Arguments = "\"" + dir + "\"", UseShellExecute = true });
			}
			else
			{
				Process.Start(new ProcessStartInfo { FileName = "xdg-open", Arguments = dir, UseShellExecute = true });
			}
			LauncherLog.Info("[logs] 已打开日志目录: " + dir);
			WriteJsonAsync(response, 200, new { ok = true, path = dir }).GetAwaiter().GetResult();
		}
		catch (Exception ex)
		{
			WriteJsonAsync(response, 500, new { ok = false, message = ex.Message }).GetAwaiter().GetResult();
		}
	}

	private static async Task HandleListVersionsAsync(HttpListenerResponse response)
	{
		AppConfig config = LoadConfig();
		string gameDir = ResolveGameDirectory(config, null);
		List<string> local = VersionResolver.ListLocalVersions(gameDir);
		await WriteJsonAsync(response, 200, new { gameDir, local });
	}

	private static async Task HandleListRemoteVersionsAsync(HttpListenerResponse response)
	{
		AppConfig config = LoadConfig();
		string gameDir = ResolveGameDirectory(config, null);
		using CancellationTokenSource cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
		List<VersionEntry> online = await new MinecraftDownloader(ApiHttp, _ => { }).FetchOnlineVersionsAsync(cts.Token);
		List<string> local = VersionResolver.ListLocalVersions(gameDir);
		await WriteJsonAsync(response, 200, new
		{
			gameDir,
			versions = online.Select(v => new { v.Id, v.Type, v.ReleaseTime, installed = local.Contains(v.Id) })
		});
	}

	private static async Task HandleDownloadVersionAsync(HttpListenerRequest request, HttpListenerResponse response)
	{
		JsonDocument body = await ReadBodyAsync(request);
		string versionId = JsonUtil.GetString(body.RootElement, "versionId")?.Trim();
		if (string.IsNullOrWhiteSpace(versionId))
		{
			throw new InvalidOperationException("versionId 不能为空");
		}

		AppConfig config = LoadConfig();
		string gameDir = ResolveGameDirectory(config, null);

		DownloadTaskEntry entry;
		lock (DownloadGate)
		{
			entry = new DownloadTaskEntry
			{
				Id = "dlv" + DateTimeOffset.Now.ToUnixTimeMilliseconds(),
				VersionId = versionId,
				Status = "queued",
				Message = "排队中",
				StartedAt = DateTimeOffset.Now
			};
			DownloadTasks.Add(entry);
		}

		_ = Task.Run(() => RunDownloadTaskAsync(entry, gameDir));
		await WriteJsonAsync(response, 200, new { ok = true, id = entry.Id, versionId, status = "queued" });
	}

	private static async Task RunDownloadTaskAsync(DownloadTaskEntry entry, string gameDir)
	{
		try
		{
			lock (DownloadGate)
			{
				entry.Status = "downloading";
				entry.Message = "正在获取版本元数据";
			}

			var downloader = new MinecraftDownloader(ApiHttp, msg =>
			{
				lock (DownloadGate)
				{
					entry.Message = msg;
				}
			}, progress =>
			{
				lock (DownloadGate)
				{
					entry.Progress = progress.Percentage;
					if (!string.IsNullOrWhiteSpace(progress.Message))
					{
						entry.Message = progress.Message;
					}
				}
			});

			await downloader.InstallVersionAsync(gameDir, entry.VersionId, CancellationToken.None);
			lock (DownloadGate)
			{
				entry.Status = "completed";
				entry.Progress = 1.0;
				entry.Message = "安装完成";
			}
		}
		catch (Exception ex)
		{
			lock (DownloadGate)
			{
				entry.Status = "failed";
				entry.Message = ex.Message;
			}
		}
	}

	private static async Task HandleListDownloadsAsync(HttpListenerResponse response)
	{
		object[] snapshot;
		lock (DownloadGate)
		{
			snapshot = DownloadTasks.Select(t => new
			{
				t.Id,
				t.VersionId,
				name = "Minecraft " + t.VersionId + " 客户端",
				t.Status,
				t.Progress,
				t.Message,
				t.StartedAt
			}).ToArray();
		}

		await WriteJsonAsync(response, 200, new { tasks = snapshot });
	}

	/* ================= 自更新 ================= */

	private static async Task HandleCheckUpdateAsync(HttpListenerResponse response)
	{
		AppConfig config = LoadConfig();
		string url = config.UpdateUrl?.Trim() ?? string.Empty;
		if (string.IsNullOrWhiteSpace(url))
		{
			await WriteJsonAsync(response, 200, new { enabled = false, currentVersion = LauncherService.LauncherVersion, message = "未配置更新源（config.json 的 updateUrl）" });
			return;
		}

		try
		{
			UpdateManifest? manifest = await UpdateService.FetchManifestAsync(ApiHttp, url, CancellationToken.None);
			if (manifest == null || string.IsNullOrWhiteSpace(manifest.Version))
			{
				await WriteJsonAsync(response, 200, new { enabled = true, currentVersion = LauncherService.LauncherVersion, error = "更新服务器响应无效" });
				return;
			}
			bool available = UpdateService.IsNewerVersion(manifest.Version, LauncherService.LauncherVersion);
			LauncherLog.Info("[update] 检查更新：current=" + LauncherService.LauncherVersion + " latest=" + manifest.Version + " available=" + available);
			await WriteJsonAsync(response, 200, new
			{
				enabled = true,
				currentVersion = LauncherService.LauncherVersion,
				updateAvailable = available,
				latestVersion = manifest.Version,
				notes = manifest.Notes,
				mandatory = manifest.Mandatory,
				downloadUrl = manifest.Url
			});
		}
		catch (Exception ex)
		{
			LauncherLog.Error("[update] 检查更新失败: " + ex.Message, ex);
			await WriteJsonAsync(response, 200, new { enabled = true, currentVersion = LauncherService.LauncherVersion, error = ex.Message });
		}
	}

	private static async Task HandleStartUpdateDownloadAsync(HttpListenerResponse response)
	{
		bool alreadyDownloading;
		lock (UpdateGate)
		{
			alreadyDownloading = UpdateState == "downloading";
		}
		if (alreadyDownloading)
		{
			await WriteJsonAsync(response, 200, new { ok = true, state = "downloading" });
			return;
		}

		AppConfig config = LoadConfig();
		string url = config.UpdateUrl?.Trim() ?? string.Empty;
		if (string.IsNullOrWhiteSpace(url))
		{
			await WriteJsonAsync(response, 400, new { ok = false, message = "未配置更新源（config.json 的 updateUrl）" });
			return;
		}

		try
		{
			UpdateManifest? manifest = await UpdateService.FetchManifestAsync(ApiHttp, url, CancellationToken.None);
			if (manifest == null || string.IsNullOrWhiteSpace(manifest.Url))
			{
				await WriteJsonAsync(response, 400, new { ok = false, message = "更新服务器响应无效" });
				return;
			}
			lock (UpdateGate)
			{
				UpdateState = "downloading";
				UpdateProgress = 0.0;
				UpdateVersion = manifest.Version;
				UpdatePath = string.Empty;
				UpdateError = string.Empty;
				UpdateMandatory = manifest.Mandatory;
			}
			LauncherLog.Info("[update] 开始下载更新 v" + manifest.Version + " ← " + manifest.Url);
			_ = Task.Run(() => DownloadUpdateWorkerAsync(manifest));
			await WriteJsonAsync(response, 200, new { ok = true, state = "downloading", version = manifest.Version });
		}
		catch (Exception ex)
		{
			LauncherLog.Error("[update] 启动下载失败: " + ex.Message, ex);
			await WriteJsonAsync(response, 500, new { ok = false, message = ex.Message });
		}
	}

	private static async Task DownloadUpdateWorkerAsync(UpdateManifest manifest)
	{
		try
		{
			Progress<double> progress = new Progress<double>(value =>
			{
				lock (UpdateGate)
				{
					UpdateProgress = value;
				}
			});
			string path = await UpdateService.DownloadUpdateAsync(ApiHttp, manifest, progress, CancellationToken.None);
			lock (UpdateGate)
			{
				UpdateState = "completed";
				UpdatePath = path;
				UpdateProgress = 1.0;
			}
			LauncherLog.Info("[update] 下载完成: " + path);
		}
		catch (Exception ex)
		{
			LauncherLog.Error("[update] 下载失败: " + ex.Message, ex);
			lock (UpdateGate)
			{
				UpdateState = "failed";
				UpdateError = ex.Message;
			}
		}
	}

	private static async Task HandleUpdateStatusAsync(HttpListenerResponse response)
	{
		string state, version, path, error;
		double progress;
		bool mandatory;
		lock (UpdateGate)
		{
			state = UpdateState;
			version = UpdateVersion;
			path = UpdatePath;
			error = UpdateError;
			progress = UpdateProgress;
			mandatory = UpdateMandatory;
		}
		await WriteJsonAsync(response, 200, new { state, progress, version, path, error, mandatory, currentVersion = LauncherService.LauncherVersion });
	}

	private static async Task HandleApplyUpdateAsync(HttpListenerResponse response)
	{
		string path, version;
		lock (UpdateGate)
		{
			path = UpdatePath;
			version = UpdateVersion;
		}
		if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
		{
			await WriteJsonAsync(response, 400, new { ok = false, message = "更新包未就绪，请先下载。" });
			return;
		}

		try
		{
			UpdateService.ApplyUpdate(path, PlatformPaths.GetScriptExtension());
			lock (UpdateGate)
			{
				UpdateState = "applying";
			}
			LauncherLog.Info("[update] 正在应用更新 v" + version + " → " + path);
			await WriteJsonAsync(response, 200, new { ok = true, message = "更新已启动，重启后生效。" });
			_ = Task.Run(async () =>
			{
				await Task.Delay(1200);
				Environment.Exit(0);
			});
		}
		catch (Exception ex)
		{
			LauncherLog.Error("[update] 应用更新失败: " + ex.Message, ex);
			await WriteJsonAsync(response, 500, new { ok = false, message = ex.Message });
		}
	}

	private static async Task HandleUpdateConfigAsync(HttpListenerRequest request, HttpListenerResponse response)
	{
		JsonDocument body = await ReadBodyAsync(request);
		JsonElement root = body.RootElement;
		AppConfig config = LoadConfig();
		PatchConfig(config, root);
		SaveConfig(config);
		await WriteJsonAsync(response, 200, new { ok = true });
	}

	private static void PatchConfig(AppConfig config, JsonElement root)
	{
		string GameDirectory = JsonUtil.GetString(root, "gameDirectory");
		if (GameDirectory != null) config.GameDirectory = GameDirectory;

		string javaPath = JsonUtil.GetString(root, "javaPath");
		if (javaPath != null) config.JavaPath = javaPath;

		string playerName = JsonUtil.GetString(root, "playerName");
		if (playerName != null) config.PlayerName = playerName;

		string selectedVersion = JsonUtil.GetString(root, "selectedVersion");
		if (selectedVersion != null) config.SelectedVersion = selectedVersion;

		string accountMode = JsonUtil.GetString(root, "accountMode");
		if (accountMode != null) config.AccountMode = accountMode;

		string serverHost = JsonUtil.GetString(root, "serverHost");
		if (serverHost != null) config.ServerHost = serverHost;

		string serverPort = JsonUtil.GetString(root, "serverPort");
		if (serverPort != null) config.ServerPort = serverPort;

		string curseForgeApiKey = JsonUtil.GetString(root, "curseForgeApiKey");
		if (curseForgeApiKey != null) config.CurseForgeApiKey = curseForgeApiKey;

		if (root.TryGetProperty("maxMemoryMb", out JsonElement maxMemory) && maxMemory.ValueKind == JsonValueKind.Number)
		{
			config.MaxMemoryMb = maxMemory.GetInt32();
		}

		if (root.TryGetProperty("useAutoMemory", out JsonElement useAuto) && useAuto.ValueKind == JsonValueKind.True)
		{
			config.UseAutoMemory = true;
		}
		else if (root.TryGetProperty("useAutoMemory", out useAuto) && useAuto.ValueKind == JsonValueKind.False)
		{
			config.UseAutoMemory = false;
		}

		if (root.TryGetProperty("windowWidth", out JsonElement width) && width.ValueKind == JsonValueKind.Number)
		{
			config.WindowWidth = width.GetInt32();
		}

		if (root.TryGetProperty("windowHeight", out JsonElement height) && height.ValueKind == JsonValueKind.Number)
		{
			config.WindowHeight = height.GetInt32();
		}

		if (root.TryGetProperty("useMirror", out JsonElement mirror) && mirror.ValueKind == JsonValueKind.True)
		{
			config.UseMirror = true;
		}
		else if (root.TryGetProperty("useMirror", out mirror) && mirror.ValueKind == JsonValueKind.False)
		{
			config.UseMirror = false;
		}
	}

	private static async Task HandleListWorldsAsync(HttpListenerResponse response)
	{
		AppConfig config = LoadConfig();
		List<object> worlds = new List<object>();
		foreach (GameInstance instance in config.Instances)
		{
			string gameDir = ResolveGameDirectory(config, instance);
			List<SaveInfo> saves;
			try
			{
				saves = new SaveService(gameDir).GetSaves(instance);
			}
			catch
			{
				continue;
			}

			worlds.AddRange(saves.Select(s => new
			{
				s.FolderName,
				s.DisplayName,
				s.LastPlayed,
				s.SizeText,
				s.HasIcon,
				instanceId = instance.Id,
				instanceName = instance.Name
			}));
		}

		await WriteJsonAsync(response, 200, new { worlds });
	}

	private static async Task HandleSearchContentAsync(HttpListenerRequest request, HttpListenerResponse response)
	{
		string query = request.QueryString["q"] ?? string.Empty;
		string category = request.QueryString["category"] ?? "mod";
		string gameVersion = request.QueryString["gameVersion"] ?? string.Empty;
		string loader = request.QueryString["loader"] ?? string.Empty;
		if (string.IsNullOrWhiteSpace(query))
		{
			await WriteJsonAsync(response, 200, new { results = new object[0] });
			return;
		}

		using CancellationTokenSource cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
		try
		{
			switch (category.ToLowerInvariant())
			{
				case "modpack":
				{
					List<ModpackProject> packs = await new ModrinthModpackService(ApiHttp).SearchAsync(query, cts.Token);
					await WriteJsonAsync(response, 200, new
					{
						results = packs.Select(p => new
						{
							id = p.ProjectId,
							title = p.Title,
							desc = p.Description,
							author = p.Author,
							downloads = p.Downloads,
							category,
							versions = p.Categories,
							loader = string.Empty,
							icon = p.IconUrl
						})
					});
					return;
				}
				case "shader":
				case "resourcepack":
				{
					ResourceContentKind kind = category.Equals("shader", StringComparison.OrdinalIgnoreCase)
						? ResourceContentKind.Shader
						: ResourceContentKind.ResourcePack;
					List<ResourceContentProject> projects = await new ResourceContentService(ApiHttp).SearchAsync(query, gameVersion, kind, cts.Token);
					await WriteJsonAsync(response, 200, new
					{
						results = projects.Select(p => new
						{
							id = p.ProjectId,
							title = p.Title,
							desc = p.Description,
							author = p.Author,
							downloads = p.Downloads,
							category,
							versions = new string[0],
							loader = string.Empty,
							icon = string.Empty
						})
					});
					return;
				}
				default:
				{
					ModSearchRequest req = new ModSearchRequest
					{
						Source = ModSource.Modrinth,
						Query = query,
						MinecraftVersion = gameVersion,
						Loader = loader,
						Limit = 24
					};
					List<ModSearchResult> results = await new ModContentService(ApiHttp).SearchAsync(req, cts.Token);
					await WriteJsonAsync(response, 200, new
					{
						results = results.Select(m => new
						{
							id = m.ProjectId,
							title = m.Title,
							desc = m.Description,
							author = m.Author,
							downloads = m.Downloads,
							category,
							versions = m.GameVersions,
							loader = m.Categories.FirstOrDefault(c => ModLoaderInfo.SupportedKinds.Any(k => k.ToString().Equals(c, StringComparison.OrdinalIgnoreCase))) ?? string.Empty,
							icon = m.IconUrl
						})
					});
					return;
				}
			}
		}
		catch (Exception ex)
		{
			Debug.WriteLine($"[LocalApi] content search error: {ex.Message}");
			await WriteJsonAsync(response, 200, new { results = new object[0], error = ex.Message });
		}
	}

	private static async Task HandleContentInstallAsync(HttpListenerRequest request, HttpListenerResponse response)
	{
		JsonDocument body = await ReadBodyAsync(request);
		string category = (JsonUtil.GetString(body.RootElement, "category") ?? "mod").Trim().ToLowerInvariant();
		string projectId = JsonUtil.GetString(body.RootElement, "projectId")?.Trim() ?? string.Empty;
		string instanceId = JsonUtil.GetString(body.RootElement, "instanceId")?.Trim() ?? string.Empty;
		if (string.IsNullOrWhiteSpace(projectId))
		{
			throw new InvalidOperationException("projectId 不能为空");
		}

		AppConfig config = LoadConfig();
		if (category != "modpack")
		{
			if (string.IsNullOrWhiteSpace(instanceId))
			{
				instanceId = config.SelectedInstanceId;
			}

			if (string.IsNullOrWhiteSpace(instanceId))
			{
				throw new InvalidOperationException("请先选择一个实例再安装内容");
			}

			RequireInstance(config, instanceId);
		}

		ContentTaskEntry entry = new ContentTaskEntry
		{
			Id = "ct" + DateTimeOffset.Now.ToUnixTimeMilliseconds(),
			Category = category,
			ProjectId = projectId,
			InstanceId = instanceId,
			Status = "queued",
			Message = "排队中",
			StartedAt = DateTimeOffset.Now
		};
		lock (ContentGate)
		{
			ContentTasks.Add(entry);
		}

		_ = Task.Run(() => RunContentInstallTaskAsync(entry));
		await WriteJsonAsync(response, 200, new { ok = true, id = entry.Id, category, status = "queued" });
	}

	private static async Task HandleListContentTasksAsync(HttpListenerResponse response)
	{
		object[] snapshot;
		lock (ContentGate)
		{
			snapshot = ContentTasks.Select(t => new
			{
				t.Id,
				t.Category,
				t.ProjectId,
				t.InstanceId,
				t.Title,
				t.Status,
				t.Progress,
				t.Message,
				t.ResultInstanceId,
				t.StartedAt
			}).ToArray();
		}

		await WriteJsonAsync(response, 200, new { tasks = snapshot });
	}

	private static async Task RunContentInstallTaskAsync(ContentTaskEntry entry)
	{
		try
		{
			lock (ContentGate)
			{
				entry.Status = "running";
				entry.Message = "正在准备安装";
			}

			AppConfig config = LoadConfig();
			switch (entry.Category)
			{
				case "modpack":
					entry.Title = "整合包";
					await InstallModpackContentAsync(entry, config);
					break;
				case "shader":
				case "resourcepack":
					entry.Title = entry.Category == "shader" ? "光影" : "资源包";
					await InstallResourceContentAsync(entry, config);
					break;
				default:
					entry.Title = "Mod";
					await InstallModContentAsync(entry, config);
					break;
			}

			lock (ContentGate)
			{
				entry.Status = "completed";
				entry.Progress = 1.0;
				entry.Message = "安装完成";
			}
		}
		catch (Exception ex)
		{
			lock (ContentGate)
			{
				entry.Status = "failed";
				entry.Message = ex.Message;
			}

			LauncherLog.Error($"[ContentInstall] {entry.Category} {entry.ProjectId} 失败: {ex.Message}");
		}
	}

	private static void UpdateContentTask(ContentTaskEntry entry, string message, double progress = -1)
	{
		lock (ContentGate)
		{
			if (!string.IsNullOrWhiteSpace(message))
			{
				entry.Message = message;
			}

			if (progress >= 0)
			{
				entry.Progress = progress;
			}
		}
	}

	private static async Task InstallModContentAsync(ContentTaskEntry entry, AppConfig config)
	{
		GameInstance instance = RequireInstance(config, entry.InstanceId);
		string gameDir = ResolveGameDirectory(config, instance);
		ModContentService content = new ModContentService(ApiHttp);
		ModSearchResult project = new ModSearchResult
		{
			Source = ModSource.Modrinth,
			ProjectId = entry.ProjectId
		};
		UpdateContentTask(entry, "正在获取版本信息", 0.1);
		List<ModVersion> versions = await content.GetVersionsAsync(project, instance.MinecraftVersion, instance.LoaderType);
		ModVersion version = versions.FirstOrDefault(v => v.ReleaseType.Equals("release", StringComparison.OrdinalIgnoreCase)) ?? versions.FirstOrDefault();
		if (version == null)
		{
			throw new InvalidOperationException("该 Mod 没有找到兼容当前实例的版本");
		}

		UpdateContentTask(entry, $"正在安装 {version.DisplayName} {version.VersionNumber}", 0.2);
		ModService modService = new ModService(gameDir);
		ModInstallResult result = await content.InstallWithDependenciesAsync(instance, version, modService, instance.MinecraftVersion, instance.LoaderType,
			curseForgeApiKey: config.CurseForgeApiKey ?? string.Empty,
			progress: new Progress<double>(p => UpdateContentTask(entry, string.Empty, 0.2 + p * 0.7)),
			status: msg => UpdateContentTask(entry, msg),
			ct: CancellationToken.None);
		entry.Title = version.DisplayName;
	}

	private static async Task InstallResourceContentAsync(ContentTaskEntry entry, AppConfig config)
	{
		GameInstance instance = RequireInstance(config, entry.InstanceId);
		string gameDir = ResolveGameDirectory(config, instance);
		ResourceContentKind kind = entry.Category.Equals("shader", StringComparison.OrdinalIgnoreCase)
			? ResourceContentKind.Shader
			: ResourceContentKind.ResourcePack;
		ResourceContentService content = new ResourceContentService(ApiHttp);
		ResourceContentProject project = new ResourceContentProject
		{
			ProjectId = entry.ProjectId,
			Kind = kind
		};
		UpdateContentTask(entry, "正在获取版本信息", 0.1);
		List<ResourceContentVersion> versions = await content.GetVersionsAsync(project, instance.MinecraftVersion);
		ResourceContentVersion version = versions.FirstOrDefault(v => v.VersionNumber != string.Empty) ?? versions.FirstOrDefault();
		if (version == null)
		{
			throw new InvalidOperationException("该内容没有找到可用版本");
		}

		UpdateContentTask(entry, $"正在安装 {version.FileName}", 0.2);
		await content.DownloadAsync(instance, project, version, gameDir,
			progress: new Progress<double>(p => UpdateContentTask(entry, string.Empty, 0.2 + p * 0.7)),
			ct: CancellationToken.None);
		entry.Title = version.FileName;
	}

	private static async Task InstallModpackContentAsync(ContentTaskEntry entry, AppConfig config)
	{
		ModrinthModpackService modrinth = new ModrinthModpackService(ApiHttp);
		ModpackProject project = new ModpackProject
		{
			ProjectId = entry.ProjectId
		};
		UpdateContentTask(entry, "正在获取整合包版本", 0.05);
		List<ModpackVersion> versions = await modrinth.GetVersionsAsync(project);
		ModpackVersion version = versions.FirstOrDefault(v => v.ReleaseType.Equals("release", StringComparison.OrdinalIgnoreCase)) ?? versions.FirstOrDefault();
		if (version == null)
		{
			throw new InvalidOperationException("该整合包没有可用版本");
		}

		UpdateContentTask(entry, $"正在下载 {project.Title} {version.VersionNumber}", 0.1);
		string zipPath = await modrinth.DownloadAsync(project, version);
		try
		{
			string defaultGameDir = string.IsNullOrWhiteSpace(config.GameDirectory) ? PlatformPaths.GetDefaultMinecraftDirectory() : config.GameDirectory;
			InstancePackService packService = new InstancePackService(ApiHttp, defaultGameDir, msg => UpdateContentTask(entry, msg));
			UpdateContentTask(entry, "正在创建实例并安装整合包", 0.15);
			GameInstance newInstance = await packService.ImportModrinthPackAsync(zipPath,
				msg => UpdateContentTask(entry, msg),
				CancellationToken.None);
			AppConfig latest = LoadConfig();
			latest.Instances.Add(newInstance);
			latest.SelectedInstanceId = newInstance.Id;
			SaveConfig(latest);
			entry.Title = newInstance.Name;
			entry.ResultInstanceId = newInstance.Id;
			UpdateContentTask(entry, "整合包实例创建完成", 0.95);
		}
		finally
		{
			ModrinthModpackService.DeleteDownload(zipPath);
		}
	}

	private static async Task HandleListJavaAsync(HttpListenerResponse response)
	{
		AppConfig config = LoadConfig();
		List<JavaInstallation> installations = await JavaLocator.ScanAsync(null);
		await WriteJsonAsync(response, 200, new
		{
			config.JavaPath,
			installations = installations.Select(j => new { j.JavaPath, j.VersionMajor, j.VersionText, j.Vendor, j.IsValid })
		});
	}

	private static async Task HandleListServersAsync(HttpListenerResponse response)
	{
		AppConfig config = LoadConfig();
		await WriteJsonAsync(response, 200, new { servers = config.FavoriteServers.Select(BuildServerSummary) });
	}

	private static object BuildServerSummary(FavoriteServer s)
	{
		return new
		{
			s.Id,
			s.Name,
			s.Host,
			s.Port,
			s.StatusText,
			s.Motd,
			s.VersionName,
			s.OnlinePlayers,
			s.MaxPlayers,
			s.LatencyMs,
			s.LastCheckedAt,
			favicon = s.FaviconBytes != null ? "data:image/png;base64," + Convert.ToBase64String(s.FaviconBytes) : null
		};
	}

	private static async Task HandleAddServerAsync(HttpListenerRequest request, HttpListenerResponse response)
	{
		JsonDocument body = await ReadBodyAsync(request);
		string name = JsonUtil.GetString(body.RootElement, "name")?.Trim();
		string host = JsonUtil.GetString(body.RootElement, "host")?.Trim();
		int port = 25565;
		string portText = JsonUtil.GetString(body.RootElement, "port");
		if (!int.TryParse(portText, out port) || port <= 0 || port > 65535)
		{
			port = 25565;
		}

		if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(host))
		{
			throw new InvalidOperationException("服务器名称与地址不能为空");
		}

		AppConfig config = LoadConfig();
		FavoriteServer server = new FavoriteServer { Name = name, Host = host, Port = port };
		config.FavoriteServers.Add(server);
		SaveConfig(config);
		await WriteJsonAsync(response, 200, new { ok = true, server = BuildServerSummary(server) });
	}

	private static async Task HandlePingServerAsync(HttpListenerResponse response, string serverId)
	{
		AppConfig config = LoadConfig();
		FavoriteServer server = config.FavoriteServers.FirstOrDefault(x => x.Id == serverId)
			?? throw new InvalidOperationException("服务器不存在：" + serverId);
		using CancellationTokenSource cts = new CancellationTokenSource(TimeSpan.FromSeconds(6));
		try
		{
			MinecraftServerStatus status = await ServerPingService.QueryAsync(server.Host, server.Port, cts.Token);
			server.Motd = status.Motd;
			server.VersionName = status.VersionName;
			server.OnlinePlayers = status.OnlinePlayers;
			server.MaxPlayers = status.MaxPlayers;
			server.LatencyMs = status.LatencyMs;
			server.FaviconBytes = status.FaviconBytes;
			server.StatusText = status.OnlinePlayers >= 0 ? "可连接 · " + status.LatencyMs + " ms" : "无法连接";
			server.LastCheckedAt = DateTimeOffset.Now;
		}
		catch
		{
			server.StatusText = "无法连接";
			server.LastCheckedAt = DateTimeOffset.Now;
		}

		SaveConfig(config);
		await WriteJsonAsync(response, 200, new { ok = true, server = BuildServerSummary(server) });
	}

	private static async Task HandleDeleteServerAsync(HttpListenerResponse response, string serverId)
	{
		AppConfig config = LoadConfig();
		config.FavoriteServers.RemoveAll(x => x.Id == serverId);
		SaveConfig(config);
		await WriteJsonAsync(response, 200, new { ok = true });
	}

	/* ================= 静态文件 ================= */

	private static async Task ServeStaticAsync(HttpListenerContext context, string path)
	{
		HttpListenerResponse response = context.Response;
		string relative = path.TrimStart('/');
		if (string.IsNullOrEmpty(relative))
		{
			relative = "index.html";
		}

		string full = Path.GetFullPath(Path.Combine(_uiRoot, relative.Replace('/', Path.DirectorySeparatorChar)));
		if (!full.StartsWith(Path.GetFullPath(_uiRoot), StringComparison.OrdinalIgnoreCase) || !File.Exists(full))
		{
			await WriteJsonAsync(response, 404, new { error = "Not Found: " + path });
			return;
		}

		string contentType = "application/octet-stream";
		string ext = Path.GetExtension(full).ToLowerInvariant();
		switch (ext)
		{
			case ".html":
				contentType = "text/html; charset=utf-8";
				break;
			case ".css":
				contentType = "text/css; charset=utf-8";
				break;
			case ".js":
				contentType = "application/javascript; charset=utf-8";
				break;
			case ".json":
				contentType = "application/json; charset=utf-8";
				break;
			case ".png":
				contentType = "image/png";
				break;
			case ".jpg":
			case ".jpeg":
				contentType = "image/jpeg";
				break;
			case ".svg":
				contentType = "image/svg+xml";
				break;
			case ".ico":
				contentType = "image/x-icon";
				break;
			case ".woff2":
				contentType = "font/woff2";
				break;
		}

		response.ContentType = contentType;
		byte[] bytes = await File.ReadAllBytesAsync(full);
		await response.OutputStream.WriteAsync(bytes, 0, bytes.Length);
	}

	/* ================= 工具 ================= */

	private static async Task<JsonDocument> ReadBodyAsync(HttpListenerRequest request)
	{
		using MemoryStream ms = new MemoryStream();
		await request.InputStream.CopyToAsync(ms);
		return JsonDocument.Parse(ms.ToArray());
	}

	private static async Task WriteJsonAsync(HttpListenerResponse response, int status, object payload)
	{
		byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(payload, JsonOpts);
		response.StatusCode = status;
		response.ContentType = "application/json; charset=utf-8";
		response.ContentLength64 = bytes.Length;
		await response.OutputStream.WriteAsync(bytes, 0, bytes.Length);
	}
}
