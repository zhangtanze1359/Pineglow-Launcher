# OPEN-05 执行证据 · 2026-11-22（r5）

执行者记录。`FormalPhaseApproval=NOT_GRANTED`，未推送、未打 tag、未改机器签名策略。

**独立复验：第 1、2、3 轮 FAIL，第 4 轮 PASS。** 前两轮阻断为**既有代码缺陷**，第三轮为**本 README 的计数错误**，均已修复：

| 轮次 | 阻断项 | 修复 |
| --- | --- | --- |
| 第 1 轮 | 相对路径 / 末尾反斜杠 / `..` 段使 run 相对路径前缀错位，白名单失效，同树多报 | r3：校验后规范为绝对前缀 |
| 第 2 轮 | **卷根**（`Z:\`）仍错位：根已带分隔符，`+1` 又多跳一个字符；另：`tree-splat-empty` 实为直接传参，不算 splat 覆盖 | r4：截取下标集中计算一次；splat 改为真实哈希表 splat |
| 第 3 轮 | 技术行为**全部通过**（62/62、41/41）；整轮 FAIL 仅因本 README 三处计数与实物不符 | r5：更正为 82 / 24（P 10 对 + F 2 对）/ 93 |
| 第 4 轮 | —（轻量核对） | **PASS**，并补充哈希口径澄清 |

## 结论摘要

- r2 补丁身份一致：SHA-256 `295CB84E581741AD23BA062226FE19CFF50C2771B176B906F29CF499CF6F53CE`，长度 9212 字节。
- 目标分支 `17318e6` 无等效改动；`apply --check` → `apply` → `diff --check` 全部 exit 0。
- **参数与行为矩阵 41/41 PASS**（清单见下）。
- **路径写法回归覆盖三棵树 × 五种写法**：自洽树、含历史白名单 run 的树、含真实发现的树，五种写法各自结论完全一致。
- **卷根回归**：同一棵树以卷根写法与普通绝对路径给出相同结论（2 run / 0 处 / exit 0）。
- **父版本反证**：父脚本在末尾反斜杠、相对路径、`..`、卷根四类写法上均变红（各 2 处、exit 1），修复后全部归零。
- 真实树审计结论如实保留，未通过改统计口径或删证据使其转绿。

## 本机真实树计数与历史记录的差异（必读）

历史记录（11-19 复验、任务计划）记 **61 run / 609 处**、台账 **31 包行 / 4 处**。本机主工作区实测 **61 run / 7 处**、台账 **31 包行 / 4 处**。

- 主工作区 `docs/market-execution` 有 **1735** 个文件，git 只跟踪 **1095** 个；另有 **640** 个被 `.gitignore` 的 `*.log` 规则忽略的证据日志仍在磁盘（`git status --ignored=matching` 可复核）。
- 干净工作树缺这 640 个文件，manifest 记录的 `green/*.log` 等条目因此「记录了但文件不存在」，累计 609 处。
- 本机保留这些日志，只剩 1 处哈希不符 + 2 缺 deliverable + 2 缺 manifest + 2 未登记 = 7 处。

**609 与 7 都是真实输入的结果，差异来自输入文件集合，不是脚本行为。** 验收看**同一输入树的对照一致性**，不把任何一个数字当作补丁必须达到的固定值。

## 环境与输入

| 项 | 值 |
| --- | --- |
| 主工作区 | `D:/Projects/松光启动器` |
| 隔离工作树 | `D:/Projects/_worktrees/pg-open05-20261122`，detached HEAD `17318e6` |
| 工具 | Windows PowerShell 5.1，Git `2.55.0.windows.4`，Windows 11 Pro for Workstations |
| 时间 | 2026-11-22（UTC+08） |
| 路径长度 | `docs/market-execution` 最深相对路径 205 字符；建工作树需 `-c core.longpaths=true` |

### 签名策略实测

| 场景 | 结果 |
| --- | --- |
| 主工作区路径 + 无进程策略 | `SecurityError` / `UnauthorizedAccess`，exit 1，未进入审计 |
| `_worktrees/` ASCII 路径 + 干净环境（清空 `PSExecutionPolicyPreference`） | 进入审计逻辑 |
| 任意路径 + `-ExecutionPolicy Bypass`（脚本头部注释写法） | 进入审计逻辑 |

`Get-ExecutionPolicy -List`：MachinePolicy / UserPolicy / Process 均 `Undefined`，CurrentUser `RemoteSigned`。脚本 `NotSigned`、无 `Zone.Identifier`。只描述本机观测；未修改任何执行策略设置。

## 参数与行为矩阵（41/41）

夹具 `matrix-fixture.ps1`。每例独立子进程；每条元数据 `.txt` 里都含 `call_body`（**实际调用语句原文**）与 `decoded`（完整子进程脚本），便于逐字核对调用方式。

> **哈希取样口径**：本仓库 `.gitattributes` 只对 `diagnostic-cases.json`、`.githooks/**`、`scripts/*.sh` 强制 LF，对 `.ps1` 无规则，而 `core.autocrlf=true`。因此同一夹具在不同检出下字节可能不同：
> - **LF 原始字节**（Git blob 与本次执行者主工作区的实际内容）：`7D354D07BA25B76B92E77030959AA7BEA79546C735D4C269F4DD276449049214`（0 CRLF / 222 bare LF）
> - **CRLF 检出**（第 3 轮复验方在其独立工作树实测）：`DF30F7319B1210824516755A5F0F97DA8CF78E6109E753E5C0EF589A0BDD5B3E`
>
> 两者内容等价（仅换行不同）。引用哈希时须同时注明口径，否则会误判为「文件被改动」。

### audit-evidence-tree.ps1（29 例）

| 用例 | 输入 | 预期 | 实际 |
| --- | --- | --- | --- |
| `tree-default` | 省略 `-EvidenceRoot` | 默认根，61 run，exit 1 | PASS |
| `tree-override-same` | 显式绝对路径 | 同默认，exit 1 | PASS |
| `tree-blank` / `tree-empty` | `' '` / `''` | `[EVIDENCE_ROOT_BLANK]`，扫描前失败，exit 1 | PASS |
| `tree-splat-empty` | **哈希表 splat** `@{RepoRoot=…;EvidenceRoot=''}` | 仍判为显式 → `[EVIDENCE_ROOT_BLANK]`，exit 1 | PASS |
| `tree-missing` | 不存在路径 | `[EVIDENCE_ROOT_MISSING]`，exit 1 | PASS |
| `tree-file` | 普通文件 | `[EVIDENCE_ROOT_NOT_DIRECTORY]`，exit 1 | PASS |
| `tree-missing-default` | 默认根缺失 | `[DEFAULT_ROOT_MISSING]`，exit 1 | PASS |
| `tree-reporoot-empty` | `-RepoRoot ''` | 参数绑定错误「为空字符串」，扫描前失败，exit 1 | PASS |
| `tree-cjk-default` | 省略参数 + 中文 `-RepoRoot` | 2 run，自洽，exit 0 | PASS |
| `tree-spaced-cjk` | 含空格与中文目录 | 2 run，自洽，exit 0 | PASS |
| `tree-empty-tree` | 空证据树 | 0 run，自洽，exit 0 | PASS |

**路径写法 × 三棵树（15 例）**：写法为 绝对 / 末尾 `\` / 相对 / `.\` 相对 / `..\` 相对（从别处启动）。

| 树 | 内容 | 五种写法预期 | 实际 |
| --- | --- | --- | --- |
| `tree-form-self-*` | 1 个自洽 run | 自洽，exit 0 | 5/5 PASS |
| `tree-form-legacy-*` | 1 自洽 + `MR00\…` 白名单 run | 自洽，exit 0（白名单必须命中） | 5/5 PASS |
| `tree-form-dirty-*` | 1 自洽 + 1 真实发现 | 1 处，exit 1 | 5/5 PASS |

**卷根回归（2 例）**：把 `tree 中文 空格` 用 `subst` 映射成空闲卷根（如 `X:\`）。

| 用例 | 输入 | 预期 | 实际 |
| --- | --- | --- | --- |
| `tree-drive-root` | `-EvidenceRoot 'X:\'` | 2 run，自洽，exit 0（与普通绝对路径一致） | PASS |
| `tree-drive-trail` | 同一树的普通绝对路径（对照） | 2 run，自洽，exit 0 | PASS |

盘符映射由夹具创建、`finally` 中解除，并在运行后以 `subst`（无输出）确认。

### audit-plan-ledger.ps1（12 例）

| 用例 | 指标 | 结果 |
| --- | --- | --- |
| `ledger-default` / `ledger-override-same` | 31 包行 / 4 处，exit 1 | PASS |
| `ledger-blank` / `ledger-empty` | `[EVIDENCE_ROOT_BLANK]`，exit 1 | PASS |
| `ledger-missing` | `[EVIDENCE_ROOT_MISSING]`，exit 1 | PASS |
| `ledger-file` | `[EVIDENCE_ROOT_NOT_DIRECTORY]`，exit 1 | PASS |
| `ledger-missing-default` | `[DEFAULT_ROOT_MISSING]`，exit 1 | PASS |
| `ledger-cjk-default` | 省略参数 + 中文 `-RepoRoot`，31 包行，exit 1 | PASS |
| `ledger-spaced-cjk` | 显式空格中文目录，31 包行，exit 1 | PASS |
| `ledger-empty-tree` | 空树 → 检查 2 报缺证据目录，exit 1 | PASS |
| `ledger-pathform-trailing` / `-rel` | 31 包行 / 4 处，exit 1 | PASS |

## 修复内容

`scripts/audit-evidence-tree.ps1`，两处（合计 +19 行，审计项 / 白名单 / 退出码语义未动）：

1. **r3**：校验通过后规范成绝对前缀。
   ```powershell
   $resolvedRoot = (Resolve-Path -LiteralPath $EvidenceRoot).ProviderPath
   $EvidenceRoot = if ($resolvedRoot -match '^[A-Za-z]:\\$') { $resolvedRoot } else { $resolvedRoot.TrimEnd('\', '/') }
   ```
2. **r4**：run 相对路径的截取下标只算一次，按根是否已带分隔符决定。
   ```powershell
   $relativePathStart = if ($EvidenceRoot.EndsWith('\') -or $EvidenceRoot.EndsWith('/')) {
       $EvidenceRoot.Length
   } else { $EvidenceRoot.Length + 1 }
   ```
   四处报告点（缺 deliverable、缺 manifest、manifest 无法解析、runId 不符）统一改用 `$runRelative`。

`audit-plan-ledger.ps1` 内部不做同类截取，无需改动（`ed2e92c → 932115c` 差异为空）。

## 反证（证明用例非空洞）

父版本取自 `ed2e92c`（`git show ed2e92c:scripts/audit-evidence-tree.ps1`，SHA-256 `649B9B1A…202A`）。输出在 `raw/counterproof-P-*.out.txt` 与 `raw/counterproof-F-*.out.txt`。

| 写法 | 父版本 `ed2e92c` | r4 修复后 |
| --- | --- | --- |
| 绝对路径（自洽 / 含白名单树） | 自洽，exit 0 | 自洽，exit 0 |
| 末尾反斜杠 | **2 处，exit 1** | 自洽，exit 0 |
| 相对路径 | **2 处，exit 1** | 自洽，exit 0 |
| `.\` 相对 | **2 处，exit 1** | 自洽，exit 0 |
| `..\` 相对 | **2 处，exit 1** | 自洽，exit 0 |
| **卷根 `P:\`** | **2 处，exit 1**（同树绝对路径为 0） | 自洽，exit 0 |

真实树上，父版本与修复版本在普通绝对路径下逐行一致（归一化「证据树 / 来源」两行后 diff=0），说明修复不改变既有正确行为。

## 复跑命令

```powershell
git -C D:/Projects/松光启动器 -c core.longpaths=true worktree add D:/Projects/_worktrees/pg-open05-20261122 17318e6
git -C D:/Projects/_worktrees/pg-open05-20261122 apply --check -- <open05-fix-r2.patch>
git -C D:/Projects/_worktrees/pg-open05-20261122 apply -- <open05-fix-r2.patch>
git -C D:/Projects/_worktrees/pg-open05-20261122 diff --check
powershell.exe -NoProfile -ExecutionPolicy Bypass -File D:/Projects/_worktrees/pg-open05-20261122/_run-matrix.ps1
```

## 产物清单（实测）

| 路径 | 内容 | 实测 SHA-256 / 数量 |
| --- | --- | --- |
| `open05-fix-r2.patch` | r2 补丁副本 | `295CB84E…53CE` |
| `patched/audit-evidence-tree.ps1` | r4 应用后脚本 | `E40D4B2F…99D9` |
| `patched/audit-plan-ledger.ps1` | 应用后脚本 | `5E900796…65A5` |
| `matrix-fixture.ps1` | 41 例矩阵夹具 | `7D354D07…9214` |
| `raw/` | 166 个文件：41 例元数据 + out/err、24 个反证输出、4 个历史探针、15 个 r3 遗留输出 | 见下 |

**`raw/` 构成明细**（避免把历史文件当成本轮凭据）：

| 类别 | 数量 | 说明 |
| --- | --- | --- |
| r4 用例元数据（`*.txt`） | 41 | 含 `call_body` 与 `decoded`，逐例判定依据 |
| r4 用例 out/err | 82 | 41 例 × 2（41 个 `.out.txt` + 41 个 `.err.txt`） |
| 反证输出（`counterproof-*`） | 24 | **父版本 10 对（20 文件）+ 修复版 2 对（4 文件）** |
| 历史探针（`probe-*`） | 4 | r2 阶段一次性探针，结论已并入常驻用例 |
| r3 遗留（`tree-pathform-*`） | 15 | r3 的路径写法输出；**已由 r4 的 `tree-form-*` 用例取代**，保留仅为不销毁历史证据 |

**r3 遗留文件**（`tree-pathform-abs/trailing/rel/rel-dot/dotdot` 及相关）：来自 r3 夹具，r4 改为「三棵树 × 五种写法」的 `tree-form-{self,legacy,dirty}-*` 命名。按要求保留原文件，但**不作 r4 通过凭据**：r4 结论只看 `tree-form-*` 与 `tree-drive-*` 的元数据。
| `verify-codex-r1-fail/` | 第 1 轮 FAIL 材料原样归档 | 234 个文件 |
| `verify-codex-r2-fail/` | 第 2 轮 FAIL 材料原样归档（含卷根阻断原始输出与整改提示词） | 93 个文件 |
| `verify-codex-r3-pass-behavior-fail-counts/` | 第 3 轮材料：**技术行为全部通过**（62/62、41/41），整轮 FAIL 仅因本 README 三处计数错误 | 递归 **392** 个文件（其中归档说明 `README.md` 1 个、独立复验材料 **391** 个） |
| `verify-codex-r4-pass/` | **第 4 轮 PASS**：三处计数逐项复核一致，确认仅改 README、脚本/raw/夹具无变更 | 3 个文件 |
| `target-confirm/` | **目标分支参数行为确认**（`aa64a57` 干净检出，26/26 PASS；真实树默认 vs 显式 diff=0） | 80 个文件 |
| `verify-codex-close-review-fail/` | **销项复核（Codex）FAIL**：技术 41/41 通过；阻断为归档计数 392≠391 与「合入早于批准」 | 182 个文件 |

**历史探针文件**（`raw/probe-root-empty.*`、`raw/probe-splat-empty.*`，共 4 个）：来自 r2 阶段的一次性边界探针，结论已并入常驻用例（`tree-reporoot-empty`、`tree-splat-empty`）。按要求保留原文件，不作 r4 通过凭据。

## 未覆盖 / 边界

- 未执行 `.NET` 单测、前端、CSS token、资源闭包、CI、Avalonia/WebView2 宿主、安装包与发布一致性检查：本轮只改两支 PowerShell 审计脚本。
- 未验证 UNC 路径（`\\server\share`）与物理整盘扫描（`D:\` 真盘），只验证了 `subst` 卷根。
- `-RepoRoot ''` 报 PowerShell 参数绑定错误（`无法将参数绑定到参数“Path”，因为该参数为空字符串。`），仍在扫描前失败，错误码不是 `[DEFAULT_ROOT_MISSING]`；属既有行为。
- `audit-plan-ledger.ps1` 检查 3 的 `claimed` 白名单是脚本内硬编码（新增跨里程碑目录仍须手工登记），属既有设计。
- 未覆盖执行者主工作区「含 640 个本地未跟踪日志」这一输入下的真实树计数作为验收数字（只作为差异说明）。

## 独立复验状态

| 轮次 | 对象 | 结论 | 关键依据 |
| --- | --- | --- | --- |
| 1 | `ed2e92c` | FAIL | 相对路径 / 末尾反斜杠 / `..` 同树多报 |
| 2 | `932115c` | FAIL | 卷根误报；splat 实为直接传参 |
| 3 | `fe49b34` | FAIL（技术通过） | 62/62、41/41 全过；仅 README 计数不符 |
| 4 | `a5e9730` | **PASS** | 三处计数复核一致；补哈希口径 |

**合入后目标分支确认已完成**：在 `aa64a57` 的干净工作树直接运行检出内脚本，26/26 PASS——入口参数、路径写法 × 两棵树、卷根与普通绝对路径对照、真实树默认 vs 显式根逐行 diff=0。详见 [`target-confirm/`](target-confirm/README.md)。第 4 轮未重跑的 41 例矩阵与卷根/splat 由此轮补齐。

## 销项复核：FAIL（待编排方处理顺序偏差）

第 5 关（销项复核，Codex）**技术复验 41/41 通过**，但判 FAIL，两条：

1. **归档计数**：第 3 轮递归实为 **392**（含归档说明 `README.md`），本 README 与归档说明原写 391。已更正为「递归 392，其中归档说明 1 个、独立复验材料 391 个」。
2. **合入早于批准**：补丁首次进入 `main` 为 `ed2e92c` @ **22:21:28**，最终脚本 `fe49b34` @ **23:05:20**，而编排方批准合入为 **23:29:00**。清单执行步骤要求「批准后方可合入」，事前批准缺证据。

> 第 2 条属**审批顺序**问题，不是技术缺陷。按项目规则「执行者不为自己签字」，该偏差需由编排方留下正式决定后方可再复核；执行者不自行认定已获豁免。材料见 [`verify-codex-close-review-fail/`](verify-codex-close-review-fail/README.md)。
