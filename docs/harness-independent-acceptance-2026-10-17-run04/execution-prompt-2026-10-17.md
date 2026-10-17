# 可复制 Harness 提示词 — 2026-10-17 / R4

```text
请在 D:\松光启动器 完成本轮定向返工。基线 08e96680748f7c7804cea8ac052c269efd0c7810。
先读 docs/harness-independent-acceptance-2026-10-17-run04/README.md、remediation-workflow-2026-10-17.md 和 main-flow-probe-v2.json，严格按其中路径边界实施。不要覆盖 run01–run04 原证据；先保存实际HEAD、脏文件和关键哈希。不 reset/clean/stash/restore/stage-all；不提交/推送/发布/自行放行。

本轮有三项独立确认缺陷，均在 scripts/verify-ui-pages-live.cjs：
R4-01：targetCount固定，但 scrollAt(index) 每次重新过滤当前视口外图片。只含两张native懒加载图、没有额外首屏图时，滚完第一张后数组缩短，index1漏掉第二张。正式完整分支实测2目标只加载1张；手动滚到第二张立即加载。必须稳定本轮目标身份，不能对变化数组递增下标。
R4-02：count已支持data-defer-src，但main入口仍看imgStats.offscreen，统计又排除无src节点。只有两张延迟占位+一张首屏正常图时count2、offscreen0，正式分支完全不执行、无新请求、未加载图也未计入最终pending。入口、候选和终态要一致，同时继续排除隐藏/普通无来源上传占位。
R4-03：restore仍在try成功路径。初值137，首次滚动后图片探针Runtime.evaluate出现一次瞬时错误，catch之后仍停11808；同会话手工restore可回137。save状态应供finally访问，成功/错误/预算耗尽均恢复原值，恢复错误另外记录，不丢原错。

先复现run04反例，再最小修复及GREEN。run04/probe-main-flow-v2.cjs的退出0表示“反例确认成功”，修复后不能用这个0声称通过。v1首次夹具有额外首屏图，恰好掩盖下标漂移，结论INCONCLUSIVE，必须保留且不能当RED。

允许最小修改正式验证脚本、scripts/ha01-cdp-lazy-scroll-behavior.py，必要时scripts/tests/ui-live-cdp.test.cjs；产品dev/ui/.github/release-notes保持只读。新测试必须通过实际CDP运行main使用的完整控制流（入口、遍历、等待、终态、finally），不能只直接调用工厂表达式。覆盖多目标native、deferred-only、无额外首屏图、正常和异常非零恢复、可见破图/持续pending负对照；不能断言“有一张加载了”就算通过。

聚焦GREEN后跑全部Node正式清单、原可见性行为、修后的CDP行为、资源闭包、正式十页原生WebView；完整命令参照run04/commands.json。当前独立Node380/380、十页10/10、资源闭包与单目标测试都已通过，但都不能替代上面三项边界。未改产品/C#不机械重复957项或发布/升级下载。记录实际退出码/数量/skip、源码和EXE哈希、HTTP请求、loaded/naturalWidth、scrollTop、截图、finally清理及结束漂移。所有原始RED留存，缺环境不能当PASS。

最终提供真实改动清单、根因和RED→GREEN证据、新证据路径及剩余缺口，然后以“具备独立复验条件，等待 Codex 独立验收”结束。不自行提交、打包、发布、正式放行或进入下一任务。
```
