using System;
using System.Collections.Generic;
using System.Security.Cryptography;

namespace Pineglow.Launcher.Services;

/// <summary>
/// R2-03 新闻外链 linkId 注册表。
///
/// 安全闭环约束（phase3a-r203-minimal-green-workflow-2026-09-10.md §1）：
///   1. 注册输入必须先经 <see cref="NewsEndpointPolicy.IsAllowedLink"/>。白名单外/空链接/非 HTTPS 一律不生成 ID。
///   2. ID 至少 128 位随机性，URL-safe；同一规范化 URL 在同一进程生命周期内返回稳定 ID。
///   3. 双向映射在锁内原子维护；容量上限 <see cref="Capacity"/>，超出时淘汰最久未访问项。
///   4. 不得把 linkId-URL 映射持久化到用户配置。
///   5. 打开 URL 前必须再次 <see cref="NewsEndpointPolicy.IsAllowedLink"/>。
///   6. opener seam <see cref="INewsLinkOpener"/>：生产用 Windows shell，测试用受控 mock（自动化测试永不拉起浏览器）。
/// </summary>
public sealed class NewsLinkService
{
	/// <summary>映射容量上限。达到上限时淘汰最久未注册项（LRU 简化：注册时移到 MRU）。</summary>
	public const int Capacity = 1024;

	/// <summary>受控 opener seam。生产实现用 Windows shell 打开 URL；测试可注入 mock。</summary>
	public interface INewsLinkOpener
	{
		void Open(string url);
	}

	private readonly object _gate = new();
	private readonly Dictionary<string, string> _byId = new(StringComparer.Ordinal);
	private readonly Dictionary<string, string> _byUrl = new(StringComparer.Ordinal);
	private readonly LinkedList<string> _idOrder = new();
	private readonly Dictionary<string, LinkedListNode<string>> _idNode = new(StringComparer.Ordinal);
	private readonly Func<string> _idGenerator;
	private INewsLinkOpener _opener;

	public NewsLinkService() : this(DefaultIdGenerator, new WindowsShellNewsLinkOpener()) { }

	/// <summary>DI/测试构造：自定义 ID 生成器与 opener。</summary>
	public NewsLinkService(Func<string> idGenerator, INewsLinkOpener opener)
	{
		_idGenerator = idGenerator ?? throw new ArgumentNullException(nameof(idGenerator));
		_opener = opener ?? throw new ArgumentNullException(nameof(opener));
	}

	/// <summary>当前映射条数（含被淘汰项的瞬时态，不暴露枚举）。</summary>
	public int Count
	{
		get { lock (_gate) return _byId.Count; }
	}

	/// <summary>注册一个 URL，返回服务端稳定 linkId；白名单外 / 危险形态返回 null。</summary>
	public string? RegisterLink(string? url)
	{
		if (string.IsNullOrWhiteSpace(url)) return null;
		if (!NewsEndpointPolicy.IsAllowedLink(url)) return null;

		lock (_gate)
		{
			if (_byUrl.TryGetValue(url, out string? existing))
			{
				// 同一 URL 重复注册：刷新到 MRU，返回原 ID（保证会话内稳定）
				if (_idNode.TryGetValue(existing, out LinkedListNode<string>? node))
				{
					_idOrder.Remove(node);
					_idNode[existing] = _idOrder.AddLast(existing);
				}
				return existing;
			}

			// 容量淘汰
			if (_byId.Count >= Capacity)
			{
				LinkedListNode<string> oldestNode = _idOrder.First!;
				string oldestId = oldestNode.Value;
				_idOrder.RemoveFirst();
				_idNode.Remove(oldestId);
				_byUrl.Remove(_byId[oldestId]);
				_byId.Remove(oldestId);
			}

			string id = _idGenerator();
			_byId[id] = url;
			_byUrl[url] = id;
			_idNode[id] = _idOrder.AddLast(id);
			return id;
		}
	}

	/// <summary>由 linkId 取回已注册 URL；未知 / 篡改 / 过期 ID 返回 null。</summary>
	public string? GetLinkUrl(string? linkId)
	{
		if (string.IsNullOrWhiteSpace(linkId)) return null;
		lock (_gate)
		{
			return _byId.TryGetValue(linkId, out string? url) ? url : null;
		}
	}

	/// <summary>受控打开：取回 URL 后再次 <see cref="NewsEndpointPolicy.IsAllowedLink"/> 二次校验，通过才调 <see cref="INewsLinkOpener.Open"/>。</summary>
	/// <returns>true 表示已成功传给 opener。</returns>
	/// <exception cref="NewsLinkOpenerException">opener 抛出异常时透传；调用方负责映射为受控 5xx 响应。</exception>
	public bool TryOpen(string? linkId, Action<string>? onSuccess, Action<string>? onError = null)
	{
		if (string.IsNullOrWhiteSpace(linkId)) return false;
		string? url = GetLinkUrl(linkId);
		if (string.IsNullOrEmpty(url)) return false;
		// 二次白名单校验：即使注册时已校验，仍在打开前重审
		if (!NewsEndpointPolicy.IsAllowedLink(url)) return false;
		// 不吞 opener 异常：调用方（handler）必须捕获并映射为受控 5xx。
		// 吞异常会导致 WebView 把"启动失败"误判为"成功"，违反 opener 失败契约。
		lock (_gate) { _opener.Open(url); }
		onSuccess?.Invoke(url);
		return true;
	}

	/// <summary>
	/// R2-03 TryOpen 结果/淘汰竞态返工（review-fix-3, 2026-09-10）：
	/// 把"查找 ID + 二次白名单 + opener.Open"合并为单一原子操作，避免 handler 分次调用
	/// 在 LRU 淘汰/并发窗口下错误返回 {ok:true} 而无实际 opener 调用。
	/// 返回 enum 而非 bool,handler 据此决定 200/400/404/500。
	/// </summary>
	public enum OpenOutcome
	{
		/// <summary>opener 已成功接收 URL。</summary>
		Success,
		/// <summary>linkId 未知 / 已被淘汰 / 失效。</summary>
		LinkNotFound,
		/// <summary>linkId 已知,但 URL 不再通过白名单(策略被收紧)。</summary>
		LinkNoLongerAllowed,
	}

	/// <summary>原子打开：单一 lock 内 GetLinkUrl + 二次白名单 + opener。opener 异常直接透传,由调用方映射为 5xx。</summary>
	/// <returns>OpenOutcome。opener 抛异常时不返回,直接外抛。</returns>
	public OpenOutcome OpenOrFail(string? linkId)
	{
		if (string.IsNullOrWhiteSpace(linkId)) return OpenOutcome.LinkNotFound;

		// 原子块:单 lock 内 GetLinkUrl + 二次白名单 + opener,避免分次调用产生竞态窗口
		lock (_gate)
		{
			// 测试 seam 优先:模拟"LRU 淘汰/并发窗口" — ID 在该次调用前仍存在但本调用应被识别为失效
			if (_invalidateOnNextCallForTest == linkId)
			{
				_invalidateOnNextCallForTest = null;
				return OpenOutcome.LinkNotFound;
			}

			string? url = _byId.TryGetValue(linkId, out string? v) ? v : null;
			if (string.IsNullOrEmpty(url)) return OpenOutcome.LinkNotFound;
			if (!NewsEndpointPolicy.IsAllowedLink(url)) return OpenOutcome.LinkNoLongerAllowed;

			// 不 catch:opener 异常透传,handler 映射 500
			_opener.Open(url);
			return OpenOutcome.Success;
		}
	}

	/* ===========================================================
	 * 反射 seam（仅测试可访问）：让 NewsLinkHandlerTests 能注入受控 opener 与受控 ID 生成器
	 *
	 * 通过反射访问，避免污染公共 API。
	 * =========================================================== */

	internal void SetOpenerForTest(INewsLinkOpener opener)
	{
		lock (_gate) { _opener = opener ?? throw new ArgumentNullException(nameof(opener)); }
	}

	/// <summary>测试 seam：让"下一次该 linkId 的 OpenOrFail"模拟 LRU 淘汰后返回 LinkNotFound（一次性）。</summary>
	internal void InvalidateOnNextCallForTest(string? linkId)
	{
		lock (_gate) { _invalidateOnNextCallForTest = linkId; }
	}

	private string? _invalidateOnNextCallForTest;

	internal INewsLinkOpener GetOpenerForTest()
	{
		lock (_gate) { return _opener; }
	}

	internal void ResetForTest()
	{
		lock (_gate)
		{
			_byId.Clear();
			_byUrl.Clear();
			_idOrder.Clear();
			_idNode.Clear();
		}
	}

	/* ===========================================================
	 * 默认 ID 生成器：128 位随机 → URL-safe Base64
	 * =========================================================== */

	private static string DefaultIdGenerator()
	{
		Span<byte> bytes = stackalloc byte[16];
		RandomNumberGenerator.Fill(bytes);
		string b64 = Convert.ToBase64String(bytes);
		return b64.Replace('+', '-').Replace('/', '_').TrimEnd('=');
	}
}

/// <summary>生产 opener：用 Windows shell 启动 URL（ShellExecute）。不直接同步等浏览器关闭。</summary>
internal sealed class WindowsShellNewsLinkOpener : NewsLinkService.INewsLinkOpener
{
	public void Open(string url)
	{
		// 仅在 Windows 平台启动；非 Windows 抛 PlatformNotSupportedException 让 TryOpen 转 5xx
		if (!System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(
			System.Runtime.InteropServices.OSPlatform.Windows))
		{
			throw new PlatformNotSupportedException("Windows shell opener 仅在 Windows 平台可用");
		}
		var psi = new System.Diagnostics.ProcessStartInfo
		{
			FileName = url,
			UseShellExecute = true,
		};
		System.Diagnostics.Process.Start(psi);
	}
}
