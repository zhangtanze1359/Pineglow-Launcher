# 差距分析 · 竞品对照 · 后续方向

> 反映 2026-08-16 状态。整合旧《技术对比文档》的竞品对标参考 + 2026-08-16 对话产出的时间线对照与 AI 诊断方案。
>
> ⚠️ **2026-10-01 更新**：第二节竞品对照表与第三节对标矩阵的**加载器、跨平台、Mod 可逆删除**三处已按一手材料（HMCL 官方 `docs/README_zh_Hans.md`、PCL2 官网更新日志）核实修正。**第一、五节的时间线与 AI 诊断方案仍反映 2026-08 状态**；且工作区自 2026-08-18 起有大量未提交改动未反映进本文件——使用前请先核对当前代码状态。

## 一、松光启动器时间线（2026）

| 时间 | 节点 | 说明 |
|---|---|---|
| 7/17 | v1.0.0 单体发布 | 105MB 全代码化 UI 单体 exe，逆向审计对标竞品，产出 P0~P3 差距清单 |
| 8/5~8/9 | HTML 原型 v2→v5 | 低成本验证视觉方向；**8/9 拍板纯 Web UI 架构** |
| 8/10 | **一天爆发补齐** | 打通真实后端 + 补齐 P0~P2：多源测速、免Key CF、皮肤站预设、游玩统计、新闻页、拖拽导入、启动前检查、代理、i18n、深色 token、测试、CI、自更新 |
| 8/11 | Gitee 镜像收尾 | mirror-gitee.mjs + CI 发布联动 |
| 8/16 | 当前状态 | 知识库建立 |

> 关键：8/10 一天补齐了旧技术对比文档里几乎全部 P0~P2 差距——服务层早已写好，只是接通 API 桥 + 写盘。

## 二、竞品 2026 进展对照

> 竞品数据于 **2026-10-01** 按一手材料核实并更新。核实来源：HMCL 官方 README（`docs/README_zh_Hans.md`，注意**不在仓库根目录**）、PCL2 官网更新日志、Prism 官方 README、Modrinth 官方仓库。

| 竞品 | 2026 动态（一手核实） | 与松光的关系 |
|---|---|---|
| **HMCL** | 加载器覆盖 **Forge、NeoForge、Cleanroom、Fabric、Legacy Fabric、Quilt、LiteLoader、OptiFine**（8 种）；跨平台支持 **Windows / Linux / macOS / FreeBSD** + **x86 / ARM / RISC-V / MIPS / LoongArch**（4 OS × 5 CPU 架构）；130+ 贡献者；HMCL-AE 分支仍为 AI 助手试验阶段 | ① **松光缺 Cleanroom 与 Legacy Fabric 两种加载器**，属生态硬缺口；② **HMCL 跨平台成熟度显著高于松光**（松光自陈「仅 Windows 验证」）；③ HMCL 至今**无产品级 AI 崩溃诊断**，松光该项仍领先 |
| **PCL2** | 当前 **2.13.1.1（2026-08-06）**，510 万+ 下载，7.1K Stars；可逆 Mod 操作（安装同名 Mod 时旧 Mod **删除至回收站**，#9032）；删除版本前**提醒关联存档**（#6121）；Java 版本冲突时**警告并继续启动**而非直接失败；**已因「Quilt 半身入土」删除 Quilt 支持**；崩溃识别长期积累（#7846） | ① 「可逆删除」与「删除前存档保护」是松光可低成本借鉴的两点；② Quilt 取向已分歧——**松光仍重点支持 Quilt，需重估投入产出** |
| **Prism** | 10,544 stars，2026-09-30 仍有提交；**Mod Bisect 二分定位**；UI/UX 现代化（设计 token、卡片视图、主题）；官方提供 Linux/Windows/macOS 预构建 + Flatpak nightly | Mod Bisect 松光已实现并反超；**跨平台预构建分发**是松光可对标点 |
| **Modrinth App** | 共享实例、内容披露（AI 政策）、实例日志页 | 内容生态官方客户端，偏服务器/实例协作；**共享实例协作仍是松光未涉足方向** |
| **Vortex**（新增参照） | 自动 load order、冲突解决、**Mod Profiles**、插件扩展生态（GPL-3.0） | 虽为不同品类（非 Minecraft），但其 Mod 管理三件套是「安装/卸载/配置档」完整闭环的标杆，可作为松光 Mod 管理深度的长期参照 |

## 三、竞品对标矩阵（现状）

对比对象：PCL2 / HMCL / Prism / Modrinth-CF。**加载器与跨平台两行于 2026-10-01 按一手材料修正**（此前遗漏 Cleanroom / Legacy Fabric，且低估 HMCL 跨平台成熟度）。

| 能力维度 | 松光 | PCL2 | HMCL | Prism | Modrinth/CF |
|---|:---:|:---:|:---:|:---:|:---:|
| 离线/微软/第三方登录 | ✅/✅/✅ | ✅/✅/✅ | ✅/✅/✅ | ✅/✅/✅ | ✅/✅/❌ |
| 版本下载/修复/校验 | ✅ | ✅ | ✅ | ✅ | ⚠️ |
| Forge/Fabric/NeoForge/Quilt/OptiFine | ✅ | ✅ | ✅ | ✅ | ⚠️ |
| **Cleanroom / Legacy Fabric** | ❌ **缺** | ⚠️ | ✅ **均有** | ✅ | ⚠️ |
| **正式跨平台（OS × CPU 架构）** | ❌ 仅 Windows 验证 | ❌ Windows | ✅ **4 OS × 5 架构** | ✅ 3 OS 预构建 | ❌ |
| CurseForge 整合包 | ✅ 免Key | ✅ | ✅ | ✅ | ✅ |
| 多下载源（镜像） | ✅ 多源+测速 | ✅ | ✅ | ❌ | ❌ |
| 深色模式/主题 | ✅ | ✅ | ✅ | ✅ | ✅ |
| **用户侧主题自定义** | ❌ 仅深色 | ✅ | ✅ | ✅ | ⚠️ |
| 多语言 (i18n) | ✅ 中英 | ✅ | ✅ | ✅ | ✅ |
| 自更新 | ✅ 双源 | ✅ | ✅ | ✅ | ✅ |
| 皮肤站预设 | ✅ | ✅ | ✅ | ✅ | ❌ |
| 代理设置 | ✅ | ✅ | ✅ | ✅ | ❌ |
| 崩溃分析 | ✅ 规则引擎 | ✅ | ✅ | ✅ | ❌ |
| **Mod 可逆删除（回收站）** | ❌ 直接删除 | ✅ | ✅ | ✅ | ✅ |
| **删除前提醒关联存档** | ❌ | ✅ | ⚠️ | ⚠️ | ⚠️ |
| 游玩统计 | ✅ | ✅ | ✅ | ❌ | ❌ |
| 拖拽导入 | ✅ | ✅ | ✅ | ✅ | ❌ |
| **AI 崩溃诊断** | ✅ 已实现（规则+AI 增强） | ❌ | ⚠️ AE分支试 | ❌ | ❌ |
| **Mod Bisect 二分定位** | ✅ 已实现（写盘启停+恢复） | ❌ | ❌ | ✅ | ❌ |

## 四、核心洞察

1. **技术在追平，方向在趋同**：8/10 补齐的每一项（多源、免Key、token 化、i18n、自更新）都是竞品仍打磨的方向，路线判断正确。
2. **竞品已进入新赛道**：HMCL 试 AI 助手、Prism 做 Mod Bisect、Modrinth 做共享实例协作——松光已跟进 AI 诊断与 Mod Bisect，共享实例协作仍未涉足。
3. **松光的真实优势**：工程底子（断点续传、凭据安全存储、可恢复任务、纯 Web UI 快速迭代）让它能"一天追平多年差距"。
4. **（2026-10-01 核实补充）AI 崩溃诊断的领先性仍成立**：HMCL-AE 分支至今仍是试验阶段，无任何竞品做出产品级的「崩溃日志 → AI 解释 + 修复步骤 + 一键禁用嫌疑 Mod」闭环。松光该项是真优势，值得继续投入。
5. **（2026-10-01 核实补充）两处新暴露的硬缺口**：
   - **加载器缺口**：HMCL 已覆盖 Cleanroom 与 Legacy Fabric，松光缺这两项，属生态兼容性硬伤，优先级应高于长期功能。
   - **生态取向需重估**：PCL2 已因「Quilt 半身入土」删除 Quilt 支持，而松光仍将其列为五大加载器之一。建议重新评估 Quilt 的投入产出。
6. **（2026-10-01 核实补充）可低成本借鉴的两个小改动**：PCL2 的「安装同名 Mod 时旧 Mod 删除至回收站而非永久删除」（#9032）与「删除版本前提醒即将被删除的存档」（#6121）都是低成本的**可逆性/防误删**设计，松光可快速对齐以提升用户信任。

## 五、后续方向：AI 崩溃诊断（建议优先）

### 结论：值得跟进，但聚焦"崩溃 AI 诊断"单场景，不照搬 HMCL-AE 全功能聊天

**为什么不做全功能聊天**：HMCL-AE 的 AI 全家桶（多模型、Markdown 渲染、会话、命令补全）在 JavaFX 上硬造，光 Markdown 组件就 3000+ 行。松光是纯 Web UI，这些**前端天生支持**，且数据源（崩溃分析、日志读取、Mod 健康检查）全是现成服务——做 AI 诊断，松光架构上比 HMCL 便宜一个量级。

**差异化机会**：主流启动器崩溃分析都是规则引擎（包括 HMCL 本体、Prism）。**目前没有一款把大模型接进崩溃诊断形成产品级体验**。松光做出"崩溃贴上去 → AI 自然语言解释 + 修复步骤 + 一键禁用嫌疑 Mod"就是第一个吃螃蟹的。

### 最小实现方案（M1~M4）

> 📄 **完整执行计划**：代码级改动（AppConfig/ConfigStore/AiDiagnoseService/LocalApiService/前端/i18n/验证）见 [ai-diagnosis-plan.md](ai-diagnosis-plan.md)。本节为方向概要。

**现状基准**：`CrashAnalyzerService` 是纯规则引擎。**已实现**：新增 `AiDiagnoseService`、API 端点 `POST /api/instances/{id}/diagnose`、前端弹窗与设置项（详情见 [ai-diagnosis-plan.md](ai-diagnosis-plan.md)）。方案复用 `CrashAnalyzerService` + `ModHealthService`，AI 作为"增强解释"串行输出——AI 不可用时规则结果仍在（优雅降级）。

| 步骤 | 内容 | 状态 |
|---|---|---|
| M1 | 抽公共 `ReadDiagnosticTail` + 建 `AiDiagnoseService`（仅规则降级） | ✅ 已实现 |
| M2 | 接 DeepSeek（OpenAI 兼容）+ prompt 组装 | ✅ 已实现 |
| M3 | API 路由 `POST /api/instances/{id}/diagnose` + 配置字段 | ✅ 已实现 |
| M4 | 前端 live/mock + 页面按钮 | ✅ 已实现 |
| M5 | 多提供商预设（AiProviders + SelectedAiProviderId，内置 5 模板，Ollama 免 Key） | ✅ 已实现
| M6 | 嫌疑 Mod 结构化输出 + 前端一键禁用（suspectMods + JSON 尾巴约定） | ✅ 已实现 |

> 📘 **演进记录**：单套配置 → 多提供商预设的完整决策与实现核对见 [ai-feature-evolution.md](ai-feature-evolution.md)。

**关键设计**：
- 服务层新增 `AiDiagnoseService`，`ComposePrompt` 组装崩溃日志尾部 + 规则预判 + Mod 健康报告，降低幻觉率。
- 配置：多提供商预设（`AiProviders` 列表 + `SelectedAiProviderId`），内置 DeepSeek/OpenAI/通义千问/智谱/Ollama 模板，可新增/编辑/删除/设为当前；每套 Key 独立走 SecretStore（`AiKey_{id}`），config.json 不入盘。
- API：手工路由加 `diagnose` action；异常返回 200 + `{ok:false,error}` 降级，不抛 500。
- 前端：live 加 `diagnoseInstance`，_mock 加同形状降级，页面加"AI 诊断"按钮。
- 隐私：崩溃日志含用户名/绝对路径，加"仅发送到所选模型"提示。

**不做**：多模型切换、会话历史、`/` 命令、RAG 知识库（HMCL-AE 的"待实现"清单，过度设计）。

### Mod Bisect 二分定位（对齐 Prism）

**已实现**：新增 `ModBisectService`（纯逻辑状态机：`Start`/`DisableHalfForNextTest`/`ReportTest`/`GiveUp`，候选收窄到 1 个自动定位元凶），`LocalApiService` 加 `/bisect/start|test|giveup` 路由（写盘禁用半区、结束/放弃自动恢复），前端实例菜单加「二分定位」入口与弹窗（每轮显示禁用列表，「仍然崩溃/没崩了/放弃」三按钮），live + mock + 中英 i18n 齐全。单测 12 个覆盖 8→4→2→1 全流程、元凶锁定、放弃与空会话分支。

> **2026-10-03 勘误**：上面这段的「已实现」当时只在**服务层与 UI 层**成立，**路由层不成立**。`/bisect/start|test|giveup` 与 `mods/{file}/{toggle,delete}`、`saves/{folder}/{backup,delete,open,backups}`、`saves/{folder}/backups/{name}/restore` 共 10 条路由，被嵌套在 `LocalApiService.HandleApiAsync` 的 `seg.Length == 4` 通用实例块内，条件恒假，端到端一律 404。已修复（run `20261002-182304Z`），并补 HTTP 层契约测试 `dev/PineglowLauncher.Tests/RouteReachabilityTests.cs`：10 条路由真实 HTTP 可达、一条必然未命中路径必须返回兜底 404（反空洞）、以及禁止互斥嵌套长度守卫的静态断言。

**设计**：会话按实例 Id 存 `ConcurrentDictionary`；`EnabledAtStart` 快照用于结束恢复；文件操作幂等（跳过已禁用项）。崩溃仍在 → 嫌疑在未被禁用的另一半；崩溃消失 → 锁定被禁用半区。

**回归**：`VerifyHost.exe --bisect-smoke`（API 级 E2E，自建沙盒不碰真实配置，10 项断言 + 自清理，详见 [development-guide.md](development-guide.md) 第五节）。注意：该冒烟在路由修复前**长期失败**（exit=1，首个 `POST /bisect/start` 即 404），因为既有的 8 步快照脚本从不执行它；2026-10-03 修复后实测 **exit=0、10/10 PASS**。**尚未在真实 GitHub Actions 中运行，不能当作 CI 已覆盖。**

## 六、其他遗留事项

- 🟢 ~~微软登录 DeviceCode 降级（二期）~~ ✅ 已实现（设备码流：microsoft/device + microsoft/poll）
- 🟠 figma.css 硬编码色值待 token 化
- 🟡 插件/脚本扩展能力（长远，非当前）

## 七、建议路线图

> **2026-10-01 核实后新增 7~10 项**，其中第 7 项（Cleanroom / Legacy Fabric）应优先于长期功能。

### 短期（本阶段）
1. ~~AI 崩溃诊断（M1~M4）~~ ✅ 已实现（含 M5 多提供商预设 / M6 嫌疑 Mod 结构化输出 + 一键禁用）
2. figma.css token 化清理
7. **【新增·高】补 Cleanroom / Legacy Fabric 加载器** — HMCL 已覆盖这两项，松光缺则是生态兼容硬缺口（`docs/features.md`「加载器与整合包」一节需同步）
8. **【新增·高】Mod 删除改「回收站」语义** — 对齐 PCL2 #9032，安装同名 Mod 时旧 Mod 进回收站而非永久删除
9. **【新增·中】实例删除前检查并提醒关联存档** — 对齐 PCL2 #6121，防用户误删存档；改动面小
10. **【新增·中】评估 Quilt 支持的投入产出** — PCL2 已因「Quilt 半身入土」下线 Quilt，松光需重估

### 中期（对标竞品高频）
3. ~~微软 DeviceCode 降级~~ ✅ 已实现（设置页「Microsoft 登录」改为设备码弹窗，输入 microsoft.com/link 一次性代码完成授权）
4. ~~Mod Bisect 二分定位（对齐 Prism）~~ ✅ 已实现（`ModBisectService` + `/bisect/*` API + 前端弹窗，12 个单测）
11. **【新增】刷新 `docs/features.md` 功能基线** — 现反映 2026-08-16 状态，其后大量改动未落文档，决策依据已失真

### 长期（生态与架构）
5. 评估插件/脚本接口
6. 完善共享实例/协作（对齐 Modrinth）
12. **【新增】跨平台正式支持** — 对标 HMCL（4 OS × 5 CPU 架构）与 Prism（3 OS 预构建），当前自陈「仅 Windows 验证」
13. **【新增】Modrinth `.mrpack` 实例导入互通** — 当前自定义 `.mcm` 格式不与生态互通
14. **【新增】用户侧界面主题自定义** — 当前仅深色模式，对齐 PCL2 / Prism

