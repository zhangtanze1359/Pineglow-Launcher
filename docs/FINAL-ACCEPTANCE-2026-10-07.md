# 限定修复的终态报告（2026-10-07）：**具备独立复验条件，等待 Codex 独立验收**

范围依据：`harness-blocker-diagnosis-2026-10-07.md` + `harness-blocker-remediation-workflow-2026-10-07.md` + `minimax-harness-blocker-execution-prompt-2026-10-07.md`。
本轮只改两个生产文件 + 新增两个聚焦测试 + 追加白名单；**未提交/未推送/未打 tag/未发布**；未 reset/restore/checkout/clean/stash；未批量暂存；未改用户配置/凭据/历史证据/缓存 goal。

## 1. 源码身份

| 项 | 值 |
| --- | --- |
| HEAD | `f145d14`（未变）|
| 工作区 | **47 modified / 0 staged**；未跟踪新增：2 个测试文件 + 白名单追加 |
| 白名单 | `scripts/source-snapshot-inputs.txt` **151** 项（新增类别 N 两项）|
| 门槛输入 | **312**（161 tracked + 151 白名单），门槛自检「期望 312 / 实际 312」一致 ✅ |
| 修复后 `LauncherService.cs` | sha256 `6cc9ac01533300c1e76686d3bd13049a85c3d1a99c3eb0b1772d991f9f704ec5`|
| 修复后 `LocalApiService.cs` | sha256 `2e11a2ba54b99b9fab3a270486f958bc7c1b1b8fb06e015cda36ef44b6330188` |
| **新单文件 EXE** | 53,052,722 B，**sha256 `3859e1390ab8b24ec1e0779439f163cb559653f449a566f015b0339ee4e5005d`**（旧 `6ed0c8db…`、`35e34ef9…` 作废）|

## 2. RED → GREEN（两处阻断）

| 阶段 | 聚焦测试（9 项） | 全量 C# |
| --- | --- | --- |
| **RED**（未修复源码，隔离副本）| **失败 5 / 通过 4**（5 项分别对应缺陷 ①②③④）| — |
| **GREEN**（最小修复后）| **失败 0 / 通过 9** | **失败 0 / 通过 926**（917 旧 + 9 新，**无回归**）|

修复内容（4 处，仅两个授权文件）：

1. `ResolveJvmArguments` 收尾**删除整表去重**（语义 token 与其取值原样保留）；
2. `BuildReplacements` 补 `${library_directory}` / `${classpath_separator}`；
3. 新增 `SuppliesOwnClient(gameDirectory, version)`：**profile 自己声明 `client-extra`** 且 **磁盘上确有替换 client jar**（`client-*-extra.jar` / `*-client.jar`）时才不把父版本原版 jar 放进 `-cp`；
4. `ResolveJavaPath`：把**游戏目录**传进匹配与所需主版本解析（走 `javaVersion.majorVersion`、可沿 `inheritsFrom` 上溯）；"任意 Java"必须主版本已知且兼容才使用，否则抛可读错误；**启动路径不做自动下载**（自动安装仍在整合包导入路径）。

## 3. 完整门槛（修复后源码，新 GUID 快照）

**模式 GREEN / 失败项 0**：白名单 151、文件数量 **312/312**、Node **305/305**、**C# 926/926**、UI 闭包、非 RID 锁定还原、Release build、VerifyHost、win-x64 publish、**独立冷启动 19/19**。
已知边界保留：`publish win-x64 (锁定还原探测)` **NU1004**（RID 不在锁定还原范围）。

## 4. 真实产品启动验证（六个用例，全部 PASS）

用修复后源码构建的单文件 EXE，启动真实实例/真实整合包：

| 用例 | 产品选用的 Java | 命令行 | 占位符残留 | 日志（本轮新写）| 渲染 | 崩溃 | JVM |
| --- | --- | --- | --- | --- | --- | --- | --- |
| **Forge 1.20.1** | **java-17** ✅ | 19,999 | 0 | 371 行 | LWJGL + 声音 + **12 图集** | **0** | 存活 ✅ |
| **NeoForge 21.1（MC 1.21.1）** | **java-21** ✅ | 22,154 | 0 | 483 行 | LWJGL + 声音 + **15 图集** | **0** | 存活 ✅ |
| **Vanilla 1.18.2** | **java-17** ✅ | 5,808 | 0 | 32 行 | LWJGL + 声音 + **10 图集** | **0** | 存活 ✅ |
| **Fabric 1.18.2**（真实包）| **java-17** ✅ | 8,645 | 0 | 1,335 行 | LWJGL + 声音 + **10 图集** | **0** | 存活 ✅ |
| **Quilt 1.20.1**（真实包）| **java-17** ✅ | 14,993 | 0 | 710 行 | LWJGL + 声音 + **12 图集** | **0** | 存活 ✅ |
| **Fabric 1.21.1**（真实包）| **java-21** ✅ | 15,772 | 0 | 405 行 | LWJGL + 声音 + **14 图集** | **0** | 存活 ✅ |

- Forge/NeoForge 的 `--add-opens` / `--add-exports` 各 **2 次**、占位符 **0** 残留（缺陷①②③修复生效）。
- **Java 选择双向正确**：1.18/1.20 系 → 17；**1.21 系的 NeoForge 与 Fabric 都由产品自己选到 21**（缺陷④修复生效，且不限于 Forge）。
- 每个用例都做了**"日志新鲜度"防护**（启动前删旧日志 + 只采信 `mtime > 启动时刻` 的日志），避免旧 PASS 冒充新结果。

### 保留的一次真实失败（已修正）

第一版判定（只看版本 JSON 的 `libraries` 里有没有 client jar）导致真实启动失败：Forge 只写 12 行日志、NeoForge 抛异常崩溃。
根因：Forge/NeoForge 的补丁 client **不在** `libraries` 列表里，而是安装器写到 `libraries/net/minecraft/client/client-*-extra.jar` 与 `libraries/net/minecraftforge|neoforged/**/*-client.jar`。
改为"**profile 声明 + 磁盘确有替换 jar**"两条证据后全部通过。失败证据：`run-20261007-4600-fixed-source-gate/launch-only-step.log`。

## 5. 边界（不夸大）

| 项 | 状态 |
| --- | --- |
| **进入世界 / 长时游玩** | **NOT_RUN**（本轮只到"进渲染管线"；未做世界加载与长时间运行留证）|
| MC 1.21+ 的 **Quilt** | **NOT_RUN**（验了 1.21+ 的 Fabric 与 NeoForge）|
| RID 发布 + 锁定还原 | **NOT_RUN（已知边界 NU1004）** |
| 真实微软账号登录及分支 | **BLOCKED**（需真人账号）|
| AI 诊断 | **NOT_RUN**（需第三方 Key）|
| Gitee 固定 `update.json` | **NOT_RUN**（需 token）|
| 提交 / 推送 / tag / Release / Gitee 写入 | **BLOCKED**（需对应授权）|
| 未逐行审计的既有产品范围 | **保留声明**（本会话未重新测量比例）|

## 6. 结论

- 两处本地源码阻断已**最小修复并通过聚焦 RED→GREEN、完整门槛与六个真实启动用例**。
- **终态：具备独立复验条件，等待 Codex 独立验收。**
- 本报告**不构成发布批准**；账号/密钥/token/发布授权缺失项按上表分别 BLOCKED / NOT_RUN。

## 7. 证据根

- `docs/release-remediation-evidence-2026-10-05/run-20261007-4400-limited-fix/`（RED/GREEN、聚焦与全量测试日志、修复报告）
- `docs/release-remediation-evidence-2026-10-05/run-20261007-4800-final-gate-and-launch/final/`（最终门槛：console、逐步日志、manifest）
- `docs/release-remediation-evidence-2026-10-05/run-20261007-4800-final-gate-and-launch/`（Forge/NeoForge 真实启动 JSON 与命令行）
- `docs/release-remediation-evidence-2026-10-05/run-20261007-5000-launch-regression/`（Vanilla/Fabric/Quilt/Fabric-1.21.1 的 step 日志、命令行与游戏日志）
- 旧证据：`run-20261007-3600-forge-launch-fix`、`run-20261007-3800-forge-launch-patch`、`run-20261007-4000-neoforge`、`run-20261007-4200-java-version-fix`
