# 整合包导入「取消」实测：**取消被接受，但半成品与临时目录都没清掉**（第 58 轮）

日期：2026-10-06（Asia/Hong_Kong）。本轮**未改动任何 tracked 文件**。
被测：真实 Modrinth 包 `Fabulously.Optimized-v3.15.0.mrpack`（1,245,888 B）；`operationId` 由客户端生成（与 UI 一致）。

## 1. 两个阶段的结果

| 阶段 | 动作 | 结果 |
| --- | --- | --- |
| **A 导入中取消** | 发起导入 → 35 秒后 `POST /api/instances/import/cancel {operationId}` | 取消**被接受**：`{"ok":true,"cancelled":true,"pendingCancel":false,"message":"cancelled"}` |
| **B 取消先到** | 先取消（无在途操作）→ 再用同一 id 发起导入 | 先取消返回 `{"cancelled":false,"pendingCancel":true,"message":"recorded_for_later"}`；随后导入**0 秒**返回 **409** `{"ok":false,"cancelled":true,"error":"导入已取消，未生成实例。"}` ✅ **完全正确** |

## 2. A 阶段暴露的三个问题（都有原始证据）

| # | 观察 | 证据 |
| --- | --- | --- |
| 1 | **取消后工作仍在继续**：取消时目录 **51.6 MB** → 3 分钟后 **312.6 MB** → 再后 **364.7 MB**（HTTP 调用早已结束，仍在涨）| `step.log` 时间线；目录大小三次测量 |
| 2 | **半成品目录没被清理**：残留 `instances/Fabulously Optimized 导入`，**364.7 MB / 2,805 个文件**，含 `assets`/`libraries`/`versions/1.18.2`、`mods` **0 个**、**7 个 `.download` 残片** | 目录清单与大小 |
| 3 | **临时上传目录也没清**：`%TEMP%\pineglow-import-ac849e51…`（1,216.7 KB，本次导入创建）仍在 | 临时目录清单 |
| 4 | **客户端拿到的是"连接被关闭"而不是文档里的 409**：导入调用在 **224 秒**后以 `status=0 / 基础连接已经关闭: 接收时发生错误` 结束 | `import-A-result.json` |
| 5 | **实例没有进配置**（这点是对的）：取消前后 `/api/state` 实例数都是 1 | `step.log` |

## 3. 代码对照（为什么"应该清"却"没清"）

- `InstancePackService.ImportModrinthPackAsync` 的 `catch` 里有清理：
  ```csharp
  catch {
      if (instance != null && Directory.Exists(instance.GameDirectory)) {
          try { Directory.Delete(instance.GameDirectory, recursive: true); }
          catch (Exception cleanupError) { _log("导入失败后未能删除半成品目录（需要用户手动清理）：…"); }
      }
      throw;
  }
  ```
- 导入处理器有 `catch (OperationCanceledException) → WriteJsonAsync(409, …)` 与
  `finally { PackOps.Cancel(…); if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true); }`。

**但实测两处清理都没生效**，且 **API 导入路径的 `_log` 是空委托**（`new InstancePackService(ApiHttp, defaultGameDir, _ => { })`）
⇒ 连"未能删除半成品目录（需要用户手动清理）"这句话**都无处可去**：用户看不到任何解释，只能自己发现磁盘上多了几百 MB。

**未能区分的一点（诚实边界）**：究竟是"删除动作跑了但被占用中的 `.download` 文件挡住而失败"，
还是"异常/取消路径根本没走到那两段清理"，本轮**没有插桩、无法区分**。可以确定的是**结果**：
半成品与临时目录都留下了，且**没有任何日志或界面提示**。

## 4. 影响与建议

- **用户可见**：点了"取消导入"之后，磁盘上会留下**几百 MB**的孤儿实例目录（且不在实例列表里，用户不知道它属于谁、能不能删）。
- **建议修法**（未做，属产品决定）：
  1. 清理失败时**必须让用户看见**（把 `_log` 接到真正的日志/界面提示，而不是空委托）；
  2. 清理前先关掉/释放 `.download` 句柄，或改用"先重命名到回收区、下次启动再清"的稳妥策略；
  3. 取消点加密：原版安装那一段（本次 51.6 → 364.7 MB 全在它里面）应当在**每个文件之间**检查取消；
  4. 取消要**返回确定的 409**（现在客户端看到的是连接被关闭，无法区分"取消"与"网络坏了"）。
- **严重度**：中（不阻断发布，但会让用户困惑并留下垃圾；且取消的语义与文档/界面提示不一致）。

## 5. 复现

```powershell
# 主脚本（含 A/B 两阶段）；包用任一真实 .mrpack
powershell -File ...\run-20261006-1730-import-cancel\verify-import-cancel.ps1 `
  -ExePath <候选 EXE> -MrpackPath <Fabulously.Optimized-v3.15.0.mrpack> `
  -HelperScript ...\import-helper.ps1 -JsonPath <证据>\import-cancel.json -WorkRoot %TEMP%\pg-impcancel3 -CancelAfterSec 35
```

产物：`import-cancel.json`（逐项判定）、`import-A-result.json` / `import-B-result.json`（两次导入调用的原始返回）、
`step.log`（含"取消时已落盘 51.6 MB"与"取消后残留 312.6 MB"的时间线）、`import-cancel.log`。

## 6. 本轮我自己的三个脚本坑（都记下来）

1. **`/api/state` 里没有 `gameDirectory`** —— 我按属性取，`Set-StrictMode` 直接中断脚本（本会话第 5 次同类）。
2. **`config.json` 是懒写的** —— 启动后立刻读会 `FileNotFoundException`（产品还没落盘）。
3. 上面两处都改成"先判断存在、再取值、最后给默认值"。
