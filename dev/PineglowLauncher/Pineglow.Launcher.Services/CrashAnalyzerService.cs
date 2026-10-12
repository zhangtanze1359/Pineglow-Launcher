using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Pineglow.Launcher.Services;

public static class CrashAnalyzerService
{
	public static CrashAnalysis Analyze(string gameDirectory, int exitCode)
	{
		string text = FindNewestDiagnosticFile(gameDirectory);
		string text2 = ((text == null) ? string.Empty : ReadTail(text, 524288));
		List<string> list = new List<string>();
		string summary;
		if (ContainsAny(text2, "OutOfMemoryError", "GC overhead limit exceeded", "Could not reserve enough space"))
		{
			summary = "游戏因内存不足或内存参数不合适而退出。";
			list.Add("适当增加游戏内存，同时不要超过电脑可用内存。");
			list.Add("大型整合包建议关闭其他高内存程序后重试。");
		}
		else if (ContainsAny(text2, "UnsupportedClassVersionError", "class file version"))
		{
			summary = "当前 Java 版本与 Minecraft 或 Mod 不兼容。";
			list.Add("在 Java 管理中恢复自动选择，让启动器重新准备合适的 Java。");
		}
		else if (ContainsAny(text2, "ModResolutionException", "Missing mandatory dependencies", "requires version", "NoClassDefFoundError"))
		{
			summary = "可能存在缺失的 Mod 依赖或不兼容的 Mod 版本。";
			list.Add("检查最近添加的 Mod，并安装日志中提示的前置依赖。");
			list.Add("确认所有 Mod 都匹配当前 Minecraft 版本和加载器类型。");
		}
		else if (ContainsAny(text2, "EXCEPTION_ACCESS_VIOLATION", "UnsatisfiedLinkError", "Failed to create GLFW window", "OpenGL"))
		{
			summary = "游戏在本地库、显卡驱动或图形初始化阶段失败。";
			list.Add("更新显卡驱动，并尝试移除光影、OptiFine 或图形优化 Mod。");
			list.Add("运行版本完整性验证，修复缺失的 native 文件。");
		}
		else
		{
			summary = $"游戏异常退出，退出代码为 {exitCode}。";
			list.Add("打开日志页查看退出前的错误，并尝试验证或修复当前版本。");
		}
		return new CrashAnalysis(summary, list, text);
	}

	/// <summary>读取最新诊断文件（latest.log 或 crash-reports）的尾部，供 AI 诊断等复用。</summary>
	public static string ReadNewestDiagnosticTail(string gameDirectory)
	{
		string text = FindNewestDiagnosticFile(gameDirectory);
		return (text == null) ? string.Empty : ReadTail(text, 524288);
	}

	private static string? FindNewestDiagnosticFile(string gameDirectory)
	{
		List<string> list = new List<string>();
		string text = Path.Combine(gameDirectory, "logs", "latest.log");
		if (File.Exists(text))
		{
			list.Add(text);
		}
		string path = Path.Combine(gameDirectory, "crash-reports");
		if (Directory.Exists(path))
		{
			list.AddRange(Directory.EnumerateFiles(path, "*.txt", SearchOption.TopDirectoryOnly));
		}
		return list.OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();
	}

	private static string ReadTail(string path, int maxBytes)
	{
		try
		{
			using FileStream fileStream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
			if (fileStream.Length > maxBytes)
			{
				fileStream.Seek(-maxBytes, SeekOrigin.End);
			}
			using StreamReader streamReader = new StreamReader(fileStream);
			return streamReader.ReadToEnd();
		}
		catch
		{
			return string.Empty;
		}
	}

	private static bool ContainsAny(string text, params string[] values)
	{
		return values.Any((string value) => text.Contains(value, StringComparison.OrdinalIgnoreCase));
	}
}
