# 已有功能只读巡检报告

日期：2026-10-15，Asia/Hong_Kong。

结论：确认 5 类明显产品问题，其中 FA-01 和 FA-02 共享前端状态映射缺字段这一根因，分别影响实例启动和设置保存。没有修改产品源码、真实用户配置或游戏文件，没有提交、推送或发布。不能据此对全产品或发布放行。

## 输入与方法

- SourceRoot: `D:/松光启动器`。
- 输入 HEAD: `1356efa70cfd91f6289ee6e0dc6af5160b444ab3`，开始工作区干净。
- 浏览器 ExecutionRoot: 当前源码 `ui/`；真实已安装 Edge 的 Chromium 内核，1280×800，页面正常加载原始 `index.html`、`api.js` 和 `launcher.js`。
- 浏览器通过 Playwright 拦截 `/api/**` 返回与当前 C# 响应一致的受控数据；使用真实前端状态归一化与按钮事件，不替换成 mock API 对象。POST 请求仅写入 Python 内存夹具。证据不能替代原生 WebView2、真实账号/游戏或完整后端 HTTP 验证。
- 后端 ExecutionRoot: `dev/PineglowLauncher.Tests/bin/Debug/net10.0`，本轮构建测试程序集；TEMP/TMP 指向本轮 `test-temp/`，凭据相关测试已有内存后端。未执行实际账号、真实游戏或线上状态变更。
- EvidenceRoot: 本目录。源码输入哈希见 `input-hashes.json`；最终 `git diff` 验证产品源码未变。

## 已确认问题

### FA-01 / P1：已保存的实例选择失效，首页启动了另一个实例

位置：`ui/js/api.js:385`；`ui/js/launcher.js:77`、`:3128`。

`getState()` 将服务端 config 映射成一个新对象，却没有保留 `selectedInstanceId`。虽然实例自身的 `selected` 标志被映射，`loadState()` 实际只读取 `STATE.config.selectedInstanceId`，没有这个字段就选择第一项。

复现：服务端返回列表 A、B，并明确 `config.selectedInstanceId="inst-b"`。页面实际缓存选择为 `inst-a`，首页显示 Audit Vanilla；点击开始游戏后实际发出 `/api/instances/inst-a/launch`。请求在夹具中被拦截，未真的启动游戏。

影响：重新打开或重新拉取状态后可能启动错误实例；资源中心版本/加载器筛选也使用同一个选择。需要优先修复状态透传，并补实际 API 适配器到页面的测试，而不是只测试预先注入完整 config 的桩。

### FA-02 / P1：代理等已保存设置显示为默认值，保存会覆盖原配置

位置：`ui/js/api.js:388`；`ui/js/launcher.js:632`、`:3723`、`:3750`；后端真实响应字段在 `LocalApiService.cs:1145`。

服务端已经返回 `downloadSource`、代理字段、`newsFakeIpCompat`、`minimizeOnLaunch` 和 `autoCloseOnExit`，但 API 映射丢弃这些字段。进入设置后源名称回退为官方、兼容开关显示关闭，代理对话框也读不到现有值。代理字段中 127.0.0.1/7890 仅为 placeholder，并非实际 input value。

复现：夹具配置中代理开启、地址 127.0.0.1、端口 7890。页面显示代理关闭，host/port 实际值为空；不作修改点击保存，发送 `{proxyEnabled:false, proxyHost:"", proxyPort:0, proxyUsername:"", proxyPassword:""}`。按真实 `PatchConfig()` 规则，这会关闭代理并清空地址/端口/用户名；密码空值在后端被保留，不能称为凭据被删除。

影响：用户查看后保存即可覆盖原本可用的网络设置；快捷启动行为和新闻兼容开关同样存在显示/状态回读不一致。修复时必须保留“只返回 passwordConfigured，不回传真实密码”的现有边界。

### FA-03 / P2：下载源选择按钮直接报错，无法打开选择列表

位置：`ui/js/launcher.js:3695`；相关 `dlSource` 仅在 `renderSettings()` 的局部作用域 `:634` 声明。

复现：`/api/sources` 正常返回 2 个源，点击设置的源选择按钮，真实浏览器抛出 `ReferenceError: dlSource is not defined`，没有渲染任何源选项。

影响：换源入口不可用；不是源测速失败，也不是外网连接错误。应从当前状态或 sources 响应取得选中源，再构建列表；处理加载失败和当前源标记。

### FA-04 / P2：AI 开关保存成功后缓存不更新，失败时也不回滚

位置：`ui/js/launcher.js:3652`。

该分支仅切换 class/ARIA 后调用 `updateConfig(...).catch(() => {})`。成功没有写回 `STATE.config.aiDiagnosisEnabled`；失败被吞掉，没有回滚和错误反馈。

复现：由 false 开启，夹具后端已保存 true，但页面缓存仍 false；切换语言触发重渲染，开关重新变为关闭。随后让保存返回 503，界面仍保留打开状态。

影响：用户无法判断实际是否启用；同页 AI 管理和后续重渲染使用旧状态。应补忙态、成功同步与失败回滚/错误反馈。

### FA-05 / P2：存档、服务器读取失败被误显示成“没有数据”

位置：`ui/js/launcher.js:2507`、`:2647`。

两页捕获所有读取异常后直接使用空数组。复现：夹具中有 1 个存档和 1 个收藏服务器，接口返回 503，页面显示“No saves yet…”和“No servers added yet…”，不展示错误和明确重试入口。

影响：读失败与真实空列表无法区分，会误导用户认为存档或收藏丢失。本轮没有证明文件丢失或删除。应采用与下载/内容页一致的错误终态、重试及恢复流程。

## 验证与证据

| 检查 | 结果 | 证据 |
| --- | --- | --- |
| 真实前端端到端受控复现 | 5 类全部复现，含实际启动请求目标与代理保存 payload | `browser-audit.json`、`browser-audit-launch-proof.log`、`probe-browser.py` |
| 十页基本巡检 | 10/10 页有内容、当前视口横向溢出均为 0；不代表全部操作或外部服务通过 | 同上 `smoke` |
| 现有前端测试 | 313/313 通过、0 skip；上述跨层问题未被现有断言覆盖 | `frontend-regression.log` |
| 既有后端核心聚焦检查 | 101/101 通过、0 skip | `backend-core-regression.log` |
| HTTP 层复验环境探测 | 1 项在本地假服务器 HttpListener 初始化抛错误 6“句柄无效”，未进入业务断言 | `http-api-environment.log` |
| 现场保护 | tracked 产品 diff 为空；只新增本轮检查与交接文件，测试临时缓存单独忽略 | `input-hashes.json`、最终 git status |

101 项检查范围：游玩会话、内存分配、Java 版本/元数据选择、版本继承、加载器启动装配、配置保留和默认身份、文件恢复边界、UI 打包、更新恢复。使用既有合成文件/内存夹具；不包含真实 Minecraft 启动、微软/第三方登录、联网安装、远程同 SHA CI。

最初浏览器探针使用旧版不存在的下拉框选择器，修正探针为实际首页标题；之后控制台输出 emoji 受 GBK 编码影响，改为 UTF-8 后完整结束。原始失败日志保留。它们不是新增产品缺陷。

## 停止点与材料

当前授权是检查。本轮没有修复或提交以上问题。详细修复工作流：`../existing-features-remediation-workflow-2026-10-15.md`；可复制执行提示词：`../existing-features-execution-prompt-2026-10-15.md`。

`FormalPhaseApproval=NOT_GRANTED`；整体发布结论不在本轮范围。原生 WebView2、真实网络服务、真实游戏启动至退出、RID 发布包升级和全量后端回归仍未验证。
