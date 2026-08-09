using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Pineglow.Launcher.Services;

public sealed class LaunchCommand
{
	public required string JavaPath { get; init; }

	public required List<string> Arguments { get; init; }

	public required string WorkingDirectory { get; init; }

	public string ToDisplayString()
	{
		return Quote(JavaPath) + " " + string.Join(" ", RedactArguments(Arguments).Select(Quote));
	}

	public string ToScript()
	{
		bool num = OperatingSystem.IsWindows();
		string newLine = Environment.NewLine;
		StringBuilder stringBuilder = new StringBuilder();
		if (num)
		{
			stringBuilder.AppendLine("@echo off");
			stringBuilder.AppendLine("cd /d " + Quote(WorkingDirectory));
			stringBuilder.AppendLine(Quote(JavaPath) + " " + string.Join(" ", Arguments.Select(Quote)));
			stringBuilder.AppendLine("pause");
		}
		else
		{
			stringBuilder.AppendLine("#!/usr/bin/env bash");
			stringBuilder.AppendLine("set -e");
			stringBuilder.AppendLine("cd " + BashQuote(WorkingDirectory));
			stringBuilder.AppendLine(BashQuote(JavaPath) + " " + string.Join(" ", Arguments.Select(BashQuote)));
		}
		return stringBuilder.ToString().Replace("\r\n", newLine, StringComparison.Ordinal);
	}

	private static string Quote(string value)
	{
		if (string.IsNullOrEmpty(value))
		{
			return "\"\"";
		}
		if (!value.Any(char.IsWhiteSpace) && !value.Contains('"'))
		{
			return value;
		}
		return "\"" + value.Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
	}

	private static string BashQuote(string value)
	{
		return "'" + value.Replace("'", "'\\''", StringComparison.Ordinal) + "'";
	}

	private static IEnumerable<string> RedactArguments(IReadOnlyList<string> arguments)
	{
		bool redactNext = false;
		foreach (string argument in arguments)
		{
			if (redactNext)
			{
				yield return "<redacted>";
				redactNext = false;
			}
			else if (IsSensitiveArgument(argument))
			{
				int num = argument.IndexOf('=');
				if (num >= 0)
				{
					yield return argument.Substring(0, num + 1) + "<redacted>";
					continue;
				}
				yield return argument;
				redactNext = true;
			}
			else
			{
				yield return argument;
			}
		}
	}

	private static bool IsSensitiveArgument(string argument)
	{
		string text = argument.Split('=', 2)[0];
		if (!text.Equals("--accessToken", StringComparison.OrdinalIgnoreCase) && !text.Equals("--access-token", StringComparison.OrdinalIgnoreCase) && !text.Equals("--authAccessToken", StringComparison.OrdinalIgnoreCase))
		{
			return text.Equals("--session", StringComparison.OrdinalIgnoreCase);
		}
		return true;
	}
}
