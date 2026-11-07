# 开发指南（Pineglow Launcher）

> **最后核对日：2026-11-06**。面向日常开发 / 构建 / 测试 / 发布，与本知识库其他文档配套阅读。
> 技术栈：Avalonia 12（仅作 WebView2 宿主）+ Web 前端（HTML/CSS/JS）+ .NET 10 服务层。

## 一、仓库与盘子结构

- **仓库**：`https://github.com/zhangtanze1359/Pineglow-Launcher.git`，主分支 `main`，LICENSE=GPL-3.0。
- **CI**：`.github/workflows/build.yml`（三平台构建 + Linux 测试）+ `release.yml`（发布 + Gitee 镜像）。

```
松光启动器/
├── dev/
│   ├── PineglowLauncher/            # 主项目（net10.0, x64, WinExe）
│   │   ├── Pineglow.Launcher/       #   入口（App/Program）
│   │   ├── Pineglow.Launcher.Models/#   数据模型（52 个 .cs）
│   │   ├── Pineglow.Launcher.Services/# 服务层（103 个 .cs，核心业务）
│   │   ├── Pineglow.Launcher.Views/ #   WebShellWindow（Avalonia 宿主）
│   │   ├── Pineglow.Launcher.csproj
│   │   └── VerifyHost/              # 轻量控制台宿主：起 LocalApiService + --bisect-smoke 冒烟
│   └── PineglowLauncher.Tests/      # xUnit 测试（仅纯逻辑，Linux 上跑）
├── ui/                              # Web 前端（HTML/CSS/JS，纯 Web UI 架构的关键）
│   ├── index.html
│   ├── js/{launcher,api,i18n}.js
│   └── css/figma.css
├── scripts/mirror-gitee.mjs          # Gitee 镜像发布脚本
├── docs/                             # 本知识库（唯一可信知识源）
└── .github/workflows/
```

> 前端通过 csproj 的 `<Content Include="..\..\ui\**\*">` 以 `PreserveNewest` 拷入输出目录，改前端无需重编 C#。

## 二、环境要求

- **.NET SDK 10.0.x**（`dotnet --list-sdks` 确认）。
- **Windows**：WebView2 运行时（见 `WebShellWindow` 的 WebView2 宿主）。
- 前端为纯静态资源，无 Node 依赖；`scripts/mirror-gitee.mjs` 仅在发布镜像时用 Node 跑。

## 三、构建 / 运行

```powershell
# 1. 还原 + 构建（Debug）
dotnet build dev/PineglowLauncher/Pineglow.Launcher.csproj

# 2. 直接运行
dotnet run --project dev/PineglowLauncher/Pineglow.Launcher.csproj

# 3. 发布（单文件自包含，与 CI 一致）
dotnet publish dev/PineglowLauncher/Pineglow.Launcher.csproj `
  -c Release -r win-x64 --self-contained `
  /p:PublishSingleFile=true /p:EnableCompressionInSingleFile=true `
  /p:DebugType=None /p:DebugSymbols=false
# 产物：dev/PineglowLauncher/bin/Release/net10.0/win-x64/publish/
```

> 运行时 `LocalApiService` 监听 `127.0.0.1:48123`（可用环境变量 `PINEGLOW_API_PORT` 覆盖端口），前端 `ui/index.html` 由它静态托管。

## 四、前端开发（双模式）

前端无构建步骤，`ui/` 即源码即产物。`ui/js/api.js` 按协议自动切换：

- **mock 模式**：以 `file://` 直接打开 `ui/index.html`，`SG.apiMode === 'mock'`，走内存假数据，用于 UI 验收 / 降级兜底。
- **live 模式**：以 `http://` 访问（经 `LocalApiService` 或任意静态服务器），`SG.apiMode === 'live'`，走 `fetch` 调用真实 C# API。

本地纯 UI 预览（不启动 C#）：
```powershell
python -m http.server 8000 --directory ui
# 浏览器打开 http://localhost:8000/  ← live 模式（但 API 不可达，会报错）
# 或直接双击 ui/index.html        ← mock 模式
```

> 新增前端接口时，必须在 `api.js` 的 `live` **和** `_mock` 两侧各写一份同形状实现，保持双模式一致。

## 五、测试

### 单元测试（xUnit，纯逻辑）

```powershell
# 跑全部单测
dotnet test dev/PineglowLauncher.Tests/Pineglow.Launcher.Tests.csproj -c Release
```

- 覆盖纯逻辑服务：`ModBisectServiceTests`（二分状态机）、`AiDiagnoseServiceTests`、`InstanceIsolationServiceTests`、`JavaVersionMatcherTests`、`MemoryAllocationServiceTests` 等。
- CI 当前在 Linux 运行完整 xUnit 套件，并在 Windows 运行 API 冒烟；完整 xUnit 套件已在 Windows 本地验证通过。macOS 的 WebView/端到端运行仍未验证。
- **不写 GUI / WebView 集成测试**，UI 层靠 mock 模式人工验收。

### API 级端到端冒烟（bisect-smoke）

```powershell
# 构建轻量宿主 + 跑 Mod Bisect 全流程冒烟（Windows）
dotnet build dev/PineglowLauncher/VerifyHost/VerifyHost.csproj
dev/PineglowLauncher/VerifyHost/bin/Debug/net10.0/VerifyHost.exe --bisect-smoke
# 可选 --port N 指定端口；退出码 0=通过，1=失败
```

- 进程内自建 **portable 沙盒**（`portable.flag` + 输出目录下 `data/`），绝不读写真实 `%APPDATA%\MCMiniLauncher` 配置；结束后自动清理沙盒并释放端口。
- 自动走完 **Mod Bisect 全流程断言**：start 禁用半区并落盘 → 三次 test 收窄 8→4→2→1 → 定位 `mod07-1.0.0.jar` 并自动恢复全部 Mod → giveup 分支恢复 → 无会话调用返回 400。
- 改动 `ModBisectService` 或 `/bisect/*` 路由后，以本命令回归。

## 六、改动纪律（重要）

1. **服务层原样复用，勿重写**：核心逻辑在 `Pineglow.Launcher.Services`（103 个 `.cs`，2026-11-06 实测），前端 / API / UI 只是"接通 + 写盘"。改功能先改服务，再补 API 端点与前端封装。
2. **新增 API 端点**：在 `LocalApiService.Routing.cs` 的 `BuildRoutes()` 里登记一行 `ApiRoute`，handler 放进对应的域文件（`LocalApiService.<Domain>.cs`，见 `docs/engineering.md` 的文件布局表），并同步更新 `docs/api-reference.md`。**新增路由必须同时补一条打到 HTTP 层的路由可达测试**——只测服务层发现不了「守卫条件恒假、路由落进 404 兜底」这类缺陷（2026-10-03 曾有 10 条路由因此永不可达）。`ApiRouteInventoryTests` 会枚举 `Routes` 抓漏登记。
3. **前后端双写**：`api.js` 的 `live` 与 `_mock` 必须同步（见第四节）。**新请求必须带 `X-Pineglow-Token` 头**，`live` 通道里由统一封装注入，不要在业务代码里逐个手写。
4. **版本单一来源**：`dev/PineglowLauncher/Pineglow.Launcher.csproj` 的 `<Version>` 是程序集、文件与发布产物的**唯一来源**（csproj 内已有注释说明）。仓库中**不存在** `AssemblyInfo.cs`；`GenerateAssemblyInfo` 保持默认（true）。历史文档曾写「AssemblyInfo.cs 为运行时准、csproj 供发布脚本引用」，与代码不符，已更正——不要按旧说法手写第二处版本号。
5. **配置改动**：`AppConfig` 增删字段涉及全量落盘 `config.json`；含密钥的字段加 `[JsonIgnore]` 避免明文落盘（先例：`CurseForgeKey`）。
6. **设计令牌**：颜色值一律走 CSS token，禁止在组件里硬编码色值（见 `docs/design-system.md`）。**激活来源**：`ui/css/figma.css` 先加载、`pineglow.css` 随后覆盖、`redesign.css` 最后；同名 token 以**后加载者**为准（`--base` 最终为 `pineglow.css` 的 `#10191b`，不是 figma.css 的 `#0d0e11`）。
7. **本地 API 安全边界**：进程内会话令牌 + Origin 回环同端口白名单 + `PathSafetyGuard`。改 `LocalApiService` 的静态资源投放或鉴权时先读 `docs/api-reference.md` 1.1 节，**不要**把 CORS 改回 `*`，也不要把令牌写进日志或落盘。
8. **隐私**：涉及日志/崩溃内容外发（如 AI 诊断）时，需提示用户内容含用户名/绝对路径。

## 七、发布与镜像

- 发布走 `release.yml`（三平台构建 + 生成发布物）。
- Gitee 镜像：`scripts/mirror-gitee.mjs`，由 `release.yml` 联动触发，将发布 real 同步到 Gitee 仓库。

## 八、常见排障

| 现象 | 排查方向 |
|---|---|
| 前端改了但运行没变化 | 检查 `ui/` 是否被拷入输出目录（csproj `PreserveNewest`）；确认访问的是 `http://127.0.0.1:48123` 而非缓存旧资源 |
| API 请求 404 | `LocalApiService.Routes` 未命中；对照 `docs/api-reference.md` 检查该端点是否已登记到 `BuildRoutes()`，以及 `seg` 段数字面值与 action 拼写 |
| 端口被占用 | 换环境变量 `PINEGLOW_API_PORT` 指定端口 |
| 测试在本地跑失败 | 确认是本机 .NET 版本差异，还是逻辑改动；CI 只在 Linux 跑 |
| 页面串叠 / 渲染竞态 | `launcher.js` 用 `navSeq` 做渲染竞态抑制，改动导航逻辑时勿破坏该机制 |
