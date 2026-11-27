using System;
using Pineglow.Launcher.Services;
using Xunit;
using Xunit.Abstractions;

namespace Pineglow.Launcher.Tests;

/// <summary>
/// N09-09 补充（游戏退出显式提示）：退出登记与提示判定。
///
/// 背景：用户反馈「启动不了游戏、点了没反应」——游戏秒退时「ExitCode=…」只写
/// 启动器日志，界面无任何提示。候选改为：退出监听登记 <see cref="RunningInstanceRegistry.GameExitInfo"/>，
/// 前端观察窗口按事件号去重，对「秒退（&lt;15 秒）或退出码非 0」给一条持久提示。
///
/// 覆盖：秒退 + 非 0 退出码必须提示；事件号单调递增且 LastGameExit 指向最新
/// （前端去重依据）；正常长会话结束不提示；运行中崩溃（长会话非 0 码）仍提示；
/// /api/game-status 路由已登记（漏登记就是前端轮询 404、提示永不出现）。
///
/// 未覆盖：真实 WebView2 里 toast 的呈现与时长——由独立复验的桌面级实测取证。
/// </summary>
[Collection("LocalApiService")]
public class GameExitNoticeTests
{
	private readonly ITestOutputHelper _out;

	public GameExitNoticeTests(ITestOutputHelper output)
	{
		_out = output;
		RunningInstanceRegistry.ResetGameExitForTest();
		RunningInstanceRegistry.ResetForTest();
	}

	[Fact]
	public void RecordGameExit_QuickExitWithErrorCode_NeedsNoticeAndEventIdMonotonic()
	{
		// 秒退 34ms + ExitCode=1：即用户反馈的「点了没反应」现场，必须显式提示
		DateTimeOffset started = DateTimeOffset.Now;
		RunningInstanceRegistry.GameExitInfo first = RunningInstanceRegistry.RecordGameExit(
			"inst-a", 1, started, started.AddMilliseconds(34));
		_out.WriteLine($"[exit] first: eventId={first.EventId} durationMs={first.DurationMs:F1} quick={first.QuickExit} notice={first.NeedsNotice}");
		Assert.True(first.QuickExit);
		Assert.True(first.NeedsNotice);
		Assert.Equal(1, first.EventId);
		Assert.Equal(1, first.ExitCode);
		// 退出归属（第六轮修复）：启动预约时刻读取的事件号即历史分界
		Assert.Equal(1, RunningInstanceRegistry.CurrentExitEventSeq);

		// 第二次退出：事件号单调递增，LastGameExit 指向最新（前端按事件号去重）
		RunningInstanceRegistry.GameExitInfo second = RunningInstanceRegistry.RecordGameExit(
			"inst-b", 0, started, started.AddMilliseconds(200));
		Assert.Equal(2, second.EventId);
		Assert.Same(second, RunningInstanceRegistry.LastGameExit);
	}

	[Fact]
	public void RecordGameExit_NormalLongSessionSilent_ButMidSessionCrashStillNotifies()
	{
		DateTimeOffset started = DateTimeOffset.Now.AddHours(-1);

		// 正常游玩一小时后退出（ExitCode=0）：不打扰用户
		RunningInstanceRegistry.GameExitInfo normal = RunningInstanceRegistry.RecordGameExit(
			"inst-a", 0, started, started.AddMinutes(60));
		_out.WriteLine($"[exit] normal: quick={normal.QuickExit} notice={normal.NeedsNotice}");
		Assert.False(normal.QuickExit);
		Assert.False(normal.NeedsNotice);

		// 运行中崩溃（长会话但退出码非 0）：仍然要提示
		RunningInstanceRegistry.GameExitInfo crashed = RunningInstanceRegistry.RecordGameExit(
			"inst-a", 1, started, started.AddMinutes(10));
		Assert.False(crashed.QuickExit);
		Assert.True(crashed.NeedsNotice);
	}

	[Fact]
	public void RecordGameExit_ExitsAfter_ReturnsBatchWithoutLoss()
	{
		// 第七轮独立复验反例：两次退出都发生在两次轮询之间时，
		// 批次必须逐条返回、一条都不丢（只回最近一次会丢第一条的提示）
		DateTimeOffset started = DateTimeOffset.Now;
		RunningInstanceRegistry.RecordGameExit("inst-a", 1, started, started.AddMilliseconds(20));
		RunningInstanceRegistry.RecordGameExit("inst-b", 1, started, started.AddMilliseconds(30));
		var all = RunningInstanceRegistry.GetExitsAfter(0);
		Assert.Equal(2, all.Count);
		Assert.Equal(1, all[0].EventId);
		Assert.Equal(2, all[1].EventId);
		var later = RunningInstanceRegistry.GetExitsAfter(1);
		Assert.Single(later);
		Assert.Equal(2, later[0].EventId);
	}

	[Fact]
	public void RunningCount_TracksObservedGamesForWatchLifecycle()
	{
		// 架构审查反例（双开）：观察窗口按 runningCount 收摊——还有游戏在跑就不许停表
		RunningInstanceRegistry.MarkRunningForTest("inst-a", 4321);
		RunningInstanceRegistry.MarkRunningForTest("inst-b", 4322);
		Assert.Equal(2, RunningInstanceRegistry.Count);
		RunningInstanceRegistry.MarkExited("inst-a");
		Assert.Equal(1, RunningInstanceRegistry.Count);
		RunningInstanceRegistry.MarkExited("inst-b");
		Assert.Equal(0, RunningInstanceRegistry.Count);
	}

	[Fact]
	public void GameStatusRoute_IsRegistered()
	{
		// 前端观察窗口轮询的正是这条路由；漏登记 = 轮询 404、提示永不出现
		ApiRoute? route = LocalApiService.ResolveRoute("GET", new[] { "api", "game-status" });
		Assert.NotNull(route);
		Assert.Equal(2, route!.MaxSegments);
		Assert.Null(LocalApiService.ResolveRoute("POST", new[] { "api", "game-status" }));
	}
}
