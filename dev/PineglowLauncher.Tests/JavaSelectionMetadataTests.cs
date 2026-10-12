using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Pineglow.Launcher.Models;
using Pineglow.Launcher.Services;
using Xunit;

namespace Pineglow.Launcher.Tests;

/// <summary>
/// 聚焦回归（2026-10-07 限定修复）：**Java 版本选择必须读实例版本元数据**。
///
/// 真实缺陷（NeoForge 21.1 / MC 1.21.1）：启动路径把**加载器版本 id**（`neoforge-21.1.230`）交给
/// `JavaVersionMatcher` 的**字符串版**，解析不出主版本 ⇒ 默认 17 ⇒ JVM 立刻退出：
///   `java.lang.module.InvalidModuleDescriptorException: Unsupported major.minor version 65.0`
/// 元数据版（读版本 JSON 的 `javaVersion.majorVersion`，并可沿 `inheritsFrom` 上溯）本来就能给出 21。
/// </summary>
public sealed class JavaSelectionMetadataTests : IDisposable
{
	private readonly string root = Path.Combine(Path.GetTempPath(), "pineglow-javasel-" + Guid.NewGuid().ToString("N"));

	public JavaSelectionMetadataTests() => Directory.CreateDirectory(root);

	public void Dispose()
	{
		try { if (Directory.Exists(root)) Directory.Delete(root, true); } catch { }
	}

	private void WriteVersion(string id, string json)
	{
		string path = VersionResolver.GetVersionJsonPath(root, id);
		Directory.CreateDirectory(Path.GetDirectoryName(path)!);
		File.WriteAllText(path, json);
	}

	/// <summary>与真实 NeoForge 21.1 同形：加载器版本 id + `inheritsFrom` 指向 1.21.1，父版本声明 `javaVersion.majorVersion=21`。</summary>
	private void WriteNeoForge21Layout()
	{
		WriteVersion("1.21.1", """{"id":"1.21.1","mainClass":"net.minecraft.client.main.Main","javaVersion":{"majorVersion":21,"component":"java-runtime-delta"}}""");
		WriteVersion("neoforge-21.1.230", """{"id":"neoforge-21.1.230","inheritsFrom":"1.21.1","mainClass":"cpw.mods.bootstraplauncher.BootstrapLauncher"}""");
	}

	[Fact]
	public void GetRequiredJavaMajor_LoaderIdWithMetadata_ReadsInheritedMajor()
	{
		WriteNeoForge21Layout();

		// 字符串版：加载器 id 解析不出主版本（历史缺陷的源头）
		Assert.Equal(17, JavaVersionMatcher.GetRequiredJavaMajor("neoforge-21.1.230"));
		// 元数据版：沿 inheritsFrom 读到 21
		Assert.Equal(21, JavaVersionMatcher.GetRequiredJavaMajor("neoforge-21.1.230", root));
	}

	[Fact]
	public void MatchForMinecraftVersion_LoaderIdWithMetadata_RejectsJava17()
	{
		WriteNeoForge21Layout();
		var installations = new List<JavaInstallation>
		{
			new JavaInstallation { JavaPath = Path.Combine(root, "fake-java-17", "bin", "java.exe"), VersionMajor = 17, VersionText = "17.0.20", IsValid = true }
		};

		// 传目录 ⇒ 走元数据 ⇒ 要求 21 ⇒ 17 不匹配
		Assert.Null(JavaLocator.MatchForMinecraftVersion(installations, "neoforge-21.1.230", root));
	}

	[Fact]
	public void MatchForMinecraftVersion_UnknownInstallationMajor_IsNotTreatedAsCompatible()
	{
		WriteNeoForge21Layout();
		var installations = new List<JavaInstallation>
		{
			new JavaInstallation { JavaPath = Path.Combine(root, "fake-unknown", "bin", "java.exe"), VersionMajor = 0, VersionText = "?", IsValid = true }
		};

		Assert.Null(JavaLocator.MatchForMinecraftVersion(installations, "neoforge-21.1.230", root));
	}

	[Fact]
	public async Task ResolveJavaPath_LoaderInstanceRequiring21_NeverSilentlyReturnsIncompatibleRuntime()
	{
		WriteNeoForge21Layout();
		var instance = new GameInstance
		{
			Id = "synthetic-neoforge",
			Name = "synthetic-neoforge",
			MinecraftVersion = "neoforge-21.1.230",
			GameDirectory = root,
			JavaPath = string.Empty
		};
		var config = new AppConfig { JavaPath = string.Empty, Instances = new List<GameInstance> { instance } };

		MethodInfo method = typeof(LocalApiService).GetMethod("ResolveJavaPath", BindingFlags.NonPublic | BindingFlags.Static)
			?? throw new InvalidOperationException("找不到 LocalApiService.ResolveJavaPath");

		string? returned = null;
		Exception? thrown = null;
		try
		{
			returned = (string?)method.Invoke(null, new object[] { config, instance, instance.MinecraftVersion });
		}
		catch (TargetInvocationException ex) when (ex.InnerException != null)
		{
			thrown = ex.InnerException;
		}

		if (thrown != null)
		{
			// 允许"明确拒绝"：但必须给出可读的、指向所需主版本的信息
			Assert.Contains("21", thrown.Message);
			return;
		}

		Assert.False(string.IsNullOrWhiteSpace(returned), "既没抛错也没给出路径");
		List<JavaInstallation> scanned = await JavaLocator.ScanAsync(null);
		int major = scanned.FirstOrDefault(x => string.Equals(x.JavaPath, returned, StringComparison.OrdinalIgnoreCase))?.VersionMajor ?? -1;
		Assert.True(major == 21, $"该版本需要 Java 21，但启动路径给出的是 Java {major}（{returned}）——不允许静默使用不兼容的运行时");
	}
}
