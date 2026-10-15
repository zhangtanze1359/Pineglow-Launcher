# Harness 验收阻断项详细修复流程 — 2026-10-03

状态：待实施，未执行。关联 `deepseek-harness-final-acceptance-2026-10-03.md`。范围限 F-01–F-05，不推进 MR13–MR27 新产品目标，不提交或发布。

## 1. 进入条件与基线

源码根目录是 `D:\松光启动器`。交付根目录是 `C:\Users\Administrator\.codex\worktrees\ed41\松光启动器`。二者分别显式配置，禁止根据当前 cwd 猜测。

1. 确认 Harness 没有同时运行的 turn；保留 paused。阅读用户协作规范、目录内适用的 AGENTS.md、三份本轮文档以及失败日志。
2. 保存 `git status --short --untracked-files=all`、HEAD、允许文件当前哈希和 diff。现有 1360 条状态记录均视为既有资产。
3. 捕获精确输入白名单与副本 SHA，在新的 GUID 副本构建；证据输出到新的时间戳目录。旧 RED、旧清单与失败日志不改写。
4. 原版 `ConfigPreservationTests` 暂不执行；先修凭据隔离，或仅在完整阻断原生凭据入口的独立副本中做 RED。不要枚举、导出或备份真实凭据内容。
5. 必须保留上轮两个存档绑定修复、中文存档恢复、已通过的 15 项探针、Bisect 失败恢复、默认不覆盖恢复、账号隔离与重复启动保护。

## 2. 分包依赖和交付

| 包 | 目标 | 前置 | 必需证据 |
|---|---|---|---|
| R0 | 凭据后端测试隔离 | 基线 | 读/写/删全部走隔离后端；原生调用不可达；原 5 项安全 GREEN |
| R1 | 恢复真实路径边界 | R0 | 服务/API junction RED→GREEN；隔离源不丢失；普通冲突恢复不退化 |
| R2 | 回滚失败真实结果 | R1 | 只读目标阻断回滚；needsRecovery/逐项错误；恢复成功与失败分别 GREEN |
| R3 | 正确限定运行态守卫 | R2 | GET mods 运行中 200；重复启动及危险变更仍拒绝 |
| R4 | 证据、声明与最终回归 | R0–R3 | 全量、Node、HTTP smoke、清单完整且哈希一致 |

每包最多三轮“修改→复跑”；若仍失败或需扩范围，记录真实阻断并停止。可在已明确授权的本修复范围内按依赖继续，不因绿色节点自动推进产品阶段。

## 3. R0 — 先关闭测试凭据风险

允许：`dev/PineglowLauncher/Pineglow.Launcher.Services/SecretStore.cs`、`dev/PineglowLauncher.Tests/ConfigPreservationTests.cs`；必要时新增同目录 `SecretStoreBackend.cs`、新增 `dev/PineglowLauncher.Tests/SecretStoreBackendTests.cs`，修改 `scripts/source-snapshot-inputs.txt`。

设计要求：生产默认仍使用原生 Windows/原有文件后端；测试注入覆盖 Load、Save、Delete 的统一后端。不更改用户凭据名字或迁移策略，不通过 production namespace 做“测试凭据”。内存后端只保存合成值。避免共享静态后端与测试并发互串，使用可释放 scope 或符合现有 collection 的串行边界。

RED：在不接触真实凭据的副本中复现 5 项测试进入 20 次删除意图；增加后端隔离失败时的 fail-fast 测试。若采用 adapter 对 native 边界做计数，必须保证计数后即返回，不调用 Cred*。

GREEN：5 个原配置用例保留原断言，通过注入后端安全执行；合成读取、写入、删除、异常与 scope 恢复均覆盖。证明 native API 调用次数 0；不是仅证明 secrets.json 不变。配置、.bak、.prev 的保留行为继续成立。

R0 未达到 GREEN，不运行未隔离的全量测试。

## 4. R1 — 安全恢复路径

允许：`FileRecoveryService.cs`、必要的 `LocalApiService.cs` 恢复响应；`FileRecoveryTests.cs` 及新增 `FinalAcceptanceRegressionTests.cs`。

1. 检查 OriginalPath、StoredPath、record 与实例绑定、恢复根目录及每级父路径。隔离路径必须归属该操作，不信任记录中的任意绝对路径。
2. 拒绝 junction/symlink/reparse 目标及祖先；包括游戏根、恢复根和缺失目录创建前最近存在祖先。校验之前不做删除、创建、覆盖或移动。
3. 对检查与写入间的目录替换竞态做明确设计；平台允许时使用不跟随链接的句柄/受控目录访问。不能用“前缀相同”或“已经预检查”宣称彻底关闭竞态。
4. 拒绝后状态与逐项错误准确，隔离文件、SHA、operationId 保留可再次恢复；API ok=false，不得 200+ok=true+restored。
5. 搬入本轮服务级与 HTTP junction 探针；junction 指向测试 GUID 根的 sibling，只清理自己创建的 junction，清理前验证根边界。

边界矩阵：普通恢复、默认冲突不覆盖、显式覆盖、缺失父目录、目标只读、祖先 junction、直接 reparse 文件、隔离源 reparse、伪造越界记录。所有文件使用合成 JAR/存档。保留世界对象绑定与 deleted-world 中文恢复 GREEN。

## 5. R2 — 回滚不得吞掉真实失败

允许：`ModReplacementService.cs`、必要的恢复/结果模型、`LocalApiService.cs` 的替换响应、`ModReplacementTests.cs`、`FinalAcceptanceRegressionTests.cs`。新增模型仅限 `ModReplacementResult` 等实际现有结果类型的文件；先定位再记录具体路径，禁止修改无关模型。

1. 回滚返回成功/partial/failed 及逐项错误，异常也转换为明确失败，保留原异常信息。
2. “提交失败、回滚成功”与“提交失败、回滚失败”分开表达。仅所有旧文件恢复后显示“已回滚”。
3. 失败时带 needsRecovery、operationId、失败文件，并保持可重试恢复。不要为做 GREEN 删除只读目标、清空恢复记录或去掉失败断言。
4. JAR、禁用 JAR、sidecar/元数据提交失败分别检查原字节、最终字节、恢复记录状态；没有复现的场景明确写未验证。
5. 用本轮只读目标故障注入复现 RED；修复后同一故障仍存在，结果应准确报告人工/重试恢复需求。解除合成只读阻断后验证再次恢复成功。

## 6. R3 — 限定守卫

允许：`LocalApiService.cs` 相关 handler；`InstanceLifecycleTests.cs`（实际存在时复用）、`WorldBackupScopeTests.cs`、`FinalAcceptanceRegressionTests.cs`。

列出本轮加入运行态检查的每个路由及读取/变更/启动类别。只读 Mod 查询允许在运行中进行；其他只读列表按产品既有约定复查，若需要改变既有产品策略先记录阻断，不擅自扩项。保留 launch 的并发与原子性、运行中移除实例拒绝、存档恢复/危险 Mod 变更拒绝。

以当前测试进程 PID 做合成注册，并在 finally 清理。GET mods 返回 200 且不写文件；真实 HTTP 测试重复启动和变更仍返回拒绝。不能只通过反射检查方法是否含某字符串。

## 7. R4 — 重建可信交付包

允许：项目相关 `docs/market-execution/STATUS.md` 与本次新增时间戳证据包、任务台账实际路径、`scripts/audit-evidence-tree.ps1` / 实际对应审计脚本。旧 manifest、旧 log、旧 RED 一律只读；更正采用新增 correction 文件或新包。

1. 保留 MG0 的 drift-notes.md 与 MG2 的 verify-mg2-full.log 当前字节；列出旧清单值、实际 SHA/大小、来源与差异。不要只静默重算旧 manifest 后称历史一直一致。
2. 若日志需重新取得，使用新运行、新时间戳、新文件。记录命令、cwd、开始结束、exit、唯一测试数、skip、源码 SHA。
3. 清单遍历顶层 MG 和各 MR run 的全部包；校验 path 边界、missing/unlisted、SHA、size、runId。含异常返回非零，含 manifest 自身为失败；detached SHA 独立存放。
4. 正确区分实现任务、准备文档、条件任务、市场验证、独立验收与发布。“全部证据就绪”不得表示全部实现/需求成立。
5. 修正 GroupName 描述为备份相对父目录；守卫/运行注册表现状、数量和证据层级均与源码一致。
6. R0 完成后在新独立副本跑完整 C#、全部 Node、Release、Bisect smoke、原 15 + 本轮 5 探针及新增回归。不能把替身证明与原生产实现全量合并计数。

## 8. 验证命令与停止点

```powershell
Set-Location -LiteralPath 'D:\松光启动器'
git status --short --untracked-files=all
git rev-parse HEAD
git diff -- 'dev/PineglowLauncher/Pineglow.Launcher.Services/SecretStore.cs' 'dev/PineglowLauncher.Tests/ConfigPreservationTests.cs'
# 以下动态命令必须在新建、哈希绑定的独立副本运行。
# R0 前仅在原生凭据已彻底阻断的取证副本执行配置测试。
dotnet test '.\dev\PineglowLauncher.Tests\Pineglow.Launcher.Tests.csproj' -c Release --filter 'FullyQualifiedName~ConfigPreservationTests|FullyQualifiedName~SecretStoreBackendTests' --logger trx
dotnet test '.\dev\PineglowLauncher.Tests\Pineglow.Launcher.Tests.csproj' -c Release --filter 'FullyQualifiedName~FileRecovery|FullyQualifiedName~ModReplacement|FullyQualifiedName~WorldBackupScope|FullyQualifiedName~FinalAcceptanceRegressionTests' --logger trx
# R0 隔离已证明后，才运行全量。
dotnet test '.\dev\PineglowLauncher.Tests\Pineglow.Launcher.Tests.csproj' -c Release --logger trx
$repairNodeTests = @(Get-ChildItem -LiteralPath '.\ui\tests' -Filter '*.test.cjs' | Select-Object -ExpandProperty FullName)
node --test @repairNodeTests
dotnet build '.\dev\PineglowLauncher\VerifyHost\VerifyHost.csproj' -c Release
& '.\dev\PineglowLauncher\VerifyHost\bin\Release\net10.0\VerifyHost.exe' --bisect-smoke
```

每条命令立即记录 `$LASTEXITCODE`，不要让后一条覆盖前一条。失败保留原始输出，不能用“预期失败”把 RED 写为 exit 0。采用全量入口枚举时与本轮 20 个入口对账，增加/减少必须解释。

回退：只回退本次包内的最小变更，依据新基线逐项审阅；不用 reset/restore/clean/stash 或覆盖既有文件。失败后保留旧隔离文件，禁止用全目录清理消除故障证据。

完成交付：改动清单、每包 RED/GREEN、所有命令记录、源码快照、清单校验、未验证清单及剩余风险。终止语固定为“具备独立复验条件，等待 Codex 独立验收”。正式阶段、发布和后续目标继续未授权。
