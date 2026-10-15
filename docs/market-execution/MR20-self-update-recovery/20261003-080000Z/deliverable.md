# MR20 · 自更新安全、失效与恢复 · 交付报告

- **run-id**：`20261003-080000Z`
- **执行范围**：平台资产匹配、下载截断判定、**备份→替换→逐字节校验→失败回滚**、apply 结果记录、信任链声明
- **依据**：`docs/market-task-plan-2026-10-03.md` §7.2/§8、`market-research-task-workflow-2026-10-02.md` §12（MR20 卡片）
- **最终状态**：**证据就绪**（替换与回滚链路已可真实执行验证；真实安装目录替换与真实重启未做，见 §六）
- **未执行**：不提交、不推送、不发布

---

## 一、结论先行

| 项 | 结果 |
|---|---|
| 🔴 **最大缺口：更新不能回退** | ✅ **已补**。原 `ApplyUpdate` 直接 `copy /Y` 覆盖旧 exe，**没有备份、没有校验、没有回滚** —— MG3 风险清单里这一条此前没有任何防线 |
| 备份 → 替换 → 逐字节校验 → 回滚 | ✅ 脚本改为：先备份；替换失败用备份放回；替换后 `fc /b`（Unix `cmp -s`）逐字节校验，不一致再回滚 |
| **异常时仍有可运行旧版** | ✅ 三条失败路径**都会启动旧版本**（`backup_failed` / `replace_failed` / `verify_failed`） |
| apply 结果可验证 | ✅ 脚本写出 `apply-state.json`；**界面不能因为"脚本已启动"就显示更新完成**，只有 `applied` 才算替换成功 |
| 平台资产匹配 | ✅ 资产名含别的平台记号（如 `linux-x64`）→ **下载前拒绝**；无记号则接受但在原因里说明"无法断言平台匹配" |
| 下载截断判定 | ✅ 按清单声明的大小判截断，给出比"校验失败"更精确的原因（含声明/实际字节数） |
| 信任链声明准确 | ✅ 明确写出"SHA256 用于发现损坏/截断，**不提供发布者身份认证**；同源可同时篡改"，并回给界面 |
| C# 测试 | ✅ 新增 19 个用例（含 **3 个真实执行替换脚本**的用例）；RED 基线 **6 失败 / 13 通过** → **19 通过 / 0 失败** |
| C# 全量回归 | ✅ **747 通过 / 0 失败 / 0 跳过**（728 + 19） |
| Node 全量回归 | ✅ **216 通过 / 0 失败**（16 个文件，未变） |

---

## 二、问题

改动前的替换脚本（Windows 分支）只有四行核心逻辑：

```bat
copy /Y "<新包>" "<当前 exe>" >nul
start "" "<当前 exe>"
```

后果：

1. **没有备份**：新包一旦有问题，用户**再也回不到能跑的版本**；
2. **不看退出码**：`copy` 失败（文件被占用、权限不足、磁盘满）也照样 `start`，用户以为更新成功了；
3. **没有校验**：替换到一半（断电、杀进程）不会被发现；
4. **没有记录**：界面无法知道到底替换成功没有。

---

## 三、实际改动

| 文件 | 状态 | SHA256 |
|---|---|---|
| `dev/PineglowLauncher/Pineglow.Launcher.Services/UpdateService.cs` | 修改（本包前为干净） | `F050172E68930AEF14802BCE7738C71F8C06D3E76F1ECE3436C6ADB81FAFB8C1` |
| `dev/PineglowLauncher/Pineglow.Launcher.Services/LocalApiService.cs` | 修改（原已是 `M`） | 见 `manifest.json` |
| `dev/PineglowLauncher.Tests/UpdateRecoveryTests.cs` | **新增** | 见 `manifest.json` |
| `scripts/source-snapshot-inputs.txt` | 修改 | 见 `manifest.json` |

### 3.1 新的替换脚本（Windows 分支）

```bat
copy /Y "%TARGET%" "%BACKUP%" >nul
set "RC=%errorlevel%"
if not "%RC%"=="0" ( echo {"result":"backup_failed",...} > "%STATE%" & goto abort )
copy /Y "%NEW%" "%TARGET%" >nul
set "RC=%errorlevel%"
if not "%RC%"=="0" ( copy /Y "%BACKUP%" "%TARGET%" >nul & echo {"result":"replace_failed",...} & goto abort )
fc /b "%NEW%" "%TARGET%" >nul
set "RC=%errorlevel%"
if not "%RC%"=="0" ( copy /Y "%BACKUP%" "%TARGET%" >nul & echo {"result":"verify_failed",...} & goto abort )
echo {"result":"applied",...} > "%STATE%"
start "" "%TARGET%"
goto done
:abort
rem after rollback we still start the old binary, so a broken update cannot leave the user without a runnable launcher
start "" "%TARGET%"
:done
```

- 脚本生成被抽成**纯函数** `BuildApplyScript(...)`（含 `restartCommand` 参数），因此测试能**真实执行**它；
- 脚本内容为**纯 ASCII**（含中文注释会让 cmd 在 `chcp` 生效前按 ANSI 解码，属真实编码风险）。

### 3.2 其余三处

- `IsAssetCompatible(asset, currentRid, out reason)`：拒绝别的平台记号；
- `DownloadUpdateAsync`：先按 `manifest.Size` 判截断（比 SHA 失败更精确），再校验 SHA；
- `TrustModelNote`：准确的信任链声明，经 `/api/update/status` 的 `trustNote` 回给界面；
- `ReadApplyResult` + `/api/update/status` 的 `lastApplyResult`：界面可据此判断"上次到底替换成功没有"。

---

## 四、RED → GREEN

**RED 构造方式**：把本包改动的 4 处行为还原成改动前（无平台匹配、不判截断、不准确的信任声明、脚本无备份/校验/状态写入），保留新类型以便测试编译。

**RED：6 失败 / 13 通过**（`red/01-update-recovery-before.log`）

13 个通过里有两类，如实说明：

| 通过项 | 为什么 RED 下也通过 |
|---|---|
| 版本比较 7 例 | `IsNewerVersion` 本包**未改动** |
| 平台匹配"接受" 2 例 | 改动前也接受（它什么都不检查） |
| `ReadApplyResult` 2 例 | 读取器是本包新增，但它是独立纯函数，RED 下仍可测 |
| **`backup_failed` 1 例** | **RED 构造的副作用**：我中和"备份"步骤时留下了紧随其后的 `if errorlevel 1`，而它读到了上一条 `find` 的退出码 1，于是**照样报了 backup_failed** |

**最后这条不是小事**，见 §五。

**GREEN**

| 检查 | 结果 | 日志 |
|---|---|---|
| `--filter …UpdateRecoveryTests` | **19 通过 / 0 失败** | `green/02-update-recovery-after.log` |
| `dotnet test`（全量） | **747 通过 / 0 失败 / 0 跳过**（59 s） | `green/03-csharp-full.log` |
| `node --test`（16 个 `.test.cjs`） | **216 通过 / 0 失败** | `green/04-node-full.log` |

**三个真实执行替换脚本的用例**（不是只断言字符串）：

| 场景 | 实测输出 |
|---|---|
| 正常路径 | `state=applied target=NEW-CONTENT backup=OLD-CONTENT`（旧版本留在备份里，可回退） |
| 备份失败（备份路径在不存在目录里） | `state=backup_failed target=OLD-CONTENT`（什么都没动） |
| 替换失败（把"新包"做成目录） | `state=replace_failed target=OLD-CONTENT backup=OLD-CONTENT`（**已回滚**） |

---

## 五、本轮最重要的发现（由 RED 意外暴露）

RED 里 `backup_failed` 用例"通过"的原因，是 `if errorlevel 1` **读的是紧邻上一条命令的退出码**。
我把 `copy` 那一行删掉后，检查读到了上一条 `find` 的退出码 1，于是误报 `backup_failed`。

**这暴露了生成脚本的真实脆弱点**：任何人在 `copy` 与 `if errorlevel` 之间插入一条命令，
判断就会读错退出码 —— 而这是"更新失败却显示成功"或反之的**静默错误**。

已加固为把退出码**立刻取出来**：

```bat
copy /Y "%TARGET%" "%BACKUP%" >nul
set "RC=%errorlevel%"
if not "%RC%"=="0" ( ... )
```

三处检查全部改为该形式，并在测试里用三个真实执行用例覆盖（加固后仍然 19/19）。

---

## 六、明确未做与未验证

| 项 | 说明 |
|---|---|
| 🔴 **真实安装目录替换** | 未做。卡片明确："应用更新到真实安装目录另需具体授权"。本包只在临时目录里真实执行了替换脚本。 |
| 🔴 **真实重启** | 未做：测试用**空操作**代替 `start` 命令（脚本其余部分与产品生成的完全一致）。 |
| **Unix 分支未真实执行** | `.sh` 分支结构相同（`cmp -s` + 回滚），但本机是 Windows，**只在 Windows 上真实跑了**；macOS/Linux 分支仅做结构断言。 |
| **UI 未接线** | ✅ **已在第 17 轮补上**（见 §九）：界面会展示"上次替换未成功"与信任链声明，并配套 `update-recovery.test.cjs`。 |
| 断电/杀进程中断 | "替换到一半"由 `fc /b` 逐字节校验覆盖逻辑，但**没有真的模拟断电**。 |
| 签名清单 / 身份认证 | **未实现**（本包只做准确的声明）。卡片要求"外部签名凭据不在本包自动申请或使用"，故未引入任何签名凭据。 |
| 旧配置/凭据不被重置 | 未改动配置路径；但没有专门的"更新前后配置不变"用例。 |
| 主备切换留证 | 下载的主/备回退逻辑本包未改动，也未新增用例。 |

---

## 七、回退方法

```powershell
$run = 'C:\Users\Administrator\.codex\worktrees\ed41\松光启动器\docs\market-execution\MR20-self-update-recovery\20261003-080000Z'
Copy-Item "$run\green\UpdateService.cs.mr20" `
  'D:\松光启动器\dev\PineglowLauncher\Pineglow.Launcher.Services\UpdateService.cs' -Force
(Get-Item 'D:\松光启动器\dev\PineglowLauncher\Pineglow.Launcher.Services\UpdateService.cs').LastWriteTime = Get-Date
```

其余：删除新增的 `UpdateRecoveryTests.cs`；`LocalApiService.cs` 去掉 `trustNote`/`lastApplyResult`
两行与 `ApplyUpdate` 的 `version` 实参；白名单去掉类别 B 的新增行（31→30）。

**注意**：`UpdateService.cs` 与 `LocalApiService.cs` 会话前**无 BOM**（与会话前快照一致），回退后请保持。

---

## 八、证据清单

```
red/01-update-recovery-before.log        RED 基线 6 失败 / 13 通过
green/
  UpdateService.cs.mr20                  最终实现版备份（SHA F050172E…）
  UpdateService.cs.mr20.sha256.txt
  01-update-recovery-first.log           首次运行 19 通过 / 0 失败
  02-update-recovery-after.log           加固后 19 通过 / 0 失败
  03-csharp-full.log                     747 通过 / 0 失败
  04-node-full.log                       216 通过 / 0 失败
  git-status-after.txt                   1325 行
  changed-files.sha256.txt
manifest.json
```

**本报告不宣布阶段放行、不提交、不发布。**

---

## 九、收尾追加（第 17 轮：UI 接线）

### 9.1 为什么必须补

替换脚本是**退出启动器之后**才跑的。界面当时只能知道"脚本已启动"——如果脚本失败了
（`backup_failed` / `replace_failed` / `verify_failed`），用户下次打开启动器看到的**仍是旧版本**，
却会以为更新完成了。卡片明确要求"apply 成功必须可验证，不根据'脚本已启动'就显示更新完成"，
而后端已经写下 `apply-state.json`，缺的就是把它**显示给用户**这一步。

### 9.2 改动

| 文件 | 改动 |
|---|---|
| `ui/js/api.js` | `getUpdateStatus()` 显式归一 `trustNote` / `lastApplyResult`（服务端没给也不崩） |
| `ui/js/launcher.js` | 新增 `renderUpdateTrust(el, s)`；`initUpdateSection` 初始化时调用；设置页新增 `#updateTrust` 容器 |
| `ui/js/i18n.js` | 中英各 4 个键 |
| `ui/tests/update-recovery.test.cjs` | **新增**（正是计划里 MR20 期望的文件名） |

展示规则（有意克制）：
- **只在 `applied === false` 时**显示告警"上次替换未成功（{结果码}）· 目标版本 {v}"，
  并说明"当前仍是旧版本；旧版本已备份，可手动回退"——成功时**不打扰**用户；
- 信任链声明来自服务端（`s.trustNote`），**不在前端硬编码**，避免两处说法漂移。

### 9.3 收尾证据

| 检查 | 结果 | 日志 |
|---|---|---|
| `node --test ui/tests/update-recovery.test.cjs` | **5 通过 / 0 失败** | `green/05-node-update-recovery.log` |
| `dotnet test`（全量） | **747 通过 / 0 失败** | `green/06-csharp-full-final.log` |
| `node --test`（17 个 `.test.cjs`） | **221 通过 / 0 失败** | `green/07-node-full-final.log` |

`git status` 1325 → 1326。

### 9.4 收尾后仍未验证

- 真实 WebView2 里的设置页渲染与告警显示未验证（只做接线级断言与静态扫描）；
- 真实替换脚本执行后的"下次启动读取 `apply-state.json`"整条链路，需要真实安装目录替换（需授权）。
