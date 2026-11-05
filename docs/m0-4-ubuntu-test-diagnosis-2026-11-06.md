# M0-4 Ubuntu Node/.NET 测试失败定位

> 日期：2026-11-06 | 基线：`30f7b56e`（v1.0.3 源码）
> 状态：**根因已定位并按方案 B 完成改造；本地 967/967 + 380/380 通过；runner 复测待推送**
> 改造实现见 `m0-consolidated-report-2026-11-06.md` 第一节与提交 `98f2a26`、`7212793`。

## 一、先纠正一处过时结论

11-03 诊断文档写：「另有 main 构建的 Ubuntu Node 测试失败，位于 `scripts/tests/goal-validation.test.cjs`，进程未启动产生 null 退出状态。」

**这个结论已经过时。** 核实两次最近一次 main 构建：

| run | 提交 | Ubuntu 失败步骤 |
|---|---|---|
| 41598910035 | `724fed50` | 第 11 步 **Run tests**（dotnet test） |
| 41599393945 | `30f7b56e` | 第 11 步 **Run tests**（dotnet test） |

两次的第 10 步「Run Node front-end tests (from inventory)」**都是 success**。也就是说 `goal-validation.test.cjs` 的 Node 问题**已被修复**（修复提交 `e89023d` 改了该文件，`docs/release-fix-2026-11-03/validation.md` 也记载「harness 现在在 Unix 选 `pwsh`」）。

**当前 Ubuntu 失败发生在 .NET 测试阶段，不是 Node 阶段。** 把这两件事混在一起会导致修错地方。

## 二、已确认的事实

| 项 | 证据 |
|---|---|
| 失败步骤 | `Run tests` → `dotnet test dev/PineglowLauncher.Tests/Pineglow.Launcher.Tests.csproj -c Release` |
| 失败范围 | 仅 ubuntu-latest；同一次运行 Windows、macOS 作业均 success |
| 持续性 | `724fed50`、`30f7b56e` 两次都失败，非偶发 |
| 注释内容 | 仅 `Process completed with exit code 1.`＋10 条 xUnit analyzer warning，**没有具体失败测试名** |
| 日志 | `GET /actions/jobs/{id}/logs` 返回 **403 Must have admin rights**，匿名无法取 |

## 三、根因候选（按概率排序，均为**假设**，未经 runner 验证）

### 候选 1（最高）：测试直接调用 `cmd.exe`，Linux 上没有这个可执行文件

静态扫描 `dev/PineglowLauncher.Tests/*.cs`：**41 个用例**涉及 Windows 外部命令，其中 **5 个没有平台守卫**。

| 文件 | 行 | 用例 | 问题 |
|---|---|---|---|
| `FileRecoveryEdgeCaseTests.cs` | 91 | `Quarantine_RejectsFileReachedThroughJunctionAncestor` | `TryCreateJunction` 用 `cmd.exe /c mklink /J`，并在 `Assert.True(created, ...)` 断言必须成功 |
| `InstanceRunningGuardTests.cs` | 89 | `Registry_SelfHeals_WhenTheProcessHasAlreadyExited` | `Process.Start("cmd.exe","/c exit")` |
| `InstanceRunningGuardTests.cs` | 111 | `Registry_SelfHeals_ForAPidThatDoesNotExist` | 同上 |
| `InstanceRunningGuardTests.cs` | 179 | `RemovingAnInstanceWhoseGameAlreadyDied_IsNotBlockedByAStaleEntry` | 同上 |
| `InstanceLaunchGuardTests.cs` | 113 | `Launch_AfterTheProcessExited_IsNotBlockedByTheGuard` | 经 `DeadPid()` → `cmd.exe` |

**为什么必然失败**：Linux 上 `Process.Start("cmd.exe")` 抛 `Win32Exception`（找不到文件），不是返回非零退出码。第一个用例更进一步——即使能执行，`mklink` 在 Linux 也不存在，`Assert.True(created, "本机应能创建目录联接")` 会硬失败。

**注意**：这 5 个用例**全部早于 v1.0.1**（`git cat-file -e 8845574:...` 均存在，`cmd.exe` 出现次数与当前相同）。所以这不是新代码引入的回归，而是**「Ubuntu 跑全量 dotnet test」这个门槛从一开始就与这批用例不兼容**。

### 候选 2：`Registry_ReportsARegisteredLiveProcessAsRunning` 等用 `Environment.ProcessId` 的用例

Linux 上 `Process.GetProcessById` 语义与 Windows 有差异（`/proc` 权限、僵尸进程），可能误判「已退出」。概率中等，但要先解决候选 1 才能判定。

### 候选 3：文件占用 / 只读目录语义差异

`FileShare.None` 在 Linux 上不产生独占锁，`UnwritableDirectoryTests` 用「把目录做成文件」的绕法虽然跨平台，但 `FileRecovery` 在 Linux 上的错误路径可能不同。概率中低。

## 四、已选定并实施：方案 B（2026-11-06 更新）

**已采纳方案 B**，不是给用例加平台守卫。决定性理由：xunit 2.9.3 **没有动态 skip**
（`Assert.Skip` 是 v3 才引入）。若改用 `if (!IsWindows()) return;`，那 5 个用例在 Linux 上
会**静默 return 并显示为通过**——测试是绿的，实际什么都没验。它们保的是
「拒绝隔离重解析点」「陈旧条目自愈」这类安全语义，在开发主力平台之外永久跳过比失败更危险。

实施：
- 新增 `CrossPlatformTestProcess.cs`：Windows `cmd.exe /c exit`，Unix `/bin/sh -c "exit 0"`，
  都返回「真实 PID + 已退出」，调用方断言不变。
- `TryCreateJunction` → `TryCreateDirectoryLink`：Windows 优先 `mklink /J`（免特权），
  失败退回 `Directory.CreateSymbolicLink`；Unix 直接 `Directory.CreateSymbolicLink`，
  .NET 会映射为 `FileAttributes.ReparsePoint`，与 Windows 判定口径一致。

本地验证：三项测试类 19/19，.NET 全量 967/967（0 跳过），前端 380/380。

三个方案的原始取舍记录保留在下文，供回溯。

| 方案 | 做法 | 代价 |
|---|---|---|
| A. 加平台守卫 | 非 Windows 直接 return，用 `Assert.Skip` 明确记为跳过 | xunit 2.9.3 不支持动态 skip → 会变成静默通过；Ubuntu 上 5 项永久不再验证 |
| **B. 跨平台替身（已采纳）** | 进程替换为平台自带 shell；junction 用例加 symlink 分支 | 需改写 5 个用例并确认替身在 Windows 也可靠（已验证） |
| C. 拆分作业 | Ubuntu 只跑平台无关子集（`--filter`） | 改动工作流本身，风险最高，且计划禁止「放宽工作流」 |

## 五、下一步（需要 runner）

1. 推送 `remediation/v1.1.0` 到远端（当前网络 `schannel: server closed abruptly`，且属对外动作需授权）。
2. 触发 `build.yml`，确认 Ubuntu 第 11 步 `Run tests` 转绿。
3. 若仍失败，用仓库 admin 权限导出 Artifact / 日志，按具体失败测试名继续定位。
4. 结论回填本文件。

## 六、本地可复现部分（已做）

| 检验 | 结果 |
|---|---|
| Windows 全量 .NET（隔离分支 `remediation/v1.1.0`） | **967/967 通过**，0 跳过（前提：PATH 里 `C:\Windows\System32` 优先，否则 `tasklist`/`find` 会被 Git Bash 的同名工具遮蔽，见 M0-3 文档） |
| 5 个改造后的用例在 Windows | 全部通过，且 `mklink /J` 分支实际被执行（`创建目录链接 = True ... 平台 Win32NT`） |
| 前端清单测试 | **380/380 通过** |
| Linux | **本机无 WSL（被安全策略禁用）、无 Docker，无法本地复现** |

## 七、附带修掉的另一处夹具缺陷（CL-05）

排查过程中发现 `scripts/tests/ui-resource-closure.test.cjs` 的 CL-05 也是假失败，
根因不在 Ubuntu 而在**本机 Node v22.22.2 + Windows 的 `fs.cpSync`**：
祖先目录含非 ASCII 时会另建一个 GBK 乱码目录（`松光 测试` → `鏉惧厜 娴嬭瘯`），
返回值不报错但目标目录 0 个文件。对照实验证据见 `m0-consolidated-report-2026-11-06.md` 第二节。
已在干净基线 `30f7b56e` 上复现，确认与本次改动无关。
