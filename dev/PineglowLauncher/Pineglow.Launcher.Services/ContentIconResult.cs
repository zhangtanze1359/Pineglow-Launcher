using System;

namespace Pineglow.Launcher.Services;

/// <summary>
/// 内容资源图标（Mod / Modpack / Shader / ResourcePack）下载/缓存结果。
/// 任何错误都不携带内部网络细节、token 或 API Key 字段。
/// </summary>
public enum IconStatus
{
	/// <summary>没有可信 URL（搜索结果中无图标或映射已过期）→ 404。</summary>
	NotFound = 0,
	/// <summary>成功返回经过验证的图片字节。</summary>
	Ok = 1,
	/// <summary>命中磁盘缓存（未过期），未发起任何网络请求。</summary>
	FromCache = 2,
	/// <summary>命中过期缓存，但上游当前不可达，按 stale 返回（标注 X-Pineglow-Cache: stale）。</summary>
	Stale = 3,
	/// <summary>Content-Length 预报或流式累计超过 2 MiB。</summary>
	TooLarge = 4,
	/// <summary>Content-Type 不在白名单。</summary>
	BadContentType = 5,
	/// <summary>魔数 / 文件签名无效或被截断。</summary>
	BadImage = 6,
	/// <summary>上游返回 4xx/5xx。</summary>
	UpstreamError = 7,
	/// <summary>连接或总超时或调用方取消。</summary>
	NetworkError = 8,
}

public sealed class IconResult
{
	public IconStatus Status { get; init; }
	/// <summary>经过 Content-Type + 魔数双重校验后的图片字节（仅 Status ∈ {Ok, FromCache, Stale} 非空）。</summary>
	public byte[]? Bytes { get; init; }
	/// <summary>规范化后的真实 MIME，例如 image/png。</summary>
	public string? Mime { get; init; }
	/// <summary>磁盘缓存路径（仅在写入或命中磁盘时设置）。</summary>
	public string? CachePath { get; init; }
	/// <summary>上游响应中的 ETag（用于条件请求）。</summary>
	public string? ETag { get; init; }
	/// <summary>上游响应中的 Last-Modified（用于条件请求）。</summary>
	public string? LastModified { get; init; }

	// ----- 便捷工厂方法 -----
	public static IconResult NotFound() => new IconResult { Status = IconStatus.NotFound };
	public static IconResult Of(IconStatus status) => new IconResult { Status = status };
	public static IconResult Success(byte[] bytes, string mime, string cachePath, string? etag, string? lastModified)
		=> new IconResult { Status = IconStatus.Ok, Bytes = bytes, Mime = mime, CachePath = cachePath, ETag = etag, LastModified = lastModified };
	public static IconResult CacheHit(byte[] bytes, string mime, string cachePath, string? etag, string? lastModified, bool stale)
		=> new IconResult { Status = stale ? IconStatus.Stale : IconStatus.FromCache, Bytes = bytes, Mime = mime, CachePath = cachePath, ETag = etag, LastModified = lastModified };
}
