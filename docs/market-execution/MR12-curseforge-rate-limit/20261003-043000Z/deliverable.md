# MR12 · CurseForge 限流闭环 · 交付报告

- **run-id**：`20261003-043000Z`
- **执行范围**：CurseForge 请求策略（Retry-After、退避抖动、失败分类、单一出口并发上限）+ 三处调用点接线 + 测试
- **依据**：`docs/market-task-plan-2026-10-03.md` §10、`market-research-task-workflow-2026-10-02.md` §10
- **最终状态**：**证据就绪**（合成 429/Retry-After 驱动；真实限流未复现，见 §六）
- **未执行**：MR13 及其它任务包；不提交、不推送、不发布

---

## 一、结论先行

| 项 | 结果 |
|---|---|
| 复现缺口 | ✅ CF 三处请求（搜索 / 文件列表 / 下载地址）**全都只调用 `EnsureSuccessStatusCode()`**：429 被当普通失败抛出，不读 Retry-After、不退避、无法区分限流与 Key 无效 |
| 遵守 Retry-After | ✅ 秒数与 HTTP 日期两种形式都支持；`Retry-After` 比退避短时仍给退避，避免立刻再撞 |
| 退避 + 抖动 | ✅ 指数退避 400→800→1600ms（`MaxDelay` 10s 封顶），抖动把等待缩放到 [50%,100%]，避免整点同时重试 |
| 失败分类 | ✅ `RateLimited / Unauthorized / NotFound / ServerError / NetworkError`，401/403/404/500 **不重试**（重试只浪费配额） |
| 可执行提示 | ✅ 分类提示**并入 `ex.Message`**：路由把 `ex.Message` 直接回给界面，只写"已尝试 4 次"用户不知道怎么办 |
| 单一出口 + 并发上限 | ✅ 所有 CF 请求走同一策略，并发上限 **2**（不绕过官方 API 限制） |
| 不伪造成功 | ✅ 限流耗尽时抛异常，不返回空结果冒充"没有搜到" |
| C# 测试 | ✅ 新增 10 个用例；RED 基线 **9 失败 / 1 通过** → 实现后 **10 通过 / 0 失败** |
| C# 全量回归 | ✅ **718 通过 / 0 失败 / 0 跳过**（708 + 10） |
| Node 全量回归 | ✅ **216 通过 / 0 失败**（16 个文件，未变） |

---

## 二、问题

改动前 `ModContentService` 里 CF 的三处请求长这样：

```csharp
using HttpResponseMessage response = await _http.SendAsync(message, ct);
response.EnsureSuccessStatusCode();
```

后果：

1. **不读 `Retry-After`**：服务端明确说了"等 N 秒再来"，客户端当没看见，短时间连续请求会持续撞限流；
2. **不退避**：429 之后立刻重试或立刻放弃；
3. **分不清原因**：`EnsureSuccessStatusCode` 抛出的 `HttpRequestException` 无法区分
   "限流（稍后重试就能好）" / "Key 无效（要去设置里改）" / "真的没有这个项目（换个关键词）"，
   界面只能笼统报"失败"；
4. **没有并发上限**：多个搜索/安装并发时可能同时打出多条 CF 请求。

---

## 三、实际改动

| 文件 | 状态 | SHA256 |
|---|---|---|
| `dev/PineglowLauncher/Pineglow.Launcher.Services/CurseForgeRequestPolicy.cs` | **新增** | `2F2F64F3A54A58CA5B484F11ABA01875F92CB397A7441BE477C57E7241CAD822` |
| `dev/PineglowLauncher/Pineglow.Launcher.Services/ModContentService.cs` | 修改（本包前为干净） | `D39A5A2102049795940602D2071DAFFBF4C8A1F3DE3317705201ADC860FB55CC` |
| `dev/PineglowLauncher.Tests/CurseForgeRateLimitTests.cs` | **新增** | `30E8D12725523381A129A4BAC461333E098AD2C1CBDD7A4619A9D043C0ECD9F7` |
| `scripts/source-snapshot-inputs.txt` | 修改（未跟踪） | `75018421D77A0D2869F7924C103C3BD1C342E7C2593C0687E83899D9163A5087` |

`git status` 1317 → 1319（+2：1 个新增源码 + 1 个新增测试；`ModContentService.cs` 由干净变为 `M`）。

### 3.1 `CurseForgeRequestPolicy`

- **必须传请求工厂而不是 `HttpRequestMessage`**：.NET 不允许同一条请求被发送两次，重试必须重建。
- 重试条件：**429 / 503** 与**网络异常**；其余（401/403/404/5xx-其它）立即分类抛出，不重试。
- `MaxAttempts = 4`，`MaxConcurrentRequests = 2`，`BaseDelay = 400ms`，`MaxDelay = 10s`。
- 测试缝：`DelayForTest`（接管等待，测试才能秒级跑完）、`JitterForTest`（固定抖动使退避可断言）。

### 3.2 接线

三处调用点改为：

```csharp
using HttpResponseMessage response = await CurseForgeRequestPolicy.SendAsync(
    _http, () => CreateCurseForgeRequest(HttpMethod.Get, url, apiKey), ct);
```

### 3.3 关于 UI：本包未新增 UI 代码（说明理由）

计划的允许范围含 `ui/js/*`。但可执行提示已经**并入异常消息**，而路由把 `ex.Message` 直接回给界面
（与其它接口一致），所以"稍后重试 / 检查 Key / 换个关键词"会照常显示给用户——
**不需要为改而改 UI**。**未新增**的是"稍后重试"按钮这类专门交互（见 §六）。

---

## 四、RED → GREEN

**RED 构造方式**：`CurseForgeRequestPolicy` 是本包新增的类型，直接把文件删掉只会得到**编译错误**（证明不了行为）。
所以 RED 基线 = 保留类型与辅助函数，**只把 `SendAsync` 换回 MR12 之前的单次 `EnsureSuccessStatusCode()`**。
这样"通过的用例"恰好是纯辅助函数用例，不夸大缺口。

**RED：9 失败 / 1 通过**（`red/01-cf-before.log`）

唯一通过的是 `Backoff_IsBoundedAndJittered`——它测的是纯函数 `Backoff`，与限流行为无关，如实记录。

**GREEN**

| 检查 | 结果 | 日志 |
|---|---|---|
| `--filter …CurseForgeRateLimitTests` | **10 通过 / 0 失败** | `green/02-cf-after.log` |
| `dotnet test`（全量） | **718 通过 / 0 失败 / 0 跳过**（58 s） | `green/03-csharp-full.log` |
| `node --test`（16 个 `.test.cjs`） | **216 通过 / 0 失败** | `green/04-node-full.log` |

关键实测数据（都在日志里）：
- 429 + `Retry-After: 3` → 等 **3s** 后重试并成功（`calls=2`）；
- 429 + HTTP 日期 → 解析出 **3.04s** 等待；
- 429 无 Retry-After → 退避 **400ms / 800ms / 1600ms** 严格递增，4 次后抛 `RateLimited`；
- 403 → **只调用 1 次**，`Unauthorized`，提示"检查 Key"；
- 404 → **只调用 1 次**，`NotFound`，提示**不含**"稍后重试"（不把"没有结果"说成"限流"）；
- 瞬时网络错误 → 重试一次后成功；
- 6 条并发请求 → 实测最大并发 **2**（等于上限）。

---

## 五、本轮自身发现并修正的问题

1. **可执行提示没送到用户手里**：异常消息原本只有"CurseForge 持续限流（已尝试 4 次）。"，
   而 `Hint`（"请在约 7 秒后重试"）是**独立属性**，路由只回 `ex.Message` —— 用户看不到该怎么办。
   已把提示并入消息（`Hint` 改为复用同一个 `HintFor`），并加断言锁住"消息里必须有可执行提示"。
2. **测试断言措辞写错两次**：先按"稍后重试"字面断言，而带 Retry-After 的分支实际是"请在约 N 秒后重试"。
   已改为断言语义关键词（"过于频繁" + "重试" + 秒数）。
3. **测试必须固定抖动**：不固定时退避是随机区间，`400ms → 800ms` 的区间可能重叠（400 与 400 相等），
   断言会偶发失败。已用 `JitterForTest = 1.0` 让退避可精确断言。

---

## 六、明确未做与未验证

| 项 | 说明 |
|---|---|
| 🔴 **真实 CurseForge 限流** | **未复现**。需要真实 API Key 与配额；计划本身也写明"真实限流可能无法在短时间复现，用合成即可"。本包全部用合成响应驱动。 |
| 请求合并 / 缓存 | 计划提到"缓存/合并重复请求"。本包做了**并发上限**与**统一出口**，但**没有做**同 URL 在途请求的去重合并，也没有做结果缓存。 |
| 专门的"稍后重试"按钮 | 未新增。提示经既有错误通道显示；一键重试的交互未做。 |
| 搜索分页游标 | 计划提到"搜索分页游标"。本包未改动分页逻辑。 |
| 图标/更新路径的限流 | `ModContentService` 的三处 CF 请求已全部接线；但**图标获取**（`ContentIconService`）与其它可能的 CF 出口本包未逐一审计。 |
| 真实 WebView2 呈现 | 未验证。 |

---

## 七、回退方法

```powershell
$run = 'C:\Users\Administrator\.codex\worktrees\ed41\松光启动器\docs\market-execution\MR12-curseforge-rate-limit\20261003-043000Z'
Copy-Item "$run\green\CurseForgeRequestPolicy.cs.mr12" `
  'D:\松光启动器\dev\PineglowLauncher\Pineglow.Launcher.Services\CurseForgeRequestPolicy.cs' -Force
(Get-Item 'D:\松光启动器\dev\PineglowLauncher\Pineglow.Launcher.Services\CurseForgeRequestPolicy.cs').LastWriteTime = Get-Date
```

其余：`ModContentService.cs` 把三处 `CurseForgeRequestPolicy.SendAsync(...)` 还原成
`CreateCurseForgeRequest` + `_http.SendAsync` + `EnsureSuccessStatusCode()`；删除新增测试；
白名单去掉类别 A/B 的新增行并把计数 22→21、28→27。

---

## 八、证据清单

```
red/01-cf-before.log                     RED 基线 9 失败 / 1 通过
green/
  CurseForgeRequestPolicy.cs.mr12        MR12 实现版备份（SHA 038FD3EB…）
  CurseForgeRequestPolicy.cs.mr12.sha256.txt
  01-cf-first.log                        首轮（1 处断言措辞写错）
  02-cf-after.log                        10 通过 / 0 失败
  03-csharp-full.log                     718 通过 / 0 失败
  04-node-full.log                       216 通过 / 0 失败
  git-status-after.txt                   1319 行
  changed-files.sha256.txt
manifest.json
```

**本报告不宣布阶段放行、不提交、不发布。**
