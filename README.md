# Pineglow Launcher · 松光启动器

跨平台 Minecraft 启动器 —— 轻量、现代、开箱即用。

> **纯 Web UI 架构**:Avalonia 仅作 WebView2 宿主,界面由 `ui/` 下的 HTML/CSS/JS 实现,通过本地 `LocalApiService`(127.0.0.1:48123)与 C# 服务层通信。

## ✨ 功能

- **账号**:离线 / 微软正版（浏览器授权登录）/ Authlib 第三方皮肤站
- **版本**:官方版本安装 / 校验 / 修复,断点续传 + 可恢复任务队列
- **加载器与整合包**:Forge / Fabric / NeoForge / Quilt / OptiFine,Modrinth 整合包导入安装
- **实例**:独立隔离目录、实例级 Java、Mod 启停 / 依赖解析 / 健康检查
- **内容**:Modrinth 搜索、资源包 / 光影 / 存档管理、服务器收藏与状态 Ping
- **Java**:自动检测与自动安装、内存智能推荐
- **工程**:日志落盘、崩溃分析、诊断包导出、自更新框架、双语界面

## 🚀 构建

```bash
cd dev/PineglowLauncher
dotnet publish -c Release -r win-x64 --self-contained /p:PublishSingleFile=true
```

产物为 Windows x64 单文件自包含 exe（无需安装 .NET 运行时）。

### 运行环境要求（正式附件）

| 项 | 要求 | 说明 |
| --- | --- | --- |
| 操作系统 | Windows x64 | 正式附件只发布 `win-x64`；本轮验收环境为 Windows 11 x64 |
| WebView2 Runtime | **必需** | 界面由 WebView2 承载。Windows 11 与装有新版 Edge 的 Windows 10 通常已具备；缺失时需安装 Evergreen Runtime，否则窗口起不来界面 |
| .NET 运行时 | 不需要 | 附件是 self-contained 单文件，运行时随 EXE 自带 |
| 原生图形库 | 已内嵌 | SkiaSharp / HarfBuzz 等原生库随单文件自解包，不需要额外 DLL 或 `UI/` 目录 |

已知限制：CI 会编译 Linux/macOS 产物，但 WebView 与端到端运行目前仅在 Windows 验证，**不能视为正式跨平台发布支持**。

## 🏗 架构

```
Avalonia 壳(WebView2) ──加载──▶ ui/ 前端(HTML/CSS/JS)
        │                            │ fetch
        └──── LocalApiService ◀──────┘
                  │
        C# 服务层(LauncherService 等服务,JSON/文件持久化)
```

## 📄 许可证

[GPL-3.0](./LICENSE)

---

*松光启动器是一个独立开源项目,与 Mojang/Microsoft 无关联。Minecraft 是 Mojang AB 的商标。*
