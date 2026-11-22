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
| 4 | 全量 + 合成 CPU/IO 压力 × 3 | **1502/1502 全过**，各约 84 s（+17%，压力生效但仍 0 失败） |

**合计 9 次全量套件运行，全部 1502/1502，0 失败 0 跳过。**

时序证据：目标用例独立采集 8 次，`state=applied`，`waited` 4.7–4.9 s（均值 4.74），**全部 ≥ 2 s**，与 sleeper 生命周期（`ping -n 6` ≈ 5 s）吻合 —— 证明替换发生在目标进程退出之后。

## 与历史相比已消除的成因

| 成因 | 历史 | 当前 |
| --- | --- | --- |
| PATH 抢占（裸写 `find` 命中 GNU find） | 曾导致等待循环被跳过 | 产品脚本全部 `"%SystemRoot%\System32\<tool>.exe"` |
| `tasklist` 镜像名截断 | 默认表格截到 25 字符 | `/FO CSV /NH` |
| 等待无上界 | 无 | `MaxRunningProcessWaitIterations = 300` |
| 夹具继承宿主 PATH | 是 | 显式 `System32;Windows;System32\Wbem` |

## 发现的结构性风险（未修，供独立复核判断）

`WaitForExit(30000)` 超时后**不抛异常、不改退出码**，代码直接继续读 `apply-state.json`。这出现在两处，且第一处被 **6 个执行脚本的用例**共享（本类共 9 个 `[Fact]`，另 3 个只断言脚本文本）：

- `UpdateApplyScriptPathTests.cs:73`（`RunApplyScript`，实测 6 个调用点）
- `UpdateRecoveryTests.cs:318`

且 `UpdateApplyScriptPathTests` **未加入** `DisableParallelization` 集合，全量运行时与其他类并行。历史只盯住 1 条用例，但该模式是共通的。

**本轮未修改**：是否加超时余量、或把该用例纳入串行集合，需独立复核与编排方决定。

## 销项口径

open-items 原文：「复现并修复后经独立复验确认，或**未复现但由独立复核明确接受已有覆盖与风险说明**。不得以历史五次通过或本轮少量通过自动销项。」

本轮按后者交付：覆盖范围 + 风险说明，**不写成「缺陷已修复」**。销项需独立复核。

## 未覆盖 / 局限

1. **CI 平台**：`.github/workflows/build.yml` 在 Windows 与 Ubuntu 跑全量；Ubuntu 上本类用例因 `OperatingSystem.IsWindows()` 为 false 而 `return` 静默跳过（AGENTS.md 规则 3 禁止的做法，属既有代码），故 Ubuntu 的「全过」不包含本用例真实执行。
2. **未在 4 核 / 共享 CI runner 形态下压力复跑**（本轮最高约 8 核忙循环）。
3. **未做实包自更新链路**：本轮是测试夹具直接执行生成的 `.bat`。
4. 未复现不等于不存在：仅说明当前机器与负载下不可复现。

## 证据索引

[`docs/evidence/2026-11-23-open04/`](../evidence/2026-11-23-open04/README.md)：`README.md`（实测数据）、`ANALYSIS.md`（结构分析与判定口径）、`raw/`（四层原始输出）。

## 待编排方决定

1. 是否派独立复核接受「未复现 + 风险说明」，从而销项。
2. 是否要求进一步补测（4 核形态、CI runner 形态、实包链路）。
3. 是否修 `WaitForExit` 超时静默继续的模式（会改测试代码，需另立事项或纳入本项）。
