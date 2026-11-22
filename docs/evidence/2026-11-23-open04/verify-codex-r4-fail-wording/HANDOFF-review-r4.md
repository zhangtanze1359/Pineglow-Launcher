# OPEN-04 第 4 轮独立复核交接

**技术复跑**。冻结的 `63dadf7caf84657c30011fc2770af9f5b56e7cea` 未复现历史更新替换失败。  
**销项判断**。暂不接受该提交的现有证据文档。README 把配置 `.tmp` 的并发归因写为事实，原始 TRX 和源码未证实该归因。旧批次用语还把计算 worker 数写为达到的 CPU 核数。  
`FormalPhaseApproval=NOT_GRANTED`

## 输入及环境

- SourceRoot `D:/Projects/松光启动器`。ExecutionRoot `D:/Projects/_worktrees/pineglow-open04-review-r4`。EvidenceRoot `docs/evidence/2026-11-23-open04-review-r4` 位于本轮 T3 工作树。
- `pwd` 实际输出为 `C:\Users\Administrator\.t3\worktrees\松光启动器\t3-245d9d92-1ad3683ed8bc7f83`，`git rev-parse --show-toplevel` 为 `C:/Users/Administrator/.t3/worktrees/松光启动器/t3-245d9d92-1ad3683ed8bc7f83`，`git rev-parse HEAD` 为 `63dadf7caf84657c30011fc2770af9f5b56e7cea`。初始 T3 工作树干净，主工作区另有既存未跟踪文件。冻结核对见 [source-review.txt](../evidence/2026-11-23-open04-review-r4/raw/source-review.txt) 和 [cleanup.txt](../evidence/2026-11-23-open04-review-r4/raw/cleanup.txt)。
- `git diff --stat 367ab91 63dadf7 -- dev/` 空输出，`git diff --exit-code 367ab91 63dadf7 -- dev/` 为 0。提交链完整，受测输入未改。Windows 10.0.26200 win-x64，16 个逻辑处理器，.NET SDK 10.0.301，xUnit 2.9.3。PATH 前置 `C:\Windows\System32;C:\Windows`，`TEMP` 和 `TMP` 指向执行工作树内 `.review-temp`，四个系统工具均解析到 System32。构建退出 0，22 个警告，0 错误。[构建与环境](../evidence/2026-11-23-open04-review-r4/raw/build.txt)
- 所有测试使用清单的唯一 C# 项目 `scripts/test-inventory.txt:53`。清单双向检查退出 0，35 个前端文件及 1 个 C# 项目。[清单检查](../evidence/2026-11-23-open04-review-r4/raw/inventory-check.txt)

## A 项文件核对

| 项 | 判定 | 证据 |
| --- | --- | --- |
| A1 | 前次矛盾已消除 | README:96 与 :181–185 一致，ANALYSIS:54–58、执行交接:47、open-items:28 一致。四文档全文搜索没有把 30 秒不足或提前读到 `applying` 写作现行推演。两夹具先同步读流再等待（路径类:72–73、恢复类:317–318）。状态未预写，早读可能无有效结果。 |
| A2 | 外部遥测归因已撤回，负载边界仍有两处需要收紧 | 四文档无 `77 s`、`19,839` 或「压力生效」的原字串。README:98、:108 与执行交接:19 记录启动 8 个计算 Job 和 1 个 IO Job 并清理，明确未采集强度与持续时间。README:177 与执行交接:60 仍写「压力最高约 8 核」，旧证据只支持启用的 worker 数。README:98 的两个零残留数字未有单列盘点，旧日志第 14 行记录清理动作。SELF-AUDIT:57 以禁止声称的语境保留「压力生效」，没有把它声明为已证。 |
| A3 | 观测已准确单列，README:160 的因果定性未证 | README:150–161、执行交接:70 保留一次失败、三次隔离和其余三轮全量通过、执行者六轮均未出现。独立读取第 3 轮 `full-01.trx:1945–1950` 和 `runs.csv`，确认断言及各轮结果。ConfigPreservationTests.cs:41、:57、:62、:194 对应独立临时目录、路径覆盖和残留断言。ConfigStore.cs:55–58 的覆盖值是进程共享静态属性，:239–245 取锁，:291–313 同步写、刷盘、轮转、Move。共享路径或 Move 异常仍属排查假说。该路径与更新脚本的备份/替换/校验链不同，按配置失败单列。 |

## B 项独立核对

| 项 | 核对结果 |
| --- | --- |
| 执行者计数 | 通过。逐份旧原始摘要逐行复算为 10 + 45 + 40 + 4506 + 4506 = 9107，0 失败、0 跳过。另 8 条时序缺计数，排除。 |
| 增幅 | 通过。72.3 / 73.9 / 72.2 秒与 83.9 / 84.7 / 84.8 秒，均值 72.80 → 84.4666667 秒，增幅 16.025641%，四舍五入 16.03%。 |
| 源码 | 通过。UpdateService.cs:639 为绝对路径 tasklist + `/FO CSV /NH`（同一行有 find），:643 为 ping，:652 为 fc，:684 为常量 300。 |
| SHA-256 | 通过。路径测试 `9597A417DFFC27B463E3B24603842391BCD5D19EFFEC7E7EBA251B583BAC9A52`，服务文件 `F11C2D24DA3A266F8BDF961B75E49E67A6810BAB9D163EB5B22C18505749AF77`。 |
| 夹具调用 | 通过。路径类 103/140/173/247/289/344，恢复类 343/369/394 通过包装函数、446/472 直接调用核心函数，共 11 个用例可走对应等待路径。 |
| CI 定义与守卫 | 通过。build.yml:121–124 定义 Windows / Ubuntu 测试。路径类 85/123/158/216/272/315 共 6 个平台守卫，Ubuntu 提前返回，另外三条文本断言走正常路径。 |

[历史复算输出](../evidence/2026-11-23-open04-review-r4/raw/historical-recount.txt) · [源码与前轮日志核对](../evidence/2026-11-23-open04-review-r4/raw/source-review.txt)

## 独立重跑

工作目录为 ExecutionRoot，全量采用默认 xUnit 并行。预期单跑 1 条、路径 9 条、并发 8 条、全量 1502 条。每轮命令、目录、预期和时间见 [commands.jsonl](../evidence/2026-11-23-open04-review-r4/raw/commands.jsonl)，起止时间和退出码见 [runs.csv](../evidence/2026-11-23-open04-review-r4/raw/runs.csv)，时间为 UTC+08:00。表中通过/失败/跳过/总数由 TRX、TRX 明细和控制台交叉验证。

| 轮次 | 退出码 | 通过 / 失败 / 跳过 / 总计 | waited（秒） | 整轮（秒） | TRX |
| --- | ---: | ---: | ---: | ---: | --- |
| target-01 | 0 | 1 / 0 / 0 / 1 | 5.3 | 8.044 | [TRX](../evidence/2026-11-23-open04-review-r4/raw/target-01.trx) |
| target-02 | 0 | 1 / 0 / 0 / 1 | 5.2 | 7.971 | [TRX](../evidence/2026-11-23-open04-review-r4/raw/target-02.trx) |
| target-03 | 0 | 1 / 0 / 0 / 1 | 5.3 | 7.994 | [TRX](../evidence/2026-11-23-open04-review-r4/raw/target-03.trx) |
| target-04 | 0 | 1 / 0 / 0 / 1 | 5.2 | 7.922 | [TRX](../evidence/2026-11-23-open04-review-r4/raw/target-04.trx) |
| target-05 | 0 | 1 / 0 / 0 / 1 | 5.2 | 7.942 | [TRX](../evidence/2026-11-23-open04-review-r4/raw/target-05.trx) |
| target-06 | 0 | 1 / 0 / 0 / 1 | 5.3 | 8 | [TRX](../evidence/2026-11-23-open04-review-r4/raw/target-06.trx) |
| target-07 | 0 | 1 / 0 / 0 / 1 | 5.2 | 7.916 | [TRX](../evidence/2026-11-23-open04-review-r4/raw/target-07.trx) |
| target-08 | 0 | 1 / 0 / 0 / 1 | 5.2 | 7.924 | [TRX](../evidence/2026-11-23-open04-review-r4/raw/target-08.trx) |
| target-09 | 0 | 1 / 0 / 0 / 1 | 5.2 | 8.344 | [TRX](../evidence/2026-11-23-open04-review-r4/raw/target-09.trx) |
| target-10 | 0 | 1 / 0 / 0 / 1 | 5.3 | 8.146 | [TRX](../evidence/2026-11-23-open04-review-r4/raw/target-10.trx) |
| paths-1 | 0 | 9 / 0 / 0 / 9 | 5.2 | 9.561 | [TRX](../evidence/2026-11-23-open04-review-r4/raw/paths-1.trx) |
| concurrency-1 | 0 | 8 / 0 / 0 / 8 | — | 5.793 | [TRX](../evidence/2026-11-23-open04-review-r4/raw/concurrency-1.trx) |
| paths-2 | 0 | 9 / 0 / 0 / 9 | 5.3 | 9.62 | [TRX](../evidence/2026-11-23-open04-review-r4/raw/paths-2.trx) |
| concurrency-2 | 0 | 8 / 0 / 0 / 8 | — | 5.803 | [TRX](../evidence/2026-11-23-open04-review-r4/raw/concurrency-2.trx) |
| paths-3 | 0 | 9 / 0 / 0 / 9 | 5.2 | 9.629 | [TRX](../evidence/2026-11-23-open04-review-r4/raw/paths-3.trx) |
| concurrency-3 | 0 | 8 / 0 / 0 / 8 | — | 5.793 | [TRX](../evidence/2026-11-23-open04-review-r4/raw/concurrency-3.trx) |
| full-1 | 0 | 1502 / 0 / 0 / 1502 | 5.3 | 85.172 | [TRX](../evidence/2026-11-23-open04-review-r4/raw/full-1.trx) |
| full-2 | 0 | 1502 / 0 / 0 / 1502 | 5.3 | 85.817 | [TRX](../evidence/2026-11-23-open04-review-r4/raw/full-2.trx) |
| full-stress-1 | 0 | 1502 / 0 / 0 / 1502 | 5.5 | 90.514 | [TRX](../evidence/2026-11-23-open04-review-r4/raw/full-stress-1.trx) |

本轮合计 **4567 通过、0 失败、0 跳过**。历史目标实际进入 16 轮，16 次均为 `applied`，`waited` 5.2–5.5 秒。该数值是夹具从调用脚本到返回的耗时。用例先确认占位进程仍运行，再断言目标内容已替换和耗时至少 2 秒。路径组和三轮全量均有显式拒绝 ACE 分支的 `replace_failed` 与旧内容保留日志，API 互斥由并发组单独验证。配置 `.tmp` 用例在三轮全量均通过，前次失败继续保留。

全量 TRX 按 `testId` 有 1502 个唯一项目，1493 个显示名。首次核对脚本把显示名误当唯一值，已更正为同时核对 ID、显示名、结果明细和控制台计数。[复算输出](../evidence/2026-11-23-open04-review-r4/raw/trx-audit.txt)

本轮负载在两轮普通全量后运行。8 个计算进程分别累计增加约 88.72–89.34 CPU 秒，IO 进程至少记录 4000 次 1 MiB 文件写读，9 个进程均已退出。IO 为临时文件操作。以上采样来自本轮合成负载。[负载原始采样](../evidence/2026-11-23-open04-review-r4/raw/stress-workers.txt)

## 销项判断和范围

依据 `docs/open-items.md:45`，目标用例覆盖和独立复跑已足以支持「在本轮矩阵未复现」。当前证据文档仍不足以签署销项。将 README:160 的并发归因收回到假说层，把 README:177 与执行交接:60 的负载量写成 worker 数，再对冻结输入差异进行独立核对。若测试输入一致，现存 TRX 可复用。详见 [最小补齐流程](../evidence/2026-11-23-open04-review-r4/2026-11-23-最小补齐流程.md) 和 [独立复核提示词](../evidence/2026-11-23-open04-review-r4/2026-11-23-独立复核提示词.md)。

残余风险包括历史失败成因未知、同步管道可能阻塞、夹具忽略等待返回值和子进程退出码、调度差异，以及配置 `.tmp` 的独立偶发。未执行实包自更新链路、真实产品二进制重启、UNC、物理整盘、实际 CI、四核共享 runner、夹具实际超时分支。

清理时 Git 已取消登记，删除磁盘目录时返回 `Invalid argument`。随后核对绝对路径并清理本轮目录，只移除测试留下的目录链接本身，确认目录与 Git 登记均无残留。19 份 TRX 留在 EvidenceRoot。本地 main 终点仍为冻结提交。[清理及冻结证据](../evidence/2026-11-23-open04-review-r4/raw/cleanup.txt)

RESULT: FAIL
