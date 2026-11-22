# OPEN-02 独立复验交接 · 2026-11-22

**结论：FAIL，当前候选暂不合入。** 指定的 RED、GREEN、回归、清单和四道门槛全部符合预期。生产配置存储路径另有已复现的副作用：旧下载源自动迁移可删除无关的凭据。合入条件未达成。 `FormalPhaseApproval=NOT_GRANTED`。

## 工作树与输入

```text
pwd
C:\Users\Administrator\.t3\worktrees\松光启动器\t3-d5c8bb85-36c12a80f74ee483
git rev-parse --show-toplevel
C:/Users/Administrator/.t3/worktrees/松光启动器/t3-d5c8bb85-36c12a80f74ee483
git rev-parse HEAD
274675f0aaf686833239811adc39684d5b02743c
git status --short --untracked-files=all
```

上面是 19:23（UTC+08）开工取证，末条命令输出为空。使用 T3 为本任务创建的隔离工作树，位于 `C:/Users/Administrator/.t3/worktrees/`。命令使用 PowerShell 5.1，SDK 10.0.301、Node v24.19.0，PATH 前置 `C:\Windows\System32;C:\Windows`，TEMP/TMP 指向本工作树的 `.cache/tmp`。候选构建程序集版本为 1.0.5。所有测试数据为本工作树独立实测。详见[开工记录](C:/Users/Administrator/.t3/worktrees/松光启动器/t3-d5c8bb85-36c12a80f74ee483/verify-logs/00-preflight.txt)和[收尾记录](C:/Users/Administrator/.t3/worktrees/松光启动器/t3-d5c8bb85-36c12a80f74ee483/verify-logs/08-final-state.txt)。

| 校验对象 | 实测 SHA-256 |
| --- | --- |
| 候选补丁 | `422C562580F0BC858F7E5F399BA879266EAB696E5C278A92CC1806E8EB1F0545` |
| 候选测试（原件、复制件） | `A01D4131357CDAF23E6608A163FC8B65E37C4BEE6BEF132A120783D7CC700D34` |
| 应用后 Configuration.cs | `29EE486BB683B7E3F38470E67595E15821E70FFD5C60EA0F6B9D38A246BA383B` |
| 应用后 Instances.cs | `C78892DDE83FA38BE993B10C89968A3A971AC3AB76681B97A5074848D1253916` |

四个 SHA-256 均与任务输入匹配。RED 前两个生产文件与 HEAD 无差异。应用补丁仅修改两个生产文件，+20/−2，`git diff --check` 退出 0。保留本工作树内的候选现场，Git 暂存区为空。主工作区、执行工作树和原始输入按只读处理。见[补丁应用记录](C:/Users/Administrator/.t3/worktrees/松光启动器/t3-d5c8bb85-36c12a80f74ee483/verify-logs/01b-apply.txt)。

## 独立运行结果

所有命令在上述 Git 根目录执行。时间为 2026-11-22，UTC+08。完整起止时间、命令和退出码见[执行记录](C:/Users/Administrator/.t3/worktrees/松光启动器/t3-d5c8bb85-36c12a80f74ee483/verify-logs/commands.txt)和[探针执行记录](C:/Users/Administrator/.t3/worktrees/松光启动器/t3-d5c8bb85-36c12a80f74ee483/verify-logs/probe-command.txt)。

| 阶段 | 时间（UTC+08） | 预期与实测 | 退出码 | 原始输出 |
| --- | --- | --- | ---: | --- |
| RED | 19:23:43–19:23:56 | 预期且实测 2 通过 / 10 失败 / 0 跳过，共 12 | 1（预期） | [01-red.txt](C:/Users/Administrator/.t3/worktrees/松光启动器/t3-d5c8bb85-36c12a80f74ee483/verify-logs/01-red.txt) |
| 应用补丁 | 19:24:26 | 预期且实测两个文件 +20/−2，哈希一致 | 0 | [01b-apply.txt](C:/Users/Administrator/.t3/worktrees/松光启动器/t3-d5c8bb85-36c12a80f74ee483/verify-logs/01b-apply.txt) |
| GREEN | 19:24:51–19:25:02 | 预期且实测 12 通过 / 0 失败 / 0 跳过 | 0 | [02-green.txt](C:/Users/Administrator/.t3/worktrees/松光启动器/t3-d5c8bb85-36c12a80f74ee483/verify-logs/02-green.txt) |
| 配置、状态与路由回归 | 19:25:02–19:25:06 | 预期且实测 79 通过 / 0 失败 / 0 跳过 | 0 | [03-regression.txt](C:/Users/Administrator/.t3/worktrees/松光启动器/t3-d5c8bb85-36c12a80f74ee483/verify-logs/03-regression.txt) |
| 清单双向校验 | 19:25:06 | 预期且实测 35 个 Node 测试文件、1 个 C# 测试项目，文件一致 | 0 | [04-inventory.txt](C:/Users/Administrator/.t3/worktrees/松光启动器/t3-d5c8bb85-36c12a80f74ee483/verify-logs/04-inventory.txt) |
| 四道门槛 | 19:25:06–19:26:41 | 全部 PASS。C# 1497 通过 / 0 失败 / 0 跳过，前端 508 通过 / 0 失败 / 0 跳过，CSS token 49 个，资源闭包通过 | 0 | [05-gates.txt](C:/Users/Administrator/.t3/worktrees/松光启动器/t3-d5c8bb85-36c12a80f74ee483/verify-logs/05-gates.txt) |
| 资源闭包完整摘要 | 19:27:24 | 两个入口，扫描 20 个文件、36 条本地引用，无缺失引用 | 0 | [05b-closure.txt](C:/Users/Administrator/.t3/worktrees/松光启动器/t3-d5c8bb85-36c12a80f74ee483/verify-logs/05b-closure.txt) |
| 生产存储故障探针 | 19:26:50–19:26:55、19:27:24–19:27:26 | 预期保留无关凭据；实测删除 1 次。两次运行均复现，第二次增加单次凭据读异常场景 | 1、1 | [首次探针](C:/Users/Administrator/.t3/worktrees/松光启动器/t3-d5c8bb85-36c12a80f74ee483/verify-logs/06-production-store-probe.txt) · [扩展探针](C:/Users/Administrator/.t3/worktrees/松光启动器/t3-d5c8bb85-36c12a80f74ee483/verify-logs/07-production-store-probe-expanded.txt) |

```powershell
dotnet test dev/PineglowLauncher.Tests/Pineglow.Launcher.Tests.csproj -c Release --filter "FullyQualifiedName~DownloadSourceHttpMigrationTests"
git apply "D:/Projects/松光启动器/docs/evidence/2026-11-22-open02/03-candidate-final.patch"
dotnet test dev/PineglowLauncher.Tests/Pineglow.Launcher.Tests.csproj -c Release --filter "FullyQualifiedName~DownloadSourceHttpMigrationTests"
dotnet test dev/PineglowLauncher.Tests/Pineglow.Launcher.Tests.csproj -c Release --filter "FullyQualifiedName~DownloadSourceMigrationTests|FullyQualifiedName~ConfigBackupRotationTests|FullyQualifiedName~ConfigPreservationTests|FullyQualifiedName~ConfigDefaultIdentityTests|FullyQualifiedName~NewsFakeIpConfigTests|FullyQualifiedName~HostDependenciesTests|FullyQualifiedName~LocalApiServiceSplitTests|FullyQualifiedName~RouteReachabilityTests|FullyQualifiedName~ApiRouteInventoryTests"
node scripts/run-inventory-tests.cjs --check
node scripts/gates.cjs
node scripts/check-ui-resource-closure.cjs
dotnet run --project .cache/open02-config-probe/Probe.csproj -c Release -p:BuildProjectReferences=false
```

RED 的 10 个失败均为业务断言。7 个用例在 HTTP `downloadSource` 断言失败（`mcbbs`、`MCBBS`、` mcbbs `、`Official`、`BMCLAPI` 五组参数，以及并发与写回异常探针），3 个空值用例在写回次数断言失败。原始输出示例为 `Expected: "bmclapi"` / `Actual: "mcbbs"` 和 `Expected: 1` / `Actual: 0`。编译、还原、监听和权限均未构成失败原因。门槛按预期提示 `docs/baseline.md` 记载的 `dcdf056` 与当前 `274675f` 不一致，门槛仍退出 0。

## 合入阻断 · 自动迁移删除无关凭据

**产品路径。** `LoadConfig` 在同一把 `ConfigGate` 内读取、规范化并调用 `Store.Save(config)`。生产实现接到 `ConfigStore.SaveAsync`。读取任一 AI 凭据失败时，`ConfigStore.LoadAsync` 的异常分支会把已读取的 CurseForge 密钥清空。迁移随后调用全量保存，`ConfigStore.SaveCore` 在写 JSON 之前把该空值传给 `SecretStore.Save`，触发删除。若本次写回随后抛异常，`LoadConfig` 仍返回规范化结果，先前发生的删除没有回滚。见 [LocalApiService.Configuration.cs](C:/Users/Administrator/.t3/worktrees/松光启动器/t3-d5c8bb85-36c12a80f74ee483/dev/PineglowLauncher/Pineglow.Launcher.Services/LocalApiService.Configuration.cs:86)、[生产存储接线](C:/Users/Administrator/.t3/worktrees/松光启动器/t3-d5c8bb85-36c12a80f74ee483/dev/PineglowLauncher/Pineglow.Launcher.Services/IConfigStore.cs:36)、[读异常分支](C:/Users/Administrator/.t3/worktrees/松光启动器/t3-d5c8bb85-36c12a80f74ee483/dev/PineglowLauncher/Pineglow.Launcher.Services/ConfigStore.cs:94)、[保存顺序](C:/Users/Administrator/.t3/worktrees/松光启动器/t3-d5c8bb85-36c12a80f74ee483/dev/PineglowLauncher/Pineglow.Launcher.Services/ConfigStore.cs:237)、[空值删除语义](C:/Users/Administrator/.t3/worktrees/松光启动器/t3-d5c8bb85-36c12a80f74ee483/dev/PineglowLauncher/Pineglow.Launcher.Services/SecretStore.cs:124)。

**实测。** 探针走真实回环 `GET /api/state` 和生产配置存储代码。JSON 根重定向到工作树 TEMP，凭据后端只使用内存中的合成条目，并对指定操作注入 IOException。四种场景的原生凭据调用均为 0，日志未输出凭据值。探针编译项目独立于候选测试项目，引用的应用 DLL 与候选 Release 构建的 SHA-256 相同。见[探针源码](C:/Users/Administrator/.t3/worktrees/松光启动器/t3-d5c8bb85-36c12a80f74ee483/.cache/open02-config-probe/Program.cs)、[项目文件](C:/Users/Administrator/.t3/worktrees/松光启动器/t3-d5c8bb85-36c12a80f74ee483/.cache/open02-config-probe/Probe.csproj)及[程序集哈希](C:/Users/Administrator/.t3/worktrees/松光启动器/t3-d5c8bb85-36c12a80f74ee483/verify-logs/08-final-state.txt)。

| 输入 | 注入故障 | HTTP | 返回下载源 | JSON 下载源 | 无关合成凭据 |
| --- | --- | ---: | --- | --- | --- |
| `official` | AI 凭据读失败，后续凭据保存可失败 | 200 | `official` | `official` | 保留，删除 0 次 |
| `mcbbs` | 无 | 200 | `bmclapi` | `bmclapi` | 保留，删除 0 次 |
| `mcbbs` | 仅 AI 凭据读失败 | 200 | `bmclapi` | `bmclapi` | **已删除，1 次** |
| `mcbbs` | AI 凭据读失败，随后凭据保存失败 | 200 | `bmclapi` | `mcbbs` | **已删除，1 次** |

决定性原始片段为 `HTTP=200; responseSource=bmclapi; diskSource=mcbbs; unrelatedKeyPresent=False; unrelatedKeyDeletes=1; nativeCredentialCalls=0`。探针退出 1 是合成凭据保留断言失败。单次读异常场景同时证明，JSON 成功写回时也会删除无关凭据。[扩展探针原始输出](C:/Users/Administrator/.t3/worktrees/松光启动器/t3-d5c8bb85-36c12a80f74ee483/verify-logs/07-production-store-probe-expanded.txt)

## 其余代码审查

- `LoadConfig` 的读取、规范化及迁移保存共享 `LocalApiService.ConfigGate`，与该类内的 `SaveConfig` 串行。正常写回后重复读取无额外保存，16 个并发状态请求仅写回一次。该锁证明覆盖这条服务调用链。
- `/api/state` 回传 `Normalize` 的结果。候选测试同时断言 HTTP 状态、JSON 源值、`MirrorHelper.SelectedKey`、URL 改写、文件内容、写回次数和从新存储重载后的结果。`Official` / `BMCLAPI` 各写回一次并落盘为小写，固定了 `Ordinal` 比较语义。原有测试和断言均保留。见[状态处理代码](C:/Users/Administrator/.t3/worktrees/松光启动器/t3-d5c8bb85-36c12a80f74ee483/dev/PineglowLauncher/Pineglow.Launcher.Services/LocalApiService.Instances.cs:28)及[候选测试](C:/Users/Administrator/.t3/worktrees/松光启动器/t3-d5c8bb85-36c12a80f74ee483/dev/PineglowLauncher.Tests/DownloadSourceHttpMigrationTests.cs:17)。
- 候选异常用例的 `ThrowingSaveStore` 在写入前抛出，验证 HTTP 200 和旧 JSON 保留。生产保存前置的凭据操作由独立探针覆盖。执行交接中关于 `SaveConfig` 使用 `Normalize` 的表述与候选源码不符，该方法仍保留原有空白判断，这两处生产修改涉及 `LoadConfig` 和状态回传。见 [SaveConfig](C:/Users/Administrator/.t3/worktrees/松光启动器/t3-d5c8bb85-36c12a80f74ee483/dev/PineglowLauncher/Pineglow.Launcher.Services/LocalApiService.Configuration.cs:114)。
- 本轮未运行原生 Windows 凭据管理器、Avalonia/WebView2 GUI 或发布包冷启动。产品问题的运行证据来自实际生产配置保存逻辑与合成凭据后端的组合。候选与测试输入原样保留，未合并、未提交、未推送。

RESULT: FAIL
