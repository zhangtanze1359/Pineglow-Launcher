# 缺陷 ④（Java 版本选错）：**根因细化 + 补丁 v3（两份补丁一起可编译）**（第 88 轮）

日期：2026-10-07（Asia/Hong_Kong）。工作区未改动（`modified=47 / staged=0`）；补丁只在 `%TEMP%` 副本里验证。

## 1. 根因细化（比上一轮更精确）

上一轮只说到"产品用了 Java 17 而 NeoForge 需要 Java 21"。本轮把**为什么选错**定位到具体代码：

- **启动路径** `LocalApiService.ResolveJavaPath(config, instance, instance.MinecraftVersion)` →
  `JavaLocator.MatchForMinecraftVersion(found, minecraftVersion)`（**不传游戏目录**）→ 内部走 `JavaVersionMatcher.GetRequiredJavaMajor(string)`。
- 而 `instance.MinecraftVersion` 是**加载器版本 id**（如 `neoforge-21.1.230`、`1.20.1-forge-47.4.3`），字符串解析器认不出它，**默认返回 17**。
- 实测（用产品自己的 DLL 跑探针）：

| 传入 | 判定所需 Java |
| --- | --- |
| `1.21.1` | **21** ✅ |
| `neoforge-21.1.230` | **17** ❌ |
| `1.20.1-forge-47.4.3` | **17** ❌ |
| `fabric-loader-0.19.3-1.18.2` | 17（碰巧对）|
| `quilt-loader-0.21.0-1.20.1` | 17（碰巧对）|

⇒ **凡是"加载器版本 id"形式的实例，所需 Java 都会被判成 17**。因此 **MC 1.21+ 的加载器实例（NeoForge / 现代 Forge / 新版 Fabric）在只有 Java 17 的机器上必然启动失败**。
产品其实**已经有**元数据版重载 `GetRequiredJavaMajor(version, gameDirectory, log)`（读版本 JSON 的 `javaVersion.majorVersion`，并能沿 `inheritsFrom` 上溯），**只是启动路径没用它**。

## 2. 补丁 v3：`proposed-fix-java-version-selection.diff`（3.0 KB，`git apply --check` ✅）

改 `LocalApiService.ResolveJavaPath`：

1. **把游戏目录传进匹配**：`MatchForMinecraftVersion(found, minecraftVersion, javaGameDir)` —— 走版本 JSON 的 `javaVersion.majorVersion`；
   所需主版本也用元数据版 `GetRequiredJavaMajor(minecraftVersion, javaGameDir)`（解析不出时才退回字符串版）。
2. **不再静默用不匹配的 Java**：只有"任意 Java"确实 `IsCompatible` 时才用它。
3. **先自动安装**所需主版本（复用整合包导入那条 `JavaRuntimeService.InstallAsync` 路径）。
4. 都不行时抛**可读错误**：`该版本需要 Java 21，但本机只找到 Java 17（…），且自动安装未成功。请在设置中指定 Java 21 的路径。`

**验证**：两份补丁（Forge 启动装配 + Java 版本选择）**一起应用并编译**：**0 警告 0 错误** ✅（在 `%TEMP%` 副本里；工作区未动）。

## 3. 我自己的两个 harness bug（都记录在案）

1. **又踩了"旧日志陷阱"**：第一次验证时，我的脚本读到的是**上一轮手工验证留下的 `latest.log`**（时间戳 `01:33`，而不是当轮），于是把 3 项检查报成 PASS，而实际上那一轮 NeoForge **根本没起来**（`java=0`、命令行捕获长度 0）。
   ⇒ 已在脚本里修正：**启动前删除旧日志 + 记录时间戳**，只有 `mtime > 启动时刻` 才采信标记，否则标记一律记 0 并打印警告。
2. **补丁生成的索引错位**：v2 的补丁里两处编辑都用**原始文本**算出的偏移量，第二处编辑后第一处的偏移失效 ⇒ 生成出语法坏行
   （`…请在设置中指定 Java 路径");vaGameDir);`）⇒ 构建报 `CS1002/CS1513`。⇒ 改为**每次编辑后在更新后的文本上重新定位锚点**，v3 通过。

## 4. 当前状态（诚实口径）

| 项 | 状态 |
| --- | --- |
| NeoForge **导入** | **PASS**（262 秒，B2/B2b 对 NeoForge 成立）|
| NeoForge **启动** | **仍 FAIL**（本轮用"补丁 v2"的那次运行因语法错误而未生效；v3 已可编译，**端到端复验待下一轮**）|
| 缺陷 ④ 根因 | **已定位到代码**（加载器版本 id → 默认 17；启动路径未用元数据版匹配）|
| 缺陷 ④ 补丁 | **v3 就绪**，与 Forge 装配补丁**一起可编译**；未应用到工作区 |
| Forge（1.20.1）回归 | **PASS**（本轮用打补丁的构建：命令行 19,999 字符、LWJGL、12 图集、0 崩溃、JVM 存活整个观察期）|

## 5. 复现

```powershell
# 两份补丁一起应用 + 构建（副本，不动工作区）
git apply --check <证据>\proposed-fix-java-version-selection.diff
git apply --check <证据>\proposed-fix-forge-launch-assembly.diff
dotnet build dev/PineglowLauncher/Pineglow.Launcher.csproj -c Release   # 期望 0 警告 0 错误
# 端到端复验（下一轮）：用打过两份补丁的构建启动 NeoForge 实例，期望它自己挑到 Java 21 并进渲染管线
```

产物：`proposed-fix-java-version-selection.diff`（v3）、`verify-java-version-fix.ps1`（含"旧日志"防护）、`javafix-step.log`（v2 的构建失败原文）、`javafix.json`。
