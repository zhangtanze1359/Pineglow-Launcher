# 夹具隔离收尾 · 剩余 3 个自建夹具已隔离（缺口登记表 A21 收尾）

- **run-id**：`20261003-380000Z`
- **性质**：把第 48 轮清点出的"未覆盖夹具"逐个补齐，并给出**正确的**四根覆盖表
- **依据**：`MR17-fixture-isolation-audit/20261003-370000Z` §六 的剩余项
- **最终状态**：**证据就绪**（5/6 夹具四根全覆盖；1 个夹具仍缺 rec/bisect，**潜在**）

---

## 一、本轮的改动

### 1. 新增共享助手 `TestIsolationRoots.cs`

```csharp
TestIsolationRoots.InstallCurrent();      // StartAsync 里（三个根：恢复区/二分落盘/诊断报告）
TestIsolationRoots.ReleaseCurrent();      // DisposeAsync / finally 里
```

- `Install()` / `Dispose()`：显式持有句柄的用法；
- `InstallCurrent()` / `ReleaseCurrent()`：静态槽位，供**不想加字段与构造参数**的既有夹具使用
  （这些测试类都在 `[Collection("LocalApiService")]` 里**严格串行**，同一时刻只有一个夹具活着）；
- `InstallCurrent(includeConfigStore: true)`：**额外**把配置存储也换成内存 store
  —— 只有"完全没有隔离配置"的用例才该用（News 两个夹具会**先**装自己的 store，再覆盖会把它们冲掉）。

### 2. 三个夹具接上隔离

| 文件 | 接法 |
|---|---|
| `NewsFakeIpConfigTests.cs` | `StartAsync` 装三根；`DisposeAsync`（服务停止后）释放 |
| `NewsLinkHandlerTests.cs` | 同上 |
| `ContentIconServiceTests.cs` | 两个**直接 Start** 的用例：`try` 前装（`includeConfigStore: true`）、`finally` 里释放 |

**踩到的一个小坑**：`ContentIconServiceTests.cs` **没有 namespace 声明**（在全局命名空间），
所以助手必须用**全限定名** `Pineglow.Launcher.Tests.TestIsolationRoots` 才能解析（首次构建 4 个 CS0103）。

---

## 二、**正确的**四根覆盖表（修正第 48 轮那张）

第 48 轮的表是按"夹具文件里有没有 `Override*ForTest`"grep 出来的，**会漏掉走共享助手的夹具** ✗。
正确判据是"自身有 override **或** 调用了共享助手"：

| 夹具文件 | config | recovery | bisect | diagnostic | 方式 |
|---|---|---|---|---|---|
| `LocalApiTestHarness.cs`（主） | ✅ | ✅ | ✅ | ✅ | 显式 override |
| `LauncherStartupBoundaryTests.cs` | ✅ | ✅ | ✅ | ✅ | 显式 override |
| `ContentIconServiceTests.cs` | ✅ | ✅ | ✅ | ✅ | **共享助手** |
| `NewsFakeIpConfigTests.cs` | ✅ | ✅ | ✅ | ✅ | **共享助手** |
| `NewsLinkHandlerTests.cs` | ✅ | ✅ | ✅ | ✅ | **共享助手** |
| `AiCancelAndRedirectTests.cs` | ✅ | ❌ | ❌ | ✅ | 显式 override（第 47 轮） |

**5 / 6 夹具四根全覆盖。**

---

## 三、剩下的那一格：`AiCancelAndRedirectTests` 的 rec / bisect

- 它第 47 轮已装 **config + diagnostic** 的显式 override；
- **recovery / bisect 仍未重定向**；
- **但它是潜在缺口、不是活跃缺陷**：真实 `%APPDATA%` 下 **`recovery` 与 `bisect` 目录根本不存在**
  （见 §四），且本轮全量跑完仍未出现。

**为什么本轮没有直接补**：它已经有自己的 `_diagnosticScope` 字段与构造参数，
再叠一个"装三根"的助手会造成**重复重定向**（助手的 diag 覆盖会把它的字段作用域打乱）。
正确做法是给它**显式**加 `_recoveryScope` / `_bisectScope`（含构造参数与释放）—— 那是 3 处结构性改动，
本轮如实留作剩余项，而不是草率叠加。

---

## 四、验证（决定性）

| 检查 | 结果 |
|---|---|
| C# 全量 | **817 通过 / 0 失败 / 0 跳过** |
| 真实三根（全量前 → 后） | diag **259 → 259**、rec **0 → 0**、bisect **0 → 0** ✅ |
| `NewsFakeIpConfigTests` + `NewsLinkHandlerTests` | **31 通过 / 0 失败** |
| `ContentIconServiceTests` | **203 通过 / 0 失败** |
| Node 全量 | **230 通过 / 0 失败** |
| 真实目录现状 | `diagnostics` 259 个文件（历史遗留，未清理）；`recovery`/`bisect` **不存在** |

---

## 五、仍未覆盖

| 项 | 说明 |
|---|---|
| `AiCancelAndRedirectTests` 的 **rec / bisect** | 潜在缺口（见 §三），需 3 处结构性改动 |
| **残留 259 个测试垃圾** | **未清理**（需你同意，见第 47 轮报告 §四） |
| `cache` / `content-icons` | 未纳入守卫（无对应重定向缝） |
| 守卫的"触发"RED | 仍未做（要触发就得故意写用户真实目录，见第 48 轮 §五） |

---

## 六、实际改动

| 文件 | 改动 |
|---|---|
| `dev/PineglowLauncher.Tests/TestIsolationRoots.cs` | **新增**（共享隔离助手） |
| `dev/PineglowLauncher.Tests/NewsFakeIpConfigTests.cs` | 接助手（装 + 释放） |
| `dev/PineglowLauncher.Tests/NewsLinkHandlerTests.cs` | 接助手（装 + 释放） |
| `dev/PineglowLauncher.Tests/ContentIconServiceTests.cs` | 两个直接 Start 的用例接助手（含 config） |
| `docs/market-execution/gap-register.md` | A21 更新为"5/6 全覆盖，1 处潜在" |
| `docs/market-task-plan-2026-10-03.md` | 相关行补记 |

**产品代码零改动。**

---

## 七、证据清单

```
green/TestIsolationRoots.cs.fixed             共享助手（修复后参照）
green/NewsFakeIpConfigTests.cs.fixed          接助手后参照
green/NewsLinkHandlerTests.cs.fixed           接助手后参照
green/ContentIconServiceTests.cs.fixed        接助手后参照
green/01-news-classes.log                     31 通过 / 0 失败
green/04-icon-class.log                       203 通过 / 0 失败
green/07-csharp-full-final.log                817 通过 / 0 失败（真实三根未变）
green/06-node-full.log                        230 通过 / 0 失败
manifest.json
```

**本报告不宣布阶段放行、不提交、不发布。**
