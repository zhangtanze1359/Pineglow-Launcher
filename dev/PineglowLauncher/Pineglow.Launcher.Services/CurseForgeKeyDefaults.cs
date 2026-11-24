using System;
using System.Linq;
using System.Reflection;

namespace Pineglow.Launcher.Services;

/// <summary>
/// CurseForge API Key 的**运行期**默认值来源。
///
/// ⚠️ 本文件不包含任何 Key 字面量（OPEN-08，2026-11-24）。
///
/// 历史（别把它当旧账删掉）：
///   2026-11-16 曾以「有截止日期的临时豁免」形式把 Key 硬编码在这里，
///   标注的移除期限是 v1.0.5，但直到 v1.0.6 仍未移除，且发布说明错误地声称"已移除"。
///   2026-11-24 编排方决定：**移除内嵌 Key，但不轮换，且不能让 CurseForge 下载失效**。
///
/// 现行机制（社区通例，2026-11-24 核对 Prism / basalt-launcher / OneLauncher 源码确认）：
///   三家都把 Key 编译进二进制让用户零配置，其中 basalt-launcher 最干净——
///   Key 只存在于构建环境，源码树里没有字面量。本项目采用同一模式。
///
///   构建期：<c>Pineglow.Launcher.csproj</c> 读环境变量 <c>PINEGLOW_CURSEFORGE_BUILD_KEY</c>，
///           通过 <c>AssemblyMetadata</c> 把它写进程序集（不生成 .cs 文件、不进源码树）。
///   运行期：解析顺序仍是 调用方传入 → 环境变量 → 构建期注入值。
///
/// 为什么用 AssemblyMetadata 而不是生成常量：
///   MSBuild 的 <c>WriteLinesToFile</c> 在本工具链上会把分号当列表分隔符吞掉，
///   生成 C# 不可靠；<c>AssemblyMetadata</c> 由 MSBuild 原生产生，字符串无需手工转义。
/// </summary>
internal static class CurseForgeKeyDefaults
{
	/// <summary>构建期写入程序集的元数据键名。改名会静默丢 Key，故由测试钉住。</summary>
	internal const string MetadataKey = "PineglowCurseForgeApiKey";

	/// <summary>
	/// 构建期注入的 Key；**空串表示构建机未提供**（例如本地开发构建）。
	/// 空串不是故障——运行期会走"缺 Key 引导"路径。
	/// </summary>
	internal static readonly string Embedded = ReadFromAssemblyMetadata();

	private static string ReadFromAssemblyMetadata()
	{
		try
		{
			var metas = typeof(CurseForgeKeyDefaults).Assembly
				.GetCustomAttributes<AssemblyMetadataAttribute>();

			string? value = metas
				.FirstOrDefault(m => string.Equals(m.Key, MetadataKey, StringComparison.Ordinal))?.Value;

			return value?.Trim() ?? string.Empty;
		}
		catch
		{
			// 读元数据失败不应让产品不可用：降级为"没有内置 Key"，走缺 Key 引导。
			return string.Empty;
		}
	}

	/// <summary>
	/// 本次构建是否真的注入了 Key。供测试与诊断区分「产品故意不带」与「构建漏配」。
	/// </summary>
	internal static bool ProvidedAtBuildTime => Embedded.Length > 0;
}
