using System.Linq;
using Pineglow.Launcher.Services;

namespace Pineglow.Launcher.Tests;

/// <summary>ModBisectService 使用进程级静态会话字典，测试必须串行执行。</summary>
[CollectionDefinition("ModBisect", DisableParallelization = true)]
public class ModBisectCollectionDefinition
{
}

[Collection("ModBisect")]
public class ModBisectServiceTests
{
	[Fact]
	public void Start_WithEightMods_DisablesFourOnFirstStep()
	{
		var session = ModBisectSession.Start("inst-1", Enumerable.Range(1, 8).Select(i => $"mod{i}.jar"));

		var disabled = session.DisableHalfForNextTest();

		Assert.Equal(1, session.Step);
		Assert.True(session.WaitingForReport);
		Assert.Equal(4, disabled.Count);
		Assert.Equal(8, session.Candidates.Count); // 候选在 ReportTest 后才收窄
		Assert.Equal(4, session.DisabledHalf.Count);
		Assert.Equal(8, session.EnabledAtStart.Count);
		Assert.Equal("inst-1", session.TargetInstance);
	}

	[Fact]
	public void NotCrashed_LocksCandidatesToDisabledHalf()
	{
		var session = ModBisectSession.Start("inst-1", Enumerable.Range(1, 8).Select(i => $"mod{i}.jar"));
		var disabled = session.DisableHalfForNextTest();

		session.ReportTest(crashed: false);

		Assert.False(session.WaitingForReport);
		Assert.Equal(disabled, session.Candidates);
	}

	[Fact]
	public void Crashed_KeepsCandidatesOutsideDisabledHalf()
	{
		var session = ModBisectSession.Start("inst-1", Enumerable.Range(1, 8).Select(i => $"mod{i}.jar"));
		var disabled = session.DisableHalfForNextTest();

		session.ReportTest(crashed: true);

		Assert.Equal(Enumerable.Range(1, 8).Select(i => $"mod{i}.jar").Except(disabled), session.Candidates);
	}

	[Fact]
	public void EightMods_FullFlow_NarrowsToSingleCulprit()
	{
		var session = ModBisectSession.Start("inst-1", Enumerable.Range(1, 8).Select(i => $"mod{i}.jar"));

		// 轮 1：禁用 1-4，仍然崩溃 → 嫌疑 5-8
		session.DisableHalfForNextTest();
		session.ReportTest(crashed: true);

		// 轮 2：禁用 5-6，仍然崩溃 → 嫌疑 7-8
		var disabled2 = session.DisableHalfForNextTest();
		Assert.Equal(new[] { "mod5.jar", "mod6.jar" }, disabled2);
		session.ReportTest(crashed: true);

		// 轮 3：禁用 7，不崩 → 锁定 7
		var disabled3 = session.DisableHalfForNextTest();
		Assert.Equal(new[] { "mod7.jar" }, disabled3);
		session.ReportTest(crashed: false);

		Assert.True(session.Finished);
		Assert.Equal("mod7.jar", session.Culprit);
		Assert.Null(session.FinishReason);
		Assert.False(session.WaitingForReport);
	}

	[Fact]
	public void TwoMods_SingleReport_IdentifiesCulprit()
	{
		var session = ModBisectSession.Start("inst-1", new[] { "a.jar", "b.jar" });

		var disabled = session.DisableHalfForNextTest();
		Assert.Equal(new[] { "a.jar" }, disabled);

		session.ReportTest(crashed: false);

		Assert.True(session.Finished);
		Assert.Equal("a.jar", session.Culprit);
	}

	[Fact]
	public void SingleMod_StartsFinished()
	{
		var session = ModBisectSession.Start("inst-1", new[] { "only.jar" });

		var disabled = session.DisableHalfForNextTest();

		Assert.Empty(disabled);
		Assert.True(session.Finished);
		Assert.Equal("only.jar", session.Culprit);
	}

	[Fact]
	public void NoMods_StartsFinishedWithReason()
	{
		var session = ModBisectSession.Start("inst-1", Enumerable.Empty<string>());

		var disabled = session.DisableHalfForNextTest();

		Assert.Empty(disabled);
		Assert.True(session.Finished);
		Assert.Null(session.Culprit);
		Assert.Equal("未发现任何启用的 Mod", session.FinishReason);
	}

	[Fact]
	public void GiveUp_FinishesWithoutCulprit()
	{
		var session = ModBisectSession.Start("inst-1", Enumerable.Range(1, 4).Select(i => $"mod{i}.jar"));
		session.DisableHalfForNextTest();

		session.GiveUp();

		Assert.True(session.Finished);
		Assert.Null(session.Culprit);
		Assert.Equal("用户放弃", session.FinishReason);
	}

	[Fact]
	public void ReportWithoutWaiting_IsIgnored()
	{
		var session = ModBisectSession.Start("inst-1", new[] { "a.jar", "b.jar" });

		session.ReportTest(crashed: false);

		Assert.False(session.Finished);
		Assert.Equal(2, session.Candidates.Count);
	}

	[Fact]
	public void GetOrStart_ReplacesExistingSession()
	{
		ModBisectSession first = ModBisectService.GetOrStart("inst-1", new[] { "a.jar", "b.jar" });
		first.DisableHalfForNextTest();

		ModBisectSession second = ModBisectService.GetOrStart("inst-1", new[] { "c.jar" });

		Assert.Equal(0, second.Step);
		Assert.Equal(new[] { "c.jar" }, second.Candidates);
		Assert.NotSame(first, second);
	}

	[Fact]
	public void Remove_ClearsSession()
	{
		ModBisectService.GetOrStart("inst-1", new[] { "a.jar" });
		Assert.NotNull(ModBisectService.Get("inst-1"));

		ModBisectService.Remove("inst-1");

		Assert.Null(ModBisectService.Get("inst-1"));
	}

	[Fact]
	public void Get_MissingSession_ReturnsNull()
	{
		Assert.Null(ModBisectService.Get("does-not-exist"));
	}
}