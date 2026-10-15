# 可复制给 DeepSeek Harness / MiniMax 的再返修提示词

以下是待授权执行文本；生成本文不代表启动或修复授权。

---

处理 FINAL-REMEDIATION 独立复验 RA-01–RA-04，按 S1→S2→S3→S4 依赖完成最小修复和证据，停止在 Codex 独立验收点。不要恢复 MR00–MR27 完整产品目标，不提交不发布。

先读：

1. `C:\Users\Administrator\Documents\Codex\2026-09-25\gen-2\outputs\项目协作规范-2026-09-25.md`
2. `C:\Users\Administrator\.codex\worktrees\0f45\松光启动器\docs\deepseek-harness-remediation-reacceptance-2026-10-03.md`
3. 同目录 `deepseek-harness-remediation-rework-workflow-2026-10-03.md`
4. `docs/market-acceptance/20261003-remediation-recheck/RemediationBoundaryProbes.cs`、`test-summary.json`、`source-final-protection.json`、`boundary-run-20261003T070203Z-6ef2deb1/07-new-boundaries.log`、`08-new-boundaries-repeat.log`、`independent-manifest-audit.json` 与3份auditor日志。
5. ed41 `docs/market-execution/FINAL-REMEDIATION/20261003-052200Z` 的原交付、旧审计脚本及known-mismatches，仅只读。

固定 SourceRoot=`D:\松光启动器`；DeliveryRoot=`C:\Users\Administrator\.codex\worktrees\0f45\松光启动器`。每节点记录cwd。HEAD f145d14eb26b0b7459a28491e764ac8c573085d3，但实际基线是dirty当前文件。先保存status、HEAD、允许文件SHA、diff、白名单，确认无并发执行。在新GUID哈希绑定副本测试，证据另存DeliveryRoot/docs/market-execution/FINAL-REMEDIATION-R2/<实际UTC时间戳>。

允许修改仅限 SourceRoot：

- `dev/PineglowLauncher/Pineglow.Launcher.Services/FileRecoveryService.cs`、`LocalApiService.cs` 的恢复/替换相关逻辑；必要同目录新路径防护文件先列具体名字与理由。
- `dev/PineglowLauncher.Tests/FileRecoveryTests.cs`、`FileRecoveryEdgeCaseTests.cs`、`FinalAcceptanceRegressionTests.cs`，可新增 `RemediationBoundaryRegressionTests.cs`。
- `scripts/source-snapshot-inputs.txt` 只追加必要新输入。
- 必要时 `ModReplacementService.cs` 仅其现有结果类型字段。
- DeliveryRoot 的本轮新证据、correction、复制后审计脚本和实施报告。

禁止修改 SecretStore/后端、配置、无关UI/CI/发布及其他业务；原ed41历史包、本轮验收证据、旧RED/log/manifest、真实APPDATA/Windows凭据/游戏/存档全部只读。禁止reset/restore/checkout/clean/stash、批删、stage-all、commit/push/PR/deploy/release或进入下一阶段。清理测试目录先校验绝对路径在自己创建的GUID根；junction只删链接，不递归进入target。

S1：复现 ModReplaceUndo_MustRejectJunctionAboveRecordedModsRoot 的真实HTTP RED。记录根是game/mods，game→outside的junction在请求前已存在，当前返回200/ok=true/restored并越界写入；这不需要竞态。把验证锚定到可信实例根和完整有效祖先，拒绝record子目录根以上及恢复根以上的重解析路径。写入前拒绝且保留源/SHA/operation；明确解决或可证明限制TOCTOU，不能靠Move后复核声称源被保留。正常替换撤销、中文路径、冲突不覆盖、显式覆盖、缺失目录/sidecar均GREEN。

S2：复现 StoredPath_MustBelongToSameInstanceAndOperation 与 HttpForgedStoredPath_MustNotConsumeOtherInstanceFile 的RED。只改合成A记录的StoredPath/Sha为B，当前HTTP200并BStoredExists=false。严格绑定请求instanceId/operationId、record元数据和可信操作files目录；不能只检查全局_recoveryRoot前缀。源实际祖先/SHA也需在消费前验证。跨实例、同实例跨操作、伪造ID/operation、源hash不符均拒绝且A/B源不动；保留幂等恢复兼容。

S3：当前服务文案真实，别把旧“已回滚”缺陷重新描述为未修。修的是HTTP字段：RollbackFailure_HttpMustExposeRecoveryFields 当前400仅error字符串。局部失败JSON保持error和HTTP兼容并返回status、needsRecovery、operationId、recoveryStatus、failedFiles；回滚成功/失败分开，旧源保留，解除只读后仍可再次恢复。不要全局改异常协议。

S4：保留MG0/MG2两历史差异，不重写旧清单。复制审计器到新包，精确绑定已知差异的manifest版本、旧记录值与登记实际SHA/size，第三种字节非零；空根/覆盖缺项非零；detached校验。原3合成案例现均假通过exit0：必须同场景转为检测失败exit非零，同时正确完整树通过。新correction封存旧文件当前字节/来源/差异和新清单。不要只按路径永久豁免。审计结果放包外并封存detached，避免自引用。

验证命令（先创建白名单副本，在副本运行）：

```powershell
git -C 'D:\松光启动器' status --short --untracked-files=all
git -C 'D:\松光启动器' rev-parse HEAD
# Set-Location 到本轮新GUID副本，以下每条独立记录。
dotnet test '.\dev\PineglowLauncher.Tests\Pineglow.Launcher.Tests.csproj' -c Release --filter 'FullyQualifiedName~ConfigPreservationTests|FullyQualifiedName~SecretStoreBackendTests' --logger trx
dotnet test '.\dev\PineglowLauncher.Tests\Pineglow.Launcher.Tests.csproj' -c Release --filter 'FullyQualifiedName~RemediationBoundary|FullyQualifiedName~FinalAcceptance|FullyQualifiedName~IndependentRouteProbes|FullyQualifiedName~FileRecovery' --logger trx
dotnet test '.\dev\PineglowLauncher.Tests\Pineglow.Launcher.Tests.csproj' -c Release --logger trx
$repairInputs = @(Get-ChildItem -LiteralPath '.\ui\tests' -Filter '*.test.cjs' -File | Select-Object -ExpandProperty FullName)
node --test @repairInputs
dotnet build '.\dev\PineglowLauncher\VerifyHost\VerifyHost.csproj' -c Release
& '.\dev\PineglowLauncher\VerifyHost\bin\Release\net10.0\VerifyHost.exe' --bisect-smoke
# 新审计器及3个反例，命令写入新证据包；原始历史文件不改。
```

本轮已独立通过875C#、238Node、13R0、32旧/常驻探针、19Bisect；875含旧20探针且不含本轮新5，子集不要叠加。新5探针1通过4失败（RA02服务/HTTP重复根因），重复运行不增加唯一数量。转常驻后全量实际计数如实记录，不能删探针凑旧875。

每次命令保留cwd、argv、UTC起止、exit、TRX计数/skip、源码SHA、rawHTTP/字节、RED→GREEN、最小diff、source/config保护及未验证GUI/真实游戏/凭据往返/远程CI。用新输出路径，不覆盖失败TRX。每包最多3轮修复；真实失败/越界/需扩白名单时保留现场并报告，禁止削弱断言或吞掉子命令exit。

最终只写：**具备独立复验条件，等待 Codex 独立验收**。技术验收、正式阶段批准、发布及后续任务未授予。
