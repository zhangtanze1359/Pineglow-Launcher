using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using Pineglow.Launcher.Models;

namespace Pineglow.Launcher.Services;

/// <summary>
/// MR04：可恢复的文件隔离服务。
///
/// 设计要点：
///   - 隔离区位于**实例之外**（由调用方给出恢复根），因此即使游戏目录被移动或删除，
///     隔离副本与记录依然存在。
///   - **先写记录、再动文件**：记录里含原路径、隔离路径、SHA256 与状态，
///     进程在任意时刻中断都能据此恢复或如实报告冲突。
///   - 同卷用 <see cref="File.Move(string, string)"/>；跨卷失败时退化为
///     「复制 → SHA256 校验 → 删除源」，校验不通过则保留源并删除半成品。
///   - 恢复默认**不覆盖**同名现有文件：目标已存在且 SHA 不同即报 <c>conflict</c>，
///     相同则视为 <c>already_restored</c>（重复恢复幂等）。
///   - 只处理游戏目录内部的文件；符号链接/重解析点与越界路径一律拒绝。
///   - V1 不做物理清理：保留期只用于提示，不自动删除。
/// </summary>
public sealed class FileRecoveryService
{
	/// <summary>V1 保留期（天）。只用于提示，不触发自动清理。</summary>
	public const int DefaultRetentionDays = 30;

	private static readonly JsonSerializerOptions RecordJsonOptions = new JsonSerializerOptions
	{
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
		WriteIndented = true
	};

	private readonly string _recoveryRoot;

	public FileRecoveryService(string recoveryRoot)
	{
		if (string.IsNullOrWhiteSpace(recoveryRoot))
		{
			throw new InvalidOperationException("恢复根不能为空");
		}

		_recoveryRoot = Path.GetFullPath(recoveryRoot);
	}

	public string RecoveryRoot => _recoveryRoot;

	public string GetInstanceDirectory(string instanceId) => Path.Combine(_recoveryRoot, SafeSegment(instanceId));

	public string GetOperationDirectory(string instanceId, string operationId) =>
		Path.Combine(GetInstanceDirectory(instanceId), SafeSegment(operationId));

	public static DateTimeOffset GetExpiresAt(FileRecoveryRecord record) =>
		record.CreatedAt.AddDays(DefaultRetentionDays);

	/* ===================== 隔离 ===================== */

	/// <summary>
	/// 把 <paramref name="absolutePaths"/> 移入恢复区，返回含逐文件结果的记录。
	/// 单个文件失败不会中断其余文件；失败会在该文件项上带原因，绝不当成成功。
	/// </summary>
	public async Task<FileRecoveryRecord> QuarantineAsync(
		string instanceId,
		string gameDirectory,
		string reason,
		IReadOnlyList<string> absolutePaths)
	{
		string fullGameDir = Path.GetFullPath(gameDirectory);
		string operationId = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N").Substring(0, 8);
		string operationDir = GetOperationDirectory(instanceId, operationId);
		string filesDir = Path.Combine(operationDir, "files");
		Directory.CreateDirectory(filesDir);

		var record = new FileRecoveryRecord
		{
			OperationId = operationId,
			InstanceId = instanceId,
			GameDirectory = fullGameDir,
			Reason = reason,
			CreatedAt = DateTimeOffset.UtcNow,
			Status = "quarantining"
		};

		// 先落记录：此时还没有任何文件被移动，中断也不会丢信息。
		await WriteRecordAsync(operationDir, record).ConfigureAwait(false);

		foreach (string path in absolutePaths)
		{
			record.Files.Add(QuarantineOne(fullGameDir, filesDir, path));
		}

		int quarantined = record.Files.Count(x => x.State == "quarantined");
		record.Status = quarantined == record.Files.Count
			? "quarantined"
			: (quarantined > 0 ? "partial" : "failed");

		await WriteRecordAsync(operationDir, record).ConfigureAwait(false);
		return record;
	}

	private static FileRecoveryEntry QuarantineOne(string fullGameDir, string filesDir, string path)
	{
		var entry = new FileRecoveryEntry { OriginalPath = SafeFullPath(path), State = "failed" };

		if (string.IsNullOrWhiteSpace(entry.OriginalPath))
		{
			entry.Error = "路径无效";
			return entry;
		}

		if (!IsPathInside(fullGameDir, entry.OriginalPath))
		{
			entry.Error = "拒绝隔离游戏目录之外的文件";
			return entry;
		}

		entry.RelativePath = Path.GetRelativePath(fullGameDir, entry.OriginalPath);

		if (!File.Exists(entry.OriginalPath))
		{
			entry.State = "missing";
			entry.Error = "文件不存在";
			return entry;
		}

		if (IsReparsePoint(entry.OriginalPath) || HasReparseAncestor(fullGameDir, entry.OriginalPath))
		{
			entry.Error = "拒绝隔离符号链接/重解析点";
			return entry;
		}

		// A16 修复（MR04 范围）：哈希阶段也会因文件被占用/只读而抛出。
		// 修复前这里没有任何保护，异常会直接冲出 QuarantineAsync，
		// 与 MR04 报告所述"走 IOException 回退并逐项报错"不符（那只覆盖了下面的移动阶段）。
		// 现在与移动阶段用同一组异常类型，失败时逐项记录，而不是让整次操作炸掉。
		try
		{
			entry.Sha256 = HashFile(entry.OriginalPath);
			entry.SizeBytes = new FileInfo(entry.OriginalPath).Length;
		}
		catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
		{
			entry.State = "failed";
			entry.Error = "无法读取文件（可能被占用或只读）：" + ex.Message;
			return entry;
		}

		string stored = Path.Combine(filesDir, entry.RelativePath);
		string? storedDir = Path.GetDirectoryName(stored);
		if (!string.IsNullOrEmpty(storedDir))
		{
			Directory.CreateDirectory(storedDir);
		}

		try
		{
			File.Move(entry.OriginalPath, stored);
			entry.StoredPath = stored;
			entry.State = "quarantined";
			return entry;
		}
		catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
		{
			// 常见原因：跨卷、被占用、只读。退化为「复制 → 校验 → 删源」。
			return CopyThenRemove(entry, stored, ex);
		}
	}

	private static FileRecoveryEntry CopyThenRemove(FileRecoveryEntry entry, string stored, Exception moveError)
	{
		try
		{
			File.Copy(entry.OriginalPath, stored, overwrite: false);
			string copiedSha = HashFile(stored);
			if (!string.Equals(copiedSha, entry.Sha256, StringComparison.OrdinalIgnoreCase))
			{
				TryDelete(stored);
				entry.State = "failed";
				entry.Error = "复制后 SHA256 校验不通过：" + moveError.Message;
				return entry;
			}

			File.Delete(entry.OriginalPath);
			entry.StoredPath = stored;
			entry.State = "quarantined";
			return entry;
		}
		catch (Exception ex)
		{
			// 源文件必须保留：宁可失败，也不能让它凭空消失。
			TryDelete(stored);
			entry.State = "failed";
			entry.Error = ex.Message;
			return entry;
		}
	}

	/* ===================== 恢复 ===================== */

	/// <summary>
	/// 把记录中的文件恢复到原位置，返回更新后的逐文件结果。
	/// 默认不覆盖同名现有文件；重复恢复是幂等的。
	/// </summary>
	public Task<FileRecoveryRecord> RestoreAsync(FileRecoveryRecord record, bool overwrite = false) =>
		RestoreAsync(record, overwrite, trustedGameDirectory: null);

	/// <summary>
	/// S1（RA-01）重载：调用方提供**可信实例根**（HTTP 层从实例配置解析，进程内调用方从实例给出）。
	/// 记录里的 <c>GameDirectory</c> 只当候选根：真正的边界与祖先链校验锚定在可信根上，
	/// 并用句柄真实路径核对，禁止把不可信记录中的子目录当成完整边界。
	/// </summary>
	public async Task<FileRecoveryRecord> RestoreAsync(FileRecoveryRecord record, bool overwrite, string? trustedGameDirectory)
	{
		string operationDir = GetOperationDirectory(record.InstanceId, record.OperationId);
		string fullGameDir = SafeFullPath(record.GameDirectory);
		string trustedRoot = string.IsNullOrWhiteSpace(trustedGameDirectory) ? string.Empty : SafeFullPath(trustedGameDirectory);

		foreach (FileRecoveryEntry entry in record.Files)
		{
			RestoreOne(entry, fullGameDir, string.IsNullOrWhiteSpace(trustedRoot) ? null : trustedRoot, operationDir, overwrite);
		}

		int restored = record.Files.Count(x => x.State == "restored" || x.State == "already_restored");
		bool anyConflict = record.Files.Any(x => x.State == "conflict");
		bool anyFailed = record.Files.Any(x => x.State == "failed");
		record.Status = restored == record.Files.Count
			? "restored"
			: (anyConflict ? "conflict" : (restored == 0 && anyFailed ? "failed" : "partial"));

		await WriteRecordAsync(operationDir, record).ConfigureAwait(false);
		return record;
	}

	private void RestoreOne(FileRecoveryEntry entry, string fullGameDir, string? trustedRoot, string operationDir, bool overwrite)
	{
		entry.Error = null;

		if (string.IsNullOrWhiteSpace(fullGameDir))
		{
			entry.State = "failed";
			entry.Error = "记录中的游戏目录无效";
			return;
		}

		if (string.IsNullOrWhiteSpace(entry.OriginalPath))
		{
			entry.State = "failed";
			entry.Error = "记录中的原路径无效";
			return;
		}

		if (!IsPathInside(fullGameDir, entry.OriginalPath))
		{
			entry.State = "failed";
			entry.Error = "恢复目标越界，拒绝写入";
			return;
		}

		// S1（RA-01）：记录里的 GameDirectory 只是**候选根**，不是信任锚。
		// mod-replace 记录的根是 `game\mods`；把上层 `game` 换成 junction 后，"撤销替换"曾能写到实例目录之外。
		// 因此以**可信实例根**（调用方从实例配置/请求得到）为锚，记录根必须落在它之内。
		if (trustedRoot != null)
		{
			if (!PathSafetyGuard.PathsEqual(fullGameDir, trustedRoot) && !IsPathInside(trustedRoot, fullGameDir))
			{
				entry.State = "failed";
				entry.Error = "记录中的游戏目录不属于当前实例，拒绝恢复：" + fullGameDir;
				return;
			}

			if (!IsPathInside(trustedRoot, entry.OriginalPath))
			{
				entry.State = "failed";
				entry.Error = "恢复目标越出可信实例目录，拒绝写入";
				return;
			}
		}

		string anchor = trustedRoot ?? fullGameDir;

		// 托管层的早期过滤（跨平台可用）；Windows 上真正封住 RA-01 的是随后的句柄真实路径校验。
		if (!IsRealPathChain(anchor, entry.OriginalPath, out string targetChainError))
		{
			entry.State = "failed";
			entry.Error = "恢复目标路径不可信，拒绝写入：" + targetChainError;
			return;
		}

		if (!PathSafetyGuard.TryRejectReparseLeaf(entry.OriginalPath, out string leafError))
		{
			entry.State = "failed";
			entry.Error = "恢复目标不可信，拒绝写入：" + leafError;
			return;
		}

		if (string.IsNullOrWhiteSpace(entry.StoredPath))
		{
			entry.State = "failed";
			entry.Error = "隔离副本不存在，无法恢复";
			return;
		}

		// S2（RA-02）：隔离副本必须精确属于**本实例、本操作**的 files 目录，
		// 而不是"只要在全局恢复根里就行" —— 否则伪造记录可以消耗另一个实例的隔离文件。
		string operationFilesDir = Path.Combine(operationDir, "files");
		string storedFull = SafeFullPath(entry.StoredPath);
		if (string.IsNullOrWhiteSpace(storedFull)
			|| !IsPathInside(_recoveryRoot, storedFull)
			|| !IsPathInside(operationFilesDir, storedFull))
		{
			entry.State = "failed";
			entry.Error = "隔离副本不属于本实例/本操作，拒绝使用（伪造或跨操作的记录）";
			return;
		}

		if (!string.IsNullOrWhiteSpace(entry.RelativePath))
		{
			string actualRelative = Path.GetRelativePath(operationFilesDir, storedFull).Replace('\\', '/');
			string declaredRelative = entry.RelativePath.Replace('\\', '/');
			if (!string.Equals(actualRelative, declaredRelative, StringComparison.OrdinalIgnoreCase))
			{
				entry.State = "failed";
				entry.Error = "隔离副本的相对路径与记录不符，拒绝使用：" + actualRelative;
				return;
			}
		}

		if (!IsRealPathChain(_recoveryRoot, storedFull, out string storedChainError))
		{
			entry.State = "failed";
			entry.Error = "隔离副本路径不可信，拒绝使用：" + storedChainError;
			return;
		}

		// 句柄级校验：源链的**真实路径**必须与词法路径一致，且在本操作结束前不能被改名/删除。
		if (!PathSafetyGuard.TryPinDirectoryChain(_recoveryRoot, storedFull, createMissingTargetDirectory: false, out PathSafetyGuard.PinnedPath? storedPin, out string storedPinError))
		{
			entry.State = "failed";
			entry.Error = "隔离副本路径不可信，拒绝使用：" + storedPinError;
			return;
		}

		// 目标链同样钉住；缺失的父目录在最近存在的祖先被钉住之后才创建。
		if (!PathSafetyGuard.TryPinDirectoryChain(anchor, entry.OriginalPath, createMissingTargetDirectory: true, out PathSafetyGuard.PinnedPath? targetPin, out string targetPinError))
		{
			storedPin!.Dispose();
			entry.State = "failed";
			entry.Error = "恢复目标路径不可信，拒绝写入：" + targetPinError;
			return;
		}

		using (storedPin)
		using (targetPin)
		{
			RestoreOnePinned(entry, anchor, storedFull, overwrite);
		}
	}

	/// <summary>目标链与源链已被句柄钉住之后的实际恢复动作（调用方负责 Dispose 两个 pin）。</summary>
	private void RestoreOnePinned(FileRecoveryEntry entry, string anchor, string storedFull, bool overwrite)
	{

		// 先看原位置：上一次恢复已经把文件放回去时，隔离副本自然已经不在了，
		// 这属于幂等成功，不能报成"隔离副本不存在"的失败。
		if (File.Exists(entry.OriginalPath))
		{
			string existingSha = HashFile(entry.OriginalPath);
			if (string.Equals(existingSha, entry.Sha256, StringComparison.OrdinalIgnoreCase))
			{
				// 只有确认该源属于本操作（上面的绑定校验已通过），才允许清理它。
				if (File.Exists(storedFull))
				{
					TryDelete(storedFull);
				}

				entry.State = "already_restored";
				return;
			}

			if (!overwrite)
			{
				entry.State = "conflict";
				entry.Error = "目标位置已有同名文件且内容不同，默认不覆盖";
				return;
			}
		}

		if (!File.Exists(storedFull))
		{
			entry.State = "failed";
			entry.Error = "隔离副本不存在，无法恢复";
			return;
		}

		// S2：消费隔离源**之前**核对源 SHA。记录被篡改时，不能把"别人的文件"搬过来。
		string sourceSha = HashFile(storedFull);
		if (!string.Equals(sourceSha, entry.Sha256, StringComparison.OrdinalIgnoreCase))
		{
			entry.State = "failed";
			entry.Error = "隔离副本 SHA256 与记录不符，拒绝消费该源";
			return;
		}

		try
		{
			File.Move(storedFull, entry.OriginalPath);
		}
		catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
		{
			try
			{
				File.Copy(storedFull, entry.OriginalPath, overwrite: true);
				string copiedSha = HashFile(entry.OriginalPath);
				if (!string.Equals(copiedSha, entry.Sha256, StringComparison.OrdinalIgnoreCase))
				{
					TryDelete(entry.OriginalPath);
					entry.State = "failed";
					entry.Error = "恢复后 SHA256 校验不通过：" + ex.Message;
					return;
				}

				File.Delete(storedFull);
			}
			catch (Exception copyEx)
			{
				entry.State = "failed";
				entry.Error = copyEx.Message;
				return;
			}
		}

		// 写入后复核：句柄真实路径必须仍然与请求路径一致。
		// 注意：这是**第二层**，不是唯一依赖 —— 第一层是写入前钉住从句柄解析出的真实路径，
		// 且句柄不含 FILE_SHARE_DELETE，操作期间目录不能被改名/删除。
		if (!PathSafetyGuard.TryPinDirectoryChain(anchor, entry.OriginalPath, createMissingTargetDirectory: false, out PathSafetyGuard.PinnedPath? postPin, out string postError))
		{
			entry.State = "failed";
			entry.Error = "恢复写入后复核发现问题（目录在写入过程中被替换）：" + postError;
			return;
		}

		postPin!.Dispose();

		string finalSha = HashFile(entry.OriginalPath);
		if (!string.Equals(finalSha, entry.Sha256, StringComparison.OrdinalIgnoreCase))
		{
			entry.State = "failed";
			entry.Error = "恢复完成但 SHA256 与隔离前不一致";
			return;
		}

		entry.State = "restored";
	}

	/* ===================== 查询 ===================== */

	/// <summary>列出某实例的全部恢复记录，按时间倒序。损坏或缺失的记录会被跳过。</summary>
	public Task<List<FileRecoveryRecord>> ListAsync(string instanceId)
	{
		var list = new List<FileRecoveryRecord>();
		string instanceDir = GetInstanceDirectory(instanceId);
		if (!Directory.Exists(instanceDir))
		{
			return Task.FromResult(list);
		}

		foreach (string dir in Directory.EnumerateDirectories(instanceDir))
		{
			string recordPath = Path.Combine(dir, "record.json");
			if (!File.Exists(recordPath))
			{
				continue;
			}

			try
			{
				FileRecoveryRecord? record = JsonSerializer.Deserialize<FileRecoveryRecord>(
					File.ReadAllText(recordPath), RecordJsonOptions);
				if (record != null
					&& string.Equals(record.InstanceId, instanceId, StringComparison.Ordinal))
				{
					list.Add(record);
				}
			}
			catch
			{
				// 记录不可读时跳过，不伪装成一条可用记录。
			}
		}

		return Task.FromResult(list.OrderByDescending(x => x.CreatedAt).ToList());
	}

	public Task<FileRecoveryRecord?> LoadAsync(string instanceId, string operationId)
	{
		string recordPath = Path.Combine(GetOperationDirectory(instanceId, operationId), "record.json");
		if (!File.Exists(recordPath))
		{
			return Task.FromResult<FileRecoveryRecord?>(null);
		}

		try
		{
			FileRecoveryRecord? record = JsonSerializer.Deserialize<FileRecoveryRecord>(
				File.ReadAllText(recordPath), RecordJsonOptions);

			// S2：请求标识必须与记录标识**逐字相等**（Ordinal，不做任何归一化）。
			// `SafeSegment` 会剥掉非字母数字字符，`a-b` 与 `ab` 会落到同一个目录；
			// 只按目录取记录会让伪造请求误碰另一个操作。
			if (record != null
				&& !string.Equals(record.InstanceId, instanceId, StringComparison.Ordinal))
			{
				return Task.FromResult<FileRecoveryRecord?>(null);
			}

			if (record != null
				&& !string.Equals(record.OperationId, operationId, StringComparison.Ordinal))
			{
				return Task.FromResult<FileRecoveryRecord?>(null);
			}

			return Task.FromResult(record);
		}
		catch
		{
			return Task.FromResult<FileRecoveryRecord?>(null);
		}
	}

	/* ===================== 工具 ===================== */

	/// <summary>目标必须严格位于根目录内部（根目录自身不算）。</summary>
	public static bool IsPathInside(string rootFullPath, string candidateFullPath)
	{
		if (string.IsNullOrWhiteSpace(rootFullPath) || string.IsNullOrWhiteSpace(candidateFullPath))
		{
			return false;
		}

		string root;
		string candidate;
		try
		{
			root = Path.GetFullPath(rootFullPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
			candidate = Path.GetFullPath(candidateFullPath);
		}
		catch
		{
			return false;
		}

		if (string.Equals(candidate, root, StringComparison.OrdinalIgnoreCase))
		{
			return false;
		}

		return candidate.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
	}

	public static string HashFile(string path)
	{
		using FileStream fs = File.OpenRead(path);
		return Convert.ToHexString(SHA256.HashData(fs));
	}

	private static string SafeSegment(string value)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			throw new InvalidOperationException("标识不能为空");
		}

		string cleaned = new string(value.Where(c => char.IsLetterOrDigit(c) || c == '-' || c == '_').ToArray());
		if (string.IsNullOrEmpty(cleaned) || cleaned == "." || cleaned == "..")
		{
			throw new InvalidOperationException("标识含非法字符：" + value);
		}

		return cleaned;
	}

	private static string SafeFullPath(string path)
	{
		try
		{
			return Path.GetFullPath(path);
		}
		catch
		{
			return string.Empty;
		}
	}

	private static bool IsReparsePoint(string path)
	{
		try
		{
			return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
		}
		catch
		{
			return false;
		}
	}

	/// <summary>
	/// R1（验收阻断项 F-01）：确认 <paramref name="candidateFullPath"/> 落到
	/// <paramref name="rootFullPath"/> 之下时，整条**真实**路径链都不含重解析点。
	///
	/// 检查范围：
	///   * 根目录自身（必须存在、是目录、且不是联接）；
	///   * 从叶子的父目录向上直到根目录的每一级**已存在**层级
	///     —— 因此"缺失目录"自然回落到"最近存在的祖先"；
	///   * 叶子自身（若已存在且是重解析点）。
	///
	/// 这是**写入前**的校验；它不声称关闭"校验与写入之间的竞态"，
	/// 竞态的显式处理见 <see cref="RestoreOne"/> 的写入后复核与交付文档的残留风险一节。
	/// </summary>
	private static bool IsRealPathChain(string rootFullPath, string candidateFullPath, out string error)
	{
		error = string.Empty;

		string root;
		string candidate;
		try
		{
			root = Path.GetFullPath(rootFullPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
			candidate = Path.GetFullPath(candidateFullPath);
		}
		catch
		{
			error = "路径无法规范化";
			return false;
		}

		if (string.Equals(root, candidate, StringComparison.OrdinalIgnoreCase))
		{
			error = "目标就是根目录自身";
			return false;
		}

		if (File.Exists(root))
		{
			error = "根路径是文件而不是目录：" + root;
			return false;
		}

		if (!Directory.Exists(root))
		{
			error = "根目录不存在，无法确认写入边界：" + root;
			return false;
		}

		if (TryFindReparsePointOnPath(root, candidate, out string offender))
		{
			error = "路径上存在符号链接/重解析点（junction）：" + offender;
			return false;
		}

		return true;
	}

	/// <summary>在 <paramref name="rootFullPath"/> 之内的路径链上查找第一个重解析点（含根自身与叶子）。</summary>
	private static bool TryFindReparsePointOnPath(string rootFullPath, string candidateFullPath, out string offender)
	{
		offender = string.Empty;

		string root;
		string candidate;
		try
		{
			root = Path.GetFullPath(rootFullPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
			candidate = Path.GetFullPath(candidateFullPath);
		}
		catch
		{
			return false;
		}

		if (IsReparsePoint(root))
		{
			offender = root;
			return true;
		}

		string? current = Path.GetDirectoryName(candidate);
		while (!string.IsNullOrEmpty(current) && IsPathInside(root, current))
		{
			if ((Directory.Exists(current) || File.Exists(current)) && IsReparsePoint(current))
			{
				offender = current;
				return true;
			}

			current = Path.GetDirectoryName(current);
		}

		if (File.Exists(candidate) && IsReparsePoint(candidate))
		{
			offender = candidate;
			return true;
		}

		return false;
	}

	private static bool HasReparseAncestor(string root, string path)
	{
		try
		{
			string rootFull = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
			string? current = Path.GetDirectoryName(Path.GetFullPath(path));
			while (!string.IsNullOrEmpty(current) && current.Length > rootFull.Length)
			{
				if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
				{
					return true;
				}

				current = Path.GetDirectoryName(current);
			}
		}
		catch
		{
		}

		return false;
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

	private async Task WriteRecordAsync(string operationDir, FileRecoveryRecord record)
	{
		Directory.CreateDirectory(operationDir);
		string recordPath = Path.Combine(operationDir, "record.json");
		string tempPath = recordPath + ".tmp";
		await File.WriteAllTextAsync(tempPath, JsonSerializer.Serialize(record, RecordJsonOptions)).ConfigureAwait(false);
		File.Move(tempPath, recordPath, overwrite: true);
	}
}
