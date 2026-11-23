# OPEN-04 第 5 轮独立销项复核

目标 `453f18f03f001982e008f8ed94d8f5b00b12dcaa`。历史目标用例在已核验的 Windows 测试矩阵中未复现。四处指定措辞均通过。当前销项材料暂不接受，台账的累计轮数缺逐轮依据，覆盖表把合成失败场景写成了旧版二进制可运行的证明。

## 1. 受测输入

2026-11-23 07:47–07:49（UTC+08:00）。开始时 `Get-Location`、`git rev-parse --show-toplevel` 和 `git rev-parse HEAD` 的原始输出依次为：

```text
C:\Users\Administrator\.t3\worktrees\松光启动器\t3-1dc5e363-3d50ed11564a4317
C:/Users/Administrator/.t3/worktrees/松光启动器/t3-1dc5e363-3d50ed11564a4317
453f18f03f001982e008f8ed94d8f5b00b12dcaa
```

起始 `git status --short --untracked-files=all` 和 `git diff --stat` 为空。

```powershell
git -C D:/Projects/松光启动器 diff --exit-code 63dadf7 453f18f -- dev/ scripts/test-inventory.txt .github/workflows/build.yml global.json
Write-Output "exit=$LASTEXITCODE"
git -C D:/Projects/松光启动器 diff --name-only 63dadf7 453f18f
```

前一条无 diff 且退出码 0。后一条退出码 0，共 61 个路径，均位于 `docs/`，其中 57 个是第 4 轮归档，其余是执行证据 README、自审、执行交接、台账。主工作区 HEAD 核对为目标。按第 4 轮约定复用测试证据，并重新解析 19 份 TRX、对应控制台日志和 `runs.csv`。复算退出码 0。第 5 轮执行测试数为 0，第 4 轮的证据核定为 19 轮、4567 通过、0 失败、0 跳过。10 次单跑、3 次路径类、3 次并发类、2 次默认并行全量、1 次合成负载全量。目标用例实际执行 16 次，均为 `applied`，`waited` 5.2–5.5 s。三轮全量的 1502 个唯一测试 ID 和显示名对应关系完全一致。

[命令与原始输出](../evidence/2026-11-23-open04-review-r5/recheck.txt) · [第 4 轮逐轮命令](../evidence/2026-11-23-open04/verify-codex-r4-fail-wording/raw/commands.jsonl) · [第 4 轮逐轮退出码](../evidence/2026-11-23-open04/verify-codex-r4-fail-wording/raw/runs.csv)

## 2. 四处措辞

| 位置 | 现文 | 判定与依据 |
| --- | --- | --- |
| 执行证据 README:160–163 | 「观察到 `.tmp` 残留，根因未定」「现有证据未确认并发写入」 | PASS。并发写为待查假说，保留第 3 轮一次观察、隔离复跑及其余全量结果。 |
| 执行证据 README:98、217；执行交接:60 | 「启动了 8 个计算作业 + 1 个 IO 作业」，未采样实际 CPU 核数和持续性 | PASS。旧压力日志第 8 行只记录 `cpu=8 io=1`；第 4 轮独立采样另有来源。 |
| 执行证据 README:100；执行交接:78 | 「日志只记录清理动作被调用，没有执行事后盘点」 | PASS。旧压力日志第 14 行与脚本第 88 行固定文字一致。脚本第 84–87 行的清理命令都使用 `-ErrorAction SilentlyContinue`。第 87 行在删除前枚举文件作为删除输入，第 88–89 行追加文字、写日志。没有清理后再枚举作业或文件、统计数目或报告清理成功状态的步骤。 |
| SELF-AUDIT:57 | 「历史与本次的负载强度、持续时间均未核实」 | PASS。该段限定于执行者批次。 |

在执行证据 README、SELF-AUDIT、执行交接和台账全文搜索六组指定字面。只有 README:100 的无法给出残留数说明和执行交接:78 的撤回原文表格命中「残留 Job」。执行交接的零数值由反引号包围，按去标记后的可见文字核对，也位于旧表述列。

[旧压力日志](../evidence/2026-11-23-open04/raw/open04-layer4-stress.txt#L14) · [压力脚本](../evidence/2026-11-23-open04/layer4-stress.ps1#L83) · [执行交接](HANDOFF-2026-11-23-open04-execution.md#L72)

## 3. 自审复算

六项全部复核。旧摘要合计 `10+45+40+4506+4506=9107`，0 失败和 0 跳过，另 8 条时序行的计数字段为空。旧全量均值 72.80 到 84.4666667 s，增幅 16.025641%。源码 639、643、652、684 行所列工具、参数和常量均匹配。路径类和服务源码 SHA-256 重新计算匹配，全文见命令输出。两夹具等待路径有 6+5 个用例调用点。路径类 6 处 Windows 守卫和其余 3 个文本断言逐一查证，工作流第 121–124 行使 Windows 与 Ubuntu 执行测试项目。

[自审复算原始输出](../evidence/2026-11-23-open04-review-r5/recheck.txt) · [目标及回滚测试源码](../../dev/PineglowLauncher.Tests/UpdateApplyScriptPathTests.cs) · [恢复测试源码](../../dev/PineglowLauncher.Tests/UpdateRecoveryTests.cs)

## 4. 证据边界

1. CI 合规。Ubuntu 的 6 条 `.bat` 用例提前返回，xUnit 显示通过，文档明确从 Windows 复跑样本排除。3 条纯文本断言会执行。保留既有测试守卫是本轮复核范围边界。
2. 负载合规。历史执行批次只记录作业启动数量和清理语句。第 4 轮另有进程 CPU 累计值、IO 迭代、进程退出记录，适用于独立批次。
3. 主文档第 203 行、自审第 60 行完整列出未验证范围。主文档第 145 行仍把目标用例的时序证据写成「失败时保留可运行旧版」的证据。第 4 轮全量控制台分别记录等待、替换失败、备份失败及拒写。路径类源码分别断言目标内容、备份内容和模拟重启标记。目标时序用例不执行重启，故该行需要拆开所引用的用例和结论。
4. 未复现口径合规。README:219 和执行交接:62 都限定当前机器与本次负载。

[第 4 轮负载记录](../evidence/2026-11-23-open04/verify-codex-r4-fail-wording/raw/stress-workers.txt) · [负载全量控制台](../evidence/2026-11-23-open04/verify-codex-r4-fail-wording/raw/full-stress-1.txt#L1264) · [完整未覆盖范围](../evidence/2026-11-23-open04/README.md#L203)

## 5. 销项判定与残余风险

依据冻结版 `docs/open-items.md:47`，**不接受当前整份覆盖与风险说明作为销项依据**。接受第 4 轮 19 份 TRX 所支持的有限技术结论。冻结版台账第 30 行的「60+ 轮全量、43+ 次目标用例」没有可用的逐轮汇总依据。第 4 轮精确为 3 轮全量和 16 次目标实际执行。第 3 轮公开摘要注明 23 轮总运行，其中 4 轮全量。第 2 轮网络中断无结论。台账第 15 行称四次均支持，与第 30 行对第 2 轮的描述不一致；README 第 145 行证据范围也需收紧。

残余风险为历史触发条件或根因未知、同步 stdout/stderr 读取可能阻塞、两夹具忽略等待返回值和子进程退出码，以及不同资源与调度下可能表现不同。真实启动器自更新、真实二进制重启、UNC、物理整盘、实际 CI、四核共享 runner 和夹具真实超时分支未验证。配置 `.tmp` 独立偶发的根因仍未知。

## 6. 最小补齐清单

[最小补齐流程](../evidence/2026-11-23-open04-review-r5/2026-11-23-最小补齐流程.md) · [可复制复核提示词](../evidence/2026-11-23-open04-review-r5/2026-11-23-独立复核提示词.md)

证据与交接只写在本独立工作树。主工作区和产品文件没有写入，也没有提交。正式阶段审批 `FormalPhaseApproval=NOT_GRANTED`。

RESULT: FAIL
