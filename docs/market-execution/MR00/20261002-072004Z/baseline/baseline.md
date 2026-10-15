# MR00 · 执行根、源码输入与证据基线

- run-id：`20261002-072004Z`
- 范围：**仅 MR00 基线**（不进入 MR01 业务实施，不进入 MR02~MR27）
- 结论：**冷构建通过 / 全套测试通过 / 单文件发布通过 / RID 发布未锁定（已知边界） / 真实运行未验证**

---

## 一、目录定义与身份

| 角色 | 路径 | 说明 |
|---|---|---|
| SourceRoot（只读） | `D:\松光启动器` | 真实工作区，本批**全程只读** |
| DeliveryRoot（只读） | `C:\Users\Administrator\.codex\worktrees\ed41\松光启动器` | 仅存放 Codex 的两份规划/提示词文档 |
| EvidenceRoot | `C:\Users\Administrator\.codex\worktrees\ed41\松光启动器\docs\market-execution` | 本批全部证据 |
| ExecutionRoot | `C:\Users\Administrator\AppData\Local\Temp\pineglow-snapshot-810a28bd0d3d456c9e28201fb6fdf158` | 由快照脚本生成的隔离执行根 |

**固定入口**：`EvidenceRoot\MR00\execution-root.json`
（run-id 副本：`EvidenceRoot\MR00\20261002-072004Z\baseline\execution-root.json`，内容相同）

### 两个根的 Git 事实（只读核对）

| 项 | SourceRoot | DeliveryRoot |
|---|---|---|
| HEAD | `f145d14eb26b0b7459a28491e764ac8c573085d3` | **相同** |
| 分支 | `main` | detached（空） |
| tracked 文件 | 161 | 161 |
| 已修改（M/A/D/R） | **35** | 0 |
| 未跟踪 | **1251** | 仅本批新增的 evidence 目录 + Codex 两份规划文档 |

> ⚠️ **HEAD 相同 ≠ 内容相同。** 2026-09-28 记录为 32 个 `M` + 1240 个未跟踪；
> 本轮实测为 **35 个 `M` + 1251 个未跟踪**。**不得沿用旧数字**，本批全部按当前实际审查清单计算。

### 环境

| 项 | 值 |
|---|---|
| SDK | `10.0.301`（`global.json`: `rollForward=latestPatch`, `allowPrerelease=false`） |
| Node | `v24.19.0` |
| npm | `11.17.0` |
| PowerShell | 5.1.26100.9549 |
| 平台 | Windows x64 |

---

## 二、源码输入清单审查

### 2.1 白名单分类（`scripts/source-snapshot-inputs.txt`，53 项）

| 类别 | 数量 | 内容 |
|---|---|---|
| A | 15 | 生产源码 / UI / vendor（10 个服务 `.cs`、`skin3d-core.js`、`skin3d.js`、`ui/vendor/*`） |
| B | 16 | C# 测试源 |
| C | 6 | 测试夹具图片（`TestAssets/ContentIcons/valid-*`） |
| D | 1 | `global.json` |
| E | 2 | 两个 `packages.lock.json` |
| F | 11 | 正式 Node 测试 `ui/tests/*.test.cjs` |
| G | 2 | 本轮可复现性工具（脚本 + 白名单本身） |

### 2.2 未跟踪文件逐项分类结果（1251 项）

| 类别 | 总数 | 白名单内 |
|---|---:|---:|
| `docs/` 历史证据 | 1115 | 0 |
| `ui/tests/` 探针 / 备份 / 临时 | 20 | 0 |
| `scripts/` 临时脚本 | 32 | 0 |
| `.testagent/` + `.trae/` | 18 | 0 |
| `TestResults/*.trx` | 4 | 0 |
| **生产服务 `.cs`** | **10** | **10** ✅ |
| **C# 测试 `.cs`** | **17** | **16** ⚠️ |
| 测试夹具图片 | 6 | 6 ✅ |
| **Node 正式测试** | **11** | **11** ✅ |
| **UI / vendor** | **5** | **5** ✅ |
| lock 文件 / `global.json` | 2 / 1 | 2 / 1 ✅ |
| 其他（`.cindy-worktrees` 等） | 7 | 0 |

### 2.3 唯一白名单差异及其定性

`dev/PineglowLauncher.Tests/DownloadResumeTests.cs` — **未跟踪且不在白名单**。

- 实测 **0 字节**（`Get-Item.Length = 0`，SHA256 `E3B0C442…B855` 即空串哈希），创建于 2026-10-01 00:33:42。
- `Pineglow.Launcher.Tests.csproj` 使用 **SDK 默认 glob**（无显式 `<Compile>`，未关闭 `EnableDefaultCompileItems`），
  空 `.cs` 编译后不产生任何类型。
- **定性：对构建与测试结果零影响**（不减少任何测试用例）。
  按"没有依据就不修改已有正确工具"，**本批不修改白名单**，仅记录该差异。

### 2.4 快照完整性（工具实测，非声明）

| 指标 | 值 |
|---|---|
| tracked 工作区版本复制 | 161 |
| 白名单复制 | 53 / 53（缺失 0） |
| 快照文件总数 | **214**（期望 = 161 + 53，脚本按实际值动态计算，非硬编码） |
| 额外 / 泄漏文件 | **0** |
| 构建前 `bin` / `obj` | **0** |
| 白名单逐项 SHA256 与源一致 | 全部一致 |
| 全量逐文件 SHA 与源不一致 | **0** |
| 快照内源中不存在的文件 | **0** |

全量 214 条 manifest：`EvidenceRoot\MR00\20261002-072004Z\baseline\input-manifest.json`
纯文本 SHA 清单：同目录 `input-manifest.sha256.txt`

---

## 三、正式验证结果（本批逐项实测）

执行位置：ExecutionRoot（`Set-Location` 到该目录后逐步执行）。
每步独立进程、独立日志、独立退出码，**非零不会被后续成功覆盖**。

| # | 命令 | 退出码 | 用时 | 日志 | 结果 |
|---|---|---:|---:|---:|---|
| 01 | `node --test --test-reporter=spec <11 个 .test.cjs>` | **0** | 3.1s | 19,270 B | **184 通过 / 0 失败 / 0 跳过** |
| 02 | `dotnet restore launcher --locked-mode` | **0** | 0.9s | 212 B | PASS |
| 03 | `dotnet restore tests --locked-mode` | **0** | 1.0s | 280 B | PASS |
| 04 | `dotnet build launcher -c Release --no-restore` | **0** | 2.8s | 264 B | PASS |
| 05 | `dotnet test tests -c Release --no-restore` | **0** | 60.2s | 6,218 B | **612 通过 / 0 失败 / 0 跳过** |
| 06 | `dotnet build VerifyHost -c Release` | **0** | 2.2s | 1,373 B | PASS（1 个 CS0649 warning） |
| 07 | `dotnet publish -r win-x64 … -p:RestoreLockedMode=true` | **1** | 0.9s | 1,345 B | **KNOWN-UNLOCKED**（NU1004） |
| 08 | `dotnet publish -r win-x64 --self-contained /p:PublishSingleFile=true …` | **0** | 7.0s | 575 B | PASS |
| 09 | `VerifyHost.exe --bisect-smoke --port 48130` | **1** | 0.2s | — | **FAIL（见 §四）** |

### 3.1 锁定边界（准确表述，勿夸大）

- **非 RID 路径受锁定约束**：launcher 与 tests 的 `--locked-mode` 还原均返回 0。
- **RID 发布无法锁定**：步骤 07 必然 **NU1004**，原文实测记录：
  > `项目的运行时标识符已更改。项目的运行时标识符: win-x64，锁定文件的运行时标识符: 。`
  > `…项目的包引用: …, Microsoft.NET.ILLink.Tasks >= 10.0.9。`
  记为 **KNOWN-UNLOCKED**——既不算通过，也不掩盖为普通失败。锁定策略属 `MR19`。
- **不得把 `--locked-mode` 传给 `dotnet publish`**（`dotnet restore` 专属开关，会报 `MSB1001`）。

### 3.2 单文件产物（路径必须区分）

| | 路径 | 大小 | SHA256 |
|---|---|---:|---|
| ❌ **不是**发布产物 | `<EXEC>\dev\PineglowLauncher\bin\Release\net10.0\win-x64\Pineglow.Launcher.exe` | 335,872 B | `102B398A8067A637…4D52F` |
| ✅ 发布产物 | `<EXEC>\dev\PineglowLauncher\bin\Release\net10.0\win-x64\**publish**\Pineglow.Launcher.exe` | **45,644,552 B** | `3B71DCC1DB72A2BC79945F611492BC366402CD455D7DA307BC06BEF33CDA7001` |

> 该单文件产物的大小与 SHA256 **与 2026-09-28 勘误 §14.1 记录逐字节一致**，
> 说明跨两次独立快照的单文件产物可复现。

### 3.3 本批明确**未验证**的项

真实 GUI 启动 / WebView2 真实页面 / 真实游戏启动 / 真实付费 AI 模型调用 /
CI（GitHub Actions）实际运行 / Linux 与 macOS 上的一切 / RID 锁定还原。

---

## 四、本轮发现的缺陷（详见 `mr00-verification-defects.md`）

| # | 缺陷 | 严重度 | 归属 |
|---|---|---|---|
| 1 | `verify-source-snapshot.ps1` 步骤执行器**假通过**：非 ASCII 证据路径下命令从未执行却记为 PASS | 🔴 严重 | 已在 ExecutionRoot 内最小修复 |
| 2 | 同上，**挂死**：`Start-Process -Wait` 被常驻 `VBCSCompiler.exe` 阻塞 | 🟠 中 | 已在 ExecutionRoot 内最小修复 |
| 3 | **Bisect 与 Mod 启停/删除 HTTP 路由不可达**（恒 404，死代码） | 🔴 严重产品缺陷 | `MR04` / `MR06` |
| 4 | `dotnet publish`（无锁定）**就地改写 `packages.lock.json`** | 🟠 中（已量化） | `MR19` |
| 5 | 唯一白名单差异 `DownloadResumeTests.cs` 为 0 字节空文件 | 🟢 无影响 | 仅记录 |

---

## 五、只读边界核验

- **SourceRoot 未被改写**：抽验 11 个关键文件（6 份文档、2 个脚本、2 个 lock 文件、`LocalApiService.cs`），
  SHA256 **11/11 与本批 `input-manifest.json` 记录完全一致**。
- 验证器脚本补丁前后 SourceRoot SHA 均为 `E3AAF2572CC47517D6C8D3383D3E924FC0B03030E61F4335AC7940B5904EEBB3`。
- DeliveryRoot 的两份 Codex 规划文档保持原样，未读写。
- 真实 `%APPDATA%\MCMiniLauncher`、Windows 凭据库、用户游戏目录/存档**未被触碰**：
  `--bisect-smoke` 的沙盒经核实落在 `<EXEC>\...\VerifyHost\bin\...\data\`（`PlatformPaths` portable 分支，
  `GetDefaultMinecraftDirectory()` 与 `GetConfigDirectory()` 都返回 `<BaseDirectory>/data`），且仅绑定 `127.0.0.1`。
- 未执行：`git reset/restore/checkout/clean/stash`、`git add -A`、暂存、提交、推送、PR、部署、打标签、镜像动作。
- 未调用真实付费模型、未联系用户、未分发产物。

### 本轮自身的一次越界与纠正

执行初期误将取证脚本写入 `D:\松光启动器\evidence\`（属只读路径）。
发现后立即将 3 个文件移入 EvidenceRoot，并用 `mavis-trash` 移除该新建目录；
`git status` 确认该路径已无残留，SourceRoot 的 35 个 `M` 与 1251 个未跟踪资产未受影响。
**记录在案以供独立验收方核对。**
