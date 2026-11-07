using System;
using System.Collections.Generic;
using System.Linq;
using Pineglow.Launcher.Services;
using Xunit;
using Xunit.Abstractions;

namespace Pineglow.Launcher.Tests;

/// <summary>
/// 反遮挡回归（R5-02 补齐）：表驱动路由是「取第一个命中」，所以一条更宽的路由若排在
/// 更窄的路由前面，就会把后者吞掉——被吞的那条永远不可达，而测试仍可能全绿。
///
/// 2026-10-03 的真实事故（10 条路由恒 404）就是这一类的静态版本；改造为路由表后，
/// 端点清单测试只覆盖了「登记的路径可达」，没有覆盖「任意一条登记的路径都不会被前面的路由抢走」。
/// 本测试对每条路由构造一个它必然命中的代表路径，断言 ResolveRoute 解析回的正是它自己。
/// </summary>
public sealed class RouteShadowingTests
{
	private readonly ITestOutputHelper _out;

	public RouteShadowingTests(ITestOutputHelper output) => _out = output;

	[Fact]
	public void EveryRegisteredRouteResolvesToItself()
	{
		var routes = LocalApiService.Routes;
		var violations = new List<string>();

		for (int i = 0; i < routes.Count; i++)
		{
			ApiRoute route = routes[i];
			string method = route.Method == "*" ? "GET" : route.Method;

			for (int len = route.MinSegments; len <= route.MaxSegments; len++)
			{
				// 该长度下无法构造出命中本路由的路径（有字面段落在长度之外）。
				if (route.Literals.Keys.Any(k => k >= len))
				{
					continue;
				}

				string[] seg = BuildRepresentativePath(route, len);
				if (!route.Matches(method, seg))
				{
					violations.Add($"[{i}] {route.Signature} 自身 Matches 返回 false（构造路径 {string.Join('/', seg)}）——测试构造器有 bug");
					continue;
				}

				ApiRoute? hit = LocalApiService.ResolveRoute(method, seg);
				if (!ReferenceEquals(hit, route))
				{
					int hitIndex = hit is null ? -1 : IndexOf(routes, hit);
					violations.Add(
						$"[{i}] {route.Signature} 被 [{hitIndex}] {hit?.Signature ?? "<null>"} 抢走：" +
						$"{method} /{string.Join('/', seg)}");
				}
			}
		}

		_out.WriteLine($"[shadowing] 检查 {routes.Count} 条路由，违规 {violations.Count} 处");
		Assert.True(violations.Count == 0,
			"存在被前面路由遮挡（永远不可达）的登记：" + Environment.NewLine + string.Join(Environment.NewLine, violations));
	}

	private static string[] BuildRepresentativePath(ApiRoute route, int length)
	{
		var seg = new string[length];
		seg[0] = "api";
		for (int k = 1; k < length; k++)
		{
			seg[k] = route.Literals.TryGetValue(k, out string? literal) ? literal : "x" + k;
		}

		return seg;
	}

	private static int IndexOf(IReadOnlyList<ApiRoute> routes, ApiRoute target)
	{
		for (int i = 0; i < routes.Count; i++)
		{
			if (ReferenceEquals(routes[i], target))
			{
				return i;
			}
		}

		return -1;
	}
}
