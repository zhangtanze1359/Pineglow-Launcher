# 记录 103：更正记录 102 —— **UI 早就会显示加载器**，入口比我说的小

运行标识：`run-2026-10-10_015215-578dbafb` ｜ 2026-10-12 08:14–08:30（+08:00）
性质：**只读审计 + 文档更正，产品目录零改动**（0 staged）。

---

## 我上一轮说了什么，错在哪

记录 102 我写：「`launcher.js` 里渲染/显示 `loaderType`、`loaderVersion` —— **0 处**」
并据此判断「UI 里根本没有"加载器"这个概念」，把 UI 入口说成**三件套**。

**这是错的。** 我 grep 的是字面量 `loaderType`，而 UI 用的是**映射后的字段 `loader`**：

| 位置 | 内容 |
| --- | --- |
| `api.js:248`（真实映射，`mapInstance`） | **`loader: i.loaderType \|\| 'Vanilla',`** |
| `api.js:251` | `iconBg: defaultGradient(i.loaderType)` |
| `launcher.js:496` | `<span class="inst-tag">${esc(i.loader \|\| 'Vanilla')}</span>` ← **实例卡片上就显示着加载器** |
| `launcher.js:484` | `const kind = (i.loader && i.loader !== 'Vanilla') ? 'mod' : 'all';` ← 还用它做筛选 |

**所以 UI 一直在显示加载器**（实例名旁边的那个 `inst-tag`），而且用它区分"模组/原版"筛选。

## 因此 UI 入口是**两件套**，不是三件套

| # | 做什么 | 状态 |
| --- | --- | --- |
| 1 | `api.js` 加 `installLoader(instanceId, loaderType, loaderVersion)` 调用 | **要做** |
| 2 | 在实例视图里显示当前加载器 | **已经有了**（`launcher.js:496` + `api.js:248`） |
| 3 | 加"安装加载器"按钮与选择器（`bindInstallations()` `:3129`） | **要做** |

**比我上一轮说的少一件。** 而且第 2 件本来就是我"才意识到"的那件 —— 它**早就存在**。

## 这是同一类错误的第六个样本

| 轮次 | 我的检查规则 | 现实 |
| --- | --- | --- |
| 44 | 猜"令牌过期→401" | 没有任何 401 |
| 45 | "探针没记 URL" | 一直记着 |
| 48 | "产品流水线 FAIL" | 夹具没带 `.gitignore` |
| 57 | "prelaunch 假阴性" | 产品创建了那个目录 |
| 86 | 正则 `[a-z0-9\-]*` | 漏掉大写文件名 |
| **92** | **grep `loaderType`** | **UI 用的是映射后的 `loader`** |

**六次里有五次是"我的观测装置比现实窄"** —— 这一次的形式是
**"我在错误的抽象层上找东西"**：UI 不直接用后端字段名，中间隔了一层映射，
而我没去看那一层。

**教训**：**grep 一个字段名之前，先确认这个名字在这一层是否就是它。**
跨层（后端字段 → 前端映射 → 渲染）时，字段名**通常会被改**。

## 状态（更正后）

| 项 | 状态 |
| --- | --- |
| 缺陷 1 的后端 | 端到端验证通过（原版实例 → 40 秒 → Fabric） |
| 缺陷 1 的 UI 入口 | **未做**，需要**两件**（api 调用 + 按钮），不是三件 |
| 已就绪的补丁 | 18 条（已合并验证） |
| 待你决定 | ① 提交（(a) / **(a′) 已验证安全** / (b)）；② C 的范围；③ 是否做这两处 UI |

状态串：`TechnicalAcceptance=NOT_PASSED`；`FormalPhaseApproval=NOT_GRANTED`；`ReleaseApproval=NOT_GRANTED`。
