# OPEN-02 v3 第三轮独立复验交接 · 2026-11-22

**结论：FAIL，OPEN-02 暂不放行。** 指定的测试、四道门槛和前两轮生产存储探针均符合预期。代码审查及补充探针确认，无待转存旧凭据时，账户补全触发的全量保存失败也会将 `SecretMigrationPending` 置为 `true`。这使原本可独立进行的下载源写回被跳过，违反本轮标志置位条件。 `FormalPhaseApproval=NOT_GRANTED`。

## 输入与环境

```text
pwd
D:\Projects\_worktrees\pineglow-open02-verify3-20261122
git rev-parse --show-toplevel
D:/Projects/_worktrees/pineglow-open02-verify3-20261122
git rev-parse HEAD
274675f0aaf686833239811adc39684d5b02743c
git -c core.longpaths=true status --short --untracked-files=all
```

开工状态和暂存区输出为空。使用 PowerShell 5.1、.NET SDK 10.0.301 和 Node v24.19.0。PATH 前置 `C:\Windows\System32;C:\Windows`，TEMP/TMP 指向隔离树的 `.cache/tmp`。本报告使用 2026-11-22 当日 UTC+08 时间。所有运行数字均来自上面工作树的本轮进程。 [开工原始输出](D:/Projects/_worktrees/pineglow-open02-verify3-20261122/verify-logs/00-preflight.txt) · [完整命令、工作目录、起止时间和退出码](D:/Projects/_worktrees/pineglow-open02-verify3-20261122/verify-logs/commands.txt)

| SHA-256 校验对象 | 本轮实测，与简报一致 |
| --- | --- |
| v1 RED 测试 | `A01D4131357CDAF23E6608A163FC8B65E37C4BEE6BEF132A120783D7CC700D34` |
| v3 补丁 | `1DEA39D4D80961C3DBA8BEB94011B8EBDCC729B331E8FDBC06E78B4307B7FD07` |
| v3 测试原件与工作树复制件 | `9357B0C2108A62B575FE3ECC5CDC1B7FDD843B5323F3EF1100FEC76AE8CA66F4` |
| 应用后 AppConfig.cs | `EF7AC244AEDB8D495570EF105B58135902285B505CD11E30B296A239F3BA9ED8` |
| 应用后 ConfigStore.cs | `D1D1E00900DC67621061F118A2A3A29A57F58CE700B23BB3A866E2D1C10B5B92` |
| 应用后 IConfigStore.cs | `1F05159C63EF67CE0035F3C084F190C9BF528E225542684098319588EF6C5B72` |
| 应用后 LocalApiService.Configuration.cs | `897AD35EF9B9504DBD5FB3D9096F558647F25D7FAF013DBBFFA38C2BEDBFF683` |
| 应用后 LocalApiService.Instances.cs | `C78892DDE83FA38BE993B10C89968A3A971AC3AB76681B97A5074848D1253916` |

生产补丁只命中指定五个文件，+67/−6。补丁检查、应用、`git diff --check` 均退出 0。探针加载的 1.0.5 应用程序集与本轮 v3 Release 构建 SHA-256 相同，并与基线程序集不同。 [补丁统计](D:/Projects/_worktrees/pineglow-open02-verify3-20261122/verify-logs/02d-diff-stat.txt) · [应用后哈希](D:/Projects/_worktrees/pineglow-open02-verify3-20261122/verify-logs/02e-applied-hashes.txt) · [v3 与探针程序集哈希](D:/Projects/_worktrees/pineglow-open02-verify3-20261122/verify-logs/07c-candidate-assembly.txt) · [基线程序集哈希](D:/Projects/_worktrees/pineglow-open02-verify3-20261122/verify-logs/01c-baseline-assembly.txt)

## 独立执行结果

| 验证 | 时间（UTC+08） | 预期与实测 | Exit | 原始输出 |
| --- | --- | --- | ---: | --- |
| RED，v1 测试对干净基线 | 19:55:27–19:55:41 | 一致，2 通过 / 10 失败 / 0 跳过，共 12 | 1（预期） | [01-red.txt](D:/Projects/_worktrees/pineglow-open02-verify3-20261122/verify-logs/01-red.txt) |
| 旧凭据探针，对照干净基线 | 19:56:47–19:56:50 | 一致，`official` 与 `mcbbs` 都保留旧密钥，后端恢复后可重试转存 | 0 | [01b-legacy-baseline.txt](D:/Projects/_worktrees/pineglow-open02-verify3-20261122/verify-logs/01b-legacy-baseline.txt) |
| v3 补丁应用与核对 | 19:57:17 | 一致，五个文件 +67/−6，哈希匹配 | 0 | [02b-apply.txt](D:/Projects/_worktrees/pineglow-open02-verify3-20261122/verify-logs/02b-apply.txt) · [02e-applied-hashes.txt](D:/Projects/_worktrees/pineglow-open02-verify3-20261122/verify-logs/02e-applied-hashes.txt) |
| GREEN，v3 定向 | 19:57:17–19:57:28 | 一致，15 通过 / 0 失败 / 0 跳过 | 0 | [03-green.txt](D:/Projects/_worktrees/pineglow-open02-verify3-20261122/verify-logs/03-green.txt) |
| 配置、状态与路由回归 | 19:57:28–19:57:32 | 一致，79 通过 / 0 失败 / 0 跳过 | 0 | [04-regression.txt](D:/Projects/_worktrees/pineglow-open02-verify3-20261122/verify-logs/04-regression.txt) |
| 清单双向校验 | 19:57:32 | 一致，35 个 Node 测试文件，1 个 C# 测试项目 | 0 | [05-inventory.txt](D:/Projects/_worktrees/pineglow-open02-verify3-20261122/verify-logs/05-inventory.txt) |
| 四道门槛 | 19:57:32–19:59:09 | 一致，C# 1500/0/0，前端 508/0/0，CSS token 49 个，资源闭包通过 | 0 | [06-gates.txt](D:/Projects/_worktrees/pineglow-open02-verify3-20261122/verify-logs/06-gates.txt) · [闭包摘要](D:/Projects/_worktrees/pineglow-open02-verify3-20261122/verify-logs/06b-closure.txt) |
| 第 1 轮生产存储探针 | 19:59:31–19:59:33 | 四个指定场景、文件锁及显式全量保存反证均满足断言 | 0 | [07b-production-store-probe.txt](D:/Projects/_worktrees/pineglow-open02-verify3-20261122/verify-logs/07b-production-store-probe.txt) |
| 第 2 轮旧凭据探针，对照 v3 | 19:59:33–19:59:35 | 两个下载源的保留与重试断言均通过 | 0 | [08-legacy-credential-v3.txt](D:/Projects/_worktrees/pineglow-open02-verify3-20261122/verify-logs/08-legacy-credential-v3.txt) |
| 补充探针，仅账户补全保存失败 | 19:59:35–19:59:36 | 预期待转存标志为 `False` 且可写回下载源。实测标志为 `True`，故障期间磁盘保留 `mcbbs` | 1 | [09-account-only-save-fault.txt](D:/Projects/_worktrees/pineglow-open02-verify3-20261122/verify-logs/09-account-only-save-fault.txt) |

RED 的 10 个失败均为断言。7 个用例在 HTTP 下载源检查失败，3 个空值用例在持久化次数检查失败。代表性原文为 `Expected: "bmclapi"` / `Actual: "mcbbs"`，以及 `Expected: 1` / `Actual: 0`。门槛有预期的 `docs/baseline.md` 过期告警，记录的 `dcdf056` 与实际 `274675f` 不同。独立闭包命令扫描两个入口、20 个文件、36 条本地引用，缺失 0。 [RED 报错](D:/Projects/_worktrees/pineglow-open02-verify3-20261122/verify-logs/01-red.txt) · [门槛原始输出](D:/Projects/_worktrees/pineglow-open02-verify3-20261122/verify-logs/06-gates.txt) · [闭包原始输出](D:/Projects/_worktrees/pineglow-open02-verify3-20261122/verify-logs/06b-closure.txt)

在上述工作目录用 PowerShell 执行了以下主要命令。RED 用原样 v1 测试，GREEN 用原样 v3 测试。 [实跑命令原始记录](D:/Projects/_worktrees/pineglow-open02-verify3-20261122/verify-logs/commands.txt)

```powershell
dotnet test dev/PineglowLauncher.Tests/Pineglow.Launcher.Tests.csproj -c Release --filter "FullyQualifiedName~DownloadSourceHttpMigrationTests"
dotnet run --project .cache/open02-config-probe/Probe.csproj -c Release -p:BuildProjectReferences=false -- --legacy-credential --baseline
git apply --check "D:/Projects/松光启动器/docs/evidence/2026-11-22-open02/18-candidate-final-v3.patch"
git apply "D:/Projects/松光启动器/docs/evidence/2026-11-22-open02/18-candidate-final-v3.patch"
git diff --check
git diff --stat
dotnet test dev/PineglowLauncher.Tests/Pineglow.Launcher.Tests.csproj -c Release --filter "FullyQualifiedName~DownloadSourceHttpMigrationTests"
dotnet test dev/PineglowLauncher.Tests/Pineglow.Launcher.Tests.csproj -c Release --filter "FullyQualifiedName~DownloadSourceMigrationTests|FullyQualifiedName~ConfigBackupRotationTests|FullyQualifiedName~ConfigPreservationTests|FullyQualifiedName~ConfigDefaultIdentityTests|FullyQualifiedName~NewsFakeIpConfigTests|FullyQualifiedName~HostDependenciesTests|FullyQualifiedName~LocalApiServiceSplitTests|FullyQualifiedName~RouteReachabilityTests|FullyQualifiedName~ApiRouteInventoryTests"
node scripts/run-inventory-tests.cjs --check
node scripts/gates.cjs
node scripts/check-ui-resource-closure.cjs
dotnet run --project .cache/open02-config-probe/Probe.csproj -c Release -p:BuildProjectReferences=false
dotnet run --project .cache/open02-config-probe/Probe.csproj -c Release --no-build -- --legacy-credential
dotnet run --project .cache/open02-config-probe/Probe.csproj -c Release --no-build -- --account-only-save-fault
```

## 前两轮阻断均已修复

探针使用实际回环 `GET /api/state`、生产 `ProductionConfigStore`、隔离 JSON 和合成凭据后端。原生凭据调用为 0。复用了前轮独立探针，增加首次及恢复后的磁盘下载源、实际镜像与合成密钥值校验，并新增账户补全故障场景。 [本轮探针源码](D:/Projects/_worktrees/pineglow-open02-verify3-20261122/.cache/open02-config-probe/Program.cs) · [探针项目](D:/Projects/_worktrees/pineglow-open02-verify3-20261122/.cache/open02-config-probe/Probe.csproj) · [基线探针源文件归档哈希](D:/Projects/_worktrees/pineglow-open02-verify3-20261122/verify-logs/01d-baseline-probe-archive.txt)

| 已入库凭据场景 | HTTP | 实际生效源 | 磁盘源 | 后端写 / 删 |
| --- | ---: | --- | --- | --- |
| `official` 控制，AI 读异常，启用删除故障 | 200 | `official` | `official`，字节不变 | 0 / 0 |
| `mcbbs` 正常 | 200 | `bmclapi` | `bmclapi` | 0 / 0 |
| `mcbbs`，AI 读异常 | 200 | `bmclapi` | `bmclapi` | 0 / 0 |
| `mcbbs`，AI 读异常并启用删除故障 | 200 | `bmclapi` | `bmclapi` | 0 / 0 |
| `mcbbs`，AI 读异常且 `config.json.tmp` 独占 | 200 | `bmclapi` | `mcbbs`，字节不变 | 0 / 0 |

表中五个场景的合成 CurseForge 凭据均保留。锁释放后的显式全量保存反证记录 `precondition=True; keyPresent=False; curseForgeDeletes=1`。 [第一轮探针原文](D:/Projects/_worktrees/pineglow-open02-verify3-20261122/verify-logs/07b-production-store-probe.txt)

v3 的 `legacy-transfer-fault-mcbbs` 与独立基线对照均在首次故障后保留主配置中的旧密钥，备份及凭据后端无该密钥。v3 首次 HTTP 200，回传与实际生效均为 `bmclapi`，磁盘保留 `mcbbs`，文件字节不变。恢复后第二次请求仍为 HTTP 200，`secondKeyConfigured=True`，后端获得原合成值，磁盘源变为 `bmclapi`，主配置移除旧字段。基线在两次请求均使用 `mcbbs`，凭据保留及重试行为与 v3 一致。 `official` 对照通过。 [基线探针](D:/Projects/_worktrees/pineglow-open02-verify3-20261122/verify-logs/01b-legacy-baseline.txt) · [v3 决定性原始行](D:/Projects/_worktrees/pineglow-open02-verify3-20261122/verify-logs/08-legacy-credential-v3.txt:4)

## 本轮阻断 · 账户补全失败误触发待转存保护

`SecretMigrationPending` 是 `internal` 且带 `[JsonIgnore]`。它只有一处赋值，但赋值条件覆盖整个 `LoadAsync` 的迁移保存异常。缺少账户列表时，`EnsureAccounts` 生成离线账户并返回 `true`，也会使 `flag || flag2` 进入全量保存。全量保存中无关的 `AiApiKey` 删除抛错后，同一 `catch` 将标志置为 `true`。随后下载源调用 `SaveConfigOnlyAsync`，被该标志拦住。 [账户补全](D:/Projects/_worktrees/pineglow-open02-verify3-20261122/dev/PineglowLauncher/Pineglow.Launcher.Services/AccountService.cs:20) · [合并迁移条件](D:/Projects/_worktrees/pineglow-open02-verify3-20261122/dev/PineglowLauncher/Pineglow.Launcher.Services/ConfigStore.cs:184) · [保存异常与置位](D:/Projects/_worktrees/pineglow-open02-verify3-20261122/dev/PineglowLauncher/Pineglow.Launcher.Services/ConfigStore.cs:195) · [持久化保护](D:/Projects/_worktrees/pineglow-open02-verify3-20261122/dev/PineglowLauncher/Pineglow.Launcher.Services/ConfigStore.cs:245) · [全量保存的 AI 凭据删除](D:/Projects/_worktrees/pineglow-open02-verify3-20261122/dev/PineglowLauncher/Pineglow.Launcher.Services/ConfigStore.cs:272)

补充探针的输入是 `mcbbs`、空账户列表、已有默认实例，且配置无旧 CurseForge 密钥、微软刷新令牌及旧 AI 配置。合成后端无条目，读取正常，仅拒绝旧 AI 凭据删除。直接加载确认账户补全和故障均发生。随后连续两次请求均正常使用 `bmclapi`，主配置字节未变，仍为 `mcbbs`。解除合成删除故障后，同一位置第三次请求完成落盘。下列为决定性原始行。 [补充探针源码](D:/Projects/_worktrees/pineglow-open02-verify3-20261122/.cache/open02-config-probe/Program.cs:70) · [补充探针完整输出](D:/Projects/_worktrees/pineglow-open02-verify3-20261122/verify-logs/09-account-only-save-fault.txt:3)

```text
case=account-only-save-fault-direct; precondition=True; initialAccounts=0; generatedAccounts=1; legacyCredentials=False; secretMigrationPending=True; rejectedDeletes=1
case=account-only-save-fault-state; HTTP=200; responseSource=bmclapi; effectiveSource=bmclapi; diskSource=mcbbs; unchangedBytes=True; writes=0; rejectedDeletes=1; nativeCredentialCalls=0
case=account-only-save-fault-state; HTTP=200; responseSource=bmclapi; effectiveSource=bmclapi; diskSource=mcbbs; unchangedBytes=True; writes=0; rejectedDeletes=1; nativeCredentialCalls=0
case=account-only-save-fault-state; HTTP=200; responseSource=bmclapi; effectiveSource=bmclapi; diskSource=bmclapi; unchangedBytes=False; writes=0; rejectedDeletes=0; nativeCredentialCalls=0
accountOnlyProbe=FAIL
```

分类为**产品行为不符合验收条件**。夹具按预期开启与解除删除异常，探针退出 1 来自验收断言。旧有账户补全路径原先就可能全量保存失败。本轮新增影响是该异常被当成旧凭据待转存，从而阻止本可单独执行的下载源写回。两轮历史凭据丢失均未在指定场景中复现。

## 其余代码审查与现场

- 标志位于 `AppConfig` 且不序列化。置位时 config-only 路径跳过整包重写。正常路径在 `LocalApiService.ConfigGate` 内读取、规范化和写回，生产覆盖只写配置。首次迁移 `ConfigOnlySaveCount == 1`、`SaveCount == 0`。重复请求、重新加载以及 16 次并发分别断言相应保存次数和实际生效镜像。 [标志声明](D:/Projects/_worktrees/pineglow-open02-verify3-20261122/dev/PineglowLauncher/Pineglow.Launcher.Models/AppConfig.cs:65) · [读取和写回](D:/Projects/_worktrees/pineglow-open02-verify3-20261122/dev/PineglowLauncher/Pineglow.Launcher.Services/LocalApiService.Configuration.cs:86) · [生产接线](D:/Projects/_worktrees/pineglow-open02-verify3-20261122/dev/PineglowLauncher/Pineglow.Launcher.Services/IConfigStore.cs:49) · [写回计数回归](D:/Projects/_worktrees/pineglow-open02-verify3-20261122/dev/PineglowLauncher.Tests/DownloadSourceHttpMigrationTests.cs:30)
- 第一轮回归有合成读取故障前置断言、保存后端操作数断言及显式全量保存反证。第二轮两条回归分别经存储层和真实 HTTP，断言故障时主文件同时保留旧密钥和旧下载源，恢复后密钥入库且下载源落盘。去除保护会直接触及这些文件内容断言。本轮使用独立探针复核，未进行可选 guard 变异。 [已入库凭据回归](D:/Projects/_worktrees/pineglow-open02-verify3-20261122/dev/PineglowLauncher.Tests/DownloadSourceHttpMigrationTests.cs:135) · [存储层回归](D:/Projects/_worktrees/pineglow-open02-verify3-20261122/dev/PineglowLauncher.Tests/DownloadSourceHttpMigrationTests.cs:189) · [HTTP 回归](D:/Projects/_worktrees/pineglow-open02-verify3-20261122/dev/PineglowLauncher.Tests/DownloadSourceHttpMigrationTests.cs:243)
- 主工作区、执行树、历史复验树及候选输入均按只读处理。隔离工作树保留原样候选、探针和日志，Git 暂存区为空。未修产品或测试，未提交、合并、推送、打 tag 或发布。真实 Windows 凭据管理器、Avalonia/WebView2 GUI 和发布包冷启动未验证。 [收尾状态及哈希](D:/Projects/_worktrees/pineglow-open02-verify3-20261122/verify-logs/10-final-state.txt) · [完整 Git 状态](D:/Projects/_worktrees/pineglow-open02-verify3-20261122/verify-logs/10b-final-status.txt)

RESULT: FAIL
