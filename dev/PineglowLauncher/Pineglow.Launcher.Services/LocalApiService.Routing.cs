using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Pineglow.Launcher.Models;

namespace Pineglow.Launcher.Services;

/// <summary>
/// 路由表与请求分发。
/// <para>
/// 路由表是**唯一**可枚举的端点清单（M1-1 改造结果）。**顺序即优先级**：取第一个命中项。
/// 新增端点必须在此登记，否则 <c>ApiRouteInventoryTests</c> 抓不到、文档也对不上。
/// </para>
/// </summary>
public static partial class LocalApiService
{
	/// <summary>
	/// 本地 API 路由表。**顺序即优先级**：取第一个命中项，与原先手写 if 链的语义一致。
	///
	/// **M1-1（2026-11-06）改造说明**：原先这里有 516 行 `if (seg.Length == N &amp;&amp; seg[1].Equals(...))`
	/// 手工分支。2026-10-03 出过一次真实事故——4 组要求长度 5/6/8 的守卫被嵌套进约束「长度 4」的
	/// 块内，条件恒假 ⇒ 10 条路由永远落到末尾 404，而当时 796 个绿色测试毫无感知。
	/// 根因不是"写错了"，而是这种结构**无法被机器检查**：路由集合只存在于执行路径里，
	/// 没有可枚举、可断言、可对着文档核对的清单。
	///
	/// 改成表之后：长度与段名约束在同一行内说完，不可能"嵌套出恒假条件"；
	/// 新增路由必须显式登记，漏登记会被 <c>ApiRouteInventoryTests</c> 抓到。
	/// </summary>
	internal static readonly IReadOnlyList<ApiRoute> Routes = BuildRoutes();

	private static List<ApiRoute> BuildRoutes()
	{
		// 段 0 恒为 "api"。{n} 表示该位置为路径参数。
		const int Any = 64; // "任意长度"的统一上界，避免引入 int.MaxValue 这类哨兵值。

		var r = new List<ApiRoute>();

		/* ---------- 系统 ---------- */

		// 存活检查：原条件为 seg.Length >= 2，保持"前缀匹配"语义不变。
		r.Add(ApiRoute.Range("GET", 2, Any, "存活检查", (ctx, _) => WriteHealthAsync(ctx.Response), (1, "health")));
		r.Add(ApiRoute.Exact("GET", 2, "全局状态", (ctx, _) => HandleGetStateAsync(ctx.Response), (1, "state")));
		r.Add(ApiRoute.Exact("POST", 2, "打开日志目录", (ctx, _) =>
		{
			HandleOpenLogDirectory(ctx.Response);
			return Task.CompletedTask;
		}, (1, "logs")));

		/* ---------- 实例 ---------- */

		r.Add(ApiRoute.Exact("GET", 2, "实例列表", (ctx, _) => HandleListInstancesAsync(ctx.Response), (1, "instances")));
		r.Add(ApiRoute.Exact("POST", 2, "创建实例", (ctx, _) => HandleCreateInstanceAsync(ctx.Request, ctx.Response), (1, "instances")));
		r.Add(ApiRoute.Exact("GET", 3, "实例详情", (ctx, s) => HandleInstanceDetailAsync(ctx.Response, s[2]), (1, "instances")));
		r.Add(ApiRoute.Exact("POST", 3, "导入整合包", (ctx, _) => HandleImportInstanceAsync(ctx.Request, ctx.Response), (1, "instances"), (2, "import")));
		r.Add(ApiRoute.Exact("POST", 4, "取消导入", (ctx, _) => HandleCancelImportAsync(ctx.Request, ctx.Response), (1, "instances"), (2, "import"), (3, "cancel")));
		r.Add(ApiRoute.Exact("POST", 3, "重新挂回磁盘上的游戏目录", (ctx, _) => HandleRegisterInstanceAsync(ctx.Request, ctx.Response), (1, "instances"), (2, "register")));

		// 诊断取消必须先于任何更宽松的 diagnose 匹配。
		r.Add(ApiRoute.Exact("POST", 5, "取消 AI 诊断", (ctx, s) => HandleCancelDiagnoseAsync(ctx.Request, ctx.Response, s[2]), (1, "instances"), (3, "diagnose"), (4, "cancel")));
		r.Add(ApiRoute.Exact("GET", 5, "诊断历史", (ctx, s) => HandleDiagnoseHistoryAsync(ctx.Response, s[2]), (1, "instances"), (3, "diagnose"), (4, "history")));
		r.Add(ApiRoute.Exact("GET", 5, "诊断报告导出", (ctx, s) => HandleDiagnoseExportAsync(ctx.Request, ctx.Response, s[2]), (1, "instances"), (3, "diagnose"), (4, "export")));

		// 存档：备份 / 删除 / 打开目录 / 备份列表（长度 6）
		r.Add(ApiRoute.Exact("POST", 6, "创建存档备份", (ctx, s) => HandleCreateSaveBackupAsync(ctx.Response, s[2], s[4]), (1, "instances"), (3, "saves"), (5, "backup")));
		r.Add(ApiRoute.Exact("POST", 6, "删除存档", (ctx, s) => HandleDeleteSaveAsync(ctx.Response, s[2], s[4]), (1, "instances"), (3, "saves"), (5, "delete")));
		r.Add(ApiRoute.Exact("POST", 6, "打开存档目录", (ctx, s) => HandleOpenSaveDirectoryAsync(ctx.Response, s[2], s[4]), (1, "instances"), (3, "saves"), (5, "open")));
		r.Add(ApiRoute.Exact("GET", 6, "存档备份列表", (ctx, s) => HandleListSaveBackupsAsync(ctx.Response, s[2], s[4]), (1, "instances"), (3, "saves"), (5, "backups")));
		// M2-1① 存档可恢复删除：已删除（可恢复）列表 / 恢复 / 永久清理。
		// 段数刻意与 "backups" 区分开：recoveries(6) / recoveries/{op}/restore(8) / recoveries/{op}/purge(8)。
		r.Add(ApiRoute.Exact("GET", 6, "已删除存档（可恢复）列表", (ctx, s) => HandleListSaveRecoveriesAsync(ctx.Response, s[2]), (1, "instances"), (3, "saves"), (5, "recoveries")));
		r.Add(ApiRoute.Exact("POST", 8, "恢复已删除的存档", (ctx, s) => HandleRestoreSaveRecoveryAsync(ctx.Response, s[2], s[6]), (1, "instances"), (3, "saves"), (5, "recoveries"), (7, "restore")));
		r.Add(ApiRoute.Exact("POST", 8, "永久清理存档恢复副本", (ctx, s) => HandlePurgeSaveRecoveryAsync(ctx.Response, s[2], s[6]), (1, "instances"), (3, "saves"), (5, "recoveries"), (7, "purge")));
		r.Add(ApiRoute.Exact("POST", 8, "从备份恢复存档", (ctx, s) => HandleRestoreSaveBackupAsync(ctx.Response, s[2], s[4], s[6]), (1, "instances"), (3, "saves"), (5, "backups"), (7, "restore")));

		// 二分定位
		r.Add(ApiRoute.Exact("POST", 5, "开始二分定位", (ctx, s) => HandleBisectStartAsync(ctx.Response, s[2]), (1, "instances"), (3, "bisect"), (4, "start")));
		r.Add(ApiRoute.Exact("POST", 5, "二分定位测试一轮", (ctx, s) => HandleBisectTestAsync(ctx.Request, ctx.Response, s[2]), (1, "instances"), (3, "bisect"), (4, "test")));
		r.Add(ApiRoute.Exact("POST", 5, "放弃二分定位", (ctx, s) => HandleBisectGiveUpAsync(ctx.Response, s[2]), (1, "instances"), (3, "bisect"), (4, "giveup")));
		r.Add(ApiRoute.Exact("POST", 5, "恢复未完成的二分定位", (ctx, s) => HandleBisectResumeAsync(ctx.Request, ctx.Response, s[2]), (1, "instances"), (3, "bisect"), (4, "resume")));

		// Mod
		r.Add(ApiRoute.Exact("POST", 6, "启用/禁用 Mod", (ctx, s) => HandleToggleModAsync(ctx.Response, s[2], s[4]), (1, "instances"), (3, "mods"), (5, "toggle")));
		r.Add(ApiRoute.Exact("POST", 6, "删除 Mod", (ctx, s) => HandleDeleteModAsync(ctx.Response, s[2], s[4]), (1, "instances"), (3, "mods"), (5, "delete")));
		r.Add(ApiRoute.Exact("POST", 6, "事务化替换 Mod", (ctx, s) => HandleReplaceModAsync(ctx.Request, ctx.Response, s[2], s[4]), (1, "instances"), (3, "mods"), (5, "replace")));

		// 恢复区
		r.Add(ApiRoute.Exact("POST", 6, "把隔离文件恢复到原位置", (ctx, s) => HandleRestoreRecoveryAsync(ctx.Request, ctx.Response, s[2], s[4]), (1, "instances"), (3, "recovery"), (5, "restore")));

		// 长度 4 的实例动作：按 seg[3] 区分，逐一显式登记（不再有"通用块 + 内嵌守卫"）。
		r.Add(ApiRoute.Exact("POST", 4, "删除实例", (ctx, s) => HandleDeleteInstanceAsync(ctx.Response, s[2]), (1, "instances"), (3, "delete")));
		r.Add(ApiRoute.Exact("POST", 4, "启动游戏", (ctx, s) => HandleLaunchAsync(ctx.Response, s[2]), (1, "instances"), (3, "launch")));
		r.Add(ApiRoute.Exact("POST", 4, "AI 诊断", (ctx, s) => HandleDiagnoseInstanceAsync(ctx.Request, ctx.Response, s[2]), (1, "instances"), (3, "diagnose")));
		r.Add(ApiRoute.Exact("GET", 4, "启动前检查", (ctx, s) => HandlePrelaunchCheckAsync(ctx.Response, s[2]), (1, "instances"), (3, "prelaunch-check")));
		r.Add(ApiRoute.Exact("GET", 4, "存档列表", (ctx, s) => HandleListSavesAsync(ctx.Response, s[2]), (1, "instances"), (3, "saves")));
		r.Add(ApiRoute.Exact("GET", 4, "Mod 列表", (ctx, s) => HandleListModsAsync(ctx.Response, s[2]), (1, "instances"), (3, "mods")));
		r.Add(ApiRoute.Exact("GET", 4, "资源包列表", (ctx, s) => HandleListResourcePacksAsync(ctx.Response, s[2]), (1, "instances"), (3, "resourcepacks")));
		r.Add(ApiRoute.Exact("GET", 4, "光影列表", (ctx, s) => HandleListShaderPacksAsync(ctx.Response, s[2]), (1, "instances"), (3, "shaderpacks")));
		r.Add(ApiRoute.Exact("GET", 4, "未完成二分定位查询", (ctx, s) => HandleBisectPendingAsync(ctx.Response, s[2]), (1, "instances"), (3, "bisect")));
		r.Add(ApiRoute.Exact("GET", 4, "恢复区列表", (ctx, s) => HandleListRecoveryAsync(ctx.Response, s[2]), (1, "instances"), (3, "recovery")));

		/* ---------- 账号 ---------- */

		r.Add(ApiRoute.Exact("GET", 2, "账号列表", (ctx, _) => HandleListAccountsAsync(ctx.Response), (1, "accounts")));
		r.Add(ApiRoute.Exact("DELETE", 3, "删除账号", (ctx, s) => HandleDeleteAccountAsync(ctx.Response, s[2]), (1, "accounts")));

		// 微软登录：device / poll 必须先于通用 microsoft 匹配（通用项是 3~Any 段）。
		r.Add(ApiRoute.Exact("POST", 4, "微软设备码登录开始", (ctx, _) => HandleMicrosoftDeviceStartAsync(ctx.Response), (1, "accounts"), (2, "microsoft"), (3, "device")));
		r.Add(ApiRoute.Exact("POST", 4, "微软设备码轮询", (ctx, _) => HandleMicrosoftDevicePollAsync(ctx.Response), (1, "accounts"), (2, "microsoft"), (3, "poll")));
		r.Add(ApiRoute.Range("POST", 3, Any, "微软登录（旧路径）", (ctx, _) => HandleMicrosoftLoginAsync(ctx.Response), (1, "accounts"), (2, "microsoft")));

		r.Add(ApiRoute.Exact("POST", 3, "添加离线账号", (ctx, _) => HandleAddOfflineAccountAsync(ctx.Request, ctx.Response), (1, "accounts"), (2, "offline")));
		r.Add(ApiRoute.Exact("POST", 3, "切换当前账号", (ctx, _) => HandleSelectAccountAsync(ctx.Request, ctx.Response), (1, "accounts"), (2, "select")));
		r.Add(ApiRoute.Exact("POST", 3, "刷新皮肤", (ctx, _) => HandleRefreshSkinAsync(ctx.Request, ctx.Response), (1, "accounts"), (2, "refresh-skin")));
		r.Add(ApiRoute.Exact("POST", 3, "Authlib 登录", (ctx, _) => HandleAuthlibLoginAsync(ctx.Request, ctx.Response), (1, "accounts"), (2, "authlib")));
		r.Add(ApiRoute.Exact("POST", 3, "上传皮肤", (ctx, _) => HandleUploadSkinAsync(ctx.Request, ctx.Response), (1, "accounts"), (2, "skin")));
		r.Add(ApiRoute.Exact("GET", 4, "账号皮肤图", (ctx, s) => HandleAccountSkinImageAsync(ctx.Response, s[2]), (1, "accounts"), (3, "skin-image")));

		/* ---------- 版本 / 下载 ---------- */

		r.Add(ApiRoute.Exact("GET", 2, "本地版本列表", (ctx, _) => HandleListVersionsAsync(ctx.Response), (1, "versions")));
		r.Add(ApiRoute.Exact("GET", 3, "远端版本列表", (ctx, _) => HandleListRemoteVersionsAsync(ctx.Response), (1, "versions"), (2, "remote")));
		r.Add(ApiRoute.Exact("POST", 3, "下载版本", (ctx, _) => HandleDownloadVersionAsync(ctx.Request, ctx.Response), (1, "versions"), (2, "download")));
		r.Add(ApiRoute.Exact("GET", 2, "下载任务列表", (ctx, _) => HandleListDownloadsAsync(ctx.Response), (1, "downloads")));

		/* ---------- 新闻 ---------- */

		r.Add(ApiRoute.Exact("GET", 2, "新闻列表", (ctx, _) => HandleListNewsAsync(ctx.Request, ctx.Response), (1, "news")));
		r.Add(ApiRoute.Exact("GET", 2, "新闻配图", (ctx, _) => HandleNewsImageAsync(ctx.Request, ctx.Response), (1, "news-image")));
		r.Add(ApiRoute.Exact("POST", 2, "受控打开新闻外链", (ctx, _) => HandleNewsOpenAsync(ctx.Request, ctx.Response), (1, "news-open")));

		/* ---------- 更新 ---------- */

		r.Add(ApiRoute.Exact("GET", 2, "更新状态", (ctx, _) => HandleUpdateStatusAsync(ctx.Response), (1, "update")));
		r.Add(ApiRoute.Exact("GET", 3, "检查更新", (ctx, _) => HandleCheckUpdateAsync(ctx.Response), (1, "update"), (2, "check")));
		r.Add(ApiRoute.Exact("POST", 3, "下载更新", (ctx, _) => HandleStartUpdateDownloadAsync(ctx.Response), (1, "update"), (2, "download")));
		r.Add(ApiRoute.Exact("POST", 3, "应用更新", (ctx, _) => HandleApplyUpdateAsync(ctx.Response), (1, "update"), (2, "apply")));

		/* ---------- 配置 / 存档 / 内容 ---------- */

		r.Add(ApiRoute.Exact("POST", 2, "写 config.json", (ctx, _) => HandleUpdateConfigAsync(ctx.Request, ctx.Response), (1, "config")));
		r.Add(ApiRoute.Exact("GET", 2, "存档目录列表", (ctx, _) => HandleListWorldsAsync(ctx.Response), (1, "worlds")));

		r.Add(ApiRoute.Exact("GET", 2, "内容搜索（无子路径）", (ctx, _) => HandleSearchContentAsync(ctx.Request, ctx.Response), (1, "content")));
		r.Add(ApiRoute.Exact("GET", 3, "内容搜索", (ctx, _) => HandleSearchContentAsync(ctx.Request, ctx.Response), (1, "content"), (2, "search")));
		r.Add(ApiRoute.Exact("GET", 3, "内容安装任务", (ctx, _) => HandleListContentTasksAsync(ctx.Response), (1, "content"), (2, "tasks")));
		r.Add(ApiRoute.Exact("GET", 3, "内容图标", (ctx, _) => HandleContentIconAsync(ctx.Request, ctx.Response), (1, "content"), (2, "icon")));
		r.Add(ApiRoute.Exact("POST", 3, "安装内容", (ctx, _) => HandleContentInstallAsync(ctx.Request, ctx.Response), (1, "content"), (2, "install")));

		/* ---------- 环境 / 统计 / 下载源 / 服务器 ---------- */

		r.Add(ApiRoute.Exact("GET", 2, "Java 检测", (ctx, _) => HandleListJavaAsync(ctx.Response), (1, "java")));
		r.Add(ApiRoute.Exact("GET", 2, "游玩统计", (ctx, _) => HandleGetStatsAsync(ctx.Response), (1, "stats")));
		r.Add(ApiRoute.Exact("GET", 2, "下载源列表", (ctx, _) => HandleListSourcesAsync(ctx.Response), (1, "sources")));
		r.Add(ApiRoute.Exact("POST", 3, "选择下载源", (ctx, _) => HandleSelectSourceAsync(ctx.Request, ctx.Response), (1, "sources"), (2, "select")));

		r.Add(ApiRoute.Exact("GET", 2, "服务器列表", (ctx, _) => HandleListServersAsync(ctx.Response), (1, "servers")));
		r.Add(ApiRoute.Exact("POST", 2, "添加服务器", (ctx, _) => HandleAddServerAsync(ctx.Request, ctx.Response), (1, "servers")));
		r.Add(ApiRoute.Exact("POST", 4, "Ping 服务器", (ctx, s) => HandlePingServerAsync(ctx.Response, s[2]), (1, "servers"), (3, "ping")));
		r.Add(ApiRoute.Exact("POST", 4, "删除服务器", (ctx, s) => HandleDeleteServerAsync(ctx.Response, s[2]), (1, "servers"), (3, "delete")));

		return r;
	}

	private static Task WriteHealthAsync(HttpListenerResponse response)
		=> WriteJsonAsync(response, 200, new { ok = true, pid = Environment.ProcessId, port = Port });

	/// <summary>按表顺序取第一个命中路由；无命中返回 null。供 <see cref="HandleApiAsync"/> 与清单测试共用。</summary>
	internal static ApiRoute? ResolveRoute(string method, string[] seg)
	{
		foreach (ApiRoute route in Routes)
		{
			if (route.Matches(method, seg))
			{
				return route;
			}
		}

		return null;
	}

	private static async Task HandleApiAsync(HttpListenerContext context, string path)
	{
		HttpListenerRequest request = context.Request;
		HttpListenerResponse response = context.Response;
		string method = request.HttpMethod.ToUpperInvariant();
		string[] seg = path.Trim('/').Split('/').Select(s => Uri.UnescapeDataString(s)).ToArray();
		// seg = ["api", ...]

		try
		{
			ApiRoute? route = ResolveRoute(method, seg);
			if (route != null)
			{
				await route.Handler(context, seg);
				return;
			}

			await WriteJsonAsync(response, 404, new { error = $"未找到接口：{method} {path}" });
		}
		catch (InvalidOperationException ex)
		{
			await WriteJsonAsync(response, 400, new { error = ex.Message });
		}
		catch (Exception ex)
		{
			Debug.WriteLine($"[LocalApi] api error: {ex}");
			await WriteJsonAsync(response, 500, new { error = ex.Message });
		}
	}
}
