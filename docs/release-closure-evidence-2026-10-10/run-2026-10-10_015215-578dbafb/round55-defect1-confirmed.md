# 记录 55：缺陷 1 的表述**经查成立**（不是每项核查都会找出错误）

运行标识：`run-2026-10-10_015215-578dbafb` ｜ 2026-10-11 21:28–21:32（+08:00）
性质：**只读审计 + 文档补证，产品目录零改动**（0 staged）。

---

## 查的是什么

上一轮把缺陷 4 的范围收窄了（从"整合包导入"收窄到"本地文件导入"）。既然同类描述可能过宽，
本轮就查另一条**无补丁**的缺陷——**缺陷 1：「加载器只能通过整合包导入安装」**。

**如果版本下载接口能装加载器版本，这条就也过宽了。**

## 结果：**成立**

| 检查 | 读数 |
| --- | --- |
| 路由 `POST /api/versions/download` → | `HandleDownloadVersionAsync`（`LocalApiService.cs:4306`） |
| 它读什么参数 | 只读 `versionId`（`:4309`） |
| 它做什么 | `new MinecraftDownloader(...).InstallVersionAsync(gameDir, entry.VersionId, ...)`（`:4364`）——**纯原版安装** |
| 这一段里有没有任何加载器处理 | **没有**（`loader`/`Fabric`/`Forge`/`Quilt` 在该段一次都没出现） |

所以：**版本下载接口只装原版；加载器确实只有整合包导入这一条路**
（`InstancePackService.InstallLoaderAsync`，分派 Fabric / Quilt / Forge / NeoForge 四条，遇 OptiFine 报错）。

**缺陷 1 的表述经查成立**，报告无需更正。

## 为什么这条也值得记

最近几轮连续发现**自己的描述过宽**（缺陷 4、`httpErrors` 成因、"探针没记 URL"、"204" 当全仓规模）。
容易由此得出一个错误的印象：**"我的描述总是过宽"**。

本轮是一个反例：**同样的方法用在缺陷 1 上，结论是"原文准确"**。
这说明那几轮的问题不是"我习惯性夸大"，而是**"从一个样本推广到全集"这个具体动作**——
缺陷 4 是这么错的（536 秒的样本 → "整合包导入"全集），而缺陷 1 没有这个动作
（它本来就是全树对码的结论）。

**核查的价值不在于"总能找出错"，而在于把两类分开：经得起查的，和经不起查的。**

状态串：`TechnicalAcceptance=NOT_PASSED`；`FormalPhaseApproval=NOT_GRANTED`；`ReleaseApproval=NOT_GRANTED`。