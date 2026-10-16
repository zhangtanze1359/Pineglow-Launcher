# 可复制给 DeepSeek Harness 的第二轮返工提示词

本文件仅为交接材料，需用户明确交给执行者后才构成实施请求。

```text
请在 D:\松光启动器 修复本轮独立验收 H2-01/02/03，范围限定在正式界面验证器及其测试，不改已通过的产品功能，不提交/推送/打tag/发布。

先读 docs/harness-independent-acceptance-2026-10-16-run02/README.md、boundary-probes.json 和 remediation-workflow-2026-10-16.md。基线HEAD为f9b109d9ac3b665a926acbad3bdd96c8d57ac77e；核对当前dirty，保留所有已有验收材料、RED和截图，不reset/clean/restore/stash/stage-all。

H2-01：正式Node全清单378/379，ui-live-cdp.test.cjs:301仍要求VIEWS.length=9，而你已正确补了第十页playtime。保留10页，修正测试对准确页面身份/去重/导入守卫的契约，不删除/跳过断言，不沿用旧379/379报告。

H2-02：IMG_VISIBLE_FN把aria-hidden=true和type=image/svg+xml直接排除。真实Edge反例中两个HTML img都84×84且已破图，但统计只剩健康图total1/loaded1/broken0，图片失败谓词false。aria-hidden不是视觉隐藏，img的type也不是SVG节点类型。修正谓词与行为脚本中“aria-hidden必须不可见”的错误预期；真正hidden/隐藏祖先/无src占位仍须忽略，可见装饰图/SVG坏图仍须失败。

H2-03：同步window.scrollTo循环并立即归零，没有滚动实际的.content-panel/内层overflow容器。本轮真实HTTP懒加载反例运行正式滚动表达式后scrollTop仍0、请求0次；真正scroll_into_view后立即加载成功。请对真实滚动祖先/目标滚动，逐步等待布局和请求、恢复原位置。offscreen>0和进入分支不等于有效滚动，数据URI图片也不能证明HTTP懒加载请求触发。

允许路径：scripts/verify-ui-pages-live.cjs、scripts/tests/ui-live-cdp.test.cjs、scripts/ha01-image-visibility-behavior.py、必要的本轮报告/新证据。其余生产UI/后端/依赖/运行时/.github/真实配置账户游戏文件只读。

用真实布局验证和本轮反例做RED→最小修改→GREEN；验证真正的正式probe/scroll/最终失败判定，不复制一套能自证通过的算法。证明隐藏占位不误报、可见破图必失败、受控pending不提前通过、释放响应后成功、内层HTTP懒加载实际滚动并加载。浏览器不可用要明确skip。

最后跑聚焦行为、正式全部Node、资源闭包/清单双向一致、正式十页原生WebView。不要用allow-missing-images绕过。产品/C#未变不机械重跑957项C#。真实更新A/B/C本轮独立16/16通过，不重复改写该脚本或版本介绍。

每项记录实际输入/产物SHA、运行根、时间、命令/退出码/测试数/skip、DOM布局与请求前后对照，保留全部原始失败。仅停止本轮拥有的隔离进程，不停用系统服务/驱动，不改变防火墙/TLS/执行策略。

完成后报告真实结果及未验证边界，停止在“具备独立复验条件，等待Codex独立验收”；不自行正式放行或发布。
```
