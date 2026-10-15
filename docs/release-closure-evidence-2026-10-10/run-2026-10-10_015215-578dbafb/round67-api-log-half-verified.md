# 记录 67：缺陷 15 的补丁 —— **一半验证通过，一半没生效**（如实记录）

运行标识：`run-2026-10-10_015215-578dbafb` ｜ 2026-10-11 23:10–23:22（+08:00）
性质：**产品目录零改动**（0 staged）。补丁在隔离副本验证。
补丁：`apply-api-log.ps1`（2 步）——**未应用**。

---

## 两处改动

| # | 改什么 | 状态 |
| --- | --- | --- |
| ① | 调度器的 `catch` 只写 `Debug.WriteLine`（**不进日志文件**），补一条 `LauncherLog.Error` | **✅ 验证通过** |
| ② | `prelaunch-check` 增加"**实例自己的目录**是否存在"这一项（此前只查共享游戏目录） | **❌ 没生效** |

## ① 验证通过：500 终于有完整日志

**同一场景**（实例目录指向不存在的路径），修复后日志出现：

```
[ERROR] [LocalApi] POST /api/instances/6478bce1.../launch 失败: 未找到版本 JSON：C:\Users\...
   at Pineglow.Launcher.Services.VersionResolver.ResolveInternalAsync(String gameDirectory, String versionId, HashSet`1 visited)
   at Pineglow.Launcher.Services.VersionResolver.ResolveAsync(String gameDirectory, String versionId)
   at Pineglow.Launcher.Services.LauncherService.BuildCommandAsync(LaunchProfile profile, CancellationToken ct)
   at Pineglow.Launcher.Services.LauncherService.LaunchAsync(LaunchProfile profile, CancellationToken ct)
   at Pineglow.Launcher.Services.LocalApiService.LaunchBodyAsync(HttpListenerResponse response, String instanceId)
```

**修复前**：日志 2 行、什么都没有。**修复后**：完整异常 + **五层调用栈**。

而且这一处**覆盖面很广**：它修的是**所有** 500（不只 launch）——此前**每一个** 500 都只写 `Debug.WriteLine`，用户拿到的日志里查不到。

## ② 没生效：`prelaunch` 仍报 ok

```
prelaunch ok=True errors=0 warnings=0     ← 与我加的那项检查应有的结果不符
```

我加的检查是 `!string.IsNullOrWhiteSpace(instance.GameDirectory) && !Directory.Exists(instance.GameDirectory)`。
**没生效的可能原因（未验证）**：实例的那个目录属性名与 `GameDirectory` 不一致，或者我用 `Add-Member` 塞进去的值没有按预期反序列化回来。

**我没有继续查**——本轮上下文预算用尽。**不写成"已修"**。

## 顺带：这次实测暴露了真正的失败原因

日志里的 `未找到版本 JSON` 说明：实例目录缺失时，launch 失败在**版本 JSON 找不到**这一步，
而不是"目录不存在"。**这条错误比"目录不存在"更具体、更有用** —— 修复 ② 时应当把它一起考虑
（自检里查"版本 JSON 是否可解析"可能比查"目录是否存在"更贴近真实失败点）。

## 验证读数

| 门槛 | 结果 |
| --- | --- |
| 应用 2 步 | 命中 |
| `dotnet build -c Release` | **0 警告 0 错误** |
| C# 全量 | **941 通过 / 0 失败** |
| 发布 | `exit=0` |
| 残留进程 | **0** |

状态串：`TechnicalAcceptance=NOT_PASSED`；`FormalPhaseApproval=NOT_GRANTED`；`ReleaseApproval=NOT_GRANTED`。
