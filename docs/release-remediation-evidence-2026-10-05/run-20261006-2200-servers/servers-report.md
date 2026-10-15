# 服务器列表 + 延迟探测（ping）= **PASS**；另附一次"配置是否静默不落盘"的静态审计（第 67 轮）

日期：2026-10-06（Asia/Hong_Kong）。本轮**未改动任何 tracked 文件**。

## 1. 服务器功能实测（此前完全没覆盖）

| 步骤 | 结果 |
| --- | --- |
| 初始 `GET /api/servers` | `{"servers":[]}` ✅ |
| `POST /api/servers {name:"PublicProbe", host:"mc.hypixel.net", port:"25565"}` | `{"ok":true,"server":{…,"port":25565,"statusText":"尚未检测",…}}` ✅（端口字符串正确解析成整数）|
| `POST /api/servers/{id}/ping`（**真实公共服务器**）| **`"statusText":"可连接 · 2278 ms"`**，真实 MOTD（`Hypixel Network [1.8/26.3] … SKYBLOCK 0.27.1 | HALLOWEEN EVENT`）、版本名、在线人数 —— **2 秒返回** ✅ |
| 添加 `127.0.0.1:1` 后 ping（**未监听端口**）| **`"statusText":"无法连接"`**，2 秒内干净失败（不挂起）✅ |
| **重启启动器后** `GET /api/servers` | 两台都还在（连缓存的 `statusText` 一起）✅ **持久化正常** |
| `POST /api/servers/{id}/delete` | `{"ok":true}`，列表 2 → 1 ✅ |
| 对**已删除**的 id 再 ping | **400 `服务器不存在：eff3abb0…`** ✅ |

**判定：`PASS：添加/列出/ping（含失败路径）/重启持久化/删除 都符合预期`**
（脚本 `verify-servers.ps1`，逐项 7/7；JSON 里留了原始响应。）

> 附带信息：ping 走的是**真实 Minecraft 状态协议**（`ServerPingService.QueryAsync`，6 秒超时），
> 本机网络环境下对公网服务器**确实能拿到 MOTD/版本/人数** —— 这也顺带说明"网络被完全挡住"的担忧不成立。

## 2. 静态审计：配置有没有"改了但不落盘"

起因：第 65/66 轮那条 blocker（默认实例不落盘）属于"改了内存、没写盘"这一类，所以顺手对**所有配置写入点**做了一次机械审计：

- 在 `LocalApiService.cs` 里找出 **27 处 `config.X = …` 赋值**，按**所在方法**检查有没有 `SaveConfig(`。
- 结果：**21 处"所在方法内没有 SaveConfig"** —— 全部集中在 `PatchConfig`（18 处）与 `PatchAiProviders`（3 处）；
  而它们的调用者 `HandleUpdateConfigAsync`（L4790）是 **`PatchConfig(config, root); SaveConfig(config);`** 两步走（`PatchAiProviders` 由 `PatchConfig` 内部调用）
  ⇒ **没有任何"改了却不落盘"的配置写入点** ✅（`SaveConfig` 全文件共 20 个调用点）。
- 顺带澄清第 63 轮那条观察：`POST /api/config` 忽略 `downloadSource` **不是**持久化问题，而是 `PatchConfig` **没有覆盖这个字段**
  （改下载源的正式入口是 `POST /api/sources/select`）。

## 3. 复现

```powershell
powershell -File ...\run-20261006-2200-servers\verify-servers.ps1 `
  -ExePath <候选 EXE> -JsonPath <证据>\servers.json -WorkRoot %TEMP%\pg-servers
# 期望：PASS（7/7）；JSON 里含真实服务器的 MOTD/延迟与死端口的"无法连接"
```

产物：`servers.json`（逐步原始响应）、`servers.log`（时间线）。
