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
/// 新闻与图片代理。
/// <para>
/// 新闻列表、外链跳转与图片代理。外链由服务端持有 linkId（客户端永不提交 URL），
/// 图片走独立的 <c>NewsHttp</c> 客户端，fake-ip 兼容默认 fail closed。
/// </para>
/// </summary>
public static partial class LocalApiService
{
	private static async Task HandleListNewsAsync(HttpListenerRequest request, HttpListenerResponse response)
	{
		try
		{
			using CancellationTokenSource cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
			bool forceRefresh = string.Equals(request.QueryString["refresh"], "1", StringComparison.OrdinalIgnoreCase);
			NewsFetchResult result = await NewsService.FetchAsync(limit: 12, cts.Token, forceRefresh: forceRefresh);
			await WriteJsonAsync(response, 200, new
			{
				items = result.Items.Select(n => new
				{
					id = n.Id,
					title = n.Title,
					category = n.Category,
					date = n.Date,
					text = n.Text,
					imageId = RegisterNewsImage(n.ImageUrl),
					link = n.Link,
					linkOk = NewsEndpointPolicy.IsAllowedLink(n.Link),
					linkId = RegisterNewsLink(n.Link)
				}),
				meta = new
				{
					source = result.Source,
					state = NewsContentStateText.ToWire(result.State),
					validatedAt = result.ValidatedAt,
					fetchedAt = result.FetchedAt,
					isStale = result.IsStale,
					canRetry = result.CanRetry,
					errorKind = result.ErrorKind,
					error = result.Error
				}
			});
		}
		catch (Exception ex)
		{
			// 对外只暴露稳定错误元数据，不含堆栈与内部路径
			Debug.WriteLine("[LocalApi] news fetch error: " + ex.GetType().Name);
			await WriteJsonAsync(response, 200, new
			{
				items = new object[0],
				meta = new
				{
					source = LauncherContentNewsProvider.DefaultEndpoint.Replace("/news.json", string.Empty).Replace("https://", string.Empty),
					state = "error",
					validatedAt = DateTimeOffset.MinValue,
					fetchedAt = DateTimeOffset.MinValue,
					isStale = true,
					canRetry = true,
					errorKind = "internal",
					error = "news request failed"
				}
			});
		}
	}

	private static string? RegisterNewsImage(string imageUrl)
	{
		if (string.IsNullOrWhiteSpace(imageUrl))
		{
			return null;
		}

		return NewsImages.RegisterImageUrl(imageUrl);
	}

	/// <summary>
	/// R2-03：把新闻项的 link 注册到 NewsLinkService，返回稳定 linkId。
	/// 仅当 NewsEndpointPolicy.IsAllowedLink 放行时返回非空；非法/空链接返回 null。
	/// </summary>
	private static string? RegisterNewsLink(string? link)
	{
		if (string.IsNullOrWhiteSpace(link)) return null;
		// 二次门：仅在白名单内才生成 ID，与 /api/news 响应的 linkOk 保持一致
		if (!NewsEndpointPolicy.IsAllowedLink(link)) return null;
		return NewsLinks.RegisterLink(link);
	}

	/// <summary>
	/// R2-03：POST /api/news-open — 客户端只传 linkId；服务端取回 URL 后再次白名单校验，
	/// 通过才经受控 opener 打开已登记 URL。响应稳定 JSON，opener 失败 5xx 不泄露详情。
	/// </summary>
	private static async Task HandleNewsOpenAsync(HttpListenerRequest request, HttpListenerResponse response)
	{
		string body;
		try
		{
			using StreamReader reader = new StreamReader(request.InputStream, request.ContentEncoding ?? Encoding.UTF8);
			body = await reader.ReadToEndAsync();
		}
		catch (Exception ex)
		{
			Debug.WriteLine("[LocalApi] news-open read body error: " + ex.GetType().Name);
			await WriteJsonAsync(response, 400, new { error = "invalid_request" });
			return;
		}

		// 1) 严格 JSON 协议:body 必须是仅含精确 linkId 属性的对象
		//    协议要求(opener-failure-contract 返工 review-fix-2, 2026-09-10):
		//    - body 必须是对象
		//    - 仅允许一个属性 linkId(精确大小写)
		//    - linkId 必须是字符串且非空
		//    - 任何其他属性(包括 id / link_id / url / href / link / 重复 linkId / 大小写变体)
		//      或 linkId 非字符串/为空 → 400,opener 零调用
		string? linkId = null;
		try
		{
			using JsonDocument doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(body) ? "{}" : body);
			JsonElement root = doc.RootElement;
			if (root.ValueKind != JsonValueKind.Object)
			{
				await WriteJsonAsync(response, 400, new { error = "invalid_body" });
				return;
			}
			int propertyCount = 0;
			foreach (JsonProperty prop in root.EnumerateObject())
			{
				propertyCount++;
				if (propertyCount > 1)
				{
					// 出现超过 1 个属性 → 直接 400(不区分具体多出来的属性)
					await WriteJsonAsync(response, 400, new { error = "invalid_body" });
					return;
				}
				if (!string.Equals(prop.Name, "linkId", StringComparison.Ordinal))
				{
					// 任何非精确 linkId 属性(包含 id / link_id / url / href / link / 任意其它)→ 400
					await WriteJsonAsync(response, 400, new { error = "invalid_body" });
					return;
				}
				if (prop.Value.ValueKind != JsonValueKind.String)
				{
					// linkId 必须是非空字符串
					await WriteJsonAsync(response, 400, new { error = "invalid_body" });
					return;
				}
				linkId = prop.Value.GetString();
			}
		}
		catch (JsonException)
		{
			await WriteJsonAsync(response, 400, new { error = "invalid_body" });
			return;
		}

		if (string.IsNullOrWhiteSpace(linkId))
		{
			await WriteJsonAsync(response, 400, new { error = "invalid_body" });
			return;
		}

		// 2) 业务级原子操作:单一调用覆盖 GetLinkUrl + 二次白名单 + opener
		//    (review-fix-3, 2026-09-10: 消除 LRU 淘汰/并发窗口的虚假 200)
		NewsLinkService.OpenOutcome outcome;
		try
		{
			outcome = NewsLinks.OpenOrFail(linkId);
		}
		catch (Exception ex)
		{
			Debug.WriteLine("[LocalApi] news-open opener error: " + ex.GetType().Name);
			// opener 异常 → 500,响应不暴露原始 URL / 异常堆栈 / 内部路径 / opener 消息文本
			await WriteJsonAsync(response, 500, new { error = "opener_failed" });
			return;
		}

		switch (outcome)
		{
			case NewsLinkService.OpenOutcome.Success:
				await WriteJsonAsync(response, 200, new { ok = true });
				return;
			case NewsLinkService.OpenOutcome.LinkNotFound:
				// 未知 / 篡改 / 过期 / 淘汰 / 竞态窗口 ID → 业务 404
				await WriteJsonAsync(response, 404, new { error = "link_not_found" });
				return;
			case NewsLinkService.OpenOutcome.LinkNoLongerAllowed:
				// 映射项不再通过白名单(策略被收紧):业务拒绝,映射项仍保留以利诊断
				await WriteJsonAsync(response, 400, new { error = "link_no_longer_allowed" });
				return;
		}
	}

	private static async Task HandleNewsImageAsync(HttpListenerRequest request, HttpListenerResponse response)
	{
		// Phase 3A：客户端只允许提交服务端注册的图片 ID，不再接受任意上游 URL
		string id = request.QueryString["id"] ?? string.Empty;
		if (string.IsNullOrWhiteSpace(id))
		{
			await WriteJsonAsync(response, 400, new { error = "imageId 不能为空" });
			return;
		}

		try
		{
			using CancellationTokenSource cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
			byte[] bytes = await NewsImages.FetchImageAsync(id, cts.Token);
			response.Headers["X-Content-Type-Options"] = "nosniff";
			response.Headers["Content-Security-Policy"] = "default-src 'none'";
			response.Headers["Cache-Control"] = "private, max-age=1800";
			response.Headers["Referrer-Policy"] = "no-referrer";
			response.ContentType = "application/octet-stream";
			response.ContentLength64 = bytes.Length;
			await response.OutputStream.WriteAsync(bytes, 0, bytes.Length);
		}
		catch (NewsImageException ex)
		{
			int status = ex.Kind switch
			{
				"unknown_image" => 404,
				"policy_rejected" or "dns_private" or "dns_mixed" or "dns_failed" or "mime_rejected" or "decode_rejected" or "size_rejected" or "redirect_limit" or "redirect_invalid" => 400,
				"timeout" => 504,
				_ => 502
			};
			await WriteJsonAsync(response, status, new { error = ex.Kind });
		}
		catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or TaskCanceledException)
		{
			await WriteJsonAsync(response, 502, new { error = "image fetch failed" });
		}
	}
}
