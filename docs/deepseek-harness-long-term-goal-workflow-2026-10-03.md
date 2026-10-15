# 松光启动器 Windows 本机离线质量闭环实施计划（2026-10-03）

> 给执行者：按 G0→G4 顺序在目标模式中连续执行，本文件与独立提示词共同定义范围。采用一个 Harness 主执行会话，不要求额外子代理或未安装技能。用户指定的独立验收、现场保护和禁止提交规则优先于通用 writing-plans 模板的提交建议。

**Goal：** 将当前松光启动器的 Windows 本机离线普通功能整理成有冻结输入、有完整证据覆盖、有可重复行为验证、有中断恢复记录的质量闭环；交付一套六流程场景矩阵、严格证据审计器和可恢复运行器，达到“具备独立复验条件，等待 Codex 独立验收”。

**Architecture：** 保留 Avalonia/WebView2 + C# LocalApiService + 原生 JavaScript。业务恢复安全实现不进入本目标的修改范围。先把证据根/包/字段契约与封存顺序落实为机器门槛，再在现有正常功能和 UI 上补齐行为证明，最后用新GUID源码副本串起验证和最终封存。

**Tech Stack：** 当前本机 .NET10、xUnit、Node内置测试、PowerShell、Python标准库。不开新依赖，不升级工具链，不改CI。

**Spec：** 当前目录 deepseek-harness-r2-independent-acceptance-2026-10-03.md；用户指定三份 Codex 执行与独立验收规则；旧市场任务流程只用于历史需求核对，不构成本目标的无限授权。

**当前状态：** 方案与提示词已备好，尚未向 Harness 发送、尚未创建其目标、尚未授权实施。用户把本目标提示词提交给 Harness 后，该提示词定义其中的连续实施范围；不能由实施者自己把“规划材料存在”解释成已获授权。

## 1. 为什么采用这个长线目标

当前普通回归已有独立 C#24、Node238、Bisect19通过，反复堆叠同一类自检的边际价值有限。真正暴露的问题是：

1. 最后封存成功后又改包内文件，最终包不能通过自己的审计；
2. 审计器只验证发现的manifest，能漏掉整个必需根或同级包；
3. 部分UI测试仅为静态接线与文案扫描，无法证明加载/异常/重复操作时的真实业务反馈；
4. 原市场计划跨度过大，混入真实用户、费用、分发、GUI、跨平台等不同依赖，不适合一次无边界目标运行。

本目标保留一个可独立完成的工程闭环。受限的恢复安全验收、真实用户与发布资格仍留在外部门槛，不让目标模式无限等待，也不通过换执行者补做已拒绝的场景。

## 2. 固定目录、白名单与禁止事项

### 2.1 根目录

- SourceRoot：`D:\松光启动器`。实现事实来自其dirty工作区，不是34ca旧提交。
- PlanRoot：`C:\Users\Administrator\.codex\worktrees\34ca\松光启动器\docs`。
- 新EvidenceRoot：`PlanRoot\market-execution\LONG-QUALITY-GOAL\<真实UTC时间戳>-<GUID前8位>`。
- 历史ed41、0f45的所有证据与本轮独立验收目录：只读。
- 执行目录：每次从SourceRoot白名单建立新GUID临时副本；先确认绝对路径位于本轮临时根且不存在。
- 香港时间用于人读报告，UTC用于runId；不得手工编造“310000Z”之类时间。

### 2.2 允许修改的精确对象

所有路径均相对于SourceRoot，新增前先记录路径与职责：

| 对象 | 允许范围 |
| --- | --- |
| `scripts/audit-goal-evidence.py` | 新增，严格根/包/新schema审计，不覆盖历史审计器 |
| `scripts/capture-goal-baseline.ps1` | 新增，采集输入/状态/命令与产物对应关系 |
| `scripts/run-goal-validation.ps1` | 新增，运行阶段、传播退出码、恢复记录与最终封存 |
| `scripts/tests/audit-goal-evidence.test.py` | 新增，普通合成证据目录验证审计契约 |
| `scripts/tests/goal-validation.test.cjs` | 新增，用合成子命令测试运行器失败传播与中断恢复 |
| `scripts/source-snapshot-inputs.txt` | 只追加确需纳入的本目标新脚本/测试，不扫入真实配置或证据目录 |
| `ui/js/api.js` | 仅六流程普通响应/错误/缺字段传递；保留原API路径、token逻辑与兼容字段 |
| `ui/js/launcher.js` | 仅六流程加载、取消、失败、重试、按钮忙态、焦点回收；不加新业务入口 |
| `ui/js/i18n.js` | 仅所需中英文状态与重试文案 |
| `ui/css/figma.css` | 仅必要的忙态/失败提示/可见焦点，保持既有整体设计 |
| 六个既有Node测试与 `ui/tests/goal-core-flow.test.cjs` | 既有：instance-removal、file-recovery、bisect-recovery、import-cancel、diagnostic-history、update-recovery；新增文件专放尚未覆盖的行为场景 |
| `dev/PineglowLauncher.Tests/GoalOrdinaryFlowTests.cs` | 仅需要新增的正常API跨层验收；普通合成文件与临时本机服务 |
| `dev/PineglowLauncher.Tests/LocalApiTestHarness.cs` | 仅新增夹具隔离参数与确定性等待；不得改默认生产凭据后端或削弱隔离 |
| PlanRoot内新报告、新correction、新run包 | 不覆盖本轮报告和任何历史封存文件 |

除上表外，所有C#产品实现只读，包括FileRecoveryService、PathSafetyGuard、SecretStore及Backend、ConfigStore、ModReplacementService、UpdateService、LauncherService、LocalApiService。本目标可揭示其普通功能缺口，但不能擅自修复；确需修改则记录具体根因与精确文件，停止依赖项，完成仍独立的其他工作。

### 2.3 一律禁止

不reset/clean/restore/checkout/stash、不批删、不stage-all、不commit/push/PR/标签/发布/部署。不操作真实用户存档、账号、Windows凭据、真实APPDATA配置、真实安装目录。不跑真实模型、不发外部消息、不招募/分发、不增加遥测、不改变防火墙/TLS/代理/权限策略。不复制竞品代码，不启动MR13–MR27新产品能力，不换栈、不做全仓库重构。

本目标**不执行、不委派**先前被平台终止范围内的恢复安全反证，包括恶意恢复记录、跨实例消耗与联接绕过/竞争探针。原测试保留，不删除、不改skip、不弱化断言；用独立执行清单明确NOT_RUN，不能用全量调用暗中带出这些场景。该范围后续只能在正常平台条件允许的专门验收中处理。

## 3. 目标模式、持续推进与完成定义

- 创建一个本机离线质量目标，连续完成G0→G4。
- 子包自检GREEN且证据契约通过，可自动进入下一个**本目标内部**子包；内部推进不是Codex独立验收或正式放行。
- 每个子包均保存checkpoint；不因写了checkpoint就暂停，不每轮询问“是否继续”。
- 每个具体失败最多3轮有假设的修复。重复同一错误、需要越界、平台明确拒绝或必要输入缺失时，记录BLOCKED及影响；不依赖它的工作仍可继续。
- 预算不足、缺工具或等待外部证据不得标“完成”。持久记录剩余项后按客户端能力等待后续输入；不自增预算，不承诺自动提醒。
- 外部安全/GUI/游戏/CI门槛不属于本有界目标的完成条件，但最终报告必须显式列出。它们不自动阻塞纯脚本与普通UI工作，也不因本目标完成而变成PASS。
- G0–G4全部必需本机项通过后，目标状态可完成，含义仅是**这个本机闭环交付完成**。最终产品状态仍为等待Codex验收、FormalPhaseApproval=NOT_GRANTED、ReleaseApproval=NOT_GRANTED。

建议目标描述（登记到Harness目标模式）：

> 在保护松光启动器现有dirty工作区和全部历史证据的前提下，完成Windows本机离线质量闭环：修复证据根/包覆盖与封存顺序，冻结六条普通用户流程的27项行为场景，补齐有证据的UI状态缺口，建立退出码/测试数量/输入SHA可靠的可恢复运行器，最终以新GUID源码副本交付完整机器可审计的候选验收包。只推进本方案G0–G4，不开展受限安全反证、真实模型/用户/游戏/凭据/安装更新、CI与发布。全部本机必需门槛满足后停在“具备独立复验条件，等待Codex独立验收”。

不要为本任务指定未经用户要求的token预算。不要修改会话缓存文件来伪造目标登记；只能使用Harness实际提供的目标模式功能。工具未暴露时如实报告，不声称已创建。

## 4. G0：冻结基线与可执行场景清单

**输入：** R2包、本轮独立报告、SourceRoot真实文件、现有白名单。  
**输出：** execution-root.json、input-manifest.json、requirements.json、expected-evidence.json、checkpoint.json。

- [ ] 保存HEAD、git status逐文件、获准文件基线SHA；确认现有变更与本目标变更分开。
- [ ] 记录SourceRoot/ExecutionRoot/PlanRoot/EvidenceRoot、SDK/Node/Python、运行时间、平台限制。
- [ ] 从tracked文件与显式白名单构建副本，排除真实配置/密钥。复制后逐项SHA核对；不要复用旧二进制或旧GUID副本。
- [ ] 冻结六流程27个场景，每项包含输入、实际生产入口、可观察预期、证据层级、既有测试ID、缺口和本目标方法。
- [ ] 冻结执行方法白名单；把受限测试与真实环境项目列为NOT_RUN，不改原测试定义。
- [ ] 写明每个依赖项；已有证据充分的场景复用既有行为测试，不为增长用例数添加重复测试。
- [ ] checkpoint状态BASELINED，基线与输入清单审计通过后进入G1。

requirements.json最小接口：

```json
{
  "schemaVersion": 1,
  "scope": "windows-local-offline-quality",
  "items": [
    {
      "id": "RECOVERY-04",
      "required": true,
      "entry": "openRecoveryPanel",
      "fixture": "synthetic partial recovery response",
      "expected": "show failed files; preserve a retry action; never show full success",
      "layer": "node-behavior",
      "testId": "actual method or node subtest name",
      "evidence": "relative output path"
    }
  ]
}
```

testId必须由执行时实际方法填充；上例是字段契约，不允许把说明文字当作实测值。

## 5. G1：严格证据门槛与封存修复

**修改/新增：** audit-goal-evidence.py、其Python测试；capture-goal-baseline.ps1的终态采集；新correction包。  
**只读：** 两份历史证据根、R2旧清单、旧审计脚本与失败记录。

expected-evidence.json接口：

```json
{
  "schemaVersion": 1,
  "roots": [
    {
      "id": "r2",
      "path": "absolute path captured from the real baseline",
      "required": true,
      "packages": [
        {
          "relativePath": "FINAL-REMEDIATION-R2/20261003-071724Z",
          "runId": "20261003-071724Z",
          "schemaPolicy": "strict-new",
          "required": true
        }
      ]
    }
  ],
  "exceptions": []
}
```

执行时必须装入实际绝对根与完整包清单：历史ed41、R2根0f45、当前目标新根；不得只写示例中的R2一个包。新包数量由冻结清单计算，不把61当永久常数。legacy-policy只适用于显式登记的老包。

- [ ] 在全新合成目录先复现本轮覆盖反例，保存RED与真实exit；不碰业务恢复安全反证。
- [ ] 每个required根存在且满足该根自己的覆盖清单；不能把missing根过滤掉。
- [ ] 每个required包有唯一manifest、runId与目录一致；同级未登记运行包必须报错，明确允许的包外报告目录必须单列。
- [ ] strict-new要求非空files、唯一规范化路径、非负整数size、64位SHA、真实文件大小/哈希、detached。空包只允许有显式N/A理由且不用于证明测试完成。
- [ ] 兼容files/entries/artifacts时采用显式schemaPolicy；不能缺字段自动回退空集合。
- [ ] 历史例外绑定旧manifest自身SHA、路径、记录旧SHA/size、当前SHA/size、原因和来源。重复登记、未消费的预期例外、新漂移均显式失败或单独策略声明，不能靠路径豁免。
- [ ] 把R2 source-after漂移放进新correction：保留旧manifest和当前字节、附原工具调用顺序。无法恢复原字节则写UNKNOWN，不能生成一个“原始版本”。
- [ ] 不把新增correction当成静默原包PASS：原包封存失效仍记录，最终只有“已显式纠正的历史基线”与新包严格通过。
- [ ] 终态采集→最终内容完成→封存→包外独立审计，之后包内禁止再写。后续修改必须用新runId。
- [ ] 全部合成门槛通过、真实树覆盖吻合、例外单列后进入G2。

必须的合成验证，至少覆盖以下19类、逐项保存实际stdout/stderr/exit：完整有效包；恰好登记例外；空单根；混合根中缺失required根；混合根中空required根；未封存同级包；空新manifest；缺size；缺runId；错误runId；重复条目；缺失文件；包内漏登记；未知SHA漂移；已知路径第三种字节；错误detached；新包缺detached；自引用；封存后重写。正例exit0，负例非零且明确给出原因，异常崩溃不能作为正确拒绝。

新增两类边界：已登记旧例外对应的manifest自身变化；声明package count与实际不符。均非零。真实旧包不修改，合成目录保留。

## 6. G2：六流程27项普通行为证据与最小UI闭环

**可改：** 上表限定的UI文件、六个Node测试、新goal-core-flow测试。  
**接口：** 使用现有SG.api，错误对象既有status/payload必须保留；不改路由语义或C#产品协议。静态扫描仅作辅助，关键场景至少运行实际UI函数或API归一化函数。

| 场景组 | 数量 | 冻结预期 |
| --- | ---: | --- |
| INSTANCE-01..04 | 4 | 取消不请求；确认从列表移除且文案清楚；移除选中项后选择态正确；请求期间重复点击不产生第二次请求 |
| RECOVERY-01..06 | 6 | 查询失败可重试；空列表；正常恢复成功；partial/failed展示逐文件结果；conflict不说成功覆盖；提交期间按钮忙态且失败可重试 |
| BISECT-01..05 | 5 | 无pending才出现正常开始；pending有恢复/继续选择；pending查询失败明确错误与重试；普通文件占用失败保持需恢复状态；恢复失败不显示全部完成 |
| IMPORT-01..04 | 4 | 取消前有operationId绑定；受理前取消反馈准确；受理后取消展示实际结果；取消失败/未知operation不说已取消 |
| DIAGNOSIS-01..04 | 4 | 无Key本地结果可用；空历史；历史读取失败可重试；未保留原证据导出明确说明 |
| UPDATE-01..04 | 4 | 无上次结果不误报；上次未替换明确仍为旧版；缺兼容字段不崩；状态查询失败不显示更新成功 |

27个场景不是27个必需新增测试：已有行为覆盖可直接映射，缺少的才补。至少记actual production entry、request list、DOM状态/按钮可用性、状态文本与失败分支。

已观察的具体入口：launcher.js的openModBisect在pending查询catch中直接忽略错误并显示开始状态。先用普通fetch失败模拟产生RED，预期显示“无法确认上次状态，重试”；修复仅该UI查询失败分支，不修改服务端恢复安全。不能借此开展越界恢复探针。

- [ ] 先运行既有六类Node测试，登记哪些是行为、哪些仅静态。
- [ ] 对缺口构造有效RED，使用真实UI函数与可控响应/DOM夹具；夹具崩溃不是产品RED。
- [ ] 仅修复被RED证明的正常流程状态缺口。已通过项保持原实现；不要新建不存在的更新/恢复业务入口。
- [ ] 每个修复GREEN后跑对应文件及相关API回归。
- [ ] 所有27场景均为行为PASS或有证据的等价既有覆盖后进入G3。把需要真实WebView的证据单列未验证，不能用synthetic DOM替代。

## 7. G3：正常API与状态/磁盘闭环

**可新增：** GoalOrdinaryFlowTests.cs；仅必要的LocalApiTestHarness隔离参数。  
**产品实现只读。** 发现产品缺陷需改C#服务时，报告精确位置与复现，停止该依赖，不随意扩白名单。

优先已有命令与本轮已通过方法：

```powershell
# 先在本目标新GUID副本中运行，results目录为新run包，日志保留实际exit。
dotnet test 'dev/PineglowLauncher.Tests/Pineglow.Launcher.Tests.csproj' -c Release --filter 'FullyQualifiedName~ConfigPreservationTests|FullyQualifiedName~SecretStoreBackendTests' --logger 'trx;LogFileName=secrets.trx'
dotnet test 'dev/PineglowLauncher.Tests/Pineglow.Launcher.Tests.csproj' -c Release --no-build --filter 'FullyQualifiedName~RollbackFailure_HttpExposesStructuredRecoveryFields|FullyQualifiedName~RollbackSuccess_HttpReportsNoRecoveryNeeded|FullyQualifiedName~MetadataFailure_HttpExposesStructuredFields|FullyQualifiedName~ModReplaceUndo_NormalRestore_StillWorksWithTrustedRoot|FullyQualifiedName~SecretBackend_ConcurrentAsyncFlowsStayIsolated' --logger 'trx;LogFileName=ordinary-r2.trx'
dotnet test 'dev/PineglowLauncher.Tests/Pineglow.Launcher.Tests.csproj' -c Release --no-build --filter 'FullyQualifiedName~DeleteMod_QuarantinesJarAndSidecar_KeepsByteIdenticalCopies|FullyQualifiedName~DeleteMod_DisabledMod_QuarantinesDotDisabledName|FullyQualifiedName~Restore_PutsJarAndSidecarBackByteIdentical|FullyQualifiedName~Restore_Twice_IsIdempotent_AndKeepsFileContent|FullyQualifiedName~Restore_WithDifferentFileAtOriginalPath_ReportsConflictAndDoesNotOverwrite|FullyQualifiedName~DeleteMod_LeavesEveryOtherFileUntouched' --logger 'trx;LogFileName=recovery-normal.trx'
$nodeInputs = @(Get-ChildItem 'ui/tests' -Filter '*.test.cjs' | Select-Object -ExpandProperty FullName)
node --test @nodeInputs
dotnet build 'dev/PineglowLauncher/VerifyHost/VerifyHost.csproj' -c Release
& 'dev/PineglowLauncher/VerifyHost/bin/Release/net10.0/VerifyHost.exe' --bisect-smoke
```

- [ ] 对服务层已有测试先读副作用，建立实际方法白名单；从ImportCancellationTests、BisectRecoveryTests、DiagnosticReportTests等选择普通已获准的方法。
- [ ] 对比UI场景与API/磁盘状态：同一operationId、同一状态与失败明细；使用普通隔离夹具，禁止伪造攻击记录或越界路径。
- [ ] 缺少正常跨层证据时才新增GoalOrdinaryFlowTests，至少覆盖真实本机HTTP返回与磁盘/记录一致性，不能只断言HTTP200。
- [ ] 重复操作、正常服务重建后的查询、解除普通占用后的重试，保证状态反馈真实；不做竞态攻击、真实游戏或杀用户进程。
- [ ] 记录baseline24/238/19用于变化解释，最终数量从TRX/Node实际输出读取。新增方法、子集与重复运行不能累加成“全量”。
- [ ] 不使用带受限场景的全量C#调用；所执行集合称“本目标获准常规回归”。required集合漏项、skip或0测试均失败。
- [ ] 保持原生凭据调用增量0，测试所有运行根是GUID临时目录；任一夹具无法隔离则停止该项。

## 8. G4：可恢复质量运行器、封存与最终交付

**新增：** run-goal-validation.ps1、运行器自身Node测试；capture-goal-baseline.ps1；新最终run包。  
**消费者：** 使用G0输入与方法清单、G1严格审计、G2/G3证据。  
**产出：** machine-verdict.json、scenario-matrix.json、changed-files.json、final-report.md、manifest.json、manifest.sha256、包外审计结果。

每个stage记录：

```json
{
  "id": "node-regression",
  "inputManifestSha256": "actual 64-character digest",
  "startUtc": "actual ISO timestamp",
  "endUtc": "actual ISO timestamp",
  "workingDirectory": "actual isolated path",
  "command": "redacted actual command",
  "exitCode": 0,
  "executed": 238,
  "passed": 238,
  "failed": 0,
  "skipped": 0,
  "rawEvidence": ["relative stdout log", "relative structured result"],
  "status": "PASS"
}
```

以上是接口例子；新实际运行必须填实测值，不能沿用本轮238或复制example摘要。

运行器规则：

- 依赖restore/build/test/smoke/audit逐阶段判断真实退出码；捕获子进程结束后立即保存exit，不让Out-File覆盖。
- 无TRX/非法TRX/0测试/required skip/计数不符/后台仍运行/超时均不能PASS。
- 每项绑定同一冻结input SHA与产物SHA。先已有聚焦、再相关回归与全部Node；不因想“更全”跑受限集合。
- checkpoint仅存状态和证据引用，不能自行授权新范围。
- 恢复时确认相同输入SHA、命令、产物与已封存证据；不匹配的新stage采用新runId，保留旧失败/未完成记录。
- 已完成stage且所有绑定一致才可复用，不把“曾启动”视为“已完成”。
- 源目录保护终态与changed-files先采集，最终报告内容完成后再封存。封存之后只写包外结果，最终自动检查包内哈希没有变化。

自身合成测试至少6项：stage非零传播；缺TRX；0测试；required skip；中断时没有完成证据不得复用；输入变化拒绝复用。另做2次运行演示：第一次让本轮拥有的普通合成stage失败并保留，第二次恢复只复用有完整证据的stage。不得终止用户现有实例或覆盖旧输出。

最终必需门槛：

1. G0真实根与输入清单完整，源改动仅白名单；所有未知并发漂移已说明。
2. G1合成门槛全部符合预期；全部required根/包被检查；新包无未登记漂移；历史差异有明确旧包状态和correction，不能宣称原始全部一致。
3. 六流程27个required场景全部具备行为证据；未验证真实环境单列。
4. 本目标正常C#集合、全部Node、VerifyHost、Bisect通过；非零数量、无required skip、退出码可靠。
5. 运行器自身门槛与一次失败→恢复流程通过；同输入完成stage才能复用。
6. final包封存后再次包外审计与哈希校验通过；没有seal后写包内的操作。
7. 最终报告写清实施变化、原始RED/GREEN、唯一测试数、未验证、安全限制、Codex待验收点。

machine-verdict应区分 `ImplementationReady=true` 与 `IndependentAcceptance=NOT_GRANTED`；只用ready决定是否结束实施，不让ready自动授予阶段批准。

## 9. 失败、回退与终态

每个失败记录根因假设、最多3轮尝试、精确差异与原始证据。夹具/环境/平台/产品缺陷分开；普通工具故障有界恢复，明确拒绝不换工具绕过。

本目标不自动回退产品。需要撤回本目标变化时，只对本轮精确diff做逆向补丁并核对并发资产；不能用完整旧文件覆盖dirty工作区。

完成时交付：最终报告、27项场景矩阵、输入/改动清单、运行器命令、严格审计结果、旧差异correction、剩余门槛表。结束句固定：

**具备独立复验条件，等待 Codex 独立验收。**

只完成本目标，停止；不提交、不推送、不发布、不自动恢复完整MR00–MR27。若必需本机项失败，报告BLOCKED与剩余项，不能把目标标complete。

