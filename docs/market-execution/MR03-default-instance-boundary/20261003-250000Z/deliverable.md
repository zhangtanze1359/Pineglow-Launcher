# MR03 补测 · "移除后的默认实例补建"边界（缺口登记表 A7）——**并查出一处夹具与生产的分叉**

- **run-id**：`20261003-250000Z`
- **性质**：查清 MR03 明确留下的未验证点；**发现既有绿色用例反映的是夹具而非生产**
- **依据**：`MR03-remove-from-list/20261002-190000Z/deliverable.md` §六：
  "移除后的默认实例补建 | 观察到一个未验证点：`InstanceService.EnsureDefaultInstance` 会在实例数为 0 时补建默认实例，
  但本包测试路径**未触发它**；『移除最后一个实例后 UI 会看到什么』需要在真实 GUI 验证"
- **最终状态**：**证据就绪**（4 个用例；3 个 RED 成立；**新增登记 A17**）

---

## 一、核心发现：**测试夹具绕过了 `EnsureDefaultInstance`，所以既有用例不代表生产行为**

| | 加载路径 | 是否调用 `EnsureDefaultInstance` |
|---|---|---|
| **生产** | `LocalApiService.LoadConfig()` → `ProductionConfigStore.Load()` → `ConfigStore.LoadAsync()` | **会**（`ConfigStore.cs:153`） |
| **测试夹具** | `LocalApiTestHarness` 装 `RouteTestMemoryConfigStore`（`LocalApiTestHarness.cs:127-128`） | **不会** —— 它的 `Load()` 直接返回内存对象 |

**实测对照**（同一个动作：移除唯一实例 → 再发一次 `GET /api/instances`）：

```
[A7] 夹具存储下：  count=0  selected=''
[A7] 生产语义存储下：count=1  selected='bc11bf32…'
```

即：**在生产语义下，移除最后一个实例后，下一次请求会看到一个"补建出来的默认实例"**；
而既有用例 `Remove_LastInstance_LeavesEmptyListAndBlankSelection`（断言 `count==0`）**只在该夹具下成立**。

> 这不是"用例写错了"，而是**证据边界**问题：该用例证明了"删除请求本身不会补建"，
> 但**不能**用来回答 MR03 问的"移除后 UI 会看到什么"。MR03 的原话（"本包测试路径未触发它"）
> 恰恰指向这里 —— 现在有数据了。

**为什么用"生产语义存储"而不是直接跑生产 `ConfigStore`**：真实 `ConfigStore` 走
`PlatformPaths.GetConfigDirectory()`，而该路径由"可执行文件旁的 `portable.flag`"全局切换 ——
在测试进程里动它会影响**并行运行的其它用例**。因此本包用一个**文件存储 + 加载后补建**的
`IConfigStore` 复刻那条语义（写入**临时目录**，绝不碰真实 `%APPDATA%`），
并为夹具**新增了一个接受 `IConfigStore` 的重载**（纯新增，既有调用方行为不变）。

---

## 二、第二个发现（**新增登记 A17**）：`EnsureDefaultInstance` 不修复悬空的 `SelectedInstanceId`

实测：

```
[A7] 悬空选中项处理后的 SelectedInstanceId = 已被删除的-id
```

原因在 `InstanceService.cs`：判断是 `if (GetSelectedInstance(config) == null)`，
而 `GetSelectedInstance` 在 id 不匹配时会**兜底返回第一个实例**，因此列表非空时它**永不为 null**
→ 该分支不执行 → 配置里的 `SelectedInstanceId` **保持悬空**。

**具体后果**：`GET /api/instances` 是**原样**返回 `config.SelectedInstanceId`（`LocalApiService.cs:1117`），
所以一个"由别的路径写坏"的配置会让**界面收到一个指向不存在实例的 id**。

**边界（不要夸大）**：**删除路径自己会重指向**（`HandleDeleteInstanceAsync` 第 1264–1266 行），
因此该后果只在**其它来源**（手改配置、旧版本写入）下出现。
"是否要顺手修好悬空选中项"属于**行为决策**，本包**不擅自改**，只登记（A17）。

---

## 三、四个用例

| # | 用例 | 断言 |
|---|---|---|
| 1 | `EnsureDefaultInstance_OnEmptyList_CreatesOneAndSelectsIt` | 空列表 → **补建 1 个**且把选中项指向它（这正是 MR03 说未触发的路径） |
| 2 | `EnsureDefaultInstance_OnNonEmptyList_DoesNotAddAnother` | 非空且选中有效 → 不补建、不改选中 |
| 3 | `EnsureDefaultInstance_DoesNotRepairDanglingSelection_AndEffectiveSelectionFallsBack` | 悬空选中项**不被修复**（字段保持原样）；但 `GetSelectedInstance` **兜底**到第一个实例（这正是它没立刻炸掉的原因） |
| 4 | `Remove_LastInstance_BehaviorDiffersBetweenTestHarnessAndProductionLikeStore` | **两种存储给出不同结果**（夹具 0 / 生产语义 1）——即 §一 的对照 |

---

## 四、三个 RED（证明断言有牙齿）

| RED | 改什么 | 结果 |
|---|---|---|
| **1** | `if (config.Instances.Count == 0)` → `== -1`（不再补建） | **2 失败**：用例 1 + 用例 4 |
| **1b** | 让"生产语义存储"的 `Load()` 不再补建（模拟"两种存储一致"） | **1 失败**：用例 4 —— 证明它能检出"分叉消失" |
| **2** | 加上"修复悬空选中项"（正是 A17 的候选修法） | **1 失败**：用例 3 —— 证明它守住了当前行为，且**将来若采纳该修法会被它标出来** |

三个 RED 后 `InstanceService.cs` **逐字节还原**（无 RED 标记）。

---

## 五、本轮自找并修掉的三个问题（都影响过证据可信度）

### 5.1 新测试类缺 `[Collection("LocalApiService")]` → **8 个无关用例失败**

`LocalApiService` 是**进程级静态单例**，`LocalApiServiceTestCollection.cs` 的注释写明了：
凡直接 Start/Stop 它的测试类必须属于该 collection 并严格串行。
我的新类漏了 → 与其它用例并行 → `ModReplacementTests`(4) + `InstanceRunningGuardTests`(3) + `InstanceRemovalTests`(1) 共 **8 项失败**。
补上属性后恢复。

### 5.2 **构建输出被 `Out-Null` 吞掉，导致我在测"旧二进制"**

我把 `dotnet build ... | Out-Null` 的退出码忽略了，而当时**编译是失败的**
（`FileRecoveryEdgeCaseTests.cs` 用了 `JsonDocument` 却没有 `using System.Text.Json;`）。
于是随后的 `dotnet test --no-build` **跑的是修改前的旧程序集** ——
那 5 次与 8 次"连跑通过/失败"**全都是无效证据**。补 using、**显式检查构建退出码**后才是真结果。

> 这是本会话**第三次**同类问题（第 32 轮 MR05：测试未重建；第 33 轮 MR04：RED-1 假通过；
> 本轮：构建失败被吞）。**规则**：改完实现/测试后必须 `dotnet build` 并**检查退出码**，
> 再 `dotnet test --no-build`；绝不用管道吞掉构建输出。

### 5.3 我在第 33 轮埋了一个 **flaky 测试**（本轮被全量纪律抓出）

`FileRecoveryEdgeCaseTests.Retention_ExpiredRecordsAreStillListed_NoSilentCleanup` 用
**字符串替换** `record.CreatedAt.ToString("O")` 来伪造过期；而 JSON 序列化**会省略末尾的零**
（`.1234560` → `.123456`），因此当第 7 位小数恰为 0 时替换**静默失配** → 约 10% 概率假失败。

**修正**：改为在 **JSON 层面**确定性地改写 `createdAt`（并保留 `Assert.NotEqual` 作为"确实改了"的自检）。
修正后**连跑 10 次全过**。

---

## 六、验证（**在真正重建过的二进制上**）

| 检查 | 结果 |
|---|---|
| 本组用例 | **4 通过 / 0 失败** |
| 保留期用例连跑 10 次 | **10/10 通过**（修正 flaky 后） |
| C# 全量 | **773 通过 / 0 失败 / 0 跳过**（769 + 4） |
| Node 全量 | **230 通过 / 0 失败**（18 个文件） |
| 实现文件 | RED 后逐字节还原，无 RED 标记 |

---

## 七、实际改动

| 文件 | 改动 |
|---|---|
| `dev/PineglowLauncher.Tests/InstanceDefaultInstanceBoundaryTests.cs` | **新增**（4 个用例 + 生产语义存储对照） |
| `dev/PineglowLauncher.Tests/LocalApiTestHarness.cs` | **新增重载** `StartAsync(IConfigStore, AppConfig)`（纯新增；既有重载行为不变） |
| `dev/PineglowLauncher.Tests/FileRecoveryEdgeCaseTests.cs` | **修正 flaky**（时间戳改写改为 JSON 层）+ 补 `using System.Text.Json;` |
| `docs/market-execution/gap-register.md` | A7 该项关闭；**新增 A17** |
| `docs/market-task-plan-2026-10-03.md` | MR03 行补记 |

**生产代码零净改动**（`InstanceService.cs` 未改）。

---

## 八、仍未覆盖

| 项 | 说明 |
|---|---|
| **真实 GUI** | 未验证。本包把"移除最后一个实例后会发生什么"从**猜测**变成了**有数据的结论**，但界面**实际渲染**仍需桌面会话 |
| 真实 `ConfigStore`（含 `.bak`/`.prev`/SecretStore） | 未直接跑（原因见 §一：便携开关是全局的）。本包只复刻"加载后补建"这一条语义 |
| A17 的修法 | **未实施**（行为决策，需你确认） |
| MR03 的其它剩余项 | "启动并发守卫"（无守卫，属设计缺口）、"启动器重启后的运行态"（进程内内存态）——本包未动 |

---

## 九、证据清单

```
red/InstanceService.cs.before                      RED 前实现备份
red/InstanceDefaultInstanceBoundaryTests.cs.before RED 前测试备份
red/01-red-empty-and-divergence.log                RED-1：2 失败
red/01b-red-empty-and-divergence.log               RED-1b：1 失败（分叉消失也能检出）
red/02-red-repair-dangling.log                     RED-2：1 失败（加上候选修法）
red/03-retention-flaky.log                         flaky 的一次失败（含 Assert.NotEqual 原文）
red/04-retention-repeat-real.log                   修正后连跑 10 次的记录
green/01..03-*.log                                 首次/中间运行
green/11-boundary-final.log                        本组 4 通过（真正编译后）
green/12-csharp-full-final.log                     773 通过 / 0 失败
manifest.json
```

**本报告不宣布阶段放行、不提交、不发布。**
