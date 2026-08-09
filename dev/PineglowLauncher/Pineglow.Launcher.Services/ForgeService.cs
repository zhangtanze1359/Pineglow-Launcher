using System;
using System.Net.Http;
using Pineglow.Launcher.Models;

namespace Pineglow.Launcher.Services;

public sealed class ForgeService : InstallerLoaderService
{
	public ForgeService(HttpClient http, string gameDirectory, MinecraftDownloader downloader, Action<string> log)
		: base(http, gameDirectory, downloader, log, "https://maven.minecraftforge.net/net/minecraftforge/forge/maven-metadata.xml", "https://maven.minecraftforge.net", "net/minecraftforge/forge", "forge", ModLoaderKind.Forge, "Forge")
	{
	}

	protected override string? GetMinecraftVersion(string mavenVersion)
	{
		int num = mavenVersion.IndexOf('-', StringComparison.Ordinal);
		if (num <= 0)
		{
			return null;
		}
		return mavenVersion.Substring(0, num);
	}

	protected override string GetDisplayLoaderVersion(string mavenVersion, string minecraftVersion)
	{
		string text = minecraftVersion + "-";
		if (!mavenVersion.StartsWith(text, StringComparison.OrdinalIgnoreCase))
		{
			return mavenVersion;
		}
		return mavenVersion.Substring(text.Length);
	}

	protected override string GuessVersionId(string minecraftVersion, string mavenVersion)
	{
		return minecraftVersion + "-forge-" + GetDisplayLoaderVersion(mavenVersion, minecraftVersion);
	}
}
