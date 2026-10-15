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
