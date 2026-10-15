# 记录 74：缺陷 4 的接线方案已确定 —— 以及**为什么不能只做一半**

运行标识：`run-2026-10-10_015215-578dbafb` ｜ 2026-10-12 00:34–00:44（+08:00）
性质：**只读审计，产品目录零改动**（0 staged）。

---

## 最后一块拼图：logger 的形状

上一轮缺的是"P-IMPORTLOG 插进去的那个 logger 是什么形状"。本轮取到：

```csharp
private static Action<string> CreateImportLogger()
```

**返回 `Action<string>`** —— 所以接线可以直接写：

```csharp
Action<string> importLog = CreateImportLogger();          // 复用它的节流与落盘
ContentTaskEntry importEntry = new ContentTaskEntry
{
    Id = "ct" + DateTimeOffset.Now.ToUnixTimeMilliseconds(),
    Category = "modpack",
    ProjectId = fileName,
    Source = "local",
    Title = fileName,
    Status = "running",
    Message = "正在导入 " + fileName,
    StartedAt = DateTimeOffset.Now
};
lock (ContentGate) { ContentTasks.Add(importEntry); }

InstancePackService packService = new InstancePackService(
    ApiHttp, defaultGameDir,
    msg => { UpdateContentTask(importEntry, msg); importLog(msg); },   // 进度同时进 UI 与日志
    BuildLoaderInstallerJvmArgs(config));
```

对照：内容中心路径的建条目写法在 `:5345-5359`，更新函数 `UpdateContentTask(entry, message, progress = -1)` 在 `:5435`（它自己加 `ContentGate` 锁）。

## **为什么本轮仍然不动手**（这次不是预算问题，是设计问题）

**只做"登记 + 更新"而**不**做终态，比现在更糟。**

`ContentTaskEntry.Status` 有 `queued / running / completed / failed` 四态（`:153`），
而 UI 是**轮询** `/api/content/tasks` 渲染的（`launcher.js:2464-2465`）。
如果我只登记、只更新进度、**不在导入结束时写终态**，那么这条任务会**永远停在 `running`** ——
界面上出现一个**永不消失的幽灵导入**，而且它还会盖住用户真正要看的进度。

**所以终态是必需的，不是可选的。** 而它需要 `HandleImportInstanceAsync` 的**尾部**
（成功分支、异常分支、以及 `operationId` 的取消分支 `:1458-1461`），**那一段我还没有读**。

## 下一轮要做的（只剩一件事）

读 `HandleImportInstanceAsync` 的 **`:1462-1538`**，找到：

1. 导入成功的那一行（拿到新实例 id → `Status = "completed"`、`ResultInstanceId = …`）；
2. `catch` 分支（→ `Status = "failed"`、`Message = ex.Message`）；
3. 取消分支（→ `Status = "failed"`、`Message = "已取消"`）。

然后把上面那段接线**和终态一起**写进补丁——**要么两个都有，要么都不做**。

## 当前状态（不变）

| 项 | 状态 |
| --- | --- |
| 已就绪且十五条合并验证通过的补丁 | 15 条 |
| 仍无补丁 | **2 条**：第 1 条（加载器安装入口）、第 4 条（本地文件导入进度界面） |
| 缺陷 4 的进度 | 接线方案已定；**终态锚点待读**；不做半成品 |

状态串：`TechnicalAcceptance=NOT_PASSED`；`FormalPhaseApproval=NOT_GRANTED`；`ReleaseApproval=NOT_GRANTED`。
