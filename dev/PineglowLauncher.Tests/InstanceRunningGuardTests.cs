using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using Pineglow.Launcher.Models;
using Pineglow.Launcher.Services;
using Xunit;
using Xunit.Abstractions;

namespace Pineglow.Launcher.Tests;

/// <summary>
/// MR03 收尾：「实例正在运行不得从列表移除」。
///
/// 背景：MR03 那一轮的验收里有这一条，但当时全仓**不存在**运行态记录
/// （`HandleLaunchAsync` 派发进程后不保留状态），所以只实现了 Bisect 活动守卫，
/// 并在报告里把它如实记为未达成项。本文件验证补上的这一块。
///
/// 覆盖：
///   1. 注册表语义（登记/清除/未知实例）；
///   2. **自愈**：进程已退出时不得留下陈旧条目（否则会永久挡住移除，比漏挡更糟）；
///   3. 路由守卫：运行中 → 400 且**配置未被改动**；清除后 → 200。
///
/// 明确不覆盖：真实启动 Minecraft 进程（需要真实游戏与 Java），属 MR22 内测范围。
/// </summary>
[Collection("LocalApiService")]
public class InstanceRunningGuardTests
{
	private readonly ITestOutputHelper _out;

	public InstanceRunningGuardTests(ITestOutputHelper output) => _out = output;

	private sealed class Fixture : IDisposable
	{
		public string Root { get; }
		public string InstanceId { get; } = "inst-" + Guid.NewGuid().ToString("N").Substring(0, 8);
		public AppConfig Config { get; }

		public Fixture()
		{
			Root = Path.Combine(Path.GetTempPath(), "pineglow-run-guard-" + Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(Path.Combine(Root, "mods"));
			Config = new AppConfig
			{
				GameDirectory = Root,
				SelectedInstanceId = InstanceId,
				Instances = new System.Collections.Generic.List<GameInstance>
				{
					new GameInstance { Id = InstanceId, Name = "运行守卫", GameDirectory = Root, MinecraftVersion = "1.20.4" }
				}
			};
		}

		public string Api(string suffix = "") => "/api/instances/" + InstanceId + suffix;

		public void Dispose()
		{
			try { RunningInstanceRegistry.MarkExited(InstanceId); } catch { }
			try { if (Directory.Exists(Root)) Directory.Delete(Root, true); } catch { }
		}
	}

	/* ===================== 1. 注册表语义 ===================== */

	[Fact]
	public void Registry_ReportsUnknownInstanceAsNotRunning()
	{
		RunningInstanceRegistry.ResetForTest();
		Assert.False(RunningInstanceRegistry.IsRunning("nobody-" + Guid.NewGuid().ToString("N"), out int pid));
		Assert.Equal(0, pid);
	}

	[Fact]
	public void Registry_ReportsARegisteredLiveProcessAsRunning()
	{
		RunningInstanceRegistry.ResetForTest();
		string id = "inst-" + Guid.NewGuid().ToString("N");

		// 用测试进程自己的 PID：确定"活着"
		RunningInstanceRegistry.MarkRunningPid(id, Environment.ProcessId);
		Assert.True(RunningInstanceRegistry.IsRunning(id, out int pid));
		Assert.Equal(Environment.ProcessId, pid);

		RunningInstanceRegistry.MarkExited(id);
		Assert.False(RunningInstanceRegistry.IsRunning(id, out _));
	}

	[Fact]
	public void Registry_SelfHeals_WhenTheProcessHasAlreadyExited()
	{
		RunningInstanceRegistry.ResetForTest();
		string id = "inst-" + Guid.NewGuid().ToString("N");

		// 起一个立刻退出的进程，拿到"真实但已死"的进程对象
		// M0-4：改用跨平台替身（原来是 cmd.exe，Linux 上抛 Win32Exception）。断言未变。
		using var dead = CrossPlatformTestProcess.StartExited();

		RunningInstanceRegistry.MarkRunning(id, dead);
		Assert.Equal(1, RunningInstanceRegistry.Count);

		// 关键：进程已退出 → 必须自愈，不能留下陈旧条目
		Assert.False(RunningInstanceRegistry.IsRunning(id, out _));
		Assert.Equal(0, RunningInstanceRegistry.Count);
	}

	[Fact]
	public void Registry_SelfHeals_ForAPidThatDoesNotExist()
	{
		RunningInstanceRegistry.ResetForTest();
		string id = "inst-" + Guid.NewGuid().ToString("N");

		// 起一个立刻退出的进程，用它已经消失的 PID 登记
		// M0-4：改用跨平台替身（原来是 cmd.exe，Linux 上抛 Win32Exception）。断言未变。
		using var dead = CrossPlatformTestProcess.StartExited();
		int deadPid = dead.Id;

		RunningInstanceRegistry.MarkRunningPid(id, deadPid);
		Assert.False(RunningInstanceRegistry.IsRunning(id, out _));
	}

	/* ===================== 2. 路由守卫 ===================== */

	[Fact]
	public async Task RemovingARunningInstance_IsRejected_AndConfigIsUntouched()
	{
		using var fx = new Fixture();
		await using var h = await LocalApiTestHarness.StartAsync(fx.Config);

		RunningInstanceRegistry.ResetForTest();
		RunningInstanceRegistry.MarkRunningForTest(fx.InstanceId, Environment.ProcessId);

		(int status, string body) = await h.SendAsync(HttpMethod.Post, fx.Api("/delete"), new { });
		_out.WriteLine($"[running] -> {status} body={body}");

		Assert.Equal(400, status);
		// 响应体是 JSON 转义的，必须解析后再比对中文
		using System.Text.Json.JsonDocument doc = System.Text.Json.JsonDocument.Parse(body);
		string message = doc.RootElement.GetProperty("error").GetString() ?? string.Empty;
		Assert.Contains("正在运行", message);
		Assert.Contains(Environment.ProcessId.ToString(), message);

		// 配置必须没被改动：实例还在
		(_, string listBody) = await h.SendAsync(HttpMethod.Get, "/api/instances");
		Assert.Contains(fx.InstanceId, listBody);
	}

	[Fact]
	public async Task RemovingAfterTheProcessExited_Succeeds()
	{
		using var fx = new Fixture();
		await using var h = await LocalApiTestHarness.StartAsync(fx.Config);

		RunningInstanceRegistry.ResetForTest();
		RunningInstanceRegistry.MarkRunningForTest(fx.InstanceId, Environment.ProcessId);

		(int blocked, _) = await h.SendAsync(HttpMethod.Post, fx.Api("/delete"), new { });
		Assert.Equal(400, blocked);

		// 游戏退出 → 退出监听会清掉登记
		RunningInstanceRegistry.MarkExited(fx.InstanceId);

		(int status, string body) = await h.SendAsync(HttpMethod.Post, fx.Api("/delete"), new { });
		_out.WriteLine($"[after exit] -> {status} body={body}");
		Assert.Equal(200, status);

		(_, string listBody) = await h.SendAsync(HttpMethod.Get, "/api/instances");
		Assert.DoesNotContain(fx.InstanceId, listBody);
	}

	[Fact]
	public async Task RemovingAnInstanceWhoseGameAlreadyDied_IsNotBlockedByAStaleEntry()
	{
		using var fx = new Fixture();
		await using var h = await LocalApiTestHarness.StartAsync(fx.Config);

		// 模拟"退出监听失败，留下了陈旧条目"：登记一个真实但已死的进程
		// M0-4：改用跨平台替身（原来是 cmd.exe，Linux 上抛 Win32Exception）。断言未变。
		using var dead = CrossPlatformTestProcess.StartExited();
		RunningInstanceRegistry.ResetForTest();
		RunningInstanceRegistry.MarkRunning(fx.InstanceId, dead);

		(int status, string body) = await h.SendAsync(HttpMethod.Post, fx.Api("/delete"), new { });
		_out.WriteLine($"[stale entry] -> {status} body={body}");

		// 自愈：陈旧条目不得永久挡住移除
		Assert.Equal(200, status);
	}
}
