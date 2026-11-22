# 交接 · OPEN-05 审计脚本证据目录（2026-11-22，r4）

按 [任务计划](../任务计划-2026-11-22.md) 第二节第 2 项执行。两轮独立复验（Codex `gpt-6.2-sol` high）均 FAIL，阻断项均为既有缺陷，已分别修复并补反证。**待第 3 轮独立复验**。`FormalPhaseApproval=NOT_GRANTED`，未推送、未打 tag。

## 状态

- 基线：`main@0fd28df`（含 r3 修复 `932115c`）；r4 修复尚未提交。
- 隔离工作树：`D:/Projects/_worktrees/pg-open05-20261122`（detached HEAD `17318e6`），补丁已应用并保留现场。
- 补丁：`open05-fix-r2.patch`，SHA-256 `295CB84E581741AD23BA062226FE19CFF50C2771B176B906F29CF499CF6F53CE`。
- 应用后脚本哈希：`audit-evidence-tree.ps1` = `E40D4B2F6B4A7B090AC986E36CC4B24BD68230F3A9B25864DCA22360911E99D9`；`audit-plan-ledger.ps1` = `5E900796CEE2C4E196CF99D3F55CF377F1444A5AC207EB630C5E1B71BF6B65A5`。
- 矩阵：**41/41 PASS**。
- 主工作区与隔离工作树的脚本逐字节一致。

## 两轮阻断与修复

1. **第 1 轮（r3）**：`Substring($EvidenceRoot.Length + 1)` 在相对路径 / 末尾反斜杠 / `..` 段下前缀错位，`$preConvention` 白名单失效，同一棵树多报「缺 deliverable / manifest」。修复：校验后 `Resolve-Path` 规范成绝对前缀。
2. **第 2 轮（r4）**：
   - **卷根仍错位**：`Z:\` 已带分隔符，`+1` 又跳一个字符，`MR00\…` 被截成 `R00\…`，白名单失效。修复：截取下标集中计算一次，按根是否以分隔符结尾决定。
   - **执行证据把直接传参记成了 splat**：`tree-splat-empty` 实为 `-EvidenceRoot ''`，与 `tree-empty` 重复。修复：改为真实哈希表 splat，元数据记录 `call_body` 原文。

## 本机与历史计数差异（必须按此读）

历史记录 61 run / 609 处；本机主工作区实测 61 run / **7** 处（台账 31 包行 / 4 处，与历史一致）。原因：主工作区 `docs/market-execution` 有 1735 个文件、git 仅跟踪 1095 个，另有 640 个被 `*.log` 规则忽略的日志仍在磁盘；干净工作树缺它们才报 609。**两者都是真实输入结果，差异来自文件集合。**

## 证据索引

[`docs/evidence/2026-11-22-open05/`](../evidence/2026-11-22-open05/README.md)：

| 文件 | 内容 | 哈希 / 数量 |
| --- | --- | --- |
| `open05-fix-r2.patch` | r2 补丁副本 | `295CB84E…53CE` |
| `patched/audit-evidence-tree.ps1` | r4 应用后脚本 | `E40D4B2F…99D9` |
| `patched/audit-plan-ledger.ps1` | 应用后脚本 | `5E900796…65A5` |
| `matrix-fixture.ps1` | 41 例夹具 | `7D354D07…9214` |
| `raw/` | 41 例 + 24 反证 + 4 历史探针 + 15 个 r3 遗留 | 166 个文件（明细见证据 README） |
| `verify-codex-r1-fail/` | 第 1 轮 FAIL 材料原样 | 234 个文件 |

## 待编排方决定

1. 第 3 轮独立复验。
2. 复验通过后是否合入；本轮未推送、未打 tag。
3. 是否接受把 r3+r4 的路径规范化（合计 +19 行）并入本补丁——它修的都是「同一证据树审计内容一致」这一验收入口条件的既有缺陷。
4. 合入后是否在目标分支再确认参数行为（销项条件要求）。
5. 是否需要补 UNC 路径与物理整盘扫描的验证（本轮仅 `subst` 卷根）。
