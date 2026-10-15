# 证据与文案更正 — FINAL-REMEDIATION（R4 / F-05）

本文件**只更正说法与登记差异**，不改写任何历史证据。
旧 manifest、旧 log、旧 RED 一律保持原字节；更正采用新增文件（本文件）的方式。

---

## 一、两处清单不一致（F-05，独立验收发现）

独立验收在 **59 份 manifest / 784 项文件记录**中发现 2 项与当前字节不符。
本轮**没有改写**这两个文件，也**没有**重算那两个 manifest 后宣称"历史一直一致"。
下面是本轮实测值，供独立复验直接比对。

| 清单（相对 `docs/market-execution`） | 文件 | 清单记录 | 实测（本轮） | 判定 |
|---|---|---|---|---|
| `MG0-verification-package-20261003/manifest.json` | `drift-notes.md` | 3114 字节 / `1F7197B3…F0BE88` | **4088 字节** / `C55CE93981D8D078CD66450D91AAF5571145731049B024433301EB955277CF6B` | 字节已变，清单未同步 |
| `MG2-verification-package-20261003/manifest.json` | `verify-mg2-full.log` | 41770 字节 / `B99A6D92…813C31` | 41770 字节 / `BE2E432EADE77D436B39A1706248B9C90D9280CA98DFF8C57EEE4A5C5F5E2F95` | 大小相同、字节不同 |

- 来源：两者都在 `docs/market-execution`（交付根），不是源目录 `D:\松光启动器`。
- 差异性质：**只证明"清单与当前字节不符"**，不证明篡改动机，也不证明内容造假。
- 处理方式：登记进 `known-mismatches.json`；全层级审计脚本（`audit-all-manifests.py`）把它们单列为
  `knownMismatches` 并**继续对其它任何异常返回非零**。因此"审计 exit 0"只表示**除这两项已知差异外**没有新问题，
  不表示这两项已经消失。
- 复现命令：

```powershell
Get-FileHash 'C:\Users\Administrator\.codex\worktrees\ed41\松光启动器\docs\market-execution\MG0-verification-package-20261003\drift-notes.md' -Algorithm SHA256
Get-FileHash 'C:\Users\Administrator\.codex\worktrees\ed41\松光启动器\docs\market-execution\MG2-verification-package-20261003\verify-mg2-full.log' -Algorithm SHA256
```

---

## 二、GroupName 文案更正（F-05 附项）

- 错的说法（出现在上轮 `MR03-world-backup-scope-fix` 的 deliverable 与源码注释里）：
  「GroupName 来自备份**文件名解析**（如 `worldA-20261003-030238.zip` → `worldA`）」。
- 实际来源：`SaveBackupService.ToBackupInfo`（`SaveBackupService.cs:144-149`）取的是
  **备份文件相对备份根的父目录名**（`<备份根>/<世界名>/<时间戳>.zip` → `<世界名>`）；目录名为空时才回退为「存档」。
- 本轮已改：`LocalApiService.cs` 中两处注释（列表过滤与跨世界恢复）改为描述真实来源。
  **备份/恢复的行为没有改变**，改的只是说法。

---

## 三、测试数量口径更正

| 说法 | 实际 |
|---|---|
| 「C# 全量 **835 通过**」（实现方声明） | 835 = 830（可在原生产实现上安全复跑的用例）**+ 5 个会真实触碰 Windows 凭据的配置用例**。独立验收只能证实 830；5 个必须先在隔离后端下才能跑。 |
| 「原版 835/835 独立通过」 | **不成立**。830 是原生产实现上的安全回归；5 项是替换凭据后端后的取证/隔离测试，**不能合并计数**。 |
| 本轮全量 | **875 通过 / 0 失败 / 0 跳过**（= 835 原有 + 20 本轮新增 + 20 个独立验收探针）；其中配置 5 项已在内存后端下运行并断言原生调用 0 次。 |

---

## 四、运行态守卫的逐路由现状（R3 遗留项登记）

R3 只授权把**只读 Mod 列表**从"并发启动守卫"里放出来。源码扫描发现**同类越界守卫**还有 6 处
（都是只读路由，却在实例运行时返回 400）。本轮**没有擅自修改**它们，按交付要求登记如下：

| 路由 | 处理器 | 类别 | 本轮动作 |
|---|---|---|---|
| `GET /api/instances/{id}/mods` | `HandleListModsAsync` | 只读 | **已放开**（F-04，探针证明运行中返回 200） |
| `GET /api/instances/{id}/saves` | `HandleListSavesAsync` | 只读 | 未改，登记（改它会变更既有产品策略） |
| `GET /api/instances/{id}/saves/{folder}/backups` | `HandleListSaveBackupsAsync` | 只读 | 未改，登记 |
| `POST /api/instances/{id}/saves/{folder}/open` | `HandleOpenSaveDirectoryAsync` | 打开资源管理器（非数据变更） | 未改，登记 |
| `GET /api/instances/{id}/bisect` | `HandleBisectPendingAsync` | 只读 | 未改，登记 |
| `GET /api/instances/{id}/resourcepacks` | `HandleListResourcePacksAsync` | 只读 | 未改，登记 |
| `GET /api/instances/{id}/shaderpacks` | `HandleListShaderPacksAsync` | 只读 | 未改，登记 |
| `POST /launch`、`POST /mods/{f}/toggle|delete|replace`、`POST /saves/{f}/delete`、`POST .../backups/{b}/restore`、`POST /delete`、`POST /diagnose`、`POST /bisect/*` | 各自处理器 | 变更/启动/危险操作 | **守卫保留**（探针证明仍返回 400） |

如果产品决定"运行中也要能看存档/资源包列表"，那是**产品策略变更**，需要单独授权与一轮独立验收。
