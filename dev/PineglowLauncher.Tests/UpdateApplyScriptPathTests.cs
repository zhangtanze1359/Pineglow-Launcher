using System;
using System.Diagnostics;
using System.IO;
using Pineglow.Launcher.Services;
using Xunit;
using Xunit.Abstractions;

namespace Pineglow.Launcher.Tests;

/// <summary>
/// R06 聚焦测试：**更新替换脚本在中文 + 空格安装路径下真的能跑完**。
///
/// 为什么单独补这一类：
///   - 既有 <c>UpdateRecoveryTests</c> 已经真实执行生成的替换脚本（备份 → 替换 → fc /b 校验 → 回滚 → 重启），
///     但用的都是 ASCII 临时目录；
///   - 而 R05 刚刚证明过本项目在 Windows 上踩过同一个坑：把含中文的路径写进
///     ASCII / ANSI 编码的脚本会让 cmd 直接报"文件名、目录名或卷标语法不正确"，
///     命令根本没执行，却可能被记成成功；
///   - 替换脚本是**唯一会覆盖用户正在使用的可执行文件**的路径，它在中文安装目录下是否可用
///     属于发版前必须真实验收的项，不能靠"BuildApplyScript 是纯函数"推理。
///
/// 本类用真实的 cmd.exe 执行产品生成的脚本（只把重启命令换成空操作/写标记文件），
/// 断言成功路径、失败回滚路径，以及重启命令确实被执行。
/// </summary>
public class UpdateApplyScriptPathTests
{
	private readonly ITestOutputHelper _out;

	public UpdateApplyScriptPathTests(ITestOutputHelper output)
	{
		_out = output;
	}

	private sealed record Outcome(string Output, string TargetContent, string? BackupContent, UpdateService.ApplyResult? State);

	private static string NewUnicodeDir()
	{
		string dir = Path.Combine(Path.GetTempPath(), "松光 更新 中文 路径 " + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(dir);
		return dir;
	}

	private Outcome RunApplyScript(string dir, string target, string downloaded, string backup, string state, string restart)
	{
		string scriptPath = Path.Combine(dir, "apply-update.bat");
		string script = UpdateService.BuildApplyScript(".bat", target, downloaded, backup, state, "1.0.1", restart);
		File.WriteAllText(scriptPath, script);

		ProcessStartInfo psi = new ProcessStartInfo("cmd.exe", "/c \"" + scriptPath + "\"")
		{
			UseShellExecute = false,
			CreateNoWindow = true,
			RedirectStandardOutput = true,
			RedirectStandardError = true
		};
		using Process proc = Process.Start(psi)!;
		string output = proc.StandardOutput.ReadToEnd() + proc.StandardError.ReadToEnd();
		proc.WaitForExit(30000);

		return new Outcome(
			output,
			File.Exists(target) ? File.ReadAllText(target) : string.Empty,
			File.Exists(backup) ? File.ReadAllText(backup) : null,
			UpdateService.ReadApplyResult(state));
	}

	[Fact]
	public void ApplyScript_InChineseAndSpacePath_AppliesReplacesAndRecordsApplied()
	{
		if (!OperatingSystem.IsWindows())
		{
			return; // 本用例针对 .bat 路径；Unix 分支结构相同但本机不可验证
		}

		string dir = NewUnicodeDir();
		try
		{
			string target = Path.Combine(dir, "Pineglow.Launcher.exe");
			string downloaded = Path.Combine(dir, "Pineglow-Launcher-1.0.1-win-x64-single-file.exe");
			string backup = target + ".bak-1.0.1";
			string state = Path.Combine(dir, "apply-state.json");
			string marker = Path.Combine(dir, "restart-marker.txt");
			File.WriteAllText(target, "OLD-CONTENT");
			File.WriteAllText(downloaded, "NEW-CONTENT");

			// 重启命令换成空操作 + 写一个标记文件，用来证明"重启那一行真的执行了"
			string restart = "echo RESTARTED> \"" + marker + "\"";
			Outcome outcome = RunApplyScript(dir, target, downloaded, backup, state, restart);
			_out.WriteLine($"[R06/upgrade] dir={dir}");
			_out.WriteLine($"[R06/upgrade] state={outcome.State?.Result} version={outcome.State?.Version} output={outcome.Output.Trim()}");

			Assert.Equal("applied", outcome.State?.Result);
			Assert.True(outcome.State!.Applied, "applied 必须为真");
			Assert.Equal("1.0.1", outcome.State.Version);
			Assert.Equal("NEW-CONTENT", outcome.TargetContent);
			Assert.Equal("OLD-CONTENT", outcome.BackupContent);
			Assert.True(File.Exists(marker), $"重启命令必须被执行（中文路径），output={outcome.Output}");
		}
		finally
		{
			try { Directory.Delete(dir, true); } catch { }
		}
	}

	[Fact]
	public void ApplyScript_InChineseAndSpacePath_ReplaceFailureRollsBackOldVersion()
	{
		if (!OperatingSystem.IsWindows())
		{
			return;
		}

		string dir = NewUnicodeDir();
		try
		{
			string target = Path.Combine(dir, "Pineglow.Launcher.exe");
			// 把"下载产物"做成目录 → copy /Y 必然失败，触发回滚分支
			string downloaded = Path.Combine(dir, "新版本 是目录");
			Directory.CreateDirectory(downloaded);
			string backup = target + ".bak-1.0.1";
			string state = Path.Combine(dir, "apply-state.json");
			string marker = Path.Combine(dir, "restart-marker.txt");
			File.WriteAllText(target, "OLD-CONTENT");

			Outcome outcome = RunApplyScript(dir, target, downloaded, backup, state, "echo RESTARTED> \"" + marker + "\"");
			_out.WriteLine($"[R06/upgrade-rollback] state={outcome.State?.Result} target={outcome.TargetContent}");

			Assert.Equal("replace_failed", outcome.State?.Result);
			Assert.False(outcome.State!.Applied);
			Assert.Equal("OLD-CONTENT", outcome.TargetContent);   // 回滚：旧版本内容原样
			Assert.Equal("OLD-CONTENT", outcome.BackupContent);
			Assert.True(File.Exists(marker), "失败路径也必须启动旧版本（保证用户仍有可运行启动器）");
		}
		finally
		{
			try { Directory.Delete(dir, true); } catch { }
		}
	}

	[Fact]
	public void ApplyScript_InChineseAndSpacePath_BackupFailureLeavesTargetUntouched()
	{
		if (!OperatingSystem.IsWindows())
		{
			return;
		}

		string dir = NewUnicodeDir();
		try
		{
			string target = Path.Combine(dir, "Pineglow.Launcher.exe");
			string downloaded = Path.Combine(dir, "Pineglow-Launcher-1.0.1-win-x64-single-file.exe");
			string backup = Path.Combine(dir, "不存在的备份目录", "launcher.bak");
			string state = Path.Combine(dir, "apply-state.json");
			File.WriteAllText(target, "OLD-CONTENT");
			File.WriteAllText(downloaded, "NEW-CONTENT");

			Outcome outcome = RunApplyScript(dir, target, downloaded, backup, state, "rem noop-restart");
			_out.WriteLine($"[R06/upgrade-backupfail] state={outcome.State?.Result} target={outcome.TargetContent}");

			Assert.Equal("backup_failed", outcome.State?.Result);
			Assert.Equal("OLD-CONTENT", outcome.TargetContent);
		}
		finally
		{
			try { Directory.Delete(dir, true); } catch { }
		}
	}

	[Fact]
	public void ApplyScript_DoesNotDependOnConsoleOnlyCommands()
	{
		// R06 实测缺陷回归位：替换脚本由**无控制台的 GUI 启动器**拉起，
		// `timeout /t 1` 依赖控制台输入，在 stdin 被接成管道时会永久阻塞（已复现），
		// 导致更新流程卡在等待循环里永远不替换。这里锁死"不得再用 timeout 做等待"。
		string script = UpdateService.BuildApplyScript(
			".bat", @"C:\安装 目录\launcher.exe", @"C:\安装 目录\new.exe", @"C:\安装 目录\launcher.exe.bak",
			@"C:\安装 目录\apply-state.json", "1.0.1", "start \"\" \"%TARGET%\"");
		Assert.DoesNotContain("timeout /t", script);
		Assert.Contains("ping -n 2 127.0.0.1 >nul", script);
		// 等待必须有上界，否则异常占用会永久卡住更新
		Assert.Contains("GTR " + UpdateService.MaxRunningProcessWaitIterations, script);

		// 代码页回归位（2026-10-06 新增）：脚本里含中文路径，而它由 File.WriteAllText 写成
		// **UTF-8 无 BOM**；cmd 会按**当时的 OEM 代码页**解析 .bat。所以脚本必须以纯 ASCII 开头、
		// 并在读到任何非 ASCII 之前切换到 UTF-8：
		//   line1 `@echo off`、line2 `chcp 65001` —— 这两行都是 ASCII，任何 OEM 代码页下都能正确执行。
		// 实测：把控制台代码页切成 **437**（英文 Windows / CI runner 形态）后重跑本类 7 个用例，
		// 全部通过且状态一致（`state=applied` / `state=replace_failed`）。
		// 结论：这两行是**承重结构**，不是装饰 —— 删掉 `chcp 65001`，中文安装路径就会在 437 下解析失败。
		Assert.StartsWith("@echo off" + "\r\n" + "chcp 65001", script);
	}

	[Fact]
	public void ApplyScript_WaitsForRunningTargetThenReplaces()
	{
		if (!OperatingSystem.IsWindows())
		{
			return;
		}

		string dir = NewUnicodeDir();
		Process? sleeper = null;
		try
		{
			string target = Path.Combine(dir, "Pineglow.Launcher.exe");
			string downloaded = Path.Combine(dir, "Pineglow-Launcher-1.0.1-win-x64-single-file.exe");
			string backup = target + ".bak-1.0.1";
			string state = Path.Combine(dir, "apply-state.json");

			// 用一个真实长跑进程占用目标文件名，模拟"启动器正在运行"时脚本必须等它退出。
			// ping.exe 的镜像名会被复制成 Pineglow.Launcher.exe，正好命中脚本的 IMAGENAME 过滤条件。
			string ping = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "ping.exe");
			File.Copy(ping, target, overwrite: true);
			File.WriteAllText(downloaded, "NEW-CONTENT");

			sleeper = Process.Start(new ProcessStartInfo(target, "-n 6 127.0.0.1")
			{
				UseShellExecute = false,
				CreateNoWindow = true,
				RedirectStandardOutput = true
			});
			Assert.NotNull(sleeper);
			System.Threading.Thread.Sleep(500);
			Assert.False(sleeper!.HasExited, "先决条件：目标进程必须在运行");

			Stopwatch sw = Stopwatch.StartNew();
			Outcome outcome = RunApplyScript(dir, target, downloaded, backup, state, "rem noop-restart");
			sw.Stop();
			_out.WriteLine($"[R06/upgrade-wait] state={outcome.State?.Result} waited={sw.Elapsed.TotalSeconds:N1}s");

			Assert.Equal("applied", outcome.State?.Result);
			Assert.Equal("NEW-CONTENT", outcome.TargetContent);
			Assert.True(sw.Elapsed >= TimeSpan.FromSeconds(2),
				$"脚本必须等目标进程退出后再替换，实测仅 {sw.Elapsed.TotalSeconds:N1}s（运行中的 exe 无法被覆盖）");
		}
		finally
		{
			try { if (sleeper != null && !sleeper.HasExited) sleeper.Kill(); } catch { }
			try { sleeper?.Dispose(); } catch { }
			try { Directory.Delete(dir, true); } catch { }
		}
	}

	[Fact]
	public void ApplyScript_WhenTargetIsNotWritable_KeepsRunnableOldVersion()
	{
		// R01 流程步骤 6 的**无权限**分支（此前是 NOT_RUN）：
		// 替换目标不可写时，脚本不得报 applied、必须把旧版本原样留着，并且仍然把旧版本拉起来。
		// 为什么必须真跑而不是推理：更新是唯一会覆盖"用户正在使用的那个 exe"的路径，
		// "没有写权限时会不会把用户搞成没有启动器"只能由真实 cmd 执行来回答。
		// 这里用只读属性让 OS 真的拒绝写入（cmd 的 copy 会得到 Access denied）。
		if (!OperatingSystem.IsWindows())
		{
			return; // 本用例针对 .bat 路径；Unix 分支用 chmod 模拟，本机不可验证
		}

		string dir = NewUnicodeDir();
		string target = Path.Combine(dir, "Pineglow.Launcher.exe");
		try
		{
			string downloaded = Path.Combine(dir, "Pineglow-Launcher-1.0.1-win-x64-single-file.exe");
			string backup = target + ".bak-1.0.1";
			string state = Path.Combine(dir, "apply-state.json");
			string marker = Path.Combine(dir, "restart-marker.txt");
			File.WriteAllText(target, "OLD-CONTENT");
			File.WriteAllText(downloaded, "NEW-CONTENT");
			File.SetAttributes(target, FileAttributes.ReadOnly);

			Outcome outcome = RunApplyScript(dir, target, downloaded, backup, state, "echo RESTARTED> \"" + marker + "\"");
			_out.WriteLine($"[R06/upgrade-noperm] state={outcome.State?.Result} target={outcome.TargetContent} output={outcome.Output.Trim()}");

			Assert.False(string.Equals(outcome.State?.Result, "applied", StringComparison.OrdinalIgnoreCase),
				$"目标不可写时不得报 applied（实际 {outcome.State?.Result}）");
			Assert.Equal("OLD-CONTENT", outcome.TargetContent);   // 旧版本原样保留，没有半截写入
			Assert.True(File.Exists(backup), "替换前必须留下备份，否则无法恢复");
			Assert.True(File.Exists(marker), "失败路径也必须启动旧版本：用户不能没有可运行的启动器");
		}
		finally
		{
			// 只读属性会让 Directory.Delete 失败，先复位
			try { File.SetAttributes(target, FileAttributes.Normal); } catch { }
			try { Directory.Delete(dir, true); } catch { }
		}
	}

	[Fact]
	public void ApplyScript_WhenTargetHasExplicitDenyAce_KeepsRunnableOldVersion()
	{
		// R01 流程步骤 6 的"无权限"分支，**第二种形态：显式拒绝 ACE**。
		// 为什么单列：上一个用例用的是只读**文件属性**（attrib R），这里是真正的**权限**（ACL deny），
		// 两者在 Windows 上走的是不同机制，失败路径不保证一样 —— 既然流程要求"无权限时仍可运行"，
		// 就不能只验一种形态。
		// 用 icacls 而不是 .NET 的 ACL API：后者在 net10.0 上要引 System.IO.FileSystem.AccessControl 包，
		// 那会改动 tests 的锁定还原文件（packages.lock.json），代价不值得。
		if (!OperatingSystem.IsWindows())
		{
			return;
		}

		string dir = NewUnicodeDir();
		string target = Path.Combine(dir, "Pineglow.Launcher.exe");
		bool denyApplied = false;
		try
		{
			string downloaded = Path.Combine(dir, "Pineglow-Launcher-1.0.1-win-x64-single-file.exe");
			string backup = target + ".bak-1.0.1";
			string state = Path.Combine(dir, "apply-state.json");
			string marker = Path.Combine(dir, "restart-marker.txt");
			File.WriteAllText(target, "OLD-CONTENT");
			File.WriteAllText(downloaded, "NEW-CONTENT");

			// 用**细粒度**的拒绝权限 (WD,AD,WA) = 写数据/追加数据/写属性。
			// 为什么不能用 icacls 的简写 (W)：实测 (W) 映射到 GENERIC_WRITE，其中含 SYNCHRONIZE，
			// 于是**连按读方式打开文件都会被拒**（.NET 的 File.ReadAllText 直接 UnauthorizedAccessException）——
			// 那是"拒绝一切打开"，不是"只拒绝写"，用它当无权限夹具会连结果都读不出来。
			if (!RunIcacls($"/deny \"{Environment.UserName}:(WD,AD,WA)\"", target))
			{
				// 前置条件建立不起来时不冒充验证过（本机/CI 正常都能建起来）
				_out.WriteLine("[R06/upgrade-deny-ace] 跳过：本机无法施加拒绝 ACE");
				return;
			}
			denyApplied = true;

			Outcome outcome = RunApplyScript(dir, target, downloaded, backup, state, "echo RESTARTED> \"" + marker + "\"");
			_out.WriteLine($"[R06/upgrade-deny-ace] state={outcome.State?.Result} target={outcome.TargetContent} output={outcome.Output.Trim()}");

			// 若这个拒绝 ACE 没生效，脚本会报 applied，下面第一条就会失败 ——
			// 也就是说这个用例不会"因为没生效而悄悄通过"。
			Assert.False(string.Equals(outcome.State?.Result, "applied", StringComparison.OrdinalIgnoreCase),
				$"目标被显式拒绝写入时不得报 applied（实际 {outcome.State?.Result}）");
			Assert.Equal("OLD-CONTENT", outcome.TargetContent);
			Assert.True(File.Exists(marker), "失败路径也必须启动旧版本：用户不能没有可运行的启动器");
		}
		finally
		{
			if (denyApplied)
			{
				RunIcacls($"/remove:d \"{Environment.UserName}\"", target);
			}
			try { Directory.Delete(dir, true); } catch { }
		}
	}

	/// <summary>对目标文件跑一次 icacls；返回是否 exit=0。输出只在失败时打印（不解析本地化文本）。</summary>
	private bool RunIcacls(string arguments, string path)
	{
		ProcessStartInfo psi = new ProcessStartInfo("icacls.exe", "\"" + path + "\" " + arguments)
		{
			UseShellExecute = false,
			CreateNoWindow = true,
			RedirectStandardOutput = true,
			RedirectStandardError = true
		};
		using Process proc = Process.Start(psi)!;
		string output = proc.StandardOutput.ReadToEnd() + proc.StandardError.ReadToEnd();
		proc.WaitForExit(15000);
		if (proc.ExitCode != 0)
		{
			_out.WriteLine($"[R06/upgrade-deny-ace] icacls {arguments} exit={proc.ExitCode} output={output.Trim()}");
			return false;
		}
		return true;
	}
}
