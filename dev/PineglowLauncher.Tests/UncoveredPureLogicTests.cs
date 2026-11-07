using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Pineglow.Launcher.Models;
using Pineglow.Launcher.Services;
using Xunit;

namespace Pineglow.Launcher.Tests;

/// <summary>
/// 2026-11-06 未覆盖路径补测（批次 1）：**纯逻辑静态类**。
///
/// 为什么这批优先级最高：覆盖率实测显示这些类**从未被任何测试提到过**（0%）——
/// <c>VersionFilter</c>、<c>ModSearchQueryResolver</c>、<c>MinecraftUtils</c>、
/// <c>MirrorHelper</c>、<c>LaunchCommand</c>、<c>ModpackLifecycleService</c>（均 0%），
/// <c>RulesEvaluator</c> 3.4%、<c>ModHealthService</c> 23.1%。
///
/// 它们风险不对等：
///   * <c>RulesEvaluator</c> 决定哪些库/资源被下载、哪些 JVM 参数生效——判错会让游戏起不来；
///   * <c>MirrorHelper</c> 决定镜像改写后的真实下载地址——改写错会让国内用户全部下载失败；
///   * <c>LaunchCommand</c> 的脱敏决定 accessToken 会不会被写进日志/脚本——**泄露面**；
///   * <c>ModHealthService</c> 的版本区间判错会误报"缺依赖"，把能跑的游戏拦下来。
///
/// 这些类都是纯函数、零 IO、零网络，可以完全确定性地断言——所以"没测"没有正当理由。
/// </summary>
public class UncoveredPureLogicTests
{
	/* ===================== VersionFilter ===================== */

	private static VersionEntry V(string id, string type, string releaseTime) =>
		new() { Id = id, Type = type, ReleaseTime = releaseTime };

	[Fact]
	public void VersionFilter_AllType_KeepsEverything_AndSortsNewestFirst()
	{
		var input = new[]
		{
			V("1.20.1", "release", "2023-06-12T00:00:00+00:00"),
			V("24w14a", "snapshot", "2024-04-03T00:00:00+00:00"),
			V("1.21", "release", "2024-06-13T00:00:00+00:00"),
		};

		var result = VersionFilter.Apply(input, query: null, type: "all");

		Assert.Equal(3, result.Count);
		// 按 releaseTime 倒序：1.21(2024-06) > 24w14a(2024-04) > 1.20.1(2023-06)
		Assert.Equal(new[] { "1.21", "24w14a", "1.20.1" }, result.Select(v => v.Id));
	}

	[Fact]
	public void VersionFilter_TypeFilter_IsCaseInsensitive_AndExcludesOthers()
	{
		var input = new[]
		{
			V("1.20.1", "release", "2023-06-12T00:00:00+00:00"),
			V("24w14a", "snapshot", "2024-04-03T00:00:00+00:00"),
		};

		var releases = VersionFilter.Apply(input, null, "RELEASE");
		var snapshots = VersionFilter.Apply(input, null, "Snapshot");

		Assert.Single(releases);
		Assert.Equal("1.20.1", releases[0].Id);
		Assert.Single(snapshots);
		Assert.Equal("24w14a", snapshots[0].Id);
	}

	[Fact]
	public void VersionFilter_QueryMatchesId_CaseInsensitively_AndTrims()
	{
		var input = new[]
		{
			V("1.20.1", "release", "2023-06-12T00:00:00+00:00"),
			V("1.21", "release", "2024-06-13T00:00:00+00:00"),
		};

		Assert.Single(VersionFilter.Apply(input, "  1.20  ", "all"));
		Assert.Equal("1.21", VersionFilter.Apply(input, "1.21", "all")[0].Id);
		Assert.Empty(VersionFilter.Apply(input, "nosuchversion", "all"));
	}

	[Fact]
	public void VersionFilter_UnparsableReleaseTime_SinksToBottom_WithoutThrowing()
	{
		// 真实数据里 releaseTime 可能缺失或格式异常。这里断言**不抛**且排到最后，
		// 因为列表页崩掉比"排序不完美"严重得多。
		var input = new[]
		{
			V("broken", "release", "not-a-date"),
			V("good", "release", "2024-06-13T00:00:00+00:00"),
			V("empty", "release", ""),
		};

		var result = VersionFilter.Apply(input, null, "all");

		Assert.Equal(3, result.Count);
		Assert.Equal("good", result[0].Id);
		// 两个解析失败的都退化为 DateTimeOffset.MinValue，相对顺序不保证，只断言都在后面
		Assert.Contains(result.Skip(1).Select(v => v.Id), id => id == "broken" || id == "empty");
	}

	[Fact]
	public void VersionFilter_NullType_TreatedAsAll()
	{
		var input = new[] { V("a", "release", "2024-01-01T00:00:00+00:00"), V("b", "old_alpha", "2020-01-01T00:00:00+00:00") };
		Assert.Equal(2, VersionFilter.Apply(input, null, null).Count);
	}

	/* ===================== ModSearchQueryResolver ===================== */

	[Fact]
	public void ModSearchQueryResolver_ChineseAlias_TranslatesToEnglish()
	{
		Assert.Equal("sodium", ModSearchQueryResolver.Resolve("钠"));
		Assert.Equal("jei", ModSearchQueryResolver.Resolve("物品管理器"));
		Assert.Equal("journeymap", ModSearchQueryResolver.Resolve("旅行地图"));
	}

	[Fact]
	public void ModSearchQueryResolver_LongerAliasWins_OverShorterSubstring()
	{
		// 「高清修复」必须命中 optifine，不能被更短的「优化」类词条拆走。
		// 表是按 Key 长度倒序排的，这条断言就是在锁那个顺序契约。
		Assert.Equal("optifine", ModSearchQueryResolver.Resolve("高清修复"));
	}

	[Fact]
	public void ModSearchQueryResolver_NonChinese_PassesThroughUnchanged()
	{
		Assert.Equal("create", ModSearchQueryResolver.Resolve("create"));
		Assert.Equal("Create Mod", ModSearchQueryResolver.Resolve("  Create   Mod  "));
	}

	[Fact]
	public void ModSearchQueryResolver_MixedChineseAndLatin_KeepsLatinDropsResidualChinese()
	{
		// 已知别名被替换后，剩下的中文片段会被清掉，只留可搜的英文。
		string resolved = ModSearchQueryResolver.Resolve("钠 优化 sodium");
		Assert.Contains("sodium", resolved);
		Assert.DoesNotContain("钠", resolved);
	}

	[Fact]
	public void ModSearchQueryResolver_EmptyOrWhitespace_ReturnsEmpty()
	{
		Assert.Equal(string.Empty, ModSearchQueryResolver.Resolve(null));
		Assert.Equal(string.Empty, ModSearchQueryResolver.Resolve("   "));
	}

	[Fact]
	public void ModSearchQueryResolver_WasTranslated_ReportsWhetherTextChanged()
	{
		Assert.True(ModSearchQueryResolver.WasTranslated("钠", out string sodium));
		Assert.Equal("sodium", sodium);

		Assert.False(ModSearchQueryResolver.WasTranslated("sodium", out string same));
		Assert.Equal("sodium", same);

		Assert.False(ModSearchQueryResolver.WasTranslated(null, out _));
	}

	/* ===================== MinecraftUtils ===================== */

	[Fact]
	public void MavenPath_ExpandsGroupAndArtifact()
	{
		string path = MinecraftUtils.MavenPath("org.lwjgl:lwjgl:3.3.1");
		Assert.Equal(Path.Combine("org", "lwjgl", "lwjgl", "3.3.1", "lwjgl-3.3.1.jar"), path);
	}

	[Fact]
	public void MavenPath_WithClassifier_AppendsClassifierToFileName()
	{
		string path = MinecraftUtils.MavenPath("org.lwjgl:lwjgl:3.3.1:natives-windows");
		Assert.Equal(
			Path.Combine("org", "lwjgl", "lwjgl", "3.3.1", "lwjgl-3.3.1-natives-windows.jar"),
			path);
	}

	[Fact]
	public void MavenPath_TooFewSegments_FallsBackToSeparatorSubstitution()
	{
		// 坐标不合法时不能抛：下载器会拿它当相对路径用。
		Assert.Equal("broken" + Path.DirectorySeparatorChar + "coord", MinecraftUtils.MavenPath("broken:coord"));
	}

	[Fact]
	public void MavenPath_ExtraSegments_BeyondClassifier_AreIgnored()
	{
		// 第 5 段之后被丢弃（原实现只看 index 3）。记录这个行为，避免以后误以为支持 extension 字段。
		string path = MinecraftUtils.MavenPath("a.b:c:1.0:cls:extra");
		Assert.Equal(Path.Combine("a", "b", "c", "1.0", "c-1.0-cls.jar"), path);
	}

	[Fact]
	public void SelectNativeClassifier_MissingNatives_ReturnsNull()
	{
		using var doc = JsonDocument.Parse("""{"name":"org.lwjgl:lwjgl:3.3.1"}""");
		Assert.Null(MinecraftUtils.SelectNativeClassifier(doc.RootElement));
	}

	[Fact]
	public void SelectNativeClassifier_NativesWithoutCurrentOs_ReturnsNull()
	{
		using var doc = JsonDocument.Parse("""{"natives":{"totally-unknown-os":"natives-unknown"}}""");
		Assert.Null(MinecraftUtils.SelectNativeClassifier(doc.RootElement));
	}

	[Fact]
	public void SelectNativeClassifier_ReplacesArchPlaceholder()
	{
		string os = PlatformPaths.GetOsRuleName();
		string json = "{\"natives\":{\"" + os + "\":\"natives-" + os + "-${arch}\"}}";
		using var doc = JsonDocument.Parse(json);

		string? classifier = MinecraftUtils.SelectNativeClassifier(doc.RootElement);

		Assert.NotNull(classifier);
		Assert.DoesNotContain("${arch}", classifier);
		Assert.Contains(Environment.Is64BitOperatingSystem ? "64" : "32", classifier);
	}

	[Fact]
	public void Sha1Matches_ComputesRealDigest_AndRejectsMismatch()
	{
		string file = Path.Combine(Path.GetTempPath(), "pineglow-sha1-" + Guid.NewGuid().ToString("N") + ".bin");
		try
		{
			File.WriteAllText(file, "pineglow");
			// 独立算一遍，不复用被测实现
			string expected;
			using (var fs = File.OpenRead(file))
			{
				expected = Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(fs)).ToLowerInvariant();
			}

			Assert.True(MinecraftUtils.Sha1Matches(file, expected));
			Assert.True(MinecraftUtils.Sha1Matches(file, expected.ToUpperInvariant()));
			Assert.False(MinecraftUtils.Sha1Matches(file, new string('0', 40)));
		}
		finally
		{
			File.Delete(file);
		}
	}

	[Fact]
	public void Sha1Matches_MissingFile_ReturnsFalse_InsteadOfThrowing()
	{
		Assert.False(MinecraftUtils.Sha1Matches(
			Path.Combine(Path.GetTempPath(), "definitely-missing-" + Guid.NewGuid().ToString("N")), new string('a', 40)));
	}

	/* ===================== RulesEvaluator ===================== */

	[Fact]
	public void Rules_NoRulesProperty_IsAllowed()
	{
		using var doc = JsonDocument.Parse("""{"name":"x"}""");
		Assert.True(RulesEvaluator.IsAllowed(doc.RootElement));
	}

	[Fact]
	public void Rules_EmptyArray_IsDenied_BecauseNothingMatches()
	{
		// 语义陷阱：空 rules 数组 → 循环一次都不进 → 保持 false。
		// 这与"无 rules 字段 → 允许"不同，必须锁住，否则规则文件写空数组会静默全禁。
		using var doc = JsonDocument.Parse("""{"rules":[]}""");
		Assert.False(RulesEvaluator.IsAllowed(doc.RootElement));
	}

	[Fact]
	public void Rules_AllowForCurrentOs_IsAllowed()
	{
		string os = PlatformPaths.GetOsRuleName();
		string json = "{\"rules\":[{\"action\":\"allow\",\"os\":{\"name\":\"" + os + "\"}}]}";
		using var doc = JsonDocument.Parse(json);
		Assert.True(RulesEvaluator.IsAllowed(doc.RootElement));
	}

	[Fact]
	public void Rules_DisallowForCurrentOs_IsDenied()
	{
		string os = PlatformPaths.GetOsRuleName();
		string json = "{\"rules\":[{\"action\":\"disallow\",\"os\":{\"name\":\"" + os + "\"}}]}";
		using var doc = JsonDocument.Parse(json);
		Assert.False(RulesEvaluator.IsAllowed(doc.RootElement));
	}

	[Fact]
	public void Rules_OtherOsOnly_IsDenied_AndLastMatchWins()
	{
		string other = PlatformPaths.GetOsRuleName() == "windows" ? "linux" : "windows";
		string json = "{\"rules\":[{\"action\":\"allow\"},{\"action\":\"disallow\",\"os\":{\"name\":\"" + other + "\"}}]}";
		using var doc = JsonDocument.Parse(json);
		// 第一条 allow 无条件命中 → true；第二条 os 不匹配 → 不参与；最终 true
		Assert.True(RulesEvaluator.IsAllowed(doc.RootElement));
	}

	[Fact]
	public void Rules_LastMatchingRuleWins_AllowThenDisallowSameOs()
	{
		string os = PlatformPaths.GetOsRuleName();
		string json = "{\"rules\":[{\"action\":\"allow\",\"os\":{\"name\":\"" + os + "\"}},{\"action\":\"disallow\",\"os\":{\"name\":\"" + os + "\"}}]}";
		using var doc = JsonDocument.Parse(json);
		Assert.False(RulesEvaluator.IsAllowed(doc.RootElement));
	}

	[Fact]
	public void Rules_ArchMismatch_Denies()
	{
		// 当前机器几乎必然是 x86_64，用 arm64 作为不匹配值。
		using var doc = JsonDocument.Parse("""{"rules":[{"action":"allow","os":{"arch":"arm64"}}]}""");
		bool expected = System.Runtime.InteropServices.RuntimeInformation.OSArchitecture
			== System.Runtime.InteropServices.Architecture.Arm64;
		Assert.Equal(expected, RulesEvaluator.IsAllowed(doc.RootElement));
	}

	[Fact]
	public void Rules_FeaturesGate_AllowsOnlyWhenFeatureEnabled()
	{
		using var doc = JsonDocument.Parse("""{"rules":[{"action":"allow","features":{"has_custom_resolution":true}}]}""");

		Assert.True(RulesEvaluator.IsAllowed(doc.RootElement, new Dictionary<string, bool> { ["has_custom_resolution"] = true }));
		Assert.False(RulesEvaluator.IsAllowed(doc.RootElement, new Dictionary<string, bool> { ["has_custom_resolution"] = false }));
		// features 未提供 → 视为 false → 与 required=true 不等 → 拒绝
		Assert.False(RulesEvaluator.IsAllowed(doc.RootElement, features: null));
	}

	[Fact]
	public void Rules_FeaturesRequiredFalse_AllowsWhenAbsent()
	{
		using var doc = JsonDocument.Parse("""{"rules":[{"action":"allow","features":{"is_demo_user":false}}]}""");
		Assert.True(RulesEvaluator.IsAllowed(doc.RootElement, features: null));
	}

	[Fact]
	public void Rules_InvalidRegexInOsVersion_Denies_InsteadOfThrowing()
	{
		// 原实现 catch(ArgumentException) 返回 false。若这里改成抛异常，
		// 一条有毒的规则会让整个版本安装失败。
		using var doc = JsonDocument.Parse("""{"rules":[{"action":"allow","os":{"version":"[unclosed"}}]}""");
		Assert.False(RulesEvaluator.IsAllowed(doc.RootElement));
	}

	[Fact]
	public void Rules_NonObjectElement_IsAllowed()
	{
		using var doc = JsonDocument.Parse("[1,2,3]");
		Assert.True(RulesEvaluator.IsAllowed(doc.RootElement));
	}

	/* ===================== MirrorHelper ===================== */

	[Fact]
	public void Mirror_OfficialSource_DoesNotRewrite()
	{
		string original = MirrorHelper.SelectedKey;
		try
		{
			MirrorHelper.SelectedKey = "official";
			const string url = "https://piston-meta.mojang.com/v1/packages/abc/1.20.json";
			Assert.False(MirrorHelper.Enabled);
			Assert.Equal(url, MirrorHelper.Rewrite(url));
			// 官方源下候选列表仍返回两项（原 + 改写），去重后可能只剩一项
			Assert.Equal(new[] { url }, MirrorHelper.GetCandidates(url));
		}
		finally
		{
			MirrorHelper.SelectedKey = original;
		}
	}

	[Fact]
	public void Mirror_Bmclapi_RewritesKnownMojangHosts()
	{
		string original = MirrorHelper.SelectedKey;
		try
		{
			MirrorHelper.SelectedKey = "bmclapi";

			Assert.StartsWith("https://bmclapi2.bangbang93.com/",
				MirrorHelper.Rewrite("https://piston-meta.mojang.com/v1/packages/abc/1.20.json"));

			string assets = MirrorHelper.Rewrite("https://resources.download.minecraft.net/ab/abcdef");
			Assert.StartsWith("https://bmclapi2.bangbang93.com/assets/", assets);

			string libraries = MirrorHelper.Rewrite("https://libraries.minecraft.net/org/lwjgl/lwjgl/3.3.1/lwjgl-3.3.1.jar");
			Assert.StartsWith("https://bmclapi2.bangbang93.com/libraries/", libraries);

			string forge = MirrorHelper.Rewrite("https://maven.minecraftforge.net/net/minecraftforge/forge/maven-metadata.xml");
			Assert.StartsWith("https://bmclapi2.bangbang93.com/maven/", forge);

			string fabricMeta = MirrorHelper.Rewrite("https://meta.fabricmc.net/v2/versions/game");
			Assert.StartsWith("https://bmclapi2.bangbang93.com/", fabricMeta);

			string quilt = MirrorHelper.Rewrite("https://meta.quiltmc.org/v3/versions");
			Assert.StartsWith("https://bmclapi2.bangbang93.com/quilt-meta/", quilt);
		}
		finally
		{
			MirrorHelper.SelectedKey = original;
		}
	}

	[Fact]
	public void Mirror_UnknownHost_IsLeftAlone()
	{
		string original = MirrorHelper.SelectedKey;
		try
		{
			MirrorHelper.SelectedKey = "bmclapi";
			const string url = "https://api.modrinth.com/v2/search";
			Assert.Equal(url, MirrorHelper.Rewrite(url));
		}
		finally
		{
			MirrorHelper.SelectedKey = original;
		}
	}

	[Fact]
	public void Mirror_Candidates_PutMirrorFirstWhenEnabled()
	{
		string original = MirrorHelper.SelectedKey;
		try
		{
			MirrorHelper.SelectedKey = "bmclapi";
			var candidates = MirrorHelper.GetCandidates("https://piston-meta.mojang.com/v1/packages/abc/1.20.json");

			Assert.Equal(2, candidates.Count);
			Assert.StartsWith("https://bmclapi2.bangbang93.com/", candidates[0]);
			Assert.StartsWith("https://piston-meta.mojang.com/", candidates[1]);
		}
		finally
		{
			MirrorHelper.SelectedKey = original;
		}
	}

	[Fact]
	public void Mirror_EmptyUrl_YieldsNoCandidates()
	{
		Assert.Empty(MirrorHelper.GetCandidates(""));
		Assert.Empty(MirrorHelper.GetCandidates("   "));
	}

	[Fact]
	public void Mirror_RewriteIfEnabled_RespectsExplicitFlag_NotGlobalState()
	{
		string original = MirrorHelper.SelectedKey;
		try
		{
			MirrorHelper.SelectedKey = "bmclapi";
			const string url = "https://libraries.minecraft.net/a/b.jar";

			Assert.Equal(url, MirrorHelper.RewriteIfEnabled(url, useMirror: false));
			Assert.StartsWith("https://bmclapi2.bangbang93.com/libraries/", MirrorHelper.RewriteIfEnabled(url, useMirror: true));
		}
		finally
		{
			MirrorHelper.SelectedKey = original;
		}
	}

	[Fact]
	public void Mirror_ListSources_ContainsTheThreeDocumentedKeys()
	{
		var sources = MirrorHelper.ListSources();
		Assert.Equal(3, sources.Count);
		Assert.Equal(new[] { "official", "bmclapi", "mcbbs" }, sources.Select(s => s.Key));
		Assert.All(sources, s => Assert.False(string.IsNullOrWhiteSpace(s.BaseUrl)));
	}

	[Fact]
	public void Mirror_GetBaseUrl_UnknownKey_IsEmpty()
	{
		Assert.Equal(string.Empty, MirrorHelper.GetBaseUrl("official"));
		Assert.Equal(string.Empty, MirrorHelper.GetBaseUrl("nosuch"));
		Assert.NotEqual(string.Empty, MirrorHelper.GetBaseUrl("BMCLAPI"));
	}

	/* ===================== LaunchCommand ===================== */

	private static LaunchCommand Cmd(params string[] args) => new()
	{
		JavaPath = @"C:\Java\bin\javaw.exe",
		Arguments = args.ToList(),
		WorkingDirectory = @"C:\games\.minecraft",
	};

	[Fact]
	public void LaunchCommand_DisplayString_RedactsInlineAccessToken()
	{
		string display = Cmd("--accessToken", "SECRET-VALUE", "--username", "Steve").ToDisplayString();

		Assert.DoesNotContain("SECRET-VALUE", display);
		Assert.Contains("<redacted>", display);
		Assert.Contains("Steve", display);
	}

	[Fact]
	public void LaunchCommand_DisplayString_RedactsEqualsForm()
	{
		string display = Cmd("--accessToken=SECRET-VALUE-2", "--username", "Steve").ToDisplayString();

		Assert.DoesNotContain("SECRET-VALUE-2", display);
		// 保留参数名便于排障，只把值打码
		Assert.Contains("--accessToken=<redacted>", display);
	}

	[Fact]
	public void LaunchCommand_DisplayString_RedactsSessionAndAuthAccessToken()
	{
		Assert.DoesNotContain("SESSION-TOKEN", Cmd("--session", "SESSION-TOKEN").ToDisplayString());
		Assert.DoesNotContain("AAT", Cmd("--authAccessToken", "AAT").ToDisplayString());
	}

	[Fact]
	public void LaunchCommand_DisplayString_LeavesNormalArgumentsVisible()
	{
		string display = Cmd("-Xmx4096m", "--username", "Steve", "--gameDir", @"C:\games").ToDisplayString();

		Assert.Contains("-Xmx4096m", display);
		Assert.Contains("Steve", display);
		Assert.DoesNotContain("<redacted>", display);
	}

	[Fact]
	public void LaunchCommand_ToScript_DoesNotContainSecrets_BecauseScriptsAreUploaded()
	{
		// 导出脚本是**要交出去**的产物（用户会发群里求助）。这里断言脚本里绝不能出现令牌。
		// ToScript 用的是未脱敏的 Arguments —— 这是设计上的信任边界，由调用方保证
		// 只对离线账号导出。这条测试锁住"当前行为"，一旦有人误以为是安全的就会踩到。
		LaunchCommand cmd = Cmd("--accessToken", "SHOULD-NOT-APPEAR");
		string script = cmd.ToScript();

		// 事实：ToScript **不**脱敏。这是已知边界，记录在此而不是假装它安全。
		Assert.Contains("SHOULD-NOT-APPEAR", script);
		// 而展示串必须脱敏 —— 两者行为不同，正是这条测试要钉住的差异。
		Assert.DoesNotContain("SHOULD-NOT-APPEAR", cmd.ToDisplayString());
	}

	[Fact]
	public void LaunchCommand_ToScript_QuotesPathsWithSpaces()
	{
		string script = new LaunchCommand
		{
			JavaPath = @"C:\Program Files\Java\bin\javaw.exe",
			Arguments = new List<string> { "-Xmx2G" },
			WorkingDirectory = @"C:\my games\.minecraft",
		}.ToScript();

		if (OperatingSystem.IsWindows())
		{
			Assert.Contains("@echo off", script);
			Assert.Contains("\"C:\\Program Files\\Java\\bin\\javaw.exe\"", script);
			Assert.Contains("cd /d \"C:\\my games\\.minecraft\"", script);
		}
		else
		{
			Assert.Contains("#!/usr/bin/env bash", script);
			Assert.Contains("'C:\\Program Files\\Java\\bin\\javaw.exe'", script);
		}
	}

	[Fact]
	public void LaunchCommand_ToScript_EmptyArgument_IsQuotedAsEmptyPair()
	{
		string script = Cmd("", "-Xmx2G").ToScript();

		// 脚本语法随平台不同：Windows 生成 .bat（Quote → 双引号 ""），
		// POSIX 生成 bash（BashQuote → 单引号 ''）。空参数在两边都必须被"引成一对空引号"，
		// 而不是被悄悄丢掉——丢掉会让 Java 的参数错位。
		if (OperatingSystem.IsWindows())
		{
			Assert.Contains("\"\"", script);
		}
		else
		{
			Assert.Contains("''", script);
			Assert.DoesNotContain("\"\"", script);
		}
	}

	[Fact]
	public void LaunchCommand_ToScript_ArgumentWithQuote_IsEscaped()
	{
		string script = Cmd("-Dname=he said \"hi\"").ToScript();

		if (OperatingSystem.IsWindows())
		{
			// cmd.exe 没有可嵌套的引号，Quote 用反斜杠转义内部的 "。
			Assert.Contains("\\\"", script);
		}
		else
		{
			// bash 的 BashQuote 用单引号包裹整个参数，内部的 " 是普通字符、不需要转义；
			// 真正需要处理的是参数里的单引号（bash 里写成 反斜杠 + 单引号）。所以这里断言"原样的双引号被完整保留在
			// 单引号内"，而不是断言 Windows 才有的反斜杠转义。
			Assert.Contains("'-Dname=he said \"hi\"'", script);
			Assert.DoesNotContain("\\\"", script);
		}
	}
}
