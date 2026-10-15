# 原版下载真实验收：**PASS**（10 项 0 失败，46 秒，真实 Mojang 源）（第 40 轮）

日期：2026-10-06（Asia/Hong_Kong）。本轮**未改动任何 tracked 文件**。
工具：`verify-vanilla-download.ps1`（本目录，可复跑）。做法：用候选产物自己的
`POST /api/versions/download` 触发**真实安装**，再逐项与 Mojang 官方元数据核对。

## 1. 为什么这一段不需要真机

R06 把"原版下载/修复"记为需要真机。但"下载"这一段**不需要 Java、也不需要游戏**：
它只依赖 `piston-meta.mojang.com`、`resources.download.minecraft.net` 与库仓库——
这些在本机实测都可达。真机剩下的只是"用 Java 把下载好的东西启动起来"。

## 2. 实测：1.5.2（真实安装，46 秒）

```
POST /api/versions/download {"versionId":"1.5.2"}
  → {"ok":true,"id":"dlv…","status":"queued"}
  → downloading 0.01  「正在获取 Minecraft 1.5.2 元数据...」
  → downloading 0.56  「正在下载游戏资源：200/749」
  → downloading 0.60  「正在下载游戏资源：250/749」
  → completed   1.0   「安装完成」        用时 46s
```

| # | 检查 | 结果 |
| --- | --- | --- |
| 1 | 任务终态 `completed` | ✅ |
| 2 | `versions/1.5.2/1.5.2.jar` 存在 | ✅ |
| 3 | **client.jar 大小 = 官方** | ✅ `got=5,564,661 want=5,564,661` |
| 4 | **client.jar SHA1 = 官方** | ✅ `465378c9dc2f…`（官方 `465378c9dc2f779ae1d6e8046ebc46fb53a57968`）|
| 5 | `versions/1.5.2/1.5.2.json` 落盘 | ✅ |
| 6 | `libraries/` 有 jar | ✅ 9 个（官方声明 13 个库；含 native 与 rules 过滤，数量本就不必相等）|
| 7 | `assets/indexes/pre-1.6.json` 落盘 | ✅ |
| 8 | **索引里每个对象都在磁盘上** | ✅ `missing=0 / 468` |
| 9 | **每个对象字节数精确一致（无截断）** | ✅ `truncated=0` |
| 10 | 唯一对象总体积与索引一致（±1%） | ✅ `got=44.01MB want=44.01MB` |

游戏目录总计 **51.7 MB**。⇒ **真实源上下载并校验了一个完整的原版版本**。

## 3. 我自己的一个错误门槛（本轮更正）

第一遍我写的门槛是"assets 体积与官方 `assetIndex.totalSize` 相差 ±5%"，它**FAIL 了**（44MB vs 47.2MB）。
追下去发现**门槛错了，不是产品错了**：官方 `totalSize` 会把**重复条目重复计入**——
`pre-1.6` 索引有 **749 条**记录，但**去重后只有 468 个对象**（44.01 MB）；`totalSize=47.21MB` 是 749 条的和。

我第一遍的 PowerShell 比对还犯了个更隐蔽的错：拿**索引的键**（pre-1.6 索引的键是虚拟路径，
如 `music/calm1.ogg`）去和**磁盘文件名**（对象 hash）比，于是得出"749 个全缺"的假结论。
改用 Python 按 `o["hash"]` 逐对象核对后才得到真相：**0 缺失、0 截断、字节完全一致**。

⇒ 脚本门槛已改成"按 hash 逐对象核对存在性与字节数"（第 40 轮修订），再跑一次得到 **PASS（10 项 0 失败）**。
这是本会话第 N 次同一类教训：**门槛本身错了会静默翻结论**，必须拿独立来源（这里是索引的 hash 与官方 sha1）核对。

## 4. 这条证据把 R06 的"Java/版本"拆开了

| 环节 | 状态 |
| --- | --- |
| 版本清单（917 个真实版本）| ✅ 第 36 轮 |
| **原版下载 + 完整性（client.jar sha1 / assets 逐对象）** | ✅ **本轮真实源 PASS** |
| 库与资源索引落盘 | ✅ 本轮 |
| Java 运行时下载/安装 | ❌ 本机 `installations=[]`，**需要真机**（Adoptium 端点本身可达，第 36 轮已核）|
| 用 Java 启动游戏 / 退出 | ❌ 需要真机（要有 Java + 真实游戏）|
| 承诺的加载器样例（Fabric/Quilt/Forge/NeoForge）| ⚠️ 端点可达（第 33 轮裸探测），安装器路径**未实测**（属可选加固，不建议在冻结期跑）|

## 4.1 两个我自己踩到的取证坑（都记下来）

1. **JSON 膨胀到 7.9MB**：`Get-Content` 返回的对象带 PS 扩展类型元数据，`ConvertTo-Json` 把
   `DefinedTypes`/`DisplayRoot` 之类整个展开。已把脚本里的 `logTail` 显式转 `[string]`，
   并把那份膨胀文件换成 4KB 的精简证据（`vanilla-152-compact.json`）。
2. **门槛用错来源**（见 §3）：拿 `totalSize` 当基准、拿索引的键当文件名 —— 两处都会静默翻结论。

## 5. 复现命令

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File `
  docs\release-remediation-evidence-2026-10-05\run-20261006-0940-vanilla-download\verify-vanilla-download.ps1 `
  -ExePath %TEMP%\pineglow-keep\candidate-5009cf73d0801e83.exe -VersionId 1.5.2 `
  -WorkRoot %TEMP%\pg-vanilla `
  -JsonPath docs\release-remediation-evidence-2026-10-05\run-20261006-0940-vanilla-download\vanilla-152.json
# 期望：原版下载真实验收判定: PASS（10 项 0 失败，约 45 秒）
# 想验现代版本可把 -VersionId 换成 1.21.4（client 28MB / 113 库 / assets 402MB，耗时更长）
```

产物：`vanilla-152-compact.json`（期望值/实测值/10 项断言/判定）、`vanilla-152.log`（原始终端输出）。
