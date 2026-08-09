using System;
using System.IO;
using System.Linq;

namespace Pineglow.Launcher.Services;

public static class MemoryAllocationService
{
	public const int MinimumMemoryMb = 1024;

	public const int MemoryStepMb = 256;

	public const int AbsoluteMaximumMemoryMb = 32768;

	public static int DetectTotalSystemMemoryMb()
	{
		long totalAvailableMemoryBytes = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
		if (totalAvailableMemoryBytes <= 0)
		{
			return 8192;
		}
		return (int)Math.Clamp(totalAvailableMemoryBytes / 1048576, 2048L, 131072L);
	}

	public static MemoryRecommendation Recommend(string? minecraftVersion, string? loaderType, int enabledModCount, int? totalSystemMemoryMb = null)
	{
		int num = Math.Max(2048, totalSystemMemoryMb ?? DetectTotalSystemMemoryMb());
		int num2 = ((num <= 4096) ? 1536 : ((num > 8192) ? Math.Max(3072, AlignUp(num / 4)) : 2048));
		int num3 = num2;
		int num4 = Math.Max(1024, AlignDown(Math.Min(32768, num - num3)));
		return new MemoryRecommendation(Math.Clamp(AlignUp(BaseMemoryForVersion(minecraftVersion) + LoaderMemory(loaderType) + Math.Min(6144, (Math.Max(0, enabledModCount) + 24) / 25 * 512)), 1024, num4), 1024, num4, num, Math.Max(0, enabledModCount));
	}

	public static int NormalizeManualMemory(int memoryMb, int maximumMb)
	{
		int max = Math.Max(1024, maximumMb);
		return Math.Clamp(AlignToNearest(memoryMb), 1024, max);
	}

	public static int CountEnabledMods(string? gameDirectory)
	{
		if (string.IsNullOrWhiteSpace(gameDirectory))
		{
			return 0;
		}
		try
		{
			string path = Path.Combine(gameDirectory, "mods");
			return Directory.Exists(path) ? Directory.EnumerateFiles(path, "*.jar", SearchOption.TopDirectoryOnly).Count() : 0;
		}
		catch
		{
			return 0;
		}
	}

	public static string FormatMemory(int memoryMb)
	{
		if (memoryMb % 1024 == 0)
		{
			return $"{memoryMb / 1024} GB";
		}
		return $"{(double)memoryMb / 1024.0:0.##} GB";
	}

	private static int BaseMemoryForVersion(string? version)
	{
		int num = MinecraftMinorVersion(version);
		if (num < 18)
		{
			if (num >= 13)
			{
				return 3072;
			}
			return 2048;
		}
		return 4096;
	}

	private static int LoaderMemory(string? loaderType)
	{
		switch (loaderType?.Trim().ToLowerInvariant() ?? string.Empty)
		{
		case "forge":
		case "neoforge":
			return 1024;
		case "fabric":
		case "quilt":
			return 512;
		case "optifine":
			return 256;
		default:
			return 0;
		}
	}

	private static int MinecraftMinorVersion(string? version)
	{
		if (string.IsNullOrWhiteSpace(version))
		{
			return 20;
		}
		string[] array = version.Split('.', '-', '_');
		if (array.Length < 2 || !int.TryParse(array[0], out var result) || result != 1 || !int.TryParse(array[1], out var result2))
		{
			return 20;
		}
		return result2;
	}

	private static int AlignUp(int value)
	{
		return (int)Math.Ceiling((double)value / 256.0) * 256;
	}

	private static int AlignDown(int value)
	{
		return Math.Max(1024, value / 256 * 256);
	}

	private static int AlignToNearest(int value)
	{
		return (int)Math.Round((double)value / 256.0, MidpointRounding.AwayFromZero) * 256;
	}
}
