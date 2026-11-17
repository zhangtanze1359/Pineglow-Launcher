using System;
using Pineglow.Launcher.Models;

namespace Pineglow.Launcher.Services;

/// <summary>
/// 一个本地 API 宿主运行所需的**全部外部依赖**，作为一个不可分割的值
/// （架构评审候选 ③ 第 2 步）。
///
/// <para>
/// 改造前这些依赖是 <c>LocalApiService</c> 上的 4 个静态 override 字段
/// （config store / recovery 根 / bisect 根 / diagnostic 根），各自独立可换。
/// 独立可换听起来灵活，实际代价是**漏一个就往真实 %APPDATA% 里写**——
/// 第 47 轮实测就有一个夹具只重定向了配置存储，诊断报告一直写到真实目录。
/// </para>
/// <para>
/// 现在它们是一个值：要么整份给，要么整份不给。默认值只有一份
/// （<see cref="Production"/>），测试也只需描述"我要换哪一块"，
/// 不再需要记得还有哪几块存在。
/// </para>
/// </summary>
internal sealed class HostDependencies
{
	public HostDependencies(IConfigStore store, StorageRoots roots, OutboundHttp http)
	{
		Store = store ?? throw new ArgumentNullException(nameof(store));
		Roots = roots ?? throw new ArgumentNullException(nameof(roots));
		Http = http ?? throw new ArgumentNullException(nameof(http));
	}

	public IConfigStore Store { get; }

	public StorageRoots Roots { get; }

	public OutboundHttp Http { get; }

	/// <summary>生产默认：真实 config.json + 配置目录下三个存储根 + 一组出站客户端。</summary>
	public static HostDependencies Production() => new(
		new ProductionConfigStore(),
		StorageRoots.Production(),
		new OutboundHttp());

	/// <summary>换配置存储，其余保持不变（测试用）。</summary>
	public HostDependencies WithStore(IConfigStore store) => new(store, Roots, Http);

	/// <summary>换三个存储根，其余保持不变（测试用）。</summary>
	public HostDependencies WithRoots(StorageRoots roots) => new(Store, roots, Http);
}
