# 松光启动器 G2/G4 有限返工实施计划（2026-10-04）

> 给执行者：单一Harness会话按R0→R4连续执行。采用目标模式时允许推进本计划内部阶段，完成后停止等待独立验收。用户规则优先于通用writing-plans的提交、子代理和目录建议；本计划不要求这些动作。

**Goal：** 关闭独立验收LQ-01至LQ-06，建立不会把取消意图、旧测试证据、变更输入或空执行当成完成的本机质量闭环。

**Architecture：** 保留既有C#服务与原生JS结构；仅调整导入UI终态和PowerShell验证运行器。将一个attempt的原始证据保存到不可覆盖的独立目录，恢复指针与历史记录分开；输入清单与执行副本实际字节共同绑定。

**Tech Stack：** 本机.NET10、Node内置测试、WindowsPowerShell5.1/Python标准库；无新依赖，无工具链升级。

**Spec：** `deepseek-harness-long-quality-independent-acceptance-2026-10-04.md`；原G0–G4工作流继续规定正常C#选集和全部外部停止点。真实证据根为 `docs\market-acceptance\20261004-long-quality-independent`。

**状态：** 仅供审阅的返工材料，不授权产品修改；用户将独立提示词提交给Harness后，提示词定义其实施范围。

## 固定边界

- SourceRoot=`D:\松光启动器`，保持dirty状态；PlanRoot=`C:\Users\Administrator\.codex\worktrees\34ca\松光启动器\docs`。
- 新证据根=`PlanRoot\market-execution\LONG-QUALITY-REWORK\<实际UTC时间>-<GUID8位>`；每次建立不存在的新GUID临时执行根。
- 可修改：`ui/js/launcher.js`仅handleImportFiles取消终态，`ui/js/i18n.js`仅对应中英文提示，`ui/tests/goal-core-flow.test.cjs`和`ui/tests/import-cancel.test.cjs`仅导入行为断言；`scripts/run-goal-validation.ps1`、`scripts/tests/goal-validation.test.cjs`仅运行器契约；`scripts/capture-goal-baseline.ps1`仅必要输入绑定/捕获字段；`scripts/source-snapshot-inputs.txt`只追加本轮所需输入。
- 可新增本轮证据下的stages配置、普通合成夹具、封存与校验脚本；新增源码侧脚本必须先在checkpoint写明路径与用途，只能服务本计划既定运行器验证，不能引入相邻功能。
- 只读：`scripts/audit-goal-evidence.py`及26项测试、C#产品服务与测试、api.js、CSS、index.html、pineglow.css、pine-forest.svg、设计文档；全部历史证据与独立验收证据。
- 禁止reset/restore/checkout/clean/stash/stage-all、删除历史、提交/推送/PR/发布、真实凭据/外部模型/游戏/更新安装、受限恢复安全反证、全量受限C#、绕过平台拒绝。
- 不修改Skip、删测试、放宽minCount/minTests、用异常崩溃代替正确拒绝或将必要case标观察项。最多3次有假设的修复；确需越界就明确记录BLOCKED及剩余任务。

## R0 现场与真实RED

**输入：** 当前292输入与LQ独立证据。**产物：** 新基线、新runId、LQ问题台账和可执行RED。

- [ ] 记录HEAD、完整status、源清单与关键SHA；识别并记录用户/并发写入者改动，不把全量git diff归为本轮成果。
- [ ] 先运行独立反证采集器与导入终态测试，保存未修源码上的实际RED。复验脚本只读旧证据，输出必须放新目录；若复制旧测试须重新绑定当前GUID执行根。
- [ ] 核对环境：Node子进程WindowsPowerShell必须能加载Get-FileHash；可只调整子进程PSModulePath，记录具体值，不改机器全局设置。
- [ ] 从独立 `import-terminal-disproof.test.cjs` 提取3个INDEPENDENT断言进入源码测试，保留原对照；禁止仅修改期望文案令错误取消通过。

可核实的旧证据复现命令（旧目录仅供阅读，不直接在旧目录重跑写入）：

```powershell
# 复制到新证据目录后，脚本中的independent-inputs.json须绑定本轮GUID副本
& 'C:\Program Files\nodejs\node.exe' '<新证据目录>\runner-disproof.cjs'
& 'C:\Program Files\nodejs\node.exe' --test --test-reporter=tap --test-name-pattern INDEPENDENT '<新证据目录>\import-terminal-disproof.test.cjs'
```

这里路径由R0创建后写入checkpoint，不能把占位符命令宣称为已经执行。

## R1 导入真实终态

**文件：** launcher.js、i18n.js、goal-core-flow.test.cjs/import-cancel.test.cjs。
**接口：** 使用既有importInstance/cancelImport与operationId；不改服务端或API契约。

- [ ] 增补并先验证失败：取消API失败→导入普通失败；仅pendingCancel→导入普通失败；同operationId对照。
- [ ] 将请求取消、服务端取消确认、导入最终结果分开保存；只有服务端返回明确cancelled或导入错误携带已确认取消信号才显示已取消。
- [ ] 点击取消/pendingCancel不证明没有生成实例；一般传输错误保留失败/结果尚未确认的准确提示，允许用户查询实例状态。不得将普通异常改标err.cancelled。
- [ ] 保留真正取消、未受理取消、正常成功、取消后最终成功、取消API失败和按钮状态既有行为；用最终await后的断言验证，不只截取点击瞬间。
- [ ] 聚焦测试GREEN后跑全部Node；最低原266项加新增用例，0required skip，实际命令/结果计数记账。

## R2 运行器输入、证据与恢复

**文件：** run-goal-validation.ps1、goal-validation.test.cjs，必要捕获字段。
**输入：** 非空有效inputs清单、真实ExecutionRoot、非空唯一id的完整StagesFile。**产物：** 每attempt原始stdout/stderr/TRX/产物、不可覆盖stage记录、汇总verdict。

- [ ] 对inputs逐项核实际执行文件存在、整数size与SHA匹配；输入清单自身SHA相等不能代替实际输入相等。首次运行和Resume均检查。空/重复/无效清单必须正确拒绝。
- [ ] stage指纹采用确定性序列化，包含file、argv完整数组、cwd、expect、produces、输入清单SHA与已核实际输入身份；避免字符串分隔拼接歧义。
- [ ] 首次与每次重跑建立新的attempt目录，拒绝拿既存TRX作为新结果；TRX必须属于本次子进程生成的证据目录。不要删除旧TRX来证明新生成。
- [ ] 核TRX total/executed/passed/failed/notExecuted非负整数与一致性，至少executed达到门槛、无失败/required skip；核UnitTestResult条目与outcome相符。字段缺失、矛盾和0executed即使total>0也必须FAIL。
- [ ] 所有必需产物存在、size/SHA匹配；新produces改变即不得复用。build、VerifyHost与smoke补准确DLL/EXE等产物绑定，消费者确保使用同一构建结果。只读检查产物，不在真实运行根篡改二进制。
- [ ] 相同输入、完整指纹和全部原始证据才能REUSED；不兼容输入/定义须拒绝沿用旧runId并要求或建立新runId/目录，旧COMPLETED/FAILED状态及其引用原始证据原样保留。
- [ ] 空stages、unknown Only、重复id/缺必需stage不能整体PASS；合法Only只产生明确partial verdict，不可被上层当作全部门槛完成。
- [ ] 超时、非零退出、后台未结束保持FAIL；实际退出码立即记录，不能由tee/Out-File覆盖。

必须新增的独立断言，不少于原10项+以下7种：实际inputs字节变但清单不变；旧TRX未被子命令更新；total>0而executed/passed为0；produces增补缺失文件；输入SHA变时同runId/目录拒绝覆盖；空stages；unknown Only。另补失败恢复原始日志不可覆盖、build产物缺失拒绝复用对照。采集器自身exit0不是这些负例的通过条件；要断言子runner非零和明确原因码。

## R3 GUID副本常规回归与恢复演示

- [ ] 用R1/R2完成后的实际输入重建全新副本，并逐项比对复制SHA，不复用旧bin/obj。
- [ ] 测试项目Release build；按原g4/goal-stages.json的明确筛选集合运行13+5+6+49，实际至少73/73、0required skip。继续排除ImportWithALiveTokenButCancelledMidway，不宣称全C#通过。
- [ ] 全部Node（原266+新增实际数量）、审计器26项及真实3根覆盖审计、运行器原10+新增契约用例、VerifyHost Release build、Bisect19项。
- [ ] 普通合成失败→Resume演示只复用有完整绑定的stage；证明失败attempt旧JSON、stdout/stderr/TRX的SHA在恢复后未变。输入改变另做新runId示例，不能把不复用与保留历史混为一项。
- [ ] 更新场景矩阵IMPORT-03/04为真实终态证明，非单纯按测试标题自动登记；其他27个场景绑定最终测试文件SHA与日志。

实际环境及原筛选表达式已在旧sealed `g4/goal-stages.json` 中，可读取复制到新配置并重新绑定executionRoot/runDir；先审阅当前脚本副作用再执行。旧配置和旧结果绝不改写。

## R4 终态封存与交付

- [ ] 所有必需项完成后才implementationReady=true；错误、跳过、0测试、partial、不兼容输入均不能进入完成状态。
- [ ] 完成报告、changed-files、源终态清单、机器verdict、场景矩阵、计数、环境校正、范围外门槛。
- [ ] 包内引用使用可移植相对路径；需要指向包外原件时标明对应包内只读副本与SHA，不能依赖可被覆盖的绝对run目录文件。
- [ ] 完成所有包内内容→manifest列全file/size/SHA→detached→包外审计→逐项包外再次校验；封存后包内禁止任何写入。封存失败不补写旧包，用新runId。
- [ ] 汇报LQ-01至06逐条RED/GREEN、原始失败保留情况、精确命令、输入SHA、stage复用身份和实际测试数。FormalPhaseApproval/ReleaseApproval始终NOT_GRANTED。
- [ ] 结尾固定“具备独立复验条件，等待 Codex 独立验收”；不提交、不发布、不扩张功能或自动派发下一目标。
