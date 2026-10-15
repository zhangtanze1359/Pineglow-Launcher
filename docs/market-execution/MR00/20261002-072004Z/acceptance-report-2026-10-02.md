# MR00 + MR01 第一批 · 独立验收报告

- **run-id**：`20261002-072004Z`
- **执行范围**：仅 MR00（执行根、源码输入与证据基线）+ MR01（功能事实矩阵刷新与调研勘误）
- **未执行**：MR02~MR27 的任何业务实施、用户联系、分发、发布
- **依据**：`docs/market-research-task-workflow-2026-10-02.md` 与
  `docs/minimax-market-research-execution-prompt-2026-10-02.md`

---

## 〇、最终状态

> ## **具备独立复验条件，等待 Codex 独立验收**
>
> 存在**两项阻断后续 MR 包的 P0 产品缺陷**（本批只记录、未修，按授权范围本批不修业务代码）。
> **暂不放行**任何业务任务包。

---

## 一、结论先行

| 项 | 结论 |
|---|---|
| 冷构建（隔离快照） | ✅ **通过** — 214 文件、泄漏 0、`bin`/`obj` 0、逐文件 SHA 与源全一致 |
| 正式 Node 测试 | ✅ **184 通过 / 0 失败 / 0 跳过**（11 个 `.test.cjs`） |
| 非 RID 锁定还原 | ✅ launcher 与 tests 均 `--locked-mode` exit 0 |
| C# Release 全套 | ✅ **612 通过 / 0 失败 / 0 跳过** |
| win-x64 单文件发布 | ✅ exit 0；45,644,552 B；SHA256 `3B71DCC1…`（**三次独立快照逐字节一致**） |
| RID 发布锁定 | ⚠️ **KNOWN-UNLOCKED（已知边界，非通过）** — 必然 NU1004 |
| API 级端到端冒烟 | 🔴 **失败** — `VerifyHost.exe --bisect-smoke` exit=1，根因是路由不可达 |
| 真实 GUI / 游戏 / 真实 AI | ⬜ **未验证**（明确不在本批范围） |
| GitHub Actions 实际运行 | ⬜ **未验证**（本轮不推送、不建 PR） |
| 主工作区只读性 | ✅ **未改写**（11/11 关键文件 SHA 与 manifest 一致） |
| MR01 文档包 | ✅ 6 份允许文件已更新，逐文件 diff 已交付 |

---

## 二、🔴 阻断项（必须先修，否则后续 MR 包建立在不可达的地基上）

### 阻断 1：Bisect 与 Mod 启停/删除的 HTTP 路由不可达（死代码）

**`LocalApiService.cs:511`（要求 `seg.Length == 5`）与 `:531`（要求 `seg.Length == 6`）
被嵌套在 `:415` 的 `if (seg.Length == 4 && …)` 块内 ⇒ 两个条件恒为 false ⇒ 落到 `:789` 返回 404。**

| 证据类型 | 内容 |
|---|---|
| 静态（可复算） | 大括号深度：`:415`=3、`:511`=4、`:531`=5、`:409`=3。`tooling/prove_unreachable_routes.py` + `logs/unreachable-routes-proof.txt` |
| 真实运行 | 隔离沙盒内 `--bisect-smoke` **exit=1**，首个 `POST /bisect/start` 即 404 |
| 定位方法 | 预置 `portable.flag` + `data/` 使冒烟不自清理 → 见 `config.json` 与 8 个假 Mod 已生成、**无 `.disabled`** ⇒ 失败点确定在第一次 bisect 调用 |
| 旁证 | `:408-409` 注释显示 `diagnose/cancel` 曾因**同一类嵌套问题**被提到块外；bisect/mods 漏做 |

**影响**：`/api/instances/{id}/bisect/{start,test,giveup}` 与 `/api/instances/{id}/mods/{file}/{toggle,delete}` 端到端不可用。
调研"Bisect 已实现且可靠（12 单测 + API 三端点齐备 + UI 三处接线）"中，**服务层与 UI 层属实，路由层不成立**。

**归属**：`MR04` / `MR06`（P0）。

### 阻断 2：现有测试结构上无法发现路由不可达

| 测试 | 覆盖层级 | 触及 HTTP 路由 |
|---|---|---|
| `ModBisectServiceTests` 12 个 `[Fact]` | `ModBisectService` 服务层 | ❌ 仅 `using Pineglow.Launcher.Services` |
| 全仓 C# 测试引用 `/bisect/` 路由字符串 | — | ❌ **0 处** |
| `ui/tests/api-contract.test.cjs` | 前端契约 | ❌ 无断言 |
| `--bisect-smoke`（唯一 API 级 E2E） | API 级 | ✅ 但**从未被执行**（原脚本 8 步不含它） |

**结论**：796 个绿色测试（Node 184 + C# 612）对该缺陷**完全无感知**。
修复时必须补**打到 HTTP 层**的测试，否则同类问题会再次静默复发。

### 次级问题（同批记录，未修）

| # | 问题 | 归属 |
|---|---|---|
| 3 | Bisect 禁用/恢复异常只写日志（`:1931-1934`、`:1952-1955`），不阻止错误推进、不反馈用户 | `MR06` / `MR07` |
| 4 | `ModService.DeleteMod` 为不可恢复 `File.Delete`（`:107`、`:111`） | `MR04` |
| 5 | 实例"删除"只移除配置记录（`:1045-1057` 零文件操作），UI 语义与存档提示未明确 | `MR03` |
| 6 | `dotnet publish`（无锁定）**就地改写 `packages.lock.json`**：8.5 KB → 10.6 KB（+51 行，新增 `Microsoft.NET.ILLink.Tasks` 与 `net10.0/win-x64` 段） | `MR19` |
| 7 | `docs/api-reference.md:36-39` 仍把 bisect/mods 路由写成可用且无警示 —— **该文件在本批白名单外，未修改** | 需 Codex 决定是否扩范围 |

---

## 三、本轮修的两个工具缺陷（`scripts/verify-source-snapshot.ps1`）

原脚本有两个**会让验证结论不可信**的缺陷，均在本轮被实测触发：

### 缺陷 A 🔴 假通过

以 `-EvidenceDir` 指向含非 ASCII 字符的路径运行时，**8 个步骤全部报 `exit=0 PASS`，每步 1.0s，
而 `step*.log` 一个都没生成** —— 命令从未执行，但 `%ERRORLEVEL%` 仍为 0 被采信。

根因：临时 `.cmd` 以 `-Encoding ascii` 写出，日志路径中的中文被写成 `?????`，
`cmd.exe` 整行解析失败并跳过该行，命令未跑而退出码仍是进入该行前的 0。

**这是"把验证器故障变成验证通过"的缺陷**：任何步骤失败在该配置下都会被静默记为通过。

### 缺陷 B 🟠 挂死

`Start-Process -Wait` 等待**整棵进程树**，而 `dotnet build` 派生的常驻 `VBCSCompiler.exe`
（父进程退出后仍存活，idle 约 10 分钟）使每个编译步骤空等。实测：build 步骤日志已完整写出，
脚本仍阻塞，`step7` 日志 5 分钟内不出现。

### 修复与验证

补丁仅应用于 **ExecutionRoot 副本**（授权范围 B）；**SourceRoot 原件未改写**
（补丁前后 SHA 均为 `E3AAF2572CC47517D6C8D3383D3E924FC0B03030E61F4335AC7940B5904EEBB3`）。

**以中文证据目录重跑修复后的脚本（即原缺陷的触发条件）**：

| | 修复前 | 修复后 |
|---|---|---|
| 步骤日志 | **0 个** | **8 个全部生成** |
| 退出码 | 全为假 `0` | **全部真实可读** |
| build / C# 测试 | 挂死 | 2.2s / 58.0s 正常完成 |
| 最终判定 | FAIL（产物缺失） | **`finalVerdict = PASS`** |

过程中补丁自身引入过一次 `ExitCode` 读不到的回归（全部步骤判 FAIL）。
**护栏方向正确**——表现为"吵闹的失败"而非"静默的假通过"，这正是加护栏的价值。两次运行证据均保留。

---

## 四、目录、身份与输入清单

| 角色 | 路径 |
|---|---|
| SourceRoot（**只读**） | `D:\松光启动器` |
| DeliveryRoot（只读） | `C:\Users\Administrator\.codex\worktrees\ed41\松光启动器` |
| EvidenceRoot | `…\ed41\松光启动器\docs\market-execution` |
| **ExecutionRoot** | `C:\Users\Administrator\AppData\Local\Temp\pineglow-snapshot-810a28bd0d3d456c9e28201fb6fdf158` |

固定入口：`EvidenceRoot\MR00\execution-root.json`（run-id 副本内容相同）

| 身份项 | 值 |
|---|---|
| SourceRoot HEAD / 分支 | `f145d14eb26b0b7459a28491e764ac8c573085d3` / `main` |
| DeliveryRoot HEAD | **相同**（detached）—— 但**内容不同**，SourceRoot 有 35 个 `M` + 1251 个未跟踪 |
| tracked / 白名单 / 快照文件 | 161 / 53 / **214** |
| SDK / Node / npm | 10.0.301 / v24.19.0 / 11.17.0 |
| 泄漏文件 / `bin`,`obj` / SHA 不一致 | **0 / 0 / 0** |

> ⚠️ 2026-09-28 记录为 32 个 `M` + 1240 个未跟踪；本轮实测 **35 + 1251**。
> **未沿用旧数字**，全部按当前实际审查清单计算。

**白名单唯一差异**：`DownloadResumeTests.cs` 未跟踪且不在白名单，但为 **0 字节空文件**
（SHA256 = 空串哈希），csproj 用 SDK 默认 glob，空 `.cs` 不产生类型或测试 ⇒ **零影响**，
按"没有依据就不修改已有正确工具"**未修改白名单**。

---

## 五、逐步验证结果（每步独立退出码）

| # | 命令 | 退出码 | 用时 | 日志 | 结果 |
|---|---|---:|---:|---:|---|
| 01 | `node --test --test-reporter=spec <11 个 .test.cjs>` | 0 | 3.0s | 19,270 B | **184 通过 / 0 失败 / 0 跳过** |
| 02 | `dotnet restore launcher --locked-mode` | 0 | 0.9s | 212 B | PASS |
| 03 | `dotnet restore tests --locked-mode` | 0 | 1.0s | 280 B | PASS |
| 04 | `dotnet build launcher -c Release --no-restore` | 0 | 2.8s | 264 B | PASS |
| 05 | `dotnet test tests -c Release --no-restore` | 0 | 60.2s | 6,218 B | **612 通过 / 0 失败 / 0 跳过** |
| 06 | `dotnet build VerifyHost -c Release` | 0 | 2.2s | 1,373 B | PASS（1 个 CS0649 warning） |
| 07 | `dotnet publish -r win-x64 … -p:RestoreLockedMode=true` | **1** | 0.9s | 1,345 B | **KNOWN-UNLOCKED**（NU1004） |
| 08 | `dotnet publish -r win-x64 --self-contained /p:PublishSingleFile=true …` | 0 | 7.0s | 575 B | PASS |
| 09 | `VerifyHost.exe --bisect-smoke --port 48130` | **1** | 0.2s | 318 B | **FAIL**（阻断 1） |

**步骤 09 的安全性已先行审查**：沙盒经 `PlatformPaths` portable 分支验证，
配置与游戏目录均落在 `<EXEC>\…\data\`；仅绑定 `127.0.0.1`（`LocalApiService.cs:177`）；
端口事先确认空闲；运行后 `portable.flag` 与 `data/` 状态复原、端口已释放。

### 锁定边界（准确表述）

- **非 RID 路径受锁定约束**（两个 `--locked-mode` 均 exit 0）。
- **RID 发布无法锁定**：步骤 07 原文为
  `项目的运行时标识符已更改。项目的运行时标识符: win-x64，锁定文件的运行时标识符: 。`
  及 `…Microsoft.NET.ILLink.Tasks >= 10.0.9`。记为 KNOWN-UNLOCKED，**不算通过**。
- **不得把 `--locked-mode` 传给 `dotnet publish`**（`dotnet restore` 专属开关，报 `MSB1001`）。

### 单文件产物（路径必须区分）

| | 路径 | 大小 | SHA256 |
|---|---|---:|---|
| ❌ 非发布产物 | `<rid>/Pineglow.Launcher.exe`（build 输出 apphost） | 335,872 B | `102B398A…4D52F` |
| ✅ 发布产物 | `<rid>/**publish**/Pineglow.Launcher.exe` | **45,644,552 B** | `3B71DCC1DB72A2BC79945F611492BC366402CD455D7DA307BC06BEF33CDA7001` |

**该 SHA 与 2026-09-28 勘误 §14.1 的记录逐字节一致**，本轮两次独立快照也一致
⇒ 跨三次快照的单文件产物可复现。

---

## 六、MR01 功能事实矩阵

**状态词纪律**（旧文档的"一个 ✅ 覆盖所有层级"是主要歧义来源）：
`代码存在` / `自动验证` / `真实场景验证` / `未验证` / `不支持`。

**本轮唯一可引用数字**：Node 184 / 0 / 0；C# 612 / 0 / 0；单文件 45,644,552 B；
RID 锁定 KNOWN-UNLOCKED。**历史 Gate 不重新宣布，历史测试数不作本轮结果。**

### 8 条指定勘误全部落实

| # | 修正 |
|---|---|
| 1 | `.mrpack` **已实现**（`ImportModrinthPackAsync:274` + 格式探测，`LocalApiService:1091-1094`/`:3626` 接线）。导出仍 `.mcm`——**导入≠导出** |
| 2 | 实例"删除"**只移除配置记录**（`:1045-1057` 零文件操作） |
| 3 | `DeleteMod` 是**不可恢复** `File.Delete`（`:107`/`:111`） |
| 4 | Bisect 禁用/恢复**异常只写日志**（`:1931-1934`/`:1952-1955`）；**不把 12 个单测当真实恢复达标** |
| 5 | **不是 Tauri**（全仓 `tauri` 零命中）；已有自研 `UpdateService`（含 SHA256 校验 + HTTPS 强制） |
| 6 | 现有 **5 种**加载器（`ModLoaderKind`）；Cleanroom/Legacy Fabric/LiteLoader **不在枚举中**；**Quilt 不自动移除** |
| 7 | 不写"Bisect 反超 Prism""唯一/领先 AI""三平台 CI = 正式支持"；竞品未证实的能力写"本次材料未证实" |
| 8 | CurseForge Key 违规结论**已被调研撤回**，本文**不作新的法律定性**；只记可核实的注释事实与剩余工作 |

### 文档改动（6 份，全部在 ExecutionRoot 内）

| 文件 | +/- | 主要内容 |
|---|---:|---|
| `docs/features.md` | +229 / -66 | 状态分级定义、全量功能矩阵、🔴 缺陷节、可复验事实、遗留事项 + MR ID |
| `docs/roadmap.md` | +71 / -20 | 下调 4 条对标结论、新增 P0 可达性路线、MR00~MR27 映射 |
| `docs/development-guide.md` | +70 / -6 | §五之二 快照与可复现性验证、两个验证器缺陷、锁定边界、bisect-smoke 红灯、改动纪律硬规则 |
| `docs/README.md` | +38 / -10 | 索引刷新、关键事实速查、最高优先级缺陷、维护规约扩充 |
| `docs/architecture.md` | +36 / -2 | §三点五 路由门控陷阱与硬规则；非 Tauri；服务数 76 |
| `README.md` | +16 / -4 | 功能列表加状态提示、新增「已知缺陷」 |

---

## 七、只读边界与本轮自身的一次越界

- **SourceRoot 未被改写**：11 个关键文件（6 份文档、2 个脚本、2 个 lock 文件、`LocalApiService.cs`）
  SHA256 **11/11 与 `input-manifest.json` 完全一致**。
- DeliveryRoot 的两份 Codex 规划文档**原样未动**（mtime 仍为 15:12:53）。
- `D:\市场调研` 全程只读。
- 真实 `%APPDATA%\MCMiniLauncher`、Windows 凭据库、用户游戏目录/存档**未触碰**。
- 未执行：`git reset/restore/checkout/clean/stash`、`git add -A`、暂存、提交、推送、PR、部署、打标签、镜像。
- 未调用真实付费模型、未联系用户、未分发产物。

### ⚠️ 本轮自身的一次越界（如实记录）

执行初期（**读到执行提示词之前**）误将取证脚本写入 `D:\松光启动器\evidence\`，属只读路径。
发现后立即：3 个文件移入 EvidenceRoot → 用 `mavis-trash` 移除该新建目录 →
`git status` 确认该路径无残留。SourceRoot 的 35 个 `M` 与 1251 个未跟踪资产**未受影响**。
保留此记录供独立验收方核对。

---

## 八、交付物清单

### 固定入口
| 文件 | 内容 |
|---|---|
| `MR00\execution-root.json` | ExecutionRoot 身份、run-id、manifest 路径、环境、已知边界 |
| `MR00\20261002-072004Z\baseline\execution-root.json` | 同内容副本 |
| `MR00\20261002-072004Z\baseline\input-manifest.json` | **214 条**逐文件 SHA + 来源分类 + 与源比对结果 |
| `MR00\20261002-072004Z\baseline\input-manifest.sha256.txt` | 同上纯文本（26,446 B） |

### 报告
| 文件 | 内容 |
|---|---|
| `MR00\20261002-072004Z\baseline\baseline.md` | MR00 基线（身份、清单审查、9 步结果、锁定边界、只读核验） |
| `MR00\20261002-072004Z\mr00-verification-defects.md` | 5 个缺陷的完整证据链、修复与验证 |
| `MR01\20261002-072004Z\mr01-report.md` | MR01 改动清单、8+3 条勘误对照、层级纪律、只读边界 |
| `本文件` | 独立验收报告 |

### 补丁与文档包
| 文件 | 内容 |
|---|---|
| `MR00\20261002-072004Z\outputs\verify-source-snapshot-mr00-fix.patch` | 验证器最小修复（66 行 diff） |
| `MR01\20261002-072004Z\outputs\mr01-docs.patch` | 6 份文档逐文件 unified diff（60,820 B） |
| `MR01\20261002-072004Z\outputs\{README.md,docs\*.md}` | 更新后文档本体 |

### 证据与工具
- `MR00\…\logs\formal\` — 9 步逐步 JSON（command/cwd/起止/exitCode/测试数）+ stdout + **未经转码的 raw 日志**
- `MR00\…\logs\fixed-script-run2-cjk-evidence\` — 修复后脚本在中文证据目录下的 `finalVerdict=PASS`
- `MR00\…\logs\fixed-script-run-cjk-evidence\` — 补丁首版的回归证据（全部 FAIL，护栏生效）
- `MR00\…\logs\unreachable-routes-proof.txt` — 路由不可达的大括号深度证明
- `MR00\…\logs\whitelist-audit.txt`、`baseline-commands.json`、`rootcause-repro\` — 清单审查与最小复现
- `MR01\…\logs\fact-matrix.txt` — 功能事实基础数据
- `tooling\` — 11 个可复跑脚本（基线、清单审计、逐文件 manifest、逐步验证、沙盒冒烟、路由证明、补丁生成、文档打包）

---

## 九、供独立复验的命令

```powershell
$ev  = 'C:\Users\Administrator\.codex\worktrees\ed41\松光启动器\docs\market-execution'
$run = '20261002-072004Z'
$exe = 'C:\Users\Administrator\AppData\Local\Temp\pineglow-snapshot-810a28bd0d3d456c9e28201fb6fdf158'

# 1) 执行根身份与输入完整性
Get-Content "$ev\MR00\execution-root.json" -Encoding UTF8
(Get-Content "$ev\MR00\$run\baseline\input-manifest.json" -Encoding UTF8 | ConvertFrom-Json).Count   # 应为 214

# 2) 逐步验证结果（每步独立退出码与测试数）
Get-ChildItem "$ev\MR00\$run\logs\formal\*.json" | ForEach-Object {
  $j = Get-Content $_.FullName -Encoding UTF8 | ConvertFrom-Json
  '{0,-28} exit={1,-4} {2,-16} node={3}/{4} cs={5}/{6}' -f $j.name, $j.exitCode, $j.status, $j.nodePass, $j.nodeTests, $j.csPassed, $j.csTotal
}

# 3) 单文件产物（注意 publish 子目录，不是 apphost）
Get-FileHash "$exe\dev\PineglowLauncher\bin\Release\net10.0\win-x64\publish\Pineglow.Launcher.exe" -Algorithm SHA256
# 期望 3B71DCC1DB72A2BC79945F611492BC366402CD455D7DA307BC06BEF33CDA7001

# 4) 路由不可达复算
python "$ev\MR00\$run\tooling\prove_unreachable_routes.py" 'D:\松光启动器\dev\PineglowLauncher\Pineglow.Launcher.Services\LocalApiService.cs'

# 5) 主工作区未被改写（抽样比对 manifest）
$man = Get-Content "$ev\MR00\$run\baseline\input-manifest.json" -Encoding UTF8 | ConvertFrom-Json
$man | Where-Object { $_.path -match '^(README|docs/|scripts/verify|scripts/source)' } | ForEach-Object {
  $cur = (Get-FileHash (Join-Path 'D:\松光启动器' ($_.path -replace '/','\')) -Algorithm SHA256).Hash.ToLower()
  '{0,-34} {1}' -f $_.path, $(if ($cur -eq $_.sha256) { '未被改写' } else { '★ 已变化' })
}

# 6) 重新验证修复后的快照脚本（注意：EvidenceDir 建议用纯 ASCII 以外的中文路径以复现原缺陷条件）
powershell -NoProfile -ExecutionPolicy Bypass -File "$exe\scripts\verify-source-snapshot.ps1" `
  -RepoRoot 'D:\松光启动器' -EvidenceDir "$env:TEMP\pineglow-recheck"

# 7) API 级端到端冒烟（预期 exit=1，直至阻断 1 修复）
& "$exe\dev\PineglowLauncher\VerifyHost\bin\Release\net10.0\VerifyHost.exe" --bisect-smoke --port 48130
```

---

## 十、未验证项与残留风险

| 项 | 状态 |
|---|---|
| 真实 GUI 启动 / WebView2 真实页面 | **未验证**（本批明确不做） |
| 真实游戏启动 | **未验证** |
| 真实付费 AI 模型调用 | **未验证**（且本批禁止） |
| GitHub Actions 实际运行 | **未验证**（未推送、未建 PR） |
| Linux / macOS 上的一切（含 `--locked-mode` 还原） | **未验证**（本机仅 Windows） |
| RID 发布锁定 | **已证明不可行**（NU1004），修复属 `MR19` |
| `docs/api-reference.md:36-39` 仍把不可达路由写成可用 | **未修改**（白名单外），需 Codex 决定 |
| `docs/engineering.md`、`docs/ai-*.md` 未同步本轮勘误 | **未修改**（白名单外） |
| 快照与执行根保留在 `%TEMP%` | **未清理**（按设计保留供独立复验；清理需用 `-CleanupSnapshot` 或 `mavis-trash`） |

---

## 十一、下一步需要 Codex / 用户裁决

1. **是否放行 `MR04` / `MR06`** 去修路由可达性？（建议先修，它是 P0 且被测试盲区掩盖）
2. **是否扩 MR01 白名单**以同步 `docs/api-reference.md`（它目前仍把不可达路由写成可用）
3. **是否接受本批对调研 4 条结论的下调**，并据此决定 `MR09`（AI 增量价值）与 `MR14`（Quilt/加载器）的优先级
4. 下一批任务包需明确：**任务 ID、执行根、依赖验收记录、允许文件、证据根、停止点**

**本报告不宣布任何阶段放行、提交或发布。**
