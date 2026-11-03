using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Pineglow.Launcher.Models;
using Pineglow.Launcher.Services;
using Xunit;
using Xunit.Abstractions;

namespace Pineglow.Launcher.Tests;

/// <summary>
/// MR03 补测（缺口登记表 A7 的剩余项）：**同一实例的并发启动守卫**。
///
/// MR03b 的 manifest `knownGaps` 原话："同一实例重复启动**没有并发守卫**（注册表以最后一次为准）"。
/// 本包把它补上并测掉：修复前 `HandleLaunchAsync` 只 `MarkRunning`、**从不检查 `IsRunning`**，
/// 于是同一实例能起两个游戏进程同时写同一份存档（真实的损坏风险），
/// 而删除守卫此后只跟踪最新的那个 PID。
///
/// **未覆盖**：真实启动 Minecraft（需要真实游戏与 Java）—— 本类只验证**守卫**本身：
/// 已在运行 → 拒绝；进程已退出 → 不拦；从未运行 → 不拦。
/// </summary>
[Collection("LocalApiService")]
public class InstanceLaunchGuardTests
{
	private readonly ITestOutputHelper _out;

	public InstanceLaunchGuardTests(ITestOutputHelper output) => _out = output;

	private sealed class Fixture : IDisposable
	{
		public string Root { get; }
		public string InstanceId { get; } = "launch-guard-inst";
		public AppConfig Config { get; }

		public Fixture()
		{
			Root = Path.Combine(Path.GetTempPath(), "pineglow-a7b-" + Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(Root);
			Config = new AppConfig
			{
				GameDirectory = Root,
				SelectedInstanceId = InstanceId,
				Instances = new List<GameInstance>
				{
					// 带版本号：让启动能走到"守卫之后"的流程，从而证明守卫**没有过度拦截**
					new GameInstance { Id = InstanceId, Name = "launch", GameDirectory = Root, MinecraftVersion = "1.20.4" }
				}
			};
		}

		public void Dispose()
		{
			try { RunningInstanceRegistry.MarkExited(InstanceId); } catch { }
			try { if (Directory.Exists(Root)) Directory.Delete(Root, true); } catch { }
		}
	}

	private static string? ErrorOf(string body)
	{
		try
		{
			using JsonDocument doc = JsonDocument.Parse(body);
			return doc.RootElement.TryGetProperty("error", out JsonElement e) && e.ValueKind == JsonValueKind.String
				? e.GetString()
				: null;
		}
		catch
		{
			return null;
		}
	}

	/// <summary>起一个立刻退出的进程，拿到"真实但已死"的 PID。</summary>
	private static int DeadPid()
	{
		using Process dead = Process.Start(new ProcessStartInfo("cmd.exe", "/c exit")
		{
			CreateNoWindow = true,
			UseShellExecute = false
		})!;
		dead.WaitForExit();
		return dead.Id;
	}

	/* ===================== ① 已在运行 → 必须拒绝 ===================== */

	[Fact]
	public async Task Launch_WhenInstanceAlreadyRunning_IsRejectedWithReadableError()
	{
		using var fx = new Fixture();
		// 用**存活**的 PID 标记为运行中（测试进程自己）
		RunningInstanceRegistry.MarkRunningForTest(fx.InstanceId, Environment.ProcessId);
		Assert.True(RunningInstanceRegistry.IsRunning(fx.InstanceId), "前置：注册表应报告该实例正在运行");

		await using var h = await LocalApiTestHarness.StartAsync(fx.Config);
		(int status, string body) = await h.SendAsync(HttpMethod.Post, "/api/instances/" + fx.InstanceId + "/launch", new { });

		string? error = ErrorOf(body);
		_out.WriteLine($"[A7b] 已在运行时 launch -> {status} error={error}");

		Assert.True(status == 400, $"同一实例重复启动必须被拒绝（400），实际 {status}");
		Assert.False(string.IsNullOrWhiteSpace(error), "必须带可读 error");
		Assert.Contains("已在运行", error!, StringComparison.Ordinal);
		Assert.Contains(Environment.ProcessId.ToString(), error!, StringComparison.Ordinal);
	}

	/* ===================== ② 进程已退出 → 不得被陈旧条目拦住 ===================== */

	[Fact]
	public async Task Launch_AfterTheProcessExited_IsNotBlockedByTheGuard()
	{
		using var fx = new Fixture();
		RunningInstanceRegistry.MarkRunningForTest(fx.InstanceId, DeadPid());

		await using var h = await LocalApiTestHarness.StartAsync(fx.Config);
		(int status, string body) = await h.SendAsync(HttpMethod.Post, "/api/instances/" + fx.InstanceId + "/launch", new { });

		string? error = ErrorOf(body);
		_out.WriteLine($"[A7b] 陈旧条目下 launch -> {status} error={error}");

		// 自愈之后守卫不应再拦；后面的失败（没有真实 Java/游戏）允许，但**不能**是"已在运行"
		Assert.False(error != null && error.Contains("已在运行", StringComparison.Ordinal),
			"进程已退出时守卫不得再拦（注册表应自愈）。实际 error=" + error);
	}

	/* ===================== ③ 从未运行 → 不得被拦住 ===================== */

	[Fact]
	public async Task Launch_WhenNeverStarted_IsNotBlockedByTheGuard()
	{
		using var fx = new Fixture();
		Assert.False(RunningInstanceRegistry.IsRunning(fx.InstanceId), "前置：该实例不应在运行");

		await using var h = await LocalApiTestHarness.StartAsync(fx.Config);
		(int status, string body) = await h.SendAsync(HttpMethod.Post, "/api/instances/" + fx.InstanceId + "/launch", new { });

		string? error = ErrorOf(body);
		_out.WriteLine($"[A7b] 未运行时 launch -> {status} error={error}");

		Assert.False(error != null && error.Contains("已在运行", StringComparison.Ordinal),
			"从未运行时守卫不得拦截。实际 error=" + error);
	}

	/* ===================== ④ 原子预约本身 ===================== */

	[Fact]
	public void TryBeginLaunch_SecondCallIsRejected_WhileTheFirstHoldsTheReservation()
	{
		string id = "atomic-" + Guid.NewGuid().ToString("N");
		try
		{
			Assert.True(RunningInstanceRegistry.TryBeginLaunch(id, out int first), "第一次预约必须成功");
			Assert.True(first == 0, "首次预约时没有占用者 PID");

			// 关键：**预约中**也必须被视为占用，否则第二次启动会挤进来
			Assert.True(RunningInstanceRegistry.IsRunning(id), "预约中的实例必须被视为占用");
			Assert.False(RunningInstanceRegistry.TryBeginLaunch(id, out int second), "第二次预约必须失败");
			Assert.True(second == 0, "占用者是「正在启动中」时 PID 为 0（而不是误报一个 PID）");

			// 释放后必须能再次预约
			RunningInstanceRegistry.AbortLaunch(id);
			Assert.False(RunningInstanceRegistry.IsRunning(id), "释放后不应再被视为占用");
			Assert.True(RunningInstanceRegistry.TryBeginLaunch(id, out _), "释放后必须能重新预约");
			_out.WriteLine("[A7c] 预约互斥成立：第二次 TryBeginLaunch 被拒；释放后可重新预约");
		}
		finally
		{
			RunningInstanceRegistry.AbortLaunch(id);
		}
	}

	[Fact]
	public void TryBeginLaunch_UnderConcurrency_ExactlyOneCallerWins()
	{
		string id = "atomic-race-" + Guid.NewGuid().ToString("N");
		try
		{
			// **顺序**调用只能验证互斥语义，抓不到"检查后再赋值"这类非原子实现。
			// 真正的原子性必须用**并发**调用验证：32 个线程同时抢，必须**恰好一个**赢。
			const int contenders = 32;
			int winners = 0;
			var gate = new object();

			Task[] tasks = Enumerable.Range(0, contenders).Select(_ => Task.Run(() =>
			{
				if (RunningInstanceRegistry.TryBeginLaunch(id, out _))
				{
					lock (gate)
					{
						winners++;
					}
				}
			})).ToArray();

			Task.WaitAll(tasks);

			_out.WriteLine($"[A7c] {contenders} 个并发 TryBeginLaunch，赢家数 = {winners}");
			Assert.True(winners == 1,
				$"并发预约必须**恰好一个**成功（原子性），实际 {winners} 个 —— 说明 TryBeginLaunch 不是原子的");
		}
		finally
		{
			RunningInstanceRegistry.AbortLaunch(id);
		}
	}

	[Fact]
	public void TryBeginLaunch_WithTheSeamOpened_ExactlyOneCallerWins()
	{
		string id = "atomic-barrier-" + Guid.NewGuid().ToString("N");
		const int contenders = 8;
		int winners = 0;
		var gate = new object();

		// 关键：把「检查」与「占位」之间的窗口**人为撑开** ——
		// 所有线程都必须先通过检查，再一起去占位。
		// 于是：原子实现（TryAdd）仍然恰好 1 个赢家；非原子实现（检查后再赋值）会**全部**赢。
		// 这就是第 43 轮缺失的那个"确定性可复现的 RED"。
		using var barrier = new System.Threading.Barrier(contenders);
		try
		{
			RunningInstanceRegistry.BeforeReserveForTest = () => barrier.SignalAndWait(TimeSpan.FromSeconds(10));

			Task[] tasks = Enumerable.Range(0, contenders).Select(_ => Task.Run(() =>
			{
				if (RunningInstanceRegistry.TryBeginLaunch(id, out _))
				{
					lock (gate)
					{
						winners++;
					}
				}
			})).ToArray();

			Task.WaitAll(tasks);

			_out.WriteLine($"[A7d] 屏障撑开竞争窗口后，{contenders} 个调用者的赢家数 = {winners}");
			Assert.True(winners == 1,
				$"把竞争窗口撑开后，原子实现必须**恰好 1 个**赢家，实际 {winners} 个 —— 说明 TryBeginLaunch 不是原子的");
		}
		finally
		{
			RunningInstanceRegistry.BeforeReserveForTest = null;
			RunningInstanceRegistry.AbortLaunch(id);
		}
	}

	[Fact]
	public void AbortLaunch_AfterUpgrade_DoesNotRemoveTheRunningEntry()
	{
		string id = "atomic-upgrade-" + Guid.NewGuid().ToString("N");
		try
		{
			Assert.True(RunningInstanceRegistry.TryBeginLaunch(id, out _));
			// 模拟"进程真的起来了"：MarkRunningPid 会把条目升级为 Launching=false
			RunningInstanceRegistry.MarkRunningPid(id, Environment.ProcessId);

			// 这正是外壳 finally 里无条件调用 AbortLaunch 的安全依据
			RunningInstanceRegistry.AbortLaunch(id);
			Assert.True(RunningInstanceRegistry.IsRunning(id, out int pid), "升级后的条目不得被 AbortLaunch 删掉");
			Assert.True(pid == Environment.ProcessId, "PID 应保持为真实进程号");
			_out.WriteLine("[A7c] 升级后 AbortLaunch 是 no-op（外壳 finally 可无条件调用）");
		}
		finally
		{
			RunningInstanceRegistry.MarkExited(id);
		}
	}

	/* ===================== ⑤ 启动失败必须释放预约（否则永久挡住） ===================== */

	[Fact]
	public async Task Launch_WhenItFailsAfterReserving_ReleasesTheReservation()
	{
		using var fx = new Fixture();
		await using var h = await LocalApiTestHarness.StartAsync(fx.Config);

		// 这个夹具没有版本 JSON → 启动会在**预约之后**失败
		(int firstStatus, string firstBody) = await h.SendAsync(HttpMethod.Post, "/api/instances/" + fx.InstanceId + "/launch", new { });
		string? firstError = ErrorOf(firstBody);
		_out.WriteLine($"[A7c] 第一次 launch -> {firstStatus} error={firstError}");

		// 关键：失败路径必须把预约释放掉，否则该实例会被**永久挡住**
		Assert.False(RunningInstanceRegistry.IsRunning(fx.InstanceId),
			"启动失败后预约必须被释放（AbortLaunch 在 finally 里）；否则该实例将永远无法再次启动");

		(int secondStatus, string secondBody) = await h.SendAsync(HttpMethod.Post, "/api/instances/" + fx.InstanceId + "/launch", new { });
		string? secondError = ErrorOf(secondBody);
		_out.WriteLine($"[A7c] 第二次 launch -> {secondStatus} error={secondError}");

		Assert.False(secondError != null && secondError.Contains("已在运行", StringComparison.Ordinal),
			"第一次失败之后第二次不得被当成「已在运行」。实际 error=" + secondError);
		Assert.False(secondError != null && secondError.Contains("正在启动中", StringComparison.Ordinal),
			"第一次失败之后不得残留「正在启动中」的预约。实际 error=" + secondError);
	}
}
