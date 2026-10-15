# Forge/NeoForge 判定：**Forge 同病（artifact 级证据）** + 安装阶段一个未解 404（第 51 轮）

日期：2026-10-06（Asia/Hong_Kong）。本轮**未改动任何 tracked 文件**。
两件事：① 用"安装器里的 version.json"把 **Forge 是否同病** 从"待定"变成**有 artifact 级证据**；
② 记下一个**新的、尚未解释**的 Forge 安装失败（HTTP 404）。

## 1. Forge：**同病**（不需要装它也能判定）

Forge 的 profile 由**官方安装器**生成，所以直接看安装器里嵌的 `version.json` 即可
（`forge-1.20.1-47.4.3-installer.jar`，6,068,826 B，官方 maven，HTTP 200）：

| 字段 | 值 |
| --- | --- |
| `id` | `1.20.1-forge-47.4.3` |
| `inheritsFrom` | **`1.20.1`**（继承型 profile —— 与 Fabric 同一形态）|
| `mainClass` | `cpw.mods.bootstraplauncher.BootstrapLauncher` |
| `arguments.jvm` | **16 条** |
| **其中是否有 `-Djava.library.path`** | **❌ 没有**（16 条里是 `-DignoreList`、`-DmergeModules`、`-DlibraryDirectory=${library_directory}`、`-p`、`--add-modules`…）|
| `libraries` | 29 |

⇒ 因为 `VersionResolver` 对 `arguments` 是**整体替换**（第 49 轮），Forge profile 这 16 条会把父版本
（1.20.1）那套含 `-Djava.library.path=${natives_directory}` 的 jvm 参数**整体挤掉**，
而 Forge 自己**也没带**库路径 ⇒ **Forge 实例与 Fabric 一样会在 LWJGL 初始化时崩**。
（这与第 50 轮"待定"相比是**升级**：不用跑一遍安装也能定，依据是**安装器自己的产物**。）

## 2. 影响面（更新，含证据类型）

| 加载器 | 证据类型 | 自带库路径？ | 结论 |
| --- | --- | --- | --- |
| **Fabric** | **实测**（导入 + 启动，第 47 轮）| ❌（官方 profile 只有 1 条 jvm）| **🔴 已复现崩溃** |
| **Forge** | **artifact 级**（安装器内 `version.json`，本轮）| ❌（16 条里没有）| **🔴 同病（机制相同）** |
| **Quilt** | 代码路径同构（`QuiltService` 同样原样写官方 profile）| 未逐字核对 | **🔴 极可能同病** |
| **NeoForge** | 未核对（该 maven 元数据里没有 1.20.1 家族）| ? | **❓ 未定** |

⇒ 可以说：**"所有继承型加载器都受影响"是有依据的**（Fabric 实测 + Forge artifact 级 + Quilt 同构），
但 NeoForge 我**没有**核对，所以发布说明若要写"全部加载器"，NeoForge 那一格应标"未核"。

## 3. 新观察（**未解释**）：Forge 安装阶段的 HTTP 404

本轮真跑了一次 Forge 包导入（Modrinth `optimized-fps` 1.20.1，依赖 `forge=47.4.3`，22 个文件），
**导入在 330 秒后失败**：

```
{"error":"Forge installer 下载失败：下载失败 HTTP 404：forge-1.20.1-47.4.3-installer.jar。请检查网络后重试。"}
```

**已排除的可能**（都实测过）：

| 假设 | 实测 | 结论 |
| --- | --- | --- |
| URL 拼错 | 错误里的文件名形态 = `{artifact}-{mavenVersion}-installer.jar`，mavenVersion=`1.20.1-47.4.3`，与 `GetInstallerUrl` 一致 | 排除 |
| 官方 URL 不存在 | `https://maven.minecraftforge.net/net/minecraftforge/forge/1.20.1-47.4.3/forge-1.20.1-47.4.3-installer.jar` → **200 / 6,068,826 B**（`.sha1` 也 200）| 排除 |
| 网络不可达 | 同上；且 .NET 10 **直连**（`UseProxy=false`）与**走系统代理**都 200 | 排除 |
| User-Agent 被拦 | 空 / `Pineglow-Launcher/1.0` / `Mozilla/5.0` / `curl/8.4.0` / `python-requests` / `Java/17` **全部 200** | 排除 |
| 被镜像改写 | `config.DownloadSource` 默认 `official`，`MirrorHelper.Enabled` 为假 ⇒ `Rewrite` 应为恒等；且 `DownloadSource` 只在显式 `/api/sources/select` 时才写 | 基本排除 |

⇒ **根因未定**。下一步诊断（已想好、未做）：利用产品**自带的代理设置**（`config.ProxyEnabled/ProxyHost/ProxyPort`）
把请求导向一个**会记录目标 URL 的本地代理**，即可看到启动器真正请求的地址；或临时加一行日志再跑。
**本轮不下任何结论**——它可能是环境（本机 fake-IP/透明代理对某些请求变体的应答），也可能是产品缺陷。

## 4. 顺带发现的一个设计缺口（低严重度，与上面 404 无关）

`LoaderInstallHelper.DownloadFileAsync` 用的是 `MirrorHelper.Rewrite(url)`——**单地址、无回退**；
而同一个类里已经有 `MirrorHelper.GetCandidates(url)`（返回 `[镜像, 原地址]` 两候选，**带回退**），
其它地方（版本清单）用的就是后者。⇒ 用户一旦选了镜像源，而该镜像没有某个产物，
**会直接失败而不会回退到官方地址**。建议：`DownloadFileAsync` 改用 `GetCandidates` 逐个尝试。

## 5. 复现

```powershell
# Forge 是否同病（看安装器内嵌 profile）
Invoke-WebRequest https://maven.minecraftforge.net/net/minecraftforge/forge/1.20.1-47.4.3/forge-1.20.1-47.4.3-installer.jar -OutFile f.jar
# 解压后看 version.json 的 inheritsFrom 与 arguments.jvm 里有没有 java.library.path
# Forge 导入实测（会失败，见 §3）
powershell -File ...\run-20261006-1400-forge-probe\probe-forge-instance.ps1 -ExePath <EXE> -MrpackPath <optimized-fps.mrpack> -WorkRoot %TEMP%\pg-forge -JsonPath <证据>\forge-probe.json
```

产物：`forge-probe.json` / `forge-probe.log` / `step.log`（导入 330 秒后 404 失败的原始终端输出）。
（安装器 jar 与临时目录已清理，避免占用空间。）
