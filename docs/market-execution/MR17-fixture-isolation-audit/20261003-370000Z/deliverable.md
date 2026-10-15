# 测试夹具隔离**清点**与共享守卫（缺口登记表 A21）

- **run-id**：`20261003-370000Z`
- **性质**：把"逐个补重定向"换成**一个共享守卫**，并如实标出仍未覆盖的夹具
- **依据**：`gap-register.md` A21（第 47 轮新增）：
  "只修了已确证的一个夹具 …… 其余夹具是否重定向了 config / recovery / bisect / diagnostic **四个根**，**未逐个清点**"
- **最终状态**：**部分关闭**（清点完成；共享守卫落地；**仍有 3 个自建夹具未纳入守卫**）

---

## 一、清点结果（A21 要的那张表）

直接调用 `LocalApiService.Start(...)` 的文件（即"自建夹具"）共 6 个：

| 夹具文件 | config | recovery | bisect | diagnostic |
|---|---|---|---|---|
| `LocalApiTestHarness.cs`（**主夹具**） | ✅ | ✅ | ✅ | ✅ |
| `AiCancelAndRedirectTests.cs` | ✅ | ❌ | ❌ | ✅（第 47 轮修） |
| **`LauncherStartupBoundaryTests.cs`**（我第 40 轮写的） | ❌ | ❌ | ❌ | ❌ |
| `ContentIconServiceTests.cs` | ❌ | ❌ | ❌ | ❌ |
| `NewsFakeIpConfigTests.cs` | ✅ | ❌ | ❌ | ❌ |
| `NewsLinkHandlerTests.cs` | ✅ | ❌ | ❌ | ❌ |

---

## 二、**关键判断：不能盲目逐个补**

真实 `%APPDATA%\MCMiniLauncher` 的实测状态：

| 根 | 文件数 | 最新写入 |
|---|---|---|
| `diagnostics` | **259** | 10-03（**被污染过**，第 47 轮已修） |
| `recovery` | **目录不存在** | — |
| `bisect` | **目录不存在** | — |
| `config.json` | 1 | **09-07**（未被动过） |

**结论："没重定向" ≠ "实际在写"。** recovery/bisect 的 `❌` 是**潜在**风险（今天没有真实写入），
config 的 `❌` 也只是**读**了真实文件。

因此正确的做法不是逐个补（改了也可能漏），而是**加一个共享守卫**，让**任何**真实目录写入立刻暴露。

---

## 三、两处改动

### 3.1 **扩展 `RealConfigGuard`：把三个根纳入守卫**（高杠杆）

`RealConfigGuard` 在 `LocalApiTestHarness.DisposeAsync` 里**已经被比对**（不等即失败）。
把三个存储根的文件数快照加进这个类，就**自动保护了每一个使用主夹具的测试**：

```csharp
public int DiagnosticsCount { get; }
public int RecoveryCount { get; }
public int BisectCount { get; }
// 并在 Equals / ToString 里一并比较
```

- 只统计**有专用重定向缝**的三个根（diagnostics / recovery / bisect）；
- `cache` / `content-icons` 等由产品正常使用、夹具没有对应缝，**不纳入**（避免误报）。

### 3.2 修掉**我自己**那个夹具（`LauncherStartupBoundaryTests`，第 40 轮写的）

清点发现它**四个根一个都没隔离** —— 配置读取会落到真实 `config.json`。
修法：在**构造函数**里装、在 `Dispose` 里卸（xUnit 每个用例新建一次实例，因此覆盖本类全部用例）：

```csharp
_isolation.Add(LocalApiService.OverrideConfigStoreForTest(new RouteTestMemoryConfigStore(new AppConfig())));
_isolation.Add(LocalApiService.OverrideRecoveryRootForTest(NewTempRoot("rec")));
_isolation.Add(LocalApiService.OverrideBisectStoreRootForTest(NewTempRoot("bis")));
_isolation.Add(LocalApiService.OverrideDiagnosticStoreRootForTest(NewTempRoot("dia")));
```

---

## 四、验证

| 检查 | 结果 |
|---|---|
| C# 全量（**含扩展后的守卫**） | **817 通过 / 0 失败 / 0 跳过** |
| 真实三个根（全量前 → 后） | diag **259 → 259**、rec **0 → 0**、bisect **0 → 0** ✅ **全部未变** |
| 守卫是否误报 | **没有**（全量仍全绿） |
| Node 全量 | **230 通过 / 0 失败** |

---

## 五、**诚实说明：守卫的"触发"没有做 RED**

我没有用 RED 证明"守卫真的会在泄漏时失败"，**原因是要做到这一点就必须故意往用户真实数据目录里写文件** ——
那正是本任务要消除的行为，我不愿意为了证明守卫而污染用户目录。

可以给出的替代依据：

- 守卫比较的是**文件数**，而第 47 轮实测的泄漏**恰好表现为文件数变化**（259 → 每轮 +11）；
- 该守卫与**已经在用**的 config 守卫是同一机制、同一断言点（`DisposeAsync` 里 `_pre.Equals(post)`）。

若你要一个真正的 RED，可行做法是**在 CI/临时机器**上做（那里污染真实目录无所谓），本机不做。

---

## 六、仍未覆盖（如实列出）

| 项 | 说明 |
|---|---|
| **`ContentIconServiceTests.cs`** | 四个根**都未重定向**；它自建夹具，**不在**扩展守卫的保护范围内 |
| **`NewsFakeIpConfigTests.cs`** / **`NewsLinkHandlerTests.cs`** | recovery / bisect 未重定向；同样**不在**守卫保护范围内 |
| 残留 259 个测试垃圾 | **未清理**（需你同意，见第 47 轮报告 §四） |
| `cache` / `content-icons` | 未纳入守卫（无对应重定向缝） |

> 下一步的最小增量：把上述 3 个文件按 `LauncherStartupBoundaryTests` 的方式各加 4 个 overrides，
> 并在每个文件里复用 `RealConfigGuard` 做前后比对。

---

## 七、实际改动

| 文件 | 改动 |
|---|---|
| `dev/PineglowLauncher.Tests/LocalApiTestHarness.cs` | `RealConfigGuard` 扩展：三个根的文件数纳入比较 |
| `dev/PineglowLauncher.Tests/LauncherStartupBoundaryTests.cs` | 构造函数装 4 个 overrides + `Dispose` 卸载（+`IDisposable`） |
| `docs/market-execution/gap-register.md` | A21 **部分关闭**，剩余项写明 |
| `docs/market-task-plan-2026-10-03.md` | 相关行补记 |

**产品代码零改动。**

---

## 八、证据清单

```
green/LocalApiTestHarness.cs.fixed           修复后参照
green/LauncherStartupBoundaryTests.cs.fixed  修复后参照
green/01-csharp-full.log                     817 通过 / 0 失败（真实三根未变）
green/02-node-full.log                       230 通过 / 0 失败
manifest.json
```

**本报告不宣布阶段放行、不提交、不发布。**
