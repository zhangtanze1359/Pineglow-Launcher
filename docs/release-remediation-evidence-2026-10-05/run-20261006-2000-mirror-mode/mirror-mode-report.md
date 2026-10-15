# 镜像源模式实测：**PASS**（装得成、产物与官方逐字一致、流量确实走镜像）（第 63 轮）

日期：2026-10-06（Asia/Hong_Kong）。本轮**未改动任何 tracked 文件**。
背景：设置里给用户的"下载源"选项（官方 / BMCLAPI）本会话**从未端到端验过**（只验过 `/api/sources` 列表）。

## 1. 正确做法（我第一版用错了端点）

| 端点 | 结果 |
| --- | --- |
| `POST /api/config {"downloadSource":"bmclapi"}` | 返回 `{"ok":true}`，但 `GET /api/sources` **仍是 `selected=official`** ⇒ **该端点根本不处理这个字段**（源码里只有 `HandleSelectSourceAsync` 会写 `config.DownloadSource`）|
| `POST /api/sources/select {"key":"bmclapi"}` | `{"ok":true,"selected":"bmclapi","useMirror":true}`，`GET /api/sources` → `selected=bmclapi` ✅ |

**顺带记一条观察（低严重度）**：`POST /api/config` 对**未知字段**静默返回成功（我用 `{"downloadSource":"official"}` 复测，`selected` 仍为 `bmclapi`）。
⇒ 客户端如果误用该端点改下载源，会**以为改成功了**。界面走的是正确端点，所以只影响"自己写脚本/第三方集成"的人。

## 2. 镜像模式下的真实安装（1.5.2）

| 项 | 值 |
| --- | --- |
| 安装终态 | **`completed`**，用时 **126 秒**（官方源对照：48 秒——镜像更慢但可用）|
| `client.jar` | 5,564,661 B，**sha1 = `465378c9dc2f779ae1d6e8046ebc46fb53a57968`，与官方逐字一致** ✅ |
| assets | **468 个对象**（与官方源那轮完全一致）✅ |
| 流量 | **确实走了镜像**：代理日志里 15 个主机，其中镜像池 `bmclapi2.bangbang93.com`(8)、`bmclapi2.sakura.sld.tw`(4)、`bmclapi5/6.sakura.sld.tw`(9)、`bmclapi.qytzz.cn`(4)、`mirrors.cernet.edu.cn`(6)、`download.mcbbs.net`(2)、`oba*.hejuqingci.qingcilove.top`(8)、`open-bmcl-api.lunar.cc`(2)、`unnys3.sakura.sld.tw`(5) |

**判定：`PASS：镜像源模式下安装成功、jar 与官方 sha1 一致、且流量确实走了镜像主机`**

**观察（不是缺陷）**：镜像模式下仍有少量官方主机请求（`libraries.minecraft.net` 6 次、`piston-meta.mojang.com` 1 次、`launchercontent.mojang.com` 1 次）
⇒ BMCLAPI 只镜像部分路径，库文件仍可能回官方源；这与"镜像=全量替代"的直觉不同，**发布说明里不要写成"完全不走官方"**。

## 3. 我自己的两个坑

1. **用错端点**（把 `downloadSource` 塞进 `/api/config`）⇒ 第一版结论是"选了镜像却仍是官方"，差一步就误报产品缺陷。
   教训：**改设置类操作，先读源码确认是哪个路由写的**（我这次是事后 grep `config.DownloadSource = ` 才定位到 `/api/sources/select`）。
2. **PowerShell 5.1 解析坑**：`Log ('判定: ' + $(if (...) { '…' } else { "…" + (...) + '）"' }))` 这种嵌套写法被解析器判为"字符串缺少终止符"，
   整脚本无法运行（错误定位在后续行，具有误导性）。改成**先算变量、再 Log** 即可。v1 脚本已删除，v2（错端点）/v3（正确）都留档。

## 4. 复现

```powershell
# 正确版（v3）：选源 → 确认 → 装 1.5.2 → 核对 sha1/assets/代理主机
powershell -File ...\run-20261006-2000-mirror-mode\verify-mirror-mode-v3.ps1 `
  -ExePath <候选 EXE> -ProxyScript ...\run-20261006-1430-forge-404-trace\logging-proxy.cjs `
  -JsonPath <证据>\mirror-mode.json -WorkRoot %TEMP%\pg-mirror4
```

产物：`mirror-mode.json`（判定/产物/主机分布）、`step3.log`（v3 时间线）、`proxy3.log`（代理原始行）、
`verify-mirror-mode-v2.ps1`（用错端点那版，留作教训）、`mirror-mode.log`（v2 输出）。
