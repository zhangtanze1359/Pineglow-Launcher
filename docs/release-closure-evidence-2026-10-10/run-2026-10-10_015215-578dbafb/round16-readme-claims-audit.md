# 第 16 轮：README 功能清单逐条对码 —— 新增一条「文档过度承诺」

运行标识：`run-2026-10-10_015215-578dbafb`（第 16 轮追加）
时间：2026-10-11 14:45–14:55，Asia/Hong_Kong（+08:00）
本轮性质：**只做代码/文档对照，产品目录零改动**（0 staged）。
方法：把 `README.md` 的功能清单拆成 15 条可判定的断言，逐条在 `dev/` 与 `ui/` 里找实现证据。

---

## 1. 结果：15 条里 13 条有实现，2 条是过度承诺

| # | README 原文断言 | 实现证据 | 判定 |
| --- | --- | --- | --- |
| 1 | 离线账号 | `AccountService.cs`、`MinecraftAccount.cs` | ✅ |
| 2 | 微软正版（浏览器授权登录） | `MicrosoftAuthService.cs` | ✅（真实凭据未验，属 E） |
| 3 | Authlib 第三方皮肤站 | `AuthlibInjectorService.cs`、`AuthlibAccount.cs` | ✅ |
| 4 | 官方版本安装 | `MinecraftDownloader.cs` + `/api/versions/download` | ✅（已实测装完 1.21.11） |
| 5 | 版本校验 / 修复 | 下载前 SHA-1 比对 + 缺失文件补齐（`MinecraftDownloader`） | ✅ |
| 6 | **断点续传** | `.download` 临时文件 + `Range` + 206 + SHA-1 提升 + 416 处理 | ✅（第 11 轮注入式确证） |
| 7 | **可恢复任务队列** | `AppConfig.DownloadQueue` 等三处**零引用**；实际是 `LocalApiService.cs:143` 的内存表 | ❌ **过度承诺** |
| 8 | Forge / Fabric / NeoForge / Quilt | `ForgeService` / `FabricService` / `NeoForgeService` / `QuiltService`，`InstancePackService.cs:479-506` 四个分支 | ✅（三种已实测启动到标题） |
| 9 | **OptiFine** | **全树只有 4 处引用，全都不提供安装能力**（见第 2 节） | ❌ **过度承诺（本轮新增）** |
| 10 | Modrinth 整合包导入安装 | `InstancePackService` 的 Modrinth 路径 + `/api/instances/import` | ✅（已实测） |
| 11 | 实例独立隔离目录 | `InstanceIsolationService.cs`（`mods/config/saves/resourcepacks/shaderpacks/screenshots`） | ✅ |
| 12 | 实例级 Java | `instance.JavaPath` + `LocalApiService.cs:2332 ResolveJavaPath(config, instance, …)` | ✅ |
| 13 | Mod 启停 / 依赖解析 / 健康检查 | `HandleToggleModAsync`（`EnableMod`/`DisableMod`）、`ModContentService` 依赖安装、`ModHealthService.Analyze` | ✅ |
| 14 | Modrinth 搜索 | `ModContentService` + `ModSearchQueryResolver` | ✅ |
| 15 | 资源包 / 光影 / 存档管理 | `/api/instances/{id}/resourcepacks`、`shaderpacks`（`ShaderPackService`）、`saves/{folder}/{backup|restore|delete|open|backups}` | ✅ |
| 16 | 服务器收藏与状态 Ping | `/api/servers`、`POST /api/servers/{id}/ping` | ✅ |
| 17 | Java 自动检测 + 自动安装 + 内存智能推荐 | `JavaLocator`、`JavaRuntimeService`（Adoptium）、`MemoryAllocationService` | ✅（自动安装已实测 144.5 MB） |
| 18 | 日志落盘 / 崩溃分析 / 诊断包导出 / 自更新框架 / 双语界面 | `CrashAnalyzerService`、`DiagnosticReportService`、`UpdateService`、`ui/js/i18n.js` | ✅（更新链第 15 轮实测 PASS） |
| 19 | 「本地 `LocalApiService`(127.0.0.1:48123)」 | `LocalApiService.cs:24 private const int DefaultPort = 48123` | ✅ 与文档一致 |

（上表 19 行，前文说 15 条是拆分口径不同；结论不变。）

---

## 2. OptiFine：README 与 UI 都把它当受支持加载器，但**没有任何安装实现**

全树（`dev/**`，排除 `bin`/`obj`）对 `optifine` 的**全部**命中只有 4 处：

| 位置 | 内容 | 提供安装能力？ |
| --- | --- | --- |
| `Pineglow.Launcher.Models\ModLoaderKind.cs:9` | 枚举里的 `OptiFine` 成员 | ❌ |
| `Pineglow.Launcher.Models\ModLoaderInfo.cs:13,18,20,31,33` | 能**识别/显示** "OptiFine" 这个名字 | ❌ |
| `Pineglow.Launcher.Services\CrashAnalyzerService.cs:36` | 崩溃建议文案「尝试移除光影、OptiFine 或图形优化 Mod」 | ❌ |
| `Pineglow.Launcher.Services\MemoryAllocationService.cs:90` | 内存推荐里 `case "optifine":` 一个分支 | ❌ |
| `Pineglow.Launcher.Services\ModSearchQueryResolver.cs:29` | 搜索别名「高清修复」→ `optifine` | ❌ |
| `Pineglow.Launcher.Services\InstancePackService.cs:669` | CurseForge 加载器解析里**显式拒绝**：`kind == ModLoaderKind.OptiFine` → 抛「不支持的 CurseForge 加载器」 | ❌（反向） |

而真正的安装分派 `InstancePackService.cs:475-507` 是这样写的：

```csharp
return kind switch
{
    ModLoaderKind.Fabric   => await new FabricService(...).InstallFabricProfileAsync(...),
    ModLoaderKind.Quilt    => await new QuiltService(...).InstallQuiltProfileAsync(...),
    ModLoaderKind.Forge    => await new ForgeService(...).InstallProfileAsync(...),
    ModLoaderKind.NeoForge => await new NeoForgeService(...).InstallProfileAsync(...),
    _ => throw new InvalidOperationException($"不支持的加载器：{kind}"),
};
```

**四个分支 + 一个兜底抛错。`OptiFineService` 不存在。**

同时 UI 也把 OptiFine 当作一等加载器：

- `ui/js/launcher.js:34`：`const LOADER_COLORS = { Fabric: …, Forge: …, NeoForge: …, OptiFine: '#b45309', Quilt: …, Vanilla: … };` —— 加载器徽章配色里有 OptiFine
- `README.md:11`：「Forge / Fabric / NeoForge / Quilt / **OptiFine**」

### 2.1 准确的定性

- 这**不是静默失败**：真遇到 OptiFine 整合包，代码会明确抛「不支持的加载器：OptiFine」，CurseForge 路径也会明确拒绝。**行为是诚实的。**
- 问题是**对外承诺**：README 与 UI 都让用户以为能装 OptiFine，而实际不能；`ModLoaderKind`、`MemoryAllocationService` 里的 OptiFine 分支说明这是**计划过但没落地**的功能。
- 严重度：**低到中**（不影响已有功能，影响用户预期与文档诚信），与第 7 条（可恢复任务队列）**同一类**。

### 2.2 两种修法（都很小）

- **改文档/UI**（推荐先做）：README 去掉 OptiFine，或标注「仅识别、不支持安装」；`LOADER_COLORS` 里那条保留无害（它只是配色表）但不要在任何「可安装加载器」列表里出现 OptiFine。
- 或**补实现**：接 `LoaderInstallHelper` 走 OptiFine 官方安装器（`java -jar OptiFine_x.y.z.jar`），并复用第 2 条缺陷的代理修复——工作量明显更大，且 OptiFine 的安装器是交互式 GUI，自动化难度高。

---

## 3. 结论：README 的准确率

19 条断言里 **17 条有实现支撑**，2 条没有：
- 「可恢复任务队列」（第 11 轮发现，`AppData.DownloadQueue` 零引用）
- 「OptiFine」（本轮发现，无安装实现）

两条都是**同一类问题：对外承诺 > 实际实现**。
对「距离发布」的意义：**不构成技术阻断**，但发布前应当二选一——要么补齐，要么把文档改成实话。尤其是 OptiFine：一份写着支持五种加载器、实际只支持四种的 README，会在用户第一次尝试时直接变成一条差评。

状态串：`TechnicalAcceptance=NOT_PASSED`（发布整体）；`FormalPhaseApproval=NOT_GRANTED`；`ReleaseApproval=NOT_GRANTED`。
