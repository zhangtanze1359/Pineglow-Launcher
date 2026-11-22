# OPEN-05 执行证据 · 2026-11-22（r3，含第 1 轮复验阻断修复）

执行者记录。目标：复核 r2 补丁身份、可应用性与参数行为，产出可独立复验的原始输出。`FormalPhaseApproval=NOT_GRANTED`，未推送、未打 tag、未改机器签名策略。

**第 1 轮独立复验 FAIL**（Codex `gpt-6.2-sol` high），阻断项是**既有缺陷**：同一证据树换个 `-EvidenceRoot` 写法（相对路径 / 末尾多余反斜杠 / `..` 段）会多报「缺 deliverable / manifest」。r3 修复并补齐回归用例；本节以下全部为 r3 结果。

## 结论摘要

- r2 补丁身份一致：SHA-256 `295CB84E581741AD23BA062226FE19CFF50C2771B176B906F29CF499CF6F53CE`，长度 9212 字节。
- 目标分支 `17318e6` 无等效改动；`git apply --check` → `apply` → `diff --check` 全部 exit 0。
- **参数矩阵 29/29 PASS**：两脚本各覆盖默认根 / 显式同树 / 空串（含 splat 形式）/ 空白 / 不存在 / 普通文件 / 默认根缺失 / `-RepoRoot ''` / 空格中文路径（含省略参数走中文默认根）/ 空证据树，外加 5 档「同一树的路径写法」回归。
- **路径写法回归（r3 新增）**：同一夹具树、同样 2 个 run，绝对路径、末尾反斜杠、相对路径、`.\` 相对、`..\` 相对五种写法全部 `证据树内部自洽`、exit 0。
- **反证**：父提交原脚本在同样 5 档里只有绝对路径自洽；其余 4 档各报 2 处不一致、exit 1。说明回归用例确实能抓到缺陷，不是空洞断言。
- 真实树审计结论如实保留，未通过改统计口径或删证据使其转绿。

## 本机真实树计数与历史记录的差异（必读）

历史记录（11-19 复验、任务计划）记的是 **61 run / 609 处**、台账 **31 包行 / 4 处**。本轮在本机主工作区实测为 **61 run / 7 处**、台账 **31 包行 / 4 处**。差异来源：

- 主工作区 `docs/market-execution` 有 **1735** 个文件，但 `git` 只跟踪 **1095** 个；另外 **640** 个是被 `.gitignore` 的 `*.log` 规则忽略的证据日志（`git status --ignored` 可复核）。
- 干净工作树里这 640 个文件不存在，因此 manifest 记录的 `green/*.log` 等条目缺失，报出大量「记录了但文件不存在」→ 合计 609 处。
- 本机工作区保留了这些日志，只剩 1 处哈希不符 + 2 缺 deliverable + 2 缺 manifest + 2 未登记 = 7 处。

**结论**：609 与 7 都是真实输入的审计结果，差别来自**输入树的文件集合**，不是脚本行为。两者都不能当作“补丁必须达到的固定数字”；验收看**同一输入树的对照一致性**（见 open-items 的结果解释）。

## 环境与输入

| 项 | 值 |
| --- | --- |
| 主工作区 | `D:/Projects/松光启动器`，基线 `ed2e92c`（父 `17318e6`） |
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

`Get-ExecutionPolicy -List`：MachinePolicy / UserPolicy / Process 均 `Undefined`，CurrentUser `RemoteSigned`。脚本均 `NotSigned`、无 `Zone.Identifier`。上述只描述本机；未修改任何执行策略设置。

## 参数矩阵结果（29/29）

夹具 `matrix-fixture.ps1`（SHA-256 `B1ACD7E2…95B7`）；每例独立子进程，原始 stdout/stderr/元数据在 `raw/`。

### audit-evidence-tree.ps1

| 用例 | 输入 | 预期 | 实际 |
| --- | --- | --- | --- |
| `tree-default` | 省略 `-EvidenceRoot` | 默认根，61 run，exit 1 | PASS |
| `tree-override-same` | 显式绝对路径 | 同默认，61 run，exit 1 | PASS |
| `tree-blank` / `tree-empty` | `' '` / `''` | `[EVIDENCE_ROOT_BLANK]`，exit 1 | PASS |
| `tree-splat-empty` | splat 传空值（与直接写等价） | `[EVIDENCE_ROOT_BLANK]`，exit 1 | PASS |
| `tree-missing` | 不存在路径 | `[EVIDENCE_ROOT_MISSING]`，exit 1 | PASS |
| `tree-file` | 普通文件 | `[EVIDENCE_ROOT_NOT_DIRECTORY]`，exit 1 | PASS |
| `tree-reporoot-empty` | `-RepoRoot ''` | 参数绑定错误「为空字符串」，扫描前失败，exit 1 | PASS |
| `tree-missing-default` | 默认根缺失 | `[DEFAULT_ROOT_MISSING]`，exit 1 | PASS |
| `tree-cjk-default` | 省略参数 + 中文 `-RepoRoot` | 2 run，自洽，exit 0 | PASS |
| `tree-spaced-cjk` | 含空格与中文目录 | 2 run，自洽，exit 0 | PASS |
| `tree-pathform-abs` | 绝对路径 | 2 run，自洽，exit 0 | PASS |
| `tree-pathform-trailing` | 末尾多 `\` | 同上 | PASS |
| `tree-pathform-rel` | `tree 中文 空格` | 同上 | PASS |
| `tree-pathform-rel-dot` | `.\tree 中文 空格` | 同上 | PASS |
| `tree-pathform-dotdot` | `..\tree 中文 空格`（cwd 在别处） | 同上 | PASS |
| `tree-empty-tree` | 空证据树 | 0 run，自洽，exit 0 | PASS |

### audit-plan-ledger.ps1

| 用例 | 指标 | 结果 |
| --- | --- | --- |
| `ledger-default` / `ledger-override-same` | 31 包行 / 4 处，exit 1 | PASS |
| `ledger-blank` / `ledger-empty` | `[EVIDENCE_ROOT_BLANK]`，exit 1 | PASS |
| `ledger-missing` | `[EVIDENCE_ROOT_MISSING]`，exit 1 | PASS |
| `ledger-missing-default` | `[DEFAULT_ROOT_MISSING]`，exit 1 | PASS |
| `ledger-file` | `[EVIDENCE_ROOT_NOT_DIRECTORY]`，exit 1 | PASS |
| `ledger-cjk-default` | 省略参数 + 中文 `-RepoRoot`，31 包行，exit 1 | PASS |
| `ledger-spaced-cjk` | 31 包行，孤儿目录，exit 1 | PASS |
| `ledger-empty-tree` | 31 包行，20 处缺证据目录，exit 1 | PASS |
| `ledger-pathform-trailing` / `-rel` | 31 包行 / 4 处，exit 1 | PASS |

> 说明：`ledger-empty-tree` 首轮我把预期写成 exit 0，实际空树必然让检查 2 报缺证据目录并 exit 1——错的是**夹具预期**，不是断言口径；已改正且未放宽判定。

## r3 修复内容

`scripts/audit-evidence-tree.ps1` 在校验成功后、进入扫描前，把 `-EvidenceRoot` 规范成绝对路径前缀：

```powershell
$resolvedRoot = (Resolve-Path -LiteralPath $EvidenceRoot).ProviderPath
$EvidenceRoot = if ($resolvedRoot -match '^[A-Za-z]:\\$') { $resolvedRoot } else { $resolvedRoot.TrimEnd('\', '/') }
```

原因：后续用 `$run.FullName.Substring($EvidenceRoot.Length + 1)` 算相对路径；调用方传相对路径或末尾多余反斜杠时前缀长度错位，`$preConvention` 白名单（`MR00\20261002-072004Z` 等）匹配失效。盘符根（`D:\`）保留尾反斜杠，避免退化成 `D:` 改变扫描与显示语义。

**只改这一处，8 行新增**；两支脚本的审计检查项、白名单、退出码口径均未动。`audit-plan-ledger.ps1` 内部不做同类截取，无需改动。

## 反证（证明回归用例非空洞）

把父提交 `17318e6` 的原脚本跑同样 5 档（输出在 `raw/counterproof-P-*.out.txt`）：

| 写法 | 父提交原脚本 | r3 修复后 |
| --- | --- | --- |
| 绝对路径 | 自洽，exit 0 | 自洽，exit 0 |
| 末尾反斜杠 | **2 处不一致，exit 1** | 自洽，exit 0 |
| 相对路径 | **2 处不一致，exit 1** | 自洽，exit 0 |
| `.\` 相对 | **2 处不一致，exit 1** | 自洽，exit 0 |
| `..\` 相对 | **2 处不一致，exit 1** | 自洽，exit 0 |

同一棵树上，父版本与修复版本在**绝对路径**下逐行输出一致（归一化「证据树 / 来源」两行后 diff=0），证明修复不改变既有正确行为。

## 复跑命令

```powershell
git -C D:/Projects/松光启动器 -c core.longpaths=true worktree add D:/Projects/_worktrees/pg-open05-20261122 17318e6
git -C D:/Projects/_worktrees/pg-open05-20261122 apply --check -- <open05-fix-r2.patch>
git -C D:/Projects/_worktrees/pg-open05-20261122 apply -- <open05-fix-r2.patch>
git -C D:/Projects/_worktrees/pg-open05-20261122 diff --check
powershell.exe -NoProfile -ExecutionPolicy Bypass -File D:/Projects/_worktrees/pg-open05-20261122/_run-matrix.ps1
```

## 产物清单

| 路径 | 内容 | 哈希 |
| --- | --- | --- |
| `open05-fix-r2.patch` | r2 补丁副本 | `295CB84E…53CE` |
| `patched/audit-evidence-tree.ps1` | r3 应用后脚本 | `90E38219…ADCF` |
| `patched/audit-plan-ledger.ps1` | 应用后脚本 | `5E900796…65A5` |
| `matrix-fixture.ps1` | 29 例矩阵夹具 | `B1ACD7E2…95B7` |
| `raw/` | 29 例 × (out/err/meta) + 5 组反证输出 | 101 个文件 |

## 未覆盖 / 边界

- 未执行 `.NET` 单测、前端、CSS token、资源闭包、CI、Avalonia/WebView2 宿主、发布包与发布一致性检查：本轮只改两支 PowerShell 审计脚本。
- `audit-plan-ledger.ps1` 检查 3 的 `claimed` 白名单是脚本内硬编码（新增跨里程碑目录仍须手工登记），属既有设计，不在本补丁范围。
- `-RepoRoot ''` 报的是 PowerShell 参数绑定错误（`无法将参数绑定到参数“Path”，因为该参数为空字符串。`），仍在扫描前失败，错误码不是 `[DEFAULT_ROOT_MISSING]`；属既有行为，未收窄。
- `raw/` 中 `tree-file.*`、`tree-cjk-default.*`、`ledger-file.*`、`ledger-cjk-default.*`、`probe-root-empty.*`、`probe-splat-empty.*` 早期版本文件已按用例名重新生成，不再有孤立历史文件。
