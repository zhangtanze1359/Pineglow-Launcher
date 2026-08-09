using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Pineglow.Launcher.Models;

namespace Pineglow.Launcher.Services;

public sealed class ModrinthModpackService
{
	private const string Api = "https://api.modrinth.com/v2";

	private readonly HttpClient _http;

	public ModrinthModpackService(HttpClient http)
	{
		_http = http;
	}

	public async Task<List<ModpackProject>> SearchAsync(string query, CancellationToken ct = default(CancellationToken))
	{
		if (string.IsNullOrWhiteSpace(query))
		{
			throw new InvalidOperationException("请输入整合包名称或关键词。");
		}
		string value = Uri.EscapeDataString(JsonSerializer.Serialize(new string[1][] { new string[1] { "project_type:modpack" } }));
		string requestUri = $"{"https://api.modrinth.com/v2"}/search?query={Uri.EscapeDataString(query.Trim())}&limit=20&index=relevance&facets={value}";
		using HttpResponseMessage response = await _http.GetAsync(requestUri, ct);
		response.EnsureSuccessStatusCode();
		using JsonDocument jsonDocument = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(ct));
		List<ModpackProject> list = new List<ModpackProject>();
		foreach (JsonElement item in jsonDocument.RootElement.GetProperty("hits").EnumerateArray())
		{
			string text = ReadString(item, "slug");
			list.Add(new ModpackProject
			{
				ProjectId = ReadString(item, "project_id"),
				Slug = text,
				Title = ReadString(item, "title"),
				Description = ReadString(item, "description"),
				Author = ReadString(item, "author"),
				IconUrl = ReadString(item, "icon_url"),
				WebsiteUrl = (string.IsNullOrWhiteSpace(text) ? string.Empty : ("https://modrinth.com/modpack/" + text)),
				Downloads = ReadInt64(item, "downloads"),
				Categories = ReadStrings(item, "categories"),
				GameVersions = ReadStrings(item, "versions")
			});
		}
		return list;
	}

	public async Task<List<ModpackVersion>> GetVersionsAsync(ModpackProject project, CancellationToken ct = default(CancellationToken))
	{
		if (string.IsNullOrWhiteSpace(project.ProjectId))
		{
			throw new InvalidOperationException("整合包信息不完整，无法获取版本。");
		}
		using HttpResponseMessage response = await _http.GetAsync("https://api.modrinth.com/v2/project/" + Uri.EscapeDataString(project.ProjectId) + "/version", ct);
		response.EnsureSuccessStatusCode();
		using JsonDocument jsonDocument = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(ct));
		List<ModpackVersion> list = new List<ModpackVersion>();
		foreach (JsonElement item in jsonDocument.RootElement.EnumerateArray())
		{
			JsonElement value;
			JsonElement element = ((item.TryGetProperty("files", out value) && value.ValueKind == JsonValueKind.Array) ? value.EnumerateArray().FirstOrDefault((JsonElement candidate) => ReadString(candidate, "filename").EndsWith(".mrpack", StringComparison.OrdinalIgnoreCase)) : default(JsonElement));
			if (element.ValueKind == JsonValueKind.Object)
			{
				JsonElement value2;
				JsonElement element2 = (element.TryGetProperty("hashes", out value2) ? value2 : default(JsonElement));
				list.Add(new ModpackVersion
				{
					VersionId = ReadString(item, "id"),
					VersionNumber = ReadString(item, "version_number"),
					ReleaseType = ReadString(item, "version_type"),
					FileName = ReadString(element, "filename"),
					DownloadUrl = ReadString(element, "url"),
					Sha1 = ((element2.ValueKind == JsonValueKind.Object) ? ReadString(element2, "sha1") : string.Empty),
					Sha512 = ((element2.ValueKind == JsonValueKind.Object) ? ReadString(element2, "sha512") : string.Empty),
					GameVersions = ReadStrings(item, "game_versions"),
					Loaders = ReadStrings(item, "loaders"),
					PublishedAt = ReadDate(item, "date_published")
				});
			}
		}
		return (from item in list
			orderby item.ReleaseType.Equals("release", StringComparison.OrdinalIgnoreCase) descending, item.PublishedAt descending
			select item).ToList();
	}

	public async Task<string> DownloadAsync(ModpackProject project, ModpackVersion version, CancellationToken ct = default(CancellationToken))
	{
		if (string.IsNullOrWhiteSpace(version.DownloadUrl))
		{
			throw new InvalidOperationException(project.Title + " 的这个版本没有可用下载地址。");
		}
		string directory = Path.Combine(Path.GetTempPath(), "pineglow-modpack-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(directory);
		string path = (string.IsNullOrWhiteSpace(version.FileName) ? "modpack.mrpack" : Path.GetFileName(version.FileName));
		string path2 = Path.Combine(directory, path);
		try
		{
			using (HttpResponseMessage response = await _http.GetAsync(version.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct))
			{
				response.EnsureSuccessStatusCode();
				await using Stream input = await response.Content.ReadAsStreamAsync(ct);
				await using FileStream output = File.Create(path2);
				await input.CopyToAsync(output, ct);
			}
			if (!string.IsNullOrWhiteSpace(version.Sha512))
			{
				await VerifyHashAsync(path2, version.Sha512, SHA512.Create(), ct);
			}
			else if (!string.IsNullOrWhiteSpace(version.Sha1))
			{
				await VerifyHashAsync(path2, version.Sha1, SHA1.Create(), ct);
			}
			return path2;
		}
		catch
		{
			if (Directory.Exists(directory))
			{
				Directory.Delete(directory, recursive: true);
			}
			throw;
		}
	}

	public static void DeleteDownload(string? path)
	{
		if (!string.IsNullOrWhiteSpace(path))
		{
			string directoryName = Path.GetDirectoryName(path);
			if (!string.IsNullOrWhiteSpace(directoryName) && Directory.Exists(directoryName))
			{
				Directory.Delete(directoryName, recursive: true);
			}
		}
	}

	private static async Task VerifyHashAsync(string path, string expected, HashAlgorithm algorithm, CancellationToken ct)
	{
		using (algorithm)
		{
			await using FileStream stream = File.OpenRead(path);
			if (!Convert.ToHexString(await algorithm.ComputeHashAsync(stream, ct)).ToLowerInvariant().Equals(expected, StringComparison.OrdinalIgnoreCase))
			{
				throw new InvalidDataException("整合包下载校验失败，请重新下载。");
			}
		}
	}

	private static string ReadString(JsonElement element, string property)
	{
		if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(property, out var value) || value.ValueKind != JsonValueKind.String)
		{
			return string.Empty;
		}
		return value.GetString() ?? string.Empty;
	}

	private static long ReadInt64(JsonElement element, string property)
	{
		if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(property, out var value) || !value.TryGetInt64(out var value2))
		{
			return 0L;
		}
		return value2;
	}

	private static List<string> ReadStrings(JsonElement element, string property)
	{
		if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(property, out var value) || value.ValueKind != JsonValueKind.Array)
		{
			return new List<string>();
		}
		return (from item in value.EnumerateArray()
			where item.ValueKind == JsonValueKind.String
			select item.GetString() ?? string.Empty into item
			where item.Length > 0
			select item).ToList();
	}

	private static DateTimeOffset ReadDate(JsonElement element, string property)
	{
		if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(property, out var value) || value.ValueKind != JsonValueKind.String || !DateTimeOffset.TryParse(value.GetString(), out var result))
		{
			return DateTimeOffset.MinValue;
		}
		return result;
	}
}
