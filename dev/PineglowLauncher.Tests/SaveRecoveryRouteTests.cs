using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using Pineglow.Launcher.Models;
using Xunit;

namespace Pineglow.Launcher.Tests;

/// <summary>
/// 存档恢复区（<c>/saves/recoveries</c>）的路由契约。
///
/// 由来（2026-11-17 真机九页验收抓出来的真实缺陷）：
///   恢复区三条路由是照抄"存档备份"的段数写的 —— 但备份是**按存档目录**的
///   （<c>saves/{folder}/backups</c>，6/8 段），恢复区是**实例级**的
///   （<c>saves/recoveries</c>，5/7 段）。段数多算了 1，于是前端
///   <c>GET /api/instances/{id}/saves/recoveries</c> 一直返回 404「未找到接口」，
///   世界列表页因此长期显示错误态——而所有单测都是绿的（没有任何用例发过这个 URL）。
///
/// 这条测试的价值不在"覆盖度"，在于**它走的是前端真实使用的那条 URL**。
/// </summary>
[Collection("LocalApiService")]
public sealed class SaveRecoveryRouteTests
{
	private sealed class Fixture : IDisposable
	{
		public Fixture()
		{
			Root = Path.Combine(Path.GetTempPath(), "pineglow-savereccov-" + Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(Root);
			GameDirectory = Path.Combine(Root, "game");
			Directory.CreateDirectory(Path.Combine(GameDirectory, "saves"));

			Instance = new GameInstance
			{
				Id = "reccov-inst",
				Name = "Recovery",
				GameDirectory = GameDirectory,
			};
			Config = new AppConfig
			{
				GameDirectory = GameDirectory,
				Instances = new List<GameInstance> { Instance },
				SelectedInstanceId = Instance.Id,
			};
		}

		public string Root { get; }

		public string GameDirectory { get; }

		public GameInstance Instance { get; }

		public AppConfig Config { get; }

		public string Api(string suffix) => "/api/instances/" + Instance.Id + suffix;

		public void Dispose()
		{
			try { Directory.Delete(Root, recursive: true); } catch { }
		}
	}

	[Fact]
	public async Task SaveRecoveryList_IsReachableAtTheUrlTheFrontendUses()
	{
		using Fixture fx = new Fixture();
		await using LocalApiTestHarness h = await LocalApiTestHarness.StartAsync(fx.Config);

		(int status, string body) = await h.SendAsync(HttpMethod.Get, fx.Api("/saves/recoveries"));
		Assert.Equal(200, status);
		Assert.DoesNotContain("未找到接口", body);
		Assert.Contains("recoveries", body);
	}

	[Fact]
	public async Task SaveRecoveryRestore_IsRouted_AndRejectsAnUnknownOperation()
	{
		using Fixture fx = new Fixture();
		await using LocalApiTestHarness h = await LocalApiTestHarness.StartAsync(fx.Config);

		(int status, string body) = await h.SendAsync(
			HttpMethod.Post, fx.Api("/saves/recoveries/nope/restore"), new { });

		// 路由必须命中（不是 404「未找到接口」）；具体状态取决于"记录不存在"的业务判定。
		Assert.DoesNotContain("未找到接口", body);
		Assert.NotEqual(404, status);
	}
}
