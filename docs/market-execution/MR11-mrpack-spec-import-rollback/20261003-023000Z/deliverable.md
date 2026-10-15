# MR11 · `.mrpack` 导入的规范、互通与回滚 · 交付报告

> **收尾说明（同一 run-id 第 10 轮补齐）**：本报告最初把"导入取消"列为未做，状态是"部分证据就绪"。
> 后来在同一 run-id 内补齐了 **取消注册表接线 + 取消端点 + 前端取消按钮 + Node 接线测试**，
> 并顺带修掉三个真实缺陷（含一个我自己写出的短路 bug）。§一 与 §六 已更新；补齐部分见 §九。

- **run-id**：`20261003-023000Z`
- **执行范围**：MR11 全部（规范校验、必需字段错误处理、哈希镜像回退、失败回滚加固、**导入取消**）
- **依据**：`docs/market-task-plan-2026-10-03.md` §9、`market-research-task-workflow-2026-10-02.md` §9
- **最终状态**：**证据就绪**（后端 + 前端 + 测试；真实端到端导入仍未验证，见 §六）
- **未执行**：真实端到端 `.mrpack` 导入；MR12 及其它任务包；不提交、不推送、不发布

---

## 一、先纠正一个容易虚报的地方

计划给的验收里列了"路径逃逸防护""哈希校验""失败可恢复"等项。**侦察发现前两项早就实现了**，所以本包**没有**把它们算成自己的成果：

| 计划提到的能力 | 实测结论 |
|---|---|
| 路径逃逸防护 | ✅ **本包之前就存在**：`ExtractFolderFromArchive` 有前缀校验、`GetSafeTargetPath` 有规范化校验。本包只把它们**纳入测试**（`GetSafeTargetPath` 加 `internal` 以便直测） |
| 文件哈希校验 | ✅ **本包之前就存在**：sha512 + sha1 双校验。本包修的是它**失败后的处理**（见下） |
| 失败时删除半成品目录 | ✅ **本包之前就存在**。本包修的是它**会掩盖原始异常** |
| `env.client == "unsupported"` 跳过 | ✅ 已存在，纳入测试 |
| `formatVersion` 校验 | 🔴 **缺失** → 本包补上 |
| 必需字段缺失时的错误信息 | 🔴 **缺失**（裸 JSON 异常）→ 本包补上 |
| 哈希不匹配时的镜像回退 | 🔴 **缺失**（第一个镜像内容不对就整次导入失败）→ 本包补上 |
| 导入取消 | ✅ **已在第 10 轮补齐**（见 §九）：注册表 + 端点 + 前端取消按钮 + Node 测试 |

---

## 二、四处真实修复

### 2.1 规范版本校验（新增）

`.mrpack` 规范要求 `modrinth.index.json` 的 `formatVersion` 为 1。原实现**完全没读这个字段**——一个 `formatVersion: 2` 的包会被按 1 的语义静默导入。现在：

- `formatVersion` 缺失 → 按 1 处理（兼容早期包，有测试）；
- `formatVersion != 1` → 明确报错：`不支持的整合包规范版本：formatVersion=2，当前只支持 1。`

### 2.2 必需字段的清晰错误（新增）

原实现对 `dependencies` 与 `files` 直接 `GetProperty(...)`，字段缺失时抛的是裸 JSON 异常（用户看不懂）。现在分别给出中文说明，并额外校验 `dependencies` 里必须有 `minecraft`；`name` 缺失时退到安全名字 `Modrinth 整合包`（避免生成奇怪的游戏目录名）。**未知的额外字段一律忽略**（有测试），不会因为规范将来加字段就拒绝导入。

### 2.3 哈希校验失败时换下一个镜像（修复）

`downloads` 给多个地址，本意就是应对"某个镜像内容不对/被篡改"。原实现第一个镜像哈希不匹配就 `throw`，**整次导入失败**，后面正确的镜像根本不会被访问。现在：

- 哈希不匹配视为**该镜像失败**，清理临时文件后继续下一个；
- 只有**所有镜像都失败或都不匹配**才抛 `所有下载地址均失败或哈希校验不通过：<path>`；
- 顺带修掉一个真实缺陷：**取消被当成可重试的网络错误**（`TaskCanceledException` 落在重试分支里）。现在 `ct.IsCancellationRequested` 时直接抛出，不再重试。

### 2.4 失败回滚不掩盖原始异常（修复）

原 `catch` 里直接 `Directory.Delete`：如果删除失败（文件被占用等），抛出的清理异常会**盖掉真正的导入错误**，用户看到的是"目录删不掉"而不是失败原因。现在清理失败只记一条日志（写明路径需要手动清理），**原始异常照常抛出**。

### 2.5 关于 UI：本包无改动（说明理由）

计划的允许范围包含 `ui/js/*`。但本包四处修复**都是后端行为**：错误信息经既有导入错误通道呈现给用户，镜像回退与回滚对界面不可见。因此本包**不动 UI**——为改而改会增加无验证的改动面。

---

## 三、实际改动

| 文件 | 状态 | SHA256 |
|---|---|---|
| `dev/PineglowLauncher/Pineglow.Launcher.Services/InstancePackService.cs` | 修改（**本包前为干净**） | `BD98B4F3E00883A26009BF9DF4ACD7BE6B06BFDD0E9FB11D5CFE31F50A8542B2` |
| `dev/PineglowLauncher.Tests/InstancePackTests.cs` | **新增** | `746625D1679C206872C45E201CF3FE4E02FA3A192B8024715BC2B345095664C6` |
| `scripts/source-snapshot-inputs.txt` | 修改（未跟踪） | `202B114997B728AF56DDA992293DA2F0AC47F4929ECF7F3374EE91186E184D46` |

`git status` 1313 → 1315（+2：1 个新增测试 + `InstancePackService.cs` 由干净变为 `M`）。

**可见性变化**（仅为可测，不改行为）：`ModrinthPackFile` 与 `GetSafeTargetPath` 改为 `internal`；`DownloadModrinthPackFileAsync` 改为 `internal`；新增 `internal static ParseModrinthIndex` 与 `internal sealed class ModrinthPackManifest`。

> 为什么新增 `ParseModrinthIndex`：真实导入一定会联网安装 Minecraft 与加载器，单测里跑不通也不该跑。
> 把"规范字段校验"这一步从导入流程里提出来，是**提取**而不是重写——导入流程本身原样保留。

---

## 四、RED → GREEN

**RED 构造方式（需要说明）**：该文件在本包开始前是**干净的**（`git status` 未修改），原则上可回退到 `git HEAD`；
但直接回退会让测试**编译不过**（测试要调新方法签名），那样的 RED 只是编译错误、证明不了行为。
所以 RED 基线 = 当前实现**只还原我这一轮改动的 7 处行为**（4 处校验、1 处名字兜底、2 处哈希回退），
其余能力原样保留——这样"通过的用例"恰好是**本包没有改动**的能力，不夸大缺口。

**RED：7 失败 / 8 通过**（`red/01-mrpack-before.log`）

| 失败（= 本包修的） | 通过（= 本包没动的既有能力） |
|---|---|
| formatVersion 不接受 2 | 格式探测（mrpack/CF/MCM/未知） |
| 缺 dependencies 无清晰错误 | 路径逃逸防护 |
| 缺 minecraft 依赖无清晰错误 | env.client=unsupported 跳过 |
| 缺 files 无清晰错误 | 未知字段容忍 |
| name 缺失无兜底 | 第一个镜像哈希正确时只用它 |
| 哈希不匹配不换镜像 | 接受规范最小包 |
| 所有镜像都不匹配时的报错口径 | 缺 formatVersion 默认 1 |

**GREEN**

| 检查 | 结果 | 日志 |
|---|---|---|
| `--filter …InstancePackTests` | **15 通过 / 0 失败** | `green/02-mrpack-after.log` |
| `dotnet test`（全量） | **702 通过 / 0 失败 / 0 跳过**（58 s） | `green/03-csharp-full.log` |
| `node --test`（15 个 `.test.cjs`） | **211 通过 / 0 失败** | `green/04-node-full.log` |

---

## 五、本轮自身发现并修正的问题

1. **测试写错了命名空间/嵌套类型**：`ModpackArchiveFormat` 在 `Pineglow.Launcher.Models` 而非 `Services`；`ModrinthPackManifest` 是 `InstancePackService` 的嵌套类型。已修正引用。
2. **RED 基线构造脚本第一次全没命中**：我按 CRLF 拼多行字符串，而该文件是 **LF** 行尾。改为 LF 后 7/7 命中（脚本对每个替换都做了命中校验，未命中的会中止而不是静默跳过）。
3. **差点用错回退源**：我在改完文件后才查 `git status`，一度以为该文件"本来就是 M"，差点得出"不能用 git HEAD 回退"的错误结论。按批次末快照比对后才确定它在本包前是干净的——这条已写进 §四 的 RED 说明。

---

## 六、明确未做与未验证

| 项 | 说明 |
|---|---|
| 🔴 **导入取消** | ✅ **已在第 10 轮补齐**，见 §九。 |
| 真实端到端导入 | 未做：真实导入必然联网安装 Minecraft 与加载器，且会写真实游戏目录。属 MR22 内测范围。 |
| 加载器安装分支 | `InstallLoaderAsync`（Fabric/Quilt/Forge/NeoForge）本包未改动也未测试。 |
| 覆盖文件（overrides）应用顺序 | 代码按 `overrides` → `client-overrides` 顺序应用，本包未改动；**但其顺序与冲突语义没有测试**（依赖真实解压路径）。 |
| `server-overrides` | 客户端启动器按规范**不应用**它；本包确认代码确实没应用，属正确行为，未做测试。 |
| 并发同名导入 | `CreateIsolatedGameDirectory` 在名称相同时的行为未验证。 |

---

## 七、回退方法

```powershell
$run = 'C:\Users\Administrator\.codex\worktrees\ed41\松光启动器\docs\market-execution\MR11-mrpack-spec-import-rollback\20261003-023000Z'
Copy-Item "$run\green\InstancePackService.cs.mr11" `
  'D:\松光启动器\dev\PineglowLauncher\Pineglow.Launcher.Services\InstancePackService.cs' -Force
(Get-Item 'D:\松光启动器\dev\PineglowLauncher\Pineglow.Launcher.Services\InstancePackService.cs').LastWriteTime = Get-Date
```
（该文件**没有** BOM，与本会话前快照一致，回退后请保持。）

其余：删除新增的 `InstancePackTests.cs`；白名单去掉类别 B 的新增行并把计数 26→25。

---

## 八、证据清单

```
red/01-mrpack-before.log                 回退 7 处行为后 7 失败 / 8 通过
green/
  InstancePackService.cs.mr11            MR11 实现版备份（SHA BD98B4F3…）
  InstancePackService.cs.mr11.sha256.txt
  01-mrpack-first.log                    首次运行（2 处测试自身引用写错）
  02-mrpack-after.log                    15 通过 / 0 失败
  03-csharp-full.log                     702 通过 / 0 失败
  04-node-full.log                       211 通过 / 0 失败
  git-status-after.txt                   1315 行
  changed-files.sha256.txt
manifest.json
```

**本报告不宣布阶段放行。** 补齐部分见 §九。

---

## 九、收尾追加（第 10 轮补齐：导入取消）

### 9.1 做法

- **复用**既有的 `AiOperationRegistry`（带 TTL / 容量 / cancel-before-register 墓碑），
  用 `ImportOperationScope = "import"` 充当"实例维"占位符（导入时实例还不存在），**没有新写一个注册表**。
- 新增 `POST /api/instances/import/cancel`（`{operationId}`）。
- `POST /api/instances/import` 接受可选 `operationId`：传了就能取消，不传则行为与之前完全一致（向后兼容）。
- 取消后返回 **409 + `cancelled:true` + "导入已取消，未生成实例。"**，且**绝不写入配置**（实例只在成功后入列表）。
- 导入器在**耗时最长的 Java 解析之前和之后各认一次取消**（原先只在后续网络请求处才会发现取消，
  用户点了取消要干等）。
- 前端：上传前生成 `operationId`（`crypto.randomUUID`），弹窗加「取消导入」按钮，
  取消后提示"导入已取消，未生成实例"而**不是**"导入失败"。

### 9.2 补齐过程中修掉的三个真实缺陷

1. **我自己写出的短路 bug（最严重）**：取消端点原写成
   `cancelled = wasActive && PackOps.Cancel(...)`。`&&` 短路导致**未知 operationId 根本不会调用 `Cancel`**，
   而 `Cancel` 正是登记 cancel-before-register 墓碑的那一步 —— "取消先到"的能力被我写废了，
   表现为**取消发生在受理之前时导入照常继续**（实测卡到 90 秒超时）。
   已改为无条件调用 `Cancel`，`cancelled` 只表达"是否取消了在途操作"。
2. **`CancellationTokenSource has been disposed.`**：注册表在墓碑分支会 `Cancel()` 并触发
   `Remove` → `Dispose()`，之后再去读 `importCts.Token` 就抛异常。已改为**先取 token 再注册**
   （`CancellationToken` 是结构体，副本在源被 dispose 后仍可安全查询取消状态）。
3. **取消被当成可重试的网络错误**（上一轮已修，这里记录其影响）：`TaskCanceledException` 原本落在重试分支，
   用户取消后会重试两次再报错。

### 9.3 收尾证据

| 检查 | 结果 | 日志 |
|---|---|---|
| `--filter …InstancePackTests\|…ImportCancellationTests` | **21 通过 / 0 失败** | `green/05-import-cancel-after.log` |
| `node --test ui/tests/import-cancel.test.cjs` | **5 通过 / 0 失败** | `green/09-node-import-cancel.log` |
| `dotnet test`（全量） | **708 通过 / 0 失败 / 0 跳过** | `green/10-csharp-full-final.log` |
| `node --test`（16 个 `.test.cjs`） | **216 通过 / 0 失败** | `green/11-node-full-final.log` |

关键实测数据（都在日志里）：
- 取消在途操作 → `{"cancelled":true,...}`，且 **CTS 真的被 cancel**（断言 `cts.IsCancellationRequested`）；
- 取消先到 → 导入请求 **8ms 内**返回 409 `cancelled:true`（修复前是 90 秒超时）；
- 带已取消令牌导入 → 抛 `OperationCanceledException`，**实例目录不存在**（不产生半成品）；
- 未知 operationId → `cancelled:false` + `pendingCancel:true`（**如实区分**"没取消到在途操作"，
  避免界面误报"已取消进行中的导入"）。

### 9.4 收尾后仍未验证

- 真实 WebView2 里的取消按钮点击链路、以及**真实联网导入的中途取消**（需要真实网络与真实 Minecraft 安装）未验证；
- 本文件覆盖的是取消的**接线与回滚**，与 AI 诊断取消复用同一套注册表实现。

**本报告不宣布阶段放行、不提交、不发布。**