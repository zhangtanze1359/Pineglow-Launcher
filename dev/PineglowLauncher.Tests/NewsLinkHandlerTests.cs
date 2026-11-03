using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Pineglow.Launcher.Models;
using Pineglow.Launcher.Services;
using Xunit;

namespace Pineglow.Launcher.Tests;

/// <summary>
/// R2-03：新闻外链系统浏览器闭环 — 真实 HTTP handler 契约测试（review-fix-1 RED 重构版）。
///
/// review-fix-1 关键修正（phase3a-r203-red-quality-rework-2026-09-07.md §2）：
///   - 用反射注入受控 NewsService 内存 cache（固定至少一条合法官方 URL + 一条非法 URL），
///     不依赖实时 feed、缓存内容或 items[0] 排序
///   - 401 token 测试标为"已有保护"（不计入 R2-03 RED）
///   - 区分：业务 400/404 vs 路由 404；携带 raw URL 拒绝；opener 调用（受控）
///
/// RED 分类报告（在交付报告中按以下分类）：
///   1. 独立 RED（行为缺口）：handler 路由缺失、linkId 字段缺失、业务级 400/404、raw URL 拒绝
///   2. 已有保护：401 token 校验（既存）
///   3. 测试设施错误：反射注入失败（不计入 RED，让测试 FAIL 但归类）
/// </summary>
[Collection("LocalApiService")]
public class NewsLinkHandlerTests
{
	/* ===========================================================
	 * 0. 受控新闻 fixture：固定 1 条合法 + 1 条非法 + 1 条非白名单
	 * =========================================================== */

	private static NewsFetchResult BuildFixture(DateTimeOffset now)
	{
		IReadOnlyList<MinecraftNewsItem> items = new[]
		{
			new MinecraftNewsItem
			{
				Id = "fx-allowed-1",
				Title = "Allowed Official Article",
				Category = "Minecraft",
				Date = "2026-09-05T00:00:00Z",
				Text = "fixture",
				ImageUrl = string.Empty,
				Link = "https://www.minecraft.net/article/fixture-allowed",
			},
			new MinecraftNewsItem
			{
				Id = "fx-not-allowed-1",
				Title = "Non Official Link",
				Category = "Minecraft",
				Date = "2026-09-05T00:00:00Z",
				Text = "fixture",
				ImageUrl = string.Empty,
				Link = "https://evil.example.com/x",
			},
		};
		return new NewsFetchResult(
			Items: items,
			Source: "fixture://r203-controlled",
			State: NewsContentState.Live,
			ValidatedAt: now,
			FetchedAt: now,
			ETag: null,
			LastModified: null,
			IsStale: false,
			CanRetry: false,
			ErrorKind: null,
			Error: null);
	}

	/* ===========================================================
	 * 1. /api/news 响应契约：linkOk=true 的项必须有 linkId
	 *
	 * 不依赖 items[0：扫描整个数组找 linkOk=true 的 fixture 项
	 * =========================================================== */

	[Fact]
	public async Task News_HasLinkIdField_ForAnyLinkOkTrueItem()
	{
		await using TestHarness harness = await TestHarness.StartAsync();
		try
		{
			using HttpResponseMessage resp = await harness.GetAsync("/api/news");
			Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
			string body = await resp.Content.ReadAsStringAsync();
			using JsonDocument doc = JsonDocument.Parse(body);

			Assert.True(
				doc.RootElement.TryGetProperty("items", out JsonElement items),
				$"/api/news 响应缺少 items 数组：{body}");
			Assert.Equal(JsonValueKind.Array, items.ValueKind);

			// 不假定 items[0]：扫描整数组找 linkOk=true 的项
			bool foundLinkOkTrue = false;
			foreach (JsonElement item in items.EnumerateArray())
			{
				if (!item.TryGetProperty("linkOk", out JsonElement linkOkEl)) continue;
				if (linkOkEl.ValueKind != JsonValueKind.True) continue;
				foundLinkOkTrue = true;
				Assert.True(
					item.TryGetProperty("linkId", out JsonElement linkIdEl),
					$"linkOk=true 的项必须包含 linkId 字段。R2-03 RED 预期失败（当前 /api/news 响应缺 linkId）。item={item.GetRawText()}");
				Assert.True(
					linkIdEl.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(linkIdEl.GetString()),
					$"linkId 必须是非空字符串。当前 kind={linkIdEl.ValueKind}");
			}
			Assert.True(
				foundLinkOkTrue,
				"测试 fixture 至少应有一条 linkOk=true 的项；否则说明受控 feed 注入失败（测试设施错误，不计入 RED）");
		}
		finally
		{
			await harness.DisposeAsync();
		}
	}

	[Fact]
	public async Task News_LinkId_IsStable_AcrossRequests()
	{
		await using TestHarness harness = await TestHarness.StartAsync();
		try
		{
			string? id1 = await ExtractLinkIdForAllowedAsync(harness);
			Assert.NotNull(id1);

			string? id2 = await ExtractLinkIdForAllowedAsync(harness);
			Assert.True(
				id1 == id2,
				$"同一规范化 URL 的 linkId 必须跨请求稳定。id1={id1} id2={id2}");
		}
		finally
		{
			await harness.DisposeAsync();
		}
	}

	/* ===========================================================
	 * 2. /api/news-open 路由 + 业务级错误
	 *
	 * 受控的 401 token 测试标为"已有保护"——保留跑通但归类
	 * =========================================================== */

	[Fact] // 已有保护：401 token 校验既存
	public async Task NewsOpen_MissingToken_Returns401_ExistingProtection()
	{
		await using TestHarness harness = await TestHarness.StartAsync();
		try
		{
			using HttpRequestMessage req = new HttpRequestMessage(HttpMethod.Post, "/api/news-open")
			{
				Content = new StringContent("{\"linkId\":\"fx\"}", Encoding.UTF8, "application/json")
			};
			using HttpResponseMessage resp = await harness.Client.SendAsync(req);
			Assert.True(
				resp.StatusCode == HttpStatusCode.Unauthorized,
				$"无 token 必须 401（已有保护）。当前 status={resp.StatusCode}");
		}
		finally
		{
			await harness.DisposeAsync();
		}
	}

	[Fact] // 已有保护：401 token 校验既存
	public async Task NewsOpen_InvalidToken_Returns401_ExistingProtection()
	{
		await using TestHarness harness = await TestHarness.StartAsync();
		try
		{
			using HttpRequestMessage req = new HttpRequestMessage(HttpMethod.Post, "/api/news-open")
			{
				Content = new StringContent("{\"linkId\":\"fx\"}", Encoding.UTF8, "application/json")
			};
			req.Headers.Add("X-Pineglow-Token", "wrong-token");
			using HttpResponseMessage resp = await harness.Client.SendAsync(req);
			Assert.True(
				resp.StatusCode == HttpStatusCode.Unauthorized,
				$"错误 token 必须 401（已有保护）。当前 status={resp.StatusCode}");
		}
		finally
		{
			await harness.DisposeAsync();
		}
	}

	[Fact]
	public async Task NewsOpen_RouteExists_DoesNotReturnRouteLevel404Or405()
	{
		await using TestHarness harness = await TestHarness.StartAsync();
		try
		{
			using HttpRequestMessage req = new HttpRequestMessage(HttpMethod.Post, "/api/news-open")
			{
				Content = new StringContent("{\"linkId\":\"fx-shape-only\"}", Encoding.UTF8, "application/json")
			};
			req.Headers.Add("X-Pineglow-Token", harness.SessionToken);
			using HttpResponseMessage resp = await harness.Client.SendAsync(req);

			int status = (int)resp.StatusCode;
			string body = await resp.Content.ReadAsStringAsync();
			bool isRouteLevel404 = status == 404 && IsRouteNotFoundBody(body);

			Assert.False(
				isRouteLevel404,
				$"/api/news-open 路由必须注册（不能是路由 404）；当前 status={status} body={body}。R2-03 RED 预期失败（handler 缺失）");
			Assert.False(
				status == 405,
				$"/api/news-open 必须接受 POST；当前 status={status}。R2-03 RED 预期失败");
		}
		finally
		{
			await harness.DisposeAsync();
		}
	}

	[Fact]
	public async Task NewsOpen_ValidToken_UnknownId_ReturnsBusinessLevelNotFound()
	{
		await using TestHarness harness = await TestHarness.StartAsync();
		try
		{
			using HttpRequestMessage req = new HttpRequestMessage(HttpMethod.Post, "/api/news-open")
			{
				Content = new StringContent("{\"linkId\":\"definitely-not-a-real-id-zzz\"}", Encoding.UTF8, "application/json")
			};
			req.Headers.Add("X-Pineglow-Token", harness.SessionToken);
			using HttpResponseMessage resp = await harness.Client.SendAsync(req);

			int status = (int)resp.StatusCode;
			string body = await resp.Content.ReadAsStringAsync();
			bool isRouteLevel404 = status == 404 && IsRouteNotFoundBody(body);

			Assert.False(
				isRouteLevel404,
				$"未知 linkId 不应被路由 404 拦截；当前 status={status} body={body}。R2-03 RED 预期失败（handler 缺失）");
			Assert.True(
				status == 400 || status == 404,
				$"未知 linkId 必须返回业务 400/404；当前 status={status} body={body}");
		}
		finally
		{
			await harness.DisposeAsync();
		}
	}

	[Fact]
	public async Task NewsOpen_ValidToken_TamperedId_ReturnsBusinessLevelNotFound()
	{
		await using TestHarness harness = await TestHarness.StartAsync();
		try
		{
			// 取真实 linkId，翻转两位作篡改
			string realId = await ExtractLinkIdForAllowedAsync(harness) ?? "fallback-id";
			Assert.True(realId.Length >= 4, "linkId 必须有足够字符可翻转");
			char first = realId[0];
			char last = realId[realId.Length - 1];
			// 修正（第 40 轮实测到的 flake）：交换首尾字符在 `first == last` 时**等于没改**，
			// 于是请求会合法成功、本用例假失败。linkId 是 32 位 hex Guid，
			// 首尾相同的概率约 1/16 ≈ 6% —— 这解释了"偶发一次"的现象。
			// 现在保证篡改一定产生**不同的** id，并显式断言这一点。
			string tampered = first == last
				? realId.Substring(0, realId.Length - 1) + (last == '0' ? '1' : '0')
				: last + realId.Substring(1, realId.Length - 2) + first;
			Assert.NotEqual(realId, tampered);

			using HttpRequestMessage req = new HttpRequestMessage(HttpMethod.Post, "/api/news-open")
			{
				Content = new StringContent($"{{\"linkId\":\"{tampered}\"}}", Encoding.UTF8, "application/json")
			};
			req.Headers.Add("X-Pineglow-Token", harness.SessionToken);
			using HttpResponseMessage resp = await harness.Client.SendAsync(req);

			int status = (int)resp.StatusCode;
			string body = await resp.Content.ReadAsStringAsync();
			bool isRouteLevel404 = status == 404 && IsRouteNotFoundBody(body);

			Assert.False(
				isRouteLevel404,
				$"篡改 linkId 不应被路由 404 拦截；当前 status={status} body={body}。R2-03 RED 预期失败");
			Assert.True(
				status == 400 || status == 404,
				$"篡改 linkId 必须返回业务 400/404；当前 status={status} body={body}");
		}
		finally
		{
			await harness.DisposeAsync();
		}
	}

	[Fact]
	public async Task NewsOpen_BodyContainingRawUrl_DoesNotBypassLinkId()
	{
		await using TestHarness harness = await TestHarness.StartAsync();
		try
		{
			string reqBody = "{\"linkId\":\"absent-id-zzz\",\"url\":\"https://evil.example.com/hacker\"}";
			using HttpRequestMessage req = new HttpRequestMessage(HttpMethod.Post, "/api/news-open")
			{
				Content = new StringContent(reqBody, Encoding.UTF8, "application/json")
			};
			req.Headers.Add("X-Pineglow-Token", harness.SessionToken);
			using HttpResponseMessage resp = await harness.Client.SendAsync(req);

			int status = (int)resp.StatusCode;
			string body = await resp.Content.ReadAsStringAsync();
			bool isRouteLevel404 = status == 404 && IsRouteNotFoundBody(body);

			Assert.False(
				isRouteLevel404,
				$"携带 raw url 的请求不应被路由 404 拦截；当前 status={status} body={body}。R2-03 RED 预期失败");
			Assert.True(
				status == 400 || status == 404,
				$"携带 raw url + 无效 linkId 必须返回业务 400/404；handler 不得因为有 url 字段就放行；当前 status={status} body={body}");
		}
		finally
		{
			await harness.DisposeAsync();
		}
	}

	/* ===========================================================
	 * 2.5 GREEN 补强：合法 ID 受控 opener 200 / 合法 ID + raw URL 400 + opener 零调用
	 * =========================================================== */

	[Fact]
	public async Task NewsOpen_ValidToken_RegisteredLinkId_OpensViaControlledOpener_Returns200()
	{
		var controlledOpener = new ControlledOpener();
		await using TestHarness harness = await TestHarness.StartAsync(opener: controlledOpener);
		try
		{
			string? linkId = await ExtractLinkIdForAllowedAsync(harness);
			Assert.False(string.IsNullOrWhiteSpace(linkId), "测试前置：必须能从受控 fixture 拿到 linkId");

			using HttpRequestMessage req = new HttpRequestMessage(HttpMethod.Post, "/api/news-open")
			{
				Content = new StringContent($"{{\"linkId\":\"{linkId}\"}}", Encoding.UTF8, "application/json")
			};
			req.Headers.Add("X-Pineglow-Token", harness.SessionToken);
			using HttpResponseMessage resp = await harness.Client.SendAsync(req);

			Assert.True(
				resp.StatusCode == HttpStatusCode.OK,
				$"合法 ID + 有效 token 应 200（opener 经受控 mock 调用）。当前 status={(int)resp.StatusCode} body={await resp.Content.ReadAsStringAsync()}");
			Assert.Single(controlledOpener.OpenedUrls);
			Assert.Equal(
				"https://www.minecraft.net/article/fixture-allowed",
				controlledOpener.OpenedUrls[0]);
		}
		finally
		{
			await harness.DisposeAsync();
		}
	}

	/* ===========================================================
	 * 2.6 opener-failure-contract 返工 RED（review-fix-2，2026-09-10）
	 * =========================================================== */

	[Fact]
	public async Task NewsOpen_ValidToken_RegisteredLinkId_ThrowingOpener_Returns500_NoLeak()
	{
		var throwingOpener = new ThrowingOpener();
		await using TestHarness harness = await TestHarness.StartAsync(opener: throwingOpener);
		try
		{
			string? linkId = await ExtractLinkIdForAllowedAsync(harness);
			Assert.False(string.IsNullOrWhiteSpace(linkId), "测试前置：必须能从受控 fixture 拿到 linkId");

			using HttpRequestMessage req = new HttpRequestMessage(HttpMethod.Post, "/api/news-open")
			{
				Content = new StringContent($"{{\"linkId\":\"{linkId}\"}}", Encoding.UTF8, "application/json")
			};
			req.Headers.Add("X-Pineglow-Token", harness.SessionToken);
			using HttpResponseMessage resp = await harness.Client.SendAsync(req);

			// 1) 状态码必须是 500
			Assert.True(
				resp.StatusCode == HttpStatusCode.InternalServerError,
				$"throwing opener 必须转 500；当前 status={(int)resp.StatusCode}");

			// 2) 响应 body 必须是 { error: "opener_failed" }，且不含 fixture URL/异常文本
			string body = await resp.Content.ReadAsStringAsync();
			Assert.True(
				body.Contains("opener_failed", StringComparison.Ordinal),
				$"响应必须含 error=opener_failed；body={body}");
			Assert.False(
				body.Contains("fixture-allowed", StringComparison.OrdinalIgnoreCase),
				$"响应不得泄露 fixture URL；body={body}");
			Assert.False(
				body.Contains("Process.Start", StringComparison.OrdinalIgnoreCase) ||
				body.Contains("notepad.exe", StringComparison.OrdinalIgnoreCase) ||
				body.Contains("HKLM", StringComparison.OrdinalIgnoreCase) ||
				body.Contains("registry", StringComparison.OrdinalIgnoreCase),
				$"响应不得泄露 opener 异常文本；body={body}");

			// 3) opener 必须被调用过（证明测试真的触发了受控抛错,不是伪造状态码）
			Assert.Single(throwingOpener.OpenedUrls);
		}
		finally
		{
			await harness.DisposeAsync();
		}
	}

	[Fact]
	public async Task NewsOpen_ValidToken_RegisteredLinkIdWithAliasId_Returns400_OpenerNotCalled()
	{
		var controlledOpener = new ControlledOpener();
		await using TestHarness harness = await TestHarness.StartAsync(opener: controlledOpener);
		try
		{
			string? linkId = await ExtractLinkIdForAllowedAsync(harness);
			Assert.False(string.IsNullOrWhiteSpace(linkId), "测试前置：必须能从受控 fixture 拿到 linkId");

			// 使用 `id` 而非精确的 `linkId` 字段名,违反"只允许精确 linkId"协议
			string reqBody = $"{{\"id\":\"{linkId}\"}}";
			using HttpRequestMessage req = new HttpRequestMessage(HttpMethod.Post, "/api/news-open")
			{
				Content = new StringContent(reqBody, Encoding.UTF8, "application/json")
			};
			req.Headers.Add("X-Pineglow-Token", harness.SessionToken);
			using HttpResponseMessage resp = await harness.Client.SendAsync(req);

			Assert.True(
				resp.StatusCode == HttpStatusCode.BadRequest,
				$"使用 `id` 别名(非精确 linkId)必须 400；当前 status={(int)resp.StatusCode}");
			Assert.Empty(controlledOpener.OpenedUrls);
		}
		finally
		{
			await harness.DisposeAsync();
		}
	}

	[Fact]
	public async Task NewsOpen_ValidToken_RegisteredLinkIdWithAliasLinkId_Returns400_OpenerNotCalled()
	{
		var controlledOpener = new ControlledOpener();
		await using TestHarness harness = await TestHarness.StartAsync(opener: controlledOpener);
		try
		{
			string? linkId = await ExtractLinkIdForAllowedAsync(harness);
			Assert.False(string.IsNullOrWhiteSpace(linkId), "测试前置：必须能从受控 fixture 拿到 linkId");

			// 使用 `link_id` 而非精确的 `linkId`,同样违反协议
			string reqBody = $"{{\"link_id\":\"{linkId}\"}}";
			using HttpRequestMessage req = new HttpRequestMessage(HttpMethod.Post, "/api/news-open")
			{
				Content = new StringContent(reqBody, Encoding.UTF8, "application/json")
			};
			req.Headers.Add("X-Pineglow-Token", harness.SessionToken);
			using HttpResponseMessage resp = await harness.Client.SendAsync(req);

			Assert.True(
				resp.StatusCode == HttpStatusCode.BadRequest,
				$"使用 `link_id` 别名(非精确 linkId)必须 400；当前 status={(int)resp.StatusCode}");
			Assert.Empty(controlledOpener.OpenedUrls);
		}
		finally
		{
			await harness.DisposeAsync();
		}
	}

	[Fact]
	public async Task NewsOpen_ValidToken_RegisteredLinkIdWithExtraField_Returns400_OpenerNotCalled()
	{
		var controlledOpener = new ControlledOpener();
		await using TestHarness harness = await TestHarness.StartAsync(opener: controlledOpener);
		try
		{
			string? linkId = await ExtractLinkIdForAllowedAsync(harness);
			Assert.False(string.IsNullOrWhiteSpace(linkId), "测试前置：必须能从受控 fixture 拿到 linkId");

			// body 包含精确 linkId + 额外普通字段 "note" → 违反"只允许 linkId"协议
			string reqBody = $"{{\"linkId\":\"{linkId}\",\"note\":\"extra\"}}";
			using HttpRequestMessage req = new HttpRequestMessage(HttpMethod.Post, "/api/news-open")
			{
				Content = new StringContent(reqBody, Encoding.UTF8, "application/json")
			};
			req.Headers.Add("X-Pineglow-Token", harness.SessionToken);
			using HttpResponseMessage resp = await harness.Client.SendAsync(req);

			Assert.True(
				resp.StatusCode == HttpStatusCode.BadRequest,
				$"包含 linkId + 额外普通字段必须 400；当前 status={(int)resp.StatusCode}");
			Assert.Empty(controlledOpener.OpenedUrls);
		}
		finally
		{
			await harness.DisposeAsync();
		}
	}

	/* ===========================================================
	 * 2.7 TryOpen 结果/淘汰竞态返工 RED（review-fix-3，2026-09-10）
	 * =========================================================== */

	[Fact]
	public async Task NewsOpen_ValidToken_RegisteredLinkId_InvalidatedBetweenLookupAndOpen_Returns404_OpenerNotCalled()
	{
		var controlledOpener = new ControlledOpener();
		await using TestHarness harness = await TestHarness.StartAsync(opener: controlledOpener);
		try
		{
			// 1) 真实 HTTP 拉取已登记 linkId(等同正常客户端先 GET /api/news)
			string? linkId = await ExtractLinkIdForAllowedAsync(harness);
			Assert.False(string.IsNullOrWhiteSpace(linkId), "测试前置：必须能从受控 fixture 拿到 linkId");

			// 2) 通过测试 seam 模拟"handler 预查与 TryOpen 之间 ID 被 LRU 淘汰/失效"窗口
			//    不使用 sleep：seam 让 NewsLinkService 内部"下一次该 linkId 调用"返回 LinkNotFound
			InvalidateOnNextCallForTest(harness, linkId!);

			// 3) 客户端 POST /api/news-open
			using HttpRequestMessage req = new HttpRequestMessage(HttpMethod.Post, "/api/news-open")
			{
				Content = new StringContent($"{{\"linkId\":\"{linkId}\"}}", Encoding.UTF8, "application/json")
			};
			req.Headers.Add("X-Pineglow-Token", harness.SessionToken);
			using HttpResponseMessage resp = await harness.Client.SendAsync(req);

			int status = (int)resp.StatusCode;
			string body = await resp.Content.ReadAsStringAsync();

			// 4) 断言：业务 404 + 脱敏文案 + 无 ok/opener 调用
			Assert.True(
				resp.StatusCode == HttpStatusCode.NotFound,
				$"竞态窗口(预查后 ID 失效)必须 404；当前 status={status} body={body}");
			Assert.True(
				body.Contains("link_not_found", StringComparison.Ordinal),
				$"响应必须含 error=link_not_found；body={body}");
			Assert.False(
				body.Contains("\"ok\"", StringComparison.Ordinal) ||
				body.Contains("\"ok\":true", StringComparison.OrdinalIgnoreCase) ||
				body.Contains("\"ok\": true", StringComparison.OrdinalIgnoreCase),
				$"响应不得含 ok 字段(成功标志)；body={body}");
			Assert.Empty(controlledOpener.OpenedUrls);
		}
		finally
		{
			await harness.DisposeAsync();
		}
	}

	/// <summary>反射调用 NewsLinkService 内部测试 seam：让"下一次该 linkId 的 OpenOrFail"模拟 LRU 淘汰后返回 LinkNotFound。</summary>
	private static void InvalidateOnNextCallForTest(TestHarness harness, string linkId)
	{
		FieldInfo? newsLinksField = typeof(LocalApiService).GetField(
			"NewsLinks", BindingFlags.NonPublic | BindingFlags.Static);
		if (newsLinksField == null)
			throw new InvalidOperationException("LocalApiService.NewsLinks 字段未找到（seam 依赖）");
		object? newsLinks = newsLinksField.GetValue(null);
		if (newsLinks == null)
			throw new InvalidOperationException("LocalApiService.NewsLinks 为 null");

		MethodInfo? seam = newsLinks.GetType().GetMethod(
			"InvalidateOnNextCallForTest", BindingFlags.NonPublic | BindingFlags.Instance);
		if (seam == null)
			throw new InvalidOperationException("NewsLinkService.InvalidateOnNextCallForTest 缺失（GREEN 阶段必须实现此测试 seam）");

		seam.Invoke(newsLinks, new object[] { linkId });
	}

	[Fact]
	public async Task NewsOpen_ValidToken_RegisteredLinkId_SuccessfulOpener_StillReturns200()
	{
		// 防回归:成功路径必须保持 200,不能因为修复 opener-failure 而打断成功路径
		var controlledOpener = new ControlledOpener();
		await using TestHarness harness = await TestHarness.StartAsync(opener: controlledOpener);
		try
		{
			string? linkId = await ExtractLinkIdForAllowedAsync(harness);
			Assert.False(string.IsNullOrWhiteSpace(linkId), "测试前置：必须能从受控 fixture 拿到 linkId");

			using HttpRequestMessage req = new HttpRequestMessage(HttpMethod.Post, "/api/news-open")
			{
				Content = new StringContent($"{{\"linkId\":\"{linkId}\"}}", Encoding.UTF8, "application/json")
			};
			req.Headers.Add("X-Pineglow-Token", harness.SessionToken);
			using HttpResponseMessage resp = await harness.Client.SendAsync(req);

			Assert.True(
				resp.StatusCode == HttpStatusCode.OK,
				$"成功 opener 仍必须 200；当前 status={(int)resp.StatusCode} body={await resp.Content.ReadAsStringAsync()}");
			Assert.Single(controlledOpener.OpenedUrls);
		}
		finally
		{
			await harness.DisposeAsync();
		}
	}

	[Fact]
	public async Task NewsOpen_ValidToken_RegisteredLinkIdWithRawUrl_Returns400_OpenerNotCalled()
	{
		var controlledOpener = new ControlledOpener();
		await using TestHarness harness = await TestHarness.StartAsync(opener: controlledOpener);
		try
		{
			string? linkId = await ExtractLinkIdForAllowedAsync(harness);
			Assert.False(string.IsNullOrWhiteSpace(linkId), "测试前置：必须能从受控 fixture 拿到 linkId");

			string body = $"{{\"linkId\":\"{linkId}\",\"url\":\"https://evil.example.com/hacker\",\"href\":\"https://evil.example.com/h\"}}";
			using HttpRequestMessage req = new HttpRequestMessage(HttpMethod.Post, "/api/news-open")
			{
				Content = new StringContent(body, Encoding.UTF8, "application/json")
			};
			req.Headers.Add("X-Pineglow-Token", harness.SessionToken);
			using HttpResponseMessage resp = await harness.Client.SendAsync(req);

			int status = (int)resp.StatusCode;
			string respBody = await resp.Content.ReadAsStringAsync();
			Assert.True(
				resp.StatusCode == HttpStatusCode.BadRequest,
				$"合法 ID + 携带 raw url 字段必须 400（handler 拒绝 raw URL 绕过 linkId）。当前 status={status} body={respBody}");
			Assert.Empty(controlledOpener.OpenedUrls);
		}
		finally
		{
			await harness.DisposeAsync();
		}
	}

	/// <summary>受控 opener 桩：记录被调用的 URL，生产代码永不真的拉起浏览器。</summary>
	private sealed class ControlledOpener : NewsLinkService.INewsLinkOpener
	{
		public List<string> OpenedUrls { get; } = new List<string>();
		public void Open(string url)
		{
			OpenedUrls.Add(url);
		}
	}

	/// <summary>受控 throwing opener：每次 Open 抛受控异常；测试断言 handler 把它可靠转 500 且不泄露 URL/异常文本。</summary>
	private sealed class ThrowingOpener : NewsLinkService.INewsLinkOpener
	{
		public List<string> OpenedUrls { get; } = new List<string>();
		public Exception ToThrow { get; set; } = new InvalidOperationException("controlled opener failure: registry='HKLM\\SOFTWARE\\Fake', url='https://www.minecraft.net/article/fixture-allowed'");
		public void Open(string url)
		{
			OpenedUrls.Add(url);
			Exception ex = ToThrow;
			// 包一层包含 URL 的内部异常,模拟生产 opener 异常通常带 URL/路径
			throw new InvalidOperationException(
				$"Process.Start failed: url='{url}', path='C:\\Windows\\System32\\notepad.exe', registry='HKLM\\SOFTWARE\\Fake'", ex);
		}
	}

	/* ===========================================================
	 * 3. 受控 fixture + 反射注入辅助
	 * =========================================================== */

	/// <summary>扫描 /api/news 响应找 linkOk=true 的项，提取其 linkId（不依赖 items[0）。</summary>
	private static async Task<string?> ExtractLinkIdForAllowedAsync(TestHarness harness)
	{
		using HttpResponseMessage resp = await harness.GetAsync("/api/news");
		Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
		string body = await resp.Content.ReadAsStringAsync();
		using JsonDocument doc = JsonDocument.Parse(body);
		Assert.True(doc.RootElement.TryGetProperty("items", out JsonElement items));
		foreach (JsonElement item in items.EnumerateArray())
		{
			if (!item.TryGetProperty("linkOk", out JsonElement linkOkEl)) continue;
			if (linkOkEl.ValueKind != JsonValueKind.True) continue;
			if (item.TryGetProperty("linkId", out JsonElement linkIdEl)
				&& linkIdEl.ValueKind == JsonValueKind.String)
			{
				return linkIdEl.GetString();
			}
		}
		return null;
	}

	/// <summary>解析 body JSON 的 error 字段，兼容 \uXXXX 转义。</summary>
	private static bool IsRouteNotFoundBody(string rawBody)
	{
		if (string.IsNullOrWhiteSpace(rawBody)) return false;
		try
		{
			using JsonDocument doc = JsonDocument.Parse(rawBody);
			if (doc.RootElement.ValueKind != JsonValueKind.Object) return false;
			if (!doc.RootElement.TryGetProperty("error", out JsonElement errEl)) return false;
			if (errEl.ValueKind != JsonValueKind.String) return false;
			string err = errEl.GetString() ?? string.Empty;
			return err.StartsWith("未找到接口", StringComparison.Ordinal)
				|| err.Contains("未找到接口", StringComparison.Ordinal);
		}
		catch
		{
			return rawBody.Contains("未找到接口", StringComparison.Ordinal)
				|| rawBody.Contains("\\u672a\\u627e\\u5230\\u63a5\\u53e3", StringComparison.OrdinalIgnoreCase);
		}
	}

	/* ===========================================================
	 * 4. 隔离测试 harness：内存 store + LocalApiService + 真实 config 守护 + 受控 NewsService feed
	 * =========================================================== */

	private sealed class TestHarness : IAsyncDisposable
	{
		public MemoryConfigStore MemoryStore { get; }
		public HttpClient Client { get; }
		public string SessionToken => LocalApiService.SessionTokenForTests;
		private readonly IDisposable _configScope;
		private readonly int _port;
		private readonly RealConfigSnapshot _preSnapshot;
		private readonly object? _originalMemoryCache;
		private bool _disposed;

		private TestHarness(MemoryConfigStore store, IDisposable configScope, int port, RealConfigSnapshot pre, object? originalMemoryCache, object? originalOpener)
		{
			MemoryStore = store;
			_configScope = configScope;
			_port = port;
			_preSnapshot = pre;
			_originalMemoryCache = originalMemoryCache;
			_originalOpener = originalOpener;
			Client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}/"), Timeout = TimeSpan.FromSeconds(8) };
		}

		private readonly object? _originalOpener;

		public static async Task<TestHarness> StartAsync(NewsLinkService.INewsLinkOpener? opener = null)
		{
			RealConfigSnapshot pre = RealConfigSnapshot.Capture();
			var store = new MemoryConfigStore();

			IDisposable scope;
			try
			{
				scope = LocalApiService.OverrideConfigStoreForTest(store);
			}
			catch (Exception ex)
			{
				throw new InvalidOperationException("OverrideConfigStoreForTest 失败：" + ex.Message, ex);
			}
			Assert.NotNull(scope);

			if (LocalApiService.IsRunning)
			{
				scope.Dispose();
				throw new InvalidOperationException("LocalApiService 已在运行；测试不能启动新端口。");
			}

			// 反射注入受控 NewsService 内存 cache（不修改生产代码）
			object? originalMemoryCache = InjectControlledFeed();
			// 反射注入受控 opener（如果提供）
			object? originalOpener = null;
			if (opener != null)
			{
				originalOpener = InjectControlledOpener(opener);
			}

			// A21（第 49 轮）：本夹具原先只重定向了配置存储，恢复区/二分落盘/诊断报告会落到真实 %APPDATA%。
			TestIsolationRoots.InstallCurrent();
			int port = await AcquirePortAsync();
			LocalApiService.Start(port);
			if (!LocalApiService.IsRunning)
			{
				RestoreFeed(originalMemoryCache);
				if (originalOpener != null) RestoreOpener(originalOpener);
				scope.Dispose();
				throw new InvalidOperationException("LocalApiService.Start 后未运行。");
			}

			return new TestHarness(store, scope, port, pre, originalMemoryCache, originalOpener);
		}

		public Task<HttpResponseMessage> GetAsync(string path)
		{
			HttpRequestMessage req = new HttpRequestMessage(HttpMethod.Get, path);
			req.Headers.Add("X-Pineglow-Token", SessionToken);
			return Client.SendAsync(req);
		}

		public async ValueTask DisposeAsync()
		{
			if (_disposed) return;
			_disposed = true;

			Exception? firstError = null;

			try
			{
				if (LocalApiService.IsRunning) LocalApiService.Stop();
			}
			catch (Exception ex)
			{
				firstError ??= new InvalidOperationException("LocalApiService.Stop 失败：" + ex.Message, ex);
			}
			// A21：释放三个存储根的重定向（恢复区 / 二分落盘 / 诊断报告）
			try { TestIsolationRoots.ReleaseCurrent(); } catch { }

			try { Client.Dispose(); }
			catch (Exception ex)
			{
				firstError ??= new InvalidOperationException("HttpClient.Dispose 失败：" + ex.Message, ex);
			}

			try { _configScope.Dispose(); }
			catch (Exception ex)
			{
				firstError ??= new InvalidOperationException("ConfigStoreOverrideScope.Dispose 失败：" + ex.Message, ex);
			}

			// 恢复 NewsService 内存 cache
			try { RestoreFeed(_originalMemoryCache); }
			catch (Exception ex)
			{
				firstError ??= new InvalidOperationException("NewsService._memoryCache 恢复失败：" + ex.Message, ex);
			}

			// 恢复 NewsLinkService opener
			if (_originalOpener != null)
			{
				try { RestoreOpener(_originalOpener); }
				catch (Exception ex)
				{
					firstError ??= new InvalidOperationException("NewsLinkService opener 恢复失败：" + ex.Message, ex);
				}
			}

			RealConfigSnapshot post = RealConfigSnapshot.Capture();
			// CONFIG_ABSENT: 测试环境无真实 config.{json,bak,prev}，不杜撰 SHA 一致
			if (_preSnapshot.IsAbsent && post.IsAbsent)
			{
				// 跳过 SHA 一致性断言；在证据报告中显式标记 CONFIG_ABSENT
				Console.Out.WriteLine($"[r203-NewsLinkHandler] CONFIG_ABSENT — {_preSnapshot}");
			}
			else
			{
				Console.Out.WriteLine($"[r203-NewsLinkHandler] config-snapshot pre={_preSnapshot}");
				Console.Out.WriteLine($"[r203-NewsLinkHandler] config-snapshot post={post}");
				if (!_preSnapshot.Equals(post))
				{
					firstError = new InvalidOperationException(
						$"真实 config 状态被改动！\npre: {_preSnapshot}\npost: {post}");
				}
			}

			if (firstError != null) throw firstError;
		}

		/// <summary>反射注入受控 NewsService 内存 cache：返回原 memoryCache（用于恢复）。</summary>
		private static object? InjectControlledFeed()
		{
			// LocalApiService.NewsService 是 private static readonly
			FieldInfo? newsServiceField = typeof(LocalApiService).GetField(
				"NewsService", BindingFlags.NonPublic | BindingFlags.Static);
			if (newsServiceField == null)
			{
				throw new InvalidOperationException(
					"LocalApiService.NewsService 字段未找到（生产代码字段名变更？测试设施错误，不计入 RED）");
			}
			object newsService = newsServiceField.GetValue(null)
				?? throw new InvalidOperationException("LocalApiService.NewsService 为 null");

			// NewsService._memoryCache 是 private
			FieldInfo? memCacheField = newsService.GetType().GetField(
				"_memoryCache", BindingFlags.NonPublic | BindingFlags.Instance);
			if (memCacheField == null)
			{
				throw new InvalidOperationException(
					"NewsService._memoryCache 字段未找到（生产代码字段名变更？测试设施错误，不计入 RED）");
			}
			object? original = memCacheField.GetValue(newsService);
			NewsFetchResult fixture = BuildFixture(DateTimeOffset.UtcNow);
			memCacheField.SetValue(newsService, fixture);
			return original;
		}

		private static void RestoreFeed(object? original)
		{
			FieldInfo? newsServiceField = typeof(LocalApiService).GetField(
				"NewsService", BindingFlags.NonPublic | BindingFlags.Static);
			if (newsServiceField == null) return;
			object? newsService = newsServiceField.GetValue(null);
			if (newsService == null) return;
			FieldInfo? memCacheField = newsService.GetType().GetField(
				"_memoryCache", BindingFlags.NonPublic | BindingFlags.Instance);
			if (memCacheField == null) return;
			memCacheField.SetValue(newsService, original);
		}

		/// <summary>反射注入受控 opener（仅测试用）。返回原 opener 实例（用于恢复）。</summary>
		private static object InjectControlledOpener(NewsLinkService.INewsLinkOpener opener)
		{
			FieldInfo? newsLinksField = typeof(LocalApiService).GetField(
				"NewsLinks", BindingFlags.NonPublic | BindingFlags.Static);
			if (newsLinksField == null)
			{
				throw new InvalidOperationException("LocalApiService.NewsLinks 字段未找到（生产代码字段名变更？）");
			}
			object newsLinks = newsLinksField.GetValue(null)
				?? throw new InvalidOperationException("LocalApiService.NewsLinks 为 null");

			MethodInfo? setOpener = newsLinks.GetType().GetMethod(
				"SetOpenerForTest", BindingFlags.NonPublic | BindingFlags.Instance);
			MethodInfo? getOpener = newsLinks.GetType().GetMethod(
				"GetOpenerForTest", BindingFlags.NonPublic | BindingFlags.Instance);
			if (setOpener == null || getOpener == null)
			{
				throw new InvalidOperationException("NewsLinkService.SetOpenerForTest/GetOpenerForTest 缺失");
			}
			object? original = getOpener.Invoke(newsLinks, null);
			setOpener.Invoke(newsLinks, new object[] { opener });
			return original ?? new object();
		}

		private static void RestoreOpener(object? original)
		{
			FieldInfo? newsLinksField = typeof(LocalApiService).GetField(
				"NewsLinks", BindingFlags.NonPublic | BindingFlags.Static);
			if (newsLinksField == null) return;
			object? newsLinks = newsLinksField.GetValue(null);
			if (newsLinks == null) return;
			MethodInfo? setOpener = newsLinks.GetType().GetMethod(
				"SetOpenerForTest", BindingFlags.NonPublic | BindingFlags.Instance);
			if (setOpener == null || original == null) return;
			try { setOpener.Invoke(newsLinks, new object[] { original }); } catch { /* 允许恢复失败 */ }
		}

		private static async Task<int> AcquirePortAsync()
		{
			int seed = Interlocked.Increment(ref _nextPortSeed) | 1;
			for (int i = 0; i < 200; i++)
			{
				int p = 49000 + ((seed + i * 37) % 16000);
				try
				{
					var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, p);
					listener.Start();
					listener.Stop();
					return p;
				}
				catch
				{
					// port taken
				}
			}
			throw new InvalidOperationException("找不到可用端口（r203 端口搜索耗尽）");
		}

		private static int _nextPortSeed = 0;
	}

	private sealed class MemoryConfigStore : LocalApiService.IConfigStore
	{
		private AppConfig _current = new AppConfig();
		public AppConfig Load() => _current;
		public void Save(AppConfig config) => _current = config;
	}

	private sealed class RealConfigSnapshot : IEquatable<RealConfigSnapshot>
	{
		public string ConfigPath { get; }
		public string? ConfigJsonSha { get; }
		public long? ConfigJsonSize { get; }
		public string? ConfigBakSha { get; }
		public long? ConfigBakSize { get; }
		public string? ConfigPrevSha { get; }
		public long? ConfigPrevSize { get; }

		/// <summary>如果 config.{json,bak,prev} 全部不存在，标记为 CONFIG_ABSENT（不杜撰一致）。</summary>
		public bool IsAbsent =>
			ConfigJsonSha == null && ConfigBakSha == null && ConfigPrevSha == null;

		private RealConfigSnapshot(string configPath)
		{
			ConfigPath = configPath;
			(ConfigJsonSha, ConfigJsonSize) = ReadOrNull(Path.Combine(configPath, "config.json"));
			(ConfigBakSha, ConfigBakSize) = ReadOrNull(Path.Combine(configPath, "config.json.bak"));
			(ConfigPrevSha, ConfigPrevSize) = ReadOrNull(Path.Combine(configPath, "config.json.prev"));
		}

		public static RealConfigSnapshot Capture() => new RealConfigSnapshot(PlatformPaths.GetConfigDirectory());

		public bool Equals(RealConfigSnapshot? other)
		{
			if (other == null) return false;
			return ConfigPath == other.ConfigPath
				&& ConfigJsonSha == other.ConfigJsonSha && ConfigJsonSize == other.ConfigJsonSize
				&& ConfigBakSha == other.ConfigBakSha && ConfigBakSize == other.ConfigBakSize
				&& ConfigPrevSha == other.ConfigPrevSha && ConfigPrevSize == other.ConfigPrevSize;
		}

		public override bool Equals(object? obj) => Equals(obj as RealConfigSnapshot);
		public override int GetHashCode() => ConfigPath.GetHashCode();
		public override string ToString()
		{
			if (IsAbsent)
			{
				return $"CONFIG_ABSENT (path={ConfigPath}; 真实 config 目录不存在或不包含 config.{{json,bak,prev}})";
			}
			return $"json={ConfigJsonSha ?? "MISSING"}/{ConfigJsonSize}, " +
				$"bak={ConfigBakSha ?? "MISSING"}/{ConfigBakSize}, " +
				$"prev={ConfigPrevSha ?? "MISSING"}/{ConfigPrevSize}";
		}

		private static (string? sha, long? size) ReadOrNull(string path)
		{
			if (!File.Exists(path)) return (null, null);
			using FileStream fs = File.OpenRead(path);
			using var sha = System.Security.Cryptography.SHA256.Create();
			byte[] hash = sha.ComputeHash(fs);
			return (Convert.ToHexString(hash), fs.Length);
		}
	}
}
