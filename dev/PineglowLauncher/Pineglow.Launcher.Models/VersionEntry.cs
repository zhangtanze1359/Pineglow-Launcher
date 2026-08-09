namespace Pineglow.Launcher.Models;

public sealed class VersionEntry
{
	public string Id { get; set; } = string.Empty;

	public string Type { get; set; } = string.Empty;

	public string Url { get; set; } = string.Empty;

	public string ReleaseTime { get; set; } = string.Empty;

	public override string ToString()
	{
		if (!string.IsNullOrWhiteSpace(Type))
		{
			return Id + " [" + Type + "]";
		}
		return Id;
	}
}
