# 文档安全描述对齐 · 逐条对照表

> 日期：2026-11-06 | 依据事项：M0-2 | 基线：本地 `47e939c`（10-17），代码核对同时参照远端 `30f7b56e`（v1.0.3）
> 原则：**只改文档，不改代码**。所有结论均在源码中定位到具体文件与行，可复核。

## 一、安全机制对照（旧表述 → 真实机制 → 源码位置）

| # | 旧文档表述 | 真实机制 | 源码位置 | 处置 |
|---|---|---|---|---|
| 1 | `docs/api-reference.md:9`「CORS：`*` 全放开」 | `Access-Control-Allow-Origin` 只回显**校验通过**的 Origin，从不写 `*` | `LocalApiService.SetCors`（约 409 行） | 改写为机制表（api-reference 1.1 节） |
| 2 | `docs/architecture.md:62`「CORS 全放开（`*`）」 | 同上，且 Origin 必须是 http + 回环 + 端口等于本进程端口 | `LocalApiService.IsAllowedOrigin` | 改为「只回显校验通过的 Origin」 |
| 3 | 旧文默认「无鉴权」 | 除 `/api/health` 外全部端点要求 `X-Pineglow-Token` | `LocalApiService.cs:344`（路由前置守门） | 新增 1.1 节首行说明 |
| 4 | 未载明令牌来源 | 进程内 `RandomNumberGenerator.GetBytes(32)` 转十六进制，每次启动重新生成，不落盘 | `LocalApiService.cs:27` `SessionToken` | 1.1 节表格第 1 行 |
| 5 | 未载明令牌如何到达前端 | 投放 `index.html` 时把 `__PINEGLOW_SESSION_TOKEN__` 占位符替换为真实令牌 | `LocalApiService.cs:5771` | 1.1 节表格第 2 行 |
| 6 | 未载明比较方式 | `CryptographicOperations.FixedTimeEquals`，且先比较长度 | `LocalApiService.IsValidSessionToken` | 1.1 节表格第 3 行 |
| 7 | 未载明 `<img>` 无法设头部 | `/skin-image`、`/api/content/icon` 接受 `?token=` 作为等价校验 | `LocalApiService.HasValidSessionToken` | 1.1 节表格第 4 行 |
| 8 | 未载明 AI 出站边界 | 代码内固定 HTTPS allowlist，非白名单端点拒绝（`endpoint_not_allowed`） | `AiEndpointPolicy.cs:123` | 1.1 节表格第 6 行 |
| 9 | 未载明 AI 重定向策略 | AI 专用 handler `AllowAutoRedirect = false`，防首跳白名单被 302/307/308 绕过 | `AiHttpClientFactory.cs:30` | 1.1 节表格第 7 行 |
| 10 | 未载明图片出站边界 | 内容图标/皮肤图只允许 `AllowedHosts` 内主机 | `ContentIconService.cs:65,341` | 1.1 节表格第 8 行 |
| 11 | 未载明路径越界 | 所有请求参数拼接路径统一校验 | `PathSafetyGuard.cs` | 1.1 节表格第 9 行 |

**复核方式**：按上表逐行在源码定位，11 条全部命中，无一条只能靠历史文档自证。

## 二、计数口径对照

| 项目 | 旧表述 | 2026-11-06 实测 | 计数方法 | 落点 |
|---|---|---|---|---|
| 前端视图 | 9 | **10** | `ui/js/launcher.js` `VIEWS` 表键数；`ui/index.html` `id="view-*"` 去重数，两者一致 | README / architecture / features |
| 服务层 | 约 67（另有 MR01 的 76） | **85** | `dev/PineglowLauncher/Pineglow.Launcher.Services/*.cs` 文件数 | README / architecture / engineering / development-guide |
| 数据模型 | 约 49 | **52** | `Pineglow.Launcher.Models/*.cs` 文件数 | development-guide |
| API 端点 | 约 50 处路由 | **59 条路径** / **43 个路由段字面量** | 路径口径 = api-reference 端点表行数；段口径 = 正则统计 `seg[n].Equals("...")` 去重 | api-reference 第二章首段 |
| 版本号 | AssemblyInfo.cs 为准 + csproj 供脚本（两处必须一致） | **csproj `<Version>` 是唯一来源**；`AssemblyInfo.cs` 在仓库中不存在，`GenerateAssemblyInfo` 保持默认 true | `ls dev/PineglowLauncher/AssemblyInfo.cs` → 不存在；csproj 第 11 行注释自称唯一来源 | development-guide 第 4 条 |
| CSS token 来源 | 「wrote figma.css」与「最终激活以 pineglow.css 为准」并存，读者无法判断 | 三层顺序加载，**后加载者胜出**：figma.css → pineglow.css → redesign.css；`--base` 最终为 `#10191b`（非 figma.css 的 `#0d0e11`） | `ui/index.html:8-10` 加载顺序 + 两文件同名 token 实测值 | design-system 头部 / development-guide 第 6 条 |

## 三、MR01 知识库断点的处置

**问题**：`docs/market-execution/MR01/20261002-072004Z/outputs/docs/` 是 2026-10-02 的文档快照，已把服务数刷到「76 个 `.cs` 实测」，但从未合回根 `docs/`，导致根文档与磁盘双重失真。

**处置：合回 + 降级为按日期归档。**
- 根 `docs/` 保持唯一权威目录地位，本次把易漂移计数改为「核对日 + 实测口径」。
- MR01 快照定位改为历史验收记录，其中与当前源码冲突的数字（76 服务、9 视图）**不回填**，避免制造第三份互相矛盾的数字。
- 新增判断规则并写入 `docs/README.md`：根文档有核对日且晚于 MR01 的，以根文档为准。
- 遗留转为后续事项：把服务数/视图数改为 CI 生成并比对，避免人工计数再次漂移。

## 四、「最后核对日」落实清单

| 文档 | 核对日 | 备注 |
|---|---|---|
| `docs/README.md` | 2026-11-06 | 新增「〇、事实基线分叉处置结论」章节 |
| `docs/api-reference.md` | 2026-11-06 | 新增 1.1 安全机制表 |
| `docs/architecture.md` | 2026-11-06 | 视图/服务计数 + 通信协议改写 |
| `docs/engineering.md` | 2026-11-06 | 服务数口径说明 |
| `docs/development-guide.md` | 2026-11-06 | 版本来源、token 来源、路由测试纪律 |
| `docs/design-system.md` | 2026-11-06 | token 加载顺序与胜出规则 |
| `docs/features.md` | 2026-11-06 | 10 视图矩阵补 playtime |

## 五、本次**未**处理（如实记录，不声称已解决）

| 项 | 状态 | 原因 |
|---|---|---|
| CI 中自动校验计数漂移 | 未做 | 属 M2 测试缺口决策范围，需先定「哪些数字值得进 CI」 |
| `docs/archive/` 与 `docs/` 根下 3653 个历史证据文件的清理 | 未做 | 属仓库结构决策，且涉及历史证据不可逆删除风险，须单独确认 |
| `figma.css` 中 50+ 处硬编码色值 | 未做 | 属 M1-3「CSS token 收敛」范围，本期只做文档口径对齐 |
| 远端 `main` 无 `docs/` 目录（仅 2 个 release-fix 文件） | 已记录 | 远端仓库结构与本分支不同，文档合入远端需另行决策分支策略 |
