# MR17 补测 · `makeEl` 替身补齐与 Tab 顺序静态清单（缺口登记表 A13）

- **run-id**：`20261003-400000Z`
- **性质**：把 MR17 的两条遗留项做成**机器可校验的守卫**；过程中发现**缺口比记录更宽**
- **依据**：`gap-register.md` A13："其它 `makeEl` 替身未补齐、tab 顺序未逐屏记录 | 补替身（已知模式）+ 静态清单"
- **最终状态**：**证据就绪**（4 个守卫用例；2 个 RED 成立）

---

## 一、① `makeEl` 替身补齐 —— **守卫立刻抓出两个 MR17 没记录的文件**

MR17 只点名了 `launcher-v3/v4/v5.test.cjs`。我把判据写成守卫（"**每个定义了 `makeEl` 的文件，
其替身都必须支持产品代码实际调用的 DOM 成员**"）后，它立刻报出：

```
launcher-behavior.test.cjs 缺少 focus
launcher-behavior.test.cjs 缺少 removeAttribute
news-behavior.test.cjs     缺少 focus
news-behavior.test.cjs     缺少 removeAttribute
news-behavior.test.cjs     缺少 closest
```

**缺口比 MR17 记录的更宽** —— 这正是"把它做成守卫"的价值。

**产品代码确实在调这些成员**（守卫里有反向核对，避免要求空谈）：

| 成员 | 产品代码调用处 |
|---|---|
| `focus` | `launcher.js:817/818/834/884` |
| `removeAttribute` | `launcher.js:877/878/879` |
| `closest` | 13 处 |

**已按同一口径补齐 5 个文件**：`launcher-v3/v4/v5`（`removeAttribute`/`focus`/`offsetParent`）、
`launcher-behavior`（同上）、`news-behavior`（再补 `closest`）。

---

## 二、② Tab 顺序**静态清单**（机器校验）

卡片要求"走六类界面记录 Tab 顺序"，MR17 只保证了**弹窗内**的顺序与返回。本轮补上**静态清单**并**钉住**：

| 用例 | 断言 |
|---|---|
| `A13-②：侧栏九类界面的 Tab 顺序` | 顺序必须是 **home → installations → worlds → servers → content → skins → downloads → news → settings** |
| `A13-②：静态 chrome 的可聚焦元素顺序` | 前四项 = **skip-link → btnMinimize → btnMaximize → btnClose**；`btnLangToggle` 必须存在且排在侧栏之后；静态可聚焦元素总数 = **15** |

**为什么是静态清单**：Node 测试跑在极简假 DOM 上，没有真实布局/焦点引擎；
真实逐屏 Tab 遍历需要浏览器。静态清单的价值是**把顺序固定下来** ——
任何调整侧栏/顶栏顺序的改动都会被这条用例标出来。

---

## 三、两个 RED

| RED | 改什么 | 结果 |
|---|---|---|
| **①** | 把 `launcher-v3` 替身里的 `focus()` 删掉 | **1 失败**：A13-① 报"缺少 focus" ✓ |
| **②** | 在 `index.html` 里**对调** `worlds` 与 `servers` 的顺序 | **1 失败**：A13-② 报"侧栏导航的 Tab 顺序变了 —— 请同步更新本清单与可访问性说明" ✓ |

两次 RED 后都**还原到修复后参照**并复核（`index.html` 与 `launcher-v3` 均逐字节一致）✓。

---

## 四、验证

| 检查 | 结果 |
|---|---|
| A13 守卫用例 | **4 通过 / 0 失败** |
| **Node 全量** | **234 通过 / 0 失败**（230 + 4；测试文件 18 → **19**） |
| C# 全量 | **817 通过 / 0 失败 / 0 跳过** |

---

## 五、仍未覆盖（如实列出）

| 项 | 说明 |
|---|---|
| **真实浏览器的逐屏 Tab 遍历** | **未做**。本轮是**静态清单**：覆盖侧栏九项 + 静态 chrome；**动态渲染出来的**每屏内容（按钮/输入框）不在其中 |
| 各屏内"焦点陷阱/顺序"的运行时行为 | 未验证（MR17 已覆盖弹窗内的顺序与返回） |
| 替身的**行为正确性** | 本轮只保证**成员存在**（`focus()` 只置一个标记位），不验证替身与真实 DOM 的语义等价 |

---

## 六、实际改动

| 文件 | 改动 |
|---|---|
| `ui/tests/a13-doubles-and-taborder.test.cjs` | **新增**（4 个守卫用例：替身成员 + 反向核对 + 侧栏顺序 + 静态 chrome 顺序） |
| `ui/tests/launcher-v3/v4/v5.test.cjs` | 替身补 `removeAttribute`/`focus`/`offsetParent` |
| `ui/tests/launcher-behavior.test.cjs` | 同上 |
| `ui/tests/news-behavior.test.cjs` | 同上 + `closest` |
| `docs/market-execution/gap-register.md` | A13 关闭 |
| `docs/market-task-plan-2026-10-03.md` | MR17 行补记 |

**产品代码零改动。**

---

## 七、证据清单

```
green/a13-doubles-and-taborder.test.cjs.fixed  新增守卫（修复后参照）
green/launcher-v3/v4/v5.test.cjs.fixed         替身补齐后参照
green/launcher-behavior.test.cjs.fixed         同上
green/news-behavior.test.cjs.fixed             同上
green/index.html.fixed                          RED 用参照
green/03-a13-fixed.log                          首次失败（抓出两个未记录文件）
green/06-a13-after-more-fixes.log               4 通过 / 0 失败
green/07-node-full.log                          234 通过 / 0 失败
green/05-csharp-full.log                        817 通过 / 0 失败
red/01-red-missing-focus.log                    RED①：删 focus → 失败
red/02-red-taborder-swapped.log                 RED②：对调侧栏顺序 → 失败
manifest.json
```

**本报告不宣布阶段放行、不提交、不发布。**
