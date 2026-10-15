# MG0 / MG0-pre 独立复验包 · 执行基线与路由可达性

- **范围**：MR00 / MR01（MG0 基线）+ W1 / W2 / W3（MG0-pre）
- **证据根**：`C:\Users\Administrator\.codex\worktrees\ed41\松光启动器\docs\market-execution\`
- **源码根**：`D:\松光启动器`
- **本包状态**：**证据就绪，等待独立复验**
- **生成方式**：由 `scripts/verify-mg0.ps1` 从证据目录**实际读出**

---

## 一、一条命令跑完全部自动检查

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\verify-mg0.ps1
```

它会：声明证据位置与**格式差异** → 跨全部 run 的证据漂移检查 → 重跑 C# / Node 全量 → **跑台账一致性审计**
→ 打印复验边界。

本次实测输出（完整日志 `verify-mg0-full.log`）：

```
扫描到 330 条记录，覆盖 17 个 run
一致=249  本里程碑内无法解释的漂移=1  被更晚里程碑覆盖的历史记录=80  文件缺失=0
```

---

## 二、**复验方最需要先知道的：三个 run 的格式并不统一**

这一点如果不先说清，复验方会以为"证据丢了"。

| run | 文件数 | `manifest.json` | 哈希清单 | 语义 |
|---|---|---|---|---|
| `MR00/20261002-072004Z` | 142 | ❌ **无** | `baseline/input-manifest.sha256.txt`（**214 条**） | **源码输入清单**，不是"本次改动的文件" |
| `MR01/20261002-072004Z` | 11 | ❌ **无** | ❌ **无** | 产出是 `outputs/docs/*` 与 `mr01-report.md` |
| `W1-W3-route-closure/20261002-182304Z` | 26 | ✅ 有 | `green/changed-files.sha256.txt` | 与后续所有包一致的约定 |

**结论**：W1–W3 完全符合后来的约定；**MR00/MR01 是早期格式**，没有机器可读索引。
本包**只声明这个事实**，不代为补写结论 —— 补写会让复验方以为那是原始证据。

### W1–W3 当时自报的结果（从它的 `manifest.json` 读出）

| 项 | 值 |
|---|---|
| W1 | 10 条路由从 `seg.Length == 4` 块里提升出来（修复 404 死路由） |
| W2 | **16 通过 / 0 失败**（修复前 15 失败 / 1 通过） |
| bisect 冒烟 | exit=0，10/10 PASS（修复前 exit=1、404） |
| C# 全量 | 628 通过 / 0 失败（最终字节） |
| Node 全量 | 184 通过 / 0 失败（最终字节） |
| git status | 1287 → 1290（+3：2 个新测试文件 + `docs/api-reference.md` 新变为修改） |

### W1–W3 当时自报的已知限制（照抄，不要省略）

- 真实 GUI / WebView2 / 真实游戏未验证；
- `saves/{folder}/open` 的真实打开行为未验证（测试刻意走不存在的存档以规避弹窗）；
- GitHub Actions 实际运行未验证；
- RID 发布锁定 NU1004 未处理（属 MR19）——**已在 MR19 结清**。

---

## 三、MG0 / MG0-pre 风险清单 → 证据映射（计划 §八）

| 计划里的风险 | 如何防住 | 复验位置 |
|---|---|---|
| **仍有 404 死路由**（MG0-pre） | W1 把 10 条路由从 `seg.Length == 4` 块里提升出来；W2 用 HTTP 层断言锁死可达性 | `W1-W3…/red/03-route-tests-before.log`（先红）→ `green/14-route-tests-final.log`（后绿） |
| **断言缺失或未先 RED**（MG0-pre） | 有 `red/` 目录且先跑出失败；W2 自报"修复前 15 失败 / 1 通过" | `W1-W3…/red/` 四个文件 |
| **源身份不明**（MG0） | MR00 交付 `baseline/execution-root.json` + `input-manifest.sha256.txt`（214 条）+ `baseline-commands.json` | `MR00…/baseline/` |
| **遗漏依赖**（MG0） | 输入清单覆盖 SDK/锁文件/Node 测试等；白名单八类计数一致（本会话持续自检） | `scripts/source-snapshot-inputs.txt` |
| **配置混入**（MG0） | 执行在隔离快照上、SourceRoot 只读；`RealConfigGuard` 在测试夹具里守护真实配置 | `MR00…/acceptance-report-2026-10-02.md` |
| **失败被吞**（MG0） | MR00 的 `mr00-verification-defects.md` 记录了自查出的缺陷 | `MR00…/mr00-verification-defects.md` |

---

## 四、漂移说明（见 `drift-notes.md`）

| 漂移 | 状态 |
|---|---|
| `dev/PineglowLauncher.Tests/ContentIconServiceTests.cs` | **已修**：MR17 改了它却漏记进变更清单（真实证据遗漏），已补记 |
| `docs/market-task-plan-2026-10-03.md` | **结构性、已解释**：计划是**活文档**，每轮都在改；W1–W3 当时记录了它的哈希，此后任何编辑都会"漂移" |

---

## 五、复验方必须独立判断的事

1. **MR00 的 214 条输入清单是否真的覆盖构建所需输入** —— 脚本只校验哈希，**不判断完整性**。
2. **MR01 的文档产出是否与当时的源码事实一致** —— 需要人读 `outputs/docs/*`。
3. **W1–W3 的真实行为** —— 当时已标未验证（真实 GUI / WebView2 / 真实游戏）。
4. **MR00/MR01 没有 `manifest.json`** —— 请判断这个格式差异是否可接受，或要求回填。

---

## 六、本包文件

| 文件 | 用途 |
|---|---|
| `README.md` | 本文件 |
| `verification-checklist.md` | 逐包验收项 → 命令 → 期望 → 证据 |
| `drift-notes.md` | 两条漂移的逐条解释（一条已修、一条结构性） |
| `verify-mg0-drift-check.log` / `verify-mg0-full.log` | 运行日志 |
| `manifest.json` | 机器可读索引 |
| `scripts/verify-mg0.ps1` | 复验脚本（UTF-8 BOM） |

> **至此四个里程碑都有复验包**：MG0/MG0-pre（本包）、MG1、MG2、MG3。
