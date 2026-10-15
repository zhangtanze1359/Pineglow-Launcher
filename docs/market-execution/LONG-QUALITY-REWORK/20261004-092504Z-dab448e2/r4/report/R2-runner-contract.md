# R2 — 运行器输入、证据与恢复契约（LQ-02..LQ-06 修复）

改动文件：`scripts/run-goal-validation.ps1`、`scripts/tests/goal-validation.test.cjs`。
审计器 `scripts/audit-goal-evidence.py` 及其 26 项测试、C# 服务与测试、`api.js`、CSS/index/设计资源、
全部历史证据**只读未改**。

## 1. 契约变更（逐条对应 LQ）

| 编号 | 新契约 |
| --- | --- |
| LQ-02 | 清单文件本身不是可信输入。每次运行（首次与 Resume）都解析清单，逐项核对**清单指向的执行根实际文件**：存在、整数 size、SHA256 全部一致；并计算 `verifiedInputSetSha256`（按路径 Ordinal 排序的长度前缀规范化文本）。空清单、重复项、绝对路径、`..`、字段非法、文件缺失一律拒绝（exit 2）。 |
| LQ-03 | 每个 attempt 在**全新 attempt 目录**中执行，因此 TRX 只能是本次子进程写的；额外保留 `TRX_PREEXISTING` 守卫（不在旧目录里删文件来“证明新生成”）。计数器必须齐全、非负且自洽：`total = executed + notExecuted`、`executed = passed + failed`、`UnitTestResult` 行数与 outcome 分布必须与计数器一致；`executed = 0` 即使 `total > 0` 也 FAIL。 |
| LQ-04 | `produces` 进入长度前缀的确定性 stage 指纹；复用额外要求每个声明的必需产物存在且 SHA256 与记录一致。 |
| LQ-05 | `run-binding.json` 绑定 `runId + 清单SHA + 已验证输入集SHA + stages文件SHA + executionRoot`。不兼容续跑在任何写入前拒绝（exit 2 + `refusal-*.json`），旧 stage 记录、日志、TRX、产物、verdict 全部保留（记录归档、verdict 归档到 `verdicts/`）。 |
| LQ-06 | 空 stages、未知 `-Only`、重复 stage id、`requiredStages` 缺失一律拒绝（exit 2）；合法 `-Only` 子集产生明确 `PARTIAL`（exit 3），不得被上层当作整体完成。 |

退出码：`0` = PASS，`1` = FAIL，`2` = REFUSED，`3` = PARTIAL。

## 2. 新增/保留的测试（10 原有 + 18 新增 = 28）

原 10 项全部保留并加强（正常通过、非零退出、缺 TRX、0 测试、required skip、TRX 有失败、
中断记录不复用、输入变化不复用、兼容复用 + 产物篡改、日志计数不足）。

新增 18 项：

- `runner 11`：期望文本可声明在 stderr（python unittest 场景）
- `LQ-02`：实际输入字节变化而清单不变 → 拒绝复用与 PASS；清单文件保持字节不变
- `LQ-02`：空 / size 不符 / SHA 非法 / 文件缺失 / `..` / 重复项清单逐项拒绝
- `LQ-03`：子命令未重写的既存 TRX 不能通过（`TRX_PREEXISTING`）
- `LQ-03`：`total>0` 而 `executed=0` 不能通过（`ZERO_TESTS`）
- `LQ-03`：Codex 报告的确切矛盾计数形状被拒绝
- `LQ-03`：计数器与 UnitTestResult 行数不一致被拒绝（`TRX_COUNTERS_INCONSISTENT`）
- `LQ-03`：完全没有结果行的 TRX 被拒绝（`TRX_NO_RESULT_ROWS`）
- `LQ-04`：新增不存在的必需产物 → 定义变化被拒绝；全新目录下同定义必须 FAIL（`ARTIFACT_MISSING`）
- `LQ-04`：必需产物被删除 → 拒绝复用并重跑
- `LQ-05`：定义变化后拒绝续跑，失败 attempt 的 stdout / TRX / verdict 字节不变
- `LQ-05`：同一定义的瞬时失败 → Resume 恢复成功；失败 attempt 证据字节不变、记录归档、verdict 归档
- `LQ-06`：空 stages 被拒绝（`NO_STAGES`）
- `LQ-06`：未知 `-Only` 被拒绝（`ONLY_NOT_FOUND`）
- `LQ-06`：合法 `-Only` → `PARTIAL` / exit 3；全量运行 → PASS
- `LQ-06`：重复 stage id 被拒绝
- `LQ-06`：`requiredStages` 缺失被拒绝
- `LQ-06`：绑定文件写入并记录已验证输入集

## 3. 本轮在 R2 中一并发现并修复的运行器缺陷

这三点都会让“真实失败”被误判为通过或让证据不可信，属于同一份运行器契约，故一并修复并测试：

1. **子进程退出码丢失。** Windows PowerShell 5.1 下 `Start-Process -PassThru` 返回的 Process 对象，
   一旦访问 `HasExited` / 调用 `WaitForExit`，`ExitCode` 就变为不可读（在 node / dotnet / python 上
   均可复现）。原实现先读 `HasExited` 再读 `ExitCode`，因此真实退出码可能丢失。
   现改为直接使用 .NET `System.Diagnostics.Process`（自有句柄）：`WaitForExit(ms)` 保留退出码，
   超时可 `Kill`，并且 `EXIT_CODE_UNAVAILABLE` 明确算失败而不是成功。
2. **裸命令名被错误解析为相对路径。** 相对 `file` 若含目录分隔符才按 stage cwd 解析；
   `node` / `dotnet` / `python` 这类裸命令名保持 PATH 查找（`START_FAILED` 覆盖找不到的情况）。
3. **期望文本只能来自 stdout。** 新增 `expect.countIn` / `expect.forbidIn`（`stdout` 默认 / `stderr` / `both`），
   使 python unittest 这类写 stderr 的子命令也能被正确计数与禁词检查。

## 4. RED → GREEN

| 步骤 | 结果 |
| --- | --- |
| RED（未修运行器 + 保留的独立夹具） | 7/7 case **exit 0 / PASS**（全部误报） |
| GREEN（聚焦契约测试） | `node --test --test-reporter=tap scripts/tests/goal-validation.test.cjs` → tests 28 / **pass 28** / fail 0 |
| GREEN（保留的独立夹具，期望未改） | 7/7 case **全部非零退出**，`falsePassAfter = 0`，`allRejected = true` |

独立夹具的对照调用（第一次非 Resume 运行）现在**正常通过**，随后被探测的那次调用被拒绝，
因此“拒绝”是可判读的信号，而不是夹具本身坏掉。为使对照成立，只对夹具做了两处**接口级重绑**
（记录在 `r3/BASELINE-HISTORY.md` 与证据根的 CHECKPOINT 中）：合成子命令改为写入 `{attemptDir}`
指向的本次 attempt 目录，且其合成 TRX 补上与自身计数器一致的结果行。**所有期望字符串未改动。**
