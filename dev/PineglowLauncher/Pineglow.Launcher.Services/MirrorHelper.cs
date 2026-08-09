using System;
using System.Collections.Generic;
using System.Linq;

namespace Pineglow.Launcher.Services;

public static class MirrorHelper
{
	private const string BmclApi = "https://bmclapi2.bangbang93.com";

	public static bool Enabled { get; set; }

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
		if (string.IsNullOrWhiteSpace(url))
		{
			return url;
		}
		if (url.Contains("piston-meta.mojang.com", StringComparison.Ordinal))
		{
			return ReplaceHost(url, "https://bmclapi2.bangbang93.com");
		}
		if (url.Contains("launchermeta.mojang.com", StringComparison.Ordinal))
		{
			return ReplaceHost(url, "https://bmclapi2.bangbang93.com");
		}
		if (url.Contains("launcher.mojang.com", StringComparison.Ordinal))
		{
			return ReplaceHost(url, "https://bmclapi2.bangbang93.com");
		}
		if (url.Contains("resources.download.minecraft.net", StringComparison.Ordinal))
		{
			return ReplaceHost(url, "https://bmclapi2.bangbang93.com/assets");
		}
		if (url.Contains("libraries.minecraft.net", StringComparison.Ordinal))
		{
			return ReplaceHost(url, "https://bmclapi2.bangbang93.com/libraries");
		}
		if (url.Contains("maven.minecraftforge.net", StringComparison.Ordinal))
		{
			return ReplaceHost(url, "https://bmclapi2.bangbang93.com/maven");
		}
		if (url.Contains("maven.fabricmc.net", StringComparison.Ordinal))
		{
			return ReplaceHost(url, "https://bmclapi2.bangbang93.com/maven");
		}
		if (url.Contains("maven.neoforged.net", StringComparison.Ordinal))
		{
			return ReplaceHost(url, "https://bmclapi2.bangbang93.com/maven");
		}
		if (url.Contains("meta.fabricmc.net", StringComparison.Ordinal))
		{
			return ReplaceHost(url, "https://bmclapi2.bangbang93.com");
		}
		if (url.Contains("meta.quiltmc.org", StringComparison.Ordinal))
		{
			return ReplaceHost(url, "https://bmclapi2.bangbang93.com/quilt-meta");
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
