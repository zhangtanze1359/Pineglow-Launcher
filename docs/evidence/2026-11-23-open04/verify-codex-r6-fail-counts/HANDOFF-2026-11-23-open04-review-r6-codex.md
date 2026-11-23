# OPEN-04 第 6 轮独立销项复核

**不接受 `327f8ff4d439ef3592dd8d37a6adbc6286eaf73d` 的覆盖与风险说明作为销项依据。** 第 4 轮原始 TRX 支持目标用例在记录的 Windows 机器和负载下未复现。冻结版材料把第 1 层十条单用例通过摘要写成十条直接状态记录，又把第 1–4 层总计写成时序输出来源。执行者与第 4 轮两侧真正保存的目标 `state=applied`、`waited` 记录合计 **24 条**。

## 1. 受测输入核对

2026-11-23 08:58:42 至 09:01:42（UTC+08）。起始 `pwd`、`git rev-parse --show-toplevel`、`git rev-parse HEAD` 的路径与版本如下。起始 `git status --short --untracked-files=all`、暂存和未暂存 diff 为空。

```text
C:\Users\Administrator\.t3\worktrees\松光启动器\t3-273e3ece-7636d8970fd6e582
C:/Users/Administrator/.t3/worktrees/松光启动器/t3-273e3ece-7636d8970fd6e582
327f8ff4d439ef3592dd8d37a6adbc6286eaf73d
```

```powershell
git -C D:/Projects/松光启动器 diff --exit-code 63dadf7 327f8ff -- dev/ scripts/test-inventory.txt .github/workflows/build.yml global.json
Write-Output "exit=$LASTEXITCODE"
git -C D:/Projects/松光启动器 diff --name-only 63dadf7 327f8ff
```

第一条无 diff，`exit=0`。第二条 `exit=0`，实际列出 **67 个路径，全部在 `docs/` 下**。主工作区 HEAD、`refs/heads/main` 均指向目标。另从执行者基线 `367ab91` 比较至目标，`docs/` 外也无差异。两版受测输入相同，复用第 4 轮 TRX。本轮测试执行数为 **0**。

从本工作树根运行 `python -X utf8 -u docs/evidence/2026-11-23-open04-review-r6/raw/recheck.py`。预期逐轮 XML 计数、结果明细、控制台时序、命令和退出码能够互证。实际复算退出 0，第 4 轮 19 份 TRX 得 **4567 通过 / 0 失败 / 0 跳过**，全部原运行退出 0，目标 16 次 `applied`，`waited` 5.2–5.5 s。三轮全量的 1502 个测试 ID 与名称映射相同。复算初次读取控制台时遇到 UTF-16 BOM，按 BOM 解码后重跑通过。路径测试及服务源码 SHA-256 见复算输出。

[逐条命令、行号、SHA-256 与复算](../evidence/2026-11-23-open04-review-r6/raw/recheck.txt) · [复算脚本](../evidence/2026-11-23-open04-review-r6/raw/recheck.py) · [第 4 轮逐轮命令](../evidence/2026-11-23-open04/verify-codex-r4-fail-wording/raw/commands.jsonl) · [第 4 轮逐轮结果](../evidence/2026-11-23-open04/verify-codex-r4-fail-wording/raw/runs.csv)

## 2. 第 5 轮三条 FAIL 的处置

1. **覆盖表部分通过，时序证据来源仍错。** README:145–149 已拆为三行。时序测试与失败恢复的证据归属已经分开。时序用例的运行先决条件及最终断言见路径测试 :214、:236–254。它启动改名的 `ping.exe`，先断言存活，再执行 `rem noop-restart` 作为重启占位，结束后断言 `applied`、目标为 `NEW-CONTENT`、夹具调用耗时至少 2 s。README:145 把第 1–4 层均写成 `waited` 与状态的来源，这四份文件只给 xUnit 摘要。4.7–4.9 s 来自另行时序采集，5.2–5.5 s 来自第 4 轮 TRX。两组值可写为与占位进程退出后完成替换一致。夹具只记录调用总耗时和最终结果，没有记录退出与替换两个事件时间。

   | 用例 | 实际断言 |
   | --- | --- |
   | 路径测试 :121 | :143–147 明确断言 `replace_failed`、`Applied=false`、目标和备份均为 `OLD-CONTENT`、重启标记存在。 |
   | 路径测试 :156 | :173–177 传入 `rem noop-restart`，断言 `backup_failed` 与目标 `OLD-CONTENT`。备份和标记无断言。 |
   | 路径测试 :214 | :244、:247–254 覆盖起始存活、`applied`、目标内容和耗时阈值。 |
   | 路径测试 :265 | :287–296 使用只读属性。断言结果非 `applied`、目标内容保留、备份和重启标记存在。备份内容未被断言。 |
   | 路径测试 :307 | :336–352 施加拒绝 ACE，成功施加时才断言结果非 `applied`、目标内容保留、重启标记存在。第 4 轮三个路径类和三个全量控制台均明确记录 `replace_failed`、`OLD-CONTENT` 和 `Access is denied`，可确认这些轮实际走到该分支。 |

   :121、:265、:307 的重启替身是 `echo RESTARTED` 写文件，README:146 的「空操作」应改为写标记。合成旧文件的内容及标记证据归属已修，:149 明确真实二进制可运行性未验证。API 行的请求互斥成立，其直接断言来自并发测试 :111、:122–151，测试钩子计数一次，无真实替换脚本进程启动。README:147 应明确这个范围。另 :223 的「另外三条」无法与四条失败场景及各自断言对应。

   [覆盖表](../evidence/2026-11-23-open04/README.md#L145) · [路径测试](../../dev/PineglowLauncher.Tests/UpdateApplyScriptPathTests.cs#L121) · [并发测试](../../dev/PineglowLauncher.Tests/UpdateConcurrencyTests.cs#L111) · [第 4 轮压力全量控制台](../evidence/2026-11-23-open04/verify-codex-r4-fail-wording/raw/full-stress-1.txt#L1264)

2. **汇总算术通过，史料可核定性部分不通过。** `open-items.md:40` 的 9 轮、13518 通过、0 失败、0 跳过可复算。执行者两个全量日志各 3 轮，合计 9012 通过，第 4 轮三份全量 TRX 合计 4506 通过。这个总数只适用于目标提交内随材料保留的执行者和第 4 轮两组全量证据。

   | 轮次 | 来源与结果 |
   | --- | --- |
   | 第 1 轮 | 原复核交接末行 FAIL。冻结提交未归档。原复核工作树内仍有 19 份 TRX，逐条复算 4567 通过、0 失败、0 跳过，含 3 轮全量和 16 条目标状态记录。可考。 |
   | 第 2 轮 | 原复核目录仅有预检和构建记录，构建退出 0，无测试 TRX。任务及台账记载中断无结论。「0」宜明确为「0 轮已核定测试」。 |
   | 第 3 轮 | 原复核交接末行 FAIL。冻结提交未归档。原复核工作树内仍有 23 份 TRX，逐条复算 6071 通过、1 失败、0 跳过。4 轮全量合计 6007 通过、1 失败；失败为第一轮配置 `.tmp` 断言。17 条目标状态均为 `applied`。故「未随本提交归档」成立，「不可逐轮核定」范围过宽。 |
   | 第 4 轮 | 归档交接及 19 份 TRX 可读。复算与第 1 节一致。 |
   | 第 5 轮 | 归档交接 :23 记录 0 次重跑、复用第 4 轮证据，末行 FAIL。 |

   第 1、3 轮只作来源查证，未并入 9 轮统计。原始 TRX 绝对路径逐个列在本轮复算输出，源工作树仅被读取。

   [台账逐轮表](../open-items.md#L30) · [执行者常规全量](../evidence/2026-11-23-open04/raw/open04-layer3-full.txt#L7) · [执行者加压全量](../evidence/2026-11-23-open04/raw/open04-layer4-stress.txt#L9) · [第 4 轮 TRX 复算](../evidence/2026-11-23-open04-review-r6/raw/recheck.txt#L239) · [第 1–3 轮本机来源](../evidence/2026-11-23-open04-review-r6/raw/recheck.txt#L281) · [第 5 轮交接](../evidence/2026-11-23-open04/verify-codex-r5-fail-coverage/HANDOFF-2026-11-23-open04-review-r5-codex.md#L23)

3. **「一致」断言已移除。** `453f18f` 的 :15 和 `0748c6d` 的 :15、:42 保留旧断言。现行台账 :15、:30–57 已无概括各轮一致支持的措辞，其余「一致」是版本或发布一致性。:15 的「5 次复核已完成」仍与同页 :35「复核未完成」抵触。README:189、:204、:215、:227 已统一为四处。:174 的「两条」特指其后补的清理和负载语句，可以保留。

   [历史与当前原文](../evidence/2026-11-23-open04-review-r6/raw/recheck.txt#L112) · [当前台账](../open-items.md#L15) · [README 四处措辞](../evidence/2026-11-23-open04/README.md#L189)

## 3. 执行者自述 A/B/C

**A 通过。** 冻结版台账已移除两句「一致」。README:229 及交接:95 中的同类字样只作被更正旧文引用。:15「5 次复核已完成」的独立矛盾如上。

**B 不通过。** 执行者原始目录恰有五个 TXT，递归查找 TRX 为 0。逐行读出的结果如下。

| 来源 | 目标次数 | 可直接读取的 `applied` 与 `waited` |
| --- | ---: | ---: |
| 第 1 层 :7–16 | 10，过滤器指名目标。每行 `exit=0 passed=1 failed=0 skipped=0 total=1 duration=5 s` | **0**，整份文件无这些字段 |
| 第 2 层 :9–13 | 5，路径类 5 轮 `passed=9 total=9`；同文件并发类 5 轮不计目标 | 0 |
| 第 3 层 :7–9 | 3，3 轮 `passed=1502 total=1502` | 0 |
| 第 4 层 :9–11 | 3，3 轮 `passed=1502 total=1502` | 0 |
| 时序采集 :9–16 | 8，逐条 `exit=0`；两项 xUnit 计数字段为空，另两项未记录 | **8**，4.7 s 六次、4.9 s 一次、4.8 s 一次，均值 4.7375 s |
| 执行者侧 | **29**，其中 10 次目标单跑通过、11 次从批次推计、8 次时序记录 | **8** |
| 第 4 轮 | **16**，逐条 TRX 通过 | **16**，5.2–5.5 s |
| 两侧合计 | **45**，批次推计仍按上述范围 | **24** |

单跑的通过计数与源码 `Assert.Equal("applied", ...)` 支持断言通过的推断。当前台账 :46 的「每行记录」、:51 的 18、:53 与 :57 的 34、README:231 的同类结论与文件内容冲突。没有逐条状态字段的数量为 **21**，其中 10 条有指名的单跑计数，11 条仅有所在批次摘要。执行者 26 个有完整计数字段的批次合计 9107 通过、0 失败、0 跳过，8 次时序采集不加入 xUnit 总分。

[第 1 层](../evidence/2026-11-23-open04/raw/open04-layer1.txt#L7) · [第 2 层](../evidence/2026-11-23-open04/raw/open04-layer2.txt#L8) · [第 3 层](../evidence/2026-11-23-open04/raw/open04-layer3-full.txt#L7) · [第 4 层](../evidence/2026-11-23-open04/raw/open04-layer4-stress.txt#L9) · [时序采集](../evidence/2026-11-23-open04/raw/open04-timing-waited.txt#L9) · [逐份 TRX 复算](../evidence/2026-11-23-open04-review-r6/raw/recheck.txt#L235)

**C 不通过。** 全文读过并搜索指定五份文件。除 A/B 外，以下现行声明仍超出证据。

- README:116、:135 用夹具总耗时直接证明事件先后。可写「起始存活、最终替换成功、耗时达阈值，符合在本夹具中等待占位进程退出后完成替换的预期」。源码 :244–254 没有事件级时间采样。README:145 的时序来源还须按第 2 节修正。
- README:216 声称实际超时分支「本轮未触发」。两处 `WaitForExit(30000)` 的返回值均未记录，日志只能说明未观测到该分支。README:96、:261–263、ANALYSIS:33–38、:54–58、执行交接:38–47、台账 :28 及 SELF-AUDIT:39–41 提到的 11 个应解释为调用含该语句的辅助方法。实际超时次数未核验。
- README:256 的「未只做」与同段实验边界不符，直接写「未做实包自更新」即可。README:7、执行交接:3 与台账 :28、:57 的结论句宜就地限定机器与负载。README:219、执行交接:89 和台账 :15 的「均已修」类完成声明随复核结果一并更新。
- SELF-AUDIT:57、ANALYSIS:62–75 把遥测缺口和复跑设计明示为边界和计划，未发现将计划中的四核、CI、实包或超时验证写为已执行。README:267–276 的命令示例中，第 2 层只有过滤器记录，第 3 层头部无完整命令。现存批次摘要可复算计数，不能反向重建完整命令行。

[时序断言](../../dev/PineglowLauncher.Tests/UpdateApplyScriptPathTests.cs#L244) · [路径夹具](../../dev/PineglowLauncher.Tests/UpdateApplyScriptPathTests.cs#L72) · [恢复夹具](../../dev/PineglowLauncher.Tests/UpdateRecoveryTests.cs#L317) · [复跑设计](../evidence/2026-11-23-open04/ANALYSIS.md#L62)

## 4. 证据边界

1. **CI 通过。** 工作流 :121–124 定义 Windows 和 Ubuntu 执行相同测试项目。路径类有 6 处 Windows 守卫（:85、:123、:158、:216、:272、:315），Ubuntu 正常提前返回并被 xUnit 记作通过。其余 3 处为脚本文本断言。Ubuntu 绿色不增加 Windows 目标样本，实际 CI 运行未核验。
2. **负载通过。** 执行者批次只记录启动 8 个计算 Job 和 1 个 IO Job。已确认脚本清理段没有事后盘点，未采样核数或持续性。独立第 4 轮有单独的进程 CPU 累计值、IO 迭代量和 9 个进程退出记录。两者各有独立适用范围。
3. **实包边界基本准确，有证据表措辞待修。** README:206、:239、SELF-AUDIT:60 包含实包自更新、真实二进制重启、UNC、物理整盘、实际 CI、四核共享 runner 和真实夹具超时分支。原 README:145 把时序证作失败恢复的问题已拆开。现文仍需修正时序出处、标记写入描述和 :216 超时声明。
4. **未复现限定存在。** README:257 和执行交接:62 明确限定当前机器、当前负载。台账 :57 的总结也应带上同一限定。

[CI 定义](../../.github/workflows/build.yml#L121) · [执行者压力清理](../evidence/2026-11-23-open04/layer4-stress.ps1#L83) · [第 4 轮压力采样](../evidence/2026-11-23-open04/verify-codex-r4-fail-wording/raw/stress-workers.txt#L11) · [未验证范围](../evidence/2026-11-23-open04/SELF-AUDIT.md#L60)

## 5. 销项判定

**不接受当前版本。** 按台账 :74 的「未复现且独立复核接受已有覆盖与风险说明」审查。第 4 轮独立 Windows 证据足以支持历史目标在记录机器和负载下未复现。冻结版说明仍把不存在的直接状态字段记为已确认，同时混用时序来源、写宽归档缺口。销项依据需要先改到能由原文件逐条核对。

残余风险为历史触发条件和根因未知、同步 stdout/stderr 可阻塞、30 秒等待不覆盖前置读流、两处夹具忽略等待返回值和子进程退出码，以及资源和调度变化。Windows 上拒绝 ACE 建立失败可以提前返回，本轮已由第 4 轮状态输出确认其实际命中场景。实包自更新、真实二进制重启、UNC、物理整盘、实际 CI、四核共享 runner 和实际夹具超时分支未验证。第 3 轮配置 `.tmp` 失败为独立观察，根因未定。

[销项条款](../open-items.md#L74) · [第 4 轮代表性 TRX](../evidence/2026-11-23-open04/verify-codex-r4-fail-wording/raw/target-01.trx#L8)

## 6. 最小补齐清单

1. [台账 :42–57](../open-items.md#L42)、[README :229–231](../evidence/2026-11-23-open04/README.md#L229) 改为执行者 29 次、两侧 45 次的分项统计，原文直接状态为 8 / 24。列明 10 条目标单跑通过计数和 11 条批次推计。
2. [README :116–149](../evidence/2026-11-23-open04/README.md#L116)、[README :223](../evidence/2026-11-23-open04/README.md#L223) 修时序来源、总耗时语义、标记写入、API 启动钩子及失败四场景。
3. [台账 :15](../open-items.md#L15)、[台账 :30–40](../open-items.md#L30)、[README :224](../evidence/2026-11-23-open04/README.md#L224) 改为「安排 5 轮，其中 4 轮有判定」。明示第 1、3 轮原件保留在原复核工作树，目标提交未归档。9/13518 限定为冻结版执行者及第 4 轮。第 2 轮写 0 轮已核定测试。
4. [README :216](../evidence/2026-11-23-open04/README.md#L216) 改为「实际超时分支未观测或未验证」，其他 11 处调用的表述按第 3 节对齐。[README :7](../evidence/2026-11-23-open04/README.md#L7)、[README :256](../evidence/2026-11-23-open04/README.md#L256)、[执行交接 :3](HANDOFF-2026-11-23-open04-execution.md#L3)、[台账 :28](../open-items.md#L28) 与 [:57](../open-items.md#L57) 直接限定机器和负载，修正「未只做」。用修订状态替换 [README :219](../evidence/2026-11-23-open04/README.md#L219)、[执行交接 :89](HANDOFF-2026-11-23-open04-execution.md#L89) 与 [台账 :15](../open-items.md#L15) 的「均已修」声明。

[最小补齐流程](../evidence/2026-11-23-open04-review-r6/2026-11-23-最小补齐流程.md) · [可复制复核提示词](../evidence/2026-11-23-open04-review-r6/2026-11-23-独立复核提示词.md)

只写入本复核工作树的交接及证据。无产品或测试文件改动，无提交、推送、tag 和 Release。正式阶段审批 `FormalPhaseApproval=NOT_GRANTED`。

RESULT: FAIL
