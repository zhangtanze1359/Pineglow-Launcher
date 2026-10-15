# W1 + W2 + W3 · 路由可达性收口 · 交付报告

- **run-id**：`20261002-182304Z`
- **执行范围**：W1 路由可达性修复 + W2 HTTP 层路由契约测试 + W3 文档同步
- **依据**：`docs/market-task-plan-2026-10-03.md` §5（W1/W2/W3 任务卡）
- **落点决定**：直接改主工作区 `D:\松光启动器`（用户本轮明确选择），改前保存原件副本作为回退基线
- **最终状态**：**具备独立复验条件，等待独立验收**

---

## 一、结论先行

| 项 | 结果 |
|---|---|
| W1 路由可达性 | ✅ 修复。10 条路由由路由器兜底 404 变为 handler 级响应 |
| W2 契约测试 | ✅ 新增 16 个用例；修复前 **15 失败 / 1 通过**，修复后 **16 通过 / 0 失败** |
| W3 文档同步 | ✅ `docs/api-reference.md`（含 1 条幽灵路由）、`docs/roadmap.md` 已更正 |
| API 级 E2E | ✅ `VerifyHost --bisect-smoke`：修复前 **exit=1（首个 `POST /bisect/start` 即 404）** → 修复后 **exit=0，10/10 PASS** |
| C# 全量回归 | ✅ **628 通过 / 0 失败 / 0 跳过**（612 既有 + 16 新增，58.5s） |
| 正式 Node 回归 | ✅ **184 通过 / 0 失败**（11 个 `.test.cjs`，3s） |
| 主工作区只读性（其余资产） | ✅ `git status` 由 1287 → 1290 行，新增仅 3 条（2 个新测试文件 + `api-reference.md` 新变为 M） |
| 真实 GUI / WebView2 / 真实游戏 | ⬜ **未验证**（本批未启动） |

---

## 二、关键发现：阻断范围比既有验收报告更宽

`20261002-072004Z` 的独立验收报告记录了 5 条不可达路由（`bisect/*` 与 `mods/{file}/*`）。本轮逐行复算后确认实际为 **10 条**——**存档功能的 5 条路由同样不可达**，此前任何报告与测试都未记录：

| # | 路由 | 方法 | 分派行（修复前） | 前端调用点 |
|---:|---|---|---:|---|
| 1 | `/saves/{folder}/backup` | POST | 450 | `ui/js/api.js:678` |
| 2 | `/saves/{folder}/delete` | POST | 450 | `api.js:695` |
| 3 | `/saves/{folder}/open` | POST | 450 | `api.js:699` |
| 4 | `/saves/{folder}/backups` | GET | 450 | `api.js:682` |
| 5 | `/saves/{folder}/backups/{name}/restore` | POST | 480 | `api.js:691` |
| 6 | `/bisect/start` | POST | 511 | `api.js:752` |
| 7 | `/bisect/test` | POST | 511 | `api.js:756` |
| 8 | `/bisect/giveup` | POST | 511 | `api.js:760` |
| 9 | `/mods/{file}/toggle` | POST | 531 | `api.js:534` |
| 10 | `/mods/{file}/delete` | POST | 531 | `api.js:538` |

**对用户的实际影响**：存档备份、备份列表、恢复备份、删除存档、打开存档目录这 5 项功能在修复前对用户完全不可用，且 796 个绿色测试对此无感知。

---

## 三、实际改动

| 文件 | 状态 | 改前 SHA256 | 改后 SHA256 |
|---|---|---|---|
| `dev/PineglowLauncher/Pineglow.Launcher.Services/LocalApiService.cs` | 修改（原已是 `M`） | `1EEF455D57897DCED28AFAEBC6F064B23EAF96D90692CC0050F6A9AA8BB03A5E` | `F9F662A85922C08AFF7715B1F3C0179729FD1FC46401474D2EC816108DD3FF77` |
| `dev/PineglowLauncher.Tests/LocalApiTestHarness.cs` | **新增** | — | `CA7A151FED50E8022232F59D49A42E66A78B2517118B5E1F090830FBBCADF78A` |
| `dev/PineglowLauncher.Tests/RouteReachabilityTests.cs` | **新增** | — | `73BDE9818547430B5178345EB99533B7B4770DDBF73AA0238AA13B2D61BAD08E` |
| `scripts/source-snapshot-inputs.txt` | 修改（未跟踪） | — | `5FB957353FD66200549E634760901FD93F60085FFF5AA357B7A978878914D2F0` |
| `docs/api-reference.md` | 修改（原未修改） | — | `3C5F2F093499AF809C1EB02533524065E8C6385D77DB653105DA62E23DA33D32` |
| `docs/roadmap.md` | 修改（原已是 `M`） | — | `C078B46B7CBB1B5B08BBBEB55ECEE64FA12E4D657E9FF7BEA6138DCDE0765358` |
| `docs/market-task-plan-2026-10-03.md` | 修改（本会话新增的台账） | — | `C13DA902D18160481947F8F16BF41CDA0316F87786B0ECE62A34D774132FEDBC` |

### W1 的修复方式

在 `:413`（cancel 守卫结束）与长度 4 通用实例块之间，新增块外前置分派：

```csharp
if (seg.Length is 5 or 6 or 8 && seg[1].Equals("instances", StringComparison.OrdinalIgnoreCase))
{
    string instanceId = seg[2];
    string action = seg[3];
    // ← 原 :449-477  saves(6)：backup|delete|open|backups
    // ← 原 :479-489  saves(8)：backups/{name}/restore
    // ← 原 :510-531  bisect(5)：start|test|giveup
    // ← 原 :531-546  mods(6)：toggle|delete
}
```

4 段块体**逐字搬移**，未改动任何 `Handle*Async` 实现。前置块已提供同名 `instanceId`/`action` 局部变量，故搬移后引用关系不变。

**否决的替代方案**：把外层守卫放宽为 `seg.Length >= 4`。原因：块内 `delete`/`launch`/`diagnose`/`prelaunch-check` 只匹配 `action`、不带长度守卫，放宽后 `/api/instances/{id}/delete/任意后缀` 会命中 `HandleDeleteInstanceAsync`——把只读探针变成移除实例的写操作。

### 改动规模与结构校验

- 相对改前原件：**4 个 hunk，全部落在分发区（文件行 412-562）**，+60/−50 行（git 移动感知渲染）。
- 4 个搬移块在新文件中各出现 **恰好 1 次**（无重复、无丢失）。
- 全文件大括号 **744 `{` / 744 `}` 平衡**。
- 文件头 BOM 已保持与原文件一致（`EF BB BF`）。

### W2 的测试设计要点

- 识别口径：路由器兜底 404 的响应体是 `{"error":"未找到接口：{method} {path}"}`（全文件仅 `:789` 一处），与各 handler 自己的 400/404 文案不同。
- **必须解析 JSON 再读 `error`**，不能对原始响应体做字符串匹配：`WriteJsonAsync` 使用默认 `JavaScriptEncoder`，非 ASCII 会被转义为 `\uXXXX`。
- **反空洞对照**：`POST /api/instances/{id}/bogus/xyz` 必须返回兜底 404 且带该标记。修复前这是**唯一通过**的用例，证明另外 15 个失败是真实的路由不可达，而不是断言本身失效。
- **静态结构守卫**：字符级扫描 `HandleApiAsync`，若某 `if (seg.Length == N` 被包在约束为其它 N 的作用域内则失败。不能用行首锚定——修复前 `:531` 形如 `}` + tab + `if (...)`，行锚定会漏掉最隐蔽的那一条；token 必须含 `if (`，否则注释里的同名文本会被误判为守卫（本轮实际踩过一次，见第六节）。
- **不碰真实数据**：`AppConfig.GameDirectory` 与 `GameInstance.GameDirectory` 都显式指向 `%TEMP%\pineglow-route-test-<guid>`；`ResolveGameDirectory` 在两者皆空时会回退到真实 `.minecraft`，故必须显式设置。`saves/{folder}/open` 故意使用不存在的文件夹，走 `RequireSave` 抛 400 的分支，**不会弹出资源管理器窗口**。
- **真实配置零改动护栏**：夹具在启动前与释放后各捕获一次真实 `config.json`/`.bak`/`.prev` 的状态与 SHA，不一致即 `Assert.Fail`。16 个用例全绿 ⇒ 真实配置未被触碰。

---

## 四、逐命令证据

| # | 命令 | cwd | 退出码 | 结果 | 日志 |
|---|---|---|---:|---|---|
| 01 | `dotnet build VerifyHost.csproj -c Release`（RED 前） | `D:\松光启动器` | 0 | 生成成功 | `red/01-build-verifyhost.log` |
| 02 | `VerifyHost.exe --bisect-smoke --port 48130`（RED） | `…\VerifyHost\bin\Release\net10.0` | **1** | `[BisectSmoke] exception: 404 (Not Found)` / `FAIL (1 checks failed)` | `red/02-bisect-smoke-before.log` |
| 03 | `dotnet test --filter FullyQualifiedName~RouteReachabilityTests`（RED） | `D:\松光启动器` | **1** | **15 失败 / 1 通过**；失败项均为"命中路由器兜底 404" | `red/03-route-tests-before.log` |
| 04 | 同上，仅静态守卫用例（扫描器修正后） | `D:\松光启动器` | **1** | 精确报出 **4 处**互斥嵌套（saves 长度 6、saves 长度 8、bisect 长度 5、mods 长度 6） | `red/04-static-guard-before.log` |
| 05 | 修复后 `dotnet test --filter …RouteReachabilityTests` | `D:\松光启动器` | 0 | **16 通过 / 0 失败**；10 条路由实测状态码见 §五 | `green/06-route-tests-after.log` |
| 06 | `dotnet build VerifyHost.csproj -c Release` | `D:\松光启动器` | 0 | 生成成功 | `green/07-build-verifyhost.log` |
| 07 | `VerifyHost.exe --bisect-smoke --port 48131`（GREEN） | `…\VerifyHost\bin\Release\net10.0` | **0** | `[BisectSmoke] PASS`，**10/10 PASS** | `green/08-bisect-smoke-after.log` |
| 08 | `dotnet test Pineglow.Launcher.Tests.csproj -c Release`（全量） | `D:\松光启动器` | 0 | **628 通过 / 0 失败 / 0 跳过**，58.5s | `green/09-csharp-full.log` |
| 09 | `node --test --test-reporter=spec <11 个 .test.cjs>` | `D:\松光启动器` | 0 | **tests 184 / pass 184 / fail 0 / skipped 0**，3.0s | `green/11-node-tests.log` |

> 每一步单独检查 `$LASTEXITCODE`，未用分号串联后只看最后一条退出码。

### 最终字节序列上的复跑（BOM 恢复之后）

§三 的改后 SHA `F9F662A8…` 是**最终产物**。为保证"验证的就是交付的那份字节"，在 BOM 恢复后对最终产物完整重跑了一遍：

| # | 命令 | 退出码 | 结果 | 日志 |
|---|---|---:|---|---|
| 10 | `dotnet build VerifyHost.csproj -c Release` | 0 | 生成成功 | `green/12-build-verifyhost-final.log` |
| 11 | `VerifyHost.exe --bisect-smoke --port 48132` | **0** | `[BisectSmoke] PASS` | `green/13-bisect-smoke-final.log` |
| 12 | `dotnet test --filter …RouteReachabilityTests` | 0 | **失败 0 / 通过 16 / 跳过 0 / 总计 16**（277 ms） | `green/14-route-tests-final.log` |
| 13 | `dotnet test`（全量） | 0 | **失败 0 / 通过 628 / 跳过 0 / 总计 628**（55 s） | `green/15-csharp-full-final.log` |
| 14 | `node --test`（11 个 `.test.cjs`） | 0 | **tests 184 / pass 184 / fail 0** | `green/16-node-tests-final.log` |


### 10 条路由修复后的实测响应

| 路由 | 状态码 | 响应摘要 |
|---|---:|---|
| `POST /mods/a.jar/toggle` | 200 | `{"ok":true,"fileName":"a.jar","isEnabled":false}` |
| `POST /mods/a.jar/delete` | 200 | `{"ok":true,"fileName":"a.jar"}` |
| `POST /bisect/start` | 200 | 会话已建，`disabledHalf:["a.jar"]`，`step:1` |
| `POST /bisect/test`（无会话） | 400 | `当前没有进行中的二分定位会话，请先开始。` ← handler 已到达 |
| `POST /bisect/giveup`（无会话） | 400 | 同上 ← handler 已到达 |
| `GET /saves/world1/backups` | 200 | `{"backups":[]}` |
| `POST /saves/world1/backup` | 200 | 真实生成 `<TEMP>\…\backups\world1\world1-20261003-023840.zip` |
| `POST /saves/world1/delete` | 200 | `{"ok":true,"folderName":"world1"}` |
| `POST /saves/nosuch/open` | 400 | `存档不存在：nosuch` ← 路由已达、且未弹出资源管理器 |
| `POST /saves/world1/backups/nosuch-backup.zip/restore` | 400 | `备份不存在：nosuch-backup.zip` ← handler 已到达 |

行为级用例（非仅"不是 404"）：
- `Bisect_StartThenGiveUp_ReachesHandlerAndRestoresMods`：start 后盘上出现 `.jar.disabled`，giveup 后两文件均恢复启用、`.disabled` 归零。
- `Bisect_StartThenTest_LocksCulpritAndRestoresMods`：`test(crashed=false)` 返回 `finished=true`、`culprit="a.jar"`，并已恢复。
- `Mods_ToggleAndDelete_ReachHandlerAndChangeDisk`：toggle 后 `a.jar` → `a.jar.disabled`，delete 后文件消失。
- `Saves_BackupListRestoreDelete_ReachHandler`：backup → 列表非空 → restore → delete 全链路 200。

---

## 五、W3 文档同步

| 文件 | 改动 |
|---|---|
| `docs/api-reference.md` | 在「实例」表前新增 2026-10-03 修正说明（10 条路由曾不可达 + 修复 run-id + 真实 GUI 仍未验证）；**修正 1 条幽灵路由**：原表写 `DELETE /api/instances/{id}/saves/{folder}`，但全仓 `method == "DELETE"` 分派只有 `:555` 的 accounts，实际删除是 `POST …/saves/{folder}/delete`；同时补上遗漏的 `POST …/saves/{folder}/open` |
| `docs/roadmap.md` | 「Mod Bisect 二分定位」节新增勘误块：原文「已实现 `/bisect/start\|test\|giveup` 路由」只在服务层与 UI 层成立、路由层当时不成立；「回归」段更正为"该冒烟在修复前长期失败、修复后实测 exit=0、**尚未在真实 GitHub Actions 中运行**" |
| `docs/market-task-plan-2026-10-03.md` | §8 台账中 W1/W2/W3 由「未授权」改为「证据就绪（待独立验收）」 |

`README.md` 无相关路由声明，未改动。

---

## 六、本轮自身的事故与恢复（如实记录）

执行过程中发生两次由我造成的文件损坏/偏差，均已恢复并验证：

1. **`LocalApiService.cs` 被脚本写成 5 行空内容。** 首次搬移脚本中 `List<string>.AddRange($L[0..413])` 的元素类型是 `Object[]`，PowerShell 抛类型转换异常后仍继续执行了写文件语句，落盘内容只剩若干空行（8 字节）。**已用 `baseline/LocalApiService.cs.orig` 覆盖回退，SHA256 与改前 `1EEF455D…` 逐字节一致（3901 行 / 136050 字节）。** 随后重做脚本，加入 `[string[]]` 显式转换与写前校验（长度区间 + 关键 token 存在性），并修正了写前校验用错单位（把字节阈值套在字符长度上）的问题。
2. **UTF-8 BOM 两次丢失。** 复算 diff 时发现首个 hunk 为 `-\uFEFFusing System;`，说明原件带 BOM 而我的写入把它去掉了（先前的 BOM 检测被 `Encoding.UTF8` 解码器吞掉，判断有误）。用 `UTF8Encoding($true)` 重写后恢复；随后又一次编辑注释（`edit` 工具写回）再次丢掉 BOM，最终再次恢复并校验 `EF BB BF` 一致。**由于字节序列因此变化，全部验证已在最终字节上重跑**（见 §四"最终字节序列上的复跑"）。BOM 不影响编译，但它会让用户的 diff 首行凭空多出一条改动，属于不必要的噪声。
3. **静态扫描器两次误报/漏报**（均已在测试代码内修正，属测试自身的缺陷）：
   - 按行首锚定会漏掉修复前最隐蔽的 `:531`（行首是 `}`）→ 改为字符级扫描。
   - target token 未含 `if (`，导致我自己新增的注释里出现的字面量被当成守卫，产生 4 个假违规 → token 收紧为 `if (seg.Length == `，并同时把注释里的字面量改成「长度 4」。

这两次事故都由**先存的基线副本**兜住；也说明"改前存原件 + 逐字节校验"这条纪律是必要的，而不是形式。

---

## 七、未验证项与残留风险

| 项 | 状态 |
|---|---|
| 真实 GUI / WebView2 页面交互 | **未验证**（本批未启动应用） |
| 真实游戏启动、真实存档备份/恢复的用户流程 | **未验证**（测试用的是 `%TEMP%` 合成目录与合成 Mod） |
| `saves/{folder}/open` 真实打开资源管理器 | **未验证**（测试刻意走不存在的存档，规避弹窗） |
| GitHub Actions 实际运行 | **未验证**（未推送、未建 PR） |
| RID 发布锁定 NU1004 | 本批未处理，属 MR19 |
| `dotnet publish` 就地改写 `packages.lock.json` | 本批未跑 `publish`，未再动该文件 |
| 并行写入 | 执行期间 `git status` 基线稳定（仅本批 3 条新增），未见外部并行改动 |

**遗留发现（本批未修，按要求不扩项）**：`docs/engineering.md`、`docs/ai-*.md` 仍未同步 `20261002-072004Z` 的勘误；`docs/phase3a-news-evidence-2026-09-05/` 下存有历史 `pre-diff-LocalApiService.cs.diff` 等文件，属既有资产，未触碰。

---

## 八、回退方法

`LocalApiService.cs` 是用户**未提交的 `M` 文件**，**禁止使用 `git checkout/restore`**（会丢弃既有工作）。回退步骤：

```powershell
$run = 'C:\Users\Administrator\.codex\worktrees\ed41\松光启动器\docs\market-execution\W1-W3-route-closure\20261002-182304Z'
Copy-Item -LiteralPath "$run\baseline\LocalApiService.cs.orig" `
  -Destination 'D:\松光启动器\dev\PineglowLauncher\Pineglow.Launcher.Services\LocalApiService.cs' -Force
(Get-FileHash 'D:\松光启动器\dev\PineglowLauncher\Pineglow.Launcher.Services\LocalApiService.cs' -Algorithm SHA256).Hash
# 期望 1EEF455D57897DCED28AFAEBC6F064B23EAF96D90692CC0050F6A9AA8BB03A5E
```

新增文件按 §三 清单移除即可：`LocalApiTestHarness.cs`、`RouteReachabilityTests.cs`，以及 `scripts/source-snapshot-inputs.txt` 中新增的 2 行与计数改动。文档改动见 `docs/api-reference.md.diff`、`docs/roadmap.md.diff`。

---

## 九、证据清单

```
baseline/
  LocalApiService.cs.orig                 改前原件（回退基线）
  LocalApiService.cs.sha256.txt           改前 SHA256
  git-status-before.txt                   改前 git 状态（1287 行）
red/
  01-build-verifyhost.log
  02-bisect-smoke-before.log              exit=1 原日志
  03-route-tests-before.log               15 失败 / 1 通过
  04-static-guard-before.log              静态扫描报出 4 处互斥嵌套
green/
  W1-LocalApiService.patch                相对改前原件的补丁
  05-route-tests-after.log                中间一次（静态扫描器修正前）
  06-route-tests-after.log                16 通过 / 0 失败
  07-build-verifyhost.log
  08-bisect-smoke-after.log               exit=0，10/10 PASS
  09-csharp-full.log                      628 通过 / 0 失败
  10-node-test-list.txt                   11 个正式 .test.cjs 清单
  11-node-tests.log                       184 通过 / 0 失败
  12-build-verifyhost-final.log           BOM 恢复后的最终产物复跑
  13-bisect-smoke-final.log               exit=0，PASS
  14-route-tests-final.log                16 通过 / 0 失败（最终字节）
  15-csharp-full-final.log                628 通过 / 0 失败（最终字节）
  16-node-tests-final.log                 184 通过 / 0 失败（最终字节）
  git-status-after.txt                    改后 git 状态（1290 行）
  changed-files.sha256.txt                本次涉及文件的改后 SHA256
docs/
  api-reference.md.diff
  roadmap.md.diff
manifest.json                             本目录每项证据的路径/SHA256/大小
```

**本报告不宣布阶段放行、不提交、不发布。** 下一步需用户决定是否放行 `MG1`（MR03–MR07），并确认 `docs/engineering.md`、`docs/ai-*.md` 是否纳入下一批文档同步。
