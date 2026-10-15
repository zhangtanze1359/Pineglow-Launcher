# 给 DeepSeek Harness 的目标模式执行提示词（2026-10-03）

当前状态：已制定目标，未向Harness发送、未创建或启动Harness目标。将下面完整代码块提交到Harness，才构成本提示词所列范围的实施指令。

建议使用松光启动器的空白Harness会话，直接读取这两份材料。旧实施会话可只读引用其日志，避免让完整历史上下文替代本轮明确目标和边界。

```text
请使用你实际提供的目标模式，把下面目标登记为一个持续任务，并按详细流程连续完成G0–G4。不要只给计划，不要每完成一个内部子包就问是否继续；本次提交授权的是这里明确列出的本机离线质量闭环。没有暴露目标模式工具时如实报告，不修改会话缓存来伪造登记。不要设置未经我要求的token预算。

目标：
在保护松光启动器现有dirty工作区和全部历史证据的前提下，完成Windows本机离线质量闭环：修复证据根/包覆盖与封存顺序，冻结六条普通用户流程的27项行为场景，补齐由有效RED证明的UI状态缺口，建立退出码/测试数量/输入SHA可靠的可恢复运行器，最终以新GUID源码副本交付完整、机器可审计的候选验收包。只推进本方案G0–G4。全部本机必需门槛满足后停在“具备独立复验条件，等待 Codex 独立验收”。

先完整读取：
1. C:\Users\Administrator\.codex\worktrees\34ca\松光启动器\docs\deepseek-harness-r2-independent-acceptance-2026-10-03.md
2. C:\Users\Administrator\.codex\worktrees\34ca\松光启动器\docs\deepseek-harness-long-term-goal-workflow-2026-10-03.md
3. C:\Users\Administrator\Documents\Codex\2026-10-03\codex-threads-01a100ce-3590-7090-aec4\outputs\AGENTS-精简规则-2026-10-03.md
4. C:\Users\Administrator\Documents\Codex\2026-10-03\codex-threads-01a100ce-3590-7090-aec4\outputs\Codex执行与独立验收规则-完整版-2026-10-03.md

固定事实：
SourceRoot=D:\松光启动器
PlanRoot=C:\Users\Administrator\.codex\worktrees\34ca\松光启动器\docs
R2Package=C:\Users\Administrator\.codex\worktrees\0f45\松光启动器\docs\market-execution\FINAL-REMEDIATION-R2\20261003-071724Z
历史ed41根=C:\Users\Administrator\.codex\worktrees\ed41\松光启动器\docs\market-execution
新EvidenceRoot=PlanRoot\market-execution\LONG-QUALITY-GOAL\真实UTC时间戳-GUID
ExecutionRoot=SourceRoot显式输入白名单复制出来的新GUID临时副本

34ca不是当前产品源码。当前产品HEAD f145d14eb26b0b7459a28491e764ac8c573085d3，仅作为历史提交标识；每次重新检查dirty源码、状态与输入SHA。

本轮独立验收结论：TechnicalAcceptance=NOT_PASSED。已经独立通过普通C#24/24、Node238/238、Bisect19/19与VerifyHost构建；895/895是你的原始全量证据，不能写成本轮Codex独立复跑。
真实树审计exit1：R2 source-after.json与封存SHA不符。原始tool/call seq1351先seal与audit，再capture-baseline -Tag after，所以封存后文件被重写。保留原件，在新correction处理，不重算旧manifest掩盖失败。
新增覆盖检查发现：混合根中required根缺失/为空、同级未封存包被漏检；空新manifest与缺size也能exit0。原12例否证已经符合预期，需扩大目标与schema门槛。

按G0–G4连续推进：
G0：保存HEAD/status/输入SHA/白名单差异，冻结27场景、正常测试方法清单、required根与包清单、checkpoint。
G1：新增严格证据审计器。逐required根和包校验，不把缺失根过滤掉；新schema必需files/size/SHA/runId/detached；例外绑定旧manifest与旧/当前字节；保留R2漂移；采集终态必须早于封存，封存后只写包外。
G2：六流程27场景：实例移除4、普通恢复6、Bisect5、导入取消4、诊断历史4、更新状态4。使用实际UI/API函数与受控响应做行为验证，静态扫描仅辅助；已覆盖的不增加重复测试。openModBisect的pending查询失败当前被忽略，先用普通查询失败产生有效RED，再仅补状态错误与重试反馈。
G3：在隔离副本运行获准普通C#/Node/API与磁盘一致性验证。现有24/238/19作基线，实际数量从TRX与日志读出；读副作用后仅添加必要的正常跨层证据，默认内存凭据后端。
G4：新增可恢复质量运行器。子进程exit可靠传播，缺结果/0测试/required skip/超时/仍运行均不PASS；输入或产物变化不能复用旧stage。演示一次普通合成stage失败→恢复，保留失败；完成最终报告和源保护后封存，再做包外独立审计。

精确允许修改范围以详细流程§2.2为准，主要是：
scripts/audit-goal-evidence.py
scripts/capture-goal-baseline.ps1
scripts/run-goal-validation.ps1
scripts/tests/audit-goal-evidence.test.py
scripts/tests/goal-validation.test.cjs
scripts/source-snapshot-inputs.txt（只追加必要新输入）
ui/js/api.js、ui/js/launcher.js、ui/js/i18n.js、ui/css/figma.css（仅六流程普通状态反馈）
六个既有Node流程测试、ui/tests/goal-core-flow.test.cjs
dev/PineglowLauncher.Tests/GoalOrdinaryFlowTests.cs
dev/PineglowLauncher.Tests/LocalApiTestHarness.cs（仅必要隔离参数与等待）
新报告/correction/新EvidenceRoot

所有C#产品服务实现只读，尤其FileRecoveryService、PathSafetyGuard、SecretStore及Backend、ConfigStore、ModReplacementService、UpdateService、LocalApiService。需要越出白名单时只停止对应依赖项，写精确根因与请求范围；仍独立的工作继续。

限制：
- 不执行或委派先前被平台终止范围内的恢复安全反证；不通过换模型/工具/执行者补做。原测试保留，执行清单明确NOT_RUN，不改skip，不弱化断言，不用全量C#调用暗中带出受限场景。
- 不动真实APPDATA配置、Windows凭据、真实账号/游戏/存档/安装更新，不跑真实模型，不发外部消息，不招募/分发，不改安全机制或权限策略。
- 不reset/clean/restore/checkout/stash，不批删，不stage-all，不commit/push/PR/发布/部署/标签，不启动MR13–MR27，不换栈或大重构。
- 不覆盖旧RED、日志、manifest、detached、R2和本轮验收材料。普通工具故障有界恢复，明确拒绝立即停止对应范围。
- 每个实际失败最多3轮有假设的修复；失败原因分product/fixture/environment/platform/baseline。预算不足或必需项缺证据不得complete。
- G0–G4内部在自检与证据门槛通过后自动推进，不将其叫作Codex独立验收或阶段批准。

交付：
每个检查记录香港时间、cwd、命令/操作、真实exit、唯一测试数/skip、输入与产物SHA、raw证据、预期/实际、状态。每个子包有checkpoint、最小diff和恢复记录；终态为新封存包，旧差异correction、27场景矩阵、机器verdict、精确改动清单、可复跑命令、失败→恢复证据。
真实GUI/WebView2、Minecraft、原生凭据往返、真实更新交接、跨平台、远程CI与受限安全验收必须继续标未验证。它们不是本机闭环的完成条件，也不会因本目标完成自动通过。

只有G0–G4全部必需本机门槛通过，才可把“这个本机质量交付目标”标完成；产品独立验收/正式阶段/发布仍未批准。
最终必须记录：
ImplementationReady=true（仅当全部本机必需项通过）
IndependentAcceptance=NOT_GRANTED
FormalPhaseApproval=NOT_GRANTED
ReleaseApproval=NOT_GRANTED
终止语：具备独立复验条件，等待 Codex 独立验收。

若必需本机项仍阻断，明确列剩余项与影响，不得标完成或自动扩范围。
```

使用本提示词后，让Harness在这个单一目标内部持续执行。最终提交上述新包给Codex独立验收即可，不需要把“技术ready”改写成产品放行。

