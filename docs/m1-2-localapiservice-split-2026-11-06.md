# M1-2 交付说明：LocalApiService 按域拆分（2026-11-06）

> **性质**：阶段完成的交付说明，不是验收结论。验收需独立的复核人与可复核证据。
> **最后核对日：2026-11-06**（对照 `remediation/v1.1.0` 分支 HEAD `a994b98` 实测）。
> **范围**：`dev/PineglowLauncher/Pineglow.Launcher.Services/LocalApiService*.cs` 与三个测试文件。**未改**任何 handler 逻辑、API 契约、会话令牌校验或 Origin 白名单。

## 一、动手前的事实

| 项 | 实测值 |
|---|---|
| 拆分前单文件 | **5,487 行** |
| 顶层成员数 | **200** |
| 承载的路由项 | 72 条 |
| 承载的静态状态 | 3 套任务状态机（`DownloadTasks` / `ContentTasks` / 更新状态）+ 二分定位会话 + Microsoft 设备码会话 + 4 组测试注入缝 |

问题不在"行数不好看"，而在**改一个域的代码要在 5 千行里翻**：M1-1 已经把路由变成可枚举的表（"端点能找到"），但 handler 本体仍全在一个文件里（"实现找不到"）。

## 二、拆法

按域切为 18 个 partial 文件：

| 文件 | 行数 | 职责 |
|---|---|---|
| `LocalApiService.cs` | 494 | **宿主层**：端口/令牌/Origin 校验、listener 启停、请求入口安全前置、共用 JSON 与路径工具 |
| `LocalApiService.Routing.cs` | 215 | `Routes` / `BuildRoutes` / `ResolveRoute` / `HandleApiAsync` |
| `LocalApiService.Configuration.cs` | 416 | `LoadConfig` / `SaveConfig` / `PatchConfig` / `PatchAiProviders` / 注入缝 |
| `LocalApiService.Instances.cs` | 322 | 实例读/建/删/重新挂载、实例摘要 |
| `LocalApiService.Instances.Import.cs` | 266 | 整合包导入（含取消）、启动前自检 |
| `LocalApiService.Instances.Launch.cs` | 230 | 启动链：Java 解析、参数拼装、进程拉起 |
| `LocalApiService.Saves.cs` | 246 | 存档列举/备份/恢复/删除、皮肤上传 |
| `LocalApiService.Mods.cs` | 480 | Mod 启停/删除/替换、恢复区、资源包与光影包 |
| `LocalApiService.Bisect.cs` | 370 | 二分定位：会话操作与单步执行 |
| `LocalApiService.Bisect.Persistence.cs` | 495 | 二分定位：落盘、跨重启恢复、依赖校验 |
| `LocalApiService.Diagnostics.cs` | 554 | AI 诊断主流程与取消、诊断报告落盘/历史/导出 |
| `LocalApiService.Accounts.cs` | 415 | 离线/Microsoft 设备码/authlib 登录、皮肤 |
| `LocalApiService.Content.cs` | 513 | 内容搜索、图标代理、安装任务机、世界列表 |
| `LocalApiService.Downloads.cs` | 143 | 版本列举与下载任务 |
| `LocalApiService.News.cs` | 247 | 新闻列表、外链跳转、图片代理 |
| `LocalApiService.Update.cs` | 241 | 自更新检查/下载/状态/应用 |
| `LocalApiService.Servers.cs` | 217 | 收藏服务器、统计、Java 列表、来源切换 |
| `LocalApiService.StaticFiles.cs` | 86 | `ui/` 静态托管与路径越界校验 |

合计 5,950 行（原 5,487 + 每文件 17 行 usings/namespace/类头/尾括号的重复开销）。

### 一个刻意的例外：静态字段不跟着域走

C# 的静态字段初始化**按声明文本顺序**执行。散到多个 partial 文件后，"谁先初始化"会变成依赖**文件名排序**的隐式契约——`LocalApiService.News.cs` 排在 `LocalApiService.Routing.cs` 前面，于是 `NewsHttp` 先于 `ApiHttp` 建好，这类差异不会编译报错、不会测试失败，只会在某个代理切换的边角场景里表现出来。

所以：**所有 `static` 字段与静态初始化留在宿主文件**，域文件只放方法。这不是偷懒，是把"顺序敏感"集中到一处，让下一次动字段的人一眼看到全部约束。

## 三、怎么保证"只是搬家"

### 逐行比对

搬运脚本先按花括号配对切出 200 个顶层成员（含前导注释与属性），分配后校验"A + B + … = 全集且无重复"，再对每个文件剥离生成的 using/namespace/类头，把类体内的**非空行**按多重集与原文件比对：

```
orig body non-blank lines vs sum of 18 files' bodies
missing total: 0
extra   total: 0
```

不是"看起来一样"，是行级多重集完全相等。任何一行内容被改动或漏掉，计数都会非零。

### 没有改的东西（逐项自查）

| 约束（来自事项描述） | 是否遵守 | 说明 |
|---|---|---|
| 不改对外 API 契约 | **是** | 路由表 72 条、handler 签名、响应结构全部未动 |
| 不改会话令牌校验 | **是** | `SessionToken` / `HasValidSessionToken` / `IsValidSessionToken` 原样在宿主文件 |
| 不改 Origin 白名单 | **是** | `IsAllowedOrigin` / `ValidateOrigin` 原样在宿主文件 |
| 不顺手改行为 | **是** | 行级比对 missing/extra 均为 0 |

结构守卫测试还会持续检查最后两条：域文件里**不得**出现这几个成员的声明（只允许调用）。

## 四、代价：4 处源码扫描测试必须改

原先有 4 个测试直接读 `LocalApiService.cs` 找子串。拆分后该路径不存在，会直接 `FileNotFoundException`——**这是拆分的真实成本，不能假装没有**。

新增 `dev/PineglowLauncher.Tests/LocalApiServiceSource.cs` 统一读法：

- `All()` —— 按**固定域顺序**（显式写死的 18 个文件名，不用 `Directory.GetFiles` 的文件系统顺序）拼接全部 partial 内容；
- `Single(name)` —— 读指定域文件，未登记的名字直接抛；
- `MethodBody(src, signature)` —— 花括号配对截方法体，找不到或未闭合都抛（而不是返回空串，空串会让后续索引断言静默通过）。

4 处断言全部改用这套读法，**断言强度一条没降**：

| 测试 | 原做法 | 现做法 | 强度变化 |
|---|---|---|---|
| `RED1_CancelRoutePath_MustComeBeforeDiagnoseRoute` | 在整个文件里比两个方法名的首次出现位置 | 在**路由表文件内**比两个路由登记的先后 | **变强**。原做法在拆分后变得无意义（方法体去了诊断文件，比跨文件位置等于比文件名），现做法直接对准"M1-1 起路由顺序就是表内顺序"这个事实 |
| `RED2_ServerDoesNotGenerateOperationId` | 全文搜 `operationId = Guid.NewGuid()` | `All()` 全文搜 | 等价 |
| `RED3_CancelEndpoint_ReturnsCancelContract` | 全文搜方法签名 → 花括号配对 | `Single(诊断文件)` + `MethodBody` | 等价（顺带把手工花括号配对换成会抛异常的版本） |
| `RED4_NewsService_MustNotBeWiredToAiHttp` | 全文搜 `(() => AiHttp)` | `All()` 全文搜 | 等价 |

## 五、新增的结构门槛

`dev/PineglowLauncher.Tests/LocalApiServiceSplitTests.cs`（4 个用例）：

| 门槛 | 内容 | 为什么 |
|---|---|---|
| 单文件 ≤ 600 行 | 遍历 18 个域文件 | 2026-10-03 的教训是"没有机器门槛的东西会退回去"。上限取现状最大值（554）× ~1.1，让门槛**当下就通过**——一条一直红的门槛等于没有门槛 |
| 目录内不得有未登记的 partial | 磁盘 vs `ExpectedFiles` 双向比对 | 否则 `All()` 会漏读未登记文件，"读源码断言的测试"变成"漏读即通过" |
| 令牌/Origin 只允许一份实现 | 域文件里不得出现这 5 个成员的**声明**（允许调用） | 复制一份安全逻辑 = 两套口径迟早分叉 |
| 门槛自身不得空洞 | 上限 ≤ 800、域清单 ≥ 10 项、路由文件必须存在 | 防止有人把参数调到 100000 让门槛恒真 |

## 六、验证结果

环境：Windows 10.0.26200 / .NET 10.0.301 / Node 22.22.2。

| 门槛 | 结果 | 备注 |
|---|---|---|
| C# 全量 | **982 / 982 通过，0 跳过** | 原 978 + 新增 4；耗时 1m5s |
| 受影响三类聚焦 | **34 / 34 通过** | `LocalApiServiceSplitTests` + `AiCancelAndRedirectTests` + `RouteReachabilityTests` |
| 前端清单 | **385 / 385 通过，0 跳过** | `node scripts/run-inventory-tests.cjs` |
| CSS token 检查器 | **exit 0** | 49 token，无字面色值、无未定义引用、无死回退 |
| 资源闭包 | **exit 0** | UI 引用全部可解析 |
| 编译告警 | **0 错误 0 警告**（主项目） | 测试项目仍有既有的 nullable/xUnit analyzer 告警，与本次无关 |

### 一个必须记录的本地环境陷阱

首次全量跑出 `UpdateApplyScriptPathTests.ApplyScript_WaitsForRunningTargetThenReplaces` 失败（`replace_failed`, waited=0.4s）。

**不是回归**，也不是本次改动的后果：替换脚本用 `tasklist | find`，在 Git Bash 下 `/usr/bin` 排在 PATH 前面，`find` 解析到 GNU find 而非 `C:\Windows\System32\find.exe`。把 `System32` 前置后 **982/982 全通过**。M0-3 已记录同一陷阱，本次复现印证。

## 七、未完成 / 边界（如实记录）

| 项 | 状态 | 原因 |
|---|---|---|
| 推送分支 | **未做** | 推送属对外动作，需明确授权；且 M0-4 记录过 `schannel` 网络中断 |
| Ubuntu/macOS runner 复测 | **未做** | 依赖推送 |
| 文件更细粒度拆分（如把诊断 554 行再分） | **未做** | 当前 554 行已在门槛内；继续拆只会增加跳转成本 |
| 域间解耦（引入接口 / DI 替换静态跨域调用） | **未做** | 属独立工作量，不在 v1.1.0 范围（已写入 `engineering.md` 改进点） |

**这项可以标"完成"**，因为事项定义的完成标志（按域拆分 + 逐批回归 + 端点清单与可达性测试全绿 + 文件清单与职责边界）全部达成，且证据可复核。但**发布门槛**仍未满足：CI 全绿需要推送后由 runner 实跑确认。

## 八、给下一个人的接入点

- 新增端点 → `LocalApiService.Routing.cs` 的 `BuildRoutes()` 加一行 + 对应域文件加 handler。
- 新增域 → 建 `LocalApiService.<Domain>.cs`，**同时**登记到 `LocalApiServiceSource.ExpectedFiles`，否则结构守卫测试会红。
- 动静态字段 → 只在 `LocalApiService.cs` 里动；动完检查字段之间的依赖顺序。
- 想跳过某条测试 → 先读 `RouteReachabilityTests` 的注释，那里记着 2026-10-03 的 10 条恒 404 事故。
