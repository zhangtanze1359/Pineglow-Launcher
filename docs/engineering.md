# 工程与服务层

> **最后核对日：2026-11-06**。包含服务层索引与 **LocalApiService 实现逻辑深析**。

## 一、服务层索引（103 个 `.cs`，2026-11-06 磁盘实测）

位于 `dev/PineglowLauncher/Pineglow.Launcher.Services/`，按职责分组。

> 口径：103 = 该目录下 `*.cs` 文件数（含模型/接口/内部辅助类，其中 18 个是 `LocalApiService` 的 partial），不等于「可注入的业务服务」数量。历史文档写的「约 67 个」「76 个」「85 个」分别是 2026-08、2026-10-02 与 M1-2 拆分前的旧快照，均已过时。该数字会随文件增删漂移，**引用时须注明核对日**。

### 启动与运行
- `LauncherService` — 构建 Java 启动命令、解析版本/classpath/natives、进程托管、游玩统计
- `LaunchCommand` — 启动命令行模型
- `JavaLocator` / `JavaInfo` / `JavaVersionMatcher` / `JavaRuntimeService` / `JavaRuntimeProgress` — Java 检测/下载/匹配
- `MemoryAllocationService` / `MemoryRecommendation` — 内存智能推荐
- `RulesEvaluator` — 特征规则评估
- `VersionFilter` / `VersionResolver` / `MinecraftVersion` / `MinecraftUtils` — 版本解析

### 账号
- `AccountService` / `MinecraftAccount` / `AuthClientOptions` / `AuthlibAccount`
- `MicrosoftAuthService`（设备码登录 + Refresh 续期 + `PollDeviceCodeOnceAsync` 单次轮询）/ `MicrosoftOAuthToken` / `MinecraftAppearance` / `MinecraftCapeInfo` / `DeviceCodeInfo`
- `AuthlibInjectorService` — 外置登录 javaagent
- `SecretStore` — 凭据安全存储（Windows 凭据管理器，非 Win 降级 secrets.json）

### 加载器与整合包
- `FabricService` / `ForgeService` / `NeoForgeService` / `QuiltService` / `InstallerLoaderService` / `LoaderInstallHelper` / `InstalledLoaderProfile`
- `ModrinthModpackService` / `InstancePackService` / `ModpackLifecycleService`

### 实例与内容
- `InstanceService` / `InstanceIsolationService` — 实例隔离
- `ModService` / `ModContentService` / `ModSearchQueryResolver` / `ModHealthService` — Mod 管理/搜索/健康检查
- `ResourceContentService` / `ResourcePackService` / `ShaderPackService` / `SaveService` / `SaveBackupService` — 资源/存档
- `ServerPingService` — 服务器 Ping

### 工程
- `LocalApiService` — HttpListener 桥接 + 静态托管（见下）
- `ConfigStore` / `PlatformPaths` / `LauncherLog` / `JsonUtil` / `EnumerableExtensions`
- `MinecraftDownloader` / `MinecraftDownloadProgress` — 下载引擎
- `DownloadTaskStatus` / `UpdateService` / `MirrorHelper` / `CrashAnalyzerService` / `CrashAnalysis` / `AiDiagnoseService` — AI 崩溃诊断（组装 prompt + 调 OpenAI 兼容接口）

## 二、LocalApiService 实现逻辑深析

### 角色定位：双服务器一体

同时做两件事：
- **API 服务器**：监听 `127.0.0.1:48123`，路由表 **81 条路由登记**（2026-11-16 实测 `grep -c 'r.Add(ApiRoute' LocalApiService.Routing.cs`；2026-11-06 时为 72，此后增长）。
- **静态文件服务器**：托管 `ui/` 目录（HTML/CSS/JS）。

前端通过 `fetch` HTTP 调用，而非 WebView 桥接 —— 这是"纯 Web UI 架构"的关键。

### 文件布局：按域拆分的 partial 文件（M1-2 起点 18 个，2026-11-16 为 19 个）

拆分前单文件 5,487 行 / 200 个顶层成员。现按域拆为 19 个 partial 文件（M1-2 时为 18；D3③ 新增 `LocalApiService.BatchUpdate.cs`）：

| 文件 | 行数 | 职责 |
|---|---|---|
| `LocalApiService.cs` | 494 | **宿主层**：端口/令牌/Origin 校验、listener 启停、请求入口安全前置、共用 JSON 与路径工具；**所有静态字段与静态初始化都留在此处** |
| `LocalApiService.Routing.cs` | 228 | 路由表（`Routes` / `BuildRoutes` / `ResolveRoute`）与 `HandleApiAsync` |
| `LocalApiService.Configuration.cs` | 416 | `LoadConfig` / `SaveConfig`、`PatchConfig` / `PatchAiProviders`、各测试注入缝 |
| `LocalApiService.Instances.cs` | 322 | 实例读/建/删/重新挂载、实例摘要 |
| `LocalApiService.Instances.Import.cs` | 266 | 整合包导入（含取消）、启动前自检 |
| `LocalApiService.Instances.Launch.cs` | 230 | 启动链：Java 解析、参数拼装、进程拉起 |
| `LocalApiService.Saves.cs` | 246 | 存档列举/备份/恢复/删除、皮肤上传 |
| `LocalApiService.Mods.cs` | 480 | Mod 启停/删除/替换、恢复区、资源包与光影包 |
| `LocalApiService.BatchUpdate.cs` | 173 | **D3③**：Mod 批量更新的 HTTP 层（建任务 / 查状态 / 取消 / 逐项撤销）；判定在 `BatchUpdatePlanner`，执行在 `BatchUpdateRunner` |
| `LocalApiService.Bisect.cs` | 370 | 二分定位：会话操作与单步执行 |
| `LocalApiService.Bisect.Persistence.cs` | 495 | 二分定位：落盘、跨重启恢复、依赖校验 |
| `LocalApiService.Diagnostics.cs` | 554 | AI 诊断主流程与取消、诊断报告落盘/历史/导出 |
| `LocalApiService.Accounts.cs` | 415 | 离线/Microsoft 设备码/authlib 登录、皮肤 |
| `LocalApiService.Content.cs` | 513 | 内容搜索、图标代理、安装任务机、世界列表 |
| `LocalApiService.Downloads.cs` | 143 | 版本列举与下载任务 |
| `LocalApiService.News.cs` | 247 | 新闻列表、外链跳转、图片代理 |
| `LocalApiService.Update.cs` | 241 | 自更新检查/下载/状态/应用 |
| `LocalApiService.Servers.cs` | 217 | 收藏服务器、统计、Java 列表、来源切换 |
| `LocalApiService.StaticFiles.cs` | 86 | `ui/` 静态托管与路径越界校验 |

**为什么静态字段不跟着域走**：C# 静态字段的初始化顺序按声明文本顺序决定，散到多个文件后"谁先初始化"会变成依赖文件名排序的隐式契约。因此字段全部挤在宿主文件，域文件只放方法。

**结构门槛**（`dev/PineglowLauncher.Tests/LocalApiServiceSplitTests.cs`）：单文件 ≤ 600 行；域文件清单**以磁盘枚举为准**（新文件放进源码目录即自动进入门槛，不再有登记表），且整个程序集里只允许一个以 `LocalApiService` 命名的类型；令牌/Origin 逻辑只允许由宿主持有（反射枚举程序集全部类型的声明成员并计数，外加两个真实调用确认行为）。每条门槛都带反向保护，防止被悄悄放宽成恒真。

> 2026-11-21（架构第 3 步）：这三条原本有两条是"把 19 个 partial 拼成一段文本再 grep"（`LocalApiServiceSource`），
> 已换成"可枚举 + 计数 + 真实请求"。为什么换、换成了什么、哪些保留，见
> `docs/architecture/local-api-instancing-2026-11-17.md` 的进度段。

### 生命周期（Start / Stop / Loop）

```
Start() → 解析端口(默认48123, PINEGLOW_API_PORT 覆盖)
        → 解析 ui 根目录(ResolveUiRoot, 多级回退)
        → new HttpListener + AddPrefix("http://127.0.0.1:{port}/")
        → _listener.Start() + Task.Run(LoopAsync)
LoopAsync() → while(_running)
            → GetContextAsync() 阻塞等待请求
            → 每请求 fire-and-forget: Task.Run(() => HandleAsync(context))  ← 并发
Stop()   → _running=false + listener.Stop/Close
```

**设计要点**：请求分发全异步，每个连接独立 `Task.Run`，可并发不互斥；`ConfigureAwait(false)` 不捕获 UI 同步上下文。

### 请求分发（HandleAsync → HandleApiAsync）

- `HandleAsync`：先 `ValidateOrigin`（不通过直接 403）→ `SetCors`（**只回显校验通过的 Origin**，从不写 `*`）→ OPTIONS 预检 204 → `/api/` 开头再校验 `X-Pineglow-Token`（`/api/health` 除外）→ `HandleApiAsync`；否则 `ServeStaticAsync`。
- `HandleApiAsync`：**查表分发**（M1-1，2026-11-06 起不再是手工 if 链）：

```
seg = path.Trim('/').Split('/')  →  ["api", "instances", "{id}", "launch", ...]
```

- 路由项声明为 `ApiRoute`（method + 段数区间 + 段索引→字面值 + handler），
  存在 `LocalApiService.Routes`（**72 条**，2026-11-06 实测）。**顺序即优先级**，取第一个命中项。
- 新增端点 = 在 `LocalApiService.Routing.cs` 的 `BuildRoutes()` 里登记一行；漏登记会被
  `ApiRouteInventoryTests` 抓到。表驱动的动机见 2026-10-03 的 10 条恒 404 事故（历史手工 if 链
  把要求长度 5/6/8 的守卫嵌进长度 4 的块内，条件恒假，当时 796 个绿色测试毫无感知）。
- 异常分类：`InvalidOperationException` → 400（业务错误），其他 → 500。

### 状态层：LoadConfig / SaveConfig（全量落盘）

- `LoadConfig()`：`ConfigGate` 锁 + `ConfigStore.LoadAsync()`，同步 `MirrorHelper.SelectedKey`。
- `SaveConfig()`：锁内全量写回 `config.json`（与桌面端共用同一文件）。
- `HandleGetStateAsync`：前端首屏总聚合接口（config 全字段 + instances + accounts + java）。

### 两类异步任务队列（核心机制）

下载类操作**不能阻塞 HTTP 响应**，设计成"入队即返回 + 后台跑 + 轮询状态"：

| 队列 | 锁 | 任务类型 | 状态机 |
|---|---|---|---|
| `DownloadTasks` | `DownloadGate` | 装 Minecraft 版本 | queued→downloading→completed/failed |
| `ContentTasks` | `ContentGate` | 装 Mod/整合包/光影/资源 | queued→running→completed/failed |

流程（以内容安装为例）：
1. `HandleContentInstallAsync` 校验 → **入队** → 立即返回 `{ok, id, status:"queued"}`。
2. 后台 `Task.Run(RunContentInstallTaskAsync)` 真执行。
3. 用 `UpdateContentTask(entry, msg, progress)`（加锁）更新进度 0→1。
4. 前端轮询 `GET /api/content|downloads` 拿进度。

### 启动游戏（HandleLaunchAsync）——业务最重 handler

按账号模式组装启动参数：
- **offline**：`config.PlayerName`，`accessToken="0"`, `userType="legacy"`。
- **microsoft**：校验 Microsoft 字段，填 `msa` + xuid。
- **authlib**：`EnsureAgentAsync` 下载 authlib-injector javaagent，拼 `-javaagent:...=serverUrl`。

然后 `LauncherService.LaunchAsync` 拉起 Java，记录 `LastLaunchTime`/`PlayCount++`，挂 `WaitForExitAsync` 累加 `TotalPlaySeconds`；配 `AutoCloseOnExit` 则退出后关闭启动器。

### 内容搜索（HandleSearchContentAsync）

按 category 走不同服务：modpack→ModrinthModpackService、shader/resourcepack→ResourceContentService、默认→ModContentService。20 秒超时，失败降级返回空数组。

### 静态服务与安全（ServeStaticAsync）

- 默认路径 → `index.html`。
- **路径穿越防护**：`Path.GetFullPath` 后校验以 `_uiRoot` 开头，否则 404。
- 按扩展名映射 Content-Type。

### 工具方法

- `ReadBodyAsync`：读请求体到 `JsonDocument`。
- `WriteJsonAsync`：统一 camelCase 序列化 + 写响应。
- `PatchConfig`：增量更新配置（只改出现的字段，null 跳过，布尔逐值处理）。

### 架构特点小结

| 特性 | 说明 |
|---|---|
| 无框架 | 纯 HttpListener + 表驱动路由，零 ASP.NET 依赖，轻量 |
| 全异步并发 | GetContextAsync + Task.Run 每请求独立任务 |
| 锁粒度 | ConfigGate / DownloadGate / ContentGate 分锁避免并发写冲突 |
| 懒创建服务 | 每个 handler 内 new 服务，无 DI 容器 |
| 出站客户端单一所有者 | `OutboundHttp` 产出具名客户端（api / ai / news / download），代理、超时、重定向策略只在那里决定；`LocalApiService` 不再持有 HttpClient 字段 |
| 依赖作为一个值 | `HostDependencies` = config store + `StorageRoots`（三个根）+ `OutboundHttp`，作为 `LocalApiHost` 的构造参数；没有"只换其中一个"的中间状态 |
| 任务即响应 | 重操作全部入队异步，HTTP 只"接受指令 + 轮询进度" |
| 按域拆文件 | 19 个 partial，单文件 ≤ 600 行（M1-2 结构门槛；清单按磁盘枚举，无需登记） |

### 潜在改进点（观察，未改）

1. ~~手工路由缺乏集中注册表~~ —— **已由 M1-1 解决**（`ApiRoute` 表 + 端点清单测试）。
   仍待处理：`ApiRoute.Range` 的 `Any = 64` 是段数规模的硬上界，超长路径会落到兜底 404。
2. `LoadConfig` 中 `GetAwaiter().GetResult()` 同步阻塞异步调用，并发下可能锁竞争 + 线程池压力。
3. 域文件之间仍有跨域直接调用（例如二分区调用 `ModService`、诊断区调用 `CrashAnalyzerService`），
   这是静态类的固有耦合；真要解耦需要引入接口与容器，属独立工作量，不在 v1.1.0 范围。
4. 旧客户端不 Dispose（候选 ⑤ 的选择）：换代理后旧的一组交给 GC，换取"换配置不打断在途请求"。
   代价是代理频繁切换时会有一小批未回收的连接池。要精确到"引用归零才销毁"需要借用语义，属单独一步。
5. ~~`OutboundHttp` 还是静态的~~ —— **已由候选 ③ 第 2 步解决**：它与 config store、三个存储根一起
   作为 `HostDependencies` 进 `LocalApiHost` 的构造参数，替换入口只剩 `OverrideHostDependenciesForTest`。
   仍未解决的是**多实例**：`LocalApiService` 的静态成员被全库调用，同一进程只能有一个 host 在跑。