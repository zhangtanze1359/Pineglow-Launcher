# 第 12 轮：镜像库下载 403 的根因已完全确定（User-Agent）

运行标识：`run-2026-10-10_015215-578dbafb`（第 12 轮追加）
时间：2026-10-11 14:16–14:24，Asia/Hong_Kong（+08:00）
本轮性质：**只做验证与取证，产品目录零改动**（0 staged）。
本轮目标：把第 9 轮留下的「镜像下库下载 403，可复现但未完全归因」闭合掉。

---

## 1. 结论（先给答案）

**根因确定：产品用的 `.NET HttpClient` 默认不发送 `User-Agent`；镜像 302 跳转后的落点（学术镜像）对「无 UA」和「浏览器式 UA」返回 403，对「工具式 UA」返回 200。**

因此第 9 轮观察到的 403 **不是网络问题、不是镜像本身坏、不是 TLS 问题**，而是一个**确定性、可 100% 复现**的产品侧缺陷。
修复方向（一行）：**给下载用的 `HttpClient` 设一个显式的非浏览器 `User-Agent`**（例如 `PineglowLauncher/<版本>`）。

---

## 2. 实验设计

被验 URL（与产品在第 9 轮实际请求的完全一致）：

```
https://bmclapi2.bangbang93.com/maven/net/fabricmc/fabric-loader/0.19.5/fabric-loader-0.19.5.jar
```

客户端用**与产品同族**的 .NET 10 `HttpClient`（不是 curl——第 9 轮的教训正是「curl 通不代表产品通」）：

```csharp
new HttpClientHandler { AllowAutoRedirect = true, UseProxy = false }   // 与产品默认一致
new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(60) }
// 逐例设置 User-Agent；每例重复 2 次；读完整个响应体并统计字节数
```

源码与原始日志：`round12-ua-rootcause/Program.cs`、`probe.csproj`、`ua-matrix.log`

---

## 3. 实测结果（10/10 一致，无一次例外）

| `User-Agent` | 第 1 次 | 第 2 次 | 字节数 |
| --- | --- | --- | --- |
| **不发送（.NET 默认）** | **403 Forbidden** | **403 Forbidden** | 206（错误页） |
| `curl/8.4.0` | 200 OK | 200 OK | **1,984,980** |
| **`PineglowLauncher/1.0`** | **200 OK** | **200 OK** | **1,984,980** |
| `Mozilla/5.0` | 403 Forbidden | 403 Forbidden | 19,233（错误页） |
| `Wget/1.21.4` | 200 OK | 200 OK | **1,984,980** |

- 200 时的 1,984,980 字节与**官方源同一文件逐字节同大小**（第 9 轮已核对），即拿到的是真文件。
- 跳转落点由镜像负载均衡决定（本轮实际出现过 `mirrors.cernet.edu.cn`、`cmcc.mirrors.ustc.edu.cn`、`mirror.nyist.edu.cn`），**三处 UA 策略一致**。
- 判别规律：**无 UA → 403；浏览器式 UA → 403；工具式 UA（curl/Wget/自定义非浏览器 token） → 200**。

### 3.1 追加验证：产品里**已经存在**的两个 UA 字面量都通过（第二轮）

为了避免「随便编一个 UA 能过」的怀疑，第二轮直接拿产品代码里现成的两个 UA 字面量重测：

| `User-Agent`（产品现有字面量） | 出处 | 第 1 次 | 第 2 次 | 字节数 |
| --- | --- | --- | --- | --- |
| `Pineglow-Launcher/1.0` | `ModContentService.cs:39`、`ResourceContentService.cs:26`、`ContentIconService.cs:264` | **200 OK** | **200 OK** | 1,984,980 |
| `PineglowLauncher/1.0.0`（形状同 `MinecraftNewsService.cs:107`、`UpdateService.cs:247`） | `PineglowLauncher/<LauncherVersion>` | **200 OK** | **200 OK** | 1,984,980 |
| `Mozilla/5.0 (Windows NT 10.0; Win64; x64)` | 对照组 | 403 Forbidden | 403 Forbidden | 19,233 |

**即：修复不需要发明新东西——把产品里已有的 UA 约定套到下载客户端上即可，实测 4/4 通过。**

---

## 4. 与产品观察的吻合度

| 观察 | 本轮解释 |
| --- | --- |
| 第 9 轮：产品请求该 URL 得 **403** | 产品 `HttpClient`（`LocalApiService.CreateApiHttp`）**从未设置 `User-Agent`** → 命中「无 UA → 403」 |
| 第 9 轮：同一 URL 用 **curl** 得 302→**200** | curl 自带 `curl/x.y.z` UA → 命中「工具式 UA → 200」 |
| 第 9 轮：.NET 10 客户端跟随跳转得 **403**，curl 得 200 | 同一条规律，不是巧合；本轮把它复现成了 10/10 |

至此第 9 轮的「疑似与 UA 有关」升级为**已证实**。

---

## 5. 影响面（准确的边界）

- **会受影响**：所有经过镜像 302 跳转、落到学术镜像的资源下载（Fabric 库是已确证的一例）。
- **不受影响**：
  - 直连官方源（`libraries.minecraft.net` 等）不走这条跳转链；
  - 本轮镜像下的**整包安装本身是成功的**（第 9 轮：全新空目录装完 1.7.10，130 秒 / 33 库 / 674 资源）——说明绝大多数资源没落在这条 403 路径上。
- **结论**：这是一个**特定路径上的确定性缺陷**，不是「镜像整体不可用」。严重度：中。

---

## 6. 附带确认的环境事实（与产品无关，但影响此前的归因）

同一时间段实测 `piston-meta.mojang.com/mc/game/version_manifest_v2.json`：

| 路径 | 6 次读数 |
| --- | --- |
| 直连 | `000`(5.0s)、`000`(10.6s)、`200`(12.3s)、`200`(20.0s)、`200`(10.2s)、`200`(10.2s) |
| 系统代理 | `000`(5.0s)、`200`(9.1s) |

**`curl.exe` 自己也在间歇性连接失败**（`000`），成功时也要 9–20 秒才拿到一个约 200 KB 的 JSON。
产品 `ApiHttp` 的超时是 **45 秒**，所以第 11 轮那次「7 秒失败」不是超时，而是连接层立刻失败。

**这一条修正了第 7/8 轮的归因口径**：当时「curl / Invoke-WebRequest / .NET 10 全 200，只有产品失败」的观察，不足以支持「产品 HTTP 客户端特有故障」的结论——本机的直连路线本身就在抖动。第 7/8 轮那些失败应标为**环境因素待定**，而不是产品缺陷。
（唯一被本轮确证的产品侧 HTTP 缺陷是上面的 UA 问题，而它是**跳转落点策略**触发的，与「TLS 抖动」是两码事。）

---

## 7. 待你决定

第 6 条产品缺陷（镜像下库下载 403）现在从「方向」升级为「确定结论」，修复是一行：给 `LocalApiService.CreateApiHttp` 造出的 `HttpClient` 设置显式非浏览器 `User-Agent`。
**仍属产品改动，需要你授权**（当前授权范围只有 A+B）。

状态串：`TechnicalAcceptance=NOT_PASSED`（发布整体）；`FormalPhaseApproval=NOT_GRANTED`；`ReleaseApproval=NOT_GRANTED`。
