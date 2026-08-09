using System;
using System.Net.Http;
using Pineglow.Launcher.Models;

namespace Pineglow.Launcher.Services;

public sealed class NeoForgeService : InstallerLoaderService
{
	public NeoForgeService(HttpClient http, string gameDirectory, MinecraftDownloader downloader, Action<string> log)
		: base(http, gameDirectory, downloader, log, "https://maven.neoforged.net/releases/net/neoforged/neoforge/maven-metadata.xml", "https://maven.neoforged.net/releases", "net/neoforged/neoforge", "neoforge", ModLoaderKind.NeoForge, "NeoForge")
	{
	}

	protected override string? GetMinecraftVersion(string mavenVersion)
	{
		string[] array = mavenVersion.Split('.');
		if (array.Length < 2)
		{
			return null;
		}
		string text = array[0];
		if (!(text == "20"))
		{
			if (text == "21")
			{
				if (array[1] == "0")
				{
					return "1.21";
				}
				return "1.21." + array[1];
			}
		}
		else
		{
			if (array[1] == "1")
			{
				return "1.20.1";
			}
			if (array[1] == "2")
			{
				return "1.20.2";
			}
			if (array[1] == "3")
			{
				return "1.20.3";
			}
			if (array[1] == "4")
			{
				return "1.20.4";
			}
			if (array[1] == "5")
			{
				return "1.20.5";
			}
			if (array[1] == "6")
			{
				return "1.20.6";
			}
		}
		return null;
	}

	protected override string GetDisplayLoaderVersion(string mavenVersion, string minecraftVersion)
	{
		return mavenVersion;
	}

	protected override string GuessVersionId(string minecraftVersion, string mavenVersion)
	{
		return "neoforge-" + mavenVersion;
	}

	protected override bool IsInstalledVersionId(string id, string minecraftVersion, string mavenVersion)
	{
		if (id.Contains("neoforge", StringComparison.OrdinalIgnoreCase))
		{
			if (!id.Contains(mavenVersion, StringComparison.OrdinalIgnoreCase))
			{
				return id.Contains(minecraftVersion, StringComparison.OrdinalIgnoreCase);
			}
			return true;
		}
		return false;
	}
}
