# 第 28 轮：实现 README 最后一条未兑现的承诺（可恢复任务队列）——并顺手撞出一个**真实的产品缺陷**

运行标识：`run-2026-10-10_015215-578dbafb`（第 28 轮追加）
时间：2026-10-11 17:41–17:55，Asia/Hong_Kong（+08:00）
本轮性质：**产品目录零改动**（0 staged）。全部在 `%TEMP%` 隔离副本里完成。
补丁：`round28-durable-queue/` 下的 `apply-durable-queue.ps1`（7 步）——**未应用**。

---

## 1. 做了什么

README 第 10 行承诺「断点续传 + **可恢复任务队列**」。第 11 轮已查明：模型（`AppConfig.DownloadQueue` +
`VersionDownloadQueueItem` + `VersionDownloadQueueState`）**存在但零引用**，真正的队列是 `LocalApiService` 里一张
**进程生命周期内的 `List`**——重启即丢。

本轮把它接上：

| 步 | 改动 |
| --- | --- |
| 0–1 | `VersionDownloadQueueItem` 补上 `Id` / `Progress` / `StartedAt`（活队列本来就有这三样，模型缺） |
| 2 | 在活队列旁加持久化助手：**只在状态迁移时落盘**（进度每秒刷很多次，不能每次都写 config.json） |
| 3 | `Start()` 时把上次的队列读回来 |
| 4–6 | 在 **入队 / 开始下载 / 终态** 三处调用落盘 |

**设计选择（保守）**：重启时仍处于 `Waiting`/`Downloading` 的条目**不自动重下**——它们在进程死亡时就已中断，
这里如实标成 `failed` 并写明「上次运行被中断，可重新下载」，是否重新发起由用户决定。
（自动重下会在用户没要求的情况下打网络，且"可恢复"的本意是"看得见、能重发"。）

---

## 2. 第一版补丁**把启动器弄挂了**——而这是个真缺陷，不是我的笔误

第一版把 `RestoreDownloadQueue()` 直接放在 `Start()` 里（`Start()` 由 `App.OnFrameworkInitializationCompleted`
在 **Avalonia UI 线程**上调用）。结果：

| 构建 | API 起来了吗 | 日志 |
| --- | --- | --- |
| **未打补丁**的出厂 EXE（同一夹具） | **apiUp=True** | 2 行（启动 + 监听） |
| 第一版补丁 EXE | **apiUp=False** | **只有 1 行**（启动） |

进一步刻画：进程**活着**、CPU **平在 0.84 秒**（不是自旋）、线程 35 个、**stderr 全空**、日志再无输出
——这是**阻塞等待**，不是崩溃。60 秒后依旧。

### 2.1 根因

`LocalApiService.cs` 里的生产配置存储：

```csharp
private sealed class ProductionConfigStore : IConfigStore
{
    public AppConfig Load() => ConfigStore.LoadAsync().GetAwaiter().GetResult();
```

而 `ConfigStore.LoadAsync()` 是 `async Task<AppConfig>`。
**在带 `SynchronizationContext` 的线程上同步等待 async，就是经典的死锁**：continuation 被投回**那个正在阻塞等待的 UI 线程**，谁也动不了。

`LoadConfig()` / `SaveConfig()` 今天之所以没出事，只是因为**目前所有调用点都在 `HttpListener` 的线程池线程上**（那里没有 SyncContext）。
**我的补丁是第一个从 UI 线程调用它的人**——于是立刻死锁。

### 2.2 这是一个**真实的产品缺陷**（本轮新增第 11 条）

| 维度 | 判断 |
| --- | --- |
| 现状 | **潜伏**：当前没有 UI 线程调用者，所以线上没暴露 |
| 性质 | **地雷**：任何人在 UI 线程（菜单回调、启动初始化、对话框处理器）里调一次 `LoadConfig()`/`SaveConfig()`，**应用直接挂死**——没有异常、没有日志、CPU 空闲，极难定位 |
| 已复现 | **是**（本轮，A/B 隔离：未打补丁 apiUp=True / 打补丁 apiUp=False） |
| 顺带影响 | 它**挡住了"启动时恢复状态"这类正当功能**——本轮就是被它挡住的 |
| 修复方向 | 在 `ConfigStore.LoadAsync()/SaveAsync()` 的每个 `await` 上加 `.ConfigureAwait(false)`（让 continuation 不需要 UI 线程），或把 `IConfigStore` 改成异步接口。前者是一行到几行的改动，且能一次性消除所有同类风险 |

### 2.3 我的补丁怎么绕过它

把恢复放到后台线程，**不阻塞 UI 线程**：

```csharp
// FIX-DURABLEQUEUE: restore off the UI thread — LoadConfig() is sync-over-async internally.
_ = Task.Run(RestoreDownloadQueue);
```

改完重跑：`build 0 警告 0 错误`、**941/0 全量测试**、发布 `exit=0` → `100881B71B3007ADA868B587E457927A21577EB698EBB5B11E6347EC5AD70EC6`。

---

## 3. 现场验证：硬杀 → 重启 → 任务还在

用打了补丁的 EXE 起一个夹具，发起 1.21.11 下载，等 40 秒，然后**硬杀**（不做优雅退出），再重启：

| 阶段 | 读数 |
| --- | --- |
| 下发下载 | `{"ok":true,"id":"dlv1791712343852","versionId":"1.21.11","status":"queued"}` |
| 杀之前队列 | `status=downloading, progress=0.05, message=下载：1.21.11.jar` |
| **杀之前 config.json 里已落盘** | **1 条**，`state=1`（Downloading），`msg=正在获取版本元数据` |
| 硬杀 | 进程确认消失 |
| **重启后 `GET /api/downloads`** | **任务还在**，`id`/`versionId`/`startedAt` 原样保留，**`status=failed`**，**`message=上次运行被中断，可重新下载`**，`progress` 归 0 |
| 产品日志 | `[Downloads] 已恢复 1 条下载队列记录` |
| 残留进程 | **0** |

对照第 11 轮测得的**修复前**行为：同样硬杀再重启，`GET /api/downloads` 返回 **`{"tasks":[]}`**。

**即：README 的「可恢复任务队列」现在真的成立了**（作为待应用的补丁）——任务跨进程存活、重启后可见、如实标注中断、用户可重新发起。

---

## 4. 本轮的两条产出

1. **新缺陷第 11 条（已复现）**：`ProductionConfigStore.Load()/Save()` 同步等待 async → **在 UI 线程调用会死锁**。
   严重度**中**：今天潜伏，但是地雷，而且挡住了正当功能。修复方向：`ConfigureAwait(false)`。
2. **P-DURABLEQUEUE 补丁**：兑现 README 最后一条未实现的承诺，已有现场硬杀/重启证据。

（另：第一版补丁的失败**不是靠"编译+测试通过"能发现的**——941 个用例全绿、build 0 警告。
是靠"把它真的跑起来"才发现的。这条经验值得记住。）

状态串：`TechnicalAcceptance=NOT_PASSED`（发布整体）；`FormalPhaseApproval=NOT_GRANTED`；`ReleaseApproval=NOT_GRANTED`。
