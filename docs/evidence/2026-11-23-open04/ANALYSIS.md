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
| `UpdateApplyScriptPathTests.cs:73`（`RunApplyScript` 共享路径） | 本类 **6 个**执行脚本的用例走这条路径（调用点第 103、140、173、247、289、344 行） |
| `UpdateRecoveryTests.cs:318`（`RunApplyScriptCore`） | 恢复类 **5 个**调用点：`RunApplyScript(` 3 处（343、369、394）+ `RunApplyScriptCore(` 直接调用 2 处（446、472） |

历史记录只盯住了其中 1 条（`ApplyScript_WaitsForRunningTargetThenReplaces`），但超时后静默继续的模式是**共通**的。

> 更正记录（2026-11-23）：本文件此处经两次修订才准确。① 最初写「9 个用例共享」——把 `[Fact]` 总数误当调用点数。② 随后改为「恢复类 3 个调用点」——只统计了 `RunApplyScript(`，**漏掉 `RunApplyScriptCore(` 的 2 处直接调用**。③ 现值为 6（路径类）+ 5（恢复类）＝ **11 个用例**走该超时路径。第 1 次独立复核的「恢复类 5 个」是正确的。

本用例的时序链（**已按第 2 次独立复核更正**）：

```text
Process.Start(sleeper: ping -n 6 ≈ 5s)      // 占用目标文件名
Thread.Sleep(500)                            // 等它"确实在运行"
RunApplyScript:
    psi...RedirectStandardOutput/Error = true
    proc.StandardOutput.ReadToEnd() + proc.StandardError.ReadToEnd()   // ← 先【同步】读完两路流
    proc.WaitForExit(30000)                  // ← 超时返回 false，返回值被忽略
    return new Outcome(output, target, backup, UpdateService.ReadApplyResult(state))
```

**风险点的准确表述**（原分析有两处偏差，此处更正）：

1. **30 秒计时只覆盖「读流之后」的等待**，**不覆盖读流本身**。`ReadToEnd()` 是同步阻塞，它先于 `WaitForExit` 完成；若子进程迟迟不关闭输出流，阻塞点在 `ReadToEnd()`，而 `WaitForExit(30000)` 根本还没开始计时。
2. **`WaitForExit(30000)` 的返回值被忽略**（`false` 表示超时），夹具也**未读取子进程退出码**，随后直接读文件与状态。
3. 因此**「提前读到 `applying` 中间态」这一推演不成立**：该夹具**没有预写 `applying`**；若脚本未写完，`ReadApplyResult` 得到的是**缺失或不可解析**的状态，而不是 `applying`。

**结论**：超时静默继续这一结构缺陷**确实存在**（忽略返回值、不校验退出码），但**历史失败是否由这条路径引发仍属假说**，本轮未观测到该分支。残余风险应表述为：同步管道阻塞、等待返回值被忽略、调度竞争——而非「必然读到 applying」。

`UpdateRecoveryTests` 的 `RunApplyScriptCore` 是同样的形态（先 `ReadToEnd` 再 `WaitForExit(30000)`，第 316–318 行）。

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
