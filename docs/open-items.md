# 遗留开放项

> 这张表回答一个问题：**现在还有什么没解决。**
> 只登记**确实还开着**的项；解决后从表里删掉，不是标成"已完成"留着。
>
> 每条必须能指回一个能复现的证据文件或命令。写"应该没问题"的一律不接受。

## 一、会咬发布门槛的

| ID | 项 | 判定 | 证据 / 复现 | 影响 |
|---|---|---|---|---|
| **OPEN-01** | `ContentIconServiceTests.GetAsync_FailedFetchAllowsRetry` 随机失败 | **进行中（另有未提交修复）** | 证据见下；**修复代码在 `D:/Projects/_worktrees/pineglow-icon-race`（分支 `fix/icon-inflight-race`）的工作区里，未提交** | C# 门槛不能声称"稳定全绿"；每次验收都要连跑两次才敢下结论 |

还没合入、**别重复做**：`D:/Projects/_worktrees/pineglow-icon-race` 的工作区里已经有一版修复（未提交）：

- 取表时跳过已完成任务：`if (_inFlight.TryGetValue(cacheKey, out existing) && !existing.IsCompleted)`
- 建任务后同步判 `task.IsCompleted`，已完成的当场不进表
- 注销改成 `RemoveIfSame` + `ExecuteSynchronously`，防旧任务删掉新条目
- 新增 `InFlightCountForTest` 与两个不变量用例（正向：失败后必须能重试；反向：在途不得被误注销）

那份工作区的证据目录 `docs/evidence/2026-11-17-icon-inflight-race/` 里记的频率对比是：修复前 30 次 13 红，修复后 40 次 0 红。
**但那些数字在被提交与独立复跑之前只是一份自评**——按 `AGENTS.md` 的验证规矩，下一个接手的人需要：

1. 在同一份代码上自己连跑 40 次 `--filter FullyQualifiedName~ContentIconServiceTests.GetAsync_`
2. 确认 `GetAsync_InFlightEntrySurvives_UntilTheDownloadActuallyCompletes` 能咬住（把 `!existing.IsCompleted` 去掉应当变红）

原定位（作为背景保留）：

> 该用例第一次请求返回 502（记 `UpstreamError`），第二次应返回 OK。
> 问题在去重表的移除时机：`_inFlight[cacheKey] = task;` 之后用
> `ContinueWith(... _inFlight.Remove(cacheKey))` 注销。`ContinueWith` 是**排队执行**的，
> 任务完成后还要等线程池调度才真正从表里删。这段窗口里第二次 `GetAsync` 会拿到那个
> **已经失败、已经结束**的旧任务，直接返回 `UpstreamError`，根本不会再发一次请求。
>
> 修的方向：取表时跳过已结束的任务；**不要**加 `Thread.Sleep` 掩盖。

## 二、测试覆盖的真实缺口

| ID | 项 | 判定 | 证据 / 复现 | 影响 |
|---|---|---|---|---|
| **OPEN-02** | `LoadConfig` 真的调用 `MirrorHelper.Normalize` 没有断言守住 | 未处置 | `docs/evidence/2026-11-17-backport-r1/05-mutation.txt` 变异 C：把迁移整段删掉后 `DownloadSourceMigrationTests` 仍 10/10 绿 | 迁移逻辑被静默删除时不会红。现有测试只测纯函数与 `ConfigStore.LoadAsync` 的原始读入 |

补齐路径（明确，未做）：走 `LocalApiTestHarness` → `GET /api/state` → 断言 `config.downloadSource`。
路由在 `LocalApiService.Instances.cs:48`；harness 在 `dev/PineglowLauncher.Tests/LocalApiTestHarness.cs`，
启动时必须属于 `[Collection("LocalApiService")]`。

## 三、发布线遗留（来自 R5 轮次，仍未处置）

| ID | 项 | 判定 | 证据 | 说明 |
|---|---|---|---|---|
| **OPEN-03** | `java-discovery-check.yml` 缺失；v1.1.0 无发布说明 | 未处置 | `docs/evidence/2026-11-15-c2/验收报告.md` 第 61 行 | 需要编排方决定是否补，涉及 `.github/workflows/*`，不是谁都能动 |
| **OPEN-04** | 更新脚本并发抖动无复现记录 | 未处置 | 同上，第 62 行 | 没有复现步骤就修不了；先要有一次真实复现 |

> `R5-05` 的版本号那一半（csproj 停在 `1.0.0`）**在 `backport/v1.0.4-to-main-20261117` 上已修**（改成 1.0.4 + 补发布说明），
> 但那条分支还没合回 `main`，所以 `main` 上仍成立。合并后从这张表删掉。
> 检查：`grep -n "<Version>" dev/PineglowLauncher/Pineglow.Launcher.csproj`

## 四、工具链缺口（会在验收时误导人）

| ID | 项 | 判定 | 证据 | 说明 |
|---|---|---|---|---|
| **OPEN-05** | `scripts/audit-evidence-tree.ps1` 与 `audit-plan-ledger.ps1` 的默认 `EvidenceRoot` 指向不存在的路径 | 未处置 | 两个脚本的 `param` 块默认值是 `C:\Users\Administrator\.codex\worktrees\ed41\...`，该目录已不存在 | 默认参数直接跑会失败。要么把默认值改成仓库内的相对路径，要么删掉默认值强制传参 |

## 五、怎么更新这张表

1. **解决了一条**：从对应小节删掉整行，并在提交信息里写清依据（哪个证据文件、哪条命令）。
2. **新发现一条**：加到对应小节，`ID` 顺延，必须带「证据 / 复现」列并且那一列能真的跑。
3. **判定只有三个值**：`未处置` / `进行中` / `已复现待修`。不要写「观察中」「基本解决」这类模糊值。
4. 每条都不要写预期完成时间——这个项目的时间估计从来没准过，写了只会变成噪声。

## 六、这张表的来源

| 项 | 从哪来 |
|---|---|
| OPEN-01 / OPEN-02 | 2026-11-17 回灌轮的独立验收（`docs/evidence/2026-11-17-backport-r1/`） |
| OPEN-03 / OPEN-04 | 2026-11-15 C2 第六轮独立验收（`docs/evidence/2026-11-15-c2/验收报告.md`） |
| OPEN-05 | 2026-11-17 门槛补强时发现（扫 `scripts/*.ps1` 的默认参数） |
