# 文件层「替换 / 回滚 / 备份 / 恢复」真实验收：**PASS（20 项 0 失败）**（第 44 轮）

日期：2026-10-06（Asia/Hong_Kong）。本轮**未改动任何 tracked 文件**。
工具：`verify-file-transactions-v2.ps1`（本目录，可复跑；v1 是卡死的那版，保留作对照）。
做法：用候选产物自己的 API 对**真实文件**做备份/恢复/隔离删除/事务替换，并逐项做**字节级**核对与反证。

## 1. 三组行为与结果

### A 存档：备份 → 改动 → 恢复（非破坏式）

| 步骤 | 实测 |
| --- | --- |
| 存档被识别 | `GET /api/worlds` 列出 `TestWorld`（4.02 KB）|
| 创建备份 | `POST /api/instances/{id}/saves/TestWorld/backup` → `{"ok":true,"path":"…\\backups\\TestWorld\\TestWorld-20261006-053100.zip"}` |
| 备份列表 | `GET …/backups` → `fileName=TestWorld-20261006-053100.zip`（596 B）|
| 制造改动 | 改写 `level.dat`、删除 `region/r.0.0.mca`、新增 `extra.tmp` |
| 恢复 | `POST …/backups/{name}/restore` → 200，**落盘路径是 `saves\TestWorld (1)`** |
| **恢复出的内容 = 备份前** | ✅ `level.dat` 与 `region` 的 SHA256 与备份前**逐字节一致** |
| **非破坏式** | ✅ 正在使用的 `TestWorld` **没有被覆盖**（live `level.dat` 仍是改动后的字节）|
| 反证 | ✅ 恢复不存在的备份 → **400** |

### B Mod：可恢复删除 → 恢复

| 步骤 | 实测 |
| --- | --- |
| 删除 | `POST …/mods/AcceptanceMod.jar/delete` → `{"ok":true,"recoverable":true,"status":"quarantined","operationId":"20261005-213149-8d93cd34","files":[{"relativePath":"mods\\AcceptanceMod.jar","state":"quarantined","sha256":"FB51…"}]}` |
| 原位置 | ✅ `mods/AcceptanceMod.jar` 确实不在了 |
| 恢复 | `POST …/recovery/{opId}/restore` → `{"ok":true,"status":"restored","overwrite":false,"conflicts":0}` |
| **恢复后字节一致** | ✅ SHA256 与删除前一致 |

### C Mod：事务化替换 + **回滚反证**

| 步骤 | 实测 |
| --- | --- |
| 反例：新包字节与 `expectedSha256` **不匹配** | ✅ **400 被拒绝**（模拟传输损坏/截断）|
| **原文件未被破坏** | ✅ 拒绝后 `ReplaceMe.jar` 的 SHA256 与替换前一致 |
| **没有半成品** | ✅ mods 目录里没有留下那个新包 |
| 正例：哈希匹配的新包 | ✅ 200 `{"status":"replaced","oldSha256":"E9BE…"}`，落盘字节 = 新包 |

⇒ 一并验证了 R06「Mod/存档：替换与回滚、实例级备份恢复」的**文件层**行为。

## 2. 我自己的两个**错误预期**（都已更正，才得到正确的 PASS）

1. **以为"恢复存档"是原地覆盖**。实际 `SaveBackupService.RestoreBackup` 走
   `SaveService.ImportSave`，是**非破坏式导入**：目标已存在时写成 `TestWorld (1)`，**绝不覆盖正在用的存档**。
   这是**数据安全的正确设计**——错的是我的断言。（证据：handler 注释里还记着"第 63 轮修复跨世界恢复"那条缺陷。）
2. **以为替换会校验"是不是合法 jar/zip"**。实际校验是 **上传字节的 SHA256 与客户端声明是否一致**（完整性），
   不是文件格式白名单。所以"塞一个假 jar"会被接受（用户自己选的文件），而**哈希不匹配**才会被拒。
   改成"哈希不匹配"作反例后，回滚行为被正面证明。

## 3. 三个操作层教训（v1 → v2 的过程）

| 现象 | 根因 | 处理 |
| --- | --- | --- |
| v1 卡死且**看不到卡在哪** | stdout 只在进程结束后才落盘；`Set-StrictMode` 下空管道的 `Measure-Object` 没有 `Sum` 属性 → 属性访问直接中断脚本 | v2 改为**每步立即追加日志**（`step.log`）+ 每个请求 30 秒超时 |
| 备份名取不到（属性不存在） | 真实字段是 **`fileName`**，我按 `name` 取；StrictMode 下又直接中断 | 用 `PSObject.Properties` 逐个判定，不再想当然 |
| 中文实例名变成 `??????` | PS 5.1 把字符串 body 按本地代码页编码 | 请求体显式用 **UTF-8 字节**；验收用 ASCII 名 |

> 另注：v2 首次运行时脚本中断，但**启动器还在跑**——我又一次用"事后核对"的方式
> （从 `http://127.0.0.1:<port>/` 的 meta 标签现取会话令牌）直接在活的实例上查清了真实响应字段，
> 这是对上一轮那次抢救的可复用做法。

## 4. 诚实边界

- 这些是**文件层**行为；"在游戏里看到效果"（例如换存档后进世界、换 mod 后启动）仍需**真机**。
- 本轮**没有**验证：`running` 状态下的只读保护（需要真的跑着游戏）、Mod 二分定位（Bisect）的完整轮次、
  导入取消的清理（早前轮次有干跑与自动化测试覆盖）。
- 也没验证 CurseForge/MCM 格式（包格式问题，与文件事务无关）。

## 5. 复现命令

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File `
  docs\release-remediation-evidence-2026-10-05\run-20261006-1100-file-transactions\verify-file-transactions-v2.ps1 `
  -ExePath %TEMP%\pineglow-keep\candidate-5009cf73d0801e83.exe -WorkRoot %TEMP%\pg-filetx3 `
  -JsonPath docs\release-remediation-evidence-2026-10-05\run-20261006-1100-file-transactions\file-transactions.json
# 期望：文件层事务/恢复验收判定: PASS（20 项 0 失败，约 2 秒）
```

产物：`file-transactions.json`（20 项断言）、`step.log`（逐步时间线，含每次请求耗时）、
`file-transactions.log`（stdout 全量）。
