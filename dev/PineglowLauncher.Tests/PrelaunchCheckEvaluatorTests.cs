using System;
using System.Collections.Generic;
using System.Linq;
using Pineglow.Launcher.Services;
using Xunit;

namespace Pineglow.Launcher.Tests;

/// <summary>
/// M2-1④ 启动前自检的判定层直测。
///
/// 为什么单独为「判定」建测试文件：原实现把 IO 与判定混在 <c>HandlePrelaunchCheckAsync</c> 里，
/// 想验证「内存接近上限要给警告」就必须先造一台内存刚好不够的机器，所以这条分支从来没被测过。
/// 现在判定收进 <see cref="PrelaunchCheckEvaluator"/>（纯函数、无 IO），可以逐条钉住。
///
/// 钉的三件事：
///   1. **问题码稳定**——前端按 code 分支，改码等于改接口，必须被测试挡住；
///   2. **action 必须指向真实存在的界面入口**——给了按钮却跳不到，比不给按钮更糟；
///   3. **errors/warnings 与 issues 不可能互相矛盾**——旧字段由新字段派生，这里验证派生口径。
/// </summary>
public sealed class PrelaunchCheckEvaluatorTests
{
	/* ===================== 辅助 ===================== */

	/// <summary>一切正常的事实基线，单项测试只改它关心的那一个字段。</summary>
	private static PrelaunchFacts Healthy() => new PrelaunchFacts(
		MinecraftVersion: "1.20.1",
		JavaPath: @"C:\java\bin\javaw.exe",
		JavaPathExists: true,
		JavaResolveError: string.Empty,
		GameDirectory: @"D:\game",
		GameDirectoryExists: true,
		DriveFreeBytes: 64L * 1024 * 1024 * 1024,
		AvailableMemoryBytes: 16L * 1024 * 1024 * 1024,
		MaxMemoryMb: 4096);

	private static List<PrelaunchIssue> Evaluate(PrelaunchFacts facts) => PrelaunchCheckEvaluator.Evaluate(facts);

	/* ===================== 正常路径 ===================== */

	[Fact]
	public void HealthyFacts_ProduceNoIssues()
	{
		Assert.Empty(Evaluate(Healthy()));
	}

	/* ===================== 每个问题码各一条 ===================== */

	[Fact]
	public void MissingVersion_IsAnErrorPointingAtInstallVersion()
	{
		PrelaunchIssue issue = Assert.Single(Evaluate(Healthy() with { MinecraftVersion = "  " }));

		Assert.Equal(PrelaunchIssueCodes.InstanceNoVersion, issue.Code);
		Assert.Equal(PrelaunchIssue.SeverityError, issue.Severity);
		Assert.True(issue.IsError);
		Assert.Equal(PrelaunchActions.InstallVersion, issue.Action);
		Assert.Contains("游戏版本", issue.Message);
		// 动作按钮必须有文案，否则前端渲染出空按钮
		Assert.False(string.IsNullOrWhiteSpace(issue.ActionLabel));
	}

	[Fact]
	public void MissingJava_IsAnErrorPointingAtSelectJava()
	{
		PrelaunchIssue issue = Assert.Single(Evaluate(Healthy() with { JavaPathExists = false }));

		Assert.Equal(PrelaunchIssueCodes.JavaMissing, issue.Code);
		Assert.Equal(PrelaunchActions.SelectJava, issue.Action);
		Assert.Contains(@"C:\java\bin\javaw.exe", issue.Message);
	}

	[Fact]
	public void JavaResolveError_IsADistinctCodeFromJavaMissing()
	{
		// 这两个场景用户要做的事不同（一个去重选，一个去查磁盘），
		// 如果共用一个码，前端就只能给同一条引导。
		PrelaunchIssue issue = Assert.Single(Evaluate(Healthy() with
		{
			JavaPathExists = false,
			JavaResolveError = "未找到可用的 Java 运行时",
		}));

		Assert.Equal(PrelaunchIssueCodes.JavaResolveFailed, issue.Code);
		Assert.Equal(PrelaunchActions.SelectJava, issue.Action);
		Assert.Contains("未找到可用的 Java", issue.Message);
	}

	[Fact]
	public void JavaResolveError_TakesPrecedenceOverJavaMissing()
	{
		// 解析都失败了，"文件是否存在"这个判断本身没有意义，不能同时报两条。
		// 保证：JavaResolveError 非空时只产出 JavaResolveFailed 一条。
		List<PrelaunchIssue> issues = Evaluate(Healthy() with
		{
			JavaPathExists = false,
			JavaPath = string.Empty,
			JavaResolveError = "boom",
		});

		Assert.Single(issues);
		Assert.Equal(PrelaunchIssueCodes.JavaResolveFailed, issues[0].Code);
	}

	[Fact]
	public void MissingGameDirectory_IsOnlyAWarning()
	{
		// 首次启动会自己创建目录，拦住启动是错的。
		PrelaunchIssue issue = Assert.Single(Evaluate(Healthy() with { GameDirectoryExists = false }));

		Assert.Equal(PrelaunchIssueCodes.GameDirMissing, issue.Code);
		Assert.Equal(PrelaunchIssue.SeverityWarning, issue.Severity);
		Assert.False(issue.IsError);
	}

	[Theory]
	[InlineData(0, false)]                                        // 0 = 拿不到，按"未知不提示"处理
	[InlineData(100L * 1024 * 1024, true)]                         // 100 MB
	[InlineData(512L * 1024 * 1024 - 1, true)]                     // 阈值减 1 字节仍要提示
	[InlineData(512L * 1024 * 1024, false)]                        // 正好等于阈值不提示
	[InlineData(8L * 1024 * 1024 * 1024, false)]
	public void DiskSpaceWarning_BoundaryIsInclusiveOfEqual(long freeBytes, bool expectWarning)
	{
		List<PrelaunchIssue> issues = Evaluate(Healthy() with { DriveFreeBytes = freeBytes });
		bool has = issues.Any(x => x.Code == PrelaunchIssueCodes.DiskLowSpace);

		Assert.Equal(expectWarning, has);
		if (has)
		{
			Assert.Equal(PrelaunchActions.OpenInstanceSettings, issues.Single(x => x.Code == PrelaunchIssueCodes.DiskLowSpace).Action);
		}
	}

	[Theory]
	[InlineData(0L, false)]                                        // 0 = 未知，不提示
	[InlineData(8L * 1024 * 1024 * 1024, false)]                   // 可用 8 GB、要 4 GB → 50%，正常
	[InlineData(5L * 1024 * 1024 * 1024, false)]                   // 可用 5 GB、要 4 GB → 80%，未达 90%
	[InlineData(4L * 1024 * 1024 * 1024, true)]                    // 可用 4 GB、要 4 GB → 100%，必须提示
	[InlineData(2L * 1024 * 1024 * 1024, true)]                    // 要 4 GB 但只有 2 GB → 提示
	public void MemoryWarning_UsesNinetyPercentThreshold(long availableBytes, bool expectWarning)
	{
		// 判定口径：requestedBytes >= availableBytes * 0.9。
		// 第 4 条是刻意保留的边界：4096MB 请求对 4096MB 可用 = 100% 占用，
		// 按 90% 阈值必然命中。这条曾因代码把 MB 与字节直接比较而失败，
		// 正是它抓到了单位错位（见 PrelaunchCheck.cs 里的注释）。
		List<PrelaunchIssue> issues = Evaluate(Healthy() with { AvailableMemoryBytes = availableBytes });
		bool has = issues.Any(x => x.Code == PrelaunchIssueCodes.MemoryNearLimit);

		Assert.Equal(expectWarning, has);
	}

	/* ===================== 多条问题的顺序与计数 ===================== */

	[Fact]
	public void ErrorsComeBeforeWarnings_InDeclaredPriorityOrder()
	{
		// 顺序即引导优先级：版本缺失 > Java 缺失 > 目录/磁盘/内存。
		// 前端可以"取第一条错误去决定先跳哪里"，所以顺序必须稳定。
		List<PrelaunchIssue> issues = Evaluate(Healthy() with
		{
			MinecraftVersion = string.Empty,
			JavaPathExists = false,
			GameDirectoryExists = false,
			DriveFreeBytes = 1024,
			AvailableMemoryBytes = 1L * 1024 * 1024 * 1024,
		});

		Assert.Equal(new[]
		{
			PrelaunchIssueCodes.InstanceNoVersion,
			PrelaunchIssueCodes.JavaMissing,
			PrelaunchIssueCodes.GameDirMissing,
			PrelaunchIssueCodes.DiskLowSpace,
			PrelaunchIssueCodes.MemoryNearLimit,
		}, issues.Select(x => x.Code).ToArray());

		// 前两条是 error，后三条是 warning —— 也就是"错误在前"这个不变量
		Assert.Equal(new[] { true, true, false, false, false }, issues.Select(x => x.IsError).ToArray());
	}

	[Fact]
	public void AllIssuesHaveNonEmptyCodeMessageAndRegisteredAction()
	{
		// 这条是「新增问题时忘记登记」的门槛：任何一个码或动作不在白名单里都会红。
		List<PrelaunchIssue> issues = Evaluate(new PrelaunchFacts(
			MinecraftVersion: string.Empty,
			JavaPath: string.Empty,
			JavaPathExists: false,
			JavaResolveError: "x",
			GameDirectory: @"D:\nope",
			GameDirectoryExists: false,
			DriveFreeBytes: 1,
			AvailableMemoryBytes: 1L * 1024 * 1024 * 1024,
			MaxMemoryMb: 4096));

		Assert.NotEmpty(issues);
		foreach (PrelaunchIssue issue in issues)
		{
			Assert.False(string.IsNullOrWhiteSpace(issue.Code), "问题码不能为空");
			Assert.False(string.IsNullOrWhiteSpace(issue.Message), "问题文案不能为空");
			Assert.Contains(issue.Code, PrelaunchIssueCodes.All);
			Assert.Contains(issue.Severity, new[] { PrelaunchIssue.SeverityError, PrelaunchIssue.SeverityWarning });
			if (issue.Action != null)
			{
				Assert.Contains(issue.Action, PrelaunchActions.All);
				Assert.False(string.IsNullOrWhiteSpace(issue.ActionLabel), "给了 action 就必须给按钮文案");
			}
		}
	}

	/* ===================== errors/warnings 派生口径 ===================== */

	[Fact]
	public void LegacyArraysAreDerivedFromIssues_SoTheyCannotContradict()
	{
		// 这是 handler 里的派生逻辑，但口径必须一致，所以在这里用同一套规则复算一遍。
		// 若将来有人把 handler 改回"手工往 errors 里 add"，这条测试不会红——
		// 所以 handler 一侧另有一条端到端断言（见 PrelaunchCheckEndpointTests）。
		List<PrelaunchIssue> issues = Evaluate(Healthy() with
		{
			MinecraftVersion = string.Empty,
			GameDirectoryExists = false,
		});

		string[] errors = issues.Where(x => x.IsError).Select(x => x.Message).ToArray();
		string[] warnings = issues.Where(x => !x.IsError).Select(x => x.Message).ToArray();

		Assert.Single(errors);
		Assert.Single(warnings);
		Assert.Contains("游戏版本", errors[0]);
		Assert.Contains("游戏目录不存在", warnings[0]);
	}

	/* ===================== 格式化口径 ===================== */

	[Theory]
	[InlineData(0L, "0 B")]
	[InlineData(512L, "512 B")]
	[InlineData(1024L, "1 KB")]
	[InlineData(1536L, "1.5 KB")]
	[InlineData(1048576L, "1 MB")]
	[InlineData(1073741824L, "1 GB")]
	public void FormatBytes_MatchesTheHandlerConvention(long bytes, string expected)
	{
		Assert.Equal(expected, PrelaunchCheckEvaluator.FormatBytes(bytes));
	}
}
