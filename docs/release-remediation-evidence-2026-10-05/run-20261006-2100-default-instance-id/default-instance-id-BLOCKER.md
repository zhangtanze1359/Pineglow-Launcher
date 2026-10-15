# 🔴 第三个 blocker：**全新安装下"默认游戏"实例的 id 每次都变 ⇒ 任何操作都报"实例不存在"**（第 65 轮）

日期：2026-10-06（Asia/Hong_Kong）。本轮**未改动任何 tracked 文件**。
这条是**在验证"从列表移除 ↔ 挂回"时意外撞出来的**：第一次跑那个测试时 `delete` 竟然报"实例不存在"，
顺着查下去发现根因不在 delete，而在**默认实例的创建方式**。

## 1. 最小复现（全新便携目录，`config.json` 不存在）

```
启动前 config.json 存在=False
连续三次 GET /api/state 的默认实例 id：
    cc81e482dee24891bb39a7156a241fa4
    29b415abb14140abafbccb684032f496      ← 每次都不同！
    a4c48f41602645a9b8c6f72774cb5c45
POST /api/instances/cc81e482…/launch          → 400 {"error":"实例不存在：cc81e482…"}
GET  /api/instances/cc81e482…/prelaunch-check → 400 {"error":"实例不存在：cc81e482…"}
POST /api/accounts/offline {"userName":"DefaultIdProbe"} → {"ok":true,…}   ← 这一步会落盘 config.json
此后 GET /api/state 两次：5ba1f970… / 5ba1f970…   ← 稳定了
GET  /api/instances/5ba1f970…/prelaunch-check → 200 {"ok":false,"errors":["实例未关联游戏版本…","未找到可用的 Java 运行时…"]}
```

**判定：`REPRODUCED`**（脚本 `verify-default-instance-id.ps1`，逐项检查 6/6 通过）。

## 2. 根因（`ConfigStore.LoadAsync()`）

```csharp
// ① 配置文件不存在时（ConfigStore.cs:63-74）
AppConfig obj = new AppConfig { … };
InstanceService.EnsureDefaultInstance(obj);   // ← 造一个默认实例（Id 是新 GUID）
AccountService.EnsureAccounts(obj);
return obj;                                    // ← **直接返回，从不落盘**

// ② 配置文件存在、但实例列表为空时（ConfigStore.cs:184 附近）
InstanceService.EnsureDefaultInstance(config); // ← 又在内存里造一个（新 GUID）
…
if (flag) { try { await SaveAsync(config); } catch { } }   // ← 只有"迁移过"才落盘
return config;
```

`GameInstance.Id` 是**对象级新 GUID** ⇒ 只要配置里**没有实例**，每次 `Load()`（也就是**每次 API 调用**）都会造出一个
**id 不同**的默认实例，而且**不写盘**。

## 3. 用户影响（为什么算 blocker）

- **全新安装**（还没有 `config.json`）的用户，界面上看到的"默认游戏"是**不可寻址的**：
  点启动 / 预检 / 存档 / Mod / 删除 …… 全部返回 **400 实例不存在**。
- 只要用户**先做过任何会落盘的操作**（添加账号、改任意设置、新建实例、选下载源……）就恢复正常 ——
  所以这个 bug 表现为"**第一次点启动就失败**"这种最伤人的形态。
- 同一路径还有第二个触发面：**把最后一个实例移除后**，列表里又冒出一个"新"默认实例（新 id、未落盘），
  因此它在后续调用里同样不可寻址。
- 界面启动流程本身**不会**落盘（`init()` 只做 GET：`loadAccounts` / `goTo` → `loadState`），
  所以"用户什么都不改、直接点启动"这条最自然的路径**必然踩中**。

## 4. 修法建议（一行级，未改）

在 `LoadAsync()` 的两条路径上，**造完默认实例/账号后落盘一次**（例如把 ① 的 `return obj` 前加
`try { await SaveAsync(obj); } catch { }`，把 ② 的 `flag` 条件改成"配置被修改过就落盘"）。
这样 id 稳定且默认实例持久化，`/api/state` 与后续操作就一致了。
**代价**：改源码 ⇒ 产物字节变 ⇒ 需重跑全量验证链（与另外两个 blocker 一起修最划算）。

## 5. 顺带把"从列表移除 ↔ 挂回"验清楚了（上一轮被我误判的那两项）

在**先落盘**的前提下重跑（`run-20261006-2030-instance-register/`）：

| 检查 | 结果 |
| --- | --- |
| `POST /api/instances/{id}/delete` | **`{"ok":true,"removedId":"666d58ff…","removedName":"默认游戏","gameDirectory":"…"}`** ✅ 且**文件完好**（`versions/1.5.2/1.5.2.jar` 仍是 5,564,661 B）⇒ **只改配置、不删文件** ✅ |
| 移除后实例列表 | 该 id 不再出现（但**又自动冒出一个**默认实例——见 §2 的 ② 路径）|
| `POST /api/instances/register {gameDirectory:…}` | **400 `该目录已在实例列表中，无需重复添加`** —— 因为自动重建的默认实例**占着同一个目录**，这个 400 是**正确的去重**（我第一版的期望写错了）|
| 重复挂回 / 不存在的目录 | 都是 **400** 且文案明确 ✅ |

⇒ **delete/register 本身没有缺陷**；第一版测试的两个"FAIL"是**被本轮的默认实例缺陷 + 自动重建**造成的假失败。
（教训：测试的期望要建立在"配置已落盘、实例集合稳定"的前提上；否则会把 A 的缺陷记成 B 的问题。）

## 6. 复现

```powershell
powershell -File ...\run-20261006-2100-default-instance-id\verify-default-instance-id.ps1 `
  -ExePath <候选 EXE> -JsonPath <证据>\default-instance-id.json -WorkRoot %TEMP%\pg-defaultid
# 期望（当前版本）：REPRODUCED —— 三次 state 的 id 各不同、launch/prelaunch 均 400、落盘后 id 稳定且 prelaunch 200
```

产物：`default-instance-id.json`（三次 id / 两次 400 原文 / 落盘后的 200 原文 / 逐项判定）、`default-instance-id.log`（时间线）。
