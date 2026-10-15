# 自动更新提醒与版本介绍：本地修复记录

2026-10-15，Asia/Hong_Kong。

本报告保存本地修复完成、创建提交之前的验证状态；后续经用户授权的本地提交以 Git 历史为准。未推送、打标签或发布；不将本次实现者自检称为独立验收或发布放行。`FormalPhaseApproval=NOT_GRANTED`。

## 用户可见变化

- 启动 3 秒后自动检查，前台运行每 10 分钟检查；窗口恢复前台且距离上次检查超过 1 分钟时补查。隐藏窗口不轮询。
- 发现新版显示版本号、新版本介绍、“稍后再说”和“前往更新”。一次启动中同版只自动弹一次；页面提醒条可再次打开介绍。已有业务对话框关闭后再显示自动提示。
- 更新说明按纯文本显示并保留换行，长文可滚动，远程 HTML 不执行。
- 下载和立即重启仍需用户点击，不因后台检查自动下载或退出。
- 并行查询更新源，选择版本最高的有效清单；同版保持地址链原优先级。检查和下载继续调用同一个选版方法，Gitee 的有效旧清单不会遮住 GitHub 新版。
- 对官方仓库、与清单版本相符的资产 tag，尝试读取对应 GitHub Release 正文（4 秒超时）；失败、空正文或 tag 不符时保留清单介绍。

## 后续发版如何填写介绍

打 `v1.0.1` 标签前编辑 `release-notes/v1.0.1.md`；下次版本为 `v1.0.2` 时新增 `release-notes/v1.0.2.md`，依此类推。工作流把同一份内容写入 `manifest.json.notes` 和 GitHub Release 正文，Gitee 镜像保留该字段。缺失或空白介绍会使发版准备步骤明确失败。

GitHub Release 已发布后修改正文，具备本修复的客户端后续检查也会尝试读取对应 tag 的新正文。GitHub API 不可达或限流时仍使用发版时写入清单的介绍。未实际进行线上发布验证。

已分发的旧客户端需要先通过原有“检查更新”流程或手动安装升级到包含本修复的版本，才能获得自动提醒。

## 输入、运行与现场

- SourceRoot: `D:/松光启动器`；开始 HEAD: `39e03f2b673ed342647821ae37d5e6291c028495`；开始 status 干净；输入哈希在本地提交前记录。
- 浏览器 ExecutionRoot: `dev/PineglowLauncher/bin/Release/net10.0/UI`，使用本轮 Release 构建实际输出；测试通过本机 Python 静态服务器加载。
- 后端 ExecutionRoot: `dev/PineglowLauncher.Tests/bin/Debug/net10.0`，使用本轮测试构建。
- EvidenceRoot: 本目录。浏览器请求由 Playwright 拦截并返回显式标注的验证数据，不访问用户账户或执行真实更新。
- 隔离临时目录: 本目录 `test-temp/`，仅对测试子进程设置 TEMP/TMP，未修改系统环境或实际用户配置。
- 最终源码与产物哈希: `input-hashes.json`。

## 验证结果

| 检查 | 结果 | 原始证据 |
| --- | --- | --- |
| 旧主源遮蔽新版 RED | 正常执行 1 项测试并证明旧代码返回 1.0.0，预期为 9.0.0 | `red-stale-manifest.log` |
| 新增选版和 Release 正文聚焦测试 | 12/12 通过，0 skip | `green-backend.log` |
| 新增自动检查调度 | 9/9 通过，0 skip | `green-monitor.log` |
| 前端现有及新增全套 Node 测试 | 313/313 通过，0 skip | `frontend-regression.log` |
| 现有替换/备份/回滚/中文路径/实际脚本重启命令及 UI 打包检查 | 隔离临时目录后 36/36 通过，0 skip；脚本操作合成文件，不替换真实启动器 | `update-regression-isolated.log` |
| Release 构建 | 通过，0 警告、0 错误 | `build-release.log` |
| 版本介绍脚本 | 5 项通过，包含中文、换行、空白/缺失/非法版本拒绝 | `release-notes-tests.log` |
| 实际工作流脚本块本地干跑 | 2 项通过：正文与清单相同、镜像不丢说明和备用地址 | `workflow-dry-run.log` |
| GitHub Actions 结构校验 | actionlint 1.7.7 返回 0 | `actionlint.log`（无诊断输出） |
| 真实 Edge/Chromium + 构建输出 + 假 API | 9 组通过，含自动启动/定时/前台补查、去重、业务弹窗避让、长文本、转设置/下载/手动重启接线、中英、失败恢复；0 未捕获 JS 错误 | `browser-report.json`、`browser-final.log`、`update-notice-1280.png`、`update-notice-960.png` |
| 完整清单/HTTP API 聚焦回归 | 31 项中 21 通过，10 项在 HttpListener 初始化报“句柄无效”；不是 31/31 通过 | `manifest-regression-isolated.log` |

最初替换/UI 打包回归在默认用户临时目录权限不足，20/36 通过；原始记录保留为 `update-regression.log`。改用本轮可写的隔离临时目录后 36/36 通过。最初浏览器缺少 Playwright 自带 Chromium，改用已安装 Edge；辅助服务器方式发生空响应，改用独立自有 Python 服务器后完成，原失败日志保留。

## 用户需求与具体证据

| 用户需求 | 证据 |
| --- | --- |
| “尽快修复一下”（承接自动发现更新与提醒） | `StartupAndPeriodicChecks_NotifyOncePerVersionWithReleaseNotes`、`HiddenWindow_SkipsChecksAndChecksOnForegroundWithThrottle`、`BusinessDialog_DefersNotificationWithoutReplacingIt`；浏览器启动/周期/前台/业务弹窗检查全部通过 |
| “尽快修复一下”（承接旧 Gitee 主源遮蔽新版） | `StalePrimary_NewerMirrorWinsWithItsReleaseNotes` RED→GREEN；`ManifestSelection_SourcesRunConcurrentlyAndTimeoutIndependently` |
| “推送更新的时候还要有新版本介绍” | `ReleaseNotes_OnlyMatchingTagBodyOverridesManifest`、`ReleaseNotes_OfflineKeepsPackagedNotesAndOtherRepositoriesAreIgnored`；`Workflow_GeneratedManifestAndReleaseBodyUseSameIntroduction`、`Workflow_MirrorPreservesIntroductionAndGitHubFallback`；浏览器实际介绍内容及转设置检查 |

## 限制与停止点

- HttpListener 环境错误仍使依赖该夹具的 10 项测试无法进入业务断言。没有为修复本功能而改变系统设置或修复该既有环境问题。
- 未验证原生 WebView2 中本功能、真实 GitHub/Gitee 发版、真实新版 EXE 下载替换重启、所有系统 DPI 或干净检出 CI。
- 浏览器“下载/重启”步骤验证的是前端与假 API 的用户操作契约；36 项现有回归中替换脚本实际运行针对合成文件，均不能代替真正发布包升级。
- 本地修复完成，停止在交付；不擅自进入提交、推送或线上发版。实施计划和可复制提示词见 `plan.md` 与 `execution-prompt.md`。
