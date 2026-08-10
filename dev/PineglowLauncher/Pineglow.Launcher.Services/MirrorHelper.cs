using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Pineglow.Launcher.Services;

public sealed class DownloadSourceInfo
{
	public string Key = string.Empty;
	public string Name = string.Empty;
	public string BaseUrl = string.Empty;
	public long LatencyMs = -1;
	public bool Available = true;
	public string Error = string.Empty;
}

/// <summary>
/// 下载源管理：官方 + 国内镜像（BMCLAPI / MCBBS）。
/// 选定镜像后，将 mojang/forge/fabric 等 URL 重写为镜像地址。
/// </summary>
public static class MirrorHelper
{
	private const string Official = "official";

	public static string SelectedKey { get; set; } = Official;

	public static bool Enabled => !SelectedKey.Equals(Official, StringComparison.OrdinalIgnoreCase);

	public static string GetBaseUrl(string key)
	{
		return key.ToLowerInvariant() switch
		{
			"bmclapi" => "https://bmclapi2.bangbang93.com",
			"mcbbs" => "https://download.mcbbs.net",
			_ => string.Empty,
		};
	}

	public static IReadOnlyList<DownloadSourceInfo> ListSources()
	{
		return new List<DownloadSourceInfo>
		{
			new DownloadSourceInfo { Key = Official, Name = "官方源", BaseUrl = "https://piston-meta.mojang.com" },
			new DownloadSourceInfo { Key = "bmclapi", Name = "BMCLAPI", BaseUrl = "https://bmclapi2.bangbang93.com" },
			new DownloadSourceInfo { Key = "mcbbs", Name = "MCBBS 镜像", BaseUrl = "https://download.mcbbs.net" },
		};
	}

	/// <summary>对给定 URL 探测延迟（HEAD），返回毫秒；失败返回 -1。</summary>
	public static async Task<long> MeasureLatencyAsync(HttpClient http, string baseUrl, CancellationToken ct = default)
	{
		if (string.IsNullOrWhiteSpace(baseUrl))
		{
			return -1;
		}

		string probe = baseUrl.TrimEnd('/') + "/version_manifest_v2.json";
		Stopwatch sw = Stopwatch.StartNew();
		try
		{
			using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Head, probe);
			using HttpResponseMessage response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
			sw.Stop();
			return (int)sw.ElapsedMilliseconds;
		}
		catch
		{
			sw.Stop();
			return -1;
		}
	}

	public static string Rewrite(string url)
	{
		if (!Enabled)
		{
			return url;
		}
		return RewriteCore(url);
	}

	public static string RewriteIfEnabled(string url, bool useMirror)
	{
		if (!useMirror)
		{
			return url;
		}
		return RewriteCore(url);
	}

	public static IReadOnlyList<string> GetCandidates(string url)
	{
		if (string.IsNullOrWhiteSpace(url))
		{
			return Array.Empty<string>();
		}
		string text = RewriteCore(url);
		return ((!Enabled) ? new string[2] { url, text } : new string[2] { text, url }).Distinct<string>(StringComparer.OrdinalIgnoreCase).ToArray();
	}

	private static string RewriteCore(string url)
	{
		string baseUrl = GetBaseUrl(SelectedKey);
		if (string.IsNullOrWhiteSpace(baseUrl))
		{
			return url;
		}
		if (url.Contains("piston-meta.mojang.com", StringComparison.Ordinal))
		{
			return ReplaceHost(url, baseUrl);
		}
		if (url.Contains("launchermeta.mojang.com", StringComparison.Ordinal))
		{
			return ReplaceHost(url, baseUrl);
		}
		if (url.Contains("launcher.mojang.com", StringComparison.Ordinal))
		{
			return ReplaceHost(url, baseUrl);
		}
		if (url.Contains("resources.download.minecraft.net", StringComparison.Ordinal))
		{
			return ReplaceHost(url, baseUrl + "/assets");
		}
		if (url.Contains("libraries.minecraft.net", StringComparison.Ordinal))
		{
			return ReplaceHost(url, baseUrl + "/libraries");
		}
		if (url.Contains("maven.minecraftforge.net", StringComparison.Ordinal))
		{
			return ReplaceHost(url, baseUrl + "/maven");
		}
		if (url.Contains("maven.fabricmc.net", StringComparison.Ordinal))
		{
			return ReplaceHost(url, baseUrl + "/maven");
		}
		if (url.Contains("maven.neoforged.net", StringComparison.Ordinal))
		{
			return ReplaceHost(url, baseUrl + "/maven");
		}
		if (url.Contains("meta.fabricmc.net", StringComparison.Ordinal))
		{
			return ReplaceHost(url, baseUrl);
		}
		if (url.Contains("meta.quiltmc.org", StringComparison.Ordinal))
		{
			return ReplaceHost(url, baseUrl + "/quilt-meta");
		}
		return url;
	}

	private static string ReplaceHost(string url, string newHost)
	{
		if (!Uri.TryCreate(url, UriKind.Absolute, out Uri result))
		{
			return url;
		}
		if (!Uri.TryCreate(newHost, UriKind.Absolute, out Uri result2))
		{
			return url;
		}
		string text = result2.AbsolutePath.TrimEnd('/');
		return new UriBuilder(result)
		{
			Scheme = result2.Scheme,
			Host = result2.Host,
			Port = (result2.IsDefaultPort ? (-1) : result2.Port),
			Path = text + "/" + result.AbsolutePath.TrimStart('/')
		}.Uri.ToString();
	}
}
