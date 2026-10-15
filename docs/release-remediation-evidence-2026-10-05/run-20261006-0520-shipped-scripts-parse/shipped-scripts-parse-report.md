# 交付脚本的静态解析扫描 + "交付 vs 自动执行"盘点（第 22 轮）

日期：2026-10-06（Asia/Hong_Kong）。脚本：`shipped-scripts-parse.py`；结果：`shipped-scripts-parse.json`。
本轮**未改动任何 tracked 文件**。

## 1. 起因：候选快照里有一批"从写出来那天起就没人再跑过"的脚本

白名单把 ~20 个可复现性工具脚本一起交付给评审方，但快照验证器**只执行其中一部分**。
一个语法坏掉（最典型：编辑工具抹掉 BOM，PS 5.1 就按 GBK 解析中文）的脚本会**原样进入候选**，
本地全绿时完全看不出来。**本会话已经被这个坑绊过两次**，所以补一次覆盖全量的静态检查。

## 2. 做法：只解析、不执行

| 类型 | 手段 | 为什么安全 |
| --- | --- | --- |
| `.ps1` / `.psm1` | Windows PowerShell 的 `Parser::ParseFile`（纯语法）| 不执行任何语句 |
| `.js` / `.cjs` | `node --check` | 只做语法检查，不加载 |
| `.py` | 内建 `compile()` | 不落 `__pycache__`、不 import |

## 3. 结果

```
交付脚本：.ps1/.psm1 21 个，.js/.cjs 29 个，.py 2 个（共 146 个白名单文件）
  PowerShell 解析：21/21 通过
  JS 解析(node --check)：29/29 通过
  Python 解析：2/2 通过
SHIPPED-SCRIPTS-PARSE RESULT=PASS problems=0
```

**结论：候选里交付的每一个脚本都能被正确解析**（含 21 个 PowerShell、29 个 JS、2 个 Python）。
这解决的是"语法层"问题——也正是本会话真实踩过的坑（BOM 被抹掉那次就是解析直接失败）。

## 4. 盘点：哪些交付脚本其实没有任何门禁在执行它

| 类别 | 数量 | 说明 |
| --- | --- | --- |
| 清单驱动、验证器自动跑 | **23** | `ui/tests/*.test.cjs`（`test-inventory.txt` 是唯一权威来源，由 `run-inventory-tests.cjs` 执行）|
| 验证器直接接线 | **6** | `check-ui-resource-closure.cjs`、`run-inventory-tests.cjs`、`lib/StepRunner.psm1`、`tests/step-runner.test.ps1`、`verify-single-file-standalone.ps1`、`verify-source-snapshot.ps1` |
| 人工/CI 触发（刻意不进离线验证器）| **2** | `verify-update-live-download.ps1`（需联网）、`verify-ui-pages-live.cjs`（需真实桌面）|
| **仅交付、无任何自动执行** | **21** | 见下 |

那 21 个再分三类：

1. **3 个是运行时界面资源**（`ui/js/skin3d-core.js`、`ui/js/skin3d.js`、`ui/vendor/three.min.js`）——
   它们由应用在运行时加载，行为已被九页界面验收与 Node 测试覆盖 ⇒ **不是缺口**。
2. **17 个是历史里程碑工具**（`verify-mg0..3.ps1`、`measure-*.ps1` ×4、`audit-evidence-tree.ps1`、
   `audit-claimed-tests.ps1`、`audit-plan-ledger.ps1`、`repair-evidence-manifests.ps1`、
   `run-diagnostic-benchmark.ps1`、`capture-goal-baseline.ps1`、`run-goal-validation.ps1`、`audit-goal-evidence.py`）——
   交付给评审方用于复现**更早轮次**的结论；没有任何门禁在跑它们。
3. **1 个是没人跑的测试**：`scripts/tests/audit-goal-evidence.test.py` ——
   它的驱动者是上面那个同样不自动跑的 `run-goal-validation.ps1`。
   注意：`test-inventory.txt` 只覆盖 `*.test.cjs`（Node），所以这个 Python 测试**不在清单口径内**，
   这不违反 R04 的门槛（那条门槛的语义是"Node 测试不漏跑"），但**值得知道**。

**含义**：这 18 个工具/测试脚本会**静默腐烂**——若哪天它们被改坏，没有门禁会响。
本轮至少把"语法层不会腐烂"钉住了（§3）。**运行期**行为（是否需要旧的证据目录、参数是否还成立）
**未验证**，因为那需要对应的历史证据布局。

## 5. 我这个盘点做错了三次（都记录在此）

一次小盘点连错三次，原因各不相同——都是"读文件/判据"层面的问题，值得留档：

1. **第一次**：用文件名去 `verify-source-snapshot.ps1` 文本里找，判定"只有 7 个被执行"——
   漏掉了**清单驱动的 23 个 Node 测试**（它们的名字不在脚本里，而在清单文件里）。
2. **第二次**：用 `Get-Content` 读文件解析清单/白名单，而 PS 5.1 的 `Get-Content` 在**纯 LF** 文件上
   会把行并起来（实测 `source-snapshot-inputs.txt` 214 行被读成 179 行）。
   改用 `[IO.File]::ReadAllLines` 后：白名单确实是 **146** 条（与我此前所有轮次的结论一致）。
3. **第三次**：清单每行格式是 `"<runner> <路径>"`（如 `node ui/tests/x.test.cjs`），
   我拿**裸路径**去比较，于是 23 个 Node 测试全被判成"未执行"。
   去掉 runner 前缀后才得到 §4 那张正确的表。

**教训（本会话第 7、8 次同类）**：*读文件和比较口径写错，会把整张表带反*。
它也解释了为什么这类盘点必须把**中间数**拿出来对账（例如"清单 24 条 = 23 node + 1 project"
与验证器报告里的 `nodeCount=23 / projectCount=1` 必须对得上）。

## 6. 为什么没有把它接进验证器（权衡记录）

- 它是**候选卫生**属性（脚本能否解析），不是**产品**属性；发布出去的那个 EXE 不依赖这些脚本。
- 现有那条「脚本编码守卫」已经挡住最现实的成因（非 ASCII 的 `.ps1/.psm1` 缺 BOM）。
- 把 `.py` 也纳进来会给核心验证器**新增 Python 依赖**（它现在只需要 PowerShell + dotnet + node）；
  只做 PS/JS 又不完整。
- 因此：**保持为专项检查**，并写进发布手册 §2，而不是动核心验证器。

## 7. 边界

- **只验证语法可解析**；运行期行为（缺参数、路径失效、依赖缺失、旧证据布局）**未验证**。
- 扫描范围是白名单内的脚本类文件（52 个）；tracked 的产品源码不在此列（它们由编译与测试覆盖）。
