# DeepSeek Harness 独立验收

日期：2026-10-16，Asia/Hong_Kong。SourceRoot：`D:/松光启动器`；受验 HEAD：`3ab6b0c4fa7bff1bf115391df9414af9436b9adb`。此次只做独立验收、隔离工具校正和证据交付；未修改产品/正式验证脚本，未暂存、提交、推送、打标签或发布。

## 结论

`TechnicalAcceptance=PASS_SCOPED_RP02_RP03_COLDSTART_API_REGRESSION`。

RP-02 的最小修复和 RP-03 的版本介绍通过独立复验。上轮 RP-01 应改记为本机路径相关的运行环境差异：同 SHA 的旧 EXE 和当前 EXE 在中性目录都可正常启动，当前 EXE 在工作区内仍失败。现有证据不能证明具体是 CodexSandboxService/bindflt 导致，也不能保证任意用户机器/安装路径都不受影响。没有修改或停用系统服务、安全设置、绑定过滤驱动或运行时。

`ReleaseEvidence=NEEDS_VERIFIER_AND_REPORT_CORRECTION`；正式发布未授权，`FormalPhaseApproval=NOT_GRANTED`。功能层已通过本文明确列出的检查，但仓库正式九页验证器仍 exit 1，执行报告对其总判定和失败路径证据的陈述不准确，尚不能将现有发布证据包记为全部通过。

## 实际输入和变更

开始时 tracked/untracked 均干净；当前本地提交依次为 `d138c1a`（此前 FA 修复及材料）、`65fe285`（RP-02/03）、`3ab6b0c`（证据 manifest URL 仓库名大小写）。这些提交在本轮开始前已经存在，本轮未替执行者创建或撤销提交。

相对上一轮 `1356efa`，后端实现、项目依赖和正式发布工作流没有变更。最新实质修复为：将 `$served` 初始化移到健康检查条件分支外，保留失败断言；正式 `release-notes/v1.0.1.md` 补全 FA-01 至 FA-05。

将当前全部 **3,779 个 tracked 文件**复制到本轮新中性目录，逐文件比对 SHA256，0 差异；不复用 Harness 的 `D:/pg-testrepo`。ExecutionRoot、NeutralRoot 的绝对路径见 `context.json`，精确文件清单和哈希见 `input-manifest.json`。测试和发布均从该副本运行。源仓库和副本输入在收尾再次检查，见 `final-verification.json`；此副本不是 Git checkout 或远程 CI。

## 独立复验结果

| 要求 | 实际结果 | 原始证据 |
| --- | --- | --- |
| 非 RID 依赖锁定还原 | 启动器、测试项目均 exit 0 | `launcher-locked-restore.log`、`tests-locked-restore.log` |
| 全部清单 Node | **379/379，0 fail、0 skip，exit 0** | `node-tests.log`、`node-check-exits.json` |
| 全量 C# | **957/957，0 fail、0 skip，exit 0**；TRX counters 核对，未筛选 | `csharp-tests.log`、`test-results/full-suite.trx`、`csharp-check-exits.json` |
| 清单与资源闭包 | Node 28 文件+C# 1 项目一致；资源 0 缺失 | `inventory.json`、`closure.json` |
| 当前 Harness EXE，中性目录 | **18/18，PASS**；UI 与受验源码逐字节一致 | `current-neutral.json`、同名 log |
| 同一个当前 EXE，工作区内 | API 不就绪，完整 HttpListenerException(6)；**FAIL JSON 正常落盘，exit 1** | `current-workspace.json`、同名 log、`current-workspace-exception.txt` |
| 上轮失败的旧 EXE，相同 SHA，中性目录 | **18/18，PASS** | `old-exact-retained-neutral.json`、同名 log |
| 本轮由冻结副本重新发布的 EXE | 发布 exit 0，内嵌 17 项 UI；独立冷启动 **18/18 PASS** | `publish.log`、`fresh-publish-cold.json` |
| 仓库正式九页原生 WebView 验证器 | 9/9 视图终态，但最终 **FAIL、exit 1**，隐藏图片被纳入必加载统计 | `native-ui.json`、`native-ui.log`、`native-ui-exit.json` |
| 隔离校正后，补测游玩时长的十页原生 WebView | **10/10，PASS、exit 0**；0 JS 异常、0 console.error、0 UI资源/未知本机接口失败 | `native-ui-calibrated.json`、同名 log/exit 文件、`ui-shots-calibrated/` |
| 新 EXE 真实原生交互 | live API、AI 保存、刷新后持久化、恢复原值、下载源三选项及当前选择，共 **5/5** | `native-interaction-v2.json`、同名 log、`native-source-picker.png` |
| 默认真实外网更新源链 | **阶段 A PASS**，主源失败后 GitHub 备用源取到 1.0.0 清单；未下载或应用升级 | `live-update-stageA.json`、同名 log/exit 文件 |
| 版本介绍及本地 manifest | 1.0.1，介绍与正式 Markdown 完全一致，size/SHA 来自本轮真实 EXE | `fresh-manifest.json`、`final-verification.json` |

C# 完整编译存在原有可空性/xUnit 分析器警告，不称为“零警告”。没有跳过用例或改写测试来获得通过。挂起采集配置为 90 秒，实际正常完成，未产生挂起中止或 dump。

候选关系：

- 旧 EXE：`f665c26db60941335def1f5b9f6f743aaf23d148b9ae54b6a53336e4a0bf2900`。
- Harness EXE：`72eae380db265a97c16938fd6086441e0409fd192f6b5cce1c01c65a33a32534`，实际仍在原报告的保留目录，哈希/size 与其 manifest 一致。
- 本轮新发布 EXE：`1424447f6017f8ec51195008ed859053f609196456352232622779f421dd5698`，53,089,586 bytes，程序集版本 1.0.1。来自本轮冻结副本；不将跨构建哈希差异称为源码差异。实际路径见 `fresh-publish-cold.json`。

RID publish 的锁文件另指向本轮中性目录，保护提交中的非 RID lock；没有声称 RID locked publish 通过。

## 尚需处理的发现

### HA-01 / P2：正式界面验证器误计隐藏图片，且遗漏第十页

`scripts/verify-ui-pages-live.cjs:528` 附近统计根节点内全部 img。独立原生观测：内容库 24 张图的布局宽高全为 0；`ui/css/redesign.css:81` 明确设置 `.mod-icon { display:none; }`，同一规则在旧基线已经存在。皮肤页唯一被计入的 img 是 `#localSkinTexture`，`hidden=true`、无 src、宽高 0，是尚未选择本地文件的预览节点。滚动内容库后仍全部不可见。完整 DOM 观测已对 token 查询参数脱敏，见 `native-interaction-v2.json` 和两张 diagnostic 截图。

这些节点不能用来证明可见图片加载失败；也不能把它们从统计排除后声称验证了内容库图标/真实账号皮肤。正式 VIEWS 只有九项，缺少实际存在的 `playtime` 页面。

本轮仅在证据目录复制验证器，筛除无 src、hidden、布局宽高为 0 或 visibility:hidden 的 img，并把 playtime 加入 VIEWS，得到十页 PASS；差异限定在这两处，不更改产品 DOM/CSS、不使用 `--allow-missing-images`，保留所有原错误/网络断言。正式脚本尚未修正，不能把隔离副本 PASS 写成正式脚本 PASS。

### HA-02 / P2：执行报告遗漏 FAIL 总判定，RP-02 归档证据与声称不符

`docs/release-prep-verify-2026-10-16/README.md` 将九页渲染列为门槛结果，但归档 `ui-live-report.json` 的 verdict 实际是 FAIL，problems 非空。本轮复现同样结果，不能仅摘取 okViews=9。

其 RP-02 提交说明声称 FAIL JSON 验证成功，实际归档 `rp02-failpath-report.json` 是健康成功的 PASS，servedAssets 非空。该文件不能证明健康失败分支被执行。本轮用工作区内真实失败独立补足了 FAIL 路径，因此 RP-02 实现可通过；报告仍需准确关联此类证据。

报告将服务/驱动存在和路径相关性直接写成根因、进一步断言“不是用户会遇到的发行缺陷”，超出现有证据。应改为本机路径相关环境差异，并保留影响范围和底层机制未确认的限定，不要求停用服务/驱动来补证。

### 附带核对

- 空格参数：用 Python subprocess 参数数组直接传入含“中文 冷启动”的 EXE 路径，正式 Node 验证器正常启动并跑完九页；未复现报告所称“未知参数: 冷启动”。这一现象更可能属于当时命令的引号/参数传递，不能据此认定 parseArgs 有缺陷。
- get-release-notes 的 PowerShell 7 路径已成功执行，中文和 JSON 往返一致。Windows PowerShell 5.1 的独立解析探针未获执行：系统要求脚本签名。未使用 Bypass、修改执行策略或改用别的方式继续被拒的脚本执行，因此不认领该兼容性问题已经独立复现。它不是当前 pwsh 发布流程的已证明阻断。
- 第一次交互诊断用了不存在的 `.content-card` 选择器，发生夹具超时；正确类名为 `.mod-card`。保留 v1 源码、FAIL 报告与日志；v2 对应真实选择器，5/5 通过。初次超时不记产品失败。
- 旧候选原 publish 路径在本轮已不存在，最初尝试因此报输入缺失。随后从上轮保留冷启动目录找到同 SHA EXE，完成有效复验。原失败日志保留，不计产品失败。

## 本轮边界与后续

未验证真实账号登录/皮肤、Minecraft 启动、真实资产下载/篡改拒绝、更新应用/回滚、同 SHA 远程 CI、签名及 Release 上传。皮肤页在默认离线账户下显示“无法获取皮肤贴图，请选择本地 PNG 预览”，此次仅验收其落地显示和隐藏占位统计，未将其记为默认 3D 皮肤正常或真实账户皮肤通过。

后续范围应收窄到正式验证器与报告纠正，见 `../release-acceptance-followup-workflow-2026-10-16.md` 与 `../release-acceptance-followup-prompt-2026-10-16.md`。无需依据上轮 RP-01 再盲改后端或降级运行时。源实现通过本文限定范围后，发布批准仍与技术检查分开。
