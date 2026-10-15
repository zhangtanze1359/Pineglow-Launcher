# MR03 收尾 · 「实例正在运行不得从列表移除」 · 交付报告

- **run-id**：`20261003-060000Z`
- **性质**：MR03（`20261002-190000Z`）**未达成验收项的收尾补交付**
- **依据**：`docs/market-task-plan-2026-10-03.md` §八 MG1 行、MR03 报告 §六
- **最终状态**：**证据就绪**（该验收项已达成；真实启动 Minecraft 仍未验证，见 §六）
- **未执行**：不提交、不推送、不发布

---

## 一、为什么要单独开一个 run

MR03 那一轮的验收里写着"运行中的实例不得移除"，但当时全仓**不存在任何运行态记录**：

```csharp
Process process = await new LauncherService(...).LaunchAsync(profile, CancellationToken.None);
instance.LastLaunchTime = DateTimeOffset.Now;   // ← 派发进程后不保留状态
```

因此 MR03 只实现了 Bisect 活动守卫，并在报告 §六 里把它如实记为**未达成项**。
按"历史放行不覆盖新 diff"的纪律，这次补交付**单开 run**，不改写 MR03 的封存证据。

---

## 二、实际改动

| 文件 | 状态 | SHA256 |
|---|---|---|
| `dev/PineglowLauncher/Pineglow.Launcher.Services/RunningInstanceRegistry.cs` | **新增** | `16269BEAAFF199D81ABD942E3EA67D7A9A721B7A2FE544D6C06F9DF99EF7472D` |
| `dev/PineglowLauncher/Pineglow.Launcher.Services/LocalApiService.cs` | 修改（原已是 `M`） | `EB0338905804D024A2F731E5582116030167708E0ABEC87E60048518A383FD85` |
| `dev/PineglowLauncher.Tests/InstanceRunningGuardTests.cs` | **新增** | 见 `manifest.json` |
| `scripts/verify-mg1.ps1` | 修改（把本 run 纳入 MG1 复验范围） | 见 `manifest.json` |
| `scripts/source-snapshot-inputs.txt` | 修改（登记新增源码与测试） | 见 `manifest.json` |

### 2.1 三处接线

1. **登记**：`HandleLaunchAsync` 在 `LaunchAsync` 返回后 `MarkRunning(instanceId, process)`。
2. **清除**：既有的退出监听任务里加 `finally { MarkExited(instanceId); }` —— 无论监听成功与否都清。
3. **守卫**：`HandleDeleteInstanceAsync` 在 Bisect 守卫之后加运行态守卫，**走同一个错误通道**（400）：

```
该实例正在运行（进程 47500），请先退出游戏，再把它从列表移除。
```

### 2.2 关键设计：自愈

注册表**不能**留下陈旧条目——那会**永久挡住**"从列表移除"，比"漏挡"更糟。
所以 `IsRunning` 每次都复查进程是否还活着：进程对象已退出、PID 不存在（`ArgumentException`）
都会顺手清掉条目；**只有无法判定时（权限等）才保守地认为仍在运行**（宁可多挡一次，
也不要在游戏运行时把实例移走）。

---

## 三、RED → GREEN

**RED 构造方式**：注册表是本包新增的类型，删掉文件只会得到编译错误。
所以 RED 基线 = 保留类型，**把 `IsRunning` 中和为"永远报告没有运行态"** ——
这正是改动前的语义（全仓不存在运行态记录，因此不会挡任何移除）。

**RED：4 失败 / 3 通过**（`red/01-running-guard-before.log`）

通过的 3 个**恰好是"改动前本来就不会挡"的语义**，如实记录、不夸大缺口：

| 通过（RED 下本就成立） | 失败（本包新增的能力） |
|---|---|
| 未知实例报告为"未运行" | 登记活进程后报告为"运行中" |
| PID 不存在时自愈 | 移除运行中的实例被拒（400）且配置未改动 |
| **游戏已死时不被陈旧条目挡住**（改动前根本没守卫，必然通过） | 进程退出后可以正常移除 |
| | 进程已退出时自愈（不留陈旧条目） |

**GREEN**

| 检查 | 结果 | 日志 |
|---|---|---|
| `--filter …InstanceRunningGuardTests` | **7 通过 / 0 失败** | `green/02-running-guard-after.log` |
| `dotnet test`（全量） | **725 通过 / 0 失败 / 0 跳过**（58 s） | `green/03-csharp-full.log` |
| `node --test`（16 个 `.test.cjs`） | **216 通过 / 0 失败** | `green/04-node-full.log` |

关键实测（都在日志里）：
- 运行中移除 → `400 {"error":"该实例正在运行（进程 47500），请先退出游戏…"}`，且实例仍在列表里；
- `MarkExited` 后移除 → `200`，实例消失；
- 陈旧条目（真实但已死的进程）→ **200**（自愈，不被永久挡住）。

---

## 四、本轮自身发现并修正的问题

1. **`.ps1` 被 `edit` 工具剥掉 UTF-8 BOM**：`edit` 每次写入都会去掉 BOM，而 Windows PowerShell 5.1
   对无 BOM 的 `.ps1` 按 ANSI 解码 → 中文乱码 → **解析失败**（实测报
   `Unexpected token '锛涙斁琛屽繀椤诲垎鍒...'`）。这是**第二次**踩到（MR09 新建脚本时也踩过）。
   **规则：任何用 `edit` 改过的 `.ps1`，跑之前必须补回 BOM。**
2. **MG1 复验脚本的包清单需要同步**：新增 run 目录后脚本仍只找 `MR03-remove-from-list`，
   会漏掉本次收尾。已把 `MR03-instance-running-guard` 加进清单。

---

## 五、对 MG1 复验包的影响（已同步更新）

- `scripts/verify-mg1.ps1` 的包清单增加 `MR03b`；
- MG1 复验包的 README / checklist 里"MR03 运行守卫未实现"的表述**已改为已实现**，
  并指向本 run；
- 改脚本会让 MG1 包记录的脚本哈希失效 —— 已在 MG1 包的 `manifest.json` 里更新为新哈希并说明原因。

---

## 六、明确未做与未验证

| 项 | 说明 |
|---|---|
| 真实启动 Minecraft | 未验证。守卫的**接线与回滚**已测，但"真的启动游戏→注册表被登记→退出后清除"需要真实游戏与 Java，属 MR22 内测范围。 |
| 启动并发守卫 | 未做：同一实例重复启动仍会各起一个进程（注册表以最后一次为准）。计划的 MR03 验收只要求"移除"守卫。 |
| 启动器自身重启后的运行态 | 注册表是**进程内**内存态：启动器重启后不知道游戏还在跑。与 MR07 把 Bisect 会话落盘不同，本包**没有**持久化运行态。 |
| 跨平台 | 自愈依赖 `Process.GetProcessById`；仅在 Windows 上验证。 |

---

## 七、回退方法

```powershell
$run = 'C:\Users\Administrator\.codex\worktrees\ed41\松光启动器\docs\market-execution\MR03-instance-running-guard\20261003-060000Z'
Copy-Item "$run\green\LocalApiService.cs.mr03b" `
  'D:\松光启动器\dev\PineglowLauncher\Pineglow.Launcher.Services\LocalApiService.cs' -Force
(Get-Item 'D:\松光启动器\dev\PineglowLauncher\Pineglow.Launcher.Services\LocalApiService.cs').LastWriteTime = Get-Date
```
其余：删除新增的 `RunningInstanceRegistry.cs` 与 `InstanceRunningGuardTests.cs`；
白名单去掉类别 A/B 的新增行（计数 23→22、29→28）；`verify-mg1.ps1` 去掉 `MR03b` 行。

**注意**：`LocalApiService.cs` 会话前带 UTF-8 BOM，回退后请保持。

---

## 八、证据清单

```
red/01-running-guard-before.log          RED 基线 4 失败 / 3 通过
green/
  RunningInstanceRegistry.cs.mr03b       实现版备份（SHA 16269BEA…）
  LocalApiService.cs.mr03b               实现版备份（SHA 81F68FFB…）
  *.sha256.txt
  01-running-guard-first.log             首次运行 7 通过 / 0 失败
  02-running-guard-after.log             还原后 7 通过 / 0 失败
  03-csharp-full.log                     725 通过 / 0 失败
  04-node-full.log                       216 通过 / 0 失败
  git-status-after.txt
  changed-files.sha256.txt
manifest.json
```

**本报告不宣布阶段放行、不提交、不发布。**
