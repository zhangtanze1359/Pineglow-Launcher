# 第 5 轮：真实更新链（下载 → 校验 → 替换 → 重启）复验

运行标识：`run-2026-10-10_015215-578dbafb`（第 5 轮追加）
时间：2026-10-10 02:46–02:54，Asia/Hong_Kong（+08:00）
本轮性质：**只做验证与取证，产品目录零改动**（A/B/C 相关文件哈希与前几轮一致，0 staged）。

## 1. 第 3 轮声明的受限项，本轮在不改 TLS 信任、不上传任何东西的前提下解决了

第 3 轮我判断「下载半段需要真实 HTTPS 产物地址，本机没有受信任证书，因此无法复验」。
本轮换了个角度：**用项目自己已经公开发布的 HTTPS 产物**作为更新目标——
`https://github.com/zhangtanze1359/Pineglow-Launcher/releases/download/v1.0.0/Pineglow-Launcher-win-x64-single-file.exe`
（43,989,943 字节，manifest 声明 sha256 `6130db19…`）。这样：

- 清单仍由**本地 HTTP** 服务器提供（产品允许 http/https 作为清单源，实测被接受）；
- 产物走**真实公网 HTTPS**（产品在 `UpdateService.cs:279` 强制产物必须是 https）；
- 没有改证书信任库、没有关闭 TLS 校验、没有上传任何文件、没有向第三方发送数据。

## 2. 正向链：全链通过（`d-chain-restart/chain-positive.json`，探针退出码 0）

产品日志（`<fixture>/data/logs/launcher-*.log`）逐条可见：

```
=== Pineglow Launcher 启动 v1.0.0 ===
[LocalApi] 监听 127.0.0.1:60341, ui=...\install\data\UI\1.0.0-41346c7082cb
[update] 检查更新：current=1.0.0 latest=1.0.2 available=True source=http://127.0.0.1:60340/manifest.json fallback=False
[update] 开始下载更新 v1.0.2 ← https://github.com/.../Pineglow-Launcher-win-x64-single-file.exe
[update] 下载完成: ...\install\data\updates\Pineglow-Launcher-1.0.2-win-x64-single-file.exe
[update] 正在应用更新 v1.0.2 → ...\install\data\updates\Pineglow-Launcher-1.0.2-win-x64-single-file.exe
```

| 断言 | 结果 |
| --- | --- |
| 本地 HTTP 清单被真正取用 | `manifestFetches=1`，`fallback=False` |
| 检出更新 | `current=1.0.0 latest=1.0.2 available=True` |
| 真实 HTTPS 下载 + 大小/哈希校验 | `downloadState=completed`，无 error（44 MB 约 10 秒） |
| EXE 被替换 | `exeReplaced=true`，替换后哈希 `6130DB19…` **逐字节等于清单声明的产物** |
| 旧版本被备份 | 夹具中出现 `Pineglow.Launcher.exe.bak-1.0.2` |
| 应用结果被记录 | `apply-state.json` = `{"result":"applied","version":"1.0.2"}` |
| 替换脚本执行完毕 | `apply-update.bat` 已按脚本自身逻辑 `del "%~f0"` 自删 |
| **重启后新进程可用** | `restartedHealthy=true`（同一端口 `/api/health` 200；`restartedVersion` 未取到，见 §4） |
| 只停本轮自有进程 | `ownedProcessesStopped=true`（按可执行文件路径精确匹配夹具内进程，未按进程名批量处理） |

## 3. 反向链：篡改/不匹配的产物必须被拒绝（`d-chain-negative/`，探针退出码 0）

清单里把 `sha256` 改成 64 个 `a`（长度合法、值不匹配）：

```
downloadState = failed
downloadError = 更新包下载失败：校验失败：https://github.com/.../Pineglow-Launcher-win-x64-single-file.exe
exeReplaced    = false      ← 安装目录里的 EXE 一个字节都没动
```

说明完整性校验在**真实网络路径**上确实生效，而不是只对假 handler 生效。

## 4. 顺带确认的一个真实发布事实（历史 R01 缺陷的直接证据）

第一次跑正向链时没有预置原生库，结果链条本身全部成功，但**重启起来的新进程立刻崩溃**，
夹具根目录留下 `crash.log`：

```
System.TypeInitializationException: The type initializer for 'SkiaSharp.SKImageInfo' threw an exception.
 ---> System.DllNotFoundException: Dll was not found.
   at SkiaSharp.SkiaApi.sk_colortype_get_default_8888()
   ...
   at Avalonia.AppBuilder.Setup()
```

即：**GitHub 上已发布的 v1.0.0 单文件产物本身不是自足的**（原生库没有随单文件打包）——正是 2026-10-05 报告里的 R01 缺陷，
后来的候选已修复（当前源码的单文件产物内嵌 13 项 UI 资源并自带原生库，冷启动 19/19 通过）。
本轮为了验证「重启后可用」这一段，夹具按 2026-10-06 的同类做法预置了三个原生库
（`libSkiaSharp.dll`、`libHarfBuzzSharp.dll`、`av_libglesv2.dll`，取自当前源码的构建产物），
并在结果里记录为 `nativeLibsPrePlaced`。**这是对旧产物已知缺陷的夹具适配，不属于链条本身**。

`restartedVersion` 未取到：重启后的 1.0.0 旧产物返回的首页里没有取到会话令牌 meta，未再追查（不影响健康断言）。

## 5. B 的收口状态

| B 的 GREEN 项 | 状态 |
| --- | --- |
| 旧目录/旧 UI 升级后用新版 UI | 已证（第 3 轮真实 EXE 升级探针 + 第 1 轮 legacy 探针） |
| 配置不丢 | 已证（第 3 轮：`config.json` 升级前后逐字节相同） |
| 失败可回滚 | 已证（`UpdateRecoveryTests` 真实执行脚本 + 第 3 轮 RED 实证） |
| 完整链路留证 | **本轮补齐**：本地 HTTP 清单 → 公网 HTTPS 下载 → 校验 → 替换 → 备份 → 应用记录 → 重启健康；含不匹配拒绝的反向用例 |
| 中文/空格路径 | 由流水线冷启动用例覆盖（每次发布都会跑） |

**唯一残余限制**：链条里被替换上去的产物是**已发布的旧 1.0.0 构建**（清单里标为 1.0.2），不是当前候选——
因为把当前候选放到公网 HTTPS 需要另行授权的发布动作。也就是说：链条机制已全链验证；
「用当前候选作为升级目标」这一条要等 F（有授权的候选提交与发布）才能闭环。

## 6. 本轮没有做的事

未改产品源码、未提交、未推送、未发布、未上传任何文件、未改证书信任库或任何系统安全设置。
新增的只是证据目录下的自有探针与被审阅后复制的本地清单服务器（`serve-manifest.cjs`，仅监听 127.0.0.1、只写日志）。
（该文件来自 2026-10-06 证据目录；本轮先审查了它的副作用，见第 3 轮记录中对旧链脚本的处理。）
