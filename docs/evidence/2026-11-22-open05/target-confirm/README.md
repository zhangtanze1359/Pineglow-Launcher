# OPEN-05 目标分支参数行为确认 · 2026-11-22

按 [open-items](../../../open-items.md) OPEN-05 销项条件「合入后参数行为确认」执行。执行者记录；`FormalPhaseApproval=NOT_GRANTED`，未推送、未打 tag。

## 结论

- **确认对象**：`main@aa64a57596a068d89f802825fe16826f3ab639af`（即合入后的目标分支头提交）。
- **确认方式**：从该提交新建**干净工作树** `D:/Projects/_worktrees/pg-open05-targetconf-20261122`（`git status` 0 改动），直接运行检出内的两支脚本，不注入任何外部补丁。
- **结果：26/26 PASS，exit 0。**
- 卷根写法与同一树的普通绝对路径结论一致（2 run / 0 处 / exit 0）；5 类无效参数均在扫描前失败。
- 真实树上默认根与显式根**逐行 diff = 0**（归一化「证据树 / 来源」两行后），两台脚本分别 61 run / 609 处与 31 包行 / 4 处，exit 1。

## 环境

| 项 | 值 |
| --- | --- |
| 目标提交 | `aa64a57596a068d89f802825fe16826f3ab639af`（`main`） |
| 确认工作树 | `D:/Projects/_worktrees/pg-open05-targetconf-20261122`（detached HEAD 同提交，`git status` 0 改动） |
| 工具 | Windows PowerShell 5.1；Git `2.55.0.windows.4`；Windows 11 Pro for Workstations |
| 时间 | 2026-11-22（UTC+08） |
| 路径长度 | `docs/market-execution` 最深相对路径 205 字符；建树需 `-c core.longpaths=true` |
| 执行策略 | 未修改机器设置；`Bypass` 只出现在单次进程命令行，签名策略结论同前几轮 |

被测脚本哈希（检出自目标提交）：

| 文件 | SHA-256 |
| --- | --- |
| `audit-evidence-tree.ps1` | `E40D4B2F6B4A7B090AC986E36CC4B24BD68230F3A9B25864DCA22360911E99D9` |
| `audit-plan-ledger.ps1` | `5E900796CEE2C4E196CF99D3F55CF377F1444A5AC207EB630C5E1B71BF6B65A5` |

## 用例与结果（26/26）

### A. 入口参数（13 例）

| 用例 | 输入 | 预期 | 实际 |
| --- | --- | --- | --- |
| `entry-tree-default` | 省略 `-EvidenceRoot` | 走 `-RepoRoot\docs\market-execution`，61 run，exit 1 | PASS |
| `entry-tree-blank` / `entry-tree-empty` | `' '` / `''` | `[EVIDENCE_ROOT_BLANK]`，扫描前失败，exit 1 | PASS |
| `entry-tree-missing` | 不存在路径 | `[EVIDENCE_ROOT_MISSING]`，exit 1 | PASS |
| `entry-tree-file` | 普通文件 | `[EVIDENCE_ROOT_NOT_DIRECTORY]`，exit 1 | PASS |
| `entry-tree-no-default` | 默认根缺失 | `[DEFAULT_ROOT_MISSING]`，exit 1 | PASS |
| `entry-ledger-default` | 台账默认根 | 31 包行，exit 1 | PASS |
| `entry-ledger-blank` / `-missing` / `-file` / `-no-default` | 同四类非法输入 | 各自错误标记，扫描前失败，exit 1 | PASS |
| `entry-splat-empty` | **哈希表 splat** `@{RepoRoot=…;EvidenceRoot=''}` | 仍判为显式 → `[EVIDENCE_ROOT_BLANK]`，exit 1 | PASS |
| `entry-spaced-cjk` | 含空格与中文的合法树 | 2 run，自洽，exit 0 | PASS |

### B. 路径写法 × 两棵树（8 例）

同一棵树只改 `-EvidenceRoot` 写法，结论必须不变：

| 写法 | 自洽树 | 含 `MR00\…` 白名单 run 的树 |
| --- | --- | --- |
| 绝对路径 | 1 run / 0 处 / exit 0 | 2 run / 0 处 / exit 0 |
| 末尾加 `\` | 同左 | 同左 |
| 相对路径 | 同左 | 同左 |
| `..\` 相对（从别处启动） | 同左 | 同左 |

8/8 PASS。白名单 run 在所有写法下均被正确豁免——这是 r3/r4 修复要害。

### C. 卷根（2 例）

`subst` 把「含白名单 run」的树映射为 `Z:\`，夹具自建并在 `finally` 解除，运行后 `subst` 无输出确认已清理。

| 用例 | 输入 | 预期 | 实际 |
| --- | --- | --- | --- |
| `drive-root` | `-EvidenceRoot 'Z:\'` | 2 run / 0 处 / exit 0 | PASS |
| `drive-abs-control` | 同一树的普通绝对路径 | 2 run / 0 处 / exit 0 | PASS |

### D. 真实树（3 例）

| 用例 | 结果 |
| --- | --- |
| `real-default` | 61 run / 609 处 / exit 1 |
| `real-explicit` | 61 run / 609 处 / exit 1；与 `real-default` 归一化后逐行 **diff = 0**（各 41 行） |
| `real-ledger-explicit` | 31 包行 / 4 处 / exit 1 |

## 附注：609 与 7 的差异在本轮得到印证

此前执行工作树与主工作区对同一真实树给出不同计数：

- **本确认的干净检出**（仅含 git 跟踪的 1095 个文件）：**609 处**
- **主工作区**（额外有 640 个被 `.gitignore` 的 `*.log` 仍在磁盘）：**7 处**

两者都是同一脚本对**不同文件集合**的真实审计结果，不是行为差异。609 与历史记录（11-19 复验、任务计划）一致，说明目标分支行为与历史口径对齐。

## 复跑命令

```powershell
git -C D:/Projects/松光启动器 -c core.longpaths=true worktree add D:/Projects/_worktrees/pg-open05-targetconf-20261122 aa64a57
cd D:/Projects/_worktrees/pg-open05-targetconf-20261122
powershell.exe -NoProfile -ExecutionPolicy Bypass -File ./_target-confirm.ps1
```

原始输出在 `raw/`（26 例 × `.out.txt` / `.err.txt` / `.txt` 元数据 = 78 个文件；元数据含 `call_body`、`checkout_HEAD`、`actual_exit`、`verdict`）。夹具副本 `fixture.ps1`（SHA-256 `483B96E0…E61F`），与确认工作树内实际运行的 `_target-confirm.ps1` 逐字节相同。

## 未覆盖

- UNC 路径（`\\server\share`）与物理整盘扫描（`D:\` 真盘）；本轮只验证 `subst` 卷根。
- `.NET` / 前端 / CSS token / 资源闭包 / CI / Avalonia-WebView2 宿主 / 安装包 / 发布一致性：本轮只确认两支 PowerShell 审计脚本。
