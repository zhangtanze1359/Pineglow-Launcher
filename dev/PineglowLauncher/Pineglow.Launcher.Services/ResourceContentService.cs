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

public sealed class ResourceContentService
{
	private const string Api = "https://api.modrinth.com/v2";

	private readonly HttpClient _http;

	public ResourceContentService(HttpClient http)
	{
		_http = http;
		if (!_http.DefaultRequestHeaders.UserAgent.Any())
		{
			_http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Pineglow-Launcher", "1.0"));
		}
	}

	public async Task<List<ResourceContentProject>> SearchAsync(string query, string minecraftVersion, ResourceContentKind kind, CancellationToken ct = default(CancellationToken))
	{
		if (string.IsNullOrWhiteSpace(query))
		{
			throw new InvalidOperationException("请输入资源名称或关键词。");
		}
		string projectType = ((kind == ResourceContentKind.Shader) ? "shader" : "resourcepack");
		List<List<string>> list = new List<List<string>>
		{
			new List<string> { "project_type:" + projectType }
		};
		if (!string.IsNullOrWhiteSpace(minecraftVersion))
		{
			list.Add(new List<string> { "versions:" + minecraftVersion });
		}
		string requestUri = $"{"https://api.modrinth.com/v2"}/search?query={Uri.EscapeDataString(query.Trim())}&limit=20&index=relevance&facets={Uri.EscapeDataString(JsonSerializer.Serialize(list))}";
		using HttpResponseMessage response = await _http.GetAsync(requestUri, ct);
		response.EnsureSuccessStatusCode();
		using JsonDocument jsonDocument = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(ct));
		return jsonDocument.RootElement.GetProperty("hits").EnumerateArray().Select(delegate(JsonElement hit)
		{
			string text = ReadString(hit, "slug");
			return new ResourceContentProject
			{
				ProjectId = ReadString(hit, "project_id"),
				Slug = text,
				Title = ReadString(hit, "title"),
				Description = ReadString(hit, "description"),
				Author = ReadString(hit, "author"),
				Downloads = ReadLong(hit, "downloads"),
				WebsiteUrl = "https://modrinth.com/" + projectType + "/" + text,
				Kind = kind,
				IconUrl = ReadString(hit, "icon_url")
			};
		})
			.ToList();
	}

	public async Task<List<ResourceContentVersion>> GetVersionsAsync(ResourceContentProject project, string minecraftVersion, CancellationToken ct = default(CancellationToken))
	{
		string value = (string.IsNullOrWhiteSpace(minecraftVersion) ? string.Empty : ("?game_versions=" + Uri.EscapeDataString(JsonSerializer.Serialize(new string[1] { minecraftVersion }))));
		using HttpResponseMessage response = await _http.GetAsync($"{"https://api.modrinth.com/v2"}/project/{Uri.EscapeDataString(project.ProjectId)}/version{value}", ct);
		response.EnsureSuccessStatusCode();
		using JsonDocument jsonDocument = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(ct));
		List<ResourceContentVersion> list = new List<ResourceContentVersion>();
		foreach (JsonElement item in jsonDocument.RootElement.EnumerateArray())
		{
			if (!item.TryGetProperty("files", out var value2) || value2.ValueKind != JsonValueKind.Array)
			{
				continue;
			}
			JsonElement element = value2.EnumerateArray().FirstOrDefault((JsonElement element2) => ReadBool(element2, "primary") && ReadString(element2, "filename").EndsWith(".zip", StringComparison.OrdinalIgnoreCase));
			if (element.ValueKind == JsonValueKind.Undefined)
			{
				element = value2.EnumerateArray().FirstOrDefault((JsonElement element2) => ReadString(element2, "filename").EndsWith(".zip", StringComparison.OrdinalIgnoreCase));
			}
			if (element.ValueKind != JsonValueKind.Undefined)
			{
				list.Add(new ResourceContentVersion
				{
					VersionId = ReadString(item, "id"),
					VersionNumber = ReadString(item, "version_number"),
					FileName = ReadString(element, "filename"),
					DownloadUrl = ReadString(element, "url"),
					Sha1 = (element.TryGetProperty("hashes", out var value3) ? ReadString(value3, "sha1") : string.Empty),
					FileSize = ReadLong(element, "size")
				});
			}
		}
		return list;
	}

	public async Task<string> DownloadAsync(GameInstance instance, ResourceContentProject project, ResourceContentVersion version, string defaultGameDirectory, IProgress<double>? progress = null, CancellationToken ct = default(CancellationToken))
	{
		using HttpResponseMessage response = await _http.GetAsync(version.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct);
		response.EnsureSuccessStatusCode();
		string tempDirectory = Path.Combine(Path.GetTempPath(), "mcmini-content-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(tempDirectory);
		string temp = Path.Combine(tempDirectory, "download.zip");
		try
		{
			await using (Stream input = await response.Content.ReadAsStreamAsync(ct))
			{
				await using FileStream output = File.Create(temp);
				byte[] buffer = new byte[81920];
				long expected = response.Content.Headers.ContentLength ?? version.FileSize;
				long received = 0L;
				while (true)
				{
					int num;
					int read = (num = await input.ReadAsync(buffer, ct));
					if (num <= 0)
					{
						break;
					}
					await output.WriteAsync(buffer.AsMemory(0, read), ct);
					received += read;
					if (expected > 0)
					{
						progress?.Report(Math.Min(1.0, (double)received / (double)expected));
					}
				}
			}
			if (!string.IsNullOrWhiteSpace(version.Sha1))
			{
				await using FileStream output = File.OpenRead(temp);
				if (!Convert.ToHexString(await SHA1.HashDataAsync(output, ct)).Equals(version.Sha1, StringComparison.OrdinalIgnoreCase))
				{
					throw new InvalidDataException("资源文件 SHA1 校验失败。");
				}
			}
			string text = Path.Combine(tempDirectory, SafeZipName(version.FileName));
			File.Move(temp, text, overwrite: true);
			temp = text;
			return (project.Kind == ResourceContentKind.Shader) ? new ShaderPackService(defaultGameDirectory).Import(instance, temp) : new ResourcePackService(defaultGameDirectory).ImportResourcePack(instance, temp);
		}
		finally
		{
			if (File.Exists(temp))
			{
				File.Delete(temp);
			}
			if (Directory.Exists(tempDirectory))
			{
				Directory.Delete(tempDirectory, recursive: true);
			}
		}
	}

	private static string SafeZipName(string name)
	{
		name = Path.GetFileName(name);
		if (string.IsNullOrWhiteSpace(name))
		{
			name = "download.zip";
		}
		if (!name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
		{
			name += ".zip";
		}
		char[] invalidFileNameChars = Path.GetInvalidFileNameChars();
		foreach (char oldChar in invalidFileNameChars)
		{
			name = name.Replace(oldChar, '_');
		}
		return name;
	}

	private static string ReadString(JsonElement element, string name)
	{
		if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String)
		{
			return string.Empty;
		}
		return value.GetString() ?? string.Empty;
	}

	private static long ReadLong(JsonElement element, string name)
	{
		if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(name, out var value) || !value.TryGetInt64(out var value2))
		{
			return 0L;
		}
		return value2;
	}

	private static bool ReadBool(JsonElement element, string name)
	{
		if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value))
		{
			return value.ValueKind == JsonValueKind.True;
		}
		return false;
	}
}
