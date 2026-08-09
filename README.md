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

产物为单文件自包含 exe（无需安装 .NET 运行时）。Linux / macOS 同理替换 `-r`。

## 🏗 架构

```
Avalonia 壳(WebView2) ──加载──▶ ui/ 前端(HTML/CSS/JS)
        │                            │ fetch
        └──── LocalApiService ◀──────┘
                  │
        C# 服务层(LauncherService 等 67 个服务,SQLite 存储)
```

详细见《松光启动器-纯WebUI架构说明.md》。

## 📄 许可证

[GPL-3.0](./LICENSE)

---

*松光启动器是一个独立开源项目,与 Mojang/Microsoft 无关联。Minecraft 是 Mojang AB 的商标。*
