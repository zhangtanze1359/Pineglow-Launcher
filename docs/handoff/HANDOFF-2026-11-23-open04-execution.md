# 交接 · OPEN-04 更新替换脚本偶发失败复跑（2026-11-23）

按 [任务计划](../任务计划-2026-11-22.md) 第三节 OPEN-04 执行。**结论：未复现**。`FormalPhaseApproval=NOT_GRANTED`，未推送、未打 tag。

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
| 4 | 全量 + 合成 CPU/IO 压力 × 3 | **1502/1502 全过**，各约 84 s（+16.03%，压力生效但仍 0 失败） |

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
2. **未在 4 核 / 共享 CI runner 形态下压力复跑**（本轮最高约 8 核忙循环）。
3. **未做实包自更新链路**：本轮是测试夹具直接执行生成的 `.bat`。
4. 未复现不等于不存在：仅说明当前机器与负载下不可复现。

## 证据索引

[`docs/evidence/2026-11-23-open04/`](../evidence/2026-11-23-open04/README.md)：`README.md`（实测数据）、`ANALYSIS.md`（结构分析与判定口径）、`raw/`（四层原始输出）。

## 待编排方决定

1. 是否派独立复核接受「未复现 + 风险说明」，从而销项。
2. 是否要求进一步补测（4 核形态、CI runner 形态、实包链路）。
3. 是否修 `WaitForExit` 超时静默继续的模式（会改测试代码，需另立事项或纳入本项）。
