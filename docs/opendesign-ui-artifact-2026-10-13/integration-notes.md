# 集成说明 / Integration notes

松光启动器 · 局部错误状态修复原型 · PineGlow Launcher — local error-state fix prototype

> 本文件是**原型交付说明**，用于把本原型中的状态组件、样式与文案映射回现有壳。
> 本次**只做前端局部修改**：不生成 C# 后端、不替换任何 API、不改动服务端契约。
> 原型中的版本号、任务、内容与新闻均为**模拟数据**，不代表真实安装、恢复或登录成功。

## 1. 交付物 / Deliverables

| 文件 | 说明 |
|---|---|
| `index.html` | 自包含交互原型：应用壳 + 4 个状态流程 + 启动前检查 + 状态选择器。原生 HTML/CSS/JS，无构建步骤、无外部依赖。 |
| `pineglow-state-components.css` | 局部状态组件样式（token + 状态块 + 重试按钮 + 错误/空态 + 技术详情 + 原型面板）。 |
| `brand-spec.md` | 锁定的视觉系统（六项 OKLch token、字体栈、视觉规则）。 |
| `integration-notes.md` | 本文件。 |

本地打开 `index.html` 即可；在 **960×640** 与 **1280×800** 下均应可用（侧边栏收窄、内容区滚动）。
右下角“状态控制”面板用于复现每个状态；`收起` 后变为悬浮按钮。

## 2. 组件结构 / Component structure

原型壳（与现有壳一致的部分）：

```
.pg-app
├─ .pg-sidebar            左侧导航：首页/安装管理/世界/服务器/内容中心/皮肤/下载/新闻/设置
│  ├─ .pg-brand           顶部 PINEGLOW 松树标识 + 字标
│  └─ .pg-nav             [data-action="nav"] 项；[data-screen] 决定主区内容
├─ .pg-main
│  ├─ .pg-topbar          面包屑 + 账号状态 + 语言切换（中文 / EN）
│  └─ .pg-content
│     ├─ [data-view="home"]        首页（启动入口）
│     ├─ [data-view="downloads"]   #dlVersions + #dlTasks
│     ├─ [data-view="content"]     #contentBody
│     ├─ [data-view="news"]        #newsBody
│     └─ [data-view="other"]       范围说明占位
├─ #prelaunchOverlay      启动前检查对话框（role="dialog" aria-modal）
├─ #protoPanel            状态选择器（原型工具）
└─ #toast                 操作反馈
```

需要移植到现有壳的**状态组件**（对应生产缺陷）：

| 组件 | 关键类名 / 钩子 | 所在屏幕 |
|---|---|---|
| 下载源错误块 | `.download-source-error` + `.download-retry-btn` | 下载 · 可用版本 |
| 下载任务区（独立） | `.download-tasks` | 下载 · 下载任务 |
| 内容中心错误块 | `.content-error` + `.content-retry-btn`（**沿用生产已存在的类名**） | 内容中心 |
| 新闻状态条 | `.news-statusbar` + `.news-verified-at` | 新闻 |
| 通知 / 空态 / 技术详情 | `.state-block`、`.state-notice--ok`、`.tech-details` | 通用 |
| 启动前检查 | `#prelaunchOverlay .check-row` | 全屏对话框 |

所有动态文本经 `esc()` 转义后写入；技术详情使用 `<pre class="tech-body">` + `white-space:pre-wrap; overflow-wrap:anywhere`，长文本只换行不溢出。

## 3. CSS token / Tokens

组件样式全部基于 `:root` 中的 token，不写死颜色。可直接并入 `ui/css/pineglow.css`。

```
--bg #10191b   --surface #172326   --fg #edf3ef   --muted #a3b5b7
--border #2c3e42   --accent #91d5af
--sidebar #0d1517   --danger #ff8e86   --warn #eac585
--r-card 14px   --r-btn 8px   --r-chip 999px
--surface-2/3、--hairline、--rail 由上述用 color-mix(in oklch, …) 派生
--font-ui "Segoe UI","Microsoft YaHei UI",system-ui,sans-serif
--font-mono "Cascadia Code",Consolas,ui-monospace,monospace
```

约定：
- **一个强调色**，每屏最多出现两次（唯一主操作 + 激活态导航）。错误用 `--danger`，需处理用 `--warn`，已恢复用 `--accent`。
- 悬浮/交互态通过 `color-mix` 提升背景 L 值，文字颜色**只增不减**；禁用态才允许降低对比。
- 所有可聚焦元素有 `:focus-visible` 焦点环（2px `--accent`，offset 2px）。
- 按钮最小高度 38px（桌面指针）；若移植到触屏，请提升到 44px。

## 4. 状态矩阵 / State matrix

状态选择器可复现的状态：`normal`(正常有数据) · `loading`(加载中) · `error`(请求失败) ·
`retrying`(重试进行中) · `retrySuccess`(重试成功) · `retryError`(重试仍失败) · `empty`(成功空态)。
`重试结果` 开关决定点“重试”后落到 `retrySuccess` 还是 `retryError`。

| 组件 \ 状态 | normal | loading | error | retrying | retrySuccess | retryError | empty |
|---|---|---|---|---|---|---|---|
| 启动前检查 | 结果列表（通过 / 需处理） | `检查中…` 占位行 | —— | —— | —— | —— | —— |
| 下载 · 可用版本 | 版本列表 | 骨架行 | 错误块 + 重试，**明确“请求失败”** | 重试按钮：旋转 + 禁用 | 成功提示 + 列表 | 错误块 + “仍失败”提示 | “暂无可下载版本” + 刷新 |
| 下载 · 下载任务 | —— 独立区域，**任何状态下都保留已有任务** —— ||||||
| 内容中心 | 分类 + 卡片网格 | 骨架行 | `.content-error` 错误块 + `.content-retry-btn` | 重试按钮进行中 | 成功提示 + 网格 | 错误块 + “仍失败” | “暂无内容” + 刷新 |
| 新闻 | 已验证 + 最后验证时间 + 列表 | 骨架行 | 无缓存→**尚未验证、隐藏日期**；有缓存→保留时间 + 缓存角标 + 列表 | 重试按钮进行中 | 已验证 + 新时间 + 列表 | 同 error（尊重缓存开关） | “暂无新闻” + 刷新 |

三条生产缺陷的对应处理：

1. **下载源请求失败被误当成空数组** —— `error` 与 `empty` 是**两种不同** UI：失败显示 `.download-source-error` 并给出重试；只有请求成功且返回空列表才显示“暂无可下载版本”。`.download-tasks` 区在 `renderDownloads()` 中**始终渲染**，不受版本请求结果影响。
2. **新闻首次失败返回 `0001-01-01`** —— 渲染前判断零值：未验证时渲染 `尚未验证` 胶囊并**隐藏日期**（`newsStatusBar({verified:false})`）；已有缓存时 `verified:true` 并**保留**缓存验证时间。
3. **`content-error` / `content-retry-btn` 无样式** —— 在 `pineglow-state-components.css` 中为这两个类名补齐样式；主说明为通俗文案，原始主机与 `/api` 路径移入可展开的 `.tech-details`（转义为文本），长文本换行不溢出。

## 5. 建议映射到现有前端 / Suggested mapping

**不新增 C#，不改 API。** 以下为前端局部改动建议。

### `ui/js/launcher.js`

- 把“取版本列表”的三态显式区分，替换当前的 `catch → []`：
  - 定义 `loadState = 'loading' | 'ready' | 'error'`；
  - 成功且 `length === 0` → `ready` + 空态；成功且非空 → 列表；抛错 → `error`（**不要**映射成空数组）。
  - 暴露 `retryLoadVersions()`，重试期间把按钮切到 `.download-retry-btn` 的进行中态。
- 下载任务渲染与版本请求**解耦**：任务列表从独立数据源渲染，版本区重渲染时不清空 `#dlVersions` 之外的 `.download-tasks` 容器。
- 新闻：发送前不预设时间；收到 `verified_at` 后判断零值（`0001-01-01` / Go 零值 time）→ 未验证，隐藏日期；否则写入“最后验证”；失败时若存在缓存则保留原验证时间并展示缓存内容。
- 内容中心：请求失败时渲染 `.content-error`，把原始错误（host、`/api` 路径、状态码）转义后填入 `.tech-details`，主文案保持通俗。

### `ui/css/pineglow.css`

- 将 `pineglow-state-components.css` 中的 `:root` token 与本节组件样式并入；若现有文件已有同名 token，以现有值为准（同一份色板）。
- 必须补齐的选择器：`.content-error`、`.content-retry-btn`、`.state-block`、`.state-notice--ok`、`.tech-details` / `.tech-body`、`.download-tasks`、`.news-statusbar`。
- 保留 `:focus-visible` 焦点环与 `prefers-reduced-motion` 降级。

### `ui/js/i18n.js`

- 新增词条沿用原型前缀，便于对照：`downloads.*`、`content.*`、`news.*`、`prelaunch.*`、`state.*`、`proto.*`。
- 中文优先；`index.html` 顶部 `I18N` 对象已给出中英对照，可直接迁移。关键新增文案：
  - `downloads.errorTitle/Desc/Extra`（区分失败与空态）
  - `news.unverified` / `news.verifiedAt` / `news.cached`
  - `content.errorTitle/Desc`
  - `prelaunch.*`（检查项、可处理动作、返回 / 继续）

## 6. 无障碍与尺寸 / Accessibility & sizing

- 导航、按钮、筛选、状态选择器、`<details>`、模态均为键盘可操作；`Esc` 关闭对话框；打开时做焦点陷阱并在关闭后还原。
- 模态：`role="dialog"` + `aria-modal="true"` + `aria-labelledby`；toast 使用 `aria-live="polite"`。
- 焦点可见：`:focus-visible` 焦点环；悬停态文字对比不下降。
- 目标尺寸：桌面按钮 ≥38px；触屏移植提升至 44px。
- 962×640 起：侧边栏 188–208px，内容区独立滚动，无横向滚动。

## 7. 明确声明 / Explicit limitations

- 原型为**独立项目**，不读取也不修改 D 盘仓库；所有数据为模拟，且界面上以“原型数据”标注。
- “继续 / 安装 / 登录”等按钮**不会**真实启动游戏、安装运行库或登录账号；点击仅给出诚实的提示。
- 本原型**不包含**后端与真实 API；集成时请以后端返回的真实结构为准，仅复用此处的**状态判定与呈现规则**。

