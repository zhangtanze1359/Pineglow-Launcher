# 遗留开放项

本页是项目唯一的遗留事项清单，当前保留 **2 项**（OPEN-08 安全、OPEN-07 基础设施）；OPEN-02、OPEN-03、OPEN-04、OPEN-05 均已销项并移出。文档整理日期：**2026-11-24（UTC+08）**。

> **证据边界**：OPEN-05 已于 2026-11-23 完成技术复验、合入、目标分支参数行为确认与销项复核（技术 41/41），其「合入早于批准」的顺序偏差经编排方[正式决定追认](handoff/DECISION-2026-11-23-open05-approval-order.md)后销项，证据见 [OPEN-05 执行证据](evidence/2026-11-22-open05/README.md)。OPEN-02 已于 2026-11-22 经 Codex 第 4 轮独立复验通过后合入 `main@8827b71`，按规则移出，证据见 [OPEN-02 执行证据](evidence/2026-11-22-open02/README.md)。**OPEN-04 已于 2026-11-23 经编排方签定销项**：技术复跑未复现，7 轮独立复核均未推翻该结论，接受其覆盖与风险说明，证据见 [OPEN-04 销项决定](handoff/DECISION-2026-11-23-open04-closeout.md) 与 [OPEN-04 复跑证据](evidence/2026-11-23-open04/README.md)。**OPEN-03 已于 2026-11-23 销项并已实际发布**：v1.0.6 已打 tag 并创建 Release，独立复核经编排方授权豁免（非独立复核结论），见 [OPEN-03 销项签定](handoff/DECISION-2026-11-23-open03-closeout.md) 与 [发布后验证](evidence/2026-11-23-v106-release/README.md)。
>
> 2026-11-23 实测：`node scripts/check-release-consistency.cjs` → `exit=0`（csproj `1.0.6` / 说明 `v1.0.6.md` / 最新 tag `v1.0.6` **三方一致**）。`FormalPhaseApproval=NOT_GRANTED`，技术通过不等于合入批准或发布批准。

## 一、优先级与执行总览

建议顺序：**OPEN-08 → OPEN-07**。OPEN-08 涉及已公开分发的凭据，优先于纯整洁性问题。

| ID | 类型 / 建议顺序 | 状态 | 当前阶段 | 下一步与启动条件 |
| --- | --- | --- | --- | --- |
| [OPEN-08](#open-08--内嵌-curseforge-api-key-逾期未移除且发布说明误述) | 安全 / 1 | **已修复待复核** | **已发布的 v1.0.6 说明称「已移除内嵌 Key」，实际仍在**。`CurseForgeKeyDefaults.cs` 保留 60 字符内嵌 Key 作第三级回退，其 `RemovalDeadline` 标的是 `v1.0.5`，已逾一个版本 | 需编排方决定：**移除回退**（改代码）还是**续期**（改 deadline）；发布说明需同步更正 |
| [OPEN-07](#open-07--更新主源gitee-updatejson-持续-404) | 基础设施 / 2 | 未处置 | 已实测确认：产品默认更新链第一站 `gitee.com/.../update.json` **返回 404**，每次检查更新都先吃一次失败再降级 | 需编排方决定：**修复源（需 Gitee 侧权限）** 还是 **调整默认链（改产品代码）**；两者都未获授权 |

## 二、逐项执行与验收

## OPEN-08 · 内嵌 CurseForge API Key 逾期未移除且发布说明误述

### 影响与当前状态

**这是一条已对外公开的错误陈述，不只是内部欠账。**

**事实链（每条可复核）**：

| 项 | 事实 | 证据 |
| --- | --- | --- |
| 1 | `CurseForgeKeyDefaults.cs` 保留 60 字符内嵌 Key：`$2a$10$G3lIsq…WjOCrG` | `CurseForgeKeyDefaults.cs:21` |
| 2 | 它作为 `ResolveCurseForgeApiKey` 的**第三级回退**实际生效 | `ModContentService.cs:516-530`（`fallback = EmbeddedKeyFallbackForTest ?? CurseForgeKeyDefaults.Embedded`） |
| 3 | 其 `RemovalDeadline` 标为 `v1.0.5` | `CurseForgeKeyDefaults.cs:24` |
| 4 | **已发布的 v1.0.6（含 tag 源码与二进制）仍保留该 Key** | `git show v1.0.6:.../CurseForgeKeyDefaults.cs` 中命中 1 次 |
| 5 | **v1.0.6 发布说明曾称「移除了…内嵌的 API Key」——该句不属实** | `release-notes/v1.0.6.md` 原文；GitHub Release v1.0.6 正文 |
| 6 | 守卫测试**拦不住**：`SecretHygieneTests` 把该文件列为 `ExemptFileName` 豁免，只断言「deadline 存在」而**不断言到期必须删除** | `SecretHygieneTests.cs:22,41,53-54` |

**为什么这是安全问题**：一个可分发的 API Key 随二进制发布给所有用户。任何拿到 v1.0.6 安装包的人都能提取并使用它，消耗 Key 持有人的 CurseForge 配额，或在该 Key 被封禁前滥用。

**发现方式**：2026-11-24 由任务规划方（Codex）在核实「v1.0.6 实际交付」时指出，执行者随后逐条回源码确认。**这不是规划方的推断错误，是执行者的发布说明错误。**

### 编排方决定（2026-11-24）

> **移除内嵌 Key，但不轮换、且不能让 CurseForge 下载失效。** 该 Key 未泄露，无需轮换。

这**改变了** `CurseForgeKeyDefaults.cs` 注释里记录的 2026-11-16 原始决定（原计划是「吊销并换新 → 删文件 → 全量改为用户自带」）。本次改为「保留同一把 Key，只换注入位置」。

### 社区通例（先查证，再决定方案）

按编排方要求，实施前先核对了其他开源启动器「用户零配置直接用 CurseForge」的做法：

| 启动器 | 做法 | 关键源码 |
| --- | --- | --- |
| **Prism Launcher** | 默认值硬编码在构建脚本，编译进 `BuildConfig`，运行时可被用户覆盖 | `CMakeLists.txt:280` `set(Launcher_CURSEFORGE_API_KEY …)` → `BuildConfig.cpp.in:114` → `Application.cpp:1933` `return BuildConfig.FLAME_API_KEY` |
| **basalt-launcher** | **构建期从环境变量读，cargo 注入**（源码树无字面量） | `src-tauri/build.rs`：`std::env::var("BASALT_CURSEFORGE_API_KEY")` → `cargo:rustc-env` |
| **OneLauncher** | 编译期常量 | `oneclient_net/src/config.rs:18` `constants::CURSEFORGE_API_KEY.to_string()` |

**结论：没有一家采用「代理服务」或「让用户自己配」。全部把 Key 编进二进制**，其中 **basalt 的做法最干净**（Key 只在构建环境）。本项目采用 basalt 模式。

### 实施记录（提交 `016d89f`）

| 项 | 做法 |
| --- | --- |
| Key 来源 | 构建机环境变量 `PINEGLOW_CURSEFORGE_BUILD_KEY`（GitHub Secret 已配置，2026-11-24） |
| 注入方式 | MSBuild `AssemblyMetadata` 写入程序集；**不生成 .cs 文件** |
| 读取 | `CurseForgeKeyDefaults` 通过 `Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()` 取回 |
| 解析顺序 | 不变：调用方传入 → 环境变量 → 构建期注入值 |
| 守卫 | `SecretHygieneTests` **取消豁免文件**，改为「源码树任何 .cs 出现 Key 字面量即失败」 |
| CI | `build.yml` / `release.yml` 用 `env:` 传 secret（**不走 `-p:`**，避免 Key 进进程命令行） |
| 发布门槛 | `release.yml` 新增「发布包必须含 Key 元数据」检查，secret 漏配时终止发布 |

**为什么不用 `WriteLinesToFile` 生成 .cs**：该任务在本工具链（.NET Framework 版 MSBuild）上不支持 `WritingEncoding`，且把分号当列表分隔符导致生成的 C# 语法错误——**实测踩过两次**，故改用 `AssemblyMetadata`。

### 验证结果

在隔离工作树 `pg-open08-20261124` 实测：

| 场景 | 结果 |
| --- | --- |
| 构建机未提供 Key | 构建成功，程序集**不含** Key 元数据，日志打印「未提供」 |
| 构建机提供 Key | 构建成功，程序集**含** Key 元数据键与值，日志打印「已注入（长度 60）」 |
| 守卫测试（带 Key） | **5/5 通过** |
| 源码树字面量扫描 | **命中 0 个文件** |
| 全量 C# 测试 | **1503 通过 / 0 失败 / 0 跳过**（较修复前 +1，新增守卫） |
| GitHub Actions（`45300189396`） | **success**；三个平台的 Publish 步骤均显示「已注入构建期 Key（长度 60）」 |

原始输出：[构建期注入验证](evidence/2026-11-24-open08/raw/build-injection-verify.txt)

### 遗留说明（如实记录）

1. **已发布的 v1.0.6 包里仍含旧的内嵌 Key**。本次只改了源码与后续构建；v1.0.6 的附件未变（哈希仍为 `43750657…`）。按编排方判断「未泄露、不轮换」，故不重发该版本。
2. **测试步骤与发布步骤的 Key 状态不一致**：CI 里 `dotnet test` 不读该 secret（只有 Publish 步骤配了 `env`），所以测试构建的产物不含 Key。这不影响任何测试（`SecretHygieneTests:90` 只断言行解析自洽，Key 为空也成立），但属于「测试环境 ≠ 发布环境」的已知差异。
3. **Secret 是唯一副本**。Key 现在只存在于 GitHub Secret 与 CurseForge 后台；若 secret 丢失需回后台取回。建议另存一份到仓库外的密码管理器。

### 验收清单

- [x] 该 Key 的持有人与有效性已确认并记录 —— 编排方 2026-11-24 确认「未泄露、不轮换」。
- [x] 移除或续期二选一已执行 —— 采取「移除源码内嵌」，Key 改由构建期注入。
- [x] `SecretHygieneTests` 具备防止再次逾期的断言 —— 已取消豁免文件，改为源码树零字面量。
- [x] 已发布版本的说明与实际行为一致；历史说明有误已在仓库与 GitHub Release 两处更正。
- [x] 若判定该 Key 曾随公开发布物分发，已评估轮换必要性并记录结论 —— 编排方判定不轮换。
- [ ] **独立复核**：以上改动尚未经独立复核（按 Codex 用量规划，可由编排方决定是否派一轮）。

**销项条件**：Key 状态（移除或续期）已确定并落地，守卫测试能阻止再次逾期，对外说明与实际一致；经独立复核确认。

**证据入口**：[`CurseForgeKeyDefaults.cs`](../dev/PineglowLauncher/Pineglow.Launcher.Services/CurseForgeKeyDefaults.cs)、[`ModContentService.cs`](../dev/PineglowLauncher/Pineglow.Launcher.Services/ModContentService.cs)、[`SecretHygieneTests.cs`](../dev/PineglowLauncher.Tests/SecretHygieneTests.cs)、[v1.0.6 发布说明](../release-notes/v1.0.6.md)、[下一版本规划（Codex，发现来源）](evidence/2026-11-24-next-version-planning/PLAN.md)。

## OPEN-07 · 更新主源（Gitee update.json）持续 404

### 影响与当前状态

产品 `ConfigStore.DefaultUpdateUrl`（`ConfigStore.cs:17`）指向：

```text
https://gitee.com/ztz1359/pineglow-launcher/raw/master/update.json
```

**2026-11-23 实测该地址返回 HTTP 404。** 备用源 GitHub 正常。

这不是猜测，有三条独立记录：

| 来源 | 证据 |
| --- | --- |
| 直接 curl | `HTTP 404`（0.11 s） |
| v1.0.5 自更新演练产品日志 | `[update] 清单源失败（HTTP 404（清单不存在））: https://gitee.com/...` |
| v1.0.6 发布后验证产品日志 | 同上，换了版本仍然 404 |

### 实际代价（已量化，不夸大）

更新链会**并行**查询多个源并取最高版本（`UpdateService.FetchManifestChainAsync`），所以：

| 项 | 实测 |
| --- | --- |
| Gitee 404 耗时 | **0.11 s** |
| GitHub 源耗时 | **2.54 s**（含 302 跳转） |
| 用户实际等待 | **≈2.5 s**（取最慢的，不是相加） |

**结论：功能不受影响，用户几乎感知不到。** 这是一条「每次多一个无效请求」的整洁性问题，不是可用性缺陷。登记它以如实反映现状，而不是渲染成严重问题。

### 为什么不自行修复

两条路径都需要授权，**本轮均未执行**：

1. **补上 Gitee 的 `update.json`** —— 需要 Gitee 侧仓库写权限。`release.yml:213-238` 的镜像步骤依赖 `GITEE_TOKEN`，未配置时**自动跳过**并写 `mirror-status.json`（实测本轮发布就是这样跳过的）。
2. **改代码去掉 Gitee 主源** —— 属产品代码改动，需要单独授权；且会改变「国内用户优先 Gitee」的既有设计意图，不该由执行者单方面决定。

### 执行步骤

1. 由编排方决定方向：**修源**（配 `GITEE_TOKEN`，让 `release.yml` 的镜像步骤真正写入）还是 **改链**（调整 `DefaultUpdateUrl` 或降级为可选源）。
2. 若选修源：在 GitHub Secrets 配置 `GITEE_TOKEN`（Gitee 私人令牌，需 projects 权限）后重跑一次发布，检查 `dist/mirror-status.json` 的 `updateJsonUpdated` 是否为 `true`。
3. 若选改链：改 `ConfigStore.DefaultUpdateUrl` 与 `BuildManifestUrlChain` 的优先级，同步更新相关测试。
4. 无论哪条，修完后用 `GET /api/update/check` 实测一次，确认 `manifestSources` 里主源为 `ok`。

### 验收清单

- [ ] 主源要么返回有效清单，要么从默认链中移除（二选一，不留 404）。
- [ ] `release.yml` 的镜像结果在 `mirror-status.json` 中如实反映，不出现「跳过了却说更新了」。
- [ ] 修改默认链时，客户端 `usedFallback` 语义与发布端 manifest 保持一致。
- [ ] 有可复核的实测输出（`/api/update/check` 的 `manifestSources`）。

**销项条件**：主源不再 404，或已从默认链移除且发布流程与文档同步更新；经独立复核确认。

**证据入口**：[v1.0.5 演练产品日志](evidence/2026-11-23-open03-e2e/raw/product-launcher.log)、[v1.0.6 发布后验证日志](evidence/2026-11-23-v106-release/raw/product-launcher.log)、[发布工作流](../.github/workflows/release.yml)、[更新清单链实现](../dev/PineglowLauncher/Pineglow.Launcher.Services/UpdateService.cs)、[默认源定义](../dev/PineglowLauncher/Pineglow.Launcher.Services/ConfigStore.cs)。

## 三、统一更新、验证与销项规则

### 状态与证据时效

- 状态沿用 `未处置`、`进行中`、`已复现待修`；启动条件和阻塞另列，不为整理文档擅自提升状态。
- `已复现待修` 必须有真实失败证据；代码推断、历史失败和未进入脚本均不能替代。
- 每次更新区分：历史已验证、目标基线已验证、尚待验证。历史证据注明日期、提交和范围，不把旧测试数字当成当前通过。
- 新增事项顺延 ID，附影响、证据、下一步与完成条件；已关闭项不重新混入开放清单，不另建第二份遗留事项清单。

### 验证环境与记录字段

本地产品门槛遵循 `AGENTS.md`，在 `D:/Projects/_worktrees/` 的隔离工作树执行；主工作区不用于得出产品测试通过 / 失败数字。创建工作树或写入证据目录前，应确认路径处于实际授权范围，项目约定不替代写入授权。

每份运行证据至少记录：**目标提交、工作目录、UTC+08 时间、环境和前置条件、实际命令、预期与实际结果、退出码、通过 / 失败 / 跳过数、原始日志路径**。脚本检查同时记录输入路径和参数来源；重复运行记录次数和并发度。

现存 [docs/baseline.md](baseline.md) 绑定 `main@dcdf056`。其中 C# 1453 条通过、跳过 0 条及前端 508 条通过是该旧基线的结果，不能证明其他提交通过。需要复跑全量时使用仓库既有命令：

```text
node scripts/gates.cjs
node scripts/gates.cjs --baseline
node scripts/check-release-consistency.cjs
```

上述命令按具体任务选择，不表示本次已执行；基线只由门槛脚本生成，不手改数字。产品全量验证不应为纯文档整理无条件触发。

### 销项与审批边界

1. 执行者不为自己签署独立验收；独立复验使用目标基线和可复核的原始输出。
2. 区分技术验证、独立复验、合入批准、目标分支落地及阶段 / 发布批准，不以其中一项替代其他项。
3. 满足对应事项的销项条件后，确认变更已在目标分支，再从本页移除。提交信息记录销项依据；历史交接和原始证据保留，不删除日志。
4. 清单整理后已获授权按 OPEN-02 → OPEN-05 → OPEN-04 → OPEN-03 推进实际工作；**四项均已完成并移出，当前无开放事项**。目录外写入及命令执行仍按具体操作确认，拒绝后不自动重试。推送、打 tag 和发布不因这一推进授权自动获准。
5. **合入顺序**：需写入目标分支的改动，应先在隔离工作树完成并取得独立复验与批准，批准后方可落地目标分支（OPEN-05 曾发生「合入早于批准」的顺序偏差，经编排方[正式决定追认](handoff/DECISION-2026-11-23-open05-approval-order.md)；该追认是针对已发生偏差的处理，不构成流程放宽）。
