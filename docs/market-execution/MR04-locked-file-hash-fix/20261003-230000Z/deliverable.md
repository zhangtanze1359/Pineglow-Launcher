# MR04 修复 · A16：加锁文件在哈希阶段抛出（恢复区）

- **run-id**：`20261003-230000Z`
- **性质**：**产品代码修复**（不是新功能）。让实现符合 MR04 **已文档化**的意图
- **依据**：`MR04-recovery-edge-cases/20261003-220000Z/deliverable.md` §二（实测出的缺陷）、
  `gap-register.md` 的 **A16**、`MR04-recoverable-mod-delete/…193000Z/deliverable.md` §六 的原话
- **最终状态**：**证据就绪**（RED = 抛出；GREEN = 逐项报错；全量 769/0/0 跳过）

> ⚠️ **这是本轮唯一一次改动生产代码**，因此本报告给出确切改动、风险与回退方法（见 §四、§六）。
> 若你不同意这个改动，按 §六 一行即可回退。

---

## 一、缺陷回顾（第 33 轮实测）

把文件以 `FileShare.None` 打开后调用 `QuarantineAsync`，异常**不是在移动阶段被逐项捕获**，
而是在更早的**哈希阶段**抛出并冲出 `QuarantineAsync`：

```
System.IO.IOException : ... because it is being used by another process.
  at FileRecoveryService.HashFile(String path)      // 第 399 行 File.OpenRead
  at FileRecoveryService.QuarantineOne(...)         // 第 136 行 entry.Sha256 = HashFile(...)
  at FileRecoveryService.QuarantineAsync(...)       // 第 93 行
```

而 MR04 报告 §六 写的是"**文件被占用：会走 IOException 回退并逐项报错**" ——
**那只覆盖了移动阶段（原第 146 行），哈希阶段没有任何保护。**

**影响**：删除 Mod 的路由会拿到未处理的 `IOException`（对用户是失败弹窗/500），
而不是一条"这一项失败、其余项照常"的逐项结果。

---

## 二、为什么判定这是"修复"而不是"未经授权的功能变更"

| 判据 | 事实 |
|---|---|
| 是否新增行为？ | **否**。MR04 报告**已经声明**了"逐项报错"这一行为；实现没做到 |
| 是否改变对外契约？ | 只在**原本会抛异常**的路径上改为逐项失败 —— 这正是文档承诺的行为 |
| 是否引入数据风险？ | **否**。该分支不做任何文件移动；源文件必然保留 |
| 异常范围是否收紧？ | **是**。只捕获 `IOException` / `UnauthorizedAccessException`，与紧邻的移动阶段**完全同一组类型** |
| 是否有现成的目标测试？ | **有**。第 33 轮已写好 `Skip` 标记的目标行为用例，本包把它转为正式守卫 |

---

## 三、确切改动（+13 行，一处 hunk）

`dev/PineglowLauncher/Pineglow.Launcher.Services/FileRecoveryService.cs`

**改前**（原第 136–137 行，无任何保护）：

```csharp
entry.Sha256 = HashFile(entry.OriginalPath);
entry.SizeBytes = new FileInfo(entry.OriginalPath).Length;
```

**改后**（现第 136–150 行）：

```csharp
// A16 修复（MR04 范围）：哈希阶段也会因文件被占用/只读而抛出。
// 修复前这里没有任何保护，异常会直接冲出 QuarantineAsync，
// 与 MR04 报告所述"走 IOException 回退并逐项报错"不符（那只覆盖了下面的移动阶段）。
// 现在与移动阶段用同一组异常类型，失败时逐项记录，而不是让整次操作炸掉。
try
{
    entry.Sha256 = HashFile(entry.OriginalPath);
    entry.SizeBytes = new FileInfo(entry.OriginalPath).Length;
}
catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
{
    entry.State = "failed";
    entry.Error = "无法读取文件（可能被占用或只读）：" + ex.Message;
    return entry;
}
```

| 项 | 值 |
|---|---|
| 行数 | 488 → **501**（+13） |
| 改动前 SHA256 | `6926200706E0CD97DDCFC2CC7C577310B82E66309E65547F159E987FEA3ACDD3` |
| 改动后 SHA256 | `3ADB6CA4F96F37FDD477EA62382D6726641E66A4A1E25902F27ABC9543FD3796` |

**没有其它改动**：`CopyThenRemove`、`RestoreOne`、`ListAsync` 等一律未动。

---

## 四、RED → GREEN

| | 行为 | 结果 |
|---|---|---|
| **RED**（回退修复，用修复前备份） | 加锁文件 → **抛出 `IOException`** | 目标用例 **失败**（3 通过 / 1 失败），日志含 `System.IO.IOException` |
| **GREEN**（应用修复） | 加锁文件 → **逐项 `failed` + 可读原因** | **4 通过 / 0 失败 / 0 跳过** |

GREEN 下的实际输出：

```
[A2/A16] 加锁文件 entry.State = failed
         / Error = 无法读取文件（可能被占用或只读）：The process cannot access the file
           '...\mods\locked.jar' because it is being used by another process.
```

---

## 五、测试侧的同步改动

第 33 轮留下两个用例，本包按"缺陷已修"处理：

| 用例 | 处置 |
|---|---|
| `Quarantine_LockedForReading_ThrowsToday_ButNeverLosesTheSourceOrTheRecord`（钉住缺陷的**过渡**用例） | **删除** —— 它断言的是"会抛出"，修复后不再成立。其**安全底线断言**（源文件不丢、记录已落盘）已**合并**进下面那条 |
| `Quarantine_LockedForReading_ShouldRecordPerItemErrorInsteadOfThrowing`（`Skip` 标记的目标行为） | **取消 Skip 并改名**为 `Quarantine_LockedForReading_RecordsPerItemError_WithoutLosingSourceOrRecord`，成为**正式回归守卫** |

> 修复前的"抛出"行为**没有**因此丢失证据：第 33 轮的 RED 日志与本包 `red/02-a16-red.log` 都留着。

---

## 六、回退方法（一行）

```powershell
$run = 'C:\Users\Administrator\.codex\worktrees\ed41\松光启动器\docs\market-execution\MR04-locked-file-hash-fix\20261003-230000Z'
Copy-Item "$run\red\FileRecoveryService.cs.before" 'D:\松光启动器\dev\PineglowLauncher\Pineglow.Launcher.Services\FileRecoveryService.cs' -Force
```

回退后需把测试侧也回退（恢复那条 `Skip` 用例），否则目标用例会以 `IOException` 失败。
`red/FileRecoveryService.cs.before` 与 `green/production-diff.txt` 都在本包证据里。

---

## 七、验证

| 检查 | 结果 |
|---|---|
| 本组用例 | **4 通过 / 0 失败 / 0 跳过** |
| C# 全量 | **769 通过 / 0 失败 / 0 跳过**（跳过数从 1 归零） |
| Node 全量 | **230 通过 / 0 失败**（18 个文件） |
| RED | 回退修复后目标用例以 `IOException` 失败（证据在 `red/02-a16-red.log`） |

---

## 八、风险与未覆盖

| 项 | 说明 |
|---|---|
| 行为变化面 | 只在"哈希阶段读不了文件"这一条路径上：**从抛异常改为逐项失败**。 |
| **调用方已核实（不是推测）** | `LocalApiService.cs:2499` 的调用点**本来就是按逐项 `State` 判定的**：<br>`FileRecoveryEntry[] failed = record.Files.Where(x => x.State == "failed").ToArray();`<br>`if (failed.Length > 0 \|\| !record.Files.Any(x => x.State == "quarantined"))` → `throw new InvalidOperationException(... x.Error ...)`<br>**即：修复前这条路根本走不到**（异常先冲出），调用方精心写的逐项报错形同虚设。**修复让调用方本来的意图真正生效。** |
| 另一个调用点 | `ModReplacementService.cs:141`（MR05 的替换路径）—— 其行为由 MR05 的用例覆盖，本包改动后**全量 769/0 仍通过** |
| 只读文件 | 同一分支现在也会给出可读原因；**未单独加用例**（只读文件在 Windows 上通常仍可读，未必触发该分支） |
| 磁盘不足 | 仍未构造真实用例（登记表 A2/A3 的剩余项） |
| 真实 GUI | 未验证（需桌面会话） |

---

## 九、证据清单

```
red/FileRecoveryService.cs.before          修复前备份（回退用）
red/01-after-fix-before-test-update.log    修复后、测试未同步时的失败（证明过渡用例确实在钉住旧行为）
red/02-a16-red.log                         RED：回退修复 → 目标用例以 IOException 失败
green/01-edge-after-fix.log                测试同步后 4 通过
green/02-edge-final.log                    最终 4 通过 / 0 跳过
green/03-csharp-full.log                   769 通过 / 0 失败 / 0 跳过
green/04-node-full.log                     230 通过 / 0 失败
green/production-diff.txt                  生产代码 diff 记录
manifest.json
```

**本报告不宣布阶段放行、不提交、不发布。**
