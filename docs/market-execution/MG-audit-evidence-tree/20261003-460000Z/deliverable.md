# 证据树交叉一致性审计（第 57 轮新增的验收侧工具）

- **run-id**：`20261003-460000Z`
- **性质**：新增**验收侧**工具；一次就抓出 **18 处**证据内部不一致并修掉
- **依据**：`gap-register.md` A 类已全部关闭，剩余两个半项都需你决策或无法模拟 → 转向**证据链自身的自洽性**
- **最终状态**：**证据就绪**（新审计 7 项全过；台账审计 exit 0）

---

## 一、为什么要做这个审计（现有工具的空档）

| 工具 | 检查什么 | **不**检查什么 |
|---|---|---|
| `audit-plan-ledger.ps1` | 台账 ↔ 证据目录（状态词、可定位、孤儿、run-id） | 证据目录**内部**是否自洽 |
| `verify-mg*.ps1` | 里程碑 ↔ 文件哈希（漂移/缺失） | manifest 里记的哈希是否与目录里**真实文件**一致 |
| **本包新增** `audit-evidence-tree.ps1` | **manifest ↔ 目录内实际文件** | — |

**要抓的错误类型**：本会话反复出现同族问题 ——
"先写 manifest、之后又改了 deliverable"（哈希过期）、"忘写 changed-files"、"索引数字没同步"。
这些都是**证据内部**的不一致，前两个工具都看不见 ✗。

---

## 二、新增 `scripts/audit-evidence-tree.ps1`（7 项检查）

1. 每个 run 目录必须同时有 `deliverable.md` 与 `manifest.json`；
2. `manifest.json` 必须能解析，且 `runId` 等于目录名；
3. `files[]` 里的 sha256 必须与目录里真实文件**逐一相符**；
4. `files[]` 必须覆盖目录里除 `manifest.json` 以外的**全部**文件；
5. `changed-files.sha256.txt` 记录的路径必须**存在于工作区**。

> **口径**：`manifest.json` **记录自己的哈希**属于**自指**（永远不可能满足）→ 脚本显式跳过 ✓。
> 最早那 4 个 run（`MR00`/`MR01`/`MR02` 早期、`gap-register-2026-10-03`）**早于**本约定，
> 事后补造会污染历史证据 → 脚本内置**例外清单**并注明原因 ✓。

---

## 三、**首次运行就抓出 18 处不一致**（修到 0）

| 类别 | 数量 | 根因 | 处置 |
|---|---|---|---|
| 缺 `deliverable.md` | 4 | **约定建立前**的 run | 登记为例外（不补造）✓ |
| 缺 `manifest.json` | 2 | 同上 | 同上 ✓ |
| manifest 哈希过期 | 6 | **manifest 先写、deliverable 后改** | 重算 ✓ |
| manifest 记录**自己**的哈希 | 3 | 自指 | 审计跳过（无害）✓ |
| `changed-files.sha256.txt` 路径搬家 | 2+2 | 早期把它移进了 `green/` | 改指到 `green/…` ✓ |
| **合计** | **18 → 0** | | **审计 7 项全过** ✓ |

**修复动作**：新增 `scripts/repair-evidence-manifests.ps1` ——
① 丢弃自指条目；② 路径搬家；③ **重算**所有已存在条目的哈希；④ 补登目录里未登记的文件；
⑤ 回写时把 `\uXXXX` 还原成可读字符（`ConvertTo-Json` 会转义中文）。**共修复 9 个 manifest** ✓。

---

## 四、**封存证据不重写**的原则如何处理

修 manifest = 改动**已封存的证据** ✗ —— 因此：

1. **修复前**先把 54 个 manifest **整体快照**存档到本包的 `before-manifests/` ✓；
2. 修复只改 `manifest.json`（**不动任何证据文件本身** ✓）；
3. 抽查修复后的 manifest：**JSON 合法 ✓、12 个字段、中文未被转义 ✓**；
4. 里程碑复验**未受影响**（数字与修复前一致：mg0 271/1/133/0、mg1 88/0/103/0、mg2 271/1/132/0、mg3 88/0/103/0）✓。

---

## 五、本轮自找的三个问题（都是脚本自身）

| 问题 | 现象 | 修法 |
|---|---|---|
| 证据树路径 | 脚本去 `D:\松光启动器\docs\market-execution` 找 → **不存在** ✗ | 与既有脚本一致：把**工作树**路径作为参数默认值 + 本地路径兜底 ✓ |
| `param` 位置 | `'Path' is a ReadOnly property` ✗ | **`param` 必须是脚本第一条语句**（原先被 `$ErrorActionPreference` 占了位）✓ |
| 孤儿目录 | 新证据目录未登记 → 台账审计 exit 1 ✗ | 把 `MG-audit-evidence-tree` 加进 `claimed` 列表 ✓ |

---

## 六、实际改动

| 文件 | 改动 |
|---|---|
| `scripts/audit-evidence-tree.ps1` | **新增**（7 项交叉检查） |
| `scripts/repair-evidence-manifests.ps1` | **新增**（manifest 修复器） |
| `scripts/audit-plan-ledger.ps1` | `claimed` 列表加 `MG-audit-evidence-tree` |
| `scripts/source-snapshot-inputs.txt` | 类别 G 13 → **15** |
| 9 个 run 的 `manifest.json` | **哈希重算 / 路径修正 / 补登**（修复前快照已存档） |

---

## 七、验证

| 检查 | 结果 |
|---|---|
| **新审计**（7 项） | **全部 OK，exit 0** |
| 台账审计 | **exit 0**（无孤儿） |
| 四个里程碑复验 | 全部 exit 0，数字与修复前一致 |
| 修复后 manifest | JSON 合法、中文可读 ✓ |
| 白名单八类计数 | 全部一致 ✓ |

---

## 八、仍未覆盖

| 项 | 说明 |
|---|---|
| **`deliverable.md` 内容与 manifest 声明的一致性** | 未检查（那需要读自然语言，脚本做不到） |
| 约定前的 4 个 run | 仍是"无 deliverable/manifest"状态（**刻意不补造**）✓ |
| 证据文件本身的正确性 | 本审计只查**内部自洽**，不判断证据是否真实反映产品行为 |

---

## 九、证据清单

```
green/audit-evidence-tree.log        新审计（7 项全过）
green/audit-plan-ledger.log          台账审计 exit 0
green/repair.log                     修复 9 个 manifest 的记录
green/verify-mg0..3.log              四个里程碑复验（数字未变）
before-manifests/                    修复前 54 个 manifest 的整体快照
deliverable.md / manifest.json
```

**本报告不宣布阶段放行、不提交、不发布。**
