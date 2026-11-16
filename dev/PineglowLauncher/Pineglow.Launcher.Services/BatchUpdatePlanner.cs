using System;
using System.Collections.Generic;
using System.Linq;

namespace Pineglow.Launcher.Services;

/// <summary>批量更新里单个条目的状态。</summary>
public enum BatchItemStatus
{
	Pending = 0,
	Applied = 1,
	Failed = 2,
	Skipped = 3,
	Undone = 4,
}

/// <summary>下一步动作：要么执行第 <see cref="Index"/> 项，要么结束（带原因）。</summary>
public sealed record BatchUpdateStep(bool HasWork, int Index, string Reason);

/// <summary>
/// D3③「Mod 批量更新」的**纯状态机**。不碰文件、不碰 HTTP、不睡觉，只根据当前状态数组决定下一步。
///
/// 语义（交接书原文逐条落地）：
///   * 逐项提交 —— 一次只允许一个 Pending 变成 Applied/Failed；
///   * 某项失败后停止未执行项、保留已成功结果 —— <see cref="AfterFailure"/> 把失败项之后的
///     Pending 全部变 Skipped，**已 Applied 的不动**；
///   * 取消停止后续项 —— <see cref="AfterCancel"/> 同理，但已开始的项会先跑完（判定在项与项之间，
///     不在项内部，避免半写文件）。
/// </summary>
public static class BatchUpdatePlanner
{
	public static BatchUpdateStep Next(IReadOnlyList<BatchItemStatus>? statuses, bool cancelRequested)
	{
		if (statuses == null || statuses.Count == 0)
		{
			return new BatchUpdateStep(false, -1, "empty");
		}

		if (cancelRequested)
		{
			return new BatchUpdateStep(false, -1, "cancelled");
		}

		for (int i = 0; i < statuses.Count; i++)
		{
			if (statuses[i] == BatchItemStatus.Pending)
			{
				return new BatchUpdateStep(true, i, "run");
			}
		}

		return new BatchUpdateStep(false, -1, "complete");
	}

	/// <summary>失败项之后所有未开始的条目变 Skipped；已成功的保持 Applied。</summary>
	public static IReadOnlyList<BatchItemStatus> AfterFailure(IReadOnlyList<BatchItemStatus> statuses, int failedIndex)
	{
		BatchItemStatus[] result = statuses.ToArray();
		if (failedIndex >= 0)
		{
			result[failedIndex] = BatchItemStatus.Failed;
		}
		for (int i = failedIndex + 1; i >= 0 && i < result.Length; i++)
		{
			if (result[i] == BatchItemStatus.Pending)
			{
				result[i] = BatchItemStatus.Skipped;
			}
		}
		return result;
	}

	public static IReadOnlyList<BatchItemStatus> AfterCancel(IReadOnlyList<BatchItemStatus> statuses)
	{
		return statuses.Select(s => s == BatchItemStatus.Pending ? BatchItemStatus.Skipped : s).ToArray();
	}

	/// <summary>撤销时只有 Applied 的条目会变成 Undone；不是 Applied 的原样保留。</summary>
	public static IReadOnlyList<BatchItemStatus> AfterUndo(IReadOnlyList<BatchItemStatus> statuses)
	{
		return statuses.Select(s => s == BatchItemStatus.Applied ? BatchItemStatus.Undone : s).ToArray();
	}

	/// <summary>对外汇报的整体状态码（给前端与测试断言用）。</summary>
	public static string Summarize(IReadOnlyList<BatchItemStatus>? statuses, bool cancelled)
	{
		if (statuses == null || statuses.Count == 0)
		{
			return "empty";
		}

		if (cancelled)
		{
			return "cancelled";
		}

		if (statuses.Any(s => s == BatchItemStatus.Failed))
		{
			return "partial";
		}

		if (statuses.Any(s => s == BatchItemStatus.Pending))
		{
			return "running";
		}

		if (statuses.All(s => s == BatchItemStatus.Applied || s == BatchItemStatus.Undone))
		{
			return statuses.All(s => s == BatchItemStatus.Undone) ? "undone" : "ok";
		}

		return "partial";
	}
}
