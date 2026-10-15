# Pineglow Launcher · 松光启动器

跨平台 Minecraft 启动器 —— 轻量、现代、开箱即用。

> **纯 Web UI 架构**:Avalonia 仅作 WebView2 宿主,界面由 `ui/` 下的 HTML/CSS/JS 实现,通过本地 `LocalApiService`(127.0.0.1:48123)与 C# 服务层通信。

## ✨ 功能

> 状态分级与逐项证据见 [docs/features.md](docs/features.md)。
> 本文只列**已确认可用或已确认存在**的项；不把"代码写了"当作"能用"。

- **账号**:离线 / 微软正版（浏览器授权登录）/ Authlib 第三方皮肤站
- **版本**:官方版本安装 / 校验 / 修复,断点续传 + 可恢复任务队列
- **加载器与整合包**:Forge / Fabric / NeoForge / Quilt / OptiFine（**共 5 种**）；Modrinth `.mrpack` 与 CurseForge 整合包导入
- **实例**:独立隔离目录、实例级 Java、依赖解析 / 健康检查
- **内容**:Modrinth 搜索、资源包 / 光影 / 存档管理、服务器收藏与状态 Ping
- **Java**:自动检测与自动安装、内存智能推荐
- **工程**:日志落盘、崩溃分析、诊断包导出、自更新框架、双语界面

### ⚠️ 已知缺陷（2026-10-02 核实）

- **Mod 启停/删除与 Mod 二分定位的 HTTP 路由当前不可达（返回 404）**——
  `LocalApiService` 的路由分支被错误嵌套在 `seg.Length == 4` 的块内。
  UI 已接线、服务层已实现，但端到端不可用。详见 [docs/architecture.md](docs/architecture.md) 与
  [docs/features.md](docs/features.md) §1.1。
- **Mod 删除为不可恢复的 `File.Delete`**，无隔离与撤销。
- **RID 发布无法锁定还原**（`packages.lock.json` 只覆盖 `net10.0`，已知 NU1004 边界）。

## 🚀 构建

```bash
cd dev/PineglowLauncher
dotnet publish -c Release -r win-x64 --self-contained /p:PublishSingleFile=true
```

产物为 Windows x64 单文件自包含 exe（无需安装 .NET 运行时）。CI 会编译 Linux/macOS 产物，但 WebView 与端到端运行目前仅在 Windows 验证，不能视为正式跨平台发布支持。

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
