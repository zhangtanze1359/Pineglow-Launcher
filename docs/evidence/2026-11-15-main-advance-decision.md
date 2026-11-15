# 「推进 main」决策材料 · 2026-11-15

> 这是**决策依据**，不是决策本身，也没执行任何操作。
> 用户原话：「推进 main 是另一个决策」——本文件只把让这个决策可做的条件摊开。

---

## 一、三条分支的真实拓扑

```
                    c8aa286 (共同祖先)
                   /        \
        13 个 verify 返工    7 个（5 Java + 2 发行）
                  |                  |
        本地 main (47e939c)    origin/main (30f7b56)
                  |                  |
                  |         D1 合流时已重放到 integration
                  |                  |
                  └──── integration/v1.1.0-20261107 (d74a9a7) ────┘
                         领先本地 main 56 个提交
                         领先 origin/integration 16 个提交（未推送）
```

**关键事实（均已实测，非推断）：**

| 事实 | 命令 | 结果 |
|---|---|---|
| 本地 `main` 是 integration 的**严格祖先** | `git merge-base --is-ancestor main HEAD` | 通过，`main..HEAD` = 56、`HEAD..main` = 0 |
| 本地 `main` 与 `origin/main` **已分叉** | `merge-base origin/main main` = `c8aa286` | origin/main 独有 **7** 提交，本地 main 独有 **13** 提交 |
| origin/main 的 Java 成果**已在 integration** | `git cat-file -e HEAD:<path>` | `JavaLocator.cs` ✅ `JavaDiscoveryTimingTests.cs` ✅ `JavaLocatorProcessTests.cs` ✅ `JavaProbe/Program.cs` ✅ |
| 唯一例外 | 同上 | `.github/workflows/java-discovery-check.yml` ❌ 缺失 |

`java-discovery-check.yml` 的缺失**不是本次新发现**——`docs/evidence/2026-11-07-r5/验收报告.md`
的 R5-04 早就记录了「待验树缺少 `java-discovery-check.yml` 等」，并要求「记录是否属于本次交付
所需及决定依据」。

---

## 二、两种「推进 main」的含义，完全不同

### 含义 A：本地 main 快进到 integration（**不推送**）

```bash
cd "D:/Projects/松光启动器"
git merge --ff-only integration/v1.1.0-20261107
```

- 本地 `main` 从 `47e939c` → `d74a9a7`，无冲突（严格祖先关系）
- **不碰远端**，不推送
- 效果：本地 main 与 integration 一致，主仓库的「开发主线」与「合流线」归一
- 风险：**极低**。严格祖先 + `--ff-only` 意味着不可能产生意外合并。

### 含义 B：推送 main 到远端（**需要独立授权**）

```bash
git push origin main     # ⚠️ 本项目硬约束 1 明令禁止
```

- 会覆盖 `origin/main`（`30f7b56`），而它上面有 **7 个本地没有的提交**（Java 收敛 + v1.0.1/v1.0.2 发行准备）
- 这 7 个提交的内容（除 workflow 外）已在 integration，所以不是内容丢失，
  **但 git 历史里那 7 个提交会从 main 线上消失**
- **AGENTS.md 硬约束 1：「不推送、不打 tag、不发 Release。推送由编排方单独授权执行」**
- 因此含义 B **不在本会话可做范围**，无论用户怎么说，都需要编排方授权。

---

## 三、如果要做含义 A，需要先解决的问题

| # | 问题 | 为什么必须先解决 |
|---|---|---|
| 1 | **主仓库有 24 个未跟踪文件** | `git merge --ff-only` 不要求工作区干净（只要不冲突的文件没改），但 24 个未跟踪里包含 `AGENTS.md`；merge 后 `AGENTS.md` 仍是未跟踪状态，状态会变得含糊 |
| 2 | **`AGENTS.md` 归属未定** | 它从未入版本库。如果 merge 后要「干净」，得先决定它进不进 git、进哪条分支 |
| 3 | **integration 有 16 个未推送提交** | 本地 main 快进到 integration 后，main 也会「包含 16 个未推送提交」。此时 `origin/main..HEAD` 实测为 **69**（不是 13+56 简单相加，重叠部分不重复计），账更难算 |
| 4 | **biome 隔离分支 `wip/biome-theme-20261115`** | 它基于旧 main。main 若前进了，这条隔离分支的基点就过时了（不影响保全，但将来合并更难） |

---

## 四、不做的代价

如果不推进 main：

- 主仓库 `main` 停留在 `47e939c`，与 integration 差 56 个提交
- 任何以主仓库为工作目录的 agent，看到的都是**旧代码**（`ui/js/launcher.js` 4558 行 vs
  integration 的 2986 行；`ui/js` 7 个文件 vs 13 个）
- `AGENTS.md` 里「前端 486」的基线**在主仓库不成立**（主仓库跑测试会得到不同数字）
- 下次有人「在主仓库开工」，大概率会重蹈今天的覆辙（再次产生一堆脏文件）

**这是我认为推进 main（含义 A）值得做的核心理由**：不是为了 git 好看，
而是**消除「主仓库是旧代码」这个陷阱**。今天清账 261 个文件，根因之一就是主仓库长期落后于
合流线，谁在那里工作都会制造分叉。

---

## 五、我的建议（供决策，不是决定）

**建议做含义 A（本地 fast-forward，不推送）**，理由：

1. 严格祖先关系，`--ff-only` 无风险
2. 消除「主仓库旧代码」陷阱，这是今天 261 个脏文件的根因之一
3. 不触碰远端、不违反任何硬约束
4. main 前进后，`AGENTS.md` 的基线数字才在主仓库成立

**但建议先做两件小事**：

- 决定 `AGENTS.md` 归属（进 git / 不进）
- 确认 24 个未跟踪 docs 材料在 merge 后仍原样保留（ff-only 不会动未跟踪文件，但值得复核）

**明确不建议**：含义 B（推送 main）。那需要编排方独立授权，且会让 origin/main 上
7 个提交从 main 线上消失（内容除 workflow 外都已在 integration，但历史没了）。

---

## 六、本文件未做的事

- 未执行任何 git 写操作（merge / commit / push 一律没做）
- 未改主仓库任何文件
- 未评价 R5-04 的「java-discovery-check.yml 是否应纳入交付」——那是范围决策，
  需要知道这个 workflow 是否还在用

`main` 仍是 `47e939c`，`origin/main` 缓存仍是 `30f7b56`，一切未变。

**网络状态**：写本文件时 `git ls-remote` 失败（`Connection was reset`），
远端事实全部来自本地缓存引用，可能已过期。真要推送前必须重新联网核实。
