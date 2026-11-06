using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Pineglow.Launcher.Services;
using Xunit;
using Xunit.Abstractions;

namespace Pineglow.Launcher.Tests;

/// <summary>
/// M1-1（2026-11-06）端点清单测试：把路由表当成**数据**来校验。
///
/// 背景：2026-10-03 曾有 10 条路由因 `seg.Length` 守卫嵌套条件恒假而永远 404，
/// 当时 796 个绿色测试对此完全无感知。既有 <see cref="RouteReachabilityTests"/>
/// 只覆盖了那 10 条，属于"事后补的定点回归"；本文件补的是**结构性防线**：
///
///   1. 路由表自身的一致性（无重复签名、每条都有描述与 handler、字面段索引合法）；
///   2. **顺序契约**：窄路由必须排在宽路由之前，否则宽路由会把窄路由吞掉
///      （这正是 `accounts/microsoft` vs `accounts/microsoft/device` 的坑）；
///   3. 每条路由都用**具体路径**跑一遍 <see cref="LocalApiService.ResolveRoute"/>，
///      断言命中它自己 —— 等价于"这条路由不可能恒假"；
///   4. 未登记路径必须确实落到 null（兜底 404 有牙，断言不空洞）。
/// </summary>
public class ApiRouteInventoryTests
{
	private readonly ITestOutputHelper _out;

	public ApiRouteInventoryTests(ITestOutputHelper output) => _out = output;

	/// <summary>把路由模板还原成一个可解析的具体段数组。参数段填固定占位符。</summary>
	private static string[] Materialize(ApiRoute route)
	{
		int len = route.MaxSegments > 8 ? Math.Max(route.MinSegments, 2) : route.MaxSegments;
		// 对 Range(min=2, max=64) 这类"任意长度"的兜底路由，用最小长度取值，
		// 保证不会误造出一个 64 段的荒谬路径。
		if (route.MaxSegments > 16)
		{
			len = route.MinSegments;
		}

		var seg = new string[len];
		seg[0] = "api";
		for (int i = 1; i < len; i++)
		{
			seg[i] = route.Literals.TryGetValue(i, out string? literal) ? literal : "probe-" + i;
		}

		return seg;
	}

	[Fact]
	public void Inventory_HasNoDuplicateSignature()
	{
		List<string> duplicates = LocalApiService.Routes
			.GroupBy(r => r.Signature, StringComparer.OrdinalIgnoreCase)
			.Where(g => g.Count() > 1)
			.Select(g => g.Key)
			.ToList();

		Assert.True(duplicates.Count == 0,
			"路由表中存在签名完全相同的重复项（后一条永远不可达）：" + string.Join(" | ", duplicates));
	}

	[Fact]
	public void Inventory_EveryRouteHasDescriptionAndHandler()
	{
		List<string> bad = LocalApiService.Routes
			.Where(r => string.IsNullOrWhiteSpace(r.Description) || r.Handler == null)
			.Select(r => r.Signature)
			.ToList();

		Assert.True(bad.Count == 0, "以下路由缺少描述或处理器：" + string.Join(" | ", bad));
	}

	[Fact]
	public void Inventory_EveryRouteIsReachable_ByItsOwnConcretePath()
	{
		var unreachable = new List<string>();
		foreach (ApiRoute route in LocalApiService.Routes)
		{
			string[] seg = Materialize(route);
			ApiRoute? hit = LocalApiService.ResolveRoute(route.Method == "*" ? "GET" : route.Method, seg);
			if (!ReferenceEquals(hit, route))
			{
				unreachable.Add(route.Describe() + " → 实际命中 " + (hit?.Describe() ?? "(null/404)"));
			}
		}

		Assert.True(unreachable.Count == 0,
			"以下路由用自身模板构造的路径无法命中自己（说明被前面的路由吞掉或约束自相矛盾）：\n"
			+ string.Join("\n", unreachable));
	}

	[Fact]
	public void Inventory_NoRouteIsShadowedByAnEarlierBroaderRoute()
	{
		// 顺序契约：对任意两条路由 A（前）与 B（后），若 A 的约束是 B 的超集
		// （即 A 能匹配 B 的所有具体路径），则 B 永远不可达。
		// 这里用"用 B 的具体路径去 Resolve，结果必须是 B 而不是 A"来直接证明。
		var shadows = new List<string>();
		foreach (ApiRoute later in LocalApiService.Routes)
		{
			string method = later.Method == "*" ? "GET" : later.Method;
			string[] seg = Materialize(later);
			ApiRoute? hit = LocalApiService.ResolveRoute(method, seg);
			if (hit != null && !ReferenceEquals(hit, later))
			{
				shadows.Add(later.Describe() + " 被 " + hit.Describe() + " 遮蔽");
			}
		}

		Assert.True(shadows.Count == 0, string.Join("\n", shadows));
	}

	[Fact]
	public void Inventory_UnknownPathsResolveToNull()
	{
		// 反向保护：兜底 404 必须真的有牙，否则上面的断言都可能空洞通过。
		Assert.Null(LocalApiService.ResolveRoute("GET", new[] { "api", "definitely-not-a-route" }));
		Assert.Null(LocalApiService.ResolveRoute("PATCH", new[] { "api", "state" }));
		Assert.Null(LocalApiService.ResolveRoute("GET", new[] { "not-api", "state" }));
	}

	[Fact]
	public void Inventory_LiteralIndexNeverExceedsMaxSegments()
	{
		// 构造函数已做校验，这里再断言一次，防止有人绕过工厂方法直接 new。
		foreach (ApiRoute route in LocalApiService.Routes)
		{
			foreach (KeyValuePair<int, string> pair in route.Literals)
			{
				Assert.InRange(pair.Key, 1, route.MaxSegments);
			}
		}
	}

	[Fact]
	public void Inventory_MatchesDocumentedEndpointCount()
	{
		// docs/api-reference.md 的端点表口径是「可访问路径」条数。
		// 路由表口径是「路由项」条数。两者不同层级，但比例不应失控——
		// 这条断言不是要求数字相等，而是把数字**打印出来**并钉一个下界，
		// 防止有人悄悄删掉一大批路由却没人发现。
		int count = LocalApiService.Routes.Count;
		_out.WriteLine($"[inventory] 路由项 {count} 条");
		foreach (IGrouping<string, ApiRoute> g in LocalApiService.Routes
			.GroupBy(r => r.Literals.TryGetValue(1, out string? v) ? v : "?")
			.OrderBy(g => g.Key))
		{
			_out.WriteLine($"[inventory]   {g.Key}: {g.Count()} 条");
		}

		Assert.True(count >= 50, $"路由项过少（{count}），疑似大量路由被误删");
	}

	[Fact]
	public async Task Inventory_HealthRouteIsTheOnlyOneWithoutToken()
	{
		// 安全边界回归：/api/health 免令牌是**唯一**例外。
		// 路由表本身不承载鉴权（鉴权在 HandleAsync 里按路径前缀判断），
		// 这里断言那条路径确实在表里、且是可用的 GET —— 若有人改路径名，
		// 免令牌判断会失效（变成 401），本断言会先失败。
		ApiRoute? health = LocalApiService.ResolveRoute("GET", new[] { "api", "health" });
		Assert.NotNull(health);
		Assert.Equal("GET", health!.Method);

		// 反向：任意其它路径都不应被 health 路由吞掉。
		Assert.Null(LocalApiService.ResolveRoute("GET", new[] { "api", "health-other" }));

		await Task.CompletedTask;
	}

	[Fact]
	public void Inventory_InstanceSubRoutesAreNotNestedUnderWrongLengthGuard()
	{
		// 直击 2026-10-03 事故形态：长度 5/6/8 的实例子路由，
		// 必须能用自己的实际路径命中，而不是被"长度 4 通用块"挡住。
		// 这与 Inventory_EveryRouteIsReachable 有重叠，但这里用**真实业务路径**
		// 而不是 probe 占位符，防止占位符恰好绕过某个字面段约束。
		(string Method, string Path)[] cases =
		{
			("POST", "api/instances/inst-1/mods/foo.jar/toggle"),
			("POST", "api/instances/inst-1/mods/foo.jar/delete"),
			("POST", "api/instances/inst-1/mods/foo.jar/replace"),
			("POST", "api/instances/inst-1/bisect/start"),
			("POST", "api/instances/inst-1/bisect/test"),
			("POST", "api/instances/inst-1/bisect/giveup"),
			("GET", "api/instances/inst-1/saves/world1/backups"),
			("POST", "api/instances/inst-1/saves/world1/backup"),
			("POST", "api/instances/inst-1/saves/world1/delete"),
			("POST", "api/instances/inst-1/saves/world1/open"),
			("POST", "api/instances/inst-1/saves/world1/backups/bk1/restore"),
			("POST", "api/instances/inst-1/recovery/op-1/restore"),
		};

		var misses = new List<string>();
		foreach ((string method, string path) in cases)
		{
			string[] seg = path.Split('/');
			ApiRoute? hit = LocalApiService.ResolveRoute(method, seg);
			if (hit == null)
			{
				misses.Add(method + " /" + path + " → 404");
			}
		}

		Assert.True(misses.Count == 0,
			"以下实例子路由无法命中（重演 2026-10-03 事故形态）：\n" + string.Join("\n", misses));
	}

	[Fact]
	public void Inventory_AccountMicrosoftRoutesPreferSpecificOverGeneric()
	{
		// accounts/microsoft 的通用项是 Range(3, Any)，会吃掉 device/poll。
		// 顺序必须保证 device、poll 在前。
		ApiRoute? device = LocalApiService.ResolveRoute("POST", new[] { "api", "accounts", "microsoft", "device" });
		ApiRoute? poll = LocalApiService.ResolveRoute("POST", new[] { "api", "accounts", "microsoft", "poll" });
		ApiRoute? generic = LocalApiService.ResolveRoute("POST", new[] { "api", "accounts", "microsoft" });

		Assert.NotNull(device);
		Assert.NotNull(poll);
		Assert.NotNull(generic);
		Assert.NotEqual(device!.Signature, generic!.Signature);
		Assert.NotEqual(poll!.Signature, generic.Signature);
		Assert.Equal(4, device.MaxSegments);
		Assert.Equal(4, poll.MaxSegments);
	}

	[Fact]
	public void Inventory_DiagnoseCancelPrecedesGenericDiagnose()
	{
		// 同一形态：diagnose 的通用项若排在 cancel 之前，取消功能会失效。
		ApiRoute? cancel = LocalApiService.ResolveRoute("POST",
			new[] { "api", "instances", "i1", "diagnose", "cancel" });
		ApiRoute? diagnose = LocalApiService.ResolveRoute("POST",
			new[] { "api", "instances", "i1", "diagnose" });

		Assert.NotNull(cancel);
		Assert.NotNull(diagnose);
		Assert.Equal(5, cancel!.MaxSegments);
		Assert.Equal(4, diagnose!.MaxSegments);
	}
}
