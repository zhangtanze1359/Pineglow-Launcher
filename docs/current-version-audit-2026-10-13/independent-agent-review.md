# Codex 定向修复独立子代理验收

时间：2026-10-13 16:53 +08:00，Asia/Hong_Kong。验收者：独立子代理，未参与产品修复。

`TechnicalAcceptance=PASS_SCOPED_CV01_CV08`：仅当前定义的 CV-01–08 修复、冻结源码输入、既有本地缓存来源构建、受控 Native WebView 行为技术通过。

`DefaultOnlineRestore=LIMITED`；`DefaultOnlineNinePageVerdict=FAIL`，6/9 成功、3 页外部依赖错误终态。两项原始结果保留，不能表述为默认网络全通、九页 9 PASS 或发布准备通过。

`FormalPhaseApproval=NOT_GRANTED`；`ReleaseApproval=NOT_GRANTED`。独立子代理不暂存、不提交、不推送、不发布。用户对母任务的“全部必需修复门槛通过后提交已审查改动”授权由母任务按精确路径落实；本报告不扩张提交范围。

## 输入与证明边界

- SourceRoot：`D:\松光启动器`，HEAD `1829f1c106bb32c7c894eca8f288f1d24e79447b`，main，已有 9 个 tracked 修改和大量历史 untracked 资产。
- 最终 ExecutionRoot：`C:\Users\Administrator\AppData\Local\Temp\pineglow-snapshot-2ec099278e72404596b71170e800de8b`。
- EvidenceRoot：`D:\松光启动器\docs\current-version-audit-2026-10-13`；完整门槛目录 `final-independent-snapshot-cache`。
- 输入为当前工作区磁盘字节，不是 clean HEAD 或远程 CI。302 tracked ∪ 166 白名单，重叠 163，唯一输入 305；缺失、泄漏、构建前 bin/obj 均 0。
- 独立逐项复核 305 项 SourceRoot 相对冻结基线漂移 0；最终构建后快照 304 项仍逐字节相同，唯一差异是快照内 launcher `packages.lock.json` 被普通 RID publish 改成 RID 形态。这是既有脚本明确记录的隔离构建输出变化；源仓库两份 lock 保持非 RID，源 launcher lock SHA256 `FBBC9AE67B8E8CF2E3AA63C48242BC08AC713BE47830D337914CC39C85EE0672` 不变。
- 4 个原始 RED 文件逐项 SHA256 校验漂移 0。没有删除、覆盖或改写原失败证据，没有 reset/restore/checkout/clean/stash/stage-all/commit。

## 必需修复门槛

| 要求 | 独立方法与实际观察 | 结论 |
| --- | --- | --- |
| CV-01 唯一集合计数且保留缺失/泄漏/哈希/退出码门槛 | 读真实收集 diff；运行完整快照得到唯一 305；PS7 执行原生产收集块只读模拟 4 case，重叠与 tracked/white/shared 缺失均按预期计数、记失败 | PASS；物理删除缺失输入探针 LIMITED |
| CV-02 冻结 JSON LF 检出和原冻结哈希 | `.gitattributes` 仅精确该 JSON，`git check-attr` text/eol=lf；真实有效 autocrlf 来自 Program Files/Git/etc/gitconfig=true；亲自读取保留的隔离 checkout-index 输出，冻结哈希仍 `D9F55189FA05845BAC0403A058AA63912A725AFF61555BDF25A6865DD5AB45AF`；未改 FrozenSha256 或案例内容 | PASS |
| CV-03 CRLF 下真实删除缺字段并调用解析器 | 真实 diff 采用 JSON 节点 Remove+缺字段断言；独立核对保留 CRLF 源码有 386 处 CRLF且规范换行后与最终源相同；独立重跑对应保留产物聚焦 1/1，final LF 全套 941/941 | PASS |
| CV-04 实际 API 严格模式、本地回退和错误呈现 | 真实 api.js + fetch：无参数保留 local fallback；严格模式抛错并携带 localVersions；本地列表失败不请求 remote；实际下载渲染保留 installed 列表和独立任务。聚焦包含失败/重试失败/成功恢复/成功空态/筛选/竞态/连点；真实 Native 控制 HTTP 覆盖任务保留、错误与空态、恢复与重试连点 | PASS |
| CV-05 未验证时间与有效缓存日期 | 读 parseValidatedAt/fmtDate 生产路径；null/空/无效/0001含偏移拒绝假日期，有效 live/cached 保留；受控 Native 显示尚未验证，随后真实 API 有效日期恢复 | PASS |
| CV-06 错误主说明、技术详情、重试与窄窗口 | 真实 render/bind与双语词典/CSS；HTML 转义、原生 details 默认折叠、Space操作、筛选/query保留；亲自看 content-error-details-960.png，技术文本换行、原始 `<b>` 为文字；Native scrollWidth=clientWidth=718，无横向溢出 | PASS |
| CV-07 CDP Promise、中文完整错误正文、fail closed 与脱敏 | 实跑请求 method/reject/close 等聚焦；独立 error.message Unicode转义且证据在第350字符后被识别；JSON unrelated/nested note 不放行内部错误；未知500系列阻断；嵌套token、多Cookie、敏感URL不重发、布尔统计保留；额外短 Basic/Bearer/Negotiate 反例曾失败，定向修复后独立3scheme+Proxy-Authorization+下一行头完整性通过 | PASS |
| CV-08 检查失败保护、真实重试、警告确认与防连点 | 子代理额外运行完整 launcher + 实际 api.js + 受控 fetch；500/失败重试/null均 launch0，警告在确认前launch0，确认后launch1；42聚焦包含 pending重复检查/确认去重、明确错误阻断。Native实际检查失败只有关闭/重试，成功重试只到被截断的 launch HTTP；警告确认/去重和明确issues也通过 | PASS |

CV-01 的物理文件删除动作此前明确受到工具 policy 拒绝，本子代理没有重试、换工具、换模型或委派删除；只读生产块 4 case 和真实完整快照提供此轮允许范围内证据。该限制不能被描述为已完成物理缺失输入的端到端验证。

## 独立运行与证据

1. `node --test --test-reporter=spec ui/tests/current-version-errors.test.cjs scripts/tests/ui-live-cdp.test.cjs`：第一次 42/42；短 Authorization 修复后再次独立运行 42/42，0 fail/skip/cancelled/todo，exit 0。`independent-agent-focused-r2.log`。
2. `node docs/current-version-audit-2026-10-13/independent-agent-probes.cjs`：7/7 PASS，exit 0；使用真实 api.js、完整 launcher、真实验证函数，DOM只复用原夹具声明。首轮 DOM缺 contains 的夹具失败保留在 `independent-agent-probes-fixture-failure.*`，修正仅本轮独立探针。`independent-agent-probes.json` 给关键生产文件哈希与范围。
3. 原始 `verify-input-collection.ps1` 在PS5.1缺BOM导致中文默认路径乱码；该环境失败已保存。本子代理未改原脚本，改为当前PS7读取原文件，4/4 PASS；`independent-agent-input-collection-ps7.json`。
4. 默认完整脚本 R1 在短 Authorization 修复后输入过期，按自有PID和命令校验结束；R2 默认网络 restore 长时无输出，包缓存完整且进程通过既有127.0.0.1:2080代理建立连接。停止本轮自有进程树，分别保存 `independent-agent-r1-interrupted.json`、`independent-agent-r2-online-limited.json`，保留快照/日志。未证明究竟是审计还是feed元数据等待。
5. exact项目+非RID lock独立复制，`dotnet restore --locked-mode --source C:\Users\Administrator\.nuget\packages`：launcher255ms/tests283ms，exit0。随后按母任务明确允许，在本轮子进程设置同一路径 `RestoreSources`，运行完全相同的生产 `scripts/verify-source-snapshot.ps1 -RepoRoot D:\松光启动器 -EvidenceDir ...\final-independent-snapshot-cache`，不加 Cleanup。没有改 NuGet/global 配置、源lock或安全属性；项目 assets 中实际 sources 仅既有本地包源。

完整脚本 exit 0、`verify-summary.json` PASS；执行14个阶段，其中13 PASS，RID locked探测1 KNOWN-UNLOCKED（exit1/NU1004），没有隐藏失败或跳过：

| 实际层级 | 结果 | 原始证据 |
| --- | --- | --- |
| 正式 Node 清单 | 25 test文件、实际347/347，0 fail/skip/cancelled/todo | final-independent-snapshot-cache/step1-node-tests.log |
| C# Release 全套 | 941/941，0失败/跳过 | step7-dotnet-test-release.log |
| 非 RID locked restore | launcher/tests均exit0 | step2/step3还原日志 |
| Release build、VerifyHost build、bisect HTTP/凭据后端隔离 | exit0 | step6/step8/step8b日志 |
| win-x64 locked publish | NU1004，已知未锁定边界，未声称通过 | step9-publish-locked-probe.log |
| 普通win-x64自包含单文件publish | exit0 | step10-publish.log |
| EXE独立冷启动 | 19/19；窗口标题、live API、资源MIME、解包UI字节等通过 | standalone-cold-start.json、step11日志 |

发布构建产物（仅本地验证产物，不是发布批准）：

`C:\Users\Administrator\AppData\Local\Temp\pineglow-snapshot-2ec099278e72404596b71170e800de8b\dev\PineglowLauncher\bin\Release\net10.0\win-x64\publish\Pineglow.Launcher.exe`，53,060,914 bytes，SHA256 `aa1f65471e9f9c2bbeb20c5a86d593bffaac2b4f823620fbb0ef0f2248259a1d`。

## 本地包源完整性

最初将 raw .nupkg SHA512直接与lock contentHash比较，42/42不同，原始结果保留 `independent-agent-package-cache-integrity.json`。这是签名包哈希口径差异，不能直接判缓存损坏。

子代理查 NuGet官方源码并用本机SDK的 `PackageArchiveReader.GetContentHash` 和 `ValidateIntegrityAsync` 实算：42/42规范内容哈希=原source lock，42/42 `.nupkg.metadata` contentHash=lock，42/42原始ZIP SHA512=.nupkg.sha512，42/42签名内容完整性及CMS加密签名检查通过。可重跑脚本 `independent-agent-check-package-cache.ps1`（PS7）；修正报告 `independent-agent-package-cache-normalized-integrity.json`，显式非零计数门槛42、失败0。官方实现：[NuGet PackageArchiveReader](https://github.com/NuGet/NuGet.Client/blob/dev/src/NuGet.Core/NuGet.Packaging/PackageArchiveReader.cs)，[官方 Nupkg Metadata 说明](https://github.com/NuGet/Home/wiki/Nupkg-Metadata-File)。

没有关闭NuGet签名校验；独立CMS检查验证加密签名而不复验线上证书撤销或信任链。缓存来源相同版本与原锁定内容得到证明，默认线上feed可达性仍未得到证明。

## 真实 Native WebView 与默认在线九页

母任务亲自运行真实EXE，独立子代理读完整 driver `verify-final-webview-states.cjs`、`final-webview-states/report.json`，查看content与prelaunch截图并逐项核验；13/13 PASS，errors=[]。这是母任务运行、子代理复核的证据，不能说由子代理亲自操作了13项。母任务对launch路由返回受控500以拦截真实启动，只证明前端调用与阻断规则，不证明进入游戏。

子代理对受控Native复制EXE及默认九页复制EXE均实际算SHA256，与本次最终publish完全一致；`independent-agent-native-exe-provenance.json`。

默认在线九页原始脚本 exit1/verdict FAIL，`final-nine-page-ui.json`、`final-nine-page-ui.log`、`final-nine-page-shots`保留：6/9成功（含worlds/servers两成功空态），content/downloads/news三页落入错误终态，externalPending=0，JS exceptions=0、consoleErrors=0、uiAssetFailures=0、productLocalFailures=0、problems=[]。正文证明两条 upstream网络失败；另外无账号skin404一条、cosmetic一条。没有把它改成9 PASS。

独立判定：当前CV-01–08关闭标准要求错误准确呈现、可重试、受控恢复、启动保护和验证器准确分类；这些已通过生产路径+Native受控复验。默认在线三页的外部源成功不属于本轮定义的8项关闭标准，因此不把真实网络不可达误判为修复失败。在线端到端运行状态仍为LIMITED/FAIL，不能用于宣称线上资源安装、外部服务正常或发行准备完成。

## 保护、未验证与停止点

产品9个tracked修改保持，新增产品输入仅已有 `.gitattributes` 和两聚焦测试；本子代理新增内容限于当前独立证据目录。没有暂存、提交或删用户文件。快照脚本既有冷启动结束后停止自有进程并清理其自有冷启动目录，不等于删除物理缺失输入探针；主快照按要求保留。默认在线等待的自有R1/R2已结束；母任务UI进程由母任务处理。

未验证：默认在线NuGet源完成、外部内容/新闻/版本源成功、微软及第三方账号登录、真实资源安装和启动游戏/进入世界/游玩、远程同SHA CI、正式发布。线上证书撤销/信任链也未独立复验。无需继续扩大本轮子代理验收范围；母任务可在本结论范围内进行最终文档和精确提交审查，不能写“线上全通”或“正式发布通过”。

## 输出自评

按 agent-self-evaluation 技能执行，评分用于报告质量检查，不是产品门槛。

| 维度 | 分数/5 | 证据与改进 |
| --- | --- | --- |
| 准确性 | 4 | 实跑42/7/347/941/19，hash305稳定；保留在线FAIL并解释签名hash口径。下一轮优先直接用NuGet规范hash，避免rawzip比较误设假设 |
| 完整性 | 4 | CV01–08逐项闭环，Native来源/online限制/RIDlock边界齐全；物理missing探针明确LIMITED，未声称补齐 |
| 清晰度 | 4 | 技术范围、默认在线结果、正式批准分开，真实路径和表格可核对 |
| 可执行性 | 4 | 完整EXE与summary路径、可重跑探针、精确停止点已交母任务；网络恢复需环境改变 |
| 简洁性 | 4 | 汇总门槛而不复制原日志，但签名调查与两类UI层级导致报告偏长，可给用户用短结论入口 |

平均4.0/5。改进优先级：先保留短结论入口；下次缓存诊断直接采用NuGet规范化哈希；仍遵守缺失输入删除受限范围。自检：用户应能认同本报告明确保留FAIL/LIMITED而不给出超出证据的上线结论。
