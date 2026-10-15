# 恢复版本发布收口工作流

日期：2026-10-10，Asia/Hong_Kong。依据：`release-readiness-review-restored-2026-10-10.md` 与 `release-audit-2026-10-10/` 原始证据。

**本文件是检查后方案，不授权实施。只有用户另行授权执行后，下列修改范围才生效。各包停在具备独立复验条件，等待 Codex 独立验收；提交/推送/tag/发布/线上清单写入另需对应授权。**

## 0. 固定现场与发布范围

1. 读取恢复说明、当前评估、run.json 和 312 项哈希，检查实际 cwd、HEAD、git status --short --untracked-files=all、staged 和相关 diff。不得重置为 c8aa286，不覆盖较晚两份返工文件。
2. 将正常源码、执行快照、证据与拟交付包分别记录；禁止用旧 bin/obj、缺失历史日志或另一工作树报告冒充新结果。
3. 初始范围建议 Windows x64；确认本版是否纳入 8450 皮肤应用功能。该副本只读保留，未获具体整合授权不得合并。Linux/macOS 尚无正式运行证明。
4. 保留真实 config.json/.bak/.prev 和恢复资产的存在状态/哈希；使用新 GUID portable 配置、临时端口、内存凭据后端。报告时间均使用 +08:00。

## A. 先修复候选输入闭包

允许路径（授权后）：`scripts/source-snapshot-inputs.txt`、`scripts/verify-source-snapshot.ps1` 以及确有必要的独立候选输入清单/相关验证工具与测试。
只读对照：恢复目录 `delivered-source-verification.json`、本轮 run.json、所有产品模块与现有原始证据。

RED 已有：现有脚本只收 290 项，打开 AiDiagnoseService.cs 时 exit 1。不要覆盖本轮失败日志。
核对 22 项差异，分开 11 项产品/测试/VerifyHost 依赖和 11 项项目文档。选择完整固定候选清单，或补足白名单并增加必需文件闭包断言；不要从“以前 tracked”继续推导“现在必然收集到”。新输入清单必须精确路径、禁止通配资产目录或全部 untracked。
缺项须结构化失败、真正非零，并保存汇总；文件计数相同本身不证明完整。
GREEN：独立空缓存快照逐项哈希一致，额外文件 0，清单/闭包/非 RID restore/聚焦/C#与Node全量/VerifyHost冒烟/publish/19项冷启动通过。当前 312 清单含文档，正式候选可明确选择不同文档范围，但不得悄悄删产品依赖或测试来降低数量。

## B. 修复旧 UI 升级一致性

允许路径（授权后）：`LocalApiService.cs` 的 UI 来源选择；必要时 `UiBundle.cs` 或 `UpdateService.cs` 的有备份迁移路径；`UiBundleTests.cs`、`UpdateRecoveryTests.cs`、`LauncherStartupBoundaryTests.cs` 及必要专项夹具。
禁止直接修改整个账号/AI/下载/游戏启动模块或批量清理用户安装目录。

RED：本轮 `probe-legacy-ui.ps1` 用旧 HEAD HTML + 标记，当前单文件 EXE 旁保留 UI 后 GET / 服务旧标记，exit 1。
最小方案先判定正式单文件运行，再选择当前内嵌 UI；开发/目录构建仍支持有意外置 UI。若选择迁移方案，应备份旧 UI、失败恢复并避免覆盖用户文件；不得以删目录解决。
GREEN 覆盖：无旧 UI、旧 UI、旧 configDir/UI、中文/空格路径、重复启动、目录构建/开发外置 UI；验证页面与 JS/CSS 字节属于同版 EXE。再运行隔离“下载→替换自己→重启”链路，并在下载中断、不能替换、校验不一致条件下确认回滚、原 EXE/配置存活及明示失败。

## C. 收口依赖锁定

允许路径（授权后）：两份 csproj/packages.lock.json、必要单独 publish lock/依赖策略脚本；若涉及 CI 修改，先使用 `authoring-github-workflows` 技能并验证真实 Actions 语法及现有 WorkflowInvariant/BuildLockFileTests。

RED：`dotnet restore <launcher.csproj> -r win-x64 -p:PublishSingleFile=true --locked-mode` 返回 NU1004，非 RID当前PASS；普通 publish 污染隔离 lock。
不能只把 RID 段写回当前共享 lock：现有工作流守卫会拒绝它，且可能破坏非 RID restore。先设计兼容两条路径的独立锁方案或一致的依赖图；维持非 RID/RID都能复验。若选择继续接受未锁定发布，必须在候选风险记录里明确其边界和用户决策，不得写全链 locked PASS。
GREEN：冷副本的非 RID restore/test与正式 win-x64 publish 按选定锁策略通过，两类输入可追溯，源码 lock 无意外漂移；不要关闭 TLS 或忽略 restore 错误。

## D. 最终包核心路径

先冻结最终 EXE 与输入清单，不使用开发输出 exe。每个游戏用独立实例和 Java目录，记录过程、命令行、当前时间新日志与进程归属；只清理自己的进程。

覆盖安装与加载器：Vanilla、Forge、NeoForge、Fabric、Quilt（含承诺的1.21+）；OptiFine 若发布说明承诺则补实测或明确限制。执行新安装/整合包导入→Java17/21选择→启动→进世界→退出→重启，至少代表性实例运行30分钟。导入成功或渲染初始化不能替代世界运行。
覆盖用户任务：真实 WebView关键页与主要操作；队列下载中断/重启/完成100%；实例隔离、取消与失败重试；Mod替换/删除恢复、存档备份恢复和运行中实例守卫。沿用仓库已有门槛，不机械重写所有测试；发现新产品问题先保留RED、标根因、追加明确范围的小包。
任何已有脚本须先审查是否写真实用户路径、清库或停止非本轮PID；只有隔离后才运行。

## E. 账号与外部功能范围

真实微软成功登录由用户在正常授权界面完成；不要让用户把密码/token/AI Key贴进日志。成功后测试切换/续期/取消/超时/离线回退与皮肤模型。需要第三方账号/AI时使用用户提供的正常配置条件，记录脱敏证据。
条件不足时保留NOT_RUN，不用mock或离线账号替代微软成功；未纳入预览范围的功能要在发布说明明确，不能不受控默认承诺全部可用。
8450皮肤功能整合另作独立范围，先做真实差异与依赖核对，不能整份覆盖主 ui/js/launcher.js 或丢本轮已通过修复。

## F. 准备正式候选，再经过独立验收

制作候选输入清单、版本、变更说明、已知限制、许可证、最终EXE/manifest/大小/SHA。临时publish的原生PDB不要误作必要附件。版本1.0.1本轮只是探针值，正式版本另确认。
核对当前Gitee404与GitHub旧1.0.0事实，准备具体线上变更及回退方案；不要提前写update.json。新客户端备用源可取到不代表老客户端升级成功。
先完成全部本地技术与真实场景材料，形成可审阅结果；提交/推送与远程同SHA CI需对应授权，tag可能自动发布，禁止用tag测试CI。没有发布授权时停在准备完毕。
技术与同SHA CI通过后再进行有授权的发布与实际更新源写入，核对发布后最终匿名下载资产、manifest与本地哈希相等、旧版升级成功、失败回退可用。

## 证据与交接

使用新的 `docs/release-closure-evidence-2026-10-10/run-<HK时间>-<GUID>/`，保存原始失败/成功/恢复，不覆盖 `release-audit-2026-10-10/` 或历史材料。
每项记录要求ID、当前输入哈希、cwd、时间/时区、命令/GUI步骤、预期/实际、真实退出码、executed/passed/failed/skipped、版本产物及原始日志。wrapper exit0、0测试、旧log或缺产物不得算PASS。
交付按“已修缺陷、独立待验、未执行条件、当前允许/禁止范围”分开，实施者终态为“具备独立复验条件，等待 Codex 独立验收”。
