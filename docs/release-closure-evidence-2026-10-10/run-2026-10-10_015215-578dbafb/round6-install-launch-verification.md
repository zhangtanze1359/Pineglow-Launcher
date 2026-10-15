# 第 6 轮：D 的真实安装与启动证据

运行标识：`run-2026-10-10_015215-578dbafb`（第 6 轮追加）
时间：2026-10-10 02:56–03:07，Asia/Hong_Kong（+08:00）
本轮性质：**只做验证与取证，产品目录零改动**（A/B/C 相关文件哈希与前几轮一致，0 staged）。
所有动作都在 `%TEMP%` 下由 `portable.flag` 隔离的夹具里完成，配置与游戏目录都不碰真实用户目录。

## 1. 真实安装：通过产品自己的 API 装完一个原版版本

`probe-install-launch.ps1` 驱动真实接口链：`GET /api/java` → `GET /api/versions/remote` →
`POST /api/versions/download {versionId:"1.21.11"}` → 轮询 `GET /api/downloads` → 磁盘核对。

| 断言 | 结果 |
| --- | --- |
| 远端版本列表可达 | **918 个**版本，含目标版本 |
| 下载任务终态 | `completed`，耗时 **351 秒**，消息「安装完成」 |
| 版本目录落盘 | `versions/1.21.11/`：JSON 1 个、客户端 JAR 1 个 |
| 依赖库 | `libraries/` 下 **75 个 jar** |
| 资源对象 | `assets/objects/` 下 **4591 个文件** |

这是当前候选第一次有「真实网络 + 真实落盘 + 产品自身链路」的安装证据。原始记录 `d-install/install-install.json`、`d-install/d-install-console.log`。

## 2. Java：机器上没有，产品也没有可调用的安装入口（本轮发现）

- 本机 **没有 Java**（`java` 不在 PATH、无 `JAVA_HOME`），探针实测 `GET /api/java` 返回 `{"javaPath":"","installations":[]}`。
- 仓库里确实有自动安装实现 `JavaRuntimeService`（走 `https://api.adoptium.net/v3/assets/latest`，下载并解压 JDK/JRE），
  但**它只有一个调用点**：`InstancePackService.cs:757`，即**整合包导入**路径为了满足某个 Java 大版本要求时才触发。
- `LocalApiService` 里 `java` 只有 `GET /api/java`（检测）一个路由；UI 侧也没有 `javaRuntime/installJava` 之类的调用
  （全仓搜索 `JavaRuntimeService|javaRuntime|java-runtime|installJava|ensureJava` 共 11 处命中，全部在上述服务与它的测试里）。

**README 承诺的「Java：自动检测与自动安装」在实现上只有检测是直达的，自动安装只挂在整合包导入上。**
这属于「承诺与实现范围」的差异，按你的规则应记入发布范围说明，而不是当成整体失败——
但是否要在本版补齐一个独立的 Java 安装入口，需要你决定。

本轮为把「启动」这一段验通，夹具里自备了 **Temurin JRE 21.0.12.1**（取自**产品同一个 Adoptium 接口**），
放在 `%TEMP%` 的独立目录，并把夹具 `config.json` 的 `javaPath` 指向它。**这是环境适配，不属于产品链路**，已记录在结果 JSON 里。

## 3. 真实启动：到主菜单（`d-launch2/launch.json`，探针退出码 0）

链路：离线账号 → 创建实例 → 启动前检查 → 启动。

| 断言 | 结果 |
| --- | --- |
| 离线账号 | `{"ok":true,"account":{"type":"offline","userName":"d6tester",...}}` |
| 实例创建 | 返回实例 id |
| 启动前检查 | `ok=true`，`errors=[]`、`warnings=[]`，`maxMemoryMb=4096`，gameDir 指向夹具内 `.minecraft` |
| 启动调用 | `{"ok":true,"pid":13504,"minimizeOnLaunch":false}` |
| Java 进程 | 已观察到（按 `javaw.exe` 可执行路径精确匹配夹具 JRE） |
| **到达主菜单** | **是**（游戏日志出现资源重载与字体加载） |
| 自有进程清理 | `ownedProcessesStopped=true`（只停本夹具的启动器与夹具 JRE 的 java 进程） |

游戏日志片段（`data/.minecraft/logs/latest.log`）：

```
[Datafixer Bootstrap/INFO]: 287 Datafixer optimizations took 551 milliseconds
[Render thread/INFO]: Environment: Environment[sessionHost=https://sessionserver.mojang.com, ... name=PROD]
[Render thread/INFO]: Setting user: d6tester
[Render thread/INFO]: Backend library: LWJGL version 3.3.3+5
[Render thread/INFO]: Using optional rendering extensions: GL_ARB_buffer_storage, GL_KHR_debug, ...
[Render thread/INFO]: Reloading ResourceManager: vanilla
[Worker-Main-*/INFO]: Found unifont_*-17.0.01.hex, loading
```

即：正版会话服务地址、离线身份注入、LWJGL/OpenGL 后端、资源重载与字体加载都正常完成——**这是标题界面路径**。

## 4. D 仍未覆盖的部分（如实列出）

| 未完项 | 原因 |
| --- | --- |
| 进入世界、退出、重启 | 建世界/选世界是 GUI 交互；本轮全部通过本地 API 驱动，没有桌面点击能力，未做 |
| 代表性实例连续运行 30 分钟 | 未做（本轮只验证到主菜单） |
| 加载器矩阵 | Forge / NeoForge / Fabric / Quilt 的安装与启动**均未验证**；本轮只装了原版 |
| 整合包导入 | 未做（它是 Java 自动安装的唯一入口，也是 D 的既定项） |
| 全新在线安装（含断点续传/取消/失败重试） | 只验证了正常完成路径，未验证中断与重试 |

## 5. 本轮修正的自身问题

第一次跑启动探针时，游戏正在运行并占用 `latest.log`，我用 `[IO.File]::ReadAllText` 读它触发了 `IOException`，
导致主菜单判定循环提前中断（那一次的结论「未到主菜单」是**探针缺陷**，不是产品问题）。
改为以 `FileShare.ReadWrite` 共享读句柄读取后重跑，得到本轮的结论。
第一次的原始产物保留在 `d-launch/`（其 `launch.json` 仍显示 `reachedMainMenu=False`）。

## 6. 结论与状态

- **D 的「新安装 → Java → 启动」这一段已经从「未执行」变成「有真实证据」**：安装完成、落盘可核、启动到主菜单。
- D 的其余部分（进世界、长时运行、加载器矩阵、整合包、中断恢复）仍是证据缺口，其中进世界需要桌面/界面操作能力。
- 新发现：**Java 自动安装只在整合包导入路径上可达**，与 README 承诺范围存在差异，需要你决定是补入口还是收窄承诺。
- `TechnicalAcceptance=NOT_PASSED`（发布整体）；`FormalPhaseApproval=NOT_GRANTED`；`ReleaseApproval=NOT_GRANTED`。
