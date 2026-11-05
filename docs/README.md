# 松光启动器 · 项目知识库

> **最后核对日：2026-11-06**（视图数、服务数、端点表、安全机制均按该日源码实测重算）。
> 本文档是项目知识库的**入口与目录**。涉及开发 / 设计 / 发布 / 排障任务时，先读本文档定位子文档，再以子文档正文为事实依据。
> **可信度约定**：每份子文档头部标注「最后核对日」，正文结论仅在该日期对应当前源码成立；没有核对日的旧文当作历史材料，不作事实依据。
> 历史过时文档已归档至 `docs/archive/` 与 `docs/market-execution/`，仅供可追溯参考。

## 〇、事实基线分叉的处置结论（2026-11-06）

`docs/market-execution/MR01/20261002-072004Z/outputs/docs/` 下有一份 2026-10-02 的文档快照，曾把服务数刷到「76 个 `.cs` 实测」，但**从未合回根 `docs/`**——于是根文档长期停在「约 67 个服务」，而磁盘实际早已增长。

**处置决定：合回 + 降级 MR01 为按日期归档。**

- 根 `docs/` 继续作为唯一权威目录，本次已把易漂移的计数（视图 / 服务 / 端点）改为按核对日实测口径，并加「最后核对日」。
- `docs/market-execution/MR01/...` 定位改为**按日期命名的历史快照**，不再与根文档竞争「谁更权威」；其中与当前源码冲突的数字（76 服务、9 视图）不回填。
- 判断规则：**根文档有核对日且晚于 MR01 的，以根文档为准；MR01 仅用于追溯当时的验收记录。**
- 遗留：服务数这类「数文件就能得」的指标仍会漂移，后续应改为在 CI 中生成并比对（见 M2 阶段测试缺口决策）。

## 一、项目一句话

**Pineglow Launcher（松光启动器）** —— Minecraft 启动器，**纯 Web UI 架构**：Avalonia 仅作 WebView2 宿主，界面由 `ui/` 下的 HTML/CSS/JS 实现，通过本地 `LocalApiService`（HttpListener，127.0.0.1:48123）与 C# 服务层（**85 个 `.cs`**，2026-11-06 磁盘实测）通信。

## 二、知识库导航

| 文档 | 内容 | 适用场景 |
|---|---|---|
| [architecture.md](architecture.md) | 纯 Web UI 架构、三层职责、WebView 加载 | 理解整体架构 |
| [api-reference.md](api-reference.md) | LocalApiService 完整 API 协议 + 路由实现逻辑 | 前端联调 / 新增接口 |
| [features.md](features.md) | 功能矩阵（10 视图真实状态）+ 能力清单 | 功能盘点 / 验收 |
| [design-system.md](design-system.md) | 设计 Token、对比度/动效红线、品牌母题 | UI 开发 / 视觉走查 |
| [engineering.md](engineering.md) | 服务层索引 + LocalApiService 实现逻辑深析 | 后端开发 / 排障 |
| [roadmap.md](roadmap.md) | 竞品对标、时间线对照、差距现状、后续方向 | 规划 / 决策 |
| [ai-diagnosis-plan.md](ai-diagnosis-plan.md) | AI 崩溃诊断功能的完整执行计划（代码级） | 实现 AI 诊断 |
| [ai-feature-evolution.md](ai-feature-evolution.md) | AI 功能演进记录（原 .trae 三份规划整理 + 多提供商预设现状） | 理解 AI 功能来龙去脉 / 维护 AI 配置 |
| [development-guide.md](development-guide.md) | 构建 / 缓存 / 测试 / 操作纪律 | 日常开发 |

## 三、关键事实速查（2026-11-06 核实）

- **仓库**：`https://github.com/zhangtanze1359/Pineglow-Launcher.git`，分支 `main`，LICENSE=GPL-3.0。
- **远端实况**：远端 `main` = `30f7b56`（v1.0.3 源码，2026-11-03）；已发布 Release 两个：`v1.0.3`（3 个附件）与 `v1.0.0`（2 个附件）；`v1.0.1` / `v1.0.2` 只有 tag，**无 Release**（v1.0.1 发布失败、v1.0.2 被取消）。
- **版本号**：`dev/PineglowLauncher/Pineglow.Launcher.csproj` 的 `<Version>` 是程序集、文件与发布产物的**唯一来源**（远端为 `1.0.3`）。本地主工作区 HEAD `47e939c` 的 csproj 仍是 `1.0.0`，属**落后于远端的旧状态**，不要当作当前版本。
- **CI**：`.github/workflows/build.yml`（三平台构建 + Linux 测试）+ `release.yml`（发布 + Gitee 镜像）。最近一次 `main` 构建（run 41599393945）Windows/macOS 通过，**Ubuntu 在 "Run tests" 步骤失败**；`Release v1.0.3` 工作流为 success。
- **前端视图**：**10 个**（home / installations / worlds / servers / content / skins / downloads / news / settings / playtime）。
- **API**：端点表 59 条（路径口径），源码 43 个路由段字面量分支；覆盖 health / state / instances / accounts / versions / downloads / update / config / worlds / content / java / stats / sources / servers（含 `instances/{id}/diagnose` AI 诊断）。
- **API 安全边界**：每请求 `X-Pineglow-Token`（进程内 32 字节随机令牌，`FixedTimeEquals` 比较）+ Origin 回环同端口白名单 + AI 出站固定 allowlist + `PathSafetyGuard` 路径越界校验。**不是** CORS `*` 全放开。详见 [api-reference.md](api-reference.md) 1.1。
- **崩溃分析**：`CrashAnalyzerService` 为纯规则引擎，已通过 `instances/{id}/diagnose` 暴露 + AI 增强（见 ai-diagnosis-plan.md）。
- **配置**：`AppConfig`，密钥字段（CurseForgeKey、Microsoft tokens）用 `[JsonIgnore]` 不掉盘。
- **前端双模式**：`api.js` 的 `live` / `_mock` 双通道，`file://` 下走 mock 降级。

## 四、维护规约（必读）

1. **文档是唯一可信源**：改动代码 / 功能后，同步更新本文档对应章节，避免再次产生过时文档。
2. **新增 API**：在 `api-reference.md` 补端点，并遵守后端 JSON 大小写约定。
3. **新增功能**：更新 `features.md` 状态矩阵 + `roadmap.md` 差距清单。
4. **涉及 UI 设计**：遵守 `design-system.md` 红线，禁止硬编码色值。
5. **历史文档**：一律进 `docs/archive/`，不留在根目录与正文混淆。

---

*知识库 2026-08-16 建立，整合原有 5 份文档并纳入最新分析结论。*
