using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;
using Xunit.Abstractions;

namespace Pineglow.Launcher.Tests;

/// <summary>
/// MR19 的 CI 不变量（缺口登记表 A11）：把"工作流里必须成立的事"钉进**本地测试套件**。
///
/// 为什么用测试而不是加一个 CI 步骤：本机没有 runner，加一个**从未被执行过**的 CI 步骤
/// 无法被验证；而这些不变量是**纯文本可判**的，放进测试套件就能每次全量都检查一遍。
/// 另外本包**实跑了 actionlint 1.7.7**（对两个工作流 exit 0）作为语法层的验证 ——
/// 但 actionlint 需要外部二进制，不适合放进测试，因此它只作为本包的证据。
///
/// **口径说明（不要夸大）**：本类做的是**文本级**不变量检查，**不是** YAML 解析，
/// 也**不能**替代 actionlint 的语法校验；它防的是"有人把守卫删了/顺序挪了/给 RID 发布加回锁定"
/// 这类**语义回退**。
/// </summary>
public class WorkflowInvariantTests
{
	private readonly ITestOutputHelper _out;

	public WorkflowInvariantTests(ITestOutputHelper output) => _out = output;

	private static string RepoRoot
	{
		get
		{
			string dir = AppContext.BaseDirectory;
			for (int i = 0; i < 8 && dir != null; i++)
			{
				if (Directory.Exists(Path.Combine(dir, ".github", "workflows")))
				{
					return dir;
				}

				dir = Path.GetDirectoryName(dir);
			}

			throw new DirectoryNotFoundException("找不到仓库根（.github/workflows 不存在）");
		}
	}

	private static string ReadWorkflow(string name) =>
		File.ReadAllText(Path.Combine(RepoRoot, ".github", "workflows", name));

	private static int LineOf(string[] lines, Func<string, bool> predicate)
	{
		for (int i = 0; i < lines.Length; i++)
		{
			if (predicate(lines[i]))
			{
				return i + 1;
			}
		}

		return -1;
	}

	private static readonly string[] BothWorkflows = { "build.yml", "release.yml" };

	/* ===================== 1. 守卫必须存在且真的会拒绝 ===================== */

	[Theory]
	[InlineData("build.yml")]
	[InlineData("release.yml")]
	public void EveryWorkflow_GuardsLockFilesAgainstRidForm(string workflow)
	{
		string text = ReadWorkflow(workflow);
		string[] lines = text.Split('\n');

		Assert.True(text.Contains("Guard lock", StringComparison.OrdinalIgnoreCase),
			$"{workflow} 必须包含 lock 文件守卫步骤（步骤名含 Guard lock）");

		// 守卫必须真的检查"RID 段"这一条
		Assert.True(text.Contains(@"""net10\.0/") || text.Contains("\"net10\\.0/"),
			$"{workflow} 的守卫必须检查 RID 段标记 net10.0/（否则拦不住被改写的 lock）");

		// 以及"只由 RID 发布注入"的那个包
		Assert.True(text.Contains("Microsoft\\.NET\\.ILLink\\.Tasks") || text.Contains(@"Microsoft\.NET\.ILLink\.Tasks"),
			$"{workflow} 的守卫必须检查 Microsoft.NET.ILLink.Tasks（只由 RID 发布注入）");

		// 守卫必须检查**两个** lock 文件（launcher + tests），否则漏一个就等于没守
		Assert.True(text.Contains("dev/PineglowLauncher/packages.lock.json"),
			$"{workflow} 的守卫必须覆盖 launcher 的 lock 文件");
		Assert.True(text.Contains("dev/PineglowLauncher.Tests/packages.lock.json"),
			$"{workflow} 的守卫必须覆盖 tests 的 lock 文件");

		// 守卫必须**以失败退出**（不能只打印警告）
		bool fails = text.Contains("exit 1") || text.Contains("throw ");
		Assert.True(fails, $"{workflow} 的守卫必须以失败退出（exit 1 / throw），不能只警告");
		_out.WriteLine($"[A11] {workflow}：守卫存在，检查 RID 段与 ILLink，覆盖两个 lock，会失败退出");
	}

	/* ===================== 2. 非 RID 锁定还原必须存在（守卫保护的就是它） ===================== */

	[Theory]
	[InlineData("build.yml")]
	[InlineData("release.yml")]
	public void EveryWorkflow_RestoresLauncherWithLockedModeNonRid(string workflow)
	{
		string text = ReadWorkflow(workflow);
		Assert.True(text.Contains("--locked-mode"),
			$"{workflow} 必须对非 RID 路径使用 --locked-mode（这正是守卫要保护的还原）");
		Assert.Contains("dev/PineglowLauncher/Pineglow.Launcher.csproj", text, StringComparison.Ordinal);
		_out.WriteLine($"[A11] {workflow}：存在非 RID 的 --locked-mode 还原");
	}

	/* ===================== 3. 顺序：守卫必须在 RID 发布之前 ===================== */

	[Theory]
	[InlineData("build.yml")]
	[InlineData("release.yml")]
	public void Guard_AppearsBeforeTheRidPublishStep(string workflow)
	{
		string[] lines = ReadWorkflow(workflow).Split('\n');
		int guard = LineOf(lines, l => l.Contains("Guard lock", StringComparison.OrdinalIgnoreCase));
		int publish = LineOf(lines, l => l.TrimStart().StartsWith("- name: Publish", StringComparison.Ordinal));

		Assert.True(guard > 0, $"{workflow} 找不到守卫步骤");
		Assert.True(publish > 0, $"{workflow} 找不到 Publish 步骤");
		Assert.True(guard < publish,
			$"{workflow} 的守卫（第 {guard} 行）必须排在 RID 发布（第 {publish} 行）**之前** —— 排在后面等于没守");
		_out.WriteLine($"[A11] {workflow}：守卫在第 {guard} 行、Publish 在第 {publish} 行（顺序正确）");
	}

	/* ===================== 4. RID 发布不得使用锁定还原（已实测互斥） ===================== */

	[Theory]
	[InlineData("build.yml")]
	[InlineData("release.yml")]
	public void RidPublish_MustNotUseRestoreLockedMode(string workflow)
	{
		string[] lines = ReadWorkflow(workflow).Split('\n');
		// 只看**可执行行**：注释里正当地解释了"RID 发布 + RestoreLockedMode 会 NU1004"，
		// 若不排除注释，本用例会被自己的说明文字误伤（本包第一次运行就是这样失败的）。
		var publishLines = lines
			.Where(l => !l.TrimStart().StartsWith("#", StringComparison.Ordinal))
			.Where(l => l.Contains("dotnet publish") && l.Contains("-r "))
			.ToList();
		Assert.True(publishLines.Count >= 1, $"{workflow} 应至少有一条 RID 发布命令");

		foreach (string line in publishLines)
		{
			Assert.False(line.Contains("RestoreLockedMode", StringComparison.OrdinalIgnoreCase),
				$"{workflow} 的 RID 发布不得使用 RestoreLockedMode —— 实测必然 NU1004（与 --locked-mode 互斥）：{line.Trim()}");
		}

		// 互斥的理由应当被写在注释里，避免以后有人"顺手加上锁定"
		Assert.True(ReadWorkflow(workflow).Contains("NU1004"),
			$"{workflow} 应把 NU1004 互斥的理由写在注释里，防止后人误加锁定");
		_out.WriteLine($"[A11] {workflow}：{publishLines.Count} 条 RID 发布均未使用 RestoreLockedMode，且注释记录了 NU1004 理由");
	}

	/* ===================== 5. 锁定文件必须只有一份（不得出现 RID 专用 lock） ===================== */

	[Theory]
	[InlineData("build.yml")]
	[InlineData("release.yml")]
	public void Workflows_DoNotReferenceRidSpecificLockFiles(string workflow)
	{
		string text = ReadWorkflow(workflow);
		foreach (string rid in new[] { "win-x64", "win-arm64", "linux-x64", "osx-x64" })
		{
			Assert.False(text.Contains($"packages.{rid}.lock.json", StringComparison.OrdinalIgnoreCase),
				$"{workflow} 不得引用 RID 专用 lock 文件（本项目只有一份 lock）：packages.{rid}.lock.json");
		}
	}

	/* ===================== 6. 端到端冒烟必须仍接在 CI 上 ===================== */

	[Fact]
	public void BuildWorkflow_KeepsTheBisectSmokeWired()
	{
		string text = ReadWorkflow("build.yml");
		Assert.True(text.Contains("--bisect-smoke"),
			"build.yml 必须继续跑 --bisect-smoke：这是二分定位的端到端冒烟（MR06/MR07 的证据来源）");
		Assert.True(text.Contains("VerifyHost"), "冒烟应通过 VerifyHost 运行");
		_out.WriteLine("[A11] build.yml 仍接入 --bisect-smoke");
	}

	/* ===================== 7. SDK 必须按 global.json 固定 ===================== */

	[Theory]
	[InlineData("build.yml")]
	[InlineData("release.yml")]
	public void EveryWorkflow_PinsTheSdkFromGlobalJson(string workflow)
	{
		string text = ReadWorkflow(workflow);
		Assert.Contains("actions/setup-dotnet@", text, StringComparison.Ordinal);
		Assert.True(text.Contains("global.json", StringComparison.OrdinalIgnoreCase),
			$"{workflow} 的 .NET 安装必须从 global.json 取版本（否则 SDK 漂移会让锁定还原与构建结果不可复现）");
		_out.WriteLine($"[A11] {workflow}：SDK 由 global.json 固定");
	}
}
