# 当前版本缺陷修复与独立验收工作流

日期：2026-10-13，Asia/Hong_Kong。

## 目标与授权

以 `D:\松光启动器` 为产品源，检查当前版本、完整修复本轮发现的缺陷。UI 由 OpenDesign Cloud 设计，实现先由 DeepSeek Harness 承担；其剩余任务反复超时后，用户明确授权 Codex 定向返修并由独立子代理验收。用户已明确：全部必需门槛通过后直接提交全部已审查改动；不包含推送、发布、部署或线上更新。

基线 HEAD：`1829f1c106bb32c7c894eca8f288f1d24e79447b`，分支 `main`。原有 2611 项未跟踪材料主要是历史 docs 证据，不应仅因“全部改动”批量纳入提交。逐项核对来源和敏感内容后，以明确路径提交本轮产品修复、必要测试、工作流和报告；历史证据保持原位。

## 角色和架构约束

- Codex：需求范围、根因核实、架构决策、独立验证、最终提交清单。
- 追加授权：Codex 实施 CV-04 真实 API 吞错、CV-07 中文错误分类与脱敏、CV-08 启动失败保护及对应夹具/回归；新独立子代理只读复查与隔离全量验收，实施者不能自批准。
- OpenDesign Cloud：对已证实 UI 缺陷提供设计、交互状态和适配规范，保留松光启动器现有页面和视觉风格，不扩张成无关重设计。
- DeepSeek Harness：按精确任务包实现和自检；终态只能是“具备独立复验条件，等待 Codex 独立验收”，不可自批准、提交或发布。
- 所有运行使用隔离副本、portable 数据和本轮自有进程。禁止覆盖用户配置、历史 RED、旧会话或其他执行者资产。

## 已复现问题和根因

### CV-01：tracked 和白名单重叠使源码快照误失败

`scripts/verify-source-snapshot.ps1` 将 trackedCopied 与 whitelistCopied 直接相加。302 项 tracked 已包含全部 163 项白名单，磁盘实际只有 302 个唯一文件，却期望 465，导致完整候选被判 FAIL。按实际被纳入的规范相对路径集合去重计数；不得取消数量、泄漏、缺失或 SHA 检查，也不得删除白名单。

### CV-02：冻结 JSON 受 core.autocrlf 影响

聊天 worktree 中 `diagnostic-cases.json` 原字节哈希为 `133E610B...`，仅将 CRLF 转为 LF 后恢复原冻结哈希 `D9F55189FA05845BAC0403A058AA63912A725AFF61555BDF25A6865DD5AB45AF`。302 个 tracked 输入与 D 盘比较，283 个只有换行差异，内容差异为 0。不是案例内容变更，禁止重写冻结案例或更新冻结哈希。优先使用精确 `.gitattributes` 固定此冻结数据为 LF，独立验证 Git 检出后的字节哈希。

### CV-03：整合包缺失字段测试依赖源码 LF

`InstancePackTests.ParseModrinthIndex_DefaultsMissingFormatVersionToOne` 用 `Replace("  \"formatVersion\": 1,\n", "")` 删除字段。在 CRLF C# 原始字符串中没有实际删除，失败发生在调用产品解析器前。测试夹具应以结构化 JSON 或无需换行的匹配构造缺失字段；保留字段实际缺失及解析结果为 1 的断言。

证据：`C:\Users\Administrator\.codex\worktrees\0cd9\松光启动器\docs\current-version-audit-2026-10-13\baseline-gate`。Node 305/305；C# 939/941，两个失败均为上述夹具/检出问题。单文件冷启动 19/19；非 RID 锁定还原、构建和普通 publish 通过。RID 锁定还原仍 NU1004，需准确列为未锁定构建边界，不能称全链 locked。

## 执行顺序与验收门槛

1. 保存 D 盘基线、路径清单和哈希，读取现有源码和旧证据；原始失败不可覆盖。
2. DeepSeek Harness 修复 CV-01 至 CV-03，最小范围，不修改冻结案例内容/哈希、依赖版本、发布工作流或无关产品行为。
3. Codex 聚焦验证：真实集合去重且缺文件仍失败；Windows autocrlf 检出后冻结哈希不变；LF/CRLF 下缺失字段测试都真实调用解析器并通过。
4. 独立新快照执行 `scripts/verify-source-snapshot.ps1`；核实 Node/C# 的实际执行数、0 skip、退出码传播、输入前后不漂移、当前产物哈希和冷启动结果。
5. 对本轮构建运行真实 WebView 九页巡检，读取截图、异常、资源失败与外部依赖状态。必要 UI 缺陷交给 OpenDesign Cloud，设计确认后由 Harness 实现，再重验。
6. 对启动装配、旧 UI 升级、下载恢复和关键文件事务按风险执行既有聚焦门槛。必需项缺证据写 LIMITED/NOT_RUN，不以自动化冒充真实游戏/账号验收。
7. 汇总技术结论与提交清单。全部必需项 PASS 后按用户授权提交已审查路径；有 FAIL 或必需 LIMITED 则先修复/补证，不提交“已通过”版本。

## 当前边界

本轮最终技术结论为 PASS_SCOPED_CV01_CV08，详见最终验收和独立子代理报告。OpenDesign Cloud 设计及产品集成已完成；真实微软登录、第三方 AI、进入游戏世界、远程同 SHA CI 和发布均无本轮放行。默认在线九页仍为原始FAIL（6/9），默认在线包源还原LIMITED，不能由本地受控技术通过推导线上全通。

## 2026-10-13 上午续推进

用户已确认多个关联流程，并明确授权重建失效需求卡。OpenDesign Cloud 本次设计已成功，完整4文件原型保存为docs/opendesign-ui-artifact-2026-10-13；原型层7项布局、键盘和重试检查通过。

CV-01–03已由Harness实现，新冻结快照PASS（302唯一输入、Node305/C#941/冷启动19）；core.autocrlf=true的隔离检出冻结哈希不变，CRLF源码下整合包聚焦测试1/1通过。缺失分支使用真实生产收集逻辑的只读模拟检查4/4通过；实际删除隔离副本文件的命令被工具policy拒绝，未执行。

第二批UI与工具代码已落地，但未获验收；聚焦测试13 PASS/2 FAIL（正则夹具语法、token脱敏）。独立复查还发现CV04的真实api.js层吞错、CV07不识别.NET转义中文/Windows中文DNS错误，以及新增CV08启动检查失败仍继续调用launchInstance。

CV08关闭标准：检查异常/null/明确errors均不调用启动接口；显示未完成检查的失败提示和真实重试入口；重试成功后保留既有警告确认行为，并防止连点重复检查/启动。返修范围增加ui/js/api.js的兼容可选参数throwOnRemoteError、真实API链测试、正文JSON解析和结构化字段脱敏；精确任务见deepseek-current-version-boundary-rework-2026-10-13.md。

最后需冻结最终源码，执行全量、真实WebView受控失败/恢复与正常九页巡检，再按已授权提交。当前没有提交或发布批准结果。

## 最终执行结果

用户明确批准 Codex 定向修复、独立子代理验收后，八项修复全部技术通过。最终305源码输入漂移0；Node347/347、C#941/941、冷启动19/19、Native受控HTTP13/13，零测试跳过。默认在线源等待保留LIMITED；完整缓存源非RID锁定构建及普通单文件publish通过，缓存42包规范化hash与锁一致、签名完整性通过。默认在线九页保留FAIL、6/9成功和三外部依赖错误终态，无未知本机接口或UI资源失败。RID锁定publish仍NU1004既有边界。

独立报告independent-agent-review.md给出PASS_SCOPED_CV01_CV08；最终final-acceptance.md列明所有证据和未验证项。按commit-scope.json精确路径本地提交，不推送/发布/部署；历史未跟踪文件不纳入。提交号以git log -1为准。
