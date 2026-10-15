# MR05 · Mod 替换/更新的可撤销事务 · 交付报告

- **run-id**：`20261002-201500Z`
- **执行范围**：仅 MR05（任务计划 §6 第 3 包）
- **依据**：`docs/market-task-plan-2026-10-03.md` §6、`market-research-task-workflow-2026-10-02.md` §7
- **最终状态**：**具备独立复验条件，等待独立验收**
- **未执行**：MR06–MR07 及其它任务包；不提交、不推送、不发布

---

## 一、结论先行

| 项 | 结果 |
|---|---|
| 更新语义 | ✅ 同名在线项目**原地替换**，不再生成 `xxx (1).jar` 并列副本 |
| 事务顺序 | ✅ 新包 SHA256 校验 → 隔离旧包 → 写新包 → 写元数据；**校验通过前不碰旧文件** |
| 回滚 | ✅ 提交失败、元数据失败都回滚到旧版本，旧 jar 与侧车 SHA 逐字节一致 |
| 保持启用状态 | ✅ 旧的是 `.jar.disabled` 时，新版本仍落在 `.jar.disabled` |
| 撤销 | ✅ 替换返回 `operationId`；`restore` 传 `overwrite=true` 覆盖回旧版本；默认仍不覆盖 |
| 并发 | ✅ 同一目标并发替换被拒绝（`replace_in_progress`），不是排队 |
| UI | ✅ 恢复区把 `mod-replace` 记录显示为「撤销本次更新」，点击走 `overwrite=true` |
| C# 测试 | ✅ 新增 9 个用例；实现前 **6 失败 / 3 通过** → 实现后 **9 通过 / 0 失败** |
| Node 测试 | ✅ `file-recovery.test.cjs` 由 7 增至 **11 通过 / 0 失败** |
| C# 全量回归 | ✅ **655 通过 / 0 失败 / 0 跳过**（646 + 9） |
| Node 全量回归 | ✅ **202 通过 / 0 失败**（13 个文件） |
| API 级 E2E | ✅ `VerifyHost --bisect-smoke` exit=0 |
| 真实 GUI / 真实下载 | ⬜ **未验证**（见 §六，其中"更新接线"无自动化覆盖） |

---

## 二、问题（侦察结论）

`ModContentService.DownloadAndInstallAsync` 的落地方式是：

```csharp
string text3 = modService.ImportMod(instance, tempPath);   // → GetAvailableTargetPath
```

而 `GetAvailableTargetPath` **从不覆盖**：同名文件已存在时依次尝试 `foo (1).jar`、`foo (2).jar`……

```csharp
for (int i = 1; i < 1000; i++) { text = Path.Combine(directory, $"{base} ({i}){ext}"); if (!File.Exists(text)) return text; }
```

加上 `InstallWithDependenciesAsync` 对主项目强制 `isMain: true`，**更新一个已安装的 Mod 会在旧版本旁边再放一个新版本**：旧版本继续被游戏加载、两个 jar 冲突、用户既没有"替换"也没有"撤销"。这正是计划里"不能将旧文件永久删除作为默认更新策略"要解决的相邻问题——现状比"永久删除"更糟：它连替换都没做。

---

## 三、实际改动

| 文件 | 状态 | 改后 SHA256 |
|---|---|---|
| `dev/PineglowLauncher/Pineglow.Launcher.Services/ModReplacementService.cs` | **新增** | `2A5D9B8C6F3A16C451B67477441E0845991BDC6EE210B619FCCB5674ADC1F784` |
| `dev/PineglowLauncher/Pineglow.Launcher.Services/ModContentService.cs` | 修改 | `3BFA302A117A2B55CEE6E77B448EFFC890A4A1C338AB2675899320A658C000B7` |
| `dev/PineglowLauncher/Pineglow.Launcher.Services/LocalApiService.cs` | 修改（原已是 `M`） | `6DEF1F1437E35C85BDA742E51590CB259427D6573666999F1C582128BAA9CD2C` |
| `dev/PineglowLauncher.Tests/ModReplacementTests.cs` | **新增** | `B40AF8191C933B4A9F520C115D9F3A0669937CC96ED07B52DB0435EFC95A76E9` |
| `ui/tests/file-recovery.test.cjs` | 修改（MR04 新增文件） | `50B4F8A7B3C4571D58C2ECCC932DD9D0D21131CB2282144513E57A6A4E4D6FBD` |
| `ui/js/api.js` | 修改（原已是 `M`） | `2BFDCCED028E993E5C7FB8DB601C6FA75143D39CC9CF765FCDDABFA0D9926B0D` |
| `ui/js/i18n.js` | 修改（原已是 `M`） | `A8BD2C2B80D762B0B1B6C8BC1525BCF00C92373321ECE7D505041A0D6B44F80F` |
| `ui/js/launcher.js` | 修改（原已是 `M`） | `1695032D711E6B2800D5AB1A330ECA7F2618CF47078CCD8F5E5526D44AB06DF2` |
| `scripts/source-snapshot-inputs.txt` | 修改（未跟踪） | `A14078DB4F9963200AB22E77D20FB200E87A7B14A710FDE402C37DC1E4043FBA` |
| `docs/api-reference.md` | 修改 | `51F07E94D001A431C6EE7978E1065BCDC127CC0B81B83B5FC5CCBEEA322A376C` |

`git status` 由 1297 → 1300 行（+3：2 个新文件 + `ModContentService.cs` 新变为 `M`）。

### 3.1 `ModReplacementService`：冻结的事务契约

```csharp
Task<ModReplacementResult> ReplaceAsync(GameInstance instance, ModService modService,
    string targetFileName, string stagedFilePath, string? expectedSha256 = null, ModVersion? version = null)
```

固定顺序与失败语义：

| 阶段 | 失败状态 | 旧文件是否受影响 |
|---|---|---|
| 路径越界 | `target_missing` | 否 |
| 同一目标已在替换中 | `replace_in_progress` | 否 |
| staged 不存在 | `staged_missing` | 否 |
| SHA256 与期望不符 | `hash_mismatch` | **否（校验通过前不碰旧文件）** |
| 目标不存在 | `target_missing` | 否 |
| 隔离旧包失败 | `quarantine_failed` | 尝试回滚已隔离的部分 |
| 写入新包失败 | `commit_failed` | **回滚** |
| 写元数据失败 | `metadata_failed` | **回滚** |

- 隔离复用 MR04 的 `FileRecoveryService`（reason = `mod-replace`），返回的 `operationId` 就是撤销句柄。
- 新文件先写 `<target>`（旧文件已被移走，所以是空位），写完立刻核对 SHA256。
- 回滚 = 删掉新文件 + `RestoreAsync(record, overwrite: true)` 把旧 jar 与侧车按原 SHA 放回。
- 并发用进程级 `ConcurrentDictionary` + `TryAdd`：**拒绝**而非排队。

### 3.2 接口

- `POST /api/instances/{id}/mods/{file}/replace`，body `{ newFileBase64, expectedSha256? }`；服务端把上传落到 `%TEMP%/pineglow-replace-<guid>/staged.jar`，`finally` 里清理。
- `POST /api/instances/{id}/recovery/{op}/restore` 现在读取可选 body `{ "overwrite": bool }`；**空 body / 非法 JSON 一律按不覆盖处理**（解析失败不放行覆盖）。

### 3.3 真实更新链路

`ModContentService` 新增可选构造参数 `FileRecoveryService?`：下载完成并 SHA1 校验后，若已安装的 Mod 中存在 `OnlineSource:ProjectId` 相同的项目，就走 `ModReplacementService.ReplaceAsync`（保留启用状态、旧版本进恢复区可撤销）；否则维持原来的 `ImportMod`。`LocalApiService` 传入 `CreateRecoveryService()`。

### 3.4 UI

恢复区面板区分 `reason === 'mod-replace'` 的记录：显示「Mod 更新」标签与「撤销本次更新」按钮，点击时 `restoreRecovery(inst.id, op, true)`；其它记录仍是默认不覆盖的「恢复」。中英各新增 3 个键。

---

## 四、RED → GREEN 证据

### RED（移除 replace 路由、并让 restore 忽略 overwrite 后运行新测试）

**6 失败 / 3 通过**。失败的 6 条全部是 HTTP 面用例（禁用态替换、启用态替换、hash 不符、非法请求、撤销、不影响其它文件）；通过的 3 条是服务层事务用例（提交回滚、元数据回滚、并发原语）——RED 态下 `ModReplacementService` 仍然存在，所以它们通过，如实记录。

日志：`red/01-mod-replacement-before.log`。

### GREEN（最终产物）

| 检查 | 结果 | 日志 |
|---|---|---|
| `--filter …ModReplacementTests` | **失败 0 / 通过 9** | `green/02-mod-replacement-after.log` |
| `node --test ui/tests/file-recovery.test.cjs` | **tests 11 / pass 11 / fail 0** | `green/03-node-file-recovery.log` |
| `dotnet test`（全量） | **失败 0 / 通过 655 / 跳过 0**（56 s） | `green/04-csharp-full.log` |
| `node --test`（13 个 `.test.cjs`） | **tests 202 / pass 202 / fail 0** | `green/05-node-full.log` |
| `VerifyHost --bisect-smoke` | **exit 0** | `green/06-bisect-smoke.log` |

计划要求的情境覆盖：

| 情境 | 用例 | 结果 |
|---|---|---|
| hash 不符 | `Replace_WithMismatchedHash_LeavesOldFileByteIdentical` | ✅ 旧文件 SHA 不变，连恢复记录都不产生 |
| 暂存/目标缺失、空 body、非法 base64 | `Replace_InvalidRequests_Return400WithoutTouchingAnything` | ✅ 400 且旧文件不变 |
| 提交中断 | `Replace_WhenCommitFails_RollsBackOldJarAndSidecar` | ✅ 回滚，旧 jar/侧车 SHA 一致 |
| 侧车失败 | `Replace_WhenMetadataWriteFails_RollsBackOldJarAndSidecar` | ✅ 回滚，旧 jar/侧车 SHA 一致 |
| 禁用 Mod 更新后仍禁用 | `Replace_DisabledMod_StaysDisabled_AndOldVersionIsQuarantined` | ✅ 且无 `(1)` 副本 |
| 启用态保持启用 | `Replace_EnabledMod_StaysEnabled` | ✅ |
| 重复/并发替换同一目标 | `Replace_ConcurrentGuard_RejectsSecondAttemptOnSameTarget` | ✅ 第二次被拒，释放后可再取 |
| 撤销（覆盖回旧版本） | `Undo_AfterReplace_RestoresOldVersionAtOriginalPath` | ✅ 默认报 conflict 不动新文件；`overwrite=true` 后 SHA 回到旧值 |
| 不误伤其它文件 | `Replace_LeavesOtherModsUntouched` | ✅ 其它 jar 与侧车 SHA 不变 |

---

## 五、本轮自身发现并修正的问题

1. **故障注入方式不成立**：第一版测试用"把侧车路径占成目录"制造写元数据失败，但那样侧车根本没被隔离，断言还去对目录求 SHA。改为在本仓既有的"仅测试注入缝"风格上加 `BeforeCommitForTest` / `BeforeMetadataWriteForTest`（`internal static Action<string>?`），精确覆盖**提交**与**元数据**两个回滚分支。
2. **缺 `using System.Linq;`** 导致 `List<T>.Any/All` 编译失败，构建即报错并修正。
3. **静态扫描窗口太窄**：MR04 的 Node 用例用 `slice(start, start + 2600)`，本包新增代码把 `SG.api.restoreRecovery(` 挤出窗口导致误报失败。已把两个窗口统一放宽到 4200 字符（并保留了原有的语义断言）。

---

## 六、明确未做与未验证

| 项 | 说明 |
|---|---|
| **`ModContentService` 的更新接线没有自动化测试** | 它依赖真实下载（网络 + 外部 CDN），本包没有为它造 fake HTTP 通道。这一段只有代码走查：**"已安装同一在线项目时走替换"这一条在测试里未被执行过**。这是本包最大的证据缺口。 |
| 真实 GUI / 真实安装 UI | 未启动应用；"安装更新 → 在恢复区撤销"这条真实 UI 链路未验证（计划原文要求"按真实安装 UI 再走一次更新→撤销"） |
| 并发 | 只验证了并发原语（`TryBeginReplace`/`EndReplace`），没有构造两个真正并行的 HTTP 请求 |
| 磁盘不足 / 文件被占用 | 回滚分支由注入缝覆盖，但没有构造真实的磁盘满或文件占用 |
| 侧车的"版本/变更"展示 | 恢复区展示了原因、文件与错误；**没有**做"新旧版本号对比"的界面 |
| MR06–MR07 | 未开始 |

---

## 七、回退方法

```powershell
$run = 'C:\Users\Administrator\.codex\worktrees\ed41\松光启动器\docs\market-execution\MR05-mod-replacement-transaction\20261002-201500Z'
Copy-Item "$run\green\LocalApiService.cs.mr05" `
  'D:\松光启动器\dev\PineglowLauncher\Pineglow.Launcher.Services\LocalApiService.cs' -Force
(Get-Item 'D:\松光启动器\dev\PineglowLauncher\Pineglow.Launcher.Services\LocalApiService.cs').LastWriteTime = Get-Date
```

其余：删除新增的 2 个文件（`ModReplacementService.cs`、`ModReplacementTests.cs`）；`ModContentService.cs` 去掉恢复服务参数与替换分支；`ui/js/*`、`figma.css`、`api-reference.md`、白名单按 §三 反向删除。
**注意**：还原后必须 `LastWriteTime = Get-Date`，否则 MSBuild 因时间戳未变跳过重编译（本轮与 MR04 都踩过）。

---

## 八、证据清单

```
red/01-mod-replacement-before.log        实现前 6 失败 / 3 通过
green/
  LocalApiService.cs.mr05                MR05 实现版备份（SHA 35CFAF7C…）
  LocalApiService.cs.mr05.sha256.txt
  01-mod-replacement-first.log           首次运行（1 个用例的故障注入方式错误）
  02-mod-replacement-after.log           9 通过 / 0 失败
  03-node-file-recovery.log              11 通过 / 0 失败
  04-csharp-full.log                     655 通过 / 0 失败
  05-node-full.log                       202 通过 / 0 失败（13 个文件）
  06-bisect-smoke.log                    exit 0
  git-status-after.txt                   1300 行
  changed-files.sha256.txt
manifest.json
```

**本报告不宣布阶段放行、不提交、不发布。** 下一包建议 `MR06`（Bisect 文件操作失败必须阻止错误推进）——它依赖 MR04 建立的文件操作逐项结果，并且是本路线图里另一个 P0 阻断项。
