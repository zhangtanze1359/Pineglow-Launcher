# MR20 补测 · 配置备份的**主备轮转留证**（缺口登记表 A15 的**剩余项**，现已关闭）

- **run-id**：`20261003-480000Z`
- **性质**：用一个**窄测试缝**解锁此前"必须写真实凭据"而无法验证的那一半
- **依据**：`MR20-config-preservation/20261003-350000Z` §八：
  "**「主备切换留证」（`.bak`/`.prev` 轮转）未测** —— 必须经 `SaveAsync`，那会写真实凭据（见 A19）"
- **最终状态**：**证据就绪**（5 个用例；RED 成立；**A15 完全关闭**）

---

## 一、关键思路：轮转是**纯文件操作**，不该被凭据绑住

`RotateBackupBeforeWrite` 只做两件事（读实现得出）：

```
1. 若 .bak 存在   → 把 .bak 复制到 .prev（更旧的一代）
2. 若 config.json 存在 → 把它复制到 .bak（次新的一代）
```

它被 `SaveCore` 调用，而 `SaveCore` **还会写 `SecretStore`（真实凭据存储）** ✗ ——
所以"整条 `SaveAsync` 路径"在测试里不安全。

**但轮转本身不碰任何凭据** ✓ → 本轮加一个**窄测试缝**：

```csharp
internal static void RotateBackupBeforeWriteForTest(string configPath) => RotateBackupBeforeWrite(configPath);
```

**这比给 `SecretStore` 加缝小得多，也不触及任何凭据代码** ✓。
（我此前把 A15 剩余项登记为"需要你决定是否加 `SecretStore` 缝"——**本轮找到了更小、无需授权的路径**，因此把它关掉。）

---

## 二、5 个用例（把轮转语义逐条钉住）

| # | 用例 | 断言 |
|---|---|---|
| ① | `FirstRotation_CreatesBakOnly_WithTheCurrentContent` | 第一次轮转：`.bak` = 现内容；**`.prev` 不存在**（还没有更旧的一代） |
| ② | `SecondRotation_KeepsBothGenerations` | 第二次：`.bak` = 新、`.prev` = 旧 ✓ |
| ③ | `ThirdRotation_KeepsOnlyTheTwoMostRecentGenerations` | 第三次：`.bak`=gen3、`.prev`=gen2（**gen1 被淘汰，符合两代设计**）；且轮转**不改动现配置本身** |
| ④ | `Rotation_WithoutConfigFile_CreatesNothing` | 没有 `config.json` → 不生成任何备份 |
| ⑤ | `Rotation_WhenBackupTargetIsUnusable_DoesNotThrow` | 把 `.bak` 占成**目录** → **不抛出** ✓（实现里"备份失败不影响主写入"）；且那个目录**原样保留**（不删别人的东西） |

实测输出：

```
[A15b] 第一次轮转：.bak=gen1，无 .prev
[A15b] 第二次轮转：.bak=gen2，.prev=gen1
[A15b] 第三次轮转：.bak=gen3，.prev=gen2（gen1 被淘汰，符合两代设计）
[A15b] 无 config.json 时不生成备份
[A15b] .bak 被占成目录时：未抛出 ✓
```

---

## 三、RED

| RED | 改什么 | 结果 |
|---|---|---|
| **1** | 去掉 `.prev` 那一代（只保留 `.bak`） | **2 失败** ✓（`SecondRotation_KeepsBothGenerations` 等） |

RED 后 `ConfigStore.cs` **逐字节还原**（并触碰 mtime ✓）。

---

## 四、验证

| 检查 | 结果 |
|---|---|
| 本组用例 | **5 通过 / 0 失败** |
| C# 全量 | **828 通过 / 0 失败 / 0 跳过**（823 + 5） |
| Node 全量 | **238 通过 / 0 失败**（20 个文件） |
| RED | 2 失败 ✓ |
| 还原 | 逐字节一致 ✓ |

---

## 五、实际改动

| 文件 | 改动 |
|---|---|
| `ConfigStore.cs` | **+窄测试缝** `RotateBackupBeforeWriteForTest`（生产行为不变） |
| `dev/PineglowLauncher.Tests/ConfigBackupRotationTests.cs` | **新增**（5 个用例） |
| `scripts/source-snapshot-inputs.txt` | 登记新测试（类别 B +1） |
| `docs/market-execution/gap-register.md` | **A15 完全关闭** |
| `docs/market-task-plan-2026-10-03.md` | MR20 行补记 |

---

## 六、仍未覆盖（如实列出）

| 项 | 说明 |
|---|---|
| **`SaveAsync` 的凭据写入路径** | **仍未测** —— 它写真实 `SecretStore`（A19 的隐患仍在）✓ 本轮只关掉了**轮转**那一半 |
| **端到端**（真正 `SaveAsync` 三次 → 三个文件世代） | 未做（同上原因） |
| 跨进程并发写 | 未覆盖（`ConfigGate` 是进程内锁） |
| 备份的**恢复**动作 | 未做（本项目没有"从 .bak 恢复配置"的入口，属功能缺失而非缺陷） |

> **口径**：本轮证明的是"**轮转逻辑本身**正确且失败安全"，**不是**"整条保存路径已被验证" ✓。

---

## 七、证据清单

```
green/ConfigStore.cs.fixed                测试缝后的参照
green/ConfigBackupRotationTests.cs.fixed  用例参照
green/02-rotation.log                     5 通过 / 0 失败（含实测语义）
green/03-csharp-full.log                  828 通过 / 0 失败
green/04-node-full.log                    238 通过 / 0 失败
red/01-red-no-prev-generation.log         RED：去掉 .prev → 2 失败
manifest.json
```

**本报告不宣布阶段放行、不提交、不发布。**
