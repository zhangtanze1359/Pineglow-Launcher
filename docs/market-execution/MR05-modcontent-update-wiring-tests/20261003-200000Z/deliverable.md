# MR05 补测 · `ModContentService` 更新接线的自动化测试（缺口登记表 A1）

- **run-id**：`20261003-200000Z`
- **性质**：**关闭缺口登记表的 A1** —— 一个会写文件的服务此前**没有任何测试覆盖**
- **依据**：`docs/market-execution/gap-register.md` §三 A1；
  `MR05-mod-replacement-transaction/20261002-201500Z/deliverable.md` 的"明确未做与未验证"：
  "`ModContentService` 的更新接线没有自动化测试"
- **最终状态**：**证据就绪**（4 个用例，RED 2 失败 / GREEN 4 通过）

---

## 一、为什么要补这一组（不是"为了覆盖率"）

| 事实 | 证据 |
|---|---|
| `ModContentService` **会写文件**（下载后导入或替换 Mod） | `ModContentService.cs:120-153`（`ImportMod` / 替换 / `WriteOnlineMetadata`） |
| 它有 **3 处生产调用点** | `InstancePackService.cs:248`、`LocalApiService.cs:4774`、`LocalApiService.cs:5055` |
| 它在 MR05 的"更新接线"上（事务化替换而非并列副本） | `ModContentService.cs:131-149` |
| **测试项目里对它 0 引用** | `gap-register.md` §八 抽查（命中数 = 0） |

它所属的 **MR05 在 MG1（数据安全）** 里，而 MG1 的门禁正是"**不可恢复误删、状态与磁盘不符**"。
一条**完全没被测试覆盖的更新接线**，正是这类风险的入口。

---

## 二、测的是哪条契约

MR05 加的接线（`ModContentService.cs` 第 131–149 行）：**同一个在线项目**再次安装时，
走 `ModReplacementService.ReplaceAsync` 做**事务化替换**：

1. **不产生并列副本**（不再出现 `xxx (1).jar`）；
2. **保留启用状态**（旧的是禁用，新的仍禁用）；
3. **旧版本进恢复区可撤销**；
4. 替换失败时**抛错且旧版本完好**（不丢数据）。

---

## 三、四个用例

| # | 用例 | 断言要点 |
|---|---|---|
| 1 | `Update_ExistingProject_ReplacesInPlace_KeepsDisabledState_AndOldVersionStaysRecoverable` | mods 目录**只有 1 个**该项目 jar；内容=新字节；**仍以 `.jar.disabled` 结尾**；`ListAsync` ≥1 条恢复记录；**恢复区里能找到旧版本的原字节** |
| 2 | `Update_WhenReplacementFails_Throws_AndLeavesOldVersionIntact` | 独占占用目标文件制造失败 → **必须抛错**；旧文件**仍存在且内容不变** |
| 3 | `Download_Sha1Mismatch_Throws_AndInstallsNothing` | SHA1 不符 → `InvalidDataException`；**不安装任何文件**、不改动已装文件 |
| 4 | `WithoutRecoveryService_UpdateFallsBackToSideBySideImport` | 不传 recovery → **并列导入 2 个**（把 MR05 之前的回退分支也写进测试，避免以后误改语义） |

**全部用例不发外网**：用只回固定字节的假 `HttpMessageHandler`。

---

## 四、RED → GREEN

**RED 构造方式**：把 MR05 的接线**中性化**（`if (_recovery != null && ...)` → `if (false && ...)`），
即退回 MR05 之前的"并列导入"行为。**不删文件、不改测试**。

| | 通过 | 失败 |
|---|---|---|
| **RED（接线中性化）** | **2** | **2** |
| **GREEN（还原后）** | **4** | **0** |

**RED 里通过的那 2 个必须说清楚——它们不是新能力的证据**：

| 用例 | 为什么 RED 下也通过 |
|---|---|
| `WithoutRecoveryService_...` | 它验证的正是**回退行为**，而 RED 基线就是回退行为 —— 所以它通过是**预期的**，恰恰说明该用例锁住了回退语义 |
| `Download_Sha1Mismatch_...` | 它验证的是**下载校验**，与更新接线无关 |

**失败的那 2 个**正是针对接线本身的用例（替换到位、失败不丢数据）→ 测试**有牙齿**。

---

## 五、实现文件未被留在改动状态（必须核对）

RED 需要临时改实现文件，因此逐字节核对还原：

| 项 | 值 |
|---|---|
| `ModContentService.cs` 还原后 SHA256 | `D39A5A2102049795940602D2071DAFFBF4C8A1F3DE3317705201ADC860FB55CC` |
| 与本轮开工前的备份一致 | **True** |

> 该文件相对 HEAD 本来就是 ` M`（MR05 那一轮改的），本包**没有**对它留下任何净改动。

---

## 六、验证

| 检查 | 结果 |
|---|---|
| 新增用例 | **4 通过 / 0 失败** |
| C# 全量 | **759 通过 / 0 失败**（755 + 4） |
| Node 全量 | **230 通过 / 0 失败**（18 个文件） |
| 实现文件 | 已逐字节还原 |

---

## 七、实际改动

| 文件 | 改动 |
|---|---|
| `dev/PineglowLauncher.Tests/ModContentUpdateWiringTests.cs` | **新增**（4 个用例） |
| `scripts/source-snapshot-inputs.txt` | 登记新测试（类别 B 33→34） |
| `docs/market-execution/gap-register.md` | A1 标记为**已关闭** |
| `docs/market-task-plan-2026-10-03.md` | MR05 行补记 |

**生产代码零净改动。**

---

## 八、仍未覆盖（不因本包而消失）

| 项 | 说明 |
|---|---|
| 真实网络下载 | 未验证（假处理器供字节）；真实 CDN/重定向/断流不在本包范围 |
| `InstallWithDependenciesAsync`（依赖递归安装） | **仍未覆盖** —— 本包只覆盖了 `DownloadAndInstallAsync`。它是 A1 的**剩余部分**，应作为后续小项 |
| 真实 GUI 中的更新流程 | 未验证（需桌面会话） |
| CurseForge 分支的 URL 解析 | 未覆盖（本包走 Modrinth 直链） |

---

## 九、证据清单

```
red/01-wiring-before.log                 RED 基线（2 失败 / 2 通过，含逐用例结果）
green/ModContentService.cs.mr05a         实现文件备份（RED 前）
green/01-wiring-first.log                首次运行 4 通过（GREEN 目标）
green/02-wiring-after.log                还原后 4 通过
green/03-csharp-full.log                 759 通过 / 0 失败
green/04-node-full.log                   230 通过 / 0 失败
manifest.json
```

**本报告不宣布阶段放行、不提交、不发布。**
