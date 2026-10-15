# Forge 启动三处缺陷：**补丁已备并在"打过补丁的构建"上验证通过**（第 86 轮）

日期：2026-10-07（Asia/Hong_Kong）。**工作区未改动**（`modified=47 / staged=0` 前后一致）——补丁只在 `%TEMP%` 的仓库副本里应用。
被测补丁：`proposed-fix-forge-launch-assembly.diff`（**5 个 hunk，`git apply --check` ✅**，+72/−3 行，只改 `LauncherService.cs` 一个文件）。

## 1. 补丁内容（三处修复，对应第 84–85 轮定位的三个缺陷）

| # | 缺陷 | 补丁改法 |
| --- | --- | --- |
| **①** | `LauncherService.cs:197` `list.DistinctPreserveOrder()` 把重复的 `--add-opens`/`--add-exports` token 去重 | 换成 `DeduplicateJvmArguments(list)`：**可重复选项本身与其取值一律保留**，其余仍按原样去重（保留顺序）；新增 `RepeatableJvmOptions` 白名单（`--add-opens/--add-exports/--add-reads/--add-modules/-p/--patch-module/--module-path/--limit-modules/--upgrade-module-path`）|
| **②** | `BuildReplacements` 缺 Forge 占位符 | 补两条：`["library_directory"] = Path.Combine(profile.GameDirectory, "libraries")`、`["classpath_separator"] = (OperatingSystem.IsWindows() ? ";" : ":")`（`version_name` 本来就有 ✓）|
| **③** | 类路径里放了原版 `1.20.1.jar` | 新增 `SuppliesOwnClient(version)`：版本 JSON 的 `-DignoreList=` 里含 `client-extra`（Forge/NeoForge 的标志）时**不把父版本 jar 加进 `-cp`**；`BuildClasspath` 的 `clientJar` 参数改为可空 |

> 注：`ProcessStartInfo.ArgumentList` 是**逐元素**传参，所以 ② 的替换**不需要手工加引号**（第 85 轮我手工验证时必须加引号，是因为我在拼一条扁平命令行）。

## 2. 验证：**8/8 全通过**（产品自己启动 Forge）

做法：复制仓库 → `git apply` 补丁 → `dotnet build -c Release`（**0 警告 0 错误**）→ 把 apphost 覆盖进已有便携冷目录（复用已导入的 Forge 实例/Java/assets）→ 由**产品自己**调 `/launch`。

```
Forge 标记: 行数=193  lwjgl=1  声音=1  图集=12  崩溃=0  java存活=1（整个 204 秒观察期）
Vanilla 回归: 命令行=5808 字符 含库路径=True  lwjgl=1  图集=10  崩溃=0
逐项: forge_cmdline_has_libpath=True  forge_tokens_preserved=True  forge_placeholders_substituted=True
      forge_no_vanilla_jar_in_cp=True  forge_lwjgl=True  forge_atlases=True  forge_no_crash=True
      vanilla_regression_ok=True
判定: PASS：打过补丁的产品自己把 Forge 实例启动到渲染管线，且 Vanilla 回归正常
```

## 3. 我第一次验证**失败**了 —— 原因是我自己的补丁有 bug（已修，且教训值得记）

第一版 `DeduplicateJvmArguments` 写成了 `bool keep = previousWasRepeatable || seen.Add(item);`：
第二次出现的 `--add-opens` **已经在 `seen` 里** ⇒ 仍被丢掉 ⇒ Forge 启动照旧 0 秒退出。
更危险的是：那一轮我的断言读到的**游戏日志是上一轮手工验证留下的旧内容**（时间戳 `00:51:48` 而不是当轮），差点把"旧 PASS"当成"新 PASS"。
**两条教训**：① 去重逻辑必须显式保留"可重复选项本身"，不能只靠"前一项是可重复选项"；② 断言游戏标记时必须**核对日志时间戳/文件 mtime**，否则会拿旧日志冒充新结果。

## 4. 边界

- 补丁**没有应用到工作区**（只在副本验证）：这属于**新的源码改动**，需要你授权后才落到 `D:\松光启动器`。
- 未验证：**NeoForge**（同一套装配逻辑，`client-extra` 判定同样适用，但未单独实测）；Forge 的"进入世界/长时间游玩"；② 的 `classpath_separator` 在非 Windows 平台上的行为（代码里按平台取 `;`/`:`）。
- ③ 的产品级取舍：本补丁采用"**不加父版本 jar**"；另一种做法是改用安装器产出的 `forge-<ver>-client.jar`——本轮只验证了前者可行。

## 5. 复现

```powershell
# 补丁可应用性（只读）
git apply --check <证据>\proposed-fix-forge-launch-assembly.diff
# 在副本上验证（复制仓库 → 应用 → 构建 → 复用冷目录跑 Forge + Vanilla 回归）
powershell -File ...\run-20261007-3800-forge-launch-patch\verify-forgefix-build.ps1 `
  -PatchPath <证据>\proposed-fix-forge-launch-assembly.diff -ColdDir %TEMP%\pgforge\cold `
  -JsonPath <证据>\forgefix2.json -ObserveSeconds 200
# 期望：8/8 PASS；forgefix-game.log 含 LWJGL/图集，forgefix-cmdline.txt 里 --add-opens 出现 2 次
```

产物：`proposed-fix-forge-launch-assembly.diff`、`forgefix-step.log`、`forgefix2.json`、`forgefix-cmdline.txt`、
`forgefix-game.log`（193 行）、`verify-forgefix-build.ps1`。
