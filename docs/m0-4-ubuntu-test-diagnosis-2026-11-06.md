# M0-4 Ubuntu Node/.NET 测试失败定位

> 日期：2026-11-06 | 基线：`30f7b56e`（v1.0.3 源码） | 状态：**根因已缩小到候选范围，未在 runner 复测**

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

## 四、为什么这次**不**直接改测试

`docs/superpowers/plans/2026-11-04-v1.1.0-main-update-plan.md` 与总控计划的完成定义都写明「**禁止**通过放宽超时阈值、删除测试、修改工作流绕过失败」。

给 5 个用例加 `if (!OperatingSystem.IsWindows()) return;` 表面上合理（它们测的确实是 Windows 专有行为），但**等于把 5 个用例在三平台中最能暴露问题的平台上永久跳过**，属于需要 owner 明确决策的取舍，不是我能单方面做的。三个选项：

| 方案 | 做法 | 代价 |
|---|---|---|
| A. 加平台守卫 | 5 个用例在非 Windows 直接 return，并用 `Assert.Skip` 或等价方式**明确记为跳过**而非静默通过 | Ubuntu 上这 5 项永久不再验证；需在 coverage 说明里登记 |
| B. 改用跨平台替身进程 | 把 `cmd.exe /c exit` 换成 `Process.Start` 一个跨平台即可运行的东西（如测试自身带 `--noop` 参数，或 `/bin/sh -c exit`），junction 用例改用 `ln -s` 分支 | 真正恢复三平台覆盖，但要改写 5 个用例且需确认替身在 Windows 也可靠 |
| C. 拆分作业 | Ubuntu 作业只跑与平台无关的测试子集（`--filter`），Windows 作业跑全量 | 改动工作流本身，风险最高，且总控计划禁止「放宽工作流」 |

**推荐 B**：它是唯一既保留三平台覆盖、又不放宽门槛的方案。A 次之（可接受但必须显式记跳过）。C 不建议。

## 五、下一步（需要 runner）

1. 拿到 Ubuntu 失败日志（需要仓库 admin 权限，或 owner 手动导出 Artifact `build-verify-evidence`）。
2. 在隔离分支按方案 B 改写 5 个用例，本地 Windows 先验 967/967 不退步。
3. 推送到临时分支触发 `java-discovery-check.yml` 或 build.yml，确认 Ubuntu 转绿。
4. 结论回填本文件，并把「具体失败测试名」替换掉第三节的假设。

## 六、本地可复现部分（已做）

| 检验 | 结果 |
|---|---|
| Windows 全量 .NET（隔离分支 `remediation/v1.1.0`） | **967/967 通过**，0 跳过（前提：PATH 里 `C:\Windows\System32` 优先，否则 `tasklist`/`find` 会被 Git Bash 的同名工具遮蔽，见 M0-3 文档） |
| 5 个无守卫用例在 Windows | 全部通过（说明它们测的确实是 Windows 行为） |
| Linux | **本机无 WSL（被安全策略禁用）、无 Docker，无法本地复现** |
