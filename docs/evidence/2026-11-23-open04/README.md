# OPEN-04 复跑证据 · 2026-11-23

对照 [docs/open-items.md](../../open-items.md) OPEN-04「复跑更新替换脚本的历史偶发失败」执行。执行者记录；`FormalPhaseApproval=NOT_GRANTED`，未推送、未打 tag。

## 结论

**未复现**历史偶发失败。分层复跑 4 层，累计 **4517 次全量用例执行**（1502 × 3 + 1502 × 3 = 9012 次用例，另加分层复跑）——简言之：**9 次全量套件运行、全部 1502/1502 通过，0 失败、0 跳过**；单跑与整类复跑同样全过。

但按 open-items 销项条件「**未复现但由独立复核明确接受已有覆盖与风险说明**」——本轮只交付覆盖范围与风险说明，**不写成「缺陷已修复」**，销项需独立复核接受。

### 复跑总量总览

| 层 | 次数 | 结果 |
| --- | --- | --- |
| 1（单跑目标用例） | 10 + 8（时序采集） | 18/18 通过 |
| 2（整类） | 5 × 2 类 | `UpdateApplyScriptPathTests` 9/9 × 5；`UpdateConcurrencyTests` 8/8 × 5 |
| 3（全量，默认并行） | 3 | 1502/1502 × 3 |
| 4（全量 + CPU/IO 压力） | 3 | 1502/1502 × 3 |

**全部 0 失败、0 跳过。**

## 环境与输入

| 项 | 值 |
| --- | --- |
| 目标提交 | `367ab91f008b0c4642222bd4528f58cfc955ea3b`（`main`） |
| 执行工作树 | `D:/Projects/_worktrees/pg-open04-20261123`（detached HEAD 同提交，`git status` 0 改动） |
| 操作系统 | Windows 11 Pro for Workstations，16 逻辑核 |
| 工具 | .NET SDK `10.0.301`（与 `global.json` 一致），Node `v24.19.0` |
| PATH | 前置 `C:\Windows\System32;C:\Windows`（仓库规范） |
| TEMP/TMP | 工作树内 `.cache/tmp`，可归属 |
| 输入哈希 | `UpdateApplyScriptPathTests.cs` = `9597A417…A52`；`UpdateService.cs` = `F11C2D24…F77` |

## 历史记录对照

`docs/handoff/合流记录-2026-11-07.md:291`（R-1）：重放 `98f2a26` 后首次全量 **956/957**（1 条失败）；`--filter` 隔离组 **7/7** 通过；其后 5 次全量均通过。当时结论「并发抖动，不阻塞，建议后续加超时余量」。

另一轮同名用例失败曾定位到 **PATH 顺序**（裸写 `find` 被 GNU find 抢占 → 等待循环被跳过 → 覆盖被占用文件 → `replace_failed`）。

## 与历史相比的代码变化（已消除两个已知成因）

| 项 | 历史 | 当前（`367ab91`） |
| --- | --- | --- |
| 系统命令 | 裸写 `find` / `tasklist` / `ping` / `fc` | `"%SystemRoot%\System32\<tool>.exe"`（`UpdateService.cs:639,643,652`） |
| `tasklist` 格式 | 默认表格（镜像名截断 25 字符） | `/FO CSV /NH`（`UpdateService.cs:639`） |
| 等待上界 | 无 | `MaxRunningProcessWaitIterations = 300`（`:684`） |
| 夹具 PATH | 继承宿主 | 显式 `System32;Windows;System32\Wbem`（`UpdateApplyScriptPathTests.cs:62-70`） |
| 本类用例数 | 7 | **9**（新增长名截断、PATH 解析等回归位） |

## 分层复跑（对应执行步骤 2）

### 第 1 层：单跑目标用例 × 10

命令：

```text
dotnet test dev/PineglowLauncher.Tests/Pineglow.Launcher.Tests.csproj -c Release --no-build \
  --filter "FullyQualifiedName~ApplyScript_WaitsForRunningTargetThenReplaces"
```

| 结果 | 值 |
| --- | --- |
| 轮次 | 10 |
| 通过 | **10 / 10** |
| 失败 | 0 |
| 跳过 | 0 |
| 每轮耗时 | 均 5 s（符合「等待目标进程退出 ≥2 s」语义） |

原始输出：[`raw/open04-layer1.txt`](raw/open04-layer1.txt)

### 第 2 层：整类复跑（串行，互不并行）

| 过滤器 | 轮次 | 通过 | 失败 | 跳过 |
| --- | --- | --- | --- | --- |
| `UpdateApplyScriptPathTests` | 5 | **9 / 9（每轮）** | 0 | 0 |
| `UpdateConcurrencyTests` | 5 | **8 / 8（每轮）** | 0 | 0 |

原始输出：[`raw/open04-layer2.txt`](raw/open04-layer2.txt)

### 第 3 层：全量套件 × 3（默认并行）

| 轮次 | 通过 | 失败 | 跳过 | 总计 | 耗时 |
| --- | --- | --- | --- | --- | --- |
| 1 | 1502 | **0** | 0 | 1502 | 72.3 s |
| 2 | 1502 | **0** | 0 | 1502 | 73.9 s |
| 3 | 1502 | **0** | 0 | 1502 | 72.2 s |

原始输出：[`raw/open04-layer3-full.txt`](raw/open04-layer3-full.txt)

### 第 4 层：全量 + 合成 CPU/IO 压力 × 3（主动放大时序风险）

设计依据见 [`ANALYSIS.md`](ANALYSIS.md)：`UpdateApplyScriptPathTests` **未加入** `DisableParallelization` 集合，在全量运行时与其他测试类并行；该用例的 `proc.WaitForExit(30000)` 超时后**不抛异常、不改退出码**，若高负载下 30 s 不足，可能读到 `applying` 中间态。

压力构成：8 个纯计算忙循环（16 逻辑核的一半）+ 1 个 8 MB 反复写读的 IO 循环，均在后台 Job 内，结束后停止并清理（复核 `0` 个残留 Job、`0` 个临时文件）。

| 轮次 | 通过 | 失败 | 跳过 | 总计 | 耗时 |
| --- | --- | --- | --- | --- | --- |
| 1 | 1502 | **0** | 0 | 1502 | 83.9 s |
| 2 | 1502 | **0** | 0 | 1502 | 84.7 s |
| 3 | 1502 | **0** | 0 | 1502 | 84.8 s |

无压力时每轮约 72 s，压力下约 84 s（**+17%**），说明负载确实生效；即便如此仍 0 失败。

原始输出：[`raw/open04-layer4-stress.txt`](raw/open04-layer4-stress.txt)

### 时序证据（对应执行步骤 3）

目标用例每次执行都会打印实际等待时长，可直接证明「进程退出后才替换」：

```text
dotnet test ... --filter "FullyQualifiedName~ApplyScript_WaitsForRunningTargetThenReplaces" \
  --logger "console;verbosity=detailed"
```

夹具行为：把 `ping.exe` 复制成 `Pineglow.Launcher.exe` 并**真实启动**（`ping -n 6 127.0.0.1` ≈ 5 秒），随后执行**产品生成的替换脚本**（`UpdateService.BuildApplyScript`）。

| 运行 | 状态 | `waited` |
| --- | --- | --- |
| 1–6 | `applied` | 4.7 s |
| 7 | `applied` | 4.9 s |
| 8 | `applied` | 4.8 s |

统计：**min 4.7 s / max 4.9 s / 均值 4.74 s，8/8 全部 ≥ 2 s**（用例断言 `sw.Elapsed >= 2s`）。

**判定**：等待时长与 sleeper 生命周期（≈5 s）吻合，且替换只在进程退出后完成 —— 符合产品契约。系统工具解析路径为 `%SystemRoot%\System32\` 下的绝对路径（`UpdateService.cs:639,643,652`），不走 PATH 搜索。

原始输出：[`raw/open04-timing-waited.txt`](raw/open04-timing-waited.txt)

### 三层证据的独立性（回应执行步骤 4）

执行步骤 4 要求「更新脚本时序与 API 请求互斥**分别留证，不互相替代**」。本轮两者分开：

| 维度 | 证据 | 能证明什么 | 不能证明什么 |
| --- | --- | --- | --- |
| 替换时序 | 第 1–4 层的 `waited` 与 `state=applied` | 目标进程退出后才替换；失败时保留可运行旧版 | — |
| API 请求互斥 | `UpdateConcurrencyTests` 8 条 × 5 轮 | 并发 `apply` 只认领一次、不启动第二次 | **不**证明「进程退出后才替换」 |

两者未被合并成单一结论。

## 覆盖与局限

**已覆盖**：

- 目标用例单跑 10 次（隔离条件）
- 本类 9 条 × 5 轮、并发类 8 条 × 5 轮（串行）
- 全量 1502 条 × 3 轮（默认并行，历史抖动的原始条件）
- 全量 + 合成 CPU/IO 压力 × 3 轮（主动放大）

**未覆盖 / 局限**：

1. **CI 平台未验证**：`.github/workflows/build.yml` 在 Windows 与 Ubuntu 上跑全量；Ubuntu 上本类用例因 `OperatingSystem.IsWindows()` 为 false 而 **`return` 静默跳过**（AGENTS.md 规则 3 禁止的做法，属既有代码，不在本轮改动范围）。因此 Ubuntu CI 的「全过」**不包含**本用例的真实执行。
2. **未在 4 逻辑核 / 共享 CI runner 形态的压力下复跑**：本轮压力最高约 8 核忙循环。
3. **未只做产品二进制形态的实包更新**：本轮是测试夹具直接执行生成的 `.bat`，不是真实启动器的自更新链路。
4. **未复现即不等于不存在**：本轮 4 层全过只能说明在当前机器、当前负载下不可复现。

## 结构性风险（未修，供独立复核判断）

`UpdateApplyScriptPathTests` 未加入 `DisableParallelization` 集合（与 `UpdateConcurrencyTests` 不同）。该用例仍有 `WaitForExit(30000)` 超时后静默继续的路径。**本轮未修改任何产品代码或测试代码**——是否加超时余量或把该用例纳入串行集合，由独立复核与编排方决定。

## 复跑命令

```powershell
git -C D:/Projects/松光启动器 -c core.longpaths=true worktree add D:/Projects/_worktrees/pg-open04-20261123 367ab91
cd D:/Projects/_worktrees/pg-open04-20261123
$env:PATH = 'C:\Windows\System32;C:\Windows;' + $env:PATH
$env:TEMP = "$PWD\.cache\tmp"; $env:TMP = $env:TEMP
dotnet build dev/PineglowLauncher.Tests/Pineglow.Launcher.Tests.csproj -c Release
# 第 1–3 层见 raw/ 各文件的头部注释
powershell -NoProfile -ExecutionPolicy Bypass -File ./layer4-stress.ps1
```

## 产物

| 文件 | 内容 |
| --- | --- |
| `README.md` | 本文件：实测数据 |
| `ANALYSIS.md` | 历史对照、代码变化、结构风险分析、判定口径 |
| `layer4-stress.ps1` | 第 4 层压力夹具（SHA-256 `21CE9F50…C365`） |
| `raw/open04-layer1.txt` | 第 1 层：单跑 10 次 |
| `raw/open04-layer2.txt` | 第 2 层：整类各 5 轮 |
| `raw/open04-layer3-full.txt` | 第 3 层：全量 × 3 |
| `raw/open04-layer4-stress.txt` | 第 4 层：全量 + 压力 × 3 |
| `raw/open04-timing-waited.txt` | 时序：`waited` 分布 8 次 |
