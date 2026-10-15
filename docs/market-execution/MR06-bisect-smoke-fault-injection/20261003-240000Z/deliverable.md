# MR06 补测 · `--bisect-smoke` 故障注入（缺口登记表 A5）

- **run-id**：`20261003-240000Z`
- **性质**：把 MR06 的"文件操作失败"覆盖从**单元层**推到**端到端**
- **依据**：`MR06-bisect-file-operation-failures/20261002-211500Z/deliverable.md` §六 的原话：
  "**`VerifyHost --bisect-smoke` 未按计划扩展故障注入** | 计划要求『以故障注入补充现有 8→4→2→1 正常路径』。
  本包只**确认了既有 smoke 仍 exit 0**（含不带 `step` 的兼容路径）"
- **最终状态**：**证据就绪**（smoke 从 10 项检查扩到 **19 项**；RED 成立；MG1 复验脚本实跑通过）

---

## 一、做了什么

在 `VerifyHost --bisect-smoke` 里**新增故障注入阶段**，直接验证 MR06 的核心契约：

> **一个文件操作失败时，API 不得仍报告"本轮就绪"。**

**注入方式（关键设计）**：为每个"待禁用目标"预先建一个**同名目录**
（`modNN-1.0.0.jar.disabled` 作为**目录**存在），使"把 `.jar` 改名为 `.jar.disabled`"这一步**真实失败**。

为什么这样注入而不是"删掉文件"或"加锁文件"：

| 备选 | 为什么不用 |
|---|---|
| **删掉文件** | 候选清单由 `GetModsAsync` 计算，删文件会**同时改变候选数量**，失败原因就不再是"文件操作失败"（混淆变量） |
| **`FileShare.None` 加锁** | **实测无效**：本机对加锁文件执行改名仍然成功（见 §五），注入不会触发失败 |
| **目录占位**（采用） | 候选清单只枚举**文件**（`Directory.EnumerateFiles`），目录不影响它；而改名目标被目录占住 → 文件操作**真实失败** ✓ 且与"Mod 数量变化"解耦 |

**顺序无关性**：为**全部 8 个**目标都建占位目录，因此无论被禁用的是哪一半，注入都必然生效
（实测被禁用半区在这一阶段是 `mod01-04`，与正常路径的 `mod05-08` 不同 —— 若不这样做，测试会因顺序变化而假通过）。

---

## 二、新增的 9 项断言（全部针对契约）

| 断言 | 结果 |
|---|---|
| 文件操作失败必须返回 **409** | ✅ 实测 409 |
| 响应必须 `ok=false` | ✅ |
| **不得报告本轮就绪**（`waitingForReport=false`） | ✅ |
| 必须报出可读的 `recoveryError` | ✅ 实测：`mod01-1.0.0.jar → 当文件已存在时，无法创建该文件。；mod02…；mod03…；mod04…` |
| 必须标记 `needsRecovery=true` | ✅ |
| 必须**逐项**列出失败文件 | ✅ |
| 失败后不得声称已禁用半区（`disabledHalf` 为空） | ✅ |
| 清理注入物后可正常放弃会话 | ✅ |
| 清理后磁盘无残留禁用文件 | ✅ |

服务端对应的实现（**不是新写的**，是 MR06 已有的契约）：
`ModBisectService.RecordStepResults` 在有失败项时设置 `NeedsRecovery=true`、`RecoveryError=…`、
**`WaitingForReport=false`**、`DisabledHalf` 清空；`start` 路由随即返回 409 并附"禁用半区失败，本轮未就绪。"。

---

## 三、RED（证明这些断言有牙齿）

**中性化方式**：把 `ModBisectService.RecordStepResults` 里的 `if (failed.Count > 0)` 改成 `if (false)`
（即"忽略文件操作失败"）。

| | 注入后 `start` 的状态码 | `recoveryError` | 注入断言 |
|---|---|---|---|
| **RED** | **200** | **（空）** | **7 项失败** |
| **GREEN** | **409** | 逐项列出 | **9 项全过** |

RED 的实测输出正是 MR06 契约被破坏的样子：**文件操作明明失败了，API 却返回 200 并声称本轮就绪**。
实现文件随后**逐字节还原**（`还原 = True`、无 RED 标记残留）。

---

## 四、在里程碑复验脚本里实跑（最强的验证）

`scripts/verify-mg1.ps1` 会调用 `VerifyHost.exe --bisect-smoke`。本包**不带 `-SkipTests` 完整实跑**：

```
MG1 EXIT = 0
一致=65  本里程碑内无法解释的漂移=0  被后续里程碑覆盖的历史记录=68  文件缺失=0
[PASS] fault-injection: 文件操作失败必须返回 409，实际 409
…（9 项注入检查全部 PASS）
[BisectSmoke] PASS
C# exit=0  Node exit=0  bisect-smoke exit=0（都应为 0）
```

> 顺带发现：带 `-SkipTests` 时脚本**不会跑** smoke，而日志里仍会出现各包 manifest 自报的
> `bisectSmoke = exit 0` —— 那是**历史自报值**，不是本次结果。核对时不要被它误导（本包把两者都记了）。

---

## 五、顺带实测到的一条 Windows 行为（与 A16 相关）

为做注入，我先试了"用 `FileShare.None` 独占占用待改名文件"。**实测改名仍然成功**，
`bisect/test` 照常返回 200。也就是说：

> **在 Windows 上，对一个以 `FileShare.None` 打开的文件执行改名（`File.Move`）是可以成功的**
> —— 这与第 33 轮 A16 的发现一致：当时 `QuarantineAsync` 失败在**哈希（`File.OpenRead`）**，
> 而不是失败在移动。**"文件被占用"并不等于"改名会失败"。**

这条行为对**测试设计**很重要：想制造"文件操作失败"，用"加锁"是不可靠的，用"目标被占住/文件缺失"才可靠。

---

## 六、验证

| 检查 | 结果 |
|---|---|
| `--bisect-smoke`（扩展后） | **exit 0**，**19 项 PASS / 0 FAIL**（原 10 项 + 新增 9 项） |
| C# 全量 | **769 通过 / 0 失败 / 0 跳过** |
| Node 全量 | **230 通过 / 0 失败**（18 个文件） |
| `verify-mg1.ps1`（含 smoke，完整实跑） | **exit 0** |
| 实现文件 | RED 后逐字节还原 |

---

## 七、实际改动

| 文件 | 改动 |
|---|---|
| `dev/PineglowLauncher/VerifyHost/BisectSmoke.cs` | **新增故障注入阶段**（+ 解析 `ok`/`needsRecovery`/`recoveryError`/失败文件数；+ `PostJsonAllowFailure` 允许 409） |
| `docs/market-execution/gap-register.md` | A5 标记为**已关闭** |
| `docs/market-task-plan-2026-10-03.md` | MR06 行补记 |

**该文件是已跟踪文件**，走 `git ls-files` 进快照，**不需要**加入白名单（白名单只登记未跟踪输入）✓
—— 已核实 `git ls-files -- dev/PineglowLauncher/VerifyHost/` 列出它。

**生产代码零改动**（`BisectSmoke.cs` 属测试宿主，不改产品行为）。

---

## 八、仍未覆盖

| 项 | 说明 |
|---|---|
| **恢复阶段**失败（`RecordRestoreResults`） | 未注入。注意它的失败分支**不**清 `WaitingForReport`（只设 `needsRecovery`/`RecoveryError`）—— 是否需要一并清，属于**行为问题**，本包只记录，不擅自改 |
| 其它文件操作失败形态 | 只注入了"改名目标被占住"一种；磁盘满、权限不足未覆盖 |
| 真实 GUI | 未验证（需桌面会话） |
| 真实游戏 | 未验证（需真实 Minecraft） |

---

## 九、证据清单

```
red/ModBisectService.cs.before              中性化前的实现备份
red/01-smoke-red.log                        RED：忽略失败 → start 返回 200 + recoveryError 空 → 7 项断言失败
green/01-smoke-with-injection.log           首次尝试（硬编码文件名 → 前置条件失败，保留作过程证据）
green/02-smoke-with-injection.log           第二次（加锁注入无效 → 实测到 §五 的 Windows 行为）
green/03-smoke-injection.log                目录占位注入生效：19 项 PASS
green/04-smoke-final.log                    最终 GREEN：19 项 PASS / 0 FAIL
green/05-csharp-full.log                    769 通过 / 0 失败
green/06-node-full.log                      230 通过 / 0 失败
green/07-mg1-recheck.log                    MG1（-SkipTests）—— 注意 smoke 未跑
green/08-mg1-full-with-smoke.log            MG1 完整实跑：含 9 项注入检查，exit 0
manifest.json
```

**本报告不宣布阶段放行、不提交、不发布。**
