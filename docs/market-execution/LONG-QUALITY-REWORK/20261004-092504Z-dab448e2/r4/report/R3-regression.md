# R3 — GUID 副本常规回归与恢复演示

## 1. 执行副本

最终副本：`%TEMP%\dsr-rework-r3c-10ed6d4c`（全新 GUID 路径）。
从 292 项输入清单建立，逐项复制后核 SHA：**292/292，0 不一致**（`source/copy-verification.json`）。
所有构建与测试只在该副本内执行，**没有在 `D:\松光启动器` 里构建或写测试结果**。

R3 基线（修复后的真实输入集合）封存于 `source/r3-input-manifest.json`；
终态采集与它逐项一致（`verdict/changed-files.json` 中 `terminalDrift = 0`）。

> 说明：R3 之前存在一个被取代的基线/副本（`r3\g0`）。原因见证据根 `r3\BASELINE-HISTORY.md`：
> 在它之后运行器又修了“裸命令名 PATH 解析”，**运行器自己**以 `INPUT_SIZE_MISMATCH` 拒绝了那份
> 已漂移的副本（清单文件字节未变），这正是本轮新增的 LQ-02 行为。旧副本没有被就地改写，
> 而是新建了基线与 GUID 副本。

## 2. 受门禁的运行器级联

```
powershell.exe -NoProfile -ExecutionPolicy Bypass -File <exec>\scripts\run-goal-validation.ps1 \
  -InputManifest <ev>\r3\g2\input-manifest.json \
  -RunDir <ev>\r3\run-final \
  -StagesFile <ev>\r3\goal-stages.json \
  -ExecutionRoot <exec> -EvidenceRoot <ev>
```

结果：**verdict = PASS，10/10 stage COMPLETED，failed 0，partial false，exit 0**，
已验证输入 292 项（`runner/runner-verdict.json`）。

| stage | 结果 | 计数 |
| --- | --- | --- |
| g4-build-tests | COMPLETED exit 0 | 0 error；产物 `Pineglow.Launcher.Tests.dll` 绑定 SHA |
| g4-secrets | COMPLETED exit 0 | total 13 / executed 13 / passed 13 / failed 0 / notExecuted 0 |
| g4-ordinary-r2 | COMPLETED exit 0 | 5 / 5 / 5 / 0 / 0 |
| g4-recovery-normal | COMPLETED exit 0 | 6 / 6 / 6 / 0 / 0 |
| g4-flows | COMPLETED exit 0 | 49 / 49 / 49 / 0 / 0 |
| g4-node-all | COMPLETED exit 0 | `# pass 269`（266 原有 + 3 INDEPENDENT），`# fail 0` |
| g4-runner-tests | COMPLETED exit 0 | `# pass 28`，`# fail 0` |
| g4-auditor-tests | COMPLETED exit 0 | `... ok` × 26（stderr） |
| g4-verifyhost-build | COMPLETED exit 0 | 0 error；`VerifyHost.exe` + `VerifyHost.dll` 绑定 SHA |
| g4-bisect-smoke | COMPLETED exit 0 | `[PASS]` × 19，无 `[FAIL]` |

C# 实际执行 **73/73，0 required skip**（13+5+6+49，明确筛选集合；继续排除
`ImportWithALiveTokenButCancelledMidway`，**不声称全量 C#**）。

### 2.1 真实证据覆盖审计（审计器只读）

```
python <exec>\scripts\audit-goal-evidence.py \
  --expected <旧封存包>\FINAL-LOCAL-QUALITY\20261003-092612Z-b4d896a8\g0\expected-evidence-final.json \
  --out <ev>\r3\audit-real.json
```

结果：`roots 3 / packages 62 / manifestEntries 1022 / filesVerified 1022 / failures 0 / knownDrift 3`，
`failureCodes = []`，exit 0（`audit/audit-real.log`）。与 Codex 独立验收的本轮数字一致；
3 个 `knownDrift` 是旧包已显式登记的历史差异，不被描述成“历史证据恢复无缺陷”。

## 3. 场景矩阵（27 项更新为 28 项）

`verdict/scenario-matrix.json`：**28/28 PASS**，绑定 `testFileSha256 = 59B8FC0F…` 与原始 TAP 日志
（`scenario/node-goal-core-flow.log`，其 SHA 一并记录）。

**IMPORT-03 / IMPORT-04 不再按测试标题自动登记**，而是要求“场景内测试 + 独立终态反证测试”
两条都通过，且断言都在 `await running` 之后：

| 场景 | 入口（终态断言） | 期望 | 证据 |
| --- | --- | --- | --- |
| IMPORT-03 | `cancelImport` 返回 `{pendingCancel:true, ok:false}`，随后 `importInstance` 因普通原因 reject，断言在 `await running` 之后 | 终态是“失败 + 明确结果未确认”，**绝不**说已取消，**绝不**说没有生成实例 | `IMPORT-03 cancel after acceptance…` + `INDEPENDENT IMPORT-03 pending cancel…` |
| IMPORT-04 | `cancelImport` 抛错（unknown operation），随后 `importInstance` 因普通原因 reject，断言在 `await running` 之后 | 同上；取消意图不得升级为已确认取消 | `IMPORT-04 unknown or failed cancel…` + `INDEPENDENT IMPORT-04 failed cancel…` |
| IMPORT-05 | 导入请求在途时点击取消 | `cancelImport` 使用的 operationId 与导入请求完全一致 | `INDEPENDENT IMPORT-01 cancel uses the active import operationId` |

## 4. 失败 → 恢复演示

`verdict/recovery-demo.json`，`verdict = DEMO_PASS`：

**A. 同一定义、瞬时失败 → 恢复**

1. 第 1 次运行失败（子进程 exit 1，TRX 有 1 条 Failed）→ stage FAILED，verdict FAIL，exit 1
2. 移除失败原因（**定义不变**）
3. `-Resume` → 同一 stage 在**新的 attempt 目录**中重跑并通过
4. 证明：attempt 1 的 stdout / stderr / TRX 在恢复后**逐字节不变**；
   被取代的 stage 记录归档为 `demo.incomplete.demo-run-1.json` 且与原记录**字节相同**；
   旧 verdict 归档到 `verdicts/`

**B. 输入改变 → 新 runId（单独一项）**

5. 改变清单所指输入文件的**实际字节**（清单文件本身不动）
6. 同目录 `-Resume` 被**拒绝**（exit 2，原因 `INPUT_SHA_MISMATCH`；LQ-02 门禁先于绑定检查触发），
   旧 verdict 与旧 binding **字节不变**
7. 用**新的清单 + 新的 RunDir + 新的 runId** 采纳该变更 → PASS，且 `verifiedInputSetSha256` 与基线不同

## 5. Resume 复用证明（真实级联）

对 `run-final` 再跑一次 `-Resume`：**10/10 stage REUSED**，verdict PASS，exit 0，
**没有产生任何新的 attempt 目录**，旧 verdict 归档为 `verdicts/runner-verdict.2026-10-04T101451Z.json`。
（日志：`runner/r3-runner-resume.log`）

## 6. 通过项边界

- 未验证：真实 Avalonia/WebView2 GUI、真实游戏、原生凭据、真实更新替换、跨平台、干净提交/远程 CI/发布。
- C# 数字是明确筛选集合，不是全量。
- RA-01/RA-02 受限恢复安全反证仍在范围外。
