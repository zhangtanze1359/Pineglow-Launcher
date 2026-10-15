# MR00 · 本轮发现的缺陷与修复记录

- run-id：`20261002-072004Z`
- 原则：**不为了流程好看而吞掉失败。** 以下每条都附本轮原始证据路径。
- 处置边界：MR00/MR01 **只修验证工具**（授权范围 B，仅限 ExecutionRoot 副本），**不修产品代码**。

---

## 缺陷 1 🔴 `verify-source-snapshot.ps1` 步骤执行器「假通过」

### 现象

以 `-EvidenceDir` 指向含非 ASCII 字符的路径（本次为 `…\松光启动器\docs\market-execution\…`）运行时，
脚本 8 个步骤**全部**报 `exit=0 status=PASS`，且每步用时均为 **1.0s**。

### 决定性证据

1. **步骤日志一个都没生成。** 该次运行的 `EvidenceDir` 下只有
   `snapshot-manifest.sha256.txt` 与 `verify-summary.json`；
   `step1`…`step10` 的 `.log` **全部 MISSING**。
2. 控制台在每步前打出 `The filename, directory name, or volume label syntax is incorrect.`
3. 最终判定为 `FAIL`，唯一失败项是「单文件发布产物缺失」——因为 publish 根本没跑。
4. 最小复现（`logs/rootcause-repro/`）：

   | 条件 | `.cmd` 实际内容（ASCII 写出后） | ExitCode | 日志文件 |
   |---|---|---:|---|
   | 证据路径含 CJK | `... > "C:\...\?????\...\repro-cjk.log"` | **0** | **未生成** |
   | 证据路径纯 ASCII | `... > "C:\Users\...\Temp\repro-ascii.log"` | 0 | 生成，内容 `THIS_MARKER_RAN` |

### 根因

原脚本（约 `:373-379`）把 `$logPath` 直接写进以 `-Encoding ascii` 写出的临时 `.cmd`：

```powershell
Set-Content -LiteralPath $bat -Encoding ascii -Value (
    "@echo off`r`ncd /d `"$SnapshotDir`"`r`n$CommandLine > `"$logPath`" 2>&1`r`nexit /b %ERRORLEVEL%`r`n")
```

路径中的 `松光启动器` 被写成 `?????` → `cmd.exe` **整行解析失败并跳过该行** →
命令从未执行，而 `%ERRORLEVEL%` 仍为它进入该行之前的值 `0` → `exit /b 0`
→ 脚本读到的 `$proc.ExitCode` 为 0 → **记为 PASS**。

### 危害等级

**这是把"验证器自身故障"变成"验证通过"的缺陷。** 任何步骤（测试失败、构建失败、发布失败）
在该配置下都会被静默记为通过，且**没有任何警告**。

### 修复（仅 ExecutionRoot 副本）

补丁：`MR00/20261002-072004Z/outputs/verify-source-snapshot-mr00-fix.patch`

1. 临时 `.cmd` **只接触 `%TEMP%` 下的纯 ASCII 路径**，真实日志路径由 PowerShell 搬运；
2. **双护栏**，任何一条触发都按失败处理、**绝不记 PASS**：
   - 日志文件未生成 ⇒ 命令极可能未执行 ⇒ 记 `9001`；
   - `$null -eq $code` ⇒ 退出码不可读 ⇒ 记 `9002`；
3. `$null = $proc.Handle` 必须在 `WaitForExit()` **之前**取引用句柄，否则 `.ExitCode` 读不到；
4. 非 Windows 分支同样加护栏与句柄缓存。

SourceRoot 原件**未改写**（补丁前后 SHA 均为 `E3AAF257…EEBB3`）。

### 修复的验证（不是声明，是实测）

以**中文证据目录**重跑修复后的脚本（正是触发原缺陷的条件）：

| 步骤 | exitCode | status | 用时 |
|---|---:|---|---:|
| Node 前端正式测试 | 0 | PASS | 3.0s |
| restore launcher `--locked-mode` | 0 | PASS | 0.9s |
| restore tests `--locked-mode` | 0 | PASS | 0.9s |
| build launcher Release | 0 | PASS | 2.2s |
| **C# 测试全套** | 0 | PASS | **58.0s** |
| build VerifyHost | 0 | PASS | 2.1s |
| publish RID 锁定探测 | **1** | **KNOWN-UNLOCKED** | 0.8s |
| publish 单文件 | 0 | PASS | 6.4s |

**`finalVerdict = PASS`**；`trackedCopied=161`、`whitelistCopied=53`、`snapshotFileCount=214`、
`unexpectedFiles=0`、`binObjFound=0`、`knownUnlockedBoundaries=1`。
证据：`logs/fixed-script-run2-cjk-evidence/verify-summary.json` 与 `logs/fixed-script-console.txt`。

三项关键对比：

| | 修复前 | 修复后 |
|---|---|---|
| 步骤日志 | **0 个生成** | **8 个全部生成** |
| 退出码 | 全为假 `0` | **全部真实可读** |
| build / test 步骤 | 挂死在 `-Wait` | 2.2s / 58.0s 正常完成 |

> 第一次应用补丁时曾引入 `ExitCode` 读不到的回归（全部步骤判 FAIL）。
> **护栏方向正确**——它表现为「吵闹的失败」而不是「静默的假通过」，这正是加护栏的价值。
> 补上 `$null = $proc.Handle` 后复跑通过，两次运行证据均保留在
> `logs/fixed-script-run-cjk-evidence/`（回归）与 `logs/fixed-script-run2-cjk-evidence/`（修正后）。

---

## 缺陷 2 🟠 同上，「挂死」

### 现象

改用纯 ASCII 证据目录重跑，步骤 01~04 正常，但**卡在 04 build launcher**：
该步日志在 15:23:28 已完整写出（含"0 个错误 / 0 个警告"），脚本进程却持续阻塞，
`step7` 日志在 5 分钟内始终不出现。

### 决定性证据

| 证据 | 数值 |
|---|---|
| `VBCSCompiler.exe` 启动时间 | **15:23:24**（正是 build 步骤时刻） |
| 其父进程 PID 15680 | **已退出**（成为孤儿常驻服务） |
| 脚本进程 CPU | **0.88s**（阻塞，非计算） |
| 20 秒后 VBCSCompiler | **仍在**（Roslyn 编译服务器 idle 约 10 分钟） |
| `step7` 日志 | **未出现** |

### 根因

```powershell
$proc = Start-Process -FilePath 'cmd.exe' -ArgumentList '/c', $bat ... -Wait -PassThru
```

PowerShell 的 `Start-Process -Wait` 会等待**整棵进程树**。
`dotnet build` 会派生常驻的 `VBCSCompiler.exe`（Roslyn 编译服务器），
其父进程退出后它仍存活 ⇒ 每个编译步骤都可能空等约 10 分钟。

### 修复

去掉 `-Wait`，改为对返回的 `Process` 对象调用 `WaitForExit()`（只等待 `cmd.exe` 自身）。
本批正式验证链 8 步总耗时 **77.4s**，无空等。

---

## 缺陷 3 🔴 产品缺陷：Bisect 与 Mod 启停/删除路由不可达（死代码）

**归属 `MR04` / `MR06`，本批只记录不修。**

### 静态证据

`dev/PineglowLauncher/Pineglow.Launcher.Services/LocalApiService.cs`：

```
:415  if (seg.Length == 4 && seg[1] == "instances")   // 门控：精确等于 4
:419      delete / launch / diagnose / saves …         // 本身即需 4 段 ✓
:511      if (seg.Length == 5 && action == "bisect")  // ✗ 死代码
:531      if (seg.Length == 6 && action == "mods")    // ✗ 死代码
:547  }
:789  → 404 未找到接口
```

大括号深度可复算（`logs/unreachable-routes-proof.txt`，工具 `tooling/prove_unreachable_routes.py`）：

| 行 | 要求 | 相对 `:415` 深度 | 判定 |
|---|---|---:|---|
| `:419` | `seg.Length == 4` | +1 | 内部，一致，**可达** |
| `:511` | `seg.Length == 5` | +1 | 内部，**恒 false → 不可达** |
| `:531` | `seg.Length == 6` | +2 | 内部，**恒 false → 不可达** |
| `:409` | `seg.Length == 5` | ±0 | **块外，可达**（对照） |

`:408-409` 的注释写明
"Phase 3B v3 修复批次：cancel 路由必须先于通用 diagnose。精确 seg.Length 匹配"——
`diagnose/cancel` 同样需要 5 段，**正是因为同样的嵌套问题才被提到块外**。
bisect 与 mods 两组没有做同样的提升。

### 真实运行证据

隔离沙盒内 `VerifyHost.exe --bisect-smoke --port 48130` → **exit=1**：

```
[BisectSmoke] start, portable sandbox=…\VerifyHost\bin\Release\net10.0\data
[BisectSmoke] exception: Response status code does not indicate success: 404 (Not Found).
[BisectSmoke] FAIL (1 checks failed)
```

失败点定位（零代码注入法）：预置 `portable.flag` 与 `data/` 使冒烟**不自动清理**，
事后可见沙盒内 `config.json`（3,425 B）已写入、8 个假 Mod 已创建、
但**没有任何 `.jar.disabled`** ⇒ `CreateInstance` 与实例详情查询均成功，
失败确定在第一次 `POST /bisect/start`。

另：`LocalApiService.cs:791-793` 把 `InvalidOperationException` 映射为 **400**，
因此该 404 只可能来自 `:789` 的路由兜底，与静态分析一致。

### 为什么 796 个绿色测试没发现

| 测试 | 覆盖层级 | 是否触及 HTTP 路由 |
|---|---|---|
| `ModBisectServiceTests`（12 个 `[Fact]`） | `ModBisectService` 服务层 | ❌ 仅 `using Pineglow.Launcher.Services` |
| 全仓 C# 测试引用 `/bisect/` 路由字符串 | — | ❌ **0 处** |
| `ui/tests/api-contract.test.cjs` | 前端契约 | ❌ 无相关断言 |
| `VerifyHost.exe --bisect-smoke` | **API 级 E2E** | ✅ 但**从未被执行**（脚本 8 步不含它） |

**结论：现有测试结构上无法发现"路由不可达"这一类缺陷。**

### 影响面修正

调研"Mod 二分定位已实现且可靠（12 单测 + API 三端点齐备 + UI 三处接线）"中，
**服务层与 UI 层属实，路由层不成立**，因此不能按端到端已验证引用。
这与调研自身"schema 完整度 > 实现完整度"的判断一致：schema 齐了，落地没跟上。

---

## 缺陷 4 🟠 `dotnet publish` 就地改写 `packages.lock.json`

**归属 `MR19`，本批只量化不修。**

`.github/workflows/build.yml` 的注释已提示"普通 publish 的隐式还原会就地更新 checkout 里的
`packages.lock.json`"。本轮在隔离执行根内实测到该副作用：

| | 大小 | 变化 |
|---|---:|---|
| 执行前（= SourceRoot 原件） | 8.5 KB | — |
| 执行后 | **10.6 KB** | **+51 行** |

新增内容：
- `Microsoft.NET.ILLink.Tasks`（`Direct`，`10.0.9`）——正是 NU1004 点名的缺失包；
- 新的 `net10.0/win-x64` RID 段；
- `Avalonia.Angle.Windows.Natives` 等传递项。

**这把 NU1004 的根因从"文档记录"变成了"本轮实测"**：lock 文件原本只覆盖 `net10.0`，
RID 发布需要额外段与 ILLink 任务包，二者不一致 ⇒ 锁定还原必然失败。

**对 MR19 的直接含义**：要么生成含 RID 段的 lock 文件以支持锁定发布，
要么在流程中显式接受"发布路径未锁定"并禁止普通 publish 污染 checkout 的 lock 文件。
当前 CI 已在注释里承认该副作用但未加防护。

---

## 缺陷 5 🟢 白名单唯一差异（无影响，仅记录）

`dev/PineglowLauncher.Tests/DownloadResumeTests.cs` 未跟踪且不在白名单，
但为 **0 字节空文件**（SHA256 = 空串哈希），且 csproj 用 SDK 默认 glob 包含 `.cs`，
**不产生任何类型或测试用例**。按"没有依据就不修改已有正确工具"，**未修改白名单**。
