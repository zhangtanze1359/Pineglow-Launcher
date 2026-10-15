# 运行态守卫实测：R06「running 只读」验证 + 一个真实发现（第 46 轮）

日期：2026-10-06（Asia/Hong_Kong）。本轮**未改动任何 tracked 文件**。
工具：`verify-running-guards.ps1`（本目录）+ `probe-error-body.ps1`（顺带查清错误消息通道）。
做法：真的把游戏跑起来（1.18.2 + 真实 Java 17 + 离线账号），在**游戏运行期间**逐个打带守卫的端点，
再用"退出后再试一次"作对照。

## 1. 实测：运行中被挡住的操作（都是 400）

| 探测 | 结果 |
| --- | --- |
| **重复启动** `POST /api/instances/{id}/launch`（第二次）| ✅ **400 / 18ms**（`RunningInstanceRegistry.TryBeginLaunch` 原子预约挡住）|
| **删除 Mod** `POST …/mods/GuardMod.jar/delete` | ✅ **400 / 4ms**，且**被拒后文件仍在** |
| **列出存档** `GET …/saves` | ✅ **400 / 7ms** |
| **列出存档备份** `GET …/saves/GuardWorld/backups` | ✅ **400 / 3ms** |

## 2. 实测：退出后守卫解除（对照）

杀掉 JVM（pid=4652）后：**5 秒内** `GET …/saves` 恢复 200；`POST …/mods/GuardMod.jar/delete` → **200**，
返回 `{"ok":true,"recoverable":true,"status":"quarantined","operationId":"20261005-215615-f0f4dd31"}`。
⇒ **守卫是"跟着运行态"的，不是永久锁死**，这比"挡住了"更重要。

## 3. 一个真实发现（低严重度）：**运行中仍可创建存档备份**

`POST …/saves/GuardWorld/backup`（游戏正在跑）→ **200 / 11ms**，真的写出了 zip。
代码核对：`HandleCreateSaveBackupAsync`（`LocalApiService.cs:2374-2382`）**没有任何运行态守卫**——
而它的兄弟操作（列出存档、列出备份、恢复备份、打开存档目录、删/停用 Mod、诊断、bisect…共 13 处）
都带 `RunningInstanceRegistry.IsRunning` 守卫。
⇒ 后果：对**正在写盘**的存档做快照，可能得到不一致的备份（区域文件/会话锁处于中间状态）。
建议（可选加固）：给创建备份加同一个守卫，或明确"运行中也允许备份"并接受不一致风险。
**未改代码**（属产品决定；且改 `LocalApiService.cs` 会改产物字节 ⇒ 需重跑全量验证链）。

## 4. 顺带查清：错误消息**确实送到客户端**了（是我读错了通道）

我原先看到 `status=400` 时响应体为空，一度怀疑"守卫消息到不了界面"。用 4 个必然 400 的调用同时读两个通道后：

| 调用 | 状态 | `ErrorDetails.Message` |
| --- | --- | --- |
| 恢复不存在的备份 | 400 | `{"error":"备份不存在：__nope__"}` |
| 删除不存在的实例 | 400 | `{"error":"实例不存在：__no_such_instance__"}` |
| 创建实例缺 name | 400 | `{"error":"实例名称不能为空"}` |
| 删除不存在的 Mod | 400 | `{"error":"Mod 不存在：__nope__.jar"}` |

**响应流读到空、`$_.ErrorDetails.Message` 才是正文**——这是 PowerShell 5.1 的固有行为（`Invoke-RestMethod`
抛异常时流已被消费）。⇒ **产品侧无误**，是我读取通道错了；这条也解释了前几轮里"400 但正文为空"的假象。

## 5. 本轮我自己犯的三个测试侧错误（都已修/已纠正）

1. **读错错误通道**（上节），导致两条"应被拒且带特定文案"的断言**假 FAIL**（状态码 400 其实是对的）。
2. **删除实例的路由写错**：真实路由是 `POST /api/instances/{id}/delete`（`LocalApiService.cs:591-597`），
   我用了 `DELETE /api/instances/{id}` → **404**，那条探测**不成立**（不是"被守卫拒绝"）。
3. **脚本解析错误**：`("…=$([int](…)s)")` 里 `s` 紧跟子表达式 → PowerShell 报 `Unexpected token 's'`，
   **整脚本一行都没跑**。已加"解析自检"（`Parser::ParseFile`）再运行——这个习惯值得保留：
   语法错误会让"什么都没做"看起来像"跑了但没输出"。

## 6. 诚实边界

- 守卫的判定依据是 **API 层**响应；界面是否把这些 400 文案展示给用户，本轮**没有**在真实 UI 上核对
  （前面几轮的九页 UI 验收覆盖的是页面渲染，不是这条错误提示路径）。
- 本轮测的是 **Vanilla 1.18.2 + 离线账号**；加载器实例、多实例并发、`running` 状态下的其他端点（资源包/光影/bisect）
  未逐一实测——代码里它们带同样的守卫，但"读了代码"不等于"跑过了"。

## 7. 复现命令

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File `
  docs\release-remediation-evidence-2026-10-05\run-20261006-1200-running-guards\verify-running-guards.ps1 `
  -ExePath %TEMP%\pineglow-keep\candidate-5009cf73d0801e83.exe -WorkRoot %TEMP%\pg-guard `
  -JsonPath docs\release-remediation-evidence-2026-10-05\run-20261006-1200-running-guards\running-guards.json
# 约 5 分钟（含 240 秒版本安装）：期望"运行中四个端点 400、退出后恢复 200"，
# 并把"运行中创建备份=200（允许）"作为观察项记录
powershell -NoProfile -ExecutionPolicy Bypass -File `
  docs\release-remediation-evidence-2026-10-05\run-20261006-1200-running-guards\probe-error-body.ps1 `
  -ExePath %TEMP%\pineglow-keep\candidate-5009cf73d0801e83.exe -JsonPath <证据>\error-body.json
```

产物：`running-guards.json`（11 项断言 + 6 条原始观察）、`step.log`、`error-body.json`（4 个 400 的正文取证）。
