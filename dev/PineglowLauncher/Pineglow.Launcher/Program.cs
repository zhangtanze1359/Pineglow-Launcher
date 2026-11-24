using System;
using System.IO;
using Avalonia;
using Avalonia.Logging;

namespace Pineglow.Launcher;

internal static class Program
{
	[STAThread]
	public static void Main(string[] args)
	{
		// OPEN-08 诊断入口：让**发布产物自身**报告它是否带有构建期注入的 CurseForge Key。
		//
		// 为什么需要它：单文件 + EnableCompressionInSingleFile=true 会把主程序集压缩进包内，
		// 外部脚本（含 CI）无法用明文扫描判断 EXE 里到底有没有 Key（2026-11-24 复核实测）。
		// 直接问 EXE 自己是最可靠的判定 —— 它运行的就是将要发给用户的那份二进制。
		//
		// 契约：
		//   --print-curseforge-key-status   →  stdout 打印单行 "curseforge-key=present|absent"
		//                                     退出码 0 = present，2 = absent。
		//   **绝不输出 Key 的值或长度**，只回答"有没有"。
		// 该参数存在时**不启动 GUI**，也不初始化任何 Avalonia 组件。
		if (IsCurseForgeKeyStatusQuery(args))
		{
			Environment.Exit(PrintCurseForgeKeyStatus());
			return;
		}

		try
		{
			BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
		}
		catch (Exception ex)
		{
			try
			{
				string path = Path.Combine(AppContext.BaseDirectory, "crash.log");
				File.WriteAllText(path, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + Environment.NewLine + ex);
			}
			catch
			{
			}
			throw;
		}
	}

	public static AppBuilder BuildAvaloniaApp()
	{
		return AppBuilder.Configure<App>().UsePlatformDetect().WithInterFont()
			.LogToTrace(LogEventLevel.Warning);
	}

	/// <summary>诊断参数的开关名。CI 与发布门槛按这个名字调用。</summary>
	public const string CurseForgeKeyStatusArg = "--print-curseforge-key-status";

	private static bool IsCurseForgeKeyStatusQuery(string[] args)
	{
		foreach (string a in args)
		{
			if (string.Equals(a, CurseForgeKeyStatusArg, StringComparison.OrdinalIgnoreCase))
			{
				return true;
			}
		}
		return false;
	}

	/// <summary>
	/// 打印本进程所在程序集的 CurseForge Key 状态。**只报有/无，不报值、不报长度。**
	/// 返回退出码：0 = present，2 = absent。
	///
	/// 判定用 <see cref="CurseForgeKeyDefaults.ProvidedAtBuildTime"/>，
	/// 它内部已做 Trim —— 因此"只含空白的 Key"会被正确判为 absent（2026-11-24 复核反证过的情形）。
	/// </summary>
	private static int PrintCurseForgeKeyStatus()
	{
		bool present;
		try
		{
			present = Pineglow.Launcher.Services.CurseForgeKeyDefaults.ProvidedAtBuildTime;
		}
		catch
		{
			// 读元数据失败按 absent 处理，与运行期行为一致（产品会走缺 Key 引导）。
			present = false;
		}

		Console.Out.WriteLine("curseforge-key=" + (present ? "present" : "absent"));
		Console.Out.Flush();
		return present ? 0 : 2;
	}
}
