# 第 35 轮：把「同步等 async」这一类**一次性收干净**（顺带更正我自己的一处误判）

运行标识：`run-2026-10-10_015215-578dbafb`（第 35 轮追加）
时间：2026-10-11 18:49–18:55，Asia/Hong_Kong（+08:00）
本轮性质：**产品目录零改动**（0 staged）。全部在 `%TEMP%` 隔离副本里完成。
补丁：`apply-runsync.ps1`（10 步）——**未应用**。

---

## 1. 先更正我自己的一处误判

第 29 轮我做全局审计时列了「生产代码里 6 处同步等 async」，其中一条是
`LocalApiService.cs:4793` 的 `applyResult.Result`。本轮逐个核对后确认：

```csharp
lastApplyResult = applyResult == null ? null : new
{
    result = applyResult.Result,     // applyResult 是"上次应用更新的结果"模型，不是 Task
    applied = applyResult.Applied,
    version = applyResult.Version,
    writtenAtUtc = applyResult.WrittenAtUtc
}
```

**`applyResult` 不是 `Task`，`.Result` 是模型的属性**——这一条**不是** sync-over-async。
我的审计凭 `.Result` 这个词就把它算进去了。实际需要处理的是 **10 处**，不是 11 处。

---

## 2. 一次收干净：一个机制，而不是五处各修各的

第 29 轮给 `ConfigStore` 加了 3 行 `ConfigureAwait(false)`，那修的是**已经复现过**的那条。
剩下 10 处的 async 链各不相同（`BuildInstanceSummary` → `ModService.GetModsAsync`、
`JavaLocator.ScanAsync/FindAsync`、`WriteJsonAsync`…）。逐个追 `ConfigureAwait` 是打地鼠，
而且**以后新增的链又会重新引入风险**。

所以改成**一个机制**：

```csharp
/// 同步等待一个 async 操作完成，且不捕获调用者的 SynchronizationContext。
/// Task.Run 保证委托在没有 SynchronizationContext 的线程池线程上开始执行，
/// 因此链上任何 await 都不会捕获上下文，从任何线程调用都安全。
internal static T RunSync<T>(Func<Task<T>> operation) => Task.Run(operation).GetAwaiter().GetResult();
internal static void RunSync(Func<Task> operation) => Task.Run(operation).GetAwaiter().GetResult();
```

然后把 10 个调用点全部改道：

| 调用点 | 处数 |
| --- | --- |
| `ConfigStore.LoadAsync()` / `SaveAsync()` | 各 1 |
| `BuildInstanceSummary(...)` | 2 |
| `JavaLocator.ScanAsync(null)` | 2 |
| `JavaLocator.FindAsync()` | 1 |
| `WriteJsonAsync(...)`（400 / 200 / 500 三条分支） | 3 |

**为什么这个机制是对的**：死锁的**必要条件**是"async 链捕获了调用者的上下文"。
`Task.Run` 让委托在**没有** SyncContext 的线程池线程上开始执行——这个必要条件被**结构性消除**，
而不是靠逐个 await 记得写 `ConfigureAwait(false)`。

---

## 3. 验证

| 项 | 结果 |
| --- | --- |
| 应用 10 步 | 全部命中（每步都断言"恰好匹配 N 次"） |
| `dotnet build -c Release` | **exit 0，0 警告 0 错误** |
| **全量测试** | **942 通过 / 0 失败**（941 原有 + 1 个新的 RunSync 回归测试） |
| 服务层残留的 `.GetAwaiter().GetResult()` | **只剩 3 处**：1 处在文档注释里、**2 处就是 `RunSync` 自己**（那里 `Task.Run` 已保证安全） |

新增的回归测试（`RunSyncTests.cs`）复刻第 30 轮那个「阻塞且不处理自己消息队列」的上下文：
在工作线程上装一个永不泵送的 `SynchronizationContext`，然后调用
`RunSync(() => YieldsOnceAsync())`（内部 `await Task.Yield()`），断言：

1. 30 秒内返回（不死锁）；
2. 结果正确（42）；
3. **`Post` 次数为 0**（没有把 continuation 投回调用者上下文）。

### 3.1 一处如实标注

`VerifyHost/BisectSmoke.cs` 里还有 **8 处** `.GetAwaiter().GetResult()`，本轮**没有改**：
它是**控制台测试宿主**，不是 GUI——它的线程没有 `SynchronizationContext`，不属于这条风险类。
（报告里已注明。）

---

## 4. 累计状态

| 项 | 状态 |
| --- | --- |
| 已就绪的补丁 | **10 条**：P-UA、P-FABRICMETA、P-PROXYJVM、P-FAKEIP、P-IMPORTLOG、P-DURABLEQUEUE、P-DEADLOCK、P-LOGSHARE、**P-RUNSYNC**、C（+ 3 个回归测试文件） |
| 「同步等 async」这一类 | **服务层已收干净**（唯一残留是 `RunSync` 自身，安全）+ VerifyHost（非 GUI，已注明） |
| 仍未修 | 缺陷 1、4、8、10；缺陷 12 的"第二个窗口/僵尸窗口"部分 |
| 待你决定 | C 的范围；提交 163 个未跟踪 + 41 个已修改文件；修哪几条缺陷 |

状态串：`TechnicalAcceptance=NOT_PASSED`（发布整体）；`FormalPhaseApproval=NOT_GRANTED`；`ReleaseApproval=NOT_GRANTED`。
