# 🔴 加载器实例启动崩溃：缺 `-Djava.library.path`（第 47 轮，真实复现 + 定位到代码）

日期：2026-10-06（Asia/Hong_Kong）。本轮**未改动任何 tracked 文件**。
结论：**这是本次评估里第一个"承诺功能在真机上直接崩"的缺陷 —— 严重度 blocker**。
Vanilla 能启动（第 45 轮），**Fabric（加载器）实例必崩**：`UnsatisfiedLinkError: Failed to locate library: lwjgl.dll`。

## 1. 复现步骤（真实包 + 真实启动）

1. 用 Modrinth 官方 **Fabulously Optimized v3.15.0**（`minecraft=1.18.2` + `fabric-loader=0.19.3`，43 个 mod）
   走产品自己的 `POST /api/instances/import` 导入 → 成功，实例 `loaderType=Fabric`、
   `version=fabric-loader-0.19.3-1.18.2`，`versions/fabric-loader-0.19.3-1.18.2/` 与
   `…/natives/`（**38 个文件，含 `lwjgl.dll` 449,536 B，SHA1 与 sidecar 一致、PE 头 `MZ` 正常**）都在。
2. `prelaunch-check` → **ok=True**。
3. `POST /api/instances/{id}/launch` → **200 `{"ok":true,"pid":13308}`**。
4. **约 10 秒后游戏崩溃**，`logs/latest.log`：
   ```
   [Render thread/INFO]: [STDERR]: [LWJGL] Failed to load a library. Possible solutions: ...
   [Render thread/ERROR]: Minecraft has crashed!
   net.fabricmc.loader.impl.FormattedException: java.lang.NoClassDefFoundError:
        Could not initialize class com.mojang.blaze3d.systems.RenderSystem
   Caused by: java.lang.ExceptionInInitializerError:
        Exception java.lang.UnsatisfiedLinkError: Failed to locate library: lwjgl.dll [in thread "Render thread"]
   ```

## 2. 关键对照：加载器实例的命令行**没有** `-Djava.library.path`

| | Vanilla 1.18.2（第 45 轮，**成功**）| Fabric 加载器（本轮，**崩溃**）|
| --- | --- | --- |
| 启动器自己的 JVM 主类 | `net.minecraft.client.main.Main` | `net.fabricmc.loader.impl.launch.knot.KnotClient`（正确）|
| `-Xmx` / `-Dfile.encoding` | ✅ | ✅ |
| **`-Djava.library.path`** | ✅ `…\versions\1.18.2\natives` | ❌ **完全没有** |
| `-Dminecraft.launcher.brand/version` | ✅ | ❌ 没有 |
| `-cp` | ✅ | ✅ |
| 结果 | 进主菜单、10 个图集就绪 | **崩溃**（找不到 `lwjgl.dll`）|

进程序列（每 200ms 采样）显示：**启动器拉起的第一个 JVM（pid=13308，parent=启动器）就没有该标志**；
其后的 12172/6600/21896 是整合包自带 **CrashAssistant** 模组用 `Boot --args-file …` 重新拉起的，同样没有。
⇒ 不是模组的问题，**第一个 JVM 就已经错了**。

## 3. 定位到代码：`LauncherService.ResolveJvmArguments`

```csharp
JsonElement? arguments = version.Arguments;
if (arguments.HasValue)
{
    … if (valueOrDefault.ValueKind == Object && valueOrDefault.TryGetProperty("jvm", out var value)
          && value.ValueKind == JsonValueKind.Array)
    {
        list.AddRange(FlattenArguments(value, replacements, BuildLaunchFeatures(profile)));
        goto IL_0091;                                   // ← 直接返回，不再补下面的东西
    }
}
list.Add("-Djava.library.path=" + nativesDir);          // ← 只有"没有 arguments.jvm"时才加
list.Add("-Dminecraft.launcher.brand=PineglowLauncher");
list.Add("-Dminecraft.launcher.version=" + LauncherVersion);
list.Add("-cp"); list.Add(classpath);
```

- **加载器（继承型）版本 JSON**：`versions/fabric-loader-0.19.3-1.18.2/….json` 里
  `inheritsFrom=1.18.2`，而它自己的 `arguments.jvm` **只有一条**：`"-DFabricMcEmu= net.minecraft.client.main.Main "`
  ⇒ 命中上面的分支 ⇒ **父版本（1.18.2）那套 jvm 参数（含 `-Djava.library.path=${natives_directory}`）没有被带进来**
  ⇒ 命令行缺 `-Djava.library.path`。
- **Vanilla 版本 JSON**（`versions/1.18.2/1.18.2.json`）：命令行里出现的正是兜底分支那三行
  （`-Djava.library.path` + `brand` + `version`，顺序一致）⇒ 说明它**没有**走 `arguments.jvm` 分支，于是拿到了库路径并成功启动。

**修法方向**（供决定，本轮未改）：二者选一即可——
1. 修**版本合并**：加载器 profile 与父版本合并时，把父版本的 `arguments.jvm`/`arguments.game`、`mainClass`、`libraries` 正确并入
   （本缺陷的根在最上游）；
2. 或修**兜底逻辑**：走 `arguments.jvm` 分支时，若其中**不含** `java.library.path` / `classpath` 等关键项，则补上
   `-Djava.library.path=nativesDir`（以及 `-cp`）。
   两者都要**回归测试**：Vanilla 1.18.2、Fabric 1.18.2、以及"加载器实例与 vanilla 实例各启动一次"。

## 4. 影响面（为什么这是 blocker，而不是"某个包的问题"）

- 整合包与加载器实例是**承诺功能**：`InstancePackService` 安装 Fabric/Quilt/Forge/NeoForge，
  而**所有加载器实例**都会走这条继承型 JSON 路径 ⇒ 只要用户的实例带加载器，**启动必崩**。
- 这也解释了为什么"modpack 导入 PASS（第 43 轮）"并不能推出"modpack 能玩"——**装得好 ≠ 起得来**。
- 第 45 轮的 Vanilla 启动 PASS 因此**不能**外推到加载器；R06 的"承诺的加载器样例"现在是 **RED**（不是 NOT_RUN）。

## 5. 诚实边界

- 本轮只验了 **Fabric 1.18.2**；Forge/NeoForge/Quilt 未实测（同样的继承型路径，推测同样受影响，但**未证**）。
- 崩溃点是 LWJGL 初始化；**未**验证"补上 `-Djava.library.path` 后一定能进主菜单"（那需要改代码 ⇒ 属新版本）。
- 我**没有**修改任何源码；本报告只做复现与定位。

## 6. 复现命令与产物

```powershell
# 导入真实 Fabric 包并启动（约 6 分钟）
powershell -NoProfile -ExecutionPolicy Bypass -File `
  docs\release-remediation-evidence-2026-10-05\run-20261006-1230-fabric-launch\verify-fabric-launch.ps1 `
  -ExePath %TEMP%\pineglow-keep\candidate-5009cf73d0801e83.exe -MrpackPath <Fabulously.Optimized-v3.15.0.mrpack> `
  -WorkRoot %TEMP%\pg-loader -JsonPath <证据>\fabric-launch.json
# 抓进程序列（看第一个 JVM 有没有 -Djava.library.path）
powershell -NoProfile -ExecutionPolicy Bypass -File `
  ...\probe-fabric-sequence.ps1 -ExePath <EXE> -ColdDir <上面的冷目录> -JsonPath <证据>\fabric-sequence.json -Seconds 25
```

产物：`fabric-launch.json` / `game-latest.log`（崩溃原始日志）/ `step.log`、
`fabric-sequence.json` + `sequence.log`（4 个 java 进程的时序）、
`cmdline-13308.txt`（**启动器自己那个 JVM 的完整 9801 字符命令行**，可逐字核对缺了哪个标志）。
