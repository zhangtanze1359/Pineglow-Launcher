using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Pineglow.Launcher.Models;

namespace Pineglow.Launcher.Services;

/// <summary>
/// M2-1①：存档（**目录**）级的可恢复删除。
///
/// 为什么不在 <see cref="FileRecoveryService"/> 上加目录支持：
/// 那个服务的记录（<see cref="FileRecoveryRecord"/>）与恢复逻辑逐项都是 <c>File.Exists</c> /
/// <c>File.Move</c> / 单文件 SHA256 语义，且它的 <c>ListAsync</c> 会被 Mod 恢复界面直接消费。
/// 把目录塞进同一份记录，等于让 Mod 恢复界面看到一个它无法恢复的条目 —— 那是比"删了就没了"
/// 更难排查的故障。所以这里**复用同一套约定**（同一恢复根、同一 <see cref="PathSafetyGuard"/>
/// 校验、同样"先落记录再动数据"），但记录与目录结构独立。
///
/// 记录与存储布局：
/// <code>
/// {recoveryRoot}/saves/{instanceId}/{operationId}/record.json
/// {recoveryRoot}/saves/{instanceId}/{operationId}/data/{saveFolderName}/...
/// </code>
/// 刻意放在 <c>saves/</c> 子目录下，与文件恢复的 <c>{recoveryRoot}/{instanceId}/{operationId}/</c>
/// 分开，两套 <c>ListAsync</c> 天然互不可见。
/// </summary>
public sealed class SaveRecoveryService
{
	/// <summary>V1 保留期（天）。与文件恢复保持同一口径，只用于提示，不触发自动清理。</summary>
	public const int DefaultRetentionDays = FileRecoveryService.DefaultRetentionDays;

	private const string ReasonSaveDelete = "save-delete";

	private static readonly JsonSerializerOptions RecordJsonOptions = new JsonSerializerOptions
	{
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
		WriteIndented = true
	};

	private readonly string _recoveryRoot;

	public SaveRecoveryService(string recoveryRoot)
	{
		if (string.IsNullOrWhiteSpace(recoveryRoot))
		{
			throw new InvalidOperationException("恢复根不能为空");
		}

		_recoveryRoot = Path.GetFullPath(recoveryRoot);
	}

	public string RecoveryRoot => _recoveryRoot;

	public string GetInstanceDirectory(string instanceId) =>
		Path.Combine(_recoveryRoot, "saves", SafeSegment(instanceId));

	public string GetOperationDirectory(string instanceId, string operationId) =>
		Path.Combine(GetInstanceDirectory(instanceId), SafeSegment(operationId));

	public static DateTimeOffset GetExpiresAt(SaveRecoveryRecord record) =>
		record.CreatedAt.AddDays(DefaultRetentionDays);

	/* ===================== 删除（移入恢复区）===================== */

	/// <summary>
	/// 把存档目录移入恢复区。**成功返回记录；失败抛异常且源目录保持原样。**
	///
	/// 与文件级隔离的关键差别：存档是一个整体，只成功了一半（部分文件搬走）比完全失败更糟，
	/// 所以这里不做"逐项尽力"——任一步失败就回滚到"源目录完好"的状态，并把记录标成 failed。
	/// </summary>
	public async Task<SaveRecoveryRecord> DeleteAsync(string instanceId, string gameDirectory, string saveFolderName)
	{
		if (string.IsNullOrWhiteSpace(saveFolderName))
		{
			throw new InvalidOperationException("存档名不能为空。");
		}

		string fullGameDir = Path.GetFullPath(gameDirectory);
		string savesDir = Path.Combine(fullGameDir, "saves");
		string sourcePath = Path.GetFullPath(Path.Combine(savesDir, saveFolderName));

		// ---- 1. 只允许删 saves 的**直接子目录** ----
		// 不接受调用方给的任意路径：`../../` 或绝对路径都要挡掉，否则这就是一个任意目录删除接口。
		if (!FileRecoveryService.IsPathInside(savesDir, sourcePath))
		{
			throw new InvalidOperationException("拒绝删除存档目录之外的路径：" + sourcePath);
		}

		if (!string.Equals(Path.GetDirectoryName(sourcePath)?.TrimEnd(Path.DirectorySeparatorChar),
				savesDir.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
		{
			throw new InvalidOperationException("只能删除 saves 下的一级存档目录：" + sourcePath);
		}

		if (!Directory.Exists(sourcePath))
		{
			throw new DirectoryNotFoundException("要删除的存档文件夹不存在：" + saveFolderName);
		}

		// ---- 2. 重解析点拒绝：存档目录被换成 junction 时，"移动"会变成"移动链接" ----
		// 注意：不能直接用 PathSafetyGuard.TryRejectReparseLeaf —— 它用 File.Exists 判断，
		// 而这里的叶子是**目录**，File.Exists 对目录返回 false，等于什么都没校验。
		// 目录必须自己看 DirectoryInfo.Attributes。
		if (IsDirectoryReparsePoint(sourcePath))
		{
			throw new InvalidOperationException("存档目录本身是符号链接/重解析点，拒绝删除：" + sourcePath);
		}

		// ---- 3. 钉住父链（saves → 各级 → gameDir），确认中间没有 junction ----
		using (PathSafetyGuard.PinnedPath? sourcePin = PinParentChain(fullGameDir, sourcePath))
		{
			SaveManifest manifest = ComputeManifest(sourcePath);

			string operationId = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N").Substring(0, 8);
			string operationDir = GetOperationDirectory(instanceId, operationId);
			string dataDir = Path.Combine(operationDir, "data");
			string storedPath = Path.Combine(dataDir, saveFolderName);

			Directory.CreateDirectory(dataDir);

			SaveRecoveryRecord record = new SaveRecoveryRecord
			{
				OperationId = operationId,
				InstanceId = instanceId,
				GameDirectory = fullGameDir,
				Reason = ReasonSaveDelete,
				SaveFolderName = saveFolderName,
				OriginalPath = sourcePath,
				StoredPath = storedPath,
				CreatedAt = DateTimeOffset.UtcNow,
				Status = "quarantining",
				FileCount = manifest.FileCount,
				SizeBytes = manifest.SizeBytes,
				ManifestSha256 = manifest.Sha256,
			};

			// 先落记录：此刻还没动任何数据，中断也能从记录里找回隔离位置。
			await WriteRecordAsync(operationDir, record).ConfigureAwait(false);

			try
			{
				MoveDirectory(sourcePath, storedPath, manifest);
			}
			catch (Exception ex)
			{
				// 回滚到"源目录完好"：MoveDirectory 内部已保证失败时不动源目录，
				// 但恢复区里可能留了半份副本，这里清掉，避免出现"两处都不完整"。
				TryDeleteDirectory(dataDir);

				record.Status = "failed";
				record.Error = ex.Message;
				await WriteRecordAsync(operationDir, record).ConfigureAwait(false);

				throw new InvalidOperationException("存档删除失败，原存档已保留在原处：" + ex.Message, ex);
			}

			record.Status = "quarantined";
			await WriteRecordAsync(operationDir, record).ConfigureAwait(false);
			return record;
		}
	}

	/// <summary>
	/// 移动目录并校验。**失败时源目录必须完好**，因此先复制校验、成功后才删源；
	/// 同卷且能原子改名时走 <see cref="Directory.Move(string, string)"/> 的快路径。
	/// </summary>
	private static void MoveDirectory(string sourcePath, string storedPath, SaveManifest expected)
	{
		try
		{
			Directory.Move(sourcePath, storedPath);
		}
		catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
		{
			// 常见原因：跨卷。退化为「复制 → 校验 → 删源」。
			CopyDirectory(sourcePath, storedPath);
			SaveManifest actual = ComputeManifest(storedPath);
			if (!string.Equals(actual.Sha256, expected.Sha256, StringComparison.OrdinalIgnoreCase))
			{
				TryDeleteDirectory(storedPath);
				throw new IOException("复制到恢复区后校验不通过，已放弃本次删除：" + ex.Message, ex);
			}

			Directory.Delete(sourcePath, recursive: true);
			return;
		}

		// 快路径也要复核：Directory.Move 在同一卷上几乎不会出错，但恢复区可能在别的卷且被重定向，
		// 真出问题时宁可早报，也不要留一个"记录说搬了、实际没搬全"的状态。
		SaveManifest moved = ComputeManifest(storedPath);
		if (!string.Equals(moved.Sha256, expected.Sha256, StringComparison.OrdinalIgnoreCase))
		{
			// 尽力搬回去，保持"源完好"这个不变量
			try
			{
				Directory.Move(storedPath, sourcePath);
			}
			catch
			{
				// 搬不回去时至少把差异说清楚，让人能手工找回
				throw new IOException(
					$"移动到恢复区后内容校验不一致（期望 {expected.Sha256}，实际 {moved.Sha256}），" +
					$"且无法自动回滚。数据在：{storedPath}");
			}

			throw new IOException("移动到恢复区后内容校验不一致，已回滚到原位置。");
		}
	}

	/* ===================== 查询 ===================== */

	public Task<List<SaveRecoveryRecord>> ListAsync(string instanceId)
	{
		var list = new List<SaveRecoveryRecord>();
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
				SaveRecoveryRecord? record = JsonSerializer.Deserialize<SaveRecoveryRecord>(
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

		list.Sort((a, b) => b.CreatedAt.CompareTo(a.CreatedAt));
		return Task.FromResult(list);
	}

	public async Task<SaveRecoveryRecord?> LoadAsync(string instanceId, string operationId)
	{
		string recordPath = Path.Combine(GetOperationDirectory(instanceId, operationId), "record.json");
		if (!File.Exists(recordPath))
		{
			return null;
		}

		try
		{
			return JsonSerializer.Deserialize<SaveRecoveryRecord>(await File.ReadAllTextAsync(recordPath).ConfigureAwait(false), RecordJsonOptions);
		}
		catch
		{
			return null;
		}
	}

	/* ===================== 恢复 ===================== */

	/// <summary>
	/// 把已删除的存档恢复到原实例的 saves 目录。
	///
	/// 同名冲突时**不覆盖**，而是改名成 <c>{name} (restored N)</c> ——
	/// 计划要求"恢复默认保留现有世界；同名冲突时恢复为副本"，覆盖会毁掉用户正在玩的世界。
	/// </summary>
	public async Task<SaveRecoveryRecord> RestoreAsync(string instanceId, string operationId, string trustedGameDirectory)
	{
		SaveRecoveryRecord record = await LoadAsync(instanceId, operationId).ConfigureAwait(false)
			?? throw new InvalidOperationException("找不到该恢复记录：" + operationId);

		if (record.Status == "restored")
		{
			// 幂等：重复点"恢复"不该报错，也不该再复制一份
			return record;
		}

		if (record.Status != "quarantined")
		{
			throw new InvalidOperationException("该记录的状态为「" + record.Status + "」，无法恢复。");
		}

		string trustedRoot = Path.GetFullPath(trustedGameDirectory);
		string savesDir = Path.Combine(trustedRoot, "saves");

		// 记录里的 GameDirectory 只是候选根，不是信任锚（与文件恢复的 S1 同一条纪律）。
		if (!PathSafetyGuard.PathsEqual(record.GameDirectory, trustedRoot)
			&& !FileRecoveryService.IsPathInside(trustedRoot, record.GameDirectory))
		{
			throw new InvalidOperationException("该存档不属于当前实例，拒绝恢复：" + record.GameDirectory);
		}

		Directory.CreateDirectory(savesDir);

		// 隔离副本必须精确属于**本实例、本操作**，防伪造记录消耗别人的数据。
		string operationDir = GetOperationDirectory(instanceId, operationId);
		string storedPath = Path.GetFullPath(record.StoredPath);
		if (!FileRecoveryService.IsPathInside(_recoveryRoot, storedPath)
			|| !FileRecoveryService.IsPathInside(Path.Combine(operationDir, "data"), storedPath))
		{
			throw new InvalidOperationException("隔离副本不属于本实例/本操作，拒绝使用。");
		}

		if (!Directory.Exists(storedPath))
		{
			throw new DirectoryNotFoundException("隔离副本不存在，无法恢复：" + storedPath);
		}

		SaveManifest actual = ComputeManifest(storedPath);
		if (!string.IsNullOrEmpty(record.ManifestSha256)
			&& !string.Equals(actual.Sha256, record.ManifestSha256, StringComparison.OrdinalIgnoreCase))
		{
			throw new InvalidOperationException(
				$"隔离副本内容与记录不符（期望 {record.ManifestSha256}，实际 {actual.Sha256}），拒绝恢复。");
		}

		string targetPath = GetAvailableTargetPath(savesDir, record.SaveFolderName);

		try
		{
			Directory.Move(storedPath, targetPath);
		}
		catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
		{
			CopyDirectory(storedPath, targetPath);
			SaveManifest copied = ComputeManifest(targetPath);
			if (!string.Equals(copied.Sha256, record.ManifestSha256, StringComparison.OrdinalIgnoreCase))
			{
				TryDeleteDirectory(targetPath);
				throw new IOException("恢复后内容校验不通过，已放弃：" + ex.Message, ex);
			}

			Directory.Delete(storedPath, recursive: true);
		}

		record.Status = "restored";
		record.RestoredAt = DateTimeOffset.UtcNow;
		record.RestoredPath = targetPath;
		record.Error = null;
		await WriteRecordAsync(operationDir, record).ConfigureAwait(false);
		return record;
	}

	/* ===================== 永久清理 ===================== */

	/// <summary>
	/// 永久删除恢复区里的副本。**只接受"已隔离"或"恢复失败"的记录**，
	/// 且路径必须落在本操作目录内 —— 不做这两条校验，这里就是一个任意目录删除接口。
	/// </summary>
	public async Task PurgeAsync(string instanceId, string operationId)
	{
		SaveRecoveryRecord record = await LoadAsync(instanceId, operationId).ConfigureAwait(false)
			?? throw new InvalidOperationException("找不到该恢复记录：" + operationId);

		if (record.Status == "restored")
		{
			throw new InvalidOperationException("该记录已恢复，无需清理。");
		}

		if (record.Status != "quarantined" && record.Status != "failed")
		{
			throw new InvalidOperationException("该记录的状态为「" + record.Status + "」，不能清理。");
		}

		string operationDir = Path.GetFullPath(GetOperationDirectory(instanceId, operationId));
		string dataDir = Path.Combine(operationDir, "data");

		if (!FileRecoveryService.IsPathInside(GetInstanceDirectory(instanceId), operationDir))
		{
			throw new InvalidOperationException("操作目录越界，拒绝清理。");
		}

		TryDeleteDirectory(dataDir);

		record.Status = "purged";
		await WriteRecordAsync(operationDir, record).ConfigureAwait(false);
	}

	/* ===================== 清单（内容指纹）===================== */

	/// <summary>
	/// 计算目录的内容指纹：**文件数 + 总字节 + 排序后的「相对路径:长度」行的 SHA256**。
	///
	/// 为什么不用逐文件哈希：存档动辄上千个 region 文件、几百 MB，逐文件 SHA256 会让
	/// "删除一个存档"变成分钟级操作。这里的指纹足以发现"搬漏了文件""少了一个区域文件"
	/// 这类真实故障，且对单文件内容篡改不敏感 —— **那是刻意的取舍**，因为恢复区的副本
	/// 不存在被第三方篡改的威胁模型（它在用户自己的配置目录里），要防的是搬运过程出错。
	/// </summary>
	public static SaveManifest ComputeManifest(string directory)
	{
		if (!Directory.Exists(directory))
		{
			throw new DirectoryNotFoundException("目录不存在：" + directory);
		}

		var entries = new List<(string Relative, long Length)>();
		long total = 0;

		foreach (string file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
		{
			long length;
			try
			{
				length = new FileInfo(file).Length;
			}
			catch
			{
				length = -1;
			}

			total += Math.Max(0, length);
			entries.Add((Path.GetRelativePath(directory, file).Replace('\\', '/'), length));
		}

		entries.Sort((a, b) => StringComparer.Ordinal.Compare(a.Relative, b.Relative));

		var sb = new StringBuilder();
		foreach ((string relative, long length) in entries)
		{
			sb.Append(relative).Append(':').Append(length).Append('\n');
		}

		byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()));
		return new SaveManifest(entries.Count, total, Convert.ToHexString(hash));
	}

	/* ===================== 内部工具 ===================== */

	private static PathSafetyGuard.PinnedPath? PinParentChain(string anchor, string target)
	{
		// createMissingTargetDirectory: false —— 存档目录必须已存在，不在这里造目录。
		if (!PathSafetyGuard.TryPinDirectoryChain(anchor, target, createMissingTargetDirectory: false,
				out PathSafetyGuard.PinnedPath? pin, out string error))
		{
			throw new InvalidOperationException("存档目录的父链不可信，拒绝删除：" + error);
		}

		return pin;
	}

	/// <summary>目录本身是否为重解析点（符号链接 / junction）。</summary>
	private static bool IsDirectoryReparsePoint(string directoryPath)
	{
		try
		{
			return (File.GetAttributes(directoryPath) & FileAttributes.ReparsePoint) != 0;
		}
		catch (Exception ex)
		{
			// 读不到属性时按"不可信"处理：拒绝删除比错误删除安全。
			throw new InvalidOperationException("无法读取存档目录属性，拒绝删除：" + ex.Message, ex);
		}
	}

	private static string GetAvailableTargetPath(string savesDir, string folderName)
	{
		string candidate = Path.Combine(savesDir, folderName);
		if (!Directory.Exists(candidate) && !File.Exists(candidate))
		{
			return candidate;
		}

		for (int i = 1; i < 1000; i++)
		{
			candidate = Path.Combine(savesDir, $"{folderName} (restored {i})");
			if (!Directory.Exists(candidate) && !File.Exists(candidate))
			{
				return candidate;
			}
		}

		throw new IOException("同名存档过多，无法自动生成新文件夹名。");
	}

	private static void CopyDirectory(string sourceDir, string targetDir)
	{
		Directory.CreateDirectory(targetDir);
		foreach (string file in Directory.EnumerateFiles(sourceDir))
		{
			File.Copy(file, Path.Combine(targetDir, Path.GetFileName(file)), overwrite: false);
		}

		foreach (string sub in Directory.EnumerateDirectories(sourceDir))
		{
			CopyDirectory(sub, Path.Combine(targetDir, Path.GetFileName(sub)));
		}
	}

	private static void TryDeleteDirectory(string path)
	{
		try
		{
			if (Directory.Exists(path))
			{
				Directory.Delete(path, recursive: true);
			}
		}
		catch
		{
			// 清理失败不能把主流程带崩；残留会由"保留期提示"暴露给用户。
		}
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

	private async Task WriteRecordAsync(string operationDir, SaveRecoveryRecord record)
	{
		Directory.CreateDirectory(operationDir);
		string recordPath = Path.Combine(operationDir, "record.json");
		string tempPath = recordPath + ".tmp";
		await File.WriteAllTextAsync(tempPath, JsonSerializer.Serialize(record, RecordJsonOptions)).ConfigureAwait(false);
		File.Move(tempPath, recordPath, overwrite: true);
	}
}

/// <summary>目录内容指纹的结果。</summary>
public readonly record struct SaveManifest(int FileCount, long SizeBytes, string Sha256);

/// <summary>
/// 一次「存档可恢复删除」操作的记录。与 <see cref="FileRecoveryRecord"/> 分开：
/// 后者是逐文件语义，会被 Mod 恢复界面直接消费，混用会让那边看到无法恢复的条目。
/// </summary>
public sealed class SaveRecoveryRecord
{
	public string OperationId { get; set; } = string.Empty;

	public string InstanceId { get; set; } = string.Empty;

	/// <summary>操作发生时的游戏目录绝对路径，用于校验恢复目标没有越界。</summary>
	public string GameDirectory { get; set; } = string.Empty;

	/// <summary>操作来源，固定 <c>save-delete</c>。</summary>
	public string Reason { get; set; } = string.Empty;

	public string SaveFolderName { get; set; } = string.Empty;

	/// <summary>原始绝对路径（saves 下的一级目录）。</summary>
	public string OriginalPath { get; set; } = string.Empty;

	/// <summary>恢复区内的绝对路径。</summary>
	public string StoredPath { get; set; } = string.Empty;

	public DateTimeOffset CreatedAt { get; set; }

	/// <summary>quarantining | quarantined | failed | restored | purged</summary>
	public string Status { get; set; } = string.Empty;

	public int FileCount { get; set; }

	public long SizeBytes { get; set; }

	/// <summary>隔离时的内容指纹，恢复时复核。</summary>
	public string ManifestSha256 { get; set; } = string.Empty;

	public DateTimeOffset? RestoredAt { get; set; }

	/// <summary>恢复后的实际路径（同名冲突时会带 <c>(restored N)</c> 后缀）。</summary>
	public string? RestoredPath { get; set; }

	public string? Error { get; set; }
}
