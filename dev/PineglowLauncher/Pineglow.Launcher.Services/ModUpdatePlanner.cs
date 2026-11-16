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
	int SkippedNoDownload)
{
	public int SkippedTotal => SkippedNoMetadata + SkippedUpToDate + SkippedNoDownload;
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
	public static ModUpdatePlan Plan(IReadOnlyList<ModFile>? mods, IReadOnlyList<ModVersion?>? latest)
	{
		if (mods == null || mods.Count == 0)
		{
			return new ModUpdatePlan(Array.Empty<ModUpdateCandidate>(), 0, 0, 0, 0);
		}

		List<ModUpdateCandidate> candidates = new List<ModUpdateCandidate>();
		int skippedNoMetadata = 0;
		int skippedUpToDate = 0;
		int skippedNoDownload = 0;

		for (int i = 0; i < mods.Count; i++)
		{
			ModFile mod = mods[i];
			ModVersion? version = latest != null && i < latest.Count ? latest[i] : null;

			if (!mod.HasOnlineMetadata || version == null)
			{
				skippedNoMetadata++;
				continue;
			}

			// 同一个 VersionId 就是"已经是最新"——不要拿"版本号字符串不同"当更新，
			// 那是发布方自己改了个显示名，重下一次没有任何收益。
			if (!string.IsNullOrWhiteSpace(mod.VersionId)
				&& string.Equals(mod.VersionId, version.VersionId, StringComparison.OrdinalIgnoreCase))
			{
				skippedUpToDate++;
				continue;
			}

			if (string.IsNullOrWhiteSpace(version.DownloadUrl))
			{
				skippedNoDownload++;
				continue;
			}

			candidates.Add(new ModUpdateCandidate(
				mod.FileName,
				mod.ProjectId,
				mod.VersionId,
				mod.OnlineVersionNumber,
				version));
		}

		return new ModUpdatePlan(candidates, mods.Count, skippedNoMetadata, skippedUpToDate, skippedNoDownload);
	}
}
