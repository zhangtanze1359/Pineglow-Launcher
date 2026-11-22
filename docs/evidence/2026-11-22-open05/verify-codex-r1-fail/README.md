# 第 1 轮独立复验材料（Codex，FAIL）· 原样归档

这些文件由独立复验方（Codex `gpt-6.2-sol`，reasoningEffort=high）在**它自己的**工作树产生，执行者原样复制归档，**未做任何修改**。

- 复验对象：`main@ed2e92c`（r2）
- 结论：**RESULT: FAIL**
- 阻断项：`audit-evidence-tree.ps1` 的 `Substring($EvidenceRoot.Length + 1)` 在相对路径 / 末尾反斜杠 / `..` 段下前缀错位，`$preConvention` 白名单失效，同一树多报「缺 deliverable / manifest」（父提交 `17318e6` 亦复现）。
- 原始工作树：`C:/Users/Administrator/.t3/worktrees/松光启动器/t3-01bed7a7-3338033de2098165`（临时树，可能在清理中消失）

## 文件

| 文件 | 内容 |
| --- | --- |
| `HANDOFF-review-r1.md` | 复验方的交接报告（含 PASS/FAIL、命令、计数对比） |
| `baseline.txt` | 目标提交、范围、CRLF/LF 与反向应用检查 |
| `environment.txt` | 两处检出上的签名策略探针记录 |
| `runner.txt` | 矩阵执行方式 |
| `cleanup.txt` | 变体与夹具清理记录 |
| `always-omitted.patch` | 反证变体①：参数绑定判定改为一律 `$false`（矩阵 6/20） |
| `historical-default.patch` | 反证变体②：默认根改回 ed41（矩阵 14/20） |
| `results.json` | 逐例结果汇总（29 类用例） |
| `raw/` | 225 个原始 stdout/stderr/json（含 `additional/` 的同树路径写法对照） |

## 阅读注意

- 这些文件是**上一轮**的证据，用于追溯 FAIL 的依据；本目录当前的 r3 结论以 `../README.md` 与 `../raw/` 为准。
- 复验方的临时工作树已被它自己移除；此处归档是唯一的原始副本，不要删除。
