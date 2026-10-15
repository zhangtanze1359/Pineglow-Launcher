# 松光启动器发布执行手册（Runbook）

日期：2026-10-06，Asia/Hong_Kong。配套：`release-readiness-reassessment-2026-10-06.md`（结论与逐条复评）、
`..\..\D:\松光启动器\docs\release-remediation-evidence-2026-10-05\REMEDIATION-PROGRESS-2026-10-05.md`（过程与证据）。

**这份手册只做一件事：把"放行一个版本"需要跑的命令、期望输出和中止条件排成一条可执行的路。**
它**不**构成放行；每一步的判定标准都写在下面，判定人是你。

> 术语：SourceRoot = `D:\松光启动器`；DeliveryRoot = 本文件所在目录（e423 工作树 docs）。
> 全部命令都在 SourceRoot 下执行（除特别说明）。

---

## 0. 三条不变量（任何时候都必须成立）

| 不变量 | 怎么查 | 期望 |
| --- | --- | --- |
| 两个 lock 文件必须是**非 RID 形态** | `Get-Item dev\PineglowLauncher\packages.lock.json` | 8663 字节 / sha256 `fbbc9ae67b8e8cf2`（tests: 12806 / `244ba2bcd1eb1f5a`）|
| 白名单文件必须**可 `git add`**（未被忽略）| 逐条 `git check-ignore -q -- <路径>` 看**退出码** | 0 条被忽略 |
| 环境里不得带开发旋钮 | `Get-ChildItem env: \| ? Name -like 'PINEGLOW_*'` | 理想为空。本机实测会看到 `PINEGLOW_WEBVIEW_DEBUG=9222`（历史遗留）：**冷启动门禁会自动清除并记录**（`scrubbed=…`），UI 九页脚本会自己设端口，因此无影响；但**别把新的开发旋钮引进发布环境** |

**踩过的坑（都要记住）**：
- 一次 `dotnet publish -r win-x64` 就会把 launcher 的 lock 文件**就地改写成 RID 形态**（8663 → 10868 字节），
  之后所有 `--locked-mode` 还原都会 NU1004。复原：`dotnet restore dev/PineglowLauncher/Pineglow.Launcher.csproj --force-evaluate`（复原后必须回到上面那个哈希）。
- 判断"是否被忽略"要看 `git check-ignore` 的**退出码**，不是它的输出（反选规则 `!xxx` 命中时它也会打印）。
- `git add` **必须**保持 `core.autocrlf=true`（本机默认）。用 `-c core.autocrlf=false` 提交会让这批文件产生"纯换行差异"的大 diff。

---

## 1. 本地全量验证（必跑；约 10 分钟）

```powershell
cd D:\松光启动器
powershell -ExecutionPolicy Bypass -File scripts\verify-source-snapshot.ps1 `
  -EvidenceDir docs\release-remediation-evidence-<日期>\final
```

**期望**（当前基线实测值，见下面的"常见偏差"）：

| 项 | 期望 |
| --- | --- |
| 最终判定 | `PASS`（退出码 0）|
| 快照文件数 | **307**（161 tracked + 146 白名单），额外/泄漏 0，无 bin/obj |
| Node 全套 | **304 通过 / 0 失败**（23 个文件，来自清单）|
| C# 全套 | **903 通过 / 0 失败 / 0 跳过** |
| VerifyHost 冒烟 | `[BisectSmoke] PASS` 且 `production credential backend calls must be 0` |
| 单文件 publish | **53,052,722 字节 / sha256 `5009cf73d0801e83…`**（已连续 11 轮逐字节相同）|
| 独立冷启动 | **19/19**，窗口 `松光启动器`，解包目录 `<版本>-<12位指纹>` |
| 已知未锁定边界 | RID 锁定还原探测 NU1004（如实记录，非缺陷）|

**失败怎么查**：证据目录里每个步骤都有自己的日志（`step*.log`）；`verify-summary.json` 记录组装细节
（`whitelistMissing`、`hashMismatches`、`unexpectedFiles`）；`step0-step-runner-selftest.log` 是运行器自证。

**常见偏差（先看这些，别急着改代码）**：
- `NU1004` → lock 文件被 RID 发布污染 → 按 §0 复原。
- 快照文件数不是 307 → 白名单被改动（对照 `scripts/source-snapshot-inputs.txt`）。
- 含非 ASCII 的 `.ps1/.psm1` 报编码缺陷 → 用**带 BOM** 的 UTF-8 重存（编辑工具会抹掉 BOM）。
- C# 数不是 903 → 有人加了/删了测试；**测试数下降需要解释**。

---

## 2. 专项验证（按需；每一项都写清"证明什么 / 不证明什么"）

| 验证 | 命令 | 期望 | 证明 / 不证明 |
| --- | --- | --- | --- |
| UI 九页真实渲染 | `node scripts\verify-ui-pages-live.cjs --app <单文件EXE> --out <目录> --json <文件> [--fake-ip-compat]` | `UI-PAGES RESULT=PASS views=9/9`，且 `uiAssetFailures=0`、`productLocalFailures=0` | 证明真实 WebView2 里九页渲染；**不证明**外部服务本身可用（外部失败会记为 `externalPending` 并**判 FAIL**）|
| 真实网络更新链 | `powershell -ExecutionPolicy Bypass -File scripts\verify-update-live-download.ps1 -ExePath <EXE> [-AssetUrl <真实HTTPS资产>]` | `更新链路真实验收判定: PASS` | 证明默认源降级/真实下载与篡改拒绝；**不证明**发布侧写入已发生。**第 33 轮已用真实发布资产跑过 PASS**（`-AssetUrl` = v1.0.0 的 EXE，43,989,943 B；阶段 A 主源 404 → 真实备用源降级）：`run-20261006-0750-remote-truth/` |
| 工作流静态检查（actionlint）| 见 `run-20261006-0740-actionlint/`：镜像下载 1.7.7（双镜像 SHA256 一致 = `7f12f180…`）+ `actionlint -shellcheck= -pyflakes= -color .github/workflows/*.yml` | `exit 0`、零输出 | **第 32 轮已复现**（含反向对照，证明工具真在读文件）；**不证明** runner 行为、shell 逻辑、action input 名 |
| 工作流静态审计 | 见 §5 的三份脚本（结构/触发/表达式/引用） | 全部 0 问题 | 结构层的补充证据；**不等于** runner 通过（actionlint 已单独跑，见上一行）|
| 发布门槛干跑 | `$env:PINEGLOW_SNAP=<快照目录>; python docs\release-remediation-evidence-2026-10-05\run-20261006-0110-releasejob-dryrun\release-job-dryrun.py` | `RELEASE-JOB-DRYRUN RESULT=PASS cases=18 failed=0` | 执行 `release.yml` 里真实的 `run:` 正文；**不执行** `actions/*`。快照路径用 `PINEGLOW_SNAP` 覆盖（默认值是当时那个）；成功后自动清理 `work/`，失败时保留供排查 |
| 镜像失败路径干跑 | `$env:PINEGLOW_SNAP=<快照目录>; python docs\release-remediation-evidence-2026-10-05\run-20261006-0140-gitee-mirror-dryrun\gitee-mirror-dryrun.py` | `GITEE-MIRROR-DRYRUN RESULT=PASS failed=0` | 缺 token / 假 token / API 不可达三种情形都不得声称已更新；同样自动清理 `work/` |
| 交付脚本静态解析 | `python D:\松光启动器\docs\release-remediation-evidence-2026-10-05\run-20261006-0520-shipped-scripts-parse\shipped-scripts-parse.py` | `SHIPPED-SCRIPTS-PARSE RESULT=PASS problems=0`（当前 21 PS + 29 JS + 2 PY 全过）| 证明候选里交付的每个脚本都能被**解析**（含 BOM 缺失这类真实踩过的坑）；**不证明**运行期还能用——其中 18 个工具/测试脚本**没有任何门禁在执行**，多数需要更早轮次的证据布局 |
| 候选集合双向核对 | 见 `run-20261006-0250-candidate-set-audit\candidate-set-audit.md` 的命令 | 白名单 146 条全部可 add；无必需输入漏项 | 证明"提交会纳入什么" |
| **运行态守卫（running 只读）** | `powershell -File docs\release-remediation-evidence-2026-10-05\run-20261006-1200-running-guards\verify-running-guards.ps1 -ExePath <EXE> -WorkRoot %TEMP%\pg-guard -JsonPath <证据>\running-guards.json` | 运行中：重复启动/删 Mod/列存档/列备份均 **400**；退出后 5 秒内恢复 200 | 证明守卫**跟着运行态**生效与解除。**注意两点**：① 运行中**创建**备份是允许的（无守卫，观察项）；② 该轮脚本里"400 正文"要读 `$_.ErrorDetails.Message`（PS 5.1 会消费响应流），别误判成"消息丢失" |
| **原版游戏启动/退出（真实 GPU 机器）** | `powershell -File docs\release-remediation-evidence-2026-10-05\run-20261006-1130-vanilla-launch\verify-vanilla-launch.ps1 -ExePath <EXE> -Version 1.18.2 -WorkRoot %TEMP%\pg-launch -JsonPath <证据>\vanilla-launch.json` | launch 返回 `{"ok":true,"pid":…}`；`<gameDir>\logs\latest.log` 出现 LWJGL/Sound engine/atlas；进程窗口标题 `Minecraft 1.18.2` | 证明**启动链路**（命令行/classpath/natives/账号注入/进程派发）与**退出结算**；**不证明**真实微软账号链路、加载器启动、进入世界。用**离线账号**即可跑；产品没有 stop 接口，收尾需外部 kill |
| **文件层事务/恢复**（存档备份恢复、Mod 隔离删除恢复、事务替换回滚）| `powershell -File docs\release-remediation-evidence-2026-10-05\run-20261006-1100-file-transactions\verify-file-transactions-v2.ps1 -ExePath <EXE> -WorkRoot %TEMP%\pg-filetx3 -JsonPath <证据>\file-transactions.json` | `文件层事务/恢复验收判定: PASS（20 项 0 失败）` | 证明存档**非破坏式**恢复（不覆盖在用的）、Mod 删除后可恢复且字节一致、**哈希不匹配的新包被拒且原文件不被破坏**；**不证明**"在游戏里看到效果"与 running 只读保护。注意：恢复会写成 `<world> (1)`，替换校验的是**上传字节的 SHA256**（不是 jar 格式）|
| **整合包导入（真实 .mrpack）** | 先取 Modrinth 官方 `.mrpack`（如 Fabulously Optimized 1.18.2，约 1.2 MB），再 `powershell -File docs\release-remediation-evidence-2026-10-05\run-20261006-1030-modpack-import\verify-modpack-import.ps1 -ExePath <EXE> -MrpackPath <.mrpack> -WorkRoot %TEMP%\pg-modpack -JsonPath <证据>\modpack-import.json` | `整合包导入真实验收判定: PASS（15 项 0 失败）` | 证明真实包的解析/下载/哈希/overrides/加载器 + 原版安装 + **Java 自动安装**；**不证明**进游戏、不覆盖 CurseForge/MCM 两种格式。若客户端脚本中断而服务端仍在跑，改用同目录 `verify-modpack-result.ps1 -Port -ColdDir -MrpackPath` 事后核对 |
| **Java 就绪（真实 JRE）** | `powershell -File docs\release-remediation-evidence-2026-10-05\run-20261006-1010-java-ready\verify-java-ready.ps1 -ExePath <EXE> -WorkRoot %TEMP%\pg-java-ready -JsonPath <证据>\java-ready.json` | `真实 JRE 就绪验证判定: PASS（6 项 0 失败）` | 证明 Adoptium 来源与校验和可用、目录约定正确、**产品能发现**该 JRE、`prelaunch-check` 由缺 Java 转为通过；**不证明**产品自带的 `InstallAsync`（唯一入口是整合包导入）与"真的启动游戏" |
| **原版下载（真实源）** | `powershell -File docs\release-remediation-evidence-2026-10-05\run-20261006-0940-vanilla-download\verify-vanilla-download.ps1 -ExePath <EXE> -VersionId 1.5.2 -WorkRoot %TEMP%\pg-vanilla -JsonPath <证据>\vanilla-152.json` | `原版下载真实验收判定: PASS（10 项 0 失败）` | 证明真实 Mojang 源上能装完并校验（client.jar sha1 一致、assets 逐对象齐全）；**不证明** Java 运行时安装与"用 Java 启动游戏"。注意门槛按**索引去重后的对象**算（官方 `totalSize` 含重复条目）|
| **微软登录（无需真人三段）** | `powershell -File docs\release-remediation-evidence-2026-10-05\run-20261006-0850-ms-login\probe-ms-login.ps1 -ExePath <EXE> -WorkRoot %TEMP%\<空目录> -JsonPath <证据>\ms-login.json` | `微软登录（无需真人部分）判定: PASS（真人步骤仍未验）` | 证明**内置 client_id 仍有效**、设备码端点/scope 契约正确、轮询如实 pending；**不证明**真人完成授权后的链路。会产生 900 秒后过期的待授权码 |
| **R06 外部服务腿**（版本/新闻/源/内容搜索/图片代理）| `powershell -File docs\release-remediation-evidence-2026-10-05\run-20261006-0830-r06-services\probe-r06-services.ps1 -ExePath <EXE> -WorkRoot %TEMP%\<空目录> -JsonPath <证据>\r06-services.json` | 200-OK=11 有响应=14 | 证明版本列表（917）/新闻（12 条真实外链）/源延迟/Modrinth+CurseForge 搜索真的通；**不证明**账号、Java 安装、游戏启动；两条图片代理在 fake-IP 环境**未判定**（见该目录报告 §3）|
| **资产级回退**（`url` 坏 → `fallbackUrl` 救回）| `powershell -File docs\release-remediation-evidence-2026-10-05\run-20261006-0820-asset-fallback\verify-asset-fallback-live.ps1 -ExePath <EXE> -WorkingAssetUrl <GitHub资产> -PrimaryAssetUrl <Gitee资产> -BrokenAssetUrl <真实404> -ExpectedSha256 <sha> -ExpectedSize <bytes> -WorkRoot %TEMP%\<空目录> -JsonPath <证据>\asset-fallback-real.json` | `资产级回退真实验收判定: PASS`（19 项 0 失败）| 证明**资产级回退**、主源 Gitee **匿名可下**、坏链必须失败（反证）；**不证明**"替换自己并重启"，也不证明 Gitee 的**写入** |
| **action 契约**（输入名/必填/运行时/版本存在性）| `& "<DSH内置python>" docs\release-remediation-evidence-2026-10-05\run-20261006-0810-action-contract\check-action-contracts.py --repo D:\松光启动器 --json <证据>\action-contract.json` | `ACTION-CONTRACT RESULT=PASS steps=15 actions=5 findings=0` | 补 actionlint 的盲区（它**不校验** `with` 输入名）；**不证明** runner 行为与 `secrets`/`permissions`。注意用 `git ls-remote` + `raw.githubusercontent` 而非 API（匿名配额 60/小时）|

> 桌面依赖：UI 九页与冷启动需要**真实桌面会话**；CI runner 上不可靠，别把它们塞进 tag 触发的发布流程当硬门槛。

---

## 3. 提交（需授权）

> **推送的边界（第 33 轮实测）**：远程 `main`=`c8aa286`，本地 HEAD=`f145d14`，
> 中间有 **7 个提交未推送**（涉及 39 个文件，全是功能与文档）。推送会**同时公开这 7 个提交**，
> 而且工作流是 `on: push` ⇒ **推送即触发一次真实 CI**（这正是 G14 的解锁动作）。
> 推送前必须确认这批提交是可以公开的。

```powershell
# 1) 逐条精确 add（不要 git add -A / .）
#    清单来源：scripts\source-snapshot-inputs.txt（146 条，去掉注释与空行）
#    脚本化示例（PowerShell）：
$wl = Get-Content scripts\source-snapshot-inputs.txt -Encoding UTF8 |
      Where-Object { $_.Trim() -ne '' -and -not $_.TrimStart().StartsWith('#') } |
      ForEach-Object { $_.Trim() }
$wl | ForEach-Object { git add -- $_ }

# 2) tracked 的改动单独逐个确认（44 个，见 CANDIDATE-COMMIT-PLAN §3 与其枚举命令）
git status --porcelain | Where-Object { $_ -match '^ M' }

# 3) 提交前自检
git diff --cached --stat          # 只应包含上面两类
git ls-files | Measure-Object     # 记录 tracked 数量，便于随后核对
```

**不要**：`git add -A`；把 `docs/**`、`ui/tests` 下未列白名单的探针/备份、
`.testagent/`、`.trae/`、`.test-tmp/`、历史 EXE、真实配置或凭据纳入（完整排除规则见计划 §5）。

---

## 4. 推送与远程 CI（需授权）

1. `git push`（分支）→ 触发 `build.yml`（`push: branches: [main]`、PR、手动）。
2. CI 上必须看到：锁定还原、UI 闭包、测试清单双向一致、Node 全套、C# 全套、VerifyHost 冒烟、
   以及 build 作业新加的**证据产物上传**（`build-verify-evidence`）。
   注意 `setup-node@v4` 与 `setup-dotnet@v4` 的 `global-json-file` 是**本候选首次引入**的用法
   （契约已静态校验通过，见 §2），首次推送就是它们的第一次真跑。
3. **`release.yml` 只由 tag 触发**（`push: tags: ['v*']`）——**不要为了验证而打 tag**（工作流明令禁止）。

---

## 5. 发版（需授权 + 版本定稿）

1. **版本定稿**：必须 **> 公开的 v1.0.0**（**硬要求，不是偏好**：`tag v1.0.0` 已被占用 → f61f44f；
   候选 `csproj <Version>1.0.0</Version>` 而客户端 `currentVersion` 取**程序集版本**（实测 `1.0.0`），
   `release.yml` 又会断言"程序集版本 == tag 版本" ⇒ 按 1.0.0 发只会让所有用户认为"已是最新"）。
   `tag` 形如 `v1.2.3`（`compute version` 依赖 `v` 前缀）；**同时把 `csproj` 的 `<Version>` 改成同一版本**
   （否则本机构建/证据里的版本号与发布产物不一致）。
2. 打 tag → `release.yml`：`verify` 作业先跑（同 SHA 测试互锁）→ `release` 作业 publish（带 `/p:Version=<tag 版本>`）
   → 产物形态断言 → 生成 manifest → 独立冷启动 → 真实更新链 → （可选）Gitee 镜像 → 清单与附件一致性门槛
   → 建 GitHub Release（**含 `LICENSE`**）。
3. **Gitee `update.json` 必须写**：`≤1.0.0` 的旧客户端**只认**这个固定地址、没有备用源；
   不写它们就永远升不了级。写入由 `release.yml` 的镜像步骤完成（需 `GITEE_TOKEN`）；
   缺 token / 上传失败 / 写失败都会**如实落盘**到 `mirror-status.json`，**不要**把"跳过"读成"已更新"。
4. **发布说明必须写**：已知限制（仅 win-x64；WebView2 Runtime 必需）+ 迁移说明
   （`≤1.0.0` 可能需要手动下载一次）。README 的运行环境表已写好，可直接引用。
   迁移说明里**必须**包含下面这条（第 24 轮读已发布 v1.0.0 源码后确认，
   见 `D:\松光启动器\docs\release-remediation-evidence-2026-10-05\run-20261006-0600-v100-migration/`）：

   > 从 ≤1.0.0 升级时，如果你是通过脚本/父进程（stdin 被重定向）启动的启动器，
   > 更新可能长时间停在"正在应用更新"。请手动下载一次新版；新版已修复该等待写法。

   依据：旧客户端的替换脚本用 `timeout /t 1 /nobreak` 且等待循环**没有上界**（修复版已改为 `ping` 等待 + 有界）；
   从资源管理器正常启动的用户不受影响（`timeout` 会立即报错退出、循环空转而仍能完成替换）。
   同轮已确认**清单协议兼容**：模型逐字节相同、解析大小写不敏感、旧端没有更严的校验、默认源就是那个 Gitee 文件
   ⇒ **只要 `update.json` 写成功，旧客户端就能读懂并升级**。

   **还必须写明升级后的一个动作**（第 25 轮实测，见 `run-20261006-0620-upgrade-in-place/`）：
   从**目录式**安装（启动器旁边有 `UI/` 目录，≤1.0.0 的目录式/开发树安装就是这样）升级后，
   **请删除启动器旁的 `UI/` 目录**——新版本自带界面，而解析顺序里"EXE 旁 `UI/`"**优先于**内嵌包，
   不删就会继续用旧界面（"新服务配旧界面"）。诊断：启动日志里 `[LocalApi] 监听 …, ui=<根路径>`，
   `ui=` 指向旧目录即中招。（残留的原生 DLL 已实测无害，不需要清理。）

---

## 6. 发布后（= R06 剩余项，必须真机做）

| 流程 | 怎么看 |
| --- | --- |
| 更新端到端（**最关键**）| **下载/校验/篡改拒绝这一段第 33 轮已用真实发布资产验过**（见 §2），剩下的交接段要在真机上做：跑"退出 → 替换 → 重启"，确认重启后版本与 manifest 一致、旧版本备份还在，**并且既有用户的 `config.json` 没被重置**，设置页也不应出现**来自旧版本**的"上次替换失败"横幅（第 26 轮实测：旧 `apply-state.json` 会被原样继承，见 `run-20261006-0640-stale-apply-state/`）（本地已核过字段是加法式、旧形态加载保留：见 `run-20261006-0500-config-upgrade-audit/`；真机仍要确认一遍）|
| 账号 | 微软设备码 / 取消 / 超时 / 切换 / 续期 / 离线；**无秘密泄漏** |
| Java/版本 | 原版**下载**（第 40 轮）、**Java 就绪**（第 42 轮）、**启动/退出**（第 45 轮：LWJGL/OpenAL/10 图集就绪、窗口标题 `Minecraft 1.18.2`、退出结算 305 秒，离线账号）均已 PASS；此处仍需真机：**加载器（Fabric/Forge/NeoForge）实际启动**与进入世界/存档加载。**running 只读已在第 46 轮用真实运行中的游戏实测**（`run-20261006-1200-running-guards/`），但界面是否展示那些 400 文案未在真实 UI 上核对。<br>发布说明**必须**写清 Java 要求（17/21/25；老版本要 Java 8 而自动下载不支持），并注意普通用户界面**没有"安装 Java"入口**（第 41 轮发现）|
| 下载 | 中断重启、终态进度、取消重试、重复任务、实际文件一致 |
| 整合包/Mod/存档 | **running 只读已于第 46 轮实测**（运行中删 Mod/列存档/列备份/重复启动均 400，退出后解除；运行中创建备份仍允许=观察项）；**Modrinth `.mrpack` 导入已在第 43 轮用真实包验过**（43 mod 逐文件 SHA1、overrides、加载器、原版安装、Java 自动安装全部 PASS）；此处仍需真机：**CurseForge/MCM 格式的真实包**、取消/失败清理、running 只读；**Mod/存档的替换与回滚、实例级备份恢复已在第 44 轮以文件层 PASS**（`run-20261006-1100-file-transactions/`）|
| 新闻/AI/皮肤 | **新闻的真实外链与 `linkOk` 已在第 36 轮用真实上游验过**（`run-20261006-0830-r06-services/`）；此处仍需真机的是**图片渲染**、AI 诊断（需真实 Key）、皮肤渲染（需账号）。<br>**观察项（不再是疑点）**：本机 6 次探测里图标 **4 次 200（1.3–1.8s、image/webp 4800B）/ 2 次 504（恰好 5,046ms = 5 秒连接预算）**，端点本身可用、非缺陷；真机上只需"顺带看一眼图标是否正常显示"，若**经常**缺失再按 `run-20261006-0910-image-proxy-rootcause/` 的复刻实验定位（依据 `run-20261006-0920-icon-flakiness/`）|

> 这些**不能**用 mock/替身/BAT 沙盒替代（工作流明令）；缺条件一律记 NOT_RUN。

---

## 6.5 **发布前必须知道的一条（第 47 轮新增）**

**加载器实例（Fabric/Quilt/Forge/NeoForge）目前启动必崩**：命令行缺 `-Djava.library.path`，
游戏在 LWJGL 初始化时 `UnsatisfiedLinkError: Failed to locate library: lwjgl.dll`。
复现与定位见 `run-20261006-1230-fabric-launch/fabric-launch-BLOCKER.md`（含 9801 字符完整命令行）。
⇒ **在修好之前，不能说"支持整合包/加载器"**；若决定本次照发，发布说明必须写明"含加载器的实例暂无法启动"，
或者干脆不把整合包导入作为卖点。

**修法与验证（第 48 轮已做受控实验）**：拿产品自己生成的命令行，**只插入** `-Djava.library.path=<natives>`，
其余一字不改 → Fabric 1.18.2（135 mod）实例 `Backend library: LWJGL` + `Sound engine started` + **10 个图集**、
**0 崩溃行**、JVM 存活 150 秒（基线：208 行日志后崩）。见 `run-20261006-1300-fabric-fix-sim/fix-simulation-report.md`。
两个修法：① 修版本合并（治根）；② 走 `arguments.jvm` 分支时缺该参数就补（最小改动）。
**根因已收敛到一行**：`VersionResolver.cs` 里 `libraries` 是**追加**、`arguments` 却是**整体替换** ⇒ 父版本 jvm 参数（含库路径）丢失。
**三份补丁已备好（未应用）**：`run-20261006-1310-loader-fix-patch/` —— A 治根（`proposed-fix-versionresolver.diff`，+48/−1）、
B 最小兜底（`proposed-fix-B-launcherservice.diff`，+6）、回归测试（`proposed-test-versionresolver.diff`，新文件 +39）。
**影响面（第 53 轮更正）**：**受影响 = Fabric（实测崩）/ Forge（16 条 jvm 里无库路径）/ NeoForge（15 条里无库路径）**；
**不受影响 = Quilt（**已实测**：49 mod 的 Quilt 包导入后命令行含库路径、12 图集、0 崩溃）/ Vanilla（自带库路径）**。
（第 50 轮曾推断 Quilt 同病，已被 `run-20261006-1500-loader-blast-radius/fallback-check.cs` 的逻辑推演推翻。）
另：Forge **安装**阶段实测到一个**未解释的 HTTP 404**（同一 URL 本机 200；第 52 轮用**记录型代理**进一步排除"镜像改写"——
代理日志显示启动器连的是官方 `maven.minecraftforge.net`，但经转发代理仍 404 ⇒ **仍未定因**）。**在查清之前，Forge 连"装"这一步都过不去。**
**同时确认**：产品的**代理设置在启动时不生效**（`ApiHttp` 静态初始无代理、`LoadConfig` 不刷新；实测启动后首个请求 0 条代理日志，
`POST /api/config` 后才 1 条）——排查网络类问题时**必须先 POST 一次 /api/config**，否则代理配置形同虚设。
**补丁 A 的合并算法已单独验证**（`run-20261006-1330-merge-algo-check/`：`MERGE-CHECK RESULT=PASS`）。
**修完的回归清单**：全量 903 测试 → Vanilla 启动 → Fabric 启动（命令行须含 `-Djava.library.path`）→ 因改源码 ⇒ **产物字节变 ⇒ 全量验证链重跑**（既有 EXE sha256 证据作废）；新增测试文件要加进 `scripts/source-snapshot-inputs.txt`。

---

> **📌 先看决策包**：`release-remediation-evidence-2026-10-05/run-20261006-1600-decision-package/DECISION-PACKAGE.md`
> —— 一页含：发布范围（A1/A2/A3）、加载器阻断项的三条路线（B1 治根 / B2 最小兜底 / B3 不修）、13 项产品决定与建议、三项授权（D1/D2/D3）。

## 6.6 已补验的两格（第 56 轮）

- **「进入世界 / 加载存档」= PASS（不用 GUI）**：官方 1.20.1 服务端生成真实存档（`server.jar` sha1 与官方一致）
  → 放进 `<gameDir>/saves/` → 实例 `customGameArgs = --quickPlaySingleplayer <世界名>` → 启动；
  日志出现 `Starting integrated minecraft server` / `Preparing spawn area` / **`<玩家> joined the game`** / 12 图集 / 0 崩溃，
  退出后 `totalPlaySeconds` 结算。复现脚本：`run-20261006-1630-world-entry/`。
  ⚠️ 注意：`POST /api/versions/download` 是**异步排队**（`{"status":"queued"}`），必须轮询 `/api/downloads` 到终态；
  该端点返回的是 **`{"tasks":[…]}`（对象）**，不是数组。

---

### 6.6.1 多实例并发 = PASS（第 57 轮）

两个**不同**实例可同时运行（实测两个 JVM：31644 / 24084，60 秒全程存活）；**同一实例**重复启动被 400
（`该实例已在运行（进程 …），请勿重复启动。`）；杀掉后守卫解除。⇒ 守卫语义是**按实例**、不是全局单例。
复现：`run-20261006-1700-multi-instance/`。⚠️ 未验：更多实例的压测、多实例共享同一 `saves/`/`mods/` 的并发写一致性。

---

### 6.7 导入"取消"的实测结论（第 58 轮）

- **取消先到（tombstone）**：先取消、再导入 → **409 `导入已取消，未生成实例。`、0 秒** ✅。
- **导入中取消**：取消**被接受**，但 ① 工作继续（目录 51.6 → 364.7 MB）；② **半成品目录没清**（364.7 MB / 2,805 文件 / 7 个 `.download`）；
  ③ **临时上传目录没清**；④ 客户端拿到 **"连接被关闭"**而不是文档里的 409（224 秒后）。
  代码里两处清理都在，但 **API 路径的 `_log` 是空委托** ⇒ 连"需要手动清理"的提示都无处可去。
  ⇒ 排查/写发布说明时**不要**承诺"取消会清理干净"；依据：`run-20261006-1730-import-cancel/`。

---

### 6.8 更新链实测结论（第 59 轮）

- **走通**：`check`（HTTP 清单也被接受）→ 下载（**后台任务，必须轮询 `GET /api/update` 到 `completed`**）→
  **EXE 被替换**（sha256 与清单逐字相同）→ 原进程退出 → **触发重启**；`apply-state.json` 记为 `applied`。
- **重要旁证**：**已发布的 v1.0.0 资产"只交付 EXE"时启动即崩**（`SkiaSharp` 原生库 `DllNotFoundException`）；
  候选 EXE 在同条件下正常启动，且把候选自解压出的 `libSkiaSharp.dll`/`libHarfBuzzSharp.dll`/`av_libglesv2.dll`
  放到 v1.0.0 旁边它就能启动 ⇒ **原始评审 G07/R01 被独立复现，候选打包修复有效**。
  ⇒ 发布说明里"更新"一节要提醒 **v1.0.0 用户必须更新**（他们手上那个 EXE 单独放是起不来的）。
- **加固项**：清单地址目前允许 **HTTP**（只有产物强制 HTTPS）⇒ 中间人可同时改产物地址与 SHA256，建议收紧。
- **重启后新进程提供服务：已验（第 60 轮）** —— `apply` 后 2 秒产品日志出现 `=== Pineglow Launcher 启动 v1.0.0 ===` 与 `[LocalApi] 监听 …`，
  脚本侧在同一端口拿到 `/api/health` 200（该轮把候选自解压出的 3 个原生库预置到目标目录，因为测试用的 v1.0.0 产物缺它们；真实更新不需要）。
- 仍未验：Gitee 固定 `update.json` 源（需 `GITEE_TOKEN`）。

---

### 6.9 **Forge/NeoForge 安装器 URL 多一段（第 61 轮定位，blocker）**

`InstallerLoaderService.GetInstallerUrl`：`…/{_groupPath}/{_artifactId}/{mavenVersion}/…`，而 `_groupPath` **已含 artifact 段**
⇒ 产品请求 `…/net/minecraftforge/forge/**forge**/1.20.1-47.4.3/…` = **404**（正确 URL 200）。
**当前版本 Forge/NeoForge 实例无法安装**。一行修法已备（未应用，已验证：正确 URL 下产品自己的下载器实测 6,068,826 B 成功）。
⚠️ 与 §6.5 的启动缺陷**是两个独立问题**，两个都修完 Forge/NeoForge 才真正可用。

---

### 6.10 内容子系统 = PASS（第 62 轮）

`GET /api/content/search`（mod / modpack / resourcepack / shader / curseforge 五类）全部返回真实数据；
`POST /api/content/install` 真实落盘（Sodium 1.18.2 的 1.3 MB jar）；`GET /api/content/tasks` 报 `completed/安装完成`。
复现：`run-20261006-1930-content-subsystem/`。
⚠️ 未定项：便携目录**没有** CurseForge Key 却拿到 CF 数据（直连无 key = 403），**未定因、不下结论**。

---

### 6.11 镜像源模式 = PASS（第 63 轮）

`POST /api/sources/select {"key":"bmclapi"}`（**注意：改下载源必须用这个端点**；`POST /api/config` 不处理 `downloadSource`，
却返回 `{"ok":true}`）→ 装 1.5.2 **completed**、`client.jar` sha1 与官方一致、assets 468 个、流量确实走镜像池。
⚠️ 镜像模式下仍有少量官方主机请求（库/元数据）⇒ 发布说明别写"完全不走官方"。复现：`run-20261006-2000-mirror-mode/`。

---

### 6.12 **全新安装默认实例不可寻址（第 65 轮定位，blocker）**

无 `config.json`（或实例列表为空）时，`ConfigStore.LoadAsync()` 在内存里造默认实例**却不落盘**，而 `GameInstance.Id` 是新 GUID
⇒ 每次 `/api/state` 的默认实例 id 都不同，`/launch`、`/prelaunch-check` 全部 **400 实例不存在**；有操作落盘后立刻恢复正常。
界面启动只做 GET ⇒ **"全新安装直接点启动"必然失败**。修法一行级（造完默认实例后落盘一次），与另两个 blocker 一起修最划算。

### 6.13 从列表移除 ↔ 挂回 = PASS（第 64/65 轮）

`POST /api/instances/{id}/delete` **只改配置**（返回 `{"ok":true,"removedId":…}`，游戏文件完好）；`register` 重复目录/不存在目录都是 **400** 明确文案。
⚠️ 注意：移除最后一个实例后会自动重建一个默认实例（§6.12 的同一条根因），测试期望要建立在"已落盘"的前提上。

---

### 6.14 服务器列表 + ping = PASS（第 67 轮）

添加/列出/ping/删除 全通；ping 走**真实 Minecraft 状态协议**（公网服务器实测 `可连接 · 2278 ms` + 真实 MOTD；未监听端口 `无法连接`，2 秒返回）；
**重启后列表仍在**（持久化）。另：配置写入点静态审计 27/27 都落在会 `SaveConfig` 的路径上 ⇒ 无"改了却不落盘"。
复现：`run-20261006-2200-servers/`。

---

### 6.15 Mod 二分定位 = PASS（第 68 轮）

植入 8 个假 jar（元凶 `mod5.jar`）→ `start` 禁用一半 → 按语义回报 `crashed` → **3 轮**（log₂8）定位到元凶；
`finished`/`giveup` 后**文件全部恢复**（`.jar=8 / .jar.disabled=0`）；**过期 step 被拒**（409）。复现：`run-20261006-2230-mod-bisect/`。

---

### 6.16 资源包 / 光影 安装 = PASS（第 69 轮）

`category=resourcepack` 装进 `resourcepacks/`（Faithful 32x，10.8 MB）、`category=shader` 装进 `shaderpacks/`（Complementary Reimagined，553 KB）；
任务都 `completed/安装完成`；`GET /api/instances/{id}/resourcepacks|shaderpacks` 与 `state` 计数一致。
⇒ mod / resourcepack / shader 三个内容分类**都有真机证据**。复现：`run-20261006-2300-resourcepacks/`。

---

### 6.17 第三方账号（authlib-injector / Yggdrasil）= 失败路径 PASS（第 70 轮）

`not-a-url` → 400 明确文案；非 Yggdrasil 站点 → 400（**但会透传整段 HTML**）；**真实 LittleSkin + 假凭据** → 400 且透传服务端 `errorMessage`；
失败后账号数不变、启动器健康。authlib-injector 组件两个下载 URL 均 **200 / 341,970 B**（官方 + BMCLAPI），但**只校验大小、无 SHA256**。
⚠️ 未验：真实第三方账号**成功登录**、皮肤/披风、启动时 `-javaagent` 实际注入。复现：`run-20261006-2330-thirdparty-auth/`。

---

### 6.18 Java 探测与恢复区 = PASS（第 71 轮）

`GET /api/java`：`JAVA_HOME` 指向真实 JRE 17 时能发现并判有效（`major=17 vendor=Eclipse Adoptium`），`javaPath` 正确回显。
恢复区：Mod 移入隔离 → `GET /api/instances/{id}/recovery` 出现记录（`reason=mod-delete`、`status=quarantined`、`expiresAt` 30 天）
→ `POST …/recovery/{operationId}/restore` 还原（`overwrite:false, conflicts:0`，文件回到 `mods/`）。复现：`run-20261006-2350-java-recovery/`。

---

### 6.19 全量验证链复跑 = GREEN（第 73 轮）

`scripts/verify-source-snapshot.ps1` 复跑：**模式 GREEN、失败项 0**；Node 304/304、C# 903/903、独立冷启动 **19/19**；
重新 publish 的单文件 EXE **逐字节一致**（53,052,722 B / `5009cf73…`）⇒ 冻结候选未被本轮测试破坏且可复现。
⚠️ 唯一报错是可选 `-CleanupSnapshot` 撞上 `VBCSCompiler` 占用（非验证失败）；`%TEMP%` 里历史残留快照 **12.15 GB** 已清理。
复现：`run-20261006-2400-final-refresh/`。

---

### 6.20 三个 blocker 的补丁已在"打过补丁的构建"里验证（第 74 轮）

在 `%TEMP%` 的**仓库副本**里 `git apply` 四个补丁（**不动工作区**）→ 构建 **0 警告 0 错误** →
复现测试 **4/4**：B0（`/api/state` 三次 id 相同；`/launch` 不再是"实例不存在"）、B1（合并后保留 `-Djava.library.path=${natives_directory}`）、
B2（安装器 URL 单段且 **200 / 6,068,826 B**）；并且**副本的 C# 全量测试 904/904 全绿**（903 + 新增回归测试）。
⇒ "补丁有效"已从"逻辑/局部验证"升级为"**打过补丁的构建上实测**"。复现：`run-20261006-2500-patched-build/`。
**剩余 9 步清单项**：打过补丁的 Node 304、Vanilla/Fabric/Quilt 三种真实启动、全量验证链 + 新 sha256（这些要在**批准后**于工作区正式做）。

---

### 6.21 B1 的端到端证据（第 75 轮）

同一个真实 Fabric 包：**候选约 10 秒崩溃**（`Failed to locate library: lwjgl.dll`），而**打过补丁的构建**命令行含 `-Djava.library.path`、
Fabric Loader 加载、**LWJGL + 声音 + 10 个图集、0 崩溃**（7/7）⇒ B1 从"代码级验证"升级为"**端到端验证**"。
剩余（批准后在工作区正式做）：打过补丁的 Node 304、**Vanilla/Quilt 启动回归**、Forge 包不再 404、全量验证链 + 新 sha256。
复现：`run-20261006-2600-patched-fabric-launch/`。

---

### 6.22 补丁构建的 Vanilla / Quilt 回归（第 76 轮）

**Vanilla 1.18.2**：命令行含 `-Djava.library.path` + `-Dminecraft.launcher.brand`，`LWJGL/声音/10 图集/0 崩溃`（6/6）；
**Quilt**（真实包）：命令行 18,352 字符含库路径，`LWJGL/声音/12 图集/0 崩溃`。⇒ 合并修复没弄坏这两条路径。
⚠️ 两个**我自己的**坑（产品行为正确）：① 删掉补丁副本 `data/` 会连带删掉自动装的 Java ⇒ `prelaunch` 正确报"未找到可用的 Java 运行时"；
② **装完版本不会自动把默认实例绑到该版本**（要显式 `POST /api/instances {name,version}`）。
剩余：打过补丁的 Node 304、Forge 包不再 404、全量验证链 + 新 sha256。复现：`run-20261006-2700-patched-regressions/`。

---

### 6.23 Forge 安装的**第二半**缺陷：缺 `launcher_profiles.json`（第 77 轮）

在**打过补丁的构建**上导入真实 Forge 包：**404 消失**（⇒ B2 的 URL 修复有效），但报 `Forge installer 失败，ExitCode=1`。
按产品相同的 Java/参数手工复现，安装器输出 `There is no minecraft launcher profile in "<gameDir>", you need to run the launcher first!`；
**写一个 46 字节最小 `launcher_profiles.json` 后 exit 0**（`Successfully installed client into launcher.`，落盘 `versions/1.20.1-forge-47.4.3` + 24 个库文件）。
⇒ 修 `RunInstallerAsync`（启动安装器前写最小桩；补丁 `proposed-fix-forge-launcher-profile.diff`，已通过 `git apply --check`）。
⚠️ 另外：安装器输出被产品捕获却因 **API 导入路径 `_log` 是空委托**而丢弃 ⇒ 用户只看到 `ExitCode=1`（与决定项 14① 同源）。

---

### 6.24 Forge 端到端：**本机验不了**（第 78 轮）

双补丁（B2 URL + B2b profile 桩）构建导入真实 Forge 包**仍 ExitCode=1**（441 秒，比只有 B2 时的 197 秒做得更多）；
两组对照（ASCII 路径 / **中文+空格**路径，都带最小 `launcher_profiles.json`）**报错完全相同**：
`javax.net.ssl.SSLHandshakeException: Remote host terminated the handshake` ⇒ **安装器自身的 HTTPS 下载在本机网络环境下失败**。
⇒ 结论分三层：**B2 是产品缺陷且已修好**（404 消失）；**B2b 是产品缺陷**（安装器明说缺 launcher profile，写桩后可越过）；
**"Forge 装得上"本机无法验证**（环境）。发布说明写"当前版本无法安装 Forge/NeoForge"。

---

### 6.25 打过 6 个补丁的候选：门槛逐步 PASS（第 80 轮）

在"复制仓库 + 应用 6 个补丁 + 构建"的副本上跑 `verify-source-snapshot.ps1`：**11 个可执行步骤全 PASS**（Node 304、**C# 904**、publish、冷启动 19/19）。
汇总里的"失败项 2"是**副本构造的产物**：① 未跟踪历史遗留 `scripts/run-dify-minimax-workflow.ps1`（非 ASCII 且无 BOM）被 `git add -A` 纳入；
② 快照文件数期望值按副本自身计算（512）而实际 363。**与补丁无关**（工作区正式跑是 GREEN/0）。
顺带确认：工作区里确有**未跟踪的历史遗留**（决定项 6 的实例），但它**不在候选白名单**里。复现：`run-20261006-3000-patched-final-gate/`。

---

## 7. 中止条件（出现任一条就**不要**发布）

1. §1 全量验证不是 `PASS`，或失败项 ≠ 0；
2. 单文件 EXE 的 sha256 ≠ `5009cf73d0801e83…`（说明源码或配方变了，既有证据全部作废，必须重跑 §1/§2）；
3. manifest 的 version/url/sha256/size 与最终附件不一致（发布流程里已有门槛，别绕过）；
4. Gitee `update.json` 未成功写入，却打算宣称"旧客户端可升级"；
5. 白名单漏文件（快照文件数 ≠ 307 或 `whitelistMissing` 非空）、或含非 ASCII 的脚本缺 BOM；
6. Node/C# 通过数低于基线（304 / 903）而无解释；
7. UI 九页出现 `uiAssetFailures` 或 `productLocalFailures`（那是产品问题，不是外部依赖）；
8. 你要宣称"CI 已验证"时：该仓库的 `Build`/`Release` 在真 runner 上**有成功历史**（v1.0.0 发布成功），
   但**本轮新增/改写的步骤从未在真 runner 上执行过**（证据 `run-20261006-0750-remote-truth/`）；
   首次推送跑完之前，不要把本地绿灯当作 CI 已验证。

---

## 8. 脚本速查（谁证明什么 / 不证明什么）

| 脚本 | 证明 | 不证明 |
| --- | --- | --- |
| `verify-source-snapshot.ps1` | 候选集合可构建、测试全绿、发布可复现、单文件冷启动自足 | 真实账号/游戏/更新端到端、远程 CI、actionlint |
| `verify-single-file-standalone.ps1` | 只复制 EXE 到新目录能起（19 项），且**不继承**开发旋钮 | UI 九页的交互、外部服务 |
| `verify-ui-pages-live.cjs` | 真实 WebView2 里九页渲染与图片加载 | 外部服务可用性、账号/游戏流程 |
| `verify-update-live-download.ps1` | 默认源降级、真实 HTTPS 下载与 SHA256、篡改拒绝 | 真实应用替换自己并重启 |
| `run-inventory-tests.cjs` | 测试清单与磁盘双向一致 | 测试质量本身 |
| `check-ui-resource-closure.cjs` | HTML/CSS/JS 本地引用闭包完整 | 界面运行时是否真的渲染出来 |
| 三份审计（引用/触发/表达式） | 工作流结构层的事实 | actionlint、runner 行为、表达式语义 |
| 两份干跑（发布门槛/镜像失败） | `run:` 正文的逻辑会咬 | `actions/*`、runner 环境 |

---

## 9. 做完之后

- 把每步的证据目录路径写进交付说明（本手册 §1–§2 的产出）；
- 冷 checkout 复验同 SHA（工作流 §7.3）——**这一步需要提交/推送授权**；
- 只有技术验收与放行**都**满足，才对外发布。
- **核对结论文档里的数字**：`release-readiness-reassessment-2026-10-06.md` 是决策依据，里面每个数字都应指回某一轮证据；工作推进后数字会过时（本会话第 23 轮就查出 5 处），复核时以证据文件为准、不要以文档叙述为准。
