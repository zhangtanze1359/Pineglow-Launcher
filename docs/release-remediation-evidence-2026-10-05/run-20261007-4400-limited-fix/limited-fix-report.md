# 限定修复（2026-10-07）：两处源码阻断 **RED → GREEN**（第 90 轮）

依据：`docs/harness-blocker-diagnosis-2026-10-07.md` + `docs/harness-blocker-remediation-workflow-2026-10-07.md` +
`docs/minimax-harness-blocker-execution-prompt-2026-10-07.md`（本轮授权范围：`LauncherService.cs`、`LocalApiService.cs`、聚焦测试、`source-snapshot-inputs.txt`）。
**未提交/未推送/未打 tag/未发布**；未 reset/restore/checkout/clean/stash；未批量暂存；未改用户配置/凭据/历史证据/缓存 goal。

## 1. 授权范围与基线（记录 SHA256）

| 文件 | 修复前 SHA256 |
| --- | --- |
| `dev/PineglowLauncher/Pineglow.Launcher.Services/LauncherService.cs` | `eedc971b95362b72f956c0d776dbc6be2593a62fa34c6cfd82e0f654752917b9` |
| `dev/PineglowLauncher/Pineglow.Launcher.Services/LocalApiService.cs` | `596f2a800d63fb5162a3c2e231c9f99828a710be1e0655a2822036b329ae32c0` |
| `scripts/source-snapshot-inputs.txt` | `bc0d0aec7b38a9bfd9e233888a69fa9e5dbb9280426bf77f2f0f18fd0ae9b3d8` |

基线：HEAD `f145d14`、47 modified / 0 staged。详见 `allowed-scope-sha256.json`。

## 2. 先写隔离回归并记录**真实 RED**（在未修复源码的隔离副本里跑）

新增两个聚焦测试文件（已加入白名单类别 N）：

- `dev/PineglowLauncher.Tests/LoaderLaunchAssemblyTests.cs`
- `dev/PineglowLauncher.Tests/JavaSelectionMetadataTests.cs`

**RED（修复前，隔离副本）**：`失败 5 / 通过 4 / 总计 9`

| 失败用例 | 对应缺陷 |
| --- | --- |
| `ResolveJvmArguments_KeepsRepeatedAddOpensAndAddExportsTokens` | ① 整表去重吃掉重复的 `--add-opens`/`--add-exports` token |
| `ResolveJvmArguments_LeavesNoPlaceholderInModulePath` | ② 缺 `${library_directory}`/`${classpath_separator}` |
| `BuildReplacements_ProvidesForgePlaceholders` | ② 同上（替换表缺项）|
| `BuildClasspath_SkipsVanillaJar_WhenProfileSuppliesItsOwnClient` | ③ 自带 client 的 profile 仍被塞入原版 `<mc>.jar` |
| `ResolveJavaPath_LoaderInstanceRequiring21_NeverSilentlyReturnsIncompatibleRuntime` | ④ 启动路径用加载器版本 id 匹配 ⇒ 静默给出 Java 17 |

通过的 4 项是**守卫**：元数据版能读到 21、17 不匹配 21、未知主版本不视为兼容、Fabric 必须保留版本 jar。
证据：`red-focused.log`。

## 3. 最小修复（只改两个授权生产文件，4 处）

| # | 位置 | 改法 |
| --- | --- | --- |
| ① | `LauncherService.cs` `ResolveJvmArguments` 收尾 | **删除** `list.DistinctPreserveOrder().ToList()` → 直接 `return list;`（**不对语义 token 任意去重**；选项与其取值原样、按序保留）|
| ② | `LauncherService.cs` `BuildReplacements` | 补 `["library_directory"] = Path.Combine(profile.GameDirectory, "libraries")`、`["classpath_separator"] = (Windows ? ";" : ":")` |
| ③ | `LauncherService.cs` `BuildCommandAsync` + `BuildClasspath` | 新增 `SuppliesOwnClient(gameDirectory, version)`：**按实际 profile**（libraries 里确有 `client-*-extra.jar` / `*-client.jar`）判定；自带 client 时**不**把父版本原版 jar 放进 `-cp`；`BuildClasspath` 的 `clientJar` 改可空 |
| ④ | `LocalApiService.cs` `ResolveJavaPath` | 把**游戏目录**传进 `MatchForMinecraftVersion` 与 `GetRequiredJavaMajor`（走版本 JSON 的 `javaVersion.majorVersion`，可沿 `inheritsFrom` 上溯）；**"任意 Java"必须主版本已知且兼容才使用**，否则抛可读错误（`该版本需要 Java 21，但本机只找到 Java 17（…）…`）|

> 设计取舍（按 workflow 第 4 步）：**不在启动路径做 Java 自动下载**（避免给门槛测试引入网络依赖与超时脆弱性）；自动安装仍保留在既有的整合包导入路径，错误信息里明确指向"设置中指定 Java"与"整合包导入会自动安装"。
> 未采纳 `RepeatableJvmOptions` 白名单方案（诊断指出"不是所有 JVM 选项的可靠语法定义"），改为**完全不去重**。

## 4. GREEN（隔离副本，同一套聚焦测试）

- **聚焦：`失败 0 / 通过 9 / 总计 9`**（`green-focused.log`）
- **全量 C#：`失败 0 / 通过 926 / 总计 926`**（917 旧 + 9 新，**无回归**；`green-full-csharp.log`）
- 工作区构建：**0 警告 0 错误**
- 期间修正了**我自己测试里的两处断言错误**（`JsonUtil.BuildReplacements` 的 key 是 `${key}` 形式；"跳过原版 jar"的判定发生在 `BuildCommandAsync` 而非 `BuildClasspath`），修正后 GREEN。

## 5. 尚未完成（下一轮）

1. **新 GUID 完整候选快照 + `scripts/verify-source-snapshot.ps1`**：核对真实执行数/skip/退出码/产物，**记录新的单文件 EXE SHA256**（旧 `6ed0c8db…` 作废）。
2. **真实产品启动验证**：Forge 1.20.1 → 选 Java 17；NeoForge 1.21.1 → 选 Java 21；Vanilla/Fabric/Quilt 回归；**补 1.21+ Fabric/Quilt 的 Java 选择**；核对"产品选 Java、参数 token、0 占位符残留、Java 存活、LWJGL/图集/无崩溃"；**进入世界与长时游玩分别留证**（不能只由渲染标记推出）。
3. 终态：**"具备独立复验条件，等待 Codex 独立验收"**（不自动宣布可发布）；账号/AI Key/Gitee token/正式发布授权分别列 BLOCKED/NOT_RUN。

## 6. 复现

```powershell
# RED：在"未修复源码"的隔离副本里跑（本目录脚本未单独固化，可用 git stash 之外的方式：直接对比 red-focused.log）
# GREEN：
dotnet build dev/PineglowLauncher/Pineglow.Launcher.csproj -c Release
dotnet test dev/PineglowLauncher.Tests/Pineglow.Launcher.Tests.csproj -c Release `
  --filter "FullyQualifiedName~LoaderLaunchAssemblyTests|FullyQualifiedName~JavaSelectionMetadataTests"
# 期望：9/9；全量 926/926
```

产物：`allowed-scope-sha256.json`、`red-focused.log`、`green-focused.log`、`green-full-csharp.log`。

---

# 追加（第 91 轮）：最终门槛 + 真实产品启动验证

## 最终门槛（修复后源码，新 GUID 快照 `pineglow-snapshot-293a33b2…`）

- **模式 GREEN / 失败项 0**；白名单 **151** 项；文件数量 **312/312**；Node **305**；**C# 926/926**（含 9 项新聚焦测试）；publish；**独立冷启动 19/19**。
- **新单文件 EXE**：53,052,722 B，**sha256 `3859e1390ab8b24ec1e0779439f163cb559653f449a566f015b0339ee4e5005d`**（旧 `6ed0c8db…` / `35e34ef9…` 作废）。
- 已知边界保留：`publish win-x64 (锁定还原探测)` NU1004。

## 真实产品启动（用上面这个 EXE，启动已有实例）：**14/14 项全 True**

| 实例 | 产品选用的 Java | 命令行 | token | 占位符 | 日志 | 渲染 | 崩溃 | JVM |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| **Forge 1.20.1** | **java-17** ✅ | 19,999 | `--add-opens=2` / `--add-exports=2` ✅ | **0** ✅ | 371 行（本轮新写）| **LWJGL + 声音 + 12 图集** ✅ | **0** ✅ | 存活整个观察期 ✅ |
| **NeoForge 21.1（MC 1.21.1）** | **java-21** ✅ | 22,154 | `2 / 2` ✅ | **0** ✅ | 483 行（本轮新写）| **LWJGL + 声音 + 15 图集** ✅ | **0** ✅ | 存活整个观察期 ✅ |

判定：**PASS：修复后源码的产品自己选对 Java 并启动到渲染管线**。

## 保留的一次失败与随之修正（诚实记录）

第一次用"只看版本 JSON 的 libraries 里有没有 client jar"的判定跑真实启动时：**Forge 只写了 12 行日志、NeoForge 抛异常崩溃**。
根因：Forge 1.20.1 / NeoForge 21.1 的版本 JSON **libraries 里并不列出**补丁 client，jar 是安装器直接写到
`libraries/net/minecraft/client/client-*-extra.jar` 与 `libraries/net/minecraftforge|neoforged/**/*-client.jar` 的。
⇒ 把判定改成**两条实际证据**：① profile 自己声明（`-DignoreList` 含 `client-extra`）**且** ② 磁盘上确实存在替换用的 client jar。
改后：聚焦 9/9、门槛 GREEN、真实启动 14/14 全过。失败证据保留在 `run-20261007-4600-fixed-source-gate/launch-only-step.log`。

## 仍未做（下一轮）

- **Vanilla / Fabric / Quilt 回归**，以及**补 1.21+ Fabric/Quilt 的 Java 选择**（元数据路径）。
- **进入世界 / 长时游玩**分别留证（不能只由渲染标记推出）。
- 终态报告："具备独立复验条件，等待 Codex 独立验收"；账号 / AI Key / Gitee token / 正式发布授权分别 BLOCKED / NOT_RUN。