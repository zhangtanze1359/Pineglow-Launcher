# M2-3 · 验证器统计口径 + 清单双向断言（交接记录 · 补写于 2026-11-15）

> **补写说明**：本文件是 `docs/handoff/HANDOFF-2026-11-07-claude-code.md` §三指定的产出。
> 执行期（11-07~11-14）两半代码都已在分支上，但记录没有单独成文；2026-11-15 由后续执行者补写。
>
> **口径**：代码侧事实取自提交 `f9b109d` 的实际 diff 与 10-16 的证据包；守卫部分（第四、五节）
> 是**本次实跑**的原始输出，目录 `docs/evidence/2026-11-15-cc1cc2/`。

---

## 一、先说清楚「待做」与「已完成」的错位

`HANDOFF-2026-11-07-claude-code.md` §三写着「待做：九页验证器统计口径修正 + `test-inventory` 双向断言」，
但两半**在 11-07 就已经存在于分支上**：

| 半 | 完成时间 | 证据 |
|---|---|---|
| 验证器统计口径（HA-01） | `f9b109d`（2026-10-16 22:50） | 该提交 7 个文件、+742/−19，含 `verify-ui-pages-live.cjs` 与行为报告 |
| 清单双向断言 | `scripts/run-inventory-tests.cjs` 内置，每轮门槛都跑 | 见第四节实跑输出 |

错位原因：交接书基于 11-07 的认知，而 HA-01 是 10-16 的提交。

---

## 二、修前 / 修后对照表（口径层，取自 `f9b109d` 的 diff）

| # | 项 | 修前 | 修后 |
|---|---|---|---|
| 1 | 统计页数 `VIEWS` | 9 项（`home installations worlds servers content skins downloads news settings`） | **10 项**，补上确实存在的 `playtime`（`ui/index.html:61`） |
| 2 | 计入的 `<img>` 集合 | `root.querySelectorAll('img')` **全量** | 先过 `IMG_VISIBLE_FN`：**仍在文档内但滚出视口的节点保留**（懒加载），真正隐藏的排除 |
| 3 | `pending` 判定 | `pendingSlots > 0 \|\| loaded < total` | `pendingSlots > 0 \|\| loaded + **broken** < total` |
| 4 | 结果字段 | `total / loaded / slots / pendingSlots` | 增加 **`broken`**（有 src 但 `naturalWidth === 0`）与 **`offscreen`**（`bottom <= 0 \|\| top >= innerHeight`） |
| 5 | 等待收敛 | 只等 `loaded >= total` | `loaded + broken >= total && pendingSlots === 0` 即提前结束 |

---

## 三、同一页面同一输入：修前为什么必然 FAIL

| 页 | 修前会发生什么 | 修后实测（10-16 证据 `ui-live-fixed.json`） |
|---|---|---|
| `content` | 24 张图被 `ui/css/redesign.css` 的 `.mod-icon { display:none }` 隐藏、宽高为 0，**却仍进分母**：`total=24`、`loaded=0` → `pending` 永真 → 等到超时 → 该页判 FAIL | `ok=true`，`imagesTotal=0 / loaded=0 / broken=0 / slots=0 / pending=0` |
| `skins` | 唯一被计入的 `#localSkinTexture` 是 **hidden、无 src、宽高 0** 的本地文件预览占位，被当成「加载失败」 | `ok=true`，`total=0 / loaded=0 / broken=0 / slots=0 / pending=0` |
| `news` | 异步 fetch 的图片槽位没有可靠收敛条件 | `ok=true`，`slots=12 / pending=0` |
| 其余 7 页 | 分母不含隐藏节点，本来就通过 | `ok=true`，`total=0`（home 有 `slots=4 / pending=0`） |

> 10 页全 `ok=true`，`imagesPending = []` —— 也就是说：「修后同一输入下 PASS 可解释」
> （分母里不再有不参与渲染的节点；待填槽位单独用 `pendingSlots` 表达）。

---

## 四、`test-inventory` 双向断言（**本次实跑 2026-11-15**）

**正向（正常状态）**

```
[inventory] 清单校验通过：node 测试 34 个，C# 项目 1 个，磁盘发现一致 34 个
ℹ pass 486   ℹ fail 0   ℹ skipped 0        exit = 0
```
原始输出：`docs/evidence/2026-11-15-cc1cc2/01-inventory.txt`

**反向 1 —— 清单里有、磁盘上没有**（追加 `node ui/tests/zzz-fake-guard.test.cjs` 后运行）

```
[inventory] 清单校验失败（node=35 项目=1 磁盘发现=34）
  - 清单里 1 个文件不存在（清单与源码漂移）: ui/tests/zzz-fake-guard.test.cjs
exit = 1
```
原始输出：`…/05a-inventory-negative-missing-file.txt`

**反向 2 —— 磁盘上有、清单里没有**（新建 `ui/tests/zzz-unregistered.test.cjs` 后运行）

```
[inventory] 清单校验失败（node=34 项目=1 磁盘发现=35）
  - 磁盘上有 1 个未登记的 *.test.cjs（新增测试必须登记，否则 CI 不会跑）: ui/tests/zzz-unregistered.test.cjs
exit = 1
```
原始输出：`…/05b-inventory-negative-unregistered.txt`

**反向 3 —— 行格式非法**（追加一条裸路径行）

```
[inventory] 清单行格式非法（应为 "<node|project> <path>"）: ui/tests/zzz-fake-guard.test.cjs
exit = 2
```
原始输出：`…/05-inventory-negative.txt`

> 三个反向实验制造的文件与清单改动**都已还原**（`git checkout scripts/test-inventory.txt`、
> `rm ui/tests/zzz-*.test.cjs`），运行后的 `git status` 为空。这是「守卫真的会红」的实证，
> 不是「读了代码觉得它会红」。

---

## 五、新增守卫测试的归属

- 双向断言本身位于 `scripts/run-inventory-tests.cjs`，是**每轮门槛**的一部分，不依赖某次提交；
- 11-12~11-14 那轮另新增 5 条**列表守卫**，前端测试数 **481 → 486**（逐次 481→484→485→486，
  其中 5 条守卫单独实测 5→8→9→10），该部分记在 `docs/evidence/2026-11-14-进度图.md`。

---

## 六、缺口与需定夺

1. **修前状态无法重跑**：要复现「修前 FAIL」，需要 10-16 之前的代码 + 真实 WebView2 宿主，
   本轮只做了 diff 层与 10-16 证据层的对照。若要可复跑的对照，应把 `IMG_VISIBLE_FN` 抽成
   可单测的纯函数并补一条「隐藏节点不进分母」的守卫测试 —— 这属于新工作，不在本轮范围。
2. **GUI 取舍结论不复述**：以 `docs/decision-gui-testing-2026-11-06.md` 原文为准。
3. **冷启动级验证器本轮未重跑**：同 `docs/m1-3-frontend-modularization-2026-11-07.md` 第八节。

---

## 七、门槛（本次实跑，2026-11-15 21:28，worktree HEAD `dab48b3`）

| # | 命令 | exit | 数字 | 原始输出 |
|---|---|---|---|---|
| 1 | `node scripts/run-inventory-tests.cjs` | **0** | 486/486，0 跳过，25.0 s | `…/01-inventory.txt` |
| 2 | `node scripts/check-css-tokens.cjs` | **0** | 49 token | `…/02-css-tokens.txt` |
| 3 | `node scripts/check-ui-resource-closure.cjs` | **0** | 19 文件 / 35 条引用 | `…/03-closure.txt` |
| 4 | `dotnet test dev/PineglowLauncher.Tests -c Release` | **0** | 1380 / 0 失败 / 0 跳过 | `…/04-dotnet-test.txt` |
