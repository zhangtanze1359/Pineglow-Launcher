using System;

namespace Pineglow.Launcher.Services;

public sealed class MinecraftAppRegistrationRequiredException : InvalidOperationException
{
	public const string DiagnosticStage = "minecraft-app-registration";

	/// <summary>
	/// v1.0.4：旧文案「正版登录暂未向公众开放，请稍后重试」会让用户以为是产品永久砍掉了正版登录。
	/// 实际情况是**我们提交的 Minecraft 应用仍在 Mojang 审核中**，所以改成说明原因 + 给出可用的替代路径。
	/// </summary>
	public const string UserFacingMessage =
		"正版登录正在等待 Mojang 审核（应用已提交，尚未通过）。审核通过前可以先用离线登录，或为本实例配置 authlib 登录源。";

	public MinecraftAppRegistrationRequiredException()
		: base(UserFacingMessage)
	{
	}
}
