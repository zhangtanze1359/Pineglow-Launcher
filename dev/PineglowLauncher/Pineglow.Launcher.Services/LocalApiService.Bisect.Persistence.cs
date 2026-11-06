using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Pineglow.Launcher.Models;

namespace Pineglow.Launcher.Services;

/// <summary>
/// 二分定位：落盘、恢复与依赖校验。
/// <para>
/// 二分定位会话的持久化与跨重启恢复：把每轮禁用集合与用户反馈写成可恢复记录，
/// 并在恢复时校验有效性（依赖缺失、Mod 已变更等）。
/// </para>
/// </summary>
public static partial class LocalApiService
{
	/// <summary>
	/// MR06：把**本次会话禁用过**的 Mod 恢复为启用态，返回逐文件结果。
	/// 只遍历 <see cref="ModBisectSession.EnabledAtStart"/>，因此用户原本就禁用的 Mod 不会被误启用。
	/// </summary>
	private static List<ModBisectFileResult> RestoreBisectMods(ModService service, string modsDir, ModBisectSession session)
	{
		var results = new List<ModBisectFileResult>();
		foreach (string name in session.EnabledAtStart)
		{
			var result = new ModBisectFileResult { FileName = name };
			try
			{
				string jarPath = Path.Combine(modsDir, name);
				string disabledPath = jarPath + ".disabled";
				if (File.Exists(jarPath) && !File.Exists(disabledPath))
				{
					result.State = "unchanged"; // 本来就是启用态
				}
				else if (File.Exists(disabledPath) && !File.Exists(jarPath))
				{
					service.EnableMod(new ModFile { FullPath = disabledPath, FileName = name + ".disabled", IsEnabled = false });
					if (File.Exists(jarPath) && !File.Exists(disabledPath))
					{
						result.State = "applied";
					}
					else
					{
						result.State = "failed";
						result.Error = "启用后文件未按预期改名";
					}
				}
				else if (File.Exists(jarPath) && File.Exists(disabledPath))
				{
					result.State = "failed";
					result.Error = "启用态与禁用态文件同时存在，无法确定原状";
				}
				else
				{
					result.State = "failed";
					result.Error = "文件既不在启用态也不在禁用态";
				}
			}
			catch (Exception ex)
			{
				result.State = "failed";
				result.Error = ex.Message;
				LauncherLog.Error("[Bisect] restore " + name + " failed: " + ex.Message, ex);
			}

			results.Add(result);
		}

		return results;
	}

	/* ================= MR07：二分定位的落盘、恢复与依赖校验 ================= */

	/// <summary>
	/// MR07：把当前会话落盘（原子写入）。首次落盘记录基线 SHA 与启用状态；
	/// 之后只更新可变字段，保持"会话开始时"的事实不变。
	/// </summary>
	private static async Task<BisectRecoveryRecord> PersistBisectAsync(
		string instanceId, ModBisectSession session, string gameDir, string modsDir)
	{
		BisectRecoveryStore store = CreateBisectStore();
		BisectRecoveryRecord? record = await store.LoadAsync(instanceId);
		if (record == null || !string.Equals(record.SessionId, session.SessionId, StringComparison.OrdinalIgnoreCase))
		{
			record = new BisectRecoveryRecord
			{
				SessionId = session.SessionId,
				InstanceId = instanceId,
				GameDirectory = Path.GetFullPath(gameDir),
				CreatedAt = DateTimeOffset.UtcNow,
				EnabledAtStart = session.EnabledAtStart.ToList(),
				Baseline = new List<BisectBaselineFile>()
			};

			foreach (string name in session.EnabledAtStart)
			{
				string jarPath = Path.Combine(modsDir, name);
				bool exists = File.Exists(jarPath);
				record.Baseline.Add(new BisectBaselineFile
				{
					FileName = name,
					Enabled = true,
					Sha256 = exists ? FileRecoveryService.HashFile(jarPath) : string.Empty,
					SizeBytes = exists ? new FileInfo(jarPath).Length : 0
				});
			}
		}

		record.Status = session.NeedsRecovery ? "needsRecovery" : (session.Finished ? "finished" : "active");
		record.Candidates = session.Candidates.ToList();
		record.Step = session.Step;
		record.LastResults = session.LastResults
			.Select(r => new BisectFileResultRecord { FileName = r.FileName, State = r.State, Error = r.Error })
			.ToList();
		await store.SaveAsync(record);
		return record;
	}

	/// <summary>把一轮禁用集合与用户反馈追加到落盘记录（同一步只保留一条）。</summary>
	private static async Task RecordAppliedRoundAsync(
		string instanceId, int step, IReadOnlyList<string> disabled, bool? crashed, string validity, IReadOnlyList<string> issues)
	{
		BisectRecoveryStore store = CreateBisectStore();
		BisectRecoveryRecord? record = await store.LoadAsync(instanceId);
		if (record == null)
		{
			return;
		}

		BisectAppliedRound? round = record.Rounds.FirstOrDefault(r => r.Step == step);
		if (round == null)
		{
			round = new BisectAppliedRound { Step = step, Disabled = disabled.ToList(), Validity = validity, Issues = issues.ToList() };
			record.Rounds.Add(round);
		}

		if (crashed.HasValue)
		{
			round.Crashed = crashed;
		}

		await store.SaveAsync(record);
	}

	/// <summary>
	/// MR07：禁用某一半区之前的依赖检查。
	/// 如果某个**仍然启用**的 Mod 依赖了本轮要禁用的 Mod（按 jar manifest 的 mod id 映射），
	/// 那么这一轮禁用的后果是"缺前置崩溃"，与原始崩溃不是同一件事 → 本轮无效。
	/// </summary>
	private static (string Validity, List<string> Issues) EvaluateRoundValidity(
		IReadOnlyList<ModFile> allMods, IReadOnlyList<string> disableNames)
	{
		var issues = new List<string>();
		if (disableNames.Count == 0)
		{
			return ("ok", issues);
		}

		var idToFile = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		foreach (ModFile mod in allMods)
		{
			if (!string.IsNullOrWhiteSpace(mod.ModId) && !idToFile.ContainsKey(mod.ModId))
			{
				idToFile[mod.ModId] = mod.FileName;
			}
		}

		var disabling = new HashSet<string>(disableNames, StringComparer.OrdinalIgnoreCase);
		bool dependencyBroken = false;
		bool unverifiable = false;

		foreach (ModFile mod in allMods)
		{
			if (disabling.Contains(mod.FileName))
			{
				continue; // 本轮被禁用的文件不构成"仍然启用的前提"
			}

			if (string.IsNullOrWhiteSpace(mod.ModId))
			{
				unverifiable = true;
				continue;
			}

			foreach (ModManifestDependency dep in mod.Dependencies.Where(d => d.IsRequired))
			{
				if (idToFile.TryGetValue(dep.ModId, out string? provider) && disabling.Contains(provider))
				{
					dependencyBroken = true;
					issues.Add($"{mod.FileName} 依赖前置 {dep.ModId}（{provider}），但本轮会禁用后者");
				}
			}
		}

		if (unverifiable)
		{
			issues.Add("部分仍然启用的 Mod 缺少 manifest 元数据，无法完整核对依赖");
		}

		if (dependencyBroken)
		{
			return ("invalid", issues);
		}

		return (issues.Count > 0 ? "limited" : "ok", issues);
	}

	/// <summary>
	/// MR07：把落盘记录与磁盘现状逐项比对。
	/// 只看**本会话管理过的文件**，不从文件名猜状态，也不碰用户后来加入的文件。
	/// </summary>
	private static List<object> BuildBisectConflicts(BisectRecoveryRecord record, string modsDir)
	{
		var conflicts = new List<object>();
		var expectedDisabled = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (BisectAppliedRound round in record.Rounds)
		{
			foreach (string name in round.Disabled)
			{
				expectedDisabled.Add(name);
			}
		}

		bool expectRestored = string.Equals(record.Status, "finished", StringComparison.OrdinalIgnoreCase);

		foreach (string name in record.EnabledAtStart)
		{
			string jarPath = Path.Combine(modsDir, name);
			string disabledPath = jarPath + ".disabled";
			bool hasEnabled = File.Exists(jarPath);
			bool hasDisabled = File.Exists(disabledPath);

			string actual = (hasEnabled && hasDisabled) ? "ambiguous"
				: hasEnabled ? "enabled"
				: hasDisabled ? "disabled"
				: "missing";
			string expected = (!expectRestored && expectedDisabled.Contains(name)) ? "disabled" : "enabled";

			if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
			{
				conflicts.Add(new
				{
					fileName = name,
					expected,
					actual,
					detail = actual == "ambiguous"
						? "启用态与禁用态文件同时存在，无法确定原状"
						: $"期望 {expected}，实际 {actual}"
				});
			}
		}

		return conflicts;
	}

	/// <summary>MR07：查询该实例是否有未完成的二分定位会话（只读）。</summary>
	private static async Task HandleBisectPendingAsync(HttpListenerResponse response, string instanceId)
	{
		AppConfig config = LoadConfig();
		GameInstance instance = RequireInstance(config, instanceId);

		// MR03 收尾之二（缺口登记表 A7）：同一实例的**并发启动守卫**。
		// 修复前这里从不检查运行态，`MarkRunning` 会"以最后一次为准"覆盖注册表 ——
		// 于是同一实例能起两个游戏进程同时写同一份存档（真实的损坏风险），
		// 而删除守卫此后只跟踪最新的那个 PID。
		// 位置刻意放在最前：在任何网络调用/进程派发**之前**就拒绝。
		if (RunningInstanceRegistry.IsRunning(instanceId, out int alreadyRunningPid))
		{
			throw new InvalidOperationException($"该实例已在运行（进程 {alreadyRunningPid}），请勿重复启动。");
		}

		string gameDir = ResolveGameDirectory(config, instance);

		BisectRecoveryRecord? record = await CreateBisectStore().LoadAsync(instanceId);
		if (record == null)
		{
			await WriteJsonAsync(response, 200, new { ok = true, pending = false });
			return;
		}

		bool dirChanged = !string.Equals(
			Path.GetFullPath(record.GameDirectory), Path.GetFullPath(gameDir), StringComparison.OrdinalIgnoreCase);
		List<object> conflicts = dirChanged
			? new List<object>
			{
				new { fileName = "(实例目录)", expected = record.GameDirectory, actual = gameDir, detail = "实例的游戏目录与会话开始时不一致，拒绝按该记录恢复" }
			}
			: BuildBisectConflicts(record, Path.Combine(gameDir, "mods"));

		await WriteJsonAsync(response, 200, new
		{
			ok = true,
			pending = true,
			sessionId = record.SessionId,
			status = record.Status,
			gameDirectory = record.GameDirectory,
			createdAt = record.CreatedAt,
			updatedAt = record.UpdatedAt,
			step = record.Step,
			enabledAtStart = record.EnabledAtStart,
			candidates = record.Candidates,
			rounds = record.Rounds.Select(r => new { r.Step, r.Disabled, r.Crashed, r.Validity, r.Issues }),
			lastResults = record.LastResults.Select(r => new { r.FileName, r.State, r.Error }),
			conflicts,
			canRestore = !dirChanged,
			canContinue = !dirChanged && conflicts.Count == 0
				&& !string.Equals(record.Status, "finished", StringComparison.OrdinalIgnoreCase)
		});
	}

	/// <summary>
	/// MR07：重启后处理未完成的会话。
	///   restore  —— 只把本会话开始时启用的文件恢复为启用态（不启用用户原本禁用的 Mod、不删除新加入的文件）；
	///   continue —— 磁盘与记录逐项一致时才按记录重建会话，从上一有效轮继续。
	/// </summary>
	private static async Task HandleBisectResumeAsync(HttpListenerRequest request, HttpListenerResponse response, string instanceId)
	{
		JsonDocument body = await ReadBodyAsync(request);
		string action = (JsonUtil.GetString(body.RootElement, "action") ?? "restore").Trim().ToLowerInvariant();

		AppConfig config = LoadConfig();
		GameInstance instance = RequireInstance(config, instanceId);

		// MR03 收尾之二（缺口登记表 A7）：同一实例的**并发启动守卫**。
		// 修复前这里从不检查运行态，`MarkRunning` 会"以最后一次为准"覆盖注册表 ——
		// 于是同一实例能起两个游戏进程同时写同一份存档（真实的损坏风险），
		// 而删除守卫此后只跟踪最新的那个 PID。
		// 位置刻意放在最前：在任何网络调用/进程派发**之前**就拒绝。
		if (RunningInstanceRegistry.IsRunning(instanceId, out int alreadyRunningPid))
		{
			throw new InvalidOperationException($"该实例已在运行（进程 {alreadyRunningPid}），请勿重复启动。");
		}

		string gameDir = ResolveGameDirectory(config, instance);
		string modsDir = Path.Combine(gameDir, "mods");

		BisectRecoveryStore store = CreateBisectStore();
		BisectRecoveryRecord? record = await store.LoadAsync(instanceId);
		if (record == null)
		{
			throw new InvalidOperationException("没有未完成的二分定位记录。");
		}

		if (!string.Equals(Path.GetFullPath(record.GameDirectory), Path.GetFullPath(gameDir), StringComparison.OrdinalIgnoreCase))
		{
			await WriteJsonAsync(response, 409, new
			{
				ok = false,
				error = "实例的游戏目录与会话记录不一致，拒绝按该记录操作。",
				recordedDirectory = record.GameDirectory,
				currentDirectory = gameDir
			});
			return;
		}

		List<object> conflicts = BuildBisectConflicts(record, modsDir);

		if (action == "restore")
		{
			// 只处理 EnabledAtStart 里的文件：用户原本禁用的 Mod 保持禁用，后来加入的文件原样不动。
			ModService service = new ModService(gameDir);
			var results = new List<ModBisectFileResult>();
			foreach (string name in record.EnabledAtStart)
			{
				var result = new ModBisectFileResult { FileName = name };
				string jarPath = Path.Combine(modsDir, name);
				string disabledPath = jarPath + ".disabled";
				try
				{
					if (File.Exists(jarPath) && !File.Exists(disabledPath))
					{
						result.State = "unchanged";
					}
					else if (File.Exists(disabledPath) && !File.Exists(jarPath))
					{
						service.EnableMod(new ModFile { FullPath = disabledPath, FileName = name + ".disabled", IsEnabled = false });
						if (File.Exists(jarPath) && !File.Exists(disabledPath))
						{
							result.State = "applied";
						}
						else
						{
							result.State = "failed";
							result.Error = "启用后文件未按预期改名";
						}
					}
					else if (File.Exists(jarPath) && File.Exists(disabledPath))
					{
						result.State = "failed";
						result.Error = "启用态与禁用态文件同时存在";
					}
					else
					{
						result.State = "failed";
						result.Error = "文件既不在启用态也不在禁用态";
					}
				}
				catch (Exception ex)
				{
					result.State = "failed";
					result.Error = ex.Message;
				}

				results.Add(result);
			}

			bool ok = results.All(r => r.State != "failed");
			if (ok)
			{
				await store.ClearAsync(instanceId);
			}
			else
			{
				record.Status = "needsRecovery";
				record.LastResults = results
					.Select(r => new BisectFileResultRecord { FileName = r.FileName, State = r.State, Error = r.Error })
					.ToList();
				await store.SaveAsync(record);
			}

			await WriteJsonAsync(response, ok ? 200 : 409, new
			{
				ok,
				action,
				status = ok ? "restored" : "needsRecovery",
				files = results.Select(r => new { r.FileName, r.State, r.Error }),
				conflicts
			});
			return;
		}

		if (action == "continue")
		{
			if (conflicts.Count > 0)
			{
				await WriteJsonAsync(response, 409, new
				{
					ok = false,
					error = "磁盘状态与会话记录不一致，不能直接继续；请先按记录恢复。",
					conflicts
				});
				return;
			}

			BisectAppliedRound? last = record.Rounds.LastOrDefault();
			if (last == null)
			{
				await WriteJsonAsync(response, 409, new { ok = false, error = "记录里没有已应用的轮次，无法继续；请按记录恢复。" });
				return;
			}

			ModBisectSession restored = ModBisectSession.Restore(
				record.SessionId, instanceId, record.EnabledAtStart, record.Candidates, record.Step, last.Disabled);
			ModBisectService.Put(instanceId, restored);
			await WriteBisectSessionAsync(response, restored);
			return;
		}

		throw new InvalidOperationException("未知的恢复动作：" + action);
	}

	private static async Task WriteBisectSessionAsync(HttpListenerResponse response, ModBisectSession session)
	{
		await WriteJsonAsync(response, 200, new
		{
			ok = true,
			version = session.Version,
			sessionId = session.SessionId,
			roundInvalid = session.RoundInvalid,
			roundValidity = session.RoundValidity,
			roundIssues = session.RoundIssues,
			needsRecovery = session.NeedsRecovery,
			recoveryError = session.RecoveryError,
			lastResults = session.LastResults.Select(r => new { r.FileName, r.State, r.Error }),
			enabledAtStart = session.EnabledAtStart,
			candidates = session.Candidates,
			step = session.Step,
			waitingForReport = session.WaitingForReport,
			disabledHalf = session.DisabledHalf,
			culprit = session.Culprit,
			finished = session.Finished,
			finishReason = session.FinishReason
		});
	}
}
