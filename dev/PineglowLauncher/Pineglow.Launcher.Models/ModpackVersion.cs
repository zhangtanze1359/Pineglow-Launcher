using System;
using System.Collections.Generic;
using System.Linq;

namespace Pineglow.Launcher.Models;

public sealed class ModpackVersion
{
	public string VersionId { get; set; } = string.Empty;

	public string VersionNumber { get; set; } = string.Empty;

	public string ReleaseType { get; set; } = string.Empty;

	public string FileName { get; set; } = string.Empty;

	public string DownloadUrl { get; set; } = string.Empty;

	public string Sha1 { get; set; } = string.Empty;

	public string Sha512 { get; set; } = string.Empty;

	public List<string> GameVersions { get; set; } = new List<string>();

	public List<string> Loaders { get; set; } = new List<string>();

	public DateTimeOffset PublishedAt { get; set; } = DateTimeOffset.MinValue;

	public override string ToString()
	{
		string text = ((GameVersions.Count == 0) ? "未标注游戏版本" : string.Join(" / ", GameVersions.Take(2)));
		string text2 = ((Loaders.Count == 0) ? string.Empty : (" · " + string.Join(" / ", Loaders.Take(2))));
		return VersionNumber + " · " + text + text2;
	}
}
