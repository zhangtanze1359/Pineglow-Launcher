# 记录 73：缺陷 4 的**改造勘察**（本轮只做侦察，不动手）

运行标识：`run-2026-10-10_015215-578dbafb` ｜ 2026-10-12 00:18–00:26（+08:00）
性质：**只读审计，产品目录零改动**（0 staged）。

---

## 为什么本轮不动手

缺陷 4 的修法是**优雅的**：本地文件导入（`/api/instances/import`）如果也登记一条 `ContentTaskEntry`，
那么**内容中心已有的进度界面会免费把它显示出来**——不需要改任何 UI 代码
（UI 已经在轮询 `/api/content/tasks` 并渲染 `data-progress` 与 `content.installing N%`）。

但要把这件事做完并**验证**，需要：读方法体 → 写补丁 → 构建 → **跑一次真实导入**（分钟级）。
**本轮剩余预算不足以走完**，而留一个"没验证过的补丁脚本"违反本会话一直坚持的纪律
（每个补丁都必须有验证读数）。

**所以本轮只做勘察，把下一轮要用的事实钉住。**

## 勘察结果（下一轮直接用）

| 需要的东西 | 位置 |
| --- | --- |
| 任务条目类型 | `LocalApiService.cs:145` — `private sealed class ContentTaskEntry` |
| 任务登记（内容中心路径怎么做的） | `LocalApiService.cs:5358` — `ContentTasks.Add(entry)` |
| 任务状态更新 | `LocalApiService.cs:5435` — `private static void UpdateContentTask(ContentTaskEntry entry, string message, double progress = -1)` |
| 本地导入的路由 | `LocalApiService.cs:466-470` → `await HandleImportInstanceAsync(request, response);` |
| 导入的进度回调 | 已由 **P-IMPORTLOG** 接通（`CreateImportLogger()` 会带节流地报进度） |

**方法定义**（下一轮已无需再找）：`private static async Task HandleImportInstanceAsync(HttpListenerRequest, HttpListenerResponse)`，
**位于 `:1419`，方法体到 `:1538`（120 行）**。

关键锚点（本轮已定位）：

| 位置 | 内容 | 用途 |
| --- | --- | --- |
| `:1421-1443` | 解析 `fileName` / `fileBase64` / `operationId`，base64 解码与 24 MiB 上限校验 | 入口——**任务登记应放在这之后**（参数校验通过才登记） |
| `:1446-1448` | 建临时目录、`CreateSafeTemporaryUploadPath` | |
| `:1450` | `try {` 主流程开始 | 登记的 try 应与之对齐 |
| **`:1455`** | `InstancePackService packService = new InstancePackService(ApiHttp, defaultGameDir, _ => { });` | **进度回调就是这里**——P-IMPORTLOG 已把它换成 `CreateImportLogger()`；缺陷 4 要接的是同一个位置 |
| `:1458-1461` | `operationId` 存在时建 `importCts`（可被 cancel 路由取消） | 终态要考虑取消 |

## 下一轮的步骤（写清楚，避免重复勘察）

1. 取 `HandleImportInstanceAsync` 的定义与首尾；
2. 仿照内容中心路径：入口处 `ContentTasks.Add(entry)`，把 `CreateImportLogger()` 的消息接进
   `UpdateContentTask(entry, msg, progress)`，结束/失败时落终态；
3. 构建 + 全量测试；
4. **现场验证**：用一个真实 `.mrpack` 走本地导入，用 CDP 或直接读 `/api/content/tasks`
   确认进度**随导入推进**（对照：修复前该端点对本地导入始终为空）。

## 当前状态（不变）

| 项 | 状态 |
| --- | --- |
| 已就绪且十五条合并验证通过的补丁 | 15 条 |
| 仍无补丁 | **2 条**：第 1 条（加载器安装入口）、第 4 条（本地文件导入进度界面） |
| 待你决定 | ① 提交（2467 / 204）；② C 的范围；③ 还修哪两条 |

状态串：`TechnicalAcceptance=NOT_PASSED`；`FormalPhaseApproval=NOT_GRANTED`；`ReleaseApproval=NOT_GRANTED`。
