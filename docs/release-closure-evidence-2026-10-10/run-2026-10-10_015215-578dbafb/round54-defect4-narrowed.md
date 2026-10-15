# 记录 54：缺陷 4 的表述**过宽**，已收窄

运行标识：`run-2026-10-10_015215-578dbafb` ｜ 2026-10-11 21:24–21:28（+08:00）
性质：**只读审计 + 文档更正，产品目录零改动**（0 staged）。

---

## 原文与事实

报告里缺陷 4 写的是「**整合包导入**没有进度/日志」。核查后发现**过宽**：

| 事实 | 证据 |
| --- | --- |
| `内容中心`（Modrinth）导入路径**有**进度界面 | `launcher.js:2464` 设 `data-progress`、`:2465` 显示 `content.installing N%`；数据来自 `getContentTasks()` → `/api/content/tasks` → `HandleListContentTasksAsync`（`LocalApiService.cs:5365`），由 `ContentTasks`（`:161`）供给 |
| **本地文件导入路径没有** | `/api/instances/import`（`LocalApiService.cs:466`）走的是另一条路，不进 `ContentTasks` |
| 536 秒那次实测走的是哪条 | **本地文件导入路径**（P-IMPORTLOG 补的正是 `HandleImportInstanceAsync` 的日志） |

## 更正后的表述

> 缺陷 4：**本地文件导入（`/api/instances/import`）没有进度界面**；
> `内容中心` 导入路径**有**进度条。已实测的 536 秒无输出，是本地文件路径。

## 这条为什么值得单独记

这是本会话第 N 次发现**自己的汇总层把范围说宽了**（此前：163 的范围限定、"204" 当全仓规模、
`httpErrors` 的成因、"探针没记 URL"）。模式一致：**底层证据（536 秒无输出）是真的，
但从一个样本推广成"整合包导入"这个全集，是我加上去的。**

**这次的更正不改变结论**（本地文件导入确实没有进度界面，仍是无补丁的三条之一），
但它改变**范围**——而范围正是用户决定"要不要修、修哪条"的依据。

状态串：`TechnicalAcceptance=NOT_PASSED`；`FormalPhaseApproval=NOT_GRANTED`；`ReleaseApproval=NOT_GRANTED`。