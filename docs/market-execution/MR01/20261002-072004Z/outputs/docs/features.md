# 功能矩阵与能力清单

> **事实基线：2026-10-02（MR01 刷新）。** 本文取代旧《技术对比文档》的功能盘点（其逆向结论已过时），
> 也取代 2026-08-16 版功能矩阵的结论。
>
> **证据版本**：输入为 `D:\松光启动器` 工作区在 HEAD `f145d14eb26b0b7459a28491e764ac8c573085d3`
> 之上的未提交状态（35 个已修改文件 + 1251 个未跟踪文件中的 53 项显式白名单），
> 经 `scripts/verify-source-snapshot.ps1` 组装的隔离快照。
> 完整逐文件 SHA 见 `docs/market-execution/MR00/execution-root.json` 与同 run-id 的 `input-manifest.json`。
>
> **本轮实测数字（唯一可引用的当前结果）**：Node 前端正式测试 **184 通过 / 0 失败 / 0 跳过**（11 个 `.test.cjs`）；
> C# Release 全套 **612 通过 / 0 失败 / 0 跳过**。历史报告中的旧测试数不作为当前结论。

## 〇、状态词定义（本文强制使用）

| 状态 | 含义 |
|---|---|
| **代码存在** | 能在源码中定位到实现位置；不等于可用 |
| **自动验证** | 本轮或既有测试**实际执行过**并通过，且覆盖该层级 |
| **真实场景验证** | 在真实 GUI / 真实游戏 / 真实文件上跑通并留有日志 |
| **未验证** | 本轮没有执行，或缺少证据 |
| **不支持** | 明确没有实现 |

> ⚠️ **一个绿色勾不能覆盖所有层级。** 旧版用 `✅` 同时表示"代码写了"和"验证过"，
> 这正是本轮要消除的歧义来源。

---

## 一、🔴 本轮新发现的高优先级缺陷（先于功能清单）

### 1.1 Bisect 与 Mod 操作的 HTTP 路由是不可达死代码

**证据（静态 + 真实运行双重确认）**

- `dev/PineglowLauncher/Pineglow.Launcher.Services/LocalApiService.cs:415`
  的外层分支门控为 `if (seg.Length == 4 && seg[1].Equals("instances", ...))`（**精确等于 4**）。
- 同一 `if` 块**内部**又出现两个要求更长路径的分支：
  - `:511` `if (seg.Length == 5 && action.Equals("bisect", ...))` → `start` / `test` / `giveup`
  - `:531` `if (seg.Length == 6 && action.Equals("mods", ...))` → `toggle` / `delete`
- 这两个条件要求 `seg.Length` 为 5 / 6，却被包在要求等于 4 的块内，**恒为 false**。
- 大括号深度可复算：`:415` 深度 3，`:511` 深度 4，`:531` 深度 5。
- 落到 `:789` 的兜底 `404 未找到接口`。

**真实运行确认**：在隔离沙盒内执行 `VerifyHost.exe --bisect-smoke --port 48130`，
`POST /api/instances/{id}/bisect/start` 返回 **404**，冒烟自检 `exit=1`。
（该次调用前 `CreateInstance` 与实例详情查询均已成功、沙盒内 8 个假 Mod 已写入，
因此失败点确定在第一次 bisect 调用，而非更早的步骤。）

**旁证：同一类 bug 此前已被修过一次**。`:408-409` 的注释写明
"Phase 3B v3 修复批次：cancel 路由必须先于通用 diagnose。精确 seg.Length 匹配"，
即 `diagnose/cancel` 正是因为同样的嵌套问题被**提到块外**才恢复可达（其深度为 3，在 `:415` 之前）。
bisect 与 mods 两组路由没有做同样的提升。

**影响**：UI 侧 `ui/js/api.js`、`ui/js/launcher.js` 已按调研所述接线并有 i18n 文案，
但这些调用打到的是 404。**Mod 二分定位与 Mod 启停/删除在端到端层面当前不可用。**

**为什么 796 个绿色测试没发现**：
- `dev/PineglowLauncher.Tests/ModBisectServiceTests.cs` 的 12 个 `[Fact]` 只 `using Pineglow.Launcher.Services`，
  直接测 `ModBisectService` 服务层，**不经过 HttpListener 路由**。
- 全仓 **没有任何 C# 测试**引用 `/bisect/` 或 `mods/*/toggle|delete` 路由字符串。
- `ui/tests/api-contract.test.cjs` 也未对这些路由做断言。
- 结论：现有测试**在结构上无法**发现"路由不可达"这一类缺陷。

**归属**：`MR06`（Bisect 操作失败必须阻止错误推进）应先修路由可达性；
Mod 启停/删除路由同源问题归入 `MR04`。本批**只记录，不修**（不在 MR00/MR01 授权范围）。

### 1.2 调研"Bisect 已实现且可靠"需下调

2026-10-02 调研称 Bisect 为"三项目中被文档记录得最准确的断言"，依据是
12 个单测 + API 三端点齐备 + UI 三处接线。**服务层与 UI 层属实，路由层不属实**，
因此该结论**不能按"端到端已验证"引用**。这与调研自身第六节"schema 完整度 > 实现完整度"的判断一致：
本例中 schema（路由声明、UI 接线、i18n）完整，落地（可达性）缺失。

---

## 二、前端 9 视图真实状态

| 视图 | 代码存在 | 自动验证 | 真实场景验证 | 说明 |
|---|---|---|---|---|
| home | 是 | 是 | 未验证 | 启动/停止真实链路，静态新闻 |
| installations | 是 | 部分 | 未验证 | 实例管理 + Mod 弹窗 + AI 诊断弹窗 + **Bisect 弹窗（UI 已接线但路由 404，见 §1.1）** |
| worlds | 是 | 是 | 未验证 | 读存档列表 + 备份/恢复/删除 |
| servers | 是 | 是 | 未验证 | 增删/ping 真实 |
| content | 是 | 是 | 未验证 | 搜索真实 + 安装写盘（入队式） |
| skins | 是 | 是 | 未验证 | 预览真实，上传皮肤已可用 |
| downloads | 是 | 是 | 未验证 | 远程版本 + 下载任务轮询 |
| news | 是 | 是 | 未验证 | 独立新闻页 |
| settings | 是 | 是 | 未验证 | 内存/Java/离线账号/日志/更新/微软登录/代理/下载源/AI 提供商管理 |

> 注：旧架构说明中"worlds 仅 toast""content 安装仅 toast""skins 上传未实现"等**已过时**，8/10 已全部写盘接通。
> **本轮不启动真实 GUI**，故"真实场景验证"一列统一为未验证。

---

## 三、能力全景（按模块）

### 账号体系
| 能力 | 状态 | 证据 |
|---|---|---|
| 离线账号（离线 UUID 生成） | 代码存在 + 自动验证 | `AccountService.cs` |
| 微软账号设备码登录 + Refresh Token 续期 | 代码存在 + 自动验证 | `MicrosoftAuthService.cs` / `MicrosoftBrowserAuthorizationService.cs` |
| Authlib 外置登录（Yggdrasil） | 代码存在 + 自动验证 | `AuthlibInjectorService.cs` |
| 多账号并存与切换 | 代码存在 | `HandleAddOfflineAccountAsync` / `HandleSelectAccountAsync` |
| 皮肤上传（pngBase64 + slim）、披风查看 | 代码存在 + 自动验证 | `HandleUploadSkinAsync` / `HandleAccountSkinImageAsync` |

### 游戏与版本管理
| 能力 | 状态 | 备注 |
|---|---|---|
| 官方版本清单（piston-meta + 镜像回退） | 代码存在 + 自动验证 | `MinecraftDownloader` / `MirrorHelper` |
| 版本安装/校验（SHA1）/修复/删除 | 代码存在 + 自动验证 | |
| 下载队列与后台任务、断点续传 | 代码存在 + 自动验证 | |
| 库文件/原生库/assets 并行下载 | 代码存在 | |

### 加载器与整合包
| 能力 | 状态 | 证据 / 修正 |
|---|---|---|
| **现有加载器共 5 种**：Fabric / Forge / NeoForge / Quilt / OptiFine | 代码存在 + 自动验证 | `Models/ModLoaderKind.cs`、`ModLoaderInfo.SupportedKinds = new ModLoaderKind[5]` |
| Cleanroom / Legacy Fabric | **不支持** | 二者**不在** `ModLoaderKind` 枚举中，是**尚未实施的候选项**，不是"部分支持"；`MR14` / `MR15` / `MR16` |
| LiteLoader | **不支持** | 同上，属候选项，未在枚举中 |
| Quilt 维护等级存疑 | 风险项 | 生态取向待定，`MR14` |
| **Modrinth `.mrpack` 导入** | 代码存在 + 自动验证 | **修正**：`InstancePackService.ImportModrinthPackAsync`（`:274`）与 `DetectModpackFormat`（`ModpackArchiveFormat.Modrinth`）**已实现**；`LocalApiService.cs:1091-1094` 与 `:3626` 已接线。**不是"从零缺失"** |
| CurseForge 整合包导入 | 代码存在 | `ImportCurseForgePackAsync` |
| CurseForge Key | 代码存在 | `ModContentService.cs:21-26` 注释：内置 Key 由 CurseForge 官方 2026-07-15 审批签发给本产品，可在设置页用个人 Key 覆盖。**本批不作新的法律定性**；剩余工作只是分发开关/配额/失败反馈与不暴露 Key（`MR12`） |
| 实例导出 | 代码存在 | 导出为 `.mcm` 自定义格式，**不与** Modrinth `.mrpack` / Prism `.prisminstance` 互通（导入与导出是两件事） |
| 升级前自动备份 | 代码存在 | `ModpackLifecycleService` |

> **修正说明**：调研把"实例导出不互通 .mrpack"与"没有 .mrpack 导入"混为一谈。
> 事实是：**导入已实现（.mrpack），导出仍是 .mcm**。互通性与失败恢复验证属 `MR11`。

### 实例系统
| 能力 | 状态 | 证据 / 修正 |
|---|---|---|
| 新建/复制/重命名、实例级 Java 指定 | 代码存在 + 自动验证 | |
| 实例隔离目录 | 代码存在 + 自动验证 | `InstanceIsolationService` |
| **"删除"实例** | **代码存在（语义需修正）** | **修正**：`HandleDeleteInstanceAsync`（`LocalApiService.cs:1045-1057`）**只做 `config.Instances.RemoveAll(...)` + 修正 `SelectedInstanceId` + `SaveConfig`，全程没有任何文件操作，不删除游戏目录、不删存档**。UI 术语与存档提示属 `MR03` |

### Mod 与内容管理
| 能力 | 状态 | 证据 / 修正 |
|---|---|---|
| 本地 Mod 启停/删除 | 路由不可达 | **§1.1**：`/api/instances/{id}/mods/{file}/{toggle,delete}` 恒 404。服务层方法存在 |
| **Mod 删除** | 代码存在（**不可恢复**） | **修正**：`ModService.DeleteMod`（`ModService.cs:97-113`）直接 `File.Delete(mod.FullPath)`（`:107`）与 `File.Delete(metadataPath)`（`:111`），**无隔离、无回收站、无撤销**。属可恢复删除缺口，`MR04` 本批不实现 |
| 在线搜索（Modrinth + CurseForge） | 代码存在 + 自动验证 | `ModContentService` |
| 依赖解析与自动安装 | 代码存在 | |
| Mod 更新检查 | 代码存在 | |
| Mod 健康检查 | 代码存在 | `ModHealthService` |
| **Mod 二分定位（Bisect）** | 服务层自动验证；**端到端不可用** | **修正**：12 个 `[Fact]` 覆盖 `ModBisectService` 状态机（`ModBisectServiceTests.cs`）；API 三端点声明存在但**路由不可达（§1.1）**；UI 已接线。**不能称"结束自动恢复已验证"** |
| Bisect 失败处理 | **存在缺口** | **修正**：`LocalApiService.cs:1931-1934`（禁用）与 `:1952-1955`（恢复）**均只 `LauncherLog.Error(...)` 后继续**，不阻止错误推进、不反馈给用户；恢复上下文可能在完成路径被移除。属 `MR06` / `MR07` |
| 资源包/光影包/存档管理 | 代码存在 | `ResourcePackService` / `ShaderPackService` / `SaveService` |
| 服务器收藏 + 状态 Ping | 代码存在 + 自动验证 | `ServerPingService` |

### Java 与启动
| 能力 | 状态 |
|---|---|
| Java 自动检测（常见路径/环境变量/注册表） | 代码存在 + 自动验证（`JavaLocator`） |
| Java 自动下载安装（SHA256 校验、版本匹配、按实例分配） | 代码存在 + 自动验证（`JavaRuntimeService` / `JavaVersionMatcher`） |
| 内存自动推荐 | 代码存在 + 自动验证（`MemoryAllocationService`） |
| JVM/游戏参数、窗口分辨率、直接加入服务器 | 代码存在 |
| 导出启动脚本（仅离线账号，防令牌泄露） | 代码存在 |
| 游戏日志实时显示/过滤/崩溃分析/诊断包导出 | 代码存在 + 自动验证（`CrashAnalyzerService` / `DiagnosticSnapshotService`） |
| 启动前环境检查 | 代码存在（`HandlePrelaunchCheckAsync`） |
| 启动后最小化、退出后自动关闭 | 代码存在 |

### 存档 / 皮肤 / 新闻
| 能力 | 状态 | 备注 |
|---|---|---|
| 存档列表 / 备份 / 恢复 / 删除 / 打开目录 | 代码存在 + 自动验证 | `SaveService` / `SaveBackupService` |
| 皮肤预览 / 上传 / 代理 | 代码存在 + 自动验证 | `SkinImageProxyService` |
| 新闻列表 / 链接白名单 / 图片代理 | 代码存在 + 自动验证 | 测试覆盖较密（`NewsR2TeredoNat64Tests` 41 项、`NewsImageProxyServiceTests`、`NewsLinkHandlerTests`） |
| 内容图标服务 | 代码存在 + 自动验证 | `ContentIconService`（130 项 `[Fact]`） |

### AI 崩溃诊断
| 能力 | 状态 | 备注 |
|---|---|---|
| 规则型崩溃分析 | 代码存在 + 自动验证 | `CrashAnalyzerService`（6 项 `[Fact]`） |
| AI 诊断（OpenAI 兼容、多提供商预设、Key 走 `SecretStore` 不入盘） | 代码存在 + 自动验证 | `AiDiagnoseService` / `AiEndpointPolicy` / `AiOperationRegistry` / `SecretStore`；测试含取消与重定向（24 项）与发布安全（33 项） |
| **相对规则引擎的增量价值** | **未验证** | 需在冻结案例集上对比"正确建议/实际修复/错误确信/隐私/延迟/成本"，`MR09` |
| 诊断历史、脱敏与导出 | 部分 | `DiagnosticSnapshotService` 存在；历史管理与脱敏策略待做，`MR08` |

> **对标修正**：不应写成"AI 崩溃诊断无竞品"。HMCL 已有基于规则的启动失败分析
> （`CrashReportAnalyzer` + 并行日志分析 + 崩溃信息包导出），**真正要对标的是规则型诊断**。

### 工程与体验
| 能力 | 状态 | 备注 |
|---|---|---|
| 日志落盘（保留 7 天） | 代码存在 | `%AppData%\MCMiniLauncher\logs\launcher-yyyyMMdd.log` |
| **自更新框架** | 代码存在 + 自动验证 | `UpdateService.cs`：`FetchManifestAsync` / `DownloadUpdateAsync` / `ApplyUpdate`，且已校验 SHA256（`:61-63` 拒绝无效 SHA256）、要求 HTTPS（`:65`）。**产物名硬编码 `-win-x64-single-file.exe`（`:71`），即当前仅支持 Windows x64 更新链路**。签名、回滚与失效恢复属 `MR20`，**本轮未验证** |
| 多下载源（official/bmclapi/mcbbs + 测速 + 切换） | 代码存在 | |
| HTTP 代理、皮肤站预设 | 代码存在 | |
| 游玩统计（`PlayCount` / `TotalPlaySeconds`） | 代码存在 | |
| 拖拽导入整合包 | 代码存在 | |
| i18n 双语 | 代码存在 | `ui/js/i18n.js` 语言区块 `zh` / `en` |
| 深色模式 | 代码存在 | |
| 内容源失效可解释性 | **未验证** | 属 `MR12` |

### 平台
| 项 | 状态 | 修正 |
|---|---|---|
| **宿主技术栈** | Avalonia / .NET 10 + Web UI（HTML/CSS/JS + WebView2） | **修正**：**不是 Tauri**。全仓 `dev/`、`ui/`、根 `*.md` 对 `tauri` 的检索**零命中**。因此也**不能**照搬 Tauri updater（如 `tauri_plugin_updater`）的方案 |
| Windows x64 | 代码存在 + 自动验证 | 本轮单文件发布成功（见 §四） |
| Linux x64 / macOS x64 | **未验证** | CI `build.yml` 矩阵含 `ubuntu-latest/linux-x64`、`macos-latest/osx-x64`，但**本轮未推送、未创建 PR，CI 从未实际执行**。且 `Build VerifyHost` 与 `Run bisect smoke` 步骤标注 `win-x64 only`。**三平台能出产物 ≠ 正式跨平台支持**；真实运行验证属 `MR24` / `MR25` |

---

## 四、本轮可复验的工程事实

| 项 | 结果 |
|---|---|
| 隔离执行快照 | 214 文件；tracked 工作区版本 161 + 显式白名单 53；额外/泄漏文件 **0**；`bin`/`obj` **0**；逐文件 SHA 与源**全部一致** |
| Node 前端正式测试 | **184 通过 / 0 失败 / 0 跳过**（11 个 `.test.cjs`，`node v24.19.0`） |
| 非 RID 锁定还原 | launcher 与 tests 均 `--locked-mode` **exit 0** |
| 主程序 Release 构建 | **exit 0** |
| C# Release 全套测试 | **612 通过 / 0 失败 / 0 跳过** |
| VerifyHost 构建 | **exit 0** |
| win-x64 单文件发布 | **exit 0**；`.../net10.0/win-x64/publish/Pineglow.Launcher.exe` = 45,644,552 字节，SHA256 `3B71DCC1DB72A2BC79945F611492BC366402CD455D7DA307BC06BEF33CDA7001` |
| **RID 发布锁定** | **KNOWN-UNLOCKED（已知边界，非通过）**：`dotnet publish -r win-x64 -p:RestoreLockedMode=true` 必然 **NU1004**（锁定文件只覆盖 `net10.0` 且缺 `Microsoft.NET.ILLink.Tasks`）。**不得传 `dotnet publish --locked-mode`**（它是 `dotnet restore` 专属开关）。锁定策略属 `MR19` |
| 真实 GUI / 真实游戏 / 真实 AI | **本批未验证**（明确不在 MR00/MR01 范围） |
| CI 实际运行 | **未验证**（本轮不推送、不建 PR） |

> ⚠️ 上述数字之外，**任何"测试通过"都不代表真实场景通过**。健康接口 200、mock 全绿、
> DOM 断言成功，都只证明各自覆盖的那一层。

---

## 五、工程加分项

- 断点续传 + 可恢复任务（重启后恢复未完成任务）✅
- 凭据安全存储（Windows 凭据管理器，非 Windows 降级 `secrets.json`）✅
- 诊断包导出 ✅
- 命名空间统一 `Pineglow.Launcher.*` ✅
- 版本号单一来源（程序集 = `LauncherService.LauncherVersion`）✅
- 单文件自包含发布（xUnit + Node 双测试体系）✅

## 六、遗留事项（未完成）

| 级别 | 事项 | 归属 |
|---|---|---|
| 🔴 P0 | **Bisect 与 Mod 启停/删除 HTTP 路由不可达（死代码）** | `MR06` / `MR04` |
| 🔴 P0 | **Bisect 禁用/恢复异常只写日志，不阻止错误推进** | `MR06` / `MR07` |
| 🔴 P0 | **Mod 删除为不可恢复 `File.Delete`** | `MR04` |
| 🟠 P1 | 实例"删除"实际只移除配置记录，语义与存档提示需明确 | `MR03` |
| 🟠 P1 | AI 诊断相对规则引擎的增量价值未证明 | `MR09` |
| 🟠 P1 | 自更新签名 / 回滚 / 失效恢复未验证，且仅 win-x64 链路 | `MR20` |
| 🟠 P1 | 内容源（CurseForge/Modrinth）失效的可解释性与恢复 | `MR12` |
| 🟡 P2 | `.mrpack` 互通性与失败恢复验证（导入已实现） | `MR11` |
| 🟡 P2 | Cleanroom / Legacy Fabric / LiteLoader 未实现（先核需求） | `MR14` / `MR15` / `MR16` |
| 🟡 P2 | 旧启动器实例迁移 | `MR13` |
| 🟡 P2 | 键盘/焦点/缩放无障碍 | `MR17` |
| 🟡 P2 | 安装/启动性能基线 | `MR21` |
| 🟡 P2 | 非 Windows 宿主可行性 / 单平台 beta | `MR24` / `MR25` |
| 🟢 P3 | `figma.css` 仍有硬编码色值违反 token 规则 | — |
| 🟢 P3 | 插件/脚本扩展能力（长远，非当前） | — |
