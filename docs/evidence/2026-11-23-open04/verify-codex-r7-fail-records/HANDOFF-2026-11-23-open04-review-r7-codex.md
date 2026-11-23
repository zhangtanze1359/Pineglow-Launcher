RESULT: FAIL

# OPEN-04 第 7 轮独立销项复核

在已记录的 Windows 机器与负载条件下，接受「历史目标用例未复现」的技术观察。冻结版覆盖与风险说明仍有可由原始证据反证的轮次、时序和覆盖表述，暂不接受销项。复核对象为 `0bbc63ed006c1966cf8e4d62569fdb838ac6cf97`。

`FormalPhaseApproval=NOT_GRANTED`

## 1. 受测输入核对

2026-11-23 10:03（UTC+08）首检。下列是 `pwd`、`git rev-parse --show-toplevel`、`git rev-parse HEAD` 的值。初始 `git status --short --untracked-files=all` 无输出，退出 0。

```text
C:\Users\Administrator\.t3\worktrees\松光启动器\t3-a7f4affd-204d2a4797602429
C:/Users/Administrator/.t3/worktrees/松光启动器/t3-a7f4affd-204d2a4797602429
0bbc63ed006c1966cf8e4d62569fdb838ac6cf97
```

| 命令 | 预期与实际 |
| --- | --- |
| `git -C D:/Projects/松光启动器 rev-parse HEAD` | 与目标相同，退出 0 |
| `git -C D:/Projects/松光启动器 diff --exit-code 63dadf7 0bbc63e -- dev/ scripts/test-inventory.txt .github/workflows/build.yml global.json` | 无 diff，退出 0 |
| `git -C D:/Projects/松光启动器 diff --name-only 63dadf7 0bbc63e` | 73 个路径，均在 `docs/`，退出 0 |
| `git diff --exit-code 367ab91 63dadf7 -- dev/ scripts/test-inventory.txt .github/workflows/build.yml global.json` | 无 diff，退出 0。执行者基线与第 4 轮受测跟踪输入一致 |
| `node scripts/run-inventory-tests.cjs --check` | Node 测试 35 个、磁盘发现 35 个、C# 项目 1 个，退出 0。清单登记 C# 项目，个例数量和身份由 TRX 核验 |
| `python -X utf8 docs/evidence/2026-11-23-open04-review-r7/recheck.py` | 解析执行者五份 TXT 及第 1、3、4 轮 TRX，退出 0 |

本轮复用第 4 轮的全部 19 份 TRX，独立核对 XML 计数、逐例结果、测试定义、命令与退出记录。**4567 通过、0 失败、0 跳过**，19 条运行命令均退出 0。三轮全量各 1502 个唯一测试 ID，集合相同。16 个目标用例结果均为 `Passed`，各有 `state=applied`，`waited` 5.2–5.5 s。本轮新跑的产品用例数为 0。复算记录含每份 TRX 的 SHA-256。

第 4 轮的 Windows 隔离测试工作目录为 `D:/Projects/_worktrees/pineglow-open04-review-r4`，原运行时间、命令、预期用例数、退出码与结果见对应归档。本轮读取原始产物的时间与工作目录见复算记录。[输入比较和 TRX 逐份复算](../evidence/2026-11-23-open04-review-r7/recheck.txt) · [现场与清单检查](../evidence/2026-11-23-open04-review-r7/source-and-inventory.txt) · [第 4 轮运行命令](../evidence/2026-11-23-open04/verify-codex-r4-fail-wording/raw/commands.jsonl) · [第 4 轮退出和计数](../evidence/2026-11-23-open04/verify-codex-r4-fail-wording/raw/runs.csv)

## 2. 第 6 轮四条处置

| 处置 | 判定 | 证据 |
| --- | --- | --- |
| 1. 将状态计数分为直记和推定 | **直记通过，推定措辞仍需修正** | 执行者时序日志直记 8 次，第 4 轮 TRX 直记 16 次，共 24 次。执行者常规批次有 21 次按其覆盖口径预期触达目标。台账仍用「执行次数」和「全部可确认」给条件推定赋予逐条核定的口径 |
| 2. 配置 `.tmp` 失败改属第 1 轮 | **归属及总数通过，附带全量计数失败** | 第 1 轮 `full-01.trx:1945` 是唯一失败记录，断言栈指向 `ConfigPreservationTests.cs:194`，目标更新用例当轮通过。第 3 轮 0 失败。第 1 轮其余 **3** 轮全量通过，README 此处正确，台账和执行交接仍各写 **4** |
| 3. 时序与超时边界 | **部分通过** | 进程退出时刻未采样、超时分支「未观测、未验证」已明确。README :116 仍称可证明存活期间未立即返回，:135 和 :291 仍称精确先后由脚本保证。脚本到达等待上界也会进入备份、替换 |
| 4. 区分有判定与未完成 | **部分通过** | 对第 1–5 轮单独统计，4 轮有判定属实。台账 :30 的「共 5 轮」和同表 :39 的第 6 轮冲突。截至冻结版共 6 轮，5 轮有判定且均为 FAIL，第 2 轮中断 |

[执行者时序日志](../evidence/2026-11-23-open04/raw/open04-timing-waited.txt#L9) · [台账](../open-items.md#L30) · [时序说明](../evidence/2026-11-23-open04/README.md#L116) · [全量分类和唯一失败原文](../evidence/2026-11-23-open04-review-r7/recheck.txt#L109) · [第 2 轮证据盘点](../evidence/2026-11-23-open04-review-r7/source-and-inventory.txt)

## 3. 第 3 轮全量轮数裁定

**执行者的 2 轮正确。第 6 轮交接 :47、:49 混用了不同轮次的产物。** 第 3 轮工作树目录名带 `review4`，应以工作树 HEAD、逐轮命令和报告归属辨认。该目录的 `rounds.jsonl` 只有 `full-01-default`、`full-02-loaded` 两条无过滤的全量命令，退出码均为 0。两份 TRX 各 1502 通过。其余为目标 10 轮、路径类 3 轮、并发类 3 轮，总计 **18 份，3065 通过、0 失败、0 跳过**。原脚本总表的 18 个 `met_expected` 均为 false，英文控制台解析器未读到中文计数，逐轮 `dotnet test` 退出和逐例 TRX 支持上述核定。

第 6 轮交接 :49 写给第 3 轮的 **23 份、6071 通过、1 失败、4 轮全量**实际属第 1 轮。全量文件为 `full-01`、`full-02`、`full-stress-01`、`diag-full-03`，合计 6007 通过、1 失败。第 6 轮交接 :47 写给第 1 轮的 **19 份、4567 通过、3 轮全量**实际吻合第 4 轮。保留旧报告原文，通过归档说明勘误即可。

[三轮 TRX 复算](../evidence/2026-11-23-open04-review-r7/recheck.txt#L109) · [第 6 轮原报告](../evidence/2026-11-23-open04/verify-codex-r6-fail-counts/HANDOFF-2026-11-23-open04-review-r6-codex.md#L45)

## 4. 状态推定的前提

**条件推理成立，45 只能作为按既定运行口径推得的总数。** 前提是固定版本的测试程序集在 Windows testhost 上经常规 xUnit 运行，且目标个例确实被发现、执行并返回 `Passed`。xUnit v2.9.3 分别报告 `TestSkipped` 和 `TestPassed`，通过表示用例正常完成且没有失败异常。框架不会追踪每条断言是否执行。目标方法是普通同步 `[Fact]`，没有 `Skip`。其唯一提前返回在非 Windows 守卫，Windows 分支没有吞掉断言异常的 catch，`:251–254` 顺序断言 `applied`、`NEW-CONTENT` 和耗时 ≥ 2 s，`finally` 中的 catch 只围住清理。因此，固定版目标方法在 Windows 的个例通过足以推得三个断言均到达并通过。另一条拒绝 ACE 用例在 Windows 也能提前正常返回，说明这一推理只适用于所核对的方法。[目标方法](../../dev/PineglowLauncher.Tests/UpdateApplyScriptPathTests.cs#L213) · [ACE 提前返回](../../dev/PineglowLauncher.Tests/UpdateApplyScriptPathTests.cs#L336) · [xUnit v2.9.3 结果分支](https://github.com/xunit/xunit/blob/v2-2.9.3/src/xunit.execution/Sdk/Frameworks/Runners/TestRunner.cs#L114-L155)

| 执行者批次 | 已核对 | 推定边界 |
| --- | --- | --- |
| 第 1 层，10 轮 | VSTest `~` 是不区分大小写的包含匹配。固定源码只有一条对应测试声明，第 4 轮三份全量的测试定义均只匹配该方法，同一过滤器的 10 份第 4 轮单跑 TRX 各只运行它。执行者的命令记录与 10 个 `passed=1 total=1` 相符 | 10 次是强推定，原 TXT 未保存个例名称、状态或等待字段 |
| 第 2 层，路径类 5 轮 | 相同固定源码的路径类共 9 个 `[Fact]`，第 4 轮的全量测试定义同样只有这 9 个名称匹配类过滤器，执行者各轮 `passed=9 total=9` | 5 次按固定版发现集合与记录的过滤器推定。执行者并发类的 5 轮不计目标 |
| 第 3、4 层，各 3 轮 | 第 4 轮无过滤全量含目标。执行者第 3 层日志标为全量，各轮只有汇总。第 4 层压力脚本的命令确实没有过滤器，各轮 1502 通过 | 合计推计 6 次。无过滤和相同总数本身无法逐轮核对测试定义集合、加载的程序集或个例结果 |
| 执行者时序与第 4 次复核 | 8 行直接状态和 16 条逐例 TRX 输出，共 24 条直接 `applied` | 其中执行者 8 行缺 xUnit 计数字段。第 1、3 轮的外部原证据也未归入这两组统计 |

可复核的表述为「两组有 24 条直接状态记录，另有 21 次按固定 Windows 测试程序集、过滤范围和全过计数推定，合计 45 次，其中 21 次无逐例记录」。执行者五份日志复算为 `10 + 45 + 40 + 4506 + 4506 = 9107`，26 轮、0 失败、0 跳过。另有 8 条 `waited`，均值 4.7375 s，范围 4.7–4.9 s。压力批次的平均总耗时增幅是 16.025641%。[执行者五份日志复算](../evidence/2026-11-23-open04-review-r7/recheck.txt#L97) · [第 4 轮测试定义核对](../evidence/2026-11-23-open04-review-r7/recheck.txt#L186) · [压力脚本命令](../evidence/2026-11-23-open04/layer4-stress.ps1#L62) · [VSTest 过滤器语义](https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-test-vstest#filter-option-details)

## 5. 五份文档检查结果

| 文件和位置 | 冻结版的问题或已核定边界 |
| --- | --- |
| [证据 README](../evidence/2026-11-23-open04/README.md#L116) :116、:135、:291 | 等待前置检查和调用耗时支持「与预期吻合」。没有退出或替换时刻。产品脚本 `:640` 依据进程枚举结果进入 `:ready`，`:642` 超过等待上界也会直接进入；不能据此保证精确事件先后 |
| [证据 README](../evidence/2026-11-23-open04/README.md#L145) :146–149、:225 | 三个模拟重启写标记，时序用例与备份失败的重启参数才是 `rem noop-restart`。失败与保留覆盖有四条不同断言组合，指定 `replace_failed`、`backup_failed` 的精确断言各一条。拒绝 ACE 项在第 4 轮六条结果明确执行了脚本，执行者批次汇总无法逐条确认是否走过此前置分支。API 用例通过真实回环 HTTP 发出两次请求，断言两次 200/`applying` 和启动钩子仅一次，脚本启动仍由测试钩子替代。直接断言在 `:144–151`，`:122` 只是计数变量初始化 |
| [证据 README](../evidence/2026-11-23-open04/README.md#L226) :226、:237–247、:272、:285 | 历史处置表将 23 轮旧归属写成已修处置，需指向后文勘误。:247 有推定标签，:272 又写「全部可确认」。:285 的第 3 轮两轮成立，完整勘误见第 3 节 |
| [证据 README](../evidence/2026-11-23-open04/README.md#L305) :307、:313–315、:321、:328–343 | 历史全量和现全量共同覆盖默认并行形态，历史运行环境未完整重建。:314 为「未只做」误字，:315「不可复现」超出「未复现」。:321 对脚本未结束时状态的说法过强。:333 第 2 层头部有过滤器，第 3 层无完整命令。:334 的相对脚本路径在刚检出的 `367ab91` 不存在。:343 哈希 `21CE9F50…C365` 经核对是 Git LF blob，Windows 工作树 CRLF 实物为 `5FBB916D…BD30` |
| [开放项台账](../open-items.md#L15) :5、:15、:28、:30、:34–36、:49–62 | :5 的核对记录在冻结提交中缺失。总轮次及第 1 轮全量次数不一致。:35 的 0 应限定为已核定测试轮次。:49–62 需要给 21 次加上条件推计口径。:45 的 9 轮 / 13518 通过按「随仓库归档」范围可复算 |
| [执行交接](HANDOFF-2026-11-23-open04-execution.md#L3) :3、:47、:62、:70、:96 | :3 的任务计划在冻结提交中缺失。:47 的提前读取只会返回缺失或不可解析不成立。:62 的「不可复现」范围过强。:70 第 1 轮其余三轮全量通过。:96 的 9 轮需沿用「当前随目标提交归档」限定 |
| [自审](../evidence/2026-11-23-open04/SELF-AUDIT.md#L3) :3、:53、:59 | :3 是第 4 轮启动尝试曾受限额影响的历史口径，应注明稍后实际第 4 轮已完成，:53 的「前 4 次」宜改为「自审前收到的审阅意见」并注明自审版本。:59 对 `applying` 的说明限于这两个夹具的状态来源。26 轮计数、耗时增幅、两个源码哈希、11 个调用点和六处 Windows 守卫可复算 |
| [分析](../evidence/2026-11-23-open04/ANALYSIS.md#L25) :27、:43–45、:56、:62–75、:79 | :27 的集合配置只说明可跨集合并行。:43–45 应包含 `HasExited` 的实际断言，单靠睡眠无法确认进程活着。:56 的早读结论过强。:62–75 是设计及条件式判据，`× N` 属计划，实测为 3 轮。:79 的 PATH 排除应限于已知裸命令解析机制 |

两处夹具确实都先同步读 stdout、stderr，再等待至多 30 秒，且忽略等待返回值与子进程退出码。脚本在重启命令和清理之前就可能写出最终状态，故「脚本未结束」与「状态文件尚未完整写出」是不同条件。后者可以导致读取 `null`，前者也可能读取到脚本已写出的有效终态。两处夹具没有预写 `applying`。历史成因及夹具真实超时分支均未验证。[路径夹具](../../dev/PineglowLauncher.Tests/UpdateApplyScriptPathTests.cs#L72) · [恢复夹具](../../dev/PineglowLauncher.Tests/UpdateRecoveryTests.cs#L316) · [读取结果](../../dev/PineglowLauncher/Pineglow.Launcher.Services/UpdateService.cs#L551) · [Windows 脚本等待与结果写入](../../dev/PineglowLauncher/Pineglow.Launcher.Services/UpdateService.cs#L639) · [回环 HTTP 用例](../../dev/PineglowLauncher.Tests/UpdateConcurrencyTests.cs#L133) · [真实 HTTP 请求](../../dev/PineglowLauncher.Tests/LocalApiTestHarness.cs#L225)

源码主引用和两个 CRLF 源码哈希与实物一致。实际 CI、四核共享 runner、实包自更新、真实二进制重启、UNC、物理整盘、实际夹具超时均未验证。执行者压力时段缺遥测和盘点。第 4 轮合成压力本身有独立 CPU、IO 和退出记录，适用范围是该独立批次。[哈希复算](../evidence/2026-11-23-open04-review-r7/recheck.txt#L94) · [执行者压力日志](../evidence/2026-11-23-open04/raw/open04-layer4-stress.txt#L7) · [第 4 轮负载记录](../evidence/2026-11-23-open04/verify-codex-r4-fail-wording/raw/stress-workers.txt)

## 6. 销项判定

依据 [台账原销项条件](../open-items.md#L81)，**不接受冻结版的覆盖与风险说明作为 OPEN-04 销项依据**。有限的「未复现」技术观察有足够复跑证据可接受。销项仍被文档中可独立反证的全量轮数、脚本时序保证、HTTP 请求覆盖和状态推定写法阻断。第 1 轮配置 `.tmp` 失败归属清楚，属于另一项观察。

本轮只在复核工作树新增交接和证据。工作树内受追踪内容 diff 为空。第 4 轮测试证据在这次纯文档复核中有效，最小补齐可以在相同输入下复用现有产物。保留原始文档和日志的历史版本。

## 7. 最小补齐清单

1. [台账 :15、:28、:30、:34–36](../open-items.md#L15)、[执行交接 :70、:96](HANDOFF-2026-11-23-open04-execution.md#L70)、[证据 README :226、:285](../evidence/2026-11-23-open04/README.md#L226) 按原始 TRX 修正总览为冻结时 6 轮、5 轮有判定。第 1 轮 4 轮全量，其中首轮有配置失败、其余 3 轮全过。第 3 轮 2 轮全过。第 2 轮写「0 轮已核定测试」。给第 6 轮交接 :47、:49 加归档勘误，保留原报告。把 9 轮限定为随冻结提交保留的两组证据。
2. [证据 README :237–247、:272](../evidence/2026-11-23-open04/README.md#L237)、[台账 :47–62](../open-items.md#L47) 写明 24 条直记、另有 21 次分层推定。明确固定测试程序集、Windows、目标个例在批次中执行并通过，以及逐轮发现集合是从同输入测试定义推计。表头把执行次数标为「直记或推计」，删除「全部可确认」。
3. [证据 README :116、:135、:291、:307、:314–315](../evidence/2026-11-23-open04/README.md#L116)、[分析 :27、:43–45、:79](../evidence/2026-11-23-open04/ANALYSIS.md#L25)、[执行交接 :62](HANDOFF-2026-11-23-open04-execution.md#L57) 将并发、进程前置检查与耗时限定到实际观察。删去顺序保证与「不可复现」，明确等待上界及真实事件时间未采样。
4. [证据 README :146–149、:225](../evidence/2026-11-23-open04/README.md#L146) 按四条用例列断言和各自覆盖。用第 4 轮六条带 ACE 状态的 TRX 证明该模拟失败路径实际执行。改为「真实回环 HTTP 并发两次，脚本启动由测试钩子计数，未实际启动」，把引用指到 `UpdateConcurrencyTests.cs:133–151`。说明三处标记文件为模拟重启的结果。
5. [证据 README :321](../evidence/2026-11-23-open04/README.md#L319)、[台账 :28](../open-items.md#L28)、[执行交接 :47](HANDOFF-2026-11-23-open04-execution.md#L45)、[分析 :56、:58](../evidence/2026-11-23-open04/ANALYSIS.md#L54)、[自审 :59](../evidence/2026-11-23-open04/SELF-AUDIT.md#L57) 将读到缺失或不可解析状态限定为状态未完整写出时的可能结果，注明脚本结束前也可能读到已写入的终态。保留超时分支「未观测、未验证」。
6. [自审 :3、:53](../evidence/2026-11-23-open04/SELF-AUDIT.md#L3)、[台账 :5](../open-items.md#L5)、[执行交接 :3](HANDOFF-2026-11-23-open04-execution.md#L3)、[证据 README :328–343](../evidence/2026-11-23-open04/README.md#L325) 区分第 4 轮限额中断的启动尝试与稍后实际复核。补可访问的证据引用，或将冻结提交中缺失的两份输入明确标注为未随仓库归档。压力脚本引用实际归档路径并区分示例命令与原始日志所存命令，哈希标明 LF/CRLF。

RESULT: FAIL
