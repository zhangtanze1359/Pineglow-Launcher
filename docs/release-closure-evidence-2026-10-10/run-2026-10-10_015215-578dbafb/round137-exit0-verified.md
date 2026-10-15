# 记录 137：**`Environment.Exit(0)` 成了** —— 退出码 0，日志也没丢；P-SINGLEINSTANCE 完全验证通过

运行标识：`run-2026-10-10_015215-578dbafb` ｜ 2026-10-13 01:32–01:52（+08:00）
性质：**产品目录零改动**（0 staged）。补丁在隔离副本验证。

---

## 实验结果：**两件事都对**

```
B exit code = 0                    ← ① 干净退出（此前一直是 0xE0434352）
日志行 '已有实例在运行' = 1          ← ② 日志没有被 Environment.Exit 丢掉
processes alive = 1                ← 只剩一个
A 仍在服务 = True                   ← A 不受影响
leftover = 0
```

**判定条件（日志行）与退出码（干净）同时满足。P-SINGLEINSTANCE 至此完全验证通过。**

## 我上一轮预判的风险**没有发生** —— 而这本身是个收获

上一轮我写：

> **风险**：`Environment.Exit` **不会 flush `StreamWriter` 缓冲**。如果 `LauncherLog` 不是 AutoFlush，
> **"已有实例在运行"那一行可能根本写不进文件**。

**实测：那一行在。** 所以 **`LauncherLog` 是每次写就落盘的**（AutoFlush 或等价行为）——
**这是一条此前不知道的产品事实，现在知道了。**

**我预判了一个风险、为它设计了第二个判定维度、然后它没发生** ——
**这不是白做**：如果我只盯着退出码，我就不会知道日志会不会丢；
**现在两个维度都有读数，这个"通过"是双重的。**

## 假设清单的最终状态

| # | 假设 | 结果 |
| --- | --- | --- |
| 1 | `Shutdown()` 早于生命周期初始化完 | **❌ 已排除**（第 126 轮：先 base 再 Shutdown，退出码不变） |
| 2 | `base()` 在没设 `MainWindow` 时抛错 | **未单独验证**（被 #3 的修法绕过了） |
| 3 | **绕开 Avalonia 关闭流程即可** | **✅ 成立**（`Environment.Exit(0)` → 退出码 0） |
| 4 | **`Environment.Exit` 会丢掉日志行** | **❌ 已排除**（那一行在） |

**两个假设被排除、一个成立、一个风险未发生。** 搜索空间从"不知道哪里抛的"
缩小到"**Avalonia 的关闭流程在这个时点会抛，绕开它就行**"。

## 补丁的最终形态

```csharp
_singleInstanceMutex = new Mutex(initiallyOwned: true, name: @"Local\PineglowLauncher.SingleInstance", createdNew: out bool createdNew);
if (!createdNew)
{
    LauncherLog.Info("已有实例在运行，本进程退出（单实例守卫）");
    Console.Error.WriteLine("Pineglow Launcher 已在运行。");
    // 绕开 Avalonia 的关闭流程（第 126 轮实测：先 base() 再 Shutdown() 无效）
    // 注意：Environment.Exit 不 flush StreamWriter —— 所以必须同时看那一行日志还在不在。
    Environment.Exit(0);
    return;
}
```

## 缺陷 12 至此的状态

| 部分 | 状态 |
| --- | --- |
| **日志** | **已修**（实测：失败有完整调用栈进日志） |
| **僵尸** | **已修**（实测：第二个实例干净退出、只剩一个进程、A 不受影响） |
| 根因 | **已查清**（WebView2 用户数据目录争用，判别实验支持） |
| 补丁 | **P-SINGLEINSTANCE，行为验证通过** |

## 证据等级

**A 17 / B 2 / C 1（P-STARTUPLOG，本环境不可复现）/ D 1。**

**20 条补丁里 19 条有运行时证据**，唯一没有的那条**在这台机器上验不了**。

## 当前状态

| 项 | 状态 |
| --- | --- |
| 已就绪的补丁 | 20 条（17 个脚本） |
| 证据等级 | **A 17 / B 2 / C 1 / D 1** |
| 合并验证 | 19 条那次；**20 条待重跑** |
| 唯一未做的功能 | 缺陷 13 ③ |
| 待你决定 | ① 提交；② C 的范围；③ 缺陷 12 是否采纳单实例守卫（**已完全验证**）；④ 是否做 13 ③ |

状态串：`TechnicalAcceptance=NOT_PASSED`；`FormalPhaseApproval=NOT_GRANTED`；`ReleaseApproval=NOT_GRANTED`。
