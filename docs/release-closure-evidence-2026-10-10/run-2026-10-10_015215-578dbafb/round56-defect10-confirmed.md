# 记录 56：缺陷 10 的代码级断言**经查逐条成立**

运行标识：`run-2026-10-10_015215-578dbafb` ｜ 2026-10-11 21:32–21:36（+08:00）
性质：**只读审计 + 文档补证，产品目录零改动**（0 staged）。

---

## 为什么查这条

缺陷 10 是我**唯一一条"未复现"**的缺陷（代码级推断），也是三条无补丁缺陷之一。
**推断类的断言最需要复核**——它的证据本来就是"读代码"，一旦读错就全错。

## 逐条复核：全部成立

### ① 全树确实没有全局异常处理

```
grep: UnhandledException | DispatcherUnhandledException | UnobservedTaskException | AppDomain.CurrentDomain
→ 0 命中
```

### ② `App.cs:44` 的 `new WebShellWindow()` 确实不在 try/catch 里

```csharp
31:  try
33:      LocalApiService.Start();          // ← 这里有保护
35:  catch (Exception ex)
37:      LauncherLog.Error("[LocalApi] 启动失败: " + ex.Message, ex);
38:      Console.Error.WriteLine("[LocalApi] 启动失败: " + ex.Message);
...
44:  classicDesktopStyleApplicationLifetime.MainWindow = new WebShellWindow();   // ← 这里没有
```

**而且这个不对称很说明问题**：同一段代码里，作者在 `LocalApiService.Start()`（第 31 行）
**想到了失败**并写了日志 + 控制台输出，却在紧邻的 `new WebShellWindow()`（第 44 行）**没有做同样的事**。
这不是"没写防御代码"，而是**在写防御代码的同一屏里漏了一处**。

## 结论

**缺陷 10 的表述经查逐条成立，报告无需更正。**

## 连续两轮的复核结果

| 轮 | 复核对象 | 结果 |
| --- | --- | --- |
| 43 | 缺陷 1（加载器只能经整合包导入） | **成立** |
| 44 | 缺陷 10（无全局异常处理 + `new WebShellWindow()` 无保护） | **成立** |

而更早几轮查出的是**过宽**（缺陷 4）与**错误成因**（`httpErrors`）。

**合起来看**：我的缺陷**描述**大体站得住；出问题的是**推广**（从一个样本说成全集）和**归因**（把猜测写成结论）。
这两类恰好是最容易被忽略的，因为它们**读起来都很像已经查过了**。

状态串：`TechnicalAcceptance=NOT_PASSED`；`FormalPhaseApproval=NOT_GRANTED`；`ReleaseApproval=NOT_GRANTED`。
