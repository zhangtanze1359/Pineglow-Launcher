# M1-3 · `launcher.js` 按视图模块化（交接记录 · 补写于 2026-11-15）

> **补写说明**：本文件是 `docs/handoff/HANDOFF-2026-11-07-claude-code.md` §二指定的产出。
> 11-07 → 11-14 执行期只有 7 个证据包，没有单独成文；2026-11-15 由后续执行者按现行代码补写。
>
> **口径**：本文所有「已跑」数字都指向一次真实运行的原始输出（第九节，目录
> `docs/evidence/2026-11-15-cc1cc2/`）。11-07~11-14 期间的逐视图回归数字**本次未重跑**，
> 引自 `docs/evidence/2026-11-14-进度图.md`，并在表里标明来源。

---

## 一、函数级清单（拆分的依据，取自拆前的 `ac035e6`）

| 项 | 值 |
|---|---|
| 拆前文件 | `git show ac035e6:ui/js/launcher.js` |
| 行数 | **4146 行 / 4147 行含结尾空行** |
| 顶层函数 | **156 个**（判定口径：`function name(` 与 `const name = function/=>` 两种声明；行数按花括号配平计） |
| 按名字可判视图归属 | 19 个（content 9 / skins 5 / downloads 3 / settings 2） |
| 其余 | **137 个属于跨视图核心逻辑**（启动、实例、模态、诊断、恢复、导入、i18n 桥） |

> 这 137 个是拆法必须按视图、而不是按「功能域」的原因：它们被多个视图调用，
> 一旦按功能域切分，就会立刻产生相互写隐式全局的局面。

### 拆前最大的 10 个函数

| 函数 | 行数 | 拆后去向 |
|---|---|---|
| `openAiDiagnose` | 411 | 留在 `launcher.js`（跨视图：诊断面板入口） |
| `bindSettings` | 357 | `ui/js/settings.js`（`launcher.js` 保留薄委托 `bindSettings(el)`） |
| `renderResult` | 175 | 留在 `launcher.js` |
| `renderSettings` | 141 | `ui/js/settings.js`（薄委托 `renderSettings()`） |
| `bindSkins` | 117 | `ui/js/skins.js` + `ui/js/skin-visuals.js` |
| `bindInstallations` | 107 | 留在 `launcher.js` |
| `goTo` | 88 | 留在 `launcher.js`（`SG.goTo` 路由） |
| `openRecoveryPanel` | 86 | 留在 `launcher.js` |
| `drawBisect` | 75 | 留在 `launcher.js` |
| `doLaunch` | 73 | 留在 `launcher.js` |

---

## 二、模块边界图

```
ui/index.html（14 条 <script src>，顺序由单一清单决定）
  ├─ vendor/three.min.js
  ├─ js/skin3d-core.js · js/skin3d.js        ← 3D 皮肤渲染
  ├─ js/i18n.js      (SG.t / SG.locale)      ← 共享层：文案
  ├─ js/api.js       (SG.api / SG.pages)     ← 共享层：后端通道 + 页面注册
  ├─ js/playtime.js  (SG.playtime)
  ├─ js/update-monitor.js
  ├─ js/news.js      (SG.news)
  ├─ js/downloads.js (SG.downloads)
  ├─ js/skin-visuals.js                     ← 视图内共享层（skins 专用）
  ├─ js/skins.js     (SG.skins)
  ├─ js/content.js   (SG.content)
  ├─ js/settings.js  (SG.settings)
  └─ js/launcher.js  (SG.toast / SG.goTo / 启动·实例·模态·诊断·恢复)
```

**模块协议**（news / downloads / skins / content / settings 一致）：

- IIFE 包裹 + 只挂一个命名空间 `SG.<view>`；
- 对 `launcher.js` 的依赖走 `__wire` **单向注入**（不反向写 `launcher.js` 的局部变量）；
- 纯函数暴露在 `core` 面上，便于 `module.exports` 在 node 测试里直接调用；
- `launcher.js` 为每个视图保留 `renderX()` / `bindX(el)` **薄委托**，`VIEWS` 路由表与
  `SG.__test__` 的入口名保持不变 —— 这是「对外行为不变」的可检查形式。

**守卫**：`ui/tests/ui-script-order.test.cjs` 断言 `index.html` 的加载顺序 == `ui-script-order.cjs`
的单一清单（`27545a8` 建立，本轮未推翻）。

---

## 三、逐视图拆分与回归日志

| 视图 | 模块 | 行数 | 提交 | 当次门槛（前端 / C#，引自进度图） |
|---|---|---|---|---|
| 加载清单单一来源 | `ui-script-order.cjs` | — | `27545a8` | 早于本轮 |
| playtime | `ui/js/playtime.js` | 170 | 早于本轮 | — |
| news | `ui/js/news.js` | 533 | `b559085` | 481 / 1379 |
| downloads | `ui/js/downloads.js` | 293 | `7ff1042` | 484 / 1379 |
| skins | `ui/js/skins.js` | 257 | `c2f67ab` | 485 / 1380 |
| skins 共享层 | `ui/js/skin-visuals.js` | 86 | `c2f67ab` | 同上 |
| content | `ui/js/content.js` | 526 | `94c9623` | 486 / 1380 |
| settings | `ui/js/settings.js` | 678 | `4de65f0` | 486 / 1380 |
| （插入）C# flake 修复 | — | — | `ac035e6` | 484 / **1379 → 1380** |

> 「每拆一个视图就跑一次前端全量，红了立刻回退该视图」这条执行纪律，在进度图里体现为
> 上表逐行的测试数单调递增（481 → 484 → 485 → 486），中间没有出现过先红后补的情况。

---

## 四、规模变化

| 指标 | 拆前 | 拆后 |
|---|---|---|
| `ui/js/launcher.js` | 4146 行 | **2987 行**（-1159，-28%） |
| `ui/js` 总规模 | 单文件 249 KB | **13 个脚本 / 8535 行** |
| `index.html` | 1 条主脚本 | **14 条 `<script src>`** |

---

## 五、两种独立等价性验证（执行期做法）

| 视图 | 源码逐字符 | 渲染输出逐字符 |
|---|---|---|
| skins | 5/5 个 helper | 5/5 场景 |
| content | 10/10 个函数 | 10/10 场景 |
| settings | 7/7 个函数 | 11/11 场景 |

做法：从拆前提交取原函数体，与拆出后的模块做**归一化后逐字符比对**；再让拆分前后
各走一次真实渲染路径，比对 DOM 输出。脚本保留在各自的证据包里，可用 `EQ_BASE` 复验。

---

## 六、硬编码清理表（**本次实测 2026-11-15**，非引用）

机器门槛现状：`check-css-tokens.cjs` → **exit 0，49 个 token，「三层 CSS 无字面色值，无未定义引用，无死回退」**
（原始输出：`docs/evidence/2026-11-15-cc1cc2/02-css-tokens.txt`）。

逐文件实测分布（统计脚本口径：按行扫描，`--token:` 定义行不计入「字面值」）：

| 文件 | 行数 | token 外的 `#hex` | token 外的 `rgb/rgba()` | 裸 `px` 值 | `border-radius` 声明 | `font-size` 声明 |
|---|---|---|---|---|---|---|
| `ui/css/figma.css` | 1005 | **0** | **31** | 653 | 88 | 125 |
| `ui/css/pineglow.css` | 397 | **0** | **0** | 316 | 12 | 42 |
| `ui/css/redesign.css` | 606 | **0** | **39** | 802 | 80 | 110 |
| token 定义总数 | — | 49 | — | — | — | — |

结论：

- **色值已收敛**：三层 CSS 里 token 定义之外的字面 `#hex` 为 **0 处**，与
  `docs/design-system.md` 第 32 行「禁止硬编码色值；所有颜色走 token」一致 —— 这条机器门槛守得住。
- **alpha 层仍是字面值**：70 处 `rgba(...)` 落在渐变提亮、遮罩、半透明描边、hover 背景上。
  它们不产生新色相，`check-css-tokens` 也不覆盖这种形态，所以属于「未清理」而不是「违规」。
- **间距 / 字号 / 圆角没有 token 化**：三层合计 **1771 处裸 `px`**、**180 处 `border-radius`**、
  **277 处 `font-size`** 是直接字面值。这与 `design-system.md` 第 68 行「间距、圆角、阴影统一走设计
  token，禁止魔法值」**不一致**——是历史缺口，不是本轮引入。

---

## 七、需用户定夺项（不自行裁决）

1. **三层同名 token 覆盖**：`figma.css → pineglow.css` 有 **16 个**同名不同值
   （`--base` `#0d0e11`→`#10191b`、`--green` `#38e04d`→`#91d5af`、`--danger`…），
   `pineglow.css → redesign.css` 有 **23 个**（`--card-2`、`--border` 等）。
   而 `design-system.md` 第 4 行明写「不要在两层之间写互相冲突的同名 token」。
   → 要么删掉 `figma.css` 里被覆盖的旧声明，要么修改文档承认「后加载者胜出」是常态。
2. **是否引入 spacing / radius / font-size token**：当前 0 个，字面值 1400+ 处。
   引入会立刻把 `check-css-tokens` 的覆盖面扩大一个数量级，属于规格变更，需编排方决定。
3. **70 处 `rgba` 字面值**：可新增 alpha token 或改用 `color-mix()`，同样是规格变更。

---

## 八、未验证项（**不许把「没验」写成「验过」**）

1. **冷启动逐字节 DOM 比对 —— 未做。**
   本轮所有等价性验证都是**模块级渲染对比**，不是真实 WebView2 冷启动。
   原因：主工作区 `D:/Projects/松光启动器` 存在工作区级环境缺陷（见 `AGENTS.md`「主工作区里
   `dotnet test` 会假失败」一节），worktree 里也没有起 WebView2 宿主做首页 DOM 快照比对。
   需要时应单独立项，产物应包含两张 DOM 快照与其 diff。
2. **11-07~11-14 的逐视图回归数字未重跑**，仅为引用（见第三节，来源已在表内标注）。

---

## 九、本次实跑门槛（2026-11-15 21:28，worktree `pineglow-integration-20261107`，HEAD `dab48b3`）

| # | 命令 | exit | 数字 | 原始输出 |
|---|---|---|---|---|
| 1 | `node scripts/run-inventory-tests.cjs` | **0** | 486 pass / 0 fail / 0 skipped，25.0 s；`清单校验通过：node 测试 34 个，C# 项目 1 个，磁盘发现一致 34 个` | `docs/evidence/2026-11-15-cc1cc2/01-inventory.txt` |
| 2 | `node scripts/check-css-tokens.cjs` | **0** | 49 token，三层无字面色值 | `…/02-css-tokens.txt` |
| 3 | `node scripts/check-ui-resource-closure.cjs` | **0** | 19 个文件 / 35 条本地引用，闭包完整 | `…/03-closure.txt` |
| 4 | `dotnet test dev/PineglowLauncher.Tests -c Release` | **0** | 1380 通过 / 0 失败 / 0 跳过，1 m | `…/04-dotnet-test.txt` |

> 环境说明：以上第 4 项在 worktree 内执行。同一 commit 在主工作区执行会得到 309 条
> `UnauthorizedAccessException` 假失败，属工作区级环境缺陷，与代码无关（详见 `AGENTS.md`）。
