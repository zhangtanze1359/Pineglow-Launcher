# LocalApiService 实例化：现状盘点与三步走（2026-11-17）

> 来源：架构评审候选 ③（`%TEMP%/architecture-review-20261117-0233.html`）。
> 本文只记录**事实**与**切法**，不含未落地的承诺。落地进度按提交号追加。

## 为什么不是"一次改完"

`LocalApiService` 是 `static partial class`，进程级唯一。测试因此只能：

- 起真 `HttpListener` 抢真端口（绑定期 TOCTOU → 6 次换端口重试）；
- 用 4 个静态 override 作用域（config store / recovery / bisect / diagnostics）代替注入；
- 41 个测试类进 `DisableParallelization`（`LocalApiServiceTestCollection.cs`）；
- 两条逃逸：反射私有静态（`AiCancelAndRedirectTests` 抓 `AiHttp`，两个名字都试），
  以及**断言源码文本**（`LocalApiServiceSource` 拼 19 个 partial 再 grep）。

一次性改完要同时搬走 30+ 个静态成员并重写 23 个 harness 测试类，风险不可控。所以分三步，每步都可以单独回滚、单独验收。

## 静态状态盘点（2026-11-17 实测）

按"搬走的难度"分三类：

| 类 | 成员 | 说明 |
| --- | --- | --- |
| **A. 宿主状态**（最该搬、最好搬） | `_listener`、`_uiRoot`、`_running`、`Port`、`SessionToken`、`JsonOpts` | 只被 start/stop/请求入口用；与业务域无耦合 |
| **B. 依赖**（搬走后 handler 需要拿得到） | `_defaultConfigStore`（`ProductionConfigStore`）、`ConfigGate`、`OutboundHttp`（候选 ⑤ 已完成：宿主拥有 api/ai/news/download 四个具名客户端，包含代理热替换）、`CreateRecoveryService` 的三个根 | 现在靠静态 override 注入；实例化后改成构造参数 |
| **C. 域注册表**（已按 id 键控，天然可多实例） | `DownloadTasks`/`DownloadGate`、`ContentTasks`/`ContentGate`、`UpdateState`/`UpdateGate`、`MicrosoftDeviceSessions`/`MicrosoftDeviceGate`、`AiOps`、`PackOps`、`BisectInFlight`、`Routes`、`NewsService`/`NewsLinks`/`NewsImages` | 键是 instanceId / operationId，两个 host 之间不会互相踩；但**同一 id 两个 host 会踩**，测试需按实例号隔离 |

`SecretStore` 的 override、`ContentIconService.Default` 等进程级单例属于 B/C 之外的第 4 类，单独列在遗留清单。

## 三步走

### 第 1 步：宿主对象（A 类搬家）

- 新增 `LocalApiHost`：持有 listener、端口、uiRoot、running 标志、SessionToken。
- `LocalApiService.Start/Stop/IsRunning/Port` 变成**对默认 host 的薄委托**（对外 API 与行为不变）。
- 验收：全量测试不变绿变红都不该发生（纯搬家）；`Start` 前后 `Port` 可读且与 `LocalApiHost.Port` 一致。
- 不动 B、C 类 → 测试并行度不变（这一步不承诺提速）。

### 第 2 步：依赖从构造参数进（B 类搬家）

- `LocalApiHost` 构造参数：`HostDependencies` = `IConfigStore` + `StorageRoots`（recovery/bisect/diagnostic）
  + `OutboundHttp`（api/ai/news/download 四个具名客户端）。
- harness 不再用四个静态 override，而是一次 `OverrideHostDependenciesForTest(d => d.WithStore(...).WithRoots(...))`。
- 验收：4 个静态 override **全部**退役（不是"至少两个"），全部测试仍绿；
  `RealConfigGuard`（防写 `%APPDATA%`）保留 —— 它是安全网，不是 seam。

**实际落点与原计划的差别（刻意）**：

原计划写的是三个独立构造参数。落地时合成一个 `HostDependencies` 值，理由是
"三个参数可以只传一个"与"四个静态 override 可以只换一个"是同一个漏洞：漏掉的那个会写真实 `%APPDATA%`
（第 47 轮实测：诊断报告一直在写）。合成一个值之后没有"只隔离了三个"的中间状态。

仍然只有一个 host 能运行（`LocalApiService` 的静态成员被全库调用），
所以这一步还没有带来"多实例"。多实例属于第 3 步的范围。

### 第 3 步：退役源码断言与反射（C 类收尾）

- `LocalApiServiceSource`（19 partial 拼接）与 `LocalApiServiceSplitTests` 的字符串 grep 退役，
  换成"每个域对象暴露自己的路由表"这种可枚举断言。
- `AiCancelAndRedirectTests` 反射 `AiHttp` 改为从 host 注入。
- 验收：`grep -r "GetField\|GetProperty" dev/PineglowLauncher.Tests` 归零；
  测试集合可以从 1 个串行集合拆成按域并行的 N 个。

## 明确不做的

- 不为"行数"继续拆 partial。`MaxLinesPerFile = 600` 是按体积切的，**不是 seam**（候选 ①）。
- 不把域注册表改成实例私有**除非**第 3 步证明有测试需要两个 host 用同一 instanceId。
- 不在 `v1.0.x` 发布线上做这三步：改动面覆盖整个 API 层，发布线只允许小步（见 D0 决策）。

## 进度

- 2026-11-17 早期：盘点与切法成文（本文件）。
- 2026-11-17 上午：**第 1 步完成**。新增 `Services/LocalApiHost.cs`（listener / 端口 / UI 根 / 运行标志），
  `LocalApiService` 只持有一个 `_host`，`Port` / `IsRunning` / `UiRoot` / `Start` / `Stop` / `LoopAsync` 全部改为委托。
  静态字段 `_listener` / `_uiRoot` / `_running` 已从库里消失（`grep` 为 0）。
  `LocalApiHostTests` 3 条（绑定、重复停止幂等、端口被占抛 `HttpListenerException`——正是 harness 换端口重试的前提）。
  门槛：C# 1424 → 1427 / 0 跳过、前端 489、css-tokens 49、闭包通过。
- 前置已完成：候选 ② `RequestScope`（提交 `89e0b97`）—— 它先把"一次请求需要什么"钉成了类型，
  第 2 步的构造参数可以直接复用它。
- 2026-11-17 下午：**候选 ⑤ 落地**（与第 2 步同方向、但不依赖它）。新增 `Services/OutboundHttp.cs`：
  api / ai / news / download 四个具名客户端，代理签名 + 整组原子替换 + 「旧组交给 GC」的销毁口径；
  `AiHttpClientFactory` 删除（它的全部内容就是一个 `new`，留着等于第二份 AI 策略）。
  `LocalApiService` 的 `ApiHttp` / `AiHttp` / `NewsHttp` **三个静态字段连同属性名一起消失**，
  27 处调用点直接读 `Outbound.Api/Ai/News/Download`。
  顺带修掉评审点名的真缺陷：`Instances.Launch.cs` 的 `new HttpClient()`（忽略用户代理、不释放）
  改为 `Outbound.Download`（跟随代理、600s 下载超时）。
  `OutboundHttpTests` 11 条：字段/属性归零、四条客户端的超时与重定向契约、代理只在签名变化时重建、
  **假代理真收到请求**（绝对 URI）、关代理时不走代理、AI 覆盖只影响 AI、自建 `new HttpClient` 白名单。
  反向验证：把启动路径改回 `new HttpClient()` 时 2 条判红。
  门槛：C# 1432 → 1443 / 0 跳过、前端 489、css-tokens 49、闭包通过。
- 2026-11-17 晚：**第 2 步完成**。依赖作为构造参数进宿主：
  - 新增 `Services/IConfigStore.cs`（接口 + `ProductionConfigStore`）、`Services/StorageRoots.cs`
    （三个根一个不可分割值）、`Services/HostDependencies.cs`（store + roots + http）。
  - `LocalApiHost` 构造参数改为 `HostDependencies`，并新增
    `ReplaceDependencies`（**运行中拒绝**：运行中换依赖会让在途请求拿到一半旧一半新）。
  - `LocalApiService` 的 4 个静态 override（config store / recovery / bisect / diagnostic）
    **全部退役**，只留 `OverrideHostDependenciesForTest(Func<HostDependencies,HostDependencies>)`
    一个入口；`Outbound` 也从静态字段改为读 `_host.Dependencies.Http`。
  - 夹具同步：`LocalApiTestHarness`、`TestIsolationRoots`、`LauncherStartupBoundaryTests`、
    `AiCancelAndRedirectTests`、`NewsFakeIpConfigTests`、`NewsLinkHandlerTests` 全部改为一次替换。
  - `HostDependenciesTests` 8 条：4 个旧 override 不得复活（反射）、依赖不可变、
    三个根必须落在配置目录下、替换可回滚到**替换前那一份**（不是回到默认）、
    运行中拒绝替换、以及一条**真 HTTP 接线证据**
    （`GET /api/state` 读到注入 store 的内容，`POST /api/config` 写进注入 store）。
  - `LocalApiHostTests` 与 `TestHostDependencies` 跟进构造参数。
  - 变异验证（3 次，均已还原）：把 `OverrideRecoveryRootForTest` 加回来 → 结构守卫红；
    去掉运行中拒绝 → 对应用例红；让 harness 只换 store 不换 roots → 接线证据红
    （`Expected: .../Temp/pineglow-recovery-test-...` / `Actual: .../AppData/Roaming/MCMiniLauncher/recovery`）。
  - 门槛：C# 1444 → 1452 / 0 跳过、前端 508、css-tokens 49、闭包通过。
- 2026-11-21：**第 3 步（切法 A：退役源码字符串断言）完成**（分支 `arch/step3a`）。
  - `dev/PineglowLauncher.Tests/LocalApiServiceSource.cs`（19 partial 拼接 + 硬编码顺序表 + `MethodBody` 截取）
    **删除**；替代物是 `ProductionServicesSource.cs`（只有"源码目录 / 单文件路径 / 单文件文本 / 磁盘枚举域文件"，
    没有 `All()`、没有域清单）。
  - 9 条源码文本断言逐条换法（清单与探针结果见提交信息）：可枚举数据（`ApiRoute` 表、反射枚举
    程序集类型与成员、磁盘枚举 partial 文件）、真实 HTTP 响应形状、对象图接线（新闻提供器的客户端工厂）。
  - **保留 2 条**读单文件文本的形态守卫（`LaunchDomain_DelegatesToHostedDownloadClient`、
    `HandleApiAsync_NoLongerContainsSegLengthBranches`），理由写在各自方法上：`LocalApiService` 是
    `static partial class`，反射没有"哪个成员/语句属于哪个文件"的视图，而这两条守的正是"某文件里有没有这种形态"。
  - 未做的部分（仍未开）：`AiCancelAndRedirectTests` 里 `AiOps.Count` 的反射仍在（`AiOperationRegistry`
    是私有静态字段，改成公开会扩大 API 面，属另一步）；测试集合仍是一个串行集合，按域并行的拆法没动。
  - 门槛（`node scripts/gates.cjs`，exit 0）：C# **1485 → 1485**（换法不改条数，0 跳过）、前端 508、
    css-tokens 49、闭包通过。改前数字是在同一棵树上 `git stash` 后复跑 `--only csharp` 取的。
    注：`docs/baseline.md` 记的 1453 属于更早的 `main@dcdf056`，与本分支无关（`gates.cjs` 已报基线过期）。
  - 下一步（第 4 步候选，未开始）：多实例（域注册表按 host 实例隔离）与测试集合按域并行。
