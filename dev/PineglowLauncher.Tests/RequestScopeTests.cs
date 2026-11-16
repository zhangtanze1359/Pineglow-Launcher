using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Pineglow.Launcher.Models;
using Pineglow.Launcher.Services;
using Xunit;

namespace Pineglow.Launcher.Tests;

/// <summary>
/// RequestScope 的接口测试（架构评审 ②，2026-11-17）。
///
/// 这条 seam 的价值就在这里：**两个写策略现在可以在毫秒级、不起 HTTP 服务的情况下被测**。
/// 修复前要验证「运行中的实例不能写」，必须起真 HttpListener、造夹具、发 POST，
/// 而且每加一条路由都要再补一遍同样的测试。
/// </summary>
[Collection("LocalApiService")]
public sealed class RequestScopeTests
{
	private const string InstanceId = "scope-test";

	private static (AppConfig Config, GameInstance Instance) Fixture()
	{
		GameInstance instance = new GameInstance
		{
			Id = InstanceId,
			Name = "Scope",
			GameDirectory = @"C:\scope-game",
		};
		AppConfig config = new AppConfig
		{
			Instances = new List<GameInstance> { instance },
		};
		return (config, instance);
	}

	[Fact]
	public void Write_RejectsRunningInstance_WithTheProcessIdInTheMessage()
	{
		var (config, instance) = Fixture();
		RunningInstanceRegistry.ResetForTest();
		// 必须用**活着**的 pid：IsRunning 会探活并自愈掉已消失的条目（假 pid 会被当成"没在跑"）。
			RunningInstanceRegistry.MarkRunningPid(InstanceId, Environment.ProcessId);
		try
		{
			InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
				() => RequestScope.Write(config, instance, instance.GameDirectory));
			Assert.Contains("请勿重复启动", ex.Message);
			Assert.Contains(Environment.ProcessId.ToString(), ex.Message);
		}
		finally
		{
			RunningInstanceRegistry.ResetForTest();
		}
	}

	[Fact]
	public void Write_UsesTheRouteSpecificWording_WhenOneIsGiven()
	{
		var (config, instance) = Fixture();
		RunningInstanceRegistry.ResetForTest();
		RunningInstanceRegistry.MarkRunningPid(InstanceId, Environment.ProcessId);
		try
		{
			InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
				() => RequestScope.Write(config, instance, instance.GameDirectory,
					new ScopeWording { Busy = pid => $"该实例正在运行（进程 {pid}），请先退出游戏再删除存档。" }));
			Assert.Contains("先退出游戏再删除存档", ex.Message);
			Assert.Contains(Environment.ProcessId.ToString(), ex.Message);
			Assert.DoesNotContain("请勿重复启动", ex.Message);
		}
		finally
		{
			RunningInstanceRegistry.ResetForTest();
		}
	}

	[Fact]
	public void Write_RejectsDuringBisect_OnlyWhenTheRouteIsModScoped()
	{
		var (config, instance) = Fixture();
		RunningInstanceRegistry.ResetForTest();
		try
		{
			ModBisectService.GetOrStart(InstanceId, new[] { "a.jar", "b.jar" });

			// Mod 写入 / 实例移除：拒绝
			InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
				() => RequestScope.Write(config, instance, instance.GameDirectory, requireNoBisect: true));
			Assert.Contains("二分定位", ex.Message);

			// 存档写入：不受这条策略管辖（否则二分进行时推不动自己）
			RequestScope allowed = RequestScope.Write(config, instance, instance.GameDirectory);
			Assert.Equal(InstanceId, allowed.InstanceId);
		}
		finally
		{
			ModBisectService.Remove(InstanceId);
			RunningInstanceRegistry.ResetForTest();
		}
	}

	[Fact]
	public void Read_PerformsNoWritePolicyChecks()
	{
		var (config, instance) = Fixture();
		RunningInstanceRegistry.ResetForTest();
		RunningInstanceRegistry.MarkRunningPid(InstanceId, Environment.ProcessId);
		try
		{
			// 只读：实例正在跑也照常可读（F-04 的口径）
			RequestScope scope = RequestScope.Read(config, instance, InstanceId, instance.GameDirectory);
			Assert.Equal(InstanceId, scope.InstanceId);
			Assert.Equal(@"C:\scope-game", scope.GameDirectory);
		}
		finally
		{
			RunningInstanceRegistry.ResetForTest();
		}
	}
}
