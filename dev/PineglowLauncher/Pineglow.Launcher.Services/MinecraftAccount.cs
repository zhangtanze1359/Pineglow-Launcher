using System;
using System.Collections.Generic;
using Pineglow.Launcher.Models;

namespace Pineglow.Launcher.Services;

public sealed class MinecraftAccount
{
	public string UserName { get; init; } = string.Empty;

	public string Uuid { get; init; } = string.Empty;

	public string AccessToken { get; init; } = string.Empty;

	public DateTimeOffset AccessTokenExpiresAt { get; init; }

	public string Xuid { get; init; } = string.Empty;

	public string RefreshToken { get; init; } = string.Empty;

	public string SkinUrl { get; init; } = string.Empty;

	public int SkinCount { get; init; }

	public int CapeCount { get; init; }

	public List<MinecraftCapeInfo> Capes { get; init; } = new List<MinecraftCapeInfo>();
}
