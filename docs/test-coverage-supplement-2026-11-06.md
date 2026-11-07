# 补测与覆盖率补充报告（2026-11-06）

> **性质**：在 `functional-test-summary-2026-11-06.md` 之后的一轮**补测**记录，不是验收结论。
> **最后核对日：2026-11-06**（对照 `remediation/v1.1.0` 分支 HEAD `5bdebfa` 实测）。
> **一句话结论**：测试数量与覆盖率显著提升（C# 982 → **1325**，前端 385 → **448**，整体行覆盖实测 **52.1% → 63.3%**），
> 并在过程中**实测确认了 4 处此前无文档记录的行为/不一致**。但覆盖率仍只到 63.3%，
> 未覆盖的部分集中在"需要真实账号/真实游戏/真实 GUI 交互"与"产品缺可测缝"两类，**不能读作功能已验证**。

## 一、为什么补这一轮

`functional-test-summary-2026-11-06.md` 的结论是"本地可验证范围内全绿"，但它有一处**证据强度不足**：
所有"通过"都建立在"测试跑绿了"之上，而**从来没有测过覆盖率**——
即不知道那些测试实际碰到了多少代码。本轮先用 `XPlat Code Coverage` 把这件事量出来，结果不乐观：

| 指标 | 补测前实测 |
|---|---|
| 整体行覆盖 | **52.1%**（6956 / 13360） |
| 分支覆盖 | **47.7%** |
| 完全零覆盖、且**本来就能测**的服务类 | 至少 12 个 |

零覆盖清单里有一批是"零 IO / 零网络 / 零 GUI，本来就能测"的：`VersionFilter` 0/20、
`ModSearchQueryResolver` 0/136、`ModpackLifecycleService` 0/66、`ServerPingService` 0/316、
`ResourceContentService` 0/46、`ModrinthModpackService` 0/48、`JavaRuntimeService` 0/42、
`AuthlibInjectorService` 0/20，以及 `RulesEvaluator` 3.4%、`MinecraftUtils` 8.7%、
`MirrorHelper` 1.4%、`LaunchCommand` 7.3%、`ModHealthService` 23.1%。

**这批没被测没有正当理由**，所以这一轮的主要工作就是补它们。补的时候有一条自我约束：
优先补"判错不会崩、只会静默出错"的地方（脱敏、路径守卫、账号投影、错误文案映射），
而不是为了刷数字去测 getter。

## 二、四轮补测的内容

四次提交，每轮都有独立的验证与推送记录。

### 2.1 第一轮：纯逻辑 + 文件服务 + 服务器协议 + 日志/路径 + 凭据（C# 982 → 1155，前端 385 → 428）

| 新增文件 | 项数 | 钉住的东西 |
|---|---|---|
| `UncoveredPureLogicTests` | 47 | `RulesEvaluator`（决定哪些库下载、哪些 JVM 参数生效；空 `rules` 数组必须拒绝、有毒正则必须 fail-closed 而不是抛崩）、`MirrorHelper`（6 类 host 改写规则，决定镜像真实下载地址）、`LaunchCommand` 脱敏（决定 accessToken 会不会进日志） |
| `UncoveredFileServiceTests` | 70 | 备份 zip 的 Zip-Slip 必须被拒（实测 `../../escaped.txt` 条目被拒）、`DeleteBackup` 只允许删 `backups/` 下的 `.zip`、Mod 版本区间 16 条语法、坏 zip 不能让资源包列表整页崩 |
| `ServerPingProtocolTests` | 11 | 起本地假 `TcpListener` 按协议逐字节应答，**独立实现一遍编解码**（不复用被测代码，否则是自己跟自己对答案） |
| `MirrorLatencyProbeTests` | 9 | 把探测口径钉成断言，并固化一个实测缺陷（见 3.1） |
| `LoggingAndPathsTests` | 13 | 日志轮转、7 天清理、"写日志失败不能带崩主流程"、并发写不截断行、路径计算的平台分支 |
| `SecretStoreContractTests` | 23 | 名字校验、空值删除、`AsyncLocal` 隔离；并用反射把"文件后端无路径注入点"这个**验证能力缺口**本身钉住 |
| `ui/tests/api-live-contract.test.cjs` | 43 | 统计出 `ui/js` 里 59 个 `SG.api` 方法被真实调用、测试只触达 17 个；逐方法钉住 method + 路径 + body，并锁住三处容易"顺手指错"的契约（`getUpdateStatus` 是 `GET /api/update` 而非 `/api/update/status`；`deleteInstance` 是 POST 而非 DELETE；`getNewsImage` 的令牌只走请求头，绝不进 URL） |

### 2.2 第二轮：加载器/内容/整合包 + 路径守卫 + OAuth 配置 + 前端纯函数（C# 1155 → 1233，前端 428 → 448）

| 新增文件 | 项数 | 覆盖提升 |
|---|---|---|
| `LoaderAndContentServiceTests` | 47 | `FabricService` 0 → **100%**、`QuiltService` 0 → **100%**、`ResourceContentService` 0 → **95.7%**、`ModrinthModpackService` 0 → **95.8%**、`NewsContentStateText` 0 → **100%**、`AuthlibInjectorService` 0 → 50% |
| `PathSafetyGuardTests` | 17 | `PathSafetyGuard` 52% → **63.6%**（RA-01 核心防线） |
| `AuthClientOptionsTests` | 9 | `AuthClientOptions` 0 → **100%** |
| `ui/tests/launcher-ui-pure.test.cjs` | 20 | `esc` / `uiText` / `uiIcon` / `uiPlayTime` / `parseValidatedAt` / `steveSvg` / `skinAvatar` / `newsStateText` / `newsErrorText` 等纯函数 |

`PathSafetyGuard` 是这一轮最该补的：它是 RA-01 的核心（防 junction / 重解析点把实例目录指到系统目录），
此前只被集成用例**间接**覆盖，且覆盖到的恰好都是"一切正常"和"已经越界"两条路。
本轮用真实 `mklink /J` 建 junction 实测（非 mock），断言：

- 实例目录被替换成指向 `C:\Windows` 的 junction 时必须拒绝；
- 指向父级的 junction 必须拒绝，普通子目录必须放行；
- 成功后返回的 `PinnedPath` 必须真的**阻止**目录被改名（这是它唯一的价值）；
- **不存在的路径不得因为"无法解析"而被判为安全**（fail-closed）。

`launcher-ui-pure` 只加测试、不改产品逻辑：`launcher.js` 的唯一改动是在既有的 `SG.__test__`
对象里多挂 11 个已有函数的引用（+13 行，生产路径不引用 `SG.__test__`）。

### 2.3 第三轮：账号服务 + 微软登录全链（C# 1233 → 1325）

| 新增文件 | 项数 | 覆盖提升 |
|---|---|---|
| `AccountServiceTests` | 43 | `AccountService` 23.2% → **97.8%**（270/276 行） |
| `MicrosoftAuthLoginChainTests` | 49 | `MicrosoftAuthService` 16.5% → **87.0%**（200/230 行） |

这两个类都是"判错不崩、只静默出错"的典型：

- `AccountService` 决定"当前用哪个账号"以及"账号如何投影到 legacy 配置字段"。判错的表现是把用户登到别人的账号上，或把上一个人的 `refreshToken` 残留给下一个人。零 IO，只断言内存状态。
- `MicrosoftAuthService` 的既有测试只覆盖"单次设备码轮询"6 条分支；真正决定能不能登进去的是后面那串**五跳链路**（微软 OAuth → XBL 用户认证 → XSTS 授权 → `login_with_xbox` → 所有权校验 → 玩家档案）。每一跳都会失败，而失败必须翻译成"用户能照做"的中文原因。按 URL 分派的假处理器把整条链跑通，**零真实网络**。

### 2.4 第四轮：外部依赖可达性探测（新增工具，不是测试）

见 3.1 与第四节。`scripts/probe-external-deps.cjs`，14 个端点，每个标注"支撑哪个功能 / 挂了怎么降级 / 是否真的无降级"。

## 三、补测过程中实测到的 4 处新事实

这一节是**本轮最有价值的部分**，因为下面每一条都是"跑出来的"，不是读代码猜出来的。

### 3.1 三个下载源的"可用性探测"路径都少了一段（既有缺陷，非本轮引入）

`MirrorLatencyProbe` 的探测路径是 `{baseUrl}/version_manifest_v2.json`，而真实清单在
`{baseUrl}/mc/game/version_manifest_v2.json`。用 curl 独立核实：

```
HEAD piston-meta/version_manifest_v2.json            → 404
HEAD piston-meta/mc/game/version_manifest_v2.json    → 200
```

即三个下载源的"可用性探测"路径都少了一段；又因为实现**不检查状态码**，404 仍被判为"可用"。
**后果**：下载源页的 `available=true` 只证明"能连上"，不证明"能取到清单"。
已钉成可执行断言（`MirrorLatencyProbeTests`）。

### 3.2 默认更新源 Gitee 的 `update.json` 线上 404（既有缺陷，本轮复核确认）

`ConfigStore.DefaultUpdateUrl` 指向 `https://gitee.com/ztz1359/pineglow-launcher/raw/master/update.json`，
线上返回 **404**——这个文件从未被创建。备用源（GitHub Release 的 `manifest.json`）实测 **200**，所以功能靠降级兜住。

本轮把它做成了脚本化检查，不再依赖人记得去 curl。

### 3.3 `AccountService` 里有一处**不可达的修复分支**

```csharp
if (GetSelected(config) == null)      // 想修"SelectedAccountId 指向不存在的 id"
{
    config.SelectedAccountId = config.Accounts[0].Id;
    result = true;
}
```

但 `GetSelected` 在 id 不命中时会**回退到 `Accounts[0]`**，所以列表非空时它永不返回 `null`。
后果：悬空 id 不会被纠正，配置里留脏数据；但 legacy 投影也走 `GetSelected`，所以**用户看到的行为是正确的**。
判为"非功能性缺陷 + 一处一致性缺口"，已写成断言，将来改语义时会红。

### 3.4 `UpsertThirdParty` 的 `CapeCount` 与 `Capes` 语义不一致

`CapeUrl` 为空时把 `CapeCount` 置 0，却**不清理旧的 `Capes` 列表**。
界面按 `CapeCount` 判断，所以不可见；但改成读 `Capes` 就会露出来。已钉住。

## 四、当前门槛与实测数字

| 门槛 | 补测前 | 现在 | 说明 |
|---|---|---|---|
| C# 单元/集成测试 | 982 / 982 | **1325 / 1325**，0 跳过 | +343 项，7 个新测试文件 |
| 前端 Node 测试（文件数） | 385 / 385（29 文件） | **448 / 448**（31 文件） | +63 项，2 个新测试文件 |
| 测试清单双向一致性 | 通过 | 通过（31 文件一致） | `run-inventory-tests.cjs --check` |
| CSS token 纪律 | exit 0 | exit 0（49 token） | `check-css-tokens.cjs` |
| UI 资源闭包 | exit 0 | exit 0 | `check-ui-resource-closure.cjs` |
| **整体行覆盖率** | **52.1%** | **63.3%**（8455 / 13360） | 本轮新增指标 |
| **分支覆盖率** | **47.7%** | **58.6%** | 本轮新增指标 |
| NuGet 已知漏洞（含传递依赖） | 未测 | **0** | `dotnet list package --vulnerable --include-transitive` |
| NuGet 锁定还原 | 通过 | 通过（`--locked-mode`） | 锁文件与 csproj 一致 |
| 外部端点可达性 | 未测 | **12 / 14 可达** | `probe-external-deps.cjs` |

外部端点那一行的两个"不可达"是：`update-primary` 404（3.2 的已知缺陷）、
`neoforge-maven` 连接失败（本机走 HTTP 代理，`CONNECT 502` 与"目标真挂"在这一层无法区分，
**不作为"NeoForge 已下线"的结论**，只记为"本机不可验证"）。

### 4.1 按类的覆盖提升（挑有代表性的）

| 类 | 补测前 | 现在 |
|---|---|---|
| `ModSearchQueryResolver` | 0% | **100%** |
| `VersionFilter` | 0% | **100%** |
| `ModpackLifecycleService` | 0% | **100%** |
| `FabricService` / `QuiltService` | 0% | **100%** |
| `NewsContentStateText` | 0% | **100%** |
| `AuthClientOptions` | 0% | **100%** |
| `ResourceContentService` | 0% | **95.7%** |
| `ModrinthModpackService` | 0% | **95.8%** |
| `AccountService` | 23.2% | **97.8%** |
| `MirrorHelper` | 1.4% | **90.3%** |
| `ServerPingService` | 0% | **89.9%** |
| `ModHealthService` | 23.1% | **89.4%** |
| `LaunchCommand` | 7.3% | **87.8%** |
| `MicrosoftAuthService` | 16.5% | **87.0%** |
| `RulesEvaluator` | 3.4% | **72.4%** |
| `PathSafetyGuard` | 52.3% | **63.6%** |

## 五、仍然没测到的，以及为什么

覆盖率停在 63.3% 不是没时间，是**两类结构性原因**。分开说，因为它们的处理方式完全不同。

### 5.1 GUI 层（约 460 行零覆盖）

`WebShellWindow` 0/204、`App` 0/178（另有两处 Avalonia 生成的 XAML 闭包 0/264）。
需要真实 WebView2 交互，本机无法自动化到"逐控件点击"级别。
现有替代证据：`verify-ui-pages-live.cjs` 的真实 WebView2 逐页验收（10 视图、截图 10 张、
`exceptions=0` / `consoleErrors=0`），覆盖的是"能渲染、不报错"，**不覆盖"每个控件点下去行为正确"**。

### 5.2 产品缺可测缝（约 320 行）

这批不是"测不了"，是**产品设计上没留注入点**，硬测会往真实用户目录写文件：

| 类 | 覆盖 | 卡在哪 |
|---|---|---|
| `JavaRuntimeService` | 0/42 | `InstallAsync` 全程真实下载 + 解压 + 校验，下载目标路径**写死**在 `PlatformPaths.GetConfigDirectory()/runtimes`；测试会往运行者真实的用户目录写 JDK |
| `AuthlibInjectorService` | 50% | `EnsureAgentAsync` 同样写死落盘路径 |
| `NeoForgeService` | 32.3% | 需真实安装器 jar 与 maven 全链 |
| `LoaderInstallHelper` | 11.8% | 同上 |
| `RestrictedFileSecretBackend` / `WindowsCredentialSecretBackend` | 0 / 0 | 无参构造、路径写死；已用反射断言"构造函数无参"把缺口本身钉住，将来加注入点时会红 |

**这是一条值得单独立项的改进建议**：给这三个服务加路径注入参数（默认值保持现状），
成本很低，但能让 ~320 行"决定用户机器上装什么"的代码进入自动化范围。

### 5.3 需要真实外部环境（不在本轮范围）

真实启动游戏、真实微软账号登录、真实 AI 调用、真实 CurseForge 搜索、真实大文件下载、
长时游玩统计、Linux/macOS 实机。这几项与上一份报告一致，**状态没有变化，依然是未验证**。

## 六、仍然存在的产品级问题（不因补测而消失）

| # | 问题 | 影响 | 本轮动作 |
|---|---|---|---|
| 1 | 默认更新源 `gitee update.json` 404 | 默认更新检查只能靠 GitHub 备用源；部分网络下备用源超时 → **对相当一部分用户实际不可用** | 已脚本化探测（3.2），**未修**（需 owner 决定是补文件还是换主源） |
| 2 | 镜像延迟探测路径少一段且不检查状态码 | 下载源页的"可用"标记不反映真实可用性 | 已钉成断言，**未修** |
| 3 | `AccountService` 悬空 `SelectedAccountId` 修复分支不可达 | 配置留脏数据（行为正确） | 已钉住，**未修** |
| 4 | `UpsertThirdParty` 的 `CapeCount` / `Capes` 不一致 | 当前不可见 | 已钉住，**未修** |
| 5 | 删除实例只有 `POST /delete`，无 `DELETE` | 功能正常，REST 语义不统一 | 观察，未改 |
| 6 | 无 SPA 深链回退 | 当前 hash 路由不依赖，不构成缺陷 | 观察，未改 |
| 7 | 测试夹具端口 TOCTOU 偶发（重试 6 次仍可能败） | 偶发假失败，失败信息不指向真因 | 观察，未改 |

**这一轮没有为了让 CI 变绿而调阈值、删测试、改工作流或移动已公开 tag。**
补测只加测试与工具，产品代码的唯一改动是 `launcher.js` 里 `SG.__test__` 多挂 11 个已有函数的引用（+13 行，不改变运行时行为）。

## 七、提交与推送记录

| 提交 | 内容 |
|---|---|
| `e877a30` | 第一轮补测（C# 982 → 1155，前端 385 → 428） |
| `1c1571c` | 第二轮补测（C# 1155 → 1233，前端 428 → 448） |
| `3ef8d68` | 第三轮补测（C# 1233 → 1325） |
| `5bdebfa` | 外部依赖探测脚本 |

四个提交均已推送到 `remediation/v1.1.0`，远端 head 与本地一致，工作区干净。

**推送过程有网络波折（如实记录）**：本机走 `127.0.0.1` 代理，GitHub 的 `CONNECT` 通道间歇性返回 **502**。
`3ef8d68` 这次连试 5 次全失败，第 6 次成功；`5bdebfa` 一次成功。**没有使用任何绕过手段，就是重试。**

## 八、给复核人的三点提醒

1. **不要把 63.3% 读成"功能已验证"**。它只说明"已知可测的部分测了六成"，而剩下三成多里包含了所有 GUI 交互与所有真实外部集成。
2. **本轮最有价值的产出不是覆盖率数字，是 3.1–3.4 四条实测事实**。前两条是真实缺陷（其中 3.2 影响面最大），后两条是"行为正确但存在不一致"，都写成了会红的断言。
3. **补测里刻意保留了几个"钉住缺陷"的测试**（`DeleteDownload` 无范围校验、探测路径少一段、悬空 id 不纠正）。它们现在是绿的，但它们描述的是**当前行为**而不是**正确行为**。修缺陷时它们会变红，那时应该改断言而不是改产品——这是刻意的设计。

---

**证据位置**

- 覆盖率原始数据：`D:/pineglow-cov2` … `D:/pineglow-cov6`（Cobertura XML）
- 补测前后的提交说明：`C:/Pineglow-Backup/m12-split/*.txt`
- 外部端点探测：`node scripts/probe-external-deps.cjs --json <path>`
- 上一份功能测试汇总：`docs/functional-test-summary-2026-11-06.md`
