using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Pineglow.Launcher.Models;

namespace Pineglow.Launcher.Services;

public sealed class ModContentService
{
	private const string ModrinthApi = "https://api.modrinth.com/v2";

	private const string CurseForgeApi = "https://api.curseforge.com/v1";

	private readonly HttpClient _http;

	public ModContentService(HttpClient http)
	{
		_http = http;
		if (!_http.DefaultRequestHeaders.UserAgent.Any())
		{
			_http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Pineglow-Launcher", "1.0"));
		}
	}

	public Task<List<ModSearchResult>> SearchAsync(ModSearchRequest request, CancellationToken ct = default(CancellationToken))
	{
		if (string.IsNullOrWhiteSpace(request.Query))
		{
			throw new InvalidOperationException("请输入要搜索的 Mod 名称或关键词。");
		}
		request.Query = ModSearchQueryResolver.Resolve(request.Query);
		if (request.Source != ModSource.CurseForge)
		{
			return SearchModrinthAsync(request, ct);
		}
		return SearchCurseForgeAsync(request, ct);
	}

	public Task<List<ModVersion>> GetVersionsAsync(ModSearchResult project, string minecraftVersion, string loader, string curseForgeApiKey = "", CancellationToken ct = default(CancellationToken))
	{
		if (project.Source != ModSource.CurseForge)
		{
			return GetModrinthVersionsAsync(project, minecraftVersion, loader, ct);
		}
		return GetCurseForgeFilesAsync(project, minecraftVersion, loader, curseForgeApiKey, ct);
	}

	public async Task<string> DownloadAndInstallAsync(GameInstance instance, ModVersion version, ModService modService, string curseForgeApiKey = "", IProgress<double>? progress = null, CancellationToken ct = default(CancellationToken))
	{
		if (string.IsNullOrWhiteSpace(version.DownloadUrl))
		{
			if (version.Source != ModSource.CurseForge || string.IsNullOrWhiteSpace(curseForgeApiKey))
			{
				throw new InvalidOperationException("该版本没有可用下载地址。CurseForge 下载可能需要有效 API Key 或在项目页面手动下载。");
			}
			version.DownloadUrl = await GetCurseForgeDownloadUrlAsync(version, curseForgeApiKey, ct);
		}
		using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, version.DownloadUrl);
		using HttpResponseMessage response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
		response.EnsureSuccessStatusCode();
		string tempDirectory = Path.Combine(Path.GetTempPath(), "mcmini-mod-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(tempDirectory);
		string tempPath = Path.Combine(tempDirectory, "download.jar");
		try
		{
			await using (Stream input = await response.Content.ReadAsStreamAsync(ct))
			{
				await using FileStream output = File.Create(tempPath);
				byte[] buffer = new byte[81920];
				long total = 0L;
				long expected = response.Content.Headers.ContentLength ?? version.FileSize;
				while (true)
				{
					int num;
					int read = (num = await input.ReadAsync(buffer, ct));
					if (num <= 0)
					{
						break;
					}
					await output.WriteAsync(buffer.AsMemory(0, read), ct);
					total += read;
					if (expected > 0)
					{
						progress?.Report(Math.Min(1.0, (double)total / (double)expected));
					}
				}
			}
			if (!string.IsNullOrWhiteSpace(version.Sha1))
			{
				await using FileStream output = File.OpenRead(tempPath);
				string text = Convert.ToHexString(await SHA1.HashDataAsync(output, ct)).ToLowerInvariant();
				if (!text.Equals(version.Sha1, StringComparison.OrdinalIgnoreCase))
				{
					throw new InvalidDataException($"下载文件 SHA1 校验失败：期望 {version.Sha1}，实际 {text}。");
				}
			}
			string path = SanitizeJarFileName(version.FileName);
			string text2 = Path.Combine(tempDirectory, path);
			if (File.Exists(text2))
			{
				File.Delete(text2);
			}
			File.Move(tempPath, text2);
			tempPath = text2;
			string text3 = modService.ImportMod(instance, tempPath);
			modService.WriteOnlineMetadata(text3, version);
			return text3;
		}
		finally
		{
			if (File.Exists(tempPath))
			{
				File.Delete(tempPath);
			}
			if (Directory.Exists(tempDirectory))
			{
				Directory.Delete(tempDirectory, recursive: true);
			}
		}
	}

	public async Task<ModInstallResult> InstallWithDependenciesAsync(GameInstance instance, ModVersion version, ModService modService, string minecraftVersion, string loader, string curseForgeApiKey = "", IProgress<double>? progress = null, Action<string>? status = null, CancellationToken ct = default(CancellationToken))
	{
		ModInstallResult result = new ModInstallResult();
		HashSet<string> visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		HashSet<string> installedProjects = (from mod in await modService.GetModsAsync(instance)
			where mod.HasOnlineMetadata
			select mod.OnlineSource + ":" + mod.ProjectId).ToHashSet<string>(StringComparer.OrdinalIgnoreCase);
		await InstallRecursiveAsync(version, isMain: true);
		return result;
		async Task InstallRecursiveAsync(ModVersion current, bool isMain)
		{
			ct.ThrowIfCancellationRequested();
			string item = current.Source.ToString() + ":" + ((!string.IsNullOrWhiteSpace(current.VersionId)) ? current.VersionId : current.ProjectId);
			if (visited.Add(item))
			{
				foreach (ModDependency dependency in current.Dependencies.Where((ModDependency modDependency) => modDependency.Required))
				{
					string item2 = dependency.Source.ToString() + ":" + dependency.ProjectId;
					if (string.IsNullOrWhiteSpace(dependency.ProjectId) || !installedProjects.Contains(item2))
					{
						ModVersion modVersion = (await ResolveDependencyAsync(dependency, minecraftVersion, loader, curseForgeApiKey, ct)) ?? throw new InvalidOperationException("无法找到必需依赖 " + dependency.ProjectId + " 的兼容版本。");
						status?.Invoke("安装依赖：" + modVersion.DisplayName + " " + modVersion.VersionNumber);
						await InstallRecursiveAsync(modVersion, isMain: false);
					}
				}
				string currentProjectKey = current.Source.ToString() + ":" + current.ProjectId;
				if (isMain || string.IsNullOrWhiteSpace(current.ProjectId) || !installedProjects.Contains(currentProjectKey))
				{
					string text = await DownloadAndInstallAsync(instance, current, modService, curseForgeApiKey, progress, ct);
					result.InstalledFiles.Add(text);
					installedProjects.Add(currentProjectKey);
					if (isMain)
					{
						result.MainFilePath = text;
					}
				}
			}
		}
	}

	public async Task<ModVersion?> GetLatestCompatibleVersionAsync(ModFile mod, string minecraftVersion, string loader, string curseForgeApiKey = "", CancellationToken ct = default(CancellationToken))
	{
		if (!mod.HasOnlineMetadata || !Enum.TryParse<ModSource>(mod.OnlineSource, ignoreCase: true, out var result))
		{
			return null;
		}
		List<ModVersion> source = await GetVersionsAsync(new ModSearchResult
		{
			Source = result,
			ProjectId = mod.ProjectId
		}, minecraftVersion, loader, curseForgeApiKey, ct);
		return source.FirstOrDefault((ModVersion item) => item.ReleaseType.Equals("release", StringComparison.OrdinalIgnoreCase)) ?? source.FirstOrDefault();
	}

	private async Task<ModVersion?> ResolveDependencyAsync(ModDependency dependency, string minecraftVersion, string loader, string curseForgeApiKey, CancellationToken ct)
	{
		if (dependency.Source == ModSource.Modrinth && !string.IsNullOrWhiteSpace(dependency.VersionId))
		{
			using (HttpResponseMessage response = await _http.GetAsync("https://api.modrinth.com/v2/version/" + Uri.EscapeDataString(dependency.VersionId), ct))
			{
				response.EnsureSuccessStatusCode();
				using JsonDocument jsonDocument = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(ct));
				return ParseModrinthVersion(jsonDocument.RootElement, dependency.ProjectId);
			}
		}
		if (string.IsNullOrWhiteSpace(dependency.ProjectId))
		{
			return null;
		}
		List<ModVersion> source = await GetVersionsAsync(new ModSearchResult
		{
			Source = dependency.Source,
			ProjectId = dependency.ProjectId
		}, minecraftVersion, loader, curseForgeApiKey, ct);
		return source.FirstOrDefault((ModVersion item) => item.ReleaseType.Equals("release", StringComparison.OrdinalIgnoreCase)) ?? source.FirstOrDefault();
	}

	private async Task<List<ModSearchResult>> SearchModrinthAsync(ModSearchRequest request, CancellationToken ct)
	{
		List<List<string>> list = new List<List<string>>
		{
			new List<string> { "project_type:mod" }
		};
		if (!string.IsNullOrWhiteSpace(request.MinecraftVersion))
		{
			list.Add(new List<string> { "versions:" + request.MinecraftVersion.Trim() });
		}
		string text = NormalizeModrinthLoader(request.Loader);
		if (!string.IsNullOrWhiteSpace(text))
		{
			list.Add(new List<string> { "categories:" + text });
		}
		string stringToEscape = JsonSerializer.Serialize(list);
		string requestUri = $"{"https://api.modrinth.com/v2"}/search?query={Uri.EscapeDataString(request.Query.Trim())}&limit={Math.Clamp(request.Limit, 1, 50)}&index=relevance&facets={Uri.EscapeDataString(stringToEscape)}";
		using HttpResponseMessage response = await _http.GetAsync(requestUri, ct);
		response.EnsureSuccessStatusCode();
		using JsonDocument jsonDocument = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(ct));
		List<ModSearchResult> list2 = new List<ModSearchResult>();
		foreach (JsonElement item in jsonDocument.RootElement.GetProperty("hits").EnumerateArray())
		{
			string text2 = GetString(item, "slug");
			list2.Add(new ModSearchResult
			{
				Source = ModSource.Modrinth,
				ProjectId = GetString(item, "project_id"),
				Slug = text2,
				Title = GetString(item, "title"),
				Description = GetString(item, "description"),
				Author = GetString(item, "author"),
				IconUrl = GetString(item, "icon_url"),
				WebsiteUrl = (string.IsNullOrWhiteSpace(text2) ? string.Empty : ("https://modrinth.com/mod/" + text2)),
				Downloads = GetInt64(item, "downloads"),
				Categories = GetStrings(item, "categories"),
				GameVersions = GetStrings(item, "versions")
			});
		}
		return list2;
	}

	private async Task<List<ModVersion>> GetModrinthVersionsAsync(ModSearchResult project, string minecraftVersion, string loader, CancellationToken ct)
	{
		List<string> list = new List<string>();
		if (!string.IsNullOrWhiteSpace(minecraftVersion))
		{
			list.Add("game_versions=" + Uri.EscapeDataString(JsonSerializer.Serialize(new string[1] { minecraftVersion.Trim() })));
		}
		string text = NormalizeModrinthLoader(loader);
		if (!string.IsNullOrWhiteSpace(text))
		{
			list.Add("loaders=" + Uri.EscapeDataString(JsonSerializer.Serialize(new string[1] { text })));
		}
		string requestUri = "https://api.modrinth.com/v2/project/" + Uri.EscapeDataString(project.ProjectId) + "/version" + ((list.Count == 0) ? string.Empty : ("?" + string.Join("&", list)));
		using HttpResponseMessage response = await _http.GetAsync(requestUri, ct);
		response.EnsureSuccessStatusCode();
		using JsonDocument jsonDocument = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(ct));
		List<ModVersion> list2 = new List<ModVersion>();
		foreach (JsonElement item in jsonDocument.RootElement.EnumerateArray())
		{
			ModVersion modVersion = ParseModrinthVersion(item, project.ProjectId);
			if (modVersion != null)
			{
				list2.Add(modVersion);
			}
		}
		return list2;
	}

	private async Task<List<ModSearchResult>> SearchCurseForgeAsync(ModSearchRequest request, CancellationToken ct)
	{
		RequireCurseForgeApiKey(request.CurseForgeApiKey);
		List<string> list = new List<string>
		{
			"gameId=432",
			"classId=6",
			"sortField=2",
			"sortOrder=desc",
			"pageSize=" + Math.Clamp(request.Limit, 1, 50),
			"searchFilter=" + Uri.EscapeDataString(request.Query.Trim())
		};
		if (!string.IsNullOrWhiteSpace(request.MinecraftVersion))
		{
			list.Add("gameVersion=" + Uri.EscapeDataString(request.MinecraftVersion.Trim()));
		}
		int num = CurseForgeLoaderType(request.Loader);
		if (num > 0)
		{
			list.Add("modLoaderType=" + num);
		}
		using HttpRequestMessage message = CreateCurseForgeRequest(HttpMethod.Get, "https://api.curseforge.com/v1/mods/search?" + string.Join("&", list), request.CurseForgeApiKey);
		using HttpResponseMessage response = await _http.SendAsync(message, ct);
		response.EnsureSuccessStatusCode();
		using JsonDocument jsonDocument = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(ct));
		List<ModSearchResult> list2 = new List<ModSearchResult>();
		foreach (JsonElement item in jsonDocument.RootElement.GetProperty("data").EnumerateArray())
		{
			string projectId = GetInt64(item, "id").ToString();
			string text = GetString(item, "slug");
			list2.Add(new ModSearchResult
			{
				Source = ModSource.CurseForge,
				ProjectId = projectId,
				Slug = text,
				Title = GetString(item, "name"),
				Description = GetString(item, "summary"),
				Author = (item.TryGetProperty("authors", out var value) ? GetString(value.EnumerateArray().FirstOrDefault(), "name") : string.Empty),
				IconUrl = (item.TryGetProperty("logo", out var value2) ? GetString(value2, "thumbnailUrl") : string.Empty),
				WebsiteUrl = (item.TryGetProperty("links", out var value3) ? GetString(value3, "websiteUrl") : ("https://www.curseforge.com/minecraft/mc-mods/" + text)),
				Downloads = GetInt64(item, "downloadCount"),
				Categories = (item.TryGetProperty("categories", out var value4) ? (from x in value4.EnumerateArray()
					select GetString(x, "name") into x
					where x.Length > 0
					select x).ToList() : new List<string>())
			});
		}
		return list2;
	}

	private async Task<List<ModVersion>> GetCurseForgeFilesAsync(ModSearchResult project, string minecraftVersion, string loader, string apiKey, CancellationToken ct)
	{
		RequireCurseForgeApiKey(apiKey);
		List<string> list = new List<string> { "pageSize=50" };
		if (!string.IsNullOrWhiteSpace(minecraftVersion))
		{
			list.Add("gameVersion=" + Uri.EscapeDataString(minecraftVersion.Trim()));
		}
		int num = CurseForgeLoaderType(loader);
		if (num > 0)
		{
			list.Add("modLoaderType=" + num);
		}
		using HttpRequestMessage message = CreateCurseForgeRequest(HttpMethod.Get, $"{"https://api.curseforge.com/v1"}/mods/{project.ProjectId}/files?{string.Join("&", list)}", apiKey);
		using HttpResponseMessage response = await _http.SendAsync(message, ct);
		response.EnsureSuccessStatusCode();
		using JsonDocument jsonDocument = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(ct));
		List<ModVersion> list2 = new List<ModVersion>();
		foreach (JsonElement item in jsonDocument.RootElement.GetProperty("data").EnumerateArray())
		{
			string text = GetString(item, "fileName");
			if (!text.EndsWith(".jar", StringComparison.OrdinalIgnoreCase))
			{
				continue;
			}
			JsonElement value;
			JsonElement element = (item.TryGetProperty("hashes", out value) ? value.EnumerateArray().ToList() : new List<JsonElement>()).FirstOrDefault((JsonElement x) => GetInt64(x, "algo") == 1);
			ModVersion modVersion = new ModVersion
			{
				Source = ModSource.CurseForge,
				ProjectId = project.ProjectId,
				VersionId = GetInt64(item, "id").ToString(),
				DisplayName = GetString(item, "displayName"),
				VersionNumber = GetString(item, "displayName"),
				ReleaseType = CurseForgeReleaseType((int)GetInt64(item, "releaseType")),
				FileName = text,
				DownloadUrl = GetString(item, "downloadUrl"),
				Sha1 = ((element.ValueKind == JsonValueKind.Undefined) ? string.Empty : GetString(element, "value")),
				FileSize = GetInt64(item, "fileLength"),
				GameVersions = GetStrings(item, "gameVersions")
			};
			if (!string.IsNullOrWhiteSpace(loader))
			{
				modVersion.Loaders.Add(loader);
			}
			if (item.TryGetProperty("dependencies", out var value2) && value2.ValueKind == JsonValueKind.Array)
			{
				foreach (JsonElement item2 in value2.EnumerateArray())
				{
					modVersion.Dependencies.Add(new ModDependency
					{
						Source = ModSource.CurseForge,
						ProjectId = GetInt64(item2, "modId").ToString(),
						Required = (GetInt64(item2, "relationType") == 3)
					});
				}
			}
			list2.Add(modVersion);
		}
		return list2;
	}

	private async Task<string> GetCurseForgeDownloadUrlAsync(ModVersion version, string apiKey, CancellationToken ct)
	{
		using HttpRequestMessage message = CreateCurseForgeRequest(HttpMethod.Get, $"{"https://api.curseforge.com/v1"}/mods/{version.ProjectId}/files/{version.VersionId}/download-url", apiKey);
		using HttpResponseMessage response = await _http.SendAsync(message, ct);
		response.EnsureSuccessStatusCode();
		using JsonDocument jsonDocument = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(ct));
		return GetString(jsonDocument.RootElement, "data");
	}

	private static HttpRequestMessage CreateCurseForgeRequest(HttpMethod method, string url, string apiKey)
	{
		return new HttpRequestMessage(method, url)
		{
			Headers = { 
			{
				"x-api-key",
				apiKey.Trim()
			} }
		};
	}

	private static void RequireCurseForgeApiKey(string apiKey)
	{
		if (string.IsNullOrWhiteSpace(apiKey))
		{
			throw new InvalidOperationException("CurseForge 搜索下载需要 API Key。请在设置页填写 CurseForge API Key；Modrinth 无需 Key。");
		}
	}

	private static string NormalizeModrinthLoader(string loader)
	{
		return loader.Trim().ToLowerInvariant() switch
		{
			"fabric" => "fabric", 
			"forge" => "forge", 
			"neoforge" => "neoforge", 
			"quilt" => "quilt", 
			_ => string.Empty, 
		};
	}

	private static int CurseForgeLoaderType(string loader)
	{
		return loader.Trim().ToLowerInvariant() switch
		{
			"forge" => 1, 
			"fabric" => 4, 
			"quilt" => 5, 
			"neoforge" => 6, 
			_ => 0, 
		};
	}

	private static string CurseForgeReleaseType(int value)
	{
		return value switch
		{
			1 => "release", 
			2 => "beta", 
			3 => "alpha", 
			_ => "unknown", 
		};
	}

	private static string SanitizeJarFileName(string fileName)
	{
		string text = Path.GetFileName(fileName);
		if (string.IsNullOrWhiteSpace(text))
		{
			text = "downloaded-mod.jar";
		}
		if (!text.EndsWith(".jar", StringComparison.OrdinalIgnoreCase))
		{
			text += ".jar";
		}
		char[] invalidFileNameChars = Path.GetInvalidFileNameChars();
		foreach (char oldChar in invalidFileNameChars)
		{
			text = text.Replace(oldChar, '_');
		}
		return text;
	}

	private static ModVersion? ParseModrinthVersion(JsonElement item, string fallbackProjectId)
	{
		if (!item.TryGetProperty("files", out var value) || value.ValueKind != JsonValueKind.Array)
		{
			return null;
		}
		JsonElement element = value.EnumerateArray().FirstOrDefault((JsonElement x) => GetBool(x, "primary") && GetString(x, "filename").EndsWith(".jar", StringComparison.OrdinalIgnoreCase));
		if (element.ValueKind == JsonValueKind.Undefined)
		{
			element = value.EnumerateArray().FirstOrDefault((JsonElement x) => GetString(x, "filename").EndsWith(".jar", StringComparison.OrdinalIgnoreCase));
		}
		if (element.ValueKind == JsonValueKind.Undefined)
		{
			return null;
		}
		JsonElement value2;
		string sha = (element.TryGetProperty("hashes", out value2) ? GetString(value2, "sha1") : string.Empty);
		ModVersion modVersion = new ModVersion
		{
			Source = ModSource.Modrinth,
			ProjectId = (string.IsNullOrWhiteSpace(GetString(item, "project_id")) ? fallbackProjectId : GetString(item, "project_id")),
			VersionId = GetString(item, "id"),
			DisplayName = GetString(item, "name"),
			VersionNumber = GetString(item, "version_number"),
			ReleaseType = GetString(item, "version_type"),
			FileName = GetString(element, "filename"),
			DownloadUrl = GetString(element, "url"),
			Sha1 = sha,
			FileSize = GetInt64(element, "size"),
			GameVersions = GetStrings(item, "game_versions"),
			Loaders = GetStrings(item, "loaders")
		};
		if (item.TryGetProperty("dependencies", out var value3) && value3.ValueKind == JsonValueKind.Array)
		{
			foreach (JsonElement item2 in value3.EnumerateArray())
			{
				modVersion.Dependencies.Add(new ModDependency
				{
					Source = ModSource.Modrinth,
					ProjectId = GetString(item2, "project_id"),
					VersionId = GetString(item2, "version_id"),
					Required = GetString(item2, "dependency_type").Equals("required", StringComparison.OrdinalIgnoreCase)
				});
			}
		}
		return modVersion;
	}

	private static string GetString(JsonElement element, string name)
	{
		if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String)
		{
			return string.Empty;
		}
		return value.GetString() ?? string.Empty;
	}

	private static long GetInt64(JsonElement element, string name)
	{
		if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(name, out var value) || !value.TryGetInt64(out var value2))
		{
			return 0L;
		}
		return value2;
	}

	private static bool GetBool(JsonElement element, string name)
	{
		JsonElement value = default(JsonElement);
		bool flag = element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out value);
		if (flag)
		{
			JsonValueKind valueKind = value.ValueKind;
			bool flag2 = valueKind - 5 <= JsonValueKind.Object;
			flag = flag2;
		}
		if (flag)
		{
			return value.GetBoolean();
		}
		return false;
	}

	private static List<string> GetStrings(JsonElement element, string name)
	{
		if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Array)
		{
			return new List<string>();
		}
		return (from x in value.EnumerateArray()
			where x.ValueKind == JsonValueKind.String
			select x.GetString() ?? string.Empty into x
			where x.Length > 0
			select x).ToList();
	}
}
