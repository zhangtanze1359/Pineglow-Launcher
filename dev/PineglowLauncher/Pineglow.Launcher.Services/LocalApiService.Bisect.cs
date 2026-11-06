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
/// 二分定位：操作与步骤执行。
/// <para>
/// 二分定位（逐个禁用 Mod 找出冲突源）的会话操作与单步执行。
/// 同一实例的二分请求**串行化**：上一次还在处理时直接拒绝，而不是排队。
/// </para>
/// </summary>
public static partial class LocalApiService
{
	/// <summary>MR06：同一实例的二分定位请求串行化——上一次还在处理时直接拒绝，而不是排队。</summary>
	private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte> BisectInFlight =
		new System.Collections.Concurrent.ConcurrentDictionary<string, byte>(StringComparer.OrdinalIgnoreCase);

	private static bool TryBeginBisectOperation(string instanceId) => BisectInFlight.TryAdd(instanceId, 0);

	private static void EndBisectOperation(string instanceId) => BisectInFlight.TryRemove(instanceId, out _);

	internal static bool IsBisectOperationInFlightForTest(string instanceId) => BisectInFlight.ContainsKey(instanceId);

	internal static bool TryBeginBisectOperationForTest(string instanceId) => TryBeginBisectOperation(instanceId);

	internal static void EndBisectOperationForTest(string instanceId) => EndBisectOperation(instanceId);

	/// <summary>
	/// MR06：二分定位进行中时禁止对该实例手动改 Mod —— 两套写操作同时改名会让候选集合与磁盘不一致。
	/// </summary>
	private static void RejectManualModEditDuringBisect(string instanceId)
	{
		ModBisectSession? session = ModBisectService.Get(instanceId);
		if (session != null && !session.Finished)
		{
			throw new InvalidOperationException("该实例正在进行 Mod 二分定位，请先结束或放弃定位，再手动修改 Mod。");
		}
	}

	private static async Task HandleBisectStartAsync(HttpListenerResponse response, string instanceId)
	{
		if (!TryBeginBisectOperation(instanceId))
		{
			await WriteJsonAsync(response, 409, new { ok = false, error = "该实例正在处理上一个二分定位请求，请稍后再试。" });
			return;
		}

		try
		{
			AppConfig config = LoadConfig();
			GameInstance instance = RequireInstance(config, instanceId);
			string gameDir = ResolveGameDirectory(config, instance);
			string modsDir = Path.Combine(gameDir, "mods");
			ModService service = new ModService(gameDir);
			List<ModFile> allMods = await service.GetModsAsync(instance);
			List<ModFile> enabled = allMods.Where(x => x.IsEnabled).ToList();
			if (enabled.Count == 0)
			{
				throw new InvalidOperationException("当前没有启用的 Mod，无法开始二分定位。");
			}

			// MR07：有未完成的落盘记录时先恢复/继续，不允许直接开新一轮把它覆盖掉。
			BisectRecoveryRecord? pending = await CreateBisectStore().LoadAsync(instanceId);
			if (pending != null)
			{
				await WriteJsonAsync(response, 409, new
				{
					ok = false,
					error = "该实例有未完成的二分定位会话，请先选择「恢复原状态」或「继续上一有效轮」。",
					pending = true,
					sessionId = pending.SessionId,
					status = pending.Status
				});
				return;
			}

			// MR06：上一轮的文件操作没恢复完就不许开新一轮，否则会把未恢复的文件当成"新基线"。
			ModBisectSession? existing = ModBisectService.Get(instanceId);
			if (existing != null && existing.NeedsRecovery)
			{
				await WriteJsonAsync(response, 409, new
				{
					ok = false,
					error = "上一次定位的文件操作尚未恢复完成，请先「放弃」并确认恢复。",
					needsRecovery = true,
					recoveryError = existing.RecoveryError,
					files = existing.LastResults.Select(r => new { r.FileName, r.State, r.Error })
				});
				return;
			}

			ModBisectSession session = ModBisectService.GetOrStart(instanceId, enabled.Select(x => x.FileName));

			// MR07：先评估依赖有效性，再落盘禁用集合——依赖被破坏的一轮是无效测试。
			IReadOnlyList<string> half = session.DisableHalfForNextTest();
			(string validity, List<string> issues) = EvaluateRoundValidity(allMods, half);
			session.SetRoundValidity(validity, issues);
			if (session.RoundInvalid)
			{
				await PersistBisectAsync(instanceId, session, gameDir, modsDir);
				await WriteJsonAsync(response, 409, new
				{
					ok = false,
					roundInvalid = true,
					roundValidity = validity,
					issues,
					error = "本轮会禁用仍然启用的 Mod 的前置，属于无效测试，已中止。"
				});
				return;
			}

			List<ModBisectFileResult> results = ApplyBisectStep(service, session, modsDir, half);
			session.RecordStepResults(results);
			await PersistBisectAsync(instanceId, session, gameDir, modsDir);
			await RecordAppliedRoundAsync(instanceId, session.Step, half, null, session.RoundValidity, session.RoundIssues);

			if (session.NeedsRecovery)
			{
				await WriteBisectFailureAsync(response, session, "禁用半区失败，本轮未就绪。");
				return;
			}

			await WriteBisectSessionAsync(response, session);
		}
		finally
		{
			EndBisectOperation(instanceId);
		}
	}

	private static async Task HandleBisectTestAsync(HttpListenerRequest request, HttpListenerResponse response, string instanceId)
	{
		if (!TryBeginBisectOperation(instanceId))
		{
			await WriteJsonAsync(response, 409, new { ok = false, error = "该实例正在处理上一个二分定位请求，请稍后再试。" });
			return;
		}

		try
		{
			AppConfig config = LoadConfig();
			GameInstance instance = RequireInstance(config, instanceId);
			string gameDir = ResolveGameDirectory(config, instance);
			ModService service = new ModService(gameDir);
			string modsDir = Path.Combine(gameDir, "mods");
			List<ModFile> allMods = await service.GetModsAsync(instance);
			ModBisectSession session = ModBisectService.Get(instanceId)
				?? throw new InvalidOperationException("当前没有进行中的二分定位会话，请先开始。");

			JsonDocument body = await ReadBodyAsync(request);
			bool crashed = body.RootElement.TryGetProperty("crashed", out JsonElement crashedEl) && crashedEl.ValueKind == JsonValueKind.True;
			// MR07：先记下"这一轮问的是哪一轮"，反馈要落到对应轮次上。
			int answeredStep = session.Step;
			List<string> answeredHalf = session.DisabledHalf.ToList();

			// MR06：客户端应当带上它所回答的轮次；轮次对不上说明该轮已被推进（例如双击提交），
			// 直接拒绝而不是把它当成下一轮的答案——否则会错误收窄候选。
			if (body.RootElement.TryGetProperty("step", out JsonElement stepEl)
				&& stepEl.ValueKind == JsonValueKind.Number
				&& stepEl.TryGetInt32(out int clientStep)
				&& clientStep != session.Step)
			{
				await WriteJsonAsync(response, 409, new
				{
					ok = false,
					error = "该轮已经推进，请用最新一轮的状态重新提交。",
					step = session.Step,
					version = session.Version,
					waitingForReport = session.WaitingForReport,
					finished = session.Finished
				});
				return;
			}

			if (!session.TryReportTest(crashed, out string? rejected))
			{
				await WriteJsonAsync(response, 409, new
				{
					ok = false,
					error = rejected,
					needsRecovery = session.NeedsRecovery,
					recoveryError = session.RecoveryError,
					step = session.Step,
					waitingForReport = session.WaitingForReport,
					finished = session.Finished
				});
				return;
			}

			await RecordAppliedRoundAsync(instanceId, answeredStep, answeredHalf, crashed, session.RoundValidity, session.RoundIssues);

			if (!session.Finished)
			{
				IReadOnlyList<string> half = session.DisableHalfForNextTest();
				(string validity, List<string> issues) = EvaluateRoundValidity(allMods, half);
				session.SetRoundValidity(validity, issues);
				if (session.RoundInvalid)
				{
					await PersistBisectAsync(instanceId, session, gameDir, modsDir);
					await WriteJsonAsync(response, 409, new
					{
						ok = false,
						roundInvalid = true,
						roundValidity = validity,
						issues,
						error = "下一轮会禁用仍然启用的 Mod 的前置，属于无效测试，已中止。"
					});
					return;
				}

				List<ModBisectFileResult> results = ApplyBisectStep(service, session, modsDir, half);
				session.RecordStepResults(results);
				await PersistBisectAsync(instanceId, session, gameDir, modsDir);
				await RecordAppliedRoundAsync(instanceId, session.Step, half, null, session.RoundValidity, session.RoundIssues);
				if (session.NeedsRecovery)
				{
					await WriteBisectFailureAsync(response, session, "禁用半区失败，本轮未就绪。");
					return;
				}
			}
			else
			{
				List<ModBisectFileResult> restoreResults = RestoreBisectMods(service, modsDir, session);
				session.RecordRestoreResults(restoreResults);
				await PersistBisectAsync(instanceId, session, gameDir, modsDir);
				if (session.NeedsRecovery)
				{
					// 恢复没核对通过：保留会话与落盘记录，不把"完成"当成结束。
					await WriteBisectFailureAsync(response, session, "定位结束但恢复未完成，会话已保留。");
					return;
				}

				ModBisectService.Remove(instanceId);
				await CreateBisectStore().ClearAsync(instanceId);
			}

			await WriteBisectSessionAsync(response, session);
		}
		finally
		{
			EndBisectOperation(instanceId);
		}
	}

	private static async Task HandleBisectGiveUpAsync(HttpListenerResponse response, string instanceId)
	{
		if (!TryBeginBisectOperation(instanceId))
		{
			await WriteJsonAsync(response, 409, new { ok = false, error = "该实例正在处理上一个二分定位请求，请稍后再试。" });
			return;
		}

		try
		{
			AppConfig config = LoadConfig();
			GameInstance instance = RequireInstance(config, instanceId);
			string gameDir = ResolveGameDirectory(config, instance);
			ModService service = new ModService(gameDir);
			ModBisectSession session = ModBisectService.Get(instanceId)
				?? throw new InvalidOperationException("当前没有进行中的二分定位会话，请先开始。");

			string modsDir = Path.Combine(gameDir, "mods");
			session.GiveUp();
			List<ModBisectFileResult> results = RestoreBisectMods(service, modsDir, session);
			session.RecordRestoreResults(results);
			// MR07：恢复结果落盘——即使恢复失败，重启后也能从记录里继续恢复。
			await PersistBisectAsync(instanceId, session, gameDir, modsDir);

			if (session.NeedsRecovery)
			{
				// 恢复失败必须保留会话与落盘记录：否则恢复依据随会话一起消失，用户再也看不到哪些文件没回来。
				await WriteBisectFailureAsync(response, session, "恢复未完成，会话已保留以便重试。");
				return;
			}

			ModBisectService.Remove(instanceId);
			await CreateBisectStore().ClearAsync(instanceId);
			await WriteBisectSessionAsync(response, session);
		}
		finally
		{
			EndBisectOperation(instanceId);
		}
	}

	private static async Task WriteBisectFailureAsync(HttpListenerResponse response, ModBisectSession session, string message)
	{
		await WriteJsonAsync(response, 409, new
		{
			ok = false,
			error = string.IsNullOrWhiteSpace(session.RecoveryError) ? message : message + " " + session.RecoveryError,
			needsRecovery = session.NeedsRecovery,
			recoveryError = session.RecoveryError,
			finished = session.Finished,
			version = session.Version,
			step = session.Step,
			waitingForReport = session.WaitingForReport,
			files = session.LastResults.Select(r => new { r.FileName, r.State, r.Error })
		});
	}

	/// <summary>
	/// MR06：按会话指令禁用指定 Mod 半区，返回**逐文件结果**（applied/unchanged/failed）。
	/// 不再吞掉异常：失败带原因返回，由调用方决定是否冻结推进。
	/// </summary>
	private static List<ModBisectFileResult> ApplyBisectStep(ModService service, ModBisectSession session, string modsDir, IReadOnlyList<string> disableNames)
	{
		var results = new List<ModBisectFileResult>();
		if (session.Finished || disableNames.Count == 0)
		{
			return results;
		}

		foreach (string name in disableNames)
		{
			var result = new ModBisectFileResult { FileName = name };
			try
			{
				// 会话存的是 .jar 基准名；当前盘上可能已是 .jar.disabled
				string jarPath = Path.Combine(modsDir, name);
				string disabledPath = jarPath + ".disabled";
				if (File.Exists(jarPath))
				{
					service.DisableMod(new ModFile { FullPath = jarPath, FileName = name, IsEnabled = true });
					if (File.Exists(disabledPath) && !File.Exists(jarPath))
					{
						result.State = "applied";
					}
					else
					{
						result.State = "failed";
						result.Error = "禁用后文件未按预期改名";
					}
				}
				else if (File.Exists(disabledPath))
				{
					result.State = "unchanged"; // 已是禁用态，幂等
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
				LauncherLog.Error("[Bisect] disable " + name + " failed: " + ex.Message, ex);
			}

			results.Add(result);
		}

		return results;
	}
}
