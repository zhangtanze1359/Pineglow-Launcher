# 功能矩阵与能力清单

> **最后核对日：2026-11-06**。本文取代旧《技术对比文档》的功能盘点（其逆向结论已过时）。

## 一、前端 10 视图真实状态

> 视图数以 `ui/js/launcher.js` 的 `VIEWS` 表为准（9 个常规视图 + `playtime` 委托给 `SG.playtime` 模块），与 `ui/index.html` 的 `view-*` 容器数量一致，均为 10。

| 视图 | 状态 | 说明 |
|---|---|---|
| home | ✅ 完整 | 启动/停止真实链路，静态新闻 |
| installations | ✅ 完整 | 实例管理 + Mod 弹窗（toggle 已写盘）+ AI 崩溃诊断弹窗（含嫌疑 Mod 一键禁用）+ Mod 二分定位弹窗 |
| worlds | ✅ 完整 | 读存档列表 + 备份/恢复/删除 |
| servers | ✅ 完整 | 增删/ping 真实 |
| content | ✅ 完整 | 搜索真实 + 安装写盘（入队式） |
| skins | ✅ 部分 | 预览真实，上传皮肤已可用 |
| downloads | ✅ 完整 | 远程版本 + 下载任务轮询 |
| news | ✅ 完整 | 独立新闻页 |
| settings | ✅ 完整 | 内存/Java/离线账号/日志/更新/微软登录/代理/下载源/AI 提供商管理（多预设：DeepSeek/OpenAI/通义千问/智谱/Ollama，可新增/编辑/删除/设为当前） |
| playtime | ✅ 完整 | 游玩时长树状图（按实例分组、跨午夜会话切分、区间汇总），数据取自 `/api/stats` |

> 注：旧架构说明中"worlds 仅 toast""content 安装仅 toast""skins 上传未实现"等**已过时**，8/10 已全部写盘接通。

## 二、能力全景（按模块）

### 账号体系
- 离线账号（离线 UUID 生成）✅
- 微软账号：设备码登录（microsoft.com/link 输码授权，无 redirect_uri 依赖）+ Refresh Token 续期 ✅
- Authlib 外置登录（Yggdrasil 协议，自建皮肤站/认证服务器）✅
- 多账号并存与切换 ✅
- 皮肤上传（pngBase64 + slim）✅、披风查看 ✅

### 游戏与版本管理
- 官方版本清单（piston-meta + 镜像回退）✅
- 版本安装/校验（SHA1 全量）/修复/删除 ✅
- 下载队列与后台任务 ✅、断点续传 + 可恢复任务 ✅
- 库文件/原生库/assets 并行下载 ✅

### 加载器与整合包
- Forge / Fabric / NeoForge / Quilt / OptiFine 安装 ✅
- ⚠️ **缺 Cleanroom / Legacy Fabric**（HMCL 已覆盖，生态兼容硬缺口，待补；见 [roadmap.md](roadmap.md) 第七节第 7 项）
- Modrinth 整合包导入/安装/增量更新 ✅
- CurseForge 整合包导入（内置官方签发 Pineglow Key，**已免 Key**）✅
- 实例导出（.mcm 自定义格式，**不与 Modrinth `.mrpack` / Prism `.prisminstance` 互通**）、升级前自动备份 ✅

### 实例系统
- 新建/复制/重命名/删除，实例级 Java 指定 ✅
- 实例隔离：`instances/<name>` 独立目录（完整克隆或仅用户数据）✅

### Mod 与内容管理
- 本地 Mod 启停/删除/导入，在线搜索（Modrinth + CurseForge）✅
- 依赖解析与自动安装（Modrinth 依赖图）✅
- Mod 更新检查（单个/批量）✅
- Mod 健康检查：重复安装/依赖缺失/版本不匹配/加载器兼容 ✅
- Mod 二分定位（Bisect）：分批禁用定位崩溃元凶，结束自动恢复 ✅
- 资源包/光影包/存档管理（导入/删除/备份）✅
- 服务器收藏 + 状态 Ping ✅

### Java 与启动
- Java 自动检测（常见路径/环境变量/注册表）✅
- Java 自动下载安装（SHA256 校验、版本匹配、按实例分配）✅
- 内存自动推荐（MemoryAllocationService）✅
- JVM/游戏参数自定义、窗口分辨率、直接加入服务器 ✅
- 导出启动脚本（仅离线账号，防令牌泄露）✅
- 游戏日志实时显示/过滤/崩溃分析/诊断包导出 ✅
- 启动前环境检查（Java 存在/游戏目录/磁盘空间/内存预算）✅
- 启动后最小化、退出后自动关闭 ✅

### 工程与体验
- 日志落盘（`%AppData%\MCMiniLauncher\logs\launcher-yyyyMMdd.log`，保留 7 天）✅
- 自更新框架（check/download/sha256/apply，Gitee 主源 + GitHub 备用）✅
- 多下载源（official/bmclapi/mcbbs + 测速 + 切换）✅
- 皮肤站预设 ✅、HTTP 代理设置 ✅
- 游玩统计（启动次数/时长，PlayCount/TotalPlaySeconds）✅
- 拖拽导入整合包 ✅、i18n 双语 ✅、深色模式 ✅
- 崩溃分析（规则引擎 CrashAnalyzerService）✅ + **AI 崩溃诊断**（`instances/{id}/diagnose`，OpenAI 兼容，多提供商预设管理：DeepSeek/OpenAI/通义千问/智谱/Ollama，可新增/编辑/删除/设为当前，Key 走 SecretStore 不入盘）✅

## 三、工程加分项

- 断点续传 + 可恢复任务（重启后恢复未完成任务）✅
- 凭据安全存储（Windows 凭据管理器，非 Windows 降级 secrets.json）✅
- 诊断包导出 ✅
- 命名空间统一 `Pineglow.Launcher.*` ✅
- 版本号单一来源（程序集 = LauncherService.LauncherVersion = 1.0.0）✅
- 单文件自包含发布（测试体系 xUnit + CI）✅

## 四、遗留事项（未完成）

- 🟢 ~~微软登录 DeviceCode 降级（二期）~~ ✅ 已实现（/api/accounts/microsoft/device + poll + 设置页弹窗）
- 🟠 `figma.css` 仍有硬编码色值违反 token 规则（标"当前激活 token"为准）
- 🟡 插件/脚本扩展能力（长远，非当前）
