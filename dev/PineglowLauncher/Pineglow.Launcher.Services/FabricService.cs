using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Pineglow.Launcher.Models;

namespace Pineglow.Launcher.Services;

public sealed class FabricService
{
	private const string BaseUrl = "https://meta.fabricmc.net/v2/versions";

	private readonly HttpClient _http;

	private readonly LoaderInstallHelper _helper;

	private readonly Action<string> _log;

	public FabricService(HttpClient http, string gameDirectory, MinecraftDownloader downloader, Action<string> log)
	{
		_http = http;
		_helper = new LoaderInstallHelper(http, gameDirectory, downloader, log);
		_log = log;
	}

	public async Task<List<FabricGameVersion>> GetSupportedGameVersionsAsync(CancellationToken ct = default(CancellationToken))
	{
		string text = "https://meta.fabricmc.net/v2/versions/game";
		_log("开始加载 Fabric 游戏版本：" + text);
		return await GetJsonListAsync<FabricGameVersion>(text, "Fabric 游戏版本列表", ct);
	}

	public async Task<List<LoaderGameVersion>> GetLoaderGameVersionsAsync()
	{
		return (await GetSupportedGameVersionsAsync()).Select((FabricGameVersion v) => new LoaderGameVersion
		{
			Version = v.Version,
			Stable = v.Stable
		}).ToList();
	}

	public async Task<List<FabricLoaderVersion>> GetLoaderVersionsAsync()
	{
		string text = "https://meta.fabricmc.net/v2/versions/loader";
		_log("获取 Fabric Loader 列表：" + text);
		return await GetJsonListAsync<FabricLoaderVersion>(text, "Fabric Loader 列表");
	}

	public async Task<List<FabricCompatibleLoaderVersion>> GetCompatibleLoadersAsync(string minecraftVersion)
	{
		if (string.IsNullOrWhiteSpace(minecraftVersion))
		{
			throw new InvalidOperationException("请先选择 Minecraft 版本。");
		}
		string text = Uri.EscapeDataString(minecraftVersion.Trim());
		string text2 = "https://meta.fabricmc.net/v2/versions/loader/" + text;
		_log("获取 Fabric Loader 列表：" + text2);
		return await GetJsonListAsync<FabricCompatibleLoaderVersion>(text2, "Minecraft " + minecraftVersion + " 可用 Fabric Loader");
	}

	public async Task<List<LoaderVersionEntry>> GetLoaderVersionEntriesAsync(string minecraftVersion)
	{
		return (await GetCompatibleLoadersAsync(minecraftVersion)).Select((FabricCompatibleLoaderVersion x) => new LoaderVersionEntry
		{
			Version = x.Version,
			Stable = x.Stable
		}).ToList();
	}

	public async Task<JsonDocument> GetFabricProfileJsonAsync(string minecraftVersion, string loaderVersion, CancellationToken ct = default(CancellationToken))
	{
		if (string.IsNullOrWhiteSpace(minecraftVersion))
		{
			throw new InvalidOperationException("Minecraft 版本不能为空。");
		}
		if (string.IsNullOrWhiteSpace(loaderVersion))
		{
			throw new InvalidOperationException("Fabric Loader 版本不能为空。");
		}
		string value = Uri.EscapeDataString(minecraftVersion.Trim());
		string value2 = Uri.EscapeDataString(loaderVersion.Trim());
		string text = $"{"https://meta.fabricmc.net/v2/versions"}/loader/{value}/{value2}/profile/json";
		_log("开始获取 Fabric profile JSON：" + text);
		try
		{
			using HttpResponseMessage response = await _http.GetAsync(text, ct);
			if (!response.IsSuccessStatusCode)
			{
				throw new InvalidOperationException($"Fabric Meta API 返回 {(int)response.StatusCode}，请稍后重试或检查版本是否兼容。");
			}
			JsonDocument result;
			await using (Stream stream = await response.Content.ReadAsStreamAsync(ct))
			{
				JsonDocument jsonDocument = await JsonDocument.ParseAsync(stream, default(JsonDocumentOptions), ct);
				if (jsonDocument.RootElement.ValueKind != JsonValueKind.Object)
				{
					jsonDocument.Dispose();
					throw new InvalidOperationException("Fabric profile JSON 格式异常，请稍后重试。");
				}
				result = jsonDocument;
			}
			return result;
		}
		catch (JsonException ex)
		{
			throw new InvalidOperationException("Fabric profile JSON 解析失败，请稍后重试。" + ex.Message, ex);
		}
		catch (HttpRequestException ex2)
		{
			throw new InvalidOperationException("无法连接 Fabric Meta API，请检查网络连接。" + ex2.Message, ex2);
		}
		catch (TaskCanceledException innerException)
		{
			throw new InvalidOperationException("请求 Fabric Meta API 超时，请检查网络后重试。", innerException);
		}
	}

	public async Task<string> InstallFabricAsync(GameInstance instance, string minecraftVersion, string loaderVersion, IProgress<string> logProgress)
	{
		return (await InstallFabricProfileAsync(instance, minecraftVersion, loaderVersion, logProgress)).VersionId;
	}

	public async Task<InstalledLoaderProfile> InstallFabricProfileAsync(GameInstance instance, string minecraftVersion, string loaderVersion, IProgress<string> logProgress)
	{
		if (instance == null)
		{
			throw new InvalidOperationException("当前没有选中实例，无法安装 Fabric。");
		}
		if (string.IsNullOrWhiteSpace(minecraftVersion))
		{
			throw new InvalidOperationException("请选择 Minecraft 版本。");
		}
		if (string.IsNullOrWhiteSpace(loaderVersion))
		{
			throw new InvalidOperationException("请选择 Fabric Loader 版本。");
		}
		Progress("当前选择的 Minecraft 版本：" + minecraftVersion);
		Progress("当前选择的 Loader 版本：" + loaderVersion);
		await _helper.EnsureVanillaInstalledAsync(minecraftVersion, Progress);
		using (JsonDocument profileDoc = await GetFabricProfileJsonAsync(minecraftVersion, loaderVersion))
		{
			JsonElement profileJson = profileDoc.RootElement;
			string fabricVersionId = JsonUtil.GetString(profileJson, "id") ?? ("fabric-loader-" + loaderVersion + "-" + minecraftVersion);
			if (string.IsNullOrWhiteSpace(fabricVersionId))
			{
				throw new InvalidOperationException("Fabric profile JSON 缺少 id，无法确定安装目录。");
			}
			Progress("写入 Fabric version JSON...");
			await _helper.WriteVersionJsonAsync(fabricVersionId, profileJson);
			Progress("开始下载 Fabric libraries...");
			await _helper.DownloadLibrariesAsync(profileJson, "Fabric");
			Progress("Fabric libraries 下载完成");
			return new InstalledLoaderProfile
			{
				VersionId = fabricVersionId,
				MinecraftVersion = minecraftVersion,
				LoaderKind = ModLoaderKind.Fabric,
				LoaderVersion = loaderVersion
			};
		}
		void Progress(string message)
		{
			_log(message);
			logProgress.Report(message);
		}
	}

	private async Task<List<T>> GetJsonListAsync<T>(string url, string name, CancellationToken ct = default(CancellationToken))
	{
		_ = 1;
		try
		{
			using HttpResponseMessage response = await _http.GetAsync(url, ct);
			if (!response.IsSuccessStatusCode)
			{
				throw new InvalidOperationException($"{name}请求失败：HTTP {(int)response.StatusCode}。请检查网络或稍后重试。");
			}
			return (await response.Content.ReadFromJsonAsync<List<T>>(JsonUtil.Options, ct)) ?? new List<T>();
		}
		catch (JsonException ex)
		{
			throw new InvalidOperationException(name + "解析失败，请稍后重试。" + ex.Message, ex);
		}
		catch (HttpRequestException ex2)
		{
			throw new InvalidOperationException("无法连接 Fabric Meta API 获取" + name + "，请检查网络连接。" + ex2.Message, ex2);
		}
		catch (TaskCanceledException innerException)
		{
			throw new InvalidOperationException("获取" + name + "超时，请检查网络后重试。", innerException);
		}
	}
}
