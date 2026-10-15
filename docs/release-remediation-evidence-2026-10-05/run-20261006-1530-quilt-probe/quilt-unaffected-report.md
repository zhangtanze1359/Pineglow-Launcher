# Quilt 实测：**不受该缺陷影响**（命令行含库路径、进渲染管线、0 崩溃）（第 54 轮）

日期：2026-10-06（Asia/Hong_Kong）。本轮**未改动任何 tracked 文件**。
目的：把第 53 轮"Quilt 不受影响"的**逻辑推演**升级为**真机实测**（用真实 Quilt 包导入 + 启动）。

## 1. 用什么包

Modrinth 上的真实 Quilt 包 **`ukupvp`**（`quilt-loader=0.21.0` + `minecraft=1.20.1`，`.mrpack` 233,881 B，
清单 **49 个文件 / 43.2 MB**）。导入用时 **331 秒**，产物实例：`loaderType=Quilt`、`version=quilt-loader-0.21.0-1.20.1`。

## 2. 实测结果（关键三项）

| # | 观察 | 实测 |
| --- | --- | --- |
| 1 | **产品写出的 Quilt 版本 JSON 里没有 `jvm`** | `inheritsFrom='1.20.1'`、`mainClass=org.quiltmc.loader.impl.launch.knot.KnotClient`、`arguments` 键=**`[game]`**、`有 jvm=False`、`自带库路径=False` |
| 2 | **启动器命令行含库路径（走兜底分支）** | 命令行 17,601 字符，**`含库路径=True`**，值=`…\instances\ukus-pvp-modpack 导入\versions\quilt-loader-0.21.0-1.20.1\natives` |
| 3 | **游戏正常起来（不是"起来了就崩"）** | `launch → {"ok":true,"pid":35640}`；观测 122 秒：**Quilt 相关行 121**、`Backend library: LWJGL` 1、`Sound engine started` 1、**纹理图集 12 个**、**崩溃行 0**、日志 **2,193 行**、JVM 全程存活 |

**判定：`Quilt 正常进入渲染管线且命令行含库路径（推演成立：不受该缺陷影响）`**

⇒ 第 53 轮的更正**被实测确认**：**Quilt 不受"缺 `-Djava.library.path`"影响**——因为它的 profile
`arguments` 里**没有 `jvm` 数组**，代码因此走**兜底分支**，反而把库路径补上了。

## 3. 加载器影响面（最终，含证据类型）

| 加载器 | 证据 | 结论 |
| --- | --- | --- |
| **Fabric** | **实测**（导入 + 启动，第 47 轮）| 🔴 **崩**（命令行缺库路径）|
| **Forge** | artifact 级（安装器内 `version.json` 16 条 jvm 无库路径，第 51 轮）| 🔴 **同病** |
| **NeoForge** | artifact 级（同法，15 条 jvm 无库路径，第 53 轮）| 🔴 **同病** |
| **Quilt** | **实测**（本轮：命令行含库路径、12 图集、0 崩溃）| ✅ **不受影响** |
| **Vanilla** | 实测（第 45 轮启动成功）+ profile 自带库路径 | ✅ **不受影响** |

**可写进发布说明的表述**（现在每一格都有依据）：
> 含 **Fabric / Forge / NeoForge** 加载器的实例在当前版本**无法启动**（命令行缺 `-Djava.library.path`，LWJGL 初始化即崩）；
> **Quilt 与 Vanilla 不受影响**（Quilt 已实测：49 mod 的 Quilt 包正常进入渲染管线）。

## 4. 本轮我自己的一个脚本坑（同类第三次）

上一版脚本在 `$vd.arguments.jvm` 处被 `Set-StrictMode` **打断**（Quilt 的 `arguments` **没有 `jvm` 属性**）——
**这个"读不到属性"本身就是本轮要证明的关键事实**，但脚本把它当成了致命错误。已改为统一用
`PSObject.Properties` 判定后再取；本版脚本直接**复用已导入的实例**（不重新导入），3 分钟跑完。
（同类问题在第 43/44/45 轮各出现过一次，教训一致：**StrictMode 下任何"想当然存在的属性"都会中断脚本**。）

## 5. 复现

```powershell
# ① 导入真实 Quilt 包（约 5.5 分钟）
#    用 run-20261006-1530-quilt-probe\probe-quilt-instance.ps1（注意：该版在 StrictMode 下会因无 jvm 属性中断，
#    保留它正是为了记录这个坑；正式复跑用下面这个只做启动的脚本）
# ② 复用已导入实例，只做启动 + 抓命令行 + 观测
powershell -File ...\run-20261006-1530-quilt-probe\verify-quilt-launch.ps1 `
  -ColdDir <冷目录> -ApiPort <端口> -JsonPath <证据>\quilt-launch.json -ObserveSeconds 120
```

产物：`quilt-launch.json`（版本 JSON / 命令行 / 标记 / 判定）、`quilt-cmdline.txt`（17,601 字符完整命令行）、
`quilt-game.log`（2,193 行游戏日志）、`step2.log`（逐步时间线）；`probe-quilt-instance.ps1` + `quilt-probe.log` 保留作"坑"的记录。
