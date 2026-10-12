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
using System.Text.RegularExpressions;
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
		// 现代 Forge/NeoForge 的 profile 由安装器提供**打过补丁的 client**（`*-client.jar`，确实含类文件）。
		// 只有"**当前版本/加载器**对应的那份产物可解析、且确实含类"时才把父版本原版 `<mc>.jar` 移出 -cp；
		// 否则 ModLauncher 会把原版 jar 当成自动模块（例如 `_1._20._1`）与 `minecraft` 模块冲突：
		//   java.lang.module.ResolutionException: Modules _1._20._1 and minecraft export package … to module …
		// 判定**绑定当前安装元数据**，不按"任意 *-extra.jar/*-client.jar 存在"下结论：
		//   - 无关文件、旧版本 extra jar、只有资源的 extra jar（0 个 .class）都不得改变保留状态；
		//   - 声明的产物存在但没有类文件 ⇒ 视为安装损坏，装配前明确拒绝（不静默生成错误 classpath）；
		//   - 解析不到当前产物 ⇒ 合法回退：保留原版 jar（Vanilla/Fabric/Quilt 本来就走这条路）。
		ClientSupplyState clientSupply = ResolveClientSupply(profile.GameDirectory, minecraftVersion, profile.VersionId);
		if (clientSupply == ClientSupplyState.Unusable)
		{
			throw new InvalidOperationException(
				"当前加载器客户端产物不可用：已找到 *-client.jar 但不含任何类文件，无法提供游戏类。请重新安装该加载器。");
		}
		if (clientSupply == ClientSupplyState.Missing)
		{
			_log("未找到当前加载器版本的 *-client.jar（" + profile.VersionId + "），已回退为保留原版客户端 jar。");
		}
		string? classpathClientJar = (clientSupply == ClientSupplyState.Provided) ? null : versionJarPath;
		string text2 = BuildClasspath(profile.GameDirectory, minecraftVersion, classpathClientJar);
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
		_ = Task.Run(async delegate
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
		// 这里**不能**对整张参数表去重：现代 Forge/NeoForge 的版本 JSON 会**重复使用同一个选项 token**
		// （`--add-opens` / `--add-exports` 各出现两次，各自跟一个模块 spec）。整表去重会把第二个 token 吃掉，
		// JVM 随后把它的取值当成主类并立刻退出：
		//   错误: 找不到或无法加载主类 java.base/java.lang.invoke=cpw.mods.securejarhandler
		// 语义 token（选项与其取值）必须原样、按序保留。
		return list;
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
			["resolution_height"] = profile.WindowHeight.ToString(),
			// Forge/NeoForge 的版本 JSON 还用到这两个 vanilla 不用的占位符；缺了它们，
			// `-p` 的模块路径会是字面量 `${library_directory}/…`，securejarhandler 就不是模块，启动报：
			//   java.lang.reflect.InaccessibleObjectException: … module java.base does not "opens java.lang.invoke" to unnamed module
			["library_directory"] = Path.Combine(profile.GameDirectory, "libraries"),
			["classpath_separator"] = (OperatingSystem.IsWindows() ? ";" : ":")
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

	/// <summary>客户端类供应链的判定结果。</summary>
	private enum ClientSupplyState
	{
		/// <summary>profile 没有声明自供 client（Vanilla / Fabric / Quilt）⇒ 保留原版 jar。</summary>
		NotDeclared,
		/// <summary>当前版本/加载器的 `*-client.jar` 可解析且确实含类 ⇒ 可把原版 jar 移出 -cp。</summary>
		Provided,
		/// <summary>声明了自供 client，但解析不到**当前版本**对应的产物 ⇒ 合法回退：保留原版 jar。</summary>
		Missing,
		/// <summary>产物在，但不含任何类文件 ⇒ 安装损坏，装配前明确拒绝。</summary>
		Unusable
	}

	/// <summary>
	/// 判定当前 profile 的**客户端类供应链**。判定必须**绑定当前版本/加载器安装元数据**：
	///
	///  1. profile 自己声明：版本 JSON 的 `arguments.jvm` 里有 `-DignoreList=…,client-extra,…`
	///     （加载器在说"游戏类由我的补丁产物提供"）；没声明 ⇒ <see cref="ClientSupplyState.NotDeclared"/>；
	///  2. 从**当前版本 id**（或版本 JSON 里加载器自身的 library 条目）解析出**当前加载器**的客户端产物目录，
	///     并只接受同一目录下与**同一版本 token** 同名的 `*-client.jar`：
	///       - `<mc>-forge-<ver>`  → `libraries/net/minecraftforge/forge/<mc>-<ver>/forge-<mc>-<ver>-client.jar`
	///       - `neoforge-<ver>`    → `libraries/net/neoforged/neoforge/<ver>/neoforge-<ver>-client.jar`
	///       - `<mc>-neoforge-<ver>` → 同上（NeoForge 风格）
	///     **不扫描任意目录、不按 `*-extra.jar` 后缀肯定**：无关文件（如 `unrelated-tool-extra.jar`）、
	///     旧版本 extra jar（如 1.21.1 实例里的 `client-1.20.1-…-extra.jar`）都不得改变判定；
	///  3. 解析到的产物还必须**确实含类文件**（真实 Forge/NeoForge 的 `client-*-extra.jar` 是 0 个 `.class` 的
	///     资源 jar，不能当类供应链；而 `forge-*-client.jar` / `neoforge-*-client.jar` 全是类文件）。
	///
	/// 实测（2026-10-07）：Forge 1.20.1 → `forge-1.20.1-47.4.3-client.jar`（1438/1438 个 .class）；
	/// NeoForge 21.1 → `neoforge-21.1.230-client.jar`（1601/1601 个 .class）。
	/// </summary>
	private static ClientSupplyState ResolveClientSupply(string gameDirectory, MinecraftVersion version, string versionId)
	{
		if (!DeclaresOwnClient(version))
		{
			return ClientSupplyState.NotDeclared;
		}

		string? artifactDirectory = null;
		string? jarPrefix = null;
		if (TryResolveLoaderClientCoordinates(gameDirectory, version, versionId, out artifactDirectory, out jarPrefix))
		{
			string expected = Path.Combine(artifactDirectory!, jarPrefix! + "-client.jar");
			if (File.Exists(expected))
			{
				return JarContainsClasses(expected) ? ClientSupplyState.Provided : ClientSupplyState.Unusable;
			}
		}
		return ClientSupplyState.Missing;
	}

	/// <summary>profile 是否声明"游戏类由加载器补丁产物提供"（`-DignoreList=…,client-extra,…`）。</summary>
	private static bool DeclaresOwnClient(MinecraftVersion version)
	{
		JsonElement? arguments = version.Arguments;
		if (!arguments.HasValue || arguments.Value.ValueKind != JsonValueKind.Object
			|| !arguments.Value.TryGetProperty("jvm", out JsonElement jvm) || jvm.ValueKind != JsonValueKind.Array)
		{
			return false;
		}
		foreach (JsonElement element in jvm.EnumerateArray())
		{
			if (element.ValueKind != JsonValueKind.String)
			{
				continue;
			}
			string text = element.GetString() ?? string.Empty;
			if (text.StartsWith("-DignoreList=", StringComparison.Ordinal)
				&& text.Contains("client-extra", StringComparison.OrdinalIgnoreCase))
			{
				return true;
			}
		}
		return false;
	}

	/// <summary>
	/// 解析**当前加载器**客户端产物所在目录与文件名前缀。优先用版本 JSON 里加载器自身的 library 条目
	/// （`net.minecraftforge:forge` / `net.neoforged:neoforge`），否则按版本 id 的已知形态推导。
	/// </summary>
	private static bool TryResolveLoaderClientCoordinates(string gameDirectory, MinecraftVersion version, string versionId, out string? directory, out string? jarPrefix)
	{
		directory = null;
		jarPrefix = null;

		// 优先：版本 JSON 里加载器自身的 library 条目（若存在）
		foreach (JsonElement library in version.Libraries)
		{
			if (!RulesEvaluator.IsAllowed(library))
			{
				continue;
			}
			string name = JsonUtil.GetString(library, "name") ?? string.Empty;
			if (!name.StartsWith("net.minecraftforge:forge:", StringComparison.OrdinalIgnoreCase)
				&& !name.StartsWith("net.neoforged:neoforge:", StringComparison.OrdinalIgnoreCase))
			{
				continue;
			}
			string artifactPath = TryGetLibraryArtifactPath(gameDirectory, library) ?? string.Empty;
			if (string.IsNullOrWhiteSpace(artifactPath))
			{
				continue;
			}
			directory = Path.GetDirectoryName(artifactPath);
			string fileName = Path.GetFileNameWithoutExtension(artifactPath);
			jarPrefix = fileName.EndsWith("-universal", StringComparison.OrdinalIgnoreCase)
				? fileName.Substring(0, fileName.Length - "-universal".Length)
				: fileName;
			return !string.IsNullOrWhiteSpace(directory) && !string.IsNullOrWhiteSpace(jarPrefix);
		}

		// 否则：按版本 id 的已知形态推导（Forge：`<mc>-forge-<ver>`；NeoForge：`neoforge-<ver>` / `<mc>-neoforge-<ver>`）
		string id = versionId.Trim();
		Match forge = Regex.Match(id, "^(?<mc>\\d+\\.\\d+(?:\\.\\d+)?)-forge-(?<ver>.+)$", RegexOptions.IgnoreCase);
		if (forge.Success)
		{
			string mavenVersion = forge.Groups["mc"].Value + "-" + forge.Groups["ver"].Value;
			directory = Path.Combine(gameDirectory, "libraries", "net", "minecraftforge", "forge", mavenVersion);
			jarPrefix = "forge-" + mavenVersion;
			return true;
		}
		Match neo = Regex.Match(id, "^(?:(?<mc>\\d+\\.\\d+(?:\\.\\d+)?)-)?neoforge-(?<ver>.+)$", RegexOptions.IgnoreCase);
		if (neo.Success)
		{
			string mavenVersion = neo.Groups["ver"].Value;
			directory = Path.Combine(gameDirectory, "libraries", "net", "neoforged", "neoforge", mavenVersion);
			jarPrefix = "neoforge-" + mavenVersion;
			return true;
		}
		return false;
	}

	/// <summary>该 jar 里是否存在 `.class` 条目（只读中央目录，不做全量解压）。</summary>
	private static bool JarContainsClasses(string jarPath)
	{
		try
		{
			using ZipArchive archive = ZipFile.OpenRead(jarPath);
			foreach (ZipArchiveEntry entry in archive.Entries)
			{
				if (entry.FullName.EndsWith(".class", StringComparison.OrdinalIgnoreCase))
				{
					return true;
				}
			}
		}
		catch (IOException)
		{
			return false;
		}
		catch (InvalidDataException)
		{
			return false;
		}
		return false;
	}

	private static string BuildClasspath(string gameDirectory, MinecraftVersion version, string? clientJar)
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
		if (!string.IsNullOrWhiteSpace(clientJar))
		{
			list.Add(clientJar);
		}
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
