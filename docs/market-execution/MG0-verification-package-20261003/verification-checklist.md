# MG0 / MG0-pre 复验清单（逐条可执行）

> 标 ⚠️ = 脚本验不了、必须人读；标 🔴 = 执行者已明确标为未验证。

---

## 零、前置

```powershell
cd D:\松光启动器
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\verify-mg0.ps1
```

期望：`C# exit=0  Node exit=0  台账审计 exit=0`；`本里程碑内无法解释的漂移=1`（已在 `drift-notes.md` 解释）。

---

## 一、逐项复验

### MR00 执行基线与显式源码输入

| # | 验什么 | 位置 | 期望 |
|---|---|---|---|
| 0.1 | 执行根是否明确 | `MR00…/baseline/execution-root.json` | 含 SourceRoot 与执行根；**且能看出执行在隔离快照上** |
| 0.2 | 输入清单是否显式 | `MR00…/baseline/input-manifest.sha256.txt` | **214 条** `sha  path` |
| 0.3 | ⚠️ **清单是否完整** | 同上 | 脚本**不判断**完整性：请抽查构建所需输入（csproj/lock/global.json/SDK/Node 测试）是否都在 |
| 0.4 | 基线命令是否留痕 | `MR00…/logs/baseline-commands.json` | 可复跑的命令与结果 |
| 0.5 | ⚠️ 自查缺陷是否记录 | `MR00…/mr00-verification-defects.md` | 当时自查出的缺陷有留痕（"失败被吞"风险的对策） |
| 0.6 | 🔴 **无 `manifest.json`** | —— | 早期格式，无机器可读索引；请判断是否要求回填 |

### MR01 功能事实矩阵与调研纠偏

| # | 验什么 | 位置 | 期望 |
|---|---|---|---|
| 1.1 | 事实矩阵 | `MR01…/logs/fact-matrix.txt` | 14 KB；覆盖功能事实 |
| 1.2 | ⚠️ **文档产出是否与源码事实一致** | `MR01…/outputs/docs/*`（architecture/development-guide/features/roadmap/README） | 需人读；脚本不判断 |
| 1.3 | 补丁与工具留痕 | `MR01…/outputs/mr01-docs.patch`、`tooling/*` | 可复跑 |
| 1.4 | 🔴 **无 `manifest.json`、无哈希清单** | —— | 早期格式；请判断是否要求回填 |

### W1 路由可达性修复（MG0-pre）

| # | 验什么 | 位置 | 期望 |
|---|---|---|---|
| W1.1 | 先红 | `W1-W3…/red/03-route-tests-before.log` | 有失败（**未先 RED** 风险的对策） |
| W1.2 | 后绿 | `W1-W3…/green/14-route-tests-final.log` | 全通过 |
| W1.3 | 基线副本与 SHA | `W1-W3…/baseline/LocalApiService.cs.orig` + `.sha256.txt` | 可核对回退依据 |
| W1.4 | 补丁内容 | `W1-W3…/green/W1-LocalApiService.patch` | 10 条路由提升；**注意这是当时的 diff，不应再应用** |

### W2 HTTP 层路由契约测试

| # | 验什么 | 位置 | 期望 |
|---|---|---|---|
| W2.1 | 契约用例先红后绿 | `W1-W3…/red/03-…` 与 `green/05-/06-/14-…` | 修复前 15 失败 / 1 通过 → 16/16 通过 |
| W2.2 | 用例清单 | `W1-W3…/green/10-node-test-list.txt` | 与当时的 Node 测试清单一致 |

### W3 文档白名单同步

| # | 验什么 | 位置 | 期望 |
|---|---|---|---|
| W3.1 | 文档 diff | `W1-W3…/docs/api-reference.md.diff`、`roadmap.md.diff` | 与"白名单同步"一致 |
| W3.2 | 台账是否仍引用同一证据目录 | `docs/market-task-plan-2026-10-03.md` 的 W1/W2/W3 行 | 三行都指向 `W1-W3-route-closure/20261002-182304Z` |

---

## 二、跨包检查

| # | 验什么 | 怎么验 | 期望 |
|---|---|---|---|
| C1 | 台账一致性 | `powershell -File .\scripts\audit-plan-ledger.ps1` | exit 0（状态词合法、证据可定位、无孤儿、run-id 真实） |
| C2 | 漂移是否已解释 | `MG0…/drift-notes.md` | 2 条：1 条已修（MR17 遗漏）、1 条结构性（活文档） |
| C3 | 白名单类别计数 | `scripts/source-snapshot-inputs.txt` | 八个类别声明数 == 实际条目数 |
| C4 | 含中文 `.ps1` 是否带 BOM | 前三字节 `EF BB BF` | 必须带 |

---

## 三、复验结论模板

```
自动验证：C# 755/0、Node 230/0、台账审计 exit 0、本里程碑内漂移 1（已解释）
独立复验：<逐条填 §一、§二；⚠️ 项要写是否抽查了内容>
未通过项：<逐条列；没有就写"无">
未验证项：<MR00/MR01 无 manifest.json 的格式差异；W1-W3 当时已标的真实 GUI/游戏未验证；
          MR00 输入清单的完整性未由脚本判断；MR01 文档与源码事实的一致性需人读>
用户批准：<未给出 / 已给出（附原话）>
```

**注意**：MG0 的放行条件是"具备后续指定包实施条件，仍需对应授权"。
后续包（MR03–MR21）**已经实施并各自有证据**，因此本包的实际作用是
**回溯确认基线可信**，而不是"解锁后续工作"。放行仍需你明确批准。
