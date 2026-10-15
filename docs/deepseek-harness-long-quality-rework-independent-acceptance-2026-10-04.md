# DeepSeek Harness 有限返工独立验收（2026-10-04，本日第二次验收）

**技术结论：FAIL，不通过。** 本轮确实修复了上次导入终态缺陷和七个运行器具体反证，常规回归也全部通过；但独立新增验证发现运行器仍有三个证据绑定缺口，新包不符合既定strict-new封存契约。因此不能接受“六项全部关闭、implementationReady=true”的整体结论。

FormalPhaseApproval=NOT_GRANTED。ReleaseApproval=NOT_GRANTED。本次只做独立验收和文档交付，没有修复产品、派发实施、提交、推送或发布。

## 1. 本轮对象与现场

- Harness会话：`session-0c1a0106-755e-481b-9691-c64985458e9a`，最终assistant/message seq=1021，turn/end seq=1024为completed；最终消息时间为2026-10-04 18:30:38 +08:00。
- goal=`goal-f103697d-0cda-43d3-b5ff-8032d9f325b6`，缓存phase=complete、roundsStarted=1。缓存状态只是执行者结束状态。
- 实施证据根：`C:\Users\Administrator\.codex\worktrees\34ca\松光启动器\docs\market-execution\LONG-QUALITY-REWORK\20261004-092504Z-dab448e2`。
- 最终包：上述根的 `r4\FINAL-LOCAL-QUALITY\20261004-092504Z-dab448e2-rework`。
- SourceRoot=`D:\松光启动器`，main，HEAD=`f145d14eb26b0b7459a28491e764ac8c573085d3`，dirty。
- 本次独立执行根：`C:\Users\Administrator\AppData\Local\Temp\codex-rework-acceptance-ap4aublk`，从292个终态输入逐项核SHA并复制，不使用旧bin/obj。
- 独立证据目录：`C:\Users\Administrator\.codex\worktrees\34ca\松光启动器\docs\market-acceptance\20261004-long-quality-rework-independent`。

实际改动仅六个既有文件：launcher.js、i18n.js、import-cancel.test.cjs、goal-core-flow.test.cjs、run-goal-validation.ps1、goal-validation.test.cjs；无新增/删除输入。R0的292输入与上次独立验收基线完全一致，说明本轮RED对应未修版本。接受该修改范围，不把其他dirty工作计为本轮成果。

结束时SourceRoot与独立执行根292输入均无漂移，SourceRoot git status文本和HEAD未变；上次独立验收124个文件与旧目标包148条目均再次核验无变化。失败和反证夹具保留在本次证据目录，未清理用户文件或终止用户进程。

## 2. 门槛裁定

| 门槛 | 独立结果 | 依据 |
| --- | --- | --- |
| R0 现场、真实RED与历史保护 | PASS | R0清单与上次292输入相同；只修改六个允许文件；旧证据完整 |
| R1 导入终态 | PASS（本轮既定场景） | 原独立三项反证3/3、0skip，断言原样，仅重绑ROOT；当前完整Node269/269 |
| R2 可靠运行器 | FAIL | 既有28/28和原七反证通过；独立13项契约检查10通过、3失败，见RW-01至03 |
| R3 正常回归 | PASS（选集与离线层） | C#73/73、Node269/269、审计26/26、两处构建0error、Bisect19/19 |
| R4 严格新包封存 | FAIL | 文件完整性121/121、包外digest匹配；strict-new审计exit1、MISSING_DETACHED，见RW-04 |

3/5门槛通过，2/5失败。此次FAIL来自已经复现的契约缺陷和封存格式不符合，不由范围外GUI缺口推导。

## 3. 已确认修复与通过项

导入取消API失败→导入普通失败，以及pendingCancel→导入普通失败，两种终态现均保留“失败/结果未确认，请核对实例列表”，不再误报“导入已取消”。同operationId对照通过。原测试断言保留，新增最终await后的断言有效。

原七个运行器反证现得到正确拒绝：实际输入改变但清单不变；旧TRX未由子命令生成；0executed计数；produces变化；清单SHA变化时不兼容Resume；空stages；unknown Only。另有独立有效stage/不变Resume对照、失败→恢复、合法Only→全量恢复三个对照通过，共10项。

失败恢复对照真实子命令exit=7；第二attempt通过；第一attempt日志/TRX逐项SHA不变，旧失败记录有字节相同归档。合法Only返回PARTIAL/exit3，随后全量Resume复用已完成stage并完成另一个stage。

独立执行计数：

| 检查 | 结果 |
| --- | --- |
| 常规C#选集 | 13+5+6+49=73执行/73通过/0失败/0skip；TRX计数与UnitTestResult条目一致 |
| 全部Node UI测试 | 269/269，0skip；包括goal-core-flow31项，不重复相加 |
| 运行器既有测试 | 28/28，0skip；本轮子进程明确使用WindowsPowerShell模块目录，无全局配置修改 |
| 审计器既有测试 | 26/26 |
| 旧3根覆盖审计 | 62包/1022条目，0失败、3个显式knownDrift |
| 测试项目/VerifyHost构建 | 0error，分别19warning和5warning |
| Bisect smoke | 19个[PASS]，0[FAIL]，exit0 |

查看了实施者原级联10个stage的真实exit0记录和Resume10个REUSED记录；本次独立执行采用自己的单项回归和契约检查，不将阅读原级联日志写成独立运行了同一10-stage级联。

实施者重绑旧运行器反证的合成子命令为attemptDir、补TRX结果行，是适配新有效接口的合理动作；但“expected字符串没改”本身不能证明夹具有效。本次使用独立新仪器并在正例、首轮成功、失败恢复和部分执行对照上亲自验证，没有仅因旧负例非零退出就判修复成功。

## 4. 尚需处理的四项

### RW-01 [P1] 验证根与实际执行根可以不同

位置：`D:\松光启动器\scripts\run-goal-validation.ps1:169`、`:574`、`:621`和`:649`。

Test-InputSet从InputManifest取得并核验目录A；`-ExecutionRoot`独立参与占位符，未与已核inputRoot核对。run-binding记录两种根，但不拒绝二者不一致。

独立普通合成反证：清单列明A中的source.txt和child.cjs；B中child.cjs相同，source.txt实际为不同字节；传入ExecutionRoot=B，stage.cwd={executionRoot}，子命令读B/source.txt并记录 `source=different actual input`。实际exit0、PASS，verifiedInputRoot仍为A。

影响：可把未核验副本的测试结果绑定到另一个副本的输入SHA。不是证明本轮真实269/73测错根，本轮自己的292复制已核对；缺陷影响运行器后续使用可靠性。至少同一声明的ExecutionRoot必须与清单核验根规范化相同，不能只把两者写入JSON。

### RW-02 [P1] 阶段中冻结输入漂移，最终仍PASS

位置：`scripts/run-goal-validation.ps1:574`仅在循环前Test-InputSet；`:712`至`:759`没有运行后再次核验。

反证只修改本轮合成fixture的source.txt，不修改产品：子命令在阶段中改变清单所列输入，写有效TRX并exit0。最终仍PASS；清单SHA为`50367A38…`，执行后实际SHA为`0B07DE62…`。

影响：首轮前/Resume前的核验不能证明stage结果仍对应最终冻结输入。原目标要求每项绑定同一冻结输入、输入停更后完成；需要在阶段边界和最终verdict前复核，漂移必须阻止整体PASS并保留旧attempt。

### RW-03 [P1] expect.trx未自动纳入恢复证据

位置：`scripts/run-goal-validation.ps1:441`、`:455`、`:489`起。

运行器执行时校验expect.trx；但只哈希produces中的产物与stdout/stderr。配置expect.trx却produces=[]可首轮通过。之后该TRX被改为executed=0的文件，Resume仍REUSED/PASS，沿用旧counter。

这不是要允许空证据测试：配置已经明确要求TRX，运行器必须绑定它或在配置阶段拒绝未覆盖的必需TRX。不能把可靠性责任交给调用者记得在两个字段重复填写。同样须检查删除、非法或变更TRX；测试通过后该文件仍是恢复必需证据。

RW-01至03证据为 `runner-independent-contract-results.json`（13项/10符合/3失败，采集器exit1）；`runner-fixtures-preserved`保存首轮正例和每次调用参数、实际退出码、stdout/stderr、verdict及原始文件。

### RW-04 [P1] 新包不符合既定strict-new，摘要定位也写错

原目标G1规定新包使用strict-new，现有只读审计器明确要求包内 `manifest.sha256`。本轮未提供该文件，仅由新seal.cjs把摘要写到证据根r4中，再用自己的auditPackage核文件。

独立按唯一新包身份、required=true、schemaPolicy=strict-new建立外部expected文件，直接运行未修改审计器：1根/1包/121条目核验，exit1、唯一原因 `MISSING_DETACHED`。旧3根62包审计不含本轮新包，其PASS不能证明新包strict-new通过。

文件本身完整：manifest SHA=`7996AE0CBF6B78F5E523834DC0E7CCC5A7AF55C13357EB5D1653CDF69CDC3DAE`，121条目size/SHA均匹配、0漏登记，实际包外摘要也匹配。因此不将这一点描述成121文件内容被篡改。

此外manifest和seal.cjs记录 `../20261004-092504Z-dab448e2-rework.manifest.sha256`；从实际包目录解析并不存在。真实摘要在两级父目录r4，应为 `../../...`。需要在新runId的新包中满足既定包内digest契约、提供冻结expected清单并验证所有引用；不能补写已封存旧包。

## 5. 证据与结论边界

`acceptance-summary.json`为机器摘要；`package-integrity.json`、`audit-rework-strict.json`、`history-protection.json`分别证明文件完整性/新包契约失败/历史保护；四组TRX和所有独立日志均保留。

一个额外输出计时探针实际944ms结束，未超过一秒预算，也未直接测量后代进程生命周期，**不采信为超时失败**，不计入13项契约结果或四项缺陷；原始实验保留并附 `exploratory-probe-note.json`。不能因采集脚本exit1就扩大产品失败结论。

未验证：真实Avalonia/WebView2 GUI、真实游戏/整合包、原生凭据、真实更新交接、跨平台、干净提交/远程CI/发布。C#只跑明确73项选集，继续排除ImportWithALiveTokenButCancelledMidway；RA-01/02保持既有受限范围，不在本轮补做。

建议下轮仅处理RW-01至04，保留已验收通过的导入修复；用新runId重新冻结输入、执行同一准许回归、严格封存并接受独立验收。详细工作流与独立提示词为：

- `deepseek-harness-long-quality-rework2-workflow-2026-10-04.md`
- `deepseek-harness-long-quality-rework2-prompt-2026-10-04.md`

材料已生成，尚未派发，也不授权实施。当前独立验收工作已完成。
