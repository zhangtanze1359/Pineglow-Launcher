using System;
using System.IO;
using System.Runtime.InteropServices;

namespace Pineglow.Launcher.Services;

public static class PlatformPaths
{
	private const string PortableMarkerFileName = "portable.flag";

	private static bool IsPortableMode => File.Exists(Path.Combine(AppContext.BaseDirectory, "portable.flag"));

	public static string GetDefaultMinecraftDirectory()
	{
		if (IsPortableMode)
		{
			return Path.Combine(AppContext.BaseDirectory, "data", ".minecraft");
		}
		if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
		{
			return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ".minecraft");
		}
		string folderPath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
		if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
		{
			return Path.Combine(folderPath, "Library", "Application Support", "minecraft");
		}
		return Path.Combine(folderPath, ".minecraft");
	}

	public static string GetConfigDirectory()
	{
		if (IsPortableMode)
		{
			return Path.Combine(AppContext.BaseDirectory, "data");
		}
		string folderPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
		if (string.IsNullOrWhiteSpace(folderPath))
		{
			folderPath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
		}
		return Path.Combine(folderPath, "MCMiniLauncher");
	}

	public static string GetConfigPath()
	{
		return Path.Combine(GetConfigDirectory(), "config.json");
	}

	public static string GetOsRuleName()
	{
		if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
		{
			return "windows";
		}
		if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
		{
			return "osx";
		}
		return "linux";
	}

	public static string GetNativeClassifierKey()
	{
		if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
		{
			return "natives-windows";
		}
		if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
		{
			return "natives-macos";
		}
		return "natives-linux";
	}

	public static string GetScriptExtension()
	{
		if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
		{
			return ".sh";
		}
		return ".bat";
	}
}
