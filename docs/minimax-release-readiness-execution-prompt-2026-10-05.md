# 发版前收口：可复制执行提示词

日期：2026-10-05。仅交付提示词，用户后续授权执行后才实施；当前不构成代码修改/提交/发布授权。

~~~text
请完成松光启动器的发版前收口。你负责实现、自检；Codex 负责架构判断和独立验收。终态停在“具备独立复验条件，等待 Codex 独立验收”，不得自行发布或正式放行。

先读：
C:\Users\Administrator\.codex\worktrees\e423\松光启动器\docs\release-readiness-review-2026-10-05.md
C:\Users\Administrator\.codex\worktrees\e423\松光启动器\docs\release-readiness-remediation-workflow-2026-10-05.md
用户指定的 2026-10-03 三份 Codex 工作/验收规则及适用 AGENTS.md。

目录和基线：
- SourceRoot=D:\松光启动器，main，HEAD f145d14eb26b0b7459a28491e764ac8c573085d3 加当前脏源码。
- e423 是同 HEAD 旧快照，只作材料入口，不能当当前实现。
- 开始重新记录完整 status/HEAD/diff/精确输入 SHA/工具版本。
- 每包新 GUID 冷副本复验；数据/配置/恢复区/端口/凭据读写删后端隔离。
- 新证据：D:\松光启动器\docs\release-remediation-evidence-2026-10-05\<唯一 run-id>\<任务包>，不得覆盖旧目录。

当前独立事实：
- 292 项候选输入；C#870/870；21 个 Node 文件269/269；0 fail/skip，exit0。
- actionlint 1.7.7 两工作流 exit0。
- publish成功，但Release只上传EXE+manifest；dist还有UI和原生DLL。
- 单独EXE新目录启动：Skia原生库缺失，exit -532462766。此RED必须关闭。
- pineglow.css/pine-forest.svg 未tracked且不在白名单，候选/产物缺失。
- 默认Gitee清单本轮404；资产fallbackUrl不能解决清单404。
- Build固定11文件漏10个；Release无测试前置互锁。
- snapshot脚本ASCII .cmd写中文证据路径，命令未真正执行仍记步骤PASS。

顺序分包：
A/R02：精确纳入两视觉资源，核本地静态引用闭包，漏资源RED/完整GREEN；不重做视觉。
B/R05：Unicode安全进程/输出与退出码；英文中文空格路径、命令非零、超时、零测试、日志/产物缺失均可靠失败；保留历史RED。
C/R01：真正独立单文件/自解包，或完整目录包+一致升级/完整回滚。同步资源定位/native/附件/manifest/updater，不只加属性或改扩展名。
D/R03：稳定HTTPS主备用清单、404/超时/坏JSON降级、用户自定义源保留、旧客户端迁移、镜像失败真实报告。线上写入留待发布授权。
E/R04：正式测试清单同步，覆盖21个生产前端文件及相关质量工具测试；同候选SHA通过才可Release；失败/零测试/漏必需文件不发布；actionlint通过。VerifyHost先补凭据隔离或一次性用户/runner。
F/R06：最终附件首启/账号/Java原版和承诺加载器/真实游戏/下载恢复/整合包/Mod存档恢复/新闻AI皮肤/旧版升级/故障回滚。缺条件写NOT_RUN，不能用mock替代。

精确允许范围（每包进一步缩小）：
- scripts/source-snapshot-inputs.txt、verify-source-snapshot.ps1、本次包装/资源检查工具与聚焦测试。
- .github/workflows/build.yml、release.yml。
- 主csproj发布资源/原生配置。
- LocalApiService仅ResolveUiRoot/更新handler必要区域；WebShell仅发行资源定位。
- UpdateService/UpdateManifest/ConfigStore仅更新与迁移。
- api.js/launcher.js/i18n.js仅更新协议/用户提示。
- VerifyHost仅测试配置/凭据隔离。
- 直接相关测试/发版文档/本轮新证据。

只读/禁止：
- 真实config/Windows凭据/存档/游戏文件/线上仓库、历史失败与证据、其他任务资产。
- 账号/启动参数/Mod存档/AI新闻业务、安全守卫；不能借打包重写。
- reset/clean/restore/checkout/stash、stage-all、批量删除、提交/推送/PR/tag/发布、恢复Harness、向他人发消息。
- 不扩成全部MR00–MR27，不顺手做Cleanroom/插件等规划。

验证与交付：
- 隔离RED→最小修复→GREEN；不能撤销用户改动造RED。
- 聚焦后必要全量，没新变化不重复全套。
- C#/Node、锁定还原、资源闭包、正式包冷启动、actionlint、版本/manifest一致性。
- RID普通publish未锁定如实记录，不能称全链锁定。
- 每条保存cwd、+08:00起止、输入SHA、真实退出码/测试数/skip、原始日志、GUI及包SHA。
- 凭据读写删统一内存/GUID后端，证明native不可达；portable不等同Windows凭据隔离。
- 浏览器mock/替身/BAT沙盒不代替真实游戏或客户端升级。
- 正式附件新中文/空格目录，不能依赖开发路径、PINEGLOW_UI_DIR、历史UI/DLL。
- 每包交改动清单/前后SHA/REDGREEN；最终交候选附件、输入/资产manifest、未验证矩阵、迁移和限制。
- 版本定稿、提交、远程CI、外部发布未授权的明确留待后续，不私自执行。
- 终态：具备独立复验条件，等待Codex独立验收。
~~~
