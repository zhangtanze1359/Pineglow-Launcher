# OPEN-05 独立复验 · 2026-11-22（UTC+08）

**同一证据树的一致性判定失败。** 在目标提交 `ed2e92cbc9ced6ef9cb16d93d9b1474dceb62556`，`audit-evidence-tree.ps1` 用默认根或显式规范绝对路径报告 609 处不一致，用指向同一目录的相对路径或带末尾反斜杠的绝对路径则报告 615 处。四种调用均扫描 61 个 run，均退出 1。父提交 `17318e664d7070cb10a93bb06f96efbb97d72607` 的原始脚本副本在同一树复现了 609 / 615 / 615。该行为在补丁前已存在，当前目标提交仍未满足同一树审计内容与差异计数一致的验收条件。

`FormalPhaseApproval=NOT_GRANTED`。本轮只写复验记录和证据。目标提交、脚本、执行者原始证据和断言均未改动。

## 基线与原始证据

- SourceRoot 为 `D:/Projects/松光启动器`。T3 会话和独立工作树检出均为目标提交。ExecutionRoot 为 `D:/Projects/_worktrees/pg-open05-review-20261122`。`pwd`、仓库根、HEAD、状态、提交范围及反向补丁校验见 [baseline.txt](../evidence/2026-11-22-open05-review/baseline.txt)。
- `git -C D:/Projects/松光启动器 -c core.longpaths=true worktree add --detach D:/Projects/_worktrees/pg-open05-review-20261122 ed2e92cbc9ced6ef9cb16d93d9b1474dceb62556` 退出 0。提交共改 73 个文件，全部在指定两支脚本、执行证据、执行回执和开放项清单内。
- 补丁原件和提交内的 Git blob 均为 9,212 字节，SHA-256 为 `295CB84E581741AD23BA062226FE19CFF50C2771B176B906F29CF499CF6F53CE`。独立树检出为 9,353 字节，Git 将 141 个 LF 转为 CRLF。还原 LF 后字节逐一相同，`git apply --reverse --check` 退出 0。
- 独立工作树中两支脚本的 SHA-256 分别为 `E8DFDB01305CA1010567FBD4050C12465B02D426ABAB91F42CEA81711041644B`（`audit-evidence-tree.ps1`）和 `5E900796CEE2C4E196CF99D3F55CF377F1444A5AC207EB630C5E1B71BF6B65A5`（`audit-plan-ledger.ps1`）。
- 执行者 README 的表格与其 raw 一致。分别对照两支脚本的默认和绝对显式同树 stdout，去掉路径和来源两行后均 diff=0，均退出 1。`ledger-empty-tree` 原始 stdout 有 20 行 `[缺证据目录]`、汇总 20 处，元数据记录退出 1。另对全部 20 份元数据校验退出码、实际模式命中及无效参数的扫描标记。见 [结果汇总](../evidence/2026-11-22-open05-review/results.json)。

## 命令与结果

在独立工作树运行 Windows PowerShell 5.1。每个矩阵用例独立启动 `powershell.exe -NoProfile -NonInteractive -OutputFormat Text -EncodedCommand <UTF-16LE Base64>`，解码后的调用和原始退出码分别存入各例 JSON，stdout、stderr 单独保存。下表中的 `$R` 为独立工作树，`$P` 为其真实台账，`$F` 为该轮临时夹具路径。命令统一以前缀 `& "$R\scripts\<script>.ps1"` 调用，表中展示参数。

| 参数 | 证据树实际 | 台账实际 |
| --- | --- | --- |
| `-RepoRoot $R` | 61 run / 609 处，exit 1 | 31 包行 / 4 处，exit 1 |
| `-RepoRoot $R -EvidenceRoot "$R\docs\market-execution"` | 61 run / 609 处，与默认归一化 diff=0，exit 1 | 31 包行 / 4 处，与默认归一化 diff=0，exit 1 |
| `-RepoRoot $R -EvidenceRoot ''` 或空格与制表符 | `[EVIDENCE_ROOT_BLANK]`，exit 1 | 同左 |
| `-RepoRoot $R -EvidenceRoot "$F\no-default\absent"` | `[EVIDENCE_ROOT_MISSING]`，exit 1 | 同左 |
| `-RepoRoot $R -EvidenceRoot "$F\ordinary.txt"` | `[EVIDENCE_ROOT_NOT_DIRECTORY]`，exit 1 | 同左 |
| `-RepoRoot "$F\no-default"`；台账另带 `-PlanPath $P` | `[DEFAULT_ROOT_MISSING]` 包含该根下的目标目录，exit 1 | 同左 |
| `-RepoRoot "$F\repo 中文 空格" -EvidenceRoot "$F\显式 中文 空格"`；台账另带 `-PlanPath $P` | 显式目录，1 run / 自洽，exit 0 | 31 包行 / 孤儿目录 `MR99-override`，exit 1 |
| `-RepoRoot "$F\repo 中文 空格"`；台账另带 `-PlanPath $P` | 默认目录，1 run / 自洽，exit 0 | 31 包行 / 孤儿目录 `MR99-default`，exit 1 |
| `-RepoRoot $R -EvidenceRoot "$F\empty"` | 0 run / 自洽，exit 0 | 31 包行 / 20 行缺证据目录，exit 1 |
| `-RepoRoot $R -EvidenceRoot 'docs\market-execution'` | 61 run / **615 处**，exit 1 | 31 包行 / 4 处，归一化 diff=0，exit 1 |
| `-RepoRoot $R -EvidenceRoot "$R\docs\market-execution\"` | 61 run / **615 处**，exit 1 | 31 包行 / 4 处，归一化 diff=0，exit 1 |

矩阵 20/20，0 跳过。10 个无效参数用例 stdout 为空，两个流均无“扫描到/解析到 N 个”标记。扩展同树比较 6 个，4 个通过，2 个失败；父版本对照 3 次，结果如首段。随证据保存 [复跑工具](../evidence/2026-11-22-open05-review/check_open05.py)、[工具退出码](../evidence/2026-11-22-open05-review/runner.txt) 和 [逐项输出及差异](../evidence/2026-11-22-open05-review/results.json)。

## 反证与环境

1. [总当省略](../evidence/2026-11-22-open05-review/always-omitted.patch)。临时副本将绑定判定改为 `$false`，同一矩阵通过 6/20。两支脚本的显式空串、空白、不存在路径和普通文件均转红。子进程仍退出 1，但实为扫描默认树后退出，参数错误码缺失。显式合法目录的来源、选中树检查也转红。
2. [恢复 ed41 默认根](../evidence/2026-11-22-open05-review/historical-default.patch)。临时副本改回不存在的历史路径，同一矩阵通过 14/20。两支脚本的 `default` 均提前报 `[DEFAULT_ROOT_MISSING]`、退出 1，`missing-default` 虽保持错误码和退出码，诊断路径指向 ed41，双双转红。中文 RepoRoot 默认档亦转红。变体和夹具清理后，独立工作树状态为空，`git worktree remove` 退出 0。见 [清理回执](../evidence/2026-11-22-open05-review/cleanup.txt)。
3. 签名探针以各自检出为 cwd，直接调用 `powershell.exe -NoProfile -NonInteractive -File "<绝对脚本路径>" -RepoRoot "<检出路径>"`。进程未继承 `PSExecutionPolicyPreference`，`CurrentUser=RemoteSigned`，四份脚本均为 `NotSigned`，无 `Zone.Identifier`。主工作区两份脚本均 `SecurityError / UnauthorizedAccess`、退出 1，stdout 为空。独立 ASCII 工作树两份均进入审计、各因实际审计发现退出 1。路径对照仅覆盖本机两处检出，中文路径是否为签名拦截的单独成因未验证。见 [环境信息](../evidence/2026-11-22-open05-review/environment.txt) 和 [逐次命令输出](../evidence/2026-11-22-open05-review/raw/environment)。

## 最小修复点与未验证范围

`audit-evidence-tree.ps1` 用 `$EvidenceRoot.Length + 1` 从绝对 `$run.FullName` 截取相对字符串。相对根的长度偏短，带分隔符的根多跳过一个字符。两种情形都导致历史例外匹配失效，`缺少 deliverable.md` 从 2 项增至 6 项，`缺少 manifest.json` 从 2 项增至 4 项。最小修复点是路径校验成功后，建立规范绝对证据根前缀并统一分隔符，再用该前缀计算相对 run 路径。补齐规范绝对、相对和末尾分隔符的同树对照。

本轮未执行 `gates.cjs`、.NET、前端、CSS、资源闭包、真实宿主、CI、发布包或发布一致性验证。未推送、打 tag 或发 Release。

RESULT: FAIL
