# 当前版本定向返修记录（2026-10-13）

SourceRoot：D:\松光启动器。用户明确授权将 Harness 超时后未完成的定向返修改由 Codex 实施，并交独立子代理验收。OpenDesign Cloud 成功产物沿用，不重新生成。全部必需验收通过后本地提交；推送、发布和部署未授权。

## 实际变更

- CV-04：实际 api.js 的 getRemoteVersions 增加可选 throwOnRemoteError。无参数保持原有本地版本回退；下载页使用严格模式，失败携带 localVersions，错误卡下面保留已安装版本，下载任务独立加载。
- CV-07：完整错误正文先解析 JSON，再从 error/message 提取证据，解码 .NET Unicode 中文，识别明确中文 DNS/下载源连接错误。非错误 note 字段里的 DNS 关键词不放行未知 500。报告对结构化敏感键、普通 token、完整 Cookie 头递归脱敏，保留 sessionTokenPresent 布尔统计。
- CV-08：检查异常/null/缺少布尔结果不启动，展示检查未完成、尚未启动及真实重试；明确失败继续阻断，警告仍需确认。检查和启动调用期间防连点，启动 Promise 纳入 guard 生命周期。
- 既有聚焦测试的历史语法失败保留原日志；本次恢复时该正则已经可解析。新增测试夹具缺少 DOM matches()，导致委托点击没有真正触发；补齐该方法，并对生产实际更新的 grid 节点断言，未降低预期。

## 要求与聚焦证据

命令：node --test ui/tests/current-version-errors.test.cjs scripts/tests/ui-live-cdp.test.cjs。
原始日志 codex-focused-final-green.log：exit 0，42/42 PASS，0 skip、0 cancelled、0 todo。独立子代理追加发现普通文本中短 Authorization 凭据仍残留，已在完整头脱敏中覆盖 Authorization/Proxy-Authorization，并补 Basic/Bearer/Negotiate 三个短值用例；第二次源码冻结后重新验收。

| 要求 | 精确用例名 |
|---|---|
| 实际 API 兼容回退、严格失败携带已安装版本、local 错误继续传播、成功空态 | CV-04: actual API preserves legacy local fallback and strict error with installed versions |
| 下载页实际调用严格 API 并保留本地版本 | CV-04: downloads uses actual strict API and retains installed versions below failure card |
| 检查异常/null/畸形结果、重试仍失败均零启动 | CV-08: thrown/null/malformed checks fail closed; failed retry never launches |
| 重试成功一次启动、检查防连点 | CV-08: successful retry launches once; repeated pending clicks do not duplicate checks |
| 明确问题阻断、警告需确认、确认防连点 | CV-08: explicit errors block; warnings require confirmation and confirm double-click launches once |
| 中文转义/错误字段/无关 note/200 字以后证据 | CV-07: decode Chinese error fields, reject network keywords in unrelated JSON notes |
| 结构化凭据、多 Cookie、布尔统计 | CV-07: redact structured credentials and all cookies while retaining boolean statistics |

上述证据包含真实 API 适配器配合受控 fetch、生产启动块配合受控 DOM/API。它们不能替代真实 WebView、外部服务、进入游戏世界或正式发布证明。独立子代理正在复查源码、反例和完整隔离快照；最终结论见独立报告与 final-acceptance.md。

## 保存失败与限制

codex-focused-r1/r2/r4.log 保留真实失败：URL 非敏感参数被脱敏正则过度消耗、DOM 夹具未执行委托事件、测试误用了不存在的属性选择器。修正生产脱敏边界和夹具后完成 GREEN。未改写原 RED。

此前在本轮自有临时缺文件夹具执行删除的命令被工具返回 Rejected / blocked by policy，实际没有执行。没有换工具或执行者继续删除。CV-01 使用实际生产收集块的只读模拟四种存在性边界，加正常完整快照；实际删除方法为 LIMITED。
