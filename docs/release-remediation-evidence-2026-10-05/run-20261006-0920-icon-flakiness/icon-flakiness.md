# 图标端点复查：**端点本身是好的**，之前的 504 是偶发（6 次里 2 次）（第 39 轮）

日期：2026-10-06（Asia/Hong_Kong）。本轮**未改动任何 tracked 文件**。
背景：第 36 轮首次探测到 `/api/content/icon` **504**，第 38 轮我追查后写成"根因未判定、真机必须确认的疑点"。
本轮重复实验推翻了那个"疑点"定性——**端点可用，504 是偶发**。

## 1. 六次实测的完整记录（同一候选产物、每次全新冷启动）

| 次序 | 时间点 | `/api/content/icon`（Modrinth AANobbMI）| `/api/news-image`（真实 id）|
| --- | --- | --- | --- |
| 1 | 第 36 轮首次 | **504 / 5,046ms** | 400 / 9ms |
| 2 | 第 36 轮第二次（v2 脚本）| **504 / 5,046ms** | 400 / 7ms |
| 3 | 第 39 轮（保留日志那次）| **200 / 1,387ms / image/webp / 4,800 B** | 400 / 7ms |
| 4 | 第 39 轮 复跑 1 | **200 / 1,317ms / 4,800 B** | 400 / 8ms |
| 5 | 第 39 轮 复跑 2 | **200 / 1,830ms / 4,800 B** | 400 / 8ms |
| 6 | 第 39 轮 复跑 3 | **200 / 1,402ms / 4,800 B** | 400 / 8ms |

⇒ **图标：6 次中 2 次 504、4 次 200**，且 2 次 504 都恰好是 **5,046ms**（= `ContentIconService.ConnectTimeoutSeconds = 5`）
出现在**最早两次**，此后 4 次连续成功（1.3–1.8 秒）。
⇒ **新闻图片：6 次全是 400**，稳定、快（7–9ms），符合"fake-IP 被安全检查拒绝、默认 fail closed"的设计。

## 2. 为什么可以断定"不是产品缺陷"

1. **传输层复刻成功**：我按产品代码原样复刻了它的 transport（`UseProxy=false` + `ConnectCallback` 绑定预先解析的 IP +
   `ConnectTimeout=5s`），对**同一个 URL** 取图 **200 / 22,884 B / 1,571ms**（另一条对照路 1,610ms）；
   对**上游真实图标 URL**（Modrinth 实际返回的是 `.webp`：
   `https://cdn.modrinth.com/data/AANobbMI/295862f4724dc3f78df3447ad6072b2dcd3ef0c9_96.webp`）也是
   **200 / image/webp / 4,800 B / 1,430ms**，字节头 `RIFF…WEBP` 符合它的 WebP 校验。
2. **应用自己现在也稳定成功**：4 次连续 200，返回的正是 `image/webp` 4,800 B（与上游一致）。
3. **已排除的三种解释**：不是"IP 被 SSRF 拒绝"（那会返回 **502**，且 `IsPublicAddress` 允许 198.18/15）；
   不是"上游不可用"（多种方式 200）；不是"传输实现错了"（复刻即成功）。

## 3. 剩下的事实性结论（要如实说，不要粉饰）

- 504 那两次的耗时**精确落在 5 秒连接预算上** ⇒ 当时是**建立连接超预算**（本机 fake-IP + 本地代理
  `127.0.0.1:7890` 的组合下偶发偏慢），**不是**应用逻辑错误；
- 但仍暴露一个**加固点**：图标取图是"**连接超时 5 秒、且不重试**"，
  所以**短暂网络抖动 = 用户看到图标缺失**（页面其它内容不受影响）。
  正常网络下 1.4–1.6 秒即可完成，5 秒预算平时足够；是否放宽超时或加一次重试，属**产品决定**（未改）。
- 真机检查项据此**降级**为"观察项"：真机上若**经常**缺图标，再按 §2 的复刻实验定位。

## 4. 文档更正

- `run-20261006-0910-image-proxy-rootcause/`（第 38 轮）结论里"必须在真机确认的疑点"→ **已由本文解决**；
- 复评 §3 补充段、手册 §6 的真机检查项、进展报告第 38 条 同步改为本文口径；
- 新增一条可选产品决定：**图标连接超时是否从 5s 放宽 / 是否加一次重试**（代价：改 `ContentIconService.cs`
  会改变产物字节 ⇒ 需重跑全量验证链）。

## 5. 复现命令

```powershell
# 连续跑 N 次（每次全新冷启动），统计图标与新闻图片的返回
$ev = 'docs\release-remediation-evidence-2026-10-05\run-20261006-0830-r06-services'
1..3 | ForEach-Object {
  $wr = Join-Path $env:TEMP "pg-icon-f$_"; New-Item -ItemType Directory -Force -Path $wr | Out-Null
  powershell -NoProfile -ExecutionPolicy Bypass -File "$ev\probe-r06-services.ps1" `
    -ExePath %TEMP%\pineglow-keep\candidate-5009cf73d0801e83.exe -WorkRoot $wr -JsonPath "$wr\r.json" | Out-Null
  (Get-Content "$wr\r.json" -Raw | ConvertFrom-Json).probes |
    Where-Object { $_.label -in 'content_icon_modrinth','news_image' } |
    ForEach-Object { "$($_.label) $($_.status) $($_.elapsedMs)ms $($_.bytes)B" }
}
# 期望（本机）：图标多为 200/1.3-1.8s/4800B（偶发 504/5s）；新闻图片稳定 400/7-9ms（设计内）
```
