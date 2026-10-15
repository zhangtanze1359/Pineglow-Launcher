# DeepSeek Harness 最新阻断修复报告

日期：2026-10-06。结论：四项等待授权的产品缺陷已实际修复，还修复了独立复验发现的运行器哈希命令依赖。完整发布放行仍不成立。

执行源：D:\松光启动器，main，HEAD f145d14eb26b0b7459a28491e764ac8c573085d3，原有脏改动保留。报告所在 worktree 为 C:\Users\Administrator\.codex\worktrees\e423\松光启动器；证据根为其 docs/harness-blocker-fix-evidence-2026-10-06。未提交、推送、打标签或发布。

## 最新阻断的实际身份

直接读取当前 session_projcache 并保存 before/harness-state.json：会话 session-466887a4-24c0-4da2-b92e-a6d4801f1be2，cwd D:\松光启动器；goal-93db6df4-e723-47e6-a8c1-4b5c466ddbdb revision 7，phase blocked，78 轮；llmRetry 为空。blockedReason 为模型报告的待授权补丁、外部凭据/发布条件、审计范围与产品决策，不是当前 HTTP 400 错误。

用户本次已明确授权修复。审查旧补丁后实施自己的受限修复，保留历史失败，不将实现者 PASS 当作验收。

## 修复与行为证据

| 阻断 | 实际修复 | 针对性测试 |
| --- | --- | --- |
| B0：首次配置的实例/账号 ID 每次变化 | ConfigStore 在进程内锁下生成并原子写 JSON；首次初始化不走凭据保存，写入失败会报告错误；已有配置缺实例时持久化生成 ID | LoadAsync_FirstRun_PersistsStableInstanceAndAccountIds；LoadAsync_FirstRun_DoesNotWriteOrDeleteExistingCredentials；LoadAsync_ConcurrentFirstRun_ReturnsOneIdentity；LoadAsync_ExistingConfigWithoutInstance_PersistsGeneratedId；LoadAsync_UnwritableInitialPath_ReportsFailureInsteadOfTransientIdentity |
| B1：加载器覆盖父版本 arguments，丢 native/classpath | VersionResolver 按父→子顺序拼接参数数组；保留 game/jvm、规则对象、多层继承和 libraries | ResolveAsync_LoaderChild_RetainsNativeClasspathGameAndRuleArguments（Fabric/Forge/NeoForge/Quilt）；ResolveAsync_MultipleLevelsAndGameOnlyChild_KeepsAllAncestorJvmArguments |
| B2：Maven URL 重复 artifact 目录 | InstallerLoaderService 去掉 groupPath 后重复的 artifactId 段 | GetLoaderVersionEntriesAsync_OfficialMavenLayout_DoesNotDuplicateArtifact（Forge/NeoForge） |
| B2b：安装器缺 launcher_profiles.json | LoaderInstallHelper 在真实启动路径用 CreateNew 建最小 profile；已有文件保留原始字节；写入错误不吞掉 | RunInstallerAsync_PreparesMissingProfileAndPreservesExistingBytes（已有/缺失） |
| 验证运行器不能加载 Get-FileHash | run-goal-validation.ps1 改用 .NET SHA256 对文件流计算相同字节哈希，不依赖模块自动加载 | runner hash gate works when Get-FileHash is unavailable；整个运行器 29/29 |

增加三份 C# 回归测试文件，并纳入 source-snapshot-inputs.txt 精确白名单。源码接口未增加测试专用公开 API，账户凭据测试使用内存后端。

## 独立复验及失败保留

- 第一、第二次在原 D 工作区启动测试：临时目录创建被拒绝，旧 ConfigBackupRotationTests 对照也失败。保留 red.log、red-isolated.log、environment.log 与 TRX；这些是环境失败，不是业务 RED，根因未完全证明，也未调整系统 ACL 或安全设置。
- 从当前 tracked 字节与精确白名单创建全新隔离 RED 副本：14 项执行，12 FAIL / 2 PASS，保存 red-snapshot.log 与 red-snapshot/red-snapshot.trx。四类缺陷均得到有效失败。
- 首次修复后候选快照：C# 917/917、独立 EXE 冷启动 19/19，但 Node 运行器子测试因 Get-FileHash 不可用而失败；整体正确为 FAIL，完整保留 green-snapshot 与 green-snapshot.log。
- 新增哈希环境回归先 RED（runner-hash-red.log），修复后运行器 29/29 GREEN（runner-hash-green.log）。
- 最终候选：161 tracked +149 白名单 =310 输入，无额外源码/预存 bin/obj；final-all-inputs.json 已逐字节比较源与执行副本，310/310 一致。final-snapshot.log 整体 PASS / exit 0：Node 305/305（23 文件）、C# 917/917、UI 闭包/测试清单/非 RID locked restore/Release build/VerifyHost build/普通 win-x64 publish 通过、真实独立 EXE 冷启动 19/19。新增针对性测试另有 focused-green/green.trx，14/14 通过。RID locked restore 的已知未通过边界单独保留。

本次限定技术验收：PASSED。完整发布验收：NOT_PASSED。Harness 会话自动恢复：NOT_RUN。最终执行副本为 C:\Users\Administrator\AppData\Local\Temp\pineglow-snapshot-04ed9c727a6c4cf29efa907852390a2f。

最后回读发现会话状态在本次工作期间发生变化：cache 于 13:47:57 更新，goal row seq 10556 / ver 6 的 current 已变成 null，seenGoalIds 仍保留上述 goal；lastTurn 仍 78，openTurnStartSeq 为 null。因此初始 blocked 的记录仍是本次阻断依据，但不能声称“最后仍有一个 blocked goal”或“已经继续执行”。该状态变化不是本次工具写入；实际恢复/重新发起剩余验收仍未验证。保存 final-harness-state.json 作为最终状态。

## 边界与剩余任务

本次真实 HEAD：修正后的 Forge 安装器地址 HTTP 200，长度 6056587；NeoForge HEAD 返回 TLS 建连异常。保留 official-maven-head.json。未关闭证书校验。此前 Forge 实际 Java 安装器的 SSLHandshakeException 证据仍需用新构建复验；本次地址/准备文件测试不能替代真实下载、安装和启动。

当前完整 Vanilla/Fabric/Quilt 游戏回归、Forge/NeoForge 联网安装、真人 Microsoft 登录/权益、真实 AI 密钥、正式更新源附件闭环仍需分别验收。RID 发布还原锁定探测有 NU1004，普通发布成功不代表 RID 链已锁定。既有完整产品范围尚未全部独立审计；Harness 自报的未审范围百分比本次没有重新测量。

源码修复不会自动恢复已经 blocked 的 Harness goal。本次没有改写活跃压缩会话、缓存 goal 或伪造继续执行状态。后续提示已放在 docs/minimax-harness-continuation-prompt-2026-10-06.md，详细流程见 docs/harness-blocker-remediation-workflow-2026-10-06.md。恢复后的范围应是剩余本地验收，不能自动扩大为正式发布。
