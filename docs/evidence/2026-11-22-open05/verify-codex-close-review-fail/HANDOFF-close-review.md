# OPEN-05 独立销项复核

**不可销项。** 技术复验通过，目标分支落地及用户当前的合入授权已核实。第 3 轮归档的递归文件数为 392，执行证据 README 与本次简报均写 391。此外，可核验的合入授权晚于脚本进入本地 main，先批后合入的证据不足。阶段批准状态为 `FormalPhaseApproval=NOT_GRANTED`。

## 审查对象与环境

- 目标提交 `8586e92f5f725039eb0b587e26f6b1941394dac1`。工作从 2026-11-22 23:42（UTC+08）开始，清理于 2026-11-23 00:00:59 完成。Windows PowerShell `5.1.26100.10068`，Python `3.12.10`。子进程未继承 `PSExecutionPolicyPreference`，未改执行策略设置。
- 第 0 步原始路径和 HEAD 输出如下。会话工作树起初无已跟踪或未跟踪文件变动。

  ```text
  C:\Users\Administrator\.t3\worktrees\松光启动器\t3-b52b197b-fdd5b8648d3cdf7b
  C:/Users/Administrator/.t3/worktrees/松光启动器/t3-b52b197b-fdd5b8648d3cdf7b
  8586e92f5f725039eb0b587e26f6b1941394dac1
  ```

- 产品脚本执行目录为 `D:/Projects/_worktrees/pg-open05-close-b52b197b`，使用 `git -C D:/Projects/松光启动器 -c core.longpaths=true worktree add --detach D:/Projects/_worktrees/pg-open05-close-b52b197b 8586e92` 创建，exit 0。自建夹具在同级 `open05-review-input-b52b197b` 和 `open05-review-input-b52b197b-rerun` 创建。每例命令、解释后的 PowerShell 语句、实际工作目录、时区、退出码及原始输出见[正式复跑逐例记录](../evidence/2026-11-22-open05-close-review/rerun/matrix.json)。
- 本地 `main` 初次检查指向 `8586e92`，主工作区有一处未提交 `docs/open-items.md`。取证期间编排方提交 `1e6d4fd`，末次检查主工作区已跟踪文件干净，仍有原有未跟踪文件。两提交的唯一差异是 `docs/open-items.md` 的描述更新，目标脚本、真实输入树与历史证据归档均无 diff。两支脚本在 `fe49b34`、`8586e92` 和末次观察到的 `main` 内容相同。简报列出的提交逐个对 `main` 执行祖先检查均 exit 0。完整 `git log --oneline -8`、`git show --stat 8586e92`、状态和 diff 见 [Git 与归档点验](../evidence/2026-11-22-open05-close-review/git-and-archive.txt)。

## 条件对照

以下引文取自 [目标提交中的 OPEN-05](../open-items.md#open-05--审计脚本证据目录补丁待合入)。

> **销项条件**：目标基线独立复验、合入批准、目标分支落地及合入后参数行为确认均完成。补丁仅可应用或历史 16/16 通过不得销项。

> 5. **独立复验后申请合入**：批准后方可合入，并在合入后的目标分支确认两支脚本参数行为。技术通过不替代合入审批。

| 销项条件原文分项 | 满足情况 | 依据 |
| --- | --- | --- |
| 目标基线独立复验 | 满足（技术范围） | 新建 `8586e92` 工作树，独立夹具重跑 41/41，含 7 个父版本对照。 |
| 合入批准 | 当前明示批准已核实，先批后合入证据不足 | 2026-11-22 23:29:00（UTC+08），用户原话“合入，然后在目标分支确认参数行为”（消息 849）。本地 reflog 显示补丁最早于 22:21:28、最终脚本于 23:05:20 已直接提交到 main。可见的早期“继续 OPEN-05”指示安排的是执行与审查。证据见 [整理任务计划](t3-thread://v2/acca299b-8eda-46d6-8283-83ae1895b59c/38c61823-88d9-4d75-bfb4-81f5ddc1f5f2) 与 [main reflog](../evidence/2026-11-22-open05-close-review/approval-timeline.txt)。 |
| 目标分支落地 | 满足 | `8586e92` 已落入本地 `main` 的祖先链，两支脚本的工作区换行归一后 blob 与目标提交相同，独立检出无跟踪改动。 |
| 合入后参数行为确认 | 满足 | 从目标 Git 提交直接检出并运行两支脚本。合法、非法、同树一致性、白名单与映射卷根均已独立查验。 |

| 验收清单原文 | 满足情况 | 依据 |
| --- | --- | --- |
| 两支脚本均覆盖参数矩阵，分别记录参数来源、选中根目录、错误与退出码。 | 满足 | 目标脚本 34 例。原始输出与逐例 JSON 可核查实际参数来源、路径、错误和 exit。 |
| 参数无效时在扫描前失败；合法中文、空格路径可用。 | 满足 | 无效输入均在 stderr 报指定标签、stdout 为空、exit 1。台账额外传入不存在的 `PlanPath` 验证参数错误优先于读台账。源码中验证块在扫描之前。 |
| 默认目录与显式目录在同一输入树上产生一致的审计内容和差异计数。 | 满足 | 夹具树与真实树皆验证两支脚本。逐行比较仅排除根路径和来源两行，记录根路径、来源与真实进程退出码。 |
| 保留真实审计发现，不通过删证据或改变统计语义使退出码变绿。 | 满足（脚本行为和原始材料保留） | 证据树 61 run / 609 处，台账 31 包行 / 4 处，均 exit 1。父版本绝对路径与现版真实证据树输出逐行一致。归档计数单独列在下方。 |
| 目标基线独立复验完成，合入获准，且补丁已确认落入目标分支。 | 技术与落地已满足，授权顺序证据不足 | 已有 23:29 明示批准。现有证据无法确认本地分支首次获得补丁前已获授权。 |
| 本次简报额外要求的归档数量一致 | 不满足 | 第 3 轮递归计算为 392，登记和简报为 391。 |

## 参数矩阵与反证

| 输入 | 目标版证据树 | 目标版台账 |
| --- | --- | --- |
| 省略 `-EvidenceRoot`，夹具仓库默认根存在 | 选中默认根，2 run / 0 处 / exit 0 | 选中默认根，2 包行 / 0 处 / exit 0 |
| 显式传入同一个根 | 显式来源，审计正文与 exit 均相同 | 显式来源，审计正文与 exit 均相同 |
| 正常默认根存在时改传另一棵空格中文目录 | 选中显式目录，新增夹具 run 被报告，3 run / 2 处 / exit 1 | 选中显式目录，run 统计从 2 到 3，2 包行 / 0 处 / exit 0 |
| 默认根不存在 | `[DEFAULT_ROOT_MISSING]`，exit 1 | 同左 |
| 显式空白（含制表符）或空串 | `[EVIDENCE_ROOT_BLANK]`，两例均 exit 1 | 同左 |
| 显式空串，哈希表 splat | `[EVIDENCE_ROOT_BLANK]`，exit 1 | 同左 |
| 显式路径不存在 | `[EVIDENCE_ROOT_MISSING]`，exit 1 | 同左 |
| 显式路径是普通文件 | `[EVIDENCE_ROOT_NOT_DIRECTORY]`，exit 1 | 同左 |

同一棵路径夹具包含 `MR00\20261002-072004Z`。该 run 省略 deliverable 和 manifest 以检查白名单是否仍命中。另一 run 带正常 deliverable 和 manifest。以下每格为“对象数 / 发现数 / 退出码”。

| 同树路径写法 | 目标版证据树 | 目标版台账 | `ed2e92c` 证据树 |
| --- | --- | --- | --- |
| 普通绝对 | 2 / 0 / 0 | 2 / 0 / 0 | 2 / 0 / 0 |
| 末尾反斜杠 | 2 / 0 / 0 | 2 / 0 / 0 | 2 / 2 / 1 |
| 相对 | 2 / 0 / 0 | 2 / 0 / 0 | 2 / 2 / 1 |
| `.\` | 2 / 0 / 0 | 2 / 0 / 0 | 2 / 2 / 1 |
| `..\`，从另一目录执行 | 2 / 0 / 0 | 2 / 0 / 0 | 2 / 2 / 1 |
| `subst Z:\` | 2 / 0 / 0 | 2 / 0 / 0 | 2 / 2 / 1 |

旧版取自 `git show ed2e92c:scripts/audit-evidence-tree.ps1` 的字节原样，绝对路径是阴性对照。五种非普通绝对写法均实际打印“缺少 deliverable.md”和“缺少 manifest.json”，各一处。第一次运行夹具误把错误标题写成“缺”，导致 5 个夹具判定为 FAIL，原始输出如实保留。仅修正夹具的两个匹配字面值后使用新夹具目录重跑，进程退出 0、41/41 符合预期、0 跳过。工作目录、证据与输入见[首轮控制台](../evidence/2026-11-22-open05-close-review/matrix-console.txt)、[正式逐例结果](../evidence/2026-11-22-open05-close-review/rerun/matrix.json)和[旧版卷根输出](../evidence/2026-11-22-open05-close-review/rerun/raw/parent-path-volume.stdout.txt)。

## 真实树与 SHA-256

| 真实树脚本 | 默认调用 | 同树显式调用 | 正文逐行对照 |
| --- | --- | --- | --- |
| 证据树 | 61 run / 609 处 / exit 1 | 61 run / 609 处 / exit 1 | 0 处差异。旧版普通绝对路径也各项一致。 |
| 台账 | 31 包行 / 4 处 / exit 1 | 31 包行 / 4 处 / exit 1 | 0 处差异。审计输出另报告 61 run 目录。 |

独立检出真实输入共 1095 个文件，均由 Git 跟踪。现查主工作区同路径 1735 个文件，其中 640 个是 Git 忽略的 `*.log`。因此历史 61 run / 7 处是不同输入集合的报告。独立复跑采用同树、同脚本、同统计语义逐行比较。工作区实测 SHA-256 为主工作区与独立检出共同的字节哈希。

| 文件 | 目标 Git blob | 目标提交 blob 内容 SHA-256，LF | 主工作区与独立检出 SHA-256，CRLF |
| --- | --- | --- | --- |
| `audit-evidence-tree.ps1` | `5173e5a4ee498eddd06c0909880163fb1cbe640b` | `34B75D02CD91E6C6E9D97F8BBFEBC34EBD18BE9F12AFBAD04DDC263FDDAC343D` | `E40D4B2F6B4A7B090AC986E36CC4B24BD68230F3A9B25864DCA22360911E99D9` |
| `audit-plan-ledger.ps1` | `d60b6ac7a72c482136624d15a739e8f24a7aa303` | `54797FF7FC9C8D830F7B04F951B6D49EC6AA28FE5FF6A96DBB9093355DF22BB6` | `5E900796CEE2C4E196CF99D3F55CF377F1444A5AC207EB630C5E1B71BF6B65A5` |

`git ls-files --eol` 确认为 `i/lf w/crlf`，两份检出分别以 `git hash-object --path` 重算后与目标 blob 一致。[哈希、EOL 与输入文件数](../evidence/2026-11-22-open05-close-review/git-and-archive.txt)

## 历史证据点验与最小补齐

| 归档目录 | 简报期望 | Git 递归文件数 / 磁盘递归文件数 | 结果 |
| --- | ---: | ---: | --- |
| `verify-codex-r1-fail/` | 234 | 234 / 234 | 一致。225 个文件在 `0fd28df` 做 100% 同 blob 移位。 |
| `verify-codex-r2-fail/` | 93 | 93 / 93 | 一致。 |
| `verify-codex-r3-pass-behavior-fail-counts/` | 391 | **392 / 392** | 文档计数不符。 |
| `verify-codex-r4-pass/` | 3 | 3 / 3 | 一致。 |
| `target-confirm/` | 80 | 80 / 80 | 一致。 |
| `raw/` | 166 | 166 / 166 | 41 元数据 + 41 stdout + 41 stderr + 24 反证（父版 20，修复版 4）+ 4 历史探针 + 15 r3 遗留。41 组元数据和输出逐一配对。 |

第 3 轮首次归档 `8a95573` 就包含 392 个 Git 文件，至 `8586e92` 路径集合和 blob 集合保持一致。构成为顶层 17（含归档说明 `README.md`）+ `independent-raw/` 124 + `verified-run/` 128 + `worker-rerun/` 123。仅排除顶层归档说明得到 391，因此可记录两个明示口径。其余轮次和目标确认目录也与各自首次归档的 blob 集合一致。开启精确重命名检测的删除历史在该证据目录无记录。[归档总表](../evidence/2026-11-22-open05/README.md#产物清单实测) · [点验原始记录](../evidence/2026-11-22-open05-close-review/git-and-archive.txt)

最小补齐为修订历史证据 README 的第 3 轮表项为“递归 392 个文件（其中归档说明 1 个，独立复验材料 391 个）”，同步销项简报的计数口径。合入顺序另需补充首次落地之前的批准记录。若没有事前记录，由编排方按项目规则处理已经发生的顺序偏差，并记录正式决定。独立复验方按更新后的证据点验和签署。若提交差异仅有计数文字，可核对脚本、真实输入和历史原始输出的 Git 对象身份后复用本轮技术证据。[整改步骤](../evidence/2026-11-22-open05-close-review/REMEDIATION.md) · [复核提示词](../evidence/2026-11-22-open05-close-review/REMEDIATION-PROMPT.md)

## 清理及未验证范围

- 两轮的 `subst Z: /D` 都 exit 0。映射前和解除后 `subst` 无输出。完成后再次核对 `Test-Path Z:\` 为 False。
- 复跑后独立工作树 status 无输出，用 `git worktree remove` 移除，exit 0。注册列表和磁盘路径已核验无残留，两份自建夹具也已移除。[清理日志](../evidence/2026-11-22-open05-close-review/cleanup.txt) · [盘符原始记录](../evidence/2026-11-22-open05-close-review/rerun/subst.json)
- UNC 路径和物理整盘根未覆盖。卷根结论适用于本轮验证的 `subst` 模拟根。未执行全量 gates、.NET、前端或发布一致性，也未在主工作区重跑历史 7 处的审计。
- 未修改被测对象、开放项文件或历史归档。未提交、推送、打 tag 或发 Release。

RESULT: FAIL
