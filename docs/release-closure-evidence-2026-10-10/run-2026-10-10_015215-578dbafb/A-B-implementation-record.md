# A/B 发布阻断修复实施记录（授权后）

运行标识：`run-2026-10-10_015215-578dbafb`
时间：2026-10-10 02:00–02:20，Asia/Hong_Kong（+08:00）
授权来源：本轮用户明确选择「授权修 A+B（推荐）」
授权范围：`scripts/source-snapshot-inputs.txt`、`scripts/verify-source-snapshot.ps1`、`LocalApiService.cs` 及对应测试；跑聚焦与全量回归；**不**提交、不推送、不打 tag、不发布

**结论：两项 P1 阻断已在源码层修复，并通过仓库自带的完整候选流水线（最终判定 PASS，退出码 0）与真实单文件 EXE 的端到端复验。C（RID 发布锁定）仍作为已知未锁定边界保留，未在本轮处理。**

## 1. 变更清单

| 文件 | 类型 | SHA-256（改后） |
| --- | --- | --- |
| `dev/PineglowLauncher/Pineglow.Launcher.Services/LocalApiService.cs` | 已跟踪（改前 `2E11A2BA…`，改后 `4180CECB…`） | `4180CECBCCAAA6BCAE2BDD454D26F0EA3A158376A889DA06D3E9D8C3FF689D23` |
| `dev/PineglowLauncher.Tests/UiSourceSelectionTests.cs` | 新增 | `41BED4499769F7739284216B9C5011A48E581FE10F5ED55A2A8937F6C31046BC` |
| `scripts/verify-source-snapshot.ps1` | 未跟踪（白名单输入） | `3DDAF00B650399B9771C5E839F486364B907F2E659B8ECF54D3A18D336CEC574` |
| `scripts/source-snapshot-inputs.txt` | 未跟踪（白名单） | `543EAA84C95962EA6C1A0671E09B0982564C7D26B5D4FB9D7A5EE4B871BDD7A2` |

`git status`：0 staged；已修改跟踪文件仍为 41 项（`LocalApiService.cs` 本来就在恢复后的 41 项差异里，本轮只改其中 `ResolveUiRoot` 一段）。
改动内容逐行对照见 `p1b-focused-diff.txt`：以 01:52 的修复前快照（该快照的 `LocalApiService.cs` 哈希 `2E11A2BA…`，与本轮复验阶段记录的修复前哈希一致）为基线，63 行差异**全部落在 `ResolveUiRoot` 一处**。

## 2. A：候选输入闭包

改动三处：

1. **补白名单**：新增类别 O，逐项列出 11 个产品/测试/VerifyHost 输入（外加本轮的 `UiSourceSelectionTests.cs`，共 12 项精确路径，无通配）。这 11 项在删除前属于较晚本地提交的已跟踪文件，恢复后 Git 基底变早才变成未跟踪，因此从未进过白名单。
2. **候选项闭包断言**（新增）：按目录枚举 `dev/`、`ui/`、`scripts/`、`.github/` 下全部非 bin/obj/TestResults 输入，逐项要求出现在快照中；缺项列出路径并结构化失败，写入 `verify-summary.json` 的 `closureMissing`。原实现只验「数量自洽 + 无泄漏」，而漏 22 项时这两条**依然全过**（期望数与实际数一起变小），这就是缺陷能静默通过步骤 2 的原因。
3. **抽查对象改为推导**：原实现硬编码 7 条路径并假定它们必然是 tracked、必然在快照内——正是「以前 tracked ⇒ 现在必然收集到」的错误推导；恢复后其中两项已不在 tracked 集合，`Get-Sha256` 抛 `FileNotFoundException` 使整条验证中途崩掉、连汇总都写不出。现在抽查对象由 `git diff --name-only HEAD` 推导（这些正是 HEAD 版本 ≠ 工作区版本的文件），缺项一律转成结构化失败。

GREEN 证据（`green-snapshot/`、`green-snapshot-console.log`，最终判定 **PASS**，退出码 0）：

| 检查 | 结果 |
| --- | --- |
| 白名单 | 163 项（原 151 + 类别 O 12 项），缺失 0 |
| 快照组装 | tracked 139 + 白名单 163 = **302 文件**，额外/泄漏 0 |
| 候选项闭包断言 | **通过：`dev/ui/scripts/.github` 下 298 个输入均在快照内** |
| tracked 抽查 | 通过：8 项（由 diff 推导）确为工作区版本——这正是修复前崩溃的那一步 |
| 脚本编码守卫 / 锁定形态守卫 | 均通过 |
| Node 正式测试 | 305/305，fail 0、skip 0 |
| C# 全套 | **939/939**，fail 0、skip 0（修复前 931；+8 为本轮新增用例） |
| VerifyHost / 二分冒烟 | 通过（隔离凭据后端操作 34 次） |
| 单文件 publish | 通过，53,052,722 bytes，`ca8f94c1a6173f23f38f3d2a3c949affc16d5ab67752b662c7806831f350d93e` |
| 独立冷启动 | 通过，19/19，窗口标题「松光启动器」，端口 48292 |
| RID 锁定发布探测 | 仍为 `KNOWN-UNLOCKED`（NU1004）——C 未处理，如实保留为已知边界，不计入失败项 |

## 3. B：旧目录升级界面一致性

改动：`LocalApiService.ResolveUiRoot()` 拆成「取真实输入」+ 可测试的 `ResolveUiRootCore(...)` 决策核；单文件部署（`UiBundle.IsSingleFileDeployment()`，判据是捆绑程序集 `Location` 为空）时把**内嵌解包**提到 EXE 旁外置 `UI/` 之前；`PINEGLOW_UI_DIR` 显式覆盖、目录构建/开发树的外置 UI 优先、解包失败回退外置、配置目录回退等既有语义全部保留。
链路一致性：`WebShellWindow` 的静态回退走 `LocalApiService.UiRoot`，因此「API 没起来时去看 EXE 旁 UI/」这条在单文件下必然落空的判断也一并修正。

RED（修复前，同一探针 `probe-ui-source-v2.ps1`，旧 EXE `3BA60553…`）：
`legacy` 模式退出码 1，`legacyMarkerServed=true`、`externalUiServed=true`（服务内容 SHA-256 与旧文件逐字节相同）。

单元级 RED（隔离副本内把单文件优先分支停用后重跑）：
`失败: 1，通过: 7，总计: 8`，唯一失败项正是 `SingleFileDeployment_PrefersEmbeddedBundle_OverStaleExternalUi`（退出码 1）——证明该回归位确实能抓住旧行为。

GREEN（修复后两次真实单文件 EXE）：

| 被验 EXE | 模式 | 退出码 | 旧标记 | 服务外置旧 UI | 与当前源码一致 |
| --- | --- | --- | --- | --- | --- |
| 隔离构建 `FA367D8D…` | legacy（旧外置 UI 存在） | **0** | false | false | **true** |
| 隔离构建 `FA367D8D…` | clean（干净安装对照） | 0 | false | false | true |
| 流水线构建 `ca8f94c1…` | legacy | **0** | false | false | **true** |
| 流水线构建 `ca8f94c1…` | clean | 0 | false | false | true |

四次均 `healthReady=true`、只停止探针自己启动的进程（`ownedProcessStopped=true`）。

## 4. 本轮途中发现并修正的两个自身缺陷（如实记录）

1. **编辑脚本时丢了 UTF-8 BOM**：`scripts/verify-source-snapshot.ps1` 含中文，本机 ANSI 代码页为 936，无 BOM 时 PowerShell 5.1 会按 GBK 解析成乱码。仓库既有编码守卫正是为此设立（快照内 21 个非 ASCII 的 `.ps1/.psm1` 全部带 BOM）。发现后已恢复 BOM；随后运行的编码守卫通过。这属于**我在本轮引入、并在本轮修掉**的问题。
2. **`git diff` 的 stderr 警告打断了脚本**：`$ErrorActionPreference='Stop'` 下，git 对 `.github/workflows/*.yml` 的 LF/CRLF 警告会变成 `NativeCommandError` 终止验证。修正方式只对该条只读命令临时降噪（`2>$null` + 局部 `Continue`），不改全局策略。第一次跑（`green-snapshot` 的首轮输出）的失败原始痕迹保留在该运行目录。

## 5. 未做与未验证

- **未处理 C**：win-x64 的锁定还原仍 NU1004，`packages.lock.json` 未覆盖 RID 段与 `Microsoft.NET.ILLink.Tasks`。本轮未改 csproj/lock/CI；流水线以「已知未锁定边界」记录，未写成 locked PASS。
- **未复验真实场景**：完整原生 WebView 全量操作、全新在线安装、加载器进世界与长时游玩、微软真实成功登录、皮肤/AI 第三方调用、「下载→替换自己→重启」全链与失败回滚。
- **B 的 GREEN 覆盖范围**：无旧 UI、有旧 UI、干净安装、中文/空格路径（探针目录在临时路径下）已覆盖；旧 `configDir/UI`、重复启动、目录构建外置 UI 由单元决策矩阵覆盖，没有做真实目录构建形态的端到端复跑。
- **未做**：提交、推送、tag、发布、写线上更新清单。

## 6. 状态

`A`：源码层已修，仓库自带完整流水线 PASS——**具备独立复验条件**。
`B`：源码层已修，真实单文件 EXE 端到端 RED→GREEN——**具备独立复验条件**。
`C`：未处理（需锁定方案决策）。
整体：`TechnicalAcceptance` 仍按发布整体记为 **NOT_PASSED**；`FormalPhaseApproval=NOT_GRANTED`；`ReleaseApproval=NOT_GRANTED`。
