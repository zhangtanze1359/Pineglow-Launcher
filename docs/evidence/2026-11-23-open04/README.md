# OPEN-04 复跑证据 · 2026-11-23

对照 [docs/open-items.md](../../open-items.md) OPEN-04「复跑更新替换脚本的历史偶发失败」执行。执行者记录；`FormalPhaseApproval=NOT_GRANTED`，未推送、未打 tag。

## 结论

**未复现**历史偶发失败。分层复跑 4 层，**日志可复算的完整计数为 9107 次通过**，**0 失败、0 跳过**；其中**全量套件 6 轮**（常规 3 + 压力 3），每轮 1502/1502。

但按 open-items 销项条件「**未复现但由独立复核明确接受已有覆盖与风险说明**」——本轮只交付覆盖范围与风险说明，**不写成「缺陷已修复」**，销项需独立复核接受。

> **更正记录（2026-11-23，第 2 次独立复核后）**：本文件早前写「9 次全量」「4517 次用例执行」，均**无法由日志复算**。实际日志只有 **6 轮全量**（`open04-layer3-full.txt` 3 轮 + `open04-layer4-stress.txt` 3 轮），全量用例 **9012**（1502 × 6）。加上第 1、2 层（10 + 45 + 40 = 95）得 **9107**。早前的错误来自把非全量轮次误计为全量轮次。

### 复跑总量总览（全部可由 `raw/` 日志复算）

| 层 | 轮次 | 每轮用例数 | 用例合计 | 结果 |
| --- | --- | --- | --- | --- |
| 1（单跑目标用例） | 10 | 1 | 10 | 10/10 通过，exit 0 |
| 2a（`UpdateApplyScriptPathTests`） | 5 | 9 | 45 | 45/45 通过，exit 0 |
| 2b（`UpdateConcurrencyTests`） | 5 | 8 | 40 | 40/40 通过，exit 0 |
| 3（全量，默认并行） | 3 | 1502 | 4506 | 4506/4506 通过，exit 0 |
| 4（全量 + CPU/IO 压力） | 3 | 1502 | 4506 | 4506/4506 通过，exit 0 |
| **合计（有完整计数的部分）** | **26** | — | **9107** | **9107 通过 / 0 失败 / 0 跳过** |

**另有 8 次时序采集运行**：日志只记录 `exit=0`、`state`、`waited`，**未记录 xUnit 的 passed/failed/skipped/total**（`passed=`、`failed=` 字段为空）。故**不并入上表计数**，单独作为时序证据（见「时序证据」一节）。

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

无压力时每轮 72.3 / 73.9 / 72.2 s（均值 **72.80**），压力下 83.9 / 84.7 / 84.8 s（均值 **84.47**），增幅 **16.03%**（早前写「约 17%」系单轮近似）。

**关于「压力是否真的生效」**：耗时差与额外负载相符，但**单凭耗时不足以确认两类负载全程持续运行**。本轮未采集压力期间的 CPU/IO 遥测，故该点列为**未充分证明**——第 2 次独立复核补充了该项遥测（8 个计算线程各累计约 77 s CPU、IO 线程完成 ≥19,839 轮 8 MiB 读写），可作外部佐证。

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

**计数说明**：该日志每行只记录 `exit=0`、`state`、`waited`；`passed=`/`failed=` 字段**为空**，也没有 `skipped`/`total`。因此这 8 次的 **xUnit 通过数无法从该日志核定**，只能确认「8 次进程退出码均为 0，且状态字段均为 `applied`」。若把这 8 次各计一次，名义执行为 9115 次，但**不作为已核定的计数**。

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

1. **CI 平台未验证**：`.github/workflows/build.yml:121` 在 Windows 与 Ubuntu 上跑全量。Ubuntu 上本类**6 条真实 `.bat` 用例**（含历史目标用例）因 `OperatingSystem.IsWindows()` 为 false 而**提前 `return`**，xUnit 会把这种正常返回**显示为通过**（AGENTS.md 规则 3 禁止的做法，属既有代码）；其余 **3 条纯文本断言**照常执行（它们与平台无关）。因此 **Ubuntu 的绿色结果不能为本轮的 Windows 偶发问题增加复跑样本**。
2. **未在 4 逻辑核 / 共享 CI runner 形态的压力下复跑**：本轮压力最高约 8 核忙循环。
3. **未只做产品二进制形态的实包更新**：本轮是测试夹具直接执行生成的 `.bat`，不是真实启动器的自更新链路。
4. **未复现即不等于不存在**：本轮 4 层全过只能说明在当前机器、当前负载下不可复现。

## 结构性风险（未修，供独立复核判断）

`UpdateApplyScriptPathTests` 未加入 `DisableParallelization` 集合（与 `UpdateConcurrencyTests` 不同，后者标注了 `[Collection("LocalApiService")]`）。两处夹具在 `WaitForExit(30000)` 超时后**忽略返回值、也不校验子进程退出码**，直接读文件与状态。走该路径的用例共 **11 个**：路径类 6 个（`RunApplyScript(` 调用点 103/140/173/247/289/344）+ 恢复类 5 个（`RunApplyScript(` 3 处 343/369/394 与 `RunApplyScriptCore(` 直接调用 2 处 446/472）。

**注意**（按第 2 次独立复核更正）：该夹具**没有预写 `applying`**，所以「提前读到 `applying` 中间态」的推演**不成立**——若脚本未写完，读到的是**缺失或不可解析**的状态。超时静默继续这一结构缺陷确实存在，但**历史失败是否由这条路径引发仍属假说**，本轮未观测到该分支。残余风险表述为：同步管道阻塞、等待返回值被忽略、调度竞争。

**本轮未修改任何产品代码或测试代码**——是否加超时余量或把该用例纳入串行集合，由独立复核与编排方决定。

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
