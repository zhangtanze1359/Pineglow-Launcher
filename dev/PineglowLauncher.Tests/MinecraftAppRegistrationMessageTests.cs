using Pineglow.Launcher.Services;
using Xunit;

namespace Pineglow.Launcher.Tests;

/// <summary>
/// v1.0.4 · 登录未过审提示改写（S3）的直测。
///
/// 这条文案是用户唯一能看到的解释，所以两件事必须同时成立：
///   1. 说明**为什么**不能用（等 Mojang 审核），而不是「暂未向公众开放」这种像永久关闭的说法；
///   2. 给出**现在能走的路**（离线登录 / authlib 登录源）。
/// </summary>
public sealed class MinecraftAppRegistrationMessageTests
{
	[Fact]
	public void Message_ExplainsMojangReview_WithoutOldMisleadingWording()
	{
		string message = new MinecraftAppRegistrationRequiredException().Message;

		Assert.Contains("Mojang 审核", message);
		Assert.DoesNotContain("暂未向公众开放", message);
		Assert.DoesNotContain("请稍后重试", message);
	}

	[Fact]
	public void Message_OffersUsableAlternatives()
	{
		string message = new MinecraftAppRegistrationRequiredException().Message;

		Assert.Contains("离线登录", message);
		Assert.Contains("authlib", message);
	}

	[Fact]
	public void Message_IsSharedConstant_SoUiAndTestsCannotDrift()
	{
		string fromConstant = MinecraftAppRegistrationRequiredException.UserFacingMessage;
		string fromException = new MinecraftAppRegistrationRequiredException().Message;

		Assert.Equal(fromConstant, fromException);
		Assert.False(string.IsNullOrWhiteSpace(fromConstant));
	}
}
