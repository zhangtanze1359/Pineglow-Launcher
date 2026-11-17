using System;
using System.Collections.Generic;
using System.Linq;
using Pineglow.Launcher.Models;

namespace Pineglow.Launcher.Services;

/// <summary>一条"可更新"候选：已装文件 + 查到的最新版本。</summary>
public sealed record ModUpdateCandidate(
	string FileName,
	string ProjectId,
	string InstalledVersionId,
	string InstalledVersionNumber,
	ModVersion Latest);

/// <summary>
/// 更新检查的结果。四个 Skipped* 计数是给前端解释"为什么只有 N 个可更新"用的，
/// 不是给日志凑数的：没有在线元数据 / 已是最新 / 最新版没有下载地址，三种情况要能分开说明。
/// </summary>
public sealed record ModUpdatePlan(
	IReadOnlyList<ModUpdateCandidate> Candidates,
	int Checked,
	int SkippedNoMetadata,
	int SkippedUpToDate,
	int SkippedNoDownload,
	int SkippedQueryFailed = 0,
	int SkippedNoCompatible = 0,
	IReadOnlyList<ModUpdateSkip>? Skips = null)
{
	public int SkippedTotal => SkippedNoMetadata + SkippedUpToDate + SkippedNoDownload + SkippedQueryFailed + SkippedNoCompatible;

	/// <summary>逐项跳过原因（前端"为什么只有 N 个可更新"就靠它，不再靠反推计数）。</summary>
	public IReadOnlyList<ModUpdateSkip> SkipList => Skips ?? Array.Empty<ModUpdateSkip>();
}

/// <summary>一条"没进候选"的条目与原因。<see cref="Retryable"/> 标出"重试可能变"的那一类。</summary>
public sealed record ModUpdateSkip(string FileName, ModQueryStatus Status, string? Detail)
{
	public bool Retryable => Status == ModQueryStatus.QueryFailed;
}

/// <summary>
/// D3③ 前端的"哪几个 Mod 可以更新"判定 —— **纯函数**，不做网络请求、不碰文件。
///
/// 输入是"已装列表"与"按同样顺序查回来的最新版本"（查不到的用 null），
/// 输出是可更新候选与三种跳过原因的计数。这样"是否更新"的规则可以毫秒级直测，
/// 而真正贵的网络查询留在 <see cref="ModContentService.GetLatestCompatibleVersionAsync"/>。
/// </summary>
public static class ModUpdatePlanner
{
	/// <summary>
	/// 旧口径入口（保留给既有调用方与测试）：只给"查到的版本"，null 一律按**无元数据**处理。
	/// 需要区分"查失败 / 无兼容版本"的调用方请用 <see cref="Plan(IReadOnlyList{ModFile}, IReadOnlyList{ModQueryOutcome})"/>。
	/// </summary>
	public static ModUpdatePlan Plan(IReadOnlyList<ModFile>? mods, IReadOnlyList<ModVersion?>? latest)
	{
		if (mods == null || mods.Count == 0)
		{
			return new ModUpdatePlan(Array.Empty<ModUpdateCandidate>(), 0, 0, 0, 0);
		}

		List<ModQueryOutcome> outcomes = new List<ModQueryOutcome>(mods.Count);
		for (int i = 0; i < mods.Count; i++)
		{
			ModVersion? version = latest != null && i < latest.Count ? latest[i] : null;
			outcomes.Add(version == null ? ModQueryOutcome.NoMetadata() : ModQueryOutcome.Found(version));
		}

		return Plan(mods, outcomes);
	}

	/// <summary>
	/// 真实口径：输入是查询层给出的**逐项结果**，四种状态各自成计数与明细。
	/// 这样"跳过 37 个：无元数据"不再是两个模块合计出来的半个真相。
	/// </summary>
	public static ModUpdatePlan Plan(IReadOnlyList<ModFile>? mods, IReadOnlyList<ModQueryOutcome>? outcomes)
	{
		if (mods == null || mods.Count == 0)
		{
			return new ModUpdatePlan(Array.Empty<ModUpdateCandidate>(), 0, 0, 0, 0);
		}

		List<ModUpdateCandidate> candidates = new List<ModUpdateCandidate>();
		List<ModUpdateSkip> skips = new List<ModUpdateSkip>();
		int skippedNoMetadata = 0;
		int skippedUpToDate = 0;
		int skippedNoDownload = 0;
		int skippedQueryFailed = 0;
		int skippedNoCompatible = 0;

		for (int i = 0; i < mods.Count; i++)
		{
			ModFile mod = mods[i];
			ModQueryOutcome outcome = outcomes != null && i < outcomes.Count
				? outcomes[i]
				: ModQueryOutcome.NoMetadata();
			ModVersion? version = outcome.Version;

			if (outcome.Status == ModQueryStatus.NoMetadata)
			{
				skippedNoMetadata++;
				skips.Add(new ModUpdateSkip(mod.FileName, outcome.Status, outcome.Detail ?? "缺少在线元数据（无 projectId 或来源）"));
				continue;
			}

			if (outcome.Status == ModQueryStatus.QueryFailed)
			{
				skippedQueryFailed++;
				skips.Add(new ModUpdateSkip(mod.FileName, outcome.Status, outcome.Detail));
				continue;
			}

			if (outcome.Status == ModQueryStatus.NoCompatibleVersion || version == null)
			{
				skippedNoCompatible++;
				skips.Add(new ModUpdateSkip(mod.FileName, ModQueryStatus.NoCompatibleVersion, outcome.Detail ?? "上游没有兼容版本"));
				continue;
			}

			if (!mod.HasOnlineMetadata)
			{
				skippedNoMetadata++;
				skips.Add(new ModUpdateSkip(mod.FileName, ModQueryStatus.NoMetadata, "缺少在线元数据（无 projectId 或来源）"));
				continue;
			}

			// 同一个 VersionId 就是"已经是最新"——不要拿"版本号字符串不同"当更新，
			// 那是发布方自己改了个显示名，重下一次没有任何收益。
			if (!string.IsNullOrWhiteSpace(mod.VersionId)
				&& string.Equals(mod.VersionId, version.VersionId, StringComparison.OrdinalIgnoreCase))
			{
				skippedUpToDate++;
				skips.Add(new ModUpdateSkip(mod.FileName, ModQueryStatus.Found, "已是最新"));
				continue;
			}

			if (string.IsNullOrWhiteSpace(version.DownloadUrl))
			{
				skippedNoDownload++;
				skips.Add(new ModUpdateSkip(mod.FileName, ModQueryStatus.Found, "最新版没有下载地址"));
				continue;
			}

			candidates.Add(new ModUpdateCandidate(
				mod.FileName,
				mod.ProjectId,
				mod.VersionId,
				mod.OnlineVersionNumber,
				version));
		}

		return new ModUpdatePlan(
			candidates, mods.Count, skippedNoMetadata, skippedUpToDate, skippedNoDownload,
			skippedQueryFailed, skippedNoCompatible, skips);
	}
}
