# OPEN-04 复跑方案与结构分析（执行者记录）

> 本文是复跑过程中的分析与设计记录，结论以 `README.md` 的实测数据为准。

## 历史事实（来自 docs/handoff/合流记录-2026-11-07.md:291，R-1）

- 重放 `98f2a26` 后**首次全量**得 **956/957**（1 条失败）。
- `--filter` **隔离组 7/7** 通过。
- 其后**5 次全量**均通过。
- 当时结论：「并发抖动，不阻塞，建议后续加超时余量」。
- 另一轮同名用例失败曾定位到 **PATH 顺序**（裸写 `find` 被 GNU find 抢占 → 等待循环被跳过）。

## 当前代码与历史相比的变化

| 项 | 历史（2026-11-07） | 当前（`367ab91`） |
| --- | --- | --- |
| 系统命令解析 | 裸写 `find` / `tasklist` / `ping` / `fc` | 全部 `"%SystemRoot%\System32\<tool>.exe"`（`UpdateService.cs:639,643,652`） |
| `tasklist` 输出格式 | 默认表格（镜像名截断到 25 字符） | `/FO CSV /NH`（不截断、无表头，`UpdateService.cs:639`） |
| 等待上界 | 无明确上界 | `MaxRunningProcessWaitIterations = 300`（`UpdateService.cs:684`） |
| 测试夹具 PATH | 继承宿主 PATH | 显式设为 `System32;Windows;System32\Wbem`（`UpdateApplyScriptPathTests.cs:62-70`） |
| 本用例条数 | 7（隔离组 7/7） | **9**（新增 `DoesNotTruncateLongImageNames`、`DoesNotDependOnPathResolution` 等回归位） |

**因此两个已知历史成因（PATH 抢占、名截断）已在代码层消除。**

## 结构性风险（本次分析发现，尚待实测判定）

`UpdateApplyScriptPathTests` **未加入任何 `DisableParallelization` 集合**（对比：`UpdateConcurrencyTests` 属 `[Collection("LocalApiService")]`，已禁用并行）。因此本类用例在全量运行时**与其他测试类并行**。

**影响面比历史记录更大**：`WaitForExit(30000)` 出现在两处，且第一处由 **6 个用例**共享（该类共 9 个 `[Fact]`，其中 3 个只断言脚本文本、不执行脚本）：

| 位置 | 影响 |
| --- | --- |
| `UpdateApplyScriptPathTests.cs:73`（`RunApplyScript` 共享路径） | 本类 **6 个**执行脚本的用例全部走这条超时路径（`RunApplyScript(` 调用点实测 6 处） |
| `UpdateRecoveryTests.cs:318` | 恢复类用例同样 |

历史记录只盯住了其中 1 条（`ApplyScript_WaitsForRunningTargetThenReplaces`），但超时后静默继续的模式是**共通**的。

> 更正记录（2026-11-23）：本文件早前版本写「9 个用例共享」，系把 `[Fact]` 总数（9）误当调用点数。实测 `RunApplyScript(` 调用点为 **6**，另 3 个 `[Fact]` 为纯脚本文本断言（`DoesNotDependOnConsoleOnlyCommands`、`DoesNotTruncateLongImageNames`、`DoesNotDependOnPathResolution`），不执行脚本，不受该超时路径影响。

本用例的时序链：

```text
Process.Start(sleeper: ping -n 6 ≈ 5s)      // 占用目标文件名
Thread.Sleep(500)                            // 等它"确实在运行"
RunApplyScript:
    psi...RedirectStandardOutput = true
    proc.StandardOutput.ReadToEnd()          // 同步读，阻塞直到脚本输出结束
    proc.WaitForExit(30000)                  // ← 超时【不抛异常、不改退出码】，直接返回
UpdateService.ReadApplyResult(state)         // 若脚本尚未写完 apply-state.json，读到的是中间态
```

**风险点**：`WaitForExit(30000)` 超时后代码继续执行。若在高负载下 `cmd.exe` 启动慢或 ping 回环变慢，30 秒可能不够，此时 `apply-state.json` 可能仍是 `applying`（而非 `applied`），触发 `Assert.Equal("applied", ...)` 失败。

**这解释了历史表现**：隔离运行时 5 秒必过；全量并行时偶发失败，且无稳定复现条件。

## 复跑设计（分层，对应 open-items 执行步骤 2）

| 层 | 条件 | 目的 | 结果 |
| --- | --- | --- | --- |
| 1 | 单跑目标用例 × 10 | 确认基线稳定 | 10/10 通过，各 5 s |
| 2 | `UpdateApplyScriptPathTests` × 5、`UpdateConcurrencyTests` × 5（串行，互不并行） | 确认类内稳定 | 9/9、8/8 各 5 轮全过 |
| 3 | **全量 1502 条** × 3（默认并行） | 复现历史「全量偶发」条件 | 见 README |
| 4 | 全量 + 后台 CPU/IO 压力 × N | 主动放大时序风险，检验假设 | 见 README |

## 判定口径（预先声明，避免事后调整）

- **若第 3、4 层均未复现**：按 open-items「未复现：完整记录环境、次数、并发度及通过 / 失败 / 跳过数，不写成缺陷已修复」，交独立复核判断证据是否足够。
- **若第 4 层复现**：记录失败现场（`apply-state.json` 内容、`waited` 秒数、脚本输出），判定成因为**产品等待上界不足**还是**夹具超时无断言**，再定修复范围。
- **不以历史 5 次通过或本轮少量通过自动销项**（open-items 销项条件原文）。

## 已排除的替代解释

- **PATH 抢占**：产品脚本已绝对化，夹具也固定 PATH；若仍失败，不是这个原因。
- **名截断**：已用 `/FO CSV /NH`；且有独立回归用例锁定（`ApplyScript_DoesNotTruncateLongImageNames`）。
- **测试并行导致的静态状态串扰**：本用例不触碰 `LocalApiService` 静态状态（只用临时目录 + 独立 `cmd.exe`），故不属于 `UpdateConcurrencyTests` 那类问题。
