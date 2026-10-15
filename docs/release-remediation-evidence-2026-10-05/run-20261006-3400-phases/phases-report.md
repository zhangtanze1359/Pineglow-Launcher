# 当前源码分阶段真实验收：Quilt PASS / Vanilla PASS / **Forge 装得上但启动必失败（新发现两个缺陷）**（第 84 轮）

日期：2026-10-07（Asia/Hong_Kong）。被测：当前源码（已应用 4 处修复）的**单文件 EXE** `sha256=6ed0c8db…b926f0`，全新便携冷目录。
纪律：未提交/未推送/未打 tag/未发布；未 reset/stash/批量暂存；未改历史证据；**未调整任何系统 ACL**。

## 1. 三个阶段的结果

| 阶段 | 结果 |
| --- | --- |
| **Quilt**（真实包 `ukupvp`，1.20.1）| **PASS**：导入 **ok，201 秒** → 实例 `loaderType=Quilt` → `prelaunch ok=True` → 启动：命令行 **15,419 字符含 `-Djava.library.path`**、日志 `loader=3 lwjgl=1 sound=1 **atlas=12** 崩溃=0 行数=708` |
| **Vanilla 1.18.2** | **PASS**：装版本 169 秒 → 建实例 → 启动：命令行 **5,849 字符含库路径**、`lwjgl=1 sound=1 **atlas=10** 崩溃=0` |
| **Forge**（真实包 `optimized-fps`，1.20.1 + forge 47.4.3）| **导入 PASS（269 秒）** —— 实例 `loaderType=Forge`、`versions/1.20.1-forge-47.4.3` 落盘、`installer.log` 34,095 行结尾是 **`Injecting profile` → `Successfully installed client into launcher.`** ⇒ **B2（URL）+ B2b（profile 桩）在产品级端到端成立** ✅<br>**启动 FAIL**：`launch` 返回 `{"ok":true,"pid":…}`，但 JVM **立即退出（exit 1）**，游戏日志根本没生成 |

> 说明：上一轮"Quilt 导入响应挂起 >13 分钟"**未复现**——本轮纯净复跑 201 秒正常返回。上一轮的挂起很可能是**我的客户端超时/并发占用带宽**所致；已按"不掩盖失败"原则同时保留两次记录。

## 2. **新发现：Forge/NeoForge 实例"装得上但启动必失败"——两个独立的装配缺陷**

### 缺陷 ①：JVM 参数被**去重**，重复的选项 token 被吃掉

- **代码位置**：`LauncherService.cs:197` → `return list.DistinctPreserveOrder().ToList();`（`ResolveJvmArguments` 的收尾）。
- **Forge 版本 JSON 的原始形态**（`versions/1.20.1-forge-47.4.3/1.20.1-forge-47.4.3.json`，`arguments.jvm` 共 16 条）：
  `[9] "--add-opens" [10] "java.base/java.util.jar=cpw.mods.securejarhandler" [11] "--add-opens" [12] "java.base/java.lang.invoke=cpw.mods.securejarhandler" [13] "--add-exports" [14] … [15] "--add-exports" [16] …`
  —— **选项 token 是重复出现的**（这是 `--add-opens/--add-exports` 的正确形态）。
- **产品实际生成的命令行**（19,503 字符，已存档 `forge-launch-cmdline.txt`）：`--add-opens` 只出现 **1 次**、`--add-exports` 只出现 **1 次**，
  而两个 spec 都在 ⇒ JVM 把第一个 spec 当作选项值，**把第二个 spec 当成主类**：
  ```
  WARNING: Unknown module: cpw.mods.securejarhandler specified to --add-opens
  错误: 找不到或无法加载主类 java.base.java.lang.invoke=cpw.mods.securejarhandler
  原因: java.lang.ClassNotFoundException: java.base.java.lang.invoke=cpw.mods.securejarhandler
  exitCode=1（0 秒）
  ```
  （用捕获到的命令行**手工复跑**得到的原始输出，见 `forge-manual-run.json`。）

### 缺陷 ②：Forge 专有占位符**没有被替换**

同一条命令行里：**`${library_directory}` 出现 9 次、`${classpath_separator}` 出现 7 次**（都是字面量，未替换）；
而 vanilla 的占位符（`${natives_directory}`/`${classpath}`/`${launcher_name}`）都已替换。
⇒ `-p ${library_directory}/cpw/mods/securejarhandler/…` 指向不存在的模块路径 ⇒ `cpw.mods.securejarhandler` 不是模块 ⇒
把 ① 手工补回后，失败变成：
```
java.lang.reflect.InaccessibleObjectException: Unable to make field static final java.lang.invoke.MethodHandles$Lookup … accessible:
module java.base does not "opens java.lang.invoke" to unnamed module
  at cpw.mods.jarhandling.SecureJar.from(SecureJar.java:58)
  at cpw.mods.bootstraplauncher.BootstrapLauncher.main(BootstrapLauncher.java:84)
```

### 我把两个都手工修正后的结果（**部分推进，但我自己的替换不可靠，不据此宣称修好**）

- 手工补回重复 token + 替换 `${library_directory}`（用 **8.3 短路径**避免空格切碎）+ `${classpath_separator}`=`;` + `${version_name}`：
  进程**活到 10 秒**、游戏日志**首次生成（12 行）**、并出现 **`GL info: NVIDIA GeForce RTX 4060 Laptop GPU … GL version 4.6.0`**（已创建 GL 上下文），
  但随后在 `ModuleLayerHandler.buildLayer` / `Configuration.resolveAndBind` 失败（exit 1）。
- ⚠️ **诚实边界**：我用的 8.3 短路径里含中文段（`OPTIMI~1.1导`），在控制台编码下出现乱码（日志里显示为 `OPTIMI~1.1瀵糪`），
  **这个替换本身不可靠** ⇒ **不能据此说"两处都修好就能启动"**。能确定的只有：① 与 ② 都是真实存在的装配缺陷（有代码行 + 原始命令行 + 原始报错）。

## 3. 判定（诚实口径）

| 项 | 判定 |
| --- | --- |
| Quilt 导入 + 启动 | **PASS** |
| Vanilla 导入 + 启动 | **PASS** |
| **Forge 导入（安装器全流程）** | **PASS**（B2 + B2b 产品级成立）|
| **Forge 启动** | **FAIL（新缺陷 ①+②，代码位置已定位）** |
| 上一轮"Quilt 挂起" | **未复现**（本轮 201 秒正常）|

## 4. 产物

`phase-quilt-step.log` / `quilt.json` / `quilt-cmdline.txt` / `quilt-game.log`；
`phase-vanilla-step.log` / `vanilla.json` / `vanilla-cmdline.txt` / `vanilla-game.log`；
`phase-forge-step.log` / `forge.json` / `forge-launch-cmdline.txt`（19,503 字符）/ `forge-manual-run.json`（缺陷①原始报错）/
`forge-fixed-cmdline-run.json`（补 token 后：InaccessibleObjectException）/ `forge-fixed3-cmdline-run.json`（再替换占位符后：GL info 出现但模块层失败）；
`verify-phase.ps1`（可重跑的分阶段脚本）。
