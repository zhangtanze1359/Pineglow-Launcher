using System;
using Pineglow.Launcher.Models;

namespace Pineglow.Launcher.Services;

/// <summary>
/// 少数路由对"被拒"时的措辞更具体（例如「先退出游戏再删除存档」）。
/// 措辞是**值对象参数**——判定仍然只有一处：<see cref="RequestScope.Write"/>。
/// </summary>
internal sealed class ScopeWording
{
	public Func<int, string>? Busy { get; init; }

	public string? Bisect { get; init; }
}

/// <summary>
/// 一次 API 请求已经解析好的上下文：配置、实例、游戏目录，以及「这次写操作被允许吗」。
///
/// 为什么要有它（架构评审 ②，2026-11-17）：
///   修复前每个写 handler 都自己抄一遍开场白——<c>LoadConfig()</c>、<c>RequireInstance</c>、
///   <c>ResolveGameDirectory</c>，再抄两条策略判断（同一实例是否在运行、是否处于 Mod 二分定位）。
///   「并发启动守卫」这段 8 行（含 5 行注释）在 5 个文件里出现 14 次；「二分定位中禁改 Mod」
///   另有 4 个调用点。抄 14 次的东西不是策略，是 14 份平行副本：漏抄一次就少一道守卫
///   （真实发生过——一个只读列表被误加守卫，只能用手写注释豁免）。
///
/// 策略属于「请求的构造」，不属于「请求的处理」。所以它搬进了 <see cref="Write"/>：
/// 忘了它，表现是「编辑器里少一行 BeginWriteScope」，而不是「14 处里有一处对不上」。
/// </summary>
internal sealed class RequestScope
{
	private RequestScope(AppConfig config, GameInstance? instance, string instanceId, string gameDirectory)
	{
		Config = config;
		Instance = instance;
		InstanceId = instanceId;
		GameDirectory = gameDirectory;
	}

	public AppConfig Config { get; }

	/// <summary>只读请求不指定实例时为 null；写请求永远非 null。</summary>
	public GameInstance? Instance { get; }

	public string InstanceId { get; }

	public string GameDirectory { get; }

	/// <summary>只读：解析配置与（可选的）实例，不执行任何写策略。</summary>
	internal static RequestScope Read(AppConfig config, GameInstance? instance, string instanceId, string gameDirectory)
	{
		return new RequestScope(config, instance, instanceId, gameDirectory);
	}

	/// <summary>
	/// 写：构造时就执行两条策略，违反即抛出（沿用 <see cref="InvalidOperationException"/> → HTTP 400 的既有映射）。
	/// </summary>
	/// <param name="requireNoBisect">
	/// 是否叠加「二分定位中禁写」。**只有 Mod 写入与实例移除需要它**：存档写入与二分自身
	/// 的推进不受这条策略管辖（否则二分进行中就推不动自己了）。
	/// 这也是一条被守住的口径——策略不是"到处都挂"，而是"按域挂"。
	/// </param>
	internal static RequestScope Write(AppConfig config, GameInstance instance, string gameDirectory, ScopeWording? wording = null, bool requireNoBisect = false)
	{
		if (requireNoBisect)
		{
			ModBisectSession? session = ModBisectService.Get(instance.Id);
			if (session != null && !session.Finished)
			{
				throw new InvalidOperationException(
					wording?.Bisect ?? "该实例正在进行 Mod 二分定位，请先结束或放弃定位，再手动修改 Mod。");
			}
		}

		if (RunningInstanceRegistry.IsRunning(instance.Id, out int runningPid))
		{
			throw new InvalidOperationException(
				wording?.Busy?.Invoke(runningPid) ?? $"该实例已在运行（进程 {runningPid}），请勿重复启动。");
		}

		return new RequestScope(config, instance, instance.Id, gameDirectory);
	}
}
