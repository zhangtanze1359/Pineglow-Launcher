# 松光启动器 v1.1.0 — R5 独立验收报告

## 结论

**FAIL_REWORK_REQUIRED。**

本轮独立复验中，Node 测试、CSS token 检查和 UI 资源闭包检查通过；指定的 `.NET restore` 和 `.NET test` 均在读取用户级 NuGet 配置时被拒绝，**未进入测试执行阶段，无法取得独立测试数**。这是环境阻断，不能算产品测试失败，也不能算门槛通过。

此外，源码检查发现新增测试在无法建立链接时直接返回，测试框架可能把未执行断言的用例计为通过；R5-02 涉及的路由结构断言替换也**不能证明等价**。这两项需要返工和针对性复验。远端查询因 TLS 凭据错误失败，不能判定 integration 分支是否被推送。

## 输入版本与验收环境

- 工作目录：`D:\Projects\_worktrees\pineglow-integration-20261107`
- 分支：`integration/v1.1.0-20261107`
- 独立读取的 HEAD：`91d71ac5f8645d2e90881fea4f5f81ec442941ae`
- 起始与结束 `git status --porcelain --untracked-files=all` 均为 **0 行**，工作区干净。
- 检查时间：2026-11-07 23:07–23:08，香港时间 UTC+08:00。
- 工具版本：.NET SDK `10.0.301`，Node `v24.19.0`。
- 对照材料：[执行方合流记录](D:/Projects/松光启动器/docs/handoff/合流记录-2026-11-07.md)、[C1 判定报告](D:/Projects/松光启动器/docs/evidence/2026-11-07-c1/判定报告.md)。
- 本轮证据为本次命令的工具输出和上述既有材料；按任务要求，未将报告或新日志写盘。

## 指定命令、退出码与测试数

命令在上述工作目录执行。当前 shell 是 PowerShell；使用等价的 `$env:PATH = 'C:\Windows\System32;C:\Windows;' + $env:PATH`，并在每个原生命令后立即读取 `$LASTEXITCODE`。未使用管道最后一个命令的退出码代替被验命令退出码。

| 命令原文 | 退出码 | 独立实测结果 |
|---|---:|---|
| `git rev-parse HEAD` | 0 | `91d71ac5f8645d2e90881fea4f5f81ec442941ae`；不适用测试数 |
| `git status --porcelain --untracked-files=all`，统计输出行数 | 0 | 0 行；不适用测试数 |
| `dotnet restore dev/PineglowLauncher.Tests` | **1** | 未运行测试。读取 `C:\Users\Administrator\AppData\Roaming\NuGet\NuGet.Config` 时 `Access to the path ... is denied` |
| `dotnet test dev/PineglowLauncher.Tests -c Release` | **1** | 未进入测试阶段；隐式 restore 遇到同一配置文件访问拒绝。**测试数不可得** |
| `node scripts/run-inventory-tests.cjs` | **0** | 481 tests，pass 481，fail 0，cancelled 0，skipped 0，todo 0 |
| `node scripts/check-css-tokens.cjs` | **0** | 不适用测试数；49 个 token，检查输出 OK |
| `node scripts/check-ui-resource-closure.cjs` | **0** | 不适用测试数；扫描 13 文件、29 条本地引用，无缺失 |

因此，四项测试及检查门槛中，**Node、CSS、资源闭包通过；.NET 门槛证据不足、暂不放行**。额外的 restore 前置步骤同样未通过。

### 清单双向核对

**通过，范围限于当前清单与发现规则。** 独立枚举 `scripts/tests`、`ui/tests` 下直接包含的 `*.test.cjs`：磁盘 34 个，清单 Node 项 34 个，双向集合差异为空；清单另有 1 个 C# 项目。亲读 `run-inventory-tests.cjs` 确认其先检查清单项存在，再检查发现文件是否登记，并把全部 Node 清单项传给子进程；子进程启动错误和非零状态会向外传播。本轮该子进程完成并报告 481 项。

这证明当前规则覆盖的文件集合一致；该发现规则不递归扫描其他目录，也不证明每个测试内部的每条可能分支均已执行。

## 逐项判定

### 1. 门槛与测试清单：**不通过**

Node 清单和两项静态检查通过；`.NET` 的指定 restore/test 均 exit 1，原因是配置访问被拒，未获得测试数。执行方的 1368 项结果不能替代本轮独立结果。

### 2. 合流完整性：**通过，限提交主题与指定重放范围**

`git log --oneline 2e7abcb..HEAD` 实测 **22 个提交**；`git log --oneline 30f7b56..remediation/v1.1.0` 实测 **18 个提交**。逐条对照主题，18 个整改线独有提交均有对应重放提交：

| 整改线 | 重放后 | 整改线 | 重放后 |
|---|---|---|---|
| `d4e18d2` | `4beb862` | `8ecd563` | `51d922e` |
| `e72f4cf` | `4862ed3` | `d4abd7c` | `bc91436` |
| `4505c2b` | `448b675` | `98f2a26` | `5098e4b` |
| `7212793` | `20a6c2d` | `6fe3055` | `86ded72` |
| `e2e8200` | `e1bd3e5` | `67626e2` | `7b72d33` |
| `a994b98` | `aaf94d9` | `6db5b3b` | `dffae66` |
| `e877a30` | `075721d` | `1c1571c` | `809bb45` |
| `3ef8d68` | `812e37d` | `5bdebfa` | `5937c37` |
| `708073c` | `982a86c` | `27545a8` | `97fc3d6` |

另有 `a3da6e0`、`d1f8b93`、`c35a3d3`、`91d71ac` 四个补齐提交。**主题对应并不证明所有补丁逐行语义等价**；指定范围外的发布线独有内容仍列为遗留。

### 3. main 侧 verify 返工：**通过，限所核对文件**

`git diff --stat 2e7abcb..HEAD -- scripts .github` 输出 7 个脚本路径变化，`.github/` 无变化：

- 新增 `api-smoke.cjs`、`check-css-tokens.cjs`、`probe-external-deps.cjs`、`tests/css-token-contract.test.cjs`；
- `test-inventory.txt` 增 6 行；
- `tests/goal-validation.test.cjs` 和 `tests/ui-resource-closure.test.cjs` 有夹具调整。

因此，指定抽查的既有 `verify-mg*`、`ha01-cdp-*`、`run-inventory-tests.cjs`、`check-ui-resource-closure.cjs` 以及 `build.yml`、`release.yml` 相对基线均无 blob 差异；**`test-inventory.txt` 本身并非 blob 一致**，其变化如上并已做集合检查。此项证明文件保留，不等于实际运行所有 verify 流程。

### 4. 测试削弱检查：**不通过**

按要求执行的新增行关键词扫描命中了 `Only`、`readonly`、集合 `Skip(1)`、注释及业务名称等；这些命中本身不能证明存在测试框架 skip 或 only。删除断言扫描则确认 `AiCancelAndRedirectTests.cs` 和 `RouteReachabilityTests.cs` 有实际替换，详见下节。

另外，新增 [PathSafetyGuardTests.cs](D:/Projects/_worktrees/pineglow-integration-20261107/dev/PineglowLauncher.Tests/PathSafetyGuardTests.cs:339) 在目录链接建立失败时写“跳过”后直接 `return`，第 373、431 行附近亦有同类路径；非 Windows 句柄语义用例在第 292 行附近也直接返回。xUnit 可将正常返回计为通过，输出日志不改变测试状态。已有 [FileRecoveryEdgeCaseTests.cs](D:/Projects/_worktrees/pineglow-integration-20261107/dev/PineglowLauncher.Tests/FileRecoveryEdgeCaseTests.cs:182) 也有文件符号链接创建失败后返回的路径。**本轮 .NET 测试未运行，不能判断这些分支本次是否触发。**

未发现足以证明“所有新增或修改的测试均未放宽阈值或遗漏断言”的完整证据；本项不能以关键词扫描无有效命中代替全面证明。

### 5. 越界痕迹：**不通过，远端证据不足**

- `.github/` 相对 `2e7abcb` 无改动：通过。
- `git tag --points-at HEAD` 无输出；`git tag --merged HEAD` 输出既有 `v1.0.0`。只能证明当前 HEAD 没有本地 tag，**不能仅凭此证明期间从未创建或推送其他 tag**。
- `git ls-remote --heads origin` exit **1**：Schannel `AcquireCredentialsHandle failed: SEC_E_NO_CREDENTIALS (0x8009030e)`。远端分支是否存在**未验证**；无 upstream 也不能证明未 push。
- 抽查较大提交 `982a86c` 和 `c35a3d3` 的 `git show --stat`，并扫描整个区间的 `bin/`、`obj/`、`TestResults/` 及 `.dll/.pdb/.exe` 路径，未发现这些构建产物被纳入。提交树无法反推出执行者是否曾使用 `git add -A`，因此只能报告产物抽查结果。

## R5-02：两份测试差异复核

### `AiCancelAndRedirectTests.cs`

- **路由顺序**：原先在单一 `LocalApiService.cs` 搜索 cancel、diagnose 方法名，断言两者存在且 cancel 在前；现在改在 `LocalApiService.Routing.cs` 搜索并保留同样的存在及顺序断言。新增的 `Inventory_DiagnoseCancelPrecedesGenericDiagnose` 还用具体路径调用 `ResolveRoute`，检查两条结果与段数。对当前这组典型路由的遮蔽有保护，但源码字符串先后本身不证明全部请求都会命中正确 handler。
- **取消响应签名与方法体**：删除了内联的签名存在、左花括号存在断言和括号扫描；替换为 `LocalApiServiceSource.MethodBody`。该 helper 对签名缺失、左花括号缺失、方法体不闭合均抛异常；原有 `cancelled`、`message`、`operationId` 等方法体内容断言保留。因 helper 容许签名位于索引 0、原断言要求 `> 0`，严格文本边界并非完全相同；关键的**签名缺失不会静默通过**。
- **operationId 与 NewsService 静态检查**：原读单文件，改读固定登记的全部 partial 文件；负向字符串断言保留。缺失已登记文件会抛异常，另有 partial 文件清单守卫，但它仍是文本匹配，不能证明所有等价写法都被覆盖。
- **重定向取消**：本文件对应动态测试的断言未见本次所列差异删除；但本轮 `.NET` 未运行，不能宣称实际重定向取消行为已通过复验。

### `RouteReachabilityTests.cs`

- **删除的旧保护**：在 `HandleApiAsync` 中逐字符扫描 `if (seg.Length == N`，要求至少识别 25 个守卫，并断言不存在互斥长度嵌套；它针对旧式嵌套导致恒不可达的问题。
- **替换的新保护**：保留兜底 404 文本检查；要求方法体不含特定字符串 `if (seg.Length`、包含 `ResolveRoute`，并要求路由表非空且不少于 50 条。新增 `ApiRouteInventoryTests` 检查重复签名、handler、每条已登记路由的一个具体路径、典型实例路径及典型遮蔽。
- **判定：不等价。** 新检查对已登记路由及指定典型路径提供有用保护，但“路由表至少 50 条”不是完整预期端点清单；逐条遍历现存表也不能发现某个未被独立用例覆盖的路由被删除。每条路由仅用一个具体路径探测，不能证明所有重叠范围和遮蔽均被检出；`if (seg.Length` 也是字面匹配，不能涵盖所有等价分支写法。

**最小反证建议：**在隔离验证环境中，分别删除一条不在固定业务路径用例中的已登记路由、构造仅在某个非采样长度或参数值发生的路由遮蔽，再运行相关测试，记录是否出现红灯；同时以实际预期端点清单逐项验证登记及 handler。对取消接口增加真实请求层面的“目标 handler 命中与取消结果”反证。上述反证本轮未修改产品或测试文件执行。

## 与执行方自评不一致之处

1. **.NET 门槛**：执行方记录 restore exit 0、测试 1368/1368；本轮两个命令均 exit 1，卡在用户级 NuGet.Config 访问，未取得测试数。这是不同运行环境下的独立复验受阻，不能反推原日志虚假。
2. **UI 资源闭包引用数**：合流记录第 4.5 节表格写 **13 文件／27 条本地引用**；其引用的 [05-check-ui-resource-closure.log](D:/Projects/_archive/worktree-patches/d1-replay-2026-11-07/05-check-ui-resource-closure.log) 和本轮实测均为 **13 文件／29 条**。错误位于该行的**“测试数／输出”列中本地引用数**，文件数 13 正确。
3. **“未删测试、未改断言、未把跳过写成通过”**：相对 `2e7abcb`，上述两个文件确有断言替换；新增 `PathSafetyGuardTests.cs` 存在输出“跳过”后正常返回的分支，可被框架统计为通过。是否曾在执行方运行中触发，现有本轮证据不能确定。
4. **“无 upstream 即未推送”**：无 upstream 不能证明远端无同名分支；本轮远端查询失败，故该结论未得到独立证实。
5. **提交 `c35a3d3` 的文件数**：合流记录称“8 个冲突文件”，而其 `git show --stat` 列出 **9 个路径**。这不影响提交主题与数量的对应，但记录的文件计数不准确。

## 返工与遗留清单

- **R5-01｜返工：补齐独立 .NET 门槛证据。** 在有权读取所需 NuGet 配置、且不改变安全机制的环境中，针对同一 HEAD 原样运行指定 restore 与 test，记录各自退出码、实际通过／失败／跳过数及原始输出；若环境发生变化，记录变化。
- **R5-02｜返工：补证路由与取消保护。** 针对上述漏登记、遮蔽、签名缺失和真实取消路径做可复现反证，补齐不能被现有测试检出的缺口；不以现有路由表遍历自证完整。
- **R5-03｜返工：消除未验证用例被计为通过的歧义。** 对链接权限不足和平台不支持的分支，使用可被验收结果明确识别的失败或跳过机制，并证明报告中的计数反映实际执行情况。
- **R5-04｜待核实：远端分支与 tag。** 在具备正常远端 TLS 凭据的环境重新只读查询 heads 和相关 tags，保留命令退出码；现阶段不能判定是否 push。
- **R5-05｜遗留：发布线独有内容。** 执行方记录的 JavaProbe、Java 进程／时序测试、相应 workflow 和 v1.0.2/v1.0.3 release notes 未纳入指定的 18 个独有提交重放范围；其取舍与验证需另行处理。
- **R5-06｜遗留：版本与历史时序抖动。** 执行方记录项目版本仍为 `1.0.0`，以及更新替换测试曾有一次并发时序失败。本轮没有获得 .NET 运行结果，不能消除该历史风险；版本修改和发布均需各自授权。
- **R5-07｜文档更正：**合流记录的资源引用数应核对为 29，`c35a3d3` 路径数应核对为 9。本轮按只读要求未改文档。

## 未验证项

- **真实 .NET 测试结果及其断言实际执行情况**：NuGet 配置访问被拒，测试未启动。
- **远端 integration 分支与远端 tag 当前状态**：`git ls-remote --heads origin` 因 Schannel 凭据错误失败；没有用其他身份或方式绕过。
- **原生 GUI/WebView、真实外部服务、打包产物与发布下载**：本轮指定门槛及只读验收范围未覆盖。
- **全部提交的逐行语义等价、每个测试的全部输入和所有历史操作**：本轮完成主题对应、指定文件及关键断言抽查；Git 当前树和日志不足以证明实际是否使用过某一暂存命令或发生过所有远端操作。
- **C1 所述备份实物与哈希清单**：C1 报告记录无法定位；本轮未重新开展全盘搜寻，不能证明该备份存在或不存在。

**FormalPhaseApproval=NOT_GRANTED**

