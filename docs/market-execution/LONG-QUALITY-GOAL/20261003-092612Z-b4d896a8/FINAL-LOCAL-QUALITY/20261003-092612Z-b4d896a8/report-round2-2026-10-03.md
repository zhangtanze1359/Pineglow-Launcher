# LONG-QUALITY-GOAL 执行报告 · 第 2 轮（2026-10-03）

**本轮结论：G2 DONE（27/27 场景）、G3 DONE（73 项获准 C# + 266 项 Node + 宿主冒烟）。G0/G1 维持 DONE，G4 NOT_STARTED。**
`ImplementationReady=false`（G4 未完成）· `IndependentAcceptance=NOT_GRANTED` · `FormalPhaseApproval=NOT_GRANTED` · `ReleaseApproval=NOT_GRANTED`。

**本轮发生一件必须让位于事实的事件：共享工作区出现外部并发写入**（见 §4）。它没有回退本目标的任何修复，但破坏"输入冻结"前提，最终封存必须从一份新副本重新绑定。

## 1. G2：六条普通流程的 27 项行为场景全部落地

新增 `ui/tests/goal-core-flow.test.cjs`（46532 B，SHA `14D471F7…77C1`）：在 vm 中加载**真实** `i18n.js` + `launcher.js`，注入可控 `SG.api` 与受控 DOM，直接调用真实生产入口。28 个测试 = 27 个必需场景 + 1 个夹具对照。

### 1.1 有效 RED（两批，原始日志保留）

| 批次 | 命令 | 真实 exit | 结果 |
| --- | --- | --- | --- |
| 第 1 批（4 个预测缺口） | `node --test ui/tests/goal-core-flow.test.cjs` | **1** | 5 tests / 1 pass / 4 fail：RECOVERY-01、BISECT-03、DIAGNOSIS-03 缺重试入口；DIAGNOSIS-04 `ReferenceError: versionLine is not defined` |
| 第 2 批（其余 22 项） | 同上 | **1** | 28 tests / 19 pass / 9 fail |

**诚实分类（第 2 批 9 个失败）：**

- **产品缺陷 1 个**：`INSTANCE-04` —— 移除处理函数没有忙态判断，同一 tick 双击会发两次删除请求（`the second click while in flight must be ignored`）。
- **夹具缺陷 8 个（不是产品缺陷）**：受控 DOM 没有反射布尔属性（`disabled`/`checked`）与反射属性（`meta.content`）；合成错误体缺 `enabledAtStart` 导致落到空态；前置条件没先经 `renderInstallations` 建立选中态；`settle()` 只清微任务而生产代码有 `setTimeout(0)`。这些一律**改夹具**，没有放宽任何断言，也没有把夹具问题写成产品问题。

### 1.2 最小修复（仅白名单对象）

- `ui/js/launcher.js`：恢复区查询失败 → 真实原因 + `#rRetry`；Bisect pending 查询失败 → 明确"无法确认上次状态" + `#bPendingRetry`（不再静默显示"正常开始"）；诊断历史失败 → `#dhRetry`，并删除越界的 `versionLine`；移除处理函数加忙态守卫 + `finally` 恢复可用，同时**保留**既有 MR03 静态扫描依赖的 `document.getElementById('mDel').onclick` 与 `if (!confirm(msg)) return;` 字面形式。
- `ui/js/i18n.js`：中英各新增 5 个键。
- `ui/css/figma.css`：`.modal-btn:disabled` 忙态与 `.retry-action` 左对齐（其余布局未动）。

### 1.3 GREEN 与回归

```powershell
node --test ui/tests/goal-core-flow.test.cjs      # exit 0 · 28/28
node --test <ui/tests 全部 21 个入口>              # exit 0 · 266/266 · 0 skip
```

对照 R2 独立基线（20 入口 / 238 项）：**+1 入口 / +28 项；既有测试一条未改、未弱化、未 skip。**

场景矩阵由脚本从"冻结需求 + 真实测试名 + 真实 reporter 输出"三方核对生成：

```powershell
python -X utf8 g2/build-scenario-matrix.py <ER>   # exit 0 · scenarios=27 requiredPass=27 requiredFail=0
```

`g0/requirements.json` 发生**修订 1**：`DIAGNOSIS-04` 的 `testId` 绑定为执行时真实测试名（旧 SHA `8D46EA54…EB2` → 新 `C749D532…EA8`），并在文件内 `revisions` 记录旧值、新值与理由——冻结期望文本未改。

## 2. G3：隔离副本里的正常 API 与磁盘闭环

副本：`%TEMP%\pineglow-goal-g3-5db79598fe394345a92e5099fc12cd49`（18:06:28 物化，**290 项输入** = tracked 161 + 白名单 125 + 本目标工具 4，missing=0，复制后 SHA 不一致 0）。

| 阶段 | 真实 exit | 唯一计数（读 TRX / reporter） |
| --- | --- | --- |
| `dotnet build` 测试工程 | 0 | 0 error / 19 warning |
| `g3-secrets`（凭据+配置隔离） | 0 | **13/13**，0 skip |
| `g3-ordinary-r2`（结构化失败 / 普通撤销） | 0 | **5/5**，0 skip |
| `g3-recovery-normal`（隔离/Sidecar/幂等/冲突） | 0 | **6/6**，0 skip |
| `g3-flows`（六流程跨层：InstanceRemoval / ImportCancellation / DiagnosticReport / BisectRecovery / UpdateRecovery / RecoveryVersionDisplay） | 0 | **49/49**，0 skip |
| `g3-node-all`（21 入口） | 0 | **266/266**，0 skip |
| `dotnet build` VerifyHost | 0 | 0 error / 5 warning |
| `VerifyHost.exe --bisect-smoke` | 0 | **19/19 PASS**，0 FAIL |

- 本目标获准 C# 合计 **73/73**，其中 24 项正是 R2 独立基线集合，新增 49 项为六流程普通跨层类；Node 238 → 266 是因为本目标新增 28 项行为测试。数量全部来自 TRX 计数器与 node reporter，不是"意图数"。
- **没有新增 `GoalOrdinaryFlowTests.cs`**：既有普通类已在隔离 GUID 根上断言真实 loopback HTTP + 磁盘/记录一致性，按计划"不为增长用例数添加重复测试"。
- **隔离证据**：`LocalApiTestHarness` 把恢复区/二分盘/诊断报告根重定向到 `%TEMP%\pineglow-*-test-<guid32>`，配置用内存 store；`SecretStoreBackendTests.ProcessDefaultBackend_IsTheInMemoryTestBackend_NotTheNativeOne` 本轮通过 → 测试进程默认凭据后端是内存实现而非原生。原生凭据往返仍 **NOT_VERIFIED**。
- **NOT_RUN**：`ImportCancellationTests.ImportWithALiveTokenButCancelledMidway_StillCleansUp`（中途取消竞争探针，属平台终止范围，测试原样保留、未改 skip、未派他人代做）；受限恢复安全反证仍整体 NOT_RUN。

## 3. 源保护

```powershell
python -X utf8 source-drift-check.py <ER> <ER>\g3\source-drift-after-g3.json --label after-g3
```

| 项 | 值 |
| --- | --- |
| 冻结输入 | 286 |
| 未变 | 281 |
| 白名单内变化 | 4（`scripts/source-snapshot-inputs.txt`、`ui/js/launcher.js`、`ui/js/i18n.js`、`ui/css/figma.css`） |
| **范围外变化** | **1：`ui/index.html`（非本目标所为，见 §4）** |
| 缺失 | 0 |

`HEAD` 未变、stash 为空、**0 次 git 写操作**（无 commit / push / stage / reset / clean / restore / checkout）。

## 4. 外部并发写入（必须记录，不得算作本目标改动）

18:12 的漂移检查发现 `ui/index.html` 出现范围外变化。核验过程与结论：

| 证据 | 结果 |
| --- | --- |
| G0 副本（17:28:28 物化）中 `ui/index.html` | `BBC0A6196EAC6919…`（= 冻结值） |
| G3 副本（18:06:28 物化）中 `ui/index.html` | **仍是** `BBC0A6196EAC6919…` |
| 当前源文件 | `39933134AD98FAFD…`，7221 B，mtime **18:08:34** |

即在 18:06:28 之后、由**本目标之外**的写者改成：`<title>Pineglow · 松光启动器</title>`、新增 `<link href="css/pineglow.css">`、品牌名 `MINECRAFT`→`PINEGLOW`、`brand-sub` 改为 `data-i18n="brand.subtitle">松光启动器`。同时新增未跟踪文件 `ui/css/pineglow.css`（18:10:07）、`ui/assets/pine-forest.svg`（18:08:34），并且**对本目标同样在编辑的 `ui/js/launcher.js`（18:11:08）、`ui/js/i18n.js`（18:08:34）做了增量写入**。

- **本目标没有回退任何外部改动**，也没有被外部改动回退：当前 `launcher.js` / `i18n.js` 中本目标的 `#rRetry`、`#bPendingRetry`、`#dhRetry`、忙态守卫与 5 个 i18n 键均在位。
- 针对**当前工作区**重跑：目标套件 **28/28**、全量 Node **266/266 / 0 skip** 仍然通过（`g2/node-goal-core-flow-current.log`、`g2/node-all-tests-current.log`）。
- 影响：G0–G4 的"输入冻结"前提在外部写者活跃期间不成立；G3 副本已落后于当前树（其 C# 结论不受影响，因为它不跑 UI JS）。**最终封存必须从一份新副本开始并记录它实际使用的 input-manifest SHA。**
- 完整记录：`g3/concurrent-drift-ui-index-html.json`（SHA `778DDC8ACEBC…`）。

## 5. 未运行 / 未验证

- RA-01/RA-02 恢复安全反证：**NOT_RUN**（平台终止范围，未执行、未委派、未换执行者）。
- 真实 Avalonia/WebView2 GUI、Minecraft、原生凭据往返、真实更新进程交接、跨卷/满盘、非 Windows、远程 CI：**UNVERIFIED**。

## 6. 下一轮计划（G4，仍在本目标内）

1. `scripts/run-goal-validation.ps1`：分阶段执行（restore/build/test/smoke/audit），子进程真实 exit 可靠传播；缺 TRX、非法 TRX、0 测试、required skip、超时、后台仍运行一律不 PASS；stage 绑定同一 input SHA 与产物 SHA；已完成 stage 且绑定一致才可复用。
2. `scripts/tests/goal-validation.test.cjs`：≥6 项合成用例（非零传播、缺 TRX、0 测试、required skip、中断不得复用、输入变化拒绝复用）。
3. 一次"失败 → 恢复"演示：让本轮自有的普通合成 stage 失败并保留，再用完整证据恢复。
4. 终态采集 → 封存 → **包外**审计 → 包内哈希无变化检查；产出 `machine-verdict.json`、`scenario-matrix.json`、`changed-files.json`、`final-report.md`、`manifest.json`、`manifest.sha256`，包为 `<EvidenceRoot>\FINAL-LOCAL-QUALITY\<runId>`。
5. 封存前必须确认外部写者已停止（否则以新副本重新绑定并把并发漂移写入 changed-files 与报告）。

---

## 7. 本轮追加：G4 可恢复运行器（已实现并验证，封存按用户决定暂缓）

用户在并发写入冲突上选择"继续做 G4、暂缓最终封存"，据此本轮完成：

**`scripts/run-goal-validation.ps1`**（SHA `E1DA3632…1E4A`，ASCII-only、PS 5.1 解析通过）：

- 子进程用 `Start-Process -PassThru -RedirectStandardOutput/Error` 启动，**等待结束后立即取真实 `ExitCode`**，不被任何日志写入覆盖；非零即 `EXIT_NONZERO` 失败。
- TRX 门槛：文件缺失 `TRX_MISSING`、XML 非法 `TRX_INVALID`、`total < minTests` `TESTS_BELOW_MIN`、`total = 0` `ZERO_TESTS`、`failed > 0` `TESTS_FAILED`、`notExecuted > maxSkipped` `REQUIRED_SKIP`。
- 日志门槛：`countPattern` 计数不足 `COUNT_BELOW_MIN`；`forbidPattern` 命中 `FORBIDDEN_PATTERN`。
- 超时 `TIMEOUT`、超时后仍在运行 `STILL_RUNNING` 均失败；`cwd` 缺失 `CWD_MISSING`；产物缺失 `ARTIFACT_MISSING`。
- 每个 stage 记录：`inputManifestSha256`、`fingerprint`（id+命令+工作目录+期望的展开后哈希）、真实命令、起止 UTC、退出码、TRX 计数器、每个产物的 SHA/大小、原始 stdout/stderr 的 SHA/大小、原因码。
- 复用规则：仅当记录为 `COMPLETED`、fingerprint 一致、input SHA 一致、且全部产物与原始证据 SHA 仍吻合才 `REUSED`；否则重跑，并把旧记录归档为 `<id>.incomplete.<旧runId>.json`（非 resume 重跑则归档为 `<id>.<旧runId>.json`），**从不覆盖**。
- 占位符展开（`{executionRoot}`/`{runDir}`/`{evidenceRoot}`/`{runId}`）在计算 fingerprint **之前**完成，因此换执行根或换 run 目录不可能被误判为同一 stage。

**`scripts/tests/goal-validation.test.cjs`**（SHA `61B1ED7B…B968`）：10 项合成用例全部通过（计划要求 ≥6）——正常通过并记录真实退出码、非零传播、缺 TRX、0 测试、required skip、TRX 内有失败、中断 stage 不得复用、输入变化拒绝复用、产物被篡改拒绝复用（并验证一致时可复用）、日志计数不足。原始日志：`g4/runner-synthetic-tests.log`（`3D639326…F911`）。

**失败→恢复演示**（`g4/run-g4-demo.ps1`，`g4/demo-summary.json` = `34FC325C…8155`）：

| 趟 | 结果 |
| --- | --- |
| pass1 | `demo-ok` COMPLETED；`demo-fail` **FAILED exit=2 EXIT_NONZERO**；runner exit **1**；两条记录均保留 |
| pass2（`-Resume`，新 runId） | `demo-ok` **REUSED**（输入 SHA 与证据均一致）；`demo-fail` 重跑 COMPLETED（exit 0）；旧失败记录归档为 `demo-fail.incomplete.demo-pass1.json`；runner exit **0** |

**真实 stage 定义已冻结**：`g4/goal-stages.json`（SHA `90C98F38…0224`），9 个 stage 覆盖 build/tests/node(266)/VerifyHost/bisect(19) 与**包外**严格审计；每个 `minTests` 都是上一次实测值（13/5/6/49/266/19），低于实测即失败。

**为什么仍未封存**：外部写者仍在同一工作区活动（18:30 的漂移检查新增 `.gitignore`、`docs/design-system.md` 两处范围外变化）。封存会把一个仍在变化的输入集写进 `manifest.json`，因此按用户决定推迟到输入集稳定后，从**一份新 GUID 副本**执行：终态采集 → 封存 → 包外审计 → 校验包内哈希未变，并产出 `machine-verdict.json`、`scenario-matrix.json`、`changed-files.json`、`final-report.md`。

---

**本轮终止状态：** G0 DONE · G1 DONE · G2 DONE · G3 DONE · G4 IN_PROGRESS（运行器与演示完成，封存待输入集稳定）。`ImplementationReady=false` · `IndependentAcceptance=NOT_GRANTED` · `FormalPhaseApproval=NOT_GRANTED` · `ReleaseApproval=NOT_GRANTED`。
本机闭环尚未满足；最终停在 **具备独立复验条件，等待 Codex 独立验收**。
