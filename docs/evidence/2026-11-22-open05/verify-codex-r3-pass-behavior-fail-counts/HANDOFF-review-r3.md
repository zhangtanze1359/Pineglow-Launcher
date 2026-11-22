# OPEN-05 第 3 轮独立复验

目标提交 `fe49b34f15c3ca39207fc64bcde34aec73ead74e`，与本地 `main` 一致。`FormalPhaseApproval=NOT_GRANTED`。

**判定 FAIL。** 先前两轮的路径误报和 splat 证据真实性阻断均已复验消除。自建夹具、真实检出与父版本反证的 62 项预期均满足，执行者提交的夹具也复跑出 41/41。证据 README 的三项数量明细与提交实物不符，触发本轮「表格与实测不符即 FAIL」判据。

## 提交和环境

第 0 步原始结果（`pwd`、`git rev-parse --show-toplevel`、`git rev-parse HEAD`）如下。两份工作树开工前的 `git status --short --untracked-files=all` 均无输出。

```text
C:\Users\Administrator\.t3\worktrees\松光启动器\t3-1fe68549-c9d64f5f6ec791ce
C:/Users/Administrator/.t3/worktrees/松光启动器/t3-1fe68549-c9d64f5f6ec791ce
fe49b34f15c3ca39207fc64bcde34aec73ead74e

D:\Projects\_worktrees\pg-open05-review3
D:/Projects/_worktrees/pg-open05-review3
fe49b34f15c3ca39207fc64bcde34aec73ead74e
```

实际提交链为 `17318e6 → ed2e92c → 932115c → 2cbc03f → 0fd28df → fe49b34`。简报省略的 `2cbc03f` 是第 1 轮复验材料归档提交。`git diff 932115c fe49b34 -- scripts/` 只有 `scripts/audit-evidence-tree.ps1`，`audit-plan-ledger.ps1` 无差异。源码的相对路径起点按根路径尾部是否已有分隔符选取，缺 deliverable、缺 manifest、manifest 解析失败和 runId 不匹配四处报告均使用同一个 `$runRelative`。现有历史白名单与汇总规则保持原值。见[提交统计](../evidence/2026-11-22-open05-review3/scope-show-stat.txt)、[脚本差异](../evidence/2026-11-22-open05-review3/scope-scripts.diff.txt)、[父链](../evidence/2026-11-22-open05-review3/scope-parent-chain.txt)。

执行目录 `D:\Projects\_worktrees\pg-open05-review3`，Windows PowerShell 5.1、Python 3.12.10。独立脚本复验时间 2026-11-22 23:11:15–23:11:37（UTC+08）。建树用了 `git -C D:/Projects/松光启动器 -c core.longpaths=true worktree add --detach D:/Projects/_worktrees/pg-open05-review3 fe49b34`，退出 0。每例使用独立 PowerShell 子进程，仅从子进程环境移除继承的 `PSExecutionPolicyPreference`，未设置执行策略。每例的调用、目录、时间、预期、实际、退出码和 stdout/stderr 文件名可按用例名追溯。独立驱动退出 0，`62/62`，0 跳过，其中目标版 42 例、`ed2e92c` 19 例、`932115c` 1 例。见[复跑驱动](../evidence/2026-11-22-open05-review3/review-open05.py)、[逐例记录](../evidence/2026-11-22-open05-review3/verified-run/results.json)、[夹具文件清单与哈希](../evidence/2026-11-22-open05-review3/verified-run/independent-fixtures.json)。

首遍独立驱动误把 PowerShell 序列化的 `information`/`progress` 记录当成 stderr 错误，并使用了中文参数绑定错误文本。首遍的数值结果与复跑一致，误判保存在同轮 `results.json`；正式复跑识别 CLIXML 流类型，使用实测的英文绑定错误文本，并保留原始流。正式结果位于 `verified-run/`。执行者夹具重跑的外层日志打印曾遇到控制台字符编码错误，子进程的原始载荷在打印前已单独落盘，退出码和 41 行结果由其原始文件复核。见[执行者夹具子进程](../evidence/2026-11-22-open05-review3/worker-rerun.process.txt)、[矩阵 stdout](../evidence/2026-11-22-open05-review3/worker-rerun.stdout.txt)。

## 卷根和路径写法

单元格采用 `run 数 / 发现数 / 退出码`。三棵独立夹具均在普通绝对路径和 `T:\` 各运行一次，忽略输出中的路径与来源两个展示字段后，审计正文逐行相同。

| 夹具 | 普通绝对路径 | `subst` 卷根 `T:\` |
| --- | --- | --- |
| 自洽 | 1 / 0 / 0 | 1 / 0 / 0 |
| 含 `MR00\20261002-072004Z` | 2 / 0 / 0 | 2 / 0 / 0 |
| 含未登记的 `unlisted.txt` | 2 / 1 / 1 | 2 / 1 / 1 |

| 路径写法 | 自洽 | 含历史白名单 | 含真实发现 |
| --- | --- | --- | --- |
| 绝对 | 1 / 0 / 0 | 2 / 0 / 0 | 2 / 1 / 1 |
| 绝对、末尾 `\` | 1 / 0 / 0 | 2 / 0 / 0 | 2 / 1 / 1 |
| 相对 | 1 / 0 / 0 | 2 / 0 / 0 | 2 / 1 / 1 |
| `.\` | 1 / 0 / 0 | 2 / 0 / 0 | 2 / 1 / 1 |
| `..\`、从另一目录启动 | 1 / 0 / 0 | 2 / 0 / 0 | 2 / 1 / 1 |

`git show ed2e92c:scripts/audit-evidence-tree.ps1` 导出原字节后运行相同输入。含白名单树绝对路径为 `2 / 0 / 0`。末尾反斜杠、相对、`.\`、`..\` 和卷根各为 `2 / 2 / 1`，全部是缺 `deliverable.md` 和缺 `manifest.json` 各一处。额外从 `932115c` 导出的 r3 同样在卷根输出 `R00\20261002-072004Z` 两次，r4 同树归零。初次 `subst` 查询与每次执行 `subst T: /d` 后的查询均无输出，映射在各次测试期间可见，最终清理退出 0。见[父版本卷根原始输出](../evidence/2026-11-22-open05-review3/verified-run/raw/parent-legacy-drive.out.txt)、[r3 卷根原始输出](../evidence/2026-11-22-open05-review3/verified-run/raw/r3-legacy-drive.out.txt)、[r4 卷根原始输出](../evidence/2026-11-22-open05-review3/verified-run/raw/legacy-drive.out.txt)、[subst 前后日志](../evidence/2026-11-22-open05-review3/verified-run/subst.json)、[工作树清理](../evidence/2026-11-22-open05-review3/cleanup.txt)。

## splat 与入口

提交中 `tree-splat-empty.txt` 的 `call_body` 原文，与同文件 `decoded` 内调用一致。

```powershell
call_body=$splatArgs = @{ RepoRoot = 'D:/Projects/_worktrees/pg-open05-20261122'; EvidenceRoot = '' }; & 'D:\Projects\_worktrees\pg-open05-20261122\scripts\audit-evidence-tree.ps1' @splatArgs
```

`tree-empty.txt` 使用 `-RepoRoot … -EvidenceRoot ''` 直接传参。独立使用新建哈希表和 `@reviewArgs` 得 `[EVIDENCE_ROOT_BLANK]`、exit 1，stdout 为零字节。两支被测脚本的显式空串及纯空白均报 `[EVIDENCE_ROOT_BLANK]`，不存在路径报 `[EVIDENCE_ROOT_MISSING]`，普通文件报 `[EVIDENCE_ROOT_NOT_DIRECTORY]`，省略参数且默认目录不存在报 `[DEFAULT_ROOT_MISSING]`。每个错误用例 exit 1，stdout 为空，无扫描或解析输出。含中文及空格的默认根和显式路径均可用，两种调用对同一输入的审计正文一致。见[执行者 splat 记录](../evidence/2026-11-22-open05/raw/tree-splat-empty.txt)、[执行者直接参数记录](../evidence/2026-11-22-open05/raw/tree-empty.txt)、[独立 splat 原始 stderr](../evidence/2026-11-22-open05-review3/verified-run/raw/tree-splat-empty.err.txt)。

两支脚本省略 `-EvidenceRoot` 时使用 `-RepoRoot` 下的 `docs/market-execution`。干净检出上的默认与显式输入对照分别为 `61 run / 609 处 / exit 1` 和 `31 包行 / 4 处 / exit 1`，同一脚本的审计正文一致。父版本和 r4 在同一真实检出、普通绝对路径下的审计正文也一致。

## 执行证据与阻断项

| 核对项 | README | 实测 |
| --- | --- | --- |
| 复跑矩阵 | 41/41 | 41/41，29 个树审计用例及 12 个台账用例，0 跳过，整体 exit 0 |
| `raw/` 总数 | 166 | 166 |
| 元数据、历史探针、r3 路径遗留 | 41、4、15 | 41、4、15 |
| r4 stdout/stderr 文件 | 81 | **82**，41 对 |
| 反证输出组成 | 24，父版和修复版各 7 组，部分无 stderr | **24，父版 10 对、修复版 2 对，每组都有两个文件**，部分 stderr 是空文件 |
| 第 1、2 轮归档文件数 | 234、92 | 234、**93**，第 2 轮数也用 `git ls-tree -r` 复核 |

r3 的 15 个 `tree-pathform-*` 文件和 10 个既有 `counterproof-*` 文件均原样保留，Git blob 25/25 相同。41 份 r4 已提交元数据的 `actual_exit`、编录的预期模式及扫描标记与能够复算的原始流信息一致。整体 FAIL 来自上表三个数量明细。见[执行证据 README](../evidence/2026-11-22-open05/README.md#产物清单实测)、[独立点验数据](../evidence/2026-11-22-open05-review3/verified-run/evidence-checks.json)。

### 实测 SHA-256

Windows 检出里的两支脚本与 `patched/` 副本逐字节一致。文档记录的夹具和 r2 补丁哈希对应 Git blob 的 LF 字节，Windows 检出将换行转换为 CRLF。两种表示单独记录。

| 文件 | Windows 检出 | Git blob（LF） |
| --- | --- | --- |
| `audit-evidence-tree.ps1` | `E40D4B2F6B4A7B090AC986E36CC4B24BD68230F3A9B25864DCA22360911E99D9` | `34B75D02CD91E6C6E9D97F8BBFEBC34EBD18BE9F12AFBAD04DDC263FDDAC343D` |
| `audit-plan-ledger.ps1` | `5E900796CEE2C4E196CF99D3F55CF377F1444A5AC207EB630C5E1B71BF6B65A5` | `54797FF7FC9C8D830F7B04F951B6D49EC6AA28FE5FF6A96DBB9093355DF22BB6` |
| `matrix-fixture.ps1` | `DF30F7319B1210824516755A5F0F97DA8CF78E6109E753E5C0EF589A0BDD5B3E` | `7D354D07BA25B76B92E77030959AA7BEA79546C735D4C269F4DD276449049214` |

`ed2e92c` 树审计脚本原字节 SHA-256 为 `649B9B1A22E398CE07B8A93440A3D1D4DA833945BF9205E2D6DC31BC4609202A`。r2 补丁 Git blob 共 9212 字节，SHA-256 为 `295CB84E581741AD23BA062226FE19CFF50C2771B176B906F29CF499CF6F53CE`。

## 修复范围及未验证项

最小修复是更正执行证据 README 中上述三项计数，并标注哈希的 LF/CRLF 口径供复核。本轮仅写入独立复验材料，未修改被测对象、执行证据及原归档，未提交、推送、打 tag 或发 Release。复验覆盖 Windows PowerShell 5.1、合成树及干净 Git 检出。UNC、物理整盘、执行者主工作区含本地忽略日志的输入、`gates.cjs`、.NET、前端、CI、宿主、安装包和发布均未验证。见[最小整改流程](../evidence/2026-11-22-open05-review3/REMEDIATION.md)、[可复制整改提示词](../evidence/2026-11-22-open05-review3/REMEDIATION-PROMPT.md)。

RESULT: FAIL
