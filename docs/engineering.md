# 工程与服务层

> **最后核对日：2026-11-06**。包含服务层索引与 **LocalApiService 实现逻辑深析**。

## 一、服务层索引（85 个 `.cs`，2026-11-06 磁盘实测）

位于 `dev/PineglowLauncher/Pineglow.Launcher.Services/`，按职责分组。

> 口径：85 = 该目录下 `*.cs` 文件数（含模型/接口/内部辅助类），不等于「可注入的业务服务」数量。历史文档写的「约 67 个」「76 个」分别是 2026-08 与 2026-10-02 的旧快照，均已过时。该数字会随文件增删漂移，**引用时须注明核对日**。

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
- **API 服务器**：监听 `127.0.0.1:48123`，暴露约 50 个 REST 端点给前端。
- **静态文件服务器**：托管 `ui/` 目录（HTML/CSS/JS）。

前端通过 `fetch` HTTP 调用，而非 WebView 桥接 —— 这是"纯 Web UI 架构"的关键。

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

- `HandleAsync`：`SetCors`（放开 `*`、`Cache-Control: no-store`）→ OPTIONS 预检返回 204 → `/api/` 开头走 `HandleApiAsync`，否则 `ServeStaticAsync`。
- `HandleApiAsync`：**基于路径分割的手工路由**（无框架）：

```
seg = path.Trim('/').Split('/')  →  ["api", "instances", "{id}", "launch", ...]
```

- 一串 `if (seg.Length == N && seg[1].Equals(...) && method == "...")` 硬编码匹配。
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
| 无框架 | 纯 HttpListener + 手工路由，零 ASP.NET 依赖，轻量 |
| 全异步并发 | GetContextAsync + Task.Run 每请求独立任务 |
| 锁粒度 | ConfigGate / DownloadGate / ContentGate 分锁避免并发写冲突 |
| 懒创建服务 | 每个 handler 内 new 服务，无 DI 容器 |
| 任务即响应 | 重操作全部入队异步，HTTP 只"接受指令 + 轮询进度" |

### 潜在改进点（观察，未改）

1. 手工路由缺乏集中注册表，新增接口需加多个 `if`，易与现有 seg 长度混淆。
2. `LoadConfig` 中 `GetAwaiter().GetResult()` 同步阻塞异步调用，并发下可能锁竞争 + 线程池压力。