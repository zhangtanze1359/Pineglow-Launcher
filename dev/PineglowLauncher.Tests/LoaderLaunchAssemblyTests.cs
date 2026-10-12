using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using Pineglow.Launcher.Models;
using Pineglow.Launcher.Services;
using Xunit;

namespace Pineglow.Launcher.Tests;

/// <summary>
/// 聚焦回归：**加载器实例的 JVM 参数 / 占位符 / 类路径装配**（2026-10-07 限定修复 + 同日独立验收返工）。
///
/// 钉住的行为：
///  1. 现代 Forge/NeoForge 的版本 JSON **重复使用同一个选项 token**（`--add-opens`/`--add-exports` 各两次），
///     整表去重会把第二个 token 吃掉，JVM 随后把它的取值当成主类 ⇒ 启动立刻失败；
///  2. Forge/NeoForge 用到 `${library_directory}` / `${classpath_separator}` 两个 vanilla 不用的占位符；
///  3. **替代客户端识别必须绑定当前 profile/安装元数据**：
///     - 只有"当前 MC/加载器版本对应的 `*-client.jar`（且确实含类文件）"才能把原版 `<mc>.jar` 移出 `-cp`；
///     - **无关或旧版**的 `*-extra.jar` / `*-client.jar` 不得改变保留状态（独立验收 P2 反例）；
///     - 仅有资源（无 `.class`）的 extra jar 不构成类供应链；
///     - 声明的客户端产物存在但**不可用**（无类文件）时，装配阶段给出明确错误，而不是静默生成错误 classpath。
/// </summary>
public sealed class LoaderLaunchAssemblyTests : IDisposable
{
	private readonly string root = Path.Combine(Path.GetTempPath(), "pineglow-launchasm-" + Guid.NewGuid().ToString("N"));

	public LoaderLaunchAssemblyTests() => Directory.CreateDirectory(root);

	public void Dispose()
	{
		try { if (Directory.Exists(root)) Directory.Delete(root, true); } catch { }
	}

	private static readonly BindingFlags PrivateStatic = BindingFlags.NonPublic | BindingFlags.Static;

	private static MethodInfo Method(string name) =>
		typeof(LauncherService).GetMethod(name, PrivateStatic)
		?? throw new InvalidOperationException("找不到私有静态方法：" + name);

	private static MinecraftVersion Version(string jvmJson, string? librariesJson = null)
	{
		string json = "{\"id\":\"synthetic\",\"mainClass\":\"x.y.Z\",\"arguments\":{\"jvm\":" + jvmJson + "}," +
			"\"libraries\":" + (librariesJson ?? "[]") + "}";
		using JsonDocument doc = JsonDocument.Parse(json);
		var version = new MinecraftVersion
		{
			Id = "synthetic",
			MainClass = "x.y.Z",
			Arguments = doc.RootElement.GetProperty("arguments").Clone()
		};
		foreach (JsonElement library in doc.RootElement.GetProperty("libraries").EnumerateArray())
		{
			version.Libraries.Add(library.Clone());
		}
		return version;
	}

	private static LaunchProfile Profile(string gameDirectory, string versionId = "synthetic") => new LaunchProfile
	{
		GameDirectory = gameDirectory,
		JavaPath = "java.exe",
		VersionId = versionId,
		PlayerName = "Tester",
		MaxMemoryMb = 1024,
		WindowWidth = 854,
		WindowHeight = 480
	};

	private static List<string> InvokeResolveJvmArguments(MinecraftVersion version, LaunchProfile profile, string natives, string classpath)
	{
		var replacements = (Dictionary<string, string>)Method("BuildReplacements")
			.Invoke(null, new object[] { profile, version, natives, classpath })!;
		return (List<string>)Method("ResolveJvmArguments")
			.Invoke(null, new object[] { version, replacements, natives, classpath, profile })!;
	}

	[Fact]
	public void ResolveJvmArguments_KeepsRepeatedAddOpensAndAddExportsTokens()
	{
		const string jvm = """
		["-Djava.net.preferIPv6Addresses=system",
		 "-DignoreList=client-extra,${version_name}.jar",
		 "-DlibraryDirectory=${library_directory}",
		 "-p",
		 "${library_directory}/cpw/mods/bootstraplauncher/1.1.2/bootstraplauncher-1.1.2.jar${classpath_separator}${library_directory}/cpw/mods/securejarhandler/2.1.10/securejarhandler-2.1.10.jar",
		 "--add-modules",
		 "ALL-MODULE-PATH",
		 "--add-opens",
		 "java.base/java.util.jar=cpw.mods.securejarhandler",
		 "--add-opens",
		 "java.base/java.lang.invoke=cpw.mods.securejarhandler",
		 "--add-exports",
		 "java.base/sun.security.util=cpw.mods.securejarhandler",
		 "--add-exports",
		 "jdk.naming.dns/com.sun.jndi.dns=java.naming"]
		""";
		List<string> args = InvokeResolveJvmArguments(Version(jvm), Profile(root), Path.Combine(root, "natives"), "a.jar");

		Assert.Equal(2, args.Count(a => a == "--add-opens"));
		Assert.Equal(2, args.Count(a => a == "--add-exports"));
		Assert.Contains("java.base/java.util.jar=cpw.mods.securejarhandler", args);
		Assert.Contains("java.base/java.lang.invoke=cpw.mods.securejarhandler", args);
		Assert.Contains("java.base/sun.security.util=cpw.mods.securejarhandler", args);
		Assert.Contains("jdk.naming.dns/com.sun.jndi.dns=java.naming", args);
		for (int i = 0; i < args.Count - 1; i++)
		{
			if (args[i] == "--add-opens" || args[i] == "--add-exports")
			{
				Assert.False(args[i + 1].StartsWith("--", StringComparison.Ordinal));
			}
		}
	}

	[Fact]
	public void BuildReplacements_ProvidesForgePlaceholders()
	{
		MinecraftVersion version = Version("[\"-DignoreList=client-extra,${version_name}.jar\"]");
		var replacements = (Dictionary<string, string>)Method("BuildReplacements")
			.Invoke(null, new object[] { Profile(root), version, Path.Combine(root, "natives"), "cp.jar" })!;

		// JsonUtil.BuildReplacements 会把 key 规范化成 `${key}` 形式
		Assert.True(replacements.ContainsKey("${library_directory}"), "缺少 ${library_directory} 的替换项");
		Assert.Equal(Path.Combine(root, "libraries"), replacements["${library_directory}"]);
		Assert.True(replacements.ContainsKey("${classpath_separator}"), "缺少 ${classpath_separator} 的替换项");
		Assert.Equal(OperatingSystem.IsWindows() ? ";" : ":", replacements["${classpath_separator}"]);
	}

	[Fact]
	public void ResolveJvmArguments_LeavesNoPlaceholderInModulePath()
	{
		const string jvm = """
		["-p",
		 "${library_directory}/cpw/mods/bootstraplauncher/1.1.2/bootstraplauncher-1.1.2.jar${classpath_separator}${library_directory}/cpw/mods/securejarhandler/2.1.10/securejarhandler-2.1.10.jar",
		 "--add-modules",
		 "ALL-MODULE-PATH"]
		""";
		List<string> args = InvokeResolveJvmArguments(Version(jvm), Profile(root), Path.Combine(root, "natives"), "a.jar");

		Assert.DoesNotContain(args, a => a.Contains("${library_directory}", StringComparison.Ordinal));
		Assert.DoesNotContain(args, a => a.Contains("${classpath_separator}", StringComparison.Ordinal));
	}

	// ---------------------------------------------------------------- 类路径装配夹具（真实 BuildCommandAsync）

	private string WriteLibrary(string relativePath, string content)
	{
		string path = Path.Combine(root, "libraries", relativePath.Replace('/', Path.DirectorySeparatorChar));
		Directory.CreateDirectory(Path.GetDirectoryName(path)!);
		File.WriteAllText(path, content);
		return path;
	}

	private string WriteJar(string relativePath, params string[] entryNames)
	{
		string path = Path.Combine(root, "libraries", relativePath.Replace('/', Path.DirectorySeparatorChar));
		Directory.CreateDirectory(Path.GetDirectoryName(path)!);
		using (ZipArchive zip = ZipFile.Open(path, ZipArchiveMode.Create))
		{
			foreach (string entry in entryNames)
			{
				zip.CreateEntry(entry);
			}
		}
		return path;
	}

	private string WriteVersionJar(string id)
	{
		string path = VersionResolver.GetVersionJarPath(root, id);
		Directory.CreateDirectory(Path.GetDirectoryName(path)!);
		using (ZipArchive zip = ZipFile.Open(path, ZipArchiveMode.Create))
		{
			zip.CreateEntry("META-INF/MANIFEST.MF");
		}
		return path;
	}

	private void WriteJson(string id, string json)
	{
		string path = VersionResolver.GetVersionJsonPath(root, id);
		Directory.CreateDirectory(Path.GetDirectoryName(path)!);
		File.WriteAllText(path, json);
	}

	/// <summary>
	/// 与独立验收的 BoundaryProbe 同形的夹具：父版本 1.21.1 的客户端 jar 存在；
	/// child 是 NeoForge 风格（声明 `client-extra`、`-cp ${classpath}`），且**当前替代客户端缺失**。
	/// </summary>
	private string WriteNeoForgeStyleFixture(string childId = "neoforge-21.1.230")
	{
		WriteVersionJar("1.21.1");
		WriteJson("1.21.1", """{"id":"1.21.1","mainClass":"net.minecraft.client.main.Main","javaVersion":{"majorVersion":21}}""");
		WriteJson(childId, "{\"id\":\"" + childId + "\",\"inheritsFrom\":\"1.21.1\",\"mainClass\":\"cpw.mods.bootstraplauncher.BootstrapLauncher\",\"arguments\":{\"jvm\":[\"-DignoreList=client-extra,${version_name}.jar\",\"-cp\",\"${classpath}\"]},\"libraries\":[{\"name\":\"dummy:lib:1.0\",\"downloads\":{\"artifact\":{\"path\":\"dummy/lib/1.0/lib-1.0.jar\"}}}]}");
		WriteLibrary("dummy/lib/1.0/lib-1.0.jar", "dummy"); // 让 classpath 非空（独立验收记录了空 classpath 的干扰）
		return childId;
	}

	private async Task<List<string>> BuildArgumentsAsync(string versionId)
	{
		var launcher = new LauncherService(delegate(string _) { });
		LaunchCommand command = await launcher.BuildCommandAsync(Profile(root, versionId));
		return command.Arguments;
	}

	private static string ClasspathOf(List<string> arguments)
	{
		int index = arguments.IndexOf("-cp");
		Assert.True(index >= 0 && index + 1 < arguments.Count, "命令行里没有可用的 -cp 取值");
		return arguments[index + 1];
	}

	private static bool RetainsVanilla(List<string> arguments, string vanillaJar) =>
		ClasspathOf(arguments).Split(Path.PathSeparator).Contains(vanillaJar);

	[Fact]
	public async Task BuildCommandAsync_UnrelatedExtraJar_DoesNotChangeVanillaClientRetention()
	{
		string child = WriteNeoForgeStyleFixture();
		string vanilla = VersionResolver.GetVersionJarPath(root, "1.21.1");
		List<string> before = await BuildArgumentsAsync(child);
		Assert.True(RetainsVanilla(before, vanilla), "夹具前置条件：添加无关文件前应保留原版 jar");

		WriteJar("net/minecraftforge/unrelated-tool/1.0/unrelated-tool-extra.jar", "META-INF/MANIFEST.MF");
		List<string> after = await BuildArgumentsAsync(child);

		Assert.True(RetainsVanilla(after, vanilla), "无关的 *-extra.jar 不应把原版客户端 jar 移出 classpath");
	}

	[Fact]
	public async Task BuildCommandAsync_StaleClientExtraJar_DoesNotChangeVanillaClientRetention()
	{
		string child = WriteNeoForgeStyleFixture();
		string vanilla = VersionResolver.GetVersionJarPath(root, "1.21.1");
		List<string> before = await BuildArgumentsAsync(child);
		Assert.True(RetainsVanilla(before, vanilla), "夹具前置条件：添加旧版文件前应保留原版 jar");

		// 当前是 1.21.1，库里只有 1.20.1 的 extra jar
		WriteJar("net/minecraft/client/1.20.1/client-1.20.1-extra.jar", "META-INF/MANIFEST.MF");
		List<string> after = await BuildArgumentsAsync(child);

		Assert.True(RetainsVanilla(after, vanilla), "旧版本的 client-*-extra.jar 不应把当前原版客户端 jar 移出 classpath");
	}

	[Fact]
	public async Task BuildCommandAsync_ResourceOnlyExtraJar_IsNotAcceptedAsCurrentClient()
	{
		string child = WriteNeoForgeStyleFixture();
		string vanilla = VersionResolver.GetVersionJarPath(root, "1.21.1");

		// 当前加载器目录里只有"资源型"extra jar（真实 Forge/NeoForge 的 client-*-extra.jar 就是 0 个 .class）
		WriteJar("net/neoforged/neoforge/21.1.230/neoforge-21.1.230-extra.jar", "assets/minecraft/lang/en_us.json", "META-INF/MANIFEST.MF");
		List<string> after = await BuildArgumentsAsync(child);

		Assert.True(RetainsVanilla(after, vanilla), "只有资源、没有类文件的 extra jar 不构成客户端类供应链，必须保留原版 jar");
	}

	[Fact]
	public async Task BuildCommandAsync_CurrentPatchedClientWithClasses_RemovesVanillaJar()
	{
		string child = WriteNeoForgeStyleFixture();
		string vanilla = VersionResolver.GetVersionJarPath(root, "1.21.1");

		// 与真实安装同形：当前加载器版本目录下的 *-client.jar，且确实含类文件
		WriteJar("net/neoforged/neoforge/21.1.230/neoforge-21.1.230-client.jar",
			"cpw/mods/bootstraplauncher/BootstrapLauncher.class", "META-INF/MANIFEST.MF");
		List<string> after = await BuildArgumentsAsync(child);

		Assert.False(RetainsVanilla(after, vanilla), "当前加载器确实提供含类的 patched client 时，才允许把原版 jar 移出 classpath");
	}

	[Fact]
	public async Task BuildCommandAsync_DeclaredClientJarWithoutClasses_ThrowsExplicitError()
	{
		string child = WriteNeoForgeStyleFixture();
		// 当前加载器目录里存在同名的 *-client.jar，但里面没有类文件 ⇒ 安装损坏，必须明确拒绝而不是静默生成错误 classpath
		WriteJar("net/neoforged/neoforge/21.1.230/neoforge-21.1.230-client.jar", "META-INF/MANIFEST.MF");

		InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(() => BuildArgumentsAsync(child));
		Assert.Contains("client", ex.Message, StringComparison.OrdinalIgnoreCase);
	}

	[Fact]
	public async Task BuildCommandAsync_VanillaProfile_KeepsVersionJar()
	{
		WriteVersionJar("1.18.2");
		WriteJson("1.18.2", """{"id":"1.18.2","mainClass":"net.minecraft.client.main.Main","arguments":{"jvm":["-Djava.library.path=${natives_directory}","-cp","${classpath}"]},"libraries":[]}""");
		string vanilla = VersionResolver.GetVersionJarPath(root, "1.18.2");

		List<string> arguments = await BuildArgumentsAsync("1.18.2");
		Assert.True(RetainsVanilla(arguments, vanilla), "Vanilla 没有自供 client，必须保留版本 jar");
	}

	[Fact]
	public async Task BuildCommandAsync_FabricProfile_KeepsVersionJar()
	{
		WriteVersionJar("1.18.2");
		WriteJson("1.18.2", """{"id":"1.18.2","mainClass":"net.minecraft.client.main.Main","arguments":{"jvm":["-Djava.library.path=${natives_directory}"]},"libraries":[]}""");
		WriteJson("fabric-loader-0.19.3-1.18.2", """{"id":"fabric-loader-0.19.3-1.18.2","inheritsFrom":"1.18.2","mainClass":"net.fabricmc.loader.impl.launch.knot.KnotClient","arguments":{"jvm":["-DFabricMcEmu= net.minecraft.client.main.Main "]}}""");
		string vanilla = VersionResolver.GetVersionJarPath(root, "1.18.2");

		List<string> arguments = await BuildArgumentsAsync("fabric-loader-0.19.3-1.18.2");
		Assert.True(RetainsVanilla(arguments, vanilla), "Fabric 不自供 client，必须保留版本 jar");
	}
}
