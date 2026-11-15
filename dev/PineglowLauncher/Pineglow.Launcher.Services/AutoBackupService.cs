using System;
using System.Collections.Generic;
using System.Linq;
using Pineglow.Launcher.Models;

namespace Pineglow.Launcher.Services;

/// <summary>
/// D3② 的执行结果。`FailedFolders` 里出现的存档**一个都没成功**，其余存档照常备份（单条失败不中断）。
/// </summary>
public sealed record AutoBackupOutcome(
	bool Ran,
	IReadOnlyList<string> BackedUpPaths,
	IReadOnlyList<string> FailedFolders,
	string Reason);

/// <summary>
/// D3② 的 **IO 面**：按 <see cref="AutoBackupPlanner"/> 的决定真的去写备份。
///
/// 安全边界（三条都写在这里，免得以后有人"顺手优化"掉）：
///   1. 这里**只读存档**，绝不 move/delete 源存档——备份失败也不能让用户的存档更危险；
///   2. 单个存档失败不抛出，只记进 <see cref="AutoBackupOutcome.FailedFolders"/>，其余继续；
///   3. 本方法可能抛异常（例如列出存档就失败），**调用方（退出钩子）必须自己兜住**，不许让异常
///      影响游戏进程退出路径。
/// </summary>
public static class AutoBackupService
{
	private const string LogPrefix = "[auto-backup]";

	public static AutoBackupOutcome TryBackupOnExit(
		AppConfig? config,
		string instanceId,
		string gameDirectory,
		DateTimeOffset now,
		Action<string>? log = null)
	{
		GameInstance? instance = config?.Instances?.FirstOrDefault(x => x.Id == instanceId);
		if (instance == null)
		{
			return new AutoBackupOutcome(false, Array.Empty<string>(), Array.Empty<string>(), "instance-not-found");
		}

		List<SaveInfo> saves;
		try
		{
			saves = new SaveService(gameDirectory).GetSaves(instance);
		}
		catch (Exception ex)
		{
			log?.Invoke($"{LogPrefix} 列出存档失败：{ex.GetType().Name} {ex.Message}");
			return new AutoBackupOutcome(false, Array.Empty<string>(), Array.Empty<string>(), "list-failed");
		}

		AutoBackupPlan plan = AutoBackupPlanner.Plan(instance.AutoBackupOnExit, saves, now);
		if (!plan.ShouldRun)
		{
			return new AutoBackupOutcome(false, Array.Empty<string>(), Array.Empty<string>(), plan.Reason);
		}

		SaveBackupService backupService = new SaveBackupService(gameDirectory);
		List<string> backedUp = new List<string>();
		List<string> failed = new List<string>();
		foreach (string folder in plan.TargetFolders)
		{
			SaveInfo? save = saves.FirstOrDefault(s => string.Equals(s.FolderName, folder, StringComparison.Ordinal));
			if (save == null)
			{
				failed.Add(folder);
				continue;
			}

			try
			{
				string path = backupService.CreateBackup(instance, save);
				backedUp.Add(path);
				log?.Invoke($"{LogPrefix} 已备份 {folder} → {path}");
			}
			catch (Exception ex)
			{
				failed.Add(folder);
				log?.Invoke($"{LogPrefix} {folder} 备份失败：{ex.GetType().Name} {ex.Message}");
			}
		}

		return new AutoBackupOutcome(true, backedUp, failed, failed.Count == 0 ? "ok" : "partial");
	}
}
