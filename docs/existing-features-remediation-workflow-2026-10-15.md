# 已有功能缺陷修复工作流

日期：2026-10-15，Asia/Hong_Kong。来源：`feature-audit-2026-10-15/README.md` 和 `browser-audit.json`。本文件是检查后的交接材料，不授权修改、提交或发布。

## 基线与范围

SourceRoot: `D:/松光启动器`；检查基线 HEAD: `1356efa70cfd91f6289ee6e0dc6af5160b444ab3`；本轮产品 diff 为空。执行前重新读取 status、HEAD、相关 diff 和本轮报告，保留所有既有未跟踪材料及失败证据。

允许的后续修复路径（仅在用户明确授权执行后）：`ui/js/api.js`、`ui/js/launcher.js`、`ui/js/i18n.js`、必要的 `ui/css/figma.css` 或 `ui/css/redesign.css`、对应 `ui/tests/`，以及本任务文档。后端默认只读；只有证据证明当前响应契约不满足要求，才定位最小后端修复并明确记录。不改账号凭据策略、依赖版本、发布工作流、更新选版、默认皮肤实现或无关页面设计。

EvidenceRoot: 每次执行在 `docs/feature-audit-2026-10-15/` 下新建独立运行目录，不覆盖当前 RED。ExecutionRoot 使用隔离本机服务/portable 数据和本轮构建输出，不能使用用户的真实配置、账号或游戏目录。

## 包 A：配置状态透传与实例选择（FA-01、FA-02，最高优先级）

1. 使用真实 `api.js` 和服务器原始 JSON 编写 RED：两个实例，服务端选第二个；代理/下载源/新闻兼容/快捷行为均设置非默认值。断言归一化后的字段准确，false 和 0 不被错误真值化；机密只保留 configured 标志。
2. 浏览器复现第一项：当前保存 B，首页却显示 A，点击开始实际请求 `/api/instances/inst-a/launch`。保留此 RED。
3. 修复 `live.getState()` DTO 映射。明确保留 `selectedInstanceId`、`selectedAccountId`、`downloadSource`、`proxyEnabled/Host/Port/Username`、`proxyPasswordConfigured`、`newsFakeIpCompat`、`minimizeOnLaunch`、`autoCloseOnExit`，以及已有其他配置。核对 `curseForgeApiKeyConfigured` 从服务端布尔标志读取，不读取不存在的秘密字段。
4. 页面读取已配置值；未改动保存不得关闭代理或清空已有 host/port。proxy 密码保持不回显，空输入语义沿用后端“保持旧密码”，不要为修复而回传或落盘明文凭据。
5. 补浏览器 GREEN：B 为首页当前实例且启动请求目标为 B；代理/快捷行为/新闻兼容在重新加载和语言切换后仍与服务端一致。实例选择手动切换的持久化策略必须与既有后端契约核对，不擅自新增隐式文件操作。

## 包 B：下载源列表作用域错误（FA-03）

1. 使用真实事件接线并返回至少两个可用源，RED 必须捕捉 `dlSource is not defined`，不得通过返回空列表逃避执行。
2. 在绑定函数内从当前配置或 `/api/sources.selected` 取得当前源；不要引用 `renderSettings()` 的局部变量。
3. 验证成功列表、选中标记、点击切换、刷新后保留、获取失败提示和重试；fake 源测速不宣称真实外网通过。

## 包 C：AI 开关保存状态（FA-04）

1. RED 覆盖保存成功但缓存不更新、语言重渲染回退、保存 503 仍显示成功三种行为。
2. 保存期间设置 busy/disabled 去重；成功同步服务端结果到缓存和控件；失败恢复原 class/ARIA/cache 并反馈明确错误。不要静默 catch。
3. GREEN 覆盖开启/关闭、连续点击、成功重渲染、失败回滚及重试，继续使用真实 API 适配器。

## 包 D：存档/服务器读取错误（FA-05）

1. RED 夹具必须先有数据，再注入 503，断言不能出现表示真实空集合的文案。
2. 将 loading/ready/empty/error 分开，并提供真实可执行重试。失败时不能把空数组当已成功加载的数据，不清空真实数据；是否显示旧缓存需要标记陈旧状态。
3. GREEN 验证首次失败→重试成功、真实空列表、先成功后失败和导航期间迟到响应。中英文案完整，错误文本转义，长文本不溢出。

## 验证与交付门槛

- 保存 RED→最小修复→GREEN 的源码哈希、命令、cwd、时间/时区、退出码和实际测试数；0 测试或中止不是通过。
- 运行真实 API 适配器的新增聚焦检查，然后 `node --test ui/tests/*.test.cjs`；相关后端检查使用既有内存凭据与隔离 TEMP/TMP。
- 本轮原始前端 313/313、后端核心 101/101 是基线；测试绿不能替代上述跨层反例。
- Release 构建和本轮构建输出的真实浏览器复验至少覆盖十页与五类问题。产品是 Windows WebView2，发布前原生复验须单独记录。
- 本机 HttpListener 错误 6 是已复现的环境/运行组合阻断，不能伪装为产品业务 FAIL 或用 mock 结果称真实 HTTP 已通过。沿用已有运行时问题工作流，在允许范围内定位；不修改系统安全设置、不停止用途不明的用户进程。
- 完成后交付变更清单、证据与精确结论；终态为“具备独立复验条件，等待 Codex 独立验收”。本文件不授权提交、推送、打标签、发布或进入其他阶段。
