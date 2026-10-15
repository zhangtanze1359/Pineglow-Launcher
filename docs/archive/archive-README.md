# 归档文档说明

> 本目录存放**已过时、相互矛盾**的历史文档，仅作**历史参考与可追溯性**保留。
> 正文不再更新。项目**唯一可信知识源**请以 `docs/` 根目录下的知识库正文文件为准。

## 归档原因

自纯 Web UI 架构决策（2026-08-09）与 8/10 大规模功能补齐之后，早期文档中的大量结论已失效。保留它们是为了：
- 追忆项目演进路径与决策过程；
- 保留竞品对标、设计方法论等**仍有参考价值**的部分；
- 避免"删除历史"导致的信息断层。

## 归档清单与取代关系

| 归档文档 | 历史定位 | 现已被取代 |
|---|---|---|
| `phase2-content-icons-implementation-2026-09-04.round1.bak.md` | Phase 2 第一轮最终返修报告原样备份（ContentIconServiceTests 104 / .NET 240，WebView 冒烟未执行）——历史轮次记录，非最终结果 | `docs/phase2-content-icons-implementation-2026-09-04.md`（最终放行版） |
| `phase2-content-icons-implementation-2026-09-04.round2.bak.md` | Phase 2 第二轮最终返修报告原样备份（含实施基线与旧结论：190 / 326 / 前端 88、断网未执行、MIME 415 降级、"尚未验收"）——历史轮次记录，非最终结果 | `docs/phase2-content-icons-implementation-2026-09-04.md`（最终放行版） |
| `松光启动器-纯WebUI架构说明.md` | 声称"唯一真源"的架构说明，但已过时（仍写"无 git 仓库/LICENSE/CI"，实际已存在） | `docs/architecture.md`、`docs/api-reference.md`、`docs/development-guide.md` |
| `松光启动器技术对比文档.md` | v1.0.0 逆向审计 + 竞品对标（P0~P3 差距） | 差距现状 → `docs/roadmap.md`；竞品对标 → `docs/roadmap.md` |
| `松光启动器UI-UX设计文档.md` | 设计系统与视觉规范（基于已废弃 Avalonia 技术方案） | 有效设计规范 → `docs/design-system.md` |
| `松光启动器开发经验总结.md` | 开发方法论与踩坑记录（XAML/MVVM 路线已废弃） | 工程认知 → `docs/engineering.md`、`docs/roadmap.md` |

## 使用规约

- **不要**从归档文档引用结论作为当前事实依据。
- 需要历史上下文时，可阅读归档文档，但**以正文知识库为准**。
- 若某归档内容仍有价值且未纳入正文，请将其提炼进对应正文文件后，再在归档文档顶部标注"已提炼至 X"。