using System;
using System.Collections.Generic;
using System.Linq;
using Pineglow.Launcher.Models;

namespace Pineglow.Launcher.Services;

/// <summary>
/// D3②「退出后自动备份」的判定结果。<see cref="Reason"/> 是给人看的机器码，便于日志与测试断言。
/// </summary>
public sealed record AutoBackupPlan(bool ShouldRun, IReadOnlyList<string> TargetFolders, string Reason);

/// <summary>
/// D3② 的**纯判定面**。这里只读 <see cref="SaveInfo"/> 已有字段，不碰文件系统、不读配置、不写日志。
///
/// 为什么必须独立成纯函数（交接书纪律 1）：
///   判定规则一旦和 IO 混在一起，就只能靠"起一个真的游戏进程再退出"来测——测不了、也不敢测。
///   抽出纯函数后，四条用例（开关关闭 / 两个非空 + 一个空 / 无存档 / 只有空存档）都能毫秒级直测。
///
/// 空存档口径：**只看 <see cref="SaveInfo.SizeBytes"/>**（由 <see cref="SaveService"/> 扫描时写入）。
/// 不在这里 `new DirectoryInfo(...)` 现算——那会把 IO 又搬回判定层。
/// </summary>
public static class AutoBackupPlanner
{
	public static AutoBackupPlan Plan(bool autoBackupOnExit, IReadOnlyList<SaveInfo>? saves, DateTimeOffset now)
	{
		// now 目前只用于未来可能加的节流/去重；保留在签名里，免得将来改调用方。
		_ = now;

		if (!autoBackupOnExit)
		{
			return new AutoBackupPlan(false, Array.Empty<string>(), "disabled");
		}

		if (saves == null || saves.Count == 0)
		{
			return new AutoBackupPlan(false, Array.Empty<string>(), "no-saves");
		}

		List<string> targets = saves
			.Where(s => s.SizeBytes > 0)
			.Select(s => s.FolderName)
			.Where(folder => !string.IsNullOrWhiteSpace(folder))
			.ToList();

		if (targets.Count == 0)
		{
			return new AutoBackupPlan(false, targets, "no-non-empty-saves");
		}

		return new AutoBackupPlan(true, targets, "planned");
	}
}
