# 新版 UI 测试映射

| 检查项 | 测试或证据 |
|---|---|
| 日期无效/跨度上界 | playtime: ISO calendar dates reject rollover and custom range preserves 120 day limit |
| 周一/滚动范围 | playtime: Monday based week and rolling range include today exactly once |
| 跨午夜且不发明记录 | playtime: midnight sessions split actual seconds across dates without inventing history |
| 树状图面积、边界、无重叠 | playtime: treemap conserves area, contains rectangles and has no overlaps |
| 累计/精确记录/序列化 | RecordPlaySession_PreservesLegacyTotalsAndRecordsActualInterval |
| 无效时间不落盘 | RecordPlaySession_InvalidIntervalsDoNotCreateRecordsOrChangeTotals |
| 旧配置无虚构会话 | LegacyConfig_DoesNotFabricateDatedSessions_AndNullListCanRecord |
| 生产 file 不回 mock | New product UI: file fallback still uses live API and never fabricates account data |
| 进度转换与损坏边界 | New product UI: fractional download progress maps to percent and clamps corrupt values |
| 真实页面/尺寸 | docs/ui-integration-2026-10-15/webview-report.json |
| Mod 文件/恢复/预览 | docs/ui-integration-2026-10-15/interaction-report.json |

先运行聚焦测试，最终 Node 回归一次；C# 全量如被运行时/原有夹具阻断则保存失败原文，不能用聚焦通过代替全量通过。

## FA-01–FA-05（2026-10-15）

| 要求 | 新增检查 |
| --- | --- |
| 选中 B、实际启动 B | FA01_LiveStatePreservesSavedSelection；浏览器 FA01_SavedInstanceLaunchTarget |
| 配置透传且不泄密、false/0 | FA02_LiveStatePreservesSafeConfigAndFalsyValues |
| 代理原值保存 | 浏览器 FA02_ProxyRoundTripPreservesValuesAndSecrets |
| 两源显示/切换/错误重试 | FA03_SourcePickerLoadsSelectsAndRetries；浏览器同项及关闭后迟到返回 |
| AI 成功/失败/去重/重绘 | FA04_AiToggleSynchronizesAndRollsBack；浏览器开启/关闭/重绘/失败/重试 |
| 两列表失败、重试恢复、空和迟到 | FA05_ListFailureRetriesWithoutEmptyState；FA05_LateRetryCannotNavigateBack；浏览器组合 |
| 中英文案和转义 | FA05_ListErrorTextIsEscapedAndLocalized |

先保留 6 项失败 RED，再分组修复。执行真实 API 适配器到页面的聚焦套件、完整 Node、Release 构建和构建输出浏览器检查。test-gap-analysis 复核实际断言，任何变异如需验证都在本轮隔离副本进行，不撤销用户工作。最终状态追加到 status.md。
