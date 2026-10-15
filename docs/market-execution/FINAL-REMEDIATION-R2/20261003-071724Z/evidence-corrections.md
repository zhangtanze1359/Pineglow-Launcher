# 证据更正与历史例外 — FINAL-REMEDIATION-R2（S4 / RA-04）

本文件**只更正说法与登记差异**，不改写任何历史证据。旧 manifest、旧 log、旧 RED 一律保持原字节。

---

## 一、保留的历史异常（2 项，精确绑定到字节）

上一轮与 Codex 本轮独立审计都确认：真实证据树中有 2 项清单与当前字节不符。
它们**不是**本轮造成的，本轮也**没有**重算那两个旧 manifest。机器可读登记见 `historical-exceptions.json`；
审计器要求"manifest 记录的旧值""登记表里的当前值""文件此刻实际值"三者**同时**吻合，任何**第三次**漂移立即判失败。

| 清单（相对 `docs/market-execution`） | 文件 | 旧 manifest 记录 | 登记的当前值 | 来源 |
|---|---|---|---|---|
| `MG0-verification-package-20261003/manifest.json` | `drift-notes.md` | 3114 字节 / `1F7197B3…F0BE88` | **4088 字节** / `C55CE939…77CF6B` | 交付根 ed41 |
| `MG2-verification-package-20261003/manifest.json` | `verify-mg2-full.log` | 41770 字节 / `B99A6D92…813C31` | 41770 字节 / `BE2E432E…5E2F95` | 交付根 ed41 |

说明边界：
- 只证明"清单与当前字节不符"；**不**证明篡改动机，也**不**证明内容造假。
- "已知异常"**不得**写成"全部哈希一致"。审计输出把这两项单列为 `knownMismatches`，并显式带
  `historicalExceptionsRetained: true` 与计数。
- 若今后这两条路径再出现**第三种字节**，审计返回非零（该行为已有合成否证测试覆盖）。

---

## 二、本轮审计器的三处假通过（已关闭，并保留反例）

上一轮 `audit-all-manifests.py` 存在三处"看起来通过"的缺陷，Codex 已用合成输入证明，我也独立复现：

| 合成输入 | 旧审计器 | 旧审计器退出码 | 新审计器 |
|---|---|---|---|
| 存在但没有 manifest 的空根 | `manifestCount=0 / failures=0` | 0（假通过） | **exit 1**（非空目标要求） |
| 已知路径换成第三种字节 | 仅按路径豁免 | 0（假通过） | **exit 1**（例外必须字节绑定） |
| `manifest.sha256` 为 64 个 0 | 未校验 detached | 0（假通过） | **exit 1**（detached 必须匹配） |

旧审计器的三份反例结果 JSON 保留在 `red/r1-auditor-*.json`；新审计器的 12 例合成测试
（10 个负例 + 2 个正例，含"恰好等于登记的例外必须通过"）见 `audit-negative-tests.json`。

同时补齐 Codex 要求的阳性失败项：**缺失**、**漏登记**、**未知漂移**、**runId 不一致**、**自引用**、**重复条目**。

---

## 三、口径说明（不得外推）

1. **`missingDetached=59` 是报告而非失败**：历史 59 个包生成于 detached 约定之前；本修复线的包缺少
   `manifest.sha256` 一律判失败。该策略写在审计脚本 docstring 与交付报告 §八。
2. **审计输出放在包外**（`FINAL-REMEDIATION-R2/20261003-071724Z-audit.json`），避免"生成物自引用"导致的循环；
   另有 detached `.sha256`。
3. 本轮**未**重跑 `verify-mg0..3` 与 `audit-plan-ledger/evidence-tree/claimed-tests` 这 7 个项目脚本 ——
   它们属于历史里程碑包；本轮只做全层级清单审计（覆盖两份交付根、60 份 manifest、830 项记录）。
