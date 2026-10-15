# MR06 · Bisect 文件操作失败必须阻止错误推进 · 交付报告

- **run-id**：`20261002-211500Z`
- **执行范围**：仅 MR06（任务计划 §6 第 4 包）
- **依据**：`docs/market-task-plan-2026-10-03.md` §6、`market-research-task-workflow-2026-10-02.md` §7
- **最终状态**：**具备独立复验条件，等待独立验收**
- **未执行**：MR07 及其它任务包；不提交、不推送、不发布

---

## 一、结论先行

| 项 | 结果 |
|---|---|
| 失败不再被吞 | ✅ `ApplyBisectStep`/`RestoreBisectMods` 返回**逐文件结果**（applied/unchanged/failed），不再只写日志 |
| 失败阻止推进 | ✅ 任一文件禁用失败 → **409 + `needsRecovery` + 逐文件原因**，绝不报"本轮已就绪" |
| 恢复前不移除会话 | ✅ 只有恢复逐项核对通过才 `Remove`；恢复失败保留会话与恢复上下文 |
| 候选一致性 | ✅ 部分失败时冻结候选、停止收窄；`needsRecovery` 期间拒绝测试反馈 |
| 不误启用 | ✅ 只遍历 `EnabledAtStart`，用户原本禁用的 Mod 保持禁用（RED 中即为既有正确行为） |
| 重复提交 | ✅ `test` 可带 `step`；轮次不符返回 409 且**不推进**（防双击错误收窄） |
| 并发 | ✅ 同一实例的 start/test/giveup 用 `TryAdd` 拒绝并发，而不是排队 |
| 手动操作冲突 | ✅ 定位进行中，toggle/delete/replace 一律 400 |
| UI | ✅ 新增「文件操作未完成，需要恢复」面板（含逐文件原因与「重试恢复」），与"定位完成/已恢复"明确区分 |
| C# 测试 | ✅ 新增 9 个用例；实现前 **7 失败 / 2 通过** → 实现后 **9 通过 / 0 失败** |
| 既有 Bisect 单测 | ✅ `ModBisectServiceTests` 12 个用例在全量中仍全绿 |
| C# 全量回归 | ✅ **664 通过 / 0 失败 / 0 跳过**（655 + 9） |
| Node 全量回归 | ✅ **202 通过 / 0 失败**（13 个文件） |
| API 级 E2E | ✅ `VerifyHost --bisect-smoke` exit=0（不带 `step` 的旧调用仍兼容） |
| 真实 GUI | ⬜ **未验证** |

---

## 二、问题（改动前，逐行确认）

```csharp
// ApplyBisectStep / RestoreBisectMods
catch (Exception ex) { LauncherLog.Error("[Bisect] disable " + name + " failed: " + ex.Message, ex); }
```

```csharp
// HandleBisectTestAsync 结束路径
RestoreBisectMods(service, Path.Combine(gameDir, "mods"), session);
ModBisectService.Remove(instanceId);        // ← 无条件移除
```

三条后果：
1. **禁用一半失败仍然 200 且 `waitingForReport=true`** —— 用户以为"这一轮已经就绪"，实际磁盘上只有一部分被禁用，**候选集合与落盘状态不一致**，据此反馈会把真正的元凶排除掉；
2. **恢复失败也把会话删掉** —— 恢复依据（`EnabledAtStart`）随会话一起消失，用户再也看不到哪些文件没回来，也没有重试入口；
3. 完成路径无论恢复是否成功都报 `finished=true`，UI 显示"已恢复所有 Mod"——**假的成功**。

---

## 三、实际改动

| 文件 | 状态 | 改后 SHA256 |
|---|---|---|
| `dev/PineglowLauncher/Pineglow.Launcher.Services/ModBisectService.cs` | 修改 | `DE76290828F043E9C8413C5D8398C0221057C20665B14900BA3925BB599DBE09` |
| `dev/PineglowLauncher/Pineglow.Launcher.Services/LocalApiService.cs` | 修改（原已是 `M`） | `6F060139649FF9CD8847DD28666FD96F6BB3B7A633E8D375483CB65AD51FBC75` |
| `dev/PineglowLauncher.Tests/BisectFileOperationTests.cs` | **新增** | `068DDDD3E44B90DAFD8EDCD4DB75A641E11234C7D84C524AECD912C80631C949` |
| `ui/js/api.js` | 修改（原已是 `M`） | `FE62565F719E3281F31D62033029AAC4D8CB8C55DBB83D3FABFE289F5C94DDAA` |
| `ui/js/launcher.js` | 修改（原已是 `M`） | `7F782DFFAA03F1A95A86F243D3C771220CE05FC1CDF8D8D34646AD334735BB73` |
| `ui/js/i18n.js` | 修改（原已是 `M`） | `698D83F63280E06F11CF75C89EA13B0AA6C07CFA5536EDC668B7614FF7CED3D3` |
| `scripts/source-snapshot-inputs.txt` | 修改（未跟踪） | `6D0D8F31E0F18E1D3AE686875140D38ED720C93528E176083A043E65431D21EA` |
| `docs/api-reference.md` | 修改 | `1276EB8D1B17729984A07EBE8D38DA236E2D43170FA8DE55BFF1E77C45B82427` |

`git status` 由 1300 → 1302 行（+2：`ModBisectService.cs` 新变为 `M`、新增测试文件）。

### 3.1 `ModBisectSession` 新增的状态

| 成员 | 含义 |
|---|---|
| `LastResults` | 上一次文件操作的逐项结果（`ModBisectFileResult`） |
| `NeedsRecovery` / `RecoveryError` | 状态与磁盘不一致，必须恢复后才能继续 |
| `Version` | 每次状态变更自增，便于识别"同一轮的重复结果" |
| `RecordStepResults` | 有 `failed` 就置 `NeedsRecovery`，并把 `WaitingForReport` 置 false、清空 `DisabledHalf`（**候选不再收窄**） |
| `RecordRestoreResults` | 全部成功才解除 `NeedsRecovery` |
| `TryReportTest(crashed, out rejected)` | 只在"确实在等待反馈"时受理；`NeedsRecovery` / `Finished` / 未在等待都明确拒绝 |

### 3.2 `LocalApiService`

- `ApplyBisectStep` / `RestoreBisectMods` 改为返回 `List<ModBisectFileResult>`：
  - 禁用后核对"`.jar` 消失且 `.jar.disabled` 出现"，未达成即 `failed`（不只是"没抛异常就算成功"）；
  - 恢复时区分 `unchanged`（本来就是启用态）与 `applied`；两种态同时存在 → `failed`（无法确定原状）。
- 三个 handler 都改为：**409 + `needsRecovery` + `files[]`**，`start` 在已有 `needsRecovery` 会话时直接拒绝开新一轮。
- `giveup` / `test` 结束路径：**核对通过才 `Remove`**，否则保留会话以便重试。
- 单写锁：`BisectInFlight` + `TryAdd`，并发请求返回 409。
- `RejectManualModEditDuringBisect`：定位进行中拒绝 `toggle`/`delete`/`replace`。
- 轮次校验：`test` body 里的 `step` 与当前轮次不符 → 409 「该轮已经推进」，**不推进**。

### 3.3 UI

- `_req` 现在把结构化错误体挂到 `err.payload`，二分定位面板据此渲染「文件操作未完成，需要恢复」分支：显示逐文件状态与原因 + 「重试恢复」（再次 `giveup` 触发恢复）。
- 「定位完成/已恢复」分支只在 `!needsRecovery` 时出现；提交测试结果时带上 `data.step`。
- 中英各新增 3 个键。

---

## 四、RED → GREEN 证据

### RED（回退到 MR06 之前的 `LocalApiService.cs`（MR05 版）与 `ModBisectService.cs`（HEAD 版））

**7 失败 / 2 通过**：

失败（本包的新契约）：禁用失败仍报就绪、needsRecovery 期间仍可推进、恢复失败仍删会话、修好磁盘后无从重试、过期轮次仍推进、定位中仍可手动改 Mod、并发占位不存在。

通过（**既有正确行为**，不计为本包成果）：
- `HappyPath_StartTestGiveUp_StillWorksWithVerifiedRestore` —— 8→4→2→1 正常路径本来就对；
- `GiveUp_DoesNotReEnableModsTheUserHadDisabled` —— 旧实现只遍历 `EnabledAtStart`，用户原本禁用的 Mod 本来就安全。

> RED 基线额外补了 3 个**惰性**测试帮助成员（`IsBisectOperationInFlightForTest` 等）以便测试程序集能编译；它们不改变被测行为，仅让"缺 API" 表现为行为失败而不是编译失败。

日志：`red/01-bisect-io-before.log`。

### GREEN（最终产物）

| 检查 | 结果 | 日志 |
|---|---|---|
| `--filter …BisectFileOperationTests` | **失败 0 / 通过 9** | `green/02-bisect-io-after.log` |
| `VerifyHost --bisect-smoke` | **exit 0** | `green/03-bisect-smoke.log` |
| `dotnet test`（全量） | **失败 0 / 通过 664 / 跳过 0**（55 s） | `green/04-csharp-full.log` |
| `node --test`（13 个 `.test.cjs`） | **tests 202 / pass 202 / fail 0** | `green/05-node-full.log` |

计划要求的情境覆盖：

| 情境 | 用例 | 结果 |
|---|---|---|
| 一个操作失败不得报本轮就绪 | `Start_WhenDisableFails_Reports409WithNeedsRecovery_NotRoundReady` | ✅ 409 + `needsRecovery` + `waitingForReport=false` + `a.jar` 未被半禁用 |
| 部分失败后不收窄 | `Test_DuringNeedsRecovery_IsRejected_AndDoesNotNarrowCandidates` | ✅ 409，`finished` 仍为 false |
| 恢复失败保留会话 | `GiveUp_WhenRestoreFails_KeepsSessionAndRecoveryContext` | ✅ 409 + 会话仍在（后续报"未完成"而非"没有会话"） |
| 恢复核对通过才移除 | `GiveUp_RetryAfterFixingDiskState_RestoresAndRemovesSession` | ✅ 修好磁盘后重试 200，SHA 还原，之后才报"没有会话" |
| 原已禁用不被误启用 | `GiveUp_DoesNotReEnableModsTheUserHadDisabled` | ✅ `b.jar.disabled` SHA 不变 |
| 同轮重复不推进两轮 | `Test_WithStaleStep_IsRejectedInsteadOfAdvancingWrongRound` | ✅ 409「已经推进」，`step` 未变 |
| 并发有确定结果 | `ConcurrentGuard_RejectsSecondOperationOnSameInstance` | ✅ 第二次被拒，释放后可再取 |
| 手动操作冲突显式拒绝 | `ManualModEdits_AreRejectedWhileBisectIsActive` | ✅ toggle/delete/replace 均 400，文件未被改动 |
| 正常路径不回归 | `HappyPath_…` | ✅ `culprit=a.jar`，两 jar SHA 不变 |

---

## 五、明确未做与未验证

| 项 | 说明 |
|---|---|
| **`VerifyHost --bisect-smoke` 未按计划扩展故障注入** | 计划要求"以故障注入补充现有 8→4→2→1 正常路径"。本包只**确认了既有 smoke 仍 exit 0**（含不带 `step` 的兼容路径），**没有**给 smoke 增加故障场景。故障注入覆盖在 C# 测试里完成。 |
| 真实 GUI / WebView2 | 未启动应用；「需要恢复」面板与「重试恢复」按钮的实际呈现与点击链路未验证 |
| 真实游戏验证 | 未启动游戏；"崩溃/没崩"的反馈仍由人工给出 |
| 并发 | 只验证了并发原语（`TryBeginBisectOperationForTest`），没有构造两个真正并行的 HTTP 请求 |
| 文件被占用 / 权限错误 | 用"把目标路径占成目录"制造冲突，覆盖了改名失败这一类；**没有**构造真实文件句柄占用或 ACL 拒绝 |
| MR07（重启恢复） | 未开始；本包只保证"会话在恢复核对通过前不会被移除"，**会话仍是内存态**，进程重启后依然丢失 |

---

## 六、回退方法

```powershell
$run = 'C:\Users\Administrator\.codex\worktrees\ed41\松光启动器\docs\market-execution\MR06-bisect-file-operation-failures\20261002-211500Z'
Copy-Item "$run\green\LocalApiService.cs.mr06" `
  'D:\松光启动器\dev\PineglowLauncher\Pineglow.Launcher.Services\LocalApiService.cs' -Force
Copy-Item "$run\green\ModBisectService.cs.mr06" `
  'D:\松光启动器\dev\PineglowLauncher\Pineglow.Launcher.Services\ModBisectService.cs' -Force
Get-Item 'D:\松光启动器\dev\PineglowLauncher\Pineglow.Launcher.Services\LocalApiService.cs',
         'D:\松光启动器\dev\PineglowLauncher\Pineglow.Launcher.Services\ModBisectService.cs' | ForEach-Object { $_.LastWriteTime = Get-Date }
```

其余：删除新增测试文件；`ui/js/*` 与 `api-reference.md`、白名单按 §三 反向删除。
**注意**：`LocalApiService.cs` 会话前带 UTF-8 BOM，还原后需确认 BOM 状态；两个 C# 文件还原后必须 `LastWriteTime = Get-Date`，否则 MSBuild 跳过重编译。

---

## 七、证据清单

```
red/01-bisect-io-before.log              回退后 7 失败 / 2 通过
green/
  LocalApiService.cs.mr06                MR06 实现版备份（SHA BD7A9C41…，不含 BOM）
  ModBisectService.cs.mr06               MR06 实现版备份（SHA DE762908…）
  *.sha256.txt
  01-bisect-io-first.log                 首次运行 9 通过
  02-bisect-io-after.log                 还原后 9 通过 / 0 失败
  03-bisect-smoke.log                    exit 0
  04-csharp-full.log                     664 通过 / 0 失败
  05-node-full.log                       202 通过 / 0 失败（13 个文件）
  git-status-after.txt                   1302 行
  changed-files.sha256.txt
manifest.json
```

**本报告不宣布阶段放行、不提交、不发布。** 下一包建议 `MR07`（Bisect 重启恢复、依赖与世界安全）——本包把会话的"恢复依据"保护住了，但会话仍是内存态，MR07 正是把它落盘。
