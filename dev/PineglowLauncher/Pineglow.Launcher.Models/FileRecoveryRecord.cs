using System;
using System.Collections.Generic;

namespace Pineglow.Launcher.Models;

/// <summary>
/// MR04：一次"可恢复删除"操作的记录。
///
/// 删除不再直接 <c>File.Delete</c>，而是把文件移入受管理的恢复区并留下本记录，
/// 用户可据此把文件恢复到原位置。记录在**动文件之前**就先落盘，
/// 因此即使进程在移动过程中中断，也能从记录里找回隔离位置。
/// </summary>
public sealed class FileRecoveryRecord
{
	/// <summary>操作 ID（唯一），同时是恢复区内的目录名。</summary>
	public string OperationId { get; set; } = string.Empty;

	/// <summary>所属实例 Id。</summary>
	public string InstanceId { get; set; } = string.Empty;

	/// <summary>操作发生时的游戏目录绝对路径，用于校验恢复目标没有越界。</summary>
	public string GameDirectory { get; set; } = string.Empty;

	/// <summary>操作来源，例如 <c>mod-delete</c>。</summary>
	public string Reason { get; set; } = string.Empty;

	public DateTimeOffset CreatedAt { get; set; }

	/// <summary>quarantining | quarantined | partial | failed | restored | conflict</summary>
	public string Status { get; set; } = string.Empty;

	public List<FileRecoveryEntry> Files { get; set; } = new List<FileRecoveryEntry>();
}

/// <summary>单个被隔离文件的逐项结果。失败不是异常，而是带原因的状态。</summary>
public sealed class FileRecoveryEntry
{
	/// <summary>相对游戏目录的路径；恢复时以它定位目标。</summary>
	public string RelativePath { get; set; } = string.Empty;

	/// <summary>原始绝对路径。</summary>
	public string OriginalPath { get; set; } = string.Empty;

	/// <summary>恢复区内的绝对路径；隔离未成功时为空。</summary>
	public string? StoredPath { get; set; }

	/// <summary>隔离前的 SHA256，用于恢复后逐字节核对。</summary>
	public string Sha256 { get; set; } = string.Empty;

	public long SizeBytes { get; set; }

	/// <summary>quarantined | missing | restored | already_restored | conflict | failed</summary>
	public string State { get; set; } = string.Empty;

	public string? Error { get; set; }
}
