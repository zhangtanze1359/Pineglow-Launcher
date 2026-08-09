# Pineglow Launcher（松光启动器）纯 Web UI 架构说明

> **本文档是项目现状的唯一真源**，反映 2026-08-10 的最新状态。
> 早期文档（技术对比 / 开发经验总结 / UI-UX 设计文档）写于纯 Web UI 决策（2026-08-09）之前，**仅供历史参考，已被本文档取代**。
> 涉及本项目开发/设计/发布任务时，先读本文档 + `.workbuddy/memory/` 日记忆。

---

## 一、架构总览（纯 Web UI 方案，2026-08-09 决策）

```
┌──────────────────────────────────────────────┐
│  Avalonia 壳（仅作 WebView2 宿主）            │
│   App.cs → WebShellWindow（无边框窗口+Win11圆角）│
│         ↓ 加载 http://127.0.0.1:48123/?v=<时间戳> │
├──────────────────────────────────────────────┤
│  Web 前端（ui/ 目录：HTML/CSS/JS）            │
│   8 视图：home/installations/worlds/servers/  │
│           content/skins/downloads/settings    │
│   i18n 双语（zh/en）｜ live/mock 双模式       │
└──────────────┬───────────────────────────────┘
               │ fetch http://127.0.0.1:48123/api/*
               ▼
┌──────────────────────────────────────────────┐
│  C# 服务层（原样复用，勿重写）                 │
│   LocalApiService（HttpListener 桥接 + 静态托管）│
│   InstanceService / LauncherService / 下载/账号/ │
│   版本/Mod/Java/更新等 67 个服务              │
└──────────────────────────────────────────────┘
```

- **UI 层用 Web 技术栈，不写 XAML**；C# 服务层质量高，原样复用。
- 前端通过 `LocalApiService`(HttpListener, 127.0.0.1:48123) 调 C#，**禁止前端重复实现启动/下载等核心逻辑**。
- 旧 XAML 路线（Strangler / ShellWindow+HomePage / MVVM）已废弃，相关文件已删除，勿再参考旧文档。

## 二、目录导航（D:\松光启动器）

| 路径 | 内容 |
|---|---|
| `dev/PineglowLauncher/` | C# 工程。命名空间已全部 `Pineglow.Launcher.*`（MCMini 残留清零） |
| `ui/` | **当前激活前端**：`index.html` + `css/figma.css` + `js/{launcher,api,i18n}.js` |
| `dev/figma/` | Figma 设计稿导出图（page-home/installations/settings/skins.png） |
| `dev/shots/`、`验收截图-live/` | 验收截图留档 |
| `dev/_删除备份-20260809/` | 被删除的旧 UI 备份（MainWindow.cs 等，勿依赖） |
| `ui-备份-*/`、`UI原型*.html` | 原型迭代备份（历史） |
| `.workbuddy/memory/` | 项目日记忆 + MEMORY.md 长期约定 |

## 三、设计 Token（当前激活：官方 MC Launcher 风格 + 绿色）

- 背景 `--base:#0d0e11`、卡片 `--card:#15171e`、描边 `--border:#252936`
- 强调绿 `--green:#38e04d`、主按钮 `--green-dark:#1e832a`
- 文字 `--text:#fff` / `--muted:#8b91a1` / `--subtle:#5f6473`
- ⚠️ **旧天光蓝（#7C9CFF）方向已被取代**，新页面禁止使用
- ⚠️ **figma.css 仍有 50+ 处硬编码色值违反 token 规则**（遗留，待清理）
- 设计红线：深色默认；对比度正文≥4.5:1；动效≤600ms；强调元素每屏≤2 处

## 四、API 协议（LocalApiService）

| 端点 | 方法 | 用途 |
|---|---|---|
| `/api/health` | GET | 存活检查 |
| `/api/state` | GET | 全局状态（config/instances/version/pid…） |
| `/api/instances` `{id}` `{id}/delete\|launch\|saves\|mods\|resourcepacks\|shaderpacks` | GET/POST | 实例管理 |
| `/api/accounts` `offline` `select` `microsoft` | GET/POST | 账号（微软登录走浏览器授权） |
| `/api/versions` `remote` `download` | GET/POST | 版本清单/下载 |
| `/api/downloads` | GET | 下载任务状态 |
| `/api/config` | POST | 写 config.json |
| `/api/worlds` `/api/servers` `{id}/ping\|delete` | GET/POST | 存档/服务器 |
| `/api/content/search?q&category` | GET | 内容搜索（Modrinth） |
| `/api/java` | GET | Java 检测 |
| `/api/logs/open` | POST | 打开日志目录 |
| `/api/update/check` `download` `apply`、`/api/update` | GET/POST | 自更新 |

- 错误响应体含 `error` 或 `message` 字段，前端统一解析
- **坑**：后端反序列化 camelCase JSON 时 `System.Text.Json` 需 `PropertyNameCaseInsensitive=true`

## 五、功能状态矩阵

| 视图 | 状态 | 备注 |
|---|---|---|
| home | ✅ 完整 | 启动/停止真实链路，静态新闻 |
| installations | ✅ 完整 | Mod 管理弹窗的 toggle 仅 UI（写盘待接入） |
| worlds | ⚠️ 部分 | 读存档列表，点击仅 toast |
| servers | ✅ 完整 | 增删/ping 真实 |
| content | ⚠️ 部分 | 搜索真实，安装按钮仅 toast |
| skins | ⚠️ 部分 | 预览真实，上传未实现 |
| downloads | ✅ 完整 | 远程版本+下载任务轮询 |
| settings | ✅ 完整 | 内存/Java/离线账号/日志/更新/微软登录 |

## 六、已实现的 P0（2026-08-10 完成）

- 命名空间统一 `Pineglow.Launcher.*`（MCMini 仅剩 `%AppData%\MCMiniLauncher` 用户数据路径，改需迁移）
- 版本号单一来源：`LauncherService.LauncherVersion` = 程序集版本 1.0.0
- 微软账号浏览器登录（`POST /api/accounts/microsoft`）
- 日志落盘（`%AppData%\MCMiniLauncher\logs\launcher-yyyyMMdd.log`，保留 7 天）
- 自更新框架（check/download/sha256/apply，`updateUrl` 走 config.json）

## 七、遗留事项（未完成）

- 🔴 合规：无 git 仓库 / LICENSE / CI（宣称开源但查无仓库）
- 🔴 自更新源未部署（manifest 服务器待配置）
- 🔴 微软登录 DeviceCode 登录码降级（二期）
- 🟠 GameLaunchService.cs 死代码（390 行，无人引用，待清理）
- 🟠 figma.css 硬编码色值待 token 化
- 🟠 Content 安装 / Mod toggle / 皮肤上传未写盘
- 🟠 单文件发布未压缩（105MB，`EnableCompressionInSingleFile`）
- 🟠 无测试体系
- 🟡 下载源仅 BMCLAPI、CurseForge 需 Key（国内生死项）
- 🟡 皮肤站预设（LittleSkin）未做

## 八、操作纪律（必读）

1. **改 `ui/` 后必须 `dotnet build`**：csproj 将 ui/ 作为 Content 同步到 `bin/.../UI/`，否则 exe 读旧文件。
2. **WebView 缓存**：`WebShellWindow` Source 必须带 `?v=<时间戳>`；界面不刷新先查这个。
3. **构建命令**：`cd dev/PineglowLauncher && dotnet build -c Debug`（当前 0 错误，约 130+ 个既有警告）。
4. **后端新增 API** 注意 JSON 大小写（见四）。
5. **端到端测试**会改写真实 `%AppData%\MCMiniLauncher\config.json`，测前备份、测后还原。
