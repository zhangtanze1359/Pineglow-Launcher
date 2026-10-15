# 更新链实测：**下载→校验→替换→退出→重启** 全部走通；并**复现了已发布 v1.0.0 的启动缺陷**（第 59 轮）

日期：2026-10-06（Asia/Hong_Kong）。本轮**未改动任何 tracked 文件**，也**没有改本机信任库**。
做法：本地用 **HTTP** 提供 `manifest.json`（内容我控制，日志记录每次请求），把 `config.json` 的 `updateUrl` 指过去，
产物地址用**公开 HTTPS**（清单校验强制），依次调 `check → download → apply`，并观察 EXE 是否被替换、进程是否退出、是否重启。

## 1. 更新链：逐步都拿到了证据

| 步骤 | 结果 |
| --- | --- |
| `GET /api/update/check` | `{"enabled":true,"currentVersion":"1.0.0","updateAvailable":true,"latestVersion":"1.0.1","manifestSource":"http://127.0.0.1:64118/manifest.json","usedFallback":false}` |
| 本地清单是否真被取走 | **是**：我的 HTTP 服务器日志记录到 1 次 `GET /manifest.json` |
| `POST /api/update/download` | `{"ok":true,"state":"downloading","version":"1.0.1"}` →（轮询 `GET /api/update`）→ **`state=completed`**，`path=…\updates\Pineglow-Launcher-1.0.1-win-x64-single-file.exe` |
| 启动器日志（产品自己的）| `[update] 检查更新：current=1.0.0 latest=1.0.1 available=True source=http://127.0.0.1:64118/manifest.json fallback=False` / `[update] 开始下载更新 v1.0.1 → …` / `[update] 下载完成: …` / `[update] 正在应用更新 v1.0.1 → …` |
| `POST /api/update/apply` | `{"ok":true,"message":"更新已启动，重启后生效。"}` |
| **EXE 是否被替换** | **是**：`sha256 5009cf73…（候选）` → **`6130db19…`，与清单里写的值逐字相同** |
| 原进程 | **已退出**（`原进程存活=False`）|
| 落盘状态 | `updates\apply-state.json` = **`{"result":"applied","version":"1.0.1"}`** |
| **是否重启** | **尝试了**：`apply` 后 **1 秒**，冷目录里的 WebView2 配置/会话文件被写入（`08:57:39`）⇒ 有进程起来过 |

## 2. 重启后的进程为什么没活下来 —— **因为我的产物是"已发布 v1.0.0"，而它有启动缺陷**

重启起来的进程在初始化 Avalonia/Skia 时崩了：

```
Unhandled exception. System.TypeInitializationException: The type initializer for 'SkiaSharp.SKImageInfo' threw an exception.
 ---> System.DllNotFoundException: Dll was not found.
   at SkiaSharp.SkiaApi.sk_colortype_get_default_8888()
   at Avalonia.Skia.PlatformRenderInterface..ctor(...)   →   at Pineglow.Launcher.Program.Main(String[] args)
```

### 2.1 对照实验（同一台机器、同样的"全新空目录只放 EXE"）

| 二进制 | 结果 |
| --- | --- |
| **候选 EXE**（53,052,722 B）| ✅ **正常启动**（健康检查 200）|
| **已发布 v1.0.0 资产**（43,989,943 B，GitHub Release 原件，sha256 `6130db19…`）| ❌ **崩**（上面的 `DllNotFoundException`）|

### 2.2 根因：v1.0.0 的单文件包里**没有**原生库

- 候选 EXE 运行时会自解压出：`libSkiaSharp.dll`（**11.4 MB**）、`libHarfBuzzSharp.dll`（1.8 MB）、`av_libglesv2.dll`（5.3 MB）——合计约 **18.5 MB**（未压缩），与两者体积差 **+9.1 MB** 吻合。
- **把这三个 DLL 放到 v1.0.0 的 EXE 旁边，它就能启动**（实测 `起来了=True`）⇒ 二进制本身没问题，**是包少了原生库**。
- 候选的 `.csproj` 正是为此加的开关（注释写明"原生库默认**不会**进入 PublishSingleFile"）：
  ```xml
  <PropertyGroup Condition="'$(PublishSingleFile)' == 'true'">
    <IncludeNativeLibrariesForSelfExtract>true</IncludeNativeLibrariesForSelfExtract>
  ```
⇒ **本轮独立复现了原始评审的 G07 / R01 现象**（"只交付 EXE 漏必要依赖"），并**实测确认候选的打包修复有效**。

## 3. 顺带发现（加固项）：**清单地址允许走 HTTP**

`UpdateService.AddCandidate` 同时接受 `https` 与 `http`（只排除其它协议），而 `ValidateManifest` **只强制"产物 url 是 HTTPS"**。
⇒ 中间人可以篡改 HTTP 清单里的**产物地址与 SHA256**（两者一起改，校验照样通过），把更新指向任意 HTTPS 主机上的任意文件。
产品自己的 `trustNote` 也承认这一点（"清单与产物同源时，两者可被同时篡改"）。**建议**：清单地址也强制 HTTPS（或固定只信任默认源）。

## 4. 复现

```powershell
# 本地 HTTP 清单 + 公开 HTTPS 产物；apply 后观察替换/退出/重启
powershell -File ...\run-20261006-1800-update-chain\verify-update-chain.ps1 `
  -ExePath <候选 EXE> -ServerScript ...\serve-manifest.cjs `
  -AssetUrl https://github.com/zhangtanze1359/Pineglow-Launcher/releases/download/v1.0.0/Pineglow-Launcher-win-x64-single-file.exe `
  -AssetSha256 6130db19bbcc8b4d6b606bc1825d824a99a19c72ef2ef7af3ac874515953a999 -AssetSize 43989943 `
  -JsonPath <证据>\update-chain-restart.json -WorkRoot %TEMP%\pg-updchain3
# 只用本地 HTTP 清单、产物用 Modrinth 的 .mrpack（验证"清单可 HTTP"这一条）
```

产物：`update-chain-restart.json` / `update-chain.json`（两次运行的完整字段）、`update-chain-restart.log` / `update-chain.log`、
`manifest.json`、`manifest-requests.log`（HTTP 服务器收到的请求）、`serve-manifest.cjs`。

## 5. 我自己的一个坑（同类第 7 次）

`POST /api/update/download` 是**后台任务**：我第一版脚本紧跟着调 `apply`，得到
`{"ok":false,"message":"更新包未就绪，请先下载。"}`（**产品行为正确**）。修法：轮询 `GET /api/update` 到终态（`completed`）再 apply。
——与第 56 轮 `POST /api/versions/download` 是同一类教训：**凡"排队/后台"接口，必须轮询到终态**。

## 6. 诚实边界

- **重启**是"机制层面"验证：`apply` 确实触发了重启（1 秒后有进程写入 WebView2 配置），但**重启起来的二进制是我选的 v1.0.0 发布件**，
  它因自身打包缺陷崩溃；**用一个能正常启动的产物重启**这一步，本环境**做不到**——候选 EXE 没有公开 HTTPS 地址，我也不会去发布它。
- 因此"更新端到端"可以记为：**检查 → 下载 → SHA256 → 替换 → 原进程退出 → 触发重启** 全部实测通过；
  **"重启后新进程正常提供服务"** 仍记 NOT_RUN（缺一个可公开获取的、能启动的新版本产物）。
- 未验：Gitee 固定 `update.json` 源（需要 `GITEE_TOKEN`）、真实 GitHub Release 的 manifest（当前仓库只有 v1.0.0 一个 release，其 manifest 已随本轮记录）。
