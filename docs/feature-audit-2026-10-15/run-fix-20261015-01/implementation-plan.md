# FA-01 至 FA-05 实施计划

**Goal:** 修复刚才确认的五类问题并完成本地验证。

**Architecture:** 保持后端接口；前端显式映射安全字段，来源弹窗复用既有 modal，AI 保存同步缓存/控件，列表使用既有错误组件和 goTo 导航代次保护。

**Tech Stack:** 原生 JavaScript、Node test/vm、Python Playwright、.NET 10。

**Spec:** `docs/existing-features-remediation-workflow-2026-10-15.md` 与本聊天用户“开始修复”授权。

## 范围与步骤

- [x] 保存既有审计文档及产品输入基线；新增 `.testagent` 分节不覆盖历史。
- [x] 新增真实 API→launcher 聚焦测试并保存有效 RED。
- [x] `api.js` 补 selectedInstanceId/AccountId、来源、代理状态/安全配置标志、兼容和快捷行为字段，保留 false/0。
- [x] `launcher.js` 来源弹窗读取当前 selected，支持加载失败/重试和关闭后的迟到保护；AI 保存去重/成功同步/失败回滚。
- [x] 存档与服务器失败展示独立错误和按钮；重试复用 goTo 的导航代次，禁止迟到结果抢回页面；补中文和英文。
- [x] GREEN、Node 回归、Release 构建、当前构建输出十页及五类浏览器复验。后端未改，只补必要资源/配置聚焦。
- [x] 核对 diff/哈希、逐项复核测试断言并记录局限。

不提交、推送、发布，不更改后端、凭据策略、更新逻辑或系统设置。沿用已知 HttpListener 环境限制，不用假 API 声称真实 HTTP/原生验证通过。当前授权已包含实施，无需再次确认。
