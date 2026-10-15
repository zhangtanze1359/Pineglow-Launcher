# 恢复版本 P1 阻断 · 本轮独立复验记录

运行标识：`run-2026-10-10_015215-578dbafb`
时间：2026-10-10 01:52–01:58，Asia/Hong_Kong（+08:00）
执行者：DSH 会话（承接 Codex 线程 `01a121b5-415f-7271-996c-2ce784c128f2` 的检查范围）
检查目录：`D:\松光启动器`
宿主：Windows PowerShell 5.1.26100.9549 / Windows；SDK 与 Node 未在本轮重建
被验产物：`C:\Users\Administrator\AppData\Local\Temp\pineglow-release-audit-4b7da0cd23f34522971cf2d1f1c13071\audit-dist\Pineglow.Launcher.exe`
产物 SHA-256：`3BA605531690D832900280DD29CCEAF1C0102EC71171F8498AFFF234DC47F96F`（53,052,722 bytes，与 `release-audit-2026-10-10/run.json` 记录一致）
Git HEAD：`c8aa286f65ad13433129fe0a9e86e2d3b0e9020b`；本轮结束 0 staged

**本轮只做检查、隔离复现与证据保存：没有修改任何产品源码、没有提交、推送、打 tag、发布或写线上清单。**

## 1. 本轮直接结论

| 项 | 本轮独立结果 | 原始证据（本运行目录内） | 能证明的范围 |
| --- | --- | --- | --- |
| P1-A 候选输入闭包 | **复现 FAIL**：tracked 139 + 白名单 151 = 290；步骤 2 通过后被漏文件中断 | `p1a-snapshot-console.log`、`p1a-exit.json` | 现有正式候选组装路径在恢复后的 Git 现场不再完整 |
| P1-A 漏项明细 | **复算 22 项**：11 项产品/测试/VerifyHost + 11 项 docs；独立闭包口径下 `dev/` 缺 11 项 | 本节第 3 段 | 源码仍在，缺的是候选清单 |
| P1-B 旧 UI 升级 | **复现 FAIL**：新 EXE + 旧外置 `UI/` → 服务的 HTML 与旧文件**逐字节相同** | `ui-source-legacy.json`、`p1b-legacy-v2-console.log` | 目录式旧安装原地换 EXE 会出现新后端配旧界面 |
| P1-B 对照组 | **PASS**：同一 EXE、无外置 `UI/` → 服务当前内嵌 UI（令牌归一化后与源码一致） | `ui-source-clean.json` | 判别变量只有「EXE 旁存在旧 UI 目录」一项 |
| C 发布依赖锁定 | **复现 FAIL**：`restore -r win-x64 --locked-mode` → NU1004，退出码 1 | `p1c-rid-locked.log`、`p1c-exit.json` | lock 未覆盖 win-x64 与 ILLink 任务包 |
| 锁定模式副作用 | 锁定还原**未改写** `packages.lock.json`（前后哈希相同 `FBBC9AE6…`） | `p1c-exit.json` | 失败不会污染 lock（与普通 publish 会改写不同） |
| 现场稳定性 | 相对 01:33 基线：新增条目全为未跟踪文件，**0 项新增已修改跟踪文件、0 项减少、0 staged** | `git-status-after.txt` | 本轮未改动产品源码 |

## 2. 复现方式（可重跑）

P1-A（非破坏性；脚本自身在临时目录组装快照，只向证据目录写结果）：

```
& .\scripts\verify-source-snapshot.ps1 -RepoRoot 'D:\松光启动器' -EvidenceDir <本运行目录>\p1a-snapshot
```

观察：白名单自检 151 项通过 → tracked 复制 139 → 数量校验「期望 290 / 实际 290」通过 → 泄漏检测「额外文件 0」通过 → 随后在读取
`...\dev\PineglowLauncher\Pineglow.Launcher.Services\AiDiagnoseService.cs` 时抛 `FileNotFoundException`，流程未走到测试与发布步骤。

**退出码口径说明（与本轮证据一致性有关）**：用 `pwsh -File` 调用时脚本中断表现为退出码 1（与 `release-audit-2026-10-10/existing-snapshot-exit.json` 一致）；
本轮宿主没有 `pwsh` 可执行文件，改为在当前会话内直接调用脚本，于是中断以 PowerShell 终止性异常呈现，`$LASTEXITCODE` 保持 0。
两种调用都能观察到同一事实——**流程在漏文件处中断、未完成**；差别只在包装层退出码，`p1a-exit.json` 如实记录了本轮的 0 与原因。

P1-B（本轮自有探针 `probe-ui-source-v2.ps1`，与被验产品解耦）：

```
& .\probe-ui-source-v2.ps1 -ExePath <audit-dist EXE> -Mode legacy -OutDir <本运行目录>   # 期望（修复后）0，实际 1
& .\probe-ui-source-v2.ps1 -ExePath <audit-dist EXE> -Mode clean  -OutDir <本运行目录>   # 干净安装对照，实际 0
```

`legacy` 用 `git show HEAD:ui/index.html` 加唯一标记构造旧外置 UI，新 EXE 放入同一目录后：`healthReady=true`、
`legacyMarkerServed=true`、`externalUiServed=true`（服务内容 SHA-256 `61E1007A…` 与旧文件相同）、`servedIndexMatchesCurrentSource=false`、退出码 1。
`clean` 同一 EXE 单独放置：`legacyMarkerServed=false`、`servedIndexMatchesCurrentSource=true`、退出码 0。
两次都只停止探针自己启动的进程（`ownedProcessStopped=true`）。

P1-C（隔离副本，避免触碰主目录 lock）：

```
<复制 dev/（排除 bin|obj）到临时目录> ; dotnet restore PineglowLauncher/Pineglow.Launcher.csproj -r win-x64 -p:PublishSingleFile=true --locked-mode
```

错误原文：`NU1004: 项目的运行时标识符已更改。项目的运行时标识符: win-x64，锁定文件的运行时标识符: 。`，并列出 `Microsoft.NET.ILLink.Tasks >= 10.0.9` 等项目引用差异。

## 3. P1-A 漏项：本轮独立复算

不引用上一轮报告的数字，直接重算：

- `git ls-files` = 139；`scripts/source-snapshot-inputs.txt` 实际内容行 = 151；并集 = **290**。
- 与恢复交付清单 `delivered-source-verification.json`（312 项，`exact` 全为 true）比对，**缺 22 项**。
- 其中 11 项为产品/测试/工具输入：
  `AiDiagnoseService.cs`、`MinecraftNewsService.cs`、`ModBisectService.cs`、`AiProviderProfile.cs`、
  `AiDiagnoseServiceTests.cs`、`CrashAnalyzerServiceTests.cs`、`MicrosoftAuthServiceTests.cs`、`ModBisectServiceTests.cs`、
  `VerifyHost/Program.cs`、`VerifyHost/BisectSmoke.cs`、`VerifyHost/VerifyHost.csproj`；另 11 项为 `docs/` 文档。
- 上述 11 项在现白名单中**命中 0 次**，确认不是「已列入但复制失败」。
- **独立闭包口径（新增发现，比对照 312 清单更适合做回归断言）**：把候选闭包定义为
  「`dev/`、`ui/`、`scripts/`、`.github/` 下全部非 bin/obj 文件」，则 `ui/`(33)、`scripts/`(31)、`.github/`(2) **缺口为 0**，
  只有 `dev/`(231) 缺 11 项——与上面 11 项完全重合。也就是说缺口是精确的、可枚举的、可用目录级断言守住的。

## 4. P1-B 根因与最小修复边界（只定位，未实施）

`dev/PineglowLauncher/Pineglow.Launcher.Services/LocalApiService.cs` 的 `ResolveUiRoot()`（约 228–259 行）选择顺序为：

1. `PINEGLOW_UI_DIR` 环境变量（显式覆盖）
2. **`AppContext.BaseDirectory/UI`（EXE 旁外置 UI）**
3. `UiBundle.EnsureExtracted()`（内嵌资源按版本+指纹解包）
4. `PlatformPaths.GetConfigDirectory()/UI`

第 2 步优先于第 3 步：只要升级用户的安装目录里还留着旧 `UI/`，新 EXE 就继续服务旧界面。
对照证据：`UiBundle.cs` 已实现 `IsSingleFileDeployment()`（以 `Assembly.Location` 为空判定单文件），
且 `EnsureExtracted()` 在非单文件宿主下**返回 null**（开发树/目录包继续用外置 UI，这是既有设计）。
因此问题不在 UiBundle，而在 `ResolveUiRoot` 的次序没有区分「正式单文件发行」与「目录式/开发部署」。

最小修复方向（待授权后才实施）：在 `ResolveUiRoot()` 中，当 `UiBundle.IsSingleFileDeployment()` 为真时把内嵌解包提到外置 `UI/` 之前；
保留 `PINEGLOW_UI_DIR` 显式覆盖与目录式部署的既有行为；不删除、不接管用户旧目录。
配套回归需要覆盖：无旧 UI、有旧 UI、旧 `configDir/UI`、中文/空格路径、重复启动、目录构建外置 UI。
仓库内可用的相关测试面：`UiBundleTests.cs`、`UpdateRecoveryTests.cs`、`LauncherStartupBoundaryTests.cs`。

## 5. 探针校正记录（保留原始错误）

第一次运行时 `clean` 对照组报退出码 3（`servedIndexMatchesCurrentSource=false`），与本轮结论相反。定位结果：

- 服务端在**内嵌 UI 路径**下按设计把 `content="__PINEGLOW_SESSION_TOKEN__"` 替换为本次会话令牌；
  逐行比对显示整份 HTML 与当前源码**只差这一行**（源码 7221 bytes，服务内容同长度，仅令牌值不同）。
- 即：这是**探针断言过严**（未归一化令牌），不是产品缺陷，也不是内嵌 UI 陈旧。
- 处理：保留第一次的原始产物 `ui-source-clean-run1-original.json`、`ui-source-legacy-run1-original.json` 与 `probe-ui-source.ps1`；
  另写 `probe-ui-source-v2.ps1`，在比较前把令牌值归一化回占位符，再重跑两种模式。

## 6. 证据层级与未验证项

- 本轮可断言：当前源码**能编译、能打包、单文件首启可用**（沿用 01:33 的 publish 与 19/19 冷启动记录，未在本轮重跑）；
  两个 P1 阻断在**独立复现**下依然存在，且 P1-B 已用对照组把判别变量收敛到「外置旧 UI 目录」。
- 本轮不可断言：完整原生 WebView 全量操作、全新在线安装、加载器进世界与长时游玩、真实微软成功登录、皮肤/AI 第三方调用、
  「下载→替换自己→重启」全链与失败回滚、干净提交与同 SHA 远程 CI。
- 未重跑：Node 305 与 C# 931 全量测试、单文件冷启动 19 项、线上更新入口 HTTP（本轮对 `gitee.com` 的只读请求解析到非公网 IP 而失败，
  未据此改变 01:37 记录的 Gitee 404 / GitHub 1.0.0 事实）。
- 用户 `config.json/.bak/.prev` 未在本轮被读写；探针使用独立临时目录、随机端口与便携标记，进程只停自己启动的实例。

## 7. 授权状态

原线程的用户指令是「检查距离发布还有多少距离」，授权范围为检查/评估。
`release-readiness-remediation-restored-2026-10-10.md` 明确「本文件是检查后方案，不授权实施」。
本轮因此停在**已复现、已定位、待授权**：`TechnicalAcceptance=NOT_PASSED`、`FormalPhaseApproval=NOT_GRANTED`、`ReleaseApproval=NOT_GRANTED`。
A（候选输入闭包）与 B（升级界面一致性）的最小修改范围已精确到文件与方法，具备立即实施条件。
