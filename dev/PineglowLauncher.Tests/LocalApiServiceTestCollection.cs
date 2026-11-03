using Xunit;

namespace Pineglow.Launcher.Tests;

/// <summary>
/// R2-02 Step 4.3 全量测试隔离返工：所有直接调用 <see cref="Services.LocalApiService"/> Start/Stop/IsRunning
/// 或依赖其真实 listener 端口的测试类，必须属于此 collection 并严格串行。
///
/// 原因：<see cref="Services.LocalApiService"/> 是进程级静态单例；任何 <c>Stop()</c> 都会关闭
/// 当前所有正在发起 HTTP 请求的 listener。xUnit 默认按 test collection 并行调度时，
/// NewsFakeIpConfigTests 与 ContentIconServiceTests 并发会出现 "远程主机强迫关闭现有连接"
/// （HttpRequestException / SocketException），导致 AllTests 出现稳定失败。
///
/// 授权范围（review-fix-1 隔离返工）：
///   - <see cref="NewsFakeIpConfigTests"/>：通过 <c>OverrideConfigStoreForTest</c> 注入内存 store
///     并启动 LocalApiService 做真实 HTTP 调用 + token 校验
///   - <see cref="ContentIconServiceTests"/>：基线停止 + 真实启动 + 真实 HttpClient 调用
///     /api/content/icon（必须依赖真实 listener）
///
/// 其他测试类虽引用 127.0.0.1 或 HttpClient，但均使用 FakeHandler / TcpListener 测试桩，
/// 不与 LocalApiService 共享进程级资源，**不**纳入本 collection。
///
/// 不允许把整个测试程序集（assembly-level）关并行来掩盖资源归属；本 collection
/// 仅对共享 LocalApiService 的测试类做资源互斥。
/// </summary>
[CollectionDefinition("LocalApiService", DisableParallelization = true)]
public sealed class LocalApiServiceTestCollection
{
	// 故意为空：仅作为 collection 标识与并行禁用入口。
}
