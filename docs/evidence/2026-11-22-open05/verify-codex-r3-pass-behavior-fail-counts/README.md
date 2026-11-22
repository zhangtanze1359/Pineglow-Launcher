# 第 3 轮独立复验材料（Codex）· 原样归档

由独立复验方（Codex `gpt-6.2-sol`，reasoningEffort=high）在**它自己的**工作树产生，执行者原样复制，**未做任何修改**。

- 复验对象：`main@fe49b34`（r4）
- **技术行为：全部通过。** 自建夹具 + 父版本对照 **62/62** 项预期；复跑执行者提交内的夹具 **41/41**。卷根、相对路径、末尾反斜杠、`.\`、`..\` 五类写法在三棵树（自洽 / 含白名单 run / 含真实发现）上结论一致；父版本反证成立（五类写法各 `2/2/1`）；splat 的 `call_body` 确认为真实哈希表 splat。
- **整轮结论：FAIL**，唯一失败判据是**执行证据 README 三处数量明细与提交实物不符**（见下）。
  - r4 stdout/stderr 实为 **82**（41 对），README 写 81
  - 反证输出实为 **父版 10 对 + 修复版 2 对 = 24 文件**，README 写「各 7 组」
  - 第 2 轮归档实为 **93** 个文件，README 写 92

> 这不是被测脚本的缺陷，是执行者自评材料的准确性缺陷。r5（`a5e9730`）已更正三处数字。

## 目录

| 路径 | 内容 |
| --- | --- |
| `HANDOFF-review-r3.md` | 复验交接报告（含卷根表、五写法 × 三树表、父版本反证） |
| `REMEDIATION.md` / `REMEDIATION-PROMPT.md` | 复验方给出的最小整改流程与可复制提示词 |
| `results.json` / `evidence-checks.json` | 逐例结果与证据点验结果 |
| `independent-fixtures.json` / `independent-rerun.stdout.txt` | 复验方自建夹具定义与独立复跑输出 |
| `subst.json` | 盘符映射日志（初始查询与每次解除后均无输出，证明已清理） |
| `scope-parent-chain.txt` / `scope-scripts.diff.txt` / `scope-show-stat.txt` | 提交范围与脚本差异（`932115c → fe49b34` 仅 `audit-evidence-tree.ps1`） |
| `worker-rerun.*` | 复跑执行者夹具（41/41）的进程、stdout、stderr |
| `cleanup.txt` | 工作树清理记录 |
| `review-open05.py` | 复验方自建的核对脚本 |
| `independent-raw/` | 复验方独立夹具的原始输出（124 文件） |
| `verified-run/` | 正式一轮的原始输出与结果（128 文件；`raw/` 与 `independent-raw` 高度重叠，后者为独立夹具首轮） |
| `worker-rerun/` | 执行者夹具复跑的逐例 `out/err/meta`（123 文件） |

三个 raw 子目录的关系：`independent-raw` 与 `verified-run/raw` 是复验方自建夹具的同名输出（后者为正式判定所用那一轮）；`worker-rerun/` 是复验方对**执行者提交内夹具**的复跑。保留全部，便于逐例追溯。

## 阅读注意

- 本目录是**第 3 轮**材料，用于追溯「技术通过但文档计数不符」这一判定；r5 的更正见 `../README.md` 与提交 `a5e9730`。
- 复验方的临时工作树会被清理，此处是唯一原始副本。
