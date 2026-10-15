# 证据树入口 · 松光启动器 V1 计划执行

> **决策者入口：[STATUS.md](STATUS.md)** —— 一页看清「现在能复现什么、还剩什么、需要你决定什么」。

> 本文件是**唯一入口**。证据树里有 55 个包目录 / 54 个 run、4 个里程碑复验包、16 个脚本（其中 3 个是审计、4 个是里程碑复验）——
> 从这里开始，不要凭记忆找。

- **源码根**：`D:\松光启动器`
- **证据根**：`C:\Users\Administrator\.codex\worktrees\ed41\松光启动器\docs\market-execution\`（即本目录）
- **计划台账**：`D:\松光启动器\docs\market-task-plan-2026-10-03.md`
- **最近更新**：2026-10-03

---

## 一、怎么验（五条命令）

```powershell
cd D:\松光启动器

# 1) 台账一致性：状态词合法、证据可定位、无孤儿目录、run-id 真实
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\audit-plan-ledger.ps1

# 2) MG0 / MG0-pre：MR00 / MR01 / W1 / W2 / W3
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\verify-mg0.ps1

# 3) MG1：MR03–MR07（含 bisect 冒烟）
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\verify-mg1.ps1

# 4) MG2：MR08 / MR09 / MR11 / MR12
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\verify-mg2.ps1

# 5) MG3：MR17 / MR19 / MR20 / MR21
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\verify-mg3.ps1
```

每个脚本都会：汇总该里程碑各包的 `manifest.json` → **证据漂移检查** → 重跑 C# / Node 全量（MG1 另含 bisect 冒烟）
→ 打印"复验边界"（明确写出它**不能**替代什么）。

**最近一次全部结果**（2026-10-03，**四个脚本都在同一天复跑过**）：

| 脚本 | 结果 |
|---|---|
| `audit-plan-ledger.ps1` | exit 0 |
| `verify-mg0.ps1` | exit 0；一致 276 / 本里程碑内漂移 1（已解释：活文档 `docs/market-task-plan-…`，见 MG0 的 `drift-notes.md`）/ 覆盖 143 / 缺失 0 |
| `verify-mg1.ps1` | exit 0；一致 93 / 本里程碑内漂移 0 / 覆盖 113 / 缺失 0；bisect 冒烟 PASS |
| `verify-mg2.ps1` | exit 0；一致 276 / 本里程碑内漂移 1（已解释：`CurseForgeRateLimitTests.cs`，见 MG2 的 `drift-notes.md`）/ 覆盖 141 / 缺失 0 |
| `verify-mg3.ps1` | exit 0；一致 93 / 本里程碑内漂移 **0** / 覆盖 113 / 缺失 0 |
| 全量测试 | **C# 835 通过 / 0 失败 / 0 跳过；Node 238 通过 / 0 失败**（20 个文件） |

> **这些数字会随新包变化，这是正常的**：漂移检查把"全部 run 的记录"按路径取最新一条。
> 新增一个包就会新增记录（"一致"数上升），若它记录了某个文件的新哈希，该文件在更早里程碑里的
> "漂移"就会转成"被更晚包覆盖"（漂移数下降）。**上面的数字测于 2026-10-03 第 63 轮**；
> 复验时请以你自己跑出来的数字为准，并对照各里程碑的 `drift-notes.md` 解释每一条漂移。

> **为什么这四个数字看起来不一样（249 vs 56）**：`verify-mg0` 与 `verify-mg2` 的漂移检查扫描**全部 run**
> （所以"一致"数大，是整个仓库的记录总数）；`verify-mg1` 与 `verify-mg3` 的包清单更小、记录更少。
> **口径不同不等于结果矛盾** —— 每个脚本都会打印自己的口径。
>
> ⚠️ **`verify-mg2.ps1` 在 2026-10-03 被修正过**：它原先只从 **MG2 自己的四个包**构建"最新记录"，
> 于是"某文件后来被更晚的包改过"会被**误报成无法解释的漂移**（实测误报 6 条）。
> 已改为扫描全部 run，与 `verify-mg0/mg1/mg3` 同一模型。**修正前的数字（一致 17 / 漂移 6）
> 保留在 MG2 自己的复验包里**，不要拿它当结论。详见 `MR00-MR27-evidence-index/20261003-140000Z/deliverable.md`。

---

## 一之二、**缺口登记表**（剩余工作的分类入口）

`gap-register.md` —— 把 18 个包里分散的 **102 条**"未做/未验证"汇总、去重、分类：

| 类别 | 数量 | 含义 |
|---|---|---|
| **A 本机可关** | 2（A1/A2/A3/A5/A7/A8/A9/A11/A12 已关闭；A16 已修复；**新增 A17/A18**） | 不需授权、不需真实环境，写代码/测试/文档即可关闭 |
| **B 需真实环境** | 8 | 需真实 WebView2/桌面会话、真实游戏、真实 CI、跨机器 |
| **C 需你授权** | 5 | 外发（费用）、联系用户、分发、签名凭据 |
| **D 已关闭** | 9 | **后续轮次已关闭，但原报告里仍标 🔴** —— 复验方最容易踩 |

**A1 已于第 31–32 轮全部关闭**（`MR05-modcontent-update-wiring-tests/20261003-200000Z`）。原最重要的发现是：`ModContentService`（会写文件、在 Mod 安装/更新路径上）在测试项目里
**引用数为 0** —— 一个没有测试覆盖的数据写入服务，而它所属的 MG1 门禁正是"数据安全"。
详见登记表 §三 A1 与 §八 核实结果（D 类 12/12 通过、A 类抽查 4/4 确认开放）。
## 二、四个里程碑复验包

| 包 | 范围 | 目录 |
|---|---|---|
| **MG0 / MG0-pre** | MR00 / MR01 / W1–W3 | `MG0-verification-package-20261003/` |
| **MG1** | MR03–MR07 | `MG1-verification-package-20261003/` |
| **MG2** | MR08 / MR09 / MR11 / MR12 | `MG2-verification-package-20261003/` |
| **MG3** | MR17 / MR19 / MR20 / MR21 | `MG3-verification-package-20261003/` |

每个包内含：`README.md`（范围/一键复验/漂移口径）、`verification-checklist.md`（逐条可执行 + 风险映射）、
运行日志、`manifest.json`。

**另有一份跨里程碑审计**：`MR00-MR27-ledger-consistency-audit/20261003-130000Z/`（台账 ↔ 磁盘证据）。

---

## 三、已执行包与证据位置（从磁盘实际读出）

| 包 | 证据目录 | run | 关键结果（自报） |
|---|---|---|---|
| MR00 | `MR00/` | 20261002-072004Z | 142 文件；**无 manifest**；哈希清单在 `baseline/input-manifest.sha256.txt`（214 条，语义=源码输入） |
| MR01 | `MR01/` | 20261002-072004Z | 11 文件；**无 manifest、无哈希清单**；产出 `outputs/docs/*` |
| W1–W3 | `W1-W3-route-closure/` | 20261002-182304Z | W1 提升 10 条路由；W2 16/16（修复前 15 失败）；冒烟 10/10 PASS；C# 628 / Node 184 |
| MR02 | `MR02/` | 20261003-110000Z | **准备包**：访谈 0 人、未联系、未发表单；画像全为假设 |
| MR03 | `MR03-remove-from-list/` | 20261002-190000Z | C# 7 / Node 7 |
| MR03 收尾 | `MR03-instance-running-guard/` | 20261003-060000Z | C# 7（RED 4 失败/3 通过）；全量 725 |
| MR03 补测 | `MR03-default-instance-boundary/` | 20261003-250000Z | **默认实例补建边界已查清**：生产语义下移除最后一个实例后**下一次请求会补建默认实例**（count=1），而**测试夹具绕过 `EnsureDefaultInstance`**（count=0）→ 既有绿色用例反映夹具而非生产；另查出**悬空 `SelectedInstanceId` 不被修复**（新增 A17） |
| MR03 补测 2 | `MR03-launch-concurrency-guard/` | 20261003-310000Z | **并发启动守卫已补上**：已在运行 → **400 + "该实例已在运行（进程 N），请勿重复启动。"**（位置在任何网络/派发之前）；陈旧条目/未运行 → 不拦；RED 成立；**并修掉 harness 的端口 TOCTOU flake**（连跑 3 次全量 807/0） |
| MR03 补测 3 | `MR03-atomic-launch-reservation/` | 20261003-320000Z | **守卫升级为原子预约**：`TryBeginLaunch`（`TryAdd`）+ `AbortLaunch`，外壳 `finally` 无条件释放（升级后是 no-op）；7 用例含 **32 并发 → 恰好 1 赢家**、**失败必释放预约**；**RED 未复现**如实记录 |
| MR03 补测 4 | `MR03-deterministic-atomicity-red/` | 20261003-330000Z | **原子性 RED 确定性复现**：测试缝 + `Barrier` 撑开「检查→占位」窗口 → 原子版赢家数 **1**、非原子版 **3** → 断言失败；本类 8 用例；还原到**修复后参照**逐字节一致 |
| MR03 A23 | `MR03-world-backup-scope-fix/` | 20261003-500000Z | **修 Codex 独立探针发现的 2 个缺陷**：备份列表未按世界过滤（worldB 拿到 worldA 的备份）+ **跨世界恢复被接受**（200 并写出 `saves\worldA (1)`，数据安全）→ 已修；**用验证方自己的探针原样复跑 15/15 全过**；3 常驻用例 + RED；C# 835/0 |
| MR04 | `MR04-recoverable-mod-delete/` | 20261002-193000Z | C# 11 / Node 7；含真实 C:↔D: 跨卷 |
| MR04 补测 | `MR04-recovery-edge-cases/` | 20261003-220000Z | **边界情形补测**：重解析点（**用目录联接，不需特权** —— MR04 当年以为做不了）/ 加锁文件 / 保留期策略；4 通过 + 1 跳过；3 个 RED 成立；**并实测出一个缺陷**（加锁文件在哈希阶段抛出，登记表 A16） |
| MR04 修复 | `MR04-locked-file-hash-fix/` | 20261003-230000Z | **A16 修复（产品代码）**：哈希阶段纳入与移动阶段相同的 try/catch，加锁文件改为**逐项报错**；RED=抛出 / GREEN=逐项；**调用方 `LocalApiService.cs:2501` 的逐项报错因此真正生效** |
| MR05 | `MR05-mod-replacement-transaction/` | 20261002-201500Z | C# 9 / Node 11 |
| MR05 A14 | `MR05-recovery-version-display/` | 20261003-410000Z | **恢复区展示「旧版本 → 新版本」**：列表加 `oldVersion`/`newVersion`（旧读被隔离侧车、新读现网侧车）+ 前端展示 + i18n；2 用例 + RED；C# 819/0；**新发现 A22（替换端点不接受版本元数据）** |
| MR05 A22 | `MR05-replace-version-metadata/` | 20261003-420000Z | **替换端点接受可选 `version`** 并透传（写新侧车）→ A14 用例改为**全程走 API** 并断言侧车被写出；RED 成立；C# 819/0；**剩余：前端调用点尚未传 version** |
| MR05 A14 e2e | `MR05-recovery-version-display/` | 20261003-430000Z | **生产路径端到端**：`ModContentService` 更新 → 恢复列表 `oldVersion=1.0 newVersion=2.0`；RED 成立（Expected "2.0" Actual null）；**并更正上轮误判**（前端无更新入口，生产走内容服务且本就传版本）；C# 820/0 |
| MR05 A4 | `MR05-file-in-use/` | 20261003-440000Z | **文件被占用/只读实测**：目标被独占→500+明确错误+旧版本原地；侧车被独占→400 已回滚；只读→200+旧版本进恢复区；立**数据安全不变量**并 RED（移动改删除→2 失败）；C# 823/0 |
| MR07 A6 | `MR07-bisect-safety-guidance/` | 20261003-450000Z | **二分面板常驻安全引导**（临时测试世界/实例副本 + 自行备份 + 启动器不自动备份）+ 中英 i18n；4 用例 + RED（含一次无效 RED 的诚实记录）；Node 238/0 |
| 验收工具 | `MG-audit-evidence-tree/` | 20261003-460000Z | **新增证据树交叉审计**（7 项：deliverable/manifest 存在、runId 一致、**manifest 哈希 ↔ 真实文件**、清单完整性、changed-files 路径存在）；首次运行抓出 **18 处**不一致并修到 0（9 个 manifest 修复，修复前快照存档）；**两个审计 exit 0** |
| 验收工具 2 | `MG-audit-claimed-tests/` | 20261003-470000Z | **声称用例名 ↔ 真实用例名**（抓幽灵用例）：49 份 deliverable / 71 个候选名 → **0 幽灵**（2 个 A16 前旧名有理由排除）；三轮误报调优（全大写/纯小写/尾随下划线）；RED 注入假名 → exit 1 精确点名 |
| MR05 补测 | `MR05-modcontent-update-wiring-tests/` | 20261003-200000Z | **更新接线补测试**：4 用例（RED 2 失败 / GREEN 4 通过）；证明"不产生并列副本 + 保留禁用状态 + 旧版本可恢复 + 失败不丢数据" |
| MR05 补测 2 | `MR05-dependency-install-tests/` | 20261003-210000Z | **依赖递归安装补测试**：6 用例；两个针对性 RED（依赖层守卫 → 2 失败；`visited` → **StackOverflow**，证明它防无限递归）；**首次 RED 未变红，暴露并修正了我的测试设计缺陷** |
| MR06 | `MR06-bisect-file-operation-failures/` | 20261002-211500Z | C# 9（RED 7 失败/2 通过） |
| MR06 补测 | `MR06-bisect-smoke-fault-injection/` | 20261003-240000Z | **故障注入补齐**：`--bisect-smoke` 10→**19 项**检查（注入后必须 409 + `ok=false` + `waitingForReport=false` + 逐项 `recoveryError`）；RED 成立；**MG1 复验脚本完整实跑 exit 0** |
| MR07 | `MR07-bisect-restart-recovery/` | 20261002-223000Z | C# 8 / Node 4（RED 8 失败/0 通过） |
| MR08 | `MR08-diagnostic-report-history/` | 20261002-234500Z | C# 9 / Node 5 |
| MR09 | `MR09-diagnostic-benchmark/` | 20261003-010000Z | 30 例案例集（SHA `D9F55189…`）、dev 20 / holdout 10；**真实模型评测未做** |
| MR09 补测 | `MR09-case-set-representativeness/` | 20261003-280000Z | **案例集自洽性与代表性**：MR09 三条声明**全部验证为真**（冻结哈希一致 / dev∩holdout 家族为空 / holdout 10/10 全 generic）；量化出 **dev 只覆盖 5 家族** → "规则已知正确率不下降"只对这 5 个成立；冻结哈希已做成不变量 |
| MR10 | `MR10-progress-gate-assessment/` | 20261003-120000Z | **判定：无需实现**；产品代码零改动 |
| MR11 | `MR11-mrpack-spec-import-rollback/` | 20261003-023000Z | C# 21 / Node 5 |
| MR12 | `MR12-curseforge-rate-limit/` | 20261003-043000Z | C# 10（RED 9 失败/1 通过） |
| MR12 补测 | `MR12-outbound-endpoint-audit/` | 20261003-270000Z | **出口逐一审计 + 6 个守卫**：CF API **3/3 在策略内**、**图标服务不碰 API**（结构上无法绕过）、新闻/AI 各有策略；**新发现 A18：Modrinth 7 个出口全部无策略**（与 CF 保护不对称） |
| MR17 | `MR17-accessibility-keyboard/` | 20261003-100000Z | 9 契约用例（RED 9 失败/0 通过） |
| 测试隔离 | `MR17-test-isolation-real-appdata/` | 20261003-360000Z | **修掉测试污染真实 `%APPDATA%`**：根因 = `AiCancelAndRedirectTests` 自建夹具**漏重定向诊断存储根**；已修 + **新增真实目录守卫**；**全量 817/0 且真实目录 259 → 259 未新增**；残留 259 个垃圾未清理（待同意） |
| MR17 A13 | `MR17-makeel-doubles-and-taborder/` | 20261003-400000Z | **替身补齐做成守卫**（立刻抓出 2 个未记录文件 → 共补 5 个）+ **Tab 顺序静态清单**（侧栏九项 + 静态 chrome + 总数 15）；Node 234/0；**2 个 RED 成立** |
| 夹具清点 | `MR17-fixture-isolation-audit/` | 20261003-370000Z | **6 个自建夹具的四根覆盖表** + **扩展 `RealConfigGuard` 把 diagnostics/recovery/bisect 纳入比较**（自动保护所有主夹具测试）+ 修掉我自己那个（四根全未隔离）；全量 817/0 且真实三根未变；**实测 recovery/bisect 目录不存在 → 没重定向≠在写** |
| 夹具隔离收尾 | `MR17-fixture-isolation-audit/` | 20261003-380000Z | **新增共享助手 `TestIsolationRoots`**，把剩余 3 个自建夹具接上（News×2 + 图标类两个直接 Start 用例）→ **5/6 夹具四根全覆盖**；全量 817/0 且真实三根未变；**剩余**：`AiCancelAndRedirectTests` 的 rec/bisect（潜在） |
| 夹具隔离关闭 | `MR17-fixture-isolation-audit/` | 20261003-390000Z | **A21 完全关闭**：助手加 `InstallRecoveryAndBisectCurrent()`（只补 rec/bisect，不干扰该夹具自有的诊断 scope）→ **6/6 夹具四根全覆盖**；全量 817/0 且真实三根未变 |
| MR19 | `MR19-reproducible-build-rid/` | 20261003-070000Z | C# 3；RID 锁定冻结；**托管程序集逐字节可复现（仅同路径，见补测 4）** |
| MR19 补测 | `MR19-singlefile-reproducibility/` | 20261003-150000Z | **单文件 exe 两次独立发布逐字节一致**（89,490,941 bytes，SHA `C99101C8…0EC13`）；锁文件副作用已还原；跨路径/跨机器未测 |
| MR19 补测 2 | `MR19-singlefile-pathcheck/` | 20261003-160000Z | **跨路径不可复现（实测）**：同一源码在 8/45/93 字符路径下发布 → **三个不同 SHA**（大小相同，仅 0.0019% 字节不同，首个差异 `0x1AC`）；源码输入经证明完全相同 |
| MR19 补测 3 | `MR19-singlefile-pathlen-discriminator/` | 20261003-170000Z | **判别实验**：同长度仅差一字母 → 产物不同（**路径字符串被逐字嵌入**，可在 exe `0xCEEB0D` 读到完整路径）；并**修正** `0x1AC` 的归因（不是长度效应，跨盘对比才出现） |
| MR19 补测 4 | `MR19-managed-assembly-pathcheck/` | 20261003-180000Z | **托管程序集也是路径相关的**（98 字符三路径 → 三个不同 DLL SHA）→ **MR19 头条结论收窄为"同路径"**；同路径相隔 2 分钟重建完全相同（排除时间混淆） |
| MR19 补测 5 | `MR19-ci-workflow-invariants/` | 20261003-260000Z | **CI 不变量本地化**：13 个用例（守卫存在/会失败/覆盖两个 lock/**顺序在发布之前**/RID 发布不得锁定/不得引用 RID 专用 lock/冒烟仍接入/SDK 由 global.json 固定）+ **actionlint 1.7.7 实跑 exit 0**；CI YAML 系**已跟踪**文件（不需白名单） |
| MR19 补测 6 | `MR19-dll-byte-diff-mechanism/` | 20261003-340000Z | **跨路径差异机制完全定位**：路径字符串 = **PDB 绝对路径（UTF-8）** @`0x341B43`/`0x3426EB` + PDB 的 SHA256；差异**恰好 6 处/73 字节**（PE 时间戳 + 3 GUID + PDB 路径 + PDB SHA）；**加 `DebugType=None` 后跨路径逐字节相同（2 轮）→ MR19 结论需收窄** |
| MR20 | `MR20-self-update-recovery/` | 20261003-080000Z | C# 19（含 3 个真实执行替换脚本）；Node 5 |
| MR20 补测 | `MR20-config-preservation/` | 20261003-350000Z | **旧配置不被重置**：新增 `ConfigStore` 路径测试缝，5 用例（字段全保留 / 空 updateUrl 才补默认 / 不注入默认实例 / 回写也保留 / **不得碰真实凭据**）；RED 成立；**新发现 A19（LoadAsync 回写会写真实凭据存储）与 A20（有测试写进真实 %APPDATA%）** |
| MR20 A15 收尾 | `MR20-config-backup-rotation/` | 20261003-480000Z | **主备轮转留证**（A15 完全关闭）：给 `ConfigStore` 加**窄测试缝**（只暴露纯文件轮转、不碰凭据）；5 用例钉住两代语义 + "备份失败不影响主写入"；RED 成立；C# 828/0 |
| MR20 A19 | `MR20-secret-persistence-contract/` | 20261003-490000Z | **凭据写入契约**（源码级守卫，不碰凭据代码）：7 处写入 + 3 个旧键清空 + 字面量↔常量一致 + 键名与落盘格式；RED 成立（3 失败）；C# 832/0；**往返仍未测** |
| MR21 | `MR21-install-launch-perf-baseline/` | 20261003-090000Z | **只测量**（`productCodeChanged=False`）；5×20 样本；基线已冻结 |
| MR21 补测 | `MR21-startup-boundaries/` | 20261003-290000Z | **启动边界实测**：端口冲突抛异常但 `App.cs` 兜住不崩、`IsRunning=false`、换端口可重启；UI 根目录缺失仍启动；**修掉真实缺陷**：外壳窗口硬编码端口 → 已改用 `LocalApiService.Port`（否则端口覆盖时**静默降级**）；**另修掉一个既有 flaky 测试** |
| MR21 补测 2 | `MR21-unwritable-directory/` | 20261003-300000Z | **目录不可写实测**：`mods` 为文件 → **500 + 可读错误**（不假装成功）；`<file>\sub` 注册 → **400 "目录不存在"**；两次失败后目录树**逐字节不变**；3 个 RED（其中 RED-2 未变红 → 补 RED-3 才抓住"静默成功"） |

**格式差异提醒**：只有 **MR00/MR01** 没有 `manifest.json`；MR00 的哈希清单名字与语义都不同；
MR01 完全没有哈希清单。详见 `MG0-verification-package-20261003/README.md` §二。

---

## 四、门槛审计：剩余包**没有一个是"被悄悄漏掉"的**

计划 §7.4 的每个条件包都有独立门槛。逐条对照当前证据：

| 包 | 计划门槛 | 门槛状态 | 阻塞点 |
|---|---|---|---|
| MR13 实例迁移 | MR11 + **MR22 证明用户需求** | ❌ 未达 | MR11 ✅；**MR22 未执行**（需分发/联系授权 + 真实用户） |
| MR14 加载器投入决策 | MR02、**MR22** | ❌ 未达 | MR02 ✅（准备包）；**MR22 未执行** |
| MR15 Legacy Fabric | **MR14 通过**且 MR19 就绪 | ❌ 未达 | MR14 未达（依赖链） |
| MR16 Cleanroom | 同上 | ❌ 未达 | 同上 |
| MR18 Token/主题 | MR17 + **用户需求** | ❌ 未达 | MR17 ✅；**无任何用户证据**（MR02 是准备包、访谈 0 人） |
| MR24 非 Windows 宿主 | MR19、**MR23** | ❌ 未达 | MR19 ✅；**MR23 未执行** |
| MR25 单平台 beta | **MR24 通过** + 平台范围授权 | ❌ 未达 | 依赖链 + 需授权 |
| MR26 Bisect 可视化评估 | MR07、MR09、**MR22** | ❌ 未达 | MR07 ✅、MR09 ✅（工具层）；**MR22 未执行** |
| MR27 共享实例等决策 | **MR13、MR23** | ❌ 未达 | 均未达 |
| MR22 Windows 内测 | V1 全部必需任务 + **分发与联系授权** | ❌ 未达 | **需你授权** + 真实用户 |
| MR23 定位与渠道 | MR02、MR09、MR12、**MR22** | ❌ 未达 | 前三项 ✅；**MR22 未执行** |

**结论**：V1 的可执行工作已全部完成；剩余 11 个包的阻塞点**全部**归结为三类——
**① 需要你授权（用户研究/内测/外发）② 需要真实用户 ③ 依赖链未解锁**。
没有任何一个是"忘了做"。

---

## 五、需要你决定的事（按建议顺序）

| # | 决定 | 为什么现在需要 |
|---|---|---|
| 1 | **MR09 真实模型评测授权**（外发与费用额度） | MG2 的结论部分唯一的缺口；也是 MR10 翻转条件之一的数据来源 |
| 2 | **MR02 招募授权**（联系用户/发外部表单，8 项清单见 `MR02/…/recruiting-plan.md` §四） | 解锁 MR14/MR18/MR22/MR23/MR26 |
| 3 | **Java 缓存修复子包**（实测本地诊断 **−95%**：327ms → ~16ms） | 用户点"诊断"时的可感知延迟；按纪律需独立子包授权 |
| 4 | **MR22 内测**（分发与联系授权 + 真实用户） | 解锁 MR13/MR15/MR16/MR24/MR25/MR26/MR27 |

---

## 六、读证据的几条硬规则（避免误判）

1. **"证据就绪"≠"已通过"**。每个 `deliverable.md` 都有"明确未做与未验证"一节，必须照抄进结论。
2. **漂移数不为 0 不等于失败**。每条漂移都要在对应包的 `drift-notes.md` 里读到解释；
   目前共 4 条已解释（2 条 MG2、1 条 MG3、1 条 MG0），其余里程碑为 0。
3. **RED 是构造出来的**，各包 `deliverable.md` 写明了构造方式；"删掉文件"只会得到编译错误，不算 RED。
4. **含中文的 `.ps1` 必须带 UTF-8 BOM**（`edit` 工具会剥掉它；PowerShell 5.1 会按 ANSI 解码导致乱码）。
5. **不要改写封存证据**（`changed-files.sha256.txt` 等）。事后修改会让整条证据链失效；
   发现问题应当登记在新 run 的 `drift-notes` 里。
6. **活文档不入哈希清单**：`docs/market-task-plan-2026-10-03.md` 是台账，每轮都在改；
   把它记进变更清单必然漂移（W1–W3 当时这么做了，已在 MG0 的 `drift-notes.md` 说明）。

---

## 七、脚本清单（都可直接跑）

| 脚本 | 作用 | 实跑证据在哪 |
|---|---|---|
| `scripts/audit-plan-ledger.ps1` | 台账一致性（四项检查，exit 0/1） | `MR00-MR27-ledger-consistency-audit/20261003-130000Z/ledger-audit.log` |
| `scripts/audit-evidence-tree.ps1` | 证据树交叉一致性审计（7 项：deliverable/manifest、runId、**manifest 哈希 ↔ 真实文件**、清单完整性、changed-files 路径） | `MG-audit-evidence-tree/20261003-460000Z/` |
| `scripts/audit-claimed-tests.ps1` | 声称用例名 ↔ 真实用例名（抓幽灵用例） | `MG-audit-claimed-tests/20261003-470000Z/` |
| `scripts/verify-mg0.ps1` | MG0/MG0-pre 复验 | `MG0-verification-package-20261003/verify-mg0-full.log` |
| `scripts/verify-mg1.ps1` | MG1 复验（含 bisect 冒烟） | `MG1-verification-package-20261003/verify-mg1-full.log` |
| `scripts/verify-mg2.ps1` | MG2 复验 | `MG2-verification-package-20261003/verify-mg2-full.log` |
| `scripts/verify-mg3.ps1` | MG3 复验 | `MG3-verification-package-20261003/verify-mg3-full.log` |
| `scripts/measure-mr21.ps1` | 性能基线测量（环境冻结 → 5 次观测 → 冻结预算） | `MR21-install-launch-perf-baseline/20261003-090000Z/measure-mr21.log` |
| `scripts/run-diagnostic-benchmark.ps1` | MR09 诊断评测（**不调用真实模型**） | `MR09-diagnostic-benchmark/20261003-010000Z/green/03-benchmark-script.log` |
| `scripts/verify-source-snapshot.ps1` | MR00 源码快照校验（隔离临时目录构建） | **MR00 那轮**：`MR00/20261002-072004Z/logs/snapshot-verify/verify-summary.json` + `logs/snapshot-verify-console.txt` + `fixed-script-run{,-2}-cjk-evidence/` |

**关于最后一条的准确表述**：`verify-source-snapshot.ps1` **不是在本会话跑的**，它的实跑证据在 **MR00 那一轮**
（含三次运行的日志、`snapshot-manifest.sha256.txt`，以及当时对该脚本自身 66 行缺陷的修复补丁
`MR00…/outputs/verify-source-snapshot-mr00-fix.patch`）。本索引**只引用**那份证据，**没有**在本会话复跑它
（它要复制工作区 + 全套构建 + 单文件发布，属重活；如需复跑请另行安排）。

其余七个脚本都**已在本会话用原生 shell（Windows PowerShell 5.1）实跑过**，日志路径见上表。
