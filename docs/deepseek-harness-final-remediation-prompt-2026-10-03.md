# 可复制给 Harness / MiniMax 的修复任务 — 2026-10-03

以下文本是待执行任务定义；只有用户明确要求执行后才启动。本文件的创建不是执行授权。

---

处理松光启动器最终验收 F-01–F-05，按 R0→R1→R2→R3→R4 依赖完成最小修复、独立副本自检和证据包，停止在 Codex 独立验收点。不要恢复完整 MR00–MR27 连续产品目标。

先读取：

1. `C:\Users\Administrator\Documents\Codex\2026-09-25\gen-2\outputs\项目协作规范-2026-09-25.md`
2. `C:\Users\Administrator\.codex\worktrees\ed41\松光启动器\docs\deepseek-harness-final-acceptance-2026-10-03.md`
3. 同目录 `deepseek-harness-final-remediation-workflow-2026-10-03.md`
4. 同目录 `market-acceptance\20261003-final\09-independent-all-final.log`、`10-all-failures-repeat.log`、`credential-intents.jsonl`、`independent-evidence-audit.json`、`FinalAcceptanceProbes.cs`

源码根目录固定 `D:\松光启动器`；文档交付根目录固定 `C:\Users\Administrator\.codex\worktrees\ed41\松光启动器`。每个执行节点显式记录 cwd，不混用两个目录。源 HEAD 基线 f145d14eb26b0b7459a28491e764ac8c573085d3，但实际对象为 dirty 当前文件。先保存 git status、diff、允许文件 SHA 与显式输入白名单。若有并发执行，停止并报告。

允许修改范围：

- `D:\松光启动器\dev\PineglowLauncher\Pineglow.Launcher.Services\SecretStore.cs`、`FileRecoveryService.cs`、`ModReplacementService.cs`、`LocalApiService.cs` 中本次问题相关代码。
- 可新增上述目录 `SecretStoreBackend.cs`；不要改真实凭据命名或生产迁移行为。
- `D:\松光启动器\dev\PineglowLauncher.Tests\ConfigPreservationTests.cs`、`FileRecoveryTests.cs`、`ModReplacementTests.cs`、`WorldBackupScopeTests.cs`，可新增 `SecretStoreBackendTests.cs`、`FinalAcceptanceRegressionTests.cs`。先核实已有文件名；若不同，在基线清单列明实际路径，不扩大到其他模块。
- `D:\松光启动器\scripts\source-snapshot-inputs.txt`，仅追加必要新输入。
- 如替换响应模型位于单独文件，仅允许修改该实际结果类型的字段，先记录其精确路径与理由。
- 当前文档交付根下的 STATUS、实际任务台账、新时间戳证据目录；证据审计脚本只补本次全层级 SHA/size/缺项检查。

只读/禁止：原验收目录及历史 run 包、旧 manifest/原始 log、真实 APPDATA 配置、Windows 凭据、真实游戏/存档、其他项目、无关 UI/功能、发布和 CI 配置。禁止 reset/restore/checkout/clean/stash、批量删除、add-A、提交/推送/PR/部署/发布、真实外部服务调用及进入下一阶段。保留所有既有未提交改动。测试沙盒清理必须确认绝对目标位于自己新建 GUID 根中，junction 仅删除链接本身。

R0：先隔离 SecretStore 的读/写/删后端。现有 ConfigPathOverrideForTest 和 portable.flag 不能阻止 Windows CredDeleteW/CredWriteW。不要原样运行那 5 个配置测试。使用内存后端或完全阻断 native 的取证副本保留 RED，随后将原配置断言接到统一隔离后端，证明 native 次数 0、scope 清理及并发隔离。不得枚举或导出真实凭据，不在日志记录密钥或 token。仅 secrets.json 不变不足以通过。

R1：恢复路径必须拒绝目标及祖先 junction/reparse、非法隔离源、越界记录。任何写入前确认实例/操作与真实路径边界，并明确处理目录替换竞态。恢复拒绝保留 StoredPath 文件和 SHA、返回 ok=false/逐项失败。把现有服务和真实 HTTP junction 探针保留为 RED→GREEN，普通恢复/默认冲突不覆盖/显式覆盖/缺失目录/中文存档恢复不退化。

R2：RollbackAsync 不吞掉真实失败，不无条件说“已回滚”。回滚输出真实 restored/partial/failed，替换失败可返回 needsRecovery、operationId、逐文件错误。使用现有 BeforeCommitForTest 注入只读阻断保留 RED；GREEN 时目标阻断仍存在，错误提示必须准确，旧文件保留，解除阻断后可再次恢复。覆盖 JAR 与 sidecar 事务。

R3：HandleListModsAsync 是只读路由，不得被重复启动守卫拦截。合成 running PID + HTTP GET mods 应为 200；重复启动、运行中移除实例、危险变更仍拒绝。逐路由审查，不全局删除守卫，不启动真实游戏。保留 worldA/worldB 备份筛选与跨世界恢复拒绝。

R4：保留 MG0 drift-notes.md 与 MG2 verify-mg2-full.log 及旧 manifest 的当前字节，另建 correction/新运行记录来源与差异，不静默重写历史。覆盖全部 59 个现有层级清单（新增包也纳入），验证 SHA、大小、漏项、路径与 runId；失败返回非零，manifest 自身排除并另存 detached SHA。文案准确区分实现、准备任务、未授权条件任务、市场验证与技术验收。GroupName 实际来自备份相对父目录。

执行和证据要求：

```powershell
# 基线只读
Set-Location -LiteralPath 'D:\松光启动器'
git status --short --untracked-files=all
git rev-parse HEAD
# 动态测试先创建新显式白名单副本；之后 Set-Location 到该副本。
# R0 的原生凭据隔离证据完成前，禁止跑未隔离的完整测试。
dotnet test '.\dev\PineglowLauncher.Tests\Pineglow.Launcher.Tests.csproj' -c Release --filter 'FullyQualifiedName~ConfigPreservationTests|FullyQualifiedName~SecretStoreBackendTests' --logger trx
dotnet test '.\dev\PineglowLauncher.Tests\Pineglow.Launcher.Tests.csproj' -c Release --filter 'FullyQualifiedName~FileRecovery|FullyQualifiedName~ModReplacement|FullyQualifiedName~WorldBackupScope|FullyQualifiedName~FinalAcceptanceRegressionTests' --logger trx
# R0 GREEN 后全量
dotnet test '.\dev\PineglowLauncher.Tests\Pineglow.Launcher.Tests.csproj' -c Release --logger trx
$repairNodeTests = @(Get-ChildItem -LiteralPath '.\ui\tests' -Filter '*.test.cjs' | Select-Object -ExpandProperty FullName)
node --test @repairNodeTests
dotnet build '.\dev\PineglowLauncher\VerifyHost\VerifyHost.csproj' -c Release
& '.\dev\PineglowLauncher\VerifyHost\bin\Release\net10.0\VerifyHost.exe' --bisect-smoke
```

每条命令独立保存 cwd、命令原文、UTC 起止、exit、测试数量、skip、关键输出和源码 SHA。原始 RED exit 1 保留，后续 GREEN 另存。原 C# 830 安全通过与 5 项凭据替身证明不能混写为原版 835 独立通过；本轮原 15+新5探针共20项16通过4失败，其中junction有服务与API两项。完整套件新增测试后的实际数量如实列出，不硬凑旧835。

每包最多三轮修复复跑；真实失败、越界或需要扩大允许路径时保留现状并报告阻断。最终证据须含：最小 diff、RED→GREEN、全部20探针、新隔离后端证明、完整 C#/Node/构建/19项 Bisect 冒烟、全层级清单审计、dirty保护检查、未验证 GUI/更新真实进程交接/远程 CI 清单。不得用 fixture 错误代替生产路径 RED，不削弱断言以凑 GREEN。

成功终止语必须为：**具备独立复验条件，等待 Codex 独立验收**。技术验收、正式阶段批准、发布与后续任务仍由用户/Codex另行决定。
