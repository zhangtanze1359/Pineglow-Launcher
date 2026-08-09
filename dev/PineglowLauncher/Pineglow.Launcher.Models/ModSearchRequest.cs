namespace Pineglow.Launcher.Models;

public sealed class ModSearchRequest
{
	public ModSource Source { get; set; }

	public string Query { get; set; } = string.Empty;

	public string MinecraftVersion { get; set; } = string.Empty;

	public string Loader { get; set; } = string.Empty;

	public string CurseForgeApiKey { get; set; } = string.Empty;

	public int Limit { get; set; } = 20;
}
