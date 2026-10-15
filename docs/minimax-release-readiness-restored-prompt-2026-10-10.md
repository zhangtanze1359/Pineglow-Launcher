# MiniMax 发布收口执行提示词

日期：2026-10-10，Asia/Hong_Kong。
本提示词仅供复制使用；当前聊天只授权检查，保存此文件不代表已经授权或派发实施。

```text
请按 Codex 2026-10-10 对恢复版本的独立评估，完成松光启动器的有界发布收口，停止在“具备独立复验条件，等待 Codex 独立验收”。

项目绝对路径：D:\松光启动器。不要使用聊天挂载的 f313 旧工作树作为主源码。
先读：
1. 项目恢复说明-2026-10-10.md
2. docs/release-readiness-review-restored-2026-10-10.md
3. docs/release-readiness-remediation-restored-2026-10-10.md
4. docs/release-audit-2026-10-10/run.json、original-boundary.json、legacy-ui.json、existing-snapshot-console.log、rid-locked.log
5. 用户提供的三份 Codex执行/独立验收规则，以及本目录适用规则。

基线：Git HEAD c8aa286f65ad13433129fe0a9e86e2d3b0e9020b只是恢复的远程历史，当前工作区保留10月源码。312输入哈希已通过，LauncherService和LoaderLaunchAssemblyTests是较晚返工版本，不可用较早恢复基准覆盖。0 staged。Node305/305、C#931/931、聚焦14/14、原始两反例2/2、单文件冷启动19/19在本轮通过；聚焦包含在全量中，不相加。

本次执行范围：工作流A/B/C的限定修改与其验证；再完成D中不依赖真人凭据的隔离验证及F的本地候选交付准备。账号/外部服务条件不足时如实NOT_RUN，不擅自取得或写入凭据。8450皮肤改动保持只读，除非另有明确整合授权。

精确允许路径：
- scripts/source-snapshot-inputs.txt、scripts/verify-source-snapshot.ps1；必要的固定候选清单及相关工具/测试。
- dev/PineglowLauncher/Pineglow.Launcher.Services/LocalApiService.cs仅UI根选择；必要的UiBundle.cs/UpdateService.cs仅升级UI迁移与一致性。
- UiBundleTests.cs、UpdateRecoveryTests.cs、LauncherStartupBoundaryTests.cs及对应新隔离夹具。
- dev/PineglowLauncher/Pineglow.Launcher.csproj、packages.lock.json；dev/PineglowLauncher.Tests/Pineglow.Launcher.Tests.csproj、packages.lock.json；必要独立publish lock/策略脚本。
- .github/workflows/build.yml、release.yml仅为依赖锁策略必要适配。若修改先使用authoring-github-workflows技能，运行现有相关守卫/语法检查，不可更改发布触发条件以实际发版。
- 新证据docs/release-closure-evidence-2026-10-10/run-<HK时间>-<GUID>/与本轮交付报告。

只读：其他产品模块、原始恢复C盘目录、原工作树及preserved-worktrees、全部现有验收报告与原始证据、用户配置/凭据、Git已有历史。禁止对这些资产覆盖或批量清理。
禁止：reset/restore/checkout/clean/stash、批量暂存/删除、提交/推送/tag/发布/部署/线上update.json写入、编辑Harness缓存/goal、向其他聊天或其他人发送消息。tag会触发自动发布，不能用tag来试CI。超出允许范围的新问题先报告，不扩大修复。

先记录cwd/HEAD/git status --short --untracked-files=all、staged及关键输入哈希，隔离用户数据、端口、实例和凭据后端。不要复用旧bin/obj或旧exe。

A的RED：原有tracked139+白名单151=290，只收290项，较晚提交原tracked文件变为untracked，漏掉22项（11源码/测试/VerifyHost+11文档），脚本OpenRead AiDiagnoseService.cs异常exit1。按固定候选闭包修复，不能全纳untracked；缺文件须结构化失败与真实非零，不靠数量表面一致。GREEN必须无遗漏的空缓存快照通过所有相关检查。

B的RED：本轮新单文件exe放到残留旧UI/目录旁，清除PINEGLOW_UI_DIR，GET /仍返回旧界面，legacyUiServed=true、exit1。修正式单文件的UI选择或有备份迁移，保留开发/目录模式外置UI。不得直接删除旧UI。复跑旧/无旧UI、configDir/UI、中文/空格、重复启动，再隔离验证下载→替换→重启及失败回滚、配置存活。

C的RED：非RID locked restore成功，带win-x64和PublishSingleFile的locked restore返回NU1004；普通publish改写隔离lock。不能仅往现有共享lock塞RID段破坏非RID和工作流守卫。设计可兼容的锁策略并同时证明两路径。若只能保留未锁定发布，如实记录待用户决策，不能标全链locked PASS。

保留已通过的客户端供应链修复；原探针两个负例继续通过。必要验证依次为：测试清单双向一致、UI闭包、非RID锁定还原、聚焦14、C#全量931/Node305、VerifyHost build与--bisect-smoke（独立端口、生产凭据调用0）、按最终Release参数publish、只EXE中文空格冷启动19、旧UI迁移负例、新版进世界/退出重启与承诺加载器正常路径、故障恢复。测试数量变动需解释新增/删改，不能删失败用例或降格skip。

真实微软/AI/第三方账号成功链路与Gitee发布写入需要正常用户条件/对应授权；条件不足时继续其他独立项并标NOT_RUN，不问用户粘贴密钥。Quilt1.21+与其他承诺加载器必须逐项证实。渲染初始化不等于进入世界或30分钟游玩。

记录每项命令、cwd、+08:00时间、退出码、测试executed/passed/failed/skipped、原始日志、输入/产物hash、GUI实际步骤。原RED/失败完整保留。wrapper exit0/零测试/旧日志/产物缺失不可称PASS。只停止路径核对属于本次GUID沙盒的进程。

最终交付：改动与根因、A/B/C的RED→GREEN、完整候选输入与产物清单、实际核心场景矩阵、未验证条件、发布前仍需事项、真实用户配置与源码范围保护、原始证据绝对路径。不要把自检PASS写成正式发布批准。终态：具备独立复验条件，等待Codex独立验收。
```
