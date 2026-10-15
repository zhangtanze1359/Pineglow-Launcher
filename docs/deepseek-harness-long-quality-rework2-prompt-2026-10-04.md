# DeepSeek Harness 第二轮有限返工目标提示词（2026-10-04）

状态：尚未派发。用户提交下文后定义实施范围；本文件存在不授权执行。

```text
继续松光启动器运行器与严格封存的有限返工，在目标模式按S0→S3连续执行。本轮只处理Codex第二次独立验收RW-01至04；上次导入修复已独立通过，不再修改UI。

先完整读取：
C:\Users\Administrator\.codex\worktrees\34ca\松光启动器\docs\deepseek-harness-long-quality-rework-independent-acceptance-2026-10-04.md
C:\Users\Administrator\.codex\worktrees\34ca\松光启动器\docs\deepseek-harness-long-quality-rework2-workflow-2026-10-04.md
原G0–G4工作流及用户指定三份Codex规则继续适用。

事实：上次具体反证已修，Node269、C#73、runner28、audit26、Bisect19均通过；独立13项契约10通过/3失败，旧新包strict-new审计MISSING_DETACHED。当前整体技术验收FAIL，goal缓存complete不能代替验收。

SourceRoot=D:\松光启动器，保持dirty；新证据目录=C:\Users\Administrator\.codex\worktrees\34ca\松光启动器\docs\market-execution\LONG-QUALITY-REWORK2\真实UTC时间-GUID8位。每次从实际输入清单建立全新GUID副本并核292项SHA，不复用旧bin/obj。
仅允许改scripts/run-goal-validation.ps1与scripts/tests/goal-validation.test.cjs。其他源码、导入UI、C#服务/测试、审计器及26项测试、捕获脚本/白名单、历史包/独立验收证据只读。可在新证据目录新增普通合成fixtures、stages/expected清单、封存脚本与报告。
禁止reset/restore/checkout/clean/stash/stage-all、提交/推送/PR/发布；不跑受限安全反证/全量受限C#，不做真实凭据、游戏、更新安装、外部模型或跨平台/CI。不要启动额外代理；一个主会话推进即可。

S0：保存HEAD/status/292输入和本轮前改动。复制本次独立runner-independent-contract.cjs到新证据目录，重绑新的independent-inputs.json，不改accepted期望；未修版应13项10通过3失败、exit1，正例必须实际PASS。保留旧新包strict-new MISSING_DETACHED的真实RED；不改旧包制造通过。944ms输出计时实验未证明超时，不把它计为缺陷或必需修复。

S1/RW-01：核验清单executionRoot与显式-ExecutionRoot的规范化绝对路径一致；未传取已核根，显式不同即在启动子命令前REFUSED。run-binding的兼容检查也要绑定根；输出cwd可以是runDir/attemptDir，不以禁止输出目录偷换此要求。
S1/RW-02：不仅首次/Resume前核输入，还要阶段边界及最终verdict前核输入和清单SHA。子命令在普通fixture中改变已列输入后，即使写有效TRX/exit0也不能整体PASS。保留漂移attempt、原因与历史，不回退文件。
S2/RW-03：expect.trx必须自动纳入恢复必需证据并绑定size/SHA，或明确拒绝漏登记配置；不能依赖调用者重复写produces。TRX被改动、删除、非法或0executed后不得REUSED；不变TRX对照需通过，旧证据保留。
S3/RW-04：全新runId和包，冻结expected-evidence清单覆盖本轮新包strict-new且保留旧三根历史；包内必须有manifest.sha256，生成manifest排除两文件自引用，manifest+digest完成才封存。既定audit-goal-evidence.py不得修改，实际审计新包exit0/0fail；旧三根PASS或自编seal脚本PASS不能替代。核所有包内相对引用，修复旧摘要../层级错误。封存后只写包外结果，错误用新runId，绝不补写旧包。

修完后源停更，建立新GUID副本，跑准许C#13+5+6+49至少73/73零skip；继续排除ImportWithALiveTokenButCancelledMidway及受限集合。全部Node269、runner原28加新增、audit26、VerifyHost构建0error、Bisect19，以及独立13项全部符合期望/exit0。演示失败→恢复不改旧attempt每个文件SHA；兼容Resume只复用完整证据，输入/根变使用新runId与目录。
明确记录PowerShell5.1子进程环境，只能校正本轮进程的PSModulePath，不改全局配置。每个问题最多3次有假设修复，原始失败均保留；必要越界或明确拒绝记录BLOCKED，继续允许独立项，不换工具/执行者绕过，不以预算耗尽标完成。
全部S0–S3门槛通过才能implementationReady=true。交付逐项RED/GREEN、真实命令/exit/计数/skip、输入/产物SHA、根绑定、恢复记录、新strict-new封存包、包外独立审计和未验证项。FormalPhaseApproval/ReleaseApproval始终NOT_GRANTED。
最后固定：具备独立复验条件，等待 Codex 独立验收。
```
