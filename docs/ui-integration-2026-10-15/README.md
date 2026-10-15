# 新版 UI 集成交付记录

日期：2026-10-15，Asia/Hong_Kong。

**结论：新版设计已成为产品默认 UI 入口，构建成功，限定范围的 UI 与交互验证通过。完整 C# 回归及默认运行时兼容性仍未闭环，不声明整体发布验收通过。**

## 来源与现场

- SourceRoot：`D:/松光启动器`；HEAD：`ea6d1cbf145bd4ae479d9f4774dba468351a91a8`。
- 设计来源：`C:/Users/Administrator/AppData/Roaming/Open Design/namespaces/release-stable-win/data/projects/0f20f592-346b-4326-adb0-8d62487e2006/pineglow-launcher.html`（2026-10-14 最新深黑版本）。原件只读；来源哈希见 `design-input-hashes.json`。
- 初始 tracked diff 为空；2710 项既有未跟踪文件保留，完整清单见 `baseline-status.txt`。
- 最终原生 ExecutionRoot：本目录 `preview/`，使用本轮构建输出的 `UI/`；不是设计原型，也不是源码目录替换。最终产物/源码哈希见 `final-input-hashes.json`。
- EvidenceRoot：本目录。真实接口交互夹具单独放在 `runtime/fixture-game/`，未触及真实游戏目录和账户。图中“验收实例”及游玩记录是测试夹具，不代表用户真实游玩。

## 完成范围

1. `ui/index.html` 默认加载 `ui/css/redesign.css`：深黑色板、白色主按钮、细边框卡片、统一顶栏、分组导航、独立中英切换与账户入口。
2. 原有九页均保留真实业务接口；首页调整为实例启动、累计统计、真实下载队列与新闻。内容中心使用新版卡片布局。
3. 增加 Ctrl/⌘+K 快速操作，支持搜索、键盘选择、Enter 执行、Esc 关闭和归还焦点；迟到的刷新结果不再强制导航回旧页面。
4. Mod 管理为右侧抽屉，支持搜索、启用/禁用筛选、状态文本、行内移除确认、恢复区；启停后同步真实文件名。删除与恢复仍走原有可恢复 API。
5. 新增第十页“游戏时长”：真实记录的实例分组/会话树状图、日期筛选、自定义范围、月历、跨午夜拆分与尺寸兜底。`/api/stats` 增量返回会话。旧累计时长保留，不回填虚构日期；游戏正常退出且启动器仍在运行时才记录会话。
6. 本地 PNG 选择先预览，应用为独立按钮；离线/Authlib 账户不上传。读取失败显示明确缺图态，不再用任意配色人形冒充贴图。生产入口即使退到 file 协议也不切换到示例账户/模拟成功。
7. 新文件登记在测试清单和候选快照白名单；未提交、推送或发布。

## 验证结果

| 层级 | 结果 | 证据 |
|---|---|---|
| 产品构建 | PASS；0 警告、0 错误 | `build-final.log` |
| Node 回归 | PASS；353/353，0 skip | `node-final.log` |
| 新增 C# 会话模型 | PASS；3/3 | `focused-dotnet.log`、`dev/PineglowLauncher.Tests/GamePlaySessionTests.cs` |
| 测试清单、资源闭包、JS 语法 | PASS；26 个 Node 文件双向一致，11 个静态资源扫描文件，无缺失引用 | `resource-closure.json`、清单校验命令 |
| 最新构建真实 WebView2 | PASS；十页 × 1280×800 / 960×640 / 1600×900 = 30/30，无横向溢出、无未捕获 JS 异常 | `webview-report.json`、`webview-built-final-run.log`、`*-live.png` |
| 关键交互 | PASS；6 组，使用隔离 Edge 与真实产品本地 API | `interaction-report.json`、`verify-interactions.cjs` |
| 独立冷启动 | PASS；真实本地 API、构建输出新 CSS、原生 WebView2 页面可用 | `preview-cold-start.json` |
| 默认 .NET 10.0.9 下 C# 全量 | 未通过；798/944 通过，146 失败，多为 HttpListener 初始化/依赖失败 | `dotnet-host-regression.log`、`tests/host-regression.trx` |
| 指定 10.0.0 的完整 C# 重跑 | 未完成；507 项完成且通过后长期无进展，本轮主动停止自有 testhost | `dotnet-final-pinned.log`、`tests/final-pinned-regression.trx`、`full-regression-timeout.json` |

说明：TRX 的“testhost 崩溃/中止”是本轮对无进展测试进程执行停止后的结果，不作为产品自行崩溃的证明。日志末尾即使出现“已通过 507”也不代表全量 944 通过。第一轮临时目录权限失败、运行时探针、原生窗口并存导致的 WebView2 初始化失败均已保留，没有改写为成功。

最终真实 WebView2 的三档尺寸使用 CDP 设置渲染视口；不是手动验证所有 Windows DPI/系统缩放组合。六组有数据交互使用独立浏览器，不能据此替代原生文件选择器、读屏器或在线账户服务验证。

## 本地查看

已经启动一个使用隔离 portable 数据的原生预览窗口，状态记录在 `preview/owned-preview.json`。重新打开可在 PowerShell 7 执行：

```powershell
pwsh -NoProfile -File "D:\松光启动器\scripts\start-new-ui.ps1" -Isolated
```

该脚本使用本机已安装且本轮验证过的 .NET 10.0.0，仅为本地兼容启动入口；不修改系统设置、不安装或切换系统运行时。默认 .NET 10.0.9 在带产品依赖的探针中报 HttpListener 句柄无效，仍需独立处理；不能把本地兼容入口当成发布修复。WebView2 同类实例参数不一致时可能发生初始化冲突，重新查看前请关闭本轮旧预览窗口。

不带 `-Isolated` 时按普通启动器方式读取真实用户配置；本轮自动验证未使用此模式。

## 未验证与停止点

- 未执行微软账户真实登录/上传、完整游戏启动至退出的新会话采集、联网安装、真实用户数据迁移、读屏器、全部系统 DPI、干净提交/CI、RID 发布包和在线发布。
- 长期运行中启动器被提前关闭的会话不记录，界面已说明；没有将其伪造为完成会话。
- FormalPhaseApproval=NOT_GRANTED；Git 提交/推送/发布均未执行。
- 默认运行时与 C# 全量回归的后续材料分别见 `../new-ui-runtime-remediation-workflow-2026-10-15.md` 与 `../new-ui-runtime-execution-prompt-2026-10-15.md`。

## 需求与证据

| 用户需求 | 证据 |
|---|---|
| “把这套重构好的ui加进来作为新版本的ui” | 默认入口 `ui/index.html`、`redesign.css`、最新构建真实 WebView2 30/30、`interaction-report.json` 六组交互、353/353 前端回归；日期/布局与会话模型的具体新增测试名见 `.testagent/plan.md`。默认运行时和完整 C# 验收限制如上。 |
