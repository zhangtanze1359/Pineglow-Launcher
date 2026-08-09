using System;
using System.IO;

namespace Pineglow.Launcher.Services;

/// <summary>
/// 轻量日志落盘：按天轮转 launcher-yyyyMMdd.log，保留最近 7 天，线程安全，失败不致命。
/// 目录：%AppData%\MCMiniLauncher\logs（便携模式为 data\logs）。
/// </summary>
public static class LauncherLog
{
	private static readonly object Gate = new object();
	private static string _dir = string.Empty;
	private static string _file = string.Empty;
	private static StreamWriter? _writer;
	private static int _lastDay = -1;

	public static void Init(string? directory = null)
	{
		lock (Gate)
		{
			try
			{
				_dir = directory ?? Path.Combine(PlatformPaths.GetConfigDirectory(), "logs");
				Directory.CreateDirectory(_dir);
				RolloverLocked();
				CleanupOldLocked();
			}
			catch
			{
				_writer = null;
			}
		}
	}

	public static void Info(string message) => Write("INFO", message);

	public static void Warn(string message) => Write("WARN", message);

	public static void Error(string message) => Write("ERROR", message);

	public static void Error(string message, Exception ex) => Write("ERROR", message + " | " + ex);

	public static string GetLogDirectory() => _dir;

	public static string GetCurrentLogFile() => _file;

	private static void Write(string level, string message)
	{
		lock (Gate)
		{
			try
			{
				if (_writer == null)
				{
					return;
				}
				if (DateTime.Now.DayOfYear != _lastDay)
				{
					RolloverLocked();
				}
				_writer.WriteLine($"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}");
				_writer.Flush();
			}
			catch
			{
				// 日志写入失败不影响主流程
			}
		}
	}

	private static void RolloverLocked()
	{
		try
		{
			string file = Path.Combine(_dir, "launcher-" + DateTime.Now.ToString("yyyyMMdd") + ".log");
			if (_writer != null && string.Equals(_file, file, StringComparison.Ordinal))
			{
				_lastDay = DateTime.Now.DayOfYear;
				return;
			}
			_writer?.Dispose();
			_file = file;
			_lastDay = DateTime.Now.DayOfYear;
			_writer = new StreamWriter(file, append: true) { AutoFlush = true };
		}
		catch
		{
			_writer = null;
		}
	}

	private static void CleanupOldLocked()
	{
		try
		{
			DateTime cutoff = DateTime.Now.AddDays(-7);
			foreach (string file in Directory.GetFiles(_dir, "launcher-*.log"))
			{
				if (File.GetLastWriteTime(file) < cutoff)
				{
					try
					{
						File.Delete(file);
					}
					catch
					{
					}
				}
			}
		}
		catch
		{
		}
	}
}
