using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;
using Xunit.Abstractions;

namespace Pineglow.Launcher.Tests;

/// <summary>
/// MR12 补测（缺口登记表 A8）：**出站出口审计** + 把"不得绕过策略"钉成守卫。
///
/// MR12 报告 §六 原话："图标/更新路径的限流 | `ModContentService` 的三处 CF 请求已全部接线；
/// 但**图标获取**（`ContentIconService`）与其它可能的 CF 出口**本包未逐一审计**。"
///
/// 本类做两件事：
///   1. **审计**（结论见本包 deliverable 的出口表）：CF API 的每个请求点都在策略内；
///      图标服务**不碰** CF/Modrinth API（它只取服务端构建的可信 URL 映射）；
///      新闻/AI 出口各有自己的策略；**Modrinth 完全没有策略**（已登记为 A18）。
///   2. **守卫**：把上面这些不变量做成静态检查，防止以后有人新开一个"绕过策略的出口"。
///
/// **口径**：本类是**静态源码检查**，不是运行时验证；它防的是"新加出口时忘了接策略"这类回退。
/// </summary>
public class OutboundEndpointPolicyTests
{
	private readonly ITestOutputHelper _out;

	public OutboundEndpointPolicyTests(ITestOutputHelper output) => _out = output;

	private static string ServicesDir
	{
		get
		{
			string dir = AppContext.BaseDirectory;
			for (int i = 0; i < 8 && dir != null; i++)
			{
				string candidate = Path.Combine(dir, "dev", "PineglowLauncher", "Pineglow.Launcher.Services");
				if (Directory.Exists(candidate))
				{
					return candidate;
				}

				dir = Path.GetDirectoryName(dir);
			}

			throw new DirectoryNotFoundException("找不到 Services 目录");
		}
	}

	private static string ReadService(string fileName) => File.ReadAllText(Path.Combine(ServicesDir, fileName));

	/// <summary>去掉注释行，避免"说明文字"被当成代码（第 37 轮踩过这个坑）。</summary>
	private static string[] CodeLines(string text) =>
		text.Split('\n').Select(l => l.TrimEnd('\r')).ToArray();

	/* ===================== 1. CF API：每个请求点都必须在策略内 ===================== */

	[Fact]
	public void EveryCurseForgeApiRequest_IsWrappedByTheRequestPolicy()
	{
		string text = ReadService("ModContentService.cs");
		string[] lines = CodeLines(text);

		int sites = 0;
		var offenders = new List<string>();
		for (int i = 0; i < lines.Length; i++)
		{
			if (!lines[i].Contains("api.curseforge.com/v1", StringComparison.Ordinal))
			{
				continue;
			}

			// 常量声明不是请求点
			if (lines[i].Contains("const string CurseForgeApi", StringComparison.Ordinal))
			{
				continue;
			}

			sites++;

			// 允许两种写法：同一行内出现策略调用，或**紧邻上一行**是策略调用（本仓库的实际写法）
			bool wrapped = lines[i].Contains("CurseForgeRequestPolicy.SendAsync(", StringComparison.Ordinal)
				|| (i > 0 && lines[i - 1].Contains("CurseForgeRequestPolicy.SendAsync(", StringComparison.Ordinal));
			if (!wrapped)
			{
				offenders.Add($"第 {i + 1} 行：{lines[i].Trim()}");
			}
		}

		Assert.True(sites >= 3, $"CF API 请求点应至少 3 处（搜索/文件/下载地址），实际 {sites}");
		Assert.True(offenders.Count == 0,
			"发现**绕过 CurseForgeRequestPolicy** 的 CF API 请求点（限流/退避/Retry-After 都不会生效）：\n" + string.Join("\n", offenders));
		_out.WriteLine($"[A8] CF API 请求点 {sites} 处，全部在 CurseForgeRequestPolicy.SendAsync 内");
	}

	[Fact]
	public void CurseForgePolicy_StillEnforcesConcurrencyAndRetryAfter()
	{
		string text = ReadService("CurseForgeRequestPolicy.cs");
		// 这三条是 MR12 的核心能力，缺一个就等于策略名存实亡
		Assert.True(text.Contains("MaxConcurrentRequests", StringComparison.Ordinal), "策略必须仍有并发上限");
		Assert.True(text.Contains("Retry-After", StringComparison.Ordinal) || text.Contains("RetryAfter", StringComparison.Ordinal),
			"策略必须仍然处理 Retry-After");
		Assert.True(text.Contains("SemaphoreSlim", StringComparison.Ordinal), "并发上限必须真的用信号量实现");
		_out.WriteLine("[A8] CF 策略仍具备并发上限 + Retry-After 处理");
	}

	/* ===================== 2. 图标出口：不得直接碰 CF/Modrinth API ===================== */

	[Fact]
	public void ContentIconService_MustNotCallTheModApis()
	{
		string text = ReadService("ContentIconService.cs");
		Assert.False(text.Contains("api.curseforge.com", StringComparison.Ordinal),
			"图标服务不得直接请求 CF API（它只应取服务端构建的**可信 URL 映射**里的媒体地址）——" +
			"这正是 MR12 说'未逐一审计'的那一条，现在被钉住");
		Assert.False(text.Contains("api.modrinth.com", StringComparison.Ordinal),
			"图标服务不得直接请求 Modrinth API");

		// 它必须仍然具备超时约束（没有 API 限流不等于可以无限等）
		Assert.True(text.Contains("ConnectTimeoutSeconds", StringComparison.Ordinal)
			|| text.Contains("TotalTimeoutSeconds", StringComparison.Ordinal),
			"图标服务必须仍有连接/总超时约束");
		_out.WriteLine("[A8] 图标服务不碰 CF/Modrinth API，且保留连接/总超时");
	}

	/* ===================== 3. 新闻出口：三类入口都必须过策略 ===================== */

	[Fact]
	public void NewsEndpoints_AreGatedByNewsEndpointPolicy()
	{
		string policy = ReadService("NewsEndpointPolicy.cs");
		foreach (string required in new[] { "IsAllowedFeedEndpoint", "IsAllowedImageUrl", "IsAllowedLink" })
		{
			Assert.True(policy.Contains(required, StringComparison.Ordinal),
				$"NewsEndpointPolicy 必须提供 {required}（feed / 图片 / 链接三类入口各自把关）");
		}

		Assert.True(ReadService("MinecraftNewsService.cs").Contains("IsAllowedFeedEndpoint", StringComparison.Ordinal),
			"新闻 feed 取回前必须校验端点");
		Assert.True(ReadService("NewsImageProxyService.cs").Contains("IsAllowedImageUrl", StringComparison.Ordinal),
			"新闻图片代理必须校验图片 URL");
		Assert.True(ReadService("NewsLinkService.cs").Contains("IsAllowedLink", StringComparison.Ordinal),
			"新闻链接必须校验白名单");
		_out.WriteLine("[A8] 新闻 feed / 图片 / 链接三类入口都过 NewsEndpointPolicy");
	}

	/* ===================== 4. AI 出口：必须过 AiEndpointPolicy ===================== */

	[Fact]
	public void AiEndpoints_AreGatedByAiEndpointPolicy()
	{
		string policy = ReadService("AiEndpointPolicy.cs");
		Assert.True(policy.Contains("api.deepseek.com", StringComparison.Ordinal)
			|| policy.Contains("deepseek", StringComparison.OrdinalIgnoreCase),
			"AiEndpointPolicy 应声明允许的 AI 端点");
		Assert.True(policy.Contains("api.minimaxi.com", StringComparison.Ordinal)
			|| policy.Contains("minimax", StringComparison.OrdinalIgnoreCase),
			"AiEndpointPolicy 应覆盖已支持的第二个供应商");
		_out.WriteLine("[A8] AI 端点由 AiEndpointPolicy 白名单把关");
	}

	/* ===================== 5. Modrinth：**数量触发器**（当前无策略，已登记 A18） ===================== */

	[Fact]
	public void ModrinthApiCallSites_CountIsPinned_SoNewEgressIsNoticed()
	{
		string[] files = Directory.GetFiles(ServicesDir, "*.cs");
		var sites = new List<string>();
		foreach (string file in files)
		{
			string[] lines = CodeLines(File.ReadAllText(file));
			for (int i = 0; i < lines.Length; i++)
			{
				// 排除**任何**常量声明行：三个文件分别叫 ModrinthApi / Api / Api，
				// 只按一个名字排除会漏掉两个（本包第一次就是这样把 7 个出口数成了 9 个）。
				if (lines[i].Contains("api.modrinth.com", StringComparison.Ordinal)
					&& !lines[i].Contains("const string", StringComparison.Ordinal))
				{
					sites.Add(Path.GetFileName(file) + ":" + (i + 1));
				}
			}
		}

		_out.WriteLine("[A8] Modrinth API 出口：" + string.Join(", ", sites));

		// **故意做成精确数量**：新增 Modrinth 出口时本用例会失败，迫使作者回答
		// "这个新出口要不要限流/并发策略？"（当前答案：没有策略，见 A18）
		Assert.True(sites.Count == 7,
			$"Modrinth API 出口数量已变化（当前 {sites.Count}，记录值 7 —— 3 个常量声明不计）。\n" +
			"这不是'数量必须永远不变'，而是一个**触发器**：新增出口时请一并评估是否需要限流/并发策略——\n" +
			"现状是 **Modrinth 侧完全没有策略**（CurseForge 有），已登记为缺口 A18。\n" +
			"确认后请更新本用例里的记录值。\n实际出口：" + string.Join(", ", sites));
	}
}
