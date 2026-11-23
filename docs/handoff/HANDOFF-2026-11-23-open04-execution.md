# 交接 · OPEN-04 更新替换脚本偶发失败复跑（2026-11-23）

按 [任务计划](../任务计划-2026-11-22.md) 第三节 OPEN-04 执行。**结论：在当前记录的 Windows 机器与负载条件下未复现**（该限定不可省略）。`FormalPhaseApproval=NOT_GRANTED`，未推送、未打 tag。

## 状态

- 基线：`main@367ab91f008b0c4642222bd4528f58cfc955ea3b`。
- 执行工作树：`D:/Projects/_worktrees/pg-open04-20261123`（detached HEAD 同基线，`git status` 0 改动）。
- 环境：Windows 11 Pro for Workstations，16 逻辑核；.NET SDK `10.0.301`；PATH 前置 `C:\Windows\System32;C:\Windows`；TEMP 在工作树内。
- **未修改任何产品代码或测试代码**（本轮只复跑与取证）。

## 复跑结果（分层）

| 层 | 条件 | 结果 |
| --- | --- | --- |
| 1 | 单跑 `ApplyScript_WaitsForRunningTargetThenReplaces` × 10 | **10/10 通过**，0 跳过，每轮 5 s |
| 2 | `UpdateApplyScriptPathTests` × 5 / `UpdateConcurrencyTests` × 5（串行） | **9/9、8/8 每轮全过** |
| 3 | 全量 1502 条 × 3（默认并行） | **1502/1502 全过**，各约 72 s |
| 4 | 全量 + 合成 CPU/IO 压力 × 3 | **1502/1502 全过**，各约 84 s（较无压力 **+16.03%**；负载已起但**未采集强度/持续时间遥测**，故「负载持续生效」属未充分证明） |

**合计 6 轮全量（1502 × 6 = 9012 次）+ 第 1、2 层 95 次 = 9107 次通过，0 失败 0 跳过。**

> 更正（2026-11-23，第 2 次独立复核后）：早前写「9 次全量」「4517 次用例」，均无法由日志复算——实际日志只有 6 轮全量。另有 8 次时序采集运行的日志缺 xUnit 计数字段，不并入上表。

时序证据：目标用例独立采集 8 次，`state=applied`，`waited` 4.7–4.9 s（均值 4.74），**全部 ≥ 2 s**，与 sleeper 生命周期（`ping -n 6` ≈ 5 s）吻合。该日志未记录 passed/failed/skipped/total，故只作时序证据、不计入 xUnit 计数。

## 与历史相比已消除的成因

| 成因 | 历史 | 当前 |
| --- | --- | --- |
| PATH 抢占（裸写 `find` 命中 GNU find） | 曾导致等待循环被跳过 | 产品脚本全部 `"%SystemRoot%\System32\<tool>.exe"` |
| `tasklist` 镜像名截断 | 默认表格截到 25 字符 | `/FO CSV /NH` |
| 等待无上界 | 无 | `MaxRunningProcessWaitIterations = 300` |
| 夹具继承宿主 PATH | 是 | 显式 `System32;Windows;System32\Wbem` |

## 发现的结构性风险（未修，供独立复核判断）

两处夹具在 `WaitForExit(30000)` 超时后**忽略返回值、也不校验子进程退出码**，直接读文件与状态：

- `UpdateApplyScriptPathTests.cs:73`（`RunApplyScript`）—— **6 个**执行脚本的用例共享（调用点 103、140、173、247、289、344 行）
- `UpdateRecoveryTests.cs:318`（`RunApplyScriptCore`）—— 恢复类 **5 个**（`RunApplyScript(` 3 处：343、369、394；`RunApplyScriptCore(` 直接调用 2 处：446、472）

**合计 11 个用例**走该超时路径。

且 `UpdateApplyScriptPathTests` **未加入** `DisableParallelization` 集合，全量运行时与其他类并行。历史只盯住 1 条用例，但该模式是共通的。

**推演更正**：两处都是**先同步读完 stdout/stderr，再 `WaitForExit(30000)`**，故 30 秒计时**不覆盖读流本身**；且夹具**未预写 `applying`**，若脚本未写完读到的是「缺失或不可解析」，**不是** `applying`。因此「提前读到中间态」不成立，历史失败成因仍属假说。残余风险为：同步管道阻塞、等待返回值被忽略、调度竞争。

**本轮未修改**：是否加超时余量、或把该用例纳入串行集合，需独立复核与编排方决定。

## 销项口径

open-items 原文：「复现并修复后经独立复验确认，或**未复现但由独立复核明确接受已有覆盖与风险说明**。不得以历史五次通过或本轮少量通过自动销项。」

本轮按后者交付：覆盖范围 + 风险说明，**不写成「缺陷已修复」**。销项需独立复核。

## 未覆盖 / 局限

1. **CI 平台**：`.github/workflows/build.yml:121` 在 Windows 与 Ubuntu 跑全量。Ubuntu 上本类 **6 条真实 `.bat` 用例**（含历史目标用例）提前 `return`，xUnit 视其为通过；其余 **3 条文本断言**照常执行。**Ubuntu 的绿色结果不能为本轮 Windows 偶发问题增加样本。**
2. **未在 4 核 / 共享 CI runner 形态下压力复跑**：本批次只**启动了 8 个计算作业 + 1 个 IO 作业**，未采样实际 CPU 核数或持续性。
3. **未做实包自更新链路**：本轮是测试夹具直接执行生成的 `.bat`。
4. 未复现不等于不存在：仅说明当前机器与负载下不可复现。

## 证据索引

[`docs/evidence/2026-11-23-open04/`](../evidence/2026-11-23-open04/README.md)：`README.md`（实测数据）、`ANALYSIS.md`（结构分析与判定口径）、`raw/`（四层原始输出）。

## 独立复核观测到的额外失败（本轮未出现）

**第 1 次独立复核**（`320963f`，23 份 TRX，6071 通过 / **1 失败** / 0 跳过）在首轮默认并行全量（`full-01`）中观测到 `ConfigPreservationTests.LoadAsync_IfItWritesBack_StillPreservesExistingFields`（`:194`「不应留下 .tmp 临时文件」）失败；该轮随后隔离复跑 3 次（`diag-config-01/02/03`），**失败后另有 3 轮全量通过**（`full-02`、`full-stress-01`、`diag-full-03`）。**观察到 `.tmp` 残留，根因未定**；现有证据**未确认**并发写入这一排查方向。**本执行者 6 轮全量从未出现**（第 4 次复核 19 轮亦未出现）。该断言覆盖配置持久化，与本项的“可执行文件替换”分属两条代码路径，建议单列跟踪，不计入本项销项依据。

> **轮次更正（第 6 轮复核后）**：本节此前把该失败记为「第 3 次独立复核」。核对第 1、3 轮复核工作树的 TRX 后确认：**失败属第 1 次复核**（`t3-0fc59f52` 的 `full-01.trx:1945`）；第 3 次复核（`t3-399f2fe4`，18 份 TRX）**0 失败**。

## 第 5 轮复核前的补充修正（2026-11-23）

第 4 次复核的最小补齐流程第 2 条含两条执行者当时**未落实**的要求，经回到原始日志核对确认成立并已补齐：

| 位置 | 旧表述 | 问题 | 现表述 |
| --- | --- | --- | --- |
| README:98 | 「复核 `0` 个残留 Job、`0` 个临时文件」 | 原始日志第 14 行只有固定文字 `stress jobs stopped and temp files removed`；`layer4-stress.ps1` 的 `finally` 各步均带 `-ErrorAction SilentlyContinue`，**无任何事后盘点** | 「日志只记录清理动作被调用，没有执行事后盘点」，并明确不能给出最终数目 |
| SELF-AUDIT:57 | 以禁止声称的语境保留歧义字面 | 全文搜索口径不一致 | 「历史与本次的负载强度、持续时间均未核实」 |

**受测输入未变**：`git diff --exit-code 63dadf7 <本轮候选> -- dev/ scripts/test-inventory.txt .github/workflows/build.yml global.json` 为空，第 4 轮 19 份 TRX 可按复核方原话「输入一致时复用」。

## 第 5 轮复核结果（2026-11-23，目标 `453f18f`）

`gpt-6.2-sol` high 判 **FAIL（仍不接受销项）**，但本轮**未重跑任何产品测试**——它先自己验证受测输入未变（`git diff --exit-code 63dadf7 453f18f -- dev/ scripts/test-inventory.txt .github/workflows/build.yml global.json` 为 `exit=0`，变更 61 个路径全在 `docs/` 下），再按第 4 轮约定复用 19 份 TRX。

**通过的部分**：4 处措辞全部 PASS（它还独立打开 `open04-layer4-stress.txt` 与 `layer4-stress.ps1` 核验了「日志无事后盘点」这条执行者自述，确认成立）；六项自审全部复核通过；证据边界四项中 CI、负载、未复现限定三项合规。

**判 FAIL 的三条（均已修）**：

| # | 问题 | 处置 |
| --- | --- | --- |
| 1 | README 覆盖表把时序用例的 `waited`/`applied` 当成「失败时保留可运行旧版」的证据（证据归属错误） | 拆为「占位进程替换时序 / 替换失败与备份 / API 请求互斥」三行，各自写明能证明与不能证明；点明用例名 `KeepsRunnableOldVersion` ≠ 已证真实二进制可运行 |
| 2 | `open-items.md:30`「累计四轮复核独立复跑 60+ 轮全量、43+ 次目标用例」无逐轮汇总支撑（**执行者新引入的夸大**：把总轮次 19+23+19=61 误当全量轮次） | 改为逐轮表格；可逐轮核定的全量只有 **9 轮 / 13518 次**（执行者 6 + 第 4 轮 3） |
| 3 | `open-items.md:15`「4 次独立复核：技术结论一致支持」与「第 2 轮无结论」自相矛盾 | 状态列改成分轮表述，不再用「一致」概括 |

另：README:186、:201 的「两处」已统一为「四处」。

材料归档：[`docs/evidence/2026-11-23-open04/verify-codex-r5-fail-coverage/`](../evidence/2026-11-23-open04/verify-codex-r5-fail-coverage/README.md)。

## 待编排方决定

1. 是否派独立复核接受「未复现 + 风险说明」，从而销项。
2. 是否要求进一步补测（4 核形态、CI runner 形态、实包链路）。
3. 是否修 `WaitForExit` 超时静默继续的模式（会改测试代码，需另立事项或纳入本项）。
