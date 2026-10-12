using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Pineglow.Launcher.Models;

public sealed class LauncherAccount
{
	public string Id { get; set; } = Guid.NewGuid().ToString("N");

	public string Type { get; set; } = "offline";

	public string UserName { get; set; } = "Player";

	public string Uuid { get; set; } = string.Empty;

	public string Xuid { get; set; } = string.Empty;

	public string ClientId { get; set; } = string.Empty;

	public string AuthServerUrl { get; set; } = string.Empty;

	public string SkinUrl { get; set; } = string.Empty;

	public string SkinModel { get; set; } = "classic";

	public int SkinCount { get; set; }

	public int CapeCount { get; set; }

	public List<MinecraftCapeInfo> Capes { get; set; } = new List<MinecraftCapeInfo>();

	public DateTimeOffset AccessTokenExpiresAt { get; set; } = DateTimeOffset.MinValue;

	[JsonIgnore]
	public string RefreshToken { get; set; } = string.Empty;

	[JsonIgnore]
	public string AccessToken { get; set; } = string.Empty;

	public bool IsMicrosoft => Type.Equals("microsoft", StringComparison.OrdinalIgnoreCase);

	public bool IsThirdParty => Type.Equals("authlib", StringComparison.OrdinalIgnoreCase);

	public override string ToString()
	{
		return UserName + "  ·  " + (IsMicrosoft ? "Microsoft" : (IsThirdParty ? "第三方" : "离线"));
	}
}
