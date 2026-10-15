# 第 27 轮：首启风险「没有 WebView2 运行时会怎样」——审计结果 + 一次**失败**的复现尝试

运行标识：`run-2026-10-10_015215-578dbafb`（第 27 轮追加）
时间：2026-10-11 17:27–17:31，Asia/Hong_Kong（+08:00）
本轮性质：**只读审计 + 隔离夹具实验，产品目录零改动**（0 staged）。

---

## 1. 为什么要查这个

README 把 WebView2 写成**必需项**，并说"缺失时需安装 Evergreen Runtime，否则窗口起不来界面"。
这是一台**干净机器上用户遇到的第一个东西**，而前 26 轮从没验过它。所以本轮查两件事：
① 代码里有没有优雅的失败路径；② 能不能在不破坏本机的前提下复现。

---

## 2. 代码审计结果：**没有任何优雅失败路径**

| 位置 | 事实 |
| --- | --- |
| 全局异常处理 | **全树没有任何** `AppDomain.CurrentDomain.UnhandledException` / `DispatcherUnhandledException` / `TaskScheduler.UnobservedTaskException` |
| `App.cs:44` | `classicDesktopStyleApplicationLifetime.MainWindow = new WebShellWindow();` —— **没有 try/catch**（同一方法里 `LocalApiService.Start()` 是有 try/catch 的，窗口创建没有） |
| `WebShellWindow.axaml.cs:177` | `private async void OnWindowOpenedAsync(...)` —— **`async void` 且无外层 catch**；里面的 `Web.Source = …` 一旦抛异常，异常逃逸成未处理异常 |
| `WebShellWindow.axaml.cs:203-210` | 健康检查失败时走 catch，catch 里仍然给 `Web.Source` 赋值——**如果 WebView 本身起不来，这一句照样会抛** |
| 专门处理 WebView2 缺失的代码 | **不存在**（全树检索 `WebView2RuntimeNotFound` / `GetAvailableCoreWebView2BrowserVersionString` / `CoreWebView2Environment` 均无命中） |

**推断（代码级，未复现）**：在没有 WebView2 运行时的机器上，Avalonia 创建原生 WebView 控件会抛
（WebView2 SDK 的 `WebView2RuntimeNotFoundException`），而产品**没有任何一层接住它**——
既不会弹出"请安装 WebView2 运行时"的提示，也不会在日志里留下解释，用户看到的只会是进程直接消失。

---

## 3. 复现尝试：**失败了，而且我能证明它失败**

我想在不卸载本机 WebView2 的前提下模拟"运行时缺失"。做法是给启动器设：

```
WEBVIEW2_BROWSER_EXECUTABLE_FOLDER = C:\definitely-not-a-webview2-runtime
```

结果：**进程没有崩**（60 秒内仍在运行），API 正常起来（`GET /` → 200，6,962 字节），stderr/stdout 全空。

**但"没崩"并不等于"缺失时是优雅的"**——必须确认这个环境变量到底有没有起作用。于是用第 19 轮的窗口捕获探针把**真实窗口**拍下来对比：

| 捕获 | SHA-256 | 字节 |
| --- | --- | --- |
| 第 19 轮（正常 WebView2） | `E8D42C4447F3A43E34F585A3067F21776F8D8844418F3F29C910A2C6F39CE89C` | 423,614 |
| 本轮（设了"指向不存在目录"） | `E8D42C4447F3A43E34F585A3067F21776F8D8844418F3F29C910A2C6F39CE89C` | 423,614 |

**逐字节相同** → 界面被完整渲染 → **WebView2 全程正常工作** → **那个环境变量根本没有让运行时失效**。

所以：

> **这次"模拟缺失"是无效实验。** 我**没有**复现出"没有 WebView2"的场景，
> 也就**不能**声称"缺失时进程不会崩"。第 2 节只是代码级推断。

把这条记下来的实际价值：**下一个人想用同样的办法模拟时会白跑一趟**——
`WEBVIEW2_BROWSER_EXECUTABLE_FOLDER` 在这个 Avalonia 宿主上不改变运行时解析结果。

（其余能想到的办法都越界了：卸载本机 WebView2、重命名 `EdgeWebView` 目录——都会改动系统状态，不做。）

---

## 4. 定性

| 维度 | 判断 |
| --- | --- |
| 是不是已复现的缺陷 | **不是**。代码级推断，**未复现** |
| 现实风险 | **低到中**：Windows 11 自带 WebView2；更新过的 Windows 10 随 Edge 安装也有。风险集中在 LTSC／Server／被精简过的系统 |
| 文档是否诚实 | **是**。README 明确写了"必需"与"缺失时需安装 Evergreen Runtime" |
| 真正的缺口 | 缺的是**失败时的可读性**：没有提示、没有日志解释、没有全局兜底。用户只会看到进程消失，无从知道要装什么 |
| 建议（很小的改动） | 给 `new WebShellWindow()` 与 `OnWindowOpenedAsync` 加 try/catch，失败时写一条明确的 `LauncherLog.Error`（说明需要 WebView2 运行时与下载地址），并尽量弹一个最小提示框；可再加一个全局 `UnhandledException` 兜底把异常落日志。**属产品改动，需授权** |

---

## 5. 累计缺陷清单的变化

新增**第 10 条**，但明确标注为「**未复现（代码级推断）**」——与前面 9 条（都已复现）在证据等级上**不同**，不应混为一谈。

状态串：`TechnicalAcceptance=NOT_PASSED`（发布整体）；`FormalPhaseApproval=NOT_GRANTED`；`ReleaseApproval=NOT_GRANTED`。
