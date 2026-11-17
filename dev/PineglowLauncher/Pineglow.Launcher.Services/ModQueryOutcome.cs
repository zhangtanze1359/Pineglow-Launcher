using Pineglow.Launcher.Models;

namespace Pineglow.Launcher.Services;

/// <summary>
/// 单个 Mod「查最新版本」这件事的**真实结果**。
///
/// 为什么需要它（架构评审 ⑥，2026-11-17）：
///   修复前查询层只返回 <c>ModVersion?</c>，于是 <c>null</c> 一个人演三种情况——
///   没在线元数据 / 查了但上游没有兼容版本 / 查询抛异常（连不上、限流、Key 失效）。
///   判定层（<see cref="ModUpdatePlanner"/>）再把它统一记成「无元数据」，
///   用户看到「跳过 37 个：无元数据」——两个模块共同产出的半个真相，
///   而真正该做的是"这 37 个里 12 个其实查失败，稍后重试就行"。
/// </summary>
public enum ModQueryStatus
{
	/// <summary>查到了可用版本。</summary>
	Found,

	/// <summary>本地没有在线元数据（没有 projectId / source），从未发出请求——重试也不会变。</summary>
	NoMetadata,

	/// <summary>上游没有与当前 MC 版本 / 加载器兼容的版本——重试也不会变（除非换版本）。</summary>
	NoCompatibleVersion,

	/// <summary>查询失败（网络 / 鉴权 / 上游 5xx）。**重试可能变**，与上面两种本质不同。</summary>
	QueryFailed,
}

/// <summary>一条查询结果：状态 + 版本（仅 <see cref="ModQueryStatus.Found"/> 时非空）+ 可选细节。</summary>
public sealed record ModQueryOutcome(ModQueryStatus Status, ModVersion? Version = null, string? Detail = null)
{
	public static ModQueryOutcome Found(ModVersion version) => new ModQueryOutcome(ModQueryStatus.Found, version);

	public static ModQueryOutcome NoMetadata(string? detail = null) => new ModQueryOutcome(ModQueryStatus.NoMetadata, null, detail);

	public static ModQueryOutcome NoCompatible(string? detail = null) => new ModQueryOutcome(ModQueryStatus.NoCompatibleVersion, null, detail);

	public static ModQueryOutcome Failed(string detail) => new ModQueryOutcome(ModQueryStatus.QueryFailed, null, detail);
}
