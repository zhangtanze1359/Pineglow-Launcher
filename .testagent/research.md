# 新版 UI 有界测试研究

目标为本轮新增的 playtime.js 时间/布局计算、GameInstance.RecordPlaySession、产品 API 模式与下载进度；既有 Node vm 测试与 xUnit SDK 项目是权威惯例。未重构或恢复任何原有源码。

检查项：真实会话跨午夜分日；日期有效性与 120 天上界；面积与边界无重叠；旧累计保留且不回填虚构会话；可空旧字段往返；生产页面即使 file 回退也不伪造账户；分数进度转百分数；新版导航/确认语义；真实隔离 API 与浏览器交互。

环境证据：本机默认 .NET 10.0.9 在引用产品依赖的探针中 HttpListener.Start 抛 Win32 6；相同探针显式 10.0.0 成功。GUI/测试必须区分实际运行时。未更改系统配置。

## FA-01–FA-05 修复研究（2026-10-15）

有界目标为 `api.js` 配置 DTO、`launcher.js` 的实例消费、来源弹窗、AI 保存状态、存档/服务器错误重试和 `i18n.js`。基线 HEAD 为 1356efa；上一轮检查文档和截图是已有用户资产，不覆盖。测试使用 Node 原有 vm/DOM 惯例，加载真实 API 适配器和完整 launcher；浏览器使用本轮构建 UI、拦截响应和显式验证数据。后端只读，真实账号/游戏不参与。

检查清单：非首项选择和启动目标；安全字段/false/0 透传；代理未改保存；两源列表/选择/失败重试/迟到响应；AI 成功同步、关闭、连点、失败回滚、重试、语言重绘；两列表的失败/空/恢复、重复重试和离页迟到响应；中英与转义。补已有套件的跨层缺口，不重建整个测试体系。

已检索可调用工具与本地技能目录，`find-untested-sources` 和 `assertion-quality` 均不可用；目标与现有测试配对为 `api.js→api-contract.test.cjs`、`launcher.js/i18n.js→current-version-errors.test.cjs`。新增 `feature-state-regression.test.cjs` 复用既有 DOM 桩并使用真实 fetch 适配器，另以真实 Edge 验证 DOM 交互。
