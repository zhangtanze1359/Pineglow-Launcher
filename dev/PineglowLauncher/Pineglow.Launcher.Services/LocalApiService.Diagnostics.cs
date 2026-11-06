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
/// AI 诊断与诊断报告。
/// <para>
/// AI 诊断的主流程、取消端点，以及诊断报告的落盘、历史与导出。
/// 取消语义：客户端预生成 operationId，服务端只登记不生成；缺失或非法一律 400。
/// </para>
/// </summary>
public static partial class LocalApiService
{
	// Phase 3B v2 修复批次：AI 诊断操作注册表（per-request operationId → CTS）。
	private static readonly AiOperationRegistry AiOps = new AiOperationRegistry();

	// MR11：整合包导入的取消注册表。复用同一份带 TTL/容量/tombstone 的实现，
	// 用 ImportOperationScope 当作"实例维"的占位符（导入时实例还不存在）。
	private const string ImportOperationScope = "import";

	private static readonly AiOperationRegistry PackOps = new AiOperationRegistry();

	/// <summary>MR11 测试缝：让取消路由可以在没有真实导入在途的情况下被验证。</summary>
	internal static bool RegisterPackOperationForTest(string operationId, CancellationTokenSource cts) =>
		PackOps.Register(ImportOperationScope, operationId, cts);

	private static async Task HandleDiagnoseInstanceAsync(HttpListenerRequest request, HttpListenerResponse response, string instanceId)
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

		int exitCode = 0;
		bool confirmAiSend = false;
		bool localOnly = false;
		string operationId = string.Empty;
		string locale = "zh";
		try
		{
			JsonDocument body = await ReadBodyAsync(request);
			if (body.RootElement.TryGetProperty("exitCode", out JsonElement ec) && ec.ValueKind == JsonValueKind.Number)
			{
				exitCode = ec.GetInt32();
			}
			if (body.RootElement.TryGetProperty("confirmAiSend", out JsonElement confirm) && confirm.ValueKind == JsonValueKind.True)
			{
				confirmAiSend = confirm.GetBoolean();
			}
			if (body.RootElement.TryGetProperty("localOnly", out JsonElement local) && local.ValueKind == JsonValueKind.True)
			{
				localOnly = local.GetBoolean();
			}
			if (body.RootElement.TryGetProperty("operationId", out JsonElement op) && op.ValueKind == JsonValueKind.String)
			{
				operationId = op.GetString() ?? string.Empty;
			}
			if (body.RootElement.TryGetProperty("locale", out JsonElement loc) && loc.ValueKind == JsonValueKind.String)
			{
				string? locStr = loc.GetString();
				if (locStr == "zh" || locStr == "en")
				{
					locale = locStr;
				}
			}
			// Phase 3B v3 修复：服务端不再生成 operationId；缺失/非法必须由客户端预生成。
			if (string.IsNullOrEmpty(operationId) || operationId.Length < 8 || operationId.Length > 128)
			{
				await WriteJsonAsync(response, 400, new { ok = false, error = "operationId 缺失或非法；必须由客户端预生成（crypto.randomUUID）" });
				return;
			}
		}
		catch
		{
			// 无 body 或非法 body 时按 400 fail closed；客户端必须预生成 operationId。
			await WriteJsonAsync(response, 400, new { ok = false, error = "body 缺失或非法；必须由客户端预生成 operationId" });
			return;
		}

		// Phase 3B v2 修复 P0-4 / P0-1：每个 AI 诊断的取消通过 operationId 注册到 AiOps；
		// 服务端在 DiagnoseStructuredAsync、HTTP send、响应读取、retry delay 共享同一 linked token。
		// 客户端通过 POST /api/instances/{id}/diagnose/cancel 触发取消。
		// 不再依赖 request.InputStream.CanRead（已被独立验收 § B1 反证）。
		using CancellationTokenSource operationCts = new CancellationTokenSource();
		bool registered = AiOps.Register(instanceId, operationId, operationCts);
		if (!registered)
		{
			// 注册失败（容量满或重复 operationId）：直接返回错误，避免污染 registry
			await WriteJsonAsync(response, 400, new { ok = false, error = "operationId 已被占用/registry 满", operationId });
			return;
		}

		CancellationToken linkedToken = operationCts.Token;
		string tail = CrashAnalyzerService.ReadNewestDiagnosticTail(gameDir);
		CrashAnalysis rule;
		try
		{
			rule = CrashAnalyzerService.Analyze(gameDir, exitCode);
		}
		catch
		{
			rule = new CrashAnalysis(string.Empty, Array.Empty<string>(), null);
		}

		ModHealthReport mods = new ModHealthReport();
		List<string> installedModFiles = new List<string>();
		try
		{
			List<ModFile> modList = await new ModService(gameDir).GetModsAsync(instance);
			mods = ModHealthService.Analyze(instance, modList);
			installedModFiles.AddRange(modList.Select(m => m.FileName));
		}
		catch
		{
			// Mod 健康检查失败时不影响 AI 诊断
		}

		string? javaVersion = null;
		try
		{
			// Java metadata is supplementary; cap its cost and share the operation's cancellation.
			using var javaBudget = CancellationTokenSource.CreateLinkedTokenSource(linkedToken);
			javaBudget.CancelAfter(TimeSpan.FromSeconds(1));
			string javaPath = !string.IsNullOrWhiteSpace(instance.JavaPath) ? instance.JavaPath : config.JavaPath;
			JavaInfo javaInstallation;
			if (!string.IsNullOrWhiteSpace(javaPath))
			{
				var installation = await JavaLocator.ValidateInstallationAsync(javaPath, cancellationToken: javaBudget.Token);
				javaInstallation = installation == null ? null : new JavaInfo { Path = installation.JavaPath, VersionText = installation.VersionText, VersionMajor = installation.VersionMajor };
			}
			else javaInstallation = await JavaLocator.FindAsync(javaBudget.Token);
			javaVersion = javaInstallation?.VersionText ?? javaInstallation?.Path;
		}
		catch
		{
		}

		DiagnosticSnapshot snapshot = new DiagnosticSnapshot
		{
			Context = DiagnosticContextFactory.Create(
				Environment.OSVersion.VersionString,
				LauncherService.LauncherVersion,
				instance,
				javaVersion,
				exitCode,
				string.Empty,
				DateTimeOffset.UtcNow),
			EvidenceLog = tail,
		};

		AiProviderProfile aiProfile = config.AiProviders.FirstOrDefault(p => p.Id == config.SelectedAiProviderId);
		bool configured = config.AiDiagnosisEnabled && aiProfile != null && !string.IsNullOrWhiteSpace(aiProfile.BaseUrl);
		string? aiText = null;
		string? aiFailedMsg = null;
		AiDiagnoseResult? aiResult = null;
		// Phase 3C-2 P0 修复：真实发送元数据。
		// - 服务端配置快照（aiProfile.Name / aiProfile.Model）由 LocalApi 闭包注入，
		//   AiDiagnoseService 不感知；URL 永不进入 providerName。
		// - 仅当 AiDiagnoseService.SendOnceAsync 紧贴 http.SendAsync 之前触发回调时填 sentAtUtc；
		//   localOnly / 未确认 / endpoint 被拒 / 发送前取消 都不填。
		DateTimeOffset? dispatchSentAtUtc = null;
		string resolvedProviderName = aiProfile?.Name ?? string.Empty;
		string resolvedModel = string.IsNullOrWhiteSpace(aiProfile?.Model) ? "deepseek-chat" : aiProfile!.Model!;
		// Gate R2 修复 P0-2：localOnly 必须独立于 configured。未配置 provider、关闭 AI 开关、
		// endpoint 不在 allowlist 等情况下，UI 仍可请求「仅本地分析」并得到本地规则/Mod 健康结果。
		if (localOnly || configured)
		{
			try
			{
				string baseUrl = aiProfile?.BaseUrl ?? string.Empty;
				string apiKey = aiProfile?.ApiKey ?? string.Empty;
				// per-operation 闭包：捕获服务端配置快照的 providerName/model，
				// 仅记录服务端实际发起 SendAsync 的 UTC 时间戳。
				Action<AiDispatchAttempt> onFirstSendAttempt = attempt =>
				{
					dispatchSentAtUtc = attempt.SentAtUtc;
				};
				aiResult = await AiDiagnoseService.DiagnoseStructuredAsync(
					AiHttp, baseUrl, apiKey, resolvedModel,
					snapshot, rule, mods, linkedToken,
					confirmAiSend: confirmAiSend, localOnly: localOnly,
					locale: locale,
					onFirstSendAttempt: onFirstSendAttempt);
				if (!aiResult.Ok)
				{
					aiFailedMsg = aiResult.ErrorCode + ": " + aiResult.Summary;
				}
			}
			catch (Exception ex)
			{
				aiFailedMsg = ex.Message;
			}
		}

		// MR08：把本次诊断记成一份冻结结构的报告（只写本地、脱敏后入库）。
		// 历史写入失败绝不能影响诊断本身，因此单独 try/catch。
		await RecordDiagnosticReportAsync(
			config, instance, operationId, snapshot, rule, mods, aiResult, aiFailedMsg,
			dispatchSentAtUtc, resolvedProviderName, resolvedModel, configured, localOnly,
			operationCts.IsCancellationRequested, tail);

		try
		{
			await WriteJsonAsync(response, 200, new
			{
				ok = true,
				hasTail = !string.IsNullOrEmpty(tail),
				snapshot = new
				{
					version = DiagnosticSnapshot.Version,
					id = snapshot.SnapshotId,
					context = new
					{
						operatingSystem = snapshot.Context.OperatingSystem,
						launcherVersion = snapshot.Context.LauncherVersion,
						minecraftVersion = snapshot.Context.MinecraftVersion,
						loaderType = snapshot.Context.LoaderType,
						loaderVersion = snapshot.Context.LoaderVersion,
						javaVersion = snapshot.Context.JavaVersion,
						maxMemoryMb = snapshot.Context.MaxMemoryMb,
						customJvmArgs = DiagnosticJvmArgsRedactor.Redact(snapshot.Context.CustomJvmArgs),
						exitCode = snapshot.Context.ExitCode,
						logSource = snapshot.Context.LogSource,
						logModifiedAt = snapshot.Context.LogModifiedAt,
						missingInformation = snapshot.Context.MissingInformation
					}
				},
				rule = new { summary = rule.Summary, suggestions = rule.Suggestions },
				mods = new
				{
					mods.EnabledCount,
					mods.DisabledCount,
					mods.OnlineManagedCount,
					issues = mods.Issues.Select(i => new { i.Severity, i.Title, i.Description })
				},
				ai = aiText,
				aiResult = aiResult == null ? null : new
				{
					ok = aiResult.Ok,
					status = aiResult.Status,
					confidence = aiResult.Confidence,
					summary = aiResult.Summary,
					causes = aiResult.Causes,
					actions = aiResult.Actions,
					warnings = aiResult.Warnings,
					missingInformation = aiResult.MissingInformation,
					errorCode = aiResult.ErrorCode
				},
				aiError = aiFailedMsg,
				// Phase 3C-2 P0 修复：aiDispatch 三元组完全来自服务端配置快照。
				// providerName/model 永不接受 URL 或请求 body；sentAtUtc 仅在服务端 SendAsync 入口被触发时填。
				aiDispatch = dispatchSentAtUtc == null ? null : new
				{
					providerName = resolvedProviderName,
					model = resolvedModel,
					sentAtUtc = dispatchSentAtUtc.Value.ToString("o")
				},
				configured,
				suspectMods = installedModFiles.Count == 0
					? AiDiagnoseService.ExtractSuspectModFileNames(aiText)
					: AiDiagnoseService.NormalizeSuspectModFileNames(AiDiagnoseService.ExtractSuspectModFileNames(aiText), installedModFiles)
			});
		}
		finally
		{
			try { operationCts.Cancel(); } catch { }
		}
	}

	/// <summary>
	/// 显式 cancel endpoint：客户端发送 POST /api/instances/{id}/diagnose/cancel，body 含 operationId。
	/// 服务端在 AiOps 注册表中查找并 cancel 对应 CTS。重复取消幂等；不存在的 operationId 也返回确定性结果，不泄露其他实例/operation。
	/// </summary>
	private static async Task HandleCancelDiagnoseAsync(HttpListenerRequest request, HttpListenerResponse response, string instanceId)
	{
		string operationId = string.Empty;
		try
		{
			JsonDocument body = await ReadBodyAsync(request);
			if (body.RootElement.TryGetProperty("operationId", out JsonElement op) && op.ValueKind == JsonValueKind.String)
			{
				operationId = op.GetString() ?? string.Empty;
			}
		}
		catch
		{
			// 无 body 时 operationId 仍为空字符串
		}

		bool cancelled = false;
		if (!string.IsNullOrEmpty(operationId))
		{
			cancelled = AiOps.Cancel(instanceId, operationId);
		}

		await WriteJsonAsync(response, 200, new
		{
			ok = true,
			operationId,
			cancelled,
			message = cancelled ? "cancelled" : "no_active_operation",
		});
	}

	/* ================= MR08：诊断报告（历史 / 导出） ================= */

	/// <summary>
	/// MR08：把一次诊断落成**冻结结构**的报告。只碰本地文件；历史写入失败不影响诊断结果。
	/// ReportId 直接用 operationId —— 同一操作只记录一次。
	/// </summary>
	private static async Task RecordDiagnosticReportAsync(
		AppConfig config, GameInstance instance, string operationId, DiagnosticSnapshot snapshot,
		CrashAnalysis rule, ModHealthReport mods, AiDiagnoseResult? aiResult, string? aiFailedMsg,
		DateTimeOffset? dispatchSentAtUtc, string providerName, string model, bool configured, bool localOnly,
		bool wasCancelled, string rawTail)
	{
		if (string.IsNullOrWhiteSpace(operationId))
		{
			return;
		}

		try
		{
			var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
			string San(string? text)
			{
				string redacted = DiagnosticRedactor.Redact(text, out Dictionary<string, int> local);
				foreach (KeyValuePair<string, int> kv in local)
				{
					counts[kv.Key] = counts.TryGetValue(kv.Key, out int prev) ? prev + kv.Value : kv.Value;
				}

				return redacted;
			}

			var report = new DiagnosticReport
			{
				ReportId = operationId,
				InstanceId = instance.Id,
				InstanceName = San(instance.Name),
				CreatedAt = DateTimeOffset.UtcNow,
				// 取消优先：取消时 AiDiagnoseService 往往只留下失败消息，不能记成 failed。
				Status = wasCancelled ? "cancelled"
					: (aiResult != null && !aiResult.Ok) ? "failed"
					: (aiResult == null && !string.IsNullOrWhiteSpace(aiFailedMsg)) ? "failed"
					: (!configured || localOnly) ? "rule_only"
					: "completed",
				Environment = new DiagnosticReportEnvironment
				{
					OperatingSystem = snapshot.Context.OperatingSystem,
					LauncherVersion = snapshot.Context.LauncherVersion,
					MinecraftVersion = snapshot.Context.MinecraftVersion,
					LoaderType = snapshot.Context.LoaderType,
					LoaderVersion = snapshot.Context.LoaderVersion,
					JavaVersion = snapshot.Context.JavaVersion,
					MaxMemoryMb = snapshot.Context.MaxMemoryMb,
					ExitCode = snapshot.Context.ExitCode,
					LogSource = snapshot.Context.LogSource,
					LogModifiedAt = snapshot.Context.LogModifiedAt
				}
			};

			report.RuleSummary = San(rule.Summary);
			report.RuleSuggestions = rule.Suggestions == null
				? new List<string>()
				: rule.Suggestions.Select(San).ToList();

			// fact / inference / unknown 分开呈现：与本项目 Phase 3C-1 的纪律一致。
			report.Facts.Add(San("规则引擎结论：" + rule.Summary));
			report.Facts.Add(San($"退出码 {snapshot.Context.ExitCode}，日志来源 {snapshot.Context.LogSource}"));
			report.Facts.Add($"加载器 {snapshot.Context.LoaderType} {snapshot.Context.LoaderVersion}，Minecraft {snapshot.Context.MinecraftVersion}");
			report.Facts.Add($"Mod：启用 {mods.EnabledCount} / 禁用 {mods.DisabledCount} / 在线管理 {mods.OnlineManagedCount}");
			foreach (var issue in mods.Issues)
			{
				report.Facts.Add(San($"[{issue.Severity}] {issue.Title}：{issue.Description}"));
			}

			if (aiResult != null)
			{
				foreach (AiDiagnoseCause cause in aiResult.Causes)
				{
					string line = San($"{cause.Title}：{cause.Description}（{cause.Kind}，置信度 {cause.Confidence:0.##}）");
					if (string.Equals(cause.Kind, "fact", StringComparison.OrdinalIgnoreCase))
					{
						report.Facts.Add(line);
					}
					else
					{
						report.Inferences.Add(line);
					}
				}

				foreach (string missing in aiResult.MissingInformation) report.Unknowns.Add(San(missing));
				foreach (string warning in aiResult.Warnings) report.Inferences.Add(San("警告：" + warning));

				report.Ai = new DiagnosticReportAi
				{
					Configured = configured,
					ProviderName = San(providerName),
					Model = San(model),
					SentAtUtc = dispatchSentAtUtc,
					Status = aiResult.Status,
					ErrorCode = aiResult.ErrorCode,
					Cancelled = string.Equals(aiResult.Status, "cancelled", StringComparison.OrdinalIgnoreCase),
					Confidence = aiResult.Confidence
				};

				// 只有已注册的安全动作才标 Safe（UI 也只给这些渲染按钮）；未知动作只留文字。
				report.Actions = aiResult.Actions.Select(a => new DiagnosticReportAction
				{
					Id = a.Id,
					Title = San(a.Title),
					Description = San(a.Description),
					Risk = a.Risk,
					Reversible = a.Reversible,
					Safe = string.Equals(a.ActionType, "disable_mod", StringComparison.OrdinalIgnoreCase)
				}).ToList();
			}
			else
			{
				report.Ai = new DiagnosticReportAi
				{
					Configured = configured,
					ProviderName = San(providerName),
					Model = San(model),
					SentAtUtc = dispatchSentAtUtc,
					Status = "not_run",
					Cancelled = false
				};

				if (!string.IsNullOrWhiteSpace(aiFailedMsg))
				{
					report.Unknowns.Add(San("AI 未给出结论：" + aiFailedMsg));
				}
			}

			foreach (string missing in snapshot.Context.MissingInformation) report.Unknowns.Add(San(missing));

			report.Summary = San(Truncate(
				rule.Summary + (aiResult == null || string.IsNullOrWhiteSpace(aiResult.Summary) ? string.Empty : " | " + aiResult.Summary),
				600));

			// 证据摘录默认不留存；只有用户显式开启才写入，且仍然是脱敏后的。
			report.KeptEvidence = config.DiagnosticKeepEvidence;
			report.EvidenceExcerpt = config.DiagnosticKeepEvidence
				? San(DiagnosticLogSanitizer.SelectEvidence(rawTail, 4000))
				: null;

			report.Redaction.Counts = counts;
			report.Redaction.TotalRedactions = counts.Values.Sum();

			await CreateDiagnosticStore().SaveAsync(report);
		}
		catch (Exception ex)
		{
			LauncherLog.Error("[Diagnose] 写入诊断报告失败（不影响诊断结果）：" + ex.Message, ex);
		}
	}

	private static string Truncate(string? text, int max)
	{
		if (string.IsNullOrEmpty(text))
		{
			return string.Empty;
		}

		return text.Length <= max ? text : text.Substring(0, max) + "…";
	}

	/// <summary>MR08：诊断历史列表（只读本地；超量/超期只提示，不自动删除）。</summary>
	private static async Task HandleDiagnoseHistoryAsync(HttpListenerResponse response, string instanceId)
	{
		AppConfig config = LoadConfig();
		RequireInstance(config, instanceId);

		DiagnosticReportHistory history = await CreateDiagnosticStore().ListAsync(instanceId);
		await WriteJsonAsync(response, 200, new
		{
			instanceId,
			limit = history.Limit,
			retentionDays = history.RetentionDays,
			overLimitCount = history.OverLimitCount,
			olderThanRetentionCount = history.OlderThanRetentionCount,
			notice = history.Notice,
			reports = history.Reports.Select(r => new
			{
				reportId = r.ReportId,
				createdAt = r.CreatedAt,
				status = r.Status,
				summary = r.Summary,
				aiConfigured = r.Ai.Configured,
				aiStatus = r.Ai.Status,
				providerName = r.Ai.ProviderName,
				model = r.Ai.Model,
				actionCount = r.Actions.Count,
				keptEvidence = r.KeptEvidence,
				redactionTotal = r.Redaction.TotalRedactions
			})
		});
	}

	/// <summary>MR08：导出一份报告（Markdown / JSON）。原始证据摘录需显式 includeEvidence=1。</summary>
	private static async Task HandleDiagnoseExportAsync(HttpListenerRequest request, HttpListenerResponse response, string instanceId)
	{
		AppConfig config = LoadConfig();
		RequireInstance(config, instanceId);

		string reportId = (request.QueryString["reportId"] ?? string.Empty).Trim();
		string format = (request.QueryString["format"] ?? "md").Trim().ToLowerInvariant();
		bool includeEvidence = (request.QueryString["includeEvidence"] ?? "0") == "1";
		if (string.IsNullOrWhiteSpace(reportId))
		{
			throw new InvalidOperationException("reportId 不能为空");
		}

		DiagnosticReport? report = await CreateDiagnosticStore().LoadAsync(instanceId, reportId);
		if (report == null)
		{
			throw new InvalidOperationException("诊断报告不存在：" + reportId);
		}

		string text = format == "json"
			? DiagnosticReportService.ExportJson(report, includeEvidence)
			: DiagnosticReportService.ExportMarkdown(report, includeEvidence);
		byte[] bytes = Encoding.UTF8.GetBytes(text);
		response.StatusCode = 200;
		response.ContentType = format == "json" ? "application/json; charset=utf-8" : "text/markdown; charset=utf-8";
		response.ContentLength64 = bytes.Length;
		await response.OutputStream.WriteAsync(bytes, 0, bytes.Length);
	}
}
