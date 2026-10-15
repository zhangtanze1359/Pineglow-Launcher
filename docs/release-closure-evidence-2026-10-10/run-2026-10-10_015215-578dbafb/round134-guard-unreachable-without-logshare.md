# 记录 134：B 为什么崩 —— **它死在 `LauncherLog.Init()`，根本到不了守卫**（这一棵树没打 P-LOGSHARE）

运行标识：`run-2026-10-10_015215-578dbafb` ｜ 2026-10-13 00:26–00:50（+08:00）
性质：**只读 + 隔离夹具实测，产品目录零改动**（0 staged）。

---

## 本轮先把夹具修好了（上一轮的教训）

```
=== fixture verification ===
  exe present=True  size matches=True  flag=True  config absent=True
```

**这次逐项核对了**（不再静默复制、不再用构建产物而是 **publish** 出来的单文件）。
**所以夹具不是原因。**

## 但结果和上一轮一样

```
A apiUp=True alive=True pid=3356
B alive=False
B exit code = -532462766        ← 又是 0xE0434352（.NET 未处理异常）

日志里只有 A 的两行：
  === Pineglow Launcher 启动 v1.0.0 ===
  [LocalApi] 监听 127.0.0.1:53751, ui=…
'已有实例在运行' = 0             ← 守卫那一行**没有**
```

**B 连"=== Pineglow Launcher 启动 ==="那一行都没写。**

## 所以崩溃点**在启动日志之前**

看 `App.cs` 的顺序（我第 119 轮读过全文）：

```
21:  LauncherLog.Init();                                    ← 打启动日志之前
22:  LauncherLog.Info("=== … 启动 v… ===");                  ← B 连这行都没写
…
54:  _singleInstanceMutex = new Mutex(…)                    ← 我的守卫在这里
55:  if (!createdNew) { … "已有实例在运行" … }
```

**B 死在 `:21` 附近** —— 而守卫在 `:54`。**它根本到不了。**

## 为什么 `LauncherLog.Init()` 会让第二个进程崩

**因为我这一棵树（`pineglow-si-*`）只打了 P-SINGLEINSTANCE，没有打 P-LOGSHARE。**

- **P-LOGSHARE 改的正是这一行**：`new StreamWriter(file, append: true)` 的共享模式
  （默认 `FileShare.Read` **不允许第二个进程打开**）；
- **没打它的时候**：A 占着 `launcher-2026…log`，**B 打不开 → 抛异常**；
- 而 `LauncherLog.Init()` 在 `:21`，**在 try/catch 之外** → **未处理异常 → `0xE0434352` → 进程死**。

**这解释了上一轮的崩溃，也解释了本轮的** —— 两次都是同一个原因，而**不是夹具**。

## 由此得到一条**关于补丁集本身**的事实

> **单实例守卫在"没打 P-LOGSHARE"的树上根本执行不到** ——
> 第二个实例**在打开日志时就死了**，比守卫早 33 行。

**所以要验这个守卫，必须把 P-LOGSHARE 一起打上。**
**下一轮的测法**：应用 **P-LOGSHARE + P-SINGLEINSTANCE**（或干脆全 20 条）再跑。

**这同时说明一件事**：**"逐个补丁单独验证"对这种有前置依赖的补丁不成立** ——
P-SINGLEINSTANCE 的前置不只是"能编译"，还包括"日志层允许多进程"。

## 另外：本机有一个**不属于我的** Launcher 实例

查端口占用时发现：

```
pid=28600  path=C:\Users\Administrator\.codex\worktrees\bfeb\松光启动器\dev\PineglowLauncher\bin\Release\net10.0\Pineglow.Launcher.exe
```

它**不在 `%TEMP%` 下**，来自另一个 worktree —— **很可能是你自己的实例**。

**我没有杀它**（我的清理只针对 `%TEMP%` 下的自有夹具进程，本轮那个数=0）。
这也顺带解释了本轮开始时"默认端口 48123 被占"。

**顺带一个观察**：那个实例是**未打补丁**的构建，所以它**不会**创建我那个具名互斥体 ——
**所以我的守卫测试不受它影响**（本轮确实用了一个显式端口跑，也照样复现了崩溃）。

## 当前状态

| 项 | 状态 |
| --- | --- |
| 已就绪的补丁 | 20 条（17 个脚本） |
| 证据等级 | A 15 / B 2 / **C 2**（P-STARTUPLOG 不可复现；**P-SINGLEINSTANCE 待验**）/ D 1 |
| **P-SINGLEINSTANCE** | 编译级通过；**验证被前置依赖挡住** —— 需与 P-LOGSHARE 一起打 |
| 合并验证 | 19 条那次；**20 条待重跑** |
| 唯一未做的功能 | 缺陷 13 ③ |
| 待你决定 | ① 提交；② C 的范围；③ 缺陷 12 是否采纳单实例守卫；④ 是否做 13 ③ |

状态串：`TechnicalAcceptance=NOT_PASSED`；`FormalPhaseApproval=NOT_GRANTED`；`ReleaseApproval=NOT_GRANTED`。
