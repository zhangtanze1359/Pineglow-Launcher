# MR04 补测 · 恢复区边界情形（缺口登记表 A2/A3）——**并实测出一个真实缺陷**

- **run-id**：`20261003-220000Z`
- **性质**：关闭 A2/A3 的三项（重解析点拒绝、文件被占用、保留期策略），并**新登记一个真实缺陷**
- **依据**：`MR04-recoverable-mod-delete/20261002-193000Z/deliverable.md` §六 的原话
- **最终状态**：**证据就绪**（4 用例通过 + 1 用例 Skip 标记目标行为；**3 个 RED 全部成立**）

---

## 一、MR04 当时说"做不了"的那一项，其实做得到

> MR04 §六 原话："符号链接/重解析点拒绝：代码已实现（文件自身与祖先目录都检查），但**没有测试覆盖** ——
> 在 Windows 上创建符号链接需要特权或开发者模式，**本机未验证**"

**本轮实测：两项都验证了。**

| 子项 | 怎么做的 | 结果 |
|---|---|---|
| **祖先目录是重解析点** | 用 **目录联接（`mklink /J`）** —— 它**不需要管理员权限**（需要特权的是符号链接） | ✅ 创建成功（`True`）；经由联接访问的 jar 被拒绝，`State=failed`、`Error=拒绝隔离符号链接/重解析点` |
| **文件自身是符号链接** | `File.CreateSymbolicLink` | ✅ **本机创建成功**；该文件被拒绝，且**真实文件未被移动** |

**所以 MR04 把"本机未验证"归因于"需要特权"是不完整的**：目录联接不需要特权，足以覆盖**祖先检查**这条路径。

---

## 二、**实测出的真实缺陷**（不是覆盖缺口，是行为与文档不符）

把文件以 `FileShare.None` 打开后调用 `QuarantineAsync`，异常**不是**在"移动/复制"阶段被逐项捕获，
而是在更早的**哈希阶段**就抛了出来：

```
System.IO.IOException : The process cannot access the file '...locked.jar' because it is being used by another process.
  at FileRecoveryService.HashFile(String path)      // 第 399 行 File.OpenRead
  at FileRecoveryService.QuarantineOne(...)         // 第 136 行 entry.Sha256 = HashFile(...)
  at FileRecoveryService.QuarantineAsync(...)       // 第 93 行
```

而 MR04 §六 写的是"**文件被占用：会走 IOException 回退并逐项报错**" ——
**那只覆盖了移动阶段（第 146 行的 try/catch），哈希阶段没有任何保护。**

**影响**：调用方（删除 Mod 的路由）会拿到一个**未处理的 `IOException`**，而不是一条"逐项报错"的记录。
**修法很小**：把第 136 行的 `HashFile` 纳入与第 146 行相同的
`try/catch (IOException | UnauthorizedAccessException)`，失败时写 `State="failed"` + `Error`。

**本包不改产品代码**（这会改变错误语义，应作为独立小项决定），而是：
1. 用一个用例**钉住当前真实行为**，并断言抛出之后仍然成立的两条**安全底线**；
2. 用一个 **`Skip` 标记的用例**写出**目标行为** —— 修好后移除 Skip，它就成为回归守卫。

---

## 三、四个通过的用例

| # | 用例 | 断言 |
|---|---|---|
| 1 | `Quarantine_RejectsFileReachedThroughJunctionAncestor` | 联接祖先 → `failed` + 含"重解析点"；**源文件原样保留**；整条记录 `failed`（不假装成功） |
| 2 | `Quarantine_RejectsSymlinkFile_WhenEnvironmentAllowsCreatingOne` | 符号链接文件 → 拒绝；**真实文件未被移动**。若本机无法创建符号链接，**如实报告该子项未验证**（不写成通过） |
| 3 | `Quarantine_LockedForReading_ThrowsToday_ButNeverLosesTheSourceOrTheRecord` | 当前行为**抛出 `IOException`**；**源文件仍在且内容不变**；**操作记录已落盘**（先落记录的设计在这里救了一次） |
| 4 | `Retention_ExpiredRecordsAreStillListed_NoSilentCleanup` | `GetExpiresAt = CreatedAt + 30 天`；**过期记录仍被列出**（本项目决策是只提示、不自动清理）；过期后**恢复能力不消失** |
| — | `Quarantine_LockedForReading_ShouldRecordPerItemErrorInsteadOfThrowing` | **Skip**：目标行为（逐项记录失败而非抛出） |

---

## 四、三个 RED（证明测试有牙齿）

| RED | 中性化/注入什么 | 结果 |
|---|---|---|
| **1** | 重解析点检查 `if (IsReparsePoint(...) \|\| HasReparseAncestor(...))` → `if (false)` | **2 失败**（用例 1、2）；`entry.State = quarantined` —— 文件被移走而不是被拒绝 |
| **2a** | 保留期常量 `AddDays(30)` → `AddDays(0)` | **1 失败**（用例 4 的常量断言） |
| **2b** | 在 `ListAsync` 注入**静默清理**（丢弃已过期记录） | **1 失败**（用例 4 的"过期仍被列出"断言）—— 证明它能抓住**未来**有人加静默删除 |
| **3** | 去掉"先落记录" | **1 失败**（用例 3 的"记录已落盘"安全底线） |

**用例 3 的"当前行为"断言是过渡性的**：修好缺陷后它应当被更新（把 Skip 的那条转为正式守卫）。
这一点已写在用例注释里，避免以后误读。

---

## 五、**方法学教训：RED 假通过**（必须记录）

RED-1 的**第一次**运行报告"4 通过 / 0 失败" —— 看起来像"测试没牙齿"。排查发现：
**`dotnet test` 没有重建程序集**，我改的实现根本没进到被测二进制里（日志里错误文案仍是旧的）。

**处置**：改成 **先显式 `dotnet build`，再 `dotnet test --no-build`**，RED-1 立刻变成 **2 失败**（`State = quarantined`）。
此后所有 RED 都走这条路径。

> 这与第 32 轮 MR05 的教训同源：**改完实现后不能假定测试会自动重建**。
> 两次都留下了失败/误导日志作为证据（`red/01-red-reparse.log` 是假通过的那次）。

---

## 六、实现文件未被留下净改动

三次 RED 都改了 `FileRecoveryService.cs`，逐字节核对：

| 项 | 值 |
|---|---|
| 最终 SHA256 | 与开工前备份 `FileRecoveryService.cs.mr04a` **一致**（每次都打印 `还原 = True`） |
| 残留 RED 标记 | 无 |

---

## 七、验证

| 检查 | 结果 |
|---|---|
| 本组用例 | **4 通过 / 0 失败 / 1 跳过** |
| C# 全量 | **769 通过 / 0 失败 / 1 跳过**（765 + 4 + 1 跳过） |
| Node 全量 | **230 通过 / 0 失败**（18 个文件） |
| 实现文件 | 逐字节还原 |

---

## 八、实际改动

| 文件 | 改动 |
|---|---|
| `dev/PineglowLauncher.Tests/FileRecoveryEdgeCaseTests.cs` | **新增**（5 个用例，其中 1 个 Skip） |
| `scripts/source-snapshot-inputs.txt` | 登记新测试（类别 B 35→36） |
| `docs/market-execution/gap-register.md` | A2/A3 三项关闭；**新增 A16：加锁文件在哈希阶段抛出的缺陷** |
| `docs/market-task-plan-2026-10-03.md` | MR04 行补记 |

**生产代码零净改动。**

---

## 九、仍未覆盖

| 项 | 说明 |
|---|---|
| **磁盘不足** | 仍**未构造真实用例**（无法可靠制造"磁盘满"）。本组用"文件被占用"覆盖了同一回退分支的失败路径，但**不是**磁盘满。 |
| 磁盘不足下的 `CopyThenRemove` 半途失败 | 未覆盖 |
| 真实 GUI 的删除/恢复面板 | 未验证（需桌面会话） |
| 符号链接用例的环境依赖 | 本机可创建；**在无开发者模式的机器上该用例会走"如实报告未验证"分支**（不失败） |

---

## 十、证据清单

```
red/01-red-reparse.log            RED-1 **假通过**的那次（构建未重建）—— 保留作为教训证据
red/03-red-reparse-debug.log      RED-1 显式 build 后：2 失败，State=quarantined
red/04-red-retention-constant.log RED-2a：保留期常量 → 1 失败
red/05-red-silent-cleanup.log     RED-2b：注入静默清理 → 1 失败
red/06-red-record-first.log       RED-3：去掉先落记录 → 1 失败
green/FileRecoveryService.cs.mr04a  实现文件备份
green/01-edge-first.log           首次运行（3 通过 / 1 失败 —— 失败暴露了缺陷）
green/02-edge-second.log          重写用例后 4 通过 / 1 跳过
green/03-edge-final.log           最终 4 通过 / 1 跳过
green/04-csharp-full.log          769 通过 / 0 失败 / 1 跳过
green/05-node-full.log            230 通过 / 0 失败
manifest.json
```

**本报告不宣布阶段放行、不提交、不发布。**
