# 修复 Codex 独立探针发现的 **2 个缺陷** · 世界备份的归属过滤与跨世界恢复（缺口登记表 A23）

- **run-id**：`20261003-500000Z`
- **性质**：**修产品缺陷**（由**独立复验**发现，不是我自己找的）
- **来源**：`docs/market-acceptance/20261003-W1-W3/`（Codex 侧独立验收）
  - `IndependentRouteProbes.BackupsListIsScopedToRequestedWorld` → **FAIL**
  - `IndependentRouteProbes.RestoreRejectsBackupFromDifferentWorld` → **FAIL**
- **最终状态**：**证据就绪**（**用验证方自己的探针复跑 15/15 全过**；3 个常驻用例；RED 成立）

---

## 一、独立验收发现了什么（**我自己的测试没抓到**）

Codex 的独立探针（`independent-probes.trx`：15 个探针，**13 通过 / 2 失败**；`08-failures-repeat.log` 复跑仍 2 失败）：

| 探针 | 实测（修复前） |
|---|---|
| `BackupsListIsScopedToRequestedWorld` | 请求 **worldB** 的备份列表，返回的却是 **worldA** 的备份（响应里 `groupName:"worldA"`）✗ |
| `RestoreRejectsBackupFromDifferentWorld` | 用 worldA 的备份恢复到 **worldB** → **200 `{ok:true, path:…\saves\worldA (1)`}** ✗✗ |

**第二条是数据安全问题**：用户可以把**另一个世界的备份**恢复进来，还写出了 `worldA (1)` 目录 ✓。

> **这两个缺陷我自己的测试完全没覆盖** —— 它们由**独立探针**发现。
> 这正是"独立复验"的价值，也是计划坚持"证据就绪 ≠ 独立复验通过"的原因。

---

## 二、根因：两个处理器都**忽略了 `folderName`**

| 位置 | 修复前 |
|---|---|
| `HandleListSaveBackupsAsync`（2369 行） | `GetBackups(instance)` → 返回该实例的**全部**备份 ✗ |
| `HandleRestoreSaveBackupAsync`（2390 行） | `GetBackups(instance).FirstOrDefault(x => x.FileName == backupName)` → **不校验归属** ✗ |

---

## 三、修复（两处，都很小）

```csharp
// 修复 1：列表按请求的世界过滤（GroupName 来自备份文件名解析：worldA-20261003-030238.zip → worldA）
List<BackupFileInfo> backups = new SaveBackupService(gameDir).GetBackups(instance)
    .Where(x => string.Equals(x.GroupName, folderName, StringComparison.OrdinalIgnoreCase))
    .ToList();

// 修复 2：跨世界恢复必须拒绝（数据安全）
if (!string.Equals(backup.GroupName, folderName, StringComparison.OrdinalIgnoreCase))
{
    throw new InvalidOperationException($"该备份属于「{backup.GroupName}」，不能恢复到「{folderName}」。");
}
```

**边界说明**：无法解析归属的备份（`GroupName` 回退为「存档」）**不属于任何具体世界** →
它们不会出现在任何世界的列表里 ✓（这是正确的保守行为 ✓）。

---

## 四、**用验证方自己的探针复跑（决定性证据）**

把 Codex 的 `IndependentRouteProbes.cs` **临时**拷进测试项目、原样运行，跑完**立即移除**：

```
探针文件 SHA（前 24）= 06B36DFF7C1F7275ACDC33C9…
总计: 15   通过: 15   失败: 0        ← 修复前是 13/15
```

**这是最强的验证形式**：不是"我认为修好了"，而是**发现缺陷的那组探针现在全过** ✓✓。
探针文件已移除，仓库无痕迹 ✓。

---

## 五、常驻覆盖（我自己的 3 个用例）

| 用例 | 断言 |
|---|---|
| `BackupsList_IsScopedToTheRequestedWorld` | worldA 列表含它的备份；**worldB 列表必须为空** ✓（实测 `backups:[]`） |
| `Restore_RejectsABackupFromAnotherWorld_AndChangesNothing` | **4xx/5xx** + 错误信息说明"属于哪个世界" + **整棵目录树逐字节不变**（含"不得留下 `worldA (1)`"）|
| `Restore_OfTheSameWorld_StillWorks` | **正面**：同世界恢复仍 200 且内容正确（别把功能改坏）✓ |

实测输出：

```
[A23] worldB 的列表 = {"folderName":"worldB","backups":[]}
[A23] worldB 用 worldA 的备份恢复 -> 400
[A23] 解析后的错误信息 = 该备份属于「worldA」，不能恢复到「worldB」。
[A23] worldA 用 worldA 的备份恢复 -> 200
```

---

## 六、RED

| RED | 改什么 | 结果 |
|---|---|---|
| **1** | **同时撤掉两处修复**（回到探针发现缺陷时的行为） | **2 失败** ✓（正是那两个用例） |

RED 后 `LocalApiService.cs` **逐字节还原**（并触碰 mtime ✓）。

---

## 七、本轮自找的一个问题

用例首跑 **2 通过 / 1 失败**，失败的是**我自己的断言** ✗：
`Assert.Contains("属于", body)` —— 而 JSON 响应里的中文是 **`\uXXXX` 转义**的 ✗。
**修法**：先解析 JSON 再断言 ✓（实测得到 `该备份属于「worldA」，不能恢复到「worldB」。`）✓。

> 这条规则**我自己的笔记里就有**（"JSON 里的非 ASCII 是转义的 → 必须解析，绝不做字符串匹配"），
> 本轮又踩了一次 ✓ —— 已在该用例里写成注释，防止再犯。

---

## 八、验证

| 检查 | 结果 |
|---|---|
| **Codex 探针原样复跑** | **15/15 通过**（修复前 13/15）|
| 我的常驻用例 | **3 通过 / 0 失败** |
| C# 全量 | **835 通过 / 0 失败 / 0 跳过**（832 + 3）|
| Node 全量 | **238 通过 / 0 失败** |
| RED | 2 失败 ✓ |
| 还原 | 逐字节一致 ✓ |
| 探针文件 | 已移除，仓库无痕迹 ✓ |

---

## 九、实际改动

| 文件 | 改动 |
|---|---|
| `LocalApiService.cs` | 两处修复（列表按世界过滤 + 拒绝跨世界恢复） |
| `dev/PineglowLauncher.Tests/WorldBackupScopeTests.cs` | **新增**（3 个常驻用例） |
| `scripts/source-snapshot-inputs.txt` | 登记新测试（类别 B +1） |
| `docs/market-execution/gap-register.md` | **新增 A23 并关闭**（独立验收发现的缺陷） |
| `docs/market-task-plan-2026-10-03.md` | MR03 行补记 |

---

## 十、仍未覆盖

| 项 | 说明 |
|---|---|
| **Codex 验收的其它产物** | 我只处理了这 2 个失败 ✗ —— `implementer-manifest-audit.json`（**对我的 manifest 的审计**）**尚未逐条读** ✓ 属下一轮 |
| 备份归属的**其它入口** | 只修了"列表"与"恢复"两处；`delete backup` 等入口未审 ✓ |
| 真实 GUI 下的备份界面 | 未验证 |

---

## 十一、证据清单

```
green/LocalApiService.cs.fixed              修复后参照
green/WorldBackupScopeTests.cs.fixed        用例参照
green/01-codex-probes-verbatim.log          **Codex 探针原样复跑 15/15 通过**
green/02-my-tests.log                       首次：2 通过 / 1 失败（我自己的转义断言错）
green/03-my-tests-final.log                 修正后 3 通过 / 0 失败
green/04-csharp-full.log                    835 通过 / 0 失败
green/05-node-full.log                      238 通过 / 0 失败
red/01-red-both-defects-restored.log        RED：撤掉两处修复 → 2 失败
manifest.json
```

**本报告不宣布阶段放行、不提交、不发布。**
