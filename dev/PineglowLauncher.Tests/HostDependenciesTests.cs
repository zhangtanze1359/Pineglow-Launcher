using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Pineglow.Launcher.Models;
using Pineglow.Launcher.Services;
using Xunit;

namespace Pineglow.Launcher.Tests;

/// <summary>
/// 候选 ③ 第 2 步的结构与行为守卫：依赖作为构造参数进宿主。
///
/// <para>要守三件事：</para>
/// <list type="number">
/// <item>
/// <b>没有"半个依赖"的中间状态</b>：config store 与三个存储根必须一起换。
/// 旧写法四个独立静态 override 允许"换了三个、忘了第四个"，漏的那个就往真实
/// <c>%APPDATA%</c> 里写（第 47 轮实测：诊断报告一直在写）。这条用类型 + 反射两边钉。
/// </item>
/// <item>
/// <b>依赖只在未运行时替换</b>：运行中换依赖会让在途请求拿到"一半旧一半新"的存储。
/// 旧写法没有这条保护，只是没人试过。
/// </item>
/// <item>
/// <b>替换是可回滚的</b>：scope 释放后回到替换前那一份，不是回到"某个默认值"。
/// 夹具层层叠加隔离时，这一条决定它们会不会互相踩。
/// </item>
/// </list>
///
/// <para>
/// 本类里有一条会起真 listener 发 HTTP 请求，所以必须进串行集合
/// （<c>LocalApiService</c> 是进程级静态单例，同一时刻只能有一个 harness 活着）。
/// </para>
/// </summary>
[Collection("LocalApiService")]
public class HostDependenciesTests
{
	private static readonly string[] RemovedOverrideMethods =
	[
		"OverrideConfigStoreForTest",
		"OverrideRecoveryRootForTest",
		"OverrideBisectStoreRootForTest",
		"OverrideDiagnosticStoreRootForTest",
	];

	[Fact]
	public void LocalApiService_HasNoPerDependencyOverrideMethods()
	{
		// 四个独立 override 一旦回来，"只隔离了三个根"这个 bug 就又可能发生。
		// 反射而不是 grep：partial 里怎么写字面量都骗不过它。
		const BindingFlags flags = BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;
		string[] present = typeof(LocalApiService).GetMethods(flags)
			.Select(m => m.Name)
			.Where(name => RemovedOverrideMethods.Contains(name, StringComparer.Ordinal))
			.ToArray();

		Assert.True(present.Length == 0,
			$"按依赖拆开的 override 又出现了：{string.Join("、", present)}。" +
			"依赖只能通过 OverrideHostDependenciesForTest 整份替换——" +
			"拆开就允许「只隔离了其中几个」，漏掉的那些会写真实 %APPDATA%。");

		// 正向：那一个入口必须存在
		MethodInfo? unified = typeof(LocalApiService).GetMethod("OverrideHostDependenciesForTest", flags);
		Assert.NotNull(unified);
	}

	[Fact]
	public void HostDependencies_IsAtomic_EachHalfSurvivesTheOther()
	{
		var store = new RouteTestMemoryConfigStore(new AppConfig());
		var roots = new StorageRoots(@"C:\r", @"C:\b", @"C:\d");
		var deps = new HostDependencies(store, roots, new OutboundHttp());

		var otherStore = new RouteTestMemoryConfigStore(new AppConfig());
		var otherRoots = new StorageRoots(@"C:\r2", @"C:\b2", @"C:\d2");

		// 换 store：roots 与 http 不得被顺带改掉。
		HostDependencies storeSwapped = deps.WithStore(otherStore);
		Assert.Same(otherStore, storeSwapped.Store);
		Assert.Same(roots, storeSwapped.Roots);
		Assert.Same(deps.Http, storeSwapped.Http);

		// 换 roots：store 与 http 不得被顺带改掉。
		HostDependencies rootsSwapped = deps.WithRoots(otherRoots);
		Assert.Same(otherRoots, rootsSwapped.Roots);
		Assert.Same(store, rootsSwapped.Store);
		Assert.Same(deps.Http, rootsSwapped.Http);

		// 原对象不可变（链式调用不能把调用方手里那份改掉）。
		Assert.Same(roots, deps.Roots);
		Assert.Same(store, deps.Store);
	}

	[Fact]
	public void StorageRoots_RejectsEmptyAndProducesThreeDistinctDirectories()
	{
		Assert.Throws<ArgumentException>(() => new StorageRoots(string.Empty, "b", "d"));
		Assert.Throws<ArgumentException>(() => new StorageRoots("r", "  ", "d"));
		Assert.Throws<ArgumentException>(() => new StorageRoots("r", "b", null!));

		StorageRoots production = StorageRoots.Production();
		Assert.NotEqual(production.Recovery, production.Bisect, StringComparer.OrdinalIgnoreCase);
		Assert.NotEqual(production.Bisect, production.Diagnostics, StringComparer.OrdinalIgnoreCase);
		Assert.NotEqual(production.Recovery, production.Diagnostics, StringComparer.OrdinalIgnoreCase);

		// 三个根都必须落在配置目录下（游戏目录之外），否则删游戏会连恢复区一起删。
		string config = PlatformPaths.GetConfigDirectory();
		foreach (string root in new[] { production.Recovery, production.Bisect, production.Diagnostics })
		{
			Assert.StartsWith(config, root, StringComparison.OrdinalIgnoreCase);
		}
	}

	[Fact]
	public void OverrideHostDependencies_RestoresThePreviousValueOnDispose()
	{
		HostDependencies before = LocalApiService.CurrentHostDependenciesForTest;
		StorageRoots roots = TempRoots();

		using (LocalApiService.OverrideHostDependenciesForTest(deps => deps.WithRoots(roots)))
		{
			Assert.Same(roots, LocalApiService.CurrentHostDependenciesForTest.Roots);
			// store / http 必须"沿用"，而不是被重置成生产值 ——这是变形函数相对"传一份完整依赖"的价值。
			Assert.Same(before.Store, LocalApiService.CurrentHostDependenciesForTest.Store);
			Assert.Same(before.Http, LocalApiService.CurrentHostDependenciesForTest.Http);
		}

		Assert.Same(before, LocalApiService.CurrentHostDependenciesForTest);
	}

	[Fact]
	public void OverrideHostDependencies_RejectsNullMutationResult()
	{
		Assert.Throws<InvalidOperationException>(() =>
			LocalApiService.OverrideHostDependenciesForTest(_ => null!));
	}

	[Fact]
	public void LocalApiHost_RefusesToSwapDependenciesWhileRunning()
	{
		LocalApiHost host = new LocalApiHost(TestHostDependencies.TempRoots("swap"));
		using var probe = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
		probe.Start();
		int port = ((System.Net.IPEndPoint)probe.LocalEndpoint).Port;
		probe.Stop();

		host.Start(port, "ui");
		try
		{
			var ex = Assert.Throws<InvalidOperationException>(() =>
				host.ReplaceDependencies(TestHostDependencies.TempRoots("swap2")));
			Assert.Contains("运行中", ex.Message, StringComparison.Ordinal);
		}
		finally
		{
			host.Stop();
		}

		// 停止后可以换
		host.ReplaceDependencies(TestHostDependencies.TempRoots("swap3"));
		Assert.NotNull(host.Dependencies);
	}

	[Fact]
	public void HostDependencies_ProductionWiresRealStoreAndOutboundHttp()
	{
		HostDependencies production = HostDependencies.Production();
		Assert.IsType<ProductionConfigStore>(production.Store);
		Assert.NotNull(production.Http.Api);
		Assert.NotNull(production.Http.Download);
	}

	/// <summary>
	/// 接线证据：**一次**替换同时生效于配置存储与三个存储根，并且真的从 HTTP 请求里看得见。
	///
	/// <para>
	/// 这条不是"代码看起来接上了"：它会起真 listener、发真请求
	/// （<c>GET /api/state</c> 读配置、<c>POST /api/config</c> 写配置），然后断言
	/// 写落在注入的 store 里、根落在注入的三个目录上。
	/// 旧写法要分别证明四次，且没有任何东西能阻止你只证明了三次。
	/// </para>
	/// </summary>
	[Fact]
	public async Task OneOverride_WiresStoreAndAllThreeRoots_ThroughRealHttp()
	{
		var store = new RecordingConfigStore(BuildConfigWithTempInstance());
		await using LocalApiTestHarness harness = await LocalApiTestHarness.StartAsync(store, BuildConfigWithTempInstance());

		HostDependencies wired = LocalApiService.CurrentHostDependenciesForTest;
		Assert.Same(store, wired.Store);
		Assert.Equal(harness.RecoveryRoot, wired.Roots.Recovery, StringComparer.OrdinalIgnoreCase);
		Assert.Equal(harness.BisectStoreRoot, wired.Roots.Bisect, StringComparer.OrdinalIgnoreCase);
		Assert.Equal(harness.DiagnosticStoreRoot, wired.Roots.Diagnostics, StringComparer.OrdinalIgnoreCase);

		// 读路径：HTTP 200 且回显的是注入 store 里的内容（证明 LoadConfig 读的是 _host.Dependencies.Store）。
		(int status, string body) = await harness.SendAsync(HttpMethod.Get, "/api/state");
		Assert.Equal(200, status);
		Assert.Contains("recording-player", body, StringComparison.Ordinal);

		// 写路径：POST /api/config 必须落到注入的 store（而不是真实 config.json）。
		(int writeStatus, _) = await harness.SendAsync(HttpMethod.Post, "/api/config", new { playerName = "written-by-test" });
		Assert.Equal(200, writeStatus);
		Assert.True(store.SaveCount >= 1, "POST /api/config 没有调用注入 store 的 Save");
		Assert.Equal("written-by-test", store.Current.PlayerName);
	}

	private static AppConfig BuildConfigWithTempInstance()
	{
		string dir = Path.Combine(Path.GetTempPath(), "pineglow-hostdeps-inst-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(dir);
		var config = new AppConfig
		{
			GameDirectory = dir,
			PlayerName = "recording-player",
		};
		config.Instances.Add(new GameInstance
		{
			Id = "inst-hostdeps",
			Name = "hostdeps",
			GameDirectory = dir,
			MinecraftVersion = "1.20.1",
		});
		config.SelectedInstanceId = "inst-hostdeps";
		return config;
	}

	/// <summary>会记账的配置存储：能回答"HTTP 写有没有真的落到这里"。</summary>
	private sealed class RecordingConfigStore(AppConfig initial) : IConfigStore
	{
		public AppConfig Current { get; private set; } = initial;

		public int SaveCount { get; private set; }

		public AppConfig Load() => Current;

		public void Save(AppConfig config)
		{
			Current = config;
			SaveCount++;
		}
	}

	private static StorageRoots TempRoots()
	{
		string tag = Guid.NewGuid().ToString("N");
		string recovery = Path.Combine(Path.GetTempPath(), "pineglow-depstest-" + tag + "-rec");
		string bisect = Path.Combine(Path.GetTempPath(), "pineglow-depstest-" + tag + "-bis");
		string diagnostics = Path.Combine(Path.GetTempPath(), "pineglow-depstest-" + tag + "-dia");
		return new StorageRoots(recovery, bisect, diagnostics);
	}
}
