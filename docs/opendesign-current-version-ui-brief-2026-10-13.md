# OpenDesign Cloud UI 修复需求与验收约束

日期：2026-10-13，Asia/Hong_Kong。源版本：D:\松光启动器，HEAD 1829f1c。这是当前版本的错误状态修复，不增加新的产品功能。

## 真实发现

- CV-04：下载中心 renderDownloads 的 getRemoteVersions catch 把失败转换成空数组，网络不可达时界面显示“暂无可下载版本”，没有解释或重试操作。应区分加载、成功空列表、请求失败、已有下载任务。
- CV-05：新闻服务首次失败返回 DateTimeOffset.MinValue；前端当有效时间格式化，真实截图显示“最后验证 1-01-01”。未成功验证时展示“尚未验证”或隐藏日期；保留失败/缓存/过期状态差别。
- CV-06：内容中心 content-error/content-retry-btn 没有对应样式。真实 DNS 失败把底层主机与 /api/content/search 路径直接铺在主内容区。主呈现用易懂说明和明确重试按钮，技术信息置于可展开详情，长文本换行，维持键盘可用和焦点可见。

证据：docs/current-version-audit-2026-10-13/baseline-ui-shots/{content,downloads,news}.png。九页真实 WebView 巡检没有未捕获异常；content/downloads/news 的外部数据未成功加载，不据此认定外部服务成功。

## 设计方向

桌面 WebView 界面，中文优先，提供英文文案对应。保持左侧九页导航、松树标识、暗色森林风格，复用现有色板：base #10191b、card #172326、border #2c3e42、green #91d5af、text #edf3ef、muted #a3b5b7、danger #ff8e86。字体 Segoe UI / Microsoft YaHei UI，卡片圆角 14px，按钮 8px。

只设计受影响三个页面的局部状态。交付能运行的 HTML/CSS 原型、状态矩阵和可供 Harness 集成的组件/尺寸规范。必须包含加载、失败、重试进行中、重试成功、重试仍失败、成功空态；下载已有任务在版本请求失败时仍可见。不得编造真实下载完成/外部账号登录成功。

## 本地集成验收

1. 下载接口拒绝时显示错误和真实重试入口，不显示“暂无版本”；成功返回 [] 时才显示空态。任务区独立展示已有任务或自身错误。
2. 日期为 null、空值、无效日期、0001-01-01 时都不显示历史假日期；有效验证时间、缓存时间正常保留。
3. 内容失败信息保持 HTML 转义，默认不铺开技术路径，详情可展开；窄窗口不溢出，重试按钮可用键盘触发且不会重复发送并发请求。
4. 重试恢复真实数据后移除错误状态，保留所选内容源、类别、查询和版本筛选。
5. Codex 用隔离实际 WebView 和可控 HTTP 失败/恢复输入验证；原型成功不等于本地修复通过。

## 额外工具缺陷（由 Harness 修复）

CV-07：scripts/verify-ui-pages-live.cjs 的 connectCdp 错误处理引用作用域外 method。独立模拟 CDP error 产生 ReferenceError 而未正常拒绝待处理 Promise。需保存每个 pending 请求的 method，正确 reject；测试真实错误响应不会同步抛异常/悬挂。原始反例：current-version-audit-2026-10-13/cdp-error-red.log。

同脚本必须保留真实环境失败，不将默认最小字符数未满足等同于“仍在加载”，也不得将所有 HTTP 500 一概算环境失败。需根据明确终态和响应体区分 UI 失败、正常空态、外部依赖失败及未知内部错误，未知继续阻断；证据 URL 的 token 等敏感值必须脱敏后归档。
