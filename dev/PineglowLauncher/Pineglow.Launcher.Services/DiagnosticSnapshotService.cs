using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

using Pineglow.Launcher.Models;

namespace Pineglow.Launcher.Services;

public sealed record DiagnosticContext(
	string OperatingSystem,
	string LauncherVersion,
	string MinecraftVersion,
	string LoaderType,
	string LoaderVersion,
	string JavaVersion,
	int MaxMemoryMb,
	string CustomJvmArgs,
	int? ExitCode,
	string LogSource,
	DateTimeOffset? LogModifiedAt,
	IReadOnlyList<string> ModFiles,
	IReadOnlyList<string> MissingInformation);

public static class DiagnosticContextFactory
{
	public static DiagnosticContext Create(
		string operatingSystem,
		string launcherVersion,
		Pineglow.Launcher.Models.GameInstance instance,
		string javaVersion,
		int? exitCode,
		string logSource,
		DateTimeOffset? logModifiedAt)
	{
		List<string> missing = new List<string>();
		if (string.IsNullOrWhiteSpace(javaVersion)) missing.Add("javaVersion");
		if (exitCode == null) missing.Add("exitCode");
		if (string.IsNullOrWhiteSpace(logSource)) missing.Add("logSource");
		return new DiagnosticContext(
			operatingSystem,
			launcherVersion,
			instance.MinecraftVersion,
			ModLoaderInfo.Normalize(instance.LoaderType),
			instance.LoaderVersion,
			javaVersion,
			instance.MaxMemoryMb,
			instance.CustomJvmArgs,
			exitCode,
			logSource,
			logModifiedAt,
			Array.Empty<string>(),
			missing);
	}
}

public sealed class DiagnosticSnapshot
{
	public const int Version = 1;
	public string SnapshotId { get; init; } = Guid.NewGuid().ToString("N");
	public DiagnosticContext Context { get; init; } = new(string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, 0, string.Empty, null, string.Empty, null, Array.Empty<string>(), Array.Empty<string>());
	public string EvidenceLog { get; init; } = string.Empty;
}

public static class DiagnosticLogSanitizer
{
	private static readonly Regex AccessToken = new("(?i)(access[_-]?token|auth[_-]?token|authorization|bearer)[=: ]+[a-z0-9._-]{8,}", RegexOptions.Compiled);
	private static readonly Regex SessionId = new("(?i)(session[_-]?id|x-box-live|player[_-]?session)[=: ]+[a-z0-9._-]{8,}", RegexOptions.Compiled);
	private static readonly Regex UserPath = new("(?i)([A-Za-z]:[\\/](Users|Documents and Settings)[\\/][^,:\\r\\n]+|/(home|Users)/[^/:, \\r\\n]+)", RegexOptions.Compiled);
	private static readonly Regex ServerAddress = new("(?i)(server|host|address)[=: ]+[a-z0-9.-]+:[0-9]{2,5}", RegexOptions.Compiled);
	private static readonly Regex ChatMessage = new("(?im)^[^\r\n]*(?:chat|said|message)[^\r\n]*$", RegexOptions.Compiled | RegexOptions.Multiline);
	private const string NL = "\n";
	private static readonly Regex EmptyLine = new("^\\s*$", RegexOptions.Compiled);

	public static string Sanitize(string input)
	{
		if (string.IsNullOrEmpty(input))
		{
			return string.Empty;
		}

		string text = AccessToken.Replace(input, "[REDACTED_TOKEN]");
		text = SessionId.Replace(text, "[REDACTED_SESSION]");
		text = ServerAddress.Replace(text, "[REDACTED_ADDRESS]");
		text = UserPath.Replace(text, "[REDACTED_PATH]");
		text = ChatMessage.Replace(text, "[REDACTED_CHAT]");
		return text;
	}

	public static string SelectEvidence(string input, int maxCharacters)
	{
		if (string.IsNullOrWhiteSpace(input))
		{
			return string.Empty;
		}

		string sanitized = Sanitize(input);
		IEnumerable<string> relevant = sanitized
			.Split('\n')
			.Where(line => !EmptyLine.IsMatch(line))
			.Where(IsRelevant);
		List<string> selected = relevant.ToList();
		if (selected.Count == 0)
		{
			selected = sanitized.Split('\n').TakeLast(80).ToList();
		}

		string evidence = string.Join(NL, selected);
		if (evidence.Length <= maxCharacters)
		{
			return evidence;
		}

		int headLength = Math.Min(600, evidence.Length);
		int tailLength = Math.Min(maxCharacters - headLength - 16, evidence.Length - headLength);
		if (tailLength <= 0)
		{
			return evidence.Substring(evidence.Length - Math.Min(maxCharacters, evidence.Length));
		}

		return evidence.Substring(0, headLength) + "...[EVIDENCE_TRIMMED]..." + evidence.Substring(evidence.Length - tailLength);
	}
	private static bool IsRelevant(string line)
	{
		return line.Contains("Caused by", StringComparison.OrdinalIgnoreCase)
			|| line.Contains("Error", StringComparison.OrdinalIgnoreCase)
			|| line.Contains("OutOfMemory", StringComparison.OrdinalIgnoreCase)
			|| line.Contains("UnsupportedClass", StringComparison.OrdinalIgnoreCase)
			|| line.Contains("NoClassDefFound", StringComparison.OrdinalIgnoreCase)
			|| line.Contains("Missing", StringComparison.OrdinalIgnoreCase)
			|| line.Contains("Mixin", StringComparison.OrdinalIgnoreCase)
			|| line.Contains("Duplicate", StringComparison.OrdinalIgnoreCase)
			|| line.Contains("OpenGL", StringComparison.OrdinalIgnoreCase)
			|| line.Contains("EXCEPTION_ACCESS_VIOLATION", StringComparison.OrdinalIgnoreCase)
			|| line.Contains("hs_err", StringComparison.OrdinalIgnoreCase)
			|| line.Contains("ModResolution", StringComparison.OrdinalIgnoreCase);
	}
}

/// <summary>
/// Phase 3B A-02：JVM 参数脱敏器。剥离 access/refresh token、session id、
/// 服务器地址、用户路径等敏感模式；保留 chat/message 之外的所有模式以避免误伤
/// 命令行参数（如 -Dmessage=foo）。
/// </summary>
public static class DiagnosticJvmArgsRedactor
{
	private static readonly Regex AccessToken = new("(?i)(access[_-]?token|auth[_-]?token|authorization|bearer|password|passwd|pwd|secret|api[_-]?key)[=: ]+[a-z0-9._-]{6,}", RegexOptions.Compiled);
	private static readonly Regex SessionId = new("(?i)(session[_-]?id|x-box-live|player[_-]?session)[=: ]+[a-z0-9._-]{8,}", RegexOptions.Compiled);
	private static readonly Regex ServerAddress = new("(?i)(server|host|address)[=: ]+[a-z0-9.-]+:[0-9]{2,5}", RegexOptions.Compiled);
	private static readonly Regex UserPath = new("(?i)([A-Za-z]:[\\/](Users|Documents and Settings)[\\/][^,:\\r\\n]+|/(home|Users)/[^/:, \\r\\n]+)", RegexOptions.Compiled);

	public static string Redact(string jvmArgs)
	{
		if (string.IsNullOrEmpty(jvmArgs))
		{
			return string.Empty;
		}

		string text = jvmArgs;
		text = AccessToken.Replace(text, "[REDACTED_TOKEN]");
		text = SessionId.Replace(text, "[REDACTED_SESSION]");
		text = ServerAddress.Replace(text, "[REDACTED_ADDRESS]");
		text = UserPath.Replace(text, "[REDACTED_PATH]");
		return text;
	}
}
