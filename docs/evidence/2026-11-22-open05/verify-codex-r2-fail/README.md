# 第 2 轮独立复验材料（Codex，FAIL）· 原样归档

由独立复验方（Codex `gpt-6.2-sol`，reasoningEffort=high）在**它自己的**工作树产生，执行者原样复制，**未做任何修改**。

- 复验对象：`main@932115c`（r3）
- 结论：**RESULT: FAIL**
- 阻断项①：**卷根仍误报**。`Substring($EvidenceRoot.Length + 1)` 对已带分隔符的卷根（`Z:\`）多跳一个字符，`MR00\…` 截成 `R00\…`，历史白名单失效 → 同一自洽夹具在卷根写法下报 2 处、exit 1。
- 阻断项②：**执行证据把直接传参记成了 splat**。`tree-splat-empty` 的命令实为 `-EvidenceRoot ''`，与 `tree-empty` 重复，`29/29` 不能证明 splat 覆盖；另夹具实测哈希与 README 不符、4 个历史探针文件位置需说明。
- 原始工作树：`C:/Users/Administrator/.t3/worktrees/松光启动器/t3-857f93cf-c98b7232fe8581a3`（临时树，可能被清理）

## 文件

| 文件 | 内容 |
| --- | --- |
| `HANDOFF-review-r2.md` | 复验交接（含 PASS/FAIL、对照表、阻断项） |
| `REMEDIATION-PROMPT-r2.md` | 复验方给出的整改执行提示词 |
| `scope-and-environment.txt` | 提交范围与环境（`ed2e92c → 932115c` 仅 +8 行） |
| `worker-evidence-check.txt` | 对执行者 README 与 raw 的逐项核对（含 splat 实为直接传参的证据） |
| `run-summary.txt` / `results.csv` / `results.json` | 逐例结果汇总 |
| `review-open05.py` | 复验方自建的核对脚本 |
| `cleanup.txt` | 变体与工作树清理记录 |
| `raw/` | 复验方的原始输出（含 `drive-candidate.out.txt` 卷根证据） |

## 阅读注意

- 这些是**上一轮**的证据，用于追溯 FAIL 依据；r4 结论以 `../README.md` 与 `../raw/` 为准。
- `raw/` 中 `drive-candidate.out.txt` 是卷根阻断的直接证据，可作为 r4 修复的对照。
