# 第 8 轮：加载器矩阵收口与代理补救验证

运行标识：`run-2026-10-10_015215-578dbafb`（第 8 轮追加）
时间：2026-10-10 03:41–04:12，Asia/Hong_Kong（+08:00）
本轮性质：**只做验证与取证，产品目录零改动**（A/B/C 相关文件哈希与前几轮一致，0 staged）。
全部动作在 `%TEMP%` 的 portable 夹具内完成；只停止本轮夹具自己的进程。

## 1. 结论：加载器矩阵 3/4 已被真实启动验证到标题界面

| 加载器 | 无代理补救（第 7 轮） | 加两个代理开关后（本轮） | 标题界面证据 |
| --- | --- | --- | --- |
| **Quilt 0.30.1** | 导入成功（617 秒） | — | `Loading Minecraft 1.21.11 with Quilt Loader 0.30.1` + OpenAL + Sound engine started |
| **Forge 61.2.1** | 失败（424 秒，`installer 失败，ExitCode=1`） | **成功（448 秒）** | `ModLauncher 10.2.6` / `--launchTarget forge_client` / OpenAL / Sound engine started |
| **NeoForge 21.11.45** | 失败（358 秒，同上） | **成功（396 秒）** | `FancyModLoader version 10.0.36 (CLIENT in PROD)` / OpenAL / Sound engine started |
| Fabric 0.19.5 | 失败（10 秒，Adoptium SSL） | 失败（6 秒 版本源 / 重试 330 秒 Fabric Meta SSL） | 无 |

三个成功的实例都建在**独立游戏目录** `<mc>/instances/<实例名>/` 下，`versions/` 里同时有原版与加载器 profile：

```
d7-quilt 导入      loader=Quilt      ver=0.30.1
d7-forge 导入      loader=Forge      ver=61.2.1
d7-neoforge 导入   loader=NeoForge   ver=21.11.45
```

启动前检查三项全部 `ok=true` 且无警告；`launch` 都返回了 pid；实例日志中都出现
`OpenAL initialized on device OpenAL Soft on 耳机 (Realtek(R)...)`、`Sound engine started`、
`Created: ... minecraft:textures/atlas/blocks.png-atlas`——这是标题界面路径。
原始日志：`d-loader-matrix-proxy/instance-latest-forge.log`、`instance-latest-neoforge.log`、`d-loader-matrix/quilt-instance-latest.log`。

两个日志里确实有报错，但都是**离线账号的预期现象**，不是产品缺陷：

- Forge：`Failed to fetch Realms feature flags`（Realms 鉴权失败）
- NeoForge：`Failed to fetch user properties ... InvalidCredentialsException: Status: 401`

## 2. 代理补救验证：第 7 轮的根因结论得到确认

本轮引入两个**互相独立**的开关（都只作用于夹具，不改产品）：

- `-Proxy 127.0.0.1:7897` → 写进产品的代理设置（影响 `ApiHttp`：元数据、Mod 下载）；
- `-JavaToolOptions "-Dhttps.proxyHost=127.0.0.1 -Dhttps.proxyPort=7897 …"` → 通过环境变量传递给**安装器子进程** JVM。

**只加这两个开关、其他都不变**，Forge 与 NeoForge 从 `installer 失败，ExitCode=1` 变成导入成功并可启动到标题界面。
这直接确认第 7 轮的判定：失败原因是**安装器子进程没有代理配置**，而产品自身的代理设置不会传给它。
同时这也给出了一条**用户侧可行的工作避开方案**（设置 `JAVA_TOOL_OPTIONS`），可写进发布说明。

产品确实有代理设置界面（`i18n.js`：「HTTP 代理」「下载与登录请求走代理（适合配合科学上网工具）」），
配置会用于 `ApiHttp/AiHttp/NewsHttp`（`RefreshApiHttpProxy`），但**不覆盖加载器安装器**。

## 3. Fabric：失败三次，但无法归因（尚未解决）

三次失败各不相同：

| 次序 | 时长 | 报错 |
| --- | --- | --- |
| 第 7 轮第 1 次 | 10 秒 | `无法准备整合包所需 Java：The SSL connection could not be established` |
| 第 8 轮第 2 次 | 6 秒 | `无法连接 Minecraft 版本下载源，已自动尝试官方和镜像地址。` |
| 第 8 轮第 3 次（单独重试） | 330 秒 | `无法连接 Fabric Meta API，请检查网络连接。The SSL connection could not be established` |

**但同机同址的其他客户端都正常**（本轮实测，均在失败窗口附近）：

| 客户端 | meta.fabricmc.net | api.adoptium.net | piston-meta.mojang.com |
| --- | --- | --- | --- |
| PowerShell `Invoke-WebRequest`（直连/走代理） | 200 | 200 | 200 |
| `curl.exe`（直连/走代理） | 200 | 200 | 200 |
| .NET Framework `HttpClient`（直连/走代理） | 200 | 200 | 200 |
| **.NET 10 `HttpClient`**（直连/走代理，即产品同代运行时） | 200 | 200 | 200 |

而且第 7 轮那次 Adoptium 失败之后，产品自己**成功下载了 47 MB 的 JRE**（落盘 144.5 MB）；
本轮产品在配置代理后也能正常列出 918 个远端版本。
因此**无法把 Fabric 的失败归因于产品代码、主机、或代理**：这是一个只在产品的 HTTP 客户端上出现的间歇性 TLS 失败，
且三次报错分属不同子系统（Adoptium、版本源、Fabric Meta），更像传输层偶发而不是某条链路的固定缺陷。
**结论：Fabric 未验证，归因未解决**；下一步需要抓取产品的原始握手/内部异常（或加一次性诊断日志）才能定性，这属于产品侧改动，需授权。

## 4. 探针可靠性说明（第三次）

矩阵探针里的 `reachedTitle` 自动判定**在本轮两次运行中仍不可用**（都报 false）：
第 8 轮那个后台作业在我把探针改成「递归找最新 latest.log」**之前**就已经把旧脚本读入执行了。
因此**标题界面的判定以实例日志为准**（第 1 节与第 3 节引用的是实例自己的 `logs/latest.log`，不是探针字段）。
探针的正确版本已落盘（`probe-loader-matrix.ps1` 现在会递归定位日志并记录 `gameLogPath`），供下次运行使用。

## 5. D 的状态（更新）

| 项 | 状态 |
| --- | --- |
| 原版安装 / Java 自动安装 / 原版启动到标题界面 | 已证（第 6 轮） |
| **Quilt / Forge / NeoForge 安装 + 启动到标题界面** | **已证（第 7、8 轮，经整合包导入）** |
| Fabric | 失败三次，归因未解决 |
| 普通实例装加载器的入口 | **不存在**（第 7 轮） |
| 进世界、退出/重启、连续 30 分钟 | 未做（需要界面操作） |
| 安装中断/重试/取消、整合包导入的进度与日志 | 未做；导入路径的 `_log` 仍是空操作 |

## 6. 状态

`TechnicalAcceptance=NOT_PASSED`（发布整体）；`FormalPhaseApproval=NOT_GRANTED`；`ReleaseApproval=NOT_GRANTED`。
累计待你决定的产品问题：加载器安装入口、代理不传递给安装器、安装器输出被丢弃、导入路径无进度/日志、Fabric 间歇 TLS 的定性。
