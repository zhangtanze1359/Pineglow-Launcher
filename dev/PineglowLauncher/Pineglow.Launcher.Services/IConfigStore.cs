using System;
using Pineglow.Launcher.Models;

namespace Pineglow.Launcher.Services;

/// <summary>
/// 配置读写的最小契约（架构评审候选 ③ 第 2 步）。
///
/// <para>
/// 搬出 <c>LocalApiService</c> 的嵌套类型，是为了让 <see cref="LocalApiHost"/> 的构造参数
/// 不再依赖宿主类本身——否则"把依赖作为构造参数传进去"会变成"宿主类型引用自己的嵌套接口"，
/// 谁也没真正解耦。
/// </para>
/// <para>
/// 实现只有两个：生产用 <see cref="ProductionConfigStore"/>；测试用自己的内存 store。
/// 这个接口刻意只有 <see cref="Load"/> / <see cref="Save"/> 两个同步方法：
/// 任何更多的方法都会让"测试实现要复刻多少生产语义"变成一个说不清的问题
/// （参见 <c>InstanceDefaultInstanceBoundaryTests</c> 里那条"夹具存储 vs 生产语义存储"的对照）。
/// </para>
/// </summary>
internal interface IConfigStore
{
	AppConfig Load();

	void Save(AppConfig config);
}

/// <summary>
/// 生产配置存储：委托给 <see cref="Services.ConfigStore"/>（真实 config.json + SecretStore）。
///
/// <para>
/// 不要把 <c>ConfigStore</c> 的语义（.bak/.prev 轮转、SecretStore 回填、旧配置迁移）
/// 在这里复刻一遍——那会让"生产到底走哪条路径"出现两个答案。
/// </para>
/// </summary>
internal sealed class ProductionConfigStore : IConfigStore
{
	public AppConfig Load() => ConfigStore.LoadAsync().GetAwaiter().GetResult();

	public void Save(AppConfig config) => ConfigStore.SaveAsync(config).GetAwaiter().GetResult();
}
