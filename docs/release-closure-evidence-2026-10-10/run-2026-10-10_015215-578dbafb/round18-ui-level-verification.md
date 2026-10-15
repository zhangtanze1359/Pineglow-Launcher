# 第 18 轮：**界面级**验证（本会话第一次）—— 用真实 Chromium 引擎驱动出厂前端

运行标识：`run-2026-10-10_015215-578dbafb`（第 18 轮追加）
时间：2026-10-11 14:53–15:03，Asia/Hong_Kong（+08:00）
本轮性质：**只做验证与取证，产品目录零改动**（0 staged）。探针与脚本都写在证据目录，产品源码未动。
被验产物：`CA8F94C1A6173F23F38F3D2A3C949AFFC16D5AB67752B662C7806831F350D93E`（与 3 次流水线发布逐字节相同）。

---

## 1. 为什么要做、怎么做的

第 1–17 轮的验证都停在 **API 级**（直接打 `127.0.0.1:<port>/api/...`）。而 README 的第一句就是
「**纯 Web UI 架构**：Avalonia 仅作 WebView2 宿主，界面由 `ui/` 下的 HTML/CSS/JS 实现」——
也就是说**出厂界面本身从未被真正打开过**。基线评估里把「完整原生 WebView 全量交互扫描」列为未验证项，正是这一块。

本轮把这块补上：

| 组件 | 路径 | 作用 |
| --- | --- | --- |
| 驱动脚本 | `probe-ui-level.ps1` | 起启动器 → 取会话令牌 → 起 **headless Chrome** → 调 Node 探针 → 只停自己起的进程 |
| 基础探针 | `cdp-ui-probe.cjs` | 通过 **CDP**（Chrome DevTools Protocol）加载出厂页面，收集异常/console/HTTP 失败，截图、取渲染后 DOM |
| 导航巡检 | `cdp-ui-nav-sweep.cjs` | 逐一点击侧栏每一项，逐视图截图 + 取正文 + 记录该视图内的 JS/HTTP 错误 |

技术要点：**Node ≥ 22 内置 `fetch` 与 `WebSocket`**，所以整个 CDP 客户端**零 npm 依赖**——
这与本仓库「没有 `package.json`、没有 `node_modules`」的既有设计一致，没有给仓库引入任何新依赖。

*证据等级*：驱动的是 **headless Chrome 154**，**不是**产品实际使用的 WebView2 运行时。二者同为 Chromium 内核，但**不等价**；本轮能证明「出厂 `ui/` 在真实浏览器引擎里能加载、能渲染、能与本地 API 完成数据往返」，不能证明 WebView2 宿主特有的行为（如 `chrome.webview` 通道）。

---

## 2. 结果：界面整体通过

`round18-ui-level/`（首次加载）：

| 断言 | 结果 |
| --- | --- |
| 页面 load 事件 | ✅ |
| 页面标题 | `Pineglow · 松光启动器` |
| 渲染出的元素数 | 188 |
| 会话令牌 meta 存在 | ✅ |
| **未捕获 JS 异常** | **0** |
| **console.error** | **0** |
| 动态内容标记 | 版本号、MC 版本列表、实例名、下载文案、设置文案**全部命中** |
| 截图 | `ui-shot.png`（215,690 字节，1418×802） |

我在截图里**肉眼核对**了首页：标题栏、账号卡（d6tester / 离线模式）、侧栏两组导航、主视觉（「回到你的方块世界。」）、统计条（9m 总游玩时长 / 6 次游玩 / 7 个实例）、当前版本下拉与「开始游戏」按钮、最新资讯四张卡片——**全部正常渲染，数据来自本地 API**。

---

## 3. 九个侧栏视图逐一点击（`round18-ui-nav-sweep/`）

九个入口**全部点中**，各产生一张截图，**全部 0 JS 异常、0 console.error**：

| 视图 | 渲染结果（正文摘要） | 截图 |
| --- | --- | --- |
| 开始游戏 | 主视觉 + 统计 + 资讯 | 219,600 B |
| 安装管理 | 实例/版本管理界面 | 89,785 B |
| 世界 | 「你所有实例中的单人存档 / **还没有存档，进入游戏创建一个世界吧。**」（空态） | 37,627 B |
| 服务器 | 「你收藏的多人服务器 / 添加服务器 / **还没有添加服务器，点击右上角添加。**」（空态） | 39,181 B |
| 内容中心 | 见 §4 | 26,426 B → 复核 167,738 B |
| 皮肤 | 皮肤视图；`skin-image` 404（**离线账号本来就没有皮肤**，属预期） | 47,917 B |
| 下载 | 见 §4 | 26,437 B → 复核 60,582 B |
| 新闻 | 1,909 字符正文 | 94,181 B |
| 设置 | 「JAVA / 系统分配 / 内存分配 / 调整分配给 Minecraft 进程的内存上限，推荐默认 4GB。 / 4.0 GB / 自动内存分配」 | 89,023 B |

---

## 4. 一次自我更正：先报「内容中心/下载空白」，复核后**是我等得不够久**

第一次巡检（每视图停 **2.5 秒**）时，`内容中心` 与 `下载` 两视图的主区**看起来是空白的**，而且侧栏高亮**仍停在上一项**。
我当时的初步判读是「这两个视图可能是坏的」。

**我没有就此下结论，而是做了聚焦复核**：只跑这两个视图、把停留时间提到 **12 秒**（`round18-ui-focus-content/`）。结果两者都**完整渲染**：

- **内容中心**（3,383 字符）：「内容中心 / 搜索并安装模组、整合包、光影与资源包 / **Modrinth** / CurseForge / 模组 / 整合包 / 光影 / 资源包 / 默认游戏 · / **Sodium** / 作者：jellysquid3 / … / **Sodium Extra** / … / **Reese's Sodium Options** …」
  —— 截图显示真实的 Modrinth 目录：模组卡片、真实图标、作者、下载量（241.7M 等）、支持版本标签、`安装` 按钮、来源切换与搜索框。
- **下载**（1,615 字符）：「下载 / 下载 Minecraft 版本并查看进度 / 可用版本 / 正式版 / 快照 / 全部 / **26.3** 正式版 2026-09-15 / … / **1.21.11 正式版 2025-12-09 已安装** / 1.21.10 …」
  —— 真实的 Mojang 版本清单，并且**正确把已装的 1.21.11 标成「已安装」**。

**结论更正：这两个视图没有问题，是我的巡检停留时间不够**（它们要先联网取远端数据：Modrinth 搜索、Mojang 版本清单）。
两个视图都在同一台网络很差的机器上，2.5 秒显然不够。
→ **不计为产品缺陷**；并把巡检默认停留时间改为可配置（`cdp-ui-nav-sweep.cjs` 第 4/5 个参数：标签过滤、停留毫秒）。

---

## 5. 本轮新增的产品缺陷：**fake-IP 兼容开关在这种网络下不起作用**

### 5.1 现象

界面首页的资讯卡片图片位是**斜纹占位**，控制台显示 `/api/news-image?id=…` 全部 **400**。直接打 API 拿到精确原因：

```
{"error":"dns_mixed"}   HTTP=400
```

这台机器的解析结果（实测）：

```
launchercontent.mojang.com -> 198.18.0.33, 2001:2::21
www.minecraft.net          -> 198.18.0.67, 2001:2::43
```

即 **IPv4 落在 198.18.0.0/15（fake-IP），IPv6 落在 2001:2::/48（基准测试段）** —— 典型的 Clash 类 TUN/fake-IP 模式。

### 5.2 产品其实**专门为这种情况做了开关**——但它不生效

- `AppConfig.cs:112-117`：`NewsFakeIpCompat`，注释写明「**Clash 类 TUN/fake-ip 新闻图片兼容**，默认 false（fail closed）」。
- 设置页有开关：`ui/js/i18n.js:238-239` —— 「TUN/fake-IP 新闻图片兼容 / 默认关闭，仅影响官方新闻图片；**仅当 Clash 类 TUN/fake-ip 模式导致官方新闻图片无法加载时开启**。」
- 后端接线：`LocalApiService.cs:75-77`、`:1153-1154`、`:4966-4976`。

### 5.3 实测：把它打开，**依然 400 `dns_mixed`**

我把夹具配置的 `newsFakeIpCompat` 改成 `true`、重启、先取资讯列表（注册图片 ID）再取图片：

```
=== with newsFakeIpCompat = TRUE ===
{"error":"dns_mixed"}|HTTP=400
（实验后已把夹具 config.json 原样还原，newsFakeIpCompat 回到 False）
```

### 5.4 代码级根因（可解释为什么必然如此）

`NewsImageProxyService.NewsSsrfGuard.SelectConnectableAddress`（`:378-417`）：

```csharp
List<IPAddress> fakeIp = addresses.Where(IsFakeIpBenchmarkAddress).ToList();
if (fakeIp.Count > 0) {
    if (!allowFakeIpRanges) { /* dns_private 或 dns_mixed */ }
    if (fakeIp.Count != addresses.Count) {
        throw new NewsImageException("dns_mixed", "fake-ip mixed with other address classes");  // :410
    }
    return fakeIp[0];
}
```

而 `IsFakeIpBenchmarkAddress`（`:437-447`）：

```csharp
if (candidate.AddressFamily != AddressFamily.InterNetwork) { return false; }   // :440-443 只认 IPv4
```

**它只识别 IPv4 的 198.18/15。** 于是本机的两个解析结果里：`198.18.0.33` 算 fake-IP、`2001:2::21` **不算**，
`fakeIp.Count (1) != addresses.Count (2)` → 第 410 行**照样抛 `dns_mixed`**。

也就是说：**只要解析器同时返回 fake-IP 的 IPv4 和任何 IPv6 地址（双栈机器上是常态），这个开关就永远不可能生效。**
（若那条 IPv6 被归入「危险地址」，则第 393 行会更早抛同样的 `dns_mixed`——两条路径的结论一致。）

### 5.5 定性

| 维度 | 判断 |
| --- | --- |
| 是产品缺陷吗 | **是。** 一个为特定场景**专门提供**、且在设置页**明确宣传**的开关，在它宣称的场景里**不生效** |
| 触发条件 | Clash 类 TUN/fake-IP 模式 **且** 机器双栈（DNS 同时返回 IPv4 fake-IP 与 IPv6） |
| 影响面 | 官方新闻图片不显示（斜纹占位）。**不影响下载、安装、启动等核心功能** |
| 严重度 | **中低**：不阻断发布，但正好命中目标用户群里非常常见的一种网络配置，且会让用户按提示打开开关后**依然无效**，比"没有这个开关"更伤信任 |
| 修复方向 | 让 `IsFakeIpBenchmarkAddress` 也覆盖 IPv6 基准测试段（`2001:2::/48`，RFC 5180）/ 或在兼容模式下把「fake-IP + 非公网」视为同一类并允许选择；属产品改动，**需授权** |

---

## 6. 附带的两个小观察（都不构成缺陷）

1. **`/favicon.ico` 404**：`ui/index.html` 没有 `rel="icon"`，`ui/` 里也没有 favicon 文件，所以浏览器自动请求时 404。纯外观问题，一行即可修（或不修）。
2. **皮肤视图的 `skin-image` 404**：离线账号（d6tester）没有皮肤，属**预期**；正版账号下才会有。

---

## 7. 本轮的最终口径

- **界面级验证通过**：出厂 `ui/` 在真实 Chromium 引擎里能加载、能渲染、能与本地 API 完成数据往返；九个侧栏视图全部可达、全部无 JS 异常。
- 这条填上了基线评估里「完整原生 WebView 全量交互扫描」缺口的大部分（**WebView2 宿主特有的部分仍属未验证**）。
- 新增待决缺陷 1 条（fake-IP 兼容开关失效）。

状态串：`TechnicalAcceptance=NOT_PASSED`（发布整体）；`FormalPhaseApproval=NOT_GRANTED`；`ReleaseApproval=NOT_GRANTED`。
