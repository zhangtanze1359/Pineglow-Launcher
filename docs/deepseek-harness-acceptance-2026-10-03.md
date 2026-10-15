# DeepSeek Harness 最新任务独立验收（2026-10-03）

**结论：部分通过。W1 的 10 条路由可达性修复有效，W2 约定的可达性测试及既有回归通过；整批证据封口仍需修正，存档功能不能宣布完整验收通过。** 独立复验发现同一根因引起的两个存档对象契约错误，以及一个证据清单错误。没有观察到存档被覆盖或数据丢失，不把对象错误夸大成数据损毁。

| 判断对象 | 本次判断 | 含义 |
|---|---|---|
| W1 分派长度守卫修复 | 技术 PASS | 10 条路由已接通；业务 handler 未被重写 |
| W2 预定可达性验证 | 技术 PASS，边界有限 | 16 项全部通过，改前版本独立重现 15 RED / 1 GREEN；不等于 handler 完整业务正确 |
| W3 文档同步 | 约定文件已同步 | API、roadmap、台账已更新；备份按存档隔离的真实限制须补记 |
| 整批交付证据 | 暂不封口 | manifest 对自身的哈希和大小记录过期，需追加修正版 |
| 存档备份列表与恢复对象契约 | FAIL | 两个失败用例各独立复跑仍失败，见 A-01 |
| 真实 GUI / Explorer / 游戏 / CI / RID 发布 | 未验证 | 本批未作这些承诺的实机验收 |
| MG1 / 后续 MR 任务 / 发布批准 | NOT_GRANTED | 本次不执行阶段推进、修复、提交、推送或发布 |

## 1. 最新会话与授权核对

- 最新项目会话：`session-2e72bd8c-79b8-434c-8c38-d5f3a1190634`，cwd 为 `D:\松光启动器`。
- 原始压缩记录：`C:\Users\Administrator\.dsh\sessions\--D-~677E~5149~542F~52A8~5668--\session-2e72bd8c-79b8-434c-8c38-d5f3a1190634\session.v4.jsonl.zstd`，最后修改于 2026-10-03 02:47:57 +08:00。
- 解码得到 453 个连续 zstd frame、2 轮任务、138 个 assistant/message。原记录只读；审查工具只提取用户要求、问题选择和计划批准记录，未读取凭据文件。
- 用户先要求整理计划，随后说“进行下一步”。会话 seq 249 中的问题答复明确选择“直接在主工作区改（推荐）”及“同批带上 W3 文档同步（推荐）”；seq 302 记录计划已批准。**直接修改 D 盘与带上 W3 有会话依据，不能误判为未经授权。**
- 获批计划明确不新增 `VerifyHost --route-smoke`，复用既有 `--bisect-smoke`；因此本批没有 route-smoke 开关不是漏做。
- 最新完成结果固定入口：`docs/market-execution/W1-W3-route-closure/CURRENT-RUN.txt` → `20261002-182304Z`。旧 MR00 的 CURRENT-RUN 不是本次最新路由修复结果。

上述用户答复与批准原文见 [会话提取](market-acceptance/20261003-W1-W3/session-summary.json)。产物完成时间为 2026-10-03 02:47 左右，不能因 run-id 使用 UTC 的 20261002 前缀而误认成旧一日任务。

## 2. 工作区与字节核对

| 对象 | 当前核验 |
|---|---|
| SourceRoot | `D:\松光启动器`，main，HEAD `f145d14eb26b0b7459a28491e764ac8c573085d3` |
| DeliveryRoot | `C:\Users\Administrator\.codex\worktrees\ed41\松光启动器`；只用于本次文档和证据 |
| 正式独立快照 | `C:\Users\Administrator\AppData\Local\Temp\pineglow-independent-4b2ee15c82e349518501c1ffb40104f1` |
| 快照输入 | 全部 tracked 工作区文件 + `scripts/source-snapshot-inputs.txt` 精确白名单，去重 216 文件；复制时逐文件 SHA256 核对 |
| 未纳入的 C# 源 | 仅 `dev/PineglowLauncher.Tests/DownloadResumeTests.cs`，0 字节，不产生类型或测试 |
| 本次执行环境 | Windows，.NET SDK 10.0.301，Node v24.19.0 |
| 本次源目录保护 | 216 个输入结束时 SHA 全不变；git status 前后均 1290 行，无差异 |

不能用 ed41 的干净生产源码代替 D 盘当前脏源码。所有构建、listener、文件操作探针和复现均在独立副本与自建合成目录内执行。独立探针只写入快照测试项目，未加入 D 盘项目。

交付列出的 7 个最终文件 SHA256 全部与当前源文件一致。`LocalApiService.cs` 最终 SHA 为 `F9F662A85922C08AFF7715B1F3C0179729FD1FC46401474D2EC816108DD3FF77`；改前副本 SHA 为 `1EEF455D57897DCED28AFAEBC6F064B23EAF96D90692CC0050F6A9AA8BB03A5E`。独立生成的源码差异只有四个 hunk，位于分派块约 412–562 行，确认四组守卫从互斥的长度 4 块移到长度 5/6/8 的前置块；其余 handler 未改变。

文件状态证据见 [源目录保护核对](market-acceptance/20261003-W1-W3/source-readonly-audit.json)、[最终产物核对](market-acceptance/20261003-W1-W3/final-artifact-audit.json)、[独立源码 diff](market-acceptance/20261003-W1-W3/independent-source.patch)。未通过恢复到 Git HEAD 的方式获取基线，也未覆盖已有脏工作。

## 3. 独立执行结果

这些数字为 Codex 本次真实复跑，非转述 Harness 的绿色报告。C# 全套在加入独立探针前执行，保持交付原有的 628 项计数；之后只跑独立探针。没有把重跑次数累加成新的独立测试数量。

| 检查 | 退出码 | 结果 | 原始证据 |
|---|---:|---|---|
| 新增 RouteReachabilityTests | 0 | 16 / 16，跳过 0 | `01-focused.log`、`route-original.trx` |
| 原始 C# 全套 Release | 0 | 628 / 628，跳过 0，约 56 秒 | `02-full.log`、`full-original.trx` |
| 正式 11 个 Node 测试文件 | 0 | 184 / 184，失败 / 跳过 0 | `03-node.log`、`node-inputs.txt` |
| VerifyHost Release 构建 | 0 | 成功 | `04-verifyhost-build.log` |
| VerifyHost --bisect-smoke --port 49137 | 0 | 10 个检查全部 PASS | `05-bisect-smoke.log` |
| Codex 独立边界探针 | 1 | 15 项，13 通过 / 2 失败，跳过 0 | `06-independent-probes.log`、`independent-probes.trx` |
| 改前副本 + 同一新增测试 | 1 | 16 项，1 通过 / 15 失败 | `07-independent-red.log`、`independent-red.trx` |
| 两个失败用例再次复跑 | 1 | 0 通过 / 2 失败 | `08-failures-repeat.log`、`independent-failures-repeat.trx` |

TRX 中测试时段（+08:00）分别为：新增测试 02:55:54；全套 02:57:31–02:58:28；探针 02:59:19–02:59:20；改前 RED 03:02:35–03:02:36；失败复跑 03:02:38。完整时间与计数见 [test-summary.json](market-acceptance/20261003-W1-W3/test-summary.json)，执行命令见 [commands.json](market-acceptance/20261003-W1-W3/commands.json)。构建日志含既有 CS8618、xUnit 分析器等 warning，不能描述成“零警告构建”。

13 个通过的独立探针覆盖：

- 4 个多余路径段请求，包括 `/delete/extra`、`/launch/extra`、Mod 删除及存档删除的额外段，均 404 且不修改文件或实例。
- 4 个错误 HTTP 方法请求，均 404 且无文件修改。
- 缺失、错误会话令牌各一项，均 401 且不删除 JAR。
- URI 编码的中文及空格 Mod 文件名可正常切换，内容字节保持。
- 备份后修改原 `level.dat`，恢复响应所指向的新副本包含原字节；原存档保持修改后字节。删除只移除所请求原存档，另一个存档保持。**当前恢复语义是创建可用名称的副本，不是原位覆盖。**
- Bisect 开始后放弃，原来已经禁用的 Mod 保持禁用；所有文件内容与前态一致。

独立探针源码见 [IndependentRouteProbes.cs](market-acceptance/20261003-W1-W3/IndependentRouteProbes.cs)。夹具是合成 `.jar` 和 `level.dat`，真实 HTTP listener 与本地文件 IO；不是实际 Minecraft 模组运行或真实世界游玩验收。共享 harness 的 RealConfigGuard 比较每个相关用例前后的真实 config / bak / prev SHA；本次没有抓取一个覆盖所有旧 C# 测试的外部真实配置全程快照，因此不夸称已额外证明每个历史测试都不落盘。

## 4. A-01：备份对象边界错误（P2，已稳定复现）

**同一根因有两个失败表现：路径中的 `folderName` 被忽略。** 前端 `ui/js/launcher.js` 的单个存档弹窗调用 `listSaveBackups(instanceId, folderName)`，渲染服务端返回的全部行，并把当前 folderName 继续用于恢复请求。它没有在前端过滤 groupName，故错误不是仅能手工构造的理论边界。

复现使用两个不同目录 worldA 与 worldB，只备份 worldA：

1. `POST /api/instances/{id}/saves/worldA/backup` → 200。
2. `GET /api/instances/{id}/saves/worldB/backups` → 200，但 backups 中含 `worldA-...zip`，`groupName=worldA`。按存档请求的列表没有隔离对象。
3. `POST /api/instances/{id}/saves/worldB/backups/worldA-...zip/restore` → 200，并创建 `saves/worldA (1)`，无视 URL 所指 worldB。
4. 两项用例再次运行，仍出现同样失败。原始响应保留在日志及 TRX。

根因位置：`LocalApiService.cs:1678–1685` 列表处理器直接调用实例范围 `GetBackups(instance)`；`:1687–1696` 恢复处理器在该全量列表里仅按 FileName 选中。`SaveBackupService.RestoreBackup` 随后按 ZIP 顶层存档导入，返回新的副本路径。**并未观察到跨存档覆盖**，风险是用户在 worldB 弹窗看到并恢复 worldA，界面对象与实际结果不一致。

归因：上述 handler 与服务体在 Harness 改前副本中已存在，W1 的独立 diff 没有改动它们。故这是本轮接通路由后暴露的历史问题，不能声称是 W1 新引入；同时也不能让“路由已接通”掩盖真实业务缺陷。

W2 的 10 条可达性断言允许 200/400/409/500，目的是识别是否还落入兜底 404；单个存档 happy path 也没有两个对象的交叉检查。**这一测试设计能证明路由已命中，不能证明列表/恢复对象契约正确。** 500 被允许不自动等于错误的测试，但不能当业务成功证据。

建议单独修复对象绑定并补 HTTP 回归；保留当前恢复为副本的语义，未经明确需求不改成原位覆盖。详细执行边界见单独修复流程。

## 5. A-02：manifest 自身记录过期（P2，证据封口必须修正）

交付 `manifest.json` 的 files 数组共 26 项。其他 25 个文件的 SHA 和大小均核对通过；唯一失败是 `path=manifest.json`：记录大小 6475 B、SHA `502C47C0BE64F40A3D1E15F15A749A2CDE8DE87EE94FFAEB07BAB89A17026993`，实际 8825 B、SHA `DDD924374624883B231F9CAEAEA1F3F12079DB6D31493FA2A871A1E4C36EA595`。

这使“按 manifest 逐项校验全部成功”的声明不成立，但没有证据表明其他原日志被篡改。manifest 不应纳入自身内容哈希，否则写入记录本身会继续改变哈希。建议保留原记录，追加修正版 manifest，只列外部产物；manifest 自身使用 detached 的 `.sha256` 文件封口。不要反复回填自身 SHA，不修改原 RED 日志。

核对结果见 [implementer-manifest-audit.json](market-acceptance/20261003-W1-W3/implementer-manifest-audit.json)。

## 6. 其他边界

- **打开存档目录的目标不一致（源码发现，实机尚未验证）**：`:1708–1716` 先找所选存档，响应 path 为该存档 FullPath，但实际调用 `OpenSavesDirectory(instance)` 打开整个 saves 父目录。不把未运行 Explorer 的静态发现写成“实机失败”；需明确产品想打开单个存档还是总目录，再作专门验收。
- `docs/engineering.md` 与 `docs/ai-*.md` 的旧声明仍待后续同步。获批 W3 列表没有授权本轮改这些文件，不列为本轮越界或漏改。
- 本次不复跑 publish，不修改 RID 锁定问题；已知 NU1004 属 MR19。GUI/WebView2、游戏、真实 Explorer、GitHub Actions、跨平台仍未验证。
- 没有因为 Harness 出现过误写 5 行空文件、BOM 丢失就直接否定最终结果：改前副本、最终 SHA、BOM 和实际回归均已复核。恢复后的当前生产文件有效。

## 7. 后续交付与授权边界

本次验收已经完成。保留 W1/W2 技术通过事实；整批封口先补 A-02，存档按对象完整业务验收先补 A-01。不要以本报告为 MR03–MR07、MG1 或发布的授权。

已交付两份独立文档：

- [详细修复流程](deepseek-harness-remediation-workflow-2026-10-03.md)：证据封口 → 对象契约 RED → 最小修复 → GREEN → 独立复验。
- [可直接交给执行 AI 的提示词](deepseek-harness-remediation-prompt-2026-10-03.md)：明确源目录、证据目录、文件白名单、测试命令与停止条件。**本次未调用 Harness / MiniMax / OpenCode 执行修复。**

## 8. 本次输出自检

采用 agent-self-evaluation 进行报告自查，平均 4.2 / 5；这是对本次验收输出的质量自评，不是项目门禁。

| 维度 | 分数 | 证据与改进 |
|---|---:|---|
| 准确性 | 5 | TRX、原始响应、哈希及用户批准分别取证；未夸大成覆盖或数据丢失 |
| 完整性 | 4 | 完成最新包及关键反例复验；真实 GUI/Explorer 不在本次实机覆盖，另列待验 |
| 清晰度 | 4 | W1 PASS 与业务 FAIL 分列；长报告需通过最终答复的摘要链接阅读 |
| 可执行性 | 4 | 有保留 RED 的探针与两份修复交接；真实打开目标需在后续产品契约中明确 |
| 简洁性 | 4 | 详细证据集中在附件；分析过程个别工具输出过长，后续应优先提取字段 |

改进优先级：先修正备份对象契约，再补 GUI 的目录目标复验；对外以简短结论和证据表为入口。自检判断：用户可从报告看到实际复跑、缺陷证据和下一步边界，能合理同意这一结论。
