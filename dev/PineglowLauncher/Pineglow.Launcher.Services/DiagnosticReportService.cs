using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Pineglow.Launcher.Models;

namespace Pineglow.Launcher.Services;

/// <summary>
/// MR08：报告级脱敏器。在既有 <see cref="DiagnosticLogSanitizer"/> 之上补齐
/// API Key、provider URL、URL 里的凭据/密钥查询参数，并**统计**每类脱敏次数，
/// 供导出物说明"脱敏了什么"。
/// 原则：只删凭据与地址，保留类名、Mod 名与异常类型——它们是诊断线索。
/// </summary>
public static class DiagnosticRedactor
{
	private static readonly (string Category, Regex Pattern)[] Rules =
	{
		("api_key", new Regex(@"(?i)\b(?:api[_-]?key|x-api-key|apikey|secret|password|passwd|pwd)\b\s*[=:]\s*\S{4,}", RegexOptions.Compiled)),
		("api_key", new Regex(@"\bsk-[A-Za-z0-9_\-]{8,}", RegexOptions.Compiled)),
		("api_key", new Regex(@"\beyJ[A-Za-z0-9_\-]{8,}\.[A-Za-z0-9_\-]{8,}\.[A-Za-z0-9_\-]{4,}", RegexOptions.Compiled)),
		("token", new Regex(@"(?i)\b(?:access[_-]?token|refresh[_-]?token|auth[_-]?token|id[_-]?token|authorization|bearer)\b\s*[=: ]\s*[A-Za-z0-9._\-]{6,}", RegexOptions.Compiled)),
		("session", new Regex(@"(?i)\b(?:session[_-]?id|x-box-live|player[_-]?session)\b\s*[=: ]\s*[A-Za-z0-9._\-]{6,}", RegexOptions.Compiled)),
		("provider_url", new Regex(@"(?i)https?://[a-z0-9.\-]*\bapi\b[a-z0-9.\-]*[^\s""'<>]*", RegexOptions.Compiled)),
		("url_credential", new Regex(@"(?i)https?://[^\s""'<>/@]+@", RegexOptions.Compiled)),
		("url_secret", new Regex(@"(?i)(?:[?&](?:key|token|api[_-]?key|access[_-]?token|apikey)=)[^\s""'<>&]+", RegexOptions.Compiled)),
		("address", new Regex(@"(?i)\b(?:server|host|address|ip)\b\s*[=: ]\s*[a-z0-9.\-]+(?::\d{2,5})?", RegexOptions.Compiled)),
		("address", new Regex(@"\b\d{1,3}(?:\.\d{1,3}){3}(?::\d{2,5})?\b", RegexOptions.Compiled)),
		// 注意：必须以空白为界，否则会把同一行后面的类名/Mod 名一起吞掉（本包实测踩过）。
		("path", new Regex(@"(?i)[A-Za-z]:[\\/](?:Users|Documents and Settings)[\\/][^,:;\s""']+", RegexOptions.Compiled)),
		("path", new Regex(@"(?i)/(?:home|Users)/[^/:\s""']+", RegexOptions.Compiled)),
	};

	private static readonly Dictionary<string, string> Placeholders = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
	{
		["api_key"] = "[REDACTED_API_KEY]",
		["token"] = "[REDACTED_TOKEN]",
		["session"] = "[REDACTED_SESSION]",
		["provider_url"] = "[REDACTED_PROVIDER_URL]",
		["url_credential"] = "[REDACTED_CREDENTIALS]",
		["url_secret"] = "[REDACTED_KEY]",
		["address"] = "[REDACTED_ADDRESS]",
		["path"] = "[REDACTED_PATH]",
	};

	/// <summary>脱敏并统计；<paramref name="counts"/> 记录每类替换次数。</summary>
	public static string Redact(string? input, out Dictionary<string, int> counts)
	{
		counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
		if (string.IsNullOrEmpty(input))
		{
			return string.Empty;
		}

		string text = input;
		foreach ((string category, Regex pattern) in Rules)
		{
			int hits = pattern.Matches(text).Count;
			if (hits == 0)
			{
				continue;
			}

			text = pattern.Replace(text, Placeholders[category]);
			counts[category] = counts.TryGetValue(category, out int previous) ? previous + hits : hits;
		}

		// 最后再过一遍共享脱敏器：任何它覆盖而我们遗漏的模式也不会流出去。
		string shared = DiagnosticLogSanitizer.Sanitize(text);
		if (!string.Equals(shared, text, StringComparison.Ordinal))
		{
			counts["shared_sanitizer"] = counts.TryGetValue("shared_sanitizer", out int prev) ? prev + 1 : 1;
			text = shared;
		}

		return text;
	}

	public static string Redact(string? input) => Redact(input, out _);
}

/// <summary>
/// MR08：诊断报告的历史与导出。
///
/// 设计边界（与计划一致）：
///   - 历史**默认只保存脱敏摘要**；原始日志正文默认不留存；
///   - 超期/超量**不静默删除**：只统计并提示用户清理（V1 不自动清理）；
///   - 导出 Markdown / JSON 两种可读格式，且**不含** provider URL、Key 与完整 prompt；
///   - 全程只碰本地文件，本地模式零外部请求。
/// </summary>
public sealed class DiagnosticReportService
{
	/// <summary>历史保留上限（条）。超出只提示，不自动删。</summary>
	public const int HistoryLimit = 50;

	/// <summary>历史保留期（天）。超出只提示，不自动删。</summary>
	public const int RetentionDays = 30;

	private static readonly JsonSerializerOptions ReportJsonOptions = new JsonSerializerOptions
	{
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
		WriteIndented = true
	};

	private readonly string _root;

	public DiagnosticReportService(string root)
	{
		if (string.IsNullOrWhiteSpace(root))
		{
			throw new InvalidOperationException("诊断报告根目录不能为空");
		}

		_root = Path.GetFullPath(root);
	}

	public string Root => _root;

	public string GetInstanceDirectory(string instanceId) => Path.Combine(_root, SafeSegment(instanceId));

	public string GetReportPath(string instanceId, string reportId) =>
		Path.Combine(GetInstanceDirectory(instanceId), SafeSegment(reportId) + ".json");

	/// <summary>写入（覆盖）一份报告。同一 ReportId 只保留一份。</summary>
	public async Task SaveAsync(DiagnosticReport report)
	{
		string dir = GetInstanceDirectory(report.InstanceId);
		Directory.CreateDirectory(dir);
		string path = GetReportPath(report.InstanceId, report.ReportId);
		string temp = path + ".tmp";
		await File.WriteAllTextAsync(temp, JsonSerializer.Serialize(report, ReportJsonOptions)).ConfigureAwait(false);
		File.Move(temp, path, overwrite: true);
	}

	public Task<DiagnosticReport?> LoadAsync(string instanceId, string reportId)
	{
		string path = GetReportPath(instanceId, reportId);
		if (!File.Exists(path))
		{
			return Task.FromResult<DiagnosticReport?>(null);
		}

		try
		{
			return Task.FromResult(JsonSerializer.Deserialize<DiagnosticReport>(
				File.ReadAllText(path), ReportJsonOptions));
		}
		catch
		{
			return Task.FromResult<DiagnosticReport?>(null);
		}
	}

	/// <summary>列出历史（新的在前），并给出超量/超期提示。不删除任何文件。</summary>
	public Task<DiagnosticReportHistory> ListAsync(string instanceId)
	{
		string dir = GetInstanceDirectory(instanceId);
		var reports = new List<DiagnosticReport>();
		if (Directory.Exists(dir))
		{
			foreach (string file in Directory.EnumerateFiles(dir, "*.json"))
			{
				try
				{
					DiagnosticReport? report = JsonSerializer.Deserialize<DiagnosticReport>(
						File.ReadAllText(file), ReportJsonOptions);
					if (report != null)
					{
						reports.Add(report);
					}
				}
				catch
				{
					// 不可读的记录跳过，不伪装成一条可用历史
				}
			}
		}

		List<DiagnosticReport> ordered = reports.OrderByDescending(r => r.CreatedAt).ToList();
		DateTimeOffset cutoff = DateTimeOffset.UtcNow.AddDays(-RetentionDays);
		int olderThanRetention = ordered.Count(r => r.CreatedAt < cutoff);
		int overLimit = Math.Max(0, ordered.Count - HistoryLimit);

		return Task.FromResult(new DiagnosticReportHistory
		{
			Reports = ordered,
			Limit = HistoryLimit,
			RetentionDays = RetentionDays,
			OverLimitCount = overLimit,
			OlderThanRetentionCount = olderThanRetention,
			Notice = (overLimit > 0 || olderThanRetention > 0)
				? $"历史已超出建议范围（上限 {HistoryLimit} 条 / {RetentionDays} 天）：超量 {overLimit} 条、超期 {olderThanRetention} 条。V1 不自动清理，请自行确认后删除——避免静默丢掉待恢复证据。"
				: string.Empty
		});
	}

	/// <summary>导出为 Markdown（可读、可粘贴进 issue）。</summary>
	public static string ExportMarkdown(DiagnosticReport report, bool includeEvidence)
	{
		var sb = new StringBuilder();
		sb.AppendLine("# 松光启动器 · 崩溃诊断报告");
		sb.AppendLine();
		sb.AppendLine($"- 报告 ID：`{report.ReportId}`");
		sb.AppendLine($"- 实例：{report.InstanceName}（`{report.InstanceId}`）");
		sb.AppendLine($"- 生成时间：{report.CreatedAt:yyyy-MM-dd HH:mm:ss zzz}");
		sb.AppendLine($"- 状态：{report.Status}");
		sb.AppendLine();

		sb.AppendLine("## 环境");
		sb.AppendLine();
		sb.AppendLine($"- 操作系统：{report.Environment.OperatingSystem}");
		sb.AppendLine($"- 启动器：{report.Environment.LauncherVersion}");
		sb.AppendLine($"- Minecraft：{report.Environment.MinecraftVersion}");
		sb.AppendLine($"- 加载器：{report.Environment.LoaderType} {report.Environment.LoaderVersion}".TrimEnd());
		sb.AppendLine($"- Java：{report.Environment.JavaVersion}");
		sb.AppendLine($"- 最大内存：{report.Environment.MaxMemoryMb} MB");
		sb.AppendLine($"- 退出码：{(report.Environment.ExitCode.HasValue ? report.Environment.ExitCode.Value.ToString() : "未知")}");
		sb.AppendLine($"- 日志来源：{report.Environment.LogSource}（修改于 {report.Environment.LogModifiedAt:yyyy-MM-dd HH:mm:ss}）");
		sb.AppendLine();

		sb.AppendLine("## 本地规则结论");
		sb.AppendLine();
		sb.AppendLine(report.RuleSummary);
		foreach (string suggestion in report.RuleSuggestions)
		{
			sb.AppendLine($"- {suggestion}");
		}
		sb.AppendLine();

		sb.AppendLine("## 事实（本地直接观察）");
		sb.AppendLine();
		foreach (string fact in report.Facts) sb.AppendLine($"- {fact}");
		sb.AppendLine();

		sb.AppendLine("## 推断（含不确定性）");
		sb.AppendLine();
		foreach (string item in report.Inferences) sb.AppendLine($"- {item}");
		sb.AppendLine();

		sb.AppendLine("## 未知（信息不足）");
		sb.AppendLine();
		foreach (string item in report.Unknowns) sb.AppendLine($"- {item}");
		sb.AppendLine();

		sb.AppendLine("## AI 增强");
		sb.AppendLine();
		sb.AppendLine($"- 是否配置提供方：{(report.Ai.Configured ? "是" : "否")}");
		sb.AppendLine($"- 提供方：{report.Ai.ProviderName}");
		sb.AppendLine($"- 模型：{report.Ai.Model}");
		sb.AppendLine($"- 真实发送时间：{(report.Ai.SentAtUtc.HasValue ? report.Ai.SentAtUtc.Value.ToString("yyyy-MM-dd HH:mm:ss zzz") : "未发送")}");
		sb.AppendLine($"- 状态：{report.Ai.Status}（置信度 {report.Ai.Confidence:0.##}）");
		if (!string.IsNullOrWhiteSpace(report.Ai.ErrorCode)) sb.AppendLine($"- 错误码：{report.Ai.ErrorCode}");
		if (report.Ai.Cancelled) sb.AppendLine("- 本次请求已取消");
		sb.AppendLine();

		sb.AppendLine("## 建议动作");
		sb.AppendLine();
		foreach (DiagnosticReportAction action in report.Actions)
		{
			sb.AppendLine($"- [{action.Risk}] {action.Title}（{(action.Reversible ? "可撤销" : "不可撤销")}{(action.Safe ? "" : " · 未注册为安全动作")}）");
			if (!string.IsNullOrWhiteSpace(action.Description)) sb.AppendLine($"  - {action.Description}");
		}
		sb.AppendLine();

		sb.AppendLine("## 脱敏说明");
		sb.AppendLine();
		sb.AppendLine(report.Redaction.Note);
		sb.AppendLine();
		sb.AppendLine($"- 脱敏总次数：{report.Redaction.TotalRedactions}");
		foreach (KeyValuePair<string, int> kv in report.Redaction.Counts.OrderBy(k => k.Key))
		{
			sb.AppendLine($"  - {kv.Key}：{kv.Value}");
		}
		sb.AppendLine();

		if (includeEvidence)
		{
			sb.AppendLine("## 证据摘录（脱敏后）");
			sb.AppendLine();
			sb.AppendLine("```");
			sb.AppendLine(string.IsNullOrWhiteSpace(report.EvidenceExcerpt)
				? "（本次诊断未保留证据摘录：默认不留存原文，需在诊断时显式开启）"
				: report.EvidenceExcerpt);
			sb.AppendLine("```");
		}
		else
		{
			sb.AppendLine("> 未包含证据摘录。需要时请在导出时显式勾选「包含脱敏证据摘录」。");
		}

		return sb.ToString();
	}

	/// <summary>导出为 JSON（字段与界面一致；同样不含 URL/Key/prompt）。</summary>
	public static string ExportJson(DiagnosticReport report, bool includeEvidence)
	{
		var payload = new Dictionary<string, object?>
		{
			["version"] = report.Version,
			["reportId"] = report.ReportId,
			["instanceId"] = report.InstanceId,
			["instanceName"] = report.InstanceName,
			["createdAt"] = report.CreatedAt,
			["status"] = report.Status,
			["environment"] = report.Environment,
			["ruleSummary"] = report.RuleSummary,
			["ruleSuggestions"] = report.RuleSuggestions,
			["facts"] = report.Facts,
			["inferences"] = report.Inferences,
			["unknowns"] = report.Unknowns,
			["ai"] = report.Ai,
			["actions"] = report.Actions,
			["redaction"] = report.Redaction,
			["summary"] = report.Summary,
			["keptEvidence"] = report.KeptEvidence,
		};

		if (includeEvidence)
		{
			payload["evidenceExcerpt"] = report.EvidenceExcerpt ?? string.Empty;
		}

		return JsonSerializer.Serialize(payload, ReportJsonOptions);
	}

	private static string SafeSegment(string value)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			throw new InvalidOperationException("标识不能为空");
		}

		string cleaned = new string(value.Where(c => char.IsLetterOrDigit(c) || c == '-' || c == '_').ToArray());
		if (cleaned.Length == 0)
		{
			throw new InvalidOperationException("标识含非法字符：" + value);
		}

		return cleaned;
	}
}

/// <summary>历史列表结果：报告 + 超量/超期提示（不自动清理）。</summary>
public sealed class DiagnosticReportHistory
{
	public List<DiagnosticReport> Reports { get; set; } = new List<DiagnosticReport>();
	public int Limit { get; set; }
	public int RetentionDays { get; set; }
	public int OverLimitCount { get; set; }
	public int OlderThanRetentionCount { get; set; }
	public string Notice { get; set; } = string.Empty;
}
