# OPEN-02 v2 第二轮独立复验 · 2026-11-22

**结论：FAIL，OPEN-02 暂不放行。** v2 在指定的四种生产存储场景中保留了已入库的合成凭据，验收测试及四道门槛均符合预期。独立代码审查和补充探针发现另一处产品回归：旧 CurseForge 密钥仅存在于 `config.json`、凭据转存暂时失败时，下载源迁移会先移除主配置中的密钥，导致凭据后端恢复后也无法自动重试转存。 `FormalPhaseApproval=NOT_GRANTED`。

## 复验输入与工作目录

执行目录为独立新建的 `D:/Projects/_worktrees/pineglow-open02-verify2-20261122`。开工命令及原始输出如下，末两条命令输出为空。使用 PowerShell 5.1、.NET SDK 10.0.301、Node v24.19.0，`TEMP` / `TMP` 指向此工作树内的 `.cache/tmp`，PATH 前置 Windows 系统目录。第一次创建工作树因历史文档路径过长退出 128，Git 清理了未完成的检出。仅对第二次创建命令加 `-c core.longpaths=true` 后成功。所有验收测试和探针命令在 D 盘隔离树运行，补充探针的基线应用程序集在本任务初始分配的干净 T3 工作树编译。应用版本为 1.0.5。 [开工取证](D:/Projects/_worktrees/pineglow-open02-verify2-20261122/verify-logs/00-preflight.txt) · [基线编译命令](D:/Projects/_worktrees/pineglow-open02-verify2-20261122/verify-logs/baseline-build-command.txt) · [基线编译原始输出](D:/Projects/_worktrees/pineglow-open02-verify2-20261122/verify-logs/09-baseline-build.txt)

```text
pwd
D:\Projects\_worktrees\pineglow-open02-verify2-20261122
git rev-parse --show-toplevel
D:/Projects/_worktrees/pineglow-open02-verify2-20261122
git rev-parse HEAD
274675f0aaf686833239811adc39684d5b02743c
git -c core.longpaths=true status --short --untracked-files=all
git diff --stat
```

三个输入及复制到工作树中的测试文件均按原始字节校验。生产补丁应用后为四个文件 +52/−6，`git diff --check` 退出 0。 [输入哈希](D:/Projects/_worktrees/pineglow-open02-verify2-20261122/verify-logs/00-preflight.txt) · [应用与输出哈希](D:/Projects/_worktrees/pineglow-open02-verify2-20261122/verify-logs/02-apply.txt)

| 校验对象 | 实测 SHA-256，与简报一致 |
| --- | --- |
| v1 测试（RED） | `A01D4131357CDAF23E6608A163FC8B65E37C4BEE6BEF132A120783D7CC700D34` |
| v2 补丁 | `CB0E83F936F6C0A4D2ABEFD11FBA4BFA089237FBF71CCB5D193136AE4DB991AC` |
| v2 测试 | `00CA509F3403ADEBCE36F9942F1C48451E2FD996DC6DE1132C060182299F6B2F` |
| 应用后 Configuration | `897AD35EF9B9504DBD5FB3D9096F558647F25D7FAF013DBBFFA38C2BEDBFF683` |
| 应用后 Instances | `C78892DDE83FA38BE993B10C89968A3A971AC3AB76681B97A5074848D1253916` |
| 应用后 IConfigStore | `1F05159C63EF67CE0035F3C084F190C9BF528E225542684098319588EF6C5B72` |
| 应用后 ConfigStore | `479798AA3270445E0C7476CAB1BE165D0B5BAB478623AD6B1237BF8E57D78CD5` |

## 本轮从头实跑

时间为 2026-11-22，UTC+08。下表中的计数来自本轮进程输出。完整命令、毫秒精度时间、工作目录和退出码见记录。 [测试命令](D:/Projects/_worktrees/pineglow-open02-verify2-20261122/verify-logs/commands.txt) · [探针命令](D:/Projects/_worktrees/pineglow-open02-verify2-20261122/verify-logs/probe-command.txt)

| 检查 | 时间 | 预期与实测 | exit | 原始输出 |
| --- | --- | --- | ---: | --- |
| RED，v1 测试对干净基线 | 19:39:41–19:39:54 | 2 通过 / 10 失败 / 0 跳过，共 12 | 1（预期） | [01-red.txt](D:/Projects/_worktrees/pineglow-open02-verify2-20261122/verify-logs/01-red.txt) |
| 应用 v2 并核对哈希 | 19:40:20 | 四个生产文件 +52/−6，四个文件哈希符合预期 | 0 | [02-apply.txt](D:/Projects/_worktrees/pineglow-open02-verify2-20261122/verify-logs/02-apply.txt) |
| GREEN，v2 定向 | 19:40:31–19:40:44 | 13 通过 / 0 失败 / 0 跳过 | 0 | [03-green.txt](D:/Projects/_worktrees/pineglow-open02-verify2-20261122/verify-logs/03-green.txt) |
| 配置、状态与路由回归 | 19:40:44–19:40:48 | 79 通过 / 0 失败 / 0 跳过 | 0 | [04-regression.txt](D:/Projects/_worktrees/pineglow-open02-verify2-20261122/verify-logs/04-regression.txt) |
| 清单双向校验 | 19:40:48 | 35 个 Node 测试文件和 1 个 C# 项目，磁盘发现一致 | 0 | [05-inventory.txt](D:/Projects/_worktrees/pineglow-open02-verify2-20261122/verify-logs/05-inventory.txt) |
| 四道门槛 | 19:40:48–19:42:24 | C# 1498 通过 / 0 失败 / 0 跳过，前端 508 通过 / 0 失败 / 0 跳过，CSS token 49 个，资源闭包通过 | 0 | [06-gates.txt](D:/Projects/_worktrees/pineglow-open02-verify2-20261122/verify-logs/06-gates.txt) |
| 指定生产存储探针及额外文件锁 | 19:43:59–19:44:03 | 四种指定场景、锁文件场景及全量保存反证均满足断言 | 0 | [07b-production-store-probe.txt](D:/Projects/_worktrees/pineglow-open02-verify2-20261122/verify-logs/07b-production-store-probe.txt) |
| 旧凭据转存故障，v2 | 19:44:03–19:44:04 | 预期两组均保留凭据并在后端恢复后成功转存。实测 `official` 通过，`mcbbs` 失败 | 1 | [08b-legacy-credential-candidate.txt](D:/Projects/_worktrees/pineglow-open02-verify2-20261122/verify-logs/08b-legacy-credential-candidate.txt) |
| 旧凭据转存故障，干净基线 | 19:44:04–19:44:05 | 两组均保留凭据，后端恢复后均成功转存 | 0 | [10b-legacy-credential-baseline.txt](D:/Projects/_worktrees/pineglow-open02-verify2-20261122/verify-logs/10b-legacy-credential-baseline.txt) |

```powershell
# RED 用原样 v1 测试，GREEN 用原样 v2 测试
dotnet test dev/PineglowLauncher.Tests/Pineglow.Launcher.Tests.csproj -c Release --filter "FullyQualifiedName~DownloadSourceHttpMigrationTests"
git apply --check "D:/Projects/松光启动器/docs/evidence/2026-11-22-open02/14-candidate-final-v2.patch"
git apply "D:/Projects/松光启动器/docs/evidence/2026-11-22-open02/14-candidate-final-v2.patch"
dotnet test dev/PineglowLauncher.Tests/Pineglow.Launcher.Tests.csproj -c Release --filter "FullyQualifiedName~DownloadSourceHttpMigrationTests"
dotnet test dev/PineglowLauncher.Tests/Pineglow.Launcher.Tests.csproj -c Release --filter "FullyQualifiedName~DownloadSourceMigrationTests|FullyQualifiedName~ConfigBackupRotationTests|FullyQualifiedName~ConfigPreservationTests|FullyQualifiedName~ConfigDefaultIdentityTests|FullyQualifiedName~NewsFakeIpConfigTests|FullyQualifiedName~HostDependenciesTests|FullyQualifiedName~LocalApiServiceSplitTests|FullyQualifiedName~RouteReachabilityTests|FullyQualifiedName~ApiRouteInventoryTests"
node scripts/run-inventory-tests.cjs --check
node scripts/gates.cjs
dotnet run --project .cache/open02-config-probe/Probe.csproj -c Release -p:BuildProjectReferences=false
dotnet run --project .cache/open02-config-probe/Probe.csproj -c Release --no-build -- --legacy-credential
dotnet .cache/open02-probe-baseline/VerifyHost.dll --legacy-credential
```

RED 为 7 条 HTTP 下载源断言和 3 条写回次数断言。典型原始输出为 `Expected: "bmclapi"` / `Actual: "mcbbs"` 与 `Expected: 1` / `Actual: 0`。还原、编译和监听均完成，失败由断言触发。四道门槛提示历史基线记录的 `dcdf056` 与当前 `274675f` 不同，四项均 PASS，脚本 exit 0。 [RED 原始输出](D:/Projects/_worktrees/pineglow-open02-verify2-20261122/verify-logs/01-red.txt) · [门槛原始输出](D:/Projects/_worktrees/pineglow-open02-verify2-20261122/verify-logs/06-gates.txt)

## 第一轮阻断的复核

探针确认生产 Host 接线实际使用 `ProductionConfigStore`。隔离配置文件和合成凭据后端启用后，通过回环 `GET /api/state` 记录请求窗口内的凭据操作。以下每行均为 HTTP 200，合成 CurseForge 密钥存在，写入和删除次数均为 0，原生凭据调用次数为 0。 [探针源码](D:/Projects/_worktrees/pineglow-open02-verify2-20261122/.cache/open02-config-probe/Program.cs) · [原始输出](D:/Projects/_worktrees/pineglow-open02-verify2-20261122/verify-logs/07b-production-store-probe.txt)

| 场景 | AI 读异常命中 | HTTP 与实际生效源 | 磁盘源 |
| --- | ---: | --- | --- |
| `official` 控制，启用删除故障 | 1 | `official` | `official`，文件字节一致 |
| `mcbbs` 正常 | 0 | `bmclapi` | `bmclapi` |
| `mcbbs`，AI 读取抛错 | 1 | `bmclapi` | `bmclapi` |
| `mcbbs`，AI 读取抛错并启用旧删除故障 | 1 | `bmclapi` | `bmclapi` |
| `mcbbs`，AI 读取抛错并独占锁住 `config.json.tmp` | 1 | `bmclapi` | `mcbbs`，文件字节一致 |

在同一合成后端和锁释放后，直接调用全量 `ProductionConfigStore.Save`，确认已处于内存密钥为空、后端仍有密钥的前置状态。原始输出记录 `keyPresent=False`、`curseForgeDeletes=1`、`delete:CurseForgeApiKey` 和 `nativeCredentialCalls=0`。这验证了保留断言可以检出第一轮的误删。 [全量 Save 反证](D:/Projects/_worktrees/pineglow-open02-verify2-20261122/verify-logs/07b-production-store-probe.txt:13)

## 阻断 · 旧密钥转存失败后被移出主配置

**产品路径。** `ConfigStore.LoadAsync` 支持从旧 JSON 读取明文 `CurseForgeApiKey`，在凭据后端为空时通过 `SaveAsync` 尝试转存。转存抛错被捕获，旧主配置仍可供未来重试。v2 此后照常进入下载源变更分支，`SaveConfigOnlyAsync` 把整个 `AppConfig` 序列化回主配置。密钥属性带 `[JsonIgnore]`，这次写回会移除仍未转存的旧字段。原生 Windows 后端的 `CredWriteW` 失败本来也会抛错，探针用合成 IOException 验证了产品如何处理这类失败。 [旧凭据读取](D:/Projects/_worktrees/pineglow-open02-verify2-20261122/dev/PineglowLauncher/Pineglow.Launcher.Services/ConfigStore.cs:92) · [转存尝试](D:/Projects/_worktrees/pineglow-open02-verify2-20261122/dev/PineglowLauncher/Pineglow.Launcher.Services/ConfigStore.cs:195) · [新增写回](D:/Projects/_worktrees/pineglow-open02-verify2-20261122/dev/PineglowLauncher/Pineglow.Launcher.Services/ConfigStore.cs:243) · [序列化设置](D:/Projects/_worktrees/pineglow-open02-verify2-20261122/dev/PineglowLauncher/Pineglow.Launcher.Models/AppConfig.cs:63) · [原生失败语义](D:/Projects/_worktrees/pineglow-open02-verify2-20261122/dev/PineglowLauncher/Pineglow.Launcher.Services/SecretStoreBackend.cs:136)

**反证与对照。** 用同一个 `VerifyHost.dll` 分别装载干净基线与 v2 应用 DLL。两次对照均在合成密钥仅存在于旧 JSON、合成后端对写入抛错的条件下请求状态。首个请求后恢复合成后端写入，再请求一次。所有请求均返回 HTTP 200，原生凭据调用为 0。 [探针及装载程序集的 SHA-256](D:/Projects/_worktrees/pineglow-open02-verify2-20261122/verify-logs/11-final-state.txt)

| 应用与输入 | 首次请求后主配置含密钥 | 首次请求后备份含密钥 | 后端恢复后第二次请求显示已配置 | 后端恢复后密钥已入库 |
| --- | --- | --- | --- | --- |
| 基线，`mcbbs` | 是 | 无备份 | 是 | 是 |
| v2，`official` | 是 | 无备份 | 是 | 是 |
| v2，`mcbbs` | **否** | 是 | **否** | **否** |

v2 失败行 `case=legacy-transfer-fault-mcbbs` 的关键原始字段如下。文件锁场景在同一探针二进制中通过。故障注入是夹具的预期行为，验收失败来自候选对失败的处理。唯一残留副本在 `.bak`，本轮未尝试用备份恢复或验证后续轮转。 [候选失败原文](D:/Projects/_worktrees/pineglow-open02-verify2-20261122/verify-logs/08b-legacy-credential-candidate.txt:4) · [基线通过原文](D:/Projects/_worktrees/pineglow-open02-verify2-20261122/verify-logs/10b-legacy-credential-baseline.txt:4)

```text
firstHTTP=200; firstSource=bmclapi; firstKeyConfigured=True; transferAttempts=1
activeConfigContainsKey=False; backupContainsKey=True; keyInBackend=False
secondHTTP=200; secondSource=bmclapi; secondKeyConfigured=False; keyInBackendAfterRecovery=False
nativeCredentialCalls=0; pass=False
legacyProbe=FAIL
```

## 其余代码审查与授权

- `LoadConfig` 的下载源写回只调 `SaveConfigOnly`。生产实现在 `ConfigGate` 内调用 `ApplySelectedToLegacy` 和 `WriteConfigFile`，该保存调用链不访问 `SecretStore`。接口默认回退到 `Save`，生产覆盖、既有测试 store 仍可运行。`SaveConfig` 用 `MirrorHelper.Normalize` 更新 `SelectedKey`，HTTP 状态返回值也经规范化。 [加载与保存](D:/Projects/_worktrees/pineglow-open02-verify2-20261122/dev/PineglowLauncher/Pineglow.Launcher.Services/LocalApiService.Configuration.cs:86) · [接口回退与生产覆盖](D:/Projects/_worktrees/pineglow-open02-verify2-20261122/dev/PineglowLauncher/Pineglow.Launcher.Services/IConfigStore.cs:24) · [HTTP 状态](D:/Projects/_worktrees/pineglow-open02-verify2-20261122/dev/PineglowLauncher/Pineglow.Launcher.Services/LocalApiService.Instances.cs:28)
- v2 的 13 个用例为 10 组迁移和重新加载数据、1 条 16 请求并发测试、1 条写回失败测试和 1 条生产存储测试。v1 的行为断言保留，原写回次数改为分别统计 config-only 与全量保存，要求全量保存为零。生产测试先强制 AI 读异常和内存缺少密钥，再检查 config-only 期间后端操作数不变，随后用同一对象显式全量保存验证删除。 [候选测试](D:/Projects/_worktrees/pineglow-open02-verify2-20261122/dev/PineglowLauncher.Tests/DownloadSourceHttpMigrationTests.cs:18)
- 候选生产补丁和原样测试仅进入验收工作树。主工作区、执行树和第一轮工作树按只读处理。未修复产品或测试，Git 暂存区为空，未提交、合并、推送、打 tag 或发布。真实 Windows 凭据管理器、Avalonia/WebView2 和发行包未验证。此次复验保留候选和原始日志，供编排方复核。 [收尾状态](D:/Projects/_worktrees/pineglow-open02-verify2-20261122/verify-logs/11-final-state.txt)

RESULT: FAIL
