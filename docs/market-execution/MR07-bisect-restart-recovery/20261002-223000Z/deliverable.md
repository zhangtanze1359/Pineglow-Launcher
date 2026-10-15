# MR07 · Bisect 重启恢复、依赖与存档保护 · 交付报告

- **run-id**：`20261002-223000Z`
- **执行范围**：仅 MR07（任务计划 §6 第 5 包）
- **依据**：`docs/market-task-plan-2026-10-03.md` §6、`market-research-task-workflow-2026-10-02.md` §7
- **最终状态**：**具备独立复验条件，等待独立验收**
- **未执行**：MR08 及其它任务包；不提交、不推送、不发布

---

## 一、结论先行

| 项 | 结果 |
|---|---|
| 会话落盘 | ✅ 新建 `BisectRecoveryStore`，记录（含基线与 SHA）写在**游戏目录之外**，原子写入（`.tmp` → 替换） |
| 中断后可见 | ✅ `GET /api/instances/{id}/bisect` 返回未完成会话与**逐项冲突**（不靠文件名猜） |
| 恢复原状态 | ✅ `resume restore` 只处理本会话开始时启用的文件；**不启用用户原本禁用的 Mod、不删除后来加入的文件** |
| 继续上一有效轮 | ✅ `resume continue` 仅在磁盘与记录逐项一致时重建会话；有冲突则 409 + `conflicts[]`，不覆盖任何文件 |
| 不允许覆盖 | ✅ 有未完成记录时 `start` 直接 409，必须先恢复/继续 |
| 依赖校验 | ✅ 禁用集合会破坏"仍然启用的 Mod 的前置"时，该轮标 **invalid**、**不落盘任何禁用**、拒绝反馈 |
| 元数据缺失 | ✅ 报 `roundValidity: "limited"` + `roundIssues[]`；仍可执行，但不假装依赖已核对完整 |
| UI | ✅ 打开二分定位面板时先查未完成会话，提供「恢复原状态 / 继续上一有效轮」（有冲突时禁用继续） |
| C# 测试 | ✅ 新增 8 个用例；实现前 **8 失败 / 0 通过** → 实现后 **8 通过 / 0 失败** |
| Node 测试 | ✅ 新增 `bisect-recovery.test.cjs` **4 通过 / 0 失败** |
| C# 全量回归 | ✅ **672 通过 / 0 失败 / 0 跳过**（664 + 8） |
| Node 全量回归 | ✅ **206 通过 / 0 失败**（13→14 个文件） |
| API 级 E2E | ✅ `VerifyHost --bisect-smoke` exit=0 |
| 真实 GUI / 真实游戏 | ⬜ **未验证** |

---

## 二、问题

改动前，会话只活在内存里：

```csharp
private static readonly ConcurrentDictionary<string, ModBisectSession> Sessions = ...;
```

进程一重启（或崩溃），这些事实就全丢了：
- 哪些文件是**本次会话**禁用的（与用户自己禁用的混在一起）；
- 它们的原始状态与原始 SHA 是什么；
- 已经推进到第几轮、候选集合是什么。

MR06 之后虽然"恢复核对通过前不移除会话"，但那只覆盖**同一进程内**；一旦进程结束，用户只能靠文件名猜——而"猜"正是计划明确禁止的。

---

## 三、实际改动

| 文件 | 状态 | 改后 SHA256 |
|---|---|---|
| `dev/PineglowLauncher/Pineglow.Launcher.Services/BisectRecoveryStore.cs` | **新增** | `136DBC3ADB5B474A0159E4B97E9B96FFA79BE7022D8CE2045EF5A0C2E181D091` |
| `dev/PineglowLauncher/Pineglow.Launcher.Services/ModBisectService.cs` | 修改（原已是 `M`） | `B8E0D7F73CABC337162FAB374ED65348BBF5FE65EAD5AFEF20CE037B14513E4E` |
| `dev/PineglowLauncher/Pineglow.Launcher.Services/LocalApiService.cs` | 修改（原已是 `M`） | `5E1ECF3462BDA3F3AF125D9D98300E591373107AEF7C3582A74B870954B2E426` |
| `dev/PineglowLauncher.Tests/BisectRecoveryTests.cs` | **新增** | `339DFCEF642CC37D8993A75B77901D335CC385AFA3C68DED0EF610739C69F936` |
| `dev/PineglowLauncher.Tests/LocalApiTestHarness.cs` | 修改（W2 新增文件） | `6B1EF3767724E42157B43A8960784AFCC4AFBEE104592F8D737DC2E3C41325C7` |
| `ui/tests/bisect-recovery.test.cjs` | **新增** | `50D9169FF38D6513E85AA4EE268D2BA4595A3F534984B702D9267DCFF47AFF66` |
| `ui/js/api.js` | 修改（原已是 `M`） | `66F8725D9FD943DAC7FDE715DE34D48F401E677B5CEE8D1207C39C178E665FEC` |
| `ui/js/launcher.js` | 修改（原已是 `M`） | `7D26FD3A99711B8A927E78340F68D4FB41FB2E7DE6FA3F14E5B0A60CBDDC3B51` |
| `ui/js/i18n.js` | 修改（原已是 `M`） | `2B5D27B408A7369338A793A95B7CFDA75AB4F1AD2D7C623729A46D1DF2D8B3A7` |
| `scripts/source-snapshot-inputs.txt` | 修改（未跟踪） | `A100F84AC01E67B82CFEB1EF3E88B4500120E05061E55AD49F383B99426A1E97` |
| `docs/api-reference.md` | 修改 | 见 `git diff` |

`git status` 由 1302 → 1305 行（+3：3 个新增文件；`ModBisectService.cs`/`LocalApiService.cs` 原本就是 `M`）。

### 3.1 `BisectRecoveryStore`：落盘契约

- 位置：`<配置目录>/bisect/<instanceId>.json`（**游戏目录之外**，游戏目录被移动/删除也不丢）。
- 内容：`SessionId`、`InstanceId`、`GameDirectory`、`Status`、`EnabledAtStart[]`、`Baseline[]`（逐文件 `FileName/Enabled/Sha256/SizeBytes`）、`Rounds[]`（`Step/Disabled/Crashed/Validity/Issues`）、`Candidates`、`Step`、`LastResults`。
- 原子写入：先写 `<id>.json.tmp` 再 `File.Move(overwrite: true)`，避免半截记录。
- 读取时校验 `InstanceId` 与请求一致，不一致即视为不可读（防张冠李戴）；JSON 损坏按"无记录"处理（有测试覆盖）。

### 3.2 API

- `GET /api/instances/{id}/bisect` —— `pending/sessionId/status/step/rounds/conflicts[]/canRestore/canContinue`。
  `conflicts` 由 `BuildBisectConflicts` 逐项比对：期望状态来自持久化的 `Rounds[].Disabled`（**不是**从文件名猜），实际状态取自磁盘的 `x.jar` / `x.jar.disabled` / 两者并存 / 都不存在。
- `POST /api/instances/{id}/bisect/resume` —— `restore` / `continue`，语义见上表；两个动作都先校验**记录里的游戏目录与当前实例目录一致**，不一致直接 409。
- `start` 在有未完成记录时 409；`giveup`/结束路径在恢复核对通过后 `ClearAsync`。

### 3.3 依赖校验（`EvaluateRoundValidity`）

- 数据来源：**jar manifest**（`ModFile.ModId` 与 `ModFile.Dependencies`，由现有 `ApplyLocalMetadata` 从 `fabric.mod.json`/`quilt.mod.json`/`mods.toml` 解析）——无需新增元数据模型。
- 规则：若某个**仍然启用**的 Mod 的必需前置，正好落在本轮禁用集合里 → `invalid`，**不落盘任何禁用**，返回 409 与逐条原因。
- 若"仍然启用"的 Mod 里存在缺 manifest 的（无法核对依赖）→ `limited` + `roundIssues`，仍照常执行。
- `invalid` 的会话拒绝测试反馈（`TryReportTest` 明确拒绝并说明原因）。

### 3.4 UI

打开二分定位面板时先 `bisectPending`；有未完成会话则渲染「上一次定位没有走完」面板：显示已应用轮次、创建时间、逐项冲突，以及「恢复原状态 / 继续上一有效轮」两个入口（`canContinue=false` 时禁用后者）。中英各新增 7 个键。

---

## 四、RED → GREEN 证据

### RED（回退到 MR06 版本的 `LocalApiService.cs` 与 `ModBisectService.cs`）

**8 失败 / 0 通过**。RED 基线额外补了一个**惰性**注入缝（`OverrideBisectStoreRootForTest` 返回空 scope）以便测试程序集编译；它不改变被测行为。

日志：`red/01-bisect-recovery-before.log`。

### GREEN（最终产物）

| 检查 | 结果 | 日志 |
|---|---|---|
| `--filter …BisectRecoveryTests` | **失败 0 / 通过 8** | `green/02-bisect-recovery-after.log` |
| `node --test ui/tests/bisect-recovery.test.cjs` | **tests 4 / pass 4 / fail 0** | `green/03-node-bisect-recovery.log` |
| `dotnet test`（全量） | **失败 0 / 通过 672 / 跳过 0**（55 s） | `green/04-csharp-full.log` |
| `node --test`（14 个 `.test.cjs`） | **tests 206 / pass 206 / fail 0** | `green/05-node-full.log` |
| `VerifyHost --bisect-smoke` | **exit 0** | `green/06-bisect-smoke.log` |

计划要求的验收点覆盖：

| 情境 | 用例 | 结果 |
|---|---|---|
| 中断在"轮次提交后" | `InterruptedAfterStart_PendingIsFound_AndContinueRebuildsSession` | ✅ 落盘记录可查、`canContinue`、继续后能再上报 |
| 中断在"恢复前" | `InterruptedBeforeRestore_ResumeRestorePutsEveryManagedFileBack` | ✅ 恢复后逐文件 SHA 与原值一致，记录被清空 |
| 恢复只碰本会话管理的文件 | `ResumeRestore_DoesNotEnableUserDisabledMods_AndDoesNotDeleteNewFiles` | ✅ 用户禁用项仍禁用且 SHA 不变；后加入文件原样保留 |
| 磁盘与记录不一致 | `Continue_WithDiskMismatch_ListsConflictsAndRefuses` | ✅ 列出冲突、拒绝继续、不覆盖 |
| 有未完成记录不得重开 | `Start_WithPendingRecord_IsRefusedUntilResolved` | ✅ 409 + `pending=true` |
| 依赖导致的新崩溃不错误收窄 | `DependencyBreak_MarksRoundInvalid_AndTouchesNoFile` | ✅ `roundInvalid`，且**一个文件都没被禁用** |
| 元数据缺失如实标注 | `MissingManifestMetadata_IsReportedAsLimited_NotInvalid` | ✅ `limited` + issues，仍照常执行 |
| 记录损坏 | `CorruptRecord_IsTreatedAsNoPendingSession` | ✅ 按"无记录"处理，不伪装成可用会话 |

---

## 五、本轮自身发现并修正的问题

1. **测试里的字符串字面量错误**：`"继续后应当回到"等待反馈"状态"` 用了 ASCII 双引号，编译失败（CS1003）。改用「」。
2. **`openModBisect` 不是 async 却用了 `await`**：`node --check` 直接报语法错误。已改为 `async function`。
3. **`LocalApiService.cs` 的 BOM**：本会话前该文件带 BOM，`edit` 工具会剥掉；每轮结束都用会话前的 MR00 快照做字节真值归一（本轮检查结果为"无需修正"）。

---

## 六、明确未做与未验证

| 项 | 说明 |
|---|---|
| **"临时测试世界 / 实例副本"的引导未实现** | 计划要求"UI 要求在临时测试世界/实例副本运行"并给出可校验的存档备份/副本指引。本包**没有**做这部分 UI/文案，也没有触碰存档服务。 |
| 真实进程中断 | 测试用 `ModBisectService.Remove` **模拟**"进程重启"（内存态清空、落盘保留），**不是**真的杀进程再启动。真的杀进程/重启应用未验证。 |
| 真实 Minecraft 测试副本 | 未启动游戏；"原存档 SHA 前后相同"没有实测（本包也没有触碰存档路径）。 |
| 世界安全 | 计划提到"不自动进入真实世界、不承诺通用禁用世界保存"——本包未实现任何世界相关逻辑，因此也谈不上验证。 |
| UI 真实呈现 | 未启动应用；恢复选择面板与两个按钮的实际渲染/点击链路未验证。 |
| 启动时自动扫描 | 计划提到"重启检测未完成会话"。当前是**打开定位面板时**查询（按需），没有在应用启动时主动扫描所有实例。 |
| MR08 | 未开始。 |

---

## 七、回退方法

```powershell
$run = 'C:\Users\Administrator\.codex\worktrees\ed41\松光启动器\docs\market-execution\MR07-bisect-restart-recovery\20261002-223000Z'
Copy-Item "$run\green\LocalApiService.cs.mr07" `
  'D:\松光启动器\dev\PineglowLauncher\Pineglow.Launcher.Services\LocalApiService.cs' -Force
Copy-Item "$run\green\ModBisectService.cs.mr07" `
  'D:\松光启动器\dev\PineglowLauncher\Pineglow.Launcher.Services\ModBisectService.cs' -Force
Get-Item 'D:\松光启动器\dev\PineglowLauncher\Pineglow.Launcher.Services\LocalApiService.cs',
         'D:\松光启动器\dev\PineglowLauncher\Pineglow.Launcher.Services\ModBisectService.cs' | ForEach-Object { $_.LastWriteTime = Get-Date }
```

其余：删除新增的 3 个文件；`LocalApiTestHarness.cs` 去掉 bisect 存储注入；`ui/js/*` 按 §三.4 反向删除；`api-reference.md` 与白名单反向删除。
**注意**：还原后必须 `LastWriteTime = Get-Date`；`LocalApiService.cs` 会话前带 BOM。

---

## 八、证据清单

```
red/01-bisect-recovery-before.log        回退后 8 失败 / 0 通过
green/
  LocalApiService.cs.mr07                 MR07 实现版备份（SHA 4A3885D0…）
  ModBisectService.cs.mr07                MR07 实现版备份（SHA B8E0D7F7…）
  *.sha256.txt
  01-bisect-recovery-first.log            首次运行（1 个字符串字面量编译错误）
  02-bisect-recovery-after.log            8 通过 / 0 失败
  03-node-bisect-recovery.log             4 通过 / 0 失败
  04-csharp-full.log                      672 通过 / 0 失败
  05-node-full.log                        206 通过 / 0 失败（14 个文件）
  06-bisect-smoke.log                     exit 0
  git-status-after.txt                    1305 行
  changed-files.sha256.txt
manifest.json
```

**本报告不宣布阶段放行、不提交、不发布。** 至此 MG1（MR03–MR07）五包全部"证据就绪"，可整体提交独立复验。
