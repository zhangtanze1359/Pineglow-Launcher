using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Pineglow.Launcher.Models;

namespace Pineglow.Launcher.Services;

/// <summary>批量更新的一条输入：文件名 + 新字节 + 预期 SHA256 + 可选版本元数据（形状与单条 replace 一致）。</summary>
public sealed record BatchUpdateInput(string FileName, byte[]? Bytes, string? ExpectedSha256, ModVersion? Version, string? DownloadUrl = null, string? ExpectedSha1 = null);

/// <summary>已成功应用的一条：文件名 + 恢复区操作号 + 写入后文件的 SHA256（撤销时用于校验"还是我写的那个版本"）。</summary>
public sealed record BatchUpdateApplied(string FileName, string OperationId, string? NewSha256);

public sealed record BatchUndoResult(string FileName, bool Ok, string Reason);

/// <summary>
/// 一次批量更新的任务状态。**只在内存里**：进程退出即消失（批量更新不跨进程恢复，这是刻意的最小实现）。
/// </summary>
public sealed class BatchUpdateJob
{
	public string JobId { get; } = Guid.NewGuid().ToString("N");
	public string InstanceId { get; init; } = string.Empty;
	public DateTimeOffset CreatedAt { get; } = DateTimeOffset.Now;
	public List<string> FileNames { get; } = new();
	public List<BatchItemStatus> Statuses { get; } = new();
	public List<BatchUpdateApplied> Applied { get; } = new();
	public string? FailedFile { get; set; }
	public string? FailedReason { get; set; }
	public bool CancelRequested { get; set; }
	public bool Finished { get; set; }

	public string State => Finished
		? BatchUpdatePlanner.Summarize(Statuses, CancelRequested)
		: (CancelRequested ? "cancelling" : "running");
}

/// <summary>任务登记表。按 jobId 查，过期自动清（默认 1 小时）。</summary>
public static class BatchUpdateRegistry
{
	private static readonly ConcurrentDictionary<string, BatchUpdateJob> Jobs = new(StringComparer.Ordinal);

	public static void Add(BatchUpdateJob job) => Jobs[job.JobId] = job;

	public static BatchUpdateJob? Get(string jobId) =>
		jobId != null && Jobs.TryGetValue(jobId, out BatchUpdateJob? job) ? job : null;

	/// <summary>仅测试用：避免用例之间互相看见对方的任务。</summary>
	public static void Clear() => Jobs.Clear();
}

/// <summary>
/// 批量更新的执行器：按 <see cref="BatchUpdatePlanner"/> 的决定，逐项调用**已有的**事务化替换
/// （<see cref="ModReplacementService.ReplaceAsync"/>），不新造第二套替换逻辑。
///
/// 三条硬约束：
///   1. 一次只跑一项，项与项之间才看取消/失败状态（不打断项内部，避免半写文件）；
///   2. 失败即停：失败项之后的条目全部 Skipped，已成功的保留；
///   3. 逐项撤销靠恢复区 —— 每条 `Applied` 都带 `OperationId`，撤销时用
///      <see cref="FileRecoveryService.RestoreAsync"/> 回到旧文件。
/// </summary>
public static class BatchUpdateRunner
{
	private const string LogPrefix = "[batch-update]";

	public static async Task RunAsync(
		BatchUpdateJob job,
		IReadOnlyList<BatchUpdateInput> inputs,
		GameInstance instance,
		ModService modService,
		FileRecoveryService recovery,
		Func<string, CancellationToken, Task<byte[]>>? downloadBytes = null,
		Action<string>? log = null)
	{
		try
		{
			ModReplacementService replacer = new ModReplacementService(recovery);
			while (true)
			{
				BatchUpdateStep step = BatchUpdatePlanner.Next(job.Statuses, job.CancelRequested);
				if (!step.HasWork)
				{
					if (job.CancelRequested)
					{
						IReadOnlyList<BatchItemStatus> afterCancel = BatchUpdatePlanner.AfterCancel(job.Statuses);
						CopyInto(job.Statuses, afterCancel);
					}
					job.Finished = true;
					return;
				}

				BatchUpdateInput input = inputs[step.Index];
				string? stagedPath = null;
				try
				{
					// 下载放在**项与项之间**：这样"取消停止后续项"真的能省掉后面的下载
					// （旧实现把全部下载塞在建任务之前，导致最慢的一段完全不可取消）。
					byte[] bytes = await ResolveBytesAsync(input, downloadBytes);
					string sha256 = string.IsNullOrWhiteSpace(input.ExpectedSha256)
						? Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant()
						: input.ExpectedSha256;
					stagedPath = StageFile(input.FileName, bytes);
					ModReplacementResult result = await replacer.ReplaceAsync(
						instance, modService, input.FileName, stagedPath, sha256, input.Version);

					if (!result.Ok)
					{
						throw new InvalidOperationException(result.Error ?? result.Status ?? "replace-failed");
					}

					job.Statuses[step.Index] = BatchItemStatus.Applied;
					job.Applied.Add(new BatchUpdateApplied(input.FileName, result.OperationId ?? string.Empty, result.NewSha256));
					log?.Invoke($"{LogPrefix} 已应用 {input.FileName}（operation={result.OperationId}）");
				}
				catch (Exception ex)
				{
					job.FailedFile = input.FileName;
					job.FailedReason = ex.Message;
					IReadOnlyList<BatchItemStatus> afterFailure = BatchUpdatePlanner.AfterFailure(job.Statuses, step.Index);
					CopyInto(job.Statuses, afterFailure);
					job.Finished = true;
					log?.Invoke($"{LogPrefix} {input.FileName} 失败，停止后续项：{ex.Message}");
					return;
				}
				finally
				{
					DeleteStaged(stagedPath);
				}
			}
		}
		catch (Exception ex)
		{
			job.FailedReason = ex.Message;
			job.Finished = true;
			log?.Invoke($"{LogPrefix} 未预期异常：{ex.Message}");
		}
	}

	/// <summary>
	/// 逐项撤销。**先校验目标文件还是本批次写入的那个版本**：被外部改过就拒绝撤销该项
	/// （否则会把用户手工换的版本一起覆盖掉），并把冲突项报回去。
	/// </summary>
	public static async Task<IReadOnlyList<BatchUndoResult>> UndoAsync(
		BatchUpdateJob job,
		GameInstance instance,
		ModService modService,
		FileRecoveryService recovery,
		string instanceGameDirectory)
	{
		List<BatchUndoResult> results = new List<BatchUndoResult>();
		string modsDir = modService.GetModsDirectory(instance);
		foreach (BatchUpdateApplied applied in job.Applied.ToList())
		{
			try
			{
				string current = Path.Combine(modsDir, applied.FileName);
				if (!File.Exists(current))
				{
					results.Add(new BatchUndoResult(applied.FileName, false, "target-missing"));
					continue;
				}

				if (!string.IsNullOrWhiteSpace(applied.NewSha256))
				{
					string actual = ComputeSha256(current);
					if (!string.Equals(actual, applied.NewSha256, StringComparison.OrdinalIgnoreCase))
					{
						results.Add(new BatchUndoResult(applied.FileName, false, "target-modified"));
						continue;
					}
				}

				FileRecoveryRecord? record = await recovery.LoadAsync(job.InstanceId, applied.OperationId);
				if (record == null)
				{
					results.Add(new BatchUndoResult(applied.FileName, false, "operation-not-found"));
					continue;
				}

				await recovery.RestoreAsync(record, overwrite: true, instanceGameDirectory);
				results.Add(new BatchUndoResult(applied.FileName, true, "restored"));
			}
			catch (Exception ex)
			{
				results.Add(new BatchUndoResult(applied.FileName, false, ex.GetType().Name + ": " + ex.Message));
			}
		}

		if (results.All(r => r.Ok))
		{
			CopyInto(job.Statuses, BatchUpdatePlanner.AfterUndo(job.Statuses));
			job.Applied.Clear();
		}

		return results;
	}

	private static async Task<byte[]> ResolveBytesAsync(BatchUpdateInput input, Func<string, CancellationToken, Task<byte[]>>? downloadBytes)
	{
		if (input.Bytes != null && input.Bytes.Length > 0)
		{
			return input.Bytes;
		}

		if (string.IsNullOrWhiteSpace(input.DownloadUrl))
		{
			throw new InvalidOperationException("该项既没有字节也没有下载地址：" + input.FileName);
		}

		if (downloadBytes == null)
		{
			throw new InvalidOperationException("没有可用的下载器，无法取：" + input.FileName);
		}

		// 用 await 而不是 .GetAwaiter().GetResult()：真机实测后者在批次线程里有卡死风险
		byte[] bytes = await downloadBytes(input.DownloadUrl, CancellationToken.None).ConfigureAwait(false);
		if (!string.IsNullOrWhiteSpace(input.ExpectedSha1))
		{
			string actual = Convert.ToHexString(SHA1.HashData(bytes)).ToLowerInvariant();
			if (!string.Equals(actual, input.ExpectedSha1, StringComparison.OrdinalIgnoreCase))
			{
				throw new InvalidOperationException($"下载内容 sha1 不匹配（{input.FileName}）：期望 {input.ExpectedSha1}，实际 {actual}");
			}
		}
		return bytes;
	}

	private static string StageFile(string fileName, byte[] bytes)
	{
		string dir = Path.Combine(Path.GetTempPath(), "pineglow-mod-batch-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(dir);
		string path = Path.Combine(dir, Path.GetFileName(fileName));
		File.WriteAllBytes(path, bytes);
		return path;
	}

	private static void DeleteStaged(string? stagedPath)
	{
		if (string.IsNullOrWhiteSpace(stagedPath))
		{
			return;
		}

		try
		{
			string? dir = Path.GetDirectoryName(stagedPath);
			if (dir != null && Directory.Exists(dir))
			{
				Directory.Delete(dir, recursive: true);
			}
		}
		catch
		{
		}
	}

	private static void CopyInto(List<BatchItemStatus> target, IReadOnlyList<BatchItemStatus> source)
	{
		for (int i = 0; i < target.Count && i < source.Count; i++)
		{
			target[i] = source[i];
		}
	}

	private static string ComputeSha256(string path)
	{
		using FileStream stream = File.OpenRead(path);
		byte[] hash = SHA256.HashData(stream);
		return Convert.ToHexString(hash).ToLowerInvariant();
	}
}
