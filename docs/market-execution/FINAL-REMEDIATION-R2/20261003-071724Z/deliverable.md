# FINAL-REMEDIATION-R2 交付报告（S1–S4 / RA-01～RA-04）— 2026-10-03

**具备独立复验条件，等待 Codex 独立验收。**

技术验收、正式阶段批准、发布与后续产品任务**均未授予**；本轮不提交、不推送、不发布、不恢复完整 MR00–MR27 连续目标。

---

## 一、授权、基线与本轮对象

| 项 | 值 |
|---|---|
| 任务定义 | `0f45` 工作树 `docs/deepseek-harness-remediation-rework-workflow-2026-10-03.md` + 同名 prompt |
| 独立复验结论（本轮对象） | `docs/deepseek-harness-remediation-reacceptance-2026-10-03.md`：`TechnicalAcceptance=NOT_PASSED` |
| 源码根（唯一写入口） | `D:\松光启动器` |
| 本轮交付/证据根 | `C:\Users\Administrator\.codex\worktrees\0f45\松光启动器` |
| 源 HEAD（未变） | `f145d14eb26b0b7459a28491e764ac8c573085d3` |
| 工作区状态行数 | 变更前 **1363** → 变更后 **1365**（+2 = 本轮新增的 2 个文件，无其它增删） |
| 并发 | 无。执行前无运行中的 dotnet / Harness 任务；`D:` 未被其他进程写入 |

**改动范围保护（`dirty-protection.json`）**：以**上一轮**的 286 项快照输入为基准逐项比对当前源码 ——
**280 项字节完全一致、4 项变化、0 项落在允许集合之外**；另 2 项"缺失"是上一轮从交付根注入的探针文件
（它们本来就不在源目录）。变化的 4 项 + 新增的 2 项，全部落在 Codex 返修流程 §2 的白名单内。

**新增文件（先写精确路径与理由，符合 §2 要求）**

| 路径 | 理由 |
|---|---|
| `dev/PineglowLauncher/Pineglow.Launcher.Services/PathSafetyGuard.cs` | RA-01 的**唯一**最小实现载体：句柄式"真实路径 + 防替换"守卫。放进 `FileRecoveryService.cs` 会让该文件同时承担恢复事务与内核互操作两件事；单独成文件便于独立审查与移植。只服务本问题，不引入通用工具层。 |
| `dev/PineglowLauncher.Tests/RemediationBoundaryRegressionTests.cs` | 把 Codex 本轮 5 个边界探针转入常驻回归，并补齐 S1/S2/S3 要求的矩阵（越界/跨实例/跨操作/源 SHA/相对路径/ID 归一化/结构化字段/正常路径不退化）。 |

---

## 二、改动清单（最小 diff）

| 文件 | +/- | 内容 |
|---|---:|---|
| `dev/PineglowLauncher/Pineglow.Launcher.Services/PathSafetyGuard.cs` | **新增 10166 B** | `CreateFileW(FILE_FLAG_OPEN_REPARSE_POINT\|BACKUP_SEMANTICS)` + `GetFinalPathNameByHandleW` 逐级钉住目录链；共享模式**不含 FILE_SHARE_DELETE** |
| `dev/PineglowLauncher/Pineglow.Launcher.Services/FileRecoveryService.cs` | +128 / −27 | 可信根重载；锚点改为"可信实例根"；`StoredPath` 精确绑定 `<op>/files`；相对路径一致性；消费前核对源 SHA；`LoadAsync`/`ListAsync` 逐字 ID 绑定 |
| `dev/PineglowLauncher/Pineglow.Launcher.Services/LocalApiService.cs` | +18 / −7 | 恢复入口传入可信实例根；替换失败改为**结构化 JSON**（S3） |
| `dev/PineglowLauncher.Tests/FinalAcceptanceRegressionTests.cs` | +1 / −1 | 断言随新的绑定错误文案更新（行为未放宽） |
| `dev/PineglowLauncher.Tests/RemediationBoundaryRegressionTests.cs` | **新增 22515 B** | 15 项常驻回归 |
| `scripts/source-snapshot-inputs.txt` | +4 / −0 | 仅追加本轮 2 个新输入（类别 I） |

未修改：`SecretStore*`（R0 不得回退）、`ModReplacementService.cs`、`ModReplacementTests.cs`、`ModReplaceFileInUseTests.cs`、
`FileRecoveryEdgeCaseTests.cs`、`WorldBackupScopeTests.cs`、旧 manifest / 旧 log / 旧 RED / 真实 APPDATA / Windows 凭据 / 真实游戏存档。

---

## 三、S1 — 可信目标根与祖先链（RA-01 / P1）

**根因**：`mod-replace` 记录的 `GameDirectory` 是 `game\mods`；上一轮的校验只覆盖这个根及其内部，
把**上层 `game`** 换成 junction 后，撤销替换仍会把文件写到实例目录之外，接口返回 `200 / ok=true / restored`。

**设计（可证明的限制，而不是"检查过应该没问题"）**
1. 词法边界：目标必须严格位于**可信实例根**之内；记录里的 `GameDirectory` 只作候选，必须落在可信根之内。
2. `PathSafetyGuard` 逐级打开目录链（从目标父目录到锚点）：
   - `FILE_FLAG_OPEN_REPARSE_POINT` ⇒ 不跟随**最后一级**；**中间级由内核解析**；
   - `GetFinalPathNameByHandleW` 取真实路径并**与请求路径逐字比较** —— 记录根**之上**的任何一级
     （例如 `game`）被换成联接时，真实路径会与请求路径不同 ⇒ **写入前**拒绝；
   - 句柄共享模式 `FILE_SHARE_READ|WRITE`（**不含 DELETE**）⇒ 操作期间这些目录不能被改名/删除，
     "校验→写入"竞态被真正封住；缺目录在最近存在的祖先被钉住之后才创建，创建后再钉一次。
3. 托管层 `IsRealPathChain` + 叶子重解析点检查作为跨平台早期过滤；句柄校验失败一律**拒绝**
   （路径检查异常不当成"非联接"）。
4. 拒绝发生在消费隔离源之前；隔离副本、SHA、operationId 原样保留。

**RED（`red/red-boundary-probes-dsh.log`，我自己的独立复现；`red/red-boundary-probes-codex.log` 为 Codex 原始）**

```text
restoreHTTP=200; body={"ok":true,...,"status":"restored",...}; outsideFileExists=True
失败! - 失败: 4，通过: 1，总计: 5，exit 1
```

**GREEN（`green/green-boundary-probes.log`）**：同一份 Codex 探针 **5/5 通过**；
`green-r2-regressions.log` 的 RA-01 矩阵（HTTP / 带可信根 / **不带可信根** / 隔离区 `files` 被换成联接）**全部拒绝**，
且都不产生越界文件、隔离源仍在。其中"不带可信根"一项证明修复**不依赖调用方是否传锚点**——
`game` 被替换时，`game\mods` 的句柄真实路径已不等于请求路径。

---

## 四、S2 — 实例与操作绑定（RA-02 / P1）

**根因**：`StoredPath` 只要求位于**全局恢复根**内，未绑定本实例、本次操作；且 `SafeSegment` 会剥掉 `-` 等字符，
`ab` 与 `a-b` 落到同一目录，只按目录取记录会误碰另一个操作。

**实现**
1. `StoredPath` 必须精确落在 `<_recoveryRoot>/<本实例>/<本操作>/files` 之下（不是"恢复根内就行"）。
2. `entry.RelativePath` 必须与源在 `files` 下的实际相对路径一致。
3. **消费隔离源之前**核对源文件 SHA256 与记录一致。
4. `LoadAsync` / `ListAsync` 要求记录的 `InstanceId`/`OperationId` 与请求**逐字相等**（Ordinal，不做归一化）。
5. HTTP 层继续要求记录的游戏目录绑定当前实例；服务层执行同一约束（不依赖外层）。

**RED**（服务层与 HTTP 层同一根因）

```text
status=restored; target=other-instance-B; BStoredExists=False; AStoredExists=True
HTTP=200; ok=true; BStoredExists=False; target=http-other-instance-B
```

**GREEN**：跨实例（服务/HTTP）、**同实例跨操作**、源 SHA 不符、相对路径不符、ID 归一化碰撞 —— 全部拒绝；
A、B 的隔离源都不被移动；A 的普通恢复与 mod-replace 撤销仍通过。

---

## 五、S3 — HTTP 结构化失败（RA-03 / P2）

**根因**：服务层字段已存在，但 HTTP 层把 `needsRecovery` 等拼进中文散文后抛出，通用异常响应只有 `{error}`。

**实现**：替换失败改为局部结构化响应，**保持 400 与 `error` 字段兼容**，同时返回
`ok / status / needsRecovery / operationId / recoveryStatus / failedFiles`。未改动全局异常协议与无关路由。

**GREEN**：`commit_failed + 回滚失败` → `needsRecovery=true`、`recoveryStatus=failed`、`failedFiles` 非空；
`commit_failed + 回滚成功` → `needsRecovery=false`、`recoveryStatus=restored`、`failedFiles` 为空、旧字节复原；
`metadata_failed` → 同类结构化字段且旧包复原。客户端不再需要解析中文。

---

## 六、S4 — 证据门槛与封存（RA-04 / P2）

**做法**：把旧审计器**复制**到本包为 `audit-all-manifests-r2.py`（旧脚本原样保留、未覆盖），只做最小修复：
1. **非空目标**：目标清单为空（或根本身不存在）⇒ 失败；
2. **历史例外精确绑定字节**：必须同时满足"manifest 记录的旧 SHA/size""登记表里的当前 SHA/size""文件此刻实际值"三者吻合；
   **第三次漂移**判失败（`historical-exceptions.json` 是机器可读登记，`evidence-corrections.md` 是文字说明）；
3. **detached 校验**：`manifest.sha256` 必须与 `manifest.json` 一致；本修复线的包缺 detached ⇒ 失败；
4. 覆盖**两份交付根**（历史 59 份在 `ed41`，本轮新包在 `0f45`），全部层级、路径边界、缺项/漏登记/重复/越界/runId/自引用一并检查；
5. 审计输出放**包外**并另存 detached，避免"生成物自引用"。

**审计器自身的否证测试（`audit-negative-tests.py`，必须全部符合预期）**

| 合成输入 | 期望 | 实测 |
|---|---|---|
| 空根 | 非零 | exit 1 ✓ |
| 有目录无 manifest | 非零 | exit 1 ✓ |
| 错误 detached（64 个 0） | 非零 | exit 1 ✓ |
| 本修复线包缺 detached | 非零 | exit 1 ✓ |
| 清单列出的文件缺失 | 非零 | exit 1 ✓ |
| 包内存在未登记文件 | 非零 | exit 1 ✓ |
| 未知路径 SHA 漂移 | 非零 | exit 1 ✓ |
| runId 不一致 | 非零 | exit 1 ✓ |
| 清单自引用 | 非零 | exit 1 ✓ |
| **已登记路径出现第三种字节** | 非零 | exit 1 ✓ |
| 完整有效包 | 0 | exit 0 ✓ |
| **恰好等于登记的例外** | 0 | exit 0 ✓ |

`12 例 / 0 不匹配`。另有旧审计器的三种假通过作为**反例证据**保留在 `red/r1-auditor-*.json`。

**真实证据树审计（`../20261003-071724Z-audit.json`，exit 0）**：
`roots=2, manifestCount=61（含本包）, fileEntries=874, selfEntries=0, knownMismatches=2, failures=0`。
两处历史差异被单列为"保留的历史异常"，**没有**静默重算旧清单，**也没有**把"已知异常"写成"全部哈希一致"。

---

## 七、完整验证（同版代码、新 GUID 白名单副本 289 项输入，missing = 0）

| 检查 | 结果 | exit | 证据 |
|---|---|---:|---|
| Codex 本轮 5 个边界探针 | **5/5**（RED 为 1/5） | 0 | `green/green-boundary-probes.log` |
| 本轮常驻回归（新增） | **15/15** | 0 | `green/green-r2-regressions.log` |
| R0 凭据隔离专项 | **13/13** | 0 | `green/green-r0-secrets.log` |
| 前轮 20 验收探针 + 12 常驻回归 | **32/32** | 0 | `green/green-existing-probes.log` |
| C# 全量（含全部探针与新增回归） | **895 通过 / 0 失败 / 0 跳过** | 0 | `green/green-full-csharp.log` + TRX |
| Node（20 个入口） | **238 通过 / 0 失败** | 0 | `green/green-node.log` |
| Release VerifyHost 构建 | 成功（0 错误） | 0 | `green/green-verifyhost-build.log` |
| Bisect 真实 loopback HTTP + 文件沙盒 | **19/19 PASS** | 0 | `green/green-bisect-smoke.log` |
| 全层级清单审计（真实树） | 61 manifest / 874 记录 / 0 失败 | 0 | `../20261003-071724Z-audit.json` |
| 审计器合成否证 | 12 例全部符合预期 | 0 | `audit-negative-tests.json` |

**数量口径**：上一轮全量 875；本轮新增 15 项常驻回归 + 引入 Codex 5 个边界探针 ⇒ **895**。
13 / 32 是 895 的子集，不能累加；RED 与本轮重复运行不叠加到唯一用例数。
**没有**把"上一轮的 875"当作本轮结论，也没有削弱任何断言去凑 GREEN。

---

## 八、未验证 / 残留风险（不得外推）

1. **非 Windows**：没有等价的句柄语义，`PathSafetyGuard` 退回托管属性检查 ⇒ **该平台上的目录替换竞态未关闭**。
2. **句柄获取失败的策略是"拒绝"**：若链上目录被其他进程以不允许共享的方式占用，恢复会**失败**（而不是冒险写入）。
   这是有意的 fail-closed 取舍，但会表现为"某些占用场景下无法恢复"，需要在真实环境观察误拒率。
3. **`missingDetached` 口径**：历史 59 个包在建立 detached 约定之前生成，审计**报告**但**不判失败**；
   本修复线的包缺 detached 一律失败。该策略写在脚本 docstring 与本节，不隐藏。
4. 真实 **Avalonia/WebView2 GUI**、真实 **Minecraft**、**自更新真实进程交接**、**Windows 原生凭据读写往返**、
   **远程 CI/干净提交**、**跨卷/满盘**：本轮均未验证。
5. 真实游戏运行中的并发恢复（用户一边玩一边撤销替换）未验证；API 层仍有"运行中拒绝危险变更"的守卫。

---

## 九、复现命令

```powershell
$RUN = 'C:\Users\Administrator\.codex\worktrees\0f45\松光启动器\docs\market-execution\FINAL-REMEDIATION-R2\20261003-071724Z'
$ACC41 = 'C:\Users\Administrator\.codex\worktrees\ed41\松光启动器\docs\market-acceptance'
$ACC0F = 'C:\Users\Administrator\.codex\worktrees\0f45\松光启动器\docs\market-acceptance'
$extra = @(
  "$ACC0F\20261003-remediation-recheck\RemediationBoundaryProbes.cs|dev/PineglowLauncher.Tests/RemediationBoundaryProbes.cs",
  "$ACC41\20261003-final\FinalAcceptanceProbes.cs|dev/PineglowLauncher.Tests/FinalAcceptanceProbes.cs",
  "$ACC41\20261003-W1-W3\IndependentRouteProbes.cs|dev/PineglowLauncher.Tests/IndependentRouteProbes.cs"
)
& "$RUN\make-snapshot.ps1" -SourceRoot 'D:\松光启动器' -Dest "$env:TEMP\pineglow-r2-recheck" `
  -ManifestPath "$RUN\recheck-manifest.json" -ExtraInputs $extra
Set-Location "$env:TEMP\pineglow-r2-recheck"
dotnet test '.\dev\PineglowLauncher.Tests\Pineglow.Launcher.Tests.csproj' -c Release --filter 'FullyQualifiedName~RemediationBoundaryProbes' --logger trx
dotnet test '.\dev\PineglowLauncher.Tests\Pineglow.Launcher.Tests.csproj' -c Release --no-build --filter 'FullyQualifiedName~RemediationBoundaryRegressionTests' --logger trx
dotnet test '.\dev\PineglowLauncher.Tests\Pineglow.Launcher.Tests.csproj' -c Release --no-build --filter 'FullyQualifiedName~ConfigPreservationTests|FullyQualifiedName~SecretStoreBackendTests' --logger trx
dotnet test '.\dev\PineglowLauncher.Tests\Pineglow.Launcher.Tests.csproj' -c Release --no-build --filter 'FullyQualifiedName~FinalAcceptanceProbes|FullyQualifiedName~IndependentRouteProbes|FullyQualifiedName~FinalAcceptanceRegressionTests' --logger trx
dotnet test '.\dev\PineglowLauncher.Tests\Pineglow.Launcher.Tests.csproj' -c Release --logger trx
$nodeInputs = @(Get-ChildItem '.\ui\tests' -Filter '*.test.cjs' | Select-Object -ExpandProperty FullName); node --test @nodeInputs
dotnet build '.\dev\PineglowLauncher\VerifyHost\VerifyHost.csproj' -c Release
& '.\dev\PineglowLauncher\VerifyHost\bin\Release\net10.0\VerifyHost.exe' --bisect-smoke
# 证据门槛：先跑否证测试，再审计真实树
python "$RUN\audit-negative-tests.py"
python "$RUN\audit-all-manifests-r2.py"
```
> 等价一条命令：`& "$RUN\r2-green-run.ps1" -SourceRoot 'D:\松光启动器' -RunDir $RUN -Dest "$env:TEMP\pineglow-r2-recheck" -ProbeBoundary <…> -ProbeFinal <…> -ProbeIndependent <…> -Full`
> 再跑 `& "$RUN\run-node-and-host.ps1" -Root "$env:TEMP\pineglow-r2-recheck" -OutDir "$RUN\green"`。
> 本目录 `.ps1` 全部 **ASCII-only**（本机把无 BOM UTF-8 脚本按 ANSI 解析，中文注释会破坏语法）。

---

## 十、证据索引（本包 = `docs/market-execution/FINAL-REMEDIATION-R2/20261003-071724Z`）

| 文件 | 内容 |
|---|---|
| `deliverable.md` | 本报告 |
| `evidence-corrections.md` | 两份历史文件的来源/旧值/现值/差异（文字版） |
| `historical-exceptions.json` | 机器可读的历史例外登记（旧值 + 当前值，审计据此判"是否再次漂移"） |
| `audit-roots.json` | 审计覆盖的两份交付根（历史包在 ed41，本包在 0f45） |
| `audit-all-manifests-r2.py` / `audit-negative-tests.py` | 修复后的审计器 + 其合成否证测试（12 例） |
| `../20261003-071724Z-audit.json` / `.sha256` / `.log` | 真实树的审计结果（**包外**，防自引用） |
| `source-before.json` / `source-after.json` / `source-status-*.txt` | 源目录只读基线与前后工作区状态 |
| `dirty-protection.json` | 与上一轮 286 项输入的逐项比对（4 变 / 280 同 / 0 越界 / 2 交付根额外输入） |
| `green-snapshot-manifest.json`（289 项） | 本轮哈希绑定副本的输入清单（含 3 个探针） |
| `red/` | RED：Codex 原始 `07-new-boundaries.log`、我的独立复现日志、旧审计器三种假通过 JSON |
| `green/` | GREEN：边界探针 5/5、常驻回归 15/15、R0 13/13、既有 32/32、全量 895、Node 238、VerifyHost、Bisect 19、TRX |
| `make-snapshot.ps1` / `invoke-test.ps1` / `r2-green-run.ps1` / `capture-baseline.ps1` / `run-node-and-host.ps1` / `seal-evidence.ps1` | 本轮可复跑工具 |
| `manifest.json` / `manifest.sha256` | 本包封存清单（不自引用）与 detached |

**终止语：具备独立复验条件，等待 Codex 独立验收。**
