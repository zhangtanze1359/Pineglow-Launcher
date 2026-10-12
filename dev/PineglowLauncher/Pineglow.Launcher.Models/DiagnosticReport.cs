using System;
using System.Collections.Generic;

namespace Pineglow.Launcher.Models;

/// <summary>
/// MR08：一次崩溃诊断的**冻结结构**报告。
///
/// 与 Phase 3C-1/3C-2 保持一致：事实（fact）、推断（inference）、未知（unknown）分开呈现，
/// AI 派发元数据只保留 provider 名称/模型/真实发送时间，**永不写入 URL、Key 或完整 prompt**。
/// </summary>
public sealed class DiagnosticReport
{
	public const int CurrentVersion = 1;

	public int Version { get; set; } = CurrentVersion;

	/// <summary>报告 ID。等于触发的 operationId，因此"同一操作只记录一次"。</summary>
	public string ReportId { get; set; } = string.Empty;

	public string InstanceId { get; set; } = string.Empty;

	public string InstanceName { get; set; } = string.Empty;

	public DateTimeOffset CreatedAt { get; set; }

	/// <summary>completed | cancelled | failed | rule_only</summary>
	public string Status { get; set; } = "completed";

	public DiagnosticReportEnvironment Environment { get; set; } = new DiagnosticReportEnvironment();

	/// <summary>本地规则引擎的结论摘要（无 Key 用户也能看到的部分）。</summary>
	public string RuleSummary { get; set; } = string.Empty;

	public List<string> RuleSuggestions { get; set; } = new List<string>();

	/// <summary>事实：本轮直接观察到的东西。</summary>
	public List<string> Facts { get; set; } = new List<string>();

	/// <summary>推断：规则/AI 给的解释，带不确定性。</summary>
	public List<string> Inferences { get; set; } = new List<string>();

	/// <summary>未知：信息不足、需要用户补充的部分。</summary>
	public List<string> Unknowns { get; set; } = new List<string>();

	public DiagnosticReportAi Ai { get; set; } = new DiagnosticReportAi();

	/// <summary>可执行建议。只有已注册的安全动作才允许出现在这里。</summary>
	public List<DiagnosticReportAction> Actions { get; set; } = new List<DiagnosticReportAction>();

	public DiagnosticRedactionSummary Redaction { get; set; } = new DiagnosticRedactionSummary();

	/// <summary>脱敏摘要：历史默认保存的就是这一份。</summary>
	public string Summary { get; set; } = string.Empty;

	/// <summary>脱敏后的证据摘录。只有在用户显式开启"保留原文"时才写入。</summary>
	public string? EvidenceExcerpt { get; set; }

	public bool KeptEvidence { get; set; }
}

public sealed class DiagnosticReportEnvironment
{
	public string OperatingSystem { get; set; } = string.Empty;
	public string LauncherVersion { get; set; } = string.Empty;
	public string MinecraftVersion { get; set; } = string.Empty;
	public string LoaderType { get; set; } = string.Empty;
	public string LoaderVersion { get; set; } = string.Empty;
	public string JavaVersion { get; set; } = string.Empty;
	public int MaxMemoryMb { get; set; }
	public int? ExitCode { get; set; }
	public string LogSource { get; set; } = string.Empty;
	public DateTimeOffset? LogModifiedAt { get; set; }
}

public sealed class DiagnosticReportAi
{
	/// <summary>是否配置了可用的 provider（无 Key 时为 false，规则结果仍然保留）。</summary>
	public bool Configured { get; set; }

	public string ProviderName { get; set; } = string.Empty;

	public string Model { get; set; } = string.Empty;

	/// <summary>服务端真正发起发送的时间；没有发送则为 null。</summary>
	public DateTimeOffset? SentAtUtc { get; set; }

	public string Status { get; set; } = string.Empty;

	public string? ErrorCode { get; set; }

	public bool Cancelled { get; set; }

	public double Confidence { get; set; }
}

public sealed class DiagnosticReportAction
{
	public string Id { get; set; } = string.Empty;
	public string Title { get; set; } = string.Empty;
	public string Description { get; set; } = string.Empty;

	/// <summary>low | medium | high</summary>
	public string Risk { get; set; } = string.Empty;

	public bool Reversible { get; set; }

	/// <summary>是否命中已注册的安全动作映射（未命中的动作不得直接执行）。</summary>
	public bool Safe { get; set; }
}

/// <summary>脱敏统计：导出物里必须能说明"脱敏了什么"。</summary>
public sealed class DiagnosticRedactionSummary
{
	public Dictionary<string, int> Counts { get; set; } = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

	public int TotalRedactions { get; set; }

	public string Note { get; set; } = "本地脱敏：token/API Key/authorization/session/地址/用户路径/provider URL 已替换为占位符；类名与 Mod 名保留，避免丢失诊断线索。";
}
