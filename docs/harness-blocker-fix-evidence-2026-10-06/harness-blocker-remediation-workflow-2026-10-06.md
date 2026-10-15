# DeepSeek Harness 阻断修复后的继续验收流程

日期：2026-10-06。执行根：D:\松光启动器。用户已授权本次阻断修复；正式 commit/push/tag/Release/Gitee 发布不在本次授权内。

## 已处理的阻断

当前会话 session-466887a4-24c0-4da2-b92e-a6d4801f1be2 的 goal-93db6df4-e723-47e6-a8c1-4b5c466ddbdb 是模型报告的 blocked，而非 HTTP 400 重试。四项待授权补丁已由 Codex 审查后实际实施：默认 ID 持久化、arguments 继承、安装器 URL、launcher profile。还修复了本次独立复验发现的 Get-FileHash 环境依赖。

源码中的现有 R01–R06 改动全部保留。不要再次运行旧 apply-and-verify.ps1：旧补丁已不再适用于当前文件，且首次配置保存补丁有凭据副作用。不要将上一轮“903/904”测试数当作当前验收阈值。

## 本地继续验收

1. 阅读同目录 harness-blocker-fix-report-2026-10-06.md，检查实际源码和最终证据，不只读结论。
2. 记录当前 HEAD、脏状态、tracked 当前字节和 scripts/source-snapshot-inputs.txt 的精确输入；仅使用新 GUID 隔离副本运行，不混用旧 pg-patched 或已污染的历史临时仓库。
3. 运行 scripts/verify-source-snapshot.ps1 -RepoRoot D:\松光启动器 -EvidenceDir <新证据目录>。核对 Node 正式清单、C# 测试实际执行数、UI 闭包、Release 构建、VerifyHost、单文件附件冷启动。任何失败必须保留原始日志，退出非零。
4. 运行真实 Vanilla/Fabric/Quilt 启动回归：使用合成离线账号、隔离实例和已有合法包，核对启动参数、库目录、实际 Java 进程与游戏日志；不能把参数解析测试替代实际进游戏证据。
5. Forge/NeoForge 联网安装仍需独立复验。记录 Java 版本、完整 TLS cause、端点状态和失败阶段；使用已配置且授权的网络/代理做对照。不能关闭证书校验。修正 Forge URL 的 HTTP 200 不代表整个安装成功；本次 NeoForge HEAD 仍出现 TLS 错误。
6. 微软真人账号、正版权益、AI 实际提供商密钥验收需要对应账号或密钥。凭据不要写入文档、日志、源码或提示词。

## 发布前剩余任务

- 当前 packages.lock.json 不覆盖 win-x64 RID 的全部依赖，发布还原仍是明确的未锁定边界。补齐 RID 锁定验证或作出经批准的可复现性例外。
- 核查正式更新源、实际发布附件/manifest/SHA256 的闭环；没有正式发布物时不得伪造“更新闭环通过”。GITEE_TOKEN 只通过受保护的环境/凭据注入。
- 原 Harness 记录有 ≥70.8% 既有源码范围未逐行复审。这是完整产品审计缺口，不能因为本次五处修复关闭而宣称全产品可发布。列出的“20 项产品决策”需逐项确认适用范围；不应阻止已经授权的本地缺陷复验。
- 将技术验收与正式发布批准分开。未授权 commit/push/tag/发布时可完成本地验收并交付真实结果，不能重复申请源码修复授权，也不能无限续跑。

## 会话状态

本次没有覆盖压缩会话、缓存 goal 或伪造 resumed/complete 状态。初始 goal 为 blocked；最终 cache 的 current 已被清为空，lastTurn 仍 78，且无 open turn，不能声称会话正在继续。该变化不是本次工具写入。产品源码修复不自动恢复已停止的执行。继续时应把下一步缩到上述本地验收；外部条件不足的项目分别标 BLOCKED/NOT_RUN，停止空转并交付。
