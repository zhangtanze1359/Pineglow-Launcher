# MR04 · Mod 删除改为可恢复隔离 · 交付报告

- **run-id**：`20261002-193000Z`
- **执行范围**：仅 MR04（任务计划 §6 第 2 包）
- **依据**：`docs/market-task-plan-2026-10-03.md` §6、`market-research-task-workflow-2026-10-02.md` §7
- **最终状态**：**具备独立复验条件，等待独立验收**
- **未执行**：MR05–MR07 及其它任务包；不提交、不推送、不发布

---

## 一、结论先行

| 项 | 结果 |
|---|---|
| 可恢复删除 | ✅ `mods/{file}/delete` 不再 `File.Delete`：jar 与 `.mcmini.json` 侧车作为同一次操作移入恢复区 |
| 恢复 | ✅ `POST /api/instances/{id}/recovery/{op}/restore` 逐字节还原；重复恢复幂等；冲突默认不覆盖 |
| 恢复区列表 | ✅ `GET /api/instances/{id}/recovery` 返回逐文件状态、SHA、体积与 30 天保留期 |
| 安全边界 | ✅ 越界路径、符号链接/重解析点拒绝；先写记录再动文件；失败如实报错不假装成功 |
| 跨卷 | ✅ 用例在 C:/D: 两个真实卷之间执行，走「复制 → SHA256 校验 → 删除源」回退并通过 |
| UI | ✅ Mod 行新增可恢复删除入口 + 实例菜单新增「恢复区」面板；`ui/css/figma.css` 补样式 |
| C# 测试 | ✅ 新增 11 个用例；实现前 **6 失败 / 4 通过** → 实现后 **11 通过 / 0 失败** |
| Node 测试 | ✅ 新增 7 个用例 → **7 通过 / 0 失败** |
| C# 全量回归 | ✅ **646 通过 / 0 失败 / 0 跳过**（635 + 11） |
| Node 全量回归 | ✅ **198 通过 / 0 失败**（12→13 个文件） |
| API 级 E2E | ✅ `VerifyHost --bisect-smoke` exit=0（MR04 未触及 Bisect，作为回归确认） |
| 真实 GUI / WebView2 | ⬜ **未验证**（本批未启动应用） |

---

## 二、问题

`HandleDeleteModAsync` → `ModService.DeleteMod` 用的是不可恢复的 `File.Delete`：

```csharp
File.Delete(mod.FullPath);
string metadataPath = GetMetadataPath(mod.FullPath);
if (File.Exists(metadataPath)) File.Delete(metadataPath);
```

一次误点就让 Mod 永久消失。再加上 MR04 侦察时发现的另一件事：**UI 根本没有 Mod 删除入口**（`openModManager` 只有 toggle，`mod.delete*` 键不存在），也就是说删除能力只存在于 API，用户界面上既没有入口也没有恢复路径。

---

## 三、实际改动

| 文件 | 状态 | 改后 SHA256 |
|---|---|---|
| `dev/PineglowLauncher/Pineglow.Launcher.Models/FileRecoveryRecord.cs` | **新增** | `5469E48EF55017C79F1EF4F212C501FABC6A767DD9D7D2C3A41962562E591AF1` |
| `dev/PineglowLauncher/Pineglow.Launcher.Services/FileRecoveryService.cs` | **新增** | `6926200706E0CD97DDCFC2CC7C577310B82E66309E65547F159E987FEA3ACDD3` |
| `dev/PineglowLauncher/Pineglow.Launcher.Services/LocalApiService.cs` | 修改（原已是 `M`） | `ED2C96B9B21156CDCB95B32024053A863270E9C9AE402AA51A7B7E3459DB39A7` |
| `dev/PineglowLauncher/Pineglow.Launcher.Services/ModService.cs` | 修改 | `7464A8CF83EEE3D261C323A9985FCF19EF168E441476729A8A7C6314EE94B13F` |
| `dev/PineglowLauncher.Tests/LocalApiTestHarness.cs` | 修改（W2 新增文件） | `55E585459E64458047A324664008048E7DF0E2D656F578A2D11F5A1EFBD597B6` |
| `dev/PineglowLauncher.Tests/FileRecoveryTests.cs` | **新增** | `1B6C65419F0FBF389191F821A61FC673D3620EFC0812F204C394E3F2B2FDDAF0` |
| `ui/tests/file-recovery.test.cjs` | **新增** | `79512F2E1A81F10CCE338100E733FD9D3CB19180247F3826F459688ECCD9A1E4` |
| `ui/js/api.js` | 修改（原已是 `M`） | `F7A073304EAD859C99C1D9BAC85D97B15ED5C554B9A0BE68968E917D2BF88E0A` |
| `ui/js/i18n.js` | 修改（原已是 `M`） | `7A90707C6795E754074E354980549646520416D9551A178B9B3DAC19B08F64A4` |
| `ui/js/launcher.js` | 修改（原已是 `M`） | `7477D9BDFD5F617C9ABD0A749379325D92E74EAE82AF23E7A87E488356EB9D7F` |
| `ui/css/figma.css` | 修改（原已是 `M`） | `E80A9FEF5FCA718AC42AC95A56ABC13C86F1C90C6FE0692C8E72E2C4F115EC9C` |
| `scripts/source-snapshot-inputs.txt` | 修改（未跟踪） | `3A5A638A543CA18619FBF950DA457466FEE87BBEE10EB1A0C8E60B81A03C36BE` |
| `docs/api-reference.md` | 修改 | `43FFEED201FFA5ADFC4593E1F99C00CD13CB78E3F9F2C84413E681AB7FEAEBEE` |

`git status` 由 1292 → 1297 行（+5：4 个新文件 + `ModService.cs` 新变为 `M`）。

### 3.1 `FileRecoveryService`：冻结的契约

- 恢复根在**实例之外**（生产为 `<配置目录>/recovery`），游戏目录被移动或删除也不会丢隔离副本。
- `QuarantineAsync(instanceId, gameDirectory, reason, paths)` → `FileRecoveryRecord`
- `RestoreAsync(record, overwrite=false)` → `FileRecoveryRecord`（**逐文件** `State`/`Error`，不是单一布尔）
- `ListAsync(instanceId)` / `LoadAsync(instanceId, op)`；`GetExpiresAt()` = 创建时间 + **30 天**（只用于提示，不自动清理）
- 隔离：先写 `record.json`（`status=quarantining`）→ 逐文件 `File.Move`；抛 `IOException`/`UnauthorizedAccessException` 时退化为「复制 → SHA256 校验 → 删除源」，校验不过则**删掉半成品并保留源**。
- 恢复：目标已存在且 SHA 相同 → `already_restored`（幂等）；SHA 不同且未开 `overwrite` → `conflict`，**不写一个字节**；副本不存在 → `failed` 且带原因。
- 拒绝：`IsPathInside` 之外的路径、文件自身或其祖先目录带 `ReparsePoint` 属性。
- 记录写入用 `record.json.tmp` → `File.Move(overwrite)` 的原子替换。

### 3.2 `LocalApiService`

- `HandleDeleteModAsync`：收集 jar + 存在的侧车 → `QuarantineAsync(reason: "mod-delete")`；**有任何文件 `failed` 或一个都没隔离成功时抛 400**，不把"没删掉"报成成功；响应带 `operationId/recoverable/files[]`。
- 新增 `GET /api/instances/{id}/recovery`（在长度 4 实例块内）与 `POST /api/instances/{id}/recovery/{operationId}/restore`（在**块外**的长度 6 前置分派里，避免重演 W1 的嵌套缺陷——W2 的静态守卫用例会强制这一点）。
- 新增 `OverrideRecoveryRootForTest`，测试绝不触碰真实 `%APPDATA%\…\recovery`。

### 3.3 `ModService`

`DeleteMod` 保留为不可恢复的基础原语，但加上明确文档注释：**用户可见的删除必须走 `FileRecoveryService`**。

### 3.4 UI

- Mod 行右侧新增 `mod-del` 按钮：确认框说明"移入恢复区、可随时还原、不会真正删除"，取消时不发请求；成功后重新渲染 Mod 列表。
- 实例菜单新增「恢复区」入口 → `openRecoveryPanel(inst)`：列出每次操作的时间、文件数、体积、逐文件状态与错误；「恢复」按钮调用 `restoreRecovery`，冲突时提示"未覆盖"。
- i18n 中英各新增 21 个键；`figma.css` 新增 `.mod-del` 与 `.rec-*` 样式。

---

## 四、RED → GREEN 证据

### RED（把删除处理器与恢复路由临时回退到 MR03 行为后运行新测试）

**6 失败 / 4 通过**。失败的正是本包的新契约：quarantine 式删除、恢复区列表、恢复、幂等、冲突、未知操作 400。通过的 4 条是服务层单元测试（`IsPathInside`、越界拒绝、部分失败记录）与「不误删其它文件」——后者在物理删除下同样成立，属既有正确行为，不计为本包成果。

日志：`red/01-file-recovery-before.log`。

### GREEN（最终产物）

| 检查 | 结果 | 日志 |
|---|---|---|
| `--filter …FileRecoveryTests` | **失败 0 / 通过 11** | `green/02-file-recovery-after.log` |
| `node --test ui/tests/file-recovery.test.cjs` | **tests 7 / pass 7 / fail 0** | `green/03-node-file-recovery.log` |
| `dotnet test`（全量） | **失败 0 / 通过 646 / 跳过 0**（55 s） | `green/04-csharp-full.log` |
| `node --test`（13 个 `.test.cjs`） | **tests 198 / pass 198 / fail 0** | `green/05-node-full.log` |
| `VerifyHost --bisect-smoke` | **exit 0** | `green/06-bisect-smoke.log` |

C# 覆盖的六类情境（计划 §7 要求）：

| 情境 | 用例 | 结果 |
|---|---|---|
| 启用态文件 | `DeleteMod_QuarantinesJarAndSidecar_KeepsByteIdenticalCopies` | ✅ 原位置消失、恢复区同 SHA |
| 禁用态文件（`.jar.disabled`） | `DeleteMod_DisabledMod_QuarantinesDotDisabledName` | ✅ |
| 侧车 `.mcmini.json` | 同上第一条（同一次操作，逐文件状态） | ✅ |
| 同名冲突 | `Restore_WithDifferentFileAtOriginalPath_ReportsConflictAndDoesNotOverwrite` | ✅ 新文件未被覆盖、隔离副本仍保留 |
| 跨卷失败 | `Quarantine_AcrossVolumes_CopiesVerifiesThenRemovesSource` | ✅ **真实 C:↔D: 跨卷**，复制→校验→删源，恢复也跨卷 |
| 中断/部分失败 | `Quarantine_PartialFailure_RecordsMissingFileWithoutClaimingSuccess` | ✅ 记录先落盘、逐项状态、可从磁盘读回 |
| 附加：重复恢复幂等 | `Restore_Twice_IsIdempotent_AndKeepsFileContent` | ✅ `already_restored`，内容不变 |
| 附加：未知文件不被移除 | `DeleteMod_LeavesEveryOtherFileUntouched` | ✅ 只消失 2 个目标文件，其余 SHA 全等 |
| 附加：越界与路径包含 | `Quarantine_RejectsPathOutsideGameDirectory`、`IsPathInside_RejectsSiblingPrefix_AndTheRootItself` | ✅ 含同前缀兄弟目录与 `..` 上跳 |

---

## 五、本轮自身发现并修正的三个问题

1. **`RestoreOne` 判定顺序错误（真实缺陷）**：先检查"隔离副本是否存在"，导致第二次恢复被误报为 `failed`（副本已在上次恢复时移走）。GREEN 首跑即暴露（9/10）。已改为**先看原位置**：原文件已就位且 SHA 相同即 `already_restored`（幂等），再检查副本。
2. **测试编译错误**：`Assert.Equal(30, x, "消息")` 被解析到错误的 xUnit 重载（期望 `Func<int,int,bool>`）。改为 `Assert.True(x == 30, "消息")`。
3. **BOM 误判与纠正（值得记录的过程问题）**：`edit` 工具会用 UTF-8 无 BOM 写回，剥掉文件原有的 BOM。我用一个自写的 `git diff` 检测器判断哪些文件"丢了 BOM"，该检测器有误——它把 `+` 行误判成 `-` 行，导致我给 **5 个本来没有 BOM 的文件**（`api.js`、`i18n.js`、`launcher.js`、`figma.css`、`api-reference.md`）加上了 BOM。
   最终用**会话前的 MR00 隔离快照（`%TEMP%\pineglow-snapshot-810a28bd…`）做字节级真值**逐文件比对，才得出正确结论：这 5 个文件会话前无 BOM（HEAD 也无），而 `LocalApiService.cs` 会话前**有** BOM。现已全部归一，并逐文件复核"与快照 BOM 状态一致"。
   **教训**：二进制/编码状态不要靠 diff 推断，要用会话前的字节副本比对；MR00 快照这次成了关键真值来源。

---

## 六、明确未做与未验证

| 项 | 说明 |
|---|---|
| 真实 GUI / WebView2 | 未启动应用；删除按钮、恢复区面板的实际渲染与点击链路均为未验证 |
| 符号链接/重解析点拒绝 | 代码已实现（文件自身与祖先目录都检查），但**没有测试覆盖**——在 Windows 上创建符号链接需要特权或开发者模式，本机未验证 |
| 保留期到期行为 | 只实现"提示"（`expiresAt` + `retentionDays`），**没有任何自动清理**；UI 也未展示过期提示（计划允许，永久清理需单独授权） |
| 磁盘不足 | `CopyThenRemove` 会捕获异常并保留源，但没有构造"磁盘满"的真实用例 |
| 文件被占用 | 同上：会走 `IOException` 回退并逐项报错，但没有真实占用用例 |
| MR05–MR07 | 未开始 |

---

## 七、回退方法

`LocalApiService.cs` / `ModService.cs` / `ui/js/*` / `figma.css` / `api-reference.md` 都是未提交的 `M` 文件，**禁止 `git checkout/restore`**。

```powershell
$run = 'C:\Users\Administrator\.codex\worktrees\ed41\松光启动器\docs\market-execution\MR04-recoverable-mod-delete\20261002-193000Z'
# MR04 实现版的 LocalApiService.cs（如需再次回到本包结果）
Copy-Item "$run\green\LocalApiService.cs.mr04" `
  'D:\松光启动器\dev\PineglowLauncher\Pineglow.Launcher.Services\LocalApiService.cs' -Force
(Get-Item 'D:\松光启动器\dev\PineglowLauncher\Pineglow.Launcher.Services\LocalApiService.cs').LastWriteTime = Get-Date
```

其余文件按 §三 清单反向删除：新增的 4 个文件直接删除；`ModService.cs` 去掉 `DeleteMod` 的文档注释；`LocalApiTestHarness.cs` 去掉恢复根注入；`ui/js/{api,i18n,launcher}.js` 与 `figma.css` 按本包新增块反向删除。
**注意**：还原后必须 `LastWriteTime = Get-Date`，否则 MSBuild 会因时间戳未变而跳过重编译（本轮踩过）。

---

## 八、证据清单

```
red/
  01-file-recovery-before.log           实现前 6 失败 / 4 通过
green/
  LocalApiService.cs.mr04               MR04 实现版备份（SHA BC328E5E…，不含 BOM）
  LocalApiService.cs.mr04.sha256.txt
  02-file-recovery-after.log            11 通过 / 0 失败
  03-node-file-recovery.log             7 通过 / 0 失败
  04-csharp-full.log                    646 通过 / 0 失败
  05-node-full.log                      198 通过 / 0 失败（13 个文件）
  06-bisect-smoke.log                   exit 0
  git-status-after.txt                  1297 行
  changed-files.sha256.txt
manifest.json
```

**本报告不宣布阶段放行、不提交、不发布。** 下一包建议 `MR05`（Mod 替换/更新的可撤销事务），它直接复用本包的 `FileRecoveryService` 与"逐文件 SHA 可核对"的验收方式。
