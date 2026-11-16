using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Pineglow.Launcher.Services;
using Xunit;

namespace Pineglow.Launcher.Tests;

/// <summary>
/// S9 验收的可执行部分：**「CurseForge 第三方 Key 不进分发程序」**。
///
/// 这一条以前是"文档里写着要求、代码里内嵌了一个 60 字符的 Key"，属于最糟的组合：
/// 验收标准存在，但没人能自动发现它被违反。这里补两道机器门槛：
///   1. 源码树里**不得**再出现 CurseForge Key 的字面量（用 `$2a$10$` 前缀识别）；
///   2. 运行期 Key 只来自「调用方传入 → 环境变量」，两者都没有时必须抛**可执行**的提示。
/// </summary>
[Collection("LocalApiService")]
public sealed class SecretHygieneTests
{
	private const string SourceRoot = "dev/PineglowLauncher";

	private static string RepoRoot => Path.GetFullPath(Path.Combine(
		AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

	[Fact]
	public void SourceTree_HasNoCurseForgeApiKeyLiteral()
	{
		string root = Path.Combine(RepoRoot, SourceRoot);
		Assert.True(Directory.Exists(root), "找不到源码目录：" + root);

		List<string> offenders = Directory
			.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
			.Where(path => !path.Contains("/bin/", StringComparison.Ordinal) && !path.Contains("/obj/", StringComparison.Ordinal))
			.Where(path => File.ReadAllText(path).Contains("$2a$10$", StringComparison.Ordinal))
			.Select(Path.GetFileName)
			.ToList();

		Assert.True(offenders.Count == 0,
			"源码里仍有 CurseForge API Key 字面量（第三方 Key 不得进分发程序）：" + string.Join("、", offenders));
	}

	[Fact]
	public void Resolve_UsesConfiguredValueFirst()
	{
		string? before = Environment.GetEnvironmentVariable(ModContentService.CurseForgeApiKeyEnvironmentVariable);
		try
		{
			Environment.SetEnvironmentVariable(ModContentService.CurseForgeApiKeyEnvironmentVariable, "from-env");
			Assert.Equal("from-env", ModContentService.ResolveCurseForgeApiKey(null));
			Assert.Equal("from-env", ModContentService.ResolveCurseForgeApiKey("   "));
			// 调用方给了就用调用方的（设置页优先于环境变量）
			Assert.Equal("from-config", ModContentService.ResolveCurseForgeApiKey("from-config"));
		}
		finally
		{
			Environment.SetEnvironmentVariable(ModContentService.CurseForgeApiKeyEnvironmentVariable, before);
		}
	}

	[Fact]
	public void Resolve_ReturnsEmpty_WhenNothingConfigured()
	{
		string? before = Environment.GetEnvironmentVariable(ModContentService.CurseForgeApiKeyEnvironmentVariable);
		try
		{
			Environment.SetEnvironmentVariable(ModContentService.CurseForgeApiKeyEnvironmentVariable, null);
			Assert.Equal(string.Empty, ModContentService.ResolveCurseForgeApiKey(null));
			Assert.Equal(string.Empty, ModContentService.ResolveCurseForgeApiKey(string.Empty));
		}
		finally
		{
			Environment.SetEnvironmentVariable(ModContentService.CurseForgeApiKeyEnvironmentVariable, before);
		}
	}

	[Fact]
	public void MissingKeyError_TellsUserWhereToPutIt()
	{
		// 错误提示必须能执行：要么去设置页填，要么设环境变量。
		// 断言的是**产品里的同一个字符串**，不是测试自己拼一份。
		string message = ModContentService.MissingKeyMessage;

		Assert.Contains("设置页", message);
		Assert.Contains(ModContentService.CurseForgeApiKeyEnvironmentVariable, message);
		Assert.DoesNotContain("默认 Key", message);
	}
}
