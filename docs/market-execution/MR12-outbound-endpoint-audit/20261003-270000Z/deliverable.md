# MR12 补测 · 出站出口审计与"不得绕过策略"守卫（缺口登记表 A8）

- **run-id**：`20261003-270000Z`
- **性质**：把 MR12 的"未逐一审计"变成**已审计 + 有守卫**
- **依据**：`MR12-curseforge-rate-limit/20261003-043000Z/deliverable.md` §六：
  "**图标/更新路径的限流** | `ModContentService` 的三处 CF 请求已全部接线；
  但**图标获取**（`ContentIconService`）与其它可能的 CF 出口**本包未逐一审计**。"
- **最终状态**：**证据就绪**（6 个守卫用例；2 个 RED 成立；**新增登记 A18**）

---

## 一、审计结论（回答 MR12 的问题）

### 1. CurseForge API：**每个请求点都在策略内** ✅

| 行 | 内容 | 是否在策略内 |
|---|---|---|
| `ModContentService.cs:19` | `const string CurseForgeApi = …`（**常量声明**，不是请求） | — |
| `ModContentService.cs:339` | `…/mods/search?…` | ✅ 上一行是 `CurseForgeRequestPolicy.SendAsync(`（338） |
| `ModContentService.cs:380` | `…/mods/{project}/files` | ✅ 上一行是策略调用（379） |
| `ModContentService.cs:430` | `…/mods/{version}/download-url` | ✅ 上一行是策略调用（429） |

**实测 3 个请求点全部在策略内**，且策略仍具备 `MaxConcurrentRequests`（并发上限，`SemaphoreSlim` 实现）、
`Retry-After` 处理、指数退避 + 抖动。

### 2. 图标获取：**不碰 CF/Modrinth API** ✅

`ContentIconService` **不含任何 API 字面量**。它的设计是"`LocalApiService` 只接受
`(source, category, projectId)` 三元组，**绝不允许客户端提交任意 URL**"，
图标 URL 来自**服务端构建的可信映射**（带 TTL），实际取的是 CDN 媒体地址。

> **所以 MR12 担心的"图标路径绕过 CF 限流"在结构上不可能发生** —— 因为它根本不走 CF API。
> 它仍有连接/总超时约束（`ConnectTimeoutSeconds` / `TotalTimeoutSeconds`）。

### 3. 其它出口的策略覆盖（本轮新增的全局视角）

| 出口 | 策略 | 状态 |
|---|---|---|
| `api.curseforge.com` | `CurseForgeRequestPolicy` | ✅ 3/3 在策略内 |
| `api.modrinth.com` | **无** | 🔴 **7 个出口全部无策略** → **新增 A18** |
| AI（deepseek / minimax） | `AiEndpointPolicy` | ✅ 白名单把关 |
| 新闻 feed / 图片 / 链接 | `NewsEndpointPolicy` | ✅ 三类入口各自把关 |
| Fabric / Quilt / Forge / NeoForge meta+maven | 无专门策略 | ⚪ 记录（元数据端点，低频） |
| Adoptium（Java）/ Mojang piston / Microsoft 认证 | 无专门策略 | ⚪ 记录 |

**A18（新登记）**：**Modrinth 侧完全没有限流/并发策略**，而 Modrinth 同样有速率限制。
这是与 CurseForge 的**保护不对称**：同一类操作（搜索/取版本/下载地址）走两个源，只有一个受限流保护。
**修法建议**（未实施）：把 `CurseForgeRequestPolicy` 的并发闸 + 退避抽成通用 `HttpRequestPolicy`，
两个源共用；或为 Modrinth 增加同类策略。

---

## 二、6 个守卫用例（防"新开一个绕过策略的出口"）

| # | 守卫 | 断言 |
|---|---|---|
| 1 | `EveryCurseForgeApiRequest_IsWrappedByTheRequestPolicy` | 每个 CF API 请求点必须在 `CurseForgeRequestPolicy.SendAsync(` 内（同行或**紧邻上一行**）；并断言请求点 **≥3** |
| 2 | `CurseForgePolicy_StillEnforcesConcurrencyAndRetryAfter` | 策略必须仍有并发上限（`SemaphoreSlim`）与 `Retry-After` 处理 |
| 3 | `ContentIconService_MustNotCallTheModApis` | 图标服务**不得**出现 CF/Modrinth API 字面量；且必须仍有连接/总超时 |
| 4 | `NewsEndpoints_AreGatedByNewsEndpointPolicy` | 三类入口（feed / 图片 / 链接）各自必须过对应校验 |
| 5 | `AiEndpoints_AreGatedByAiEndpointPolicy` | AI 端点必须在白名单里 |
| 6 | `ModrinthApiCallSites_CountIsPinned_SoNewEgressIsNoticed` | **数量触发器**：当前 **7** 个 Modrinth 出口，数量变化即失败，迫使作者回答"新出口要不要限流" |

---

## 三、两个 RED（证明守卫有牙齿）

| RED | 改什么 | 结果 |
|---|---|---|
| **1** | 在 `ModContentService` 里加一个**绕过策略**的 CF 请求（`_http.GetAsync("https://api.curseforge.com/v1/mods/1")`） | **1 失败**：`EveryCurseForgeApiRequest_IsWrappedByTheRequestPolicy` |
| **2** | 让 `ContentIconService` **直接持有 Modrinth API 字面量** | **1 失败**：`ContentIconService_MustNotCallTheModApis` |

两个 RED 后生产文件**逐字节还原**（均打印 `还原 = True`）。

---

## 四、本轮自找并修掉的一个统计错误

第一次运行 **5 通过 / 1 失败**：数量触发器报告 **9**，而我记录的是 **10**。

排查发现**两个错**：
1. 我的"排除常量声明"只认 `const string ModrinthApi`，而另两个文件里的常量叫 **`Api`**
   → 2 个常量声明被误算成出口；
2. 真实出口是 **7** 个（不是 9 也不是 10）。

**修正**：排除规则改为"排除任何含 `const string` 的行"（不依赖常量名），记录值改为 **7**。
修正后 **6/6 通过**，输出明确列出 7 个出口：
`ModContentService.cs:226/261/299`、`ModrinthModpackService.cs:32/63`、`ResourceContentService.cs:45/71`。

> 教训与本会话前几轮同源：**统计口径的边界（什么算"一处"）必须先用实例核对，再写断言**。

---

## 五、验证

| 检查 | 结果 |
|---|---|
| 守卫用例 | **6 通过 / 0 失败** |
| C# 全量 | **792 通过 / 0 失败 / 0 跳过**（786 + 6） |
| Node 全量 | **230 通过 / 0 失败**（18 个文件） |
| 生产文件 | 两个 RED 后逐字节还原 |

---

## 六、实际改动

| 文件 | 改动 |
|---|---|
| `dev/PineglowLauncher.Tests/OutboundEndpointPolicyTests.cs` | **新增**（6 个守卫用例） |
| `scripts/source-snapshot-inputs.txt` | 登记新测试（类别 B 38→39） |
| `docs/market-execution/gap-register.md` | A8 标记为**已关闭**；**新增 A18（Modrinth 无策略）** |
| `docs/market-task-plan-2026-10-03.md` | MR12 行补记 |

**生产代码零净改动。**

---

## 七、A8 里**没有**做的部分（如实列出）

| 项 | 说明 |
|---|---|
| **请求合并 / 结果缓存** | 仍未做（MR12 已说明：做了并发上限与统一出口，没做同 URL 在途去重与缓存） |
| **专门的"稍后重试"按钮** | 仍未做（提示经既有错误通道显示） |
| **搜索分页游标** | 仍未改 |
| **A18 的修法** | **未实施**（把并发闸抽成通用策略是设计变更，需你确认） |

> 本包关闭的是 A8 里**可审计、可守卫**的那部分（"图标/更新路径的限流"），
> 其余三项是**功能增量**，不属于"缺口审计"。

---

## 八、仍未覆盖

| 项 | 说明 |
|---|---|
| 真实 CurseForge 限流 | 仍未复现（MR12 已说明用合成响应驱动） |
| 真实 Modrinth 限流 | 未验证（A18 的对侧） |
| 元数据端点（Fabric/Quilt/Forge/Adoptium/Mojang）的策略 | **只记录，未评估**：本轮给出清单，但没有为它们设计策略 |
| 运行时行为 | 本类是**静态源码检查**，不验证运行时是否真的遵守了并发上限（那由 MR12 的用例覆盖） |

---

## 九、证据清单

```
green/01-outbound-first.log        首次运行 5 通过 / 1 失败（暴露统计口径错误）
green/02-outbound-fixed.log        修正后 6 通过，列出 7 个 Modrinth 出口
green/03-outbound-final.log        最终 6 通过 / 0 失败
green/04-csharp-full.log           792 通过 / 0 失败
green/05-node-full.log             230 通过 / 0 失败
red/ModContentService.cs.before        RED 前备份
red/ContentIconService.cs.before       RED 前备份
red/01-red-cf-bypass.log           RED-1：绕过策略的 CF 请求 → 1 失败
red/02-red-icon-api.log            RED-2：图标服务持有 API 字面量 → 1 失败
manifest.json
```

**本报告不宣布阶段放行、不提交、不发布。**
