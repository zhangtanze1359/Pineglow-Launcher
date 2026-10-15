# DeepSeek Harness FINAL-REMEDIATION-R2 独立验收（2026-10-03）

本轮技术结论：**不通过，暂不放行**。普通恢复、回滚 HTTP 字段与相关常规回归通过；证据封存与覆盖门槛仍失败。RA-01/RA-02 的恢复安全反证场景本轮未执行，不能独立关闭。

`TechnicalAcceptance=NOT_PASSED`  
`FormalPhaseApproval=NOT_GRANTED`  
`ReleaseApproval=NOT_GRANTED`  
本轮只完成验收与长线目标材料，没有实施产品返修，没有向 Harness 发送任务或启动其目标模式。

## 1. 最新对话与验收对象

直接读取本地 Harness 会话：
`C:\Users\Administrator\.dsh\sessions\--D-~677E~5149~542F~52A8~5668--\session-e88dfc99-3fb7-4444-91c8-783bc3303a65\session.v4.jsonl.zstd`。
该文件包含多个 zstd frame，逐帧解压共 1371 条事件；最后完整模型回复为 seq=1366，香港时间 2026-10-03 15:38:21，最后 turn/end 为 completed。

它声明按 S1–S4 修复，新增 15 项常驻回归，C#895、Node238、Bisect19 通过，并明确停在“具备独立复验条件，等待 Codex 独立验收”。新建的 dac78285 会话没有用户提示、没有执行步骤、没有活动目标，不能当成新交付。

- 实际源码根：`D:\松光启动器`，HEAD `f145d14eb26b0b7459a28491e764ac8c573085d3`，含既有大量未提交资产。
- R2 包：`C:\Users\Administrator\.codex\worktrees\0f45\松光启动器\docs\market-execution\FINAL-REMEDIATION-R2\20261003-071724Z`。
- 历史交付根：`C:\Users\Administrator\.codex\worktrees\ed41\松光启动器`。
- 当前 34ca 是旧提交工作树；不是 R2 产品源码。本轮只在它的 docs 中新增材料。
- 本轮隔离源码：`C:\Users\Administrator\AppData\Local\Temp\pineglow-codex-r2-79578cd926db4451a6c4f85bdc37e515`。
- 本轮证据：本目录 `market-acceptance/20261003-r2-independent/`。

R2 输入记录共 289 项，其中 3 项为交付根的额外探针。独立核对其余 **286 项真实源输入，SHA256 和大小全部一致**；新隔离副本也复制这 286 项，missing=0。没有把旧提交工作树当作真实修复版本。

另外独立比对 R1 输入：280项字节不变，4项变化（FileRecoveryService、LocalApiService、FinalAcceptanceRegressionTests、source-snapshot-inputs）；新增2项（PathSafetyGuard、RemediationBoundaryRegressionTests）。旧清单另2项是交付根探针，在源目录本就不存在。结果见 r1-to-r2-source-diff.json，本轮未发现该输入集合内的范围外变化。

## 2. 必需门槛与状态

| 要求 | 方法和预期 | 本轮证据 | 状态 |
| --- | --- | --- | --- |
| 当前输入对应 R2 | 286 源文件逐项 SHA/大小一致 | source-binding-before.json、snapshot-inputs.json | PASS |
| RA-01 可信目标根及恢复安全边界 | 独立运行原反证和可控竞态验证 | 源码已审查；原实施者 5/5 与15/15已核对；当前安全反证未执行 | LIMITED，不能关闭 |
| RA-02 实例/操作/源文件绑定 | 独立运行相关反证；拒绝前不消费源 | 源码已审查；当前反证未执行 | LIMITED，不能关闭 |
| RA-03 结构化失败 HTTP | 回滚失败/回滚成功/metadata失败，分别检查 JSON 与字节 | ordinary-r2.trx/log | PASS |
| 正常恢复及已有凭据隔离 | 普通撤销、JAR+sidecar、禁用态、幂等、冲突、内存后端 | 两份当前 TRX，24/24 | PASS，精确范围 |
| Node 相关完整回归 | 20入口、非零测试数、无 skip | node-tests.log、node-command.json | PASS，238/238 |
| VerifyHost / Bisect 常规冒烟 | 本轮构建、实际 loopback HTTP 和文件沙盒 | verifyhost-build.log、bisect-smoke.log | PASS，19/19 |
| RA-04 真实树封存一致 | 当前文件与清单一致，精确历史例外单列 | real-tree-audit.json/log | FAIL |
| RA-04 原否证 | 原12种输入均符合预期 | original-audit-cases.log | PASS，12/12 |
| RA-04 覆盖与字段完整性 | 多根漏项、未封存同级包、空清单、大小缺失不得产生充分通过 | audit-coverage-summary.json | FAIL / 门槛缺口 |

安全反证未执行不等于产品 FAIL；整体不通过已有证据门槛的实测失败支撑。

## 3. 阻断项

### F-R2-01：P1，最终封存后再次写入包内文件，当前包审计失败

原版 R2 审计器对两份真实交付根复跑：

```text
roots=2, manifestCount=61, fileEntries=874
selfEntries=0, knownMismatches=2, missingDetached=59
failures=1, exit=1
path=source-after.json
recordedSha256=7CA0937ED50944ADF32776FEED37C4F2458D527FD7B1F2194933C2B0CFE7F161
actualSha256=0FDB01944B5926DA9D500EC98D756476A85FB7BB5F73F61F8D97D9A46CA899B2
recordedSize=actualSize=4192
```

这不是“两处已登记历史异常”中的一处，是 R2 当前包新增的不一致。manifest 自身 detached 仍正确，不能因此认为包内全部文件一致。

根因证据来自 Harness 原始 tool/call **seq=1351**：同一调用先运行 seal-evidence，再审计，再运行 capture-baseline -Tag after，后者重写已经封存的 source-after.json。原审计成功发生在最后一次重写之前。原始调用顺序保存在 harness-sealing-call-order.json。

**结论边界**：证明封存操作顺序错误与当前字节漂移；不推测动机，不据此认定所有测试日志造假，也不证明业务源码发生了额外修改。

最小方向：基线终态采集必须先于封存；封存后只允许包外写入；封存后再写必须成为显式失败。保留原 R2 manifest、source-after 与审计结果，在新 correction 包说明差异和来源。不得直接重算旧清单以掩盖失败。

### F-R2-02：P2，审计覆盖只保证“找到的 manifest”，不能保证“要求的目标”

独立临时合成输入中，下列 **3 个覆盖反例**均返回 exit=0：

1. 一个有效根 + 一个已声明但不存在的必需根；
2. 一个有效根 + 一个已声明但为空的必需根；
3. 一个有 manifest 的有效包 + 同根下一个有 results.log、没有 manifest 的同级运行包。

审计器第81行先把不存在的根过滤掉；第220行只检查全局 manifest 总数是否为0。第188–196行的未登记检查只在已发现 manifest 所属包内部运行，因此不会发现同级未封存包。要求的根/包可以整体漏检。

证据：audit-coverage-probes.py、coverage-required-root-missing.json、coverage-required-root-empty.json、coverage-unsealed-sibling-package.json。所有输入为普通证据文本，无产品文件或用户数据操作。

最小方向：先冻结必需根和必需包清单，逐项校验存在性、非空与唯一性；任何根/包缺失都失败。rglob 只能发现实际对象，不能单独充当期望覆盖清单。

### F-R2-03：P2，新包清单字段门槛过宽

同一独立检查还观察到：

- 正确 detached + 空 files 清单 + 无证据文件，exit=0；
- 有文件且 SHA 正确，但记录没有任何 size 字段，exit=0。

原版逻辑把缺失 files 退化为空集合，把 recorded_size=None 当成不校验大小。结合要求“非空、大小、runId、覆盖清单”而言，新运行包的机器门槛仍不足。

这是新包严格契约的缺口，**不将此前历史 schema 兼容策略单独认定为历史产品错误**。后续应显式分开 strict-new 与 legacy-policy，禁止静默降级。空包若确需存在，必须作为有理由的 N/A 对象列入期望清单，不能代表一项测试完成。

七项独立检查合计：有效控制 PASS；重复条目拒绝控制 PASS；以上五项在预期严格门槛下均得到假充分通过。重复条目实际能被审计器拒绝，不能说该功能失效。

## 4. 通过项及计数边界

本轮独立运行：

- C#第一组18/18：原凭据/配置隔离专项13，加R2普通5（3种结构化失败、普通撤销、并发异步内存后端）。
- C#第二组6/6：删除隔离JAR/sidecar、禁用态、还原JAR/sidecar、重复幂等、冲突不覆盖、其它文件保留。两组方法不重叠，本轮独立24项，0失败、0跳过。
- Node全部20入口，238/238，0失败、0跳过。
- Release VerifyHost 构建 exit0，5条编译 warning、0错误；常规Bisect19个PASS，exit0。
- 原版证据审计否证12/12符合预期；它已修好旧版的空单根、错误detached、已知路径第三种字节问题。

另已核对实施者原始 TRX：全量895/895，边界5/5，常驻15/15。**这是实施者原始证据，不是本轮独立895项复跑**；子集不能加到全量总数。本轮没有调用包含受限恢复安全场景的完整C#测试集合。

## 5. 平台限制、静态观察与未验证项

之前 Codex 同一返修验收回合被产品以“possible cybersecurity risk”终止。提示只说明回合失败，没有精确定位被拒绝的命令；记录见 platform-limit-record.json。因此本轮不再运行同一恢复安全反证，也不让另一个执行者或工具替代执行被拒绝范围；仍执行了可独立开展的普通回归和证据完整性检查。

RA-01/02 目前保留“源码已有改动、实施者原证据通过、本轮独立运行证据缺失”的状态。不得沿用上一轮 FAIL 宣称新实现必然失败，也不得沿用实施者 PASS 宣称独立关闭。

额外静态观察：PathSafetyGuard.TryPinDirectory 的有效句柄在第283–309行部分失败 return 路径没有显式 Dispose，只有第312行成功时才加入 PinnedPath。SafeHandle 最终回收能释放资源，但即时释放语义尚有审查项；本轮没有运行压力验证，不把它写成已复现的资源泄漏事故。目录创建后完整链和可控竞态证明亦需要后续符合平台条件的专项独立检查。

真实Avalonia/WebView2 GUI、Minecraft、Windows原生凭据往返、自更新真实进程交接、跨卷/满盘、非Windows运行、干净提交/远程CI均未验证。当前工具的原生电脑控制不可用；没有把Node或浏览器替代结果说成真实WebView。

## 6. 现场保护与复跑

本轮结束核对286项源输入哈希，漂移0；D盘 git status逐行未变。产品源码未修复、未提交、未推送、未发布。原始失败、R2包、历史异常和本轮临时副本均保留。没有读取凭据内容或执行原生凭据往返；没有对真实用户配置做独立前后哈希，因此不声称已证明真实配置整体不变。

普通测试的精确 filter、工作目录、香港时间和退出码保存在 ordinary-r2-command.json、recovery-normal-command.json、node-command.json、host-commands.json。输入与产物来自本轮GUID副本。

真实树只读复跑（输出必须是新包外路径）：

```powershell
$pkg = 'C:\Users\Administrator\.codex\worktrees\0f45\松光启动器\docs\market-execution\FINAL-REMEDIATION-R2\20261003-071724Z'
$out = Join-Path $env:TEMP ('pineglow-audit-' + [guid]::NewGuid().ToString('N') + '.json')
python (Join-Path $pkg 'audit-all-manifests-r2.py') '' $out
$code = $LASTEXITCODE
Write-Output "exit=$code output=$out"
```

独立覆盖检查在本轮新证据目录；不要向原包写输出，不覆盖本轮已有失败记录。

## 7. 后续交付

推荐长线目标：**建立松光启动器 Windows 本机离线质量闭环：先修复证据覆盖与封存门槛，再把六条普通用户流程接成可重复行为验证，最终交付可恢复执行的质量运行器和诚实的验收候选包。**

这是适合目标模式的有界工程目标，允许实施者在获授权后连续完成内部子包；不要求每个子包等待人工，但不得自行批准产品阶段。它不包含当前受限的恢复安全反证、不假定完整MR00–MR27已获授权、不把GUI/游戏/CI缺口自动关闭。

详细流程：同目录 deepseek-harness-long-term-goal-workflow-2026-10-03.md。  
可复制提示词：同目录 deepseek-harness-long-term-goal-prompt-2026-10-03.md。  
当前只交付这两份材料，Harness目标尚未登记或启动。

