# R06 外部服务的真实端点盘点：多条"服务腿"实际已通，两条图片代理未判定（第 36 轮）

日期：2026-10-06（Asia/Hong_Kong）。本轮**未改动任何 tracked 文件**。
方法：启动**已验证的候选产物**（`5009cf73d0801e83…`），用它的会话令牌调用它自己的**只读**接口，
打真实上游；完整响应体落盘在 `bodies/`。工具：`probe-r06-services.ps1`（本目录，可复跑）。

## 1. 起因：我上一阶段把"外部服务不可用"当成了挡箭牌

前几轮我把 R06 里"新闻/AI/皮肤 真外链"归为"本机假 IP 环境限制"。本轮先做了**裸探测**（不经应用）：
`piston-meta.mojang.com`、`launchercontent.mojang.com`、`meta.fabricmc.net`、`meta.quiltmc.org`、
`api.adoptium.net`、`api.curseforge.com`、`login.microsoftonline.com`、`gitee.com`、`github.com`
**全部可达**（200/405）。⇒ 外部服务不是阻断点，**该验的就得验**。

## 2. 通过候选产物实测的结果（14 项探测）

| 端点 | 状态 | 耗时 | 真实内容（要点）|
| --- | --- | --- | --- |
| `/api/health` | 200 | 11ms | `ok,pid,port` |
| `/api/state` | 200 | 405ms | `version,pid,config,instances,accounts,java` |
| `/api/stats` | 200 | 14ms | 统计字段齐全 |
| `/api/instances` | 200 | 13ms | `instances,selectedInstanceId` |
| `/api/accounts` | 200 | 11ms | `accounts`（冷启动 1 个离线占位）|
| **`/api/versions/remote`** | **200** | **2,368ms** | **917 个真实版本**：`26.4-snapshot-2`（2026-09-29）、`26.3`（2026-09-15）… |
| **`/api/news`** | **200** | 17ms~9s | **12 条真实新闻**：source=`launchercontent.mojang.com`、`isStale=false`、`error=""`；条目带真实外链（如 `minecraft.net/article/planet-earth-iii-perspectives`）且 `linkOk=true` |
| `/api/java` | 200 | 244ms | `installations=[]`（本机没装 Java ⇒ **Java 安装/启动仍只能真机做**）|
| **`/api/sources`** | **200** | 925ms | 3 个源带**真实延迟**：official（piston-meta）724ms 可用、BMCLAPI 189ms 可用、**MCBBS 镜像不可用** |
| **`/api/content/search?source=modrinth`** | **200** | 2,560ms | **24 条真实结果**（Sodium 等，含 downloads/loaders 真实字段）|
| **`/api/content/search?source=curseforge`** | **200** | 4,355ms | **24 条真实结果** ⇒ **内置 CurseForge Key 可用** |
| `/api/content/icon`（真实 Modrinth id）| **504** | 5,046ms | 见 §3（**未判定**）|
| `/api/news-image`（真实 id）| **400** | 9ms | 见 §3（**未判定**）|
| `/api/accounts/{不存在}/skin-image` | 404 | 2ms | **路由需要账号**；无账号时 404 是正常响应（我早前用 `/skin-image` 探测是**我路径写错**，不是产品失败）|

## 3. 两条图片代理：**已追查**（第 38 轮更正 —— 一条解释清楚、一条推翻了我的猜测）

> **第 38 轮更正**：本节原写"两条都未判定（环境可疑）"。追查后（`run-20261006-0910-image-proxy-rootcause/`）：
> **news-image 的 400 属设计行为**（fake-IP 被安全检查拒绝，`NewsFakeIpCompat` 默认 fail closed）；
> **content/icon 的 504 根因仍未判定，但已排除两种解释**（不是 SSRF 拒绝——那会是 502 且 `IsPublicAddress` 允许 198.18/15；
> 也不是"上游不可用"——同一 IP 直连 1.6 秒成功）。⇒ 图标那条是**真机必须确认的疑点**。

代码里的映射（`LocalApiService.cs:4539-4548`）：`unknown_image` → **404**；
`policy_rejected / dns_private / dns_mixed / dns_failed / mime_rejected / …` → **400**；`timeout` → **504**。

- **news-image 400（9ms）**：既然 `unknown_image` 是 404，400 说明命中**策略/DNS 类**分支。
  本机所有目标域名都解析到 **fake-IP `198.18.0.x`**（实测：`cdn.modrinth.com`→198.18.0.33、
  `piston-meta.mojang.com`→198.18.0.34 …），而 R-02 的 fake-IP 兼容开关**默认 fail closed**。
  ⇒ 最可能是 `dns_private`（**环境产物**）。**但我没取到 `{"error":"<kind>"}` 正文**（诊断脚本在后台卡住、
  已终止），所以记为**未判定**，真机复核。
- **content/icon 504（5,046ms）**：5,046ms 正好是 `ContentIconService.ConnectTimeoutSeconds = 5`
  ⇒ 应用侧**连接超时**。同一 URL（`cdn.modrinth.com/data/AANobbMI/icon.png`）我用 PowerShell 直接取 **200 / 22,884 B / image/png**，
  说明"上游本身可达"⇒ 差异出在本机这套 fake-IP + 透明代理与应用自带连接策略之间，**无法在不改网络配置的前提下判定**。
  记为**未判定**；界面上它表现为"该图未加载"，不影响页面其它内容（这正是设计上的降级行为）。

> 会话纪律：这两条**不能**当成"产品通过"，也**不能**当成"产品缺陷"——只能说"在本机环境下未判定"。
> 谁要下结论，必须在**真实网络环境**（非 fake-IP）复跑同一脚本。

## 4. 由此产生的两条文档更正

1. **决策项 1（`/api/versions/remote` 的超时契约）前提要收窄**：该端点**正常工作时是好的**
   （917 个版本，2.4 秒）。之前我把它描述得像"常态返回 500 + 界面上写暂无可下载版本"。
   准确说法：**上游不可达/超时才退化**，问题是"退化时只有 500 与一句提示、没有类型化失败与重试"。
   是否修仍由你定，但**紧迫度下降了**。
2. **R06 的"服务腿"应当从 NOT_RUN 里分出来**：版本列表、新闻（含真实外链与 `linkOk`）、源延迟与可用性、
   Modrinth/CurseForge 搜索（含内置 Key）**都已用真实上游验过**。
   NOT_RUN 剩下的才是真正需要真机的部分：真实账号登录、Java 安装与游戏启动/退出、
   整合包导入、Mod/存档替换与回滚、以及"下载后替换自己并重启"。

## 5. 本轮**没有**证明的

1. 真实账号（设备码完成、取消、超时、切换、续期）——需要真实微软账号；
2. Java 下载→安装→启动游戏——本机 `installations=[]`；
3. 整合包导入、Mod/存档替换与回滚、实例备份恢复；
4. `UpdateService.ApplyUpdate` 的"替换自己并重启"；
5. 新闻图片/内容图标的真实渲染（§3 未判定）；
6. 皮肤渲染（需要账号）。

## 6. 复现命令

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File `
  docs\release-remediation-evidence-2026-10-05\run-20261006-0830-r06-services\probe-r06-services.ps1 `
  -ExePath %TEMP%\pineglow-keep\candidate-5009cf73d0801e83.exe `
  -WorkRoot %TEMP%\pg-r06-probe `
  -JsonPath docs\release-remediation-evidence-2026-10-05\run-20261006-0830-r06-services\r06-services.json
# 期望：200-OK=11 有响应=14（图片两条见 §3 的环境说明）
```

产物：`r06-services.json`（14 项探测的摘要与预览）、`r06-services.log`、
`bodies/`（含 `versions_remote.json` 92 KB、`content_search_modrinth.json`、`content_search_curseforge.json`、
`news.json`、`sources.json` 等**完整响应体**）。
