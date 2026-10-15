# Correction: FINAL-REMEDIATION-R2 post-seal rewrite of `source-after.json`（2026-10-03）

本文件是 **LONG-QUALITY-GOAL / G1** 的新 correction 记录。它只**登记差异与来源**，不修改任何历史证据：

- 旧 `manifest.json`、旧 `manifest.sha256`、旧 RED、旧日志、旧审计结果、R2 包内所有文件：**原字节保留，未重算、未覆盖**。
- 旧包封存失效这件事本身**继续记录为失败事实**；新增 correction 不代表原包变成 PASS。

## 1. 事实

独立复跑本轮新审计器（`scripts/audit-goal-evidence.py`，冻结预期 `g0/expected-evidence.json`，不含任何例外时）：

| 项 | 值 |
| --- | --- |
| 审计根 | `C:\Users\Administrator\.codex\worktrees\ed41\松光启动器\docs\market-execution`、`C:\Users\Administrator\.codex\worktrees\0f45\松光启动器\docs\market-execution` |
| 真实退出码 | **1** |
| 包数 / 记录数 | 61 / 874 |
| 失败项 | `FINAL-REMEDIATION-R2/20261003-071724Z` → `source-after.json`：manifest 记录 `7CA0937E…F161`，磁盘实际 `0FDB0194…899B2`（大小同为 4192） |

原始 exit=1 结果保留为 `g1/audit-history-raw.json`（未加例外，未修饰）；只按字节登记的版本为 `g0/expected-evidence.json` + `g1/audit-history-bound.json`（exit 0，`knownDrift` 恰好 3 项）。

该结果与 Codex 本轮独立验收一致：
`34ca\docs\market-acceptance\20261003-r2-independent\r2-seal-mismatch.json`
（`record.sha256=7CA0…F161`、`actualSha256=0FDB…899B2`、`actualSize=4192`）。

## 2. 根因（同一次工具调用的顺序）

证据：`34ca\docs\market-acceptance\20261003-r2-independent\harness-sealing-call-order.json`（只读引用，本目标未修改）。其中 **seq=1351** 的 `pwsh` 调用按以下顺序执行：

1. `seal-evidence.ps1 -Package $RUN -RunId '20261003-071724Z' …` → 生成 `manifest.json` / `manifest.sha256`，此时 `source-after.json` 被登记为 `7CA0937E…F161`；
2. `audit-all-manifests-r2.py` → 此刻审计通过（文件仍是密封时那一份）；
3. `capture-baseline.ps1 -SourceRoot 'D:\松光启动器' -OutDir $RUN -Tag 'after'` → **再次写入已封存的包内文件** `source-after.json`，字节变为 `0FDB0194…899B2`。

因此“当时审计通过、此后复验失败”是**可确认的操作顺序问题**，不是随机损坏。边界：本记录只证明封存后被写入与当前字节漂移，不推测动机，也不据此认定测试日志或业务源码造假。

## 3. 原始字节是否可恢复

**UNKNOWN。** 目录内没有 `7CA0937E…F161` 这一份 `source-after.json` 的副本，`source-before.json` 是另一份内容（其自身 SHA 与 manifest 记录一致）。本 correction **不生成**“原始版本”，不重算旧清单，不用近似文件冒充原字节。

## 4. 新规则（本次修复线的封存契约）

- **顺序**：终态采集（`capture-goal-baseline.ps1 -Stage terminal`）→ 最终内容完成 → 封存（生成 `manifest.json` + `manifest.sha256`）→ **包外**独立审计 → 之后包内禁止任何写入；要改必须换新 `runId` 重跑。
- **可检测**：封存后再写包内文件会被本轮审计器判失败——
  - 已冻结的 `manifestSha256` 变化 → `MANIFEST_SHA_DRIFT`；
  - 包内出现未登记文件 → `UNLISTED_FILE`；
  - 已登记例外的 manifest 再变化 → `EXCEPTION_MANIFEST_DRIFT`；
  - 已登记例外的目标文件出现**第三种字节** → `FILE_DRIFT_UNREGISTERED`。
  以上四类均有合成否证测试逐项断言（`scripts/tests/audit-goal-evidence.test.py` 的 test_19 / test_13 / test_20 / test_15），真实退出码非 0。
- **包外输出**：审计报告、测试日志、运行结果一律写到被审包之外的路径；本目标的新包 `FINAL-LOCAL-QUALITY/<runId>` 的审计结果写在 `<EvidenceRoot>\…` 包外，并另有 detached `sha256`。

## 5. 与本 correction 绑定的字节

`g0/historical-exceptions.json` 登记 3 条例外，每条同时绑定「旧 manifest 记录值 / 登记的当前值 / 此刻磁盘实际值」三方：

| manifest | 文件 | 旧记录 | 登记当前 | 性质 |
| --- | --- | --- | --- | --- |
| `MG0-verification-package-20261003/manifest.json` | `drift-notes.md` | 3114 B / `1F7197B3…F0BE88` | 4088 B / `C55CE939…77CF6B` | 历史（Codex 已登记） |
| `MG2-verification-package-20261003/manifest.json` | `verify-mg2-full.log` | 41770 B / `B99A6D92…813C31` | 41770 B / `BE2E432E…5E2F95` | 历史（Codex 已登记） |
| `FINAL-REMEDIATION-R2/20261003-071724Z/manifest.json` | `source-after.json` | 4192 B / `7CA0937E…F161` | 4192 B / `0FDB0194…899B2` | **本次封存后重写** |

三方任一不符即非零退出；重复登记、未被消费的登记、第三次漂移都显式失败。
