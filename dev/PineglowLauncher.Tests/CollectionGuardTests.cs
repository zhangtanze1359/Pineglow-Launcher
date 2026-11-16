using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace Pineglow.Launcher.Tests;

/// <summary>
/// 守卫：凡是启动 <c>LocalApiTestHarness</c> 的测试类，**必须**声明 <c>[Collection(...)]</c>。
///
/// 为什么（2026-11-17 retro）：
///   <c>LocalApiTestHarness</c> 有全局约束——同一进程只允许一个 <c>LocalApiService</c>，
///   第二个 harness 会抛「LocalApiService 已在运行」。xUnit 默认并行跑不同测试类，
///   于是新加一个 harness 类而忘了并入串行集合，全量就会偶发一片红
///   （本次会话真实发生两次：DiagnoseWithoutAiTests、CurseForgeKeyRequiredTests）。
///
/// 判定必须**按类**而不是按文件：`UpdateManifestFallbackTests.cs` 里就有两个类，
/// 只有带 harness 的那个需要 `[Collection]`。
/// </summary>
public sealed class CollectionGuardTests
{
	private const string HarnessUsage = "LocalApiTestHarness.StartAsync";

	/// <summary>顶层类声明：行首非空白、非 attribute、非注释，行内出现 class 关键字。</summary>
	private const string TopLevelClassPattern = @"^[^\s@/*][^\n]*\bclass\s+([A-Za-z_][\w]*)";

	private static string TestsDirectory => Path.GetFullPath(Path.Combine(
		AppContext.BaseDirectory, "..", "..", "..", "..", "PineglowLauncher.Tests"));

	/// <summary>
	/// 切出**顶层**类，按「到下一个顶层类声明为止」分段。
	///
	/// 三个刻意的取舍（第一版都在这里踩过假红）：
	/// 1. 不按花括号配平 —— C# 字符串字面量里的花括号（JSON 片段）会让配平算歪；
	/// 2. 不吃嵌套类 —— 测试夹具（Fixture / TinyServer）是 private sealed class，缩进非 0，
	///    按嵌套类切段会把外层类的 harness 用法错记到夹具头上；
	/// 3. 下一段的 attributes 窗口从上一个**类声明**开始，而不是上一段的结尾。
	/// </summary>
	internal static IEnumerable<(string Name, string Attributes, string Body)> SplitClasses(string text)
	{
		MatchCollection decls = Regex.Matches(text, TopLevelClassPattern, RegexOptions.Multiline);
		for (int i = 0; i < decls.Count; i++)
		{
			Match decl = decls[i];
			int bodyStart = decl.Index;
			int bodyEnd = i + 1 < decls.Count ? decls[i + 1].Index : text.Length;
			int attrStart = i == 0 ? 0 : decls[i - 1].Index;
			yield return (decl.Groups[1].Value,
				text.Substring(attrStart, decl.Index - attrStart),
				text.Substring(bodyStart, Math.Max(0, bodyEnd - bodyStart)));
		}
	}

	[Fact]
	public void HarnessUsers_MustDeclareCollectionAttribute_PerClass()
	{
		Assert.True(Directory.Exists(TestsDirectory), "找不到测试目录：" + TestsDirectory);

		List<string> offenders = new List<string>();
		foreach (string file in Directory.EnumerateFiles(TestsDirectory, "*.cs", SearchOption.TopDirectoryOnly))
		{
			// 守卫自己的文件里有刻意拼出来的反例示例，不能算违规
			if (string.Equals(Path.GetFileName(file), nameof(CollectionGuardTests) + ".cs", StringComparison.Ordinal))
			{
				continue;
			}

			string text = File.ReadAllText(file);
			if (!text.Contains(HarnessUsage, StringComparison.Ordinal))
			{
				continue;
			}

			foreach (var (name, attributes, body) in SplitClasses(text))
			{
				if (!body.Contains(HarnessUsage, StringComparison.Ordinal))
				{
					continue;
				}

				if (!attributes.Contains("[Collection(", StringComparison.Ordinal))
				{
					offenders.Add(Path.GetFileName(file) + "#" + name);
				}
			}
		}

		Assert.True(offenders.Count == 0,
			"这些测试类启动了 LocalApiTestHarness 但没声明 [Collection(...)]，会和别的 harness 类并行冲撞：" +
			string.Join("、", offenders));
	}

	/// <summary>
	/// 反向自检：判定逻辑能区分「有 Collection 的类」与「没有的类」，且不把同文件里的非 harness 类算进来。
	/// 这条保证上面那条不是永远通过的空壳。
	/// </summary>
	[Fact]
	public void Detector_ScopesByClass_NotByFile()
	{
		string text = "using Xunit;\n\n"
			+ "public class NoHarness\n{\n}\n\n"
			+ "[Collection(\"LocalApiService\")]\npublic class HarnessOk\n{\n\tvoid Run() { LocalApiTestHarness.StartAsync(new AppConfig()); }\n}\n\n"
			+ "public class HarnessMissing\n{\n\tvoid Run() { LocalApiTestHarness.StartAsync(new AppConfig()); }\n}\n";

		List<string> offenders = SplitClasses(text)
			.Where(c => c.Body.Contains(HarnessUsage, StringComparison.Ordinal))
			.Where(c => !c.Attributes.Contains("[Collection(", StringComparison.Ordinal))
			.Select(c => c.Name)
			.ToList();

		Assert.Equal(new[] { "HarnessMissing" }, offenders);
	}
}
