# MG1 独立复验包 · 数据安全与 Bisect 恢复

- **范围**：MR03 / MR04 / MR05 / MR06 / MR07
- **证据根**：`C:\Users\Administrator\.codex\worktrees\ed41\松光启动器\docs\market-execution\`
- **源码根**：`D:\松光启动器`（1251 个未跟踪的用户资产全程未触碰）
- **本包状态**：**证据就绪，等待独立复验**
- **生成方式**：由 `scripts/verify-mg1.ps1` 从各包 `manifest.json` 与 `changed-files.sha256.txt` **实际读出**

---

## 一、一条命令跑完全部自动检查

本机只有 Windows PowerShell 5.1（没有 `pwsh`）：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\verify-mg1.ps1
```

它会：汇总五个包的 manifest → **跨全部 run 的证据漂移检查** → 重跑 C# / Node 全量 → **bisect 冒烟** → 打印复验边界。

本次实测输出（完整日志 `verify-mg1-full.log`）：

```
扫描到 90 条记录，覆盖 11 个 run
一致=42  本里程碑内无法解释的漂移=0  被后续里程碑覆盖的历史记录=49  文件缺失=0
已通过! - 失败: 0，通过: 725，已跳过: 0，总计: 725
ℹ tests 216   ℹ pass 216   ℹ fail 0
[BisectSmoke] PASS
C# exit=0  Node exit=0  bisect-smoke exit=0
```

---

## 二、本包的漂移检查比 MG2 的更强（也说明分工）

| | `verify-mg1.ps1`（本包） | `verify-mg2.ps1` |
|---|---|---|
| 扫描范围 | **全部 run**（10 个，86 条记录） | 仅本里程碑 4 个包 |
| 漂移分类 | "最新记录来自本里程碑" → 必须解释；"来自更晚里程碑" → 正常覆盖 | 仅本里程碑内比较 |
| 能发现 | 本里程碑内的漂移 | 本里程碑内的漂移 |

**分工**：两个脚本各自检查自己里程碑的**最新记录**。因此：

- MG1 脚本把 `scripts/source-snapshot-inputs.txt`、`CurseForgeRateLimitTests.cs` 的漂移归为"被后续里程碑覆盖"——因为它们的最新记录来自 MG2；
- 这两条漂移由 **MG2 的复验包**负责解释（见 `MG2-verification-package-20261003/drift-notes.md`）；
- **两个脚本刻意不共用代码**：让每个里程碑的证据自洽、已记录哈希保持稳定（改共用脚本会让已封存的哈希再次失效）。

---

## 三、五个包各自声称什么

| 包 | 聚焦测试 | 全量（当时） | bisect 冒烟 | git status |
|---|---|---|---|---|
| MR03 从列表移除（只改配置） | C# 7 / Node 7 | C# 635、Node 191（12 文件） | — | 1290 → 1292 |
| MR04 可恢复的 Mod 删除 | C# 11 / Node 7 | C# 646、Node 198（13 文件） | exit 0 | 1292 → 1297 |
| MR05 Mod 替换事务 | C# 9 / Node 11 | C# 655、Node 202（13 文件） | exit 0 | 1297 → 1300 |
| MR06 Bisect 文件操作失败 | C# 9 | C# 664、Node 202（13 文件） | exit 0 | 1300 → 1302 |
| MR07 Bisect 重启恢复 | C# 8 / Node 4 | C# 672、Node 206（14 文件） | exit 0 | 1302 → 1305 |

每个包目录下有：`deliverable.md`、`manifest.json`、`red/`（实现前真实失败）、`green/`（最终字节上的通过 + 回退备份 + 变更文件哈希 + git status 快照）。

---

## 四、复验方必须独立判断的事

1. **实现是否真的满足验收**：脚本不读代码，请按 `verification-checklist.md` 逐条抽查。
2. **RED 是否真实**：MG1 各包的 RED 基线都是"回退到上一个包的版本 + 必要的惰性注入缝"，
   每个包 `deliverable.md` 写明了构造方式与"为什么不能直接删文件"（删了只是编译错误）。
3. **真实环境行为**：真实 GUI / 真实游戏 / **跨卷恢复**等在本仓库环境无法覆盖。
4. **已知限制（照抄，不要省略）**：
   - **MR03 的"实例运行中不得移除"已在收尾 run `MR03-instance-running-guard/20261003-060000Z` 补上**
     （新增 `RunningInstanceRegistry`，带**自愈**：进程已死就自动清条目，避免陈旧条目永久挡住移除）。
     本包的复验范围已包含该 run（脚本清单里的 `MR03b`）。
     **仍未验证**：真实启动 Minecraft 的完整链路、启动器重启后的运行态（注册表是进程内内存态）。
   - MR04 的 30 天保留策略**没有自动清理**（只记录到期时间）。
   - MR06 的 `VerifyHost --bisect-smoke` 故障注入扩展**未做**（属该包欠交付，已记录在它的报告里）。
   - MR07 的"临时测试世界/实例副本"引导**未做**；"真实进程中断"是**模拟**的（清空内存会话）。

---

## 五、"通过"怎么记录

计划 §八：**"通过"必须分别记录 自动验证结果、独立复验结果、用户是否批准**。

- 自动验证：见 §一；
- 独立复验：**尚未进行**（本包是它的输入）；
- 用户批准：**尚未给出**。

执行者最多能写"证据就绪"；**历史放行不覆盖新 diff**。本包不宣布阶段放行、不提交、不发布。

---

## 六、本包文件

| 文件 | 用途 |
|---|---|
| `README.md` | 本文件 |
| `verification-checklist.md` | 逐包验收项 → 命令 → 期望 → 证据；MG1 风险清单逐条对到证据 |
| `verify-mg1-drift-check.log` | 只做漂移检查的运行日志 |
| `verify-mg1-full.log` | 含全量测试与 bisect 冒烟的完整日志 |
| `manifest.json` | 机器可读索引 |
| `scripts/verify-mg1.ps1` | 复验脚本（UTF-8 BOM，PowerShell 5.1 可直接跑） |
