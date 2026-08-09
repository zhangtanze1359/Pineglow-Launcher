using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Pineglow.Launcher.Models;

namespace Pineglow.Launcher.Services;

public sealed class MinecraftDownloader
{
	private const string ManifestUrl = "https://piston-meta.mojang.com/mc/game/version_manifest_v2.json";

	private readonly HttpClient _http;

	private readonly Action<string> _log;

	private readonly Action<MinecraftDownloadProgress>? _progress;

	public MinecraftDownloader(HttpClient http, Action<string> log, Action<MinecraftDownloadProgress>? progress = null)
	{
		_http = http;
		_log = log;
		_progress = progress;
	}

	public async Task<List<VersionEntry>> FetchOnlineVersionsAsync(CancellationToken ct = default(CancellationToken))
	{
		Exception lastError = null;
		JsonDocument doc = null;
		foreach (string url in MirrorHelper.GetCandidates("https://piston-meta.mojang.com/mc/game/version_manifest_v2.json"))
		{
			try
			{
				using HttpResponseMessage response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
				response.EnsureSuccessStatusCode();
				await using Stream stream = await response.Content.ReadAsStreamAsync(ct);
				CancellationToken cancellationToken = ct;
				doc = await JsonDocument.ParseAsync(stream, default(JsonDocumentOptions), cancellationToken);
			}
			catch (OperationCanceledException) when (ct.IsCancellationRequested)
			{
				throw;
			}
			catch (Exception ex2) when (((ex2 is HttpRequestException || ex2 is TaskCanceledException || ex2 is IOException || ex2 is JsonException) ? 1 : 0) != 0)
			{
				lastError = ex2;
				_log("版本列表来源不可用，正在尝试备用下载源：" + new Uri(url).Host);
				continue;
			}
			break;
		}
		if (doc == null)
		{
			throw new HttpRequestException("无法连接 Minecraft 版本下载源，已自动尝试官方和镜像地址。请检查网络后重试。", lastError);
		}
		using (doc)
		{
			List<VersionEntry> list = new List<VersionEntry>();
			foreach (JsonElement item in doc.RootElement.GetProperty("versions").EnumerateArray())
			{
				list.Add(new VersionEntry
				{
					Id = (JsonUtil.GetString(item, "id") ?? string.Empty),
					Type = (JsonUtil.GetString(item, "type") ?? string.Empty),
					Url = (JsonUtil.GetString(item, "url") ?? string.Empty),
					ReleaseTime = (JsonUtil.GetString(item, "releaseTime") ?? string.Empty)
				});
			}
			return list.Where((VersionEntry v) => !string.IsNullOrWhiteSpace(v.Id)).ToList();
		}
	}

	public async Task InstallVersionAsync(string gameDirectory, string versionId, CancellationToken ct = default(CancellationToken))
	{
		Directory.CreateDirectory(gameDirectory);
		Directory.CreateDirectory(Path.Combine(gameDirectory, "versions"));
		Directory.CreateDirectory(Path.Combine(gameDirectory, "libraries"));
		Directory.CreateDirectory(Path.Combine(gameDirectory, "assets", "indexes"));
		Directory.CreateDirectory(Path.Combine(gameDirectory, "assets", "objects"));
		_log("开始安装版本 " + versionId);
		Report("正在获取 Minecraft " + versionId + " 元数据...", 0.01);
		VersionEntry selected = (await FetchOnlineVersionsAsync(ct)).FirstOrDefault((VersionEntry v) => v.Id.Equals(versionId, StringComparison.OrdinalIgnoreCase));
		if (selected == null)
		{
			throw new InvalidOperationException("在线版本清单中找不到 " + versionId);
		}
		Directory.CreateDirectory(Path.Combine(gameDirectory, "versions", selected.Id));
		string versionJsonPath = VersionResolver.GetVersionJsonPath(gameDirectory, selected.Id);
		await DownloadFileAsync(selected.Url, versionJsonPath, null, ct);
		Report("版本元数据下载完成", 0.05);
		MinecraftVersion version = await VersionResolver.ResolveAsync(gameDirectory, selected.Id);
		await DownloadClientJarAsync(gameDirectory, selected.Id, version, ct);
		Report("游戏客户端文件下载完成", 0.1);
		await DownloadLibrariesAsync(gameDirectory, version, ct);
		await DownloadAssetsAsync(gameDirectory, version, ct);
		Report("Minecraft " + versionId + " 安装完成", 1.0);
		_log("版本 " + versionId + " 安装完成");
	}

	public async Task RepairInstalledVersionAsync(string gameDirectory, string versionId, CancellationToken ct = default(CancellationToken))
	{
		MinecraftVersion version = await VersionResolver.ResolveAsync(gameDirectory, versionId);
		string versionId2 = (string.IsNullOrWhiteSpace(version.InheritsFrom) ? versionId : version.InheritsFrom);
		await DownloadClientJarAsync(gameDirectory, versionId2, version, ct);
		await DownloadLibrariesAsync(gameDirectory, version, ct);
		await DownloadAssetsAsync(gameDirectory, version, ct);
		_log("已修复本地版本 " + versionId);
	}

	private async Task DownloadClientJarAsync(string gameDirectory, string versionId, MinecraftVersion version, CancellationToken ct)
	{
		JsonElement? downloads = version.Downloads;
		if (downloads.HasValue)
		{
			JsonElement valueOrDefault = downloads.GetValueOrDefault();
			if (valueOrDefault.ValueKind == JsonValueKind.Object)
			{
				if (valueOrDefault.TryGetProperty("client", out var value))
				{
					string text = JsonUtil.GetString(value, "url");
					string expectedSha = JsonUtil.GetString(value, "sha1");
					if (!string.IsNullOrWhiteSpace(text))
					{
						string versionJarPath = VersionResolver.GetVersionJarPath(gameDirectory, versionId);
						await DownloadFileAsync(text, versionJarPath, expectedSha, ct);
					}
				}
				return;
			}
		}
		_log("版本 JSON 没有 downloads.client 字段，跳过 client.jar 下载");
	}

	private async Task DownloadLibrariesAsync(string gameDirectory, MinecraftVersion version, CancellationToken ct)
	{
		List<(JsonElement, string)> list = new List<(JsonElement, string)>();
		int num = 0;
		foreach (JsonElement library in version.Libraries)
		{
			num++;
			if (RulesEvaluator.IsAllowed(library) && library.TryGetProperty("downloads", out var value) && value.ValueKind == JsonValueKind.Object)
			{
				if (value.TryGetProperty("artifact", out var value2))
				{
					list.Add((value2, $"库 {num}/{version.Libraries.Count}"));
				}
				string text = SelectNativeClassifier(library);
				if (!string.IsNullOrWhiteSpace(text) && value.TryGetProperty("classifiers", out var value3) && value3.ValueKind == JsonValueKind.Object && value3.TryGetProperty(text, out var value4))
				{
					list.Add((value4, $"Native {num}/{version.Libraries.Count}"));
				}
			}
		}
		int completed = 0;
		int total = list.Count;
		if (total == 0)
		{
			Report("无需下载额外库文件", 0.4);
		}
		SemaphoreSlim semaphore = new SemaphoreSlim(6);
		await Parallel.ForEachAsync<(JsonElement, string)>(list, ct, async delegate((JsonElement Artifact, string Label) item, CancellationToken token)
		{
			await semaphore.WaitAsync(token);
			try
			{
				await DownloadLibraryArtifactAsync(gameDirectory, item.Artifact, token, item.Label);
				int num2 = Interlocked.Increment(ref completed);
				if (num2 % 5 == 0 || num2 == total)
				{
					_log($"库文件进度 {num2}/{total}");
					Report($"正在下载库文件：{num2}/{total}", 0.1 + 0.3 * (double)num2 / (double)Math.Max(1, total));
				}
			}
			finally
			{
				semaphore.Release();
			}
		});
	}

	private async Task DownloadLibraryArtifactAsync(string gameDirectory, JsonElement artifact, CancellationToken ct, string label)
	{
		string text = JsonUtil.GetString(artifact, "path");
		string text2 = JsonUtil.GetString(artifact, "url");
		string expectedSha = JsonUtil.GetString(artifact, "sha1");
		if (!string.IsNullOrWhiteSpace(text) && !string.IsNullOrWhiteSpace(text2))
		{
			string text3 = Path.Combine(gameDirectory, "libraries", text.Replace('/', Path.DirectorySeparatorChar));
			_log(label + ": " + Path.GetFileName(text3));
			await DownloadFileAsync(text2, text3, expectedSha, ct);
		}
	}

	private async Task DownloadAssetsAsync(string gameDirectory, MinecraftVersion version, CancellationToken ct)
	{
		JsonElement? assetIndex = version.AssetIndex;
		if (assetIndex.HasValue)
		{
			JsonElement valueOrDefault = assetIndex.GetValueOrDefault();
			if (valueOrDefault.ValueKind == JsonValueKind.Object)
			{
				string text = JsonUtil.GetString(valueOrDefault, "id") ?? version.Assets ?? version.Id;
				string text2 = JsonUtil.GetString(valueOrDefault, "url");
				string expectedSha = JsonUtil.GetString(valueOrDefault, "sha1");
				if (string.IsNullOrWhiteSpace(text2))
				{
					return;
				}
				string indexPath = Path.Combine(gameDirectory, "assets", "indexes", text + ".json");
				await DownloadFileAsync(text2, indexPath, expectedSha, ct);
				await using FileStream stream = File.OpenRead(indexPath);
				using (JsonDocument doc = await JsonDocument.ParseAsync(stream, default(JsonDocumentOptions), ct))
				{
					if (!doc.RootElement.TryGetProperty("objects", out var value) || value.ValueKind != JsonValueKind.Object)
					{
						return;
					}
					List<(string, string, string)> list = new List<(string, string, string)>();
					foreach (JsonProperty item3 in value.EnumerateObject())
					{
						string text3 = JsonUtil.GetString(item3.Value, "hash");
						if (!string.IsNullOrWhiteSpace(text3) && text3.Length >= 2)
						{
							string text4 = text3.Substring(0, 2);
							InlineArray5<string> buffer = default(InlineArray5<string>);
							buffer[0] = gameDirectory;
							buffer[1] = "assets";
							buffer[2] = "objects";
							buffer[3] = text4;
							buffer[4] = text3;
							string item = Path.Combine(buffer);
							string item2 = "https://resources.download.minecraft.net/" + text4 + "/" + text3;
							list.Add((text3, item, item2));
						}
					}
					int total = list.Count;
					_log($"开始下载 {total} 个资源文件...");
					if (total == 0)
					{
						Report("无需下载额外资源文件", 1.0);
					}
					int completed = 0;
					SemaphoreSlim semaphore = new SemaphoreSlim(8);
					await Parallel.ForEachAsync<(string, string, string)>(list, ct, async delegate((string Hash, string Output, string Url) asset, CancellationToken token)
					{
						await semaphore.WaitAsync(token);
						try
						{
							await DownloadFileAsync(asset.Url, asset.Output, asset.Hash, token, quiet: true);
							int num = Interlocked.Increment(ref completed);
							if (num % 50 == 0 || num == total)
							{
								_log($"资源进度 {num}/{total}");
								Report($"正在下载游戏资源：{num}/{total}", 0.4 + 0.6 * (double)num / (double)Math.Max(1, total));
							}
						}
						finally
						{
							semaphore.Release();
						}
					});
				}
				return;
			}
		}
		_log("版本 JSON 没有 assetIndex 字段，跳过资源下载");
	}

	private static string? SelectNativeClassifier(JsonElement library)
	{
		return MinecraftUtils.SelectNativeClassifier(library);
	}

	private async Task DownloadFileAsync(string url, string outputPath, string? expectedSha1, CancellationToken ct, bool quiet = false)
	{
		if (File.Exists(outputPath) && (string.IsNullOrWhiteSpace(expectedSha1) || Sha1Matches(outputPath, expectedSha1)))
		{
			return;
		}
		IReadOnlyList<string> candidateUrls = MirrorHelper.GetCandidates(url);
		if (candidateUrls.Count == 0)
		{
			throw new InvalidOperationException("下载地址为空。");
		}
		Directory.CreateDirectory(Path.GetDirectoryName(outputPath));
		string temp = outputPath + ".download";
		if (!quiet)
		{
			_log("下载：" + Path.GetFileName(outputPath));
		}
		if (File.Exists(temp) && !string.IsNullOrWhiteSpace(expectedSha1) && Sha1Matches(temp, expectedSha1))
		{
			File.Move(temp, outputPath, overwrite: true);
			return;
		}
		bool flag = default(bool);
		for (int attempt = 1; attempt <= 4; attempt++)
		{
			try
			{
				long existingBytes = (File.Exists(temp) ? new FileInfo(temp).Length : 0);
				string requestUri = candidateUrls[(attempt - 1) % candidateUrls.Count];
				using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, requestUri);
				if (existingBytes > 0)
				{
					request.Headers.Range = new RangeHeaderValue(existingBytes, null);
				}
				using HttpResponseMessage response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
				if (existingBytes > 0 && response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable)
				{
					File.Delete(temp);
					if (!quiet)
					{
						_log("续传位置无效，重新下载：" + Path.GetFileName(outputPath));
					}
					continue;
				}
				response.EnsureSuccessStatusCode();
				bool append = existingBytes > 0 && response.StatusCode == HttpStatusCode.PartialContent;
				if (existingBytes > 0 && !append && !quiet)
				{
					_log("服务器不支持续传，重新下载：" + Path.GetFileName(outputPath));
				}
				if (append && !quiet)
				{
					_log($"继续下载：{Path.GetFileName(outputPath)}（已完成 {FormatSize(existingBytes)}）");
				}
				await using (Stream input = await response.Content.ReadAsStreamAsync(ct))
				{
					await using FileStream output = new FileStream(temp, append ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);
					await input.CopyToAsync(output, ct);
				}
				if (!string.IsNullOrWhiteSpace(expectedSha1) && !Sha1Matches(temp, expectedSha1))
				{
					File.Delete(temp);
					throw new InvalidDataException("SHA1 校验失败：" + outputPath);
				}
				File.Move(temp, outputPath, overwrite: true);
				break;
			}
			catch (OperationCanceledException) when (ct.IsCancellationRequested)
			{
				if (!quiet)
				{
					_log("下载已暂停，可继续：" + Path.GetFileName(outputPath));
				}
				throw;
			}
			catch (Exception ex2) when (((Func<bool>)delegate
			{
				// Could not convert BlockContainer to single expression
				flag = attempt < 4;
				if (flag)
				{
					flag = ((ex2 is HttpRequestException || ex2 is TaskCanceledException || ex2 is IOException || ex2 is InvalidDataException) ? true : false);
				}
				return flag;
			}).Invoke())
			{
				if (ex2 is InvalidDataException && File.Exists(temp))
				{
					File.Delete(temp);
				}
				TimeSpan delay = TimeSpan.FromSeconds(Math.Pow(2.0, attempt));
				if (!quiet)
				{
					_log($"下载失败，{delay.TotalSeconds}s 后切换下载源重试 ({attempt}/{4})：{Path.GetFileName(outputPath)} - {ex2.Message}");
				}
				await Task.Delay(delay, ct);
			}
			catch
			{
				throw;
			}
		}
	}

	private static bool Sha1Matches(string path, string sha1)
	{
		return MinecraftUtils.Sha1Matches(path, sha1);
	}

	private static string FormatSize(long bytes)
	{
		if (bytes < 1024)
		{
			return bytes + " B";
		}
		if (bytes >= 1048576)
		{
			return $"{(double)bytes / 1024.0 / 1024.0:0.0} MB";
		}
		return $"{(double)bytes / 1024.0:0.0} KB";
	}

	private void Report(string message, double percentage)
	{
		_progress?.Invoke(new MinecraftDownloadProgress(message, Math.Clamp(percentage, 0.0, 1.0)));
	}
}
