# Forge/NeoForge **启动**：三个装配缺陷全部定位，且**组合修复后实测进渲染管线**（第 85 轮）

日期：2026-10-07（Asia/Hong_Kong）。**未改动产品源码**（修复是在**捕获到的同一条命令行**上手工应用后验证的）。
被测：当前源码单文件 EXE `sha256=6ed0c8db…b926f0`；真实包 `optimized-fps`（1.20.1 + forge 47.4.3，导入 **282 秒成功**）。

## 1. 三个缺陷（都有代码位置 + 原始证据）

| # | 缺陷 | 代码位置 | 证据 |
| --- | --- | --- | --- |
| **①** | **JVM 参数被去重**，重复的选项 token 被吃掉 | `LauncherService.cs:197` `list.DistinctPreserveOrder()`（`EnumerableExtensions.cs` 用 `HashSet` 实现）| Forge JSON 里 `--add-opens`×2、`--add-exports`×2；产品生成的命令行里各只剩 **1** 个 ⇒ JVM 把第二个 spec 当主类：`错误: 找不到或无法加载主类 java.base.java.lang.invoke=cpw.mods.securejarhandler`（0 秒 exit 1）|
| **②** | **Forge 专有占位符未替换** | `LauncherService.BuildReplacements`（333–359 行）只有 vanilla 占位符，**没有 `library_directory` / `classpath_separator`**（`version_name` 有 ✓）| 产品命令行里 `${library_directory}` 出现 **9 次**、`${classpath_separator}` **7 次**（字面量）⇒ 模块路径无效 ⇒ 手工补回 ① 后失败变为 `InaccessibleObjectException: … does not "opens java.lang.invoke" to unnamed module` |
| **③** | **类路径里放进了原版 client jar**（Forge 应使用安装器产出的 `forge-<ver>-client.jar`）| `LauncherService` 的版本 jar/类路径装配（`BuildClasspath`，第 48 行调用）| 产品的 `-cp`（94 条）**含 `versions\1.20.1\1.20.1.jar`**、**不含** `forge-1.20.1-47.4.3-client.jar`（该文件**确实存在**，4,840,443 B，由安装器产出）；于是 ModLauncher 看到自动模块 `_1._20._1` 与 `minecraft` 冲突：`java.lang.module.ResolutionException: Modules _1._20._1 and minecraft export package net.minecraft.util.profiling.jfr.event to module immediatelyfast` |

## 2. 组合修复后的**实测结果：Forge 跑起来了**

在同一台机器、同一个实例、同一条捕获命令行的基础上只做三件事：
① 补回被去重的 `--add-opens`/`--add-exports` token（各 1 → 2）；
② 替换 `${library_directory}` / `${classpath_separator}`（对含空格+中文的路径**加引号**）；
③ **从 `-cp` 里去掉原版 `1.20.1.jar`**（94 → 93 条）。

```
200 秒内仍存活 = True（整个观察期）
stderr 行数 = 0（无任何报错）
游戏日志: 行数=193  LWJGL=1  声音=1  图集=12  崩溃=0
日志尾部: [Forge Version Check/INFO] [forge] Found status: OUTDATED Current: 47.4.3 …
```

⇒ **三个缺陷是 Forge 启动失败的全部原因**（至少在本机、该包、该路径形态下），且修复方向被端到端验证：
命令行里**重复选项 token 必须保留**、**Forge 占位符必须替换并正确引用**、**原版 client jar 不能进类路径**。

## 3. 边界与诚实口径

- 本轮**没有改产品源码**：修复是手工作用于捕获的命令行；因此这是**"缺陷定位 + 修复配方验证"**，不是"产品已修好"。
- ③ 的产品级修法需要产品决策：Forge 实例的"游戏 jar"应当是安装器产出的 `forge-<ver>-client.jar`（还是干脆不把任何原版 jar 放进 `-cp`）；
  本轮验证只证明了**去掉原版 jar 后不再冲突**（未验证"换成 forge client jar"是否等价或更好）。
- 路径形态：本轮实例目录**含空格与中文**（`Optimized fps 1.20.1 导入`）——②的替换**必须加引号**，否则会被空格切碎（我第一次验证就踩了这个坑，已记录）。
- 未验证：NeoForge（同一套装配逻辑，缺陷应同样适用，但未单独实测）；Forge 进入世界/长时间游玩。

## 4. 复现

```powershell
# ① 用当前源码 EXE 导入真实 Forge 包（会成功）
powershell -File ...\run-20261006-3400-phases\verify-phase.ps1 -ExePath <当前源码 EXE> `
  -ColdDir %TEMP%\pgforge\cold -Phase forge -PackPath <optimized-fps.mrpack> -JsonPath <证据>\forge-import.json
# ② 捕获产品命令行 → 按三处修正 → 手工运行（本目录脚本）
powershell -File ...\run-20261007-3600-forge-launch-fix\verify-forge-launch-fix.ps1 `
  -ColdDir %TEMP%\pgforge\cold -JsonPath <证据>\forge-fix.json -ObserveSeconds 200
#    期望：tokens_restored=True、placeholders_substituted=True；再手工去掉 -cp 里的原版 jar 后 → 存活 200 秒 / 图集 12 / 0 崩溃
```

产物：`forge-product-cmdline.txt`（19,402 字符，产品原始命令行）、`forge-fixed-cmdline.txt`（修正后）、
`forge-fix-step.log`、`forge-fix.json`、`forge-fixed-stderr-full.log`（③ 的 ResolutionException 原文）、
`forge-fixed4-run.json`（三处修正后的存活/标记结果）、`forge-fixed-game.log`（193 行，含 Forge 版本检查）。
