# 更新链补完：**重启后的新进程确实起来并提供了服务**（第 60 轮）

日期：2026-10-06（Asia/Hong_Kong）。本轮**未改动任何 tracked 文件**。
承接第 59 轮：那一轮 `apply` 后**重启被触发**，但重启起来的二进制崩了 —— 原因已查明是**我选的产物（已发布 v1.0.0）缺原生库**。
本轮把那 3 个原生库**预先放到目标目录**，让"能启动的产物"跑起来，从而把最后一格补齐。

## 1. 结果：重启后新进程正常服务 ✅

`apply` 之后，**产品自己的日志**（`data\logs\launcher-20261006.log`）连续给出：

```
09:14:50.598 [update] 正在应用更新 v1.0.1 → …\updates\Pineglow-Launcher-1.0.1-win-x64-single-file.exe
09:14:52.645 === Pineglow Launcher 启动 v1.0.0 ===                       ← 重启起来的进程（2 秒后）
09:14:52.685 [LocalApi] 监听 127.0.0.1:64714, ui=…\cold\data\UI         ← 它绑定了 API 端口
```

配合脚本侧的三条观察：

| 观察 | 值 |
| --- | --- |
| `POST /api/update/apply` | `{"ok":true,"message":"更新已启动，重启后生效。"}` |
| **EXE 被替换** | `sha256 5009cf73…` → **`6130db19…`（与清单逐字相同）** |
| 原进程 | **已退出** |
| **重启检测** | **`健康=True`**（在同一个 API 端口上拿到 `/api/health` 200）|
| 其它同名进程 | **1 个**（重启起来的新进程）|
| `apply-state.json` | `{"result":"applied","version":"1.0.1"}` |

⇒ **更新链最后一格补齐**：检查 → 下载 → SHA256 → **替换自己** → 原进程退出 → **重启并由新进程提供服务**。

## 2. 本轮的"预置原生库"是什么性质（诚实说明）

- 我把候选 EXE 自解压出的 `libSkiaSharp.dll`(11.4 MB) / `libHarfBuzzSharp.dll` / `av_libglesv2.dll` 预置到目标目录。
- **为什么需要**：我唯一能拿到的公开可下载产物是**已发布的 v1.0.0**，而它的单文件包**缺这些原生库**（第 59 轮已证：把它仨放到旁边就能启动）。
- **真实更新里不需要这一步**：新版本产物自带原生库（候选的 `<IncludeNativeLibrariesForSelfExtract>true</IncludeNativeLibrariesForSelfExtract>` 就是修这个）。
  ⇒ 本轮的预置只是**让测试用的旧产物能跑起来**，属于测试夹具，不是产品缺陷的绕过。

## 3. 一个我自己的观察偏差（记下来）

本轮我**手动**再启动一次被替换后的二进制做交叉确认，脚本报"没起来"，但**产品日志显示它其实起来了**（`09:16:45 === 启动 v1.0.0 ===` + `监听 127.0.0.1:62150`）。
原因：我的判定要求先从界面 HTML 里抓到会话令牌；而 **v1.0.0 的 UI 解析方式与候选不同**——
v1.0.0 的日志是 `ui=…\cold\data\UI`，候选是 `ui=…\cold\data\UI\1.0.0-41346c7082cb`（**版本键控解包**，正是 R02 的整改点）。
令牌抓不到 ⇒ 我的检查判"没起来"，而 `/api/health` 其实一直是 200。
⇒ **结论不受影响**（重启的判定来自 `/api/health` 与产品日志），但这提醒：**判定条件要选"与产品版本无关"的探针**。

## 4. 复现

```powershell
# 预置原生库（让测试用的 v1.0.0 产物能启动）+ 完整更新链
powershell -File ...\run-20261006-1800-update-chain\verify-update-chain.ps1 `
  -ExePath <候选 EXE> -ServerScript ...\serve-manifest.cjs `
  -AssetUrl https://github.com/zhangtanze1359/Pineglow-Launcher/releases/download/v1.0.0/Pineglow-Launcher-win-x64-single-file.exe `
  -AssetSha256 6130db19bbcc8b4d6b606bc1825d824a99a19c72ef2ef7af3ac874515953a999 -AssetSize 43989943 `
  -PrePlaceNativeLibs %TEMP%\.net\Pineglow.Launcher\<hash> `
  -JsonPath <证据>\update-chain-restart2.json -WorkRoot %TEMP%\pg-updrestart
```

产物：`update-chain-restart2.json` / `update-chain-restart2.log`（本轮完整时间线）、`post-update-serves.json`（替换后二进制的元信息）。
