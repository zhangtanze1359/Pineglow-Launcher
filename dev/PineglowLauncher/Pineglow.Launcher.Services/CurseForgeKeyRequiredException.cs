using System;

namespace Pineglow.Launcher.Services;

/// <summary>
/// v1.1.0 · S9：CurseForge 需要用户自己的 API Key，而程序里不再内置任何 Key。
///
/// 单独成一种异常类型的原因：它是**可预期的用户配置缺失**，不是服务端故障。
/// 如果按普通异常走，HTTP 层会给 500（"功能坏了"）；这里要的是 400 + 机器可读的 code，
/// 让界面能直接引导"去设置页填 Key / 先用 Modrinth"。
/// </summary>
public sealed class CurseForgeKeyRequiredException : InvalidOperationException
{
	/// <summary>给前端用的稳定错误码（不要改字面量，前端按它分支）。</summary>
	public const string ErrorCode = "curseforge_key_required";

	public CurseForgeKeyRequiredException(string message) : base(message)
	{
	}
}
