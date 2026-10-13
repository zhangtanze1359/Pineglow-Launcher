# Harness 独立复查返修：真实 API 错误链、中文正文与启动检查失败

SourceRoot/cwd D:\松光启动器。用户已授权完整修复当前版本，角色和提交边界不变。先前UI任务若仍运行不要并发写；本任务仅在其结束后执行。

Codex发现三项独立反例，不能以stub掉SG.api的测试替代生产链：

1. CV-04未闭环：真实ui/js/api.js的getRemoteVersions在remote失败时catch并返回local数组；local=[]时实际仍返回[]。下载页当前调用没有取得失败信息，因而不会显示你新增的错误卡。反例cv04-api-layer-red.json，使用实际api.js+受控fetch，exit1。
2. CV-07未闭环：evaluateFailureBody不识别Windows“ 不知道这样的主机 ”及.NET JSON转义后的中文下载源错误。cv07-localized-body-red.json三项2 FAIL/1 PASS。未知内部错误必须继续阻断。
3. 新增CV-08：doLaunch把prelaunchCheck抛错catch成null后继续doLaunchInternal。实际生产函数探针cv08-prelaunch-red.json：检查失败却调用launchInstance 1次，期望0。用户已明确选择启动/失败提示流程，这项必须修复。

Codex对第一轮实际文件运行node --test：15项中13 PASS/2 FAIL，0 skip；日志ui-round1-focused.log。ui/tests/current-version-errors.test.cjs:577有缺结束斜线的正则，导致整文件无法加载。scripts/tests/ui-live-cdp.test.cjs:231的报告脱敏用例失败，JSON普通token字段仍泄漏。所有下述修复必须先让新增聚焦测试可解析且通过；不要宣称原来已GREEN。

## 架构与精确范围

允许ui/js/api.js仅getRemoteVersions方法、ui/js/launcher.js相关调用/状态与doLaunch、ui/js/i18n.js相关双语文案、scripts/verify-ui-pages-live.cjs正文判定、现有聚焦测试及测试清单/输入清单必要精确条目；报告写docs/current-version-audit-2026-10-13/harness-boundary-rework-report.md。不改C#、CI、依赖、冻结数据、原始RED或其他功能，不提交/发布/删除用户资产。

CV-04采用兼容的可选参数：getRemoteVersions(options)，options.throwOnRemoteError默认false，旧无参数调用仍保持本地版本回退；下载页显式传{throwOnRemoteError:true}。remote失败时该模式抛出Error，Error.localVersions携带已映射的本地版本数组（可能空）。下载页catch保留error状态与localVersions，并在错误卡下显示仍可使用的已安装版本；不伪装成功空态。remote成功时返回数组契约保持。local列表接口自身失败必须继续抛错。

测试必须装载实际api.js，以fetch返回local成功/remote500，验证旧调用回退、严格调用抛错并保留local数据；再验证实际下载页调用传入严格参数并渲染失败卡。不要只替换SG.api.getRemoteVersions测试，也不要在生产中回退mock。

CV-07用完整正文先JSON.parse读取error/message文本（含error.message），再判断；.NET的\\u中文转义由JSON解析恢复。增加明确中文DNS/下载源连接失败证据，保留英语证据与未知失败阻断。JSON成功解析时不要用任意非错误字段里的网络关键词放行未知500。最后才截断显示并脱敏。测试Unicode转义中文版、Windows中文DNS、200字以后证据、未知内部错误、无关note字段含DNS关键词仍不放行。

同时修复CV-07脱敏：普通token字段、对象的token/Cookie/Authorization等敏感键、JSON正文里的token字段、完整Cookie头都不能泄漏；sessionTokenPresent等布尔统计不能误删。保留失败URL敏感值不重发规则。使用现有失败测试与新增结构化字段/多Cookie边界测试真实验证，不能只改断言。

CV-08检查异常/null必须显示“启动前检查未完成，尚未启动”的失败提示及关闭/真实重试入口，绝不调用launchInstance，不能出现“继续启动”绕过按钮。重试成功才按已有errors/warnings/ok分支继续；原有警告需用户确认的行为保持。防连点产生重复检查/启动。仅 UI 行为，不改服务端。新增实际doLaunch入口测试：抛错、null、重试仍失败都launch0；重试成功按既有规则launch1；明确检查errors仍阻断；warnings仍需用户确认。

工具约束：此前pwsh0xC0000142，别循环尝试坏shell；read/edit/write实现，未运行测试标NOT_RUN，Codex执行。已有PS1和清单BOM按字节保留；不要为BOM花几十步探针。每项报告Requirement | TestName，说明真实API与stub层区别。终态：具备独立复验条件，等待 Codex 独立验收。
