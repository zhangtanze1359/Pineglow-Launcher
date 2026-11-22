# OPEN-02 v4 第四轮独立复验交接 · 2026-11-22

**技术验收 PASS。** 基线复现、候选测试、相关回归、四道门槛和三轮历史探针均达到本轮判据。账户补全保存故障仍能触发，首次 `/api/state` 已将下载源写回 `bmclapi`，第二次请求的磁盘字节一致，凭据后端写入和删除均为 0。`FormalPhaseApproval=NOT_GRANTED`。

## 工作树与输入

从 T3 分配的干净工作树创建了 D 盘本地独立克隆，以满足项目的验收路径要求并隔离 Git 元数据。检出前后 HEAD 一致。验收数字来自下列工作树的本轮进程，历史交接与执行者记录用于确认输入和故障口径。 [创建记录](D:/Projects/_worktrees/pineglow-open02-verify4-20261122/verify-logs/00-clone.txt) · [开工原文](D:/Projects/_worktrees/pineglow-open02-verify4-20261122/verify-logs/00-preflight.txt)

```text
pwd
D:\Projects\_worktrees\pineglow-open02-verify4-20261122
git rev-parse --show-toplevel
D:/Projects/_worktrees/pineglow-open02-verify4-20261122
git rev-parse HEAD
274675f0aaf686833239811adc39684d5b02743c
git -c core.longpaths=true status --short --untracked-files=all -- . ":(exclude)verify-logs"
git diff --stat; git diff --cached --stat
```

开工状态与两个 diff 均无输出。PowerShell 5.1，.NET SDK 10.0.301，Node v24.19.0。PATH 前置 `C:\Windows\System32;C:\Windows`，`TEMP` 和 `TMP` 指向隔离树的 `.cache/tmp`。全部时间为 2026-11-22 UTC+08。

| 校验对象 | 独立实测 SHA-256，与简报一致 |
| --- | --- |
| v1 RED 测试 `03-test-final.cs` | `A01D4131357CDAF23E6608A163FC8B65E37C4BEE6BEF132A120783D7CC700D34` |
| v4 补丁 `22-candidate-final-v4.patch` | `15220EC7C5DB2999550C437D59393301DA9D3B874E86A01C34C6DACEF9CC2CC8` |
| v4 测试原件与复制件 | `0B8E2B9DFDF5A9F63CD7243A0601A0B984E16CFE52514C2D491459387A607F82` |
| 应用后 `AppConfig.cs` | `EF7AC244AEDB8D495570EF105B58135902285B505CD11E30B296A239F3BA9ED8` |
| 应用后 `ConfigStore.cs` | `2B8375FF2746FC3A0CFB8607E6D69317687A09710A92774BAE69EACC4FBEA9E5` |
| 应用后 `IConfigStore.cs` | `1F05159C63EF67CE0035F3C084F190C9BF528E225542684098319588EF6C5B72` |
| 应用后 `LocalApiService.Configuration.cs` | `897AD35EF9B9504DBD5FB3D9096F558647F25D7FAF013DBBFFA38C2BEDBFF683` |
| 应用后 `LocalApiService.Instances.cs` | `C78892DDE83FA38BE993B10C89968A3A971AC3AB76681B97A5074848D1253916` |

生产变更确认为五个指定文件，**+76/−6**。`git apply --check`、`git apply`、`git diff --check` 和收尾 `git apply --reverse --check` 均退出 0。 [测试复制件哈希](D:/Projects/_worktrees/pineglow-open02-verify4-20261122/verify-logs/01-test-hashes.txt) · [应用后哈希](D:/Projects/_worktrees/pineglow-open02-verify4-20261122/verify-logs/02-applied-hashes.txt) · [diff 统计](D:/Projects/_worktrees/pineglow-open02-verify4-20261122/verify-logs/02-diff-stat.txt) · [完整 diff](D:/Projects/_worktrees/pineglow-open02-verify4-20261122/verify-logs/02-diff.txt)

## 命令与独立结果

下列命令的工作目录均为上述 D 盘隔离树。完整参数、起止时间、实际退出码和原始日志绝对路径见 [命令记录](D:/Projects/_worktrees/pineglow-open02-verify4-20261122/verify-logs/commands.txt)。

| 检查 | 时间（UTC+08） | 预期与实测 | Exit | 原始输出 |
| --- | --- | --- | ---: | --- |
| RED，v1 对干净基线 | 20:08:53–20:09:06 | 一致，2 通过 / 10 失败 / 0 跳过，共 12 | 1（预期） | [RED](D:/Projects/_worktrees/pineglow-open02-verify4-20261122/verify-logs/01-red.txt:153) |
| 应用 v4 | 20:09:49 | 一致，5 个生产文件 +76/−6，哈希匹配 | 0 | [diff 统计](D:/Projects/_worktrees/pineglow-open02-verify4-20261122/verify-logs/02-diff-stat.txt) |
| GREEN，v4 定向 | 20:09:49–20:10:02 | 一致，17 通过 / 0 失败 / 0 跳过 | 0 | [GREEN](D:/Projects/_worktrees/pineglow-open02-verify4-20261122/verify-logs/03-green.txt) |
| 配置、状态与路由回归 | 20:10:02–20:10:06 | 一致，79 通过 / 0 失败 / 0 跳过 | 0 | [相关回归](D:/Projects/_worktrees/pineglow-open02-verify4-20261122/verify-logs/04-regression.txt) |
| 清单双向校验 | 20:10:06 | 一致，35 个 Node 测试文件、1 个 C# 项目 | 0 | [清单](D:/Projects/_worktrees/pineglow-open02-verify4-20261122/verify-logs/05-inventory.txt) |
| 四道门槛 | 20:10:06–20:11:43 | 一致，C# 1502/0/0，前端 508/0/0，CSS token 49，资源闭包通过 | 0 | [门槛](D:/Projects/_worktrees/pineglow-open02-verify4-20261122/verify-logs/06-gates.txt) · [闭包](D:/Projects/_worktrees/pineglow-open02-verify4-20261122/verify-logs/06b-closure.txt) |
| 第 1 轮生产存储探针 | 20:11:50–20:11:53 | 四个凭据场景、文件锁、全量保存反证均通过 | 0 | [第 1 轮探针](D:/Projects/_worktrees/pineglow-open02-verify4-20261122/verify-logs/07-first-production-probe.txt) |
| 第 2 轮旧 JSON 凭据探针 | 20:11:53–20:11:55 | `official` 对照与 `mcbbs` 的故障保留、恢复重试均通过 | 0 | [第 2 轮探针](D:/Projects/_worktrees/pineglow-open02-verify4-20261122/verify-logs/08-second-legacy-credential.txt) |
| 第 3 轮账户补全故障探针 | 20:11:55–20:11:56 | 直接读取及连续两次请求均满足 v4 口径 | 0 | [第 3 轮探针](D:/Projects/_worktrees/pineglow-open02-verify4-20261122/verify-logs/09-third-account-only-save-fault.txt) |

RED 的 10 个失败均为产品行为断言，其中 HTTP 下载源断言 7 条、写回次数断言 3 条。原始输出分别出现 `Expected: "bmclapi"` / `Actual: "mcbbs"` 和 `Expected: 1` / `Actual: 0`。四道门槛出现预期的基线过期告警，`dcdf056` 对 `274675f`。单独复跑资源闭包扫描两个入口、20 个文件、36 条本地引用，缺失 0，exit 0。 [RED 断言](D:/Projects/_worktrees/pineglow-open02-verify4-20261122/verify-logs/01-red.txt:38) · [基线告警](D:/Projects/_worktrees/pineglow-open02-verify4-20261122/verify-logs/06-gates.txt) · [闭包原文](D:/Projects/_worktrees/pineglow-open02-verify4-20261122/verify-logs/06b-closure.txt)

```powershell
# RED 和 GREEN 使用同一命令，分别复制原样 v1 和 v4 测试。
dotnet test dev/PineglowLauncher.Tests/Pineglow.Launcher.Tests.csproj -c Release --filter "FullyQualifiedName~DownloadSourceHttpMigrationTests"
git apply --check "D:/Projects/松光启动器/docs/evidence/2026-11-22-open02/22-candidate-final-v4.patch"
git apply "D:/Projects/松光启动器/docs/evidence/2026-11-22-open02/22-candidate-final-v4.patch"
git diff --check
git diff --stat
dotnet test dev/PineglowLauncher.Tests/Pineglow.Launcher.Tests.csproj -c Release --filter "FullyQualifiedName~DownloadSourceMigrationTests|FullyQualifiedName~ConfigBackupRotationTests|FullyQualifiedName~ConfigPreservationTests|FullyQualifiedName~ConfigDefaultIdentityTests|FullyQualifiedName~NewsFakeIpConfigTests|FullyQualifiedName~HostDependenciesTests|FullyQualifiedName~LocalApiServiceSplitTests|FullyQualifiedName~RouteReachabilityTests|FullyQualifiedName~ApiRouteInventoryTests"
node scripts/run-inventory-tests.cjs --check
node scripts/gates.cjs
node scripts/check-ui-resource-closure.cjs
dotnet run --project .cache/open02-config-probe/Probe.csproj -c Release -p:BuildProjectReferences=false
dotnet run --project .cache/open02-config-probe/Probe.csproj -c Release --no-build -- --legacy-credential
dotnet run --project .cache/open02-config-probe/Probe.csproj -c Release --no-build -- --account-only-save-fault
```

## 三轮生产路径探针

探针使用生产 `ProductionConfigStore`、实际回环 `GET /api/state`、隔离 JSON 和合成凭据后端，原生凭据调用均为 0。复用第三轮独立探针源码，本轮在探针副本补充第二次请求的零删除、零拒绝和磁盘字节稳定断言。探针装载的 1.0.5 应用 DLL 与本轮候选 Release DLL 的 SHA-256 相同，均为 `8A087076084144601E18D99A9C0397D8230A77FCC169B27214244D13E84284F0`。 [探针源码](D:/Projects/_worktrees/pineglow-open02-verify4-20261122/.cache/open02-config-probe/Program.cs) · [探针断言 diff](D:/Projects/_worktrees/pineglow-open02-verify4-20261122/verify-logs/07-probe-oracle-diff.txt) · [源码哈希](D:/Projects/_worktrees/pineglow-open02-verify4-20261122/verify-logs/07-probe-source-hashes.txt) · [程序集哈希](D:/Projects/_worktrees/pineglow-open02-verify4-20261122/verify-logs/07-probe-assembly-hashes.txt)

第 1 轮的四种场景每次都返回 HTTP 200，均保留合成 CurseForge 密钥，请求窗口后端写入和删除均为 0。`official` 控制组磁盘字节一致，其余三组把 `mcbbs` 落为 `bmclapi`。额外独占 `config.json.tmp` 时，HTTP 200，当次使用 `bmclapi`，磁盘保留 `mcbbs` 且字节一致。锁释放后的显式全量保存反证为 `keyPresent=False; curseForgeDeletes=1`。 [第 1 轮原文](D:/Projects/_worktrees/pineglow-open02-verify4-20261122/verify-logs/07-first-production-probe.txt:3)

第 2 轮 `mcbbs` 组首次转存故障时为 `firstHTTP=200; firstSource=bmclapi`，`activeConfigContainsKey=True; keyInBackend=False; firstDiskSource=mcbbs; firstBytesUnchanged=True`。恢复后端后，第二次 HTTP 200，`secondKeyConfigured=True; keyInBackendAfterRecovery=True; secondDiskSource=bmclapi; activeConfigContainsKeyAfterRecovery=False`。`official` 对照同样保留并重试转存。 [第 2 轮决定性原始行](D:/Projects/_worktrees/pineglow-open02-verify4-20261122/verify-logs/08-second-legacy-credential.txt:4)

第 3 轮的删除故障在首次请求期间仍启用。直接加载生成一个离线账户、命中一次合成删除拒绝，待转存标志为 `False`。首次请求把 `bmclapi` 落盘。第二次请求保持故障启用，但未再次触发全量保存，文件与第一次请求后字节一致。关键原始字段如下。 [第 3 轮决定性原始行](D:/Projects/_worktrees/pineglow-open02-verify4-20261122/verify-logs/09-third-account-only-save-fault.txt:3)

```text
case=account-only-save-fault-direct; precondition=True; initialAccounts=0; generatedAccounts=1; legacyCredentials=False; secretMigrationPending=False; rejectedDeletes=1
case=account-only-save-fault-state; request=first; HTTP=200; responseSource=bmclapi; effectiveSource=bmclapi; diskSource=bmclapi; unchangedBytes=False; unchangedFromPrevious=False; writes=0; deletes=4; rejectedDeletes=1; nativeCredentialCalls=0
case=account-only-save-fault-state; request=second; HTTP=200; responseSource=bmclapi; effectiveSource=bmclapi; diskSource=bmclapi; unchangedBytes=False; unchangedFromPrevious=True; writes=0; deletes=0; rejectedDeletes=0; nativeCredentialCalls=0
accountOnlyProbe=PASS
```

## 代码审查与交付边界

- `legacySecretMigration` 从 `false` 起始，只在旧单套 AI 配置迁移、从 JSON 恢复 `MicrosoftRefreshToken`、从 JSON 恢复 `CurseForgeApiKey` 三处分支置位。`EnsureAccounts` 的 `flag2` 和 SecretStore 间账户 token 迁移只参与原有全量保存条件。`SecretMigrationPending` 的唯一赋值位于保存异常分支内，受 `legacySecretMigration` 限定。 [三个旧字段分支](D:/Projects/_worktrees/pineglow-open02-verify4-20261122/dev/PineglowLauncher/Pineglow.Launcher.Services/ConfigStore.cs:114) · [账户分支](D:/Projects/_worktrees/pineglow-open02-verify4-20261122/dev/PineglowLauncher/Pineglow.Launcher.Services/ConfigStore.cs:152) · [异常分支](D:/Projects/_worktrees/pineglow-open02-verify4-20261122/dev/PineglowLauncher/Pineglow.Launcher.Services/ConfigStore.cs:200)
- `SecretMigrationPending` 为 `[JsonIgnore]` 的运行期属性。生产 `SaveConfigOnly` 按标志保护主配置，允许写回时经 `WriteConfigFile` 原子替换。`LoadConfig` 在同一把 `ConfigGate` 内读取、规范化并尝试写回，异常时当次仍按规范值生效。首次正常迁移及 16 个并发状态请求均有 `ConfigOnlySaveCount == 1`、`SaveCount == 0` 的断言。 [运行期属性](D:/Projects/_worktrees/pineglow-open02-verify4-20261122/dev/PineglowLauncher/Pineglow.Launcher.Models/AppConfig.cs:65) · [生产接线](D:/Projects/_worktrees/pineglow-open02-verify4-20261122/dev/PineglowLauncher/Pineglow.Launcher.Services/IConfigStore.cs:49) · [写回保护](D:/Projects/_worktrees/pineglow-open02-verify4-20261122/dev/PineglowLauncher/Pineglow.Launcher.Services/ConfigStore.cs:254) · [状态读取链路](D:/Projects/_worktrees/pineglow-open02-verify4-20261122/dev/PineglowLauncher/Pineglow.Launcher.Services/LocalApiService.Configuration.cs:86) · [写回计数断言](D:/Projects/_worktrees/pineglow-open02-verify4-20261122/dev/PineglowLauncher.Tests/DownloadSourceHttpMigrationTests.cs:86)
- 三轮历史回归均由候选测试和独立探针复核。第四轮新增的两个用例分别直接验证存储层和真实 HTTP 的账户补全故障，共计 17/17。可选的无条件置位变异未执行。 [已入库凭据回归](D:/Projects/_worktrees/pineglow-open02-verify4-20261122/dev/PineglowLauncher.Tests/DownloadSourceHttpMigrationTests.cs:135) · [旧凭据重试回归](D:/Projects/_worktrees/pineglow-open02-verify4-20261122/dev/PineglowLauncher.Tests/DownloadSourceHttpMigrationTests.cs:189) · [账户补全故障回归](D:/Projects/_worktrees/pineglow-open02-verify4-20261122/dev/PineglowLauncher.Tests/DownloadSourceHttpMigrationTests.cs:290)

本轮在隔离树保留原样候选、测试、探针和原始证据。收尾核对暂存区为空，命名输入的哈希保持一致。真实 Windows 凭据管理器、Avalonia/WebView2 GUI 和发布包冷启动未验证。合入、销项和发布审批均留给编排方。 [收尾哈希](D:/Projects/_worktrees/pineglow-open02-verify4-20261122/verify-logs/10-final-inputs.txt) · [收尾状态](D:/Projects/_worktrees/pineglow-open02-verify4-20261122/verify-logs/10-final-status.txt)

完整 Git 状态包含本轮生成的 .cache 探针构建和测试临时文件。全量枚举提示一条 .cache/tmp 的 mods 路径无法打开，退出 0。另一次带排除条件的状态查询因命令转义错误退出 128，当即修正并重跑，退出 0。 [状态查询转义错误](D:/Projects/_worktrees/pineglow-open02-verify4-20261122/verify-logs/10b-candidate-status.txt) · [修正后的候选状态](D:/Projects/_worktrees/pineglow-open02-verify4-20261122/verify-logs/10c-candidate-status.txt)

RESULT: PASS
