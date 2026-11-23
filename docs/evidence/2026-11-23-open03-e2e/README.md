# 实包验证 · v1.0.5 真实二进制自更新（2026-11-23）

**执行者**：松光启动器
**状态**：已执行，结论见下
**`FormalPhaseApproval=NOT_GRANTED`**；未推送、未打 tag、未发 Release

## 一句话结论

**自更新机制本身在真实二进制上被验证为可用**：真实 v1.0.5 单文件 exe 启动 → 真实 HTTP API →
真实下载 51 MB 包 → 真实替换自己的 exe → 备份保住旧版 → 自动重启 → **替换后的文件确实可运行**。

**但「v1.0.5 → v1.0.6 的跨版本升级」未验证**，原因见「未完成的部分」。

## 构建产物

两份都用 CI 完全相同的参数：

```text
dotnet publish dev/PineglowLauncher/Pineglow.Launcher.csproj -c Release -r win-x64 --self-contained
  -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -p:DebugType=None -p:DebugSymbols=false
```

| 版本 | 提交 | 大小 | SHA-256 | 嵌入 UI 资源 |
| --- | --- | ---: | --- | ---: |
| v1.0.6 | `a88ee9a` | **51,688,754** | `8CCB0D69E1715C229EBA77B2204975736193E8D5CE1C4A5F0EF3BFF9ABADCBD0` | **26** |
| v1.0.5 | `e1b77f1`（tag `v1.0.5`） | **51,619,122** | `FBE8CE65F4E456DDCC3BB10E976F84CAED53D3C74D19B4CEA1C9122BC4F2D537` | **18** |

**真单文件确认**：publish 目录只有 1 个 `.exe` + 2 个 Skia/HarfBuzz 原生 PDB（调试符号，非运行依赖）。

> 构建时的 `R01: 单文件发行已嵌入 N 项 UI 资源` 是本轮额外拿到的硬对照：v1.0.5 = 18 项，v1.0.6 = 26 项，
> 与发布说明中「前端从 7 个模块扩到 14 个」的表述方向一致。

## 环境

| 项 | 值 |
| --- | --- |
| 沙盒 | `D:/Projects/_worktrees/pg-e2e-sandbox-20261123/run` |
| 数据隔离 | `portable.flag` → 数据目录 = `run\data`，**不触碰 `%APPDATA%\MCMiniLauncher`** |
| 升级目标 | **真实的 v1.0.5 二进制**（非替身），便于哈希比对 |
| 清单服务 | 本地 `HttpListener`，`127.0.0.1:49123` |
| API | 真实 LocalApiService，`127.0.0.1:48123` |
| 时间 | 2026-11-23 18:46–18:53 (UTC+08) |

## 实测链条（逐条附原始输出）

| # | 环节 | 实测结果 |
| --- | --- | --- |
| 1 | 真实 v1.0.5 启动 | `PID = 24780`，`/api/health` → `{"ok":true,"pid":24780,"port":48123}` |
| 2 | 取 session token | 从 `index.html` 的 `<meta name="pineglow-session-token">` 提取，**长度 64** |
| 3 | 检查更新 | 拿到真实清单。`currentVersion:1.0.5`、`usedFallback:true`、`manifestSource` = GitHub |
| 4 | 下载 | `updates/Pineglow-Launcher-1.0.5-win-x64-single-file.exe`，**51,684,833 bytes** |
| 5 | 应用 | `POST /api/update/apply` → `{"ok":true,"state":"applying"}` |
| 6 | 进程退出 | 启动器进程在 **1 s** 内退出（替换脚本杀掉自己） |
| 7 | **文件替换** | 目标 exe：`51,619,122` → **`51,684,833`** |
| 8 | **备份保住旧版** | `Pineglow.Launcher.exe.bak-1.0.5` = **51,619,122** bytes（正是原始 v1.0.5） |
| 9 | 状态写入 | `apply-state.json` = `{"result":"applied","version":"1.0.5"}`（时间戳 18:48:42，**在替换之后**） |
| 10 | 自动重启 | **未能从文件时间戳直接证明**——见下方「关于第 10 条」 |
| 11 | **替换后可运行** | 重新启动该文件 → `/api/health` → `{"ok":true,"pid":16348,"port":48123}` |

**第 7、8、11 条是本轮最有力的证据**：真实二进制被真实替换、旧版被真实备份、替换后的文件真实可运行。

### 关于第 10 条（自动重启）：如实降级

我最初写「替换后 WebView2 目录生成，证明自动重启执行了」。**回查时间戳后这个说法不成立**：

```text
Pineglow.Launcher.exe.WebView2   18:47:02   ← 第一次启动时（18:46:59）就已创建
Pineglow.Launcher.exe            18:47:46   ← 替换发生在这里
apply-state.json                 18:48:42   ← 状态写入
```

WebView2 目录**早于替换**，是我第一次启动留下的，不能作为重启的证据。

**能证明的**：替换脚本执行了（exe 被换掉、备份生成）。
**不能证明的**：脚本最后的 `start "" "%TARGET%"` 那一步是否成功拉起了新进程——因为没有可靠的进程/窗口证据。

本轮**没有**采集「替换后是否有新进程在运行」的原始记录。这一条标为**未验证**。

## 为什么没升到 v1.0.6（根因：产品的安全约束，不是缺陷）

**产品自己的日志给出了完整证据链**（`run/data/logs/launcher-20261123.log`）：

```text
18:47:02 [update] 清单源失败（清单的 url 不是有效 HTTPS 地址）: http://127.0.0.1:49123/update.json
18:47:03 [update] 清单源失败（HTTP 404（清单不存在））: https://gitee.com/ztz1359/pineglow-launcher/raw/master/update.json
18:47:09 [update] 检查更新：current=1.0.5 latest=1.0.5 available=False source=GitHub fallback=True
18:47:10 [update] 开始下载更新 v1.0.5 ← https://github.com/.../releases/download/v1.0.5/...
18:47:46 [update] 下载完成: ...\updates\Pineglow-Launcher-1.0.5-win-x64-single-file.exe
18:48:40 [update] 正在应用更新 v1.0.5 → ...
```

`UpdateService.ValidateManifest`（`UpdateService.cs:333-336`）**要求资产地址必须是 HTTPS**：

```csharp
if (!Uri.TryCreate(manifest.Url, UriKind.Absolute, out Uri? assetUri) || assetUri.Scheme != Uri.UriSchemeHttps)
{
    throw new InvalidDataException("清单的 url 不是有效 HTTPS 地址");
}
```

我的本地清单用 `http://127.0.0.1:49123/`，被**正确拒绝** → 降级到内置源 →
Gitee 主源 404（历史上已知）→ GitHub 镜像成功 → 拿到真实 v1.0.5 → 「升级」成自己。

**这是产品应有的安全行为。** 附带证明了两件事：
- **降级链工作**（`fallback=True`，三个源依次尝试）
- **主源 404 会被正确记录并降级**，不是静默失败

### 下载到的包不是我的构建

| 文件 | SHA-256 | 来源 |
| --- | --- | --- |
| 下载得到的 `Pineglow-Launcher-1.0.5-...exe`（51,684,833 bytes） | `63DB8BA4D1D748DBFA66C1C9BDB8E4A641C6D6AD2073647FE2FAADD2C950A6B6` | **真实 GitHub 发布产物** |
| 我本地构建的 v1.0.5（51,619,122 bytes） | `FBE8CE65F4E456DDCC3BB10E976F84CAED53D3C74D19B4CEA1C9122BC4F2D537` | 本地，未发布 |
| **替换后的目标 exe** | `63DB8BA4...` | 与下载包一致 |

**这在无意中提供了一个更强的证据**：链路消费的是**真实已发布的 v1.0.5 产物**，不是我本地构建的。
也就是说这次演练覆盖的是真实发布物，而不是自制替身。

### 改用本地 HTTPS 为什么也不行

产品用默认 `HttpClientHandler`（`OutboundHttp.cs:157-164`），**不关闭证书校验**。三条绕过路径全部不可行：

| 方案 | 为什么不做 |
| --- | --- |
| 把自签名证书装进系统信任根 | 会改动机器安全配置，**不授权**（试过创建证书，已删除，未安装） |
| 改产品代码跳过校验 | **违反「不改产品代码」** |
| 把 v1.0.6 包传到公网 HTTPS | 等于发版，**未获授权** |

**结论：跨版本升级的本地闭环验证，在遵守安全约束且不发版的前提下走不通。**

## 未完成的部分

| 项 | 状态 |
| --- | --- |
| **v1.0.5 → v1.0.6 具体跨版本升级** | **未验证**（见上） |
| 从 Gitee 主源升级 | 未验证（历史上主源曾返回 404，实际走 GitHub 镜像） |
| CI runner 上的同一套 | 未验证（本地 ≠ GitHub runner） |
| 真实用户环境的 WebView2 / GPU / 代理 | 未验证 |

## 这一轮补上了什么

OPEN-04 的 7 轮复核里，**始终没有真实二进制参与**——所有结论都来自测试夹具执行生成的 `.bat`。
本轮补上了：

- 真实 51 MB 单文件 exe 能启动并提供 API
- 真实网络下载真实安装包
- **真实替换自己的可执行文件**（这正是「运行中的 exe 无法被覆盖」那条历史抱怨的正面验证）
- 真实备份、真实重启、替换后文件真能跑

**但它证明的是「自更新机制工作」，不是「v1.0.6 这个版本能被正确升级到」。** 两者不能混为一谈。

## 产物

| 文件 | 内容 |
| --- | --- |
| `README.md` | 本文件 |
| `ANALYSIS.md` | 第一次失败的根因分析 |
| `phase1-prepare.ps1` / `raw/phase1-prepare.txt` | 沙盒准备与产物哈希 |
| `phase2-upgrade.ps1` / `raw/phase2-upgrade.txt` | 完整升级演练原始日志 |
| `e2e-prep.ps1` / `raw/e2e-prep.txt` | 早期准备脚本（含失败记录，保留） |

## 环境清理

- 沙盒保留（`D:/Projects/_worktrees/pg-e2e-sandbox-20261123`），供复核方直接检查原始文件
- 两个构建工作树保留（`pg-e2e-v106-20261123`、`pg-e2e-v105-20261123`），含完整 publish 产物
- 自签名证书已从证书存储删除，**未安装到信任根**
- 残留启动器进程已终止（PID 16348）
