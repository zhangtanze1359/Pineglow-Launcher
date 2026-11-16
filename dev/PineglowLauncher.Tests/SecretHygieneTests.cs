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
/// 2026-11-16 编排方决定**本版本暂不轮换**（还没有真实用户），所以当前是"有截止日期的临时豁免"：
///   * 字面量只允许出现在 <c>CurseForgeKeyDefaults.cs</c> 这一个文件里；
///   * 该文件必须带 `RemovalDeadline` 标记（下个版本删它时，这两条测试会强制提醒）；
///   * 解析顺序仍是 调用方传入 → 环境变量 → 内置回退。
/// </summary>
[Collection("LocalApiService")]
public sealed class SecretHygieneTests
{
	private const string SourceRoot = "dev/PineglowLauncher";
	private const string ExemptFileName = "CurseForgeKeyDefaults.cs";

	private static string RepoRoot => Path.GetFullPath(Path.Combine(
		AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

	private static IEnumerable<string> SourceFiles()
	{
		string root = Path.Combine(RepoRoot, SourceRoot);
		Assert.True(Directory.Exists(root), "找不到源码目录：" + root);
		return Directory
			.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
			.Where(path => !path.Contains("/bin/", StringComparison.Ordinal) && !path.Contains("/obj/", StringComparison.Ordinal));
	}

	[Fact]
	public void KeyLiteral_AppearsOnlyInTheExemptFile()
	{
		List<string> offenders = SourceFiles()
			.Where(path => !string.Equals(Path.GetFileName(path), ExemptFileName, StringComparison.Ordinal))
			.Where(path => File.ReadAllText(path).Contains("$2a$10$", StringComparison.Ordinal))
			.Select(Path.GetFileName)
			.ToList();

		Assert.True(offenders.Count == 0,
			"内置 Key 只允许出现在 " + ExemptFileName + "，但这些文件里也出现了：" + string.Join("、", offenders));
	}

	[Fact]
	public void ExemptFile_HasRemovalDeadlineMarker()
	{
		// 豁免是"有期限的例外"，不是"忘了删"。没有截止版本标记就不算豁免。
		Assert.False(string.IsNullOrWhiteSpace(CurseForgeKeyDefaults.RemovalDeadline));
		Assert.StartsWith("v", CurseForgeKeyDefaults.RemovalDeadline);

		string path = Path.Combine(RepoRoot, SourceRoot, "Pineglow.Launcher.Services", ExemptFileName);
		Assert.True(File.Exists(path), "豁免文件不存在：" + path);
	}

	[Fact]
	public void Resolve_PrefersConfiguredThenEnvironment_ThenEmbeddedFallback()
	{
		string? before = Environment.GetEnvironmentVariable(ModContentService.CurseForgeApiKeyEnvironmentVariable);
		try
		{
			ModContentService.EmbeddedKeyFallbackForTest = "embedded-test-key";
			Environment.SetEnvironmentVariable(ModContentService.CurseForgeApiKeyEnvironmentVariable, "from-env");

			Assert.Equal("from-env", ModContentService.ResolveCurseForgeApiKey(null));
			Assert.Equal("from-config", ModContentService.ResolveCurseForgeApiKey("from-config"));

			Environment.SetEnvironmentVariable(ModContentService.CurseForgeApiKeyEnvironmentVariable, null);
			Assert.Equal("embedded-test-key", ModContentService.ResolveCurseForgeApiKey(null));
		}
		finally
		{
			ModContentService.EmbeddedKeyFallbackForTest = null;
			Environment.SetEnvironmentVariable(ModContentService.CurseForgeApiKeyEnvironmentVariable, before);
		}
	}

	[Fact]
	public void MissingKeyError_IsStillActionable_WhenFallbackIsDisabled()
	{
		// 下个版本删掉豁免文件后，这条就是用户看到的提示；断言产品里的同一个字符串。
		string message = ModContentService.MissingKeyMessage;

		Assert.Contains("设置页", message);
		Assert.Contains(ModContentService.CurseForgeApiKeyEnvironmentVariable, message);
		Assert.DoesNotContain("默认 Key", message);
	}
}
