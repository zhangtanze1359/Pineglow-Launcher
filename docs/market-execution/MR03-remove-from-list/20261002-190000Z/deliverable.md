# MR03 · 实例"从列表移除"的准确语义 · 交付报告

- **run-id**：`20261002-190000Z`
- **执行范围**：仅 MR03（任务计划 §6 第 1 包）
- **依据**：`docs/market-task-plan-2026-10-03.md` §6、`market-research-task-workflow-2026-10-02.md` §7
- **最终状态**：**具备独立复验条件，等待独立验收**
- **未执行**：MR04–MR07 及其他任何任务包；不提交、不推送、不发布

---

## 一、结论先行

| 项 | 结果 |
|---|---|
| 语义纠正 | ✅ UI 由"删除实例 / 此操作不可撤销"改为"从列表移除"，确认框列出游戏目录与存档数量并说明文件保留 |
| 后端契约 | ✅ 移除只改配置；响应新增 `keptFiles / gameDirectory / savesCount / sharedDirectory` |
| 活动状态保护 | ✅ 正在 Mod 二分定位的实例被拒绝移除（400）；**服务端不存在"运行中"状态，见 §六** |
| 重新挂回 | ✅ 新增 `POST /api/instances/register`，把已存在目录挂回列表，零文件写入 |
| C# 测试 | ✅ 新增 7 个用例；实现前 **6 失败 / 1 通过** → 实现后 **7 通过 / 0 失败** |
| Node 测试 | ✅ 新增 7 个用例 → **7 通过 / 0 失败** |
| C# 全量回归 | ✅ **635 通过 / 0 失败 / 0 跳过**（628 + 7） |
| Node 全量回归 | ✅ **191 通过 / 0 失败**（11→12 个文件，184 + 7） |
| 真实 GUI / WebView2 确认框呈现 | ⬜ **未验证**（本批未启动应用） |

---

## 二、问题（为什么这是 P0）

`LocalApiService.HandleDeleteInstanceAsync` 原本只从 `config.Instances` 移除记录，**从不删除游戏目录、存档与 Mod**：

```csharp
config.Instances.RemoveAll(x => x.Id == instanceId);
...
await WriteJsonAsync(response, 200, new { ok = true });
```

而 UI 同时写着：

```js
'inst.delete': '删除实例',
'inst.deleteConfirm': '删除实例「{name}」？此操作不可撤销。',
'inst.deleted': '实例已删除',
```

用户会合理地相信存档已被删除。这违反工作流 §7 的硬要求——"不能在 UI 暗示已删除文件"。同时"重新添加"入口完全缺失：一旦移除，用户没有任何界面路径把目录挂回来。

---

## 三、实际改动

| 文件 | 状态 | 改后 SHA256 | 说明 |
|---|---|---|---|
| `dev/PineglowLauncher/Pineglow.Launcher.Services/LocalApiService.cs` | 修改（原已是 `M`） | `C080A6F633970D4BA45642AC8257865EB66DB64806B846C7EAD0F7D4A5220ABA` | 移除语义 + Bisect 守卫 + register 路由 + `sharedGameDirectory` |
| `dev/PineglowLauncher.Tests/InstanceRemovalTests.cs` | **新增** | `FD0FD1738F1C11ECFBCABB80BB1BFC1FF7856CBBC9B6A17548F0832BF06792E9` | 7 个 C# 用例 |
| `ui/tests/instance-removal.test.cjs` | **新增** | `2F0AE7ECB477176A64E22263239F25CBF31A45F391487494975D89AF86621556` | 7 个 Node 用例 |
| `ui/js/i18n.js` | 修改（原已是 `M`） | `52F734FEF8F15B093213488FD52D0C9ACCD80F392BB62D676893C62298E8AA44` | 中英各 15 个新键 + 4 个键改写 |
| `ui/js/api.js` | 修改（原已是 `M`） | `85188920C7B8F729D6EE67A2E3D1A501F8ADF8EAC51A2A7CEE363369A8919319` | live + mock 各加 `registerInstance` |
| `ui/js/launcher.js` | 修改（原已是 `M`） | `45841F8C02435EBC47A5709A72110EF90175A8A973882724F3B23376DE4AF54F` | 确认文案 + "从已有目录添加"入口 |
| `scripts/source-snapshot-inputs.txt` | 修改（未跟踪） | `3031E71CAE1E5BB86BA4EF2A9F653A2542C2D68DE13D1FC2E25420DFDA139D88` | 类别 B 18→19、类别 F 11→12 |
| `docs/api-reference.md` | 修改（W3 起已为 `M`） | `C36E6AF71ACC1ADB60795CC235C8EBAC963A21CBC47C8C21AD749266843AA047` | 记录 `register` 与新的 `delete` 语义 |

`git status` 由 1290 → 1292 行，**只多出 2 条**（两个新测试文件）。`ui/js/*.js` 与 `api-reference.md` 在你原本就未提交的 `M` 文件内改动，未新增状态行。

### 后端：移除（`HandleDeleteInstanceAsync`）

- 增加 **Bisect 活动守卫**：`ModBisectService.Get(instanceId)` 存在且未完成 → 400 `该实例正在进行 Mod 二分定位，请先「放弃」或结束定位，再把它从列表移除。`
- 移除前采集 `gameDirectory / savesCount / sharedDirectory`，响应回带 `keptFiles = true`，供 UI 告知用户"文件仍在"。
- 代码内明确标注"只改配置：此处不存在任何 File/Directory 写操作"，并由测试以逐文件 SHA 证明。

### 后端：挂回（`HandleRegisterInstanceAsync`，新路由 `POST /api/instances/register`）

- 入参 `{ gameDirectory, name?, minecraftVersion? }`。
- 校验：目录非空（400 `请填写要挂回的游戏目录`）、路径合法、**目录必须已存在**（400 `目录不存在，无法挂回：…`）、**不得重复挂回同一目录**（400 `该目录已在实例列表中，无需重复添加：…`）。
- 只新增配置记录并保存；`InstanceService.CreateDefault` 之后覆写 `GameDirectory` 为用户指定目录。
- 同样零文件写入，由测试证明。

### 后端：`sharedGameDirectory`

`BuildInstanceSummary` 新增该字段：有其它实例解析到同一游戏目录时为 `true`，UI 据此在确认框追加共享目录提示。

### 前端

- `i18n.js`：`inst.delete` → 从列表移除 / Remove from List；`inst.deleteConfirm` 改为带 `{name}/{dir}/{saves}` 的说明式文案；新增 `inst.deleteSharedWarn`、`inst.deleted`（改为"已从列表移除；游戏目录与存档仍然保留。"）与 `inst.addExisting*` 系列共 15 个键，中英双侧齐全。
- `launcher.js`：`mDel` 处理改为组装 `{name, dir, saves}` 并追加共享目录提示；用户取消时直接 `return`，不发请求；安装管理页新增 `btnAddExisting` 按钮与"从已有目录添加"弹窗（含"不会创建、移动或删除任何文件"的说明）。
- `api.js`：`registerInstance` 在 live 侧 POST `/api/instances/register` 并在非 ok 响应时抛错；mock 侧只 `push` 一条列表记录。

---

## 四、RED → GREEN 证据

### RED（把 MR03 改动临时回退后运行新测试）

| 用例 | 实现前实际 | 判定 |
|---|---|---|
| `Remove_FromList_KeepsEveryGameFileByteIdentical` | `[remove] -> 200 body={"ok":true}` —— 响应缺 `keptFiles/gameDirectory/savesCount` | 失败 |
| `Remove_RejectedWhileBisectActive_AndRecordStays` | `[remove-while-bisect] -> 200`，**期望 400 实际 200** | 失败（真实缺陷） |
| `Register_ReAddsExistingDirectory_WithoutTouchingFiles` | `[register] -> 404 body={"error":"…未找到接口…"}` | 失败 |
| `Register_RejectsMissingDirectory_AndDuplicateEntry` | 同上（路由不存在，得不到 400 语义） | 失败 |
| `Remove_SharedDirectory_ReportsSharing_AndOtherInstanceSurvives` | 详情无 `sharedGameDirectory` | 失败 |
| `Remove_WithMissingGameDirectory_Succeeds_AndSecondCallReturns400` | 响应无 `savesCount` 契约 | 失败 |
| `Remove_LastInstance_LeavesEmptyListAndBlankSelection` | 通过 | ✅ **既有正确行为回归**（最后一个实例移除后列表空、选中项置空，原本就正确） |

**6 失败 / 1 通过**，日志：`red/01-instance-removal-before.log`。

> 说明：RED 里的文件保留断言并未真正被触发——失败发生在它之前的**响应契约**断言上。也就是说"不删文件"这一条在改动前**已经是正确行为**，本包新增的是可被检验的契约与入口；真正的行为缺陷是 **Bisect 会话进行中仍可被移除**（200 而非 400）。

### GREEN（最终产物）

| 检查 | 结果 | 日志 |
|---|---|---|
| `dotnet test --filter …InstanceRemovalTests` | **失败 0 / 通过 7** | `green/02-instance-removal-after.log` |
| `node --test ui/tests/instance-removal.test.cjs` | **tests 7 / pass 7 / fail 0** | `green/04-node-instance-removal.log` |
| `dotnet test`（全量） | **失败 0 / 通过 635 / 跳过 0**（55 s） | `green/05-csharp-full.log` |
| `node --test`（12 个 `.test.cjs`） | **tests 191 / pass 191 / fail 0** | `green/06-node-full.log` |

C# 用例覆盖：移除后逐文件 SHA 不变（含存档目录与 mods 目录仍在）、列表移除与选中项修正、Bisect 进行中拒绝移除、挂回后重新看到存档与 Mod 且零文件改动、目录不存在/空路径/重复挂回三种 400、共享目录提示与他人实例不受影响、最后一个实例、目录不可读时仍可移除、重复点击返回 400。

Node 用例覆盖：中英双侧键完整、确认文案不再出现"不可撤销"/"cannot be undone"且含目录与存档、live 侧 URL 与请求体正确、非 ok 响应必须抛错、launcher 确认流程接线（静态扫描，明确标注为补充防线）。

---

## 五、本轮自身发现并修正的两个问题

1. **测试自身的 bug**：`Fixture.FirstId` 实时读 `Instances[0]`，而后端会就地修改同一个 `List` 对象，删除后再取 id 抛 `ArgumentOutOfRangeException`。已改为构造时固化 `_allIds`。这是"实现正确但测试写错"的典型，已记录。
2. **MSBuild 增量编译陷阱**：用 `Copy-Item` 还原源码后，MSBuild 因为**保留了备份文件的旧时间戳**判定无需重编译，测试实际跑的是上一版程序集（表现为"还原后仍然 RED"）。修正方式：还原后显式 `LastWriteTime = Get-Date`。此后每次还原都核对 SHA 并更新时间戳。

---

## 六、明确未做与未验证

| 项 | 说明 |
|---|---|
| **"实例正在运行"守卫未实现** | 全仓不存在服务端运行态注册表（`runningInstance` / `launching` / 进程引用均无），`HandleLaunchAsync` 派发进程后不保留状态。因此本包只实现了 Bisect 守卫。计划原文允许"拒绝移除**或明确要求先结束**"，这里选择了后者：**这是一个真实缺口，应在 MR06/MR07 建立会话与运行态登记后回填**。前端确认框也未做运行态拦截（同样缺少数据来源）。 |
| 真实 GUI / WebView2 | 未启动应用；`confirm()` 的实际呈现、按钮点击链路均为未验证 |
| 移除后的默认实例补建 | 观察到一个未验证点：`InstanceService.EnsureDefaultInstance` 会在实例数为 0 时补建默认实例，但本包测试路径未触发它；"移除最后一个实例后 UI 会看到什么"需要在真实 GUI 验证 |
| MR04–MR07 | 未开始 |

---

## 七、回退方法

`LocalApiService.cs` 为未提交的 `M` 文件，**禁止 `git checkout/restore`**。

```powershell
$run = 'C:\Users\Administrator\.codex\worktrees\ed41\松光启动器\docs\market-execution\MR03-remove-from-list\20261002-190000Z'
# MR03 实现版备份（如需再次还原到本包结果）
Copy-Item "$run\green\LocalApiService.cs.mr03" `
  'D:\松光启动器\dev\PineglowLauncher\Pineglow.Launcher.Services\LocalApiService.cs' -Force
(Get-Item 'D:\松光启动器\dev\PineglowLauncher\Pineglow.Launcher.Services\LocalApiService.cs').LastWriteTime = Get-Date
```

**注意**：本轮的 `ui/js/{i18n,api,launcher}.js` **未保存改前副本**（它们是用户既有的 `M` 文件，`git diff` 混有既有改动）。手工回退时按 §三 的清单反向删除即可：`launcher.js` 的 `mDel` 新块 + `btnAddExisting` 按钮 + `bindInstallations` 中的 register 处理；`api.js` 的两处 `registerInstance`；`i18n.js` 的 15 个新键与 4 个改写字面量。下一包起改为**每个待改文件先存副本**。

---

## 八、证据清单

```
baseline/                              （本批通过绿色备份替代，见 green/LocalApiService.cs.mr03）
red/
  01-instance-removal-before.log        实现前 6 失败 / 1 通过
green/
  LocalApiService.cs.mr03               MR03 实现版备份（SHA 81E8C253…）
  LocalApiService.cs.mr03.sha256.txt
  LocalApiService.cs.final.sha256.txt   最终产物 SHA（含 BOM 恢复后）
  02-instance-removal-after.log         7 通过 / 0 失败
  03-debug-fail1.log                    测试 bug 的定位过程
  04-node-instance-removal.log          7 通过 / 0 失败
  05-csharp-full.log                    635 通过 / 0 失败
  06-node-full.log                      191 通过 / 0 失败
  git-status-after.txt                  1292 行
  changed-files.sha256.txt
manifest.json
```

**本报告不宣布阶段放行、不提交、不发布。** 下一包建议 `MR04`（Mod 可恢复删除），它依赖本包建立的"只改配置/不动文件"证据基线与 `InstanceRemovalTests` 的快照比对工具。
