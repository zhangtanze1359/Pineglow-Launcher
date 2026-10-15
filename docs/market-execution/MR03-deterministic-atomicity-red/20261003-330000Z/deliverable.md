# MR03 补测之四 · 原子性的**确定性 RED**（补上第 43 轮未达成的那一条）

- **run-id**：`20261003-330000Z`
- **性质**：让"非原子实现会被抓住"这件事**可复现地**成立
- **依据**：`MR03-atomic-launch-reservation/20261003-320000Z/deliverable.md` §三：
  "RED：**没复现**，如实记录 … 32 并发下非原子实现**仍然只有 1 个赢家** …
  因此用例 ⑤ **尚未被证明能抓住非原子实现**"
- **最终状态**：**证据就绪**（本类 8 个用例；**RED 确定性复现**；全量 812/0）

---

## 一、做法：把"检查→占位"之间的窗口**人为撑开**

第 43 轮的失败原因很具体：`ContainsKey` 检查与赋值之间的竞争窗口只有**纳秒级**，
而 32 个 `Task.Run` 的启动本身是错开的 —— 几乎不可能挤进那个窗口。

**修法**：加一个测试缝，在"检查"与"占位"之间插同步点：

```csharp
// RunningInstanceRegistry.TryBeginLaunch 内
if (IsRunning(instanceId, out existingPid)) return false;

BeforeReserveForTest?.Invoke();          // ← 测试缝（生产恒为 null，零开销）

if (!Running.TryAdd(instanceId, new Entry(0, DateTimeOffset.Now, null, Launching: true))) { ... }
```

测试里用 `Barrier` 让**所有**线程先过检查、再一起去占位：

```csharp
using var barrier = new System.Threading.Barrier(contenders);   // contenders = 8
RunningInstanceRegistry.BeforeReserveForTest = () => barrier.SignalAndWait(TimeSpan.FromSeconds(10));
```

于是：
- **原子实现**（`TryAdd`）→ 屏障放行后**恰好 1 个**赢家；
- **非原子实现**（检查后再赋值）→ 屏障放行后**多个**赢家 → 断言失败。

**生产代码的改动只有 1 行调用 + 1 个 `internal` 委托属性**，且生产路径上委托恒为 `null`。
（本仓库已有同类测试缝：`MarkRunningForTest` / `ResetForTest` / `OverrideConfigStoreForTest` 等。）

---

## 二、RED：**这次确定性复现** ✅

| 实现 | 8 个并发调用者（屏障撑开窗口）的赢家数 | 结果 |
|---|---|---|
| **原子**（`Running.TryAdd`） | **1** | **8/8 通过** ✅ |
| **非原子**（`ContainsKey` 检查后再赋值） | **3** | **1 失败** ✅ |

RED 失败信息：

```
把竞争窗口撑开后，原子实现必须**恰好 1 个**赢家，实际 3 个 —— 说明 TryBeginLaunch 不是原子的
[A7d] 屏障撑开竞争窗口后，8 个调用者的赢家数 = 3
```

> 注意：非原子版是 **3/8** 而不是 8/8 —— 屏障只保证"都过了检查"，之后的赋值彼此仍有交错。
> **>1 就足以让断言确定性失败**，这正是我们要的。

**这一次，第 43 轮那条"未达成"被补上了**：该用例现在**被证明**能抓住非原子实现。

---

## 三、还原：这次遵守了"修复后参照"规则 ✅

第 42、43 轮我连续两次用"修复前"的备份做还原源，把修复本身撤掉。本轮**先在动手前**保存了三份修复后参照：

```
green/RunningInstanceRegistry.cs.fixed
green/LocalApiService.cs.fixed
green/InstanceLaunchGuardTests.cs.fixed
```

RED 之后**还原到修复后状态**，核对结果：

```
含 TryAdd = True
与 fixed 一致 = True        ← 逐字节一致
```

---

## 四、验证

| 检查 | 结果 |
|---|---|
| 本类用例 | **8 通过 / 0 失败** |
| 本类连跑 3 次 | **3/3 PASS**（屏障用例不 flaky） |
| C# 全量 | **812 通过 / 0 失败 / 0 跳过**（811 + 1） |
| Node 全量 | **230 通过 / 0 失败**（18 个文件） |
| 还原完整性 | 与 `green/RunningInstanceRegistry.cs.fixed` **逐字节一致** |
| 实测输出 | `[A7d] 屏障撑开竞争窗口后，8 个调用者的赢家数 = 1` |

---

## 五、实际改动

| 文件 | 改动 |
|---|---|
| `RunningInstanceRegistry.cs` | +1 行调用 `BeforeReserveForTest?.Invoke()`；+1 个 `internal static Action? BeforeReserveForTest`（含说明为何存在） |
| `dev/PineglowLauncher.Tests/InstanceLaunchGuardTests.cs` | +1 个 barrier 用例（累计 8 个） |

**生产行为零变化**（委托在生产恒为 null）。

---

## 六、仍未覆盖

| 项 | 说明 |
|---|---|
| **跨进程**并发启动 | 两个启动器进程同时启动同一实例仍无协调（进程内注册表的固有限制） |
| 运行态持久化 | 仍是进程内内存态（MR03b 已列） |
| 真实启动 Minecraft | 未验证（需真实游戏/Java） |
| 删除守卫与预约的交互 | 行为正确（`IsRunning` 把预约视为占用），但**无专门用例** |
| 屏障用例的线程池假设 | 依赖线程池能同时提供 8 个线程；已用 10 秒超时兜底，但**未在低核机器上验证** |

---

## 七、证据清单

```
green/RunningInstanceRegistry.cs.fixed      修复后参照（本轮起遵守规则）
green/LocalApiService.cs.fixed              修复后参照
green/InstanceLaunchGuardTests.cs.fixed     修复后参照
green/01-green-seam.log                     原子版 8 通过（赢家数 = 1）
green/02-green-restored.log                 还原后 8 通过
green/03-csharp-full.log                    812 通过 / 0 失败
green/04-node-full.log                      230 通过 / 0 失败
red/01-red-nonatomic-barrier.log            RED：非原子版 → 赢家数 3 → 1 失败（确定性）
manifest.json
```

**本报告不宣布阶段放行、不提交、不发布。**
