# 两条图片代理的根因追查：一条已解释清楚，另一条**推翻了我上一轮的猜测**（第 38 轮）

> **⚠️ 第 39 轮终局更正**：本文 §4 把图标那条写成"真机必须确认的疑点"。第 39 轮重复实验（6 次）表明
> **端点本身可用**：4 次连续 **200 / image/webp / 4,800 B / 1.3–1.8s**，只有最早 2 次是 **504 / 恰好 5,046ms**
> （= 5 秒连接预算超时，偶发）。⇒ 定性改为"**非缺陷；仅有'连接超时 5s 且不重试 ⇒ 偶发丢图'的加固点**"。
> 见 `run-20261006-0920-icon-flakiness/`。

日期：2026-10-06（Asia/Hong_Kong）。本轮**未改动任何 tracked 文件**。
上一轮（`run-20261006-0830-r06-services/`）把两条图片端点记为"未判定（环境可疑）"。本轮把它们追到底，
结论是**一条能解释、另一条必须更正我的说法**。

## 1. 上一轮的说法与实测现象

| 端点 | 现象 |
| --- | --- |
| `/api/news-image?id=<真实 id>` | **400 / 9ms** |
| `/api/content/icon?…&projectId=AANobbMI&…` | **504 / 5,046ms** |

我上一轮猜的是"两者都因为 fake-IP（198.18.0.x）被 SSRF 防护拒绝"。**这个猜测对图标那条是错的。**

## 2. 关键代码事实

| 事实 | 位置 |
| --- | --- |
| 图标端点的状态映射：`IconStatus.NetworkError` → **504**（"上游超时或不可达"）| `LocalApiService.cs:5218-5219` |
| IP 校验失败走的是 **502**（不是 504）| `ContentIconService.cs:704-707`（`outcome.StatusCode = 502`）|
| 图标传输：`ConnectTimeout = 5s`、`TotalTimeout = 12s`、`UseProxy = false`、`ConnectCallback` 绑定"已验证 IP" | `ContentIconService.cs:52-53, 222-232, 245-257` |
| `IsPublicAddress(198.18.0.33)` **返回 true**（私网清单只有 10/127/0/≥224/169.254/172.16-31/192.168/100.64-127，**没有 198.18/15**）| `SkinImageProxyService.IsPublicAddress` |
| 新闻图片有**显式**的 fake-IP 兼容开关，且**默认 fail closed** | `NewsImageProxyService.cs:62-64, 144`、`AppConfig.NewsFakeIpCompat` |

⇒ 两点推论：
1. **图标那条不可能是"IP 被拒"**——被拒会返回 **502**，而实测是 **504**（= `NetworkError`）；
2. **新闻那条属于设计内的 fail-closed**（fake-IP 被显式拒绝；`unknown_image` 也会是 404，而实测是 400，符合策略/DNS 类）。

## 3. 决定性的对照实验：网络路径本身是好的

在这一台机器上做三组对照（同一个 URL：`https://cdn.modrinth.com/data/AANobbMI/icon.png`）：

| 客户端 | 结果 |
| --- | --- |
| PowerShell `Invoke-WebRequest`（走 WinINET 系统代理）| **200 / 22,884 B / image/png** |
| .NET 10 `SocketsHttpHandler`，`UseProxy=true`（系统代理 `127.0.0.1:7890`）| **OK 22,884 B / 1,688ms** |
| .NET 10 `SocketsHttpHandler`，`UseProxy=false`（**直连**）、`ConnectTimeout=5s` | **OK 22,884 B / 1,592ms** |

环境侧也确认了代理配置存在：注册表 `ProxyEnable=1`、`ProxyServer=127.0.0.1:7890`（WinHTTP 仍是 Direct）。
DNS 两次探测（.NET Framework 与 .NET 10）都只解析出**单个 A 记录** `198.18.0.33`；
`ChooseConnectionAddress` 优先 IPv4 ⇒ 应用要连的就是这个地址。

**结论**：从这台机器**直连那个 IP 是通的（1.6 秒拿到 22,884 字节）**，而候选产物的图标端点在它自己的
5 秒连接预算内失败并返回 504。⇒ **网络路径不是原因**，差异出在应用自己的取图流程里。

## 4. 修正后的结论

| 端点 | 修正后的定性 |
| --- | --- |
| `/api/news-image` → 400 | ✅ **解释清楚，属设计行为**：fake-IP 被安全检查拒绝（`NewsFakeIpCompat` 默认 false = fail closed）。**不是缺陷**；真机（正常 DNS）下不会触发。 |
| `/api/content/icon` → **504** | ⚠️ **本机根因未判定，但已排除两种解释**：<br>① 不是"IP 被 SSRF 拒绝"（那会是 502，且 `IsPublicAddress` 允许 198.18/15）；<br>② 不是"上游不可用"（同 IP 直连 1.6 秒成功、PowerShell 也拿到图）。<br>**这是一条必须在真机上确认的疑点**，不能写成"环境问题所以没事"。 |

我上一轮把它写成"环境可疑"，**方向是对的但解释是错的**，这一轮更正。两种仍可能的原因（都未证）：
应用进程内的 DNS 解析行为与独立进程不同（`ContentIconService.cs:711` 的 catch 会把解析异常变成 504）；
或它的"绑定已验证 IP 的自建 Socket 连接"在本机这套 fake-IP + 本地代理组合下被吞掉（而 `SocketsHttpHandler`
自带的连接路径不会）。二者都只能在**正常网络的真机**上区分。

## 5. 对文档的更正与新增检查项

- `run-20261006-0830-r06-services/r06-services-report.md` §3 的"两条都未判定"要拆分成本文 §4 的两行；
- 复评 §3 补充段里"两条图片代理未判定"同步更正；
- **真机检查项（新增）**：装好后的机器上打开"资源/Mod 搜索"，确认 **Modrinth 图标真的显示**；
  若真机上仍 504，则这是产品侧需要修的问题（届时用同一份对照实验定位到 DNS 还是自建连接）。

## 6. 复现命令

```powershell
# 1) 环境代理
netsh winhttp show proxy
Get-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Internet Settings' | Select ProxyEnable,ProxyServer

# 2) .NET 10 直连对照（用 file-based app，不需要建工程）
#    见本文 §3 三段：PowerShell / UseProxy=true / UseProxy=false

# 3) 应用侧现象（上一轮已记录）
Get-Content docs\release-remediation-evidence-2026-10-05\run-20261006-0830-r06-services\r06-services.log
```
