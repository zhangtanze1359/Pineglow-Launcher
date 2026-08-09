using System.Collections.Generic;
using Pineglow.Launcher.Models;

namespace Pineglow.Launcher.Services;

public sealed class MinecraftAppearance
{
	public string SkinUrl { get; init; } = string.Empty;

	public int SkinCount { get; init; }

	public int CapeCount { get; init; }

	public List<MinecraftCapeInfo> Capes { get; init; } = new List<MinecraftCapeInfo>();
}
