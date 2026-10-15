# Pineglow 新版 UI 集成 Implementation Plan

**Goal:** 将用户截图所示的 2026-10-14 深黑设计接入产品默认入口，并保持真实业务行为。

**Architecture:** 保留 launcher.js 现有业务状态机与 api.js 契约，新增设计令牌及呈现样式，调整应用壳、首页和交互。游戏时长使用既有累计统计并增量记录真实会话；旧累计值不伪造为会话。

**Tech Stack:** 原生 HTML/CSS/JavaScript、Avalonia/WebView2、.NET 10。

**Spec:** C:/Users/Administrator/AppData/Roaming/Open Design/namespaces/release-stable-win/data/projects/0f20f592-346b-4326-adb0-8d62487e2006/pineglow-launcher.html 与同目录 integration-notes.md、brand-spec.md。

## Global Constraints

- 当前用户授权实施、验证；不包含提交、推送或发布。
- SourceRoot=ExecutionRoot=D:/松光启动器；HEAD=ea6d1cbf145bd4ae479d9f4774dba468351a91a8。
- 初始 tracked diff 为空；2710 个既有未跟踪文件保留，状态清单先存于系统临时目录。
- EvidenceRoot=docs/ui-integration-2026-10-15；原始设计只读，设计内针对原型阶段的范围描述不代替当前实施授权。
- 不移植模拟数据库、模拟进度、模拟登录成功、离线错误开关或缺乏后端支持的暂停按钮。
- 保留下载错误/空态区分、独立下载队列、新闻验证时间、取消与恢复、诊断并发保护、皮肤预览与应用分离。

## Tasks

- [x] 1. 保存设计来源哈希与产品基线；将令牌和布局映射到 ui/index.html、ui/css/redesign.css。顶栏独立于滚动区，侧栏三组，所有原业务视图保留。
- [x] 2. ui/js/launcher.js 接入真实首页统计/队列、顶栏、导航当前页、Ctrl+K 搜索导航、双语切换和账户入口；沿用现有启动前检查与窗口消息。
- [x] 3. Mod 管理改为右侧抽屉，提供搜索/启停筛选及行内移除确认；真实恢复 API 与错误重试继续可用。
- [x] 4. 新增 ui/js/playtime.js：纯时间范围、分日、树状图布局计算与日期筛选；在 launcher.js 注册第十页。GameInstance 与 LocalApiService 增量保存真实游玩记录并通过 /api/stats 返回；无历史记录时明确说明。
- [x] 5. 本地皮肤文件先预览，独立按钮上传至微软账户；账户皮肤选择只影响预览。
- [x] 6. 使用 code-testing-agent 技能为新增时间计算与会话记录写聚焦行为测试，登记正式清单；运行 Node 回归、C# 回归、资源闭包与产品构建。
- [ ] 7. 以隔离配置启动本轮构建的真实 WebView2；检查十页、三档尺寸、语言、命令面板、模态与抽屉、错误状态与无横向溢出。保存截图、原始报告、输入/产物哈希与局限；完成自评。

## Verification commands

```powershell
node --check ui/js/launcher.js
node --check ui/js/playtime.js
node scripts/run-inventory-tests.cjs check
node --test ui/tests/*.test.cjs scripts/tests/*.test.cjs
node scripts/check-ui-resource-closure.cjs
dotnet test dev/PineglowLauncher.Tests/Pineglow.Launcher.Tests.csproj --no-restore -m
dotnet build dev/PineglowLauncher/Pineglow.Launcher.csproj --no-restore -m
```

本计划直接在当前会话执行；不引入额外提交或授权要求。真实网络登录、生产游戏数据、发布与远端 CI 分别列为未验证或未授权范围。

## 执行结果

1–6 已完成。第 7 项的最新版构建真实 WebView2 十页三视口 30/30 和六组隔离交互已通过；完整 C# 回归、全部系统 DPI 与真实外部账户/游戏仍有独立限制，因此该综合项保留未勾选。详细结果见 docs/ui-integration-2026-10-15/README.md。
