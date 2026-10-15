# MR17 · 核心流程键盘、焦点与缩放 · 交付报告

- **run-id**：`20261003-100000Z`
- **执行范围**：无障碍审计 + 键盘/焦点/缩放修复（CSS/HTML/JS 三层）+ 契约测试
- **依据**：`docs/market-task-plan-2026-10-03.md` §7.2/§8、`market-research-task-workflow-2026-10-02.md` §12（MR17 卡片）、`accessibility` 技能（WCAG 2.2）
- **最终状态**：**证据就绪**（DOM/静态层已验证；**真实键盘操作与屏幕阅读器手工检查未做**，见 §六）
- **未执行**：不提交、不推送、不发布

---

## 一、结论先行

| 项 | 结果 |
|---|---|
| 审计发现（真实缺口） | ✅ 六处，见 §二。最关键的是**键盘焦点完全不可见** |
| 焦点可见 | ✅ 补**全局** `:focus-visible`（改动前只有两个控件的 scoped 规则，等于没有兜底） |
| 弹窗键盘契约 | ✅ Escape 关闭 + Tab 焦点陷阱 + 关闭后**焦点返回触发元素** + `role="dialog"`/`aria-modal` |
| 危险操作不默认聚焦 | ✅ 优先把焦点给"取消/关闭"这类安全按钮，没有才落到第一个可聚焦元素 |
| `role="button"` 可激活 | ✅ Enter/Space 激活，且**排除原生 `<button>`**避免双重触发 |
| 异步状态可感知 | ✅ `#toast` 加 `role="status" aria-live="polite" aria-atomic` |
| 小窗口/高缩放 | ✅ 操作区**粘底**（滚动后确认/取消仍可点）+ `scroll-margin` 防焦点被遮挡 |
| 目标尺寸 | ✅ `.account-card-del`（原约 19px 高）与 `.mod-del` 提到 ≥24×24（WCAG 2.5.8） |
| 跳转链接 | ✅ 新增，且文案走 i18n（中英双语），目标 `#view-home` 补 `tabindex="-1"` 可聚焦 |
| 颜色不是唯一信号 | ✅ 危险/告警文案加前置 `!` 记号 |
| 契约测试 | ✅ 新增 9 个用例；RED 基线 **9 失败 / 0 通过** → **9 通过 / 0 失败** |
| C# 全量回归 | ✅ **752 通过 / 0 失败** |
| Node 全量回归 | ✅ **230 通过 / 0 失败**（18 个文件） |
| 🔴 **真实键盘/屏幕阅读器** | **未验证**（卡片明确要求与 DOM 模拟独立） |

---

## 二、审计发现（改前的真实缺口）

| # | 缺口 | 依据 | 影响 |
|---|---|---|---|
| 1 | CSS 里输入框有 `outline: none`，而全文件**没有全局** `:focus-visible`（只有 `.skin3d-canvas` / `.skin-control` 两条 scoped 规则） | WCAG 2.4.7 / AA | **键盘用户完全看不到焦点在哪** |
| 2 | 弹窗只有鼠标路径：无 Escape、无焦点陷阱、关闭后焦点丢失 | WCAG 2.1.1 / 2.4.3 | 键盘无法关闭弹窗；关闭后"位置丢失" |
| 3 | `role="button"` 元素（账户卡片、删除账户）可聚焦但**无法用键盘激活** | WCAG 2.1.1 / 4.1.2 | 键盘用户进得去、按不动 |
| 4 | `#toast` 无 live region | WCAG 4.1.3 | 屏幕阅读器读不到"完成/失败/已取消" |
| 5 | `.account-card-del` 约 19px 高（11px 字号 + 3px 内边距） | WCAG 2.5.8 / AA 2.2 | 目标过小 |
| 6 | 无跳转链接 | WCAG 2.4.1 | 键盘用户必须逐个 Tab 穿过整个标题栏 |

> 审计用 `accessibility` 技能给的 WCAG 2.2 检查表逐项对照实际文件，**不是**凭印象列的。

---

## 三、实际改动

| 文件 | 改动 |
|---|---|
| `ui/css/figma.css` | **追加**一段 MR17 无障碍块（约 1.9 KB）：全局 `:focus-visible`、`scroll-margin`、`.visually-hidden`、`.skip-link`、`.modal-actions` 粘底、24×24 目标尺寸、颜色之外的状态记号 |
| `ui/index.html` | 新增跳转链接（`data-i18n`）；`#view-home` 加 `tabindex="-1"`；`#toast` 加 live region 属性 |
| `ui/js/launcher.js` | 弹窗键盘契约（`modalFocusable` / `installModalKeyboard` / `focusModalInitial`）+ `role="button"` 的 Enter/Space 委托激活；`openModal`/`closeModal` 相应扩展 |
| `ui/js/i18n.js` | 中英各加 `a11y.skipToContent` |
| `ui/tests/a11y-keyboard.test.cjs` | **新增** 9 个契约用例 |
| 6 个既有测试文件 | **补齐测试替身**（见 §五） |

**CSS 采用"追加覆盖"而不是改动既有规则**：复验方一眼能看清本包改了什么，也能整块回退。
**未换 UI 框架**（卡片明确不要求）。

---

## 四、RED → GREEN

**RED 构造方式**：把我这一轮加的东西**逐块还原**（CSS 追加块、HTML 三处属性、launcher.js 的 JS 块与 `openModal`/`closeModal`、i18n 两个键），保留测试文件。原始 `openModal`/`closeModal` 我用编辑时的原文精确还原。

**RED：9 失败 / 0 通过**（`red/01-a11y-before.log`）

**GREEN**

| 检查 | 结果 | 日志 |
|---|---|---|
| `node --test ui/tests/a11y-keyboard.test.cjs` | **9 通过 / 0 失败** | `green/02-a11y-after.log` |
| `dotnet test`（全量） | **752 通过 / 0 失败** | `green/11-csharp-full-final.log` |
| `node --test`（18 个 `.test.cjs`） | **230 通过 / 0 失败** | `green/12-node-full-final.log` |

**断言写紧了一次**：我最初用 `/:focus-visible\s*\{/` 判断"有焦点样式"，但文件里本来就有 `.skin3d-canvas:focus-visible`，会**误判通过**。已收紧为"必须是**行首的全局规则**"——否则 RED 会给出假绿。

---

## 五、本轮最重要的发现：**无障碍改动打破了 48 个既有测试**

第一次跑 Node 全量：**230 个用例里 48 个失败**。

原因**不是产品缺陷，而是既有测试替身不完整**：那些测试用极简假 DOM（`makeEl`）来抽取并调用 `openModal`/`closeModal`，而假元素只有 `classList`/`setAttribute`/`children` 等少数成员。我的键盘契约调用了**标准 DOM 方法**（`contains`/`removeAttribute`/`addEventListener('keydown')`/`removeEventListener`/`focus`/`hasAttribute`），假元素没有 → `TypeError`。

这暴露了一个**测试质量问题**：那些"DOM 模拟"离真实 DOM 太远，任何用到标准 API 的产品改动都会假失败。处置（**只改测试替身，不给产品代码加特例**）：

| 文件 | 补了什么 |
|---|---|
| `ui/tests/ai-diagnose.test.cjs` | 两处 `makeEl` 补 `contains`/`removeAttribute`/`closest`/`matches`/`focus`/`offsetParent`；两处假 document 补 `removeEventListener` |
| `ui/tests/news-link.test.cjs` | 假元素补同样成员；`loadLauncher` 里被覆盖的 `addEventListener` 初始化 `_listeners` 并补 `removeEventListener` |
| `ui/tests/news-behavior.test.cjs` | 假 document 的 `_listeners` 初始化 + `removeEventListener` |

另外两处**产品侧**的可兼容写法（语义等价、非特例）：
- `dialog.hasAttribute('tabindex')` → `dialog.getAttribute('tabindex') == null`（既有替身普遍实现了 `getAttribute`）；
- `modalFocusable` 从一条带 `:not()` 的复合选择器改为**多个简单选择器分别查询再合并**（兼容性更好，且过滤逻辑不变）。

**我自己也踩了一次**：给 `news-link.test.cjs` 补 `focus()` 时写成了 `document_.activeElement`，而该文件里变量叫 `document` → `ReferenceError`。已修正。

---

## 六、明确未做与未验证

| 项 | 说明 |
|---|---|
| 🔴 **真实键盘操作** | **未做**。卡片明确要求"键盘真实操作与屏幕阅读器手工检查**独立于 DOM 模拟**"。本包只做到 DOM/静态层与契约测试。 |
| 🔴 **屏幕阅读器** | **未做**。未用 NVDA/JAWS/VoiceOver 实读；`aria-*` 与 live region 只做了结构性声明，**未经真实宣读验证**。 |
| 🔴 **125%/150%/200% 缩放核对** | **未做**。需要真实 WebView2 与桌面会话；粘底与 `scroll-margin` 只是结构性修复。 |
| **800×600 小窗口** | 未实测。`.modal` 本就有 `max-height` + 滚动，本包只加了粘底操作区。 |
| **中英文文字裁切** | 未实测（需要真实渲染）。新增文案已确保中英双语齐备并有测试。 |
| **对比度** | **未测**。`:focus-visible` 用 `var(--green)`，其与各背景的对比度需真实取样（WCAG 1.4.11 要求 ≥3:1）——**未验证**。 |
| **tab 顺序** | 未逐屏记录（卡片要求"走六类界面记录 Tab 顺序"）；本包只保证了弹窗内的顺序与返回。 |
| 其它 `makeEl` 替身 | `launcher-v3/v4/v5.test.cjs` 的同类替身未逐一补齐（它们当前不失败）；如后续改动再触发，应按同一口径补。 |

---

## 七、回退方法

```powershell
$run = 'C:\Users\Administrator\.codex\worktrees\ed41\松光启动器\docs\market-execution\MR17-accessibility-keyboard\20261003-100000Z'
foreach ($pair in @(
  @('ui/css/figma.css','figma.css.mr17'),
  @('ui/index.html','index.html.mr17'),
  @('ui/js/launcher.js','launcher.js.mr17'),
  @('ui/js/i18n.js','i18n.js.mr17')
)) {
  Copy-Item (Join-Path "$run\green" $pair[1]) -Destination $pair[0] -Force
  (Get-Item $pair[0]).LastWriteTime = Get-Date
}
```
其余：删除 `ui/tests/a11y-keyboard.test.cjs`；6 个测试文件的替身补齐按 §五 反向删除；
白名单去掉类别 F 的新增行（18→17）。
**注意**：`ui/tests/news-link.test.cjs` 会话前**带 UTF-8 BOM**（本轮一度被写入剥掉，已补回），回退时请保持。

---

## 八、证据清单

```
red/01-a11y-before.log                   RED 基线 9 失败 / 0 通过
green/
  figma.css.mr17 / index.html.mr17 / launcher.js.mr17 / i18n.js.mr17   实现版备份
  01-a11y-first.log                      首次运行 9 通过 / 0 失败
  02-a11y-after.log                      还原后 9 通过 / 0 失败
  03-csharp-full.log                     752 通过 / 0 失败
  04..09-node-*.log                      追查 48 个假失败的过程日志（保留以便复验）
  10..12-node-full-final.log             230 通过 / 0 失败
  11-csharp-full-final.log               752 通过 / 0 失败
  git-status-after.txt
  changed-files.sha256.txt
manifest.json
```

**本报告不宣布阶段放行、不提交、不发布。**

---

## 九、补记（第 24 轮跨里程碑核对时发现）

**本包的变更清单漏了一个文件。** 我在修端口派生碰撞时改了
`dev/PineglowLauncher.Tests/ContentIconServiceTests.cs`，但生成 `green/changed-files.sha256.txt` 时
**漏掉了它** —— 于是跨里程碑的哈希核对里，这个文件的最新记录停留在 MR00 的输入清单，与当前内容不符。

已把该文件连同**当前**哈希补记进本包的变更清单（该文件自那次修复后未被再改，当前内容就是本包产物）。
**这类遗漏不会自己暴露**：本包的 9 个契约用例全绿、全量测试也全绿，只有跨里程碑核对才能发现"改了但没记"。
