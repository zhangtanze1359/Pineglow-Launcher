using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Pineglow.Launcher.Models;
using Pineglow.Launcher.Services;
using Xunit;
using Xunit.Abstractions;

namespace Pineglow.Launcher.Tests;

/// <summary>
/// 2026-10-03 P0 收口（W2）：HTTP 层路由契约测试。
///
/// 背景（本轮独立复核）：
///   <c>LocalApiService.HandleApiAsync</c> 中 <c>seg.Length == 4 &amp;&amp; seg[1] == "instances"</c> 的块
///   从 :415 开到 :547，块内却还有 4 组要求 <c>seg.Length</c> 为 5/6/8 的守卫 ⇒ 条件恒假 ⇒
///   10 条路由永远落到末尾的 404。既有 796 个绿色测试对此完全无感知：
///   <c>ModBisectServiceTests</c> 只打服务层，全仓 C# 测试对 "/bisect/" 路由字符串 0 命中，
///   既有静态断言用的是"读源码找子串"（字符串确实在文件里，嵌套也在，无法证伪）。
///
/// 本文件补齐三件事：
///   1. 对 10 条路由发真实 HTTP，断言响应**不是路由器兜底 404**；
///   2. 反向保护：一个必然未命中的路径必须**确实**返回兜底 404（证明断言有牙，不空洞）；
///   3. 静态结构断言：任何 <c>seg.Length == N</c> 守卫都不得被嵌套在约束为其它 N 的作用域内。
///
/// 识别口径：路由器兜底 404 的响应体是 <c>{ "error": "未找到接口：{method} {path}" }</c>（全文件仅一处），
/// 与各 handler 自己的 400/404 文案不同。注意 <c>WriteJsonAsync</c> 使用默认 JavaScriptEncoder，
/// 非 ASCII 会被转义成 \uXXXX，因此**必须解析 JSON 读 error 字段**，不能直接对原始响应体做字符串匹配。
/// </summary>
[Collection("LocalApiService")]
public class RouteReachabilityTests
{
	private const string FallbackMarker = "未找到接口";
	private readonly ITestOutputHelper _out;

	public RouteReachabilityTests(ITestOutputHelper output) => _out = output;

	/* ===================== 合成夹具 ===================== */

	private sealed class RouteFixture : IDisposable
	{
		public string Root { get; }
		public string InstanceId { get; }
		public AppConfig Config { get; }
		public string ModsDir => Path.Combine(Root, "mods");

		public RouteFixture()
		{
			Root = Path.Combine(Path.GetTempPath(), "pineglow-route-test-" + Guid.NewGuid().ToString("N"));
			InstanceId = "inst-" + Guid.NewGuid().ToString("N").Substring(0, 8);

			Directory.CreateDirectory(ModsDir);
			File.WriteAllText(Path.Combine(ModsDir, "a.jar"), "fake mod a");
			File.WriteAllText(Path.Combine(ModsDir, "b.jar"), "fake mod b");
			Directory.CreateDirectory(Path.Combine(Root, "saves", "world1"));
			File.WriteAllText(Path.Combine(Root, "saves", "world1", "level.dat"), "fake level");

			// GameDirectory 与 GameInstance.GameDirectory 都必须显式指向合成临时目录：
			// ResolveGameDirectory 在两者皆空时会回退到真实 .minecraft 目录。
			Config = new AppConfig
			{
				GameDirectory = Root,
				SelectedInstanceId = InstanceId,
				Instances = new List<GameInstance>
				{
					new GameInstance
					{
						Id = InstanceId,
						Name = "route",
						GameDirectory = Root,
						MinecraftVersion = "1.20.4"
					}
				}
			};
		}

		public string ApiPath(string suffix) => "/api/instances/" + InstanceId + suffix;

		public int DisabledCount() => Directory.Exists(ModsDir)
			? Directory.GetFiles(ModsDir, "*.disabled").Length
			: 0;

		public void Dispose()
		{
			try { ModBisectService.Remove(InstanceId); } catch { }
			try { if (Directory.Exists(Root)) Directory.Delete(Root, true); } catch { }
		}
	}

	/* ===================== 断言辅助 ===================== */

	/// <summary>取响应体里的 error 字段（不做原始字符串匹配，规避 \uXXXX 转义）。</summary>
	private static string? ExtractError(string body)
	{
		try
		{
			using JsonDocument doc = JsonDocument.Parse(body);
			if (doc.RootElement.ValueKind == JsonValueKind.Object
				&& doc.RootElement.TryGetProperty("error", out JsonElement el)
				&& el.ValueKind == JsonValueKind.String)
			{
				return el.GetString();
			}
		}
		catch { }
		return null;
	}

	private static bool IsRouterFallback(string body) =>
		ExtractError(body)?.StartsWith(FallbackMarker, StringComparison.Ordinal) == true;

	private static string Shorten(string s) => s.Length <= 200 ? s : s.Substring(0, 200) + "…";

	/* ===================== 第 1 层：10 条路由必须可达 ===================== */

	public static IEnumerable<object[]> TenRouteShapes()
	{
		// method, path 后缀, 是否 POST
		yield return new object[] { "POST", "/mods/a.jar/toggle", true };
		yield return new object[] { "POST", "/mods/a.jar/delete", true };
		yield return new object[] { "POST", "/bisect/start", true };
		yield return new object[] { "POST", "/bisect/test", true };
		yield return new object[] { "POST", "/bisect/giveup", true };
		yield return new object[] { "GET", "/saves/world1/backups", false };
		yield return new object[] { "POST", "/saves/world1/backup", true };
		yield return new object[] { "POST", "/saves/world1/delete", true };
		yield return new object[] { "POST", "/saves/nosuch/open", true };
		yield return new object[] { "POST", "/saves/world1/backups/nosuch-backup.zip/restore", true };
	}

	[Theory]
	[MemberData(nameof(TenRouteShapes))]
	public async Task Route_IsReachable_NotRouterFallback(string method, string suffix, bool isPost)
	{
		using var fx = new RouteFixture();
		await using var h = await LocalApiTestHarness.StartAsync(fx.Config);

		string path = fx.ApiPath(suffix);
		(int status, string body) = await h.SendAsync(
			new HttpMethod(method), path, isPost ? new { crashed = false } : null);

		_out.WriteLine($"[route] {method} {path} -> {status} | error={ExtractError(body)} | body={Shorten(body)}");

		Assert.False(
			IsRouterFallback(body),
			$"路由不可达：{method} {path} 命中了路由器兜底 404（status={status}）。"
			+ $"这说明该守卫仍被嵌套在互斥的 seg.Length 作用域内。body={Shorten(body)}");

		// 状态码必须是 handler 层给出的结果，而不是"路由没接上"
		Assert.True(status is 200 or 400 or 409 or 500,
			$"{method} {path} 返回了非预期状态码 {status}；body={Shorten(body)}");
	}

	/* ===================== 第 2 层：断言必须有牙（反空洞） ===================== */

	[Fact]
	public async Task RouterFallbackMarker_HasTeeth_UnknownPathReturnsFallback404()
	{
		using var fx = new RouteFixture();
		await using var h = await LocalApiTestHarness.StartAsync(fx.Config);

		(int status, string body) = await h.SendAsync(HttpMethod.Post, fx.ApiPath("/bogus/xyz"), new { });

		_out.WriteLine($"[teeth] POST {fx.ApiPath("/bogus/xyz")} -> {status} body={Shorten(body)}");

		Assert.Equal(404, status);
		Assert.True(
			IsRouterFallback(body),
			$"兜底标记失效：未命中路由时 response.error 未以「{FallbackMarker}」开头，"
			+ $"本文件的路由可达性断言会因此空洞通过。body={Shorten(body)}");
	}

	/* ===================== 第 3 层：行为级验证（不止"不是 404"） ===================== */

	[Fact]
	public async Task Mods_ToggleAndDelete_ReachHandlerAndChangeDisk()
	{
		using var fx = new RouteFixture();
		await using var h = await LocalApiTestHarness.StartAsync(fx.Config);

		(int toggleStatus, string toggleBody) = await h.SendAsync(
			HttpMethod.Post, fx.ApiPath("/mods/a.jar/toggle"), new { });
		_out.WriteLine($"[mods] toggle -> {toggleStatus} body={Shorten(toggleBody)}");

		Assert.Equal(200, toggleStatus);
		Assert.True(File.Exists(Path.Combine(fx.ModsDir, "a.jar.disabled")), "toggle 后应出现 a.jar.disabled");
		Assert.False(File.Exists(Path.Combine(fx.ModsDir, "a.jar")), "toggle 后 a.jar 应已被改名");

		(int deleteStatus, string deleteBody) = await h.SendAsync(
			HttpMethod.Post, fx.ApiPath("/mods/a.jar.disabled/delete"), new { });
		_out.WriteLine($"[mods] delete -> {deleteStatus} body={Shorten(deleteBody)}");

		Assert.Equal(200, deleteStatus);
		Assert.False(File.Exists(Path.Combine(fx.ModsDir, "a.jar.disabled")), "delete 后禁用文件应被移除");
	}

	[Fact]
	public async Task Bisect_StartThenGiveUp_ReachesHandlerAndRestoresMods()
	{
		using var fx = new RouteFixture();
		await using var h = await LocalApiTestHarness.StartAsync(fx.Config);

		(int startStatus, string startBody) = await h.SendAsync(HttpMethod.Post, fx.ApiPath("/bisect/start"), new { });
		_out.WriteLine($"[bisect] start -> {startStatus} body={Shorten(startBody)}");

		Assert.Equal(200, startStatus);
		Assert.True(fx.DisabledCount() >= 1, "start 后应至少禁用一半 Mod（.jar.disabled）");

		(int giveUpStatus, string giveUpBody) = await h.SendAsync(HttpMethod.Post, fx.ApiPath("/bisect/giveup"), new { });
		_out.WriteLine($"[bisect] giveup -> {giveUpStatus} body={Shorten(giveUpBody)}");

		Assert.Equal(200, giveUpStatus);
		Assert.Equal(0, fx.DisabledCount());
		Assert.True(File.Exists(Path.Combine(fx.ModsDir, "a.jar")) && File.Exists(Path.Combine(fx.ModsDir, "b.jar")),
			"giveup 后两个 Mod 都应恢复为启用态");
	}

	[Fact]
	public async Task Bisect_StartThenTest_LocksCulpritAndRestoresMods()
	{
		using var fx = new RouteFixture();
		await using var h = await LocalApiTestHarness.StartAsync(fx.Config);

		(int startStatus, _) = await h.SendAsync(HttpMethod.Post, fx.ApiPath("/bisect/start"), new { });
		Assert.Equal(200, startStatus);

		(int testStatus, string testBody) = await h.SendAsync(
			HttpMethod.Post, fx.ApiPath("/bisect/test"), new { crashed = false });
		_out.WriteLine($"[bisect] test(crashed=false) -> {testStatus} body={Shorten(testBody)}");

		Assert.Equal(200, testStatus);
		using JsonDocument doc = JsonDocument.Parse(testBody);
		Assert.True(doc.RootElement.GetProperty("finished").GetBoolean(), "2 个 Mod 单轮即可锁定元凶，应 finished=true");
		Assert.Equal("a.jar", doc.RootElement.GetProperty("culprit").GetString());
		Assert.Equal(0, fx.DisabledCount());
	}

	[Fact]
	public async Task Saves_BackupListRestoreDelete_ReachHandler()
	{
		using var fx = new RouteFixture();
		await using var h = await LocalApiTestHarness.StartAsync(fx.Config);

		(int listEmptyStatus, string listEmptyBody) = await h.SendAsync(
			HttpMethod.Get, fx.ApiPath("/saves/world1/backups"));
		_out.WriteLine($"[saves] backups(empty) -> {listEmptyStatus} body={Shorten(listEmptyBody)}");
		Assert.Equal(200, listEmptyStatus);

		(int backupStatus, string backupBody) = await h.SendAsync(
			HttpMethod.Post, fx.ApiPath("/saves/world1/backup"), new { });
		_out.WriteLine($"[saves] backup -> {backupStatus} body={Shorten(backupBody)}");
		Assert.Equal(200, backupStatus);
		Assert.True(JsonDocument.Parse(backupBody).RootElement.GetProperty("ok").GetBoolean());

		(int listStatus, string listBody) = await h.SendAsync(HttpMethod.Get, fx.ApiPath("/saves/world1/backups"));
		Assert.Equal(200, listStatus);
		using JsonDocument listDoc = JsonDocument.Parse(listBody);
		JsonElement backups = listDoc.RootElement.GetProperty("backups");
		Assert.True(backups.GetArrayLength() >= 1, $"backup 后备份列表应非空；body={Shorten(listBody)}");
		string backupName = backups[0].GetProperty("fileName").GetString()!;

		(int restoreStatus, string restoreBody) = await h.SendAsync(
			HttpMethod.Post, fx.ApiPath("/saves/world1/backups/" + Uri.EscapeDataString(backupName) + "/restore"), new { });
		_out.WriteLine($"[saves] restore({backupName}) -> {restoreStatus} body={Shorten(restoreBody)}");
		Assert.Equal(200, restoreStatus);

		(int deleteStatus, string deleteBody) = await h.SendAsync(
			HttpMethod.Post, fx.ApiPath("/saves/world1/delete"), new { });
		_out.WriteLine($"[saves] delete -> {deleteStatus} body={Shorten(deleteBody)}");
		Assert.Equal(200, deleteStatus);
	}

	/* ===================== 第 4 层：静态结构守卫（防复发） ===================== */

	/// <summary>
	/// M1-1（2026-11-06）改造后本测试的**语义反转**：
	///
	/// 原实现扫描 <c>HandleApiAsync</c> 方法体内所有 <c>if (seg.Length == N</c> 守卫，
	/// 断言「不得有互斥长度嵌套」。它抓的是 2026-10-03 那次事故的**形态**。
	///
	/// 路由改成 <see cref="ApiRoute"/> 表驱动后，<c>HandleApiAsync</c> 里已经不存在任何
	/// <c>seg.Length</c> 分支——那种嵌套结构在语法上已经无法写出。所以「扫描嵌套」变成
	/// 恒真、无意义的断言（首次运行报的是「只识别到 0 个守卫，解析可能已失效」，
	/// 正是本测试自己设计的"宁可吵闹地失败"保护，说明它诚实报了"我已经没东西可查"）。
	///
	/// **本测试没有删，而是换成同强度、甚至更强的断言**：
	///   1. 旧形态必须彻底消失（方法体内不得再出现 <c>seg.Length</c> 约束分支）；
	///   2. 路由表必须真实存在且非空；
	///   3. 兜底 404 仍在（干的活没丢）。
	/// 路由集合本身的正确性由 <c>ApiRouteInventoryTests</c> 逐条断言（11 个用例覆盖
	/// 可达性、遮蔽、顺序契约、未登记路径、实例子路由不嵌套等）。两个测试合起来
	/// 比原来那条静态扫描更强：原来只能发现"嵌套"，现在连"漏登记"都能发现。
	/// </summary>
	[Fact]
	public void HandleApiAsync_NoLongerContainsSegLengthBranches()
	{
		string src = File.ReadAllText(SourceFilePath());

		const string signature = "private static async Task HandleApiAsync(";
		int sig = src.IndexOf(signature, StringComparison.Ordinal);
		Assert.True(sig > 0, "未找到 HandleApiAsync 定义");

		int open = src.IndexOf('{', sig);
		Assert.True(open > 0, "未找到 HandleApiAsync 方法体起始花括号");

		// 花括号配对求方法体范围。字符串插值中的 {} 始终成对，注释中的 {id} 等亦成对，计数安全。
		int depth = 0;
		int end = -1;
		for (int i = open; i < src.Length; i++)
		{
			if (src[i] == '{') depth++;
			else if (src[i] == '}')
			{
				depth--;
				if (depth == 0) { end = i; break; }
			}
		}
		Assert.True(end > open, "HandleApiAsync 方法体未闭合，提取失败");

		string body = src.Substring(open, end - open + 1);

		// 1) 兜底 404 必须还在（改造不得顺手把兜底删掉，否则未命中请求会返回空 200）。
		Assert.Contains(FallbackMarker, body);

		// 2) 旧形态彻底消失：不得再有任何"用 seg.Length 做条件分支"的写法。
		//    注意 token 带 "if ("：注释里出现的同名文本不算。
		int legacyBranches = 0;
		int scan = 0;
		const string lengthToken = "if (seg.Length";
		while ((scan = body.IndexOf(lengthToken, scan, StringComparison.Ordinal)) >= 0)
		{
			legacyBranches++;
			scan += lengthToken.Length;
		}

		Assert.True(legacyBranches == 0,
			$"HandleApiAsync 内仍有 {legacyBranches} 处 seg.Length 分支。"
			+ "路由必须全部登记到 ApiRoute 表（LocalApiService.Routes），否则无法被清单测试校验——"
			+ "2026-10-03 的 10 条恒 404 路由正是因为路由集合只存在于执行路径里。");

		// 3) 必须真的通过路由表分发，而不是另起一套隐式逻辑。
		Assert.Contains("ResolveRoute", body);

		// 4) 路由表必须可枚举且非空（这是清单测试的前提）。
		Assert.NotNull(LocalApiService.Routes);
		Assert.True(LocalApiService.Routes.Count >= 50,
			$"路由表仅 {LocalApiService.Routes.Count} 条，疑似未正确初始化");
	}

	private static int LineNumberOf(string text, int index)
	{
		int line = 1;
		for (int k = 0; k < index && k < text.Length; k++)
		{
			if (text[k] == '\n') line++;
		}
		return line;
	}

	private static string SnippetAt(string text, int index)
	{
		int start = index;
		while (start > 0 && text[start - 1] != '\n') start--;
		int end = index;
		while (end < text.Length && text[end] != '\n') end++;
		return text.Substring(start, end - start).Trim();
	}

	private static string SourceFilePath() => LocalApiServiceSource.PathOf("LocalApiService.Routing.cs");
}
