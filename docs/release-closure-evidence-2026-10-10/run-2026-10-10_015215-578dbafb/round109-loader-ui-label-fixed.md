# 记录 109：修掉占位符 `L` —— 按钮现在显示文案键

运行标识：`run-2026-10-10_015215-578dbafb` ｜ 2026-10-12 10:00–10:14（+08:00）
性质：**产品目录零改动**（0 staged）。补丁在隔离副本验证。

---

## 上一轮截图暴露的问题

截图里按钮上是个光秃秃的 **`L`** —— 我写补丁时把它当**临时占位**，结果它成了用户看到的文字。

## 修法：用**已经加好的**文案键

`t('inst.installLoader')` 这个键我在同一补丁的第 4 步里**已经加过**
（中文"安装加载器"、英文"Install loader"）—— **只是第 2 步没用上它**。

```diff
- …aria-label="${esc(t('inst.installLoader'))}">L</button>
+ …aria-label="${esc(t('inst.installLoader'))}">${esc(t('inst.installLoader'))}</button>
```

改动落在产品文件里现在是：

```
launcher.js:503
<button class="inst-more" data-loader="${esc(i.id)}"
        title="${esc(t('inst.installLoader'))}"
        aria-label="${esc(t('inst.installLoader'))}">${esc(t('inst.installLoader'))}</button>
```

## 验证

```
[OK] P-LOADERUI 1/4 … 4/4b   （5 步全部应用）
node --check  api.js / launcher.js / i18n.js  → 全部 exit 0
Node 全量      305 通过 / 0 失败
UI 资源闭包    exit 0
```

## 这条修正的来历（值得记）

| 谁发现的 | 发现了什么 |
| --- | --- |
| 编译器 | **没发现**（JS 不编译） |
| `node --check` / Node 全量 / UI 闭包 / 构建 / 冷启动 | **全都没发现** |
| **截图** | **发现了** |

**静态门禁查的是"能不能跑"，不是"看起来对不对"。** 一个占位符能让所有门禁全绿，
因为它语法正确、能解析、能被内嵌、能启动 —— **它只是难看且无意义**。

## ⚠️ 一个必须说明的连带影响

**我改的是补丁内容**，所以第 96 轮那次「十九条合并验证」**在严格意义上已经过期**
（补丁 2 的产物变了）。本轮只重跑了**静态门禁**（应用 / 语法 / Node / UI 闭包），
**没有重跑完整的十九条合并验证**（构建 + 测试 + 锁定发布 + 冷启动）。

**下一轮应当重跑一次**，把这一版内容纳入。

## 当前状态

| 项 | 状态 |
| --- | --- |
| 已就绪的补丁 | 19 条 |
| 合并验证 | 第 96 轮那次对应**旧内容**；本轮改动后**待重跑** |
| 缺陷 1 | 后端端到端验证通过；UI 渲染已验证、**文案已修**、**交互未验** |
| 待你决定 | ① 提交（(a) / **(a′) 已验证安全** / (b)）；② C 的范围；③ 是否做缺陷 13 ③ |

状态串：`TechnicalAcceptance=NOT_PASSED`；`FormalPhaseApproval=NOT_GRANTED`；`ReleaseApproval=NOT_GRANTED`。
