# 第 7 轮：加载器可达性与 Modrinth 导入路径实测

运行标识：`run-2026-10-10_015215-578dbafb`（第 7 轮追加）
时间：2026-10-10 03:08–03:56，Asia/Hong_Kong（+08:00）
本轮性质：**只做验证与取证，产品目录零改动**（A/B/C 相关文件哈希与前几轮一致，0 staged）。
全部动作在 `%TEMP%` 的 portable 夹具内完成，未碰真实用户目录；只停止本轮夹具自己的进程。

## 1. 主要发现：加载器安装在产品里**没有入口**，只有整合包导入能触发

**接口实测**（`d-loader-reachability/loader-reachability.json`）：产品只有一个版本安装入口 `POST /api/versions/download {versionId}`，
它对四个加载器 id 全部失败：

| 请求 versionId | 结果 |
| --- | --- |
| `1.21.11`（对照，原版） | `[downloading] 正在获取 Minecraft 1.21.11 元数据...` |
| `fabric-loader-0.17.2-1.21.11` | `[failed] 在线版本清单中找不到 fabric-loader-0.17.2-1.21.11` |
| `quilt-loader-0.28.0-1.21.11` | `[failed] 在线版本清单中找不到 quilt-loader-0.28.0-1.21.11` |
| `1.21.11-forge-61.0.1` | `[failed] 在线版本清单中找不到 1.21.11-forge-61.0.1` |
| `neoforge-21.11.0` | `[failed] 在线版本清单中找不到 neoforge-21.11.0` |

原因在 `MinecraftDownloader.InstallVersionAsync`：它只从 **Mojang 在线版本清单**里按 id 查表，加载器 id 天然不在里面。

**全仓溯源**（排除 bin/obj）：

- `ForgeService` / `NeoForgeService` / `FabricService` / `QuiltService` 的构造点只有三处：
  `InstancePackService.cs:481/485/489/497`（**整合包导入**按 `ModLoaderKind` 分派）、`VerifyHost/Program.cs`（验收宿主）、测试。
- `LocalApiService` 的请求体解析中**没有任何** `loader / loaderType / loaderVersion` 字段；`/api/instances` 只接受 `name` 与 `version`。
- UI 侧（`ui/js/*.js`）只把 `loader` 用于**显示**（`LOADER_COLORS`、实例标签、Mock 数据），没有任何安装按钮或选择器。
- `OptiFineService` **在仓库里根本不存在**。

**结论**：README 承诺的「加载器与整合包：Forge / Fabric / NeoForge / Quilt / OptiFine」中，
**加载器安装只能通过整合包导入（.mrpack/CurseForge/MCM）间接获得**；用户无法给一个普通实例装加载器，OptiFine 无实现。
这属于「承诺与实现范围」的差异，需要你决定：补入口，还是在发布说明里收窄承诺。

## 2. 唯一可达路径的实测：自建最小 .mrpack 逐个加载器

因为导入接口接收的是上传的压缩包（`fileBase64`），本轮**自建**了四个最小 `.mrpack`
（`modrinth.index.json` 里 `files: []`、只声明 `minecraft` + 加载器依赖），这样只测加载器安装本身，不引入 Mod 下载变量。

| 加载器 | 导入结果 | 说明 |
| --- | --- | --- |
| **Quilt 0.30.1** | **成功（617 秒）** | 实例 `d7-quilt 导入`，`loaderType=Quilt`、`loaderVersion=0.30.1`；启动前检查 `ok=true`/无警告；`launch` 返回 `pid`；Java 进程起来 |
| Fabric 0.19.5 | 失败（10 秒） | `无法准备整合包所需 Java：The SSL connection could not be established` |
| Forge 61.2.1 | 失败（424 秒） | `Forge installer 失败，ExitCode=1。` |
| NeoForge 21.11.45 | 失败（358 秒） | `NeoForge installer 失败，ExitCode=1。` |

导入失败的三个加载器**都把自己创建的实例目录清理掉了**（磁盘上只剩 quilt 那个），与实现的「绝不留下半成品实例」一致。

### Quilt 的完整证据（这是本轮最硬的一条）

实例使用**独立游戏目录** `<fixture>/data/.minecraft/instances/d7-quilt 导入/`，
其中 `versions/` 同时有 `1.21.11` 与 `quilt-loader-0.30.1-1.21.11`；实例自己的 `logs/latest.log` 开头与标题界面标记：

```
[03:20:39] [main/INFO]: Loading Minecraft 1.21.11 with Quilt Loader 0.30.1
[03:20:39] [main/INFO]: Loading mappings: .../libraries/org/quiltmc/hashed/1.21.11/hashed-1.21.11.jar!/mappings/mappings.tiny
[03:20:39] [main/INFO]: Loaded mapping namespaces: [official, intermediary]
...
[03:20:59] [Render thread/INFO]: Created: 2048x2048x4 minecraft:textures/atlas/blocks.png-atlas
...
（marker 命中：Sound engine started = true）
```

即：**整合包导入 → 加载器安装 → 独立实例 → 启动前检查 → 启动 → 标题界面**这条链对 Quilt 是通的。
原始文件：`d-loader-matrix/quilt-instance-latest.log`、`d-loader-matrix/loader-matrix.json`、`d-loader-matrix/matrix-plan.json`。

## 3. Forge / NeoForge 失败的根因（A/B 实测，不是推测）

`LoaderInstallHelper.RunInstallerAsync` 是这样拉起安装器的：`java -jar <installer> --installClient <gameDir>`，
**没有任何代理参数**，`RedirectStandardOutput/Error = true` 但输出只交给 `_log`——而导入路径传的是空操作 `_ => { }`。

用同一个安装器（`forge-1.21.11-61.2.1-installer.jar`）与同一个 JRE 做 A/B：

| 条件 | 退出码 | 输出尾部 |
| --- | --- | --- |
| 与产品一致（无代理参数） | **1** | `Downloading minecraft client failed, invalid checksum.` / `Try again, or use the vanilla launcher...` / `There was an error during installation` |
| 追加 `-Dhttps.proxyHost=127.0.0.1 -Dhttps.proxyPort=7897`（本机系统代理） | **0** | `... Checksum Validated: 632ace45...` / `Injecting profile` / `Successfully installed client into launcher.` |

因此 Forge/NeoForge 在本机失败的原因是**安装器子进程无法直连下载**；本机存在系统代理 `http://127.0.0.1:7897/`，
而产品的代理设置不会传给安装器 JVM。原始证据：`d-forge-installer-ab/forge-installer-ab.json`、`plain-stdout.txt`、`with-proxy-stdout.txt`。

**三个相关的产品缺口**（都值得进候选清单）：

1. 用户在启动器里配置的代理**不会传播到加载器安装器**，所以有代理的网络里 Forge/NeoForge 必然失败；
2. `LocalApiService.ApiHttp` 在未配置代理时显式 `handler.UseProxy = false`，**忽略系统代理**——
   同机上 PowerShell 走系统代理能访问的地址，产品直连可能失败（本轮 Fabric 那次的 SSL 报错与 Adoptium 直连失败即发生在这一背景下）；
3. 安装器的 stdout/stderr 被捕获后**丢弃**，导入路径的 `_log` 是空操作，用户只看到 `ExitCode=1`，没有任何可定位信息。

补充事实：Java 自动安装最终是**成功**的——夹具里出现了 `<configDir>/runtimes/java-21-windows-x64/jdk-21.0.12.1+1-jre`（144.5 MB）。
所以 Fabric 那次的 SSL 失败是**瞬时网络失败**（下一次同类请求成功），不是系统性缺陷；但它的报错形态（只给 SSL 原文）与导入路径无日志，会让人无法判断。

## 4. 本轮修正的自身缺陷（两处，都影响过结论）

1. **实例日志路径判错**：matcher 一开始读 `<mc>/logs/latest.log`，但整合包实例使用**独立游戏目录**，
   真正的日志在 `<mc>/instances/<实例名>/logs/latest.log`。所以矩阵里 Quilt 的 `reachedTitle=false` 是**探针缺陷**，
   以实例日志为准（第 2 节已给出标题界面证据）。原始错误产物保留在 `d-loader-matrix/`（含被误读的 `latest-quilt.log`）。
2. **A/B harness 第一次是无效的**：Windows PowerShell 5.1 跑在 .NET Framework 上，`ProcessStartInfo.ArgumentList` 不存在，
   于是安装器**没带任何参数**被执行，两次都是 exit 1。该结果已作废，改用 `Arguments` 字符串重跑才得到第 3 节的结论。

## 5. D 的当前状态

| 项 | 状态 |
| --- | --- |
| 原版安装（真实网络、落盘可核） | 已证（第 6 轮：1 jar + 75 库 + 4591 资源） |
| Java 检测与自动安装 | 自动安装已证可用（144.5 MB 落到 `runtimes/`）；但只在整合包导入路径可达 |
| 原版启动到标题界面 | 已证（第 6 轮） |
| 加载器（Quilt）安装 + 启动到标题界面 | **本轮已证**（经整合包导入） |
| 加载器（Forge/NeoForge） | 失败，根因已定位（代理未传递），未修复 |
| 加载器（Fabric） | 未完成（一次瞬时 SSL 失败中断） |
| 普通实例装加载器的入口 | **不存在** |
| 进世界、退出/重启、连续 30 分钟 | 未做（需要界面操作） |
| 安装中断/重试/取消 | 未做 |

## 6. 状态

`TechnicalAcceptance=NOT_PASSED`（发布整体）；`FormalPhaseApproval=NOT_GRANTED`；`ReleaseApproval=NOT_GRANTED`。
本轮新发现的三项（加载器无入口、代理不传递、安装器输出被丢弃）都属于**产品问题**，需要你决定是否列入本版修复范围。
