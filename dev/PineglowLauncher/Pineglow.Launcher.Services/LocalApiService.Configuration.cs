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

	// MR04：恢复区根目录。生产默认在配置目录下的 recovery/（位于游戏目录之外，
	// 游戏目录被删也不会丢隔离副本）；测试用 OverrideRecoveryRootForTest 注入临时目录，
	// 绝不触碰真实 %APPDATA%。scope 释放时自动还原。
	private static string? _recoveryRootOverride;

	private static string ResolveRecoveryRoot() =>
		_recoveryRootOverride ?? Path.Combine(PlatformPaths.GetConfigDirectory(), "recovery");

	private static FileRecoveryService CreateRecoveryService() => new FileRecoveryService(ResolveRecoveryRoot());

	internal static IDisposable OverrideRecoveryRootForTest(string root)
	{
		if (string.IsNullOrWhiteSpace(root)) throw new ArgumentNullException(nameof(root));
		string previous = _recoveryRootOverride;
		_recoveryRootOverride = root;
		return new RecoveryRootOverrideScope(previous);
	}

	private sealed class RecoveryRootOverrideScope : IDisposable
	{
		private readonly string? _previous;
		private bool _disposed;

		public RecoveryRootOverrideScope(string? previous) => _previous = previous;

		public void Dispose()
		{
			if (_disposed) return;
			_disposed = true;
			_recoveryRootOverride = _previous;
		}
	}

	// MR08：诊断报告历史的根目录（本地，游戏目录之外）。测试可重定向。
	private static string? _diagnosticStoreRootOverride;

	private static string ResolveDiagnosticStoreRoot() =>
		_diagnosticStoreRootOverride ?? Path.Combine(PlatformPaths.GetConfigDirectory(), "diagnostics");

	private static DiagnosticReportService CreateDiagnosticStore() => new DiagnosticReportService(ResolveDiagnosticStoreRoot());

	internal static IDisposable OverrideDiagnosticStoreRootForTest(string root)
	{
		if (string.IsNullOrWhiteSpace(root)) throw new ArgumentNullException(nameof(root));
		string previous = _diagnosticStoreRootOverride;
		_diagnosticStoreRootOverride = root;
		return new DiagnosticStoreOverrideScope(previous);
	}

	private sealed class DiagnosticStoreOverrideScope : IDisposable
	{
		private readonly string? _previous;
		private bool _disposed;

		public DiagnosticStoreOverrideScope(string? previous) => _previous = previous;

		public void Dispose()
		{
			if (_disposed) return;
			_disposed = true;
			_diagnosticStoreRootOverride = _previous;
		}
	}

	// MR07：二分定位会话的落盘根（游戏目录之外）。测试同样可重定向。
	private static string? _bisectStoreRootOverride;

	private static string ResolveBisectStoreRoot() =>
		_bisectStoreRootOverride ?? Path.Combine(PlatformPaths.GetConfigDirectory(), "bisect");

	private static BisectRecoveryStore CreateBisectStore() => new BisectRecoveryStore(ResolveBisectStoreRoot());

	internal static IDisposable OverrideBisectStoreRootForTest(string root)
	{
		if (string.IsNullOrWhiteSpace(root)) throw new ArgumentNullException(nameof(root));
		string previous = _bisectStoreRootOverride;
		_bisectStoreRootOverride = root;
		return new BisectStoreOverrideScope(previous);
	}

	private sealed class BisectStoreOverrideScope : IDisposable
	{
		private readonly string? _previous;
		private bool _disposed;

		public BisectStoreOverrideScope(string? previous) => _previous = previous;

		public void Dispose()
		{
			if (_disposed) return;
			_disposed = true;
			_bisectStoreRootOverride = _previous;
		}
	}

	// R2-02 返工（review-fix-1）：最小 internal IConfigStore 测试注入缝。
	// 生产默认仍走 ConfigStore（不改变生产配置路径、不触发 SecretStore/.bak/.prev）。
	// 仅测试可调用 OverrideConfigStoreForTest；scope 释放时自动还原。
	internal interface IConfigStore
	{
		AppConfig Load();
		void Save(AppConfig config);
	}

	private sealed class ProductionConfigStore : IConfigStore
	{
		public AppConfig Load() => ConfigStore.LoadAsync().GetAwaiter().GetResult();
		public void Save(AppConfig config) => ConfigStore.SaveAsync(config).GetAwaiter().GetResult();
	}

	private static IConfigStore _configStoreOverride;

	private static readonly IConfigStore _defaultConfigStore = new ProductionConfigStore();

	internal static IDisposable OverrideConfigStoreForTest(IConfigStore store)
	{
		if (store == null) throw new ArgumentNullException(nameof(store));
		lock (ConfigGate)
		{
			_configStoreOverride = store;
		}
		return new ConfigStoreOverrideScope();
	}

	private sealed class ConfigStoreOverrideScope : IDisposable
	{
		private bool _disposed;
		public void Dispose()
		{
			if (_disposed) return;
			_disposed = true;
			lock (ConfigGate)
			{
				_configStoreOverride = null;
			}
		}
	}

	private static AppConfig LoadConfig()
	{
		lock (ConfigGate)
		{
			IConfigStore store = _configStoreOverride ?? _defaultConfigStore;
			AppConfig config = store.Load();
			MirrorHelper.SelectedKey = string.IsNullOrWhiteSpace(config.DownloadSource) ? "official" : config.DownloadSource;
			return config;
		}
	}

	private static void SaveConfig(AppConfig config)
	{
		lock (ConfigGate)
		{
			MirrorHelper.SelectedKey = string.IsNullOrWhiteSpace(config.DownloadSource) ? "official" : config.DownloadSource;
			RefreshApiHttpProxy(config);
			IConfigStore store = _configStoreOverride ?? _defaultConfigStore;
			store.Save(config);
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
