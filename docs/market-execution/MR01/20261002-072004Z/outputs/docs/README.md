# 松光启动器 · 项目知识库

> 本文档是项目的**唯一可信知识源**，**事实基线 2026-10-02（MR01 刷新）**。
> 涉及开发 / 设计 / 发布 / 排障任务时，先读本文档，再按需进入子文档。
> 历史过时文档已归档至 `docs/archive/`，仅供可追溯参考，不作事实依据。

## 一、项目一句话

**Pineglow Launcher（松光启动器）** —— Minecraft 启动器，**纯 Web UI 架构**：Avalonia / .NET 10 仅作 WebView2 宿主，界面由 `ui/` 下的 HTML/CSS/JS 实现，通过本地 `LocalApiService`（HttpListener，127.0.0.1:48123）与 C# 服务层（**76 个 `.cs`**，2026-10-02 实测）通信。
⚠️ **本项目不是 Tauri**（全仓对 `tauri` 零命中），不要套用 Tauri 生态方案。

## 二、知识库导航

| 文档 | 内容 | 适用场景 |
|---|---|---|
| [architecture.md](architecture.md) | 纯 Web UI 架构、三层职责、WebView 加载、**路由门控陷阱** | 理解整体架构 / 排查接口 404 |
| [api-reference.md](api-reference.md) | LocalApiService 完整 API 协议 + 路由实现逻辑 | 前端联调 / 新增接口 |
| [features.md](features.md) | 功能矩阵（9 视图）+ 能力清单 + **状态分级 + 已知缺陷** | 功能盘点 / 验收 |
| [design-system.md](design-system.md) | 设计 Token、对比度/动效红线、品牌母题 | UI 开发 / 视觉走查 |
| [engineering.md](engineering.md) | 服务层索引 + LocalApiService 实现逻辑深析 | 后端开发 / 排障 |
| [roadmap.md](roadmap.md) | 竞品对标、时间线对照、差距现状、后续方向（**含 MR00~MR27 映射**） | 规划 / 决策 |
| [ai-diagnosis-plan.md](ai-diagnosis-plan.md) | AI 崩溃诊断功能的完整执行计划（代码级） | 实现 AI 诊断 |
| [ai-feature-evolution.md](ai-feature-evolution.md) | AI 功能演进记录（原 .trae 三份规划整理 + 多提供商预设现状） | 理解 AI 功能来龙去脉 / 维护 AI 配置 |
| [development-guide.md](development-guide.md) | 构建 / 缓存 / 测试 / 操作纪律 / **源码快照与可复现性验证** | 日常开发 |
| [market-research-task-workflow-2026-10-02.md](market-research-task-workflow-2026-10-02.md) | 市场调研导出的完整任务流程（28 个任务包 MR00~MR27） | 排期 / 任务授权 |
| [minimax-market-research-execution-prompt-2026-10-02.md](minimax-market-research-execution-prompt-2026-10-02.md) | 给执行 AI 的第一批指令（MR00+MR01）与后续任务包规则 | 委派执行 |

## 三、关键事实速查（已核实）

- **仓库**：`https://github.com/zhangtanze1359/Pineglow-Launcher.git`，分支 `main`，LICENSE=GPL-3.0。
- **CI**：`.github/workflows/build.yml`（三平台构建）+ `release.yml`（发布 + Gitee 镜像）。
  ⚠️ **CI 至今未实际执行过**（未推送、未建 PR）；三平台产物 ≠ 跨平台正式支持。
- **技术栈**：Avalonia 12 + Web UI + .NET 10（`global.json` 锁定 SDK 10.0.301）。**不是 Tauri。**
- **前端视图**：9 个（home / installations / worlds / servers / content / skins / downloads / news / settings）。
- **API**：`LocalApiService` 内 61 个 `Handle*Async` handler；仅 `127.0.0.1` 回环监听。
- **加载器**：现有 **5 种**（Fabric / Forge / NeoForge / Quilt / OptiFine），见 `Models/ModLoaderKind.cs`。
  Cleanroom / Legacy Fabric / LiteLoader **不在枚举中**，是未实施候选项。
- **崩溃分析**：`CrashAnalyzerService` 为纯规则引擎，已通过 `instances/{id}/diagnose` 暴露 + AI 增强（见 ai-diagnosis-plan.md）。
- **配置**：`AppConfig`，密钥字段（CurseForgeKey、Microsoft tokens）用 `[JsonIgnore]` 不掉盘。
- **前端双模式**：`api.js` 的 `live` / `_mock` 双通道，`file://` 下走 mock 降级。
- **可复现性**：工作区 HEAD 之上有大量未提交改动且依赖未跟踪生产源；用
  `scripts/verify-source-snapshot.ps1` + `scripts/source-snapshot-inputs.txt` 组装隔离快照复验。

### 🔴 当前最高优先级缺陷（2026-10-02 发现）

`/api/instances/{id}/bisect/{start,test,giveup}` 与 `/api/instances/{id}/mods/{file}/{toggle,delete}`
**路由不可达、恒返回 404**——分支被错误嵌套在 `seg.Length == 4` 的块内。
UI 已接线、服务层已实现、Bisect 有 12 个单测，但**端到端不可用**；`VerifyHost.exe --bisect-smoke` 实测 `exit=1`。
详见 [features.md](features.md) §1.1。归属 `MR04` / `MR06`。

### 2026-10-02 实测基线

Node 前端正式测试 **184 通过 / 0 失败 / 0 跳过**；C# Release 全套 **612 通过 / 0 失败 / 0 跳过**；
win-x64 单文件发布成功（45,644,552 字节，SHA256 `3B71DCC1…`）；**RID 发布锁定为 KNOWN-UNLOCKED（NU1004）**。
**真实 GUI / 真实游戏 / CI 实际运行均未验证。**

## 四、维护规约（必读）

1. **文档是唯一可信源**：改动代码 / 功能后，同步更新本文档对应章节，避免再次产生过时文档。
2. **新增 API**：在 `api-reference.md` 补端点，并遵守后端 JSON 大小写约定。
   **⚠️ 要求更长 `seg.Length` 的路由分支绝不能嵌套在要求更短 `seg.Length` 的分支内**；
   改完必须有打到 HTTP 层的测试——只测服务层发现不了路由不可达。
3. **新增功能**：更新 `features.md` 状态矩阵 + `roadmap.md` 差距清单。
4. **状态词必须分级**：`代码存在` / `自动验证` / `真实场景验证` / `未验证` / `不支持`。
   **一个绿色勾不能覆盖所有层级**；旧文档用 `✅` 同时表示"写了"和"能用"是主要歧义来源。
5. **否定性结论要交叉核对**："某功能不存在""某竞品没有"必须两侧材料齐备，
   否则写"本次材料未证实"。竞品能力未发现 ≠ 不存在。
6. **涉及 UI 设计**：遵守 `design-system.md` 红线，禁止硬编码色值。
7. **历史文档**：一律进 `docs/archive/`，不留在根目录与正文混淆。

---

*知识库 2026-08-16 建立；2026-10-02 由 MR01 刷新功能事实基线、状态分级与已知缺陷。*
