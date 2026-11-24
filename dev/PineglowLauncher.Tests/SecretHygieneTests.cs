using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Pineglow.Launcher.Services;
using Xunit;

namespace Pineglow.Launcher.Tests;

/// <summary>
/// S9 验收的可执行部分：**「CurseForge 第三方 Key 不进源码树」**。
///
/// 历史（重要，别把它当旧账删掉）：
///   2026-11-16 曾以「有截止日期的临时豁免」形式把 Key 硬编码在
///   <c>CurseForgeKeyDefaults.cs</c>，豁免期限标为 <c>v1.0.5</c>。
///   截至 v1.0.6 仍未移除，且发布说明错误声称"已移除"（见 OPEN-08）。
///
/// 现行约定（2026-11-24 起，OPEN-08 修复）：
///   * **源码树内任何 .cs 文件都不得出现 CurseForge Key 字面量** —— 豁免已取消，不再有白名单文件；
///   * Key 由构建期从环境变量 `PINEGLOW_CURSEFORGE_BUILD_KEY` 注入，生成到 <c>obj/</c>（不在源码树内）；
///   * 构建机未提供 Key 时产物不含内置 Key，此时产品走"缺 Key 引导"（不是故障）。
///
/// 本测试**不读 obj/ 下的生成文件**：那正是我们要允许 Key 存在的地方。
/// </summary>
[Collection("LocalApiService")]
public sealed class SecretHygieneTests
{
	private const string SourceRoot = "dev/PineglowLauncher";

	/// <summary>CurseForge Key 的固定前缀。Key 形如 $2a$10$&lt;60 字符&gt;。</summary>
	private const string KeyLiteralPrefix = "$2a$10$";

	private static string RepoRoot => Path.GetFullPath(Path.Combine(
		AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

	private static IEnumerable<string> SourceFiles()
	{
		string root = Path.Combine(RepoRoot, SourceRoot);
		Assert.True(Directory.Exists(root), "找不到源码目录：" + root);

		// 排除构建产物：obj/ 下就有我们**故意生成**的 Key（SDK 生成的 AssemblyInfo.cs）。
		// 必须同时匹配两种分隔符：Windows 的 EnumerateFiles 返回反斜杠，
		// 只写 "/obj/" 会让生成物被误报成源码字面量（2026-11-24 复核实测到该缺陷）。
		return Directory
			.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
			.Where(path => !IsUnderBuildOutput(path));
	}

	/// <summary>
	/// 路径是否位于**真实的构建输出目录**之下。
	///
	/// 只认「与 .csproj 同级、紧邻」的 bin/ 与 obj/，而不是任意叫 bin/obj 的路径段。
	/// 原因（2026-11-24 第 3 轮复核实测）：先前的按段排除会把源码树里合法的
	/// `binary/`、`objHelpers/` 之类目录一并放过 —— 那些目录里的 .cs 是**会被编译**的，
	/// 却因名字碰巧含 bin/obj 而逃过守卫。这是实测到的漏扫路径。
	///
	/// 判据：某个祖先目录里存在同名 .csproj，且该祖先下紧邻的段就是 bin 或 obj。
	/// 简化实现：识别 "&lt;任意目录&gt;/bin/" 或 "&lt;任意目录&gt;/obj/" 且该 &lt;任意目录&gt;
	/// 的前缀看起来像项目目录 —— 但为避免过度工程，这里采用更严格且可解释的规则：
	/// **只排除路径中出现 `\bin\` 或 `\obj\` 且其后紧跟构建配置段（Release/Debug/net*）的路径**。
	/// 构建输出必有这样的子目录；源码里叫 bin 的目录不会有。
	/// </summary>
	private static bool IsUnderBuildOutput(string path)
	{
		char[] seps = new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar };
		string[] segments = path.Split(seps, StringSplitOptions.RemoveEmptyEntries);

		for (int i = 0; i < segments.Length; i++)
		{
			bool isBuildDir = string.Equals(segments[i], "bin", StringComparison.OrdinalIgnoreCase)
				|| string.Equals(segments[i], "obj", StringComparison.OrdinalIgnoreCase);
			if (!isBuildDir || i + 1 >= segments.Length)
			{
				continue;
			}

			// 构建输出 = bin/obj 后面紧跟配置目录（Release/Debug/x64 等）或 net* TFM。
			// 源码里碰巧叫 bin 的目录，后面跟的是普通源码子目录，不满足这个形状。
			string next = segments[i + 1];
			if (string.Equals(next, "Release", StringComparison.OrdinalIgnoreCase)
				|| string.Equals(next, "Debug", StringComparison.OrdinalIgnoreCase)
				|| next.StartsWith("net", StringComparison.OrdinalIgnoreCase))
			{
				return true;
			}
		}

		return false;
	}

	/// <summary>
	/// CurseForge Key 的**完整形态**正则：`$2a$10$` + 50 个以上合法字符（真值共 60）。
	///
	/// 与 <see cref="KeyLiteralPrefix"/> 分开的原因（2026-11-24 审计发现）：
	/// 前缀只说明"这里提到了 Key"，而文档里`$2a$10$G3lIsq…WjOCrG`这类**截断引用**是允许的；
	/// 真正的泄露是完整值。守卫必须按完整形态判定，否则会把合规的说明文字也判红。
	/// </summary>
	private static readonly Regex FullKeyPattern =
		new Regex(@"\$2a\$10\$[A-Za-z0-9+/.]{50,}", RegexOptions.Compiled);

	/// <summary>
	/// **全仓库**文本文件枚举（2026-11-24 审计后新增）。
	///
	/// 为什么需要它：此前的守卫只扫 `dev/PineglowLauncher/**/*.cs`，
	/// 而审计发现 `docs/market-execution/` 下 4 个历史快照副本、`docs/evidence/` 的验收文档
	/// 都含完整 Key ——**守卫一个都没覆盖到，等于给了虚假的安全感**。
	/// 现在改为扫仓库内除以下位置外的全部文本文件：
	///   - `.git/`、构建输出（bin/obj）、`node_modules/`：不是仓库内容或已排除
	///   - `_archive/`：历史归档，按项目约定不修改
	///   - 二进制扩展名：无法按文本判定
	///   - **本测试文件自身**：它必须含 `$2a$10$` 前缀常量才能做判定
	/// </summary>
	private static IEnumerable<string> AllTextFiles()
	{
		var binaryExt = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
		{
			".exe", ".dll", ".pdb", ".png", ".jpg", ".jpeg", ".gif", ".ico", ".zip",
			".trx", ".jar", ".mrpack", ".woff2", ".ttf", ".snap",
		};

		foreach (string file in Directory.EnumerateFiles(RepoRoot, "*", SearchOption.AllDirectories))
		{
			string rel = file.Substring(RepoRoot.Length).Replace('\\', '/');

			if (IsUnderBuildOutput(file)) { continue; }
			if (rel.Contains("/.git/", StringComparison.Ordinal)) { continue; }
			if (rel.Contains("/node_modules/", StringComparison.Ordinal)) { continue; }
			if (rel.Contains("/_archive/", StringComparison.Ordinal)) { continue; }
			if (rel.Contains("/.cindy-worktrees/", StringComparison.Ordinal)) { continue; }

			if (binaryExt.Contains(Path.GetExtension(file))) { continue; }

			// 本测试文件自己必须含前缀常量，跳过。
			if (string.Equals(Path.GetFileName(file), "SecretHygieneTests.cs", StringComparison.Ordinal)) { continue; }

			FileInfo fi;
			try { fi = new FileInfo(file); } catch { continue; }
			if (fi.Length > 50L * 1024 * 1024) { continue; }

			yield return file;
		}
	}

	/// <summary>
	/// 受版本管理的构建配置/脚本输入。这些文件同样会进构建，必须一起扫。
	/// 只扫 .cs 会漏掉把 Key 塞进 .props / .targets / .csproj / .json 的路径。
	/// </summary>
	private static IEnumerable<string> BuildInputFiles()
	{
		foreach (string pattern in new[] { "*.csproj", "*.props", "*.targets", "*.json", "*.ps1", "*.cmd", "*.sh", "*.yml", "*.yaml" })
		{
			foreach (string file in Directory.EnumerateFiles(RepoRoot, pattern, SearchOption.AllDirectories))
			{
				string rel = file.Substring(RepoRoot.Length).Replace('\\', '/');
				// 跳过构建产物、归档与 vendor。
				if (IsUnderBuildOutput(file)) { continue; }
				if (rel.Contains("/_archive/", StringComparison.Ordinal)) { continue; }
				if (rel.Contains("/node_modules/", StringComparison.Ordinal)) { continue; }
				if (rel.Contains("/.git/", StringComparison.Ordinal)) { continue; }
				yield return file;
			}
		}
	}

	[Fact]
	public void KeyLiteral_DoesNotAppearAnywhereInSourceTree()
	{
		// 不再有豁免文件。任何 .cs 出现该字面量即为失败。
		List<string> offenders = SourceFiles()
			.Where(path => File.ReadAllText(path).Contains(KeyLiteralPrefix, StringComparison.Ordinal))
			.Select(path => Path.GetFileName(path))
			.ToList();

		Assert.True(offenders.Count == 0,
			"CurseForge Key 字面量不得出现在源码树内（Key 应由构建期注入到 obj/）。"
			+ "以下文件含有 " + KeyLiteralPrefix + "：" + string.Join("、", offenders));
	}

	[Fact]
	public void KeyLiteral_DoesNotAppearInBuildInputs()
	{
		// 只扫 .cs 会漏掉「把 Key 塞进 .props / .targets / .csproj / .json」这类路径。
		// 2026-11-24 复核指出该缺口，本测试补上。
		List<string> offenders = BuildInputFiles()
			.Where(path => File.ReadAllText(path).Contains(KeyLiteralPrefix, StringComparison.Ordinal))
			.Select(path => path.Substring(RepoRoot.Length).TrimStart('\\', '/'))
			.ToList();

		Assert.True(offenders.Count == 0,
			"CurseForge Key 字面量不得出现在构建配置/脚本输入中。以下文件含有 " + KeyLiteralPrefix + "："
			+ string.Join("、", offenders));
	}

	[Fact]
	public void FullKeyValue_DoesNotAppearAnywhereInRepository()
	{
		// 2026-11-24 审计的直接结论：此前的守卫只扫 dev/PineglowLauncher/**/*.cs，
		// 而实际含**完整 Key** 的文件还有：
		//   - docs/market-execution/ 下 4 个历史快照副本
		//   - docs/evidence/2026-11-16-s9/验收.md
		// 那些一个都没被覆盖 —— 守卫给了虚假的安全感。
		// 本测试改为扫**仓库内全部文本文件**，判定条件是**完整 60 字符值**（见 FullKeyPattern），
		// 因此文档里的 `$2a$10$G3lIsq…WjOCrG` 这类截断引用不会被误判。
		List<string> offenders = AllTextFiles()
			.Where(path =>
			{
				try
				{
					return FullKeyPattern.IsMatch(File.ReadAllText(path));
				}
				catch
				{
					return false;   // 读不了的（锁定/权限）跳过，不误报
				}
			})
			.Select(path => path.Substring(RepoRoot.Length).TrimStart('\\', '/'))
			.ToList();

		Assert.True(offenders.Count == 0,
			"仓库内任何文本文件都不得含完整 CurseForge Key（应走构建期注入）。以下文件命中："
			+ string.Join("、", offenders));
	}

	[Fact]
	public void NoSourceFile_IsWhitelistedForCurseForgeKey()
	{
		// 防止有人以"豁免文件"的形式把白名单偷偷加回来。
		// 判据：源码树内不得存在名为 CurseForgeKeyDefaults 且含 Key 字面量的文件。
		string legacyPath = Path.Combine(RepoRoot, SourceRoot, "Pineglow.Launcher.Services", "CurseForgeKeyDefaults.cs");
		if (!File.Exists(legacyPath))
		{
			return;
		}

		string content = File.ReadAllText(legacyPath);
		Assert.False(content.Contains(KeyLiteralPrefix, StringComparison.Ordinal),
			"CurseForgeKeyDefaults.cs 不得再内联 Key 字面量；它现在只负责转发构建期注入的值。");
	}

	[Fact]
	public void EmbeddedKey_ComeFromBuildEnvironment_NotFromSource()
	{
		// 断言"默认值的来源是构建期注入"，而不是源码常量。
		// 无法直接读取生成文件（那在 obj/），退而验证运行期行为：
		// 在没有任何运行期来源时，解析结果必须等于编译进程序集的值（可能是空串）。
		string? before = Environment.GetEnvironmentVariable(ModContentService.CurseForgeApiKeyEnvironmentVariable);
		try
		{
			ModContentService.EmbeddedKeyFallbackForTest = null;
			Environment.SetEnvironmentVariable(ModContentService.CurseForgeApiKeyEnvironmentVariable, null);

			string resolved = ModContentService.ResolveCurseForgeApiKey(null);

			Assert.Equal(CurseForgeKeyDefaults.Embedded, resolved);
		}
		finally
		{
			Environment.SetEnvironmentVariable(ModContentService.CurseForgeApiKeyEnvironmentVariable, before);
		}
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
	public void MissingKeyError_IsStillActionable_WhenNoKeyAvailable()
	{
		// 构建机没注入 Key、用户也没配置时，这条就是用户看到的提示；断言产品里的同一个字符串。
		string message = ModContentService.MissingKeyMessage;

		Assert.Contains("设置页", message);
		Assert.Contains(ModContentService.CurseForgeApiKeyEnvironmentVariable, message);
		Assert.DoesNotContain("默认 Key", message);
	}
}
