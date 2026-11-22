# OPEN-05 第二轮执行证据 · 2026-11-22

执行者记录。目标：在目标基线 `main@17318e6` 的**隔离工作树**上复核 r2 补丁的身份、可应用性与参数矩阵行为，产出可独立复验的原始输出。`FormalPhaseApproval=NOT_GRANTED`，未推送、未打 tag、未改机器签名策略。

## 结论摘要

- r2 补丁身份一致：SHA-256 `295CB84E581741AD23BA062226FE19CFF50C2771B176B906F29CF499CF6F53CE`，长度 9212 字节（副本 `open05-fix-r2.patch` 复算同值）。
- 目标分支 `17318e6` **无等效改动**：两支脚本仍指向已不存在的 `ed41` 历史交付根，工作区 blob 为 `0fa604c` / `bee2aaa`，与补丁 `index` 行完全一致。
- `git apply --check` exit 0 → `git apply` exit 0 → `git diff --check` exit 0；应用后哈希 `E8DFDB01…644B`（evidence-tree）/ `5E900796…65A5`（plan-ledger）。
- **参数矩阵 20/20 PASS**：两脚本各 10 档，含省略参数取默认根、显式同树、空串/空白/不存在/普通文件/默认根缺失在扫描前拒绝、含空格与中文的合法路径、空证据树。
- **同输入树对照**：默认目录与显式目录的输出在归一化「证据树 / 来源」两行后**逐行 diff = 0**（tree 41 行、ledger 27 行），差异计数与退出码一致。
- 真实树审计结论是历史输入的真实发现（tree 61 run / 609 处不一致，ledger 31 包行 / 4 处不一致），本轮**未**通过改统计口径或删证据使其转绿。

## 环境与输入

| 项 | 值 |
| --- | --- |
| 主工作区 | `D:/Projects/松光启动器`，HEAD `17318e664d7070cb10a93bb06f96efbb97d72607`（`main`） |
| 隔离工作树 | `D:/Projects/_worktrees/pg-open05-20261122`，detached HEAD 同 `17318e6` |
| 补丁源 | `D:/Projects/_worktrees/pineglow-open05-worker-20261119/docs/evidence/2026-11-19-open05-worker/r2/open05-fix-r2.patch`（worker 树 HEAD `291c819`，两脚本仍为未提交 `M`） |
| 工具 | Windows PowerShell 5.1（`powershell.exe`），Git `2.55.0.windows.4`，Windows 11 Pro for Workstations |
| 时间 | 2026-11-22（UTC+08） |
| 路径长度 | `docs/market-execution` 最深相对路径 205 字符；建工作树需 `-c core.longpaths=true`（否则 `Filename too long`） |

### 签名策略实测（解释旧记录“入口前被拦截”）

| 场景 | 结果 |
| --- | --- |
| 主工作区路径 + 不设进程策略 | `SecurityError` / `UnauthorizedAccess`，exit 1，未进入审计 |
| `_worktrees/` ASCII 路径 + 干净环境（`set PSExecutionPolicyPreference=`） | 进入审计逻辑，退出码由审计发现决定 |
| 任意路径 + `-ExecutionPolicy Bypass`（脚本头部注释给出的用法） | 进入审计逻辑 |

`Get-ExecutionPolicy -List`：MachinePolicy / UserPolicy / Process 均 `Undefined`，CurrentUser `RemoteSigned`。三份脚本 `Get-AuthenticodeSignature = NotSigned`，无 `Zone.Identifier` 备用流。上述只描述本机观测，不归纳为通用规律；本轮未修改任何执行策略设置（`Bypass` 仅出现在单次进程命令行）。

## 参数矩阵结果

矩阵脚本 `matrix-fixture.ps1`（SHA-256 `C33EA071…BA9B`）；每个用例独立起 `powershell.exe` 子进程，子进程环境显式清空 `PSExecutionPolicyPreference`，原始 stdout/stderr/元数据落在 `raw/`（每例 3 个文件，共 60 个）。

### audit-evidence-tree.ps1

| 用例 | 输入 | 预期 | 实际 |
| --- | --- | --- | --- |
| `tree-default` | 省略 `-EvidenceRoot`，`-RepoRoot` = 真实检出 | 默认根 `docs\market-execution`，61 run / 609 处 / exit 1 | PASS |
| `tree-override-same` | 显式 `-EvidenceRoot` = 同一真实树 | 与 default 同内容同计数，exit 1 | PASS（diff=0） |
| `tree-blank` | `-EvidenceRoot ' '` | `[EVIDENCE_ROOT_BLANK]`，exit 1 | PASS |
| `tree-empty` | `-EvidenceRoot ''` | `[EVIDENCE_ROOT_BLANK]`，exit 1 | PASS |
| `tree-missing` | `-EvidenceRoot` 不存在路径 | `[EVIDENCE_ROOT_MISSING]`，不回退，exit 1 | PASS |
| `tree-file` | `-EvidenceRoot` 普通文件 | `[EVIDENCE_ROOT_NOT_DIRECTORY]`，exit 1 | PASS |
| `tree-missing-default` | 省略参数，`-RepoRoot` 无 `docs\market-execution` | `[DEFAULT_ROOT_MISSING]`，exit 1 | PASS |
| `tree-spaced-cjk` | 显式含空格与中文的合法树（1 个自洽 run） | 正常审计，exit 0 | PASS |
| `tree-cjk-default` | 省略参数，`-RepoRoot` 含空格与中文 | 正常审计，exit 0 | PASS |
| `tree-empty-tree` | 显式空证据树 | 0 run，自洽，exit 0 | PASS |

### audit-plan-ledger.ps1

| 用例 | 输入 | 预期 | 实际 |
| --- | --- | --- | --- |
| `ledger-default` | 省略 `-EvidenceRoot`，默认台账 | 31 包行 / 4 处 / exit 1 | PASS |
| `ledger-override-same` | 显式同一真实树 | 与 default 同内容同计数，exit 1 | PASS（diff=0） |
| `ledger-blank` | `-EvidenceRoot ' '` | `[EVIDENCE_ROOT_BLANK]`，exit 1 | PASS |
| `ledger-empty` | `-EvidenceRoot ''` | `[EVIDENCE_ROOT_BLANK]`，exit 1 | PASS |
| `ledger-missing` | 不存在路径 | `[EVIDENCE_ROOT_MISSING]`，exit 1 | PASS |
| `ledger-file` | 普通文件 | `[EVIDENCE_ROOT_NOT_DIRECTORY]`，exit 1 | PASS |
| `ledger-missing-default` | 省略参数，默认根缺失 | `[DEFAULT_ROOT_MISSING]`，exit 1 | PASS |
| `ledger-spaced-cjk` | 显式含空格与中文的合法树 + 真实台账 | 正常审计，孤儿目录 PKG-A，exit 1 | PASS |
| `ledger-cjk-default` | 省略参数，`-RepoRoot` 含空格与中文 | 同上，exit 1 | PASS |
| `ledger-empty-tree` | 显式空证据树 + 真实台账 | 检查 2 报 20 处缺证据目录，exit 1 | PASS |

合计 `fixtureRuns=2 cases=20 PASS=20 FAIL=0`，matrix exit 0。

> 说明：`ledger-empty-tree` 的预期在首轮被我写错（当成 exit 0）。空树必然让检查 2 找不到任何声称“证据就绪”的包目录，报 20 处不一致并 exit 1，这是被测脚本的正确行为；改的是**夹具预期**，不是断言口径，也未放宽任何判定。

## 复跑命令

```powershell
# 建隔离工作树（需长路径支持）
git -C D:/Projects/松光启动器 -c core.longpaths=true worktree add D:/Projects/_worktrees/pg-open05-20261122 17318e6

# 应用 r2 补丁并核验
git -C D:/Projects/_worktrees/pg-open05-20261122 apply --check -- <open05-fix-r2.patch>
git -C D:/Projects/_worktrees/pg-open05-20261122 apply -- <open05-fix-r2.patch>
git -C D:/Projects/_worktrees/pg-open05-20261122 diff --check

# 参数矩阵（20 例，含原始输出落盘）
powershell.exe -NoProfile -ExecutionPolicy Bypass -File D:/Projects/_worktrees/pg-open05-20261122/_run-matrix.ps1
```

## 产物清单

| 路径 | 内容 | 哈希 |
| --- | --- | --- |
| `open05-fix-r2.patch` | r2 补丁副本 | `295CB84E…53CE`（与历史记录、矩阵输入一致） |
| `patched/audit-evidence-tree.ps1` | 应用后脚本 | `E8DFDB01…644B` |
| `patched/audit-plan-ledger.ps1` | 应用后脚本 | `5E900796…65A5` |
| `matrix-fixture.ps1` | 矩阵夹具（含建树与逐例执行） | `C33EA071…BA9B` |
| `raw/` | 20 例 × (out/err/meta) | — |

## 补丁逐行审查要点（执行者自审，待独立复核）

1. **判定依据是脚本级参数绑定状态**：`$PSBoundParameters.ContainsKey('EvidenceRoot')`。调用方用 splat（`@h`）显式传入空值也判定为「显式」——探针 `raw/probe-splat-empty.*` 得到 `[EVIDENCE_ROOT_BLANK]`，行为一致。
2. **`-RepoRoot` 未做空白判定**：`-RepoRoot ''` 会在 `Join-Path` 处抛出 `Cannot bind argument to parameter 'Path' because it is an empty string.`（`raw/probe-root-empty.*`，exit 1）。这是**参数校验错误**、仍在扫描前失败，错误码不是约定的 `[DEFAULT_ROOT_MISSING]`；属既有行为，补丁未收窄。
3. **`-RepoRoot` 省略时**由 `Resolve-Path (Join-Path $PSScriptRoot '..')` 解析，是 `$PSScriptRoot` 相对脚本位置，不依赖调用 cwd。
4. **编码未变**：两支脚本 HEAD 与补丁后首 3 字节均为 `239,187,191`（UTF-8 BOM），补丁只增注释与入口判定块。
5. **审计逻辑主体未改**：检查项、白名单与退出码口径保持原样，补丁不触碰统计语义。默认根与显式根的输出在归一化「证据树 / 来源」两行后逐行一致（diff=0）。
6. **诊断行新增**：两脚本各新增一行 `来源 = …`，便于区分默认/显式；只影响展示，不影响退出码。

## 未覆盖 / 边界

- 未执行 `.NET` 单测、前端、CSS token、资源闭包等仓库门槛：本轮只验收审计脚本路径补丁，不含产品代码改动。
- 真实 Avalonia/WebView2 宿主、发布包、CI 三平台运行均未触及。
- `audit-plan-ledger.ps1` 检查 3 的 `claimed` 白名单是脚本内硬编码（新增跨里程碑目录仍须手工登记），属既有设计，不在本补丁范围。
- 历史 16/16 与 11-19 独立复验记录保留原处，未改动其日志。
