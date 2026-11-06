using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;

namespace Pineglow.Launcher.Services;

/// <summary>
/// 一条本地 API 路由的声明式定义。
///
/// **为什么要有这个类型（M1-1，2026-11-06）**：
/// 原先 <see cref="LocalApiService.HandleApiAsync"/> 用 516 行 `if (seg.Length == N &amp;&amp; seg[1].Equals(...))`
/// 手工分支堆出来。这种写法在 2026-10-03 出过一次真实事故：4 组要求长度 5/6/8 的守卫被嵌套进
/// 约束「长度 4」的块内，条件恒假 ⇒ 10 条路由永远落到末尾 404，而 796 个绿色测试毫无感知。
///
/// 根因不是"写错了"，而是这种结构**无法被机器检查**：路由集合只存在于代码的执行路径里，
/// 没有一份可以枚举、可以断言、可以对着文档核对的清单。
///
/// 改成表驱动后：
///   * 路由集合变成可枚举数据 → 端点清单测试能逐条断言可达性；
///   * 长度与段名约束在同一行内说完，不可能"嵌套出恒假条件"；
///   * 新增路由必须显式登记，漏登记会被清单测试抓到。
///
/// **匹配顺序仍然重要**，与手写 if 链一致：取**第一个**满足全部约束的路由。
/// 例如 `accounts/microsoft`（任意长度）必须排在 `accounts/microsoft/device` 之后，
/// `instances/{id}/diagnose/cancel` 必须排在通用 diagnose 之前。
/// </summary>
public sealed class ApiRoute
{
	/// <summary>HTTP 方法；<c>*</c> 表示不限制。</summary>
	public string Method { get; }

	/// <summary>段数下界（含）。段数含开头的 <c>api</c>，即 <c>/api/state</c> 为 2 段。</summary>
	public int MinSegments { get; }

	/// <summary>段数上界（含）。</summary>
	public int MaxSegments { get; }

	/// <summary>段索引 → 必须字面匹配的值（大小写不敏感）。未列出的索引可为任意值。</summary>
	public IReadOnlyDictionary<int, string> Literals { get; }

	/// <summary>业务意图（简短中文），用于清单测试与排障。不是给用户看的文案。</summary>
	public string Description { get; }

	/// <summary>处理器。参数为 HTTP 上下文与已解码的段数组。</summary>
	public Func<HttpListenerContext, string[], Task> Handler { get; }

	private ApiRoute(
		string method,
		int minSegments,
		int maxSegments,
		IReadOnlyDictionary<int, string> literals,
		string description,
		Func<HttpListenerContext, string[], Task> handler)
	{
		Method = method;
		MinSegments = minSegments;
		MaxSegments = maxSegments;
		Literals = literals;
		Description = description;
		Handler = handler;
	}

	/// <summary>精确段数 + 指定方法的路由。</summary>
	public static ApiRoute Exact(
		string method,
		int segments,
		string description,
		Func<HttpListenerContext, string[], Task> handler,
		params (int Index, string Value)[] literals)
		=> Build(method, segments, segments, description, handler, literals);

	/// <summary>段数区间（含两端）+ 指定方法的路由。用于 `any length ≥ N` 这类兜底。</summary>
	public static ApiRoute Range(
		string method,
		int minSegments,
		int maxSegments,
		string description,
		Func<HttpListenerContext, string[], Task> handler,
		params (int Index, string Value)[] literals)
		=> Build(method, minSegments, maxSegments, description, handler, literals);

	private static ApiRoute Build(
		string method,
		int minSegments,
		int maxSegments,
		string description,
		Func<HttpListenerContext, string[], Task> handler,
		(int Index, string Value)[] literals)
	{
		if (method != "*" && method != "GET" && method != "POST" && method != "DELETE" && method != "PUT")
		{
			throw new ArgumentException("未知 HTTP 方法：" + method, nameof(method));
		}

		if (minSegments < 2)
		{
			// 段 0 恒为 "api"；再短就不可能是 /api/* 请求。
			throw new ArgumentOutOfRangeException(nameof(minSegments), "段数至少为 2（api + 至少一段）");
		}

		if (maxSegments < minSegments)
		{
			throw new ArgumentOutOfRangeException(nameof(maxSegments), "上界不得小于下界");
		}

		var map = new Dictionary<int, string>();
		foreach ((int index, string value) in literals)
		{
			if (index < 1 || index > maxSegments)
			{
				throw new ArgumentOutOfRangeException(nameof(literals),
					$"字面段索引 {index} 超出 [{1}, {maxSegments}] 范围，该约束永远不可能被求值");
			}

			if (!map.TryAdd(index, value))
			{
				throw new ArgumentException($"字面段索引 {index} 重复声明", nameof(literals));
			}
		}

		return new ApiRoute(method, minSegments, maxSegments, map, description, handler);
	}

	/// <summary>判断一条具体请求是否命中本路由。调用方负责按表顺序取第一个命中项。</summary>
	public bool Matches(string method, string[] seg)
	{
		// 段 0 必须是 "api"。生产路径上 HandleAsync 已用 StartsWith("/api/") 把关，
		// 但路由表是可被单测直接调用的，这里也校验一次——否则 "not-api/state" 这类
		// 路径会命中 state 路由，等于把前缀校验的责任推给调用方。
		if (seg.Length < 2 || !string.Equals(seg[0], "api", StringComparison.OrdinalIgnoreCase))
		{
			return false;
		}

		if (Method != "*" && !string.Equals(Method, method, StringComparison.Ordinal))
		{
			return false;
		}

		if (seg.Length < MinSegments || seg.Length > MaxSegments)
		{
			return false;
		}

		foreach (KeyValuePair<int, string> pair in Literals)
		{
			if (!string.Equals(seg[pair.Key], pair.Value, StringComparison.OrdinalIgnoreCase))
			{
				return false;
			}
		}

		return true;
	}

	/// <summary>
	/// 把本路由的约束还原成可读路径模板，例如 <c>POST /api/instances/{id}/mods/{file}/toggle</c>。
	/// 用于端点清单测试失败时的定位信息，以及和 docs/api-reference.md 的核对。
	/// </summary>
	public string Describe()
	{
		var parts = new List<string> { "api" };
		for (int i = 1; i <= MaxSegments; i++)
		{
			if (Literals.TryGetValue(i, out string? literal))
			{
				parts.Add(literal);
			}
			else
			{
				// 用 {n} 而不是 {id}：不同位置的占位符语义不同，避免读者误以为是同一个变量。
				parts.Add("{" + i + "}");
			}
		}

		string path = "/" + string.Join('/', parts);
		if (MinSegments != MaxSegments)
		{
			path += MinSegments == MaxSegments - 1
				? "（或省略末段）"
				: $"（{MinSegments}~{MaxSegments} 段）";
		}

		return Method + " " + path;
	}

	/// <summary>供端点清单测试使用的稳定标识。</summary>
	public string Signature => Method + " " + MinSegments + "-" + MaxSegments + " "
		+ string.Join(',', Literals.OrderBy(p => p.Key).Select(p => p.Key + "=" + p.Value));
}
