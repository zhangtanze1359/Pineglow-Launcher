# FINAL-LOCAL-QUALITY — 松光启动器 G2/G4 有限返工交付包

**轮次：** LONG-QUALITY-REWORK（2026-10-04）
**目标：** 关闭 Codex 独立验收（2026-10-04，`technicalAcceptance = FAIL`）提出的 LQ-01 ~ LQ-06，
建立“不会把取消意图、旧测试证据、变更输入或空执行当成完成”的本机质量闭环。

**结论：** 六项全部关闭，`implementationReady = true`。
`FormalPhaseApproval = NOT_GRANTED`，`ReleaseApproval = NOT_GRANTED`。
**具备独立复验条件，等待 Codex 独立验收。**

## 包内结构

| 路径 | 内容 |
| --- | --- |
| `README.md` | 本文件：索引与复验入口 |
| `LIMITATIONS.md` | 明确未验证项与边界 |
| `report/R0-baseline-and-red.md` | 现场冻结、环境、**未修产品上的真实 RED** |
| `report/R1-import-terminal.md` | LQ-01 修复（RED → GREEN） |
| `report/R2-runner-contract.md` | LQ-02..06 运行器契约（含本轮一并修复的退出码缺陷） |
| `report/R3-regression.md` | GUID 副本级联回归、场景矩阵、恢复演示、Resume 复用 |
| `report/R4-delivery.md` | 终态、改动清单、机器 verdict、封存流程 |
| `report/LQ-01..LQ-06-closure.md` | 六项逐条 RED/GREEN 台账 |
| `verdict/machine-verdict.json` | 机器可读总判定（gates / lq / approvals / limitations） |
| `verdict/counts.json` | 全部计数与绑定 SHA |
| `verdict/changed-files.json` | 本轮真实改动与终态漂移 |
| `verdict/scenario-matrix.json` | 28 项场景矩阵（绑定测试文件 SHA 与原始日志 SHA） |
| `verdict/recovery-demo.json` | 失败 → 恢复 / 输入变更 → 新 runId 演示 |
| `source/` | R0 基线、R3 基线、终态清单、副本核对、执行根 |
| `runner/` | 真实 stage 定义、run-binding、级联 verdict、Resume verdict、归档 verdict、stage 记录 |
| `attempts/<stage>/<n>/` | **每个 attempt 自己的** stdout/stderr 日志与 TRX（不可覆盖） |
| `independent/r0-red/` | 未修产品上的独立反证（运行器 7 例 + 导入终态 3 例） |
| `independent/r3-reproof/` | 修后同一独立仪器的复跑结果（期望未改动） |
| `audit/` | 审计器真实 3 根覆盖审计（只读运行） |
| `scenario/` | 场景矩阵原始 TAP 日志与结果 |

## 复验入口（包外分离摘要见同级 `../<runId>.manifest.sha256`）

1. **完整性**：按 `manifest.json` 逐项核对包内每个文件的存在、字节数与 SHA256；
   确认没有未登记文件；核对外部分离摘要等于 `manifest.json` 自身的 SHA256。
   包外审计结果见同级 `../out-of-package-audit.json` 与 `../post-seal-reverify.json`。
2. **RED 证据**：`independent/r0-red/runner-disproof-results.json` 应为 7 个 case 全部
   `exitCode = 0` 且 `verdict = PASS`（未修产品的误报）；`independent/r0-red/import-terminal-disproof.log`
   应为 3 执行 / 1 通过 / 2 失败。
3. **GREEN 证据**：`independent/r3-reproof/runner-disproof-results.json` 应为 7 个 case 全部非零退出；
   `independent/r3-reproof/import-terminal-disproof.log` 应为 3/3 通过。
4. **级联回归**：`runner/runner-verdict.json` 应为 `PASS`、`stageCount = 10`、`failedCount = 0`、
   `verifiedInputCount = 292`；`attempts/*/1/results/*.trx` 的计数器与
   `verdict/counts.json` 一致（C# 73/73、Node 269、运行器 28、审计器 26、Bisect 19）。
5. **场景矩阵**：`verdict/scenario-matrix.json` 为 28/28，IMPORT-03/04 各由两条测试共同支撑，
   且 `rawLogSha256` 与 `scenario/node-goal-core-flow.log` 相符。
6. **恢复**：`verdict/recovery-demo.json` 为 `DEMO_PASS`，其中
   `attempt1EvidenceUnchanged = true`、`supersededRecordArchivedByteIdentical = true`。
7. **重跑**（可选，需自建 GUID 副本）：把 `source/r3-input-manifest.json` 的 292 项复制到新的
   临时执行根并逐项核 SHA，然后用该副本里的
   `scripts/run-goal-validation.ps1 -InputManifest <manifest> -RunDir <new> -StagesFile runner/goal-stages.json
   -ExecutionRoot <copy> -Resume`；不兼容时应以 exit 2 明确拒绝，而不是复用或覆盖。

## 复验者须知

- 本包**只读**：复验请把需要的文件复制到新的临时目录再运行会写输出的脚本，不要在本包内重跑。
- 所有测试只在本轮 GUID 执行副本内运行，从未在 `D:\松光启动器` 内构建或写结果。
- 本包不包含也不依赖任何可被覆盖的绝对工作目录文件；包内引用一律为包内相对路径。
