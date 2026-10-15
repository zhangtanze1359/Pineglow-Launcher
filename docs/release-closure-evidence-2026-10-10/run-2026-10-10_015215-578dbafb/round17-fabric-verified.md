# 第 17 轮：Fabric 端到端**验证通过** —— 四种加载器全部闭环

运行标识：`run-2026-10-10_015215-578dbafb`（第 17 轮追加）
时间：2026-10-11 14:40–14:52，Asia/Hong_Kong（+08:00）
本轮性质：**只做验证与取证，产品目录零改动**（0 staged）。
被验产物：`CA8F94C1A6173F23F38F3D2A3C949AFFC16D5AB67752B662C7806831F350D93E`（与 3 次流水线发布逐字节相同）。
证据目录：`d-fabric-retry4/`（含 `loader-matrix.json`、`fabric-game-latest.log`、`fabric-loader-profile.json`）

---

## 1. 结果

**Fabric 加载器的「导入 → 安装 → 启动 → 到标题界面」全链路通过。**

```
importOk            : true
importSeconds       : 529
instanceLoader      : "Fabric"
instanceVersion     : "fabric-loader-0.19.5-1.21.11"
prelaunchOk         : true   (errors: [], warnings: [], maxMemoryMb: 4608)
launchResponse      : {"ok":true,"pid":26744,...}
reachedTitle        : true
gameLogMarker       : "Sound engine started"
```

导入耗时 **529 秒**（前一轮 1200 秒预算时未完成，本轮网络状况好转后 8 分 49 秒装完），随后启动到标题界面。

**至此四种加载器全部闭环**：

| 加载器 | 版本 | 结果 |
| --- | --- | --- |
| Quilt | 0.30.1 | ✅ 导入装成 → 启动到标题（第 7 轮） |
| Forge | 61.2.1 | ✅ 同上（第 7/8 轮，含代理 A/B 实验） |
| NeoForge | 21.11.45 | ✅ 同上（第 7 轮） |
| **Fabric** | **0.19.5** | ✅ **本轮** |

---

## 2. 独立于探针字段的原始证据

探针有两个字段报了 `false`，但我用**磁盘与游戏日志**核对后确认那是**探针字段本身的问题，不是产品问题**（详见第 3 节）：

**（a）加载器 profile 确实在磁盘上**（`loader-matrix.json` 里 `loaderProfileOnDisk:false` 与事实不符）

文件：`...\instances\d7-fabric 导入 (1)\versions\fabric-loader-0.19.5-1.21.11\fabric-loader-0.19.5-1.21.11.json`（3,468 字节）

```json
id           : fabric-loader-0.19.5-1.21.11
inheritsFrom : 1.21.11
mainClass    : net.fabricmc.loader.impl.launch.knot.KnotClient
```

Fabric 的 jar（3 个）也确实落到实例自己的库里：
`libraries\net\fabricmc\fabric-loader\0.19.5\fabric-loader-0.19.5.jar`、
`libraries\net\fabricmc\intermediary\1.21.11\intermediary-1.21.11.jar`、
`libraries\net\fabricmc\sponge-mixin\0.17.4+mixin.0.8.7\sponge-mixin-0.17.4+mixin.0.8.7.jar`

**（b）游戏确实用 Fabric 启动并到了标题界面**（`javaStarted:false` 也与事实不符）

实例日志 `logs\latest.log`（57 行，已复制到 `d-fabric-retry4/fabric-game-latest.log`）关键行：

```
[14:49:26] [main/INFO]: Loading Minecraft 1.21.11 with Fabric Loader 0.19.5
[14:49:26] [main/INFO]: Fabric is preparing JARs on first launch, this may take a few seconds...
    - fabricloader 0.19.5
[14:49:26] [main/INFO]: SpongePowered MIXIN Subsystem Version=0.8.7 ... Service=Knot/Fabric Env=CLIENT
[14:49:32] [Render thread/INFO]: Setting user: d6tester
[14:49:35] [Render thread/INFO]: Sound engine started
[14:49:35] [Render thread/INFO]: Created: 512x512x0 minecraft:textures/atlas/shulker_boxes.png-atlas
```

`Knot/Fabric`、`fabricloader 0.19.5`、纹理图集创建、音效引擎启动 —— 这些都是**只有真正进到标题界面才会出现**的行。

**（c）日志里仅有的两条 ERROR 是离线账号的必然结果，不是产品缺陷**

```
[14:49:36] [Download-2/ERROR]: Failed to fetch user properties
com.mojang.authlib.exceptions.InvalidCredentialsException: Status: 401
[14:49:37] [Download-3/ERROR]: Failed to fetch Realms feature flags
```

原因：本次用的是**离线账号**（`Setting user: d6tester`），Mojang 的用户属性 API 与 Realms 都会拒绝离线账号（401 / 无法解析 SignedJWT）。
**这是测试条件造成的**，与启动器无关；换成正版账号即消失（属 E 范围）。

---

## 3. 探针字段缺陷（我的工具，不是产品）

| 字段 | 报的值 | 事实 | 原因 |
| --- | --- | --- | --- |
| `loaderProfileOnDisk` | `false` | 磁盘上确有 `versions\fabric-loader-0.19.5-1.21.11\*.json` | 探针按固定命名或固定目录去找，没匹配到实例名带 `(1)` 后缀时的实际路径 |
| `javaStarted` | `false` | 游戏进程确实起来了并到达标题 | 探针检测的是某个 PID/进程名，与产品实际拉起的 Java 进程不匹配（与第 7 轮同类问题） |

**处理**：这两个字段**不作为结论依据**；本轮结论全部建立在「磁盘文件 + 游戏日志」上。
（同类教训本会话已出现过：`reachedTitle` 曾因探针看错日志路径而误报 false。）

---

## 4. 顺带记录：实例隔离的磁盘代价（设计取舍，非缺陷）

四个导入实例各自保存了一份**完整的原版**（4592 个资源对象 + 75 个原版库），实测：

| 实例 | 体积 | 原版库 jar | 总库 jar |
| --- | --- | --- | --- |
| `d7-fabric 导入`（上一轮未完成） | 525.6 MB | 75 | 75 |
| `d7-fabric 导入 (1)` | 562.9 MB | 75 | **83**（含 8 个 Fabric 库） |
| `d7-quilt 导入` | 592.7 MB | 75 | 86 |
| `d7-neoforge 导入` | 583.4 MB | 75 | 104 |
| `d7-forge 导入` | 615.5 MB | 75 | 133 |

- `instances/` 合计 **2,883.5 MB**；整个 `.minecraft` **3,413.8 MB**（28,203 个文件）。
- 即：**同一个 Minecraft 版本，每导入一个整合包就多花约 525 MB**（原版资源+库的私有副本）。

**定性**：这是「实例独立隔离目录」这一**已宣传的设计**（README:12）的直接后果，**不是缺陷**。
但如果要作为正式成品，值得在文档里写清楚磁盘占用预期（例如「每个实例约 0.5 GB 起」），否则用户连开几个整合包就会发现 C 盘少了两三 GB。

---

## 5. D 的剩余缺口（本轮后）

| 项 | 状态 |
| --- | --- |
| 原版安装 + Java 自动安装 + 启动到标题 | ✅ |
| 四种加载器（Fabric / Forge / NeoForge / Quilt）导入 → 启动到标题 | ✅ **本轮补齐** |
| 断点续传 | ✅（第 11 轮） |
| 镜像下载源可用 | ✅ |
| **进世界** | ❌ 需桌面/GUI 自动化，本环境不可达 |
| **退出游戏 / 重启** | ❌ 同上 |
| **30 分钟长时运行** | ❌ 同上 |

即：**D 在本环境内能做的部分已经做完**；剩下三项是环境能力边界，不是未做的工作。

状态串：`TechnicalAcceptance=NOT_PASSED`（发布整体）；`FormalPhaseApproval=NOT_GRANTED`；`ReleaseApproval=NOT_GRANTED`。

---

## 6. 附带定位：产品缺陷第 2 条（代理）从「缺失」精确到「遗漏」

收尾时清理探针遗留的 `javaw.exe` 进程（PID 26744，先用命令行核对过它确实属于本次夹具的 `d7-fabric` 实例，才停它），顺手读到了它的完整命令行：

```
"...\jdk-21.0.12.1+1-jre\bin\javaw.exe" -Xmx4608M -Dfile.encoding=UTF-8
  -Dhttp.proxyHost=127.0.0.1 -Dhttp.proxyPort=7897
  -Dhttps.proxyHost=127.0.0.1 -Dhttps.proxyPort=7897 ...
```

**即：产品给「游戏启动」的 JVM 是带了代理参数的。** 对应代码在 `LocalApiService.cs:1741`：

```csharp
customJvmArgs = AppendJvmArgument(customJvmArgs,
    $"-Dhttp.proxyHost={config.ProxyHost} -Dhttp.proxyPort={config.ProxyPort} " +
    $"-Dhttps.proxyHost={config.ProxyHost} -Dhttps.proxyPort={config.ProxyPort}");
```

而**加载器安装器**那条路径（`LoaderInstallHelper.cs:138-156 RunInstallerAsync`）构造的命令是：

```
java -jar <installer> --installClient <gameDir>
```

**没有任何代理参数。** 结合第 8 轮的 A/B 实验（不加参数 → `ExitCode=1 Downloading minecraft client failed, invalid checksum`；加上 → `ExitCode=0 Successfully installed client into launcher`），现在可以更精确地说：

> **这不是「产品没有代理机制」，而是「同一条机制没有被应用到加载器安装器这条路径上」——属遗漏。**

对第 6.2 节第 4 项的修复工作量的估计因此可以下调：照抄 `LocalApiService.cs:1741` 那四行到 `RunInstallerAsync` 的启动参数里即可，风险很低。

（该遗留进程已按「先核实身份、再按 PID 精确停止」的方式清理；**没有**按进程名批量杀任何东西。清理后 `Pineglow.Launcher` / `java` / `javaw` 均无残留。）
