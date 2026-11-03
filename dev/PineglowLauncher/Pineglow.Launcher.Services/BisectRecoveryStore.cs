using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace Pineglow.Launcher.Services;

/// <summary>MR07：一次二分定位会话的落盘快照。用于进程重启后恢复或明确列出冲突。</summary>
public sealed class BisectRecoveryRecord
{
	public string SessionId { get; set; } = string.Empty;

	public string InstanceId { get; set; } = string.Empty;

	/// <summary>会话开始时的游戏目录；恢复/继续前必须核对它没有变。</summary>
	public string GameDirectory { get; set; } = string.Empty;

	public DateTimeOffset CreatedAt { get; set; }

	public DateTimeOffset UpdatedAt { get; set; }

	/// <summary>active | needsRecovery | finished</summary>
	public string Status { get; set; } = "active";

	/// <summary>会话开始时**已启用**的文件（只有这些文件属于本会话管理范围）。</summary>
	public List<string> EnabledAtStart { get; set; } = new List<string>();

	/// <summary>开始时每个启用文件的基线（含 SHA256），恢复时以此核对。</summary>
	public List<BisectBaselineFile> Baseline { get; set; } = new List<BisectBaselineFile>();

	/// <summary>已应用过的轮次（逐轮记录禁用了哪些文件、用户反馈与有效性）。</summary>
	public List<BisectAppliedRound> Rounds { get; set; } = new List<BisectAppliedRound>();

	public List<string> Candidates { get; set; } = new List<string>();

	public int Step { get; set; }

	public List<BisectFileResultRecord> LastResults { get; set; } = new List<BisectFileResultRecord>();
}

/// <summary>会话开始时的文件基线。</summary>
public sealed class BisectBaselineFile
{
	public string FileName { get; set; } = string.Empty;

	public bool Enabled { get; set; }

	public string Sha256 { get; set; } = string.Empty;

	public long SizeBytes { get; set; }
}

/// <summary>一轮已应用的禁用操作。</summary>
public sealed class BisectAppliedRound
{
	public int Step { get; set; }

	public List<string> Disabled { get; set; } = new List<string>();

	public bool? Crashed { get; set; }

	/// <summary>ok | limited | invalid</summary>
	public string Validity { get; set; } = "ok";

	public List<string> Issues { get; set; } = new List<string>();
}

/// <summary>逐文件操作结果（落盘版）。</summary>
public sealed class BisectFileResultRecord
{
	public string FileName { get; set; } = string.Empty;

	public string State { get; set; } = string.Empty;

	public string? Error { get; set; }
}

/// <summary>
/// MR07：把二分定位会话与恢复依据落到**游戏目录之外**，进程中断/重启后仍能：
///   - 明确列出磁盘与记录的差异（冲突），而不是靠文件名猜；
///   - 把本会话管理的文件恢复到原状态；
///   - 从上一有效轮继续。
/// 写入是原子的（先写 .tmp 再替换），避免半截记录。
/// </summary>
public sealed class BisectRecoveryStore
{
	private static readonly JsonSerializerOptions RecordJsonOptions = new JsonSerializerOptions
	{
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
		WriteIndented = true
	};

	private readonly string _root;

	public BisectRecoveryStore(string root)
	{
		if (string.IsNullOrWhiteSpace(root))
		{
			throw new InvalidOperationException("二分定位恢复根不能为空");
		}

		_root = Path.GetFullPath(root);
	}

	public string Root => _root;

	public string GetRecordPath(string instanceId) => Path.Combine(_root, SafeSegment(instanceId) + ".json");

	public async Task SaveAsync(BisectRecoveryRecord record)
	{
		Directory.CreateDirectory(_root);
		record.UpdatedAt = DateTimeOffset.UtcNow;
		string path = GetRecordPath(record.InstanceId);
		string temp = path + ".tmp";
		await File.WriteAllTextAsync(temp, JsonSerializer.Serialize(record, RecordJsonOptions)).ConfigureAwait(false);
		File.Move(temp, path, overwrite: true);
	}

	public Task<BisectRecoveryRecord?> LoadAsync(string instanceId)
	{
		string path = GetRecordPath(instanceId);
		if (!File.Exists(path))
		{
			return Task.FromResult<BisectRecoveryRecord?>(null);
		}

		try
		{
			BisectRecoveryRecord? record = JsonSerializer.Deserialize<BisectRecoveryRecord>(
				File.ReadAllText(path), RecordJsonOptions);
			// 记录必须自洽：实例 Id 对不上就当作不可读，避免张冠李戴。
			if (record == null || !string.Equals(record.InstanceId, instanceId, StringComparison.OrdinalIgnoreCase))
			{
				return Task.FromResult<BisectRecoveryRecord?>(null);
			}

			return Task.FromResult<BisectRecoveryRecord?>(record);
		}
		catch
		{
			return Task.FromResult<BisectRecoveryRecord?>(null);
		}
	}

	public Task ClearAsync(string instanceId)
	{
		string path = GetRecordPath(instanceId);
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

		return Task.CompletedTask;
	}

	private static string SafeSegment(string value)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			throw new InvalidOperationException("实例 Id 不能为空");
		}

		string cleaned = string.Empty;
		foreach (char c in value)
		{
			if (char.IsLetterOrDigit(c) || c == '-' || c == '_')
			{
				cleaned += c;
			}
		}

		if (cleaned.Length == 0)
		{
			throw new InvalidOperationException("实例 Id 含非法字符：" + value);
		}

		return cleaned;
	}
}
