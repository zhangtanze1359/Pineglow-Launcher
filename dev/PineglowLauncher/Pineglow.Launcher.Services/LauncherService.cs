using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Pineglow.Launcher.Models;

namespace Pineglow.Launcher.Services;

public sealed class LauncherService
{
	/// <summary>启动器版本，单一来源：程序集版本（AssemblyInfo.cs / csproj Version）。</summary>
	public static string LauncherVersion { get; } = typeof(LauncherService).Assembly.GetName().Version?.ToString(3) ?? "1.0.0";

	private readonly Action<string> _log;

	public LauncherService(Action<string> log)
	{
		_log = log;
	}

	public async Task<LaunchCommand> BuildCommandAsync(LaunchProfile profile, CancellationToken ct = default(CancellationToken))
	{
		MinecraftVersion minecraftVersion = await VersionResolver.ResolveAsync(profile.GameDirectory, profile.VersionId);
		if (string.IsNullOrWhiteSpace(minecraftVersion.MainClass))
		{
			throw new InvalidOperationException("游戏版本文件损坏，请重新安装该版本。");
		}
		string versionJarPath = VersionResolver.GetVersionJarPath(profile.GameDirectory, profile.VersionId);
		if (!File.Exists(versionJarPath) && !string.IsNullOrWhiteSpace(minecraftVersion.InheritsFrom))
		{
			versionJarPath = VersionResolver.GetVersionJarPath(profile.GameDirectory, minecraftVersion.InheritsFrom);
		}
		if (!File.Exists(versionJarPath))
		{
			throw new FileNotFoundException("未找到 client.jar：" + versionJarPath);
		}
		string text = Path.Combine(profile.GameDirectory, "versions", profile.VersionId, "natives");
		Directory.CreateDirectory(text);
		ExtractNatives(profile.GameDirectory, minecraftVersion, text);
		string text2 = BuildClasspath(profile.GameDirectory, minecraftVersion, versionJarPath);
		Dictionary<string, string> replacements = BuildReplacements(profile, minecraftVersion, text, text2);
		List<string> list = new List<string>();
		list.Add($"-Xmx{Math.Max(512, profile.MaxMemoryMb)}M");
		list.Add("-Dfile.encoding=UTF-8");
		if (!string.IsNullOrWhiteSpace(profile.CustomJvmArgs))
		{
			foreach (string item in SplitShellArgs(profile.CustomJvmArgs))
			{
				if (!string.IsNullOrWhiteSpace(item))
				{
					list.Add(item);
				}
			}
		}
		List<string> collection = ResolveJvmArguments(minecraftVersion, replacements, text, text2, profile);
		list.AddRange(collection);
		if (!list.Contains("-cp") && !list.Contains("-classpath"))
		{
			list.Add("-cp");
			list.Add(text2);
		}
		list.Add(minecraftVersion.MainClass);
		list.AddRange(ResolveGameArguments(minecraftVersion, replacements, profile));
		if (!string.IsNullOrWhiteSpace(profile.CustomGameArgs))
		{
			foreach (string item2 in SplitShellArgs(profile.CustomGameArgs))
			{
				if (!string.IsNullOrWhiteSpace(item2))
				{
					list.Add(item2);
				}
			}
		}
		return new LaunchCommand
		{
			JavaPath = profile.JavaPath,
			Arguments = list,
			WorkingDirectory = profile.GameDirectory
		};
	}

	public async Task<Process> LaunchAsync(LaunchProfile profile, CancellationToken ct = default(CancellationToken))
	{
		LaunchCommand launchCommand = await BuildCommandAsync(profile, ct);
		_log("启动命令：");
		_log(launchCommand.ToDisplayString());
		ProcessStartInfo processStartInfo = new ProcessStartInfo
		{
			FileName = launchCommand.JavaPath,
			WorkingDirectory = launchCommand.WorkingDirectory,
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			UseShellExecute = false,
			CreateNoWindow = true
		};
		foreach (string argument in launchCommand.Arguments)
		{
			processStartInfo.ArgumentList.Add(argument);
		}
		Process process = Process.Start(processStartInfo) ?? throw new InvalidOperationException("Java 进程启动失败。");
		Task stdoutTask = Task.Run(async delegate
		{
			await PipeAsync(process.StandardOutput, ct);
		}, ct);
		Task stderrTask = Task.Run(async delegate
		{
			await PipeAsync(process.StandardError, ct);
		}, ct);
		Task.Run(async delegate
		{
			try
			{
				await process.WaitForExitAsync(ct);
				_log($"游戏进程已退出，ExitCode={process.ExitCode}");
			}
			catch (OperationCanceledException)
			{
			}
			catch (Exception ex2)
			{
				_log("等待游戏进程退出时出错：" + ex2.Message);
			}
			finally
			{
				InlineArray2<Task> buffer = default(InlineArray2<Task>);
				buffer[0] = stdoutTask;
				buffer[1] = stderrTask;
				await Task.WhenAll(buffer);
				process.Dispose();
			}
		}, ct);
		return process;
	}

	public async Task<string> ExportScriptAsync(LaunchProfile profile, string outputPath, CancellationToken ct = default(CancellationToken))
	{
		if (!profile.AccountMode.Equals("offline", StringComparison.OrdinalIgnoreCase))
		{
			throw new InvalidOperationException("已登录账号不能导出启动脚本，因为脚本会泄露登录令牌。请改用启动器启动游戏。");
		}
		await File.WriteAllTextAsync(outputPath, (await BuildCommandAsync(profile, ct)).ToScript(), Encoding.UTF8, ct);
		if (!OperatingSystem.IsWindows())
		{
			try
			{
				File.SetUnixFileMode(outputPath, UnixFileMode.OtherExecute | UnixFileMode.OtherRead | UnixFileMode.GroupExecute | UnixFileMode.GroupRead | UnixFileMode.UserExecute | UnixFileMode.UserWrite | UnixFileMode.UserRead);
			}
			catch
			{
			}
		}
		return outputPath;
	}

	private async Task PipeAsync(StreamReader reader, CancellationToken ct)
	{
		while (!ct.IsCancellationRequested)
		{
			string text = await reader.ReadLineAsync(ct);
			if (text != null)
			{
				_log(text);
				continue;
			}
			break;
		}
	}

	private static List<string> ResolveJvmArguments(MinecraftVersion version, Dictionary<string, string> replacements, string nativesDir, string classpath, LaunchProfile profile)
	{
		List<string> list = new List<string>();
		JsonElement? arguments = version.Arguments;
		if (arguments.HasValue)
		{
			JsonElement valueOrDefault = arguments.GetValueOrDefault();
			if (valueOrDefault.ValueKind == JsonValueKind.Object && valueOrDefault.TryGetProperty("jvm", out var value) && value.ValueKind == JsonValueKind.Array)
			{
				list.AddRange(FlattenArguments(value, replacements, BuildLaunchFeatures(profile)));
				goto IL_0091;
			}
		}
		list.Add("-Djava.library.path=" + nativesDir);
		list.Add("-Dminecraft.launcher.brand=PineglowLauncher");
		list.Add("-Dminecraft.launcher.version=" + LauncherVersion);
		list.Add("-cp");
		list.Add(classpath);
		goto IL_0091;
		IL_0091:
		return list.DistinctPreserveOrder().ToList();
	}

	private static List<string> ResolveGameArguments(MinecraftVersion version, Dictionary<string, string> replacements, LaunchProfile profile)
	{
		List<string> list = new List<string>();
		JsonElement? arguments = version.Arguments;
		if (arguments.HasValue)
		{
			JsonElement valueOrDefault = arguments.GetValueOrDefault();
			if (valueOrDefault.ValueKind == JsonValueKind.Object && valueOrDefault.TryGetProperty("game", out var value) && value.ValueKind == JsonValueKind.Array)
			{
				list.AddRange(FlattenArguments(value, replacements, BuildLaunchFeatures(profile)));
				goto IL_0177;
			}
		}
		if (!string.IsNullOrWhiteSpace(version.MinecraftArguments))
		{
			list.AddRange(SplitLegacyArguments(JsonUtil.ReplacePlaceholders(version.MinecraftArguments, replacements)));
		}
		else
		{
			list.AddRange(new string[18]
			{
				"--username",
				profile.PlayerName,
				"--version",
				profile.VersionId,
				"--gameDir",
				profile.GameDirectory,
				"--assetsDir",
				Path.Combine(profile.GameDirectory, "assets"),
				"--assetIndex",
				version.Assets ?? profile.VersionId,
				"--uuid",
				ResolvePlayerUuid(profile),
				"--accessToken",
				string.IsNullOrWhiteSpace(profile.AccessToken) ? "0" : profile.AccessToken,
				"--userType",
				string.IsNullOrWhiteSpace(profile.UserType) ? "legacy" : profile.UserType,
				"--versionType",
				version.Type ?? "release"
			});
		}
		goto IL_0177;
		IL_0177:
		if (profile.WindowWidth > 0 && profile.WindowHeight > 0)
		{
			AddOrReplaceArg(list, "--width", profile.WindowWidth.ToString());
			AddOrReplaceArg(list, "--height", profile.WindowHeight.ToString());
		}
		if (!string.IsNullOrWhiteSpace(profile.ServerHost))
		{
			list.Add("--server");
			list.Add(profile.ServerHost.Trim());
			if (!string.IsNullOrWhiteSpace(profile.ServerPort))
			{
				list.Add("--port");
				list.Add(profile.ServerPort.Trim());
			}
		}
		return list;
	}

	private static void AddOrReplaceArg(List<string> args, string key, string value)
	{
		int num = args.FindIndex((string x) => x.Equals(key, StringComparison.Ordinal));
		if (num >= 0)
		{
			if (num + 1 < args.Count)
			{
				args[num + 1] = value;
			}
			else
			{
				args.Add(value);
			}
		}
		else
		{
			args.Add(key);
			args.Add(value);
		}
	}

	private static List<string> FlattenArguments(JsonElement array, Dictionary<string, string> replacements, IReadOnlyDictionary<string, bool> features)
	{
		List<string> list = new List<string>();
		foreach (JsonElement item in array.EnumerateArray())
		{
			if (item.ValueKind == JsonValueKind.String)
			{
				list.Add(JsonUtil.ReplacePlaceholders(item.GetString() ?? string.Empty, replacements));
			}
			else
			{
				if (item.ValueKind != JsonValueKind.Object || !RulesEvaluator.IsAllowed(item, features) || !item.TryGetProperty("value", out var value))
				{
					continue;
				}
				if (value.ValueKind == JsonValueKind.String)
				{
					list.Add(JsonUtil.ReplacePlaceholders(value.GetString() ?? string.Empty, replacements));
				}
				else
				{
					if (value.ValueKind != JsonValueKind.Array)
					{
						continue;
					}
					foreach (JsonElement item2 in value.EnumerateArray())
					{
						if (item2.ValueKind == JsonValueKind.String)
						{
							list.Add(JsonUtil.ReplacePlaceholders(item2.GetString() ?? string.Empty, replacements));
						}
					}
				}
			}
		}
		return list.Where((string x) => !string.IsNullOrWhiteSpace(x)).ToList();
	}

	private static IReadOnlyDictionary<string, bool> BuildLaunchFeatures(LaunchProfile profile)
	{
		return new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase)
		{
			["is_demo_user"] = false,
			["has_custom_resolution"] = profile.WindowWidth > 0 && profile.WindowHeight > 0,
			["has_quick_plays_support"] = false,
			["is_quick_play_singleplayer"] = false,
			["is_quick_play_multiplayer"] = false,
			["is_quick_play_realms"] = false
		};
	}

	private static Dictionary<string, string> BuildReplacements(LaunchProfile profile, MinecraftVersion version, string nativesDir, string classpath)
	{
		string value = Path.Combine(profile.GameDirectory, "assets");
		string value2 = version.Assets ?? profile.VersionId;
		return JsonUtil.BuildReplacements(new Dictionary<string, string>
		{
			["auth_player_name"] = profile.PlayerName,
			["version_name"] = profile.VersionId,
			["game_directory"] = profile.GameDirectory,
			["assets_root"] = value,
			["assets_index_name"] = value2,
			["game_assets"] = value,
			["auth_uuid"] = ResolvePlayerUuid(profile),
			["auth_access_token"] = (string.IsNullOrWhiteSpace(profile.AccessToken) ? "0" : profile.AccessToken),
			["clientid"] = profile.ClientId ?? string.Empty,
			["auth_xuid"] = profile.Xuid ?? string.Empty,
			["user_type"] = (string.IsNullOrWhiteSpace(profile.UserType) ? "legacy" : profile.UserType),
			["user_properties"] = "{}",
			["auth_session"] = (string.IsNullOrWhiteSpace(profile.AccessToken) ? "0" : profile.AccessToken),
			["version_type"] = version.Type ?? "release",
			["natives_directory"] = nativesDir,
			["launcher_name"] = "PineglowLauncher",
			["launcher_version"] = LauncherVersion,
			["classpath"] = classpath,
			["resolution_width"] = profile.WindowWidth.ToString(),
			["resolution_height"] = profile.WindowHeight.ToString()
		});
	}

	private static string ResolvePlayerUuid(LaunchProfile profile)
	{
		if (!string.IsNullOrWhiteSpace(profile.PlayerUuid))
		{
			return profile.PlayerUuid.Replace("-", string.Empty, StringComparison.Ordinal);
		}
		return OfflineUuid(profile.PlayerName);
	}

	private static string BuildClasspath(string gameDirectory, MinecraftVersion version, string clientJar)
	{
		string separator = (OperatingSystem.IsWindows() ? ";" : ":");
		List<string> list = new List<string>();
		foreach (JsonElement library in version.Libraries)
		{
			if (RulesEvaluator.IsAllowed(library))
			{
				string text = TryGetLibraryArtifactPath(gameDirectory, library);
				if (!string.IsNullOrWhiteSpace(text) && File.Exists(text))
				{
					list.Add(text);
				}
			}
		}
		list.Add(clientJar);
		return string.Join(separator, list.Distinct<string>(StringComparer.OrdinalIgnoreCase));
	}

	private void ExtractNatives(string gameDirectory, MinecraftVersion version, string nativesDir)
	{
		foreach (JsonElement library in version.Libraries)
		{
			if (!RulesEvaluator.IsAllowed(library))
			{
				continue;
			}
			string text = TryGetNativeArtifactPath(gameDirectory, library);
			if (string.IsNullOrWhiteSpace(text) || !File.Exists(text))
			{
				continue;
			}
			try
			{
				using ZipArchive zipArchive = ZipFile.OpenRead(text);
				foreach (ZipArchiveEntry entry in zipArchive.Entries)
				{
					if (!string.IsNullOrWhiteSpace(entry.Name) && !entry.FullName.StartsWith("META-INF/", StringComparison.OrdinalIgnoreCase))
					{
						string destinationFileName = Path.Combine(nativesDir, entry.Name);
						entry.ExtractToFile(destinationFileName, overwrite: true);
					}
				}
			}
			catch (Exception ex)
			{
				_log("Native 解压失败：" + Path.GetFileName(text) + " - " + ex.Message);
			}
		}
	}

	private static string? TryGetLibraryArtifactPath(string gameDirectory, JsonElement library)
	{
		if (library.TryGetProperty("downloads", out var value) && value.ValueKind == JsonValueKind.Object && value.TryGetProperty("artifact", out var value2))
		{
			string text = JsonUtil.GetString(value2, "path");
			if (!string.IsNullOrWhiteSpace(text))
			{
				return Path.Combine(gameDirectory, "libraries", text.Replace('/', Path.DirectorySeparatorChar));
			}
		}
		string text2 = JsonUtil.GetString(library, "name");
		if (!string.IsNullOrWhiteSpace(text2))
		{
			return Path.Combine(gameDirectory, "libraries", MavenPath(text2));
		}
		return null;
	}

	private static string? TryGetNativeArtifactPath(string gameDirectory, JsonElement library)
	{
		if (!library.TryGetProperty("downloads", out var value) || value.ValueKind != JsonValueKind.Object)
		{
			return null;
		}
		string text = SelectNativeClassifier(library);
		if (string.IsNullOrWhiteSpace(text))
		{
			return null;
		}
		if (!value.TryGetProperty("classifiers", out var value2) || !value2.TryGetProperty(text, out var value3))
		{
			return null;
		}
		string text2 = JsonUtil.GetString(value3, "path");
		if (!string.IsNullOrWhiteSpace(text2))
		{
			return Path.Combine(gameDirectory, "libraries", text2.Replace('/', Path.DirectorySeparatorChar));
		}
		return null;
	}

	private static string? SelectNativeClassifier(JsonElement library)
	{
		return MinecraftUtils.SelectNativeClassifier(library);
	}

	private static string MavenPath(string name)
	{
		return MinecraftUtils.MavenPath(name);
	}

	private static IEnumerable<string> SplitLegacyArguments(string text)
	{
		List<string> list = new List<string>();
		StringBuilder stringBuilder = new StringBuilder();
		bool flag = false;
		foreach (char c in text)
		{
			if (c == '"')
			{
				flag = !flag;
			}
			else if (char.IsWhiteSpace(c) && !flag)
			{
				if (stringBuilder.Length > 0)
				{
					list.Add(stringBuilder.ToString());
					stringBuilder.Clear();
				}
			}
			else
			{
				stringBuilder.Append(c);
			}
		}
		if (stringBuilder.Length > 0)
		{
			list.Add(stringBuilder.ToString());
		}
		return list;
	}

	private static string OfflineUuid(string playerName)
	{
		byte[] array = MD5.HashData(Encoding.UTF8.GetBytes("OfflinePlayer:" + playerName));
		array[6] = (byte)((array[6] & 0xF) | 0x30);
		array[8] = (byte)((array[8] & 0x3F) | 0x80);
		return new Guid(array).ToString("N");
	}

	private static IEnumerable<string> SplitShellArgs(string raw)
	{
		List<string> list = new List<string>();
		StringBuilder stringBuilder = new StringBuilder();
		bool flag = false;
		foreach (char c in raw)
		{
			if (c == '"')
			{
				flag = !flag;
			}
			else if (!flag && char.IsWhiteSpace(c))
			{
				if (stringBuilder.Length > 0)
				{
					list.Add(stringBuilder.ToString());
					stringBuilder.Clear();
				}
			}
			else
			{
				stringBuilder.Append(c);
			}
		}
		if (stringBuilder.Length > 0)
		{
			list.Add(stringBuilder.ToString());
		}
		return list;
	}
}
