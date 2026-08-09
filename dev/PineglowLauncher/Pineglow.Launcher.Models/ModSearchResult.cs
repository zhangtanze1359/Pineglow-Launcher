using System.Collections.Generic;

namespace Pineglow.Launcher.Models;

public sealed class ModSearchResult
{
	public ModSource Source { get; set; }

	public string ProjectId { get; set; } = string.Empty;

	public string Slug { get; set; } = string.Empty;

	public string Title { get; set; } = string.Empty;

	public string Description { get; set; } = string.Empty;

	public string Author { get; set; } = string.Empty;

	public string IconUrl { get; set; } = string.Empty;

	public string WebsiteUrl { get; set; } = string.Empty;

	public long Downloads { get; set; }

	public List<string> Categories { get; set; } = new List<string>();

	public List<string> GameVersions { get; set; } = new List<string>();
}
