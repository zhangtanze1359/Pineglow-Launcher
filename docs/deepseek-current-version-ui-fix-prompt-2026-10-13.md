# Harness 第二批：CV-04 至 CV-07 UI 与验证工具修复

用户已授权完整修复当前版本，并明确UI由OpenDesign Cloud、实现由DeepSeek Harness、Codex独立验收；全部必需项通过后由Codex提交，执行者不得提交。当前SourceRoot/cwd：D:\松光启动器。

OpenDesign Cloud 已成功生成局部原型；完整源码在 `docs/opendesign-ui-artifact-2026-10-13/`，阅读 integration-notes.md，然后仅读取 CSS 的状态组件部分。原型不是生产壳，不读取其中假版本、假账号、假检查结果或mock逻辑作为真实数据。保持原ui/css/pineglow.css色板、导航、API契约。新闻实际字段为meta.validatedAt，不是原型说明里的verified_at。

第一批已实现CV-01–03；Codex新冻结快照 PASS：302唯一输入，Node305/C#941、冷启动19，Windows core.autocrlf=true的隔离检出冻结JSON哈希仍D9F55189...45AF。不要重写前三项修复或冻结数据。你上轮报告“仓库config无autocrlf等效false”不准确；真实git config --show-origin输出Program Files/Git/etc/gitconfig=true，D盘已有LF是当前磁盘事实，不等于有效配置false。当前PS脚本BOM已EF-BB-BF，PS5.1解析0错误；不得再次丢失BOM。

## 允许路径

- ui/js/launcher.js：仅下载中心、新闻时间和内容错误/重试状态及必要局部helper。
- ui/js/i18n.js、ui/css/pineglow.css：相关双语文案和Cloud状态组件的局部样式，沿用现有token；禁止拷贝完整pg-app/pg-nav原型壳、模拟数据、状态控制面板或新版本号。
- scripts/verify-ui-pages-live.cjs：CV-07 CDP错误处理；明确终态判定、错误分类、报告URL敏感值脱敏。
- 为launcher.js这一文件新增聚焦测试 ui/tests/current-version-errors.test.cjs；为验证脚本这一文件新增聚焦测试 scripts/tests/ui-live-cdp.test.cjs。也可扩展已有对应测试，避免重复测试。
- scripts/test-inventory.txt 登记实际新增Node用例文件；scripts/source-snapshot-inputs.txt 精确加入新增测试文件和根目录.gitattributes，保留所有已有条目和BOM，不加入docs证据或通配。
- docs/current-version-audit-2026-10-13/harness-ui-fix-report.md；本轮拥有的原始RED/GREEN日志和聚焦证据。

不允许改C#、依赖/lock、CI、冻结案例、其他功能、真实用户配置、原始失败、旧会话。禁止stage/commit/push/release/restore/clean/reset/stash或删除他人资产。

## 必需行为

CV-04：getRemoteVersions异常不能转为成功空列表，显示双语易懂错误和真实重试；成功[]才是空态。下载任务独立显示，版本失败仍保留已有任务，任务接口失败不能假装无任务。重试按钮进行中禁用，重复点击不产生并发；恢复后移除错误，保留版本筛选。页面切换/旧请求结束不能覆盖较新状态。

CV-05：null、空、无效日期、0001-01-01（含时区）都为未验证；不显示1-01-01，也不把无效原串直接放到日期。有效验证/缓存时间保持，失败/缓存/成功空态仍区分。保持meta.validatedAt契约。

CV-06：依Cloud局部组件修复content-error/content-retry-btn；主说明通俗，技术错误进入原生details/pre，可展开且默认不铺开。动态文本HTML转义，技术详情长文本换行；键盘焦点可见，960px窗口无横向溢出。重试保留内容源、类别、查询、游戏版本筛选；失败后再次重试仍可用。保留现有取消/事务和launch前检查行为，不增加原型mock按钮。

CV-07：connectCdp pending每个请求保存method并在错误响应正确reject；不能引用未定义变量，不能同步ReferenceError/永久挂起。未知或重复响应安全处理。真实巡检必须区分：正常有内容、成功空态、已落地错误状态、仍加载；最小字符数不够不能一概叫加载。未知本机500继续阻断；仅明确响应正文证明的上游网络错误可列外部依赖，不能把所有500放行。读取错误响应时先用完整正文做判断再截断展示；不要吞错误或改成mock通过。保存报告前脱敏token/Cookie/Bearer等；不得重新发送URL中的token后把它写入归档。

## 测试与工具

两组都是指定文件的聚焦回归测试，沿用node:test与现有VM/DOM夹具，不新建全项目测试架构。先读 `C:\Users\Administrator\.agents\skills\code-testing-agent\SKILL.md` 的focused流程和一份邻近测试。使用生产入口而非在测试里复制产品逻辑。

保留RED：independent-ui-red.json的CV04/05反例、baseline-ui-shots内容失败截图、cdp-error-red.log。你的新回归测试要覆盖失败→重试仍失败→成功、成功空数组、失败时仍有已有任务、未验证日期与有效缓存日期、转义与详情默认折叠、CDP error真正reject并含请求方法、未知500仍FAIL；提供Requirement | TestName表。

pwsh此前0xC0000142，read/edit/write可用。不要花几十步反复做编码探针；已有.ps1/.txt BOM要按字节保留。可以请求Codex执行你写出的精确检查，但未执行的一律NOT_RUN；没有真实日志不得宣称GREEN。若shell恢复可用，先窄测试再总结，不重复跑全套，Codex负责最终冻结全量与真实WebView。

报告必须含改动、根因、设计源码出处、每项真实验证/未验证及残余风险。结束语：具备独立复验条件，等待 Codex 独立验收。
