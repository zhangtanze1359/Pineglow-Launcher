using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Pineglow.Launcher.Services;

public sealed class LoaderInstallHelper
{
	public readonly record struct LoaderArtifact(string Path, string Url, string? Sha1);

	private readonly HttpClient _http;

	private readonly string _gameDirectory;

	private readonly MinecraftDownloader _downloader;

	private readonly Action<string> _log;

	public string GameDirectory => _gameDirectory;

	public LoaderInstallHelper(HttpClient http, string gameDirectory, MinecraftDownloader downloader, Action<string> log)
	{
		_http = http;
		_gameDirectory = (string.IsNullOrWhiteSpace(gameDirectory) ? PlatformPaths.GetDefaultMinecraftDirectory() : gameDirectory);
		_downloader = downloader;
		_log = log;
	}

	public async Task EnsureVanillaInstalledAsync(string minecraftVersion, Action<string> progress)
	{
		if (string.IsNullOrWhiteSpace(_gameDirectory))
		{
			throw new InvalidOperationException("当前实例的游戏目录为空，请先在设置页选择 .minecraft 目录。");
		}
		if (string.IsNullOrWhiteSpace(minecraftVersion))
		{
			throw new InvalidOperationException("请选择 Minecraft 版本。");
		}
		Directory.CreateDirectory(Path.Combine(_gameDirectory, "versions"));
		Directory.CreateDirectory(Path.Combine(_gameDirectory, "libraries"));
		progress("检查原版依赖版本...");
		if (!File.Exists(VersionResolver.GetVersionJsonPath(_gameDirectory, minecraftVersion)))
		{
			progress("原版版本 " + minecraftVersion + " 未安装，开始安装原版版本...");
			await _downloader.InstallVersionAsync(_gameDirectory, minecraftVersion);
		}
		else
		{
			progress("原版版本已存在：" + minecraftVersion);
		}
	}

	public async Task WriteVersionJsonAsync(string versionId, JsonElement profile)
	{
		if (versionId.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || versionId.Contains('/') || versionId.Contains('\\'))
		{
			throw new InvalidOperationException("加载器 profile id 包含非法路径字符：" + versionId);
		}
		string text = Path.Combine(_gameDirectory, "versions", versionId);
		Directory.CreateDirectory(text);
		await File.WriteAllTextAsync(Path.Combine(text, versionId + ".json"), JsonSerializer.Serialize(profile, JsonUtil.Options));
	}

	public async Task DownloadLibrariesAsync(JsonElement profile, string label)
	{
		if (!profile.TryGetProperty("libraries", out var value) || value.ValueKind != JsonValueKind.Array)
		{
			_log(label + " profile JSON 没有 libraries 字段，跳过 libraries 下载。");
			return;
		}
		List<JsonElement> list = value.EnumerateArray().ToList();
		List<(LoaderArtifact, int)> list2 = new List<(LoaderArtifact, int)>();
		for (int i = 0; i < list.Count; i++)
		{
			JsonElement jsonElement = list[i];
			if (RulesEvaluator.IsAllowed(jsonElement))
			{
				LoaderArtifact? loaderArtifact = ResolveLibraryArtifact(jsonElement);
				if (!loaderArtifact.HasValue)
				{
					_log($"{label} 库 {i + 1}/{list.Count} 缺少下载信息，已跳过。");
				}
				else
				{
					list2.Add((loaderArtifact.Value, i));
				}
			}
		}
		int completed = 0;
		int total = list2.Count;
		SemaphoreSlim semaphore = new SemaphoreSlim(12);
		await Parallel.ForEachAsync<(LoaderArtifact, int)>(list2, async delegate((LoaderArtifact Artifact, int Index) item, CancellationToken ct)
		{
			await semaphore.WaitAsync(ct);
			try
			{
				int value2 = Interlocked.Increment(ref completed);
				string outputPath = Path.Combine(_gameDirectory, "libraries", item.Artifact.Path.Replace('/', Path.DirectorySeparatorChar));
				await DownloadFileAsync(item.Artifact.Url, outputPath, item.Artifact.Sha1, $"{label} 库 {value2}/{total}");
			}
			finally
			{
				semaphore.Release();
			}
		});
	}

	public async Task<string> DownloadInstallerAsync(string url, string fileName, string label)
	{
		string text = Path.Combine(_gameDirectory, "installers");
		Directory.CreateDirectory(text);
		string path = Path.Combine(text, fileName);
		await DownloadFileAsync(url, path, null, label);
		return path;
	}

	public static void CleanupInstaller(string installerPath)
	{
		try
		{
			if (File.Exists(installerPath))
			{
				File.Delete(installerPath);
			}
		}
		catch
		{
		}
	}

	public async Task RunInstallerAsync(string javaPath, string installerPath, string label)
	{
		if (string.IsNullOrWhiteSpace(javaPath) || !File.Exists(javaPath))
		{
			throw new InvalidOperationException("安装 " + label + " 需要可用的 Java。请先在设置页配置 Java 路径。");
		}
		ProcessStartInfo processStartInfo = new ProcessStartInfo
		{
			FileName = javaPath,
			WorkingDirectory = _gameDirectory,
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			UseShellExecute = false,
			CreateNoWindow = true
		};
		processStartInfo.ArgumentList.Add("-jar");
		processStartInfo.ArgumentList.Add(installerPath);
		processStartInfo.ArgumentList.Add("--installClient");
		processStartInfo.ArgumentList.Add(_gameDirectory);
		_log("运行 " + label + " installer...");
		using Process process = Process.Start(processStartInfo) ?? throw new InvalidOperationException(label + " installer 启动失败。");
		Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync();
		Task<string> stderrTask = process.StandardError.ReadToEndAsync();
		await process.WaitForExitAsync();
		string stdout = await stdoutTask;
		string text = await stderrTask;
		string[] array = stdout.Split(new char[2] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
		foreach (string obj in array)
		{
			_log(obj);
		}
		array = text.Split(new char[2] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
		foreach (string obj2 in array)
		{
			_log(obj2);
		}
		if (process.ExitCode != 0)
		{
			throw new InvalidOperationException($"{label} installer 失败，ExitCode={process.ExitCode}。");
		}
	}

	public string? FindNewestVersionId(Func<string, bool> predicate, DateTime beforeInstallUtc)
	{
		string path = Path.Combine(_gameDirectory, "versions");
		if (!Directory.Exists(path))
		{
			return null;
		}
		return (from dir in Directory.EnumerateDirectories(path)
			select new
			{
				Dir = dir,
				Id = Path.GetFileName(dir),
				Json = Path.Combine(dir, Path.GetFileName(dir) + ".json")
			} into x
			where !string.IsNullOrWhiteSpace(x.Id) && File.Exists(x.Json) && predicate(x.Id)
			orderby File.GetLastWriteTimeUtc(x.Json) >= beforeInstallUtc descending, File.GetLastWriteTimeUtc(x.Json) descending
			select x.Id).FirstOrDefault();
	}

	public async Task<JsonElement?> ReadInstallerJsonAsync(string installerPath, params string[] names)
	{
		using ZipArchive archive = ZipFile.OpenRead(installerPath);
		foreach (string entryName in names)
		{
			ZipArchiveEntry entry = archive.GetEntry(entryName);
			if (entry == null)
			{
				continue;
			}
			JsonElement? result;
			await using (Stream stream = entry.Open())
			{
				using JsonDocument jsonDocument = await JsonDocument.ParseAsync(stream);
				result = jsonDocument.RootElement.Clone();
			}
			return result;
		}
		return null;
	}

	public static LoaderArtifact? ResolveLibraryArtifact(JsonElement library)
	{
		if (library.TryGetProperty("downloads", out var value) && value.ValueKind == JsonValueKind.Object)
		{
			if (value.TryGetProperty("artifact", out var value2))
			{
				string text = JsonUtil.GetString(value2, "path");
				string text2 = JsonUtil.GetString(value2, "url");
				string sha = JsonUtil.GetString(value2, "sha1");
				if (!string.IsNullOrWhiteSpace(text) && !string.IsNullOrWhiteSpace(text2))
				{
					return new LoaderArtifact(text, text2, sha);
				}
			}
			if (value.TryGetProperty("classifiers", out var value3) && value3.ValueKind == JsonValueKind.Object)
			{
				string text3 = SelectNativeClassifier(library);
				if (!string.IsNullOrWhiteSpace(text3) && value3.TryGetProperty(text3, out var value4))
				{
					string text4 = JsonUtil.GetString(value4, "path");
					string text5 = JsonUtil.GetString(value4, "url");
					string sha2 = JsonUtil.GetString(value4, "sha1");
					if (!string.IsNullOrWhiteSpace(text4) && !string.IsNullOrWhiteSpace(text5))
					{
						return new LoaderArtifact(text4, text5, sha2);
					}
				}
			}
		}
		string text6 = JsonUtil.GetString(library, "name");
		string text7 = JsonUtil.GetString(library, "url");
		if (string.IsNullOrWhiteSpace(text6) || string.IsNullOrWhiteSpace(text7))
		{
			return null;
		}
		string text8 = MinecraftUtils.MavenPath(text6).Replace(Path.DirectorySeparatorChar, '/');
		string url = text7.TrimEnd('/') + "/" + text8;
		return new LoaderArtifact(text8, url, null);
	}

	public static string MavenPath(string name)
	{
		return MinecraftUtils.MavenPath(name);
	}

	private static string? SelectNativeClassifier(JsonElement library)
	{
		return MinecraftUtils.SelectNativeClassifier(library);
	}

	public async Task DownloadFileAsync(string url, string outputPath, string? expectedSha1, string label)
	{
		if (File.Exists(outputPath) && (string.IsNullOrWhiteSpace(expectedSha1) || Sha1Matches(outputPath, expectedSha1)))
		{
			return;
		}
		url = MirrorHelper.Rewrite(url);
		Directory.CreateDirectory(Path.GetDirectoryName(outputPath));
		string temp = outputPath + ".download";
		bool flag = default(bool);
		for (int attempt = 1; attempt <= 3; attempt++)
		{
			try
			{
				_log(label + ": " + Path.GetFileName(outputPath));
				using HttpResponseMessage response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
				if (!response.IsSuccessStatusCode)
				{
					throw new InvalidOperationException($"下载失败 HTTP {(int)response.StatusCode}：{Path.GetFileName(outputPath)}");
				}
				await using (Stream input = await response.Content.ReadAsStreamAsync())
				{
					await using FileStream output = File.Create(temp);
					await input.CopyToAsync(output);
				}
				if (!string.IsNullOrWhiteSpace(expectedSha1) && !Sha1Matches(temp, expectedSha1))
				{
					File.Delete(temp);
					throw new InvalidOperationException("SHA1 校验失败：" + Path.GetFileName(outputPath));
				}
				if (File.Exists(outputPath))
				{
					File.Delete(outputPath);
				}
				File.Move(temp, outputPath);
				break;
			}
			catch (Exception ex) when (((Func<bool>)delegate
			{
				// Could not convert BlockContainer to single expression
				flag = attempt < 3;
				if (flag)
				{
					flag = ((ex is IOException || ex is HttpRequestException) ? true : false);
				}
				return flag;
			}).Invoke())
			{
				if (File.Exists(temp))
				{
					File.Delete(temp);
				}
				TimeSpan delay = TimeSpan.FromSeconds(Math.Pow(2.0, attempt));
				_log($"{label} 下载失败，{delay.TotalSeconds}s 后重试 ({attempt}/{3})");
				await Task.Delay(delay);
			}
			catch (Exception ex2) when (((ex2 is IOException || ex2 is HttpRequestException || ex2 is InvalidOperationException) ? 1 : 0) != 0)
			{
				if (File.Exists(temp))
				{
					File.Delete(temp);
				}
				throw new InvalidOperationException(label + " 下载失败：" + ex2.Message + "。请检查网络后重试。", ex2);
			}
		}
	}

	private static bool Sha1Matches(string path, string sha1)
	{
		return MinecraftUtils.Sha1Matches(path, sha1);
	}
}
