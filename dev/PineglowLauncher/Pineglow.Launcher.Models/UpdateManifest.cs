using System;

namespace Pineglow.Launcher.Models;

/// <summary>更新服务器 manifest：GET updateUrl 返回该 JSON。</summary>
public sealed class UpdateManifest
{
	public string Version { get; set; } = string.Empty;

	public string Url { get; set; } = string.Empty;

	/// <summary>备用下载地址（主地址失败时回退），可空。</summary>
	public string FallbackUrl { get; set; } = string.Empty;

	public long Size { get; set; }

	public string Sha256 { get; set; } = string.Empty;

	public string Notes { get; set; } = string.Empty;

	public bool Mandatory { get; set; }
}
