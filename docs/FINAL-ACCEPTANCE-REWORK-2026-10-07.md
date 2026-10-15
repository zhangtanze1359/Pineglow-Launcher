# 客户端识别边界返工报告（2026-10-07）：RED → GREEN，六类真实启动全过

依据：`harness-independent-acceptance-review-2026-10-07.md`（P2 阻断）+ `harness-independent-acceptance-remediation-workflow-2026-10-07.md` + `minimax-harness-independent-acceptance-rework-prompt-2026-10-07.md`。
**未提交/未推送/未打 tag/未发布**；未 reset/restore/checkout/clean/stash；未批量暂存；未改用户配置/凭据/历史证据/缓存 goal；只清理经路径确认属于自有 GUID 根的进程。

## 1. 基线（现场核对）

| 项 | 值 |
| --- | --- |
| HEAD | `f145d14`（未变）|
| 脏状态 | **47 tracked modified / 0 staged** |
| 返工前 `LauncherService.cs` | `6cc9ac01533300c1e76686d3bd13049a85c3d1a99c3eb0b1772d991f9f704ec5` |
| 返工后 `LauncherService.cs` | **`4a3f4168f19f9e420e26fcd2edbe8753795eaf8cba75b6e3060d1c0c9a7b3b78`** |
| `LoaderLaunchAssemblyTests.cs` | `d341930a502ccb1e1033359afebf36a6afb04c546fd3095dc152eec68a846340` |
| 未改动的已通过模块 | `LocalApiService.cs`（Java 选择）、下载器、账号、UI、凭据 —— **均未触碰** |

## 2. 先构造能推翻错误实现的 RED（真实 `BuildCommandAsync`，隔离副本）

新增/重写 `LoaderLaunchAssemblyTests.cs` 中的类路径装配夹具（与独立验收 `BoundaryProbe.cs` 同形：父版本 1.21.1 客户端 jar 存在、child 为 NeoForge 风格并声明 `client-extra`、当前替代客户端缺失）：

| 用例 | 断言 | 返工前 |
| --- | --- | --- |
| `BuildCommandAsync_UnrelatedExtraJar_DoesNotChangeVanillaClientRetention` | 加入 `libraries/net/minecraftforge/unrelated-tool/1.0/unrelated-tool-extra.jar` 前后都必须保留原版 jar | **FAIL**（被移出）|
| `BuildCommandAsync_StaleClientExtraJar_DoesNotChangeVanillaClientRetention` | 库里只有 `net/minecraft/client/1.20.1/client-1.20.1-extra.jar`（当前是 1.21.1）时仍须保留 | **FAIL**（被移出）|
| `BuildCommandAsync_ResourceOnlyExtraJar_IsNotAcceptedAsCurrentClient` | 只有资源、0 个 `.class` 的 extra jar 不构成类供应链 ⇒ 保留 | **FAIL**（被移出）|
| `BuildCommandAsync_DeclaredClientJarWithoutClasses_ThrowsExplicitError` | 声明的 `*-client.jar` 存在但无类文件 ⇒ 明确报错 | **FAIL**（静默生成错误 classpath）|
| 其余 6 项（重复 JVM 选项、占位符、当前 patched client 正向、Vanilla/Fabric 保留等）| — | PASS（守卫）|

**RED 记录**：`失败 4 / 通过 6 / 总计 10`，退出码 **1**（`red-boundary.log`）。

## 3. 最小修复：把"替代客户端识别"绑定到当前安装元数据 + 类供应链

只改 `LauncherService.cs`（`SuppliesOwnClient`/`IsClientJarName` → `ResolveClientSupply` + 辅助）：

1. **声明**：版本 JSON 的 `-DignoreList=` 含 `client-extra` 才进入后续判定（否则 `NotDeclared` ⇒ 保留原版 jar，Vanilla/Fabric/Quilt 原路）；
2. **绑定当前版本/加载器**：从**当前版本 id**（或版本 JSON 里加载器自身的 library 条目）解析产物目录与文件名前缀，只接受同名同版本的 `*-client.jar`：
   - `<mc>-forge-<ver>` → `libraries/net/minecraftforge/forge/<mc>-<ver>/forge-<mc>-<ver>-client.jar`
   - `neoforge-<ver>` / `<mc>-neoforge-<ver>` → `libraries/net/neoforged/neoforge/<ver>/neoforge-<ver>-client.jar`
   **不再递归扫描任意目录、不再按 `*-extra.jar` 后缀肯定**；
3. **类供应链验证**：解析到的 jar 必须**确实含 `.class` 条目**（只读 ZIP 中央目录）。实测真实产物：
   `forge-1.20.1-47.4.3-client.jar` **1438/1438 个 .class**、`neoforge-21.1.230-client.jar` **1601/1601 个 .class**；
   而 `client-*-extra.jar` 是 **13517 / 15250 条目、0 个 .class** 的资源 jar（与独立验收的证据一致）；
4. **契约**：`Provided` ⇒ 原版 jar 移出 `-cp`；`Missing` ⇒ **合法回退保留原版 jar** 并写日志；`Unusable`（存在但无类）⇒ **装配前明确拒绝**（`当前加载器客户端产物不可用…`）；不静默、不自动下载、不改用户配置。

## 4. GREEN 与门槛

| 项 | 结果 |
| --- | --- |
| 聚焦测试（`LoaderLaunchAssemblyTests` + `JavaSelectionMetadataTests`）| **14/14 通过，0 失败，0 跳过**（`green-boundary.log`）|
| 全量 C# | **931/931，0 失败，0 跳过**（917 旧 + 14 聚焦；`green-full-csharp.log`）|
| 构建 | 0 警告 0 错误 |
| 统一门槛（新 GUID 快照 `pineglow-snapshot-2e4c90d4…`）| **模式 GREEN / 失败项 0**：白名单 151、输入 **312/312**、Node **305/305**、C# **931/931**、UI 闭包、非 RID locked restore、Release build、VerifyHost、普通 win-x64 publish、**单文件 EXE 独立冷启动 19/19** |
| 已知未通过边界 | RID locked restore **NU1004**（如实保留，未移出报表）|
| **新单文件 EXE** | 53,052,722 B，**sha256 `342e140f4a0544e222a0c68f879e73ff0fa71f30967ca5990e1ea372d60302b1`** |

## 5. 六类真实启动取证（同一 EXE，自有离线副本，清空副本内手动 Java 路径）

| 实例 | 加载器 / 版本 | 自动选用 Java | 命令行 | 占位符残留 | 新日志行数 | LWJGL/图集 | 崩溃 | 观察期存活 |
| --- | --- | --- | ---: | ---: | ---: | --- | ---: | --- |
| Optimized fps 1.20.1 导入 | Forge / `1.20.1-forge-47.4.3` | **17** | 19,999 | 0 | 193 | 1 / **12** | 0 | 存活 |
| Optimized fps 1.21.1 导入 | NeoForge / `neoforge-21.1.230` | **21** | 22,154 | 0 | 327 | 1 / **15** | 0 | 存活 |
| fx-vanilla | Vanilla / `1.18.2` | **17** | 5,808 | 0 | 32 | 1 / **10** | 0 | 存活 |
| rg-vanilla | Vanilla / `1.18.2` | **17** | 5,808 | 0 | 31 | 1 / **10** | 0 | 存活 |
| Fabulously Optimized 导入 | Fabric / `fabric-loader-0.19.3-1.21.1` | **21** | 15,772 | 0 | 374 | 1 / **14** | 0 | 存活 |
| ukus-pvp-modpack 导入 | Quilt / `quilt-loader-0.21.0-1.20.1` | **17** | 14,993 | 0 | 634 | 1 / **12** | 0 | 存活 |

判定：**PASS**（`six-launches.json`，脚本成功显式 **exit 0**）。取证脚本：删旧日志 + 只采信 `mtime > 启动时刻` 的日志。

## 6. 边界（不夸大）

- 本轮只验证"**进入渲染初始化（LWJGL/图集）+ 观察期存活（40 秒）**"；**未**验证进入世界、长时间游玩、视觉验收主界面。
- **未**验证全新在线安装与 **Quilt 1.21+**。
- RID locked restore NU1004、微软账号链路、AI 第三方调用、Gitee `update.json`/token、正式发布授权：**均未在本轮获得 PASS**，分别 BLOCKED / NOT_RUN。
- 既有产品范围未逐行审计的声明继续保留。

## 7. 证据根

`docs/release-remediation-evidence-2026-10-05/run-20261007-5200-client-boundary-rework/`：
`rework-baseline-sha256.json`、`red-boundary.log`（RED，exit 1）、`green-boundary.log`（14/14）、`green-full-csharp.log`（931/931）、
`after-rework-sha256.json`、`final/`（统一门槛 console 与逐步日志）、`six-launch-step.log`、`six-launches.json`、六个 `*-cmdline.txt` 与 `*-game.log`。
原独立验收证据（保留未覆盖）：`C:\Users\Administrator\.codex\worktrees\e423\松光启动器\docs\harness-independent-acceptance-evidence-2026-10-07`。

## 8. 终态

**具备独立复验条件，等待 Codex 独立验收。** 本报告不构成实现或发布授权。

---

## 9. 追加：用**独立验收自己的探针**复跑（最强自证）

把独立验收证据里的 `BoundaryProbe.cs`（**未做任何改动**）编译并链接到**返工后**的产品 DLL，用同样的两个反例复跑：

```
"failures": 0,
"results": [
  { "name": "unrelated-extra", "retainedBefore": true, "retainedAfter": true, "pass": true },
  { "name": "stale-client",    "retainedBefore": true, "retainedAfter": true, "pass": true }
]
探针 exitCode = 0
```

⇒ 独立验收当初记录的 **P2 阻断（`failures: 2`、probe exit 1）在返工后变为 `failures: 0`、probe exit 0**，且两个反例的
`beforeClasspath` 与 `afterClasspath` **完全相同**（无关/旧版文件不再改变原版 jar 的保留状态）。
证据：`boundary-recheck-results.json`（本目录）。