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
/// <see cref="Load"/> / <see cref="Save"/> 之外只保留 <see cref="SaveConfigOnly"/>：
/// 它是"只写 config.json、不碰凭据存储"的窄通道，默认回退到 <see cref="Save"/>，
/// 因此测试实现仍不需要复刻生产凭据语义；生产实现覆盖它，供旧配置迁移这类读路径写回使用。
/// </para>
/// </summary>
internal interface IConfigStore
{
	AppConfig Load();

	void Save(AppConfig config);

	/// <summary>
	/// 只写 config.json，**不触碰凭据存储**。旧配置迁移等"读路径顺带写回"必须走这里：
	/// 凭据读取失败时内存中的密钥可能为空，全量 <see cref="Save"/> 会把空值写回 SecretStore
	/// （空值 = 删除），误删与本次迁移无关的凭据。默认回退到 <see cref="Save"/>，测试的内存 store 无需改动。
	/// </summary>
	void SaveConfigOnly(AppConfig config) => Save(config);
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

	public void SaveConfigOnly(AppConfig config) => ConfigStore.SaveConfigOnlyAsync(config).GetAwaiter().GetResult();
}
