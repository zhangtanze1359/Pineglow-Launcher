# 夹具隔离**完全关闭** · 6/6 夹具四根全覆盖（缺口登记表 A21 收尾）

- **run-id**：`20261003-390000Z`
- **性质**：补上最后一格（`AiCancelAndRedirectTests` 的 recovery / bisect），A21 可**完全关闭**
- **依据**：`MR17-fixture-isolation-audit/20261003-380000Z` §三 的剩余项
- **最终状态**：**证据就绪**（**6/6 夹具四根全覆盖**）

---

## 一、为什么最后一格要换个接法

`AiCancelAndRedirectTests` 与前几个夹具不同：它**已经自己持有**诊断根的重定向
（第 47 轮加的 `_diagnosticScope` 字段 + 构造参数）。

若直接套用"装三根"的共享助手，助手的诊断根覆盖会**重复重定向**、
把它那个字段的 `Dispose` 语义打乱（scope 释放时会还原成"助手装的那个"，而不是原始值）✗。

**因此给助手加了一个只补两个根的入口**：

```csharp
/// 只安装恢复区 + 二分落盘（不碰诊断报告）
public static void InstallRecoveryAndBisectCurrent();
```

内部用 `new TestIsolationRoots(includeDiagnostic: false)` 实现 —— **补缺口而不干扰既有结构**。

`AiCancelAndRedirectTests` 的改动只有两行：
`StartAsync` 里装、`DisposeAsync` 里（诊断 scope 释放之后）卸。

---

## 二、最终四根覆盖表（**6/6 全覆盖**）

| 夹具文件 | config | recovery | bisect | diagnostic | 方式 |
|---|---|---|---|---|---|
| `LocalApiTestHarness.cs`（主） | ✅ | ✅ | ✅ | ✅ | 显式 override |
| `LauncherStartupBoundaryTests.cs` | ✅ | ✅ | ✅ | ✅ | 显式 override |
| `AiCancelAndRedirectTests.cs` | ✅ | ✅ | ✅ | ✅ | 显式（diag）+ 助手（rec/bisect） |
| `ContentIconServiceTests.cs` | ✅ | ✅ | ✅ | ✅ | 共享助手 |
| `NewsFakeIpConfigTests.cs` | ✅ | ✅ | ✅ | ✅ | 共享助手 |
| `NewsLinkHandlerTests.cs` | ✅ | ✅ | ✅ | ✅ | 共享助手 |

**没有任何一个自建夹具再把文件写进真实 `%APPDATA%`。**

---

## 三、验证（决定性）

| 检查 | 结果 |
|---|---|
| `AiCancelAndRedirectTests` 两个类 | **31 通过 / 0 失败** |
| C# 全量 | **817 通过 / 0 失败 / 0 跳过** |
| 真实三根（全量前 → 后） | diag **259 → 259**、rec **0 → 0**、bisect **0 → 0** ✅ |
| Node 全量 | **230 通过 / 0 失败** |

---

## 四、仍未覆盖

| 项 | 说明 |
|---|---|
| **残留 259 个测试垃圾** | 真实 `diagnostics` 目录里的历史遗留，**未清理**（需你同意，见第 47 轮 §四） |
| `cache` / `content-icons` | 未纳入守卫（无对应重定向缝；产品会正常使用它们） |
| 守卫"触发"的 RED | 仍未做 —— 要触发就得**故意往用户真实目录写文件**，本机不做 |

---

## 五、实际改动

| 文件 | 改动 |
|---|---|
| `dev/PineglowLauncher.Tests/TestIsolationRoots.cs` | 新增 `InstallRecoveryAndBisectCurrent()` + 构造参数 `includeDiagnostic` |
| `dev/PineglowLauncher.Tests/AiCancelAndRedirectTests.cs` | 装/卸 rec+bisect 两行 |
| `docs/market-execution/gap-register.md` | **A21 关闭** |
| `docs/market-task-plan-2026-10-03.md` | 相关行补记 |

**产品代码零改动。**

---

## 六、证据清单

```
green/TestIsolationRoots.cs.fixed             共享助手（含新入口）
green/AiCancelAndRedirectTests.cs.fixed       最后两行改动后的参照
green/01-ai-classes.log                       31 通过 / 0 失败
green/02-csharp-full.log                      817 通过 / 0 失败（真实三根未变）
green/03-node-full.log                        230 通过 / 0 失败
manifest.json
```

**本报告不宣布阶段放行、不提交、不发布。**
