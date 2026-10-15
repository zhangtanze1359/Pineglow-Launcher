# 缺口登记表 · V1 各包"未做/未验证"的汇总与分类

> **为什么需要它**：这些条目分散在 18 个包的 `deliverable.md` 里（机械抽取共 **102 条**），
> 而且**有些早已关闭、旧报告里却仍标 🔴** —— 复验方会去追已经关掉的项。
> 本表把它们汇总、去重、分类，并明确标出"已关闭"的那些。

- **生成方式**：机械抽取每个包 `deliverable.md` 的"明确未做与未验证"节（脚本化，见 §五），
  再由人**逐条归类**（归类规则见 §一）。
- **最近更新**：2026-10-03（第 30 轮）

---

## 一、归类规则（先定规则，再归类）

| 类别 | 判据 |
|---|---|
| **A 本机可关** | 只需在本机写代码/测试/文档即可关闭：**不需要**外发、不需要真实用户、不需要桌面会话或真实游戏 |
| **B 需真实环境** | 需要：真实 WebView2/桌面会话、真实 Minecraft、真实 GitHub Actions、真实账号、真实安装目录、跨机器 |
| **C 需你授权** | 需要：外发到外部服务（费用）、联系用户、分发产物、签名凭据 |
| **D 已关闭** | 后续轮次已关闭，但**原报告里仍标 🔴** —— 保留在此以免复验方追已关项 |

---

## 二、D 类：**已关闭，但旧报告里仍标 🔴**（复验方最容易踩）

| 原报告里的 🔴 | 实际状态 | 关闭于 |
|---|---|---|
| MR03："实例正在运行"守卫未实现 | ✅ **已关闭** | `MR03-instance-running-guard/20261003-060000Z` |
| MR08：UI 未接入 / Node 测试未做 | ✅ **已关闭** | MR08 第 7 轮收尾（`MR08…/green/` 的 UI 与 `diagnostic-history.test.cjs`） |
| MR11：导入取消 | ✅ **已关闭** | MR11 第 10 轮补齐（`ImportCancellationTests` 6 例 + `import-cancel.test.cjs` 5 例） |
| MR19：单文件自包含 exe 的可复现性 | ✅ **已测**（结论：**跨路径不可复现**） | `MR19-singlefile-reproducibility/…150000Z`、`…pathcheck/…160000Z`、`…pathlen-discriminator/…170000Z` |
| MR20：UI 未接线 | ✅ **已关闭** | MR20 第 17 轮收尾（`update-recovery.test.cjs` 5 例 + 界面展示"上次替换未成功"） |
| W1–W3：`dotnet publish` 就地改写 `packages.lock.json` | ✅ **已处理**（冻结为已知副作用 + 守卫 + 实验后还原） | `MR19…070000Z`；本会话后续实验均自动还原 |
| MR10（原"未授权"）：是否需要进度协议 | ✅ **已判定：无需实现** | `MR10-progress-gate-assessment/…120000Z` |
| MR05/MR04 提到的 `MR06–MR07` 依赖 | ✅ 均已完成 | `MR06…211500Z`、`MR07…223000Z` |
| MR19：托管程序集"逐字节可复现"（**过度声称**） | ⚠️ **已收窄**：只在**同一路径**下成立 | `MR19-managed-assembly-pathcheck/…180000Z` |

> **注意**：D 类条目**没有**被改写回原报告（封存证据不改写）；本表就是它们的更正入口。

---

## 三、A 类：**本机可关（不需授权）** —— 真正可执行的待办

按"对里程碑门禁的影响"排序（MG1 数据安全 > MG3 候选 > 其它）。**A1 已于第 31–32 轮关闭；A2/A3 已于第 33 轮关闭（重解析点/占用/保留期）**（`MR05-modcontent-update-wiring-tests/20261003-200000Z`）。

| # | 归属 | 缺口 | 关闭它需要做什么 |
|---|---|---|---|
| ~~A1~~ ✅ | **MR05** | ~~`ModContentService` 的更新接线没有自动化测试~~ **已关闭**：4 个用例，RED 2 失败 / GREEN 4 通过 | 已完成；**剩余部分已于第 32 轮关闭**：`InstallWithDependenciesAsync` 6 个用例（`MR05-dependency-install-tests/20261003-210000Z`） |
| ~~A2~~ ✅ | **MR04** | 保留期到期行为未验证 | 补用例：伪造过期记录 → 断言清理/保留策略 |
| ~~A3~~ ✅ | **MR04** | 磁盘不足 / 文件被占用 / 符号链接与重解析点拒绝 | 补故障注入用例（占用可真实制造；磁盘不足可用配额或注入） |
| ~~A4（文件被占用部分）~~ ✅ | **MR05** | ~~磁盘不足 / 文件被占用~~ **部分关闭（第 55 轮）**：**文件被占用/只读已实测**（目标被独占→**500 + 明确错误 + 旧版本原地**；侧车被独占→**400 `[quarantine_failed] … 已回滚`**；只读→**200 + 旧版本进恢复区**），并立**数据安全不变量**（旧版本必须原地或进恢复区，否则就是数据丢失）；RED 把隔离的移动改成删除 → 2 失败 | 见 `MR05-file-in-use/20261003-440000Z`。**磁盘不足仍未模拟**（本机无法可靠造满卷） |
| ~~A5~~ ✅ | **MR06** | ~~`VerifyHost --bisect-smoke` 未按计划扩展故障注入~~ **已关闭（第 35 轮）**：新增故障注入阶段，smoke 从 10 项扩到 **19 项**；RED 证明断言有牙齿；MG1 复验脚本完整实跑 exit 0 | 已完成；见 `MR06-bisect-smoke-fault-injection/20261003-240000Z`。**剩余**：恢复阶段失败未注入（其失败分支不清 `waitingForReport`，属行为问题，只记录） |
| ~~A6~~ ✅ | **MR07** | ~~"临时测试世界/实例副本"的引导未实现~~ **已关闭（第 56 轮）**：二分面板顶部加**常驻**安全引导（不在任何分支里 → 恢复场景同样可见）+ 中英 i18n 两键；文案明确说清三件事（临时测试世界/实例副本、自行备份、**启动器不自动备份**）；4 用例 + RED | 已完成；见 `MR07-bisect-safety-guidance/20261003-450000Z`。**剩余**：真实 GUI 渲染未验证；"不自动进入真实世界/不承诺禁用世界保存"仍未实现（世界相关逻辑本包未动） |
| ~~A7（默认实例补建部分）~~ ✅ | **MR03** | ~~移除后的默认实例补建未验证~~ **已查清（第 36 轮）**：生产语义下**下一次请求会补建默认实例**（count=1），而测试夹具绕过 `EnsureDefaultInstance`（count=0）—— 既有绿色用例反映的是**夹具**而非生产 | 已完成；见 `MR03-default-instance-boundary/20261003-250000Z`。**启动并发守卫已于第 42 轮补上**（`MR03-launch-concurrency-guard/20261003-310000Z`：已在运行→400 + 可读错误，位置在任何网络/派发之前；3 用例 + RED；**并修掉 harness 的端口 TOCTOU flake**）。**剩余**：重启后运行态（进程内内存态）、**守卫严格原子性已于第 43 轮补上**（`MR03-atomic-launch-reservation/20261003-320000Z`：`TryBeginLaunch` 原子预约 + `AbortLaunch`；7 用例含 32 并发；**RED 已于第 44 轮做成确定性复现**（`MR03-deterministic-atomicity-red/20261003-330000Z`：测试缝 + Barrier 撑开窗口 → 非原子版赢家数 3 → 失败）） |
| ~~A8（出口审计部分）~~ ✅ | **MR12** | ~~图标/更新路径的限流未逐一审计~~ **已关闭（第 38 轮）**：**CF API 3 个请求点全在策略内**；**图标服务不碰 CF/Modrinth API**（取服务端构建的可信 URL 映射）→ 结构上不可能绕过；新闻/AI 各有策略。**并新增 6 个守卫用例**防"新开绕过出口" | 已完成；见 `MR12-outbound-endpoint-audit/20261003-270000Z`。**未做**：请求合并/缓存、"稍后重试"按钮、分页游标（属功能增量） |
| ~~A9~~ ✅ | **MR09** | ~~保留集偏向 / 案例集代表性 / 评分器只做文本级判定~~ **已关闭（第 39 轮）**：MR09 三条声明**全部验证为真**（冻结哈希一致、dev∩holdout 家族为空、holdout 10/10 全 generic）；并给出量化代表性（dev 5 家族 / holdout 10 家族、桶分布、20 种 category）；**关键边界**：dev 只覆盖 5 个家族 → "规则已知正确率不下降"只对这 5 个成立 | 已完成；见 `MR09-case-set-representativeness/20261003-280000Z`。**未做**：真实模型评测（需授权）、评分器改进 |
| ~~A10~~ ✅ | **MR19** | ~~`0x1AC` 成因（未复现）、`0x9AD088` 语义、DLL 里路径字符串位置~~ **已关闭（第 45 轮）**：**路径字符串 = PDB 绝对路径（UTF-8）在 `0x341B43`/`0x3426EB`，后接 PDB 的 SHA256**；跨路径差异**恰好 6 处/73 字节**（PE 时间戳 + 3 处 GUID + PDB 路径 + PDB SHA256），全部可归因；**并实测：加 `/p:DebugType=None /p:DebugSymbols=false` 后跨路径逐字节相同（2 轮独立重建）** | 已完成；见 `MR19-dll-byte-diff-mechanism/20261003-340000Z`。**`0x1AC` 仍未复现**（本轮为 73 字节）；**单文件 exe 未重测**；**MR19 结论需收窄**（见报告 §五） |
| ~~A11~~ ✅ | **MR19** | ~~actionlint CI 门禁未加；CI YAML 是否纳入白名单~~ **已关闭（第 37 轮）**：CI YAML **是已跟踪文件**（不需白名单）；改为把 CI 不变量写成 **13 个本地用例**（守卫存在/会失败/覆盖两个 lock/**顺序在发布之前**/RID 发布不得锁定/不得引用 RID 专用 lock/冒烟仍接入/SDK 由 global.json 固定），并**实跑 actionlint 1.7.7（exit 0）** | 已完成；见 `MR19-ci-workflow-invariants/20261003-260000Z`。**剩余**：真实 GitHub Actions 运行仍未验证；本包**没有**改工作流（不引入无法验证的 CI 步骤） |
| ~~A12（端口/UI 根目录部分）~~ ✅ | **MR21** | ~~目录不可写/端口冲突未测量~~ **已关闭（第 40–41 轮）**：**端口冲突**实测抛出 `HttpListenerException`、`App.cs` 兜住不崩、`IsRunning=false`、换端口可重启且 `/api/health→200`；**UI 根目录缺失**仍能启动；**并修掉一个真实缺陷**：外壳窗口硬编码端口导致 `PINEGLOW_API_PORT` 覆盖时**静默降级**（已改用 `LocalApiService.Port`） | 已完成；见 `MR21-startup-boundaries/20261003-290000Z`。**目录不可写已于第 41 轮关闭**（`MR21-unwritable-directory/20261003-300000Z`：`mods` 为文件→500 可读错误；`<file>\sub` 注册→400；两次失败后目录树逐字节不变）。**剩余**：WebView2 缺失路径（本机已安装）、真正的 ACL 拒绝未测、"端口被占用"的可操作提示未做 |
| ~~A13~~ ✅ | **MR17** | ~~其它 `makeEl` 替身未补齐、tab 顺序未逐屏记录~~ **已关闭（第 51 轮）**：判据写成**守卫**后立刻抓出 **2 个 MR17 未记录的文件**（`launcher-behavior`/`news-behavior`）→ 共补齐 **5 个替身**；Tab 顺序做成**机器校验的静态清单**（侧栏九项顺序 + 静态 chrome 顺序 + 总数 15）；**2 个 RED 成立** | 已完成；见 `MR17-makeel-doubles-and-taborder/20261003-400000Z`。**剩余**：真实浏览器的逐屏 Tab 遍历、动态渲染内容的顺序 |
| ~~A14~~ ✅ | **MR05** | ~~侧车的「版本/变更」展示~~ **已关闭（第 52 轮）**：恢复列表新增 `oldVersion`/`newVersion`（旧版本读被隔离侧车、新版本读现网侧车，路径基准按 reason 区分，读不到一律 null）；前端恢复区面板展示「版本：旧 → 新」+ 中英 i18n；2 用例 + RED | 已完成；见 `MR05-recovery-version-display/20261003-410000Z`。**受 A22 限制**：API 驱动的替换拿不到 newVersion |
| ~~A15~~ ✅ | **MR20** | ~~旧配置/凭据不被重置、主备切换留证~~ **部分关闭（第 46 轮）**：新增 `ConfigStore` 配置路径测试缝；5 个用例验证**旧配置字段全部保留**（含「空 updateUrl 才补默认值」「已有实例不注入默认实例」），RED 成立 | 已完成；见 `MR20-config-preservation/20261003-350000Z`。**「主备切换留证」已于第 60 轮关闭**：给 `ConfigStore` 加**窄测试缝**（只暴露纯文件轮转、**不碰凭据代码**），5 用例钉住两代轮转语义（含"备份失败不影响主写入"）；RED 成立。见 `MR20-config-backup-rotation/20261003-480000Z`。**仍未测**：`SaveAsync` 的凭据写入路径（A19） |
| **A17** | **MR03/InstanceService** | **EnsureDefaultInstance 不修复悬空的 SelectedInstanceId**（GetSelectedInstance 有兜底 → if 分支永不触发）→ GET /api/instances 会**原样返回悬空 id**（仅当配置由**非删除路径**写坏时出现；删除路径自己会重指向） | 决定是否顺手修好（行为决策）；若要修，InstanceDefaultInstanceBoundaryTests 用例 3 会标出该变化 |
| **A18** | **Modrinth 出口** | **Modrinth 侧完全没有限流/并发策略**（pi.modrinth.com 有 **7 个出口**：ModContentService 3、ModrinthModpackService 2、ResourceContentService 2），而 CurseForge 有 CurseForgeRequestPolicy（并发上限 2 + Retry-After + 退避）→ **同一类操作走两个源，只有一个受限流保护** | 建议把并发闸+退避抽成通用 HttpRequestPolicy 两个源共用，或为 Modrinth 增加同类策略（设计变更，需确认） |
| **A19** | **ConfigStore/SecretStore** | **`LoadAsync` 会回写**（`EnsureAccounts` 补默认账号即触发）→ 回写走 `SaveAsync`/`SaveCore` → **写真实 `%APPDATA%` 的凭据存储**（`SecretStore` **无测试缝**）。本包实测**未造成损害**（凭据值恰好全为空串、`secrets.json` 未创建），但只要有**非空**凭据就会写进用户真实存储 | 给 `SecretStore` 加路径测试缝（同 `ConfigStore` 款式），使 `SaveAsync`（含 `.bak`/`.prev` 轮转）可在测试中安全验证。**触及凭据代码，需确认** |
| ~~A21~~ ✅ | **测试隔离（清点）** | **清点已完成（第 48 轮）**：6 个自建夹具的四根覆盖已列成表；**扩展 `RealConfigGuard` 把 diagnostics/recovery/bisect 纳入比较**（该守卫在 `DisposeAsync` 已断言 → **自动保护所有用主夹具的测试**）；并修掉**我自己**第 40 轮那个（四根全未隔离）。**实测 recovery/bisect 目录根本不存在 →「没重定向」≠「实际在写」**，所以选择加共享守卫而非盲目逐个补 | 见 `MR17-fixture-isolation-audit/20261003-370000Z`。**剩余**：`ContentIconServiceTests`（四根全未隔离）、`NewsFakeIpConfigTests`/`NewsLinkHandlerTests`（recovery/bisect 未隔离）—— **第 49 轮已把其中 3 个接上共享助手**（`TestIsolationRoots`，见 `MR17-fixture-isolation-audit/20261003-380000Z`）→ **5/6 夹具四根全覆盖**。**已完全关闭（第 50 轮）**：给助手加 `InstallRecoveryAndBisectCurrent()` 补上最后一格 → **6/6 夹具四根全覆盖**（`MR17-fixture-isolation-audit/20261003-390000Z`）。**剩余**：残留 259 个垃圾未清理（需同意）；`cache`/`content-icons` 无守卫 |
| ~~A22~~ ✅ | **MR05/替换端点** | ~~`/mods/{file}/replace` 不接受版本元数据~~ **已关闭（第 53 轮）**：端点接受**可选** `version`（source/projectId/versionId/versionNumber/fileName/displayName）并透传给 `ReplaceAsync`（写新侧车）；`source` 解析沿用仓库既有惯例；不传时行为与修复前一致（向后兼容）。**A14 用例已改为全程走 API** 并断言新侧车被写出，RED 成立 | 已完成；见 `MR05-replace-version-metadata/20261003-420000Z`。**更正（第 54 轮）**：原先记的"前端调用点尚未传 version"**不成立** —— 前端**没有**更新入口，生产更新走 `ModContentService.cs:140-141`（**本就传了真实版本**），故 A14 在真实产品里生效；该 HTTP 端点只是**测试/自动化缝**。见 `MR05-recovery-version-display/20261003-430000Z` |
| ~~A20~~ ✅ | **测试隔离** | ~~有测试把文件写进真实 `%APPDATA%`~~ **已关闭（第 47 轮）**：根因 = `AiCancelAndRedirectTests.cs` 的自建 `TestHarness` **只重定向了配置存储、漏了诊断存储根**（该缝本已存在）；已修复并**新增真实目录守卫**（文件数变化即失败）。**全量跑完真实目录 259 → 259 未新增** | 已完成；见 `MR17-test-isolation-real-appdata/20261003-360000Z`。**残留 259 个测试垃圾未清理**（需用户同意） |
| ~~**A16**~~ ✅ | **MR04** | ~~加锁文件在 `HashFile`（第 136 行）就抛出 `IOException`，异常冲出 `QuarantineAsync`~~ **已修复（第 34 轮）**：哈希纳入与移动阶段相同的 `try/catch`，逐项记为 `failed`；调用方 `LocalApiService.cs:2501` 的逐项报错因此**真正生效** | 已完成；见 `MR04-locked-file-hash-fix/20261003-230000Z`（RED=抛出 / GREEN=逐项报错） |

---

## 四、B 类：需真实环境（本机做不了或只能模拟）

| 归属 | 缺口 | 需要什么 |
|---|---|---|
| MR03/MR04/MR05/MR06/MR07/MR08/MR11/MR12/MR17/MR19/MR20/MR21/W1–W3 | **真实 GUI / WebView2 页面交互** | 桌面会话 + 人工操作 |
| MR03/MR06/MR07/MR21 | **真实启动 Minecraft / 真实存档流程** | 真实游戏 + Java + 用户操作 |
| MR19/W1–W3 | **真实 GitHub Actions 运行** | 真实 runner（本机无） |
| MR19 | linux-x64 / osx-x64 的 RID 发布 | 对应平台或容器 |
| MR20 | 真实安装目录替换、真实重启、Unix 分支、断电/杀进程 | 真机 + 授权 |
| MR21 | 真实 GUI 首屏/渲染/UI 阻塞、真实账号流程、无 .NET runtime 的首次启动、跨机器可比性 | 桌面会话 + 干净机器 |
| MR07 | 真实进程中断 | 真机 |
| MR09/MR10 | 真实模型往返延迟与质量 | 见 C 类（需外发授权） |

---

## 五、C 类：需你授权

| 归属 | 缺口 | 需要什么授权 |
|---|---|---|
| MR09 | **真实模型评测**（MG2 结论部分唯一缺口） | 外发 + 费用额度 |
| MR10 | Java 查找的缓存修复（实测 **−95%** 诊断延迟） | 按计划纪律：瓶颈修复走**独立子包授权** |
| MR20 | 签名清单 / 身份认证 | 签名凭据 |
| MR22 | Windows 内测（分发 + 联系） | 分发与联系授权 + 真实用户 |
| MR02 | 招募与外部表单 | 联系授权（8 项清单见 `MR02/…/recruiting-plan.md` §四） |

---

## 六、下一步建议（我的选择与理由）

**建议先做 A1（MR05 的 `ModContentService` 更新接线测试）**，理由：

1. 它落在 **MG1（数据安全）** 的范围内，而 MG1 的门禁条件正是"不可恢复误删、状态与磁盘不符"；
   一条**未被测试覆盖的更新接线**正是这类风险的入口；
2. **完全本机可关**（不需要授权、不需要真实环境）；
3. 它把一个"已知未测路径"变成"有测试覆盖的路径"，是**可验证**的进展，而不是文档增量。

**次选 A5（`--bisect-smoke` 故障注入扩展）**：它能把 MR06 的覆盖从**单元层**推到**端到端层**，
价值同样高，但需要改 VerifyHost（测试宿主）。

---

## 七、本表的生成方式（可复跑）

机械抽取用：遍历证据根下每个包的最新 run → 读 `deliverable.md` →
截取"明确未做与未验证"小节 → 收集其中的表格行。
抽取脚本不是常驻脚本（一次性命令），但**归类规则（§一）与逐条归类是本表的核心内容**，
且每条都可在对应包的 `deliverable.md` 里核对。

**本表不宣布阶段放行、不提交、不发布。**

---

## 八、本表声明的核实结果（第 30 轮实跑）

### D 类（"已关闭"）逐条核实：**12 / 12 通过**

| 核实项 | 结果 |
|---|---|
| MR03 运行守卫：run 目录 + `InstanceRunningGuardTests.cs` + `RunningInstanceRegistry.cs` | ✅ 全部存在 |
| MR08：`diagnostic-history.test.cjs` | ✅ 存在 |
| MR11：`ImportCancellationTests.cs` + `import-cancel.test.cjs` | ✅ 存在 |
| MR19 单文件：三个 run 目录 | ✅ 存在 |
| MR19 托管程序集收窄：run 目录 | ✅ 存在 |
| MR20 UI 接线：`update-recovery.test.cjs` | ✅ 存在 |
| MR10 判定：run 目录 | ✅ 存在 |
| MR06/MR07：两个 run 目录 | ✅ 存在 |
| MR19 RID 冻结：run 目录 | ✅ 存在 |

### A 类（"仍开放"）抽查：4 / 4 确认确实开放

| 抽查项 | 判据 | 结果 |
|---|---|---|
| A1 `ModContentService` 无测试 | 测试项目里 `ModContent`/`ModUpdate` 引用数 | **0** → 确认开放 |
| A2/A3 MR04 保留期/磁盘不足用例 | 测试里 `RetentionExpired`/`DiskFull`/`OutOfSpace` 命中 | **0** → 确认开放 |
| A5 `--bisect-smoke` 故障注入 | VerifyHost 里 `inject`/`fault` 命中 | **0** → 确认开放 |
| A11 actionlint CI 门禁 | `.github/workflows/*.yml` 里 `actionlint` 命中 | **0** → 确认开放 |

> 抽查口径是"关键词命中数"，属于**下限证据**：命中为 0 说明**没有**该项，但命中不为 0 也不必然等于"已覆盖"。
> 因此 A 类的关闭必须用**新增用例 + RED/GREEN**来证明，而不是靠关键词消失。
