# 集成说明 / Integration notes

松光启动器 · UI 重构原型 · Pineglow Launcher — UI refactor prototype

> 本文件把原型映射回现有壳，供后续 Harness 集成阶段使用。
> 本次**只交付前端设计原型**：不生成 C# 后端、不替换 API、不改动服务端契约、不触碰 `D:\松光启动器` 仓库。
> 原型内所有启动、下载、登录、备份结果均为**模拟**，界面已逐处标注。

## 1. 交付物 / Deliverables

| 文件 | 说明 |
|---|---|
| `pineglow-launcher.html` | 自包含应用外壳原型：标题栏 + 侧边栏 + 9 个模块视图 + 命令面板 + 模态框 + Toast。原生 HTML/CSS/JS，无外部依赖、无网络请求。 |
| `brand-spec.md` | 锁定的视觉契约：六个 OKLch 令牌、字体栈、五条视觉规则、不变量。 |
| `integration-notes.md` | 本文件。 |

打开 `pineglow-launcher.html` 即可评审。原型在 **1280×800** 与 **960×640** 下均无横向滚动；
`localStorage` 记忆当前模块（`pineglow.view`）与默认实例（`pineglow.instance`）。

## 2. 与现有壳的对应 / Mapping to the existing shell

模块、文案与业务语义与 `ui/js/launcher.js` 完全一致，只替换呈现层。

| 原型 | 现有壳 | 说明 |
|---|---|---|
| `.titlebar` | `.title-bar` | 40px，品牌 + 窗口控制；关闭键 hover 用 `--danger` |
| `.sidebar` / `.nav-item` | `.sidebar` / `.navigation` / `.nav-item` | 236px，三段分组（游戏 / 资源库 / 系统）；激活项加 `aria-current="page"`（修正原导航缺少当前页标记） |
| `.topbar` | 各视图 `.page-head` | 标题与副标题统一提到顶栏，视图内不再重复 |
| `.view` | `.view[hidden]` | 由 JS 渲染，最大宽度 1200px |
| `.card` / `.card-ring` | `.card` / `.settings-section` / `.inst-card` | 面板统一为 12px 圆角、`rgba(255,255,255,.06)` 包边 |
| `.list-row` | `.inst-card` / `.world-card` / `.server-card` / `.version-row` | 卡片网格改为行式列表，信息密度更高、扫读更快 |
| `.btn` 系列 | `.play-btn` / `.btn-new` / `.modal-btn` / `.setting-btn` | 统一 34px（`btn-lg` 44px），文字 `nowrap` |
| `.seg` | `.filters` / `.filter` / `.source-btn` | 分段控件，`aria-pressed` 表达选中 |
| `.switch` | `.toggle` | `role="switch"` + `aria-checked` 原生同步 |
| `.modal` / `.scrim` | `.modal-host` / `.modal` | 焦点陷阱、`Esc`、焦点归还、危险操作不默认聚焦 |
| `.drawer` / `.scrim.right` | 原「管理 Mod」居中弹窗 | 改为右侧抽屉：实例列表不被遮挡，启停与移除在原位完成，危险操作改为抽屉内联确认而非嵌套弹窗 |
| `.treemap` / `.tm-group` / `.tm-cell` | 无（新增模块 `游戏时长`） | 时长树状图：平方化布局，外层按实例分组、内层按单次游玩，面积 ∝ 分钟数 |
| `.cal-grid` / `.cal-cell` / `.cal-bar` | 无（新增模块 `游戏时长`） | 日历热力格：格内底部填充条高度 ∝ 当日时长，有记录的日期可点击作为筛选条件 |
| `.palette` | — | 新增：`Ctrl/⌘ + K` 快速操作（页面跳转、启动、检查更新、日志目录、离线模式） |
| `.toast` | `#toast` | 异步结果唯一出口，`role="status"` + `aria-live="polite"` |
| `.state` / `.err` / `.tech` | `.state-block` / `.empty-state` / `.tech-details` | 空态与错误态仍是两种 UI；技术详情默认折叠 |

`SG.api` 方法面未做任何假设性扩展：原型里的每个入口都能对应到已存在的方法
（`getState` / `getVersions` / `createInstance` / `registerInstance` / `deleteInstance` / `getWorlds` /
`createSaveBackup` / `getServers` / `pingServer` / `addServer` / `deleteServer` / `searchContent` /
`installContent` / `getRemoteVersions` / `getDownloads` / `downloadVersion` / `getNews` / `openNewsLink` /
`getSources` / `selectSource` / `updateConfig` / `checkUpdate` / `getAccounts` / `uploadSkin` 等）。

## 3. 令牌绑定 / Tokens

`:root` 直接粘贴 Raycast 设计系统的 token 契约，未新增、未重定义任何品牌令牌。
所有界面颜色都由这六个令牌经 `color-mix()` 派生，文件内除 `:root` 外没有裸 hex。

派生层如遇旧渲染口径不接受 `color-mix()`，可用下列静态等价色替换（`transparent` 参与混合时按预乘 alpha 计算，等价于同名 rgba）：

| 派生令牌 | 静态等价 |
|---|---|
| `--fill-1` / `--fill-2` / `--fill-3` | `rgba(249,249,249,.04)` / `.07` / `.11` |
| `--line-2` / `--line-3` | `rgba(249,249,249,.10)` / `.16` |
| `--accent-soft` / `--danger-soft` | `rgba(255,99,99,.15)` |
| `--success-soft` | `hsla(151,59%,59%,.15)` |
| `--warn-soft` | `hsla(43,100%,60%,.15)` |
| `--info` / `--info-soft` | `hsl(202,100%,67%)` / `hsla(202,100%,67%,.15)` |
| `--scrim` | `rgba(7,8,10,.76)` |

松林夜景（`ui/assets/pine-forest.svg`，1000×380）保留使用，但按令牌重新着色：
天空=画布近黑、落日=`--accent`、三层山脊与松树=`--bg` 与 `--muted` 的 `color-mix` 中间色。
该图层为纯装饰（`aria-hidden`），着色值需在真实 WebView2 中采样后固化为静态色。

## 4. 状态矩阵 / State matrix

**启动流程**（原型为模拟）：
`idle` → 启动前检查模态框（Java / 内存 / 文件完整性 / 图形驱动 / 账户五项，账户项为警告级）
→ `仍然启动` → `running`（按钮变为「结束游戏」，卡片出现运行中胶囊与计时）
→ `结束游戏` → `idle`。
无实例时入口降级为「新建实例」；`Enter` 键在模态框内触发主操作。

**下载任务**：`queued` 排队中 / `downloading` 下载中（进度条 + 速率 + 剩余时间）/ `completed` 已完成。
暂停与取消为独立图标按钮（均有 `aria-label`）。
**下载源失败**与**空列表**仍是两种 UI：失败显示 `.err` 错误块 + 重试 + 技术详情；成功但为空才显示「暂无可下载版本」。
`#download-tasks` 区在版本源失败时**照常渲染**已有任务。

**离线模式**（设置 → 账户，或 `Ctrl+K`）是查看错误态的正式入口：
开启后下载源、内容中心、新闻同时进入失败态，关闭后恢复，无需任何设计器面板。

**新闻**：`live` 已同步官方最新 / `cached` 离线缓存 / `尚未验证`（隐藏日期）/ `error` 加载失败。
零值验证时间视为未验证。

**管理 Mod 抽屉**（实例列表 → 行内「Mod」按钮，或「更多 → 管理 Mod」）：
两个标签页——`模组列表`（搜索 + 全部 / 已启用 / 已禁用筛选 + 逐行启停开关 + 移入恢复区）与 `恢复区`；
抽屉头部实时显示 `版本 · 加载器 · N 个 Mod · 恢复区 M 项`，标签页数字随之更新。

- **启停**：`role="switch"` 开关，行内同时给出「已禁用」文本胶囊，状态不只靠颜色；文件名在禁用态降为 `--muted`（唯一允许降对比的状态）。
- **移除**：点删除后在该行下方展开内联确认条（`取消` / `移入恢复区`），初始焦点给「取消」，不弹出嵌套对话框。确认后文件进入恢复区，并从实例的 Mod 计数中扣除。
- **恢复区状态**：`quarantined` 可恢复 / `restored` 已恢复 / `partial` 部分恢复 / `conflict` 存在冲突 / `failed` 隔离失败。默认保留 30 天，恢复不覆盖原位置的同名文件。
- **恢复**：普通条目执行「恢复」；`reason = Mod 更新` 的条目执行「撤销本次更新」——先移除当前新版本，再放回旧版本，还原的 Mod 默认禁用。命中冲突时状态转为 `partial` 并在行内显示「! 原位置已有同名文件，已跳过（未覆盖）」。
- 所有启停、移除、恢复均为模拟操作，不会改动磁盘上的任何文件；界面与 toast 均已标注。

**游戏时长**（侧边栏「游戏时长」，或 `Ctrl+K → 打开 · 游戏时长`）：新增的第 10 个模块，由一张概览数据条 + 时长树状图 + 日历组成。

- **数据来源**：原型内置一套固定种子的模拟游玩记录（`mulberry32(20261013)`，覆盖最近 120 天，每天 0–2 次会话，时长按短 / 常规 / 长三档分布）。种子固定，因此每次打开图表完全一致，便于评审对照。
- **范围**：`本周` / `本月` / `近 7 天` / `近 30 天` / `自定义`。自定义展开两个原生日期选择器；校验规则为「起止必填、开始不晚于结束、跨度不超过 120 天」，失败时在图位显示错误块并保留上一次的有效结果。
- **树状图**：平方化（squarified）布局，外层方块 = 实例（按范围内总时长分配面积），内层方块 = 单次游玩（按分钟数分配）。方块过小时自动隐去文字标签，只保留色块。悬停或键盘聚焦任一方块，读出条显示「日期 · 实例 · 时长」；点击方块把该日期设为筛选条件。
- **日历**：周一起始的月历，格内底部填充条高度按当月最大值归一化（纯填充编码，不依赖颜色深浅），右上角显示时长。有记录的日期可点击，作为叠加筛选；再次点击或「清除筛选」取消。月份可前后翻页，与图表范围相互独立。
- **颜色**：四个实例分别取设计系统的 `--accent` / `--info` / `--success` / `--warn`，填充统一压到 20%（分组标题条 34%），保证方块内文字始终满足对比度；日历与其余控件保持单色。
- 时长同时提供两种写法：正文用「2 小时 15 分」，方块与图例用等宽的紧凑写法 `2h15` / `48m`。
- **绘制契约（勿退回）**：树状图在容器尺寸读数为 0 时**不得留空**——用卡片宽度与 CSS 高度兜底照常绘制，下一帧再用真实尺寸重画；尺寸监听必须同时比较宽与高，只比较宽度会让「宽度已就位、高度仍为 0」的首帧状态永远不触发重排；容器缺高时由脚本补写内联高度；布局过程包在 try/catch 内，失败时渲染带原因的失败态而不是空白。

## 5. 动效规范 / Motion

克制路线，全部走 `--motion-fast 150ms` / `--motion-base 200ms` / `cubic-bezier(.2,0,0,1)`：

- 视图切换：`view-in` 220ms，透明度 + 8px 上移；
- 列表入场：`row-in` 240ms，逐项 22ms 递增，第 7 项起封顶 130ms；
- 模态框与命令面板：`pop-in` 200ms，`translateY(6px) scale(.985)` → 1；
- Toast：进出 200ms；
- 悬停：背景 L 通道 +7%~11%，或描边提升一档；文字颜色只增不减；
- 运行计时与下载进度每 2s 局部更新（直接改文本与 `width`，不重渲染整棵视图，避免焦点丢失）；
- `prefers-reduced-motion: reduce` 时所有位移与缩放归零，仅保留透明度。

## 6. 无障碍与尺寸 / Accessibility & sizing

- 全局 `:focus-visible` 焦点环（`--focus-ring`，Raycast 蓝），`:focus { scroll-margin: 12px }`。
- 跳过链接指向 `#viewRoot`（`tabindex="-1"`）。
- 模态框：`role="dialog"` + `aria-modal` + `aria-labelledby`，`Esc` 关闭，`Tab` 焦点陷阱，关闭后焦点归还触发元素，初始焦点避开危险按钮。
- 动态文本一律经 `esc()`；长文本 `overflow-wrap: anywhere` + `min-width: 0`。
- 状态不只靠颜色：警告用「! 需注意」文案、离线用文字 + 信号条数量、完成用「已完成」文本。
- 目标尺寸：桌面按钮 34px / 图标按钮 30px（均高于 WCAG 2.5.8 的 24px 下限）；
  `@media (pointer: coarse)` 下全部提升到 44px。

## 7. 需要在真实 WebView2 中复验 / Unverified

以下项目在本原型中**未经真实环境验证**，集成时请逐项确认：

1. 真实 WebView2 下的键盘操作与屏幕阅读器宣读；
2. 125% / 150% / 200% 缩放；
3. `color-mix(in oklch, …)` 在目标 WebView2 版本的渲染结果（必要时用第 3 节的静态等价色）；
4. 松林夜景重着色后的实际对比度（左侧标题区必须始终满足 4.5:1）；
5. 各视图在 960×640 下的实际换行情况。

## 8. 明确声明 / Explicit limitations

- 原型为**独立项目**：不读取、不修改 `D:\松光启动器`，不操作 Git、用户配置或账号凭据。
- 所有数据为模拟：版本号、项目名与作者取自真实世界以便评审，**进度、延迟、在线人数、体积、时间均为模拟值**，界面已逐处标注「模拟」。
- 「启动游戏 / 下载 / 上传皮肤 / 登录 / 备份 / 恢复 / 删除 / 打开目录」**都不会真实执行**，点击只给出诚实的模拟提示。
- **皮肤模块不内置 64×64 贴图**：贴图来自 Mojang 会话服务，原型无网络，因此未登录时显示明确的「无法获取皮肤贴图」状态，而不是用任意配色拼一个替身人形。
- 本原型不含后端与真实 API；集成时以后端返回的真实结构为准，仅复用此处的**呈现规则与状态判定**。
