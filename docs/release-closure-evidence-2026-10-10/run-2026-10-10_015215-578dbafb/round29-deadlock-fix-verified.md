# 第 29 轮：拆掉那颗地雷 —— UI 线程「同步等 async」死锁已修好并验证

运行标识：`run-2026-10-10_015215-578dbafb`（第 29 轮追加）
时间：2026-10-11 17:57–18:07，Asia/Hong_Kong（+08:00）
本轮性质：**产品目录零改动**（0 staged）。全部在 `%TEMP%` 隔离副本里完成。
补丁：`round29-deadlock-fix/` + `apply-deadlock-fix.ps1`（**3 行**）——**未应用**。

---

## 1. 先做了一次全局审计：这类危险写法不止一处

检索整个产品里的同步等 async 写法，**生产代码里 6 处**（另有 VerifyHost 控制台程序若干处，不影响 GUI）：

| 位置 | 写法 | 今天从哪调用 |
| --- | --- | --- |
| `LocalApiService.cs:1053` | `ConfigStore.LoadAsync().GetAwaiter().GetResult()` | API 处理器（线程池） |
| `LocalApiService.cs:1054` | `ConfigStore.SaveAsync().GetAwaiter().GetResult()` | API 处理器（线程池） |
| `LocalApiService.cs:1128 / 1180` | `BuildInstanceSummary(...).GetAwaiter().GetResult()` | API 处理器 |
| `LocalApiService.cs:1173 / 2344 / 2357` | `JavaLocator.ScanAsync/FindAsync(...).GetAwaiter().GetResult()` | API 处理器 |
| `LocalApiService.cs:4261 / 4276 / 4280` | `WriteJsonAsync(...).GetAwaiter().GetResult()` | API 处理器 |
| `LocalApiService.cs:4793` | `applyResult.Result`（直接取 `Task.Result`） | API 处理器 |

**它们今天都没出事，只因为调用点恰好全在 `HttpListener` 的线程池线程上（那里没有 `SynchronizationContext`）。**
一旦有人在 UI 线程上碰其中任何一个，就会和第 28 轮一样**挂死**。

---

## 2. 修的是根：3 行

`ProductionConfigStore.Load()/Save()` 之所以会死锁，唯一原因是它们等的那条 async 链**捕获了上下文**。
而这条链上真正的 await 只有三处：

| 位置 | 原文 | 问题 |
| --- | --- | --- |
| `ConfigStore.cs:82`（`LoadAsync`） | `await File.ReadAllTextAsync(configPath);` | **真正的原因**——continuation 会被投回调用者的 SyncContext |
| `ConfigStore.cs:199`（`LoadAsync` 的迁移分支） | `await SaveAsync(config);` | 同一条链 |
| `ConfigStore.cs:234`（`SaveAsync`） | `await Task.CompletedTask;` | 占位 await（`SaveCore` 本身是纯同步的） |

三处都加 `.ConfigureAwait(false)`：

```csharp
string json = await File.ReadAllTextAsync(configPath).ConfigureAwait(false);
```

**这一改，`LoadConfig()`/`SaveConfig()` 从任何线程调用都安全**——continuation 不再需要 UI 线程。

---

## 3. 验证：用「原来会挂死的那个动作」直接证明

最干净的验证方式不是写测试，而是**把第 28 轮那个死锁场景原样重放**：
让 durable-queue 的恢复**直接在 UI 线程上跑**（`RestoreDownloadQueue();`，去掉第 28 轮被迫加的 `Task.Run` 绕行）。

| 步骤 | 结果 |
| --- | --- |
| 应用死锁修复（3 行）+ durable-queue（**UI 线程恢复**） | 3 + 7 步全部命中 |
| `dotnet build -c Release` | **exit 0，0 警告 0 错误** |
| 全量测试 | **941 通过 / 0 失败** |
| 发布单文件 | `exit=0` → `30A6470F772A126716EE19E0967CA56333DE6EB7C65E2B27FBE7AD3E9663A1FC` |
| **A. 应用能否启动**（恢复在 UI 线程上） | **apiUp = True** ← **修复前这里挂死**（apiUp=False、CPU 平在 0.84s、无异常无日志） |
| B. 发起下载 | 接受；杀之前 `downloading`，进度 0.43，`正在下载游戏资源：250/4591` |
| | config.json 里已落盘 **1 条** |
| C. **硬杀** | — |
| **D. 重启后 `GET /api/downloads`** | **任务仍在**，`id`/`versionId`/`startedAt` 原样，**`status=failed`**，**`message=上次运行被中断，可重新下载`** |
| 产品日志 | `[08] 启动` → **`[Downloads] 已恢复 1 条下载队列记录`** → `[LocalApi] 监听 …`（顺序证明它确实在 UI 线程上同步跑完并返回了） |
| 残留进程 | **0** |

**结论**：地雷已拆；而且第 28 轮那个被迫的 `Task.Run` 绕行**不再需要**。

---

## 4. 还没修的兄弟（如实标注）

第 1 节表里另外 5 处（`WriteJsonAsync` / `BuildInstanceSummary` / `JavaLocator.ScanAsync/FindAsync` / `ApplyResult.Result`）
**共享同一个危险模式，本轮没有改**：

- 它们**今天仍然潜伏**（调用点全在线程池线程上）；
- 要让它们也彻底安全，需要给它们等的那条 async 链上的 await 也加 `.ConfigureAwait(false)`（链更长，涉及 `ModService.GetModsAsync`、`JavaLocator.ValidateInstallationAsync` 等）；
- 或者用更省事的桥接写法：`Task.Run(() => XxxAsync()).GetAwaiter().GetResult()`。

**建议**：本次先合并这 3 行（它修的是**已经复现过**的那条）；其余 5 处作为同一类风险的后续项，要么按同样方式逐个处理，要么在团队内约定"UI 线程禁止调用 `LocalApiService` 的同步包装"。

---

## 5. 本轮的一次工具失误

我给 durable-queue 脚本加 `-RestoreOnUiThread` 开关时，把 **PowerShell 代码写进了单引号 here-string**，结果那段 `if ($RestoreOnUiThread) {...} else {...}` 被**原样写进了 C# 文件**，编译报 8 个语法错误。
**这次是构建拦住的**（不像第 28 轮那样要跑起来才发现）。改成在 PowerShell 里先算出要插入的 C# 文本再传给 `Patch`，问题消失。

---

## 6. 累计状态

| 项 | 状态 |
| --- | --- |
| 已复现的缺陷 | 第 11 条（UI 线程死锁地雷）**修复补丁已验证** |
| 已就绪的补丁 | **7 条**：P-UA、P-FABRICMETA、P-PROXYJVM、P-FAKEIP、P-IMPORTLOG、P-DURABLEQUEUE、**P-DEADLOCK** |
| C（发布依赖锁定） | 补丁已就绪，待你决定范围 |
| 仍未修 | 缺陷 1、4、8、10 + 第 4 节那 5 处同类风险 |

状态串：`TechnicalAcceptance=NOT_PASSED`（发布整体）；`FormalPhaseApproval=NOT_GRANTED`；`ReleaseApproval=NOT_GRANTED`。
