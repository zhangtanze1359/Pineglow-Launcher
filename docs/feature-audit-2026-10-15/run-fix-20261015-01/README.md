# FA-01 至 FA-05 本地修复与验证记录

日期：2026-10-15，Asia/Hong_Kong。五类确认问题已修复并通过限定范围本地验证，具备独立复验条件。本轮未提交、推送、发布，也没有进行另一执行者的独立验收；`FormalPhaseApproval=NOT_GRANTED`。

## 输入与现场

- SourceRoot: `D:/松光启动器`；基线 HEAD: `1356efa70cfd91f6289ee6e0dc6af5160b444ab3`。
- 开始仅有上一轮未跟踪的审计/交接材料，完整清单见 `baseline-status.txt`；这些原件、截图、JSON 和原始失败日志未覆盖。
- 浏览器 ExecutionRoot: `dev/PineglowLauncher/bin/Release/net10.0/UI`，由本轮最终 Release 构建输出。真实 Edge/Chromium，原始 `index.html → api.js → launcher.js`，请求由 Playwright 拦截，使用显式验证数据。
- 后端 ExecutionRoot: `dev/PineglowLauncher.Tests/bin/Debug/net10.0`，本轮测试构建；最终 TEMP/TMP 隔离到本运行目录的 `test-temp/`。未使用真实账户凭据或游戏文件。
- EvidenceRoot: 本目录；最终源码、构建程序集、测试程序集与构建 UI 对应哈希见 `final-input-hashes.json`。

## 修复结果

| ID | 最终行为 | 主要源码 |
| --- | --- | --- |
| FA-01 | 安全 DTO 保留 selectedInstanceId/selectedAccountId；后端保存 B 时，首页显示 B，开始游戏请求目标也是 B | `ui/js/api.js` |
| FA-02 | 正确保留代理/来源/新闻兼容/快捷行为等字段和 false/0；代理查看后原值保存，密码保持空输入且不回显。CurseForge 配置标志从后端布尔字段读取 | `ui/js/api.js` |
| FA-03 | 不再引用 renderSettings 局部变量；来源弹窗有加载、多源行式布局、当前源、失败重试、保存去重、错误后重试，以及加载/保存迟到时的业务弹窗和导航保护 | `ui/js/launcher.js`、`ui/css/figma.css` |
| FA-04 | AI 开关成功同步 cache/class/ARIA；失败恢复并提示；全局忙态保护语言重绘期间的重复保存；使用独立翻译键，不覆盖提供商管理原文案 | `ui/js/launcher.js`、`ui/js/i18n.js` |
| FA-05 | 存档/服务器读失败显示独立错误、默认折叠技术详情和真实重试；不冒充真实空列表。重试失败仍可重试，成功恢复数据，离页迟到不导航回旧页 | `ui/js/launcher.js`、`ui/js/i18n.js` |

范围：4 个生产前端文件、2 个测试/夹具文件、测试清单与源码快照白名单、测试元数据和本轮交付材料。后端、依赖、发布工作流、更新逻辑、皮肤实现及真实配置未修改。

测试清单还缺少上一轮已存在的 `update-monitor.test.cjs`，本轮在登记新增测试时一并补齐，保证清单双向校验通过。没有以忽略文件或减少测试绕过门槛。

## RED 与 GREEN

- `red-focused.log`：最初 6 项探针失败，FA-01 至 FA-05 的关键症状均在真实前端路径出现；其中迟到用例最初缺按钮引起的夹具后续异常单独保留，不作为产品 RED。
- `red-old-head.log`：通过 `git show 1356efa:ui/js/<file>` 将旧版 3 个源文件提取到本轮 `old-source/`，真实新测试加载这些隔离旧文件，13 项为 0 通过 / 13 失败。没有改写、撤销或重置工作区产品源码。
- `red-late-source-save.log`：测试复核发现本轮实现的换源保存迟到会重新触发导航，代次由 1 变 2。最小修复为同步当前源按钮文本，不重新导航，且不关闭新的业务弹窗。
- 最终 `ui/tests/feature-state-regression.test.cjs` 有 14 项。`frontend-verified-final.log` 包含全部 327 项通过，14 项新增全部在其中。独立聚焦最终 14/14 见 `green-focused-14.log`。
- 夹具修正：复用原 DOM 桩时补全原生 value/disabled 属性语义，子节点改用实际查询；未为测试变绿改变产品语义。原始失败全部保留。

## 验证结果与命令

所有命令 cwd 为 SourceRoot；时间与输入摘要见最终哈希文件。每项为 0 skip；下表成功项退出码均为 0。

| 检查 | 最终结果 | 命令/证据 |
| --- | --- | --- |
| 新增聚焦用例 | 14/14 | `node --test ui/tests/feature-state-regression.test.cjs`；`green-focused-14.log` |
| 全部前端回归 | 327/327 = 原 313 + 新 14 | `node --test ui/tests/*.test.cjs`；`frontend-verified-final.log` |
| 配置保留、默认身份和 UI 打包 | 18/18 | `dotnet test ... --filter FullyQualifiedName~ConfigPreservationTests\|FullyQualifiedName~ConfigDefaultIdentityTests\|FullyQualifiedName~UiBundleTests`，本轮隔离 TEMP/TMP；`backend-final-isolated.log` |
| Release 构建 | 0 警告、0 错误 | `dotnet build dev/PineglowLauncher/Pineglow.Launcher.csproj -c Release --no-restore -m`；`build-verified-final.log` |
| 正式测试清单校验 | 28 个 Node 文件、1 个 C# 项目，发现一致 | `node scripts/run-inventory-tests.cjs --check --json .../inventory.json`；`inventory.log` |
| 实际浏览器用户操作 | 11 组通过 | `python .../verify-fix.py`；`browser-report.json`、`browser-verified-final.log` |
| 十页/两尺寸 | 20/20，0 横向溢出，0 未捕获 JS 异常 | 上述报告 smoke；1280×800、960×640 |
| 源码与构建输出一致 | API/launcher/i18n/CSS 逐字节匹配 | `final-input-hashes.json` |

18 项 C# 重跑中有一次 UI 解包目录移动报访问拒绝，17/18，见 `backend-final.log`。保留该原始记录；新建本轮独立临时目录后重跑 18/18，见 `backend-final-isolated.log`。未修改产品解包实现，也没有声称查明拒绝的底层原因。

浏览器脚本最初用相同 URL 的 hash 导航来模拟重新打开，实际复用了页面缓存，导致“真空列表”检查未发生新读取；改成独立查询参数强制载入后正常完成。最初报告/日志保留为 `browser-first-report.json`、`browser-first.log`，未计为产品失败。

## 需求—具体测试

用户本轮原文“好的，开始修复吧”，范围承接 FA-01 至 FA-05：

| 需求 | 具体测试 |
| --- | --- |
| FA-01 正确实例选择与启动 | `FA01_LiveStatePreservesSavedSelection`；浏览器 `FA01_SavedInstanceLaunchTarget` |
| FA-02 安全字段/false/0/代理保存 | `FA02_LiveStatePreservesSafeConfigAndFalsyValues`、`FA02_ProxySavePreservesConfiguredValuesWithoutExposingPassword` |
| FA-03 多源/选中/切换/失败重试 | `FA03_SourcePickerLoadsSelectsAndRetries`、`FA03_SourceSaveFailurePreservesSelectionAndAllowsRetry` |
| FA-03 去重与迟到保护 | `FA03_DuplicateSourceSelectionOnlySavesOnce`、`FA03_LateSourceResponseDoesNotReplaceBusinessDialog`、`FA03_LateSourceSaveDoesNotRestartNavigationOrReplaceDialog` |
| FA-04 同步、回滚、跨重绘去重 | `FA04_AiToggleSynchronizesAndRollsBack`、`FA04_PendingSaveDeduplicatesAcrossLocaleRerender` |
| FA-05 错误/空/重试恢复/离页 | `FA05_ListFailureRetriesWithoutEmptyState`、`FA05_RetryFailureRemainsRetryableAndTrueEmptyIsDistinct`、`FA05_LateRetryCannotNavigateBack` |
| FA-05 文本转义与中英 | `FA05_ListErrorTextIsEscapedAndLocalized`；浏览器两尺寸与折叠详情检查 |

## 局限与停止点

浏览器和按钮操作证明真实前端接线，后端响应为隔离夹具；其中启动请求被拦截，没有实际运行 Minecraft。没有把假 API、合成文件脚本或 HTTP 健康响应当成账号/游戏/发布证明。

本机已有 HttpListener 错误 6 仍是实际 HTTP/原生验证的已知限制，本轮没有修复运行时、改变系统设置或关闭未知进程。没有重跑全量后端、原生 WebView2、真实账号登录、联网安装或真实更新包升级。

本地修复与所列验证完成，停止在交付。未提交、推送、打标签或发布；保留所有已有审计资产。
