# 记录 82：缺陷 1 的改造勘察 —— 从"工作量最大"变成"边界清楚的改动"

运行标识：`run-2026-10-10_015215-578dbafb` ｜ 2026-10-12 02:44–02:56（+08:00）
性质：**只读审计，产品目录零改动**（0 staged）。

---

## 本轮问的问题

缺陷 1 一直被我记成"工作量最大的一条"（**没有独立的加载器安装入口**）。但它到底有多大？
本轮去把它的**边界**摸清楚 —— 结果比预想的小得多。

## 事实（都已核对）

| 事实 | 出处 |
| --- | --- |
| 四条安装路径的分派方法是 `InstallLoaderAsync(loaderKind, instance, minecraftVersion, loaderVersion, javaPath, progress)` | `InstancePackService.cs:246`、`:329` 两处调用 |
| **它是 `private` 实例方法** | 同上 —— 所以 `LocalApiService` **无法直接调用它** |
| 四个加载器服务的构造签名**完全一致** | `FabricService` / `QuiltService` / `ForgeService` / `NeoForgeService` 都是 `(HttpClient http, string gameDirectory, MinecraftDownloader downloader, Action<string> log)`；后两者在 P-PROXYJVM 之后多一个**可选**的 `_installerJvmArgs` |
| **没有任何路由为已存在的实例安装加载器** | 全文件搜 `InstallLoaderAsync` / `install-loader` / `installLoader` → **0 命中**（这正是缺陷 1） |

## 因此改造是这样的（三步，都不是大工程）

1. **把分派暴露出来**：`InstallLoaderAsync` 从 `private` 改成 `public`（或加一个 public 包装）。
   这是**唯一的"结构性"改动**，也是此前没人做过这一步的原因 —— 它现在只服务于导入流程。
2. **加一条路由**：`POST /api/instances/{id}/loader`，body 带 `loaderType` / `loaderVersion`。
   处理器里 `new InstancePackService(ApiHttp, defaultGameDir, CreateImportLogger(), BuildLoaderInstallerJvmArgs(config))`
   然后调那个刚公开的方法。**构造参数都是现成的**（P-PROXYJVM / P-IMPORTLOG 已经把这两个 helper 备好了）。
3. **UI 入口**：在实例详情或"安装管理"里加一个"安装加载器"的按钮 —— 这是唯一需要动前端的部分。

## 与缺陷 4 的对比（一个有用的参照）

| | 缺陷 4（已做完） | 缺陷 1 |
| --- | --- | --- |
| 是否需要改已有方法 | 否（只加登记） | **是**（改可见性） |
| 是否需要新路由 | 否 | **是** |
| 是否需要改 UI | **否**（复用内容中心那套） | **是**（要加按钮） |
| 我估的规模 | 中 | **"最大"** → 实际**中等偏上** |

## 本轮没做什么（如实）

**没有写补丁。** 只做了勘察，因为本轮预算不足以走完"改可见性 + 加路由 + 加 UI + 构建 + 测试 + 行为验证"。
**没有把缺陷 1 说成"快做完了"** —— 它仍然是一条**无补丁**的缺陷，只是现在**边界清楚了**。

## 当前状态

| 项 | 状态 |
| --- | --- |
| 已就绪且十六条合并验证通过的补丁 | 16 条 |
| **仍无补丁** | **1 条**：缺陷 1（本轮已勘察，三步方案已定） |
| 待你决定 | ① 提交（2467 / 204）；② C 的范围；③ **是否要做缺陷 1** |

状态串：`TechnicalAcceptance=NOT_PASSED`；`FormalPhaseApproval=NOT_GRANTED`；`ReleaseApproval=NOT_GRANTED`。
