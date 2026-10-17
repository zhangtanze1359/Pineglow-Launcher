# 第四轮返工流程 — 2026-10-17

当前输入：`D:\松光启动器`，HEAD `08e96680748f7c7804cea8ac052c269efd0c7810`。完整独立结论见同目录 README。目标仅关闭 R4-01/R4-02/R4-03，保持已通过十页及可见破图契约；本文件为交接方案，不授权提交、推送、发布或进入下一阶段。

## 边界

允许最小修改：`scripts/verify-ui-pages-live.cjs`、`scripts/ha01-cdp-lazy-scroll-behavior.py`，必要时 `scripts/tests/ui-live-cdp.test.cjs`。新证据写新日期/轮次目录；可更新 `docs/release-prep-verify-2026-10-16/README.md` 的当前状态且保留历史结论。

只读：本 run04 全部证据、前3轮验收目录、`scripts/ha01-image-visibility-behavior.py`、产品 `dev/` / `ui/` / release-notes / .github。若发现确需超出范围，先记录具体根因及必要改动，不顺带重构。保护其他脏文件；不 reset/clean/stash/restore/stage-all，不覆盖旧 RED。

## 步骤及完成标准

1. 固定实际 HEAD、git status、SHA256、候选 EXE 哈希与运行目录。阅读 run04 README 和 `main-flow-probe-v2.json`；阅读完整正式 main 的入口、循环、终态和异常清理，不能只读表达式工厂。
2. 原样复现：两张 native 懒加载目标只有1张被加载；只有 data-defer-src 时 count2但入口offscreen0被跳过；初始137后瞬时 evaluate 失败最终11808。保留日志及请求/geometry/loaded 变化。run04 v1 为 INCONCLUSIVE，不能当RED或“修改前已通过”依据。
3. R4-01：在页面内保存本轮稳定候选身份，按值仅传标识/序号。滚动和等待不会让后续目标消失或重复；清理会话状态。不能改为瞬间滚到最后一张后声称所有加载成功。
4. R4-02：入口、候选、最终pending判定使用一致的实际可渲染来源口径。合法 data-defer-src 必须被遍历并等待，普通隐藏/无来源上传占位仍不算失败。延迟目标尚无 src 时不能提前 loaded==total 通过。保留 visible broken、持续 pending 会失败的证据。
5. R4-03：save 的状态置于 finally 可访问范围；探测成功、抛错、预算耗尽均尝试恢复已记录容器和 window 原始横纵位置。恢复失败单独记入报告，不覆盖最初错误；不用额外默认0掩盖保存失败。状态清理不能依赖成功路径。
6. 行为测试执行**正式完整控制流**，包括入口guard、计数/枚举、逐项等待、最终判定和finally；优先抽取可被 main 与测试共同调用的有界流程，或严格读取原main运行。只重用 buildLazyExprs 不够。覆盖两个及三个目标、无额外首屏图、deferred-only、native、非零初值、瞬时错误后恢复、破图/pending负对照。实际 CDP returnByValue:true；不得改为 ElementHandle 绕开包装器。
7. 先聚焦 RED→GREEN，再运行本 run04 commands.json 中 Node正式清单、可见性、新CDP行为、闭包、原生十页。若未改产品/C#，不机械重跑构建957项或真实下载升级。缺浏览器/环境写未执行，不能SKIP伪装PASS。
8. 新证据记录命令/退出码/数量/skip/源码及EXE SHA256/请求/滚动前后/截图/失败与恢复，不复制旧证据冒充本轮。对关键源码做结束漂移检查，只清理自己拥有的服务/浏览器/实例，保留原失败资料。

交付状态只能是“具备独立复验条件，等待 Codex 独立验收”。实现者自检不是技术验收通过，不提交、不推送、不打包发布。
