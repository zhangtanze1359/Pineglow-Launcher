# MG1 复验清单（逐条可执行）

> 用法：逐行照做。每行给出**要验什么**、**怎么验**、**期望看到什么**、**证据在哪**。
> 标 ⚠️ 的是"脚本验不了、必须人读代码/日志判断"的项。

---

## 零、前置

```powershell
cd D:\松光启动器
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\verify-mg1.ps1
```

期望：`C# exit=0  Node exit=0  bisect-smoke exit=0`、`本里程碑内无法解释的漂移=0`。

---

## 一、MG1 风险清单 → 证据映射（计划 §八 列的三条风险）

| 计划里的风险 | 本里程碑如何防住 | 复验命令 | 证据 |
|---|---|---|---|
| **不可恢复误删** | MR03"从列表移除"**只改配置、不碰磁盘文件**；MR04 删除改为**隔离到恢复区**（记录先行、copy→verify→delete 回退、30 天保留信息）；MR05 替换走**事务**（先隔离旧文件、失败可回滚） | `dotnet test --filter "FullyQualifiedName~InstanceRemovalTests\|FullyQualifiedName~FileRecoveryTests\|FullyQualifiedName~ModReplacementTests"` | `MR03…/green/02-instance-removal-after.log`、`MR04…/green/02-file-recovery-after.log`、`MR05…/green/02-mod-replacement-after.log` |
| **状态与磁盘不符** | MR04 恢复时**先看目标现状**（`already_restored` 优先于"存储副本是否存在"），并逐文件核对 SHA；MR05 用显式状态机（`replaced/staged_missing/hash_mismatch/target_missing/replace_in_progress/quarantine_failed/commit_failed/metadata_failed`）而不是布尔 | 同上 | 用例 `RestoreOne` 相关、`ModReplacementTests` 的哈希不匹配用例 |
| **恢复失败会话丢失** | MR06：**恢复逐项核对通过前不移除会话**，恢复失败返回 409 + `needsRecovery` 并保留会话；MR07：会话与基线（含 SHA）**落盘在游戏目录之外**，重启后可恢复或逐项列出冲突 | `dotnet test --filter "FullyQualifiedName~BisectFileOperationTests\|FullyQualifiedName~BisectRecoveryTests"` | `MR06…/green/02-bisect-io-after.log`、`MR07…/green/02-bisect-recovery-after.log` |
| **跨卷恢复**（MR04 实测项） | 恢复区与游戏目录可能不同卷，MR04 有一个**真实 C:↔D: 跨卷**用例 | 同上 | `FileRecoveryTests` 的跨卷用例 |
| **Bisect 端到端** | `VerifyHost --bisect-smoke`：真实 API 走完 start→test→narrow→finish→giveup 全流程 | `VerifyHost.exe --bisect-smoke --port 48171` | 本包 `verify-mg1-full.log` 里的 `[PASS]` 序列 |

---

## 二、逐包复验

### MR03 从列表移除（只改配置、不碰文件）

| # | 验什么 | 命令 / 位置 | 期望 |
|---|---|---|---|
| 3.1 | 聚焦用例 | `dotnet test --filter "FullyQualifiedName~InstanceRemovalTests"` | 7 通过 / 0 失败 |
| 3.2 | RED 口径 | `MR03…/red/01-instance-removal-before.log` | 6 失败 / 1 通过 |
| 3.3 | ⚠️ 移除不碰磁盘 | `MR03…/deliverable.md` + 用例 | 断言磁盘文件仍在 |
| 3.4 | **运行中不得移除**（收尾补交付） | `dotnet test --filter "FullyQualifiedName~InstanceRunningGuardTests"` | 7 通过 / 0 失败；RED 基线 4 失败 / 3 通过 |
| 3.5 | ⚠️ 自愈（不留陈旧条目） | 用例 `Registry_SelfHeals_WhenTheProcessHasAlreadyExited`、`RemovingAnInstanceWhoseGameAlreadyDied_IsNotBlockedByAStaleEntry` | 进程已死 → 自动清条目，不永久挡住移除 |
| 3.6 | ⚠️ 仍未验证 | `MR03-instance-running-guard…/deliverable.md` §六 | 真实启动 Minecraft 的完整链路、启动器重启后的运行态（注册表是进程内内存态） |

### MR04 可恢复的 Mod 删除

| # | 验什么 | 命令 / 位置 | 期望 |
|---|---|---|---|
| 4.1 | 聚焦用例 | `dotnet test --filter "FullyQualifiedName~FileRecoveryTests"` | 11 通过 / 0 失败 |
| 4.2 | RED 口径 | `MR04…/red/01-file-recovery-before.log` | 6 失败 / 4 通过 |
| 4.3 | 跨卷恢复 | 用例（真实 C:↔D:） | 通过 |
| 4.4 | ⚠️ 记录先行 | `FileRecoveryService.QuarantineAsync` | 先写记录再动文件；copy→verify→delete 回退 |
| 4.5 | ⚠️ 30 天保留**不自动清理** | `GetExpiresAt` | 只算到期时间，无自动删除任务 |

### MR05 Mod 替换事务

| # | 验什么 | 命令 / 位置 | 期望 |
|---|---|---|---|
| 5.1 | 聚焦用例 | `dotnet test --filter "FullyQualifiedName~ModReplacementTests"` | 9 通过 / 0 失败 |
| 5.2 | RED 口径 | `MR05…/red/01-mod-replacement-before.log` | 6 失败 / 3 通过 |
| 5.3 | ⚠️ 故障注入用测试缝 | `ModReplacementService.BeforeCommitForTest` / `BeforeMetadataWriteForTest` | 第一版故障注入（在 sidecar 路径放目录）无效，已改为测试缝 |
| 5.4 | ⚠️ 在线更新接线**无自动化测试** | `ModContentService` | 需要 fake HTTP 通道；该包如实记录了这一点 |

### MR06 Bisect 文件操作失败

| # | 验什么 | 命令 / 位置 | 期望 |
|---|---|---|---|
| 6.1 | 聚焦用例 | `dotnet test --filter "FullyQualifiedName~BisectFileOperationTests"` | 9 通过 / 0 失败 |
| 6.2 | RED 口径 | `MR06…/red/01-bisect-io-before.log` | 7 失败 / 2 通过 |
| 6.3 | 恢复失败**保留会话** | `ModBisectService` 的 `NeedsRecovery` | 恢复核对通过前不移除会话 |
| 6.4 | ⚠️ **欠交付** | `MR06…/deliverable.md` | `VerifyHost --bisect-smoke` 的故障注入扩展**未做** |

### MR07 Bisect 重启恢复

| # | 验什么 | 命令 / 位置 | 期望 |
|---|---|---|---|
| 7.1 | 聚焦用例 | `dotnet test --filter "FullyQualifiedName~BisectRecoveryTests"` | 8 通过 / 0 失败 |
| 7.2 | RED 口径 | `MR07…/red/01-bisect-recovery-before.log` | 8 失败 / 0 通过 |
| 7.3 | 落盘在游戏目录之外 | `BisectRecoveryStore` | `<配置目录>/bisect/<实例>.json`，原子写入 |
| 7.4 | 冲突**逐项列出**、不靠文件名猜 | 用例 `Continue_WithDiskMismatch_ListsConflictsAndRefuses` | 409 + `conflicts[]` |
| 7.5 | 恢复**只碰本会话管理的文件** | 用例 `ResumeRestore_DoesNotEnableUserDisabledMods_AndDoesNotDeleteNewFiles` | 用户禁用项仍禁用、后加入文件原样保留 |
| 7.6 | ⚠️ **欠交付** | `MR07…/deliverable.md` §六 | "临时测试世界/实例副本"的引导**未做**；"真实进程中断"是**模拟**的（清空内存会话），不是真杀进程 |

---

## 三、复验结论模板（照抄，不要省略"未通过"项）

```
自动验证：C# 718/0、Node 216/0、bisect-smoke PASS、本里程碑内漂移 0（脚本 exit 0）
独立复验：<逐条填 §一、§二 的结果；任何 ⚠️ 项都要写是否抽查了代码>
未通过项：<逐条列；没有就写"无">
未验证项：<照抄各包 deliverable.md 的"明确未做与未验证"，特别是 MR03 运行守卫、
          MR06 冒烟故障注入、MR07 副本引导与真实进程中断>
用户批准：<未给出 / 已给出（附原话）>
```

**注意**：MG1 的放行条件是"数据操作达到本范围验收条件"。
MR03 的"实例运行中不得移除"这一条**没有实现**，因此该条件是否达成需要你判定，
不能由执行者代填"已达成"。
