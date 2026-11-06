# Pineglow Launcher · 松光启动器

**把 Minecraft 的版本、实例和资源，放在一个顺手的地方。**

松光启动器是一款开源 Minecraft 启动器，面向希望方便地安装游戏、管理多个实例和整理内容的玩家。无论是启动原版、配置模组环境，还是为不同玩法保留独立的游戏目录，都可以从这里开始。

[下载与版本](https://github.com/zhangtanze1359/Pineglow-Launcher/releases) · [反馈问题](https://github.com/zhangtanze1359/Pineglow-Launcher/issues) · [许可证](./LICENSE)

## 你可以用它做什么

- **管理账号**：使用离线账号、微软账号或 Authlib 第三方皮肤站账号。
- **安装游戏与加载器**：安装、校验和修复官方版本，并配置 Forge、Fabric、NeoForge、Quilt 或 OptiFine。
- **整理不同玩法**：使用独立实例管理游戏目录、Java 设置和模组；支持导入 Modrinth 整合包。
- **寻找和管理内容**：浏览 Modrinth 内容，管理资源包、光影和存档，收藏服务器并查看状态。
- **处理运行环境**：检测或安装 Java，调整内存设置；通过日志、崩溃分析和诊断包排查问题。

下载、登录和在线内容依赖相应的网络与第三方服务；具体可用性请以所使用的版本和服务状态为准。

## 开始使用

1. 前往 [Releases](https://github.com/zhangtanze1359/Pineglow-Launcher/releases)，查看版本说明并下载适合的 Windows x64 附件。
2. 启动程序，按界面提示添加账号、选择或安装游戏版本，再配置 Java 和内存。
3. 选择实例并启动游戏。使用微软账号时，需要相应的 Minecraft 使用资格。

Windows 运行界面需要 WebView2 Runtime；如果系统尚未安装，请先安装。发布附件与所需文件以对应 Release 的说明和实际内容为准。

## 从源码构建

项目使用 .NET 10、Avalonia 和 HTML/CSS/JavaScript。需要安装 .NET 10 SDK；以下命令在仓库根目录运行：

```powershell
dotnet build dev/PineglowLauncher/Pineglow.Launcher.csproj -c Release
```

发布工作流的目标为 Windows x64。项目包含跨平台框架，但目前不将 Linux 和 macOS 视为已验证的正式支持平台。

### 平台支持范围（如实说明）

| 平台 | 状态 | 具体含义 |
|---|---|---|
| **Windows x64** | **正式支持** | 唯一发布目标。单文件 EXE + 更新清单 + 冷启动验证都在这条链上实跑。运行界面需要 WebView2 Runtime。 |
| Linux x64 | 仅构建验证 | CI 会编译 `linux-x64` 并跑前端测试，但**不保证能运行**。桌面宿主依赖 Win32 P/Invoke（窗口控制、进程管理）与 WebView2，Linux 上缺失这些能力，没有可用的边界替代。 |
| macOS x64 | 仅构建验证 | 同上。且 CI 的 `Run tests` 步骤刻意跳过 macOS（Avalonia WinExe 主项目在 testhost 下无法加载 `Pineglow.Launcher.dll`，属测试宿主依赖解析问题，非测试逻辑失败）。 |

**不要把"三平台 CI 通过"读成"三平台可用"。** CI 的三平台矩阵只保证**能编译**，Linux/macOS 不跑桌面宿主的真实链路。若需要在 Linux/macOS 上运行，需要先替换窗口宿主与 P/Invoke 层，这是独立的工作量，当前不在 v1.1.0 范围内。

## 项目结构

- `dev/PineglowLauncher/`：桌面宿主和 C# 服务。
- `dev/PineglowLauncher.Tests/`：测试项目。
- `ui/`：启动器的 HTML、CSS 和 JavaScript 界面。
- `.github/workflows/`：构建和发布工作流。

界面由 Avalonia WebView 承载，通过本地 API 与 C# 服务通信。

## 参与和反馈

欢迎通过 [Issues](https://github.com/zhangtanze1359/Pineglow-Launcher/issues) 报告问题或提出建议。反馈问题时，提供启动器版本、Windows 版本、复现步骤和相关日志会有助于定位；分享日志前请检查并移除账号信息等敏感内容。

## 许可证

项目采用 [GNU General Public License v3.0](./LICENSE)。

松光启动器是独立开源项目，与 Mojang 或 Microsoft 没有关联。Minecraft 是 Mojang AB 的商标。
