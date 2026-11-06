using System;
using System.IO;
using System.Linq;
using Pineglow.Launcher.Services;
using Xunit;

namespace Pineglow.Launcher.Tests;

/// <summary>
/// 2026-11-06 未覆盖路径补测（批次 5）：<c>LauncherLog</c> 与 <c>PlatformPaths</c>。
///
/// 覆盖率实测：<c>LauncherLog</c> 21.8%、<c>PlatformPaths</c> 37.9%。两者都是**横切基础设施**——
/// 出问题不是"某个功能坏了"，而是"日志没了（排障无从下手）"或"配置写到意外位置（便携模式失效）"。
///
/// 这批测试全部落在临时目录里，靠 <c>LauncherLog.Init(path)</c> 的重定向能力，
/// 不去碰真实的 %APPDATA%。
/// </summary>
public class LoggingAndPathsTests : IDisposable
{
	private readonly string _root;

	public LoggingAndPathsTests()
	{
		_root = Path.Combine(Path.GetTempPath(), "pineglow-log-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(_root);
	}

	public void Dispose()
	{
		try
		{
			// 先把 writer 释放掉，否则临时目录删不掉（文件被占用）
			LauncherLog.Init(Path.Combine(_root, "drain"));
			if (Directory.Exists(_root))
			{
				Directory.Delete(_root, recursive: true);
			}
		}
		catch
		{
		}
	}

	/// <summary>
	/// 读一个**正被 StreamWriter 持有**的日志文件。
	///
	/// 为什么不能直接 File.ReadAllText：LauncherLog 用 `new StreamWriter(file, append: true)`
	/// 打开文件，默认共享模式不允许其他句柄读，于是 ReadAllText 会抛
	/// "The process cannot access the file ... because it is being used by another process"。
	/// 这是**测试的读法问题**，不是产品缺陷——生产环境没人边写边读同一份日志。
	/// 这里显式用 FileShare.ReadWrite 打开，模拟"用户在资源管理器里打开日志"这种真实场景。
	/// </summary>
	private static string ReadWhileOpen(string path)
	{
		using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
		using var reader = new StreamReader(fs, System.Text.Encoding.UTF8);
		return reader.ReadToEnd();
	}

	private static string[] ReadLinesWhileOpen(string path)
		=> ReadWhileOpen(path).Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);

	/* ===================== LauncherLog ===================== */

	[Fact]
	public void LauncherLog_Init_CreatesDirectory_AndWritesTimestampedLines()
	{
		string dir = Path.Combine(_root, "logs");
		LauncherLog.Init(dir);

		LauncherLog.Info("hello-info");
		LauncherLog.Warn("hello-warn");
		LauncherLog.Error("hello-error");

		Assert.Equal(dir, LauncherLog.GetLogDirectory());
		string file = LauncherLog.GetCurrentLogFile();
		Assert.True(File.Exists(file), "日志文件应被创建：" + file);

		string content = ReadWhileOpen(file);
		Assert.Contains("[INFO] hello-info", content);
		Assert.Contains("[WARN] hello-warn", content);
		Assert.Contains("[ERROR] hello-error", content);
		// 时间戳格式 yyyy-MM-dd HH:mm:ss.fff
		Assert.Matches(@"\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{3}", content);
	}

	[Fact]
	public void LauncherLog_FileName_IsDateBased_AndUnderGivenDirectory()
	{
		string dir = Path.Combine(_root, "logs2");
		LauncherLog.Init(dir);
		LauncherLog.Info("x");

		string name = Path.GetFileName(LauncherLog.GetCurrentLogFile());
		Assert.Equal("launcher-" + DateTime.Now.ToString("yyyyMMdd") + ".log", name);
	}

	[Fact]
	public void LauncherLog_Appends_InsteadOfTruncating()
	{
		string dir = Path.Combine(_root, "logs3");
		LauncherLog.Init(dir);
		LauncherLog.Info("first");
		LauncherLog.Info("second");

		string content = ReadWhileOpen(LauncherLog.GetCurrentLogFile());
		Assert.Contains("first", content);
		Assert.Contains("second", content);
		// 第二次 Init 同一目录不应清空已有内容
		LauncherLog.Init(dir);
		LauncherLog.Info("third");
		content = ReadWhileOpen(LauncherLog.GetCurrentLogFile());
		Assert.Contains("first", content);
		Assert.Contains("third", content);
	}

	[Fact]
	public void LauncherLog_ErrorWithException_IncludesExceptionText()
	{
		string dir = Path.Combine(_root, "logs4");
		LauncherLog.Init(dir);
		LauncherLog.Error("boom", new InvalidOperationException("inner-detail"));

		string content = ReadWhileOpen(LauncherLog.GetCurrentLogFile());
		Assert.Contains("boom", content);
		Assert.Contains("inner-detail", content);
	}

	[Fact]
	public void LauncherLog_CleanupOld_RemovesFilesOlderThanSevenDays()
	{
		string dir = Path.Combine(_root, "logs5");
		Directory.CreateDirectory(dir);

		string old = Path.Combine(dir, "launcher-20200101.log");
		string recent = Path.Combine(dir, "launcher-recent.log");
		string unrelated = Path.Combine(dir, "notes.txt");
		File.WriteAllText(old, "old");
		File.WriteAllText(recent, "recent");
		File.WriteAllText(unrelated, "keep");
		File.SetLastWriteTime(old, DateTime.Now.AddDays(-30));
		File.SetLastWriteTime(recent, DateTime.Now);

		LauncherLog.Init(dir);

		Assert.False(File.Exists(old), "超过 7 天的 launcher-*.log 应被清理");
		Assert.True(File.Exists(recent), "7 天内的日志必须保留");
		Assert.True(File.Exists(unrelated), "非 launcher-*.log 的文件不得被误删");
	}

	[Fact]
	public void LauncherLog_WriteFailure_DoesNotThrow()
	{
		// 日志目录指向一个**文件**而不是目录 → 创建失败。
		// 关键断言：写日志永远不能把主流程带崩。
		string bogus = Path.Combine(_root, "not-a-dir");
		File.WriteAllText(bogus, "x");

		LauncherLog.Init(bogus);
		LauncherLog.Info("should-not-throw");
		LauncherLog.Error("also-not", new Exception("e"));

		// 没有抛异常即通过；这里再做一次显式陈述
		Assert.True(true);
	}

	[Fact]
	public void LauncherLog_ManyConcurrentWrites_ProduceCompleteLines()
	{
		// 并发写日志时行不能被截断/交错（Gate 锁的意义）。
		string dir = Path.Combine(_root, "logs6");
		LauncherLog.Init(dir);

		System.Threading.Tasks.Parallel.For(0, 200, i => LauncherLog.Info("line-" + i));

		string[] lines = ReadLinesWhileOpen(LauncherLog.GetCurrentLogFile());
		Assert.Equal(200, lines.Length);
		Assert.All(lines, l => Assert.Matches(@"^\[INFO\] line-\d+$", l.Substring(l.IndexOf('['))));
	}

	/* ===================== PlatformPaths ===================== */

	[Fact]
	public void PlatformPaths_OsRuleName_MatchesKnownPlatforms()
	{
		string os = PlatformPaths.GetOsRuleName();
		Assert.Contains(os, new[] { "windows", "osx", "linux" });
		if (OperatingSystem.IsWindows())
		{
			Assert.Equal("windows", os);
		}
	}

	[Fact]
	public void PlatformPaths_NativeClassifierKey_HasNativesPrefix()
	{
		string key = PlatformPaths.GetNativeClassifierKey();
		Assert.StartsWith("natives-", key);
		Assert.Contains(key, new[] { "natives-windows", "natives-macos", "natives-linux" });
	}

	[Fact]
	public void PlatformPaths_ScriptExtension_MatchesPlatform()
	{
		string ext = PlatformPaths.GetScriptExtension();
		if (OperatingSystem.IsWindows())
		{
			Assert.Equal(".bat", ext);
		}
		else
		{
			Assert.Equal(".sh", ext);
		}
	}

	[Fact]
	public void PlatformPaths_ConfigPath_IsFullyQualified_AndEndsWithConfigJson()
	{
		string path = PlatformPaths.GetConfigPath();
		Assert.True(Path.IsPathFullyQualified(path), "配置路径必须是绝对路径：" + path);
		Assert.EndsWith("config.json", path);
		Assert.Equal(Path.Combine(PlatformPaths.GetConfigDirectory(), "config.json"), path);
	}

	[Fact]
	public void PlatformPaths_MinecraftDirectory_IsAbsolute()
	{
		string dir = PlatformPaths.GetDefaultMinecraftDirectory();
		Assert.True(Path.IsPathFullyQualified(dir));
		if (OperatingSystem.IsWindows())
		{
			Assert.EndsWith(".minecraft", dir);
		}
	}

	[Fact]
	public void PlatformPaths_PortableMarker_FlipsBothDirectoriesUnderDataFolder()
	{
		// 便携模式由 EXE 同目录的 portable.flag 决定。测试程序集目录里没有这个文件，
		// 所以这里断言的是"非便携"分支；便携分支只能在真实发布产物里验（已由
		// verify-single-file-standalone.ps1 的冷启动用例覆盖，它就在 data/ 下取配置）。
		bool markerExists = File.Exists(Path.Combine(AppContext.BaseDirectory, "portable.flag"));
		string configDir = PlatformPaths.GetConfigDirectory();

		if (!markerExists)
		{
			Assert.DoesNotContain(Path.Combine(AppContext.BaseDirectory, "data"), configDir);
		}
		else
		{
			Assert.Contains(Path.Combine(AppContext.BaseDirectory, "data"), configDir);
		}
	}
}
