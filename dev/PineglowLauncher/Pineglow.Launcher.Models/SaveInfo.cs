using System;

namespace Pineglow.Launcher.Models;

public sealed class SaveInfo
{
	public string FolderName { get; set; } = string.Empty;

	public string FullPath { get; set; } = string.Empty;

	public string DisplayName { get; set; } = string.Empty;

	public DateTime LastPlayed { get; set; }

	public string SizeText { get; set; } = string.Empty;

	/// <summary>
	/// 存档目录的字节大小。与 <see cref="SizeText"/> 同源，供**纯判定**使用
	/// （D3② 退出后自动备份要「只跳过明确为空的存档」，Planner 不许自己碰文件系统）。
	/// </summary>
	public long SizeBytes { get; set; }

	public bool HasIcon { get; set; }

	public byte[]? IconBytes { get; set; }
}
