# OPEN-05 第 2 轮独立复验

**结论为 FAIL，暂不放行。** `932115c` 已修复上一轮普通目录五种写法导致的误报。有效的盘符根仍会让历史白名单失效，父版也存在该缺陷。另有一项明确的执行证据表格与原始调用不符，即标为 splat 的用例实际重复了直接传空串。独立实测真正的 splat 入口符合预期。

`FormalPhaseApproval=NOT_GRANTED`。本轮只写复验材料，未修改被测脚本、执行者证据或遗留项清单，未提交、推送、打 tag 或发 Release。

## 输入、环境与命令

第 0 步在 T3 交付工作树的原始输出如下。初始 `git status --short --untracked-files=all` 的输出为空。

```text
C:\Users\Administrator\.t3\worktrees\松光启动器\t3-857f93cf-c98b7232fe8581a3
C:/Users/Administrator/.t3/worktrees/松光启动器/t3-857f93cf-c98b7232fe8581a3
932115c6651d86081491f8fa401d8534dbbd9485
```

独立执行工作树为 `D:/Projects/_worktrees/pg-open05-review2-20261122`，以 `git -C <主仓库> -c core.longpaths=true worktree add --detach <独立工作树> 932115c` 创建，exit 0。该工作树的第 0 步依次输出如下，初始状态为空。

```text
D:\Projects\_worktrees\pg-open05-review2-20261122
D:/Projects/_worktrees/pg-open05-review2-20261122
932115c6651d86081491f8fa401d8534dbbd9485
```

- `git -C <主仓库> log -1 --stat 932115c`、`git diff --check ed2e92c 932115c` 和台账脚本的 `git diff --exit-code` 均 exit 0。提交链为 `932115c → ed2e92c → 17318e6`。脚本范围仅 `audit-evidence-tree.ps1` 的 8 行新增，其他差异在 `docs/`。
- Windows PowerShell `5.1.26100.10068`，复跑于 2026-11-22 22:52:03 至 22:52:19（UTC+08），41 个独立子进程，39 项符合预期，2 项在盘符根暴露同一缺陷，0 跳过。父版负对照的预期退出 1 已计作符合预期。测试运行器整体 exit 1。
- 在 T3 工作树运行 `python docs/evidence/2026-11-22-open05-review2/review-open05.py D:/Projects/_worktrees/pg-open05-review2-20261122`。运行器为每例保存工作目录、时间、解码命令、预期、实际、退出码和原始输出。子进程沿用机器策略，环境中未提供 `PSExecutionPolicyPreference`，无 `-ExecutionPolicy Bypass`。父样本按字节导出自 `git show ed2e92c:scripts/audit-evidence-tree.ps1`。
- 夹具为本轮自建，由白名单 `MR00\20261002-072004Z` 与产物、清单及 SHA 自洽的 `MR77\20261006-083004Z` 组成。另建含一处未登记文件的对照树。文本使用 ASCII 或 UTF-8 BOM。

[提交范围与环境](../evidence/2026-11-22-open05-review2/scope-and-environment.txt) · [逐例命令与结果](../evidence/2026-11-22-open05-review2/results.json) · [运行器](../evidence/2026-11-22-open05-review2/review-open05.py)

## 普通路径和父版反证

每格为「发现数 / exit」。夹具各 2 个 run，检出的真实树 1095 个文件、61 个 run。

| `-EvidenceRoot` 写法 | 目标自洽夹具 | 目标含发现夹具 | 目标真实树 | 父版自洽夹具 |
| --- | ---: | ---: | ---: | ---: |
| 绝对 | 0 / 0 | 1 / 1 | 609 / 1 | 0 / 0 |
| 末尾 `\` | 0 / 0 | 1 / 1 | 609 / 1 | 2 / 1 |
| 相对 | 0 / 0 | 1 / 1 | 609 / 1 | 2 / 1 |
| `.\` 相对 | 0 / 0 | 1 / 1 | 609 / 1 | 2 / 1 |
| `..\` 相对，从旁边目录启动 | 0 / 0 | 1 / 1 | 609 / 1 | 2 / 1 |

同一输入五种写法的目标版 stdout 逐字符一致。父版绝对路径和目标版绝对路径的审计正文一致。父版另外四种写法各多报一处缺 deliverable 和一处缺 manifest。

## 入口

两支脚本在省略 `-EvidenceRoot` 时选中默认根，和显式真实树的审计正文一致。证据树为 61 run / 609 处 / exit 1，台账为 31 包行 / 4 处 / exit 1。默认和显式中文、空格路径均进入正常审计。两支脚本的非法输入均 exit 1，且没有任何扫描或解析输出。

| 条件 | 错误标记 |
| --- | --- |
| 显式空串 / 全空白 | `[EVIDENCE_ROOT_BLANK]` |
| 显式不存在 | `[EVIDENCE_ROOT_MISSING]` |
| 显式普通文件 | `[EVIDENCE_ROOT_NOT_DIRECTORY]` |
| 省略时默认目录缺失 | `[DEFAULT_ROOT_MISSING]` |

另以真正的 PowerShell 参数哈希表 splat 传显式空串，证据树脚本返回 `[EVIDENCE_ROOT_BLANK]`、exit 1、stdout 为空。[独立 splat 原始错误](../evidence/2026-11-22-open05-review2/raw/tree-splat-empty.err.txt)

## 盘符根

用 `subst` 把独立夹具目录临时映射为 `Z:\`。以其子目录为 cwd，显式传 `Z:\`，目标与父版 stdout 完全一致，均扫描 2 个 run、误报 2 处、exit 1。直接传同一夹具的普通目录绝对路径则为 2 run、0 处、exit 0。保留尾部反斜杠让扫描和展示仍使用卷根，但后续 `Substring($EvidenceRoot.Length + 1)` 已经多越过一个字符，白名单里的 `MR00\...` 被截成 `R00\...`。物理盘符根遵循同一截取计算，本轮没有扫描物理 `D:\`。映射已解除。

[目标根输出](../evidence/2026-11-22-open05-review2/raw/drive-candidate.out.txt) · [父版根输出](../evidence/2026-11-22-open05-review2/raw/drive-parent.out.txt) · [普通目录对照](../evidence/2026-11-22-open05-review2/raw/drive-folder-control.out.txt)

## 执行者证据抽查

- 实有 101 个文件，即 29 组元数据、stdout、stderr（87 个），5 组反证 stdout、stderr（10 个），以及 4 个 `probe-*.txt` 历史输出。五组反证 stdout 的结论均与表格一致，单独的原始退出码元数据未提供。本轮独立反证记录了进程退出码。
- 29 份元数据均标为 `PASS`，按其记录的退出码和实际输出重算仍满足执行者的现有断言。README 的 `tree-splat-empty` 行称使用 splat，其原始元数据和当前夹具第 159 行均只写 `-EvidenceRoot ''`。README 称没有孤立历史文件，`raw/` 仍有两组 `probe-*`，没有对应矩阵元数据。
- README 给出的夹具哈希为 `B1ACD7E2…95B7`，独立检出的同名文件 SHA-256 为 `E04B531BF92E4E2FBEFC7CB290A8351EFA60E0A3696054FDE2E80EA682D56EA9`。执行者真实树矩阵输出为 609，README 中 7 处是另一个本机主工作区输入的声明，本轮未在该输入上复跑。

[执行者证据核对输出](../evidence/2026-11-22-open05-review2/worker-evidence-check.txt)

## SHA-256

| 文件 | SHA-256 |
| --- | --- |
| 目标 `audit-evidence-tree.ps1` | `90E3821945A3352A4E8767705128CB3659CDAEC04AEA317C008630101A70ADCF` |
| 目标 `audit-plan-ledger.ps1` | `5E900796CEE2C4E196CF99D3F55CF377F1444A5AC207EB630C5E1B71BF6B65A5` |
| `ed2e92c` 导出的 `audit-evidence-tree.ps1` | `649B9B1A22E398CE07B8A93440A3D1D4DA833945BF9205E2D6DC31BC4609202A` |

## 最小整改与复验顺序

1. 保持扫描根路径的绝对盘符根形式。让 run 相对路径的起始下标只计算一次，保证根路径与相对路径之间只计一个分隔符。统一复用到缺 deliverable、缺 manifest、JSON 无法解析、runId 不符四处。保持白名单、审计项、计数和退出码语义。
2. 回归盘符根映射及对应普通绝对路径，要求两者均为 2 run、0 处、exit 0。保留三棵树的五种写法覆盖，同时重跑父版负对照及入口参数矩阵。
3. 把 `tree-splat-empty` 改为真实哈希表 splat，保存解码命令、stdout、stderr、实际退出码。以新运行生成的哈希、文件数和结论更新 README，注明历史探针来源并保留原始文件。记录反证使用的提交和逐例退出码。
4. 执行者提交交接后，由独立复验者按本轮边界复跑并另行记录结果。阶段审批仍为 `NOT_GRANTED`。

未验证 `gates.cjs`、.NET、前端、CI、原生宿主、安装包、发布一致性、主工作区未跟踪日志输入、UNC 与物理 `D:\` 整盘扫描。

RESULT: FAIL
