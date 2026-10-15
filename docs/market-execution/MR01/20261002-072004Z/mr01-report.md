# MR01 · 功能事实矩阵刷新与调研勘误

- run-id：`20261002-072004Z`
- ExecutionRoot：`C:\Users\Administrator\AppData\Local\Temp\pineglow-snapshot-810a28bd0d3d456c9e28201fb6fdf158`
- 输入版本：SourceRoot HEAD `f145d14e…` 之上的未提交工作区，214 项输入逐文件 SHA 见
  `MR00/20261002-072004Z/baseline/input-manifest.json`
- 性质：**纯文档核对**。未修改任何业务代码，未做产品测试来证明文案修改。

---

## 一、改动文件（6 份，全部位于 ExecutionRoot 内）

| 文件 | +/- | 主要内容 |
|---|---:|---|
| `docs/features.md` | +229 / -66 | 全量重写：状态分级定义、功能矩阵、🔴 缺陷节、可复验工程事实、遗留事项与 MR ID |
| `docs/roadmap.md` | +71 / -20 | 下调 4 条对标结论、新增 P0 可达性路线、MR00~MR27 映射 |
| `docs/development-guide.md` | +70 / -6 | 新增 §五之二 快照与可复现性验证、两个验证器缺陷、锁定边界、bisect-smoke 红灯警告、改动纪律硬规则 |
| `docs/README.md` | +38 / -10 | 知识库索引刷新、关键事实速查、当前最高优先级缺陷、维护规约扩充 |
| `docs/architecture.md` | +36 / -2 | 新增 §三点五 路由门控陷阱与服务硬规则；技术栈非 Tauri；服务数 76 |
| `README.md` | +16 / -4 | 功能列表加状态提示、新增「已知缺陷」小节 |

逐文件 unified diff：`MR01/20261002-072004Z/outputs/mr01-docs.patch`（60,820 字节）
更新后文档本体：同目录 `outputs/{README.md,docs/*.md}`

---

## 二、本轮逐条落实的勘误（8 条指定项 + 本轮新增）

| # | 修正前（调研/旧文档口径） | 修正后（源码级/本轮实测） | 落点 |
|---|---|---|---|
| 1 | `.mrpack` 导入"从零缺失" | **已实现**：`InstancePackService.ImportModrinthPackAsync`（`:274`）+ `DetectModrinthFormat`（`ModpackArchiveFormat.Modrinth`），`LocalApiService.cs:1091-1094` 与 `:3626` 已接线。导出仍是 `.mcm`，**导入≠导出** | features §三「加载器与整合包」、roadmap |
| 2 | 实例"删除" | **只移除配置记录**：`HandleDeleteInstanceAsync`（`:1045-1057`）仅 `Instances.RemoveAll` + 修 `SelectedInstanceId` + `SaveConfig`，**零文件操作**，不删游戏目录/存档 | features §三「实例系统」、roadmap P0 |
| 3 | `ModService.DeleteMod` | **不可恢复**：`ModService.cs:107` `File.Delete(mod.FullPath)`、`:111` `File.Delete(metadataPath)`，无隔离/无撤销 → `MR04` | features §三「Mod 与内容管理」 |
| 4 | Bisect「结束自动恢复」已验证 | **失败只写日志**：`LocalApiService.cs:1931-1934`（禁用）与 `:1952-1955`（恢复）均 catch 后 `LauncherLog.Error` 并继续，不阻止错误推进、不反馈用户 → `MR06`/`MR07`。且**不把 12 个单测当真实恢复达标** | features §三、§1.1 |
| 5 | 松光是 Tauri | **不是 Tauri**：`git grep -i tauri` 在 `dev/`、`ui/`、根 `*.md` 零命中（exit=1）；csproj 引用 Avalonia 6 处。已有自研 `UpdateService`（含 SHA256 校验 + HTTPS 强制），**不能接 Tauri updater** | docs/README、architecture、features §三「平台」 |
| 6 | 五个 loader 与候选混谈 | **现有 5 种**：`ModLoaderKind` = Fabric/Forge/NeoForge/Quilt/OptiFine，`ModLoaderInfo.SupportedKinds` 长度 5。**Cleanroom / Legacy Fabric / LiteLoader 均不在枚举中**，是未实施候选项；**Quilt 不自动移除**，维护等级待评估（`MR14`） | features §三、docs/README |
| 7 | "Bisect 反超 Prism"/"唯一/领先 AI"/"PCL 无公开仓库"/"三平台 CI = 正式支持" | 全部不写。B Bisect 改为「服务层+UI 已接线，**路由不可达**」；AI 诊断改为「代码存在+自动验证，**增量价值未验证**」；PCL 公开仓库事实（`Meloong-Git/PCL`）不在本项目文档中断言；CI 明确「**从未实际执行**」，三平台产物 ≠ 正式支持 | features、roadmap §三/§四、docs/README |
| 8 | 内置 CurseForge Key 违规结论 | 该结论**已被调研撤回**，本文**不作新的法律定性**。仅记录可核实事实：`ModContentService.cs:21-26` 注释写明内置 Key 由官方 2026-07-15 审批签发、可在设置页用个人 Key 覆盖；剩余工作为**分发开关/配额/失败反馈**与不暴露 Key（`MR12`） | features §三「加载器与整合包」 |

### 本轮新增的 P0 勘误（调研完全未覆盖）

| # | 内容 | 落点 |
|---|---|---|
| 9 | **Bisect 与 Mod 启停/删除 HTTP 路由不可达**（`LocalApiService.cs:511`/`:531` 被嵌在 `:415` 的 `seg.Length == 4` 块内，恒 404）。`VerifyHost.exe --bisect-smoke` 实测 `exit=1` | features §1.1、architecture §三点五、README、docs/README、development-guide、roadmap |
| 10 | **796 个绿色测试结构上无法发现路由不可达**：Bisect 12 个 `[Fact]` 只测服务层；全仓 0 处引用 `/bisect/` 路由字符串；Node 侧无断言 | features §1.1、development-guide |
| 11 | 旧文档的「一个 ✅ 覆盖所有层级」是主要歧义来源。改为**五级状态词**：代码存在 / 自动验证 / 真实场景验证 / 未验证 / 不支持 | 全部 6 份 |

---

## 三、功能事实矩阵的层级纪律

`features.md` 新增状态词定义表并对每个能力给出四列：**代码存在 / 自动验证 / 真实场景验证 / 说明**。

本轮所有"真实场景验证"列**统一为未验证**，因为：

- 本批明确**不启动真实 GUI**、不启动游戏、不调用真实付费模型；
- `--bisect-smoke` 是本批唯一跑到 API 层的真实验证，且**失败**。

历史证据与当前未验证的区分：

| 历史材料 | 时间 | 本轮如何处理 |
|---|---|---|
| `docs/phase3c-1-runtime-closure-deliverable-2026-09-24.md` | 2026-09-24 | 链接引用；**旧测试数不作为本轮结果** |
| `docs/phase3c-2-metadata-deliverable-2026-09-24.md` | 2026-09-24 | 同上 |
| `docs/source-reproducibility-evidence-2026-09-28/` | 2026-09-28 | 单文件产物 SHA 与其记录**逐字节一致**，作为可复现性佐证 |
| 历史 Gate 结论 | — | **不重新宣布**任何 Gate 通过 |

**本轮唯一可引用的数字**：Node **184 通过 / 0 失败 / 0 跳过**；C# Release 全套 **612 通过 / 0 失败 / 0 跳过**；
单文件发布 45,644,552 B（SHA256 `3B71DCC1…`）；RID 发布 **KNOWN-UNLOCKED**（NU1004）。

---

## 四、只读边界

- 本包**未修改任何业务代码**；ExecutionRoot 内相对基线变化的文件仅：6 份文档 + 验证器副本 + 1 个构建副作用（lock 文件，见 MR00 缺陷 4）。
- SourceRoot 6 份文档 SHA 与 MR00 manifest **完全一致**（未改写）。
- `D:\市场调研` 全程只读，未写入。
- 未编造用户规模、访谈、市场收入或发布状态；未把任何推断写成事实。

---

## 五、未验证 / 待 Codex 裁决

| 项 | 状态 |
|---|---|
| 6 份文档的表述是否与 Codex 的产品意图一致 | **待独立验收** |
| 调研净结论中被本轮下调的条目是否还需进一步修正其他文档 | 待裁决（本批只改了 6 份允许文件，`api-reference.md` / `engineering.md` / `ai-*.md` 按白名单**未改**） |
| `MR04`/`MR06` 路由可达性修复的实施 | **不在本批范围**，已登记为 P0 |
| 真实 GUI / 游戏 / AI / CI | 未验证 |
