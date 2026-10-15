# DeepSeek Harness 长线质量目标独立验收（2026-10-04）

**技术结论：不通过（FAIL）。** Harness 目标缓存已为 complete，最新最终回复声称 G0–G4 完成；独立复验确认有真实交付和大量通过项，但 G2 导入终态存在产品缺陷，G4 运行器存在可重复的误报 PASS。不能接受“全部必需门槛满足”的 ImplementationReady=true。

FormalPhaseApproval=NOT_GRANTED。ReleaseApproval=NOT_GRANTED。本次只验收与形成返工材料，没有修复产品、提交、推送、发布或派发下一轮。

## 1. 对象、时间与现场

- Harness 会话：`session-dac78285-9e76-47df-82d0-b649aec12da4`；最新最终 assistant/message 为 seq=1643，后续 turn/end 为 completed。
- Harness goal：`goal-1f34d5e3-a907-473d-ae66-b83e73fd459a`，缓存 phase=complete，roundsStarted=3。这是执行者状态，不是独立技术验收。
- 交付 runId：`20261003-092612Z-b4d896a8`；包封存时间为 2026-10-03 19:32:28 +08:00。缓存文件于 10 月 4 日更新，不能据此把实际完成时间改成 10 月 4 日。
- SourceRoot：`D:\松光启动器`；HEAD=`f145d14eb26b0b7459a28491e764ac8c573085d3`，main，dirty。
- 最终包：`C:\Users\Administrator\.codex\worktrees\34ca\松光启动器\docs\market-execution\LONG-QUALITY-GOAL\20261003-092612Z-b4d896a8\FINAL-LOCAL-QUALITY\20261003-092612Z-b4d896a8`。
- 独立证据：`C:\Users\Administrator\.codex\worktrees\34ca\松光启动器\docs\market-acceptance\20261004-long-quality-independent`。
- 独立执行副本：`C:\Users\Administrator\AppData\Local\Temp\codex-quality-acceptance-17hncq03`；292 个输入逐项复制、核 SHA。本轮构建和测试只在该副本及本轮普通合成目录执行。
- 结束时292个SourceRoot输入与开始时一致，git status文本一致，HEAD未变。没有清理、回退、覆盖既有用户改动。临时副本和合成夹具保留供复验，无持续运行的本轮服务。

适用规则为用户指定的三份 AGENTS/任务模板/完整执行规则；需求来自 `deepseek-harness-long-term-goal-workflow-2026-10-03.md` 的 G0–G4。此次未触发此前受限恢复安全反证，不以替换模型、工具或执行者补做。

## 2. 必需阶段与独立结果

| 阶段 | 独立结果 | 证据与含义 |
| --- | --- | --- |
| G0 冻结输入、隔离执行 | PASS | 基线286，终态292；新增6个目标工具/测试输入，删除0；7个既有输入变化与交付记录一致。当前292个输入与终态封存清单完全匹配。 |
| G1 严格审计与封存 | PASS（本轮规定门槛） | 既有合成26/26；真实3根/62包/1022条目，0失败、3个已登记历史漂移。最终包148/148、0漏登记、detached SHA匹配。本轮未发现R2式封存后漂移。 |
| G2 六流程27场景 | FAIL | 既有28/28测试通过并纳入Node266；但 IMPORT-03/04只检查取消响应当刻，未覆盖随后的导入终态。独立生产入口反证1对照PASS、2缺陷FAIL。 |
| G3 常规回归 | PASS（既定选集） | C#实际执行73/73，0skip；Node266/266，0skip；测试项目构建0error；VerifyHost构建0error；Bisect19/19。不是全量C#或真实GUI验收。 |
| G4 可靠运行与恢复 | FAIL | 既有10/10合成测试通过，但独立7个契约反证均被错误判为exit0/PASS。输入、计数、产物、runId和空执行范围门槛不成立。 |

阶段合计：3/5通过，2/5失败；失败是本轮直接证明的产品/工具契约缺陷，不是缺少GUI导致的暂缓。

## 3. 必须返工的问题

### LQ-01 [P1] 取消意图被当成服务端确认取消

位置：`D:\松光启动器\ui\js\launcher.js:4025` 与 `:4049`。点击取消立即设置 cancelRequested=true；随后 importInstance 抛任何错误，catch 都显示“导入已取消”和取消完成提示。

独立调用真实 `handleImportFiles`，只替代普通 API 响应与 DOM，不修改产品代码：

1. importInstance保持pending → cancelImport抛unknown-operation → 前一刻正确显示“取消失败” → importInstance抛普通transport错误 → 最终错误地显示“导入已取消”。
2. cancelImport返回pendingCancel=true/ok=false → 正确显示“已记录取消” → importInstance抛“server still working” → 最终同样误报取消完成。
3. 对照确认实际cancelImport与importInstance使用同一operationId，PASS。因此失败来自终态分支，不是operationId夹具故障。

原 IMPORT-03/04 测试在 rejectImport 之前断言，再 await running 后没有最终文案断言，故普通266项通过不能反驳上述缺陷。影响：用户会误以为导入已中止、没有生成实例；当前离线证据并不证明真实服务器一定留下实例。

证据：`import-terminal-disproof.log`（exit1，3执行/1通过/2失败/0skip）、`import-terminal-disproof.test.cjs`。普通非安全功能反证。

### LQ-02 [P1] 实际输入变化仍可复用旧成功结果

位置：`scripts/run-goal-validation.ps1:291` 和 `Test-Reusable`。仅哈希InputManifest自身字节；不解析其inputs并核对执行根实际文件。

反证：先完成一个合成stage，保持清单字节、命令、日志与产物不变，修改清单中列明的source.txt，再Resume。实际exit0、PASS、reused=true。当前真实292输入恰好一致，不能推出未来续跑也可靠。

### LQ-03 [P1] 接受旧TRX及没有实际执行测试的计数

位置：`scripts/run-goal-validation.ps1:181` 起与 `:193` 起。

- 预放一个通过的unit.trx，子命令只输出日志并exit0、不写TRX：仍COMPLETED/PASS。运行器没有确认本次attempt生成了TRX。
- 子命令写total=3、executed=0、passed=0、failed=0、notExecuted=0：仍PASS。只检查total和失败/skip，没有核对executed/passed、计数一致性及真实结果条目。

必须分别阻止旧证据污染与虚假执行计数，不能只扩大现有minTests。

### LQ-04 [P1] 必需产物变化没有参与stage指纹

位置：`scripts/run-goal-validation.ps1:97` 的Get-StageFingerprint；produces缺失。

反证：成功stage之后，StagesFile只追加必需产物missing-new-output.bin；该文件并不存在。Resume仍REUSED/PASS，只检查旧记录中的旧产物。

交付g4 build/VerifyHost/smoke阶段还声明produces=[]，构建产物没有SHA绑定；这属于额外静态覆盖缺口，本轮没有修改真实构建产物制造反证。

### LQ-05 [P1] 不兼容Resume覆盖旧记录且沿用旧runId

位置：`scripts/run-goal-validation.ps1:334` 起。

反证：完成stage后使清单SHA变化，Resume同目录、同runId。确实未复用，但又COMPLETED/PASS，仍same-run-id；stages目录只有unit.json，原COMPLETED记录未归档。因为归档条件明确排除了Resume下的旧COMPLETED记录，随后同名stdout/stderr/TRX也会重写。

这违反“输入不匹配采用新runId、保留旧记录”的原需求。仅拷贝旧stage JSON也不足以保留其引用的原始日志/产物。

### LQ-06 [P2] 空执行列表也生成整体PASS

位置：`scripts/run-goal-validation.ps1:352` 起。

空stages=[]以及-Only选择不存在id两种独立反证都exit0、stageCount=0、PASS。对目标模式而言，空检查不能代表整体质量门槛完成。

LQ-02至06原始证据为 `runner-disproof-results.json`，7个独立case；`runner-fixtures-preserved`保存了每次完整调用参数、stdout/stderr、exitCode和verdict，复验可重新运行 `runner-disproof.cjs`（该采集脚本自身exit0表示成功收集反证，不表示运行器符合要求）。

## 4. 通过项与环境校正

TRX由独立XML解析，同时核对UnitTestResult条数和outcome：Secrets13、R2普通5、Recovery普通6、六流程49，共73/73/0skip。详情见 `acceptance-summary.json` 与 `results/*.trx`。

Node266包含本轮新增goal-core-flow的28项；不能把266与28重复相加。C#构建19warning/0error，VerifyHost5warning/0error。Bisect实际日志19个[PASS]，无[FAIL]。

运行器既有10项首轮由于父PowerShell7模块路径影响WindowsPowerShell5.1的Get-FileHash加载，全部未有效执行。原始 `runner-tests.log` 和exit1保留。只在本轮测试子进程环境显式指定WindowsPowerShell模块路径后，原样10项全部通过，证据 `runner-tests-ps51.log`。未改系统配置或产品脚本；首轮是环境故障，不能冒充产品RED。

本轮最终包manifest SHA=`7B09EA75`开头，完整值见 `package-integrity.json`；单独核对全部148文件、字节数、SHA、漏登记与detached。历史审计的3个knownDrift属于显式登记，不能被描述为历史证据恢复无缺陷。

## 5. 范围与下一步

未验证：真实Avalonia/WebView2 GUI、真实游戏、原生凭据、真实更新替换、跨平台、干净提交/远程CI与发布。此前RA-01/02仍保持范围限制；`ImportWithALiveTokenButCancelledMidway`未执行，所报73项是明确筛选集合，不是全量C#。

建议继续当前工程目标的**有限返工**：先修LQ-01的终态并补2个断言，再修LQ-02至06运行器门槛，采用全新runId和GUID源码副本重跑同一获准选集，最后封存并独立验收。G0/G1已有有效产物可复用为只读历史，不能改写这个旧包或将complete缓存改成已验收。

详细方案：`deepseek-harness-long-quality-rework-workflow-2026-10-04.md`。
可复制提示词：`deepseek-harness-long-quality-rework-prompt-2026-10-04.md`。

当前授权的独立验收已完成；以上材料尚未向Harness发送，实施需用户把提示词提交给Harness或明确授权派发。
