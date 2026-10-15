# 开发指南（Pineglow Launcher）

> **事实基线：2026-10-02（MR01 刷新）。** 面向日常开发 / 构建 / 测试 / 发布，与本知识库其他文档配套阅读。
> 技术栈：**Avalonia 12（仅作 WebView2 宿主）+ Web 前端（HTML/CSS/JS）+ .NET 10 服务层**。
> ⚠️ **本项目不是 Tauri**，全仓对 `tauri` 零命中，不要套用 Tauri 生态方案。
> 功能状态分级见 [features.md](features.md)；架构陷阱见 [architecture.md](architecture.md)。

## 一、仓库与盘子结构

- **仓库**：`https://github.com/zhangtanze1359/Pineglow-Launcher.git`，主分支 `main`，LICENSE=GPL-3.0。
- **CI**：`.github/workflows/build.yml`（三平台构建 + Linux 测试）+ `release.yml`（发布 + Gitee 镜像）。
  ⚠️ CI **从未在本项目实际执行过**（未推送、未建 PR），三平台产物 ≠ 跨平台正式支持。

```
松光启动器/
├── dev/
│   ├── PineglowLauncher/            # 主项目（net10.0, x64, WinExe）
│   │   ├── Pineglow.Launcher/       #   入口（App/Program）
│   │   ├── Pineglow.Launcher.Models/#   数据模型（约 49 个）
│   │   ├── Pineglow.Launcher.Services/# 服务层（76 个 .cs，核心业务）
│   │   ├── Pineglow.Launcher.Views/ #   WebShellWindow（Avalonia 宿主）
│   │   ├── Pineglow.Launcher.csproj
│   │   └── VerifyHost/              # 轻量控制台宿主：起 LocalApiService + --bisect-smoke 冒烟
│   └── PineglowLauncher.Tests/      # xUnit 测试（仅纯逻辑，Linux 上跑）
├── ui/                              # Web 前端（HTML/CSS/JS，纯 Web UI 架构的关键）
│   ├── index.html
│   ├── js/{launcher,api,i18n}.js
│   └── css/figma.css
├── scripts/
│   ├── mirror-gitee.mjs             # Gitee 镜像发布脚本
│   ├── verify-source-snapshot.ps1   # 候选源码快照与可复现性验证（见 §五）
│   └── source-snapshot-inputs.txt   # 未跟踪输入白名单（53 项，精确相对路径）
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
  沙盒隔离依赖 `PlatformPaths` 的 portable 分支：配置与默认游戏目录都落到 `<BaseDirectory>/data`。
- 自动走完 **Mod Bisect 全流程断言**：start 禁用半区并落盘 → 三次 test 收窄 8→4→2→1 → 定位 `mod07-1.0.0.jar` 并自动恢复全部 Mod → giveup 分支恢复 → 无会话调用返回 400。
- 改动 `ModBisectService` 或 `/bisect/*` 路由后，以本命令回归。

> 🔴 **2026-10-02 实测：该命令当前 `exit=1`，首个 `POST /bisect/start` 即返回 404。**
> 根因是路由分支嵌套错误（`LocalApiService.cs:511` 要求 `seg.Length == 5`，
> 却被包在 `:415` 的 `seg.Length == 4` 块内），属不可达死代码。
> **在 `MR06` 修复前，不要把 `--bisect-smoke` 通过当作已知基线。**
> 排查技巧：预置 `portable.flag` 与 `data/` 目录可让冒烟**不自动清理**，便于事后读 `config.json` 与日志。

## 五之二、候选源码快照与可复现性验证

`scripts/verify-source-snapshot.ps1` 在系统 TEMP 下组装隔离快照并跑完整验证链。
工作区在 HEAD 之上有大量未提交改动且依赖**未跟踪**的生产源文件，只带 `git diff` 会漏掉依赖而无法编译；
该脚本用「全部 tracked 工作区版本 + 显式白名单（`scripts/source-snapshot-inputs.txt`，53 项精确相对路径）」
解决这一点。

```powershell
# 完整候选快照（GREEN）
./scripts/verify-source-snapshot.ps1 -EvidenceDir <证据目录>
```

- 白名单为**精确相对路径**，禁止通配与整批 `docs`；脚本启动即自检禁止模式。
- 校验：文件数量、**通用泄漏检测（额外文件必须为 0）**、白名单逐项 SHA256 与源一致、**构建前无 `bin`/`obj`**。
- 默认**保留**快照与证据目录供独立复验；`-CleanupSnapshot` 删除前做 5 项路径安全校验。

### ⚠️ 2026-10-02 实测发现验证器两个缺陷（已在隔离快照内修复，原件未改）

| 缺陷 | 现象 | 修复 |
|---|---|---|
| **假通过** | 临时 `.cmd` 以 `-Encoding ascii` 写出；当 `-EvidenceDir` 路径含非 ASCII 字符（如中文目录名）时被写成 `???`，`cmd.exe` 整行解析失败、**命令从未执行**，但 `%ERRORLEVEL%` 仍为 0 → 8 个步骤全部被记为 `exit=0 PASS`，而 `step*.log` 一个都没生成 | 日志重定向到 `%TEMP%` 下纯 ASCII 路径，再由 PowerShell 搬运；并加**护栏：日志未生成即按失败处理（exit 9001），绝不记 PASS** |
| **挂死** | `Start-Process -Wait` 会等待**整棵进程树**；`dotnet build` 派生的常驻 `VBCSCompiler.exe`（Roslyn 编译服务器，idle 约 10 分钟）父进程退出后仍存活，导致每个编译步骤空等约 10 分钟 | 去掉 `-Wait`，改用 `$proc.WaitForExit()`（只等 `cmd.exe` 自身） |

> **给后续执行者**：使用该脚本前请确认 `-EvidenceDir` 为纯 ASCII 路径，
> 或使用已修复的副本。**任何"全部步骤 1 秒 PASS"的结果都应先确认日志文件真的生成了。**

### 锁定边界（勿夸大为"全链锁定"）

- **非 RID 路径可锁定**：`dotnet restore <csproj> --locked-mode` 对 launcher 与 tests 均返回 0。
- **RID 发布不能锁定**：`dotnet publish -r win-x64 -p:RestoreLockedMode=true` 必然 `NU1004`
  （`packages.lock.json` 只覆盖 `net10.0`，且 RID 发布会额外引入 `Microsoft.NET.ILLink.Tasks`）。
  该步骤记为 **KNOWN-UNLOCKED**（未锁定边界），既不算通过，也不掩盖为普通失败。
- **不要把 `--locked-mode` 直接传给 `dotnet publish`**——它是 `dotnet restore` 专属开关，
  直接传会报 `MSB1001: 未知开关`；publish/build 阶段必须用 MSBuild 属性 `-p:RestoreLockedMode=true`。

### 2026-10-02 可复验结果

| 项 | 结果 |
|---|---|
| 快照 | 214 文件（tracked 161 + 白名单 53），泄漏 0，`bin`/`obj` 0，逐文件 SHA 与源全一致 |
| Node 正式测试 | 184 通过 / 0 失败 / 0 跳过（11 个 `.test.cjs`） |
| C# Release 全套 | 612 通过 / 0 失败 / 0 跳过 |
| 单文件发布 | `.../net10.0/win-x64/publish/Pineglow.Launcher.exe`，45,644,552 字节，SHA256 `3B71DCC1…` |
| RID 锁定 | **KNOWN-UNLOCKED**（NU1004） |
| CI / 真实 GUI / 真实游戏 | **未验证** |

> ⚠️ 产物路径务必区分：`<rid>/Pineglow.Launcher.exe` 是 build 输出的 **apphost**（约 335 KB），
> **不是**单文件发布产物；真正产物在 `<rid>/publish/` 下（约 45 MB）。2026-09-28 曾因此取错对象。

## 六、改动纪律（重要）

1. **服务层原样复用，勿重写**：核心逻辑在 `Pineglow.Launcher.Services`（76 个 `.cs`），前端 / API / UI 只是"接通 + 写盘"。改功能先改服务，再补 API 端点与前端封装。
2. **新增 API 端点**：在 `LocalApiService.HandleApiAsync` 的手工路由处加分支（`seg` 逐段匹配），并同步更新 `docs/api-reference.md`。
   **⚠️ 硬规则：要求更长 `seg.Length` 的分支绝不能嵌套在要求更短 `seg.Length` 的分支内**（详见 [architecture.md](architecture.md) 第三节五）。
   改完必须跑 `--bisect-smoke` 或新增打到 HTTP 层的测试——只测服务层**发现不了**路由不可达。
3. **前后端双写**：`api.js` 的 `live` 与 `_mock` 必须同步（见第四节）。
4. **版本单一来源**：`AssemblyInfo.cs`（`GenerateAssemblyInfo=False` 手动管理）运行时为准，csproj 的 `<Version>` 供发布脚本引用，二者必须一致。
5. **配置改动**：`AppConfig` 增删字段涉及全量落盘 `config.json`；含密钥的字段加 `[JsonIgnore]` 避免明文落盘（先例：`CurseForgeKey`）。
6. **设计令牌**：颜色值一律走 `ui/css/figma.css` 顶部 token（`--base/--card/--green/--text/...`），禁止在组件里硬编码色值（见 `docs/design-system.md`）。
7. **隐私**：涉及日志/崩溃内容外发（如未来 AI 诊断）时，需提示用户内容含用户名/绝对路径。

## 七、发布与镜像

- 发布走 `release.yml`（三平台构建 + 生成发布物）。
- Gitee 镜像：`scripts/mirror-gitee.mjs`，由 `release.yml` 联动触发，将发布 real 同步到 Gitee 仓库。

## 八、常见排障

| 现象 | 排查方向 |
|---|---|
| 前端改了但运行没变化 | 检查 `ui/` 是否被拷入输出目录（csproj `PreserveNewest`）；确认访问的是 `http://127.0.0.1:48123` 而非缓存旧资源 |
| API 请求 404 | `LocalApiService.HandleApiAsync` 手工路由未匹配；对照 `docs/api-reference.md` 检视 `seg` 长度与 action 拼写。**若路由"明明写了"仍 404，先查它是否被嵌在要求更短 `seg.Length` 的分支内**（见 §六 纪律 2 与 architecture.md §三点五） |
| 端口被占用 | 换环境变量 `PINEGLOW_API_PORT` 指定端口 |
| 测试在本地跑失败 | 确认是本机 .NET 版本差异，还是逻辑改动；CI 只在 Linux 跑 |
| 页面串叠 / 渲染竞态 | `launcher.js` 用 `navSeq` 做渲染竞态抑制，改动导航逻辑时勿破坏该机制 |
