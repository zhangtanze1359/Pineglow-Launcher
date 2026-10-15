# MR09 补测 · 冻结案例集的自洽性、冻结性与代表性（缺口登记表 A9）

- **run-id**：`20261003-280000Z`
- **性质**：把 MR09 关于案例集的**散文声明**变成**机器校验的不变量**，并给出量化代表性
- **依据**：`MR09-diagnostic-benchmark/20261003-010000Z/deliverable.md`
  §一"dev 20 / holdout 10，holdout 各占独立成因族，与 dev **零家族重叠**（防近重复泄漏）"、
  §六"保留集的偏向 | holdout 10 例**全部是规则未知**样本"、"案例集代表性 | 30 例由人编写，**不是**真实用户日志的分布"
- **最终状态**：**证据就绪**（5 个不变量用例；2 个 RED 成立；**MR09 的三条声明全部验证为真**）

---

## 一、MR09 的声明**全部验证为真**（逐条）

| MR09 的声明 | 本包实测 | 结论 |
|---|---|---|
| 冻结案例集 **30 例**，SHA256 `D9F55189FA05845B…` | 当前哈希 `D9F55189FA05845BAC0403A058AA63912A725AFF61555BDF25A6865DD5AB45AF` | ✅ **完全一致** |
| dev 20 / holdout 10 | 20 / 10 | ✅ |
| holdout 与 dev **零家族重叠** | dev 家族 ∩ holdout 家族 = **空** | ✅ |
| holdout **各占独立成因族** | holdout 10 例 → **10 个互不相同的家族** | ✅ |
| holdout 10 例**全部是规则未知** | `expectRuleBucket` 全部为 `generic`（10/10） | ✅ |

> **口径**：本包校验的是**案例集自身的自洽性与冻结性**，**不是**评测质量 —— 真实模型评测仍未做（MR09 §六 已列）。

---

## 二、量化代表性（本包新增的事实）

### 家族分布（有效家族 = `correctFamily[id]`，否则 `category`）

| dev 家族（5） | 例数 | | holdout 家族（10） | 例数 |
|---|---|---|---|---|
| memory | 4 | | config / duplicate / encoding / launcher-config / mod-bug / network / resourcepack / server / world / wrong-dir | 各 1 |
| dependency | 5 | | | |
| java | 4 | | | |
| graphics | 5 | | | |
| injection | 2 | | | |

### 规则桶分布

| | java | memory | dependency | graphics | generic |
|---|---|---|---|---|---|
| **dev（20）** | 4 | 6 | 5 | 3 | **2** |
| **holdout（10）** | 0 | 0 | 0 | 0 | **10** |

**一个精确的措辞修正**：MR09 说 holdout"**全部是**规则未知"（成立），但 dev 里**也有 2 例** `generic`。
所以保留集的偏向是**集中**（10/10 vs dev 的 2/20），而不是"绝对新"——两者都"未知"，只是比例差一个数量级。

### 其它

- `answerable`：dev **20/20**、holdout **10/10**（全部可回答）
- `expectScorer`：`correct` 27 / `partial` 1 / `danger` 2
- `category` 取值 **20 种**（30 例里有 20 个不同 category；含 `graphics-with-memory-decoy`、`graphics-with-dependency-decoy`、`multi-problem` 等误导样本）
- 字段完整性：30 例的 `log` / `expectedCause` / `safeFix` **全部非空**；`expectScorer` 与 `expectRuleBucket` **全部落在声明枚举内**

---

## 三、**最重要的量化结论：dev 集能测什么，有硬边界**

MR09 说"『规则已知正确率不下降』这一条**必须在 dev 集上判定**"。
本包把这条的**边界**算出来了：

> **dev 只覆盖 5 个家族**（memory / dependency / java / graphics / injection）；
> **holdout 的 10 个家族在 dev 里没有任何样本**。

因此"规则已知正确率不下降"这一判定**只对这 5 个家族成立**；对另外 10 个家族（config、duplicate、encoding、
launcher-config、mod-bug、network、resourcepack、server、world、wrong-dir）**dev 集无样本可判**。
这条已写成不变量用例（⑤），任何拆分调整都会被它标出。

---

## 四、5 个不变量用例

| # | 用例 | 断言 |
|---|---|---|
| ① | `CaseSet_IsTheFrozenOne_WithTheRecordedSplit` | **文件哈希必须等于记录的冻结值**；30 例；20/10 拆分；id 唯一 |
| ② | `HoldoutFamilies_AreDisjointFromDev_AndMutuallyDistinct` | holdout ∩ dev = ∅；holdout 内部无重复家族 |
| ③ | `Holdout_IsEntirelyRuleUnknown` | holdout 全部 `generic`；且 dev 的 `generic` 例数 < 10（否则"偏向是集中"的描述不成立） |
| ④ | `EveryCase_HasRequiredFieldsAndDeclaredEnumValues` | 字段非空；枚举取值合法 |
| ⑤ | `DevSetFamilyCoverage_BoundsWhatTheDevSplitCanMeasure` | dev 家族数 = 5、holdout = 10（把"能测什么"的边界钉住） |

**① 的价值**：冻结集被**悄悄改动**会让此前所有评测数字失去可比性 —— 现在任何改动都会让测试失败，
并在失败信息里提示"若是有意变更，请同步更新 MR09 证据里的哈希与本用例的记录值"。

---

## 五、两个 RED（证明不变量有牙齿）

| RED | 改什么 | 结果 |
|---|---|---|
| **1** | 把 holdout 首个案例的 `expectRuleBucket` 从 `generic` 改成 `memory` | **2 失败**：`Holdout_IsEntirelyRuleUnknown` + **冻结哈希**用例 |
| **2** | 把该案例的 `category` 从 `network` 改成 `memory`（与 dev 同族） | **2 失败**：`HoldoutFamilies_AreDisjointFromDev_AndMutuallyDistinct` + **冻结哈希**用例 |

**两次 RED 后，冻结案例集都逐字节还原**，哈希复核等于 `D9F55189…45AF` ✅
（这一点对本包尤其重要：**改的是冻结集**，必须证明没留下任何改动。）

---

## 六、本轮自找并修掉的一个编译问题（含教训）

第一次构建 **7 个错误**：我在 C# 字符串里用**直双引号包中文**（`…（这是"dev 能测什么"的边界）…`），
把字符串提前截断了。改成「」后仍剩 2 个错误（`而非"绝对新"`），第二次才修净。

**教训**：中文文案里的直引号在 C#/PowerShell 里都是**语法字符**。本会话已因此踩坑多次
（PowerShell 两次、C# 一次），规则应当是：**代码里的中文引号一律用「」**。

**顺带一个正向例证**：这次我**先检查了 `dotnet build` 的退出码**才跑测试 ——
所以构建失败时我立刻知道"后面的测试跑的是旧二进制"，没有把无效结果当成证据
（第 36 轮就是因为吞掉构建输出而得到过无效证据）。

---

## 七、验证

| 检查 | 结果 |
|---|---|
| 不变量用例 | **5 通过 / 0 失败** |
| C# 全量 | **797 通过 / 0 失败 / 0 跳过**（792 + 5） |
| Node 全量 | **230 通过 / 0 失败**（18 个文件） |
| **冻结案例集完整性** | 哈希 = `D9F55189…45AF`，**与冻结值一致** |

---

## 八、实际改动

| 文件 | 改动 |
|---|---|
| `dev/PineglowLauncher.Tests/DiagnosticCaseSetInvariantTests.cs` | **新增**（5 个不变量用例） |
| `scripts/source-snapshot-inputs.txt` | 登记新测试（类别 B 39→40） |
| `docs/market-execution/gap-register.md` | A9 标记为**已关闭** |
| `docs/market-task-plan-2026-10-03.md` | MR09 行补记 |

**生产代码与冻结案例集零改动。**

---

## 九、仍未覆盖（A9 里**没有**做的部分）

| 项 | 说明 |
|---|---|
| **真实模型评测** | 仍未做（需外发与费用授权）——本包**不**替代它 |
| **评分器只做文本级判定** | 本包**只确认**这一点（读 `expectScorer` 与评分器机制），**没有**改进它 |
| 案例集的真实用户分布 | 未验证，也不可能用本机数据验证（MR09 已声明"由人编写，不是真实分布"） |
| holdout 的"增量"是否真的测到 | 未验证（需要真实模型调用） |

---

## 十、证据清单

```
green/01-caseset-first.log       首次运行（构建失败，未产出有效结果）
green/02-caseset.log             修正引号后 5 通过
green/03-caseset-final.log       最终 5 通过 / 0 失败
green/04-csharp-full.log         797 通过 / 0 失败
green/05-node-full.log           230 通过 / 0 失败
red/diagnostic-cases.json.before     冻结集备份（哈希 D9F55189…45AF）
red/01-red-frozen-modified.log   RED-1：改动冻结集 → 2 失败
red/02-red-family-overlap.log    RED-2：holdout 与 dev 同族 → 2 失败
manifest.json
```

**本报告不宣布阶段放行、不提交、不发布。**
