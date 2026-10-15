# 追查 Forge 安装 404（记录型代理）+ 一个已确认的新缺陷：**代理设置在启动时不生效**（第 52 轮）

日期：2026-10-06（Asia/Hong_Kong）。本轮**未改动任何 tracked 文件**。
做法：利用产品**自带的代理设置**（`config.json` 的 `proxyEnabled/proxyHost/proxyPort`）把启动器的请求
导到本机一个**记录型转发代理**（`logging-proxy.cjs`，HTTPS 走 CONNECT 隧道 ⇒ 能看到**主机名**，看不到路径）。

## 1. 结论一（**已确认的产品缺陷，低-中严重度**）：代理设置**启动时不生效**

**行为 A/B 对照**（`proxy-apply-test.ps1`，config.json 预先写好代理）：

| 步骤 | 请求 | 代理日志新增 |
| --- | --- | --- |
| A 启动后**直接**请求 | `/api/versions/remote` | **0 行**（走直连，代理被忽略）|
| B **先 `POST /api/config`**（同样的代理字段）再请求 | `/api/versions/remote` | **1 行**（走代理了）|

**判定**：`确认：代理设置启动时不生效，只有运行时保存配置后才生效`。

**代码根因**：
- `ApiHttp` 是**静态字段**，初始化为 `CreateApiHttp(proxyEnabled: false, …)`（`LocalApiService.cs:41`）；
- `LoadConfig()` 只设置 `MirrorHelper.SelectedKey`，**不刷新代理**（1051–1060）；
- 只有 `SaveConfig()` 才调用 `RefreshApiHttpProxy(config)`（1062–1071）。

**用户可见后果**：在设置里填好代理并保存 → **当次会话生效**；**重启后**（配置里有代理）→
启动器自身的 HTTP 流量（版本/Java/Mod/加载器下载、更新检查）**全部直连**，直到用户再保存一次设置。
修法（未做）：`Start()` 或首次 `LoadConfig()` 之后调用一次 `RefreshApiHttpProxy(config)`。

## 2. 结论二：Forge 安装 404 —— **已排除镜像改写**，但仍**未定因**

用记录型代理（先 `POST /api/config` 确保代理生效）重跑同一次 Forge 包导入（`optimized-fps` 1.20.1，`forge=47.4.3`）：

- 代理日志显示启动器**确实连的是官方主机**：`CONNECT maven.minecraftforge.net:443`（**没有** `bangbang93`/镜像主机）
  ⇒ **"被 MirrorHelper 改写导致 404" 这个假设被推翻**；
- 同一 URL 在本机实测：PowerShell（走系统代理）**200 / 6,068,826 B**；.NET 10 **直连**（`UseProxy=false`）**200**；
  6 种 User-Agent 全 **200**；`.sha1` 也 **200**；
- 但**经本机转发代理**（隧道直通真实主机）后，导入**仍然失败**：
  `Forge installer 下载失败：下载失败 HTTP 404：forge-1.20.1-47.4.3-installer.jar`（用时 316 秒）。

⇒ **可确定**：URL 主机对、官方地址可用、无镜像改写、UA 无关；**不可确定**：启动器在那条隧道里请求的**具体路径**（CONNECT 看不到），
因此 404 究竟来自真实 CDN 还是本机 fake-IP/透明代理链，**本轮无法判定**。我的倾向是**环境因素**（同一 URL 从本机其它客户端都是 200），
但**没有证据**，所以不写成结论。**Forge/NeoForge 的"安装"这一步在本机仍然走不通**（因此无法用实测确认它们的启动表现——
不过"启动"那一半已由第 51 轮的 **artifact 级证据**判定为同病）。

**下一步可选的定因手段**（都未做）：① 给启动器加一行日志打印最终 URL（属改源码，需授权）；
② 用 MITM 代理看路径（要装本机 CA、改信任库——**本会话一直拒绝动本机信任库**）；
③ 换一台网络环境不同的机器复跑（最干净）。

## 3. 顺带记录

- 记录型代理本身工作正常（HTTP 转发 + CONNECT 隧道），日志格式：`<ISO 时间>  CONNECT <host>:<port>` / `HTTP <method> <url>`；
- 本轮也再次印证了产品**自带代理设置确实作用于 `ApiHttp`**（B 组生效），只是**时机**不对（见 §1）。

## 4. 复现

```powershell
# ① 代理是否启动即生效
powershell -File ...\run-20261006-1430-forge-404-trace\proxy-apply-test.ps1 `
  -ExePath <EXE> -ProxyScript ...\logging-proxy.cjs -WorkRoot %TEMP%\pg-proxyapply -JsonPath <证据>\proxy-apply.json
# 期望：deltaBeforeConfigSave=0、deltaAfterConfigSave>0
# ② Forge 安装走代理后仍 404（主机正确）
powershell -File ...\trace-forge-404.ps1 -ExePath <EXE> -MrpackPath <optimized-fps.mrpack> `
  -ProxyScript ...\logging-proxy.cjs -WorkRoot %TEMP%\pg-trace404b -JsonPath <证据>\forge-404-trace.json
```

产物：`proxy-apply.json` + `proxy-apply-step.log` + `proxy-apply-proxy.log`（A/B 对照原始记录）、
`forge-404-trace.json` + `proxy.log`/`proxy-lines.txt`（主机分布与原始行）、`logging-proxy.cjs`（可复用工具）。
