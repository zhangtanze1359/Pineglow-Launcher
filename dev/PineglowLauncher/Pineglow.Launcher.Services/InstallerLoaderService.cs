using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;
using Pineglow.Launcher.Models;

namespace Pineglow.Launcher.Services;

public abstract class InstallerLoaderService
{
	private readonly string _metadataUrl;

	private readonly string _artifactBaseUrl;

	private readonly string _groupPath;

	private readonly string _artifactId;

	private readonly ModLoaderKind _kind;

	private readonly string _label;

	private readonly HttpClient _http;

	private readonly LoaderInstallHelper _helper;

	private readonly Action<string> _log;

	protected InstallerLoaderService(HttpClient http, string gameDirectory, MinecraftDownloader downloader, Action<string> log, string metadataUrl, string artifactBaseUrl, string groupPath, string artifactId, ModLoaderKind kind, string label)
	{
		_http = http;
		_helper = new LoaderInstallHelper(http, gameDirectory, downloader, log);
		_log = log;
		_metadataUrl = metadataUrl;
		_artifactBaseUrl = artifactBaseUrl.TrimEnd('/');
		_groupPath = groupPath.Trim('/');
		_artifactId = artifactId;
		_kind = kind;
		_label = label;
	}

	public async Task<List<LoaderGameVersion>> GetLoaderGameVersionsAsync()
	{
		return (from x in (from x in (await GetMavenVersionsAsync()).Select(GetMinecraftVersion).OfType<string>()
				where !string.IsNullOrWhiteSpace(x)
				select x).Distinct<string>(StringComparer.OrdinalIgnoreCase).OrderByDescending<string, string>((string x) => x, StringComparer.OrdinalIgnoreCase)
			select new LoaderGameVersion
			{
				Version = x,
				Stable = true
			}).ToList();
	}

	public async Task<List<LoaderVersionEntry>> GetLoaderVersionEntriesAsync(string minecraftVersion)
	{
		if (string.IsNullOrWhiteSpace(minecraftVersion))
		{
			throw new InvalidOperationException("请先选择 Minecraft 版本。");
		}
		return (from v in (await GetMavenVersionsAsync()).Where((string v) => string.Equals(GetMinecraftVersion(v), minecraftVersion, StringComparison.OrdinalIgnoreCase)).OrderByDescending<string, string>((string v) => v, StringComparer.OrdinalIgnoreCase)
			select new LoaderVersionEntry
			{
				Version = GetDisplayLoaderVersion(v, minecraftVersion),
				Stable = true,
				MavenVersion = v,
				DownloadUrl = GetInstallerUrl(v)
			}).ToList();
	}

	public async Task<InstalledLoaderProfile> InstallProfileAsync(GameInstance instance, string minecraftVersion, LoaderVersionEntry loader, string javaPath, IProgress<string> logProgress)
	{
		if (instance == null)
		{
			throw new InvalidOperationException("当前没有选中实例，无法安装 " + _label + "。");
		}
		if (string.IsNullOrWhiteSpace(minecraftVersion))
		{
			throw new InvalidOperationException("请选择 Minecraft 版本。");
		}
		string mavenVersion = loader.MavenVersion ?? loader.Version;
		if (string.IsNullOrWhiteSpace(mavenVersion))
		{
			throw new InvalidOperationException("请选择 " + _label + " 版本。");
		}
		string installerUrl = loader.DownloadUrl ?? GetInstallerUrl(mavenVersion);
		Progress("当前选择的 Minecraft 版本：" + minecraftVersion);
		Progress("当前选择的 " + _label + " 版本：" + loader.Version);
		await _helper.EnsureVanillaInstalledAsync(minecraftVersion, Progress);
		DateTime beforeInstall = DateTime.UtcNow.AddSeconds(-2.0);
		string text = string.Join("_", mavenVersion.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries));
		string installerPath = await _helper.DownloadInstallerAsync(installerUrl, _artifactId + "-" + text + "-installer.jar", _label + " installer");
		await _helper.RunInstallerAsync(javaPath, installerPath, _label);
		LoaderInstallHelper.CleanupInstaller(installerPath);
		string versionId = _helper.FindNewestVersionId((string id) => IsInstalledVersionId(id, minecraftVersion, mavenVersion), beforeInstall) ?? GuessVersionId(minecraftVersion, mavenVersion);
		string versionJsonPath = VersionResolver.GetVersionJsonPath(_helper.GameDirectory, versionId);
		if (!File.Exists(versionJsonPath))
		{
			throw new InvalidOperationException(_label + " installer 已运行，但未找到生成的版本 JSON：" + versionJsonPath);
		}
		return new InstalledLoaderProfile
		{
			VersionId = versionId,
			MinecraftVersion = minecraftVersion,
			LoaderKind = _kind,
			LoaderVersion = loader.Version
		};
		void Progress(string message)
		{
			_log(message);
			logProgress.Report(message);
		}
	}

	protected abstract string? GetMinecraftVersion(string mavenVersion);

	protected abstract string GetDisplayLoaderVersion(string mavenVersion, string minecraftVersion);

	protected abstract string GuessVersionId(string minecraftVersion, string mavenVersion);

	protected virtual bool IsInstalledVersionId(string id, string minecraftVersion, string mavenVersion)
	{
		if (id.Contains(minecraftVersion, StringComparison.OrdinalIgnoreCase))
		{
			if (!id.Contains(_label, StringComparison.OrdinalIgnoreCase) && !id.Contains(_artifactId, StringComparison.OrdinalIgnoreCase))
			{
				return id.Contains(mavenVersion, StringComparison.OrdinalIgnoreCase);
			}
			return true;
		}
		return false;
	}

	private async Task<List<string>> GetMavenVersionsAsync()
	{
		_log("获取 " + _label + " Maven metadata：" + _metadataUrl);
		try
		{
			using Stream stream = await _http.GetStreamAsync(_metadataUrl);
			return (from x in (await XDocument.LoadAsync(stream, LoadOptions.None, CancellationToken.None)).Descendants("version")
				select x.Value.Trim() into x
				where !string.IsNullOrWhiteSpace(x)
				select x).Distinct<string>(StringComparer.OrdinalIgnoreCase).ToList();
		}
		catch (Exception ex) when (((ex is HttpRequestException || ex is TaskCanceledException || ex is XmlException || ex is IOException) ? 1 : 0) != 0)
		{
			throw new InvalidOperationException("获取 " + _label + " 版本列表失败：" + ex.Message, ex);
		}
	}

	private string GetInstallerUrl(string mavenVersion)
	{
		return $"{_artifactBaseUrl}/{_groupPath}/{mavenVersion}/{_artifactId}-{mavenVersion}-installer.jar";
	}
}
