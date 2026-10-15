# MR07 补测 · 二分定位的「临时测试世界 / 实例副本」安全引导（缺口登记表 A6）

- **run-id**：`20261003-450000Z`
- **性质**：补上计划要求、MR07 **完全没做**的那段 UI 引导 + 用例
- **依据**：`MR07-bisect-restart-recovery` 报告 §六：
  "**"临时测试世界 / 实例副本"的引导未实现** | 计划要求"UI 要求在临时测试世界/实例副本运行"并给出可校验的存档备份/副本指引。本包**没有**做这部分 UI/文案，也没有触碰存档服务。"
- **最终状态**：**证据就绪**（4 个用例；RED 成立）

---

## 一、实现：面板顶部**常驻**安全引导

`openModBisect` 的模态里，标题与副标题之后插入常驻容器：

```html
<div class="bisect-safety" id="bisectSafety"></div>
```

紧接着填充（**不在任何分支里** —— 因此"未完成会话""需要恢复"等状态同样能看到）：

```js
const safety = document.getElementById('bisectSafety');
if (safety) {
    safety.innerHTML = `<div class="bisect-safety-title">${esc(t('bisec.safetyTitle'))}</div>` +
        `<div class="bisect-safety-body">${esc(t('bisec.safety'))}</div>`;
}
```

**放在"常驻"位置是刻意的**：安全引导在**开始之前**和**恢复场景**下最需要看到，
而不是只在"可以开始"的那个分支里 ✓。

### i18n（中英各两键）

| 键 | 中文 | English |
|---|---|---|
| `bisec.safetyTitle` | 请先在**临时测试世界或实例副本**上运行 | Run this on a **temporary test world or a copy of the instance** |
| `bisec.safety` | 本流程会分批启停 Mod，可能影响正在运行的世界。启动器**不会自动备份**或复制你的存档，请自行备份原存档，或改用实例副本 / 临时测试世界后再开始。 | … The launcher **does NOT back up** or copy your saves - back up the original save yourself, or switch to an instance copy / temporary test world before starting. |

**措辞刻意说清三件事**（正是计划要的"可校验指引"）：
① 临时测试世界 / 实例副本；② 自行备份；③ **启动器不自动备份**（避免用户误以为已经备份）。

---

## 二、用例（4 个）

| # | 用例 | 断言 |
|---|---|---|
| ① | 面板**常驻**渲染引导 | 模态里有 `bisectSafety` 容器；`openModBisect` **函数体内**填充它并使用两个键（**限定在函数体内**，防止"写在别处但面板不显示"） |
| ② | 中英**两种语言**都定义了这两个键且非空 | 每个键在 `i18n.js` 里出现 **2 次**（zh + en），取值非空 |
| ③ | 中文文案覆盖三件事 | 含「临时测试世界」「实例副本」「备份」，且明确写"不会自动备份" |
| ④ | 英文文案同样覆盖 | `temporary test world` / `instance copy` / `back up` / `does NOT back up` |

> **为什么用静态断言**：Node 侧是极简假 DOM，没有真实渲染；这类"文案必须存在且说到点子上"的契约
> 用源码级断言最稳（与 A13 的静态清单同一思路）。用例头部已写明这一口径。

---

## 三、RED（含一次**没生效**的尝试，如实记录）

| RED | 改什么 | 结果 |
|---|---|---|
| 1（**无效**） | 把填充改成 `if (false && safety)`（只改**运行时条件**） | **0 失败** ✗ —— 因为用例是**源码级**断言，源码里的键用法还在 |
| **2（有效）** | **删掉源码里 `bisec.safetyTitle` 的用法**（模拟"引导未实现"） | **1 失败**：A6-① ✓ |

RED 后 `launcher.js` **逐字节还原** ✓。

> 教训：**RED 必须与断言的层级一致** —— 源码级断言要用源码级的破坏来证伪。

---

## 四、验证

| 检查 | 结果 |
|---|---|
| A6 用例 | **4 通过 / 0 失败** |
| **Node 全量** | **238 通过 / 0 失败**（234 + 4；测试文件 19 → **20**） |
| C# 全量 | **823 通过 / 0 失败 / 0 跳过** |
| `node --check` | `launcher.js` / `i18n.js` 均通过 ✓ |
| RED | 1 失败（源码级）✓ |
| 还原 | 逐字节一致 ✓ |

---

## 五、实际改动

| 文件 | 改动 |
|---|---|
| `ui/js/launcher.js` | 二分面板加常驻安全引导块并填充 |
| `ui/js/i18n.js` | 中英各加 `bisec.safetyTitle` / `bisec.safety` |
| `ui/tests/a6-bisect-safety-guidance.test.cjs` | **新增**（4 个用例） |
| `scripts/source-snapshot-inputs.txt` | 登记新测试（类别 F +1） |
| `docs/market-execution/gap-register.md` | **A6 关闭** |
| `docs/market-task-plan-2026-10-03.md` | MR07 行补记 |

---

## 六、仍未覆盖

| 项 | 说明 |
|---|---|
| **真实 GUI 渲染** | 未验证（无桌面会话）；本轮是**源码级契约** + 语法检查 |
| **存档备份/复制的实际动作** | **没有做**（计划只要求"指引"；启动器不触碰存档路径这一点**未被改变** ✓） |
| "不自动进入真实世界 / 不承诺通用禁用世界保存" | 仍未实现世界相关逻辑（MR07 报告已列） |
| 真实 Minecraft 测试副本 | 未启动游戏 |

---

## 七、证据清单

```
green/launcher.js.fixed                          面板改动后参照
green/i18n.js.fixed                              中英键参照
green/a6-bisect-safety-guidance.test.cjs.fixed   新用例参照
green/01-a6.log                                  4 通过 / 0 失败
green/02-node-full.log                           238 通过 / 0 失败
green/03-csharp-full.log                         823 通过 / 0 失败
red/01-red-no-guidance.log                       RED①（只改运行时条件 → 无效，0 失败）
red/02-red-key-usage-removed.log                 RED②（源码级 → 1 失败）✓
manifest.json
```

**本报告不宣布阶段放行、不提交、不发布。**
