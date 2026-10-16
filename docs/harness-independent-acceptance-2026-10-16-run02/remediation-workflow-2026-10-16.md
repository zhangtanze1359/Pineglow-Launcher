# 第二轮最小返工流程

日期：2026-10-16，Asia/Hong_Kong。基线 `D:/松光启动器`，HEAD `f9b109d9ac3b665a926acbad3bdd96c8d57ac77e`。本文件为验收材料，不自动授权修改、提交或发布。

## 范围与保护

仅在得到用户实施授权后修改 `scripts/verify-ui-pages-live.cjs`、`scripts/tests/ui-live-cdp.test.cjs`、`scripts/ha01-image-visibility-behavior.py` 与必要的本轮报告/证据。产品UI、后端、依赖/运行时、发布工作流、真实用户配置与账户游戏文件只读。保留旧RED/报告/截图，不覆盖本轮probe。先核对HEAD/dirty，不reset/clean/restore/stash/stage-all。

## 处理顺序

1. **H2-01：恢复正式测试门槛。** 保留10页与playtime，修正旧九页契约。测试应验证准确页面身份、无重复和require.main导入守卫，不能只把失败断言移除。先聚焦ui-live-cdp，再按清单全跑，核对379及变化原因、失败/skip/退出码。
2. **H2-02：收窄隐藏判定。** 不把aria-hidden或img的type属性当CSS隐藏。用本轮boundary-probes的可见破图反例做RED，修正predicate和旧错误预期。真正隐藏/无来源占位继续排除，可见装饰图和HTML SVG图的坏资源仍须失败；不依赖allow-missing-images或网络分类替它放行。
3. **H2-03：真实滚动。** 对实际滚动祖先/目标图片操作，允许浏览器在每步渲染、发起请求；恢复初始scrollTop。内层overflow容器+远处HTTP懒加载图作为反例，要求滚动前请求0、滚动后请求发出且naturalWidth>0。同步window.scrollTo计数或offscreen>0不算完成证明。
4. **强化有区分力的行为检查。** 优先从正式实现调用真正的probe/scroll/最终判定，必要时为它们建立很小的测试接缝，避免另抄一份判定。明确证明：隐藏占位忽略；可见aria-hidden/SVG坏图FAIL；受控延迟图片不提前PASS、释放后成功；内层滚动有效；真实坏图不因滚动被放过。浏览器不可用仍明确skip，不能称绿色。
5. **验证最小闭环。** 聚焦行为验证→全Node清单→资源闭包/清单一致→正式脚本十页原生WebView。正常十页PASS与反例都满足，才可记HA-01通过；不因简单fixture全绿忽略旧真实反例。产品/C#无变化时不机械重跑957项C#。
6. **更新报告。** 把本轮378/379真实失败与修复后结果分别归档，不能沿用旧379/379。HA-02主要更正、16项真实更新A/B/C通过均保留，不回退此前已通过的辅助脚本修复。

## 交付与停止点

新建run编号，记录SourceRoot/ExecutionRoot/EvidenceRoot、HEAD/dirty、精确路径和SHA、命令、真实退出码/数量/skip、正常/破图/加载中/滚动的截图和HTTP请求证据。只停止本轮进程，后台辅助进程隐藏窗口，不停服务/驱动、不修改系统安全设置。交付“具备独立复验条件，等待Codex独立验收”，不自动提交、推送、打标签或发布。
