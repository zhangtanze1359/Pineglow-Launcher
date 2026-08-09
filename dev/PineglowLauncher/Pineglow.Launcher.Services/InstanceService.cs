using System.Linq;
using Pineglow.Launcher.Models;

namespace Pineglow.Launcher.Services;

public static class InstanceService
{
	public static void EnsureDefaultInstance(AppConfig config)
	{
		if (string.IsNullOrWhiteSpace(config.GameDirectory))
		{
			config.GameDirectory = PlatformPaths.GetDefaultMinecraftDirectory();
		}
		if (config.Instances.Count == 0)
		{
			GameInstance gameInstance = new GameInstance
			{
				Name = (string.IsNullOrWhiteSpace(config.SelectedVersion) ? "默认游戏" : (config.SelectedVersion + " 原版")),
				MinecraftVersion = config.SelectedVersion,
				GameDirectory = config.GameDirectory,
				JavaPath = config.JavaPath,
				MaxMemoryMb = ((config.MaxMemoryMb <= 0) ? 4096 : config.MaxMemoryMb),
				UseAutoMemory = config.UseAutoMemory,
				WindowWidth = ((config.WindowWidth <= 0) ? 1280 : config.WindowWidth),
				WindowHeight = ((config.WindowHeight <= 0) ? 720 : config.WindowHeight),
				ServerAddress = config.ServerHost,
				ServerPort = ParsePort(config.ServerPort),
				AccountMode = (string.IsNullOrWhiteSpace(config.AccountMode) ? "offline" : config.AccountMode),
				LoaderType = "Vanilla",
				BaseMinecraftVersion = config.SelectedVersion
			};
			config.Instances.Add(gameInstance);
			config.SelectedInstanceId = gameInstance.Id;
		}
		if (GetSelectedInstance(config) == null)
		{
			config.SelectedInstanceId = config.Instances[0].Id;
		}
	}

	public static GameInstance? GetSelectedInstance(AppConfig config)
	{
		return config.Instances.FirstOrDefault((GameInstance x) => x.Id == config.SelectedInstanceId) ?? config.Instances.FirstOrDefault();
	}

	public static GameInstance CreateDefault(string? version, string defaultGameDirectory)
	{
		string name = (string.IsNullOrWhiteSpace(version) ? "新游戏配置" : (version + " 原版"));
		return new GameInstance
		{
			Name = name,
			MinecraftVersion = (version ?? string.Empty),
			GameDirectory = (string.IsNullOrWhiteSpace(defaultGameDirectory) ? PlatformPaths.GetDefaultMinecraftDirectory() : defaultGameDirectory),
			MaxMemoryMb = MemoryAllocationService.Recommend(version, "Vanilla", 0).RecommendedMb,
			UseAutoMemory = true,
			WindowWidth = 1280,
			WindowHeight = 720,
			ServerPort = 25565,
			AccountMode = "offline",
			LoaderType = "Vanilla",
			BaseMinecraftVersion = (version ?? string.Empty)
		};
	}

	public static GameInstance DuplicateInstance(GameInstance source)
	{
		return new GameInstance
		{
			Name = (string.IsNullOrWhiteSpace(source.Name) ? "游戏配置 副本" : (source.Name + " 副本")),
			MinecraftVersion = source.MinecraftVersion,
			GameDirectory = source.GameDirectory,
			JavaPath = source.JavaPath,
			MaxMemoryMb = source.MaxMemoryMb,
			UseAutoMemory = source.UseAutoMemory,
			WindowWidth = source.WindowWidth,
			WindowHeight = source.WindowHeight,
			ServerAddress = source.ServerAddress,
			ServerPort = source.ServerPort,
			AccountMode = source.AccountMode,
			LoaderType = (string.IsNullOrWhiteSpace(source.LoaderType) ? "Vanilla" : source.LoaderType),
			LoaderVersion = source.LoaderVersion,
			BaseMinecraftVersion = source.BaseMinecraftVersion,
			LastLaunchTime = null,
			Notes = source.Notes,
			CustomJvmArgs = source.CustomJvmArgs,
			CustomGameArgs = source.CustomGameArgs,
			Modpack = ModpackLifecycleService.CloneSource(source.Modpack)
		};
	}

	public static int ParsePort(string? text)
	{
		if (!int.TryParse(text, out var result) || result <= 0)
		{
			return 25565;
		}
		return result;
	}
}
