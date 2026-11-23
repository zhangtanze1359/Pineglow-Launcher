# 遗留开放项

本页是项目唯一的遗留事项清单，当前保留 **2 项**（OPEN-08 安全、OPEN-07 基础设施）；OPEN-02、OPEN-03、OPEN-04、OPEN-05 均已销项并移出。文档整理日期：**2026-11-24（UTC+08）**。

> **证据边界**：OPEN-05 已于 2026-11-23 完成技术复验、合入、目标分支参数行为确认与销项复核（技术 41/41），其「合入早于批准」的顺序偏差经编排方[正式决定追认](handoff/DECISION-2026-11-23-open05-approval-order.md)后销项，证据见 [OPEN-05 执行证据](evidence/2026-11-22-open05/README.md)。OPEN-02 已于 2026-11-22 经 Codex 第 4 轮独立复验通过后合入 `main@8827b71`，按规则移出，证据见 [OPEN-02 执行证据](evidence/2026-11-22-open02/README.md)。**OPEN-04 已于 2026-11-23 经编排方签定销项**：技术复跑未复现，7 轮独立复核均未推翻该结论，接受其覆盖与风险说明，证据见 [OPEN-04 销项决定](handoff/DECISION-2026-11-23-open04-closeout.md) 与 [OPEN-04 复跑证据](evidence/2026-11-23-open04/README.md)。**OPEN-03 已于 2026-11-23 销项并已实际发布**：v1.0.6 已打 tag 并创建 Release，独立复核经编排方授权豁免（非独立复核结论），见 [OPEN-03 销项签定](handoff/DECISION-2026-11-23-open03-closeout.md) 与 [发布后验证](evidence/2026-11-23-v106-release/README.md)。
>
> 2026-11-23 实测：`node scripts/check-release-consistency.cjs` → `exit=0`（csproj `1.0.6` / 说明 `v1.0.6.md` / 最新 tag `v1.0.6` **三方一致**）。`FormalPhaseApproval=NOT_GRANTED`，技术通过不等于合入批准或发布批准。

## 一、优先级与执行总览

建议顺序：**OPEN-08 → OPEN-07**。OPEN-08 涉及已公开分发的凭据，优先于纯整洁性问题。

| ID | 类型 / 建议顺序 | 状态 | 当前阶段 | 下一步与启动条件 |
| --- | --- | --- | --- | --- |
| [OPEN-08](#open-08--内嵌-curseforge-api-key-逾期未移除且发布说明误述) | 安全 / 1 | 未处置 | **已发布的 v1.0.6 说明称「已移除内嵌 Key」，实际仍在**。`CurseForgeKeyDefaults.cs` 保留 60 字符内嵌 Key 作第三级回退，其 `RemovalDeadline` 标的是 `v1.0.5`，已逾一个版本 | 需编排方决定：**移除回退**（改代码）还是**续期**（改 deadline）；发布说明需同步更正 |
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

### 已完成的临时处置

- 仓库内 `release-notes/v1.0.6.md` 已加入更正段，明确写出「该表述不属实」与实际情况。
- **GitHub Release v1.0.6 的正文已同步更正**（`gh release edit`）。

### 为什么不自行修复代码

移除回退属**产品代码改动**，且涉及产品行为选择：

1. **移除** —— 用户在既无配置、又无环境变量时将拿不到 CurseForge 内容，需产品侧接受这个体验退化（Current 已有缺 Key 引导，可能可接受）。
2. **续期** —— 若该 Key 仍被计划使用，应把 `RemovalDeadline` 更新为真实目标版本并记录持有人与轮换计划。

两者都不是执行者能单方面决定的。**且该 Key 是否已泄漏、是否需轮换，取决于持有人判断。**

### 执行步骤

1. 编排方确认该 Key 的**持有人**与**是否仍有效**，决定吊销/轮换时点。
2. 在 [1] 移除 与 [2] 续期 之间二选一。
3. 若移除：删 `CurseForgeKeyDefaults.cs`、改 `ResolveCurseForgeApiKey` 移除第三级回退、同步改 `SecretHygieneTests`（**不得放宽为「允许字面量存在」**，应改为「源码树零该字面量」）、更新 `RemovalDeadline` 相关断言为「文件已不存在」。
4. 若续期：更新 `RemovalDeadline` 到真实版本，并在文件注释写明持有人与轮换计划；`SecretHygieneTests` 增加「deadline 不得早于当前版本」的断言，防止再次逾期。
5. 无论哪条，更正 `release-notes/v1.0.6.md` 与 GitHub Release 正文（本轮已完成），并在下一次发布说明中如实披露。

### 验收清单

- [ ] 该 Key 的持有人与有效性已确认并记录。
- [ ] 移除或续期二选一已执行，且 `CurseForgeKeyDefaults.cs` 的状态与 `RemovalDeadline` 一致。
- [ ] `SecretHygieneTests` 具备防止再次逾期的断言（不是仅「标记存在」）。
- [ ] 已发布版本的说明与实际行为一致；若历史说明有误，已在仓库与 GitHub Release 两处更正。
- [ ] 若判定该 Key 曾随公开发布物分发，已评估轮换必要性并记录结论。

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
