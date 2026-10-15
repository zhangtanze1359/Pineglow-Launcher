# 记录 57：缺陷 10 的补丁（**第 13 条补丁**）—— 给启动失败一条可读的路径

运行标识：`run-2026-10-10_015215-578dbafb` ｜ 2026-10-11 21:36–21:50（+08:00）
性质：**产品目录零改动**（0 staged）。补丁在隔离副本验证。
补丁：`apply-startup-log.ps1`（1 步）——**未应用**。

---

## 背景

缺陷 10 是**唯一一条"未复现"**的缺陷（代码级推断），第 44 轮复核后**逐条成立**：

- 全树 **0 处**全局异常处理；
- `App.cs:44` 的 `new WebShellWindow()` 无 try/catch，**而同段 `:31` 的 `LocalApiService.Start()` 有**
  （作者在同一屏里想到了失败，却漏了紧邻的一处）。

后果：窗口壳构造失败（最常见原因正是 **WebView2 运行时缺失/损坏**）时，
**进程直接消失、日志里什么都没有**。

## 改动（1 处）

在 `App.cs` 里补两件事：

1. **两条全局兜底**：`AppDomain.CurrentDomain.UnhandledException` 与
   `System.Threading.Tasks.TaskScheduler.UnobservedTaskException` → 都写进 `LauncherLog`。
   **让"以后任何一次崩溃"至少留下日志。**
2. **把窗口壳构造包进 try/catch**：失败时写一条**带解释**的日志
   （点名"最常见原因是缺少或损坏的 WebView2 运行时"），然后**重新抛出**——
   **保持"启动失败就退出"的语义不变**，只是把它从"静默消失"变成"有日志的失败"。

**这个补丁刻意不去"让它继续运行"**：窗口都建不出来时，假装还能用只会更糟。

## 验证

| 门槛 | 结果 |
| --- | --- |
| 应用 1 步 | 命中 |
| `dotnet build -c Release` | **0 警告 0 错误** |
| C# 全量 | **941 通过 / 0 失败** |
| 发布单文件 | `exit=0` |
| **独立冷启动** | **判定 PASS**（19 项，窗口标题 `松光启动器`）——**正常启动路径未受影响** |
| 产物 | `7331037DFCB2E848A4C08B124043CCC42FC8058B4358B18682332FBCCC457AAB` |

一次自查失败：首版用了 `TaskScheduler` 而 `App.cs` 没有该命名空间 → `error CS0103`；
改成全限定名 `System.Threading.Tasks.TaskScheduler` 后通过。**构建拦住的。**

## 意义

| 项 | 变化 |
| --- | --- |
| 已就绪的补丁 | **13 条**（原 12 条 + P-STARTUPLOG） |
| 无补丁的缺陷 | **从 3 条降到 2 条**（只剩第 1 条加载器安装入口、第 4 条本地导入进度界面） |

状态串：`TechnicalAcceptance=NOT_PASSED`；`FormalPhaseApproval=NOT_GRANTED`；`ReleaseApproval=NOT_GRANTED`。
