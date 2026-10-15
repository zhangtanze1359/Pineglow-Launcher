# FINAL-REMEDIATION 再返修详细流程（2026-10-03）

状态：待授权实施。关联本目录 `deepseek-harness-remediation-reacceptance-2026-10-03.md`。本计划创建不会启动 Harness，也不会恢复 MR00–MR27 完整目标。

## 1. 背景与目标

本轮实测全量 C#875、Node238、Bisect19 全通过；新5探针仍有4失败（3运行时根因），并复现审计器3种假通过。目标是关闭 RA-01–04，同时保留 R0 凭据隔离、R3 只读列表、回滚真实文案、世界备份绑定与旧 RED。

SourceRoot 固定 `D:\松光启动器`，HEAD f145d14eb26b0b7459a28491e764ac8c573085d3；当前 dirty 文件才是实施对象。原交付根 ed41 及历史包只读。本轮报告/证据/后续交付根固定 `C:\Users\Administrator\.codex\worktrees\0f45\松光启动器`；新证据放 `docs/market-execution/FINAL-REMEDIATION-R2/<实际UTC时间戳>/`，不得复用旧 runId。

## 2. 白名单和保护

允许的待授权改动：

- D 盘服务目录 `FileRecoveryService.cs`、`LocalApiService.cs` 的恢复和替换响应；必要的新路径防护帮助文件只能同服务目录、限本问题，先写精确路径和理由。
- D 盘测试目录 `FileRecoveryTests.cs`、`FileRecoveryEdgeCaseTests.cs`、`FinalAcceptanceRegressionTests.cs`，可新增 `RemediationBoundaryRegressionTests.cs`，将本轮探针转入正式回归。
- `scripts/source-snapshot-inputs.txt` 只追加实际新增输入。
- 本轮交付根下新的 correction、审计脚本与证据包，及新增实施报告。原 ed41 审计脚本复制后改，新脚本不可静默覆盖旧文件。
- 如 structured HTTP failure 需要 ModReplacementResult 字段，仅可局部改已存在 `ModReplacementService.cs` 的该结果类型，不能重构模块。

只读：SecretStore/SecretStoreBackend 及配置相关实现；本轮 286 输入、前轮 281 基线、所有原始 RED/manifest/log、真实 APPDATA、Windows 凭据、真实游戏/存档、无关 UI/CI/发布/其他项目。禁止 reset/restore/checkout/clean/stash、批删、stage-all、commit/push/PR/deploy/release。测试清理只限本次新建 GUID 根，junction 只删链接本身。

基线：确认无并发修改或运行中的执行任务；保存 git status、HEAD、允许路径当前 SHA、完整输入白名单、最小 diff。新 GUID 副本逐项 SHA 绑定，动态检查都在副本中。本轮源 status/284项哈希与真实配置未变，下一轮重新采集，不继承数值当作当前状态。

## 3. 包 S1：可信目标根与祖先链

根因：mod-replace record.GameDirectory=game/mods，检查止于 mods，漏掉 game 的 junction；API 只做词法绑定。

RED：使用本轮 `ModReplaceUndo_MustRejectJunctionAboveRecordedModsRoot`，不用改变业务实现；保存 HTTP200/ok=true 与 outsideFileExists=true。补 record 子目录、游戏根及其父路径、恢复根父路径、直接目标/源 reparse、缺失父目录矩阵。

设计：校验参数由可信实例配置与请求得到；恢复记录的根只是候选，不是信任锚。定位完整有效父链，验证记录根、可信实例根和操作源边界。路径检查异常应拒绝，不能当成“非联接”。目录替换竞态采用句柄式不跟随链接/受控目录访问或等价可证明的限制；只有前缀或写后检查不足以保证不越界。选定方案、平台限制和可复验契约先记入设计证据。

GREEN：请求前存在的 junction 被拒绝，outside 无新增文件，隔离源 SHA/文件/operationId 原样保留。正常替换撤销仍可用；普通冲突不覆盖、显式覆盖、中文/空格/连字符、sidecar 均保留。补可控竞态验证，拒绝必须发生在消费隔离源前；不能把失败文案当作避免越界证明。

## 4. 包 S2：实例与操作绑定

根因：StoredPath 只需在全局恢复根下；record ID/operation 与请求未严格绑定。

RED：保留本轮服务和 HTTP 两条跨实例探针，HTTP 仅改合成 record.json 的 StoredPath/Sha256，GameDirectory 不变；输出 BStoredExists=false 和 A目标B字节。

设计：请求 ID 与记录 ID/operation 必须相等（使用规范化规则一致，禁止删非法字符后误碰另一个ID）；StoredPath 精确归属 `_recoveryRoot/<可信实例>/<可信操作>/files`，相对路径/文件清单不跨操作。验证实际祖先与源 SHA，再执行任何移动、覆盖、删除或幂等清理。既有已恢复记录需明确兼容，不能因为源已不存在就误报或消耗其他源。

GREEN：跨实例与同实例跨操作均拒绝；A、B 原隔离源都不动；伪造 ID/operation、source hash不符、相对路径越界、恢复根联接拒绝。A 的普通恢复与mod-replace撤销通过。拒绝日志只写路径/状态/SHA，不含凭据。

## 5. 包 S3：HTTP 结构化失败

根因：服务结果字段已存在，但 HTTP 转成 `{error}` 文字。

RED：本轮 `RollbackFailure_HttpMustExposeRecoveryFields` 只读目标注入，断言可读文案和字段存在。

实现：局部响应，保持 error 与失败 HTTP状态兼容，同时返回 status、needsRecovery、operationId、recoveryStatus、failedFiles。回滚成功不得误写需人工恢复；失败要保留逐项信息。无需全局更改异常协议或无关路由。

GREEN：提交失败/回滚成功、提交失败/回滚失败、metadata失败、解除阻断后重新恢复，各验证状态与字节；JAR/禁用JAR/sidecar不退化。客户端可用 JSON 取字段，无需解析中文。

## 6. 包 S4：证据门槛与封存

保留全部历史包和本轮失败，复制旧审计器到新包后最小修复。known mismatch 必须验证 manifest旧SHA、记录旧值、已登记当前SHA/size全部匹配；任何再漂移非零。标注“保留的历史异常”而非全部一致。

校验非空目标覆盖清单（顶层MG、全部MR run及新包），缺项/未登记/越界/runId/大小/SHA/自引用/detached均失败。仅零异常返回0；如果采用精确历史例外，单列例外数量与原因，不能吞新错误。新correction封存两份历史文件当前字节及来源/旧值/差异；不改旧清单。

必须保留本轮3合成审计否证：空根、同已知路径第三种字节、错detached。加缺失、漏项、未知漂移、runId不一致阳性失败，以及完整有效包通过。实际树独立审计应与已声明异常严格吻合，审计结果放包外防自引用，另存detached。

## 7. 完整验证与放行条件

在新GUID白名单副本、同版代码上跑：R0专项13、原20验收探针、当前12常驻回归、本轮新5探针及追加矩阵；然后C#全量（如新增5常驻且保留20旧探针，基线880，实际新增数量如实计）、Node全部20入口238基线、Release VerifyHost、19项Bisect、实际清单与审计器合成否证。

保留命令原文、cwd、UTC起止、exit、TRX唯一数量/skip、输入SHA、原始HTTP/字节、RED→GREEN及source/config保护。每次输出使用新路径，不覆盖失败TRX。R0后端不得回退；不准在真实配置或凭据上跑测试。

每包最多3轮有界修复；仍失败、需要越白名单或平台限制无法满足契约则保留现场、报告具体阻断。不要削弱断言、禁用测试或靠known-path豁免凑GREEN。

撤回方式：所有实施前允许文件备份与哈希保存到本轮新包；如需撤回，只对本轮精确差异做逆向补丁且先确认并发变更。不得使用git reset/restore/clean，也不把整份旧文件覆盖到dirty现场。未获得修复授权时不执行撤回。

放行条件：RA-01/02拒绝前不写不消费他人源；RA-03结构化字段正确；RA-04机器门槛识别全部否证且精确封存；同版全量与source保护通过，Codex独立复验。GUI/真实游戏/原生凭据/CI仍独立列未验证。执行终态仅“具备独立复验条件，等待 Codex 独立验收”。
