using System;
using System.IO;
using System.Linq;
using System.Text;
using Pineglow.Launcher.Services;
using Xunit;
using Xunit.Abstractions;

namespace Pineglow.Launcher.Tests;

/// <summary>
/// R01 聚焦测试：**单文件发行的自足性**。
///
/// 原始故障（docs/release-readiness-review-2026-10-05.md R01）：
///   Release 工作流发布单个 EXE，但 UI 一直是「EXE 旁的外置 UI/ 目录」，
///   原生库也没进单文件。把发布的 EXE 单独放进新目录启动 → Skia 原生库缺失，进程直接退出。
///
/// 本类覆盖**构建期就能证明**的那一半（真机冷启动由独立验收覆盖）：
///   ① 嵌入清单里确实有发行必需的 UI 资源，且入口闭包（index.html → css/pineglow.css
///      → ../assets/pine-forest.svg）在包内可解析——这正是 R02 漏资源的二进制形态；
///   ② 解包结果与嵌入内容逐字节一致，目录名带版本+内容指纹；
///   ③ 同一个键重复解包是幂等的；解包被破坏（缺文件）后能重新解包修复；
///   ④ 非单文件宿主（开发树/目录包/测试宿主）不会被解包逻辑影响。
///
/// 不触碰真实用户目录：解包一律落在临时目录。
/// </summary>
public class UiBundleTests
{
	private readonly ITestOutputHelper _out;

	public UiBundleTests(ITestOutputHelper output)
	{
		_out = output;
	}

	private static string NewTempRoot()
	{
		string dir = Path.Combine(Path.GetTempPath(), "pineglow-uibundle-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(dir);
		return dir;
	}

	private static string ResourceNameOf(string relative)
	{
		return UiBundle.ResourcePrefix + relative.Replace('/', '\\');
	}

	/* ===================== ① 嵌入清单与入口闭包 ===================== */

	[Fact]
	public void EmbeddedBundle_ContainsReleaseCriticalResources()
	{
		Assert.True(UiBundle.HasEmbeddedUi, "程序集里必须嵌入 UI 资源，否则单文件发行无法自足");

		string[] relatives = UiBundle.ResourceNames.Select(UiBundle.RelativePathOf).ToArray();
		_out.WriteLine($"[R01] 嵌入资源 {relatives.Length} 项: {string.Join(", ", relatives)}");

		foreach (string required in new[]
		{
			"index.html",
			"css/figma.css",
			"css/pineglow.css",
			"assets/pine-forest.svg",
			"js/api.js",
			"js/i18n.js",
			"js/launcher.js",
			"js/skin3d.js",
			"js/skin3d-core.js",
			"vendor/three.min.js",
			"vendor/THREE-LICENSE.txt"
		})
		{
			Assert.Contains(required, relatives);
		}

		Assert.DoesNotContain(relatives, r => r.StartsWith("tests/", StringComparison.Ordinal));
	}

	[Fact]
	public void EmbeddedBundle_LocalReferenceClosureIsSatisfiable()
	{
		string[] relatives = UiBundle.ResourceNames.Select(UiBundle.RelativePathOf).ToArray();
		var index = Encoding.UTF8.GetString(UiBundle.ReadResource(ResourceNameOf("index.html")));
		Assert.Contains("css/pineglow.css", index);

		var css = Encoding.UTF8.GetString(UiBundle.ReadResource(ResourceNameOf("css/pineglow.css")));
		Assert.Contains("../assets/pine-forest.svg", css);

		// 解析 index.html → css/pineglow.css → ../assets/pine-forest.svg 这条链
		Assert.Contains("css/pineglow.css", relatives);
		Assert.Contains("assets/pine-forest.svg", relatives);
	}

	/* ===================== ② 解包内容与指纹 ===================== */

	[Fact]
	public void BundleKey_CarriesVersionAndContentFingerprint()
	{
		string key = UiBundle.BundleKey;
		_out.WriteLine("[R01] BundleKey=" + key);
		Assert.Matches(@"^\d+\.\d+\.\d+-[0-9a-f]{12}$", key);
	}

	[Fact]
	public void ExtractTo_WritesEveryEmbeddedResourceByteIdentical()
	{
		string root = NewTempRoot();
		try
		{
			string extracted = UiBundle.ExtractTo(root);
			Assert.Equal(Path.Combine(root, UiBundle.BundleKey), extracted);
			Assert.True(File.Exists(Path.Combine(extracted, "index.html")), "解包目录必须含入口文件");

			foreach (string name in UiBundle.ResourceNames)
			{
				string relative = UiBundle.RelativePathOf(name).Replace('/', Path.DirectorySeparatorChar);
				string path = Path.Combine(extracted, relative);
				Assert.True(File.Exists(path), "解包后缺少文件: " + relative);
				Assert.Equal(UiBundle.ReadResource(name), File.ReadAllBytes(path));
			}

			Assert.Equal(UiBundle.ResourceNames.Count,
				Directory.GetFiles(extracted, "*", SearchOption.AllDirectories).Length);
		}
		finally
		{
			TryDelete(root);
		}
	}

	/* ===================== ③ 幂等与自愈 ===================== */

	[Fact]
	public void ExtractTo_IsIdempotent_AndRepairsDamagedExtraction()
	{
		string root = NewTempRoot();
		try
		{
			string first = UiBundle.ExtractTo(root);
			string second = UiBundle.ExtractTo(root);
			Assert.Equal(first, second);

			// 破坏解包：删掉入口文件（模拟半包/被杀毒软件清掉文件）
			File.Delete(Path.Combine(first, "index.html"));
			Assert.False(UiBundle.IsExtractionUsable(first));

			string repaired = UiBundle.ExtractTo(root);
			Assert.Equal(first, repaired);
			Assert.True(File.Exists(Path.Combine(repaired, "index.html")), "损坏的解包必须被修复");
			Assert.True(UiBundle.IsExtractionUsable(repaired));

			// 只剩文件数不符（入口还在）时同样要重新解包
			File.Delete(Path.Combine(repaired, "assets", "pine-forest.svg"));
			Assert.False(UiBundle.IsExtractionUsable(repaired));
			string repaired2 = UiBundle.ExtractTo(root);
			Assert.True(File.Exists(Path.Combine(repaired2, "assets", "pine-forest.svg")));

			Assert.Empty(Directory.GetDirectories(root, ".tmp-*"));
		}
		finally
		{
			TryDelete(root);
		}
	}

	[Fact]
	public void ReadResource_UnknownName_Throws()
	{
		Assert.Throws<FileNotFoundException>(() => UiBundle.ReadResource("Pineglow.Launcher.Ui.does-not-exist.js"));
	}

	/* ===================== ④ 非单文件宿主不被影响 ===================== */

	[Fact]
	public void EnsureExtracted_WithoutForcedRoot_DoesNotExtractOnNonSingleFileHost()
	{
		string? original = Environment.GetEnvironmentVariable(UiBundle.BundleRootEnvVar);
		try
		{
			Environment.SetEnvironmentVariable(UiBundle.BundleRootEnvVar, null);

			// 测试宿主里程序集有 Location（不是单文件捆绑），因此必须返回 null：
			// 开发树/目录包/测试宿主本来就靠 EXE 旁的 UI/，不该往真实配置目录写东西。
			Assert.False(string.IsNullOrEmpty(typeof(UiBundle).Assembly.Location));
			Assert.Null(UiBundle.EnsureExtracted());
		}
		finally
		{
			Environment.SetEnvironmentVariable(UiBundle.BundleRootEnvVar, original);
		}
	}

	[Fact]
	public void EnsureExtracted_WithForcedRoot_ExtractsIntoThatRoot()
	{
		string root = NewTempRoot();
		string? original = Environment.GetEnvironmentVariable(UiBundle.BundleRootEnvVar);
		try
		{
			Environment.SetEnvironmentVariable(UiBundle.BundleRootEnvVar, root);
			string? extracted = UiBundle.EnsureExtracted();
			Assert.NotNull(extracted);
			Assert.StartsWith(root, extracted!, StringComparison.OrdinalIgnoreCase);
			Assert.True(File.Exists(Path.Combine(extracted!, "index.html")));
		}
		finally
		{
			Environment.SetEnvironmentVariable(UiBundle.BundleRootEnvVar, original);
			TryDelete(root);
		}
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
}
