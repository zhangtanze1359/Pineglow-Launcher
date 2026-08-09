using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using Pineglow.Launcher.Models;

namespace Pineglow.Launcher.Services;

public sealed class OptiFineService
{
	private readonly LoaderInstallHelper _helper;

	private readonly Action<string> _log;

	public OptiFineService(HttpClient http, string gameDirectory, MinecraftDownloader downloader, Action<string> log)
	{
		_helper = new LoaderInstallHelper(http, gameDirectory, downloader, log);
		_log = log;
	}

	public Task<List<LoaderGameVersion>> GetLoaderGameVersionsAsync()
	{
		return Task.FromResult(new List<LoaderGameVersion>
		{
			new LoaderGameVersion
			{
				Version = "请选择在线原版版本后安装",
				Stable = false
			}
		});
	}

	public Task<List<LoaderVersionEntry>> GetLoaderVersionEntriesAsync(string minecraftVersion)
	{
		if (string.IsNullOrWhiteSpace(minecraftVersion))
		{
			throw new InvalidOperationException("请先选择 Minecraft 版本。");
		}
		return Task.FromResult(new List<LoaderVersionEntry>
		{
			new LoaderVersionEntry
			{
				Version = "本地 OptiFine installer jar",
				Stable = true
			}
		});
	}

	public async Task<InstalledLoaderProfile> InstallProfileAsync(GameInstance instance, string minecraftVersion, string installerPath, string javaPath, IProgress<string> logProgress)
	{
		if (instance == null)
		{
			throw new InvalidOperationException("当前没有选中实例，无法安装 OptiFine。");
		}
		if (string.IsNullOrWhiteSpace(minecraftVersion))
		{
			throw new InvalidOperationException("请选择 Minecraft 版本。");
		}
		if (string.IsNullOrWhiteSpace(installerPath) || !File.Exists(installerPath))
		{
			throw new InvalidOperationException("请选择本地 OptiFine installer jar。");
		}
		await _helper.EnsureVanillaInstalledAsync(minecraftVersion, Progress);
		DateTime beforeInstall = DateTime.UtcNow.AddSeconds(-2.0);
		await _helper.RunInstallerAsync(javaPath, installerPath, "OptiFine");
		string text = _helper.FindNewestVersionId((string id) => id.Contains("OptiFine", StringComparison.OrdinalIgnoreCase) && id.Contains(minecraftVersion, StringComparison.OrdinalIgnoreCase), beforeInstall);
		if (string.IsNullOrWhiteSpace(text))
		{
			throw new InvalidOperationException("OptiFine installer 已运行，但未找到生成的 OptiFine 版本。请确认 installer 与 Minecraft 版本匹配。");
		}
		return new InstalledLoaderProfile
		{
			VersionId = text,
			MinecraftVersion = minecraftVersion,
			LoaderKind = ModLoaderKind.OptiFine,
			LoaderVersion = Path.GetFileNameWithoutExtension(installerPath)
		};
		void Progress(string message)
		{
			_log(message);
			logProgress.Report(message);
		}
	}
}
