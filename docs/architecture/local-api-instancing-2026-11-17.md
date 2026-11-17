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
| **B. 依赖**（搬走后 handler 需要拿得到） | `_defaultConfigStore`（`ProductionConfigStore`）、`ConfigGate`、`ApiHttp` / `AiHttp` / `NewsHttp`（含代理热替换）、`CreateRecoveryService` 的三个根 | 现在靠静态 override 注入；实例化后改成构造参数 |
| **C. 域注册表**（已按 id 键控，天然可多实例） | `DownloadTasks`/`DownloadGate`、`ContentTasks`/`ContentGate`、`UpdateState`/`UpdateGate`、`MicrosoftDeviceSessions`/`MicrosoftDeviceGate`、`AiOps`、`PackOps`、`BisectInFlight`、`Routes`、`NewsService`/`NewsLinks`/`NewsImages` | 键是 instanceId / operationId，两个 host 之间不会互相踩；但**同一 id 两个 host 会踩**，测试需按实例号隔离 |

`SecretStore` 的 override、`ContentIconService.Default` 等进程级单例属于 B/C 之外的第 4 类，单独列在遗留清单。

## 三步走

### 第 1 步：宿主对象（A 类搬家）

- 新增 `LocalApiHost`：持有 listener、端口、uiRoot、running 标志、SessionToken。
- `LocalApiService.Start/Stop/IsRunning/Port` 变成**对默认 host 的薄委托**（对外 API 与行为不变）。
- 验收：全量测试不变绿变红都不该发生（纯搬家）；`Start` 前后 `Port` 可读且与 `LocalApiHost.Port` 一致。
- 不动 B、C 类 → 测试并行度不变（这一步不承诺提速）。

### 第 2 步：依赖从构造参数进（B 类搬家）

- `LocalApiHost` 构造参数：`IConfigStore`、`IRoots`（recovery/bisect/diagnostic 三个根）、`OutboundHttp`（api/ai/news 三个具名客户端）。
- harness 不再用静态 override，而是 `new LocalApiHost(store, roots, http, port)`。
- 验收：删掉 `OverrideConfigStoreForTest` 等 4 个静态作用域中的**至少两个**，全部测试仍绿；
  `RealConfigGuard`（防写 `%APPDATA%`）保留 —— 它是安全网，不是 seam。

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
- 下一步（第 2 步）：`LocalApiHost` 收 `IConfigStore` / `IRoots` / `OutboundHttp`，harness 不再用静态 override。
  验收：删掉 4 个静态 override 里的至少两个，全部测试仍绿。
