using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Pineglow.Launcher.Models;

namespace Pineglow.Launcher.Services;

public sealed class VersionManagerService
{
	private readonly record struct LibraryFile(string Path, string? Sha1);

	private readonly HttpClient _http;

	private readonly Action<string> _log;

	public VersionManagerService(HttpClient http, Action<string> log)
	{
		_http = http;
		_log = log;
	}

	public List<VersionInfo> GetInstalledVersions(string gameDirectory)
	{
		return (from id in VersionResolver.ListLocalVersions(gameDirectory)
			select new VersionInfo
			{
				Id = id,
				Type = (ReadVersionType(gameDirectory, id) ?? "unknown"),
				JsonPath = VersionResolver.GetVersionJsonPath(gameDirectory, id),
				JarPath = VersionResolver.GetVersionJarPath(gameDirectory, id),
				Status = "未验证"
			}).OrderByDescending<VersionInfo, string>((VersionInfo x) => x.Id, StringComparer.OrdinalIgnoreCase).ToList();
	}

	public void DeleteVersion(string gameDirectory, string versionId)
	{
		if (string.IsNullOrWhiteSpace(versionId))
		{
			throw new InvalidOperationException("版本 ID 不能为空。");
		}
		string path = Path.Combine(gameDirectory, "versions", versionId);
		if (!Directory.Exists(path))
		{
			throw new DirectoryNotFoundException("版本 " + versionId + " 不存在。");
		}
		string path2 = Path.Combine(gameDirectory, "assets", "indexes", versionId + ".json");
		if (File.Exists(path2))
		{
			File.Delete(path2);
		}
		Directory.Delete(path, recursive: true);
		_log("已删除版本：" + versionId);
	}

	public async Task<VersionInfo> VerifyVersionAsync(string gameDirectory, string versionId, CancellationToken ct)
	{
		VersionInfo info = new VersionInfo
		{
			Id = versionId,
			JsonPath = VersionResolver.GetVersionJsonPath(gameDirectory, versionId),
			JarPath = VersionResolver.GetVersionJarPath(gameDirectory, versionId)
		};
		if (!File.Exists(info.JsonPath))
		{
			info.Status = "损坏";
			info.MissingFiles.Add("version JSON 文件不存在");
			info.ProblemCount = info.MissingFiles.Count;
			return info;
		}
		try
		{
			MinecraftVersion version = await VersionResolver.ResolveAsync(gameDirectory, versionId);
			if (!File.Exists(info.JarPath) && !string.IsNullOrWhiteSpace(version.InheritsFrom))
			{
				info.JarPath = VersionResolver.GetVersionJarPath(gameDirectory, version.InheritsFrom);
			}
			await using FileStream versionJsonStream = File.OpenRead(info.JsonPath);
			CancellationToken cancellationToken = ct;
			using JsonDocument jsonDoc = await JsonDocument.ParseAsync(versionJsonStream, default(JsonDocumentOptions), cancellationToken);
			JsonElement rootElement = jsonDoc.RootElement;
			info.Type = JsonUtil.GetString(rootElement, "type") ?? "unknown";
			if (!File.Exists(info.JarPath))
			{
				info.MissingFiles.Add("client.jar 缺失");
			}
			else
			{
				JsonElement? downloads = version.Downloads;
				if (downloads.HasValue)
				{
					JsonElement valueOrDefault = downloads.GetValueOrDefault();
					if (valueOrDefault.ValueKind == JsonValueKind.Object && valueOrDefault.TryGetProperty("client", out var value))
					{
						string text = JsonUtil.GetString(value, "sha1");
						if (!string.IsNullOrWhiteSpace(text) && !MinecraftUtils.Sha1Matches(info.JarPath, text))
						{
							info.MissingFiles.Add("client.jar SHA1 校验失败");
						}
					}
				}
			}
			string text2 = version.Assets ?? versionId;
			string path = Path.Combine(gameDirectory, "assets", "indexes", text2 + ".json");
			if (!File.Exists(path))
			{
				info.MissingFiles.Add("asset index " + text2 + ".json 缺失");
			}
			else
			{
				await using FileStream assetIndexStream = File.OpenRead(path);
				cancellationToken = ct;
				using JsonDocument jsonDocument = await JsonDocument.ParseAsync(assetIndexStream, default(JsonDocumentOptions), cancellationToken);
				if (jsonDocument.RootElement.TryGetProperty("objects", out var value2) && value2.ValueKind == JsonValueKind.Object)
				{
					foreach (JsonProperty item in value2.EnumerateObject())
					{
						ct.ThrowIfCancellationRequested();
						string text3 = JsonUtil.GetString(item.Value, "hash");
						if (!string.IsNullOrWhiteSpace(text3) && text3.Length >= 2)
						{
							InlineArray5<string> buffer = default(InlineArray5<string>);
							buffer[0] = gameDirectory;
							buffer[1] = "assets";
							buffer[2] = "objects";
							buffer[3] = text3.Substring(0, 2);
							buffer[4] = text3;
							if (!File.Exists(Path.Combine(buffer)))
							{
								info.MissingFiles.Add("asset " + item.Name + " 缺失");
							}
						}
					}
				}
			}
			foreach (JsonElement library in version.Libraries)
			{
				if (RulesEvaluator.IsAllowed(library))
				{
					VerifyLibraryFile(info, ResolveLibraryFile(gameDirectory, library, "artifact"), "library");
					VerifyLibraryFile(info, ResolveLibraryFile(gameDirectory, library, "classifier"), "native library");
				}
			}
		}
		catch (Exception ex)
		{
			info.Status = "损坏";
			info.MissingFiles.Add("解析失败：" + ex.Message);
			info.ProblemCount = info.MissingFiles.Count;
			return info;
		}
		info.ProblemCount = info.MissingFiles.Count;
		info.Status = ((info.ProblemCount == 0) ? "完整" : "不完整");
		return info;
	}

	public async Task RepairVersionAsync(string gameDirectory, string versionId, IProgress<string> progress, CancellationToken ct)
	{
		MinecraftDownloader downloader = new MinecraftDownloader(_http, _log);
		string text = ReadInheritedVersion(gameDirectory, versionId);
		if (string.IsNullOrWhiteSpace(text))
		{
			await downloader.InstallVersionAsync(gameDirectory, versionId, ct);
		}
		else
		{
			progress.Report("正在修复继承的原版 " + text + "...");
			await downloader.InstallVersionAsync(gameDirectory, text, ct);
			progress.Report("正在修复加载器版本 " + versionId + " 的库文件...");
			await downloader.RepairInstalledVersionAsync(gameDirectory, versionId, ct);
		}
		progress.Report("版本 " + versionId + " 修复完成");
	}

	private static string? ReadVersionType(string gameDirectory, string versionId)
	{
		try
		{
			string versionJsonPath = VersionResolver.GetVersionJsonPath(gameDirectory, versionId);
			if (!File.Exists(versionJsonPath))
			{
				return null;
			}
			using JsonDocument jsonDocument = JsonDocument.Parse(File.ReadAllText(versionJsonPath));
			return JsonUtil.GetString(jsonDocument.RootElement, "type") ?? "unknown";
		}
		catch
		{
			return null;
		}
	}

	private static LibraryFile? ResolveLibraryFile(string gameDirectory, JsonElement library, string mode)
	{
		if (!library.TryGetProperty("downloads", out var value) || value.ValueKind != JsonValueKind.Object)
		{
			return null;
		}
		if (mode == "artifact")
		{
			if (!value.TryGetProperty("artifact", out var value2) || value2.ValueKind != JsonValueKind.Object)
			{
				return null;
			}
			return ResolveFromArtifact(gameDirectory, value2);
		}
		string text = TryGetClassifierKey(library);
		if (string.IsNullOrWhiteSpace(text))
		{
			return null;
		}
		if (!value.TryGetProperty("classifiers", out var value3) || !value3.TryGetProperty(text, out var value4))
		{
			return null;
		}
		return ResolveFromArtifact(gameDirectory, value4);
	}

	private static LibraryFile? ResolveFromArtifact(string gameDirectory, JsonElement artifact)
	{
		string text = JsonUtil.GetString(artifact, "path");
		if (!string.IsNullOrWhiteSpace(text))
		{
			return new LibraryFile(Path.Combine(gameDirectory, "libraries", text.Replace('/', Path.DirectorySeparatorChar)), JsonUtil.GetString(artifact, "sha1"));
		}
		return null;
	}

	private static void VerifyLibraryFile(VersionInfo info, LibraryFile? file, string label)
	{
		if (file.HasValue)
		{
			LibraryFile valueOrDefault = file.GetValueOrDefault();
			string text;
			if (valueOrDefault.Path.Length <= 50)
			{
				text = valueOrDefault.Path;
			}
			else
			{
				string path = valueOrDefault.Path;
				text = path.Substring(path.Length - 50);
			}
			string text2 = text;
			if (!File.Exists(valueOrDefault.Path))
			{
				info.MissingFiles.Add(label + " " + text2 + " 缺失");
			}
			else if (!string.IsNullOrWhiteSpace(valueOrDefault.Sha1) && !MinecraftUtils.Sha1Matches(valueOrDefault.Path, valueOrDefault.Sha1))
			{
				info.MissingFiles.Add(label + " " + text2 + " SHA1 校验失败");
			}
		}
	}

	private static string? ReadInheritedVersion(string gameDirectory, string versionId)
	{
		try
		{
			using JsonDocument jsonDocument = JsonDocument.Parse(File.ReadAllText(VersionResolver.GetVersionJsonPath(gameDirectory, versionId)));
			return JsonUtil.GetString(jsonDocument.RootElement, "inheritsFrom");
		}
		catch
		{
			return null;
		}
	}

	private static string? TryGetClassifierKey(JsonElement library)
	{
		if (!library.TryGetProperty("natives", out var value) || value.ValueKind != JsonValueKind.Object)
		{
			return null;
		}
		string osRuleName = PlatformPaths.GetOsRuleName();
		if (!value.TryGetProperty(osRuleName, out var value2))
		{
			return null;
		}
		string text = value2.GetString();
		if (string.IsNullOrWhiteSpace(text))
		{
			return null;
		}
		return text.Replace("${arch}", Environment.Is64BitOperatingSystem ? "64" : "32", StringComparison.Ordinal);
	}
}
