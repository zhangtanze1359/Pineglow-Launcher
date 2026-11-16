using System;

namespace Pineglow.Launcher.Services;

/// <summary>
/// ⚠️ 临时豁免（有明确截止版本）：本版本继续内置 CurseForge API Key。
///
/// 背景（2026-11-16 编排方决定）：现在还没有真实用户，吊销/轮换这把 Key 的收益为零，
/// 因此**本版本不轮换**，下个版本（<see cref="RemovalDeadline"/>）再做三件事：
///   1. 去 CurseForge 后台吊销这把 Key 并换新；
///   2. 删除本文件（届时 <c>SecretHygieneTests</c> 会因为找不到豁免文件而强制提醒）；
///   3. 全量改为"用户自带 Key / 环境变量"（<c>ModContentService</c> 的解析逻辑已经支持，只是这里还留了回退）。
///
/// 为什么仍然集中放在一个文件里：S9 的验收要求是"第三方 Key 不进分发程序"，
/// 本轮是**明确的、有截止日期的例外**，不是遗忘——所以只允许这一个文件出现字面量，
/// 其他任何文件出现都会让守卫测试变红。
/// </summary>
internal static class CurseForgeKeyDefaults
{
	/// <summary>本版本暂用的内置 Key。**不要在别处引用或打印它。**</summary>
	internal const string Embedded = "$2a$10$G3lIsqa0DNZIE2lKFnLy4O7EhQdaEnPynwI8rDWFI38zPNWWjOCrG";

	/// <summary>必须移除本文件的发布线（用于守卫测试与发布检查表）。</summary>
	internal const string RemovalDeadline = "v1.0.5";
}
