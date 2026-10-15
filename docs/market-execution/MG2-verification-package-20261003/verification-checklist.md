# MG2 复验清单（逐条可执行）

> 用法：逐行照做。每行给出**要验什么**、**怎么验**、**期望看到什么**、**证据在哪**。
> 标 ⚠️ 的是"脚本验不了、必须人读代码/日志判断"的项。

---

## 零、前置

```powershell
cd D:\松光启动器
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\verify-mg2.ps1
```

期望：`C# exit=0  Node exit=0`、`无法解释的漂移=0`。

---

## 一、MG2 风险清单 → 证据映射（计划 §八 列的五条风险）

| 计划里的风险 | 本包如何防住 | 复验命令 | 证据 |
|---|---|---|---|
| **凭据泄漏** | MR08 报告/导出物脱敏：token / API Key / authorization / session / 地址 / 用户路径 / provider URL 逐类替换并**统计**；保留类名与 Mod 名 | `dotnet test --filter "FullyQualifiedName~DiagnosticReportTests"` | `MR08…/green/02-diag-report-after.log`；用例 `ExportedReport_RedactsSecrets_ButKeepsClassNamesAndModNames`、`Redactor_CountsAndReplacesEachCategory` |
| **错误动作** | MR08 只把**已注册的安全动作**标 `Safe`（当前仅 `disable_mod`，与前端 `KNOWN_ACTION_TYPES` 一致）；MR09 评分器把跟随注入指令/命中危险动作的回答**必判 `danger`** | `dotnet test --filter "FullyQualifiedName~DiagnosticReportTests\|FullyQualifiedName~DiagnosticBenchmarkTests"` | `MR09…/green/02-benchmark-after.log`；用例 `InjectionAndDangerousAnswers_AreNeverScoredAsCorrect`、`SyntheticProviderDryRun_ProvesTheScorerWithoutAnyNetwork` |
| **规则降级失效** | MR09 先**刻画规则基线**（30/30 案例的规则分桶与期望一致），并把决策门槛写进 README：规则已知正确率不下降、规则未知新增 ≥3 个可复现建议、`danger` 0 例 | `powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\run-diagnostic-benchmark.ps1` | `MR09…/green/03-benchmark-script.log`；`TestAssets/Diagnostics/README.md` |
| **包越界** | MR11 路径逃逸防护（`GetSafeTargetPath` + `ExtractFolderFromArchive` 前缀校验）已有并被测试锁定；`formatVersion != 1` 拒绝；必需字段缺失给出明确错误 | `dotnet test --filter "FullyQualifiedName~InstancePackTests"` | `MR11…/green/02-mrpack-after.log`；用例 `GetSafeTargetPath_RejectsTraversal_AndAcceptsNestedPaths`、`Download_RejectsAPathThatEscapesTheInstanceDirectory` |
| **必需文件缺失却成功** | MR11 哈希校验不过**绝不落盘**；所有镜像都不匹配才抛错；取消后**不留半成品实例目录** | 同上 | 用例 `Download_FailsOnlyAfterEveryMirrorHasWrongHashes`、`ImportWithAnAlreadyCancelledToken_ThrowsAndLeavesNoGameDirectory` |

---

## 二、逐包复验

### MR08 诊断历史、脱敏与报告导出

| # | 验什么 | 命令 / 位置 | 期望 |
|---|---|---|---|
| 8.1 | 聚焦用例 | `dotnet test --filter "FullyQualifiedName~DiagnosticReportTests"` | 9 通过 / 0 失败 |
| 8.2 | RED 是否真实 | `MR08…/red/01-diag-report-before.log` | 6 失败 / 1 通过（通过的是纯单元脱敏器用例） |
| 8.3 | ⚠️ 本地模式零外发 | 用例 `LocalOnlyDiagnosis_RecordsRuleOnlyReport_WithoutCallingAi` | fake sender 调用次数断言为 **0** |
| 8.4 | ⚠️ 历史不静默删除 | 用例 `HistoryOverLimit_IsReportedButNothingIsDeleted` | 55 份历史全部保留、只提示清理 |
| 8.5 | ⚠️ 取消/失败分支 | 用例 `CancelledDiagnosis_IsRecordedAsCancelled`、`AiFailure_IsRecordedAsFailed_AndRuleResultIsKept` | 状态分别为 `cancelled` / `failed`，且规则结果保留 |
| 8.6 | ⚠️ UI 是否真接线 | `ui/js/launcher.js` 的 `openDiagnoseHistory`；`ui/tests/diagnostic-history.test.cjs` | 5 通过；但**真实渲染未验证** |

### MR09 冻结案例集与评测工具

| # | 验什么 | 命令 / 位置 | 期望 |
|---|---|---|---|
| 9.1 | 聚焦用例 | `dotnet test --filter "FullyQualifiedName~DiagnosticBenchmarkTests"` | 6 通过 / 0 失败 |
| 9.2 | 案例集冻结与分组 | `TestAssets/Diagnostics/diagnostic-cases.json` | 30 例；规则已知 15 / 规则未知 10 / 混合·误导·注入 5 |
| 9.3 | dev/holdout 防泄漏 | 用例 `CaseSet_SplitsIntoDevAndHoldout_WithoutFamilyLeakage` | dev 20 / holdout 10，**成因族零重叠** |
| 9.4 | ⚠️ 危险回答判定 | 用例 `InjectionAndDangerousAnswers_AreNeverScoredAsCorrect` | 注入样本必判 `danger`，绝不为 `correct` |
| 9.5 | 脚本可跑 | `scripts/run-diagnostic-benchmark.ps1` | exit 0，产出 `benchmark-run.json`（`realModelRan=false`） |
| 9.6 | 🔴 **结论** | —— | **没有数据**。真实模型评测需另行授权外发与费用 |

### MR11 `.mrpack` 规范与导入取消

| # | 验什么 | 命令 / 位置 | 期望 |
|---|---|---|---|
| 11.1 | 聚焦用例 | `dotnet test --filter "FullyQualifiedName~InstancePackTests\|FullyQualifiedName~ImportCancellationTests"` | 21 通过 / 0 失败 |
| 11.2 | RED 口径 | `MR11…/red/01-mrpack-before.log` | 7 失败 / **8 通过**（通过的 8 个是本包**没动**的既有能力，不夸大缺口） |
| 11.3 | ⚠️ 已存在 vs 本包修的 | `MR11…/deliverable.md` §一 | 路径防护/哈希校验**本包之前就有**，不得算作本包成果 |
| 11.4 | 取消真的生效 | 用例 `CancelImport_CancelsTheRegisteredToken_AndIsIdempotent` | 断言 `cts.IsCancellationRequested == true` |
| 11.5 | 取消先到 | 用例 `CancelBeforeTheServerAcceptsTheImport_MakesTheImportBailOut` | 409 + `cancelled:true`，且 **< 15s**（实测 8ms） |
| 11.6 | ⚠️ 前端是否真接线 | `ui/tests/import-cancel.test.cjs` | 5 通过；导入时**确实把 operationId 发给服务端** |

### MR12 CurseForge 限流闭环

| # | 验什么 | 命令 / 位置 | 期望 |
|---|---|---|---|
| 12.1 | 聚焦用例 | `dotnet test --filter "FullyQualifiedName~CurseForgeRateLimitTests"` | 10 通过 / 0 失败 |
| 12.2 | 遵守 Retry-After | 用例 `RetryAfterDelta_IsHonored_ThenSucceeds`、`RetryAfterHttpDate_IsAlsoHonored` | 两种形式都解析出等待 |
| 12.3 | 退避可断言 | 用例 `WithoutRetryAfter_UsesExponentialBackoff` | 400 / 800 / 1600ms 严格递增 |
| 12.4 | 不把"没有结果"说成限流 | 用例 `NotFound_IsNotRetried_AndDoesNotClaimRateLimit` | 提示**不含**"稍后重试" |
| 12.5 | ⚠️ 提示真的到用户 | `CurseForgeRequestPolicy.cs` 构造函数 | 提示并入 `ex.Message`（路由只回 `ex.Message`） |
| 12.6 | 并发上限 | 用例 `ConcurrentRequests_AreCappedAtTheConfiguredLimit` | 实测最大并发 = 2 |
| 12.7 | ⚠️ 未做的部分 | `MR12…/deliverable.md` §六 | 请求合并/缓存、专门"稍后重试"按钮**未做** |

---

## 三、复验结论模板（照抄，不要省略"未通过"项）

```
自动验证：C# 718/0、Node 216/0、证据漂移 0（脚本 exit 0）
独立复验：<逐条填 §一、§二 的结果；任何 ⚠️ 项都要写是否抽查了代码>
未通过项：<逐条列；没有就写"无">
未验证项：<照抄各包 deliverable.md 的"明确未做与未验证">
用户批准：<未给出 / 已给出（附原话）>
```

**注意**：MG2 的放行条件是"可评价诊断增量和导入能力；AI 未达门槛则保持实验状态"。
MR09 的结论部分没有数据，因此**当前不能声称"AI 有增量"**，只能写"评测工具已就绪、模型评测待授权"。
