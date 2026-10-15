# API 协议（LocalApiService）

> 反映 2026-08-16 状态，从 [LocalApiService.cs](file:///d:/松光启动器/dev/PineglowLauncher/Pineglow.Launcher.Services/LocalApiService.cs) 路由代码逐条核实。
> 协议约定：错误响应体含 `error` 或 `message` 字段；业务错误返回 400，未捕获异常返回 500。

## 一、基础信息

- 基址：`http://127.0.0.1:48123`（端口可用 `PINEGLOW_API_PORT` 覆盖）。
- CORS：`*` 全放开，`Cache-Control: no-store`。
- 方法：GET / POST / DELETE / OPTIONS（OPTIONS 预检返回 204）。
- 前端封装：`ui/js/api.js` 的 `_req`（GET）/ `_post`（POST），`live` 走 HTTP、`_mock` 走内存。

## 二、完整端点表

### 系统
| 端点 | 方法 | 用途 |
|---|---|---|
| `/api/health` | GET | 存活检查（前提：`GetContextAsync` 成功即返回） |
| `/api/state` | GET | 全局状态（version/pid/config/instances/accounts/java）；config 含 `aiProviders[]`（id/name/baseUrl/model/keyConfigured，无 key 明文）+ `selectedAiProviderId` |
| `/api/config` | POST | 写 config.json（全量落盘）；`aiProviders` 整体替换并按 id 合并 SecretStore 密钥，缺失 id 视为删除，`selectedAiProviderId` 选定当前诊断提供商 |
| `/api/java` | GET | Java 检测（installations 列表） |
| `/api/stats` | GET | 游玩统计 |
| `/api/logs` | POST | 打开日志目录（explorer/xdg-open） |

### 实例（`/api/instances`）

> **2026-10-03 修正（run `20261002-182304Z`）**：本节原先有 **10 条路由永不可达**。原因是 `LocalApiService.HandleApiAsync` 把要求 `seg.Length == 5/6/8` 的守卫嵌进了要求 `seg.Length == 4` 的通用实例块内，条件恒假、直接落到末尾兜底 404。受影响的是 `mods/{file}/{toggle,delete}`、`bisect/{start,test,giveup}`、`saves/{folder}/{backup,delete,open,backups}`、`saves/{folder}/backups/{name}/restore`。现已修复并补上 HTTP 层契约测试（`dev/PineglowLauncher.Tests/RouteReachabilityTests.cs`）。**本次改动未启动真实 GUI/游戏，相关端到端行为仍为「未验证」。**

| 端点 | 方法 | 用途 |
|---|---|---|
| `/api/instances` | GET / POST | 实例列表 / 新建 |
| `/api/instances/{id}` | GET | 实例详情 |
| `/api/instances/import` | POST | 拖拽/选文件导入实例包 |
| `/api/instances/register` | POST | 把已存在的游戏目录挂回实例列表（MR03）。只新增配置记录，**不创建/移动/删除任何文件**；目录不存在或已在列表中返回 400 |
| `/api/instances/{id}/delete` | POST | 从列表移除实例（MR03）。**只删配置记录，不删游戏目录、存档与 Mod**；返回 `keptFiles/gameDirectory/savesCount/sharedDirectory`，可用 `register` 挂回。正在进行 Mod 二分定位时返回 400 |
| `/api/instances/{id}/launch` | POST | 启动游戏 |
| `/api/instances/{id}/diagnose` | POST | AI 崩溃诊断（规则引擎 + AI 增强，AI 失败/未配置返回 200 + `ai:null` 降级；`configured` 基于选中 provider 的 BaseUrl 非空，Ollama 免 Key；成功时返回 `suspectMods`（AI 从已安装 Mod 列表识别的嫌疑文件，前端可一键禁用）） |
| `/api/instances/{id}/diagnose/history` | GET | **MR08**：诊断历史列表（只读本地）。返回 `limit/retentionDays/overLimitCount/olderThanRetentionCount/notice` 与逐条摘要；**超量/超期只提示、不自动删除**，避免静默丢掉待恢复证据 |
| `/api/instances/{id}/diagnose/export` | GET | **MR08**：导出报告。查询参数 `reportId`、`format=md\|json`、`includeEvidence=0\|1`。导出物已脱敏（token/API Key/authorization/session/地址/用户路径/provider URL），保留类名与 Mod 名；证据摘录默认不含，需 `includeEvidence=1` 且诊断时开启 `DiagnosticKeepEvidence` 才有内容 |
| `/api/instances/{id}/prelaunch-check` | GET | 启动前环境检查 |
| `/api/instances/{id}/mods` | GET | Mod 列表 |
| `/api/instances/{id}/mods/...` | POST | Mod 启停/删除/替换（写盘）。`.../delete` 自 MR04 起是**可恢复删除**：jar 与 `.mcmini.json` 侧车移入恢复区并返回 `operationId`，不再物理删除；隔离失败会返回 400 而不是假装成功 |
| `/api/instances/{id}/mods/{file}/replace` | POST | **事务化替换/更新（MR05）**：body `{ newFileBase64, expectedSha256? }`。新包先落 staging 并核对 SHA256，通过后才隔离旧包 → 写新包 → 写元数据；任一步失败回滚到旧版本并返回 400（`hash_mismatch`/`commit_failed`/`metadata_failed`/`replace_in_progress` 等）。旧文件是禁用态时新版本保持禁用；成功返回 `operationId` 作为撤销句柄 |
| `/api/instances/{id}/recovery` | GET | 恢复区列表（MR04）：逐条给出 `operationId/status/createdAt/expiresAt/totalBytes` 与每个文件的 `relativePath/state/sha256`；只读，不触碰游戏文件 |
| `/api/instances/{id}/recovery/{operationId}/restore` | POST | 把一次隔离操作的文件恢复到原位置（MR04）。默认**不覆盖**同名现有文件（报 `conflict`）；重复恢复幂等（`already_restored`）。**MR05 撤销**：body 传 `{"overwrite":true}` 才会覆盖回旧版本（"撤销本次更新"） |
| `/api/instances/{id}/bisect/start` | POST | 开始 Mod 二分定位：取启用 Mod 建会话，禁用半区写盘，返回 `step/candidates/disabledHalf/waitingForReport`。**MR06**：任一文件禁用失败时返回 **409**（不再报"本轮已就绪"），body 带 `needsRecovery/recoveryError/files[]`；此时不接受测试反馈 |
| `/api/instances/{id}/bisect/test` | POST | 上报本轮测试结果 `{crashed:bool, step?}`：未结束则继续禁用下一半区，结束返回 `culprit` 并恢复全部 Mod。**MR06**：`step` 与当前轮次不符（双击/重复提交）返回 409 且不推进；`needsRecovery` 期间返回 409 且不收窄候选 |
| `/api/instances/{id}/bisect/giveup` | POST | 放弃定位：恢复启用时全部 Mod，结束会话。**MR06**：**只有恢复逐项核对通过才移除会话**；恢复失败返回 409 并保留会话（`needsRecovery=true`），修好磁盘后可重试 |
| `/api/instances/{id}/bisect` | GET | **MR07**：查询是否有未完成的二分定位会话（只读）。返回 `pending/sessionId/status/step/rounds/conflicts[]/canRestore/canContinue`；`conflicts` 是磁盘与落盘记录的逐项差异，不靠文件名猜 |
| `/api/instances/{id}/bisect/resume` | POST | **MR07**：处理未完成会话。body `{"action":"restore"}` 只把本会话开始时启用的文件恢复为启用态（不启用用户原本禁用的 Mod、不删除后加入的文件）；`{"action":"continue"}` 仅在磁盘与记录逐项一致时按记录重建会话。有冲突时返回 409 并列出 `conflicts[]` |
| `/api/instances/{id}/resourcepacks` | GET | 资源包列表 |
| `/api/instances/{id}/shaderpacks` | GET | 光影列表 |
| `/api/instances/{id}/saves` | GET / POST | 存档列表 / 操作 |
| `/api/instances/{id}/saves/{folder}/backup` | POST | 创建存档备份 |
| `/api/instances/{id}/saves/{folder}/backups` | GET | 备份列表 |
| `/api/instances/{id}/saves/{folder}/backups/{name}/restore` | POST | 恢复备份 |
| `/api/instances/{id}/saves/{folder}/delete` | POST | 删除存档（**不是** `DELETE`） |
| `/api/instances/{id}/saves/{folder}/open` | POST | 打开存档目录（explorer/xdg-open） |

### 账号（`/api/accounts`）
| 端点 | 方法 | 用途 |
|---|---|---|
| `/api/accounts` | GET | 账号列表 + capes |
| `/api/accounts/offline` | POST | 添加离线账号 |
| `/api/accounts/select` | POST | 切换账号 |
| `/api/accounts/microsoft` | POST | 微软账号设备码登录（兼容旧调用，创建设备码会话） |
| `/api/accounts/microsoft/device` | POST | 微软设备码登录：创建会话，返回 userCode/verificationUri |
| `/api/accounts/microsoft/poll` | POST | 轮询设备码登录结果（pending/completed/failed） |
| `/api/accounts/authlib` | POST | Authlib 外置登录 |
| `/api/accounts/skin` | POST | 上传皮肤（pngBase64 + slim） |

### 版本（`/api/versions`）
| 端点 | 方法 | 用途 |
|---|---|---|
| `/api/versions` | GET | 版本列表 |
| `/api/versions/remote` | GET | 远程版本清单 |
| `/api/versions/download` | POST | 触发版本下载 |

### 下载任务
| 端点 | 方法 | 用途 |
|---|---|---|
| `/api/downloads` | GET | 版本下载任务状态（轮询） |

### 内容（Mod/整合包/资源）
| 端点 | 方法 | 用途 |
|---|---|---|
| `/api/content/search?q&category` | GET | 内容搜索（Modrinth/CF） |
| `/api/content/tasks` | GET | 内容安装任务状态（轮询） |
| `/api/content/install` | POST | 入队内容安装（Mod/整合包/光影/资源） |

### 更新（`/api/update`）
| 端点 | 方法 | 用途 |
|---|---|---|
| `/api/update` | GET | 更新状态 |
| `/api/update/check` | GET | 检查更新（updateUrl 读 config.json） |
| `/api/update/download` | POST | 下载更新包 |
| `/api/update/apply` | POST | 应用更新（重启生效） |

### 下载源（`/api/sources`）
| 端点 | 方法 | 用途 |
|---|---|---|
| `/api/sources` | GET | 下载源列表 + 测速 |
| `/api/sources/select` | POST | 切换下载源 |

### 服务器（`/api/servers`）
| 端点 | 方法 | 用途 |
|---|---|---|
| `/api/servers` | GET / POST | 收藏服务器列表 / 新增 |
| `/api/servers/{id}/ping` | POST | 服务器状态 Ping |
| `/api/servers/{id}/delete` | POST | 删除服务器 |

### 存档（`/api/worlds`）
| 端点 | 方法 | 用途 |
|---|---|---|
| `/api/worlds` | GET | 世界/存档列表 |

## 三、路由实现逻辑（手工路由）

`HandleApiAsync` 基于路径分割手工匹配，**不使用 ASP.NET/框架**：

```
seg = path.Trim('/').Split('/')   // ["api", "instances", "{id}", "launch", ...]
```

- 每个端点一个 `if` 条件，匹配 `seg.Length` + `seg[i].Equals(...)` + `method`。
- 命中即调用对应 `HandleXxxAsync` 并 `return`。
- 未命中落到末尾 `404`。
- 异常：`InvalidOperationException` → 400（业务错误）；其他 → 500。

**新增接口的接入点**：在 `HandleApiAsync` 的对应 `seg` 区段追加 `if`，并新增一个 `HandleXxxAsync` 方法。

## 四、关键坑点

1. **camelCase JSON**：后端反序列化请求体时 `System.Text.Json` 需 `PropertyNameCaseInsensitive=true`；前端统一发 camelCase。
2. **下载/内容安装是异步任务**：`/api/content/install`、`/api/versions/download` 只负责**入队并立即返回**，真正执行在后台任务，进度靠轮询 `/api/content/tasks`、`/api/downloads`。**不要阻塞请求体等待完成**。
3. **配置全量落盘**：`/api/config` POST 是覆盖式写 `config.json`，前端 patch 需先读 `/api/state` 再合并提交。
4. **错误语义**：400 表示业务校验失败（如"实例不存在"），前端按 `error`/`message` 字段提示即可。

## 五、前端调用约定

- `api.js` `live` 分支每个方法对应一个端点；`_mock` 分支返回同形状的伪造数据。
- 新增端点时，前端 `live` + `_mock` 都要补，否则 `file://` 降级会报错。
- 静态资源（HTML/CSS/JS）由同一 `HttpListener` 托管，路径穿越已防护（`Path.GetFullPath` 校验前缀）。
