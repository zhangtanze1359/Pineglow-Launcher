# 记录 136：我的第一个修法**没起作用** —— 退出码还是崩溃码（两个假设都错了）

运行标识：`run-2026-10-10_015215-578dbafb` ｜ 2026-10-13 01:12–01:32（+08:00）
性质：**产品目录零改动**（0 staged）。补丁在隔离副本验证。

---

## 上一轮的假设与这一轮的尝试

上一轮我判定：B 退出码是 `0xE0434352`（未处理异常），**最可能因为我在
`OnFrameworkInitializationCompleted()` 还没返回时就调了 `Shutdown()`**。

**修法**：在 `Shutdown()` 之前先 `base.OnFrameworkInitializationCompleted();`，让框架走完初始化。

**改动已落到补丁脚本里**（3 步仍全部应用成功、publish exit 0）。

## 结果：**退出码一点没变**

```
A apiUp=True
B alive=False
B exit code = -532462766        ← 仍然 0xE0434352（期望 0）
```

**功能面仍然是对的**：

```
[INFO] === Pineglow Launcher 启动 v1.0.0 ===
[ERROR] [LocalApi] 启动失败: Failed to listen on prefix 'http://127.0.0.1:50082/'
   at … LocalApiService.Start(Nullable`1 portOverride)
   at … Pineglow.Launcher.App.OnFrameworkInitializationCompleted()
[INFO] 已有实例在运行，本进程退出（单实例守卫）      ← 判定条件命中
'已有实例在运行' = 1
processes alive = 1
```

**所以"先 base 再 Shutdown"这个修法无效。我的假设错了。**

## 现在的假设清单（把错的记下来，免得重走）

| # | 假设 | 结果 |
| --- | --- | --- |
| 1 | `Shutdown()` 早于生命周期初始化完 | **❌ 已排除** —— 先 `base()` 再 `Shutdown()`，退出码不变 |
| 2 | （隐含）`base()` 在没设 `MainWindow` 时安全 | **未验证** —— 它可能就是抛异常的那一步 |
| 3 | `Shutdown()` 本身在"生命周期还没开始跑"时抛错 | **未验证** |

**注意假设 2**：Avalonia 的 `base.OnFrameworkInitializationCompleted()` 在 classic desktop 场景下
**是否要求 `MainWindow` 已设**，我不知道 —— 而**我恰恰在没设 MainWindow 的分支里调了它**。

## 下一个实验（很明确）

**把 `base()` + `Shutdown()` 两行一起换成 `Environment.Exit(0);`** —— 完全绕开 Avalonia 的关闭流程。

**但有一个必须先想清楚的风险**：`Environment.Exit` **不会 flush `StreamWriter` 的缓冲**。
如果 `LauncherLog` 不是 AutoFlush，**"已有实例在运行"那一行可能根本写不进文件** ——
**那样判定条件就会失败，而失败的原因是我的修法，不是产品。**

**所以这个实验要同时看两件事**：
1. 退出码是不是 0；
2. **那一行日志还在不在**。

**如果第 2 条丢了**，正确做法是"先显式 flush 日志，再 Exit" —— 而 `LauncherLog` 有没有暴露 flush/shutdown，
**我还不知道**（要读 `LauncherLog.cs`）。

## 这一轮的形态：**"试了、没成、把没成的记下来"**

前几轮我几次把"试了没成"写成结论（比如第 113 轮 P-APILOG 那次）。
**本轮做的是**：改、跑、**读数没变**、**把假设标为"已排除"**、**写清下一个实验和它的风险**。

**"修法无效"本身是信息** —— 它排除了一个假设，并把搜索空间缩小到 Avalonia 的关闭流程上。

## 当前状态

| 项 | 状态 |
| --- | --- |
| 已就绪的补丁 | 20 条（17 个脚本） |
| 证据等级 | A 16 / B 2 / C 1（P-STARTUPLOG，不可复现）/ D 1 |
| **P-SINGLEINSTANCE** | **功能验证通过**（僵尸消除、判定条件命中）；**退出码仍不干净**，假设 1 已排除 |
| 合并验证 | 19 条那次；**20 条待重跑** |
| 唯一未做的功能 | 缺陷 13 ③ |
| 待你决定 | ① 提交；② C 的范围；③ 缺陷 12 是否采纳单实例守卫（功能已通过，退出码待修）；④ 是否做 13 ③ |

状态串：`TechnicalAcceptance=NOT_PASSED`；`FormalPhaseApproval=NOT_GRANTED`；`ReleaseApproval=NOT_GRANTED`。
