# 松光启动器当前版本发布就绪评估

日期：2026-10-05，Asia/Hong_Kong。评估者：Codex。

**结论：当前版本暂不能作为正式新版本发布。** 当前源码自动化回归通过，但实际交付模型存在已复现的启动失败，候选源码漏掉最新界面资源，默认更新入口不可用，CI 未同步当前测试集，最终客户端/升级链路尚缺本轮证据。

TechnicalAcceptance=NOT_PASSED；FormalPhaseApproval=NOT_GRANTED；ReleaseApproval=NOT_GRANTED。

本轮仅检查、复验和文档交付，没有修改产品代码、真实配置、提交、推送、打 tag、恢复 Harness 或发布。

## 1. 基线与现场

| 角色 | 实际对象 |
| --- | --- |
| SourceRoot | D:\松光启动器；main；HEAD f145d14eb26b0b7459a28491e764ac8c573085d3 加当前脏源码 |
| 主目录状态 | 43 M + 1382 未跟踪，共 1425 条；未跟踪内容包括代码、测试、资源及历史证据，并非都要发布 |
| 聊天工作树 | C:\Users\Administrator\.codex\worktrees\e423\松光启动器；同 HEAD 的干净旧快照 |
| DeliveryRoot | e423 工作树 docs，保存本轮报告、流程、提示词与原始证据 |
| ExecutionRoot | C:\Users\Administrator\AppData\Local\Temp\pineglow-snapshot-66830ccdb8284f699212aa1a04f44ced |
| 冻结输入 | 161 tracked 当前磁盘文件 + 131 精确白名单文件，共 292；初始副本无 bin/obj |

旧工作树的干净状态不代表 D 盘当前实现已经提交。本轮以持续开发的 D 盘主目录为实际评估对象。

状态前后均 1425 条、差异 0；131 项白名单初始 SHA 与 D 盘终态全部一致；161 项 tracked 与初始副本内容一致。发布命令改写锁文件只发生在 TEMP。

本轮公开查询：[GitHub latest release](https://github.com/zhangtanze1359/Pineglow-Launcher/releases) 为 v1.0.0；返回的最新五次 Actions 均是 2026-08-10 的历史提交，不对应当前脏源码。详细响应见 public-release-endpoints.json。

本轮 /p:Version=1.0.1 只用于验证版本注入，不代表已选定正式版本号。

## 2. 本轮验证

| ID | 要求与方法 | 实际 | 状态 |
| --- | --- | --- | --- |
| G01 | 当前源码冷副本 | 292 项可复制；漏最新视觉资源；无 bin/obj | LIMITED |
| G02 | 非 RID 锁定还原 | tests.csproj --locked-mode 同时还原项目引用；exit 0 | PASS |
| G03 | C# 全量 | 870 executed/passed，0 failed/notExecuted；exit 0 | PASS |
| G04 | Node 全量 | 21 个 *.test.cjs，269 个条目通过，0 fail/cancelled/skipped；exit 0 | PASS |
| G05 | Actions 结构 | actionlint 1.7.7，官方校验和核实；两工作流 exit 0 | PASS |
| G06 | Release 参数打包 | publish exit 0；assembly=1.0.1.0；EXE 45,828,872 字节 | PASS（仅构建） |
| G07 | 实际下载范围冷启动 | 仅 EXE，新 GUID 目录，portable + --no-api；Skia 原生库缺失，exit -532462766 | FAIL / product |
| G08 | 当前视觉资源交付 | D 盘有 pineglow.css/forest SVG；候选源码及 dist 缺失 | FAIL / delivery |
| G09 | 默认更新入口 | Gitee update.json 本轮 HTTP 404；无备用清单入口 | FAIL / external endpoint，有产品影响 |
| G10 | CI/Release 覆盖 | Build 11 个 Node 文件，候选有 21；Release 无测试前置互锁 | FAIL / release process |
| G11 | 快照验证器可信 | 中文 EvidenceDir 经 ASCII .cmd 写坏；步骤未执行仍记 PASS | FAIL / verification tooling |
| G12 | 最终完整包 GUI/游戏 | 没有当前正式附件正常启动、账号/真实游戏闭环 | NOT_RUN |
| G13 | 旧版升级与恢复 | updater 只替换 EXE；最终完整资产一致升级未验证 | LIMITED / NOT_RUN |
| G14 | 干净候选提交/远程 CI | 本轮未提交/触发；旧 CI 不对应当前输入 | NOT_RUN |

C# TRX 起止：16:05:41–16:06:57 +08:00。G07 异常发生于 Avalonia/Skia 初始化，早于窗口/API；--no-api 用于禁止访问真实配置/API。只证明单独 EXE 冷启动失败，没有验收完整目录包 GUI。

测试数字不相加为用户流程数。单测/替身/条件返回不等同真实外部服务、游戏或跨平台证明。

## 3. 必须关闭的问题

### R01 / P0：单独 EXE 的交付范围不完整

位置：release.yml:58 以 PublishSingleFile=true 发布；192–194 仅上传 EXE + manifest。主 csproj:26 将 UI 作为外置 Content；LocalApiService.cs:213 读取 EXE 旁或配置目录的 UI。

实际 dist 顶层还有 UI/、av_libglesv2.dll、libHarfBuzzSharp.dll、libSkiaSharp.dll 及两个原生 PDB。只交付 EXE 漏必要依赖。本轮全新 GUID 目录启动实测 DllNotFoundException，崩溃于 SkiaSharp.SKImageInfo 初始化。

必须明确：真正独立运行的单文件/自解包，或完整目录 ZIP/安装包。同步调整资源定位、原生依赖、release 附件、manifest、下载/应用/回滚。仅把附件改 ZIP 而保持 EXE 替换逻辑不够。

验收：正式附件在无开发目录/历史 UI/DLL 的中文及空格新路径冷启动，真实窗口、live API、资源和版本一致。

### R02 / P0：最新视觉资源漏进候选源码

ui/index.html:8 引用 css/pineglow.css；该 CSS:68 引用 ../assets/pine-forest.svg。两个文件未跟踪且不在 source-snapshot-inputs.txt；副本/dist 缺失，但 Node 全量和构建仍通过。

必须精确纳入输入集合，检查本地 HTML/CSS/JS 静态引用闭包，增加缺资源失败门槛。准备候选提交时精确纳入 Git，不能 stage-all。

验收：独立副本及正式包都有两文件，SHA 对应原件，资源 HTTP/MIME 与真实视觉正确。

### R03 / P1：默认更新清单 404

ConfigStore.cs:17 默认仅 Gitee raw/master/update.json。LocalApiService.cs:4545 起只获取这一清单；UpdateService.cs:45 遇非成功状态抛出。EXE 的 fallbackUrl 发生在取得清单之后，不能解决清单本身 404。

镜像允许跳过/失败，而默认检查仍依赖 Gitee。404 只代表本轮请求，不推断所有客户端/时刻永远失败。

必须提供稳定主/备用清单、故障降级、保留用户自定义源及旧客户端迁移。镜像失败不得输出不存在地址。线上创建/更新文件另需发布授权。

验收正常、404、超时、坏 JSON、缺镜像 token/上传失败；清单 hash/size/version/RID 与 R01 最终完整资产一致。

### R04 / P1：CI 不覆盖当前测试，tag 发布无测试互锁

build.yml:88–98 固定 11 文件，漏 10 个：

- a11y-keyboard.test.cjs
- a13-doubles-and-taborder.test.cjs
- a6-bisect-safety-guidance.test.cjs
- bisect-recovery.test.cjs
- diagnostic-history.test.cjs
- file-recovery.test.cjs
- goal-core-flow.test.cjs
- import-cancel.test.cjs
- instance-removal.test.cjs
- update-recovery.test.cjs

Release 从 v* tag 直接触发，没有运行测试或依赖同 SHA 的测试成功。actionlint 通过仅证明工作流结构。

必须统一正式测试清单，覆盖相关质量工具测试，并让正式 Release 依赖同候选 SHA 的非零测试、资源/包和版本校验。测试失败/零测试/漏必需文件不能发布。

### R05 / P1：快照验证步骤 PASS 失真

verify-source-snapshot.ps1:374 用 ASCII 写包含中文 EvidenceDir 的 .cmd。中文路径损坏，cmd 报 filename/directory/volume syntax 错误；步骤日志未产生，多项仍记 exit 0 / PASS，整体仅因 EXE 缺失退出 1。

这不证明产品测试失败，而是验证工具故障。本轮保留原始失败，改用直接进程调用取得 G02–G06。

必须使用 Unicode 安全参数/输出，准确传播退出码，日志缺失/零测试/产物缺失均失败；英文/中文/空格目录及故障用例覆盖。

### R06 / P1：最终客户端与升级证据

BuildApplyScript（UpdateService.cs:276）备份、copy、fc 的单位仅 EXE。外置 UI/原生库发行方式下不能保证服务/界面/依赖同版。单文件方案也需要证明资源解析、旧版迁移与真实重启。

最终附件需真实复验：新用户启动、账号、Java/原版与承诺加载器、整合包、下载重启恢复、Mod/存档恢复、新闻/AI/皮肤、旧版升级和故障回滚。按本次 release notes 承诺选择范围；外部账号/服务缺条件写 NOT_RUN。

## 4. 历史问题与未验证边界

旧 Harness 报告中的恢复路径、跨实例恢复、回滚文案、运行中 Mod 只读列表、凭据测试隔离，当前已有后续实现和常驻回归。full.trx 对 FinalAcceptanceRegressionTests、RemediationBoundaryRegressionTests、ConfigPreservationTests、SecretStoreBackendTests 的条目为 Passed；见 critical-test-results.json。

不把历史失败照抄为当前 FAIL，也不据此宣布正式阶段批准。既有 UI 优化报告仅覆盖该次视觉范围，不能代替最终包验收。

未运行原版 VerifyHost --bisect-smoke：它仍直接启动默认 ConfigStore；portable JSON 隔离不能证明 Windows 命名凭据隔离。应补后端隔离或在一次性用户/runner 复验。没有据此断言真实凭据已被改变。

本轮未验证完整发行包 GUI、真实微软/Authlib/CurseForge 账号、真实 Minecraft/加载器、真实更新进程交接、满盘/跨卷、Linux/macOS 原生运行、当前候选远程 CI。

## 5. 收口顺序与发布范围

建议：R02 + R05 → R01 完整包及升级架构 → R03 清单 → R04 CI → R06 最终真实验收 → 候选版本冻结与独立放行。

首发建议 Windows win-x64；若宣称正式跨平台，需要分别补目标平台窗口/WebView/安装启动/升级证据。

Cleanroom/Legacy Fabric、更多导出互通、深度依赖定位、插件、诊断效果评测和市场验证按路线图单独推进。只有纳入本次发布承诺才是该版本门槛，无须等 MR00–MR27 全部完成。

正式版本须大于公开 v1.0.0，统一 tag/程序集/运行时/manifest/资产/release notes；版本定稿、提交/推送/tag/发布留待对应授权。

## 6. 原始证据及命令

同级 release-readiness-evidence-2026-10-05 保存：

- direct-restore.log/.exitcode，direct-dotnet-test.log/.exitcode，test-results/full.trx。
- direct-node.log/.exitcode，actionlint.log/.exitcode、actionlint-tool.json。
- direct-publish.log/.exitcode，publish-files.json、publish-summary.json。
- exe-only-summary.json、exe-only.stderr.log、exe-only-crash.log。
- resource-coverage.json、ci-coverage.json、public-release-endpoints.json。
- snapshot-run.log、snapshot-exitcode.txt、candidate-snapshot/verify-summary.json。
- source-status-before/after.txt、source-protection.json、hash-protection-summary.json及逐项哈希清单。
- critical-test-results.json、audit-summary.json、证据 manifest 及 detached digest。

实际命令在 ExecutionRoot：

~~~powershell
dotnet restore dev/PineglowLauncher.Tests/Pineglow.Launcher.Tests.csproj --locked-mode
dotnet test dev/PineglowLauncher.Tests/Pineglow.Launcher.Tests.csproj -c Release --no-restore --logger 'trx;LogFileName=full.trx' --results-directory <证据目录>/test-results
node --test --test-reporter=spec <ui/tests 下 21 个 *.test.cjs 的精确路径>
dotnet publish dev/PineglowLauncher/Pineglow.Launcher.csproj -c Release -r win-x64 --self-contained /p:PublishSingleFile=true /p:EnableCompressionInSingleFile=true /p:DebugType=None /p:DebugSymbols=false /p:PublishDir=../../dist /p:Version=1.0.1
actionlint -shellcheck= -pyflakes= <D盘 build.yml> <D盘 release.yml>
~~~

RID publish 是普通还原，不能称全链锁定。原工具的步骤 PASS 不能替代直接结果。

## 7. 文档输出自检

按 agent-self-evaluation 自检，非产品放行依据：

| 维度 | 分数 | 证据/改进 |
| --- | --- | --- |
| 准确性 | 4/5 | TRX/包清单/崩溃可追溯；公网 404 仅本轮 |
| 完整性 | 4/5 | 覆盖源码测试包更新CI；完整 GUI/游戏未验证 |
| 清晰度 | 4/5 | 分开产品、工具、入口与缺证据；路径较长 |
| 可执行性 | 4/5 | 另有流程/提示词；修复须后续执行 |
| 简洁性 | 4/5 | 主要决策留在聊天，细节在报告；表格较长 |

平均 4.0/5。优先补修复后正式附件冷启动、旧版升级、真实账号/游戏及提交/CI绑定。用户可据本轮硬阻断决定暂缓发布；评分不替代验收。
