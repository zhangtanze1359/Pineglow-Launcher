# 松光启动器 · 项目知识库

> 本文档是项目的**唯一可信知识源**，反映 2026-08-16 的最新状态。
> 涉及开发 / 设计 / 发布 / 排障任务时，先读本文档，再按需进入子文档。
> 历史过时文档已归档至 `docs/archive/`，仅供可追溯参考，不作事实依据。

## 一、项目一句话

**Pineglow Launcher（松光启动器）** —— 跨平台 Minecraft 启动器，**纯 Web UI 架构**：Avalonia 仅作 WebView2 宿主，界面由 `ui/` 下的 HTML/CSS/JS 实现，通过本地 `LocalApiService`（HttpListener，127.0.0.1:48123）与 C# 服务层（约 67 个服务）通信。

## 二、知识库导航

| 文档 | 内容 | 适用场景 |
|---|---|---|
| [architecture.md](architecture.md) | 纯 Web UI 架构、三层职责、WebView 加载 | 理解整体架构 |
| [api-reference.md](api-reference.md) | LocalApiService 完整 API 协议 + 路由实现逻辑 | 前端联调 / 新增接口 |
| [features.md](features.md) | 功能矩阵（9 视图真实状态）+ 能力清单 | 功能盘点 / 验收 |
| [design-system.md](design-system.md) | 设计 Token、对比度/动效红线、品牌母题 | UI 开发 / 视觉走查 |
| [engineering.md](engineering.md) | 服务层索引 + LocalApiService 实现逻辑深析 | 后端开发 / 排障 |
| [roadmap.md](roadmap.md) | 竞品对标、时间线对照、差距现状、后续方向 | 规划 / 决策 |
| [ai-diagnosis-plan.md](ai-diagnosis-plan.md) | AI 崩溃诊断功能的完整执行计划（代码级） | 实现 AI 诊断 |
| [ai-feature-evolution.md](ai-feature-evolution.md) | AI 功能演进记录（原 .trae 三份规划整理 + 多提供商预设现状） | 理解 AI 功能来龙去脉 / 维护 AI 配置 |
| [development-guide.md](development-guide.md) | 构建 / 缓存 / 测试 / 操作纪律 | 日常开发 |

## 三、关键事实速查（已核实）

- **仓库**：`https://github.com/zhangtanze1359/Pineglow-Launcher.git`，分支 `main`，LICENSE=GPL-3.0。
- **CI**：`.github/workflows/build.yml`（三平台构建）+ `release.yml`（发布 + Gitee 镜像）。
- **前端视图**：9 个（home / installations / worlds / servers / content / skins / downloads / news / settings）。
- **API**：约 50 处路由，覆盖 health / state / instances / accounts / versions / downloads / update / config / worlds / content / java / stats / sources / servers（含 `instances/{id}/diagnose` AI 诊断）。
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
