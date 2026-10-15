# 松光启动器运行器与严格封存第二轮有限返工计划（2026-10-04）

> 按S0→S3由一个Harness主会话连续执行。遵守用户独立验收规则，不要求子代理、提交或未安装技能。材料存在不构成实施授权；用户提交对应提示词后才定义以下实施范围。

**Goal：** 关闭RW-01至04，保证核验根与执行根一致、整个执行期间输入绑定可信、恢复TRX证据完整，并交付既定strict-new审计通过的新包。

**Architecture：** 保留本轮已通过的导入UI和28项运行器测试；在既有运行器中补输入边界核验与TRX必需证据绑定，在新证据目录的封存脚本中恢复既定新包结构。

**Tech Stack：** 本机.NET10、Node内置测试、WindowsPowerShell5.1、Python标准库，无新依赖/升级。

**Spec：** `deepseek-harness-long-quality-rework-independent-acceptance-2026-10-04.md`；原G0–G4 strict-new规则及第一轮返工选集继续适用。

## 固定边界

- SourceRoot=`D:\松光启动器`；PlanRoot=`C:\Users\Administrator\.codex\worktrees\34ca\松光启动器\docs`。
- 新证据根=`PlanRoot\market-execution\LONG-QUALITY-REWORK2\<真实UTC>-<GUID8位>`；每次执行使用不存在的新GUID副本，逐项核292输入。
- 源码仅允许修改 `scripts/run-goal-validation.ps1` 和 `scripts/tests/goal-validation.test.cjs`。不需要修改导入UI，已通过项保留。
- 其他源码、C#服务/测试、审计器及26项测试、捕获脚本、白名单、所有历史包/独立证据只读。确需新增源码文件时先明确必要依赖，不能自行扩大白名单。
- 可在新证据目录新增合成fixtures、stage配置、expected清单、封存/校验脚本、报告。复制旧脚本须重绑本轮目录，不能在旧目录直接执行写入。
- 不reset/restore/checkout/clean/stash/stage-all、提交/推送/PR/发布；不运行受限安全反证或全量受限C#；不访问真实游戏、凭据、更新安装和外部模型。
- 同一问题最多3轮有假设修复，保留每轮失败证据；明确拒绝停止相应动作，不换执行者绕过。不能用预算耗尽、0测试、放宽断言、Skip或缺证据宣告完成。

## S0 基线与已证实RED

**输入：** 当前292源码、上次通过项和本次13项独立契约。**产物：** 新baseline、runId、普通RED和问题台账。

- [ ] 保存HEAD/status/输入SHA，识别本轮前dirty工作；核本次独立输入SHA作为参考，不复用已有bin/obj。
- [ ] 从本次只读证据复制 `runner-independent-contract.cjs` 到新证据目录，准备新 `independent-inputs.json` 指向新GUID副本；不改accepted条件。
- [ ] 独立采集器正常执行完应该exit1，13项中10通过/3失败。首轮正例必须exit0/PASS，避免无效夹具误作正确拒绝。
- [ ] 用只读审计器对旧rework包做外部strict-new清单审计，保存MISSING_DETACHED/exit1。这是结构RED，不改写旧包。
- [ ] 排除本次未采信的输出计时实验，不把944ms实验重新解释为超时证据；若开发额外timeout测试必须先验证自己的时间/进程对照。

## S1 核验根与全程冻结输入

**修改：** run-goal-validation.ps1、goal-validation.test.cjs。
**接口：** Test-InputSet返回inputRoot、setSha、entries、count、manifestSha；ExecutionRoot成为经过核验的唯一实际根。

- [ ] 增加失配根测试：清单A，ExecutionRoot=B，B/source字节不同，cwd={executionRoot}。实际子命令不得启动，返回明确REFUSED/非零，记录两种根及原因。
- [ ] 对根Resolve-Path后的绝对路径按Windows大小写不敏感口径比较；ExecutionRoot未传则从已核inputRoot赋值。显式不同立即拒绝。不要一概禁止测试子命令cwd=runDir/attemptDir，正确绑定根与输出cwd是两件事。
- [ ] run-binding兼容检查包含规范化executionRoot/verifiedInputRoot；首次与Resume必须使用同一绑定。输入Root变更不得在旧runId中悄悄重跑。
- [ ] 增加阶段中源数据变化测试：stage只修改自身普通fixture所列source，写有效TRX后exit0；runner应FAIL/REFUSED，不能总体PASS。
- [ ] 执行stage前后核Test-InputSet以及manifest自身SHA与初始binding；最终verdict前再次核验。出现漂移要保存attempt、原因与原始SHA，停止依赖阶段/阻止整体PASS，不回退输入、不覆盖失败证据。
- [ ] 保留正例：合法cwd输出目录、根字符串大小写规范化、不变Resume、失败后恢复、合法Only/PARTIAL。

## S2 TRX恢复必需证据

**修改：** 同上两文件。**接口：** 每stage的expect.trx自动加入必需证据集合，或配置阶段严格拒绝漏登记；集合使用展开后路径唯一身份。

- [ ] 增补expect.trx存在而produces=[]的首轮正例/配置拒绝策略，策略必须明确且测试固定。
- [ ] 若自动绑定：执行成功后保存TRX path/size/SHA，与stdout/stderr及其他produces一起归入恢复必需证据；路径去重，不要求调用者重复配置。
- [ ] Resume检查TRX存在、size/SHA匹配。证据改变不能REUSED，可在新attempt重跑并保留旧文件/记录，或明确拒绝续跑。
- [ ] 对TRX字节改动、删除、变非法XML、0executed各做独立测试；还要不变TRX复用正例。所有首次正例先实证通过，负例不能靠不存在输入/启动失败过关。
- [ ] 原28项保留，新增数量按实际记录；原13项独立契约全部符合accepted条件，采集器exit0。

## S3 常规回归与严格封存

**输入：** 完成S1/S2后的292输入与新GUID执行根。**产物：** 实际回归、恢复演示、strict-new新包及覆盖expected。

- [ ] 固定本轮源输入后重建GUID副本，核复制SHA；执行原准许13+5+6+49 C#选集、全部Node269项、运行器原28加新增、审计器26项、VerifyHost构建和Bisect19。
- [ ] C#继续排除ImportWithALiveTokenButCancelledMidway及受限集合，不称全量通过；全部required无skip、0测试不得通过。source与执行根结束时再次核292输入。
- [ ] 失败→恢复演示保留旧attempt全部字节与归档记录；兼容Resume只复用有完整TRX/产物绑定的stage。输入/根改变使用新runId与目录，不覆盖旧记录。
- [ ] 在新证据根冻结expected-evidence.json，至少声明本轮唯一新包required/strict-new、正确runId与包目录；保留旧三根历史清单，生成独立组合覆盖清单时不改旧文件。旧根和新包必须同时被检查，报告实际根/包/条目数。
- [ ] 完成包内所有普通报告/源终态/机器verdict/证据引用；生成manifest时排除manifest.json与manifest.sha256的自引用，再将manifest字节SHA写入**包内manifest.sha256**；两者完成才视为封存完成，此后只写包外结果。
- [ ] 可附额外包外digest，但不能替代strict-new包内digest；从包根逐条解析相对引用，不使用旧错误的../摘要路径。需引用包外原件时同时提供包内副本和SHA。
- [ ] 使用未改audit-goal-evidence.py --expected本轮冻结清单 --out包外报告，exit0/0fail；再独立逐项size/SHA/漏登记/detached校验。不能以自编seal.cjs的PASS替代既定审计器。
- [ ] 新包不得有命令完成后再捕获、改写source-after/报告；封存后发现错误采用新runId，不补写旧包。ImplementationReady仅在全部门槛通过后true。
- [ ] 交付RW-01至04逐条RED/GREEN、精确命令、计数、实际退出码、input/产物SHA、scope与未验证项；FormalPhaseApproval/ReleaseApproval=NOT_GRANTED。
- [ ] 结束“具备独立复验条件，等待 Codex 独立验收”，不自动进入其他项目阶段或发布。

执行命令从本次独立仪器和旧sealed `runner/goal-stages.json`核实，复制到新配置并重新绑定executionRoot/runDir/attemptDir。执行前核查脚本副作用；写下本轮真实路径与命令，不能把未填路径的建议当作已执行结果。
