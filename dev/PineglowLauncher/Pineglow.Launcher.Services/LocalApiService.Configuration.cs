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
/// 配置读写与测试注入缝。
/// <para>
/// config.json 的读写、以及把待测路径重定向到临时目录的几个<b>测试缝</b>。
/// 生产路径不受测试缝影响：默认实现恒为真实 APPDATA 与游戏目录。
/// </para>
/// </summary>
public static partial class LocalApiService
{
	/* ================= 配置读写（全量落盘，与桌面端共用 config.json） ================= */

	// 存储根：恢复区 / 二分落盘 / 诊断报告。三者不再是三个独立可换的静态字段，
	// 而是宿主依赖里的一个不可分割值（StorageRoots）——
	// 旧写法允许"只换一个"，代价是漏换的那个往真实 %APPDATA% 里写（第 47 轮实测）。
	private static FileRecoveryService CreateRecoveryService() => new FileRecoveryService(_host.Dependencies.Roots.Recovery);

	// M2-1①：存档级可恢复删除。与文件恢复共用同一个根，但记录与数据放在 saves/ 子目录下，
	// 两套 ListAsync 天然互不可见（否则 Mod 恢复界面会看到它无法恢复的存档条目）。
	private static SaveRecoveryService CreateSaveRecoveryService() => new SaveRecoveryService(_host.Dependencies.Roots.Recovery);

	private static DiagnosticReportService CreateDiagnosticStore() => new DiagnosticReportService(_host.Dependencies.Roots.Diagnostics);

	private static BisectRecoveryStore CreateBisectStore() => new BisectRecoveryStore(_host.Dependencies.Roots.Bisect);

	/// <summary>
	/// 替换宿主依赖（**仅测试**，且宿主必须未运行）。返回的 scope 释放时恢复前一份。
	///
	/// <para>
	/// 参数是"变形函数"而不是一个现成的 <see cref="HostDependencies"/>，因为夹具经常只需要改一块：
	/// <c>OverrideHostDependenciesForTest(d => d.WithRoots(roots))</c> 读作"只换存储根"，
	/// 而写 <c>Production().WithRoots(...)</c> 会连配置存储也一起还原成生产 —— 那是夹具最容易犯的错。
	/// </para>
	/// <para>
	/// 这取代了原来四个独立 override （config store / recovery / bisect / diagnostic）。
	/// 只留一个入口的好处是：不存在"改了三个、忘了第四个"的中间状态。
	/// </para>
	/// </summary>
	internal static IDisposable OverrideHostDependenciesForTest(Func<HostDependencies, HostDependencies> mutate)
	{
		if (mutate == null) throw new ArgumentNullException(nameof(mutate));
		lock (ConfigGate)
		{
			HostDependencies previous = _host.Dependencies;
			HostDependencies next = mutate(previous)
				?? throw new InvalidOperationException("OverrideHostDependenciesForTest 的变形函数返回了 null");
			_host.ReplaceDependencies(next);
			return new HostDependenciesScope(previous);
		}
	}

	private sealed class HostDependenciesScope(HostDependencies previous) : IDisposable
	{
		private bool _disposed;

		public void Dispose()
		{
			if (_disposed) return;
			_disposed = true;
			lock (ConfigGate)
			{
				_host.ReplaceDependencies(previous);
			}
		}
	}

	/// <summary>当前宿主依赖（测试用来在既有基础上做变形）。</summary>
	internal static HostDependencies CurrentHostDependenciesForTest => _host.Dependencies;

	private static AppConfig LoadConfig()
	{
		lock (ConfigGate)
		{
			AppConfig config = _host.Dependencies.Store.Load();
			MirrorHelper.SelectedKey = string.IsNullOrWhiteSpace(config.DownloadSource) ? "official" : config.DownloadSource;
			return config;
		}
	}

	private static void SaveConfig(AppConfig config)
	{
		lock (ConfigGate)
		{
			MirrorHelper.SelectedKey = string.IsNullOrWhiteSpace(config.DownloadSource) ? "official" : config.DownloadSource;
			// 代理变化在此生效（候选 ⑤）：一次整组替换，不再有"换字段 + 延迟销毁"的窗口。
			_host.Dependencies.Http.ApplyProxy(OutboundHttp.ProxySettings.From(config));
			_host.Dependencies.Store.Save(config);
		}
	}

	/// <summary>只读请求的作用域：解析配置与（可选）实例，不执行任何写策略。</summary>
	private static RequestScope BeginReadScope(string? instanceId = null)
	{
		AppConfig config = LoadConfig();
		GameInstance? instance = instanceId == null ? null : RequireInstance(config, instanceId);
		return RequestScope.Read(config, instance, instanceId ?? string.Empty, ResolveGameDirectory(config, instance));
	}

	/// <summary>写请求的作用域：构造即执行两条写策略（二分定位禁区 + 并发启动守卫）。</summary>
	private static RequestScope BeginWriteScope(string instanceId, ScopeWording? wording = null, bool requireNoBisect = false)
	{
		RequestScope scope = BeginReadScope(instanceId);
		AppConfig config = scope.Config;
		GameInstance instance = scope.Instance!;
		string gameDir = scope.GameDirectory;
		return RequestScope.Write(config, instance, gameDir, wording, requireNoBisect);
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

	private static async Task HandleUpdateConfigAsync(HttpListenerRequest request, HttpListenerResponse response)
	{
		JsonDocument body = await ReadBodyAsync(request);
		JsonElement root = body.RootElement;
		AppConfig config = LoadConfig();
		PatchConfig(config, root);
		SaveConfig(config);
		await WriteJsonAsync(response, 200, new { ok = true });
	}

	/// <summary>
	/// D3②：实例级设置写入。目前只承载 `autoBackupOnExit`；配置是**按实例**存的，
	/// 不像 `autoCloseOnExit` 那样是全局开关，所以单开一个端点而不是塞进 `POST /api/config`。
	/// </summary>
	private static async Task HandleUpdateInstanceSettingsAsync(HttpListenerRequest request, HttpListenerResponse response, string instanceId)
	{
		using JsonDocument body = await ReadBodyAsync(request);
		JsonElement root = body.RootElement;
		bool value;
		lock (ConfigGate)
		{
		RequestScope scope = BeginReadScope(instanceId);
		AppConfig config = scope.Config;
		GameInstance instance = scope.Instance!;
			if (root.TryGetProperty("autoBackupOnExit", out JsonElement flag)
				&& (flag.ValueKind == JsonValueKind.True || flag.ValueKind == JsonValueKind.False))
			{
				instance.AutoBackupOnExit = flag.GetBoolean();
			}
			value = instance.AutoBackupOnExit;
			SaveConfig(config);
		}
		await WriteJsonAsync(response, 200, new { ok = true, instanceId, autoBackupOnExit = value });
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

		if (root.TryGetProperty("aiDiagnosisEnabled", out JsonElement aiEnabled) && aiEnabled.ValueKind == JsonValueKind.True)
		{
			config.AiDiagnosisEnabled = true;
		}
		else if (root.TryGetProperty("aiDiagnosisEnabled", out aiEnabled) && aiEnabled.ValueKind == JsonValueKind.False)
		{
			config.AiDiagnosisEnabled = false;
		}

		string selectedAiProviderId = JsonUtil.GetString(root, "selectedAiProviderId");
		if (selectedAiProviderId != null) config.SelectedAiProviderId = selectedAiProviderId;

		if (root.TryGetProperty("aiProviders", out JsonElement aiProvidersArr) && aiProvidersArr.ValueKind == JsonValueKind.Array)
		{
			PatchAiProviders(config, aiProvidersArr);
		}

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

		// 代理设置
		string proxyHost = JsonUtil.GetString(root, "proxyHost");
		if (proxyHost != null) config.ProxyHost = proxyHost;

		string proxyUsername = JsonUtil.GetString(root, "proxyUsername");
		if (proxyUsername != null) config.ProxyUsername = proxyUsername;

		string proxyPassword = JsonUtil.GetString(root, "proxyPassword");
		if (!string.IsNullOrEmpty(proxyPassword)) config.ProxyPassword = proxyPassword;

		if (root.TryGetProperty("proxyPort", out JsonElement proxyPort) && proxyPort.ValueKind == JsonValueKind.Number)
		{
			config.ProxyPort = proxyPort.GetInt32();
		}

		if (root.TryGetProperty("proxyEnabled", out JsonElement proxyEnabled) && proxyEnabled.ValueKind == JsonValueKind.True)
		{
			config.ProxyEnabled = true;
		}
		else if (root.TryGetProperty("proxyEnabled", out proxyEnabled) && proxyEnabled.ValueKind == JsonValueKind.False)
		{
			config.ProxyEnabled = false;
		}

		// 快捷操作
		if (root.TryGetProperty("minimizeOnLaunch", out JsonElement minimizeOnLaunch) && minimizeOnLaunch.ValueKind == JsonValueKind.True)
		{
			config.MinimizeOnLaunch = true;
		}
		else if (root.TryGetProperty("minimizeOnLaunch", out minimizeOnLaunch) && minimizeOnLaunch.ValueKind == JsonValueKind.False)
		{
			config.MinimizeOnLaunch = false;
		}

		if (root.TryGetProperty("autoCloseOnExit", out JsonElement autoCloseOnExit) && autoCloseOnExit.ValueKind == JsonValueKind.True)
		{
			config.AutoCloseOnExit = true;
		}
		else if (root.TryGetProperty("autoCloseOnExit", out autoCloseOnExit) && autoCloseOnExit.ValueKind == JsonValueKind.False)
		{
			config.AutoCloseOnExit = false;
		}

		// R2-02：新闻图片 fake-ip 兼容开关。严格 bool 语义：
		//   - 字段为 true  → 开启
		//   - 字段为 false → 关闭
		//   - 字段缺失/字符串/数字/null → 不改变旧值（避免前端/旧数据误触发开启）
		if (root.TryGetProperty("newsFakeIpCompat", out JsonElement newsFakeIp) && newsFakeIp.ValueKind == JsonValueKind.True)
		{
			config.NewsFakeIpCompat = true;
		}
		else if (root.TryGetProperty("newsFakeIpCompat", out newsFakeIp) && newsFakeIp.ValueKind == JsonValueKind.False)
		{
			config.NewsFakeIpCompat = false;
		}
	}

	/// <summary>
	/// 整体替换 AI 提供商列表并按 id 合并密钥：
	/// 请求数组顺序即最终顺序；未带 apiKey 的项保留 SecretStore 已有值；请求中缺失的旧 id 视为删除并清理密钥。
	/// </summary>
	private static void PatchAiProviders(AppConfig config, JsonElement arr)
	{
		HashSet<string> incoming = new HashSet<string>();
		List<AiProviderProfile> next = new List<AiProviderProfile>();
		Dictionary<string, AiProviderProfile> existing = config.AiProviders.ToDictionary(p => p.Id, StringComparer.Ordinal);

		foreach (JsonElement item in arr.EnumerateArray())
		{
			string id = JsonUtil.GetString(item, "id");
			if (string.IsNullOrWhiteSpace(id))
			{
				id = Guid.NewGuid().ToString("N");
			}
			incoming.Add(id);

			string name = JsonUtil.GetString(item, "name");
			string baseUrl = JsonUtil.GetString(item, "baseUrl");
			string model = JsonUtil.GetString(item, "model");
			string apiKey = JsonUtil.GetString(item, "apiKey");

			AiProviderProfile profile;
			if (existing.TryGetValue(id, out AiProviderProfile found))
			{
				profile = found;
			}
			else
			{
				profile = new AiProviderProfile { Id = id };
			}
			if (name != null) profile.Name = name;
			if (baseUrl != null) profile.BaseUrl = baseUrl;
			if (model != null) profile.Model = model;
			if (apiKey != null)
			{
				profile.ApiKey = apiKey;
				SecretStore.Save("AiKey_" + id, apiKey);
			}
			next.Add(profile);
		}

		// 处理被删除的旧项：清理密钥
		foreach (AiProviderProfile old in config.AiProviders)
		{
			if (!incoming.Contains(old.Id))
			{
				SecretStore.Save("AiKey_" + old.Id, string.Empty);
			}
		}

		config.AiProviders = next;

		// 若当前选中项被删除，则置空
		if (!string.IsNullOrEmpty(config.SelectedAiProviderId) && !incoming.Contains(config.SelectedAiProviderId))
		{
			config.SelectedAiProviderId = string.Empty;
		}

		// 无选中项时自动选中第一个提供商，保证新增首个提供商后即为当前
		if (string.IsNullOrEmpty(config.SelectedAiProviderId) && next.Count > 0)
		{
			config.SelectedAiProviderId = next[0].Id;
		}
	}
}
