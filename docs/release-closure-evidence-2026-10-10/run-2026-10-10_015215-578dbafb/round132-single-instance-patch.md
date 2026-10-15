# 记录 132：缺陷 12 的补丁（**第 20 条**）—— 单实例守卫，且**位置是按实测根因选的**

运行标识：`run-2026-10-10_015215-578dbafb` ｜ 2026-10-12 16:44–17:10（+08:00）
性质：**产品目录零改动**（0 staged）。补丁在隔离副本验证。
补丁：`apply-single-instance.ps1`（3 步）——**未应用**。

---

## 结果

```
[OK] P-SINGLEINSTANCE 1/3 the mutex field
[OK] P-SINGLEINSTANCE 2/3 using System.Threading
[OK] P-SINGLEINSTANCE 3/3 the guard
steps applied = 3

buildExit=0   0 警告 0 错误
testExit=0    失败: 0，通过: 941
```

（941 = 未打其它补丁时的基线数；本轮只应用了这一个补丁。）

## 三步

| 步 | 内容 |
| --- | --- |
| 1/3 | 加静态字段 `private static Mutex? _singleInstanceMutex;`（含说明为什么需要它） |
| 2/3 | `using System.Threading;` |
| 3/3 | **在 `new WebShellWindow()` 之前**加守卫：拿不到具名互斥体 → 记一行日志 → `Shutdown()` → `return` |

## ⚠️ 关键在于**位置**，而位置是按实测选的

```
53:  if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime classic…)
54:      _singleInstanceMutex = new Mutex(… @"Local\PineglowLauncher.SingleInstance" … out bool createdNew);
55:      if (!createdNew)
57:          LauncherLog.Info("已有实例在运行，本进程退出（单实例守卫）");
58:          Console.Error.WriteLine("Pineglow Launcher 已在运行。");
59:          classicDesktopStyleApplicationLifetime.Shutdown();
60:          return;
63:      classicDesktopStyleApplicationLifetime.MainWindow = new WebShellWindow();
```

**必须在 `:63` 之前。** 理由来自第 121 轮的判别实验：
一旦走到 `new WebShellWindow()`，第二个实例的 WebView2 就会去抢**被第一个占用的用户数据目录**
（`<exeDir>\Pineglow.Launcher.exe.WebView2\EBWebView`），
结果是"**窗口壳建出来了、里面什么都不渲染，而且进程不退出**"。

**如果我没做那个判别实验**，我会按"抢不到端口就退出"去写 ——
而那个方案在**真实场景下不会触发**（实验证明：给第二个实例一个**可用端口**，它照样渲染不出来）。
**这个补丁的形状是实验纠正出来的。**

## 选的行为（以及没选的那个）

| 变体 | 做法 | 本轮 |
| --- | --- | --- |
| **(a) 第二个实例退出** | 记一行日志 + `Shutdown()` | **✅ 采用**（最小、可逆） |
| (b) 把第一个实例的窗口提到前台 | 需要 IPC | **未做**（更友好，但大得多） |

**选 (a) 的理由**：它是"**把僵尸去掉**"的最小改动，而且**不引入跨进程通信**。
(b) 更像成品行为，但那是**另一个决定**，不该由我替你定。

## ⚠️ 验证边界（必须说清）

| 断言 | 状态 |
| --- | --- |
| 补丁能应用（3 步各断言恰好 1 处匹配） | **✅** |
| **能编译**（0 警告 0 错误） | **✅** |
| **全量测试 941/0** | **✅** |
| **真实双击两次 → 第二个真的退出、日志有那一行** | **⏳ 未验证** |

**这条恰恰是能验的，而且验法我很熟**（第 116/118 轮做过两次同类实验）：
默认端口起 A → 再起 B → 看 B 是否退出、日志是否有"已有实例在运行"、以及**是否不再有那个空进程**。

**下一轮做它。** 在那之前，这个补丁**只能算编译级验证通过**。

## 补丁计数

| | |
| --- | --- |
| 之前 | 19 条 |
| **现在** | **20 条** |

**尚未并入合并验证**（第 96/100 轮那次是 19 条）。

## 当前状态

| 项 | 状态 |
| --- | --- |
| 已就绪的补丁 | **20 条**（17 个脚本） |
| 合并验证 | 19 条那次；**20 条待重跑** |
| 缺陷 | 15 条，全部有补丁；**缺陷 12 从"待取舍"变成"补丁已就绪"** |
| 唯一未做的功能 | 缺陷 13 ③（设置页 Java 安装入口） |
| 待你决定 | ① 提交；② C 的范围；③ **缺陷 12 是否采纳单实例守卫（选 a 还是 b）**；④ 是否做 13 ③ |

状态串：`TechnicalAcceptance=NOT_PASSED`；`FormalPhaseApproval=NOT_GRANTED`；`ReleaseApproval=NOT_GRANTED`。
