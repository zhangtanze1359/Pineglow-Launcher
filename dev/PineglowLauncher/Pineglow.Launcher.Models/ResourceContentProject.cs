namespace Pineglow.Launcher.Models;

public sealed class ResourceContentProject
{
	public string ProjectId { get; set; } = string.Empty;

	public string Slug { get; set; } = string.Empty;

	public string Title { get; set; } = string.Empty;

	public string Description { get; set; } = string.Empty;

	public string Author { get; set; } = string.Empty;

	public string WebsiteUrl { get; set; } = string.Empty;

	public long Downloads { get; set; }

	public ResourceContentKind Kind { get; set; }

	public string IconUrl { get; set; } = string.Empty;
}
