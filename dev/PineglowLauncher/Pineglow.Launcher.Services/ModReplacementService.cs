using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Pineglow.Launcher.Models;

namespace Pineglow.Launcher.Services;

/// <summary>一次替换事务的逐项结果。失败不是异常，而是带原因的状态。</summary>
public sealed class ModReplacementResult
{
	public bool Ok { get; set; }

	/// <summary>replaced | staged_missing | hash_mismatch | target_missing | replace_in_progress | quarantine_failed | commit_failed | metadata_failed</summary>
	public string Status { get; set; } = string.Empty;

	public string? Error { get; set; }

	/// <summary>旧文件的隔离记录 ID；撤销本次更新时用它。</summary>
	public string? OperationId { get; set; }

	/// <summary>
	/// R2（验收阻断项 F-03）：回滚**是否没能**把旧文件全部放回原位置。
	/// 为 true 时结果里必须同时带 <see cref="OperationId"/> 与 <see cref="FailedFiles"/>，
	/// 并且 <see cref="Error"/> 不能声称"已回滚到旧版本"。
	/// </summary>
	public bool NeedsRecovery { get; set; }

	/// <summary>回滚后的恢复记录状态（restored / partial / failed / conflict）；未发生回滚时为 null。</summary>
	public string? RecoveryStatus { get; set; }

	/// <summary>回滚后仍未回到原位置的文件（相对路径 → 原因）。</summary>
	public List<string> FailedFiles { get; set; } = new List<string>();

	public string? FileName { get; set; }

	public bool WasEnabled { get; set; }

	public string? OldSha256 { get; set; }

	public string? NewSha256 { get; set; }
}

/// <summary>
/// MR05：Mod「替换/更新」的事务化提交。
///
/// 顺序是固定的，且每一步失败都不允许丢掉旧版本：
///   1. 校验 staged 新包存在，并按需校验其 SHA256（**校验通过之前不碰旧文件**）；
///   2. 确认目标存在，记录旧文件的启用状态与 SHA；
///   3. 把旧 jar 与 <c>.mcmini.json</c> 侧车隔离进恢复区（得到撤销句柄 operationId）；
///   4. 写入新文件并核对 SHA；
///   5. 写在线元数据；任何一步失败都回滚——删掉新文件、把旧 jar 与侧车按原 SHA 恢复回去。
///
/// 另外两条硬约束：
///   - **保留启用状态**：旧的是 <c>.jar.disabled</c> 时，新版本同样落在 <c>.jar.disabled</c>；
///   - **禁止并发替换同一目标**：同一目标同时在替换中时直接拒绝，而不是排队等待。
/// </summary>
public sealed class ModReplacementService
{
	private static readonly ConcurrentDictionary<string, byte> InFlight =
		new ConcurrentDictionary<string, byte>(StringComparer.OrdinalIgnoreCase);

	private readonly FileRecoveryService _recovery;

	/// <summary>仅测试：在写新文件之前注入一次失败，用于验证 commit 回滚分支。</summary>
	internal static Action<string>? BeforeCommitForTest;

	/// <summary>仅测试：在写在线元数据之前注入一次失败，用于验证 metadata 回滚分支。</summary>
	internal static Action<string>? BeforeMetadataWriteForTest;

	public ModReplacementService(FileRecoveryService recovery)
	{
		_recovery = recovery ?? throw new ArgumentNullException(nameof(recovery));
	}

	/// <summary>占位：同一目标已在替换中时返回 false。测试可直接验证该原语。</summary>
	public static bool TryBeginReplace(string targetPath) =>
		InFlight.TryAdd(Normalize(targetPath), 0);

	public static void EndReplace(string targetPath) =>
		InFlight.TryRemove(Normalize(targetPath), out _);

	public static bool IsReplacing(string targetPath) =>
		InFlight.ContainsKey(Normalize(targetPath));

	public async Task<ModReplacementResult> ReplaceAsync(
		GameInstance instance,
		ModService modService,
		string targetFileName,
		string stagedFilePath,
		string? expectedSha256 = null,
		ModVersion? version = null)
	{
		var result = new ModReplacementResult { FileName = targetFileName };

		string modsDir = modService.GetModsDirectory(instance);
		string targetPath = Path.Combine(modsDir, targetFileName);

		if (!FileRecoveryService.IsPathInside(modsDir, targetPath))
		{
			result.Status = "target_missing";
			result.Error = "目标路径越界，拒绝替换：" + targetFileName;
			return result;
		}

		if (!TryBeginReplace(targetPath))
		{
			result.Status = "replace_in_progress";
			result.Error = "该文件正在替换中，请稍后再试：" + targetFileName;
			return result;
		}

		try
		{
			// 1) 新包必须存在；给了期望哈希就先核对——**校验通过前不动旧文件**。
			if (string.IsNullOrWhiteSpace(stagedFilePath) || !File.Exists(stagedFilePath))
			{
				result.Status = "staged_missing";
				result.Error = "待替换的新文件不存在";
				return result;
			}

			string newSha = FileRecoveryService.HashFile(stagedFilePath);
			if (!string.IsNullOrWhiteSpace(expectedSha256)
				&& !string.Equals(newSha, expectedSha256, StringComparison.OrdinalIgnoreCase))
			{
				result.Status = "hash_mismatch";
				result.Error = "新文件 SHA256 与期望不一致，已放弃替换";
				return result;
			}

			// 2) 目标必须存在，并记录旧状态。
			if (!File.Exists(targetPath))
			{
				result.Status = "target_missing";
				result.Error = "要替换的 Mod 不存在：" + targetFileName;
				return result;
			}

			result.WasEnabled = !targetPath.EndsWith(".jar.disabled", StringComparison.OrdinalIgnoreCase);
			result.OldSha256 = FileRecoveryService.HashFile(targetPath);

			// 3) 先隔离旧文件（jar + 侧车），得到撤销句柄。
			var targets = new List<string> { targetPath };
			string sidecar = targetPath + ".mcmini.json";
			if (File.Exists(sidecar))
			{
				targets.Add(sidecar);
			}

			FileRecoveryRecord record = await _recovery
				.QuarantineAsync(instance.Id, modsDir, "mod-replace", targets)
				.ConfigureAwait(false);
			result.OperationId = record.OperationId;

			bool quarantineOk = record.Files.Any(x => x.State == "quarantined")
				&& record.Files.All(x => x.State != "failed");
			if (!quarantineOk)
			{
				RollbackOutcome quarantineRollback = await RollbackAsync(record).ConfigureAwait(false);
				result.Status = "quarantine_failed";
				ApplyRollback(result, quarantineRollback, "旧文件隔离失败", Describe(record));
				return result;
			}

			// 4) 写入新文件并核对 SHA。
			try
			{
				BeforeCommitForTest?.Invoke(targetPath);
				File.Copy(stagedFilePath, targetPath, overwrite: false);
				string written = FileRecoveryService.HashFile(targetPath);
				if (!string.Equals(written, newSha, StringComparison.OrdinalIgnoreCase))
				{
					throw new IOException("写入后 SHA256 与暂存文件不一致");
				}
			}
			catch (Exception ex)
			{
				TryDelete(targetPath);
				RollbackOutcome outcome = await RollbackAsync(record).ConfigureAwait(false);
				result.Status = "commit_failed";
				ApplyRollback(result, outcome, "写入新版本失败", ex.Message);
				return result;
			}

			// 5) 写在线元数据；侧车失败同样必须回滚，旧版本不能丢。
			if (version != null)
			{
				try
				{
					BeforeMetadataWriteForTest?.Invoke(targetPath);
					modService.WriteOnlineMetadata(targetPath, version);
				}
				catch (Exception ex)
				{
					TryDelete(targetPath);
					RollbackOutcome outcome = await RollbackAsync(record).ConfigureAwait(false);
					result.Status = "metadata_failed";
					ApplyRollback(result, outcome, "写入元数据失败", ex.Message);
					return result;
				}
			}

			result.Ok = true;
			result.Status = "replaced";
			result.NewSha256 = newSha;
			return result;
		}
		finally
		{
			EndReplace(targetPath);
		}
	}

	/// <summary>
	/// 把隔离记录里的旧文件恢复到原位置（撤销更新）。
	///
	/// R2（验收阻断项 F-03）修复：修复前这里**吞掉所有异常**，调用方随后**无条件**
	/// 拼上"已回滚到旧版本" —— 实测（只读目标 + 注入提交失败）恢复记录是 <c>partial</c>、
	/// 目标仍是 foreign 内容，用户却被告知已经回滚。现在返回**真实结果**：
	/// 逐项成败、恢复记录状态、未恢复文件、以及异常本身（不再吞）。
	/// </summary>
	private async Task<RollbackOutcome> RollbackAsync(FileRecoveryRecord record)
	{
		try
		{
			FileRecoveryRecord restored = await _recovery.RestoreAsync(record, overwrite: true).ConfigureAwait(false);
			List<string> notBack = restored.Files
				.Where(x => x.State != "restored" && x.State != "already_restored")
				.Select(x => x.RelativePath + " → " + (x.Error ?? x.State))
				.ToList();
			return new RollbackOutcome
			{
				Record = restored,
				Status = restored.Status,
				Complete = notBack.Count == 0,
				FailedFiles = notBack,
				Error = (notBack.Count == 0) ? null : "未能恢复的文件：" + string.Join("；", notBack)
			};
		}
		catch (Exception ex)
		{
			// 回滚异常**不能吞**：旧文件还在恢复区，调用方必须知道"它们还没回去"。
			List<string> notBack = record.Files
				.Where(x => x.State != "restored" && x.State != "already_restored")
				.Select(x => x.RelativePath + " → " + (x.Error ?? x.State))
				.ToList();
			return new RollbackOutcome
			{
				Record = record,
				Status = "failed",
				Complete = false,
				FailedFiles = notBack,
				Error = "回滚过程抛出异常：" + ex.GetType().Name + "：" + ex.Message
			};
		}
	}

	/// <summary>
	/// 把一次回滚的**真实**结果写进替换结果。
	///
	/// 只有**逐项都回到原位**时才允许出现"已回滚"；否则必须带
	/// <see cref="ModReplacementResult.NeedsRecovery"/>、operationId 与逐文件错误。
	/// </summary>
	private static void ApplyRollback(ModReplacementResult result, RollbackOutcome outcome, string failurePrefix, string originalException)
	{
		string operationId = outcome.Record.OperationId ?? result.OperationId ?? "(未知)";
		result.OperationId ??= outcome.Record.OperationId;
		result.RecoveryStatus = outcome.Status;
		result.FailedFiles = outcome.FailedFiles;
		result.NeedsRecovery = !outcome.Complete;

		if (outcome.Complete)
		{
			result.Error = failurePrefix + "，已回滚到旧版本：" + originalException;
			return;
		}

		result.Error = failurePrefix + "，**未能完全回滚**，旧版本仍需人工恢复。"
			+ "恢复记录 " + operationId
			+ "，回滚状态 " + outcome.Status
			+ "，未恢复文件：" + (outcome.FailedFiles.Count == 0 ? "(逐项状态见恢复记录)" : string.Join("；", outcome.FailedFiles))
			+ (string.IsNullOrWhiteSpace(outcome.Error) ? string.Empty : "；原因：" + outcome.Error)
			+ "；原始失败：" + originalException;
	}

	/// <summary>一次回滚的真实结果。</summary>
	private sealed class RollbackOutcome
	{
		public FileRecoveryRecord Record { get; set; } = new FileRecoveryRecord();

		public string Status { get; set; } = "failed";

		/// <summary>旧文件是否**全部**回到原位置。</summary>
		public bool Complete { get; set; }

		public List<string> FailedFiles { get; set; } = new List<string>();

		public string? Error { get; set; }
	}

	private static string Describe(FileRecoveryRecord record) =>
		string.Join("；", record.Files.ConvertAll(x => x.RelativePath + " → " + (x.Error ?? x.State)));

	private static string Normalize(string path)
	{
		try
		{
			return Path.GetFullPath(path);
		}
		catch
		{
			return path;
		}
	}

	private static void TryDelete(string path)
	{
		try
		{
			if (File.Exists(path))
			{
				File.Delete(path);
			}
		}
		catch
		{
		}
	}
}
