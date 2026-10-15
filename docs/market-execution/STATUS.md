# 松光启动器 · 合并状态简报（第 59 轮）

> 本文件是**给决策者的入口**。它把"现在能复现什么、还剩什么、需要你决定什么"压到一页。
> 逐包细节见本目录下各包 `deliverable.md`；总入口见 `README.md`。
> **本简报不宣布放行、不提交、不发布。**

> **第 64 轮（FINAL-REMEDIATION，2026-10-03）更正**：本简报第 44 行的「C# 全量 **835** 通过」是**实现方声明**；
> 独立验收只能证实其中 **830** 项 —— 另外 5 项配置用例会**真实进入 Windows 凭据管理器**（实测 20 次 delete-intent），
> 必须先修 F-02 才能在真实用户会话里跑。另有两处证据哈希不一致（F-05）与 GroupName 文案错误。
> 逐项更正见文末 **「八、第 64 轮更正」**。

---

## 一、一句话结论

**执行侧已做完并留证（28 个包"证据就绪"）；复验侧与批准侧尚未开始（"独立复验通过""用户已批准"均为 0 条）。**
剩下能自动化的活只有两个"半项"，其余都需要**你的决定**。

---

## 二、现在就能复现的验证（我刚跑过，全部 exit 0）

```powershell
# 三个审计
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\audit-plan-ledger.ps1      # 台账 ↔ 证据
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\audit-evidence-tree.ps1     # manifest ↔ 目录内真实文件
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\audit-claimed-tests.ps1     # 声称用例名 ↔ 真实用例名

# 四个里程碑复验
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\verify-mg0.ps1 -SkipTests
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\verify-mg1.ps1 -SkipTests
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\verify-mg2.ps1 -SkipTests
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\verify-mg3.ps1 -SkipTests

# 两套全量测试
dotnet test .\dev\PineglowLauncher.Tests\Pineglow.Launcher.Tests.csproj -c Release
node --test ui\tests\*.test.cjs
```

| 检查 | 结果 |
|---|---|
| 台账审计 | **exit 0**（状态词合法、证据可定位、无孤儿、run-id 真实） |
| 证据树审计 | **exit 0**（7 项全过：manifest 哈希 ↔ 真实文件等） |
| 声称用例审计 | **exit 0**（0 幽灵用例；2 个 A16 前旧名有理由排除） |
| verify-mg0 | exit 0；一致 273 / 漂移 1（已解释）/ 覆盖 138 / 缺失 0 |
| verify-mg1 | exit 0；一致 90 / 漂移 0 / 覆盖 108 / 缺失 0 |
| verify-mg2 | exit 0；一致 273 / 漂移 1（已解释）/ 覆盖 136 / 缺失 0 |
| verify-mg3 | exit 0；一致 90 / 漂移 0 / 覆盖 108 / 缺失 0 |
| C# 全量 | **835 通过 / 0 失败 / 0 跳过** |
| Node 全量 | **238 通过 / 0 失败**（20 个文件） |
| 证据规模 | **55 个包目录 / 54 个 run** |

---

## 三、执行侧：已完成

- **MR00–MR21**：全部**证据就绪**（含 RID/可复现构建、二分定位、诊断、更新通道、性能基线、实例安全等）；
- **缺口登记表**：A1–A14、A16、A20、A21、A22 **共 19 项已关闭**（含 3 处产品代码修复：A16 哈希占用、A19 端口硬编码、A22 替换端点版本元数据）；
- **验收侧工具 3 个**（第 57–58 轮新增）：证据树交叉审计、声称用例名审计、manifest 修复器；
- **测试增量**：C# 从 664 → **835**；Node 从 202 → **238**。

---

## 四、**未完成**（这是关键）

| 侧 | 状态 | 说明 |
|---|---|---|
| **独立复验** | **0 条** | 台账里"独立复验通过"只有**定义行**，**没有任何一条实际条目** —— 按计划自己的口径，"证据就绪 ≠ 独立复验通过" |
| **用户批准** | **0 条** | 同上 |
| 批次放行 | **暂不放行** | 计划第 17 行："具备独立复验条件，等待独立验收；同时暂不放行任何业务任务包" |

**可自动化的剩余工作（只有两项，且都不是"漏做"）**：

| 半项 | 为什么没做 |
|---|---|
| ~~A15 剩余：配置轮转未测~~ **已于第 60 轮关闭** | 用**窄测试缝**（只暴露纯文件轮转、不碰凭据代码）测通了两代轮转语义 ✓。**仍未测**：凭据的「写入 → 读回」往返（第 62 轮已把**写入契约**用源码级守卫钉住 ✓） |
| **A4 剩余**：磁盘不足未模拟 | 本机**无法可靠造出写满的卷**；"文件被占用/只读"那一半已实测 ✓ |

---

## 五、需要你决定的事（按建议顺序）

### 技术决策（3 项，影响剩余工作）

1. **是否给 `SecretStore` 加路径测试缝？**
   → 加了才能安全测 `SaveAsync`（含 `.bak`/`.prev` 轮转 = A15 剩余），也能顺带消除 A19 那条"回写会写真实凭据"的隐患。
   *风险*：触及凭据代码；缝本身在生产恒为 `null`（与 `ConfigStore` 同款）。
2. **是否清理真实 `%APPDATA%\MCMiniLauncher\diagnostics` 里的 259 个测试垃圾？**
   → 那是第 47 轮修掉的隔离缺陷**此前**留下的；删除用户真实目录内容需要你点头（命令带 `-WhatIf`，见 MR17 那包 §四）。
3. **MR19 的结论是否按第 45 轮实测收窄？**
   → 实测：托管程序集在 `/p:DebugType=None /p:DebugSymbols=false` 下**跨路径逐字节可复现**（2 轮独立重建），与原结论"跨路径不可复现"不符。**我只加了批注，没改结论文字**。

### 授权决策（11 个包，全部"未授权"）

| 包 | 内容 | 依赖 |
|---|---|---|
| **MR22** | Windows 内测与改进闭环（P0） | **分发与联系授权** |
| **MR23** | 产品定位、渠道与维护成本 | MR02/MR09/MR12/MR22 |
| **MR13** | Prism/HMCL/PCL 实例迁移 | MR11、MR22 用户需求 |
| **MR14** | 加载器与 Quilt 投入决策 | MR02、MR22 |
| **MR15 / MR16** | Legacy Fabric / Cleanroom 支持 | MR14、MR19 |
| **MR18** | Token 统一与用户主题 | MR17、MR22 |
| **MR24 / MR25** | 非 Windows 可行性 / 单平台 beta | MR19、MR23 / MR24 + 平台范围授权 |
| **MR26 / MR27** | 复杂 Bisect 可视化 / 共享实例等 | MR07/MR09/MR22 · MR13/MR23 |

> 另外 **MR09 真实模型评测**需要"外发调用 + 费用预算"授权；**MR02 招募**需要分发/联系授权。
> 计划第 424 行写明：授权必须**同时**包含任务 ID、执行根、依赖验收记录、允许文件、证据根与停止点 —— **缺一项即视为未授权**。

### 一处待确认的修复

- **A19（端口硬编码修复）** 等 3 处产品代码修复是否**保留**（它们都是"关闭已登记缺口"的最小改动，但属于产品行为变更）。

---

## 六、独立复验建议怎么做

1. 跑第二节的 **9 条命令**（3 审计 + 4 复验 + 2 全量）—— 应全部 exit 0 / 全绿；
2. **抽查 2–3 个包**：打开 `deliverable.md` 的"验证"一节，按其中命令复跑，比对数字；
3. 重点抽查**产品代码被改过**的三个包：
   - `MR04-locked-file-hash-fix/`（A16：哈希阶段占用不再抛出）
   - `MR21-startup-boundaries/`（A19：外壳窗口改用 `LocalApiService.Port`）
   - `MR05-replace-version-metadata/`（A22：替换端点接受版本元数据）
4. 已知**无法在本机复验**的部分（计划已列）：真实 GUI、真实游戏、跨卷恢复、真实 CI、跨机器可复现性。

---

## 七、证据地图

| 位置 | 内容 |
|---|---|
| `README.md`（本目录） | **入口索引**：每个包的 run、关键结果、当前漂移数字 |
| `gap-register.md` | 缺口登记表（A 类本机可关 / B 类真实环境 / C 类授权 / D 类已关） |
| `MG0/MG1/MG2/MG3-verification-package-20261003/` | 四个里程碑复验包（含 `drift-notes.md` 解释漂移） |
| `MG-audit-evidence-tree/`、`MG-audit-claimed-tests/` | 第 57–58 轮新增的验收侧工具及其证据 |
| `docs/market-task-plan-2026-10-03.md`（检出目录） | **台账**（活文档，§8 状态表是机器可审计的） |
| `scripts/`（检出目录） | 3 审计 + 4 复验 + 测量/修复脚本 |

---

**本简报由执行方生成，状态词口径与计划 §8 一致：执行方最多只能声明"证据就绪"。**

---

## 八、第 64 轮更正（FINAL-REMEDIATION，2026-10-03）

### 8.1 这轮之前发生了什么

- **独立验收结论：暂不放行**（`docs/deepseek-harness-final-acceptance-2026-10-03.md`）：
  `TechnicalAcceptance=NOT_PASSED`；`FormalPhaseApproval=NOT_GRANTED`；`ReleaseApproval=NOT_GRANTED`。
- 阻断项：**F-01（P1）**恢复可穿过目录联接写到游戏目录之外；**F-02（P1）**配置测试未隔离 Windows 凭据操作
  （实测 20 次 delete-intent）；**F-03（P2）**回滚失败仍提示"已回滚"；**F-04（P2）**运行中只读 Mod 列表被 400 拦截；
  **F-05（P2）**两份清单与真实字节不符。
- **第 44 行「C# 全量 835 通过」是实现方声明，不是独立复验结论**：独立验收只能证实 **830** 项
  （另 5 项配置用例会真实进入 Windows 凭据管理器）。

### 8.2 本轮做了什么（R0–R4，只修 F-01–F-05）

| 包 | 结果 | 关键证据 |
|---|---|---|
| R0 凭据后端测试隔离 | 读/写/删统一走可注入后端；测试进程默认后端 = 内存实现；**原生调用次数 0** | `FINAL-REMEDIATION/20261003-052200Z/red/credential-intents.jsonl`（RED：20 次 delete-intent）、`green/green-r0-secrets.log`（GREEN：13/13） |
| R1 恢复路径边界 | 目标/祖先/隔离副本的**真实**路径链校验 + 隔离副本必须在本恢复区内 + 写入后复核；拒绝时保留隔离文件并 `ok=false` | `red/red-indep-probes.log`（RED：`200 + ok=true + restored` 且文件写到目录外）、`green/green-independent-probes.log`（20/20） |
| R2 回滚结果真实 | 回滚返回结构化结果，不再吞异常；**只有逐项都恢复才说"已回滚"**，失败带 `needsRecovery`/operationId/逐文件错误 | `green/green-r1-r3-focused.log`（41/41） |
| R3 守卫限定范围 | 只读 `GET …/mods` 运行中返回 **200**；重复启动、运行中移除实例、危险 Mod 变更仍返回 **400** | 同上 |
| R4 证据与回归 | 全量 **875 通过 / 0 失败 / 0 跳过**；Node 238；VerifyHost Release 构建成功；Bisect 冒烟 19/19；全层级清单审计 60 份 manifest / 830 项记录 / **0 自引用 / 2 项已知差异 / 0 其它异常（exit 0）** | `FINAL-REMEDIATION/20261003-052200Z/deliverable.md` 及 `green/`、`20261003-052200Z-audit.json` |

改动只落在 6 个获准文件 + 3 个新文件上：以独立验收的 **281 项快照输入**逐项比对，
**275 项字节一致、6 项变化、0 项越出允许集合、HEAD 未变**（`dirty-protection.json`）。

### 8.3 说法更正（不是改写历史）

1. **测试数量**：原「835 通过」= 830 可安全复跑 + 5 项触碰真实凭据的配置用例；本轮全量为 **875**
   （835 原有 + 20 本轮新增 + 20 独立验收探针），且 5 项配置用例已在内存后端下运行并断言原生调用 0 次。
2. **GroupName**：实际来源是"备份相对备份根的**父目录名**"（`SaveBackupService.ToBackupInfo`），
   不是"从备份文件名解析"。源码注释已更正；行为未变。
3. **F-05 两处清单差异**：`MG0/drift-notes.md`、`MG2/verify-mg2-full.log` 与旧 manifest 不符。
   旧 manifest 与旧 log **原样保留**，差异登记在 `known-mismatches.json` 并在
   `FINAL-REMEDIATION/20261003-052200Z/evidence-corrections.md` 中列出旧值/实测值/来源。
   **没有**静默重算旧 manifest 后宣称"历史一直一致"。
4. **运行态守卫现状**：除已放开的只读 Mod 列表外，另有 **6 个只读路由仍在运行态被 400 拦截**
   （saves 列表、备份列表、打开存档目录、bisect 待处理、resourcepacks、shaderpacks）。
   本轮**未擅自扩项**；若要一并放开，属于产品策略变更，需单独授权。

### 8.4 仍未完成 / 不得外推

- **独立复验**：仍为 0 条。本轮交付只到"具备独立复验条件，等待 Codex 独立验收"。
- **真实游戏 / Avalonia + WebView2 GUI / 真实凭据读写往返 / 自更新真实进程交接 / 远程 CI / 跨平台**：本轮均未验证。
- **校验与写入之间的 TOCTOU**：未关闭，只做了"校验 + 写入后复核 + 拒绝时保留隔离副本"，不声称竞态已彻底关闭。
- 7 个项目验证/审计脚本（`verify-mg0..3`、`audit-*`）本轮**未**重跑，只做了全层级清单审计；如需一并复跑按第二节执行。

**执行侧口径不变：最多只能声明"证据就绪 / 具备独立复验条件"，不宣布放行、不提交、不发布。**
