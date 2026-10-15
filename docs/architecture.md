# 架构（纯 Web UI）

> 反映 2026-08-16 状态。本文是架构层面的唯一可信源，取代旧的《纯WebUI架构说明》。

## 一、总体架构

```
┌────────────────────────────────────────────────┐
│  Avalonia 壳（仅作 WebView2 宿主）              │
│   App.cs → WebShellWindow（无边框+Win11圆角）    │
│         │ 加载 http://127.0.0.1:48123/?v=<时间戳> │
├────────────────────────────────────────────────┤
│  Web 前端（ui/ 目录：HTML/CSS/JS）              │
│   9 视图：home/installations/worlds/servers/    │
│           content/skins/downloads/news/settings │
│   i18n 双语（zh/en）｜ live/mock 双模式         │
└──────────────────┬─────────────────────────────┘
                   │ fetch http://127.0.0.1:48123/api/*
                   ▼
┌────────────────────────────────────────────────┐
│  C# 服务层（原样复用，勿重写）                   │
│   LocalApiService（HttpListener 桥接 + 静态托管） │
│   InstanceService/LauncherService/下载/账号/     │
│   版本/Mod/Java/更新 等约 67 个服务              │
└────────────────────────────────────────────────┘
```

## 二、三层职责

### 1. Avalonia 壳（`Pineglow.Launcher.Views/Shell/WebShellWindow`）

职责**极简**，只做：
- **加载前端**：`OnWindowOpenedAsync` 先 `GET /api/health` 探测 API，成功则加载 `http://127.0.0.1:48123/?v=<时间戳>`；失败则降级加载 `bin/.../UI/index.html`（file:// 模式）。
- **缓存破除**：Source 必须带 `?v=<yyyyMMddHHmmss>` 时间戳，否则 WebView 缓存旧 UI。
- **原生窗口能力**（Win32 hook）：
  - `InstallResizeHook`：子类化 WNDPROC，拦截 `WM_NCHITTEST`，实现 8px 边缘无边框缩放。
  - `ApplyRoundedCorners`：DwmSetWindowAttribute 开 Win11 圆角（仅 build 22000+）。
  - **窗口消息桥**：`OnWebMessageReceived` 解析前端 `window.postMessage`，处理 `window-control`（minimize/toggle-maximize/close）与 `drag-start/move/end`（自定义拖拽）。
- **调试**：`PINEGLOW_WEBVIEW_DEBUG` 环境变量可开 `--remote-debugging-port`。

> ⚠️ **禁止**在此层放业务逻辑。它是纯宿主。

### 2. Web 前端（`ui/`）

- `index.html`：页面骨架 + 9 个 `<div class="view">` 容器。
- `js/launcher.js`：`VIEWS` 表定义 9 视图（render + bind 成对），`goTo()` 懒渲染 + 导航竞态保护。
- `js/api.js`：所有后端调用封装，`live`（HTTP）+ `_mock`（file:// 内存降级）双通道。
- `js/i18n.js`：中英双语键。
- `css/figma.css`：设计系统（见 design-system.md）。

**铁律**：前端**禁止**重复实现启动/下载等核心逻辑，一律调 `LocalApiService` API。

### 3. C# 服务层（`Pineglow.Launcher.Services/`）

- 约 67 个服务，静态类/实例类并存，构造注入，无 IoC 容器。
- 均以 `Pineglow.Launcher.*` 命名空间（MCMini 残留已清零，仅剩用户数据路径 `%AppData%\MCMiniLauncher`）。
- `LocalApiService` 是唯一对前端暴露的网关（详见 engineering.md / api-reference.md）。

## 三、通信协议

- 本体：`http://127.0.0.1:48123`（端口可用环境变量 `PINEGLOW_API_PORT` 覆盖）。
- 前端 → 后端：`fetch` + HTTP（GET/POST/DELETE/OPTIONS），CORS 全放开（`*`）。
- 前端 → 窗口：`window.postMessage`（`window-control` / `drag-*`）。
- 降级：`file://` 模式下 `api.js` 走 `_mock`，界面可独立演示。

## 四、关键设计决策

| 决策 | 说明 |
|---|---|
| 纯 Web UI | Avalonia 只做壳，UI 全 Web。旧 XAML/MVVM/Strangler 路线已废弃，相关文件已删除 |
| 本地 API 桥接 | 前端 ≠ 直接碰文件/进程，全部经 HttpListener，安全且可 mock |
| live/mock 双模式 | HTTP 下真实、file:// 下内存，便于高保真验收与降级 |
| 服务层复用 | 8/10 大规模补齐时服务层基本原样复用，只加 API 桥 + 写盘接通 |

## 五、目录导航

| 路径 | 内容 |
|---|---|
| `dev/PineglowLauncher/` | C# 工程（Models / Services / Views.Shell / Tests） |
| `ui/` | 当前激活前端（index.html + css/figma.css + js/{launcher,api,i18n}.js） |
| `docs/` | 本知识库 |
| `scripts/mirror-gitee.mjs` | Gitee 镜像发布脚本 |
| `.github/workflows/` | build.yml（三平台构建 + Linux 测试）+ release.yml（发布 + Gitee 镜像） |