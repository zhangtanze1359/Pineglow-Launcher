# B1 的**端到端**证据：打过补丁的构建下 Fabric 实例**真的跑起来了**（第 75 轮）

日期：2026-10-06（Asia/Hong_Kong）。本轮**未改动工作区一个字节**。
这是三个 blocker 里最后一个缺"真机端到端"的：第 74 轮已在**打过补丁的构建**上验了代码级复现（B0/B1/B2 + 904 测试），
本轮用**同一个补丁构建**跑真实 Fabric 包，与第 47 轮候选"必崩"形成对照。

## 1. 对照（同一个真实包：Fabulously Optimized 1.18.2 + fabric-loader 0.19.3）

| | **候选（第 47 轮）** | **打过补丁的构建（本轮）** |
| --- | --- | --- |
| 导入 | ok，267 秒 | ok，**159 秒** |
| 启动器命令行 | 9,801 字符，**没有** `-Djava.library.path` | 10,561 字符，**有** `-Djava.library.path`（指向实例的 `versions\fabric-loader-0.19.3-1.18.2\natives`）|
| 主类 | `…knot.KnotClient` | `…knot.KnotClient` |
| Fabric Loader | 加载了 | **`Loading Minecraft 1.18.2 with Fabric Loader 0.19.3`** ✅ |
| 结果 | **约 10 秒崩溃**：`UnsatisfiedLinkError: Failed to locate library: lwjgl.dll`；日志 208 行 | **`Backend library: LWJGL version 3.2.2 build 10`**、**`Sound engine started`**、**10 个 `Created: …-atlas`**、**0 崩溃**、日志 **1,335 行**、JVM 存活整个 150 秒观察期 ✅ |

**判定：`PASS：补丁构建下 Fabric 实例启动成功（命令行含库路径、Fabric Loader 加载、LWJGL/声音/图集就绪、0 崩溃）——与候选"必崩"形成对照`**（逐项 **7/7**）

| 检查 | 结果 |
| --- | --- |
| 命令行含 `-Djava.library.path` | ✅（`hasLibraryPath=True`）|
| 主类是 Fabric 的 `KnotClient` | ✅ |
| Fabric Loader 已加载 | ✅ |
| `Backend library: LWJGL` | ✅ |
| `Sound engine started` | ✅ |
| 图集 > 0 | ✅（**10 个**）|
| **0 崩溃** | ✅ |

## 2. 本轮在"9 步验证清单"里补掉了哪一格

| 清单项 | 状态 |
| --- | --- |
| 全量 903 测试（+新增回归测试）| ✅ 第 74 轮：打过补丁副本 **904/904** |
| **Fabric 启动（命令行须含 `-Djava.library.path`）** | ✅ **本轮端到端 PASS** |
| B0/B1/B2 代码级复现 | ✅ 第 74 轮 4/4 |
| 仍待做（**批准后**在工作区正式执行）| Node 304（打过补丁）、**Vanilla / Quilt 启动回归**、Forge 包不再 404、全量验证链 + 新 sha256 |

## 3. 复现

```powershell
# 前提：%TEMP%\pg-patched 是第 74 轮那个"打过补丁并构建成功"的副本
powershell -File ...\run-20261006-2600-patched-fabric-launch\verify-patched-fabric-launch.ps1 `
  -PatchedColdDir %TEMP%\pg-patched\dev\PineglowLauncher\bin\Release\net10.0 `
  -MrpackPath <Fabulously.Optimized-v3.15.0.mrpack> -JsonPath <证据>\patched-fabric.json
# 期望：PASS（7/7）；日志里出现 LWJGL/声音/10 图集、0 崩溃
```

产物：`patched-fabric.json`（判定 + 逐项 + 命令行长度/库路径值 + 标记）、`patched-fabric-step.log`（逐步时间线）、
`patched-fabric-cmdline.txt`（10,561 字符完整命令行）、`patched-fabric-game.log`（1,335 行游戏日志）。
