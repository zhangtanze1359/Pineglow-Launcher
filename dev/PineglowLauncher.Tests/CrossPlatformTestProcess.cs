using System;
using System.Diagnostics;

namespace Pineglow.Launcher.Tests;

/// <summary>
/// 跨平台替身进程：给「需要一个真实存在、且已经退出的进程」的测试用。
///
/// **由来（M0-4，2026-11-06）**：`main` 分支的 Ubuntu `dotnet test` 连续两次失败
/// （run 41598910035 @ `724fed50`、41599393945 @ `30f7b56e`），而 Windows / macOS
/// 同一运行均 success。定位到 5 个用例直接 `Process.Start("cmd.exe")`：
/// Linux 上没有 `cmd.exe`，`Process.Start` 会抛 `Win32Exception`（找不到文件），
/// 不是返回非零退出码。另一个用例更硬——它跑 `mklink /J` 并断言「必须创建成功」。
///
/// **为什么改这里而不是加平台守卫**：
/// 这些用例要的是「一个真实 PID，且该进程已经结束」，与 `cmd.exe` 本身无关。
/// 加 `if (!OperatingSystem.IsWindows()) return;` 会让它们在 Linux 上**静默通过**
/// ——而 xunit 2.9.3 没有动态 skip（`Assert.Skip` 是 v3 才引入的），
/// 静默 return 等于把"跳过"藏进绿色里，比失败更难发现。
/// 换成平台自带 shell 执行「立即退出」，语义完全不变且三平台可跑。
///
/// **不改变任何断言**：调用方仍然拿到「真实但已死」的进程，断言原文照旧。
/// </summary>
internal static class CrossPlatformTestProcess
{
	/// <summary>
	/// 启动一个立刻退出的进程并等待其结束，返回一个**仍然有效**的 <see cref="Process"/>
	/// （句柄可用、PID 真实、进程已退出）。
	/// </summary>
	public static Process StartExited()
	{
		ProcessStartInfo psi;
		if (OperatingSystem.IsWindows())
		{
			psi = new ProcessStartInfo("cmd.exe", "/c exit");
		}
		else
		{
			// Unix（Linux/macOS）都有 /bin/sh；用 ArgumentList 避免手工加引号。
			psi = new ProcessStartInfo("/bin/sh");
			psi.ArgumentList.Add("-c");
			psi.ArgumentList.Add("exit 0");
		}

		psi.CreateNoWindow = true;
		psi.UseShellExecute = false;

		Process started = Process.Start(psi)
			?? throw new InvalidOperationException("无法启动替身进程：" + psi.FileName);
		started.WaitForExit();
		return started;
	}

	/// <summary>
	/// 返回一个「真实但已死」的 PID（进程对象随即释放）。
	/// 供只需要 PID、不需要进程对象的用例使用。
	/// </summary>
	public static int StartAndReapPid()
	{
		using Process p = StartExited();
		return p.Id;
	}
}
