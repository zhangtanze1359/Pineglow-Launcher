# NeoForge：**导入 PASS，启动失败——根因是 Java 版本选错**（第 87 轮）

日期：2026-10-07（Asia/Hong_Kong）。被测：**打过三处启动补丁的构建**（apphost，`sha256=102b398a…`），冷目录 `%TEMP%\pgforge\cold`。
真实包：`optimized-fps` / **NeoForge 21.1.230** / MC **1.21.1**。

## 1. 结果

| 步骤 | 结果 |
| --- | --- |
| **导入** | **PASS — 262 秒**，实例 `loaderType=NeoForge`、`versions/neoforge-21.1.230` 落盘 ⇒ **B2（安装器 URL）+ B2b（profile 桩）对 NeoForge 同样成立** ✅ |
| **启动（产品，用自动装的 Java 17）** | **FAIL**：`launch` 返回 `{"ok":true,"pid":…}`，JVM **秒退**、游戏日志都没生成、**产品侧看不到任何输出** |
| **启动（同一套参数，换 Java 21）** | **PASS**：存活整个 153 秒观察期、**日志 327 行、`LWJGL=1`、`声音=1`、`图集=15`、`崩溃=0`**，NeoForge 版本检查在跑（`[rrfs] Found status: BETA_OUTDATED`）、`[ModernFix] Game took 18.78 seconds to start` |

## 2. 根因（用"java 包装器"抓到真实报错）

产品侧看不到任何输出（API 路径的 `_log` 是空委托）。我写了一个 **java 包装器**（`%TEMP%\pgwrap`：把收到的 argv 逐条落盘，再用 `ArgumentList` 逐元素转交给真实 java 并转发其输出），
把它设成 `javaPath` 后，拿到了产品**真实使用的 61 个参数**（已存档 `neoforge-args.txt`），再用**逐元素传参**重放，得到真实报错：

```
Error occurred during initialization of boot layer
java.lang.module.FindException: Error reading module: …\neoforge-21.1.230-client.jar
Caused by: java.lang.module.InvalidModuleDescriptorException: Unsupported major.minor version 65.0
```

**`major.minor version 65.0` = Java 21 的 class 文件版本**；而产品用的是它自己装的 **Java 17**（`data/runtimes/java-17-windows-x64/…`）
⇒ **NeoForge 21.1（MC 1.21.1）需要 Java 21，产品却给了 Java 17** ⇒ 启动即失败。

**对照验证**：下载 Temurin **21.0.12.1**，用**同一套参数、同一个实例**运行 ⇒ **游戏跑起来了**（15 个图集、0 崩溃、NeoForge 版本检查在工作）。
⇒ 根因确定，且**不是**我上一轮那三处补丁造成的（我把原版 jar 加回 `-cp` 再试，仍然失败；三处修复在参数里都在：`--add-opens`×2、`--add-exports`×2、占位符 0 残留）。

## 3. 新缺陷 ④（blocker 级）：**Java 版本选择/自动安装不匹配**

- 现象：MC **1.21+** 的实例（NeoForge 21.1、以及任何需要 Java 21 的版本）在只有 Java 17 的机器上**必然启动失败**，而且**产品不给任何提示**。
- 影响面：这是"新版本游戏"的普遍问题（1.20.5+ 起就需要 Java 21），比单个加载器更广。
- 相关代码线索（未逐一确认）：产品里有 `JavaVersionMatcher`、`JavaRuntimeService`（自动安装），但本次它**沿用了 Java 17**，
  说明"版本不满足时的升级/另装"这条路径没有覆盖 1.21+ 的需求（或映射表缺 1.21.1→21）。
- 建议修法：按 MC 版本解析所需 Java 主版本（1.21+ → 21；1.20.5–1.20.6 → 21；1.18–1.20.4 → 17），
  若现有运行时主版本不足则**自动安装/提示**对应版本，并在启动前用 `JavaVersionMatcher` 拒绝不匹配的运行时（给出可读错误）。

## 4. 顺带确认的两件事

- **产品对子进程输出不可见**：JVM 的真实报错（`Unsupported major.minor version 65.0`）在产品侧**完全丢失**，用户只看到"没启动"。
  这与既有的"API 路径 `_log` 为空委托"是同一个诊断性问题（决定项 14 第①条）。
- **我上一轮的三处补丁对 NeoForge 是安全的**：参数里 `--add-opens`/`--add-exports` 各 2 次、占位符 0 残留、类路径无原版 jar，NeoForge 仍走到"Java 版本"这一层才失败。

## 5. 复现

```powershell
# ① 导入真实 NeoForge 包（会成功）
powershell -File ...\run-20261006-3400-phases\verify-phase.ps1 -ExePath <打过补丁的 EXE> `
  -ColdDir %TEMP%\pgforge\cold -Phase forge -PackPath <optimized-fps-neoforge.mrpack> -JsonPath <证据>\neoforge.json
# ② 用 java 包装器抓产品真实参数（见 %TEMP%\pgwrap；把它设为 javaPath 后 launch）
# ③ 用 Java 17 重放 → Unsupported major.minor version 65.0；换 Java 21 重放 → 游戏起来（15 图集/0 崩溃）
```

产物：`neoforge.json`、`phase-forge-step.log`、`neoforge-args.txt`（61 条真实参数）、`neoforge-args-list.txt`、
`neoforge-wrapper-output.log`（`Unsupported major.minor version 65.0` 原文）、`neoforge-java21-game.log`（327 行，Java 21 下成功启动）。
