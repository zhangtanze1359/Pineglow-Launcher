# 三个 blocker 的**待应用补丁集** + 验证清单（第 66 轮）

日期：2026-10-06（Asia/Hong_Kong）。本轮**未改动任何 tracked 文件**——所有补丁都是"把源文件复制到 `%TEMP%`、
只在副本上改、用 `git diff --no-index` 生成"，工作区始终是 `modified=44 / staged=0`。

## 0. 一句话

三个 blocker 的修法都定位到**一行/一处**、都**已在行为上验证过**（除了本集里最新那份 ConfigStore 补丁，它靠最小复现定位、逻辑明确）；
把它们一次应用、一次重跑全量验证链，是最省事的路。

## 1. 补丁清单（本目录内为副本，原始出处见括号）

| # | 补丁 | 规模 | 修什么 | 状态 |
| --- | --- | --- | --- | --- |
| ① | `proposed-fix-versionresolver.diff`（← `run-20261006-1310-loader-fix-patch/`）| **+48 / −1** | **blocker ①**：`VersionResolver` 对 `arguments` 是**整体替换** ⇒ 加载器 profile 覆盖父版本、丢掉 `-Djava.library.path`（Fabric 实测崩）| 算法已独立验证（`MERGE-CHECK RESULT=PASS`）；"只补该参数就能进渲染管线"已由受控实验证实 |
| ② | `proposed-test-versionresolver.diff`（← 同上）| **+39（新文件）** | 给 ① 配**回归测试**（断言父版本 `-Djava.library.path=${natives_directory}` 合并后仍在）| 需同时加进 `scripts/source-snapshot-inputs.txt` 白名单 |
| ③ | `proposed-fix-B-launcherservice.diff`（← 同上）| **+6** | **blocker ① 的最小兜底版**（走 `arguments.jvm` 分支时缺库路径就补）——与 ① **二选一** | 已验证（受控实验就是"只补这一个参数"）|
| ④ | `proposed-fix-installer-url.diff`（← `run-20261006-1900-forge-url-probe/`）| **−1 / +1** | **blocker ②**：`InstallerLoaderService.GetInstallerUrl` 把 artifact 段拼了两遍 ⇒ Forge/NeoForge 安装器 **404** | 已验证：修后 URL 均为 200，且产品自己的 `DownloadFileAsync` 实测成功（6,068,826 B）|
| ⑤ | `proposed-fix-configstore-default.diff`（← `run-20261006-2130-patch-set/`）| **+6 / −0**（两处）| **blocker ③**：`ConfigStore.LoadAsync()` 造完默认实例/账号**不落盘** ⇒ 每次 `/api/state` 的 id 都不同、针对它的操作全 400 | **已在打过补丁的构建上验证**：`/api/state` 三次 id 相同、`/launch` 不再是"实例不存在"；副本 C# **904/904** |
| ⑥ | `proposed-fix-forge-launcher-profile.diff`（← `run-20261006-2800-patched-forge/`）| **+7 / −0** | **blocker ④（B2b）**：跑 Forge/NeoForge 安装器前没写 `launcher_profiles.json` ⇒ 安装器 ExitCode=1 | 手工对照：写 46 字节桩后**同一安装器 exit 0**；⚠️ 端到端受**本机环境**限制（安装器 HTTPS 握手失败）|

## 2. 应用方式

```powershell
cd D:\松光启动器
# 逐个 apply（--check 先干跑）
git apply --check docs\release-remediation-evidence-2026-10-05\run-20261006-1310-loader-fix-patch\proposed-fix-versionresolver.diff
git apply        docs\release-remediation-evidence-2026-10-05\run-20261006-1310-loader-fix-patch\proposed-fix-versionresolver.diff
# ② 是新增文件，直接 apply 即可；③ 与 ① 二选一（若只想最小改动，用 ③ 不用 ①）
git apply docs\release-remediation-evidence-2026-10-05\run-20261006-1900-forge-url-probe\proposed-fix-installer-url.diff
git apply docs\release-remediation-evidence-2026-10-05\run-20261006-2130-patch-set\proposed-fix-configstore-default.diff
```
> 说明：本目录里的 `proposed-fix-*.diff` 是**副本**，`git apply` 用哪一份都行。
> **已逐份干跑 `git apply --check`：6/6 全部 ✅ 可应用**（本轮修正了三处会导致 "corrupt patch / invalid path / dev/null" 的头部问题：
> 去掉了 diff 里的人类注释行与 BOM、把临时路径改成仓库相对路径、给新增文件补了 `new file mode 100644`）。

## 3. 应用后必须跑的验证清单（按顺序）

| # | 命令 / 动作 | 期望 |
| --- | --- | --- |
| 1 | `dotnet test dev/PineglowLauncher.Tests/Pineglow.Launcher.Tests.csproj -c Release` | 全量 **903 + 新增** 全绿 |
| 2 | `run-20261006-1130-vanilla-launch\verify-vanilla-launch.ps1` | Vanilla 1.18.2 进主菜单、10 图集（**不能被 ① 弄坏**）|
| 3 | `run-20261006-1230-fabric-launch\verify-fabric-launch.ps1` + `probe-fabric-sequence.ps1` | 命令行**含** `-Djava.library.path`；`Backend library: LWJGL`、12 图集、**0 崩溃** |
| 4 | `run-20261006-1530-quilt-probe\verify-quilt-launch.ps1` | Quilt 仍正常（**不能被 ① 弄坏**）|
| 5 | Forge/NeoForge：导入一个真实包（如 `optimized-fps`）| **不再 404**（已验）；且安装器要能跑到 **`Successfully installed client into launcher.`**、落盘 `versions/1.20.1-forge-47.4.3`。⚠️ **需要正常网络**：本机安装器自身 HTTPS 下载会 `SSLHandshakeException`（ASCII 与中文路径同样失败，已排除中文路径）|
| 6 | `run-20261006-2100-default-instance-id\verify-default-instance-id.ps1` | 由 `REPRODUCED` 变为**不再复现**（三次 state 的 id 相同、launch/prelaunch 不再是"实例不存在"）|
| 7 | `run-20261006-2100-default-instance-id\verify-default-account-id.ps1` | 同上（默认账号 id 稳定）|
| 8 | `run-20261006-0450-final\final\verify-source-snapshot.ps1`（全量验证链）| 锁定还原 / Node / C# / UI 闭包 / 清单双向一致 / 发布可复现 / 冷启动 **19/19** |
| 9 | 重新生成发布物并记录新的 **sha256**（旧 `5009cf73…` 作废）| 新证据归档到 `run-<新时间戳>-final/` |

> ⚠️ 改了 `dev/**` ⇒ **产物字节变化** ⇒ 第 8 步不可省；既有"EXE sha256 逐字节一致 11 轮"的结论会随之作废，需要重新建立。

## 4. 本轮的补充证据（blocker ③ 的第二个面）

**默认账号同病**（`run-20261006-2100-default-instance-id/verify-default-account-id.ps1`）：
连续两次 `/api/state` 的默认账号 id 不同（`dd89f67a…` / `36c780ea…`），
`DELETE /api/accounts/{id}` 返回 **400 `至少保留一个账号。`**（先撞到"至少留一个"的守卫，所以这条不能像实例那样直接证明"找不到"，
但**id 不稳定本身已被证明**，且同源于 `LoadAsync()` 不落盘）。

## 5. 诚实边界

- ⑤ 的补丁**没有跑过构建/测试**（我按约定不改工作区源码）；它是最小复现 + 代码路径推出的，应用后**必须**由第 1、6、7 步确认。
- ① 与 ③ 是**二选一**：① 治根（合并语义正确、还修好 `arguments.game`），③ 改动最小；**两者不要同时应用**（会重复补参数，虽无害但不必要）。
- 本补丁集**不含**"决定项"级别的改动（代理启动生效、取消导入清理、清单强制 HTTPS、图标超时、文案等）——那些等你勾选后再做。
