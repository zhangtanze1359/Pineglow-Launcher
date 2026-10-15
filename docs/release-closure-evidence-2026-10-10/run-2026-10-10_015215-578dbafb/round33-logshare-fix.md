# 第 33 轮：让第二个实例"说出它为什么没有 API" —— 一行修复 + 把上轮的推断变成直接观测

运行标识：`run-2026-10-10_015215-578dbafb`（第 33 轮追加）
时间：2026-10-11 18:38–18:42，Asia/Hong_Kong（+08:00）
本轮性质：**产品目录零改动**（0 staged）。全部在 `%TEMP%` 隔离副本里完成。
补丁：`apply-logshare-fix.ps1`（**1 行**）——**未应用**。

---

## 1. 上轮留下的是"推断"，本轮把它变成"观测"

第 32 轮我推断：第二个实例**一行日志都没有**，是因为 `LauncherLog` 用
`new StreamWriter(file, append: true)` 打开当天日志（`StreamWriter` 默认共享模式是 `FileShare.Read`，
**不共享写**），第一个实例已持有该文件 → 第二个实例打开抛 `IOException` → `RolloverLocked` 的 catch
把 `_writer` 置空 → 此后每次 `Write()` 都在开头 `return`。

**推断的依据是"B 的日志行不存在"**——但那也可能是别的原因（比如 B 根本没走到那段代码）。
本轮把根因修掉，然后看**那条日志会不会自己冒出来**：如果冒出来，推断就被证实了。

---

## 2. 修复：一行

```csharp
// 旧：StreamWriter 默认 FileShare.Read —— 不允许另一个进程打开同一个文件
_writer = new StreamWriter(file, append: true) { AutoFlush = true };

// 新：显式 FileShare.ReadWrite，让并发实例可以一起追加写同一个文件
FileStream stream = new FileStream(file, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
_writer = new StreamWriter(stream) { AutoFlush = true };
```

隔离副本验证：`dotnet build` **0 警告 0 错误**、全量测试 **941 通过 / 0 失败**、发布 `exit=0`
→ `78CC9324724AD51CF707722370FF9CF25758E68FC33EF614701F643CC24C35F7`。

---

## 3. A/B：同样"启动两次"，日志从 2 行变成 9 行

| 构建 | 日志行数 | 含 `启动失败` 的行 |
| --- | --- | --- |
| **修复前**（出厂 EXE） | **2** | **0** |
| **修复后**（P-LOGSHARE） | **9** | **1** |

修复后的日志里出现了此前**完全不存在**的那两行：

```
2026-10-11 18:39:56.725 [INFO]  === Pineglow Launcher 启动 v1.0.0 ===
2026-10-11 18:39:56.793 [ERROR] [LocalApi] 启动失败: Failed to listen on prefix
    'http://127.0.0.1:48123/' because it conflicts with an existing registration on the machine.
    | System.Net.HttpListenerException (183): ...
       at Pineglow.Launcher.Services.LocalApiService.Start(Nullable`1 portOverride)
       at Pineglow.Launcher.App.OnFrameworkInitializationCompleted()
```

这一条同时证实了三件事：

1. **上轮的推断成立**——第二个实例的日志写入确实在静默失败；
2. **机制成立**——`HttpListenerException (183)`，就是端口被占用；
3. **修复有效**——第二个实例现在会**说出它为什么没有 API**，而且带完整调用栈。

---

## 4. 这个修复解决了什么、没解决什么

| 已解决 | 说明 |
| --- | --- |
| **失败可诊断** | 第二个实例不再"什么都不说"。日志里直接给出端口冲突与调用栈 |

| **未解决**（仍需你取舍） | 说明 |
| --- | --- |
| 仍会开出**第二个窗口** | 没有单实例守卫；两个窗口共用一个后端 |
| 第二个窗口**静默借用**第一个实例的 API | 健康检查打的是共享端口，对方会回答 → `Web.Source` 指向对方的 URL |
| 先关掉其中一个，另一个变**隐形僵尸** | 界面外观逐字节不变，后端已死 |

即：本轮把「**无声失败**」变成了「**有声失败**」——这是最低限度该做的（失败时没有日志本身就是缺陷），
但**是否要有第二个窗口**、**要不要单实例守卫**，是产品取舍，仍在你手上（报告待办第 17 项）。

---

## 5. 累计状态

| 项 | 状态 |
| --- | --- |
| 已就绪的补丁 | **9 条**：P-UA、P-FABRICMETA、P-PROXYJVM、P-FAKEIP、P-IMPORTLOG、P-DURABLEQUEUE、P-DEADLOCK、C，以及本轮的 **P-LOGSHARE**（+ 2 个回归测试文件） |
| 本轮补丁的验证 | build 0/0、C# 941/0、发布 exit 0、**双击 A/B（2 行 → 9 行）** |
| 仍未修 | 缺陷 1、4、8、10，以及缺陷 12 的"第二个窗口/僵尸窗口"部分 |
| 待你决定 | C 的范围；提交 163 个未跟踪 + 41 个已修改文件；修哪几条缺陷 |

状态串：`TechnicalAcceptance=NOT_PASSED`（发布整体）；`FormalPhaseApproval=NOT_GRANTED`；`ReleaseApproval=NOT_GRANTED`。
