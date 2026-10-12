using System;
using System.IO;
using Pineglow.Launcher.Services;
using Xunit;
using Xunit.Abstractions;

namespace Pineglow.Launcher.Tests;

/// <summary>
/// R02 聚焦测试：**升级后的界面来源一致性**。
///
/// 原始故障（2026-10-10 独立复验 S02，`probe-legacy-ui.ps1`）：
///   目录式旧安装只替换 EXE（保留旧 <c>UI/</c>）时，新进程仍服务的旧界面的 HTML
///   与旧文件**逐字节相同**（`legacyUiServed=true`）。全新安装的 19/19 冷启动通过
///   不会覆盖这条升级路径，因此它曾是发布阻断。
///
/// 根因：`ResolveUiRoot` 把「EXE 旁的 UI/」排在「内嵌资源解包」之前，而单文件发行
/// 恰恰可能落在一个残留旧 UI/ 的目录里。
///
/// 本类覆盖**决策矩阵**（真实单文件 EXE 的端到端复验由探针单独留证）：
///   ① 单文件部署：内嵌界面优先于残留外置 UI/（本回归位，修复前必红）；
///   ② 目录构建/开发树：外置 UI/ 仍然优先，且**不触发**解包（不往用户配置目录写东西）；
///   ③ 单文件但解包失败：回退外置 UI，启动不中断；
///   ④ PINEGLOW_UI_DIR 显式覆盖在两种形态下都优先；
///   ⑤/⑥/⑦ 既有回退链不变：内嵌 → 配置目录 UI/ → 外置路径。
/// </summary>
public class UiSourceSelectionTests
{
	private readonly ITestOutputHelper _out;

	public UiSourceSelectionTests(ITestOutputHelper output)
	{
		_out = output;
	}

	private static string NewTempRoot()
	{
		string dir = Path.Combine(Path.GetTempPath(), "pineglow-uiselect-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(dir);
		return dir;
	}

	/// <summary>造一个「看起来可用」的 UI 目录（含入口文件）。</summary>
	private static string NewUiDir(string root, string tag)
	{
		string dir = Path.Combine(root, tag);
		Directory.CreateDirectory(dir);
		File.WriteAllText(Path.Combine(dir, "index.html"), "<!doctype html><title>" + tag + "</title>");
		return dir;
	}

	private static string MissingDir(string root, string tag)
	{
		return Path.Combine(root, tag + "-missing-" + Guid.NewGuid().ToString("N"));
	}

	private static void TryDelete(string dir)
	{
		try
		{
			if (Directory.Exists(dir))
			{
				Directory.Delete(dir, true);
			}
		}
		catch
		{
		}
	}

	/* ============ ① 单文件部署：内嵌界面优先于残留外置 UI/（R02 回归位） ============ */

	[Fact]
	public void SingleFileDeployment_PrefersEmbeddedBundle_OverStaleExternalUi()
	{
		string root = NewTempRoot();
		try
		{
			string staleExternal = NewUiDir(root, "old-install-UI");
			string embedded = NewUiDir(root, "embedded-extract");

			string chosen = LocalApiService.ResolveUiRootCore(
				explicitUiDir: null,
				singleFileDeployment: true,
				externalUiDir: staleExternal,
				configUiDir: MissingDir(root, "config-UI"),
				extractEmbedded: () => embedded);

			_out.WriteLine($"[R02] 单文件 + 残留旧 UI → 选中 {chosen}");
			Assert.Equal(embedded, chosen);

			// 这条断言就是原始阻断的判据：升级后服务的必须是当前 EXE 自带的界面。
			Assert.NotEqual(staleExternal, chosen);
			Assert.True(File.Exists(Path.Combine(chosen, "index.html")));
			Assert.DoesNotContain("old-install-UI", File.ReadAllText(Path.Combine(chosen, "index.html")));
		}
		finally
		{
			TryDelete(root);
		}
	}

	/* ============ ② 目录构建/开发树：外置 UI/ 仍优先，且不触发解包 ============ */

	[Fact]
	public void DirectoryBuild_KeepsExternalUiFirst_AndDoesNotExtract()
	{
		string root = NewTempRoot();
		try
		{
			string external = NewUiDir(root, "dev-tree-UI");
			string embedded = NewUiDir(root, "embedded-extract");
			int extractCalls = 0;

			string chosen = LocalApiService.ResolveUiRootCore(
				explicitUiDir: null,
				singleFileDeployment: false,
				externalUiDir: external,
				configUiDir: MissingDir(root, "config-UI"),
				extractEmbedded: () => { extractCalls++; return embedded; });

			_out.WriteLine($"[R02] 目录构建 → 选中 {chosen}；解包调用 {extractCalls} 次");
			Assert.Equal(external, chosen);

			// 开发树/目录包不该往用户配置目录或临时目录写解包产物（R01 既有约定）。
			Assert.Equal(0, extractCalls);
		}
		finally
		{
			TryDelete(root);
		}
	}

	/* ============ ③ 单文件但解包不可用：回退外置 UI，启动不中断 ============ */

	[Fact]
	public void SingleFileDeployment_FallsBackToExternalUi_WhenEmbeddedUnavailable()
	{
		string root = NewTempRoot();
		try
		{
			string external = NewUiDir(root, "old-install-UI");

			string chosen = LocalApiService.ResolveUiRootCore(
				explicitUiDir: null,
				singleFileDeployment: true,
				externalUiDir: external,
				configUiDir: MissingDir(root, "config-UI"),
				extractEmbedded: () => null);

			_out.WriteLine("[R02] 单文件 + 解包失败 → 回退 " + chosen);
			Assert.Equal(external, chosen);
		}
		finally
		{
			TryDelete(root);
		}
	}

	/* ============ ④ PINEGLOW_UI_DIR 显式覆盖：两种形态都优先 ============ */

	[Theory]
	[InlineData(true)]
	[InlineData(false)]
	public void ExplicitUiDir_WinsInBothDeploymentShapes(bool singleFile)
	{
		string root = NewTempRoot();
		try
		{
			string explicitDir = NewUiDir(root, "explicit-UI");
			string external = NewUiDir(root, "external-UI");
			string embedded = NewUiDir(root, "embedded-extract");

			string chosen = LocalApiService.ResolveUiRootCore(
				explicitUiDir: explicitDir,
				singleFileDeployment: singleFile,
				externalUiDir: external,
				configUiDir: MissingDir(root, "config-UI"),
				extractEmbedded: () => embedded);

			Assert.Equal(explicitDir, chosen);

			// 指向不存在的目录时不得采用它（既有回退语义）
			string notChosen = LocalApiService.ResolveUiRootCore(
				explicitUiDir: MissingDir(root, "explicit-gone"),
				singleFileDeployment: singleFile,
				externalUiDir: external,
				configUiDir: MissingDir(root, "config-UI"),
				extractEmbedded: () => embedded);

			Assert.NotEqual(explicitDir, notChosen);
		}
		finally
		{
			TryDelete(root);
		}
	}

	/* ============ ⑤ 外置缺失：用内嵌（R01 既有行为） ============ */

	[Fact]
	public void NoExternalUi_UsesEmbeddedBundle()
	{
		string root = NewTempRoot();
		try
		{
			string embedded = NewUiDir(root, "embedded-extract");
			string missingExternal = MissingDir(root, "no-UI");

			string chosen = LocalApiService.ResolveUiRootCore(
				explicitUiDir: null,
				singleFileDeployment: false,
				externalUiDir: missingExternal,
				configUiDir: MissingDir(root, "config-UI"),
				extractEmbedded: () => embedded);

			Assert.Equal(embedded, chosen);
		}
		finally
		{
			TryDelete(root);
		}
	}

	/* ============ ⑥ 配置目录 UI/ 回退 ============ */

	[Fact]
	public void NothingElseAvailable_UsesConfigDirUi()
	{
		string root = NewTempRoot();
		try
		{
			string configUi = NewUiDir(root, "config-UI");

			string chosen = LocalApiService.ResolveUiRootCore(
				explicitUiDir: null,
				singleFileDeployment: false,
				externalUiDir: MissingDir(root, "no-UI"),
				configUiDir: configUi,
				extractEmbedded: () => null);

			Assert.Equal(configUi, chosen);
		}
		finally
		{
			TryDelete(root);
		}
	}

	/* ============ ⑦ 全不可用：返回外置路径，不抛异常（启动不因缺 UI 中断） ============ */

	[Fact]
	public void NothingAvailable_ReturnsExternalPathWithoutThrowing()
	{
		string root = NewTempRoot();
		try
		{
			string missingExternal = MissingDir(root, "no-UI");

			string chosen = LocalApiService.ResolveUiRootCore(
				explicitUiDir: null,
				singleFileDeployment: true,
				externalUiDir: missingExternal,
				configUiDir: MissingDir(root, "config-UI"),
				extractEmbedded: () => null);

			Assert.Equal(missingExternal, chosen);
		}
		finally
		{
			TryDelete(root);
		}
	}
}
