# 受控实验：**只补一个参数，Fabric 实例就起来了** —— blocker 根因确认 + 修法验证（第 48 轮）

日期：2026-10-06（Asia/Hong_Kong）。本轮**未改动任何 tracked 文件、未改任何源码**。
做法：拿**产品自己生成的那条命令行**，在 `-Dfile.encoding=UTF-8` 之后**只插入**
`-Djava.library.path=<natives>`，其余一字不改，原样跑一次；与"产品原样启动"做对照。

## 1. 结果（同一台机器、同一个实例、同一条命令行，只差一个参数）

| | A 基线（产品原样）| B 修复组（**只加 `-Djava.library.path`**）|
| --- | --- | --- |
| 命令行长度 | 9,801 | 10,015 |
| `-Djava.library.path` | ❌ 没有 | ✅ `…\versions\fabric-loader-0.19.3-1.18.2\natives` |
| `Loading Minecraft 1.18.2 with Fabric Loader 0.19.3` | ✅ | ✅ |
| `Loading 135 mods:` | ✅ | ✅ |
| `Backend library: LWJGL version 3.2.2` | ❌ | ✅ |
| `Sound engine started` | ❌ | ✅ |
| `Created: …-atlas` | ❌ | ✅ **10 个** |
| 崩溃标记（`Failed to locate library` / `has crashed`）| **2 行** | **0 行** |
| 日志行数 | 208（截断在崩溃）| **2,536** |
| JVM 状态 | 约 10 秒后退出 | **存活整个 150 秒观察期** |

**判定：`PASS：补上 -Djava.library.path 后进入渲染管线（不再崩溃）`**（`fix-sim-fixed3.json`）

⇒ 第 47 轮的定位**被证实**：加载器实例崩溃的**唯一**原因是那条命令行缺 `-Djava.library.path`；
补上后 Fabric + 135 个 mod 的实例能正常进入渲染管线。

## 2. 修法（供决定；本轮**未改**）

根因位置：`LauncherService.ResolveJvmArguments`
```csharp
if (arguments.HasValue && …TryGetProperty("jvm", out var value) && value.ValueKind == JsonValueKind.Array)
{
    list.AddRange(FlattenArguments(value, replacements, BuildLaunchFeatures(profile)));
    goto IL_0091;                       // ← 这里跳过了下面三行
}
list.Add("-Djava.library.path=" + nativesDir);
list.Add("-Dminecraft.launcher.brand=PineglowLauncher");
list.Add("-Dminecraft.launcher.version=" + LauncherVersion);
list.Add("-cp"); list.Add(classpath);
```
因为加载器（继承型）JSON 的 `arguments.jvm` 只有 Fabric 自己那一条（`-DFabricMcEmu= …`），
父版本（1.18.2）的 jvm 参数没有并进来 ⇒ 命令行缺库路径。

**两个可选修法**（都要回归 Vanilla + Fabric 两种启动）：

- **① 修版本合并（推荐，治根）**：加载器 profile 与 `inheritsFrom` 父版本合并时，
  把父版本的 `arguments.jvm` / `arguments.game` / `libraries` / `mainClass` 正确并入
  （`LoaderInstallHelper` / 版本读取处）。影响面最大，但也最正确。
- **② 最小兜底（改动最小）**：在 `arguments.jvm` 分支里，展开后检查是否含 `-Djava.library.path`，
  缺则补 `list.Add("-Djava.library.path=" + nativesDir)`（并可一并补 brand/version）。
  注意：本实验证明**只补库路径就够**；brand/version 只是元数据，可留待 ① 处理。

**验证方式（我已备好）**：`verify-fabric-launch.ps1`（导入真实包并启动）+ `probe-fabric-sequence.ps1`
（抓命令行，确认含 `-Djava.library.path`）+ 第 45 轮的 Vanilla 启动脚本；三者都要 PASS。

## 3. 本轮我自己的一个操作坑（已修）

第一次跑"修复组"时用了 `Start-Process -ArgumentList <单个字符串>`，**PowerShell 5.1 把整串当成一个参数**，
java 报 `ClassNotFoundException: -Xmx5632M -Dfile.encoding=UTF-8 …`（**不是**游戏问题，是我调用方式错）。
第二次改成写 `.cmd` 文件，又撞上"`.cmd` 按 ANSI 读、而实例路径含中文（`… 导入`）"的风险；
最终改用 **`System.Diagnostics.ProcessStartInfo.Arguments` 原样传递**（不重新加引号）才正确。
⇒ 教训：**要把一条完整命令行原样执行，别经过 PowerShell 的参数再拼接**。

## 4. 诚实边界

- 本实验是**手工复刻**产品那条命令行再补一个参数，**不是**"改了源码后的产品行为"；
  真要发布必须真的改代码并重跑全量验证链（产物字节会变）。
- 只验了 **Fabric 1.18.2**；Forge/NeoForge/Quilt 未验（同为继承型路径，推测同病，**未证**）。
- 修复组跑起来后我**主动终止**（未进世界、未玩）；它证明的是"不再崩、进入渲染管线"。

## 5. 复现命令与产物

```powershell
# A 基线 + 生成"只差一个参数"的修复组命令行
powershell -File ...\run-20261006-1300-fabric-fix-sim\verify-fabric-fix-simulation.ps1 `
  -ExePath <EXE> -MrpackPath <Fabulously.Optimized-v3.15.0.mrpack> -WorkRoot %TEMP%\pg-fixsim -JsonPath <证据>\fix-sim.json
# B 原样执行修复组命令行（用 ProcessStartInfo.Arguments，别用 Start-Process -ArgumentList）
powershell -File ...\run-fixed-verbatim.ps1 -RunDir <上面的 runDir> -ArgsFile <证据>\fixed-cmdline-args.txt -JsonPath <证据>\fix-sim-fixed3.json
```

产物：`baseline-cmdline.txt`（9801 字符，缺库路径）、`fixed-cmdline-args.txt`（10015 字符，含库路径）、
`baseline-game.log`（208 行，含崩溃）、`fixed3-game.log`（**2536 行，含 LWJGL/声音/10 图集**）、
`fix-sim.json` / `fix-sim-fixed3.json`、`step.log` / `step3.log`（逐步时间线）。
