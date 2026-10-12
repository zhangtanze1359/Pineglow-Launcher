using System;
using System.Threading;
using Pineglow.Launcher.Services;

namespace VerifyHost
{
	internal static class Program
	{
		private static void Main(string[] args)
		{
			// 日志统一按 UTF-8 写：这是**被脚本采集的**命令行输出，
			// 重定向时 .NET 默认用 OEM 代码页，会让中文标签在按 UTF-8 读取的日志里变成乱码，
			// 进而让"按证据串判定"的门槛误判（本轮实测过）。
			try
			{
				Console.OutputEncoding = System.Text.Encoding.UTF8;
			}
			catch
			{
			}

			for (int i = 0; i < args.Length; i++)
			{
				if (args[i] == "--bisect-smoke")
				{
					Environment.ExitCode = BisectSmoke.Run(args);
					return;
				}
			}

			// 指向源码 ui/ 目录，作为静态资源根
			Environment.SetEnvironmentVariable("PINEGLOW_UI_DIR", @"d:\松光启动器\ui");
			Console.WriteLine("[VerifyHost] starting LocalApiService...");
			LocalApiService.Start();
			Console.WriteLine("[VerifyHost] listening, press Ctrl+C to stop.");
			using var done = new ManualResetEventSlim(false);
			Console.CancelKeyPress += (_, e) => { e.Cancel = true; done.Set(); };
			done.Wait();
			LocalApiService.Stop();
		}
	}
}