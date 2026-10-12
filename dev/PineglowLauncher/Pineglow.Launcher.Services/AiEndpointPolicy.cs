using System;
using System.Collections.Generic;
using System.Net;

namespace Pineglow.Launcher.Services;

/// <summary>
/// Phase 3B 发布安全切片：AI endpoint 发布门禁策略。
/// 当前发布模式下仅允许代码内审核过的固定 HTTPS allowlist；任意自定义
/// OpenAI-compatible endpoint 与 http://localhost Ollama 暂时禁用。
/// 拒绝 HTTP、userinfo、IP literal、localhost、相似后缀、未知 host、非标准 scheme。
/// </summary>
public static class AiEndpointPolicy
{
	public enum Decision
	{
		Allow,
		Reject
	}

	public sealed record Evaluation(Decision Decision, string? ReasonCode, string? Reason);

	/// <summary>代码内审核过的固定 HTTPS AI 提供商基址 allowlist。</summary>
	private static readonly HashSet<string> AllowedEndpoints = new(StringComparer.OrdinalIgnoreCase)
	{
		"https://api.deepseek.com/v1",
		// Codex 2026-09-24 实测：sk-cp 订阅 Key 在国际 minimax.io 401，在中国区 api.minimaxi.com/v1/chat/completions
		// 用 MiniMax-M3 最小调用 HTTP 200。仅允许精确 URL；相似前缀/后缀域名仍由 LooksLikeSimilarSuffix 拒绝。
		"https://api.minimaxi.com/v1"
	};

	/// <summary>MiniMax 中国区精确基址：常量供 AiDiagnoseService 判定是否走 MiniMax 专用 payload（无 response_format + thinking.type=disabled）。</summary>
	public const string MiniMaxEndpoint = "https://api.minimaxi.com/v1";

	/// <summary>判断给定 baseUrl 是否为精确等于 MiniMax 中国区基址（忽略末尾斜杠与大小写）。供 AiDiagnoseService 在生成 payload 前调用。</summary>
	public static bool IsMiniMaxEndpoint(string? baseUrl)
	{
		if (string.IsNullOrWhiteSpace(baseUrl))
		{
			return false;
		}
		string normalized = baseUrl.Trim().TrimEnd('/');
		return string.Equals(normalized, MiniMaxEndpoint, StringComparison.OrdinalIgnoreCase);
	}

	/// <summary>集中常量：响应体最大字节数，超限拒收。</summary>
	public const long MaxResponseBytes = 1_048_576; // 1 MiB

	/// <summary>集中常量：解析时单个字符串字段最大长度。</summary>
	public const int MaxStringFieldLength = 2_000;

	/// <summary>集中常量：causes 数组上限。</summary>
	public const int MaxCauses = 20;

	/// <summary>集中常量：actions 数组上限。</summary>
	public const int MaxActions = 10;

	/// <summary>集中常量：warnings 数组上限。</summary>
	public const int MaxWarnings = 10;

	/// <summary>集中常量：missingInformation 数组上限。</summary>
	public const int MaxMissingInformation = 10;

	/// <summary>集中常量：单条 evidence 字符串上限。</summary>
	public const int MaxEvidenceLength = 1_500;

	public static Evaluation Evaluate(string? baseUrl)
	{
		if (string.IsNullOrWhiteSpace(baseUrl))
		{
			return new Evaluation(Decision.Reject, "endpoint_empty", "endpoint is empty");
		}

		if (!Uri.TryCreate(baseUrl.Trim(), UriKind.Absolute, out Uri? uri))
		{
			return new Evaluation(Decision.Reject, "endpoint_malformed", "endpoint is not a valid absolute URI");
		}

		if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
		{
			return new Evaluation(Decision.Reject, "endpoint_not_https", $"scheme '{uri.Scheme}' not allowed; only https is permitted");
		}

		if (!string.IsNullOrEmpty(uri.UserInfo))
		{
			return new Evaluation(Decision.Reject, "endpoint_has_userinfo", "userinfo (user:pass@) is not allowed in endpoint");
		}

		string host = uri.Host;
		if (string.IsNullOrEmpty(host))
		{
			return new Evaluation(Decision.Reject, "endpoint_empty_host", "endpoint host is empty");
		}

		if (IsLoopbackHost(host))
		{
			return new Evaluation(Decision.Reject, "endpoint_loopback", "loopback host is not allowed in release safety mode");
		}

		if (IsIpLiteral(host))
		{
			return new Evaluation(Decision.Reject, "endpoint_ip_literal", "IP literal host is not allowed");
		}

		if (LooksLikeSimilarSuffix(host, "deepseek.com") || LooksLikeSimilarSuffix(host, "minimaxi.com"))
		{
			return new Evaluation(Decision.Reject, "endpoint_similar_suffix", "host appears to mimic an allowed provider");
		}

		string normalized = baseUrl.Trim().TrimEnd('/');
		bool allowed = false;
		foreach (string allowedUrl in AllowedEndpoints)
		{
			if (string.Equals(normalized, allowedUrl.TrimEnd('/'), StringComparison.OrdinalIgnoreCase))
			{
				allowed = true;
				break;
			}
		}

		if (!allowed)
		{
			return new Evaluation(Decision.Reject, "endpoint_not_allowed", "host is not in the approved allowlist");
		}

		return new Evaluation(Decision.Allow, null, null);
	}

	private static bool IsIpLiteral(string host)
	{
		if (host.StartsWith('[') && host.EndsWith(']'))
		{
			return true;
		}
		if (IPAddress.TryParse(host, out _))
		{
			return true;
		}
		return false;
	}

	private static bool IsLoopbackHost(string host)
	{
		if (string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase))
		{
			return true;
		}
		if (IPAddress.TryParse(host, out IPAddress? ip) && IPAddress.IsLoopback(ip))
		{
			return true;
		}
		return false;
	}

	/// <summary>粗略的"相似后缀"防御：若 host 含 deepseek.com 但不精确等于 api.deepseek.com，则拒绝。</summary>
	private static bool LooksLikeSimilarSuffix(string host, string allowedBase)
	{
		if (string.Equals(host, "api." + allowedBase, StringComparison.OrdinalIgnoreCase))
		{
			return false;
		}
		return host.Contains(allowedBase, StringComparison.OrdinalIgnoreCase);
	}
}