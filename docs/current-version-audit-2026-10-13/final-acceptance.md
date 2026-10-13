# 当前版本缺陷修复最终验收（2026-10-13）

技术结论：**TechnicalAcceptance=PASS_SCOPED_CV01_CV08**。独立子代理已对本轮八项缺陷修复给出通过结论，母任务核对真实源码、原始报告、产物与WebView证据后接受该结论。按用户授权完成精确暂存审查并本地提交，提交号以仓库git log -1为准。默认在线九页巡检仍为原始FAIL，不提供在线全流程或正式发布放行。

## 输入、职责与授权

SourceRoot：D:\松光启动器，基线 main / 1829f1c106bb32c7c894eca8f288f1d24e79447b。
ExecutionRoot：C:\Users\Administrator\AppData\Local\Temp\pineglow-snapshot-2ec099278e72404596b71170e800de8b。
EvidenceRoot：D:\松光启动器\docs\current-version-audit-2026-10-13。

UI 设计由 OpenDesign Cloud 完成并导出完整四文件。Harness 完成第一批及部分 UI 修复；剩余短任务多次 TIMEOUT 后，用户明确授权 Codex 定向返修并由新独立子代理验收。本轮用户已授权验收通过后直接本地提交全部已审查改动，没有推送/发布/部署授权。

## 本轮八项缺陷

| ID | 修复行为 | 核实证据 |
|---|---|---|
| CV-01 | 快照按 tracked 与白名单唯一集合计数，缺文件仍失败 | 305唯一输入、重叠163、缺失/泄漏0；独立生产收集块只读四边界PASS |
| CV-02 | 冻结诊断JSON精确约束LF检出，不改数据/基准哈希 | autocrlf=true隔离检出仍匹配原冻结SHA；C#冻结测试通过 |
| CV-03 | 缺字段夹具按JSON结构删除，不依赖源码换行 | CRLF对应源码与最终源归一后相同；缺字段解析聚焦1/1通过；C#全量941通过 |
| CV-04 | 下载页取得真实API失败；已安装版本与下载任务保留，失败与成功空态分开 | 实际api.js受控fetch、独立7探针、原生WebView错误/重试/恢复/连点/成功空态 |
| CV-05 | 默认/无效新闻验证时间显示尚未验证，保留有效日期 | 聚焦边界与真实WebView MinValue/有效日期复验 |
| CV-06 | 错误说明、可用重试、原生技术详情、长文本换行与焦点样式 | Cloud原型7项；真实WebView960窗口、键盘详情、HTML转义、查询保留及截图 |
| CV-07 | CDP错误正确拒绝；完整中文JSON错误分类；敏感值递归脱敏 | CDP受控协议测试、Unicode/200字后/note反例、token/Cookie/短Auth回归、默认九页零未知本机错误 |
| CV-08 | 启动检查异常/null/明确问题均不启动，真实重试，警告确认及防连点 | 完整launcher+实际API独立反例；真实WebView失败零启动，成功/确认才到达被夹具拦截的启动接口 |

## 实际执行结果

| 层次 | 实际结果 | 证据 |
|---|---|---|
| 新增聚焦回归 | 42/42，exit0，0skip | codex-focused-final-green.log、independent-agent-focused-r2.log |
| 独立追加实际调用链反例 | 7/7 PASS | independent-agent-probes.json |
| 全量Node | 347/347，0fail/skip/cancelled | final-independent-snapshot-cache/step1-node-tests.log |
| 全量C# | 941/941，0fail/skip，exit0 | final-independent-snapshot-cache/step7-dotnet-test-release.log |
| 唯一源码输入 | 305，SourceRoot哈希漂移0，缺失/泄漏0 | snapshot-manifest.sha256.txt、independent-agent-final-gate-summary.json |
| 非RID锁定还原/Release/VerifyHost/bisect/单文件publish | PASS，使用本轮子进程完整本地NuGet源 | final-independent-snapshot-cache/verify-summary.json |
| 缓存完整性 | 42/42规范化contentHash=lock，归档hash=sha512，签名完整性通过 | independent-agent-package-cache-normalized-integrity.json |
| 单文件中文路径独立冷启动 | 19/19，exit0 | final-independent-snapshot-cache/standalone-cold-start.json |
| 真实原生WebView受控HTTP状态流程 | 13/13，exit0，未捕获JS异常0 | final-webview-states/report.json及截图 |
| 默认联网真实九页巡检 | **原始FAIL / exit1；6/9数据成功，3页外部错误终态** | final-nine-page-ui.json、final-nine-page-ui.log |

默认九页巡检不存在待定loading，未捕获JS异常、console.error、UI资源失败、未知本机接口失败均0。内容源返回中文DNS错误、版本下载源返回转义中文连接失败，新闻处于网络错误状态。缺省无账号皮肤404和favicon404分别记录。此项证明错误提示实际落地，不能证明三项外部服务成功或全部在线业务通过。

真实WebView使用实际前端API适配器，但其中受控HTTP复验有意拦截启动接口，未启动真实游戏。13项包含失败/重试/恢复、有效空列表、详情键盘和文本转义；测试版本号、任务为夹具，不宣称实际安装、恢复或账号成功。

## 产物与一致性

最终publish/Pineglow.Launcher.exe为单文件自包含产物，53,060,914字节。
SHA256：aa1f65471e9f9c2bbeb20c5a86d593bffaac2b4f823620fbb0ef0f2248259a1d。
独立比对真实WebView两次运行复制的EXE与该产物，哈希均相同。build目录apphost没有被冒充为单文件产物。

普通RID发布会把隔离快照内的packages.lock.json更新为RID形状；这是已记录的构建后快照变化。D盘原始非RID锁文件哈希不变，源码输入漂移0，不能把执行目录称为构建后完全未变。

## 保留的边界与权限

- 默认在线NuGet源还原长时间等待；已保留LIMITED记录并终止本轮自有任务。完整本地源锁定构建通过，无全局配置/锁/安全检查改动。联网撤销/证书信任链与最新漏洞数据未复验。
- win-x64 RID锁定publish仍是已有NU1004 / KNOWN-UNLOCKED，普通单文件publish通过。本轮未更改依赖与发布锁架构，不宣称全链锁定。
- 真实外部下载/安装、微软或第三方登录、AI服务、进入游戏世界、长时游玩、远程同SHA CI与正式发布均未通过本轮验证。FormalPhaseApproval=NOT_GRANTED。
- 此前隔离缺文件探针中的删除命令被工具返回Rejected / blocked by policy，未执行、未换工具或执行者绕过。该方法LIMITED；正常全量快照与只读生产收集块四边界均已验证。
- 仅按commit-scope.json精确路径审查、暂存与提交；2611项原有历史未跟踪材料和原始日志保留。未经脱敏的旧GUI日志、Harness思考日志、工具二进制及归档包不纳入提交。
