using System;

namespace Pineglow.Launcher.Services;

public sealed class MinecraftAppRegistrationRequiredException : InvalidOperationException
{
	public const string DiagnosticStage = "minecraft-app-registration";

	public MinecraftAppRegistrationRequiredException()
		: base("正版登录暂未向公众开放，请稍后重试。")
	{
	}
}
