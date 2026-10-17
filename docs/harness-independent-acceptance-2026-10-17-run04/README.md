# Harness 第四轮独立验收 — 2026-10-17

结论：**FAIL_REWORK_REQUIRED**。审查基线 `08e96680748f7c7804cea8ac052c269efd0c7810`（main，开始时工作区干净）。三项确定缺陷均位于 `scripts/verify-ui-pages-live.cjs` 的懒加载验收流程；没有据此认定启动器业务功能新增三项缺陷。

TechnicalAcceptance=FAIL_REWORK_REQUIRED；FormalPhaseApproval=NOT_GRANTED；ReleaseApproval=NOT_GRANTED。本轮只新增 run04 验收材料，没有修改产品/正式脚本，没有提交、推送、打包或发布。

## 输入及执行

- SourceRoot / 脚本 ExecutionRoot：`D:\松光启动器`。
- EvidenceRoot：本目录；香港时区。开始时间、HEAD、关键文件 SHA256 见 `context.json`；结束复核见 `final-verification.json`。
- 原生实例运行于独立临时目录，实际路径见 `official-ui.json.workDir`，保留了运行副本。
- 候选 EXE 沿用已保留产物，SHA256 `72eae380db265a97c16938fd6086441e0409fd192f6b5cce1c01c65a33a32534`，53,089,586 字节。当前正式 UI 验证脚本直接检查该 EXE。`dev/ui/.github/release-notes` 相对上一轮未改，见 `production-diff.txt`；没有将旧 EXE 称为当前改动重新打包的产物。
- 本轮复现使用实际 Edge/CDP 与本机 HTTP 服务。探测脚本从当前源码抽取 evaluate 包装器及正式懒加载整个分支（包含入口、循环、catch、恢复），没有复制另一套业务实现；摘录见 `main-block-under-test-v2.txt`。夹具和错误注入仅位于本轮自有浏览器会话。
- Code Review 插件当前可调用能力仅为 PR CI 诊断，本任务没有 PR 输入；完成本地源码/diff/独立运行验收，不声称远程审查或 CI 已通过。

## 问题及证据

### R4-01 / P2：对变化中的候选数组递增下标，跳过后续目标

位置：正式脚本 174–180 行；主循环 775–776 行。

`targetCount` 在滚动前固定，`scrollAt(index)` 却每次重新过滤“目前在视口外”的图片。滚动第一张后，该图退出候选数组，下次 index=1 会跳过当前 index=0 的第二张。正式分支在两张 native `loading=lazy` 图片上实测：初始 total=2 / loaded=0 / offscreen=2，结束 loaded=1，scrolledInView=1，只请求 a，b.naturalWidth=0。同一会话对 b 直接 scrollIntoView 后 b.naturalWidth=1、新增 b 请求，排除资源损坏或服务故障。

影响：可正常滚动加载的页面被验收脚本误判图片未完成。应稳定本轮目标集合及其身份，且仍只跨 CDP 传可序列化数据；不能只是固定计数后在变化数组里取递增下标。

### R4-02 / P2：延迟占位图被入口条件排除，主流程漏验

位置：正式脚本 767–768 行（IMG_VISIBLE_FN 仍按无 src/srcset 排除无来源节点）。

新 count/scrollAt 虽纳入 `data-defer-src`，入口仍要求 `imgStats.offscreen>0`。夹具含一张首屏正常图、两张真实 IO 延迟占位图：count=2，但图片探针只看到 total=1 / loaded=1 / offscreen=0。执行原样正式分支后没有 lazyScroll 记录，没有新图片请求，两张图 src 仍为空，problems 为空。直接滚到 b 后该图加载成功。

影响：此次声称修好的延迟占位场景在正式流程仍会绕过滚动及未完成图片判定。新增单目标测试直接调用 count/scrollAt，绕过入口，所以仍 PASS。修复需使用一致的可渲染候选口径进入流程，并让未加载的合法延迟目标进入最终完成判定；不能把普通无来源/隐藏上传占位一并判错。

### R4-03 / P2：滚动后探测异常跳过恢复，污染后续截图和状态

位置：正式脚本 790–801 行。

restore 仍在 try 的成功路径内。对自有会话设置初值 scrollTop=137，在首次滚动之后的下一次图片探针 Runtime.evaluate 注入一次可恢复错误。原样正式分支捕获错误并记 problems，但结束位置为 11808，未恢复137；随后用现有 restore 表达式手工清理可恢复137。这证明不是会话永久断开导致无法恢复，而是恢复调用没被执行。该要求在上一轮提示词已明确为 finally 恢复。

影响：失败后的截图与后续检查受探测滚动残留影响。将恢复放入 finally，并分别保留原始探测错误和恢复错误；记录状态的作用域应覆盖清理逻辑。

三项完整原始数据、HTTP 请求与正对照见 `main-flow-probe-v2.json`，结论 `COUNTEREXAMPLES_CONFIRMED`，三项 confirmed=true，退出0代表反例确认成功，不代表被验收代码通过。

首次多目标夹具有一张额外首屏图：滚动后它重新进入视口外候选，恰好补回数组长度，使两张目标均被访问。该结果不能证明下标稳定，也不能算缺陷 RED。原 `probe-main-flow.cjs` / `main-flow-probe.json` / log / 截图完整保留，verdict=INCONCLUSIVE。v2 移除额外首屏图（保留 deferred-only 的正常首屏图），仅校正隔离夹具，不修改产品。重跑入口：

```powershell
node docs/harness-independent-acceptance-2026-10-17-run04/probe-main-flow-v2.cjs
```

注意脚本是“确认当前反例”工具，修复后必须检查行为断言并提供预期 GREEN，不应仅沿用反例确认退出码作为通过。

## 已通过与边界

| 检查 | 独立结果 | 原始证据 |
| --- | --- | --- |
| 全部正式 Node 清单 | 380/380，0失败、0跳过、exit0 | node-tests.log / inventory.json |
| 可见性行为 | PASS、exit0，保持此前破图口径 | visibility.json / log |
| 执行者新增 CDP 单目标测试 | PASS、exit0；请求1→2；正常137→137 | cdp-helper.json / log |
| 本地资源闭包 | 0缺失、exit0 | closure.json / log |
| 当前脚本 + 保留候选原生 WebView | PASS、10/10、异常0、console.error0、UI资源及本机业务失败0 | official-ui.json / official-ui.log / ui-shots |
| 正式懒加载分支反例 | 3/3确认 | main-flow-probe-v2.json |

正式十页本轮未触发 lazyScroll 分支，因此其 PASS 不能代替三项边界复验。另有 favicon.ico 404 一项 cosmeticFailures，按既有分类非阻断，如实保留。已查看首页真实截图和错误注入后的夹具截图。旧 R3-01“仅创建函数、不调用”的单目标反例及 R3-02“正常恢复清零”的反例已修复；本轮未回退 H2-01 十页契约或 H2-02 可见性口径。

没有重新运行 C# 957项、在线升级 A/B/C、RID 发布、真实替换升级、干净检出/远程 CI。产品目录未改，当前任务聚焦验证器；本报告不将历史结果晋升为本轮全产品/发版批准。

下一步材料：`remediation-workflow-2026-10-17.md` 与 `execution-prompt-2026-10-17.md`。材料本身不授权自动实施或发布。
