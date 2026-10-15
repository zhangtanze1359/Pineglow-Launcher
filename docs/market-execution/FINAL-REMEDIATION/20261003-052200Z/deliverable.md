# FINAL-REMEDIATION 交付报告（R0–R4）— 2026-10-03

**具备独立复验条件，等待 Codex 独立验收。**

技术验收、正式阶段批准、发布与后续产品任务**均未授予**；本轮不提交、不推送、不发布、不恢复完整 MR00–MR27 连续目标。

---

## 一、授权与基线

| 项 | 值 |
|---|---|
| 源码根（唯一写入口） | `D:\松光启动器` |
| 文档/证据交付根 | `C:\Users\Administrator\.codex\worktrees\ed41\松光启动器` |
| 源 HEAD（未变） | `f145d14eb26b0b7459a28491e764ac8c573085d3` |
| 验收对象 | **当前 dirty 工作区**，不是仅 HEAD |
| 工作区状态行数 | 变更前 **1360** → 变更后 **1363**（+3 = 本轮新增的 3 个文件，无其它增删） |
| 冲突并发 | 无。执行前 Harness 连续目标为 paused，本轮未启动任何 Harness turn |
| 本轮任务定义 | `docs/deepseek-harness-final-remediation-workflow-2026-10-03.md` + 同名 prompt（交付根 `docs/`） |

**改动范围保护（`dirty-protection.json`）**：以独立验收的 281 项快照输入为基准逐项比对当前源码 ——
**275 项字节完全一致、6 项变化、0 项落在允许集合之外、0 项缺失**。变化的 6 项就是本轮获准修改的 6 个文件。

---

## 二、改动清单（最小 diff）

| 文件 | +/- | 内容 |
|---|---:|---|
| `dev/PineglowLauncher/Pineglow.Launcher.Services/SecretStore.cs` | +85 / −182 | 读/写/删改为经可注入后端；原生 P/Invoke 全部移出 |
| `dev/PineglowLauncher/Pineglow.Launcher.Services/SecretStoreBackend.cs` | **新增 10205 B** | `ISecretStoreBackend` + Windows 原生后端 + 受限文件后端 + 内存后端 |
| `dev/PineglowLauncher/Pineglow.Launcher.Services/FileRecoveryService.cs` | +157 / −3 | 恢复前真实路径链校验、隔离副本必须落在本恢复区、写入后复核、失败状态如实 |
| `dev/PineglowLauncher/Pineglow.Launcher.Services/ModReplacementService.cs` | +98 / −11 | 回滚返回结构化真实结果；不再吞异常、不再无条件宣称"已回滚" |
| `dev/PineglowLauncher/Pineglow.Launcher.Services/LocalApiService.cs` | +35 / −15 | 只读 Mod 列表去除越界守卫；恢复记录与实例绑定校验；替换失败带 needsRecovery；GroupName 文案更正 |
| `dev/PineglowLauncher.Tests/ConfigPreservationTests.cs` | +38 / −16 | 5 个原用例改在内存后端作用域内执行并断言原生调用 0 次 |
| `dev/PineglowLauncher.Tests/SecretStoreBackendTests.cs` | **新增 9493 B** | 后端注入/作用域/异常/键名/原生 0 调用证明 + 进程默认隔离 |
| `dev/PineglowLauncher.Tests/FinalAcceptanceRegressionTests.cs` | **新增 23616 B** | F-01/F-03/F-04 常驻回归 + 边界矩阵（12 项） |
| `scripts/source-snapshot-inputs.txt` | +6 / −0 | 仅追加 3 个新输入（类别 H） |

未修改：`ConfigStore.cs`、`FileRecoveryTests.cs`、`ModReplacementTests.cs`、`WorldBackupScopeTests.cs`、
`FileRecoveryEdgeCaseTests.cs`、`ModReplaceFileInUseTests.cs`、任何历史 manifest / 原始 log / 旧 RED / 真实 APPDATA / Windows 凭据。

---

## 三、R0 — 凭据后端测试隔离（F-02 / P1）

**设计**：`SecretStore.Load/Save` 只做名称校验 + 委派；后端为 `AsyncLocal` 作用域覆盖 `??` 进程默认后端。
生产默认：Windows → `WindowsCredentialSecretBackend`（目标名仍 `MCMiniLauncher/{name}`、Generic、LocalMachine、
1168 不抛、其它 Win32 错误抛 `Win32Exception`）；非 Windows → `RestrictedFileSecretBackend`（`secrets.json`，键名不加前缀）。
命名、前缀、迁移策略与落盘格式**一律不变**。

测试程序集在 `[ModuleInitializer]` 里把**进程默认后端**换成内存后端 —— 忘记写作用域的未来用例也碰不到真实凭据（fail-safe）。

**RED（`red/red-summary.json`、`red/credential-intents.jsonl`）**：在**完全阻断原生调用**的独立副本里，
把 `SecretStore.Load/Save` 换成记录"意图"后立即返回，再跑原版 5 个配置用例：

```text
exit=0（测试"通过"） delete-intent=20  write-intent=0  read-stub=15  事件行=35
```

与独立验收的 `credential-intents.jsonl` **完全一致**（20 次删除意图）。也就是说：修复前，跑一遍配置测试就可能删掉真实凭据。

**GREEN（`green/green-r0-secrets.log`）**：`13/13 通过，exit 0`
（`ConfigPreservationTests` 5 + `SecretStoreBackendTests` 8）。
证明方式不是"secrets.json 没变"，而是三件事同时断言：
① 进程默认后端是内存实现、原生后端类型不可达；② 本轮操作在内存后端上确实被受理（非空断言）；
③ `SecretStore.ProductionBackendCallCount` 增量为 **0**。另有静态守卫：`SecretStore.cs` 不含任何 `DllImport`/`Cred*`，
而 `SecretStoreBackend.cs` 保留了三个原生入口（反向证明守卫非空）。

---

## 四、R1 — 恢复路径真实边界（F-01 / P1）

**修复**：`RestoreOne` 在**任何创建/删除/覆盖/移动之前**依次校验：
① 目标在本实例游戏目录内（原有词法检查）；② `IsRealPathChain` —— 游戏根自身、叶子自身、以及从最近存在的祖先到根之间
每一级都不得是重解析点；③ `record.StoredPath` 必须落在本服务自己的恢复根之内（记录是**磁盘输入**，不可信），
并同样通过真实路径链校验；④ HTTP 入口增加"记录 ↔ 实例游戏目录"绑定校验（只允许实例游戏目录或其子目录，兼容 `mod-replace` 用 mods 目录）。
写入后执行 `TryFindReparsePointOnPath` **复核**；一旦路径上出现重解析点即如实报 failed。
失败时逐项状态为 `failed`、整体状态为 `failed`、隔离副本与 SHA 原样保留（可再次恢复），API 返回 `ok=false`。

**RED（`red/red-indep-probes.log`，与验收原始探针同一份代码）**：

```text
state=restored, outsideFileExists=True, storedExists=False
API restore => 200: {"ok":true,...,"status":"restored",...}; outsideFileExists=True
```

**GREEN（`green/green-independent-probes.log`）**：同一份探针 **20/20 通过**；
其中 API 现在返回 `{"ok":false,"status":"failed", "files":[{"state":"failed","error":"恢复目标路径不可信…重解析点（junction）…"}]}`。
新增边界矩阵（`FinalAcceptanceRegressionTests`）覆盖：服务级/HTTP 级 junction、游戏根自身为联接、伪造的越界 `OriginalPath`、
伪造的恢复区之外 `StoredPath`、正常恢复、默认冲突不覆盖、显式覆盖、缺失父目录重建、中文/空格/连字符名（含 sidecar）。

**残留风险（明确不声称关闭）**：校验与实际写入之间仍存在 TOCTOU 窗口。本轮的处理是"校验 + 写入后复核 + 拒绝时保留隔离副本"，
**没有**使用不跟随链接的句柄式目录访问，因此**不能宣称竞态已被彻底关闭**；该窗口未被本机探针触发，也没有被证明不可利用。

---

## 五、R2 — 回滚结果不得失真（F-03 / P2）

**修复**：`RollbackAsync` 返回 `RollbackOutcome`（逐项成败 / 记录状态 / 未恢复文件 / 异常原文），
异常不再被吞；`ModReplacementResult` 新增 `NeedsRecovery`、`RecoveryStatus`、`FailedFiles`。
**只有逐项都回到原位**才显示"已回滚到旧版本"；否则错误文案明确写"未能完全回滚，旧版本仍需人工恢复"，
并带 operationId、回滚状态与逐文件失败原因；HTTP 层在失败时追加 `needsRecovery=true / operationId / recoveryStatus / 未恢复文件`。

**RED**：`status=commit_failed, error=写入新版本失败，已回滚到旧版本：injected commit failure,
targetBytes=foreign-readonly, recoveryStatus=partial`（注释与探针断言"不得出现『已回滚』"失败）。

**GREEN（`green/green-r1-r3-focused.log` 41/41）**：
- 只读目标注入：`NeedsRecovery=true`、`RecoveryStatus=partial`、错误文案不含"已回滚"、旧文件仍留在恢复区；
  解除只读阻断后**再次恢复成功**（`restored`，字节回到 `old-A`）。
- 健康目标注入：`NeedsRecovery=false`、`RecoveryStatus=restored`、文案含"已回滚"、jar 与 sidecar 逐字节复原。

---

## 六、R3 — 运行态守卫限定范围（F-04 / P2）

**修复**：`HandleListModsAsync` 去掉运行态守卫（只读列表不该拦重复启动）。守卫在变更/启动/危险操作上**全部保留**。

**RED**：`running instance GET mods => 400 {"error":"该实例已在运行（进程 …），请勿重复启动。"}`。

**GREEN**：运行中 `GET /mods` → **200**（且 jar 字节不变）；同一运行态下
`POST /mods/a.jar/toggle` / `delete` / `replace` 与 `POST /instances/{id}/delete` 仍返回 **400**（文案分别是"请勿重复启动"与"正在运行"）。
逐路由现状（含本轮**未修改**的 6 个同类只读路由）见 `evidence-corrections.md` 第四节 —— 未擅自扩项。

---

## 七、R4 — 证据、清单与全量回归

**全量结果（本轮独立副本 `pineglow-far-green-4c8e21b7d0a94f6e8b3d5c7a91e2f0bd`，286 项哈希绑定输入：
161 个 tracked + 123 个白名单 + 2 个交付根探针，missing = 0）**

| 检查 | 结果 | 退出码 | 原始证据 |
|---|---|---:|---|
| C# 全量（含 20 个独立验收探针） | **875 通过 / 0 失败 / 0 跳过** | 0 | `green/green-full-csharp-final.log`、`green/test-results/green-full-csharp-final.trx` |
| C# 同口径前后对比：修复前 | 850 总计 / **846 通过 / 4 失败** | 1 | `red/red-baseline-safe-full.log` |
| C# 同口径前后对比：修复后 | 870 总计 / **870 通过 / 0 失败** | 0 | `green/green-comparable-full.log` |
| R0 专项（配置 5 + 后端 8） | 13/13 | 0 | `green/green-r0-secrets.log` |
| R1–R3 专项（含新增 12 项） | 41/41 | 0 | `green/green-r1-r3-focused.log` |
| 独立验收原 20 探针 | **20/20** | 0 | `green/green-independent-probes.log` |
| Node 前端（20 个入口） | **238 通过 / 0 失败** | 0 | `green/green-node.log`、`green/node-inputs.txt` |
| Release VerifyHost 构建 | 成功 | 0 | `green/green-verifyhost-build.log` |
| Bisect 真实本地 HTTP + 文件沙盒 | **19/19 检查通过** | 0 | `green/green-bisect-smoke.log` |
| 全层级清单审计 | 见下 | 0 | `independent-evidence-audit.json` |

**口径说明**：修复前 850 = 830（可在原生产实现上安全复跑）+ 20 探针；修复后 870 = 850 + 20 本轮新增测试。
全量 875 = 835（原有全部，含此前无法安全运行的 5 项配置用例）+ 20（本轮新增）+ 20（独立验收探针）。
**没有**把"替换后端后的取证测试"与"原生产实现上的安全回归"合并宣称。

**全层级清单审计（`audit-all-manifests.py`，exit **0**）**：
覆盖 `docs/market-execution` 下**全部 60 份 manifest**（59 份历史 + 本轮新增包）、**830 项文件记录**，逐项校验路径边界、缺失、漏登记、
SHA256、大小、runId；`manifest.json` 自身出现在清单内即判失败 —— **本轮 0 例自引用**（F-05 的自引用问题在新包中不再出现）；
detached SHA 另存 `manifest.sha256`。结果：`{"manifestCount":60,"fileEntries":830,"selfEntries":0,"knownMismatches":2,"failures":0}`。
F-05 的 2 项已知差异单列为 `knownMismatches`，**不静默重算、不覆盖旧 manifest**，说明见 `evidence-corrections.md`。
审计输出放在**包外**（`FINAL-REMEDIATION/20261003-052200Z-audit.json` 及其 `.sha256`）以避免"生成物自引用"的循环——
这是 F-05 同一教训的第二处应用。

---

## 八、未验证 / 残留风险（不得外推）

1. **真实游戏**未启动；R3 的"运行中"用当前测试进程 PID 合成注册，未跑真实 Minecraft 进程。
2. **Avalonia / WebView2 GUI** 未做本轮交互验收（API 层通过不等于 GUI 通过）。
3. **真实 Windows 凭据的写入→读回往返**未验证：本轮证明的是"测试不再触碰原生 API"，不是"原生后端读写往返正确"。
   原生后端代码按原逻辑搬迁（逐行等价），但没有真实凭据往返证据。
4. **校验与写入之间的 TOCTOU** 未关闭（见第四节残留风险）。
5. **自更新真实进程交接、远程 CI、干净提交构建、跨平台运行**均未验证。
6. 另 6 个**只读**路由仍在运行态被 400 拦截（登记未改，见 `evidence-corrections.md`）。
7. 本轮**未**重新执行 7 个项目验证/审计脚本（`verify-mg0..3`、`audit-*`）——它们属于历史里程碑包，本轮只做全层级清单审计；若需一并复跑，请在独立验收时按 `STATUS.md` 第二节执行。

---

## 九、复现命令（全部在独立副本内，不要直接对 D 盘跑动态测试）

```powershell
$RUN = 'C:\Users\Administrator\.codex\worktrees\ed41\松光启动器\docs\market-execution\FINAL-REMEDIATION\20261003-052200Z'
$ACC = 'C:\Users\Administrator\.codex\worktrees\ed41\松光启动器\docs\market-acceptance'
# 1) 一条命令重建哈希绑定副本（含交付根的两个独立验收探针；它们也会被写进 green-snapshot-manifest.json）
$extra = @(
  "$ACC\20261003-final\FinalAcceptanceProbes.cs|dev/PineglowLauncher.Tests/FinalAcceptanceProbes.cs",
  "$ACC\20261003-W1-W3\IndependentRouteProbes.cs|dev/PineglowLauncher.Tests/IndependentRouteProbes.cs"
)
& "$RUN\make-snapshot.ps1" -SourceRoot 'D:\松光启动器' -Dest "$env:TEMP\pineglow-recheck" `
  -ManifestPath "$RUN\recheck-manifest.json" -ExtraInputs $extra
# 2) 分区回归
Set-Location "$env:TEMP\pineglow-recheck"
dotnet test '.\dev\PineglowLauncher.Tests\Pineglow.Launcher.Tests.csproj' -c Release --filter 'FullyQualifiedName~ConfigPreservationTests|FullyQualifiedName~SecretStoreBackendTests' --logger trx
dotnet test '.\dev\PineglowLauncher.Tests\Pineglow.Launcher.Tests.csproj' -c Release --no-build --filter 'FullyQualifiedName~FileRecovery|FullyQualifiedName~ModReplacement|FullyQualifiedName~WorldBackupScope|FullyQualifiedName~FinalAcceptanceRegressionTests' --logger trx
dotnet test '.\dev\PineglowLauncher.Tests\Pineglow.Launcher.Tests.csproj' -c Release --no-build --filter 'FullyQualifiedName~FinalAcceptanceProbes|FullyQualifiedName~IndependentRouteProbes' --logger trx
dotnet test '.\dev\PineglowLauncher.Tests\Pineglow.Launcher.Tests.csproj' -c Release --logger trx
# 3) 前端 / 宿主
$nodeInputs = @(Get-ChildItem '.\ui\tests' -Filter '*.test.cjs' | Select-Object -ExpandProperty FullName)
node --test @nodeInputs
dotnet build '.\dev\PineglowLauncher\VerifyHost\VerifyHost.csproj' -c Release
& '.\dev\PineglowLauncher\VerifyHost\bin\Release\net10.0\VerifyHost.exe' --bisect-smoke
# 4) 全层级清单审计（exit 1 表示存在已知差异以外的异常；root 由脚本自身位置推导，不经 argv 传中文路径）
python "$RUN\audit-all-manifests.py"
```
> 等价的一条命令：`& "$RUN\green-run.ps1" -SourceRoot 'D:\松光启动器' -RunDir $RUN -Dest "$env:TEMP\pineglow-recheck" -ProbeFinal "$ACC\20261003-final\FinalAcceptanceProbes.cs" -ProbeIndependent "$ACC\20261003-W1-W3\IndependentRouteProbes.cs" -Full`，
> 再跑 `& "$RUN\run-node-and-host.ps1" -Root "$env:TEMP\pineglow-recheck" -OutDir "$RUN\green"`。
> 说明：本目录下的 `.ps1` 全部为 **ASCII-only** —— 本机 PowerShell 会把无 BOM 的 UTF-8 脚本按 ANSI 解析，中文注释会破坏语法（本轮踩过一次）。

---

## 十、证据索引（本包 = `docs/market-execution/FINAL-REMEDIATION/20261003-052200Z`）

| 文件 | 内容 |
|---|---|
| `deliverable.md` | 本报告 |
| `evidence-corrections.md` | F-05 差异、GroupName 文案、测试口径、守卫现状 |
| `known-mismatches.json` | 2 项**已知且不重写**的清单差异（审计据此单列） |
| `audit-all-manifests.py` / `../20261003-052200Z-audit.json` + `.log` + `.sha256` | 全层级清单审计脚本与结果（**输出在包外**，避免生成物自引用） |
| `source-before.json` / `source-after.json` / `source-status-*.txt` | 源目录只读基线与前后工作区状态 |
| `dirty-protection.json` | 281 项输入的前后逐项比对（6 变 / 275 同 / 0 越界） |
| `minimal-diff-numstat.txt` | 逐文件 +/- 行数 |
| `green-snapshot-manifest.json`（286 项）/ `red-copy-root.txt` / `green-copy-root.txt` / `pre-fix-source-root.txt` | 哈希绑定副本输入（含交付根 2 个探针）与各工作副本位置 |
| `red/` | RED 证据：20 探针 4 失败日志 + 20 次 delete-intent 取证 + 修复前同口径全量（846/850） |
| `green/` | GREEN 证据：全量 875、专项 13/41、20 探针、Node 238、VerifyHost、Bisect 19、以及 TRX |
| `green/green-full-csharp.log` | **中间态**：一次 870 的全量（当时 `FinalAcceptanceProbes.cs` 还没纳入快照清单，只有 `IndependentRouteProbes` 15 项）。已被 `green-full-csharp-final.log`（875，含 20 探针）取代，**保留以备对照**，不作为结论依据。 |
| `make-snapshot.ps1` / `capture-baseline.ps1` / `invoke-test.ps1` / `r0-red.ps1` / `green-run.ps1` / `run-node-and-host.ps1` | 本轮全部可复跑工具（构建副本、记录命令、RED 取证、分区回归） |
| `manifest.json` / `manifest.sha256` | 本包封存清单（**不自引用**）与 detached SHA |

**终止语：具备独立复验条件，等待 Codex 独立验收。**
