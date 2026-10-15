# DeepSeek Harness G2/G4 目标模式返工提示词（2026-10-04）

状态：提示词已生成，尚未派发。用户提交下文后定义其中的产品实施授权；文件存在不构成执行授权。

```text
继续松光启动器长线质量目标的有限返工，在目标模式中按R0→R4持续执行到具备独立复验条件。原goal缓存complete仅为执行者状态；Codex于2026-10-04独立验收FAIL，G0/G1/G3通过，G2/G4必须修复。

先完整读取：
1. C:\Users\Administrator\.codex\worktrees\34ca\松光启动器\docs\deepseek-harness-long-quality-independent-acceptance-2026-10-04.md
2. C:\Users\Administrator\.codex\worktrees\34ca\松光启动器\docs\deepseek-harness-long-quality-rework-workflow-2026-10-04.md
3. C:\Users\Administrator\.codex\worktrees\34ca\松光启动器\docs\deepseek-harness-long-term-goal-workflow-2026-10-03.md
4. 用户既有三份Codex规则：AGENTS-精简规则、Codex独立验收任务模板、Codex执行与独立验收规则-完整版，均位于C:\Users\Administrator\Documents\Codex\2026-10-03\codex-threads-01a100ce-3590-7090-aec4\outputs。

SourceRoot=D:\松光启动器，dirty，不得回退、清理、覆盖用户或其他会话工作。
新EvidenceRoot=C:\Users\Administrator\.codex\worktrees\34ca\松光启动器\docs\market-execution\LONG-QUALITY-REWORK\<实际UTC时间>-<GUID8位>。每次执行都从输入清单建立全新GUID副本并核SHA。
旧LONG-QUALITY-GOAL最终包和docs\market-acceptance\20261004-long-quality-independent严格只读。不能在独立验收目录直接重跑会写输出的脚本；复制到新证据目录并重新绑定本轮输入。

只修改计划明列的导入UI/状态文案/导入行为测试、运行器及其测试、必要捕获输入字段和白名单追加。C#服务/测试、api.js、CSS/index/设计资源、审计器和历史证据只读。不得扩展到真实更新安装、凭据、游戏、外部模型、受限恢复安全反证、CI/发布。一个主会话推进，不要求额外代理。禁止reset/restore/checkout/clean/stash/stage-all、提交/推送/PR/发布。

必须关闭6项：
LQ-01：点击取消仅代表意图。取消API失败或pendingCancel后，导入普通异常不能宣称已取消/没有实例。仅服务端明确取消信号确认取消，正常导入最终成功必须如实显示。真实await running结束后断言终态；独立operationId对照应继续通过。
LQ-02：首次与Resume逐项验证清单inputs所指执行根实际size/SHA；清单文件本身SHA不等于源文件未变。实际文件变而清单不变必须拒绝复用/PASS。
LQ-03：新attempt独立目录，子命令不生成本轮TRX不能拿旧TRX通过；核total/executed/passed/failed/skip和UnitTestResult一致性，0executed即使total>0也FAIL。
LQ-04：produces进入完整stage指纹；新增必需产物不存在不能REUSED。build/VerifyHost/smoke补真实产物SHA绑定。
LQ-05：不兼容Resume必须新runId/新目录或明确拒绝旧runId；旧成功/失败记录及其日志、TRX、产物不可覆盖。只有相同实际输入、完整定义和证据才能REUSED。
LQ-06：空stages、unknown Only、重复stage和缺必需stage不得整体PASS；合法Only明确partial，不能被上层当整体完成。

R0保留未修产品上的实际RED与完整初始status/HEAD/hash；禁止改产品制造RED。运行器原10项通过不能代替新增反证门槛，采集器自身exit0不能代表负例符合契约。
R1导入终态聚焦RED→最小修复→GREEN；补取消失败后导入失败、pending后导入失败、cancel与import同operationId，并保留正常成功/真实取消。
R2将独立7个runner反证变成断言明确拒绝的测试，补原始失败证据不可覆盖与build产物缺失拒绝复用；不删测试、不改Skip、不放宽计数。
R3重新建立GUID副本跑原准许C#选集13+5+6+49至少73/73零skip；继续排除ImportWithALiveTokenButCancelledMidway和受限安全集，不跑全量C#。全部Node原266项加新增项，审计26项+真实覆盖审计，runner原10加新增，VerifyHost构建0error，Bisect19项；如有无关失败单列，不能宣布必需门槛完成。
WindowsPowerShell5.1子进程若受父PowerShell7的PSModulePath影响，允许只改本轮子进程环境为WindowsPowerShell模块目录并保留原失败；不能改机器全局配置或当成产品RED。
R3演示普通失败→恢复，恢复后原失败attempt每个原始文件SHA不变；演示输入变更采取新runId，不能覆盖旧结果。
R4更新真实终态场景矩阵和机器verdict，所有包内内容完成后才manifest/detached封存，随后仅写包外审计与逐项复验。包内引用必须可审计且不依赖可覆盖的绝对工作文件。

同一具体错误最多3次有假设的修复；必要越界、明确平台拒绝或缺必要依赖时记录BLOCKED及影响，仍完成不依赖它的允许工作。不绕过拒绝、不自增预算、不以耗尽预算标完成。
只有全部R0–R4必需门槛通过才能implementationReady=true。独立验收/正式阶段/发布权限均NOT_GRANTED。交付逐项RED/GREEN、精确命令/exit/计数/skip、输入与产物SHA、不可覆盖恢复记录、封存包、包外审计、未验证清单。
最后固定：具备独立复验条件，等待 Codex 独立验收。
```
