# 松光启动器发布就绪复评（对 2026-10-05 原始评估的逐条复评）

日期：2026-10-06，Asia/Hong_Kong。复评者：本轮执行 AI（原始评估者：Codex，2026-10-05）。
依据：`release-readiness-review-2026-10-05.md`、`release-readiness-remediation-workflow-2026-10-05.md`
与两边全部证据。

> **📄 一页版答卷（直接回答原问题）**：同目录 `FINAL-ANSWER-2026-10-06.md`。
> **🗂 证据索引（104 个目录的唯一入口）**：`release-remediation-evidence-2026-10-05/INDEX.md`。
> **📝 交付物自评（五轴 4.2/5，含短板与三条改进）**：`release-remediation-evidence-2026-10-05/run-20261006-3100-approval-runbook/self-evaluation.md`。

**结论（第 80 轮刷新）：仍然不能作为正式新版本发布 —— 而且"性质"又变回来了：**这一次**确实还有代码要写**。**

TechnicalAcceptance=**NOT_PASSED**；ReleaseApproval=**NOT_GRANTED**（未获授权）。

**两件事都成立，别只看其中一件：**

1. **原始 6 类阻断（R01–R06）**：R01/R02/R04/R05 **已关闭且各配可复跑门槛**；R03 的**客户端侧**已关闭（含真实端点复验）；
   R06 已完成**大量**真机验收（安装/启动/退出/进世界/多实例/文件事务/运行守卫/更新全链/内容三分类安装/镜像源/整合包导入与取消/服务器与 ping/Mod 二分/第三方账号失败路径/Java 与恢复区 —— 见 §7 索引与进展报告 §3）。
   **R06 只剩**：真实微软账号**成功登录**及其分支（需真人账号）、Gitee 固定 `update.json`（需 `GITEE_TOKEN`）、AI 诊断（需第三方 Key）。
2. **但本会话在"加载器/首次运行"路径上发现了 4 个产品缺陷**（原始评估与原始整改都没覆盖到，是这一轮真机验收挖出来的）：

| # | 缺陷 | 影响 | 修法 | 验证状态 |
| --- | --- | --- | --- | --- |
| **B0** | 默认实例/账号**不落盘**（`ConfigStore.LoadAsync` 造完不保存）| **全新安装直接点启动报"实例不存在"**；移除最后一个实例后同样触发 | +6 行（两处）| 打过补丁的构建：`/api/state` 三次 id 相同、`/launch` 不再是"实例不存在" |
| **B1** | 加载器实例命令行**缺 `-Djava.library.path`**（`VersionResolver` 对 `arguments` 整体替换）| **Fabric/Forge/NeoForge 实例启动必崩**（Quilt/Vanilla 不受影响）| +48/−1（合并）或 +6（兜底）+ 回归测试 +39 | **端到端**：同一真实包，候选 ~10 秒崩，补丁构建进渲染管线（LWJGL/声音/10 图集/0 崩溃）；Vanilla/Quilt 回归也 PASS |
| **B2** | Forge/NeoForge 安装器 **URL 多一段** ⇒ 404 | **任何机器都装不上 Forge/NeoForge** | −1/+1 | 修后 URL 200；补丁构建上 404 消失 |
| **B2b** | 跑安装器前**没有 `launcher_profiles.json`** | 安装器 `ExitCode=1`（"you need to run the launcher first"）⇒ **URL 修好仍装不上** | +7 行（写最小桩）| 手工对照：写桩后同一安装器 exit 0；⚠️ 端到端受**本机网络**限制（安装器自身 HTTPS 握手失败） |

⇒ **6 份补丁全部 `git apply --check` ✅**，且**在打过补丁的构建上验证过**：构建 0 警告 0 错误、**C# 904/904**、三种启动端到端、**整条全量门槛 11 步全 PASS**。
详见 §5 的 B0–B2b 与 `run-20261006-2130-patch-set/PATCH-SET.md`、`run-20261006-1600-decision-package/DECISION-PACKAGE.md`。

> ### ⚠️ 第 29 轮补上的决定性前提：候选范围远超"整改"
>
> **别把上面的 B0–B2b 当成"全部问题"**：它们只是**加载器/首次运行**这条线上的缺陷；**候选范围本身远超"整改"**。
> 实测（第 31 轮重算）：候选相对已发布版本是 **61 项 / +13,222 行**（另有 146 个白名单新文件；原写 48 文件 / +11,954，已更正）
> 其中 **≥9,359 行（≥70.8%）属于"评估开始前就存在的功能/文档工作"**（= 已提交的 8 个功能提交 +4,135 ＋ 自 2026-10-05 评估以来哈希未变的 31 个脏文件 +5,224；原写 6,201 行 / 52%，已更正）。主要来源：AI 诊断 +930、新闻 +614、前端 `launcher.js` +2531、
> 整合包 +204、Mod 二分 +315 等，**这些在本轮评估里从未被复核**。
> 更要紧的是**两个域无法切分**：`LocalApiService.cs` 里既有功能代码（`NewsImageProxyService`、`/skin-image` 令牌校验）
> 也有整改代码（`ResolveUiRoot`/R01 内嵌包），`ui/js/launcher.js` 同理。
> 因此**发这个候选 = 连同那 ≥9,400 行未复核的功能代码一起发**，而 R06 的 NOT_RUN 项**恰好就是这些功能的真机验收**。
> 反证也要一起说：这些功能**有单元测试**（5,224 行未动脏改动中 5,095 行所在文件被 93 个已执行测试文件按名引用），
> 所以准确说法是"**单元测试绿、真机验收 NOT_RUN**"，不是"没测过的代码"。
> 依据：`D:\松光启动器\docs\release-remediation-evidence-2026-10-05\run-20261006-0700-scope-warning\`；
> **数字更正与三段分解**：`...\run-20261006-0730-scope-recount\`（第 31 轮；上一轮的 48/+11,954/52% 偏小）。
> 这让发布决定从"R01–R06 关不关"变成**三个**问题——见 §5 第 0 项。

本轮（2026-10-06）**未**提交、未推送、未打 tag、未发布、未改真实配置、未使用任何真实凭据。
`git status`：44 个已修改 tracked + 146 条白名单 = 307 冻结输入；`staged=0`。
远程：`origin` = `github.com/zhangtanze1359/Pineglow-Launcher`，远程 `main`=`c8aa286`，本地 HEAD=`f145d14`
⇒ **7 个提交未推送**（第 33 轮 `git ls-remote` 实测）。

## 0. 两份同名相近目录的区分（此前把我自己坑过一次）

| 目录 | 位置 | 内容 |
| --- | --- | --- |
| `release-**readiness**-evidence-2026-10-05/` | **本 DeliveryRoot**（e423 工作树 docs）| 原始评估的现场证据（actionlint、exe-only 崩溃、snapshot-run 等）|
| `release-**remediation**-evidence-2026-10-05/` | **SourceRoot 的 docs/**（D:\松光启动器\docs\）| 本轮复评的全部证据与进度报告 |

两者名字只差一个词。我第 11 轮曾因**只翻 SourceRoot** 而误判"原始 RED 没有留存"，
第 17 轮在 DeliveryRoot 里找到了它们（见 §2 的 G07/G11 与 §6）。

## 1. 基线与现场

| 角色 | 原始评估（10-05） | 本次复评（10-06） |
| --- | --- | --- |
| SourceRoot | D:\松光启动器，HEAD f145d14 + 脏源码 | 同（HEAD 未变），44 M + 146 白名单 = **307** |
| DeliveryRoot | e423 工作树 docs | 同（本文件即写在此） |
| 冻结输入 | 161 tracked + 131 白名单 = 292 | 161 tracked + **146** 白名单 = **307** |
| 未跟踪条目 | 1425 | 302 个顶层条目（其中 146 条为白名单） |

冻结输入从 292 增至 307 的 15 项 = 本轮纳入的 2 个视觉资源 + 12 个工具/测试 + 1 个九页验收脚本
（增量 diff 见复评证据 `run-20261006-2330`…`run-20261006-0220` 系列的 `snapshot-manifest.sha256.txt`；
其中 2118→2330 一轮为 +1 项，其余为既有）。

## 2. 对原评估 G01–G14 的逐条复评

| ID | 原始结论 | 本次复评 | 证据 |
| --- | --- | --- | --- |
| G01 当前源码冷副本 | LIMITED（漏最新视觉资源）| **PASS** | 307 项、泄漏 0、无 bin/obj；R02 已闭 |
| G02 非 RID 锁定还原 | PASS | **PASS** | launcher/tests 两份 `--locked-mode` exit 0；publish 污染后已复原为 8663 B/`fbbc9ae6…` 与基线逐字节一致 |
| G03 C# 全量 | PASS（870）| **PASS（903）** | **11 轮**整轮验证（2118 起）均 0 failed/skipped |
| G04 Node 全量 | PASS（21 文件/269）| **PASS（23 文件/304）** | 清单驱动；双向一致性校验通过 |
| G05 Actions 结构（actionlint 1.7.7）| PASS | **PASS（第 32 轮已本地复现）** | 经 `ghproxy.net`/`gh-proxy.com` 两镜像下载，2,229,473 B 与 SHA256 `7f12f180…` **双镜像一致**、并与**官方校验表**中 `windows_amd64.zip` 一行相同（校验表又被独立锚点印证）；对当前两份工作流 **exit 0、零输出**；**反向对照**（注入截断表达式/残缺表达式）均 `exit 1`，证明工具确实在读文件 ⇒ 与原始评估结论一致，NOT_REPRODUCED 消除。边界：不覆盖 runner/shell/action input（`run-20261006-0740-actionlint/`）|
| G06 Release 参数打包 | PASS（仅构建，45,828,872 B）| **PASS（含真机）** | 本轮按原样执行带 `/p:Version` 的发布配方，并用独立脚本冷启动验收通过（当时 18 项） |
| G07 实际下载范围冷启动 | **FAIL / product**（DllNotFoundException）| **PASS** | 独立冷启动 **19/19**（第 18 轮起加了"不继承开发旋钮"一项），共 11 轮 + 1 次发布配方产物；原始 RED 见 `exe-only-crash.log`（`SkiaSharp.SKImageInfo` 的 `DllNotFoundException`）|
| G08 当前视觉资源交付 | **FAIL / delivery** | **PASS** | 资源入白名单 + 闭包门槛 9 引用/缺失 0 + 真实窗口截图确认主视觉渲染 |
| G09 默认更新入口 | **FAIL / external endpoint** | **PARTIAL**（真实链路已 PASS；仅差 Gitee 写入）| 真实端点：主源 Gitee 固定 `update.json` 实测 404 → **降级到真实 GitHub Release `manifest.json` 成功取到清单**；但 Gitee `update.json` 本身**未写入**（需发布授权）⇒ ≤1.0.0 旧客户端仍升不了级。**第 33 轮用已验证候选 EXE 复跑整条真实链路：阶段 A（主源 404 → 真实备用源降级成功）/B（真实 HTTPS 下载 43,989,943 B，字节与 SHA256 与真实资产一致）/C（篡改清单被拒且无残留）全部 PASS**（`run-20261006-0750-remote-truth/`）；**第 35 轮补上客户端侧最后一条没验过的链接——资产级回退**（`url` 坏链 → `fallbackUrl` 真实资产，PASS；反证"两个都坏必须失败"也成立），并实测 **Gitee 附件匿名可下**（43,989,943 B，与 GitHub 资产字节相同）：`run-20261006-0820-asset-fallback/`|**第 24 轮用本地 `v1.0.0` tag 读了已发布客户端源码**：清单模型逐字节相同、解析大小写不敏感、旧端无更严校验、其默认源就是该文件、下载会按 `Url→FallbackUrl` 回退 ⇒ 写入动作有依据；**同时发现旧客户端替换脚本带 `timeout` 无上界缺陷，须写进发布说明** |
| G10 CI/Release 覆盖 | **FAIL / release process** | **PASS** | 清单 23 文件（原 11）；`release: needs: verify`；4 项本地工作流检查；另有 18 例发布门槛干跑 |
| G11 快照验证器可信 | **FAIL / verification tooling** | **PASS** | 步骤运行器 17/17（SR-01…SR-09，含日志缺失/零输出/零测试/缺证据必须失败）+ 编码守卫；原始误判现场见 `snapshot-run.log` 8 条 `exit=0 status=PASS 0.0s` |
| G12 最终完整包 GUI/游戏 | NOT_RUN | **PARTIAL** | 首启 PASS 19/19；**九页界面运行时 9/9**（真实 WebView2+CDP，2 次通过 / 1 次非通过已定位）；账号、Java/原版/加载器、真实 Minecraft、整合包、Mod/存档恢复、新闻/AI/皮肤真外链 **仍 NOT_RUN** |
| G13 旧版升级与恢复 | LIMITED / NOT_RUN | **PARTIAL**（含配置存活） | 替换脚本 7 项真实验收（含"无权限"两种形态与代码页 437）；真实 HTTPS 下载+SHA256+篡改拒绝实测；镜像失败路径 14 断言；**既有用户配置升级后不被重置**（第 21 轮核过，见 §7）；**旧客户端（v1.0.0）的清单兼容与替换脚本缺陷已用已发布源码核对**（第 24 轮，见 §7）——后者须进发布说明；**原地升级的外置 `UI/` 隐患已实测确认**（第 25 轮：解析顺序里 EXE 旁 `UI/` 优先于内嵌包 ⇒ 目录式老安装升级后会继续用旧界面；残留原生 DLL 已实测无碍）；**旧版本的 `apply-state.json` 会被新版原样继承**（第 26 轮实测；仅影响失败提示的显示）；**"下载→真实应用替换自己→重启"端到端仍 NOT_RUN** |
| G14 干净候选提交/远程 CI | NOT_RUN | **PARTIAL** | 候选集合双向核过数（146 条全部可 add、无必需输入漏项）；**提交/推送/远程 CI 仍未做**（需授权）|

## 3. R01–R06 收口状态

| 阻断项 | 状态 | 关键证据 | 残余/边界 |
| --- | --- | --- | --- |
| **R01** 交付范围不完整 | **已关闭（核心）** | 单文件自解包自足；只复制 EXE 到全新中文/空格目录冷启动 19/19（并显式清除继承的 `PINEGLOW_*`，见 §6）；发布配方（带 `/p:Version`）独立复验通过且**版本键控解包在非 1.0.0 下正确**（`9.9.9-5f6b42a61753`）| 本轮另补：**许可证随 Release 交付**、README 运行环境声明（原流程步骤 3 漏做）；**"旧版迁移"含既有用户配置存活：已核为加法式字段变更 + 现有测试覆盖旧形态加载（第 21 轮）**；`ui/assets/*` 被忽略却会被嵌入 glob 带上（见 §5 决策项）|
| **R02** 视觉资源漏进候选 | **已关闭** | 两资源入白名单；闭包门槛 9/0；门槛自证 7/7（含删 CSS 间接依赖的 RED）；GUI 截图确认 | 依赖 `.gitignore` 第 37 行 `!ui/assets/pine-forest.svg`（删了就退回原缺陷）；这是本轮才写下来的前提 |
| **R03** 默认更新入口 404 | **客户端侧已关闭；线上写入待授权** | 专项测试 18 项；**真实端点**：主源 404 → 真实 GitHub `manifest.json` 降级成功；真实 HTTPS 资产下载+SHA256、篡改拒绝实测；发布侧清单一致性门槛经 18 例干跑证明会咬；镜像失败路径 14 断言 | Gitee `update.json` **未写入**（旧客户端仍无法升级）；**新增观察项**：`/api/versions/remote` 30s 硬超时被呈现为 HTTP 500 + 界面"暂无可下载版本"（既有行为，需决策是否修）|
| **R04** CI 覆盖与发布互锁 | **已关闭** | 清单 23 文件双向断言；`release: needs: verify`；**VerifyHost 冒烟真跑且断言生产凭据调用 0 次**（直接回应原评估 §4 的要求）| 远程 CI 未跑（需授权）；actionlint 我这侧不可得 |
| **R05** 验证器 PASS 失真 | **已关闭** | `StepRunner.psm1` Unicode 安全 + 事实/门槛分离；SR-01…SR-09 17 项；脚本编码守卫 | 原始误判现场在 DeliveryRoot 留档；本轮另补机制复现 |
| **R06** 最终真机验收 | **三段已验，其余 NOT_RUN** | 首启 19/19；替换脚本 7 项（含**代码页 437**验证与"无权限"两种形态）；九页界面运行时 9/9×2（三次运行 news 均 12/12 缩略图；content 为 20/22、21/21、22/22——各次不同）；真实 HTTPS 下载/篡改拒绝 | 账号、Java/原版/加载器、真实 Minecraft、整合包、下载中断重试、Mod/存档恢复、新闻/AI/皮肤真外链、更新端到端 —— **缺条件一律 NOT_RUN**（§6 明令不得用 mock/替身替代）|

### 3.1 旧版迁移全貌（第 21/24/25/26/27 轮汇总；全部有源码级依据）

| 维度 | 状态 | 依据 |
| --- | --- | --- |
| 配置**位置**（`%APPDATA%\MCMiniLauncher` / portable `<EXE>/data`）| ✅ 与 v1.0.0 同句 | 第 27 轮 |
| 配置**字段** | ✅ 加法式新增两个布尔；旧形态加载保留、写回保留、不碰真实凭据库 | 第 21 轮 |
| **凭据**目标前缀（`MCMiniLauncher/`）与条目名 | ✅ 同前缀同条目名，只新增 `ProxyPassword` | 第 27 轮 |
| 内置**更新源** | ✅ 主源 Gitee 字符串不变，新增 GitHub 备用源 | 第 27 轮 |
| **清单协议** | ✅ 模型逐字节相同、解析大小写不敏感、旧端无更严校验；默认源就是待写入的那个 `update.json` | 第 24 轮 |
| 旧客户端**替换脚本** | ⚠️ `timeout` 无上界（新版已修）⇒ **发布说明必须提示**（手册 §5 已写）| 第 24 轮 |
| 残留**外置 `UI/`** | ⚠️ 新版优先用它 ⇒ **发布说明必须提示删除**（手册 §5 已写）| 第 25 轮 |
| 残留**原生 DLL** | ✅ 无害（启动 + 九页渲染正常）| 第 25 轮 |
| 陈旧 **`apply-state.json`** | ⚠️ 被原样继承，仅影响失败提示显示（决策项 3）| 第 26 轮 |
| **实例**模型与目录约定 | ✅ `GameInstance`/`InstanceIsolationService`/`InstanceService` 与 v1.0.0 **逐字节一致**（`<游戏目录>/instances/<名字>`）| 第 28 轮 |
| **注册表/启动项**足迹 | ✅ 两版都不写注册表（只用凭据管理器 + 文件）| 第 28 轮 |

**🔴（第 47 轮）发现 blocker：加载器实例启动必崩**。用真实 Fabric 包（Fabulously Optimized 1.18.2 + fabric-loader 0.19.3）
导入后启动，约 10 秒崩溃在 `UnsatisfiedLinkError: Failed to locate library: lwjgl.dll`。
对照命令行后定位：**启动器自己拉起的 JVM 缺少 `-Djava.library.path`**（Vanilla 有、加载器没有），
根因在 `LauncherService.ResolveJvmArguments`——加载器（继承型）JSON 的 `arguments.jvm` 只有 Fabric 自己那一条，
命中该分支后 `goto` 跳过兜底的三行（`-Djava.library.path` / `brand` / `version`），父版本的 jvm 参数没有并入。
⇒ **所有带加载器（Fabric/Quilt/Forge/NeoForge）的实例都无法启动**；R06「承诺的加载器样例」由 NOT_RUN 变为 **RED（blocker）**。
依据：`run-20261006-1230-fabric-launch/`（含 9801 字符完整命令行与崩溃日志）。

**（第 46 轮补充）运行态守卫也实测过了**：把游戏真的跑起来后，**重复启动 / 删 Mod / 列存档 / 列备份都返回 400**，
JVM 退出后 **5 秒内**恢复（`GET …/saves` 回 200、删 Mod 回 200 并带恢复记录）。**同时发现一处不一致**：
`POST …/saves/{world}/backup`（**创建**备份）**没有运行守卫**，游戏运行中仍返回 200
（`HandleCreateSaveBackupAsync`，`LocalApiService.cs:2374-2382`；它的 13 个兄弟操作都有守卫）
⇒ 对正在写盘的存档做快照可能不一致（低严重度，可选加固）。另查清：那类 400 的正文**确实带清晰消息**
（`{"error":"…"}`），此前"正文为空"是 PowerShell 5.1 的读取通道问题（正文在 `$_.ErrorDetails.Message`），**不是产品缺陷**。
依据：`run-20261006-1200-running-guards/`。

**（第 45 轮补充）原版游戏**真的启动起来了**（本机有真实 GPU）**：装好 1.18.2（256 秒）、就绪真实 Java 17、
加离线账号、`prelaunch-check` 通过后 `POST /launch` → `{"ok":true,"pid":5924}`；
游戏日志显示 `Setting user` → `Backend library: LWJGL 3.2.2` → `OpenAL initialized` → `Sound engine started`
→ **10 个纹理图集 Created**，进程实测 **窗口标题 `Minecraft 1.18.2`、工作集 2,685 MB**；
终止后启动器把 `totalPlaySeconds` 结算为 **305** ⇒ **启动与退出都成立**（离线账号；真实微软账号链路仍 NOT_RUN）。
依据：`run-20261006-1130-vanilla-launch/`。

**（第 44 轮补充）文件层的"替换/回滚/备份/恢复"也验过了**：用候选产物的 API 对真实文件做
存档备份→改动→恢复、Mod 可恢复删除→恢复、Mod 事务化替换（哈希不匹配必须被拒且**原文件不被破坏**），
**20 项断言 0 失败**。存档"恢复"是**非破坏式导入**（目标已存在时写成 `TestWorld (1)`，不覆盖正在用的存档），
替换的"校验"是**上传字节的 SHA256 与声明是否一致**（不是 jar 格式白名单）——这两点我原先都预期错了，已更正。
依据：`run-20261006-1100-file-transactions/`。

**（第 43 轮补充）整合包导入也在真实包上跑通了**：用 Modrinth 官方 **Fabulously Optimized v3.15.0**（1.18.2 + fabric-loader 0.19.3，
`.mrpack` 1.2 MB / 43 个 mod / 60 条 overrides）走产品自己的 `POST /api/instances/import`，
**15 项断言 0 失败**：43 个 mod **逐文件 SHA1 一致**、overrides 抽查 60 条全在、原版 1.18.2 client.jar
**大小与 SHA1 与官方一致**、**assets 3052/3052 个对象齐全 0 截断**、加载器已安装、
**Java 由产品自己下载并安装**（`runtimes/java-17-…`，`java -version` 可执行）。
⇒ 这一趟同时补齐了 **整合包导入 / Java 自动安装（InstallAsync 的唯一调用点） / 现代版本安装**三项。
依据：`run-20261006-1030-modpack-import/`。

**（第 42 轮补充）Java 这一段也打通了**：按产品约定的目录放入**真实 Adoptium Temurin JRE 17.0.20.1**
（43,780,109 B，SHA256 与 Adoptium 声明一致，`java -version` 真的能跑）后，**产品自己发现了它**
（`/api/java` → `installations=1`），**`prelaunch-check` 由"缺 Java"变为通过**（6 项 0 失败）。
⇒ R06「Java/版本」现在只剩"**在有桌面/显卡的真机上点一次启动**"。
同时发现（第 41 轮）：**普通用户没有"安装 Java"入口**——`InstallAsync` 只被整合包导入调用，
可自动下载的只有 17/21/25，界面只有路径选择器，而 i18n 却写"使用内置 JRE（默认）"。
依据：`run-20261006-1010-java-ready/`、`run-20261006-1000-java-entry/`。

**（第 40 轮补充）R06 的"原版下载"也已在真实源上验过**：用候选产物自己触发安装 1.5.2，
**client.jar 的 size 与 SHA1 与官方元数据逐字节一致**，assets **468/468 个对象齐全、0 截断、字节精确一致**，
46 秒完成（10 项断言 0 失败）⇒ 真机剩下的只有"用 Java 启动/退出"与"Java 运行时安装"。
依据：`run-20261006-0940-vanilla-download/`。

**（第 36 轮补充）R06 的"服务腿"已用真实上游验过**：版本列表 917 条、新闻 12 条真实外链
（`linkOk=true`）、更新源延迟与可用性、Modrinth/CurseForge 搜索（内置 Key 可用）。
两条图片代理已追查到底（第 38–39 轮）：**news-image 的 400 属设计行为**（fake-IP 被安全检查拒绝、默认 fail closed，6 次全 400/7–9ms）；
**content/icon 端点本身可用**——6 次里 4 次 **200 / image/webp / 4,800 B / 1.3–1.8s**，仅最早 2 次是 **504 / 恰好 5,046ms**
（5 秒连接预算在本机 fake-IP+本地代理下偶发超时）⇒ **不是缺陷**，只留一个加固点：**连接超时 5s 且不重试 ⇒ 偶发丢图**
（是否放宽/加重试属产品决定）。依据：`run-20261006-0830-r06-services/`、`run-20261006-0910-image-proxy-rootcause/`、`run-20261006-0920-icon-flakiness/`。

**结论：≤1.0.0 用户可以安全升级**（配置、凭据、更新源三条持久化链未断，清单协议兼容）；
需要用户侧做的只有发布说明里那两件事，外加一条可选的产品取舍。
**不变量建议**：把"凭据条目名只增不改"与"配置目录名不变"写进发布检查项（未来版本同样适用）。
## 4. 仍未通过 / NOT_RUN（及解锁条件）

| # | 项目 | 为什么现在过不了 | 解锁条件 |
| --- | --- | --- | --- |
| 1 | R03 线上写入（Gitee `update.json`）| 需发布授权与真实令牌 | 用户授权发布动作 |
| 2 | 更新端到端（**剩余部分：替换自己→重启→同版一致**）| **下载侧已被真实端点完整覆盖**：源级降级（第 33 轮）、资产级回退 + 主源 Gitee 匿名下载 + 篡改拒绝（第 35 轮，19 项 0 失败）；仍缺"替换自己并重启"——脚本刻意不调 `/api/update/apply`（会替换掉被测进程）。另：`UpdateService.ApplyUpdate`（启动器自己调用替换）**无直接测试覆盖**——target 是 `Environment.ProcessPath`，在测试进程里调用等于让脚本覆盖正在运行的测试宿主，安全覆盖需产品重构 | 真实发布一次 + 在真机上升级 |
| **R06-RED-3** | **全新安装下"默认游戏"实例不可寻址（blocker，第 65 轮实测）**：`config.json` 不存在（或实例列表为空）时，
`ConfigStore.LoadAsync()` 在内存里造一个默认实例（`GameInstance.Id` 是新 GUID）**却不落盘** ⇒ 每次 API 调用造出的 id 都不同：
连续三次 `/api/state` 得到三个不同 id；用第一次的 id 调 `/launch` 与 `/prelaunch-check` 都是 **400 实例不存在**；
一旦有操作落盘（建离线账号等）id 立刻稳定、prelaunch 返回 200。**界面启动流程不落盘** ⇒ "全新安装直接点启动"必然失败。
修法一行级（造完默认实例后落盘一次），建议与另两个 blocker 一起修。
**同一条根因的第二个面（第 66 轮补测）**：默认**账号**同样每次换 id（`dd89f67a…` / `36c780ea…`），
`DELETE /api/accounts/{id}` 先撞到"至少保留一个账号"的守卫（400）⇒ id 不稳定已证明、后果同源。| 用户可见：**第一次点启动就报"实例不存在"**；移除最后一个实例后同样触发 |
| **R06-RED-3** | **Forge/NeoForge 实例"装得上但启动必失败"（blocker 级，第 84 轮在**当前源码**上实测）**：两个独立装配缺陷 ——
**① `LauncherService.cs:197` `DistinctPreserveOrder()` 去重**吃掉重复的选项 token（Forge JSON 里 `--add-opens`×2/`--add-exports`×2 各剩 1 个），
JVM 把第二个 spec 当主类 ⇒ **0 秒 exit 1**（`ClassNotFoundException: java.base.java.lang.invoke=cpw.mods.securejarhandler`，手工复跑原始输出已存档）；
**② Forge 专有占位符未替换**（生成的命令行里 `${library_directory}`×9、`${classpath_separator}`×7 是字面量）⇒ 模块路径无效 ⇒
`InaccessibleObjectException: module java.base does not "opens java.lang.invoke" to unnamed module`。
**③ 类路径里放了原版 client jar**（`-cp` 含 `versions\1.20.1\1.20.1.jar`、不含安装器产出的 `forge-1.20.1-47.4.3-client.jar`）⇒
`ResolutionException: Modules _1._20._1 and minecraft export package … to module immediatelyfast`。
**第 85 轮已验证修复配方**：在同一条捕获命令行上手工补回重复 token + 替换 Forge 占位符（含空格/中文路径加引号）+ 从 `-cp` 去掉原版 jar 后，
**Forge 实例存活 200 秒、stderr 0 行、LWJGL=1 声音=1 图集=12 崩溃=0**（游戏真的跑起来）⇒ 三个缺陷即全部原因。
**第 86 轮：补丁已备并验证** —— `run-20261007-3800-forge-launch-patch/proposed-fix-forge-launch-assembly.diff`（5 hunk，`git apply --check` ✅），
在副本构建上由**产品自己**启动 Forge：**图集 12 / 0 崩溃 / JVM 存活整个 204 秒**，Vanilla 回归也正常（8/8 PASS）；**补丁未应用到工作区（需授权）**。
对照：**Forge 导入已 PASS**（269/282 秒，安装器 `Successfully installed client into launcher.`）⇒ B2/B2b 成立，但启动需上述三处修复 |
| **R06-RED-4** | **Java 版本选错 ⇒ MC 1.21+ 实例必然启动失败（blocker 级，第 87 轮实测）**：NeoForge 21.1.230（MC 1.21.1）在只有 Java 17 的机器上，产品用 Java 17 启动 ⇒
`InvalidModuleDescriptorException: Unsupported major.minor version 65.0`（65=Java 21 class 版本）⇒ **秒退且产品侧零输出**；
**换 Java 21 后同一套参数/同一实例游戏正常**（327 行日志 / LWJGL / 15 图集 / 0 崩溃）。
影响面：MC 1.20.5+ 起就需要 Java 21，**所有新版本实例**都受影响。
**第 88 轮细化根因**：启动路径把**加载器版本 id**（`neoforge-21.1.230`/`1.20.1-forge-47.4.3`）传给**字符串版**匹配器 ⇒ 解析不出主版本 ⇒ **默认 17**（实测 `neoforge-21.1.230→17`、`1.20.1-forge-47.4.3→17`，而 `1.21.1→21`）；
产品已有**元数据版**重载（读版本 JSON 的 `javaVersion.majorVersion`、可沿 `inheritsFrom` 上溯）但启动路径没用它。
**补丁 v3 就绪**：`run-20261007-4200-java-version-fix/proposed-fix-java-version-selection.diff`（传游戏目录 + 不静默用不匹配 Java + 先自动安装 + 可读错误），
与 Forge 装配补丁**一起可编译**（0 警告 0 错误）；**未应用到工作区**。 |
| **R06-RED-2b** | **Forge/NeoForge 安装的第二半：跑安装器前没有 `launcher_profiles.json`（blocker，第 77 轮在**打过补丁的构建**上发现）**：
URL 修好后 404 消失（报错变为 `Forge installer 失败，ExitCode=1`），但安装器仍失败——安装器自己的输出是
`There is no minecraft launcher profile in "<gameDir>", you need to run the launcher first!`；
**写一个 46 字节的最小 `launcher_profiles.json` 后同一安装器 exit 0**（`Successfully installed client into launcher.` + `versions/1.20.1-forge-47.4.3` + 24 个 Forge 库文件）。
修法：`RunInstallerAsync` 启动前写最小桩（补丁已备、`git apply --check` ✅）。
**第 78 轮补充（诚实边界）**：把 B2b 补丁也应用后，**本机 Forge 导入仍失败**——两组对照显示安装器自身的 HTTPS 下载报
`SSLHandshakeException: Remote host terminated the handshake`（ASCII 路径与**中文+空格**路径**报错完全相同** ⇒ 不是中文路径问题，是**本机网络环境**）。
⇒ 两个产品缺陷（URL / profile 桩）**已被定位并给出补丁**；但"**Forge 端到端装得上**"在**本机验不了**，要换网络环境。| **Forge/NeoForge 目前装不上**（两个产品缺陷 + 本机环境共同阻断）；发布说明应写"当前版本无法安装" |
| **R06-RED-2** | **Forge/NeoForge 安装器 URL 多一段 ⇒ 必然 404（blocker，第 61 轮）**：`InstallerLoaderService.GetInstallerUrl` 把 artifact 段拼了两遍
（`ForgeService` 传 `net/minecraftforge/forge` + `_artifactId=forge`；`NeoForgeService` 同理），实测产品拼法是 **404**、正确 URL 是 **200**；
metadata 拼法正确 ⇒ 只有"下载安装器"这一步坏。**更正第 51/52 轮"环境嫌疑/未定因"的判断：是产品拼错 URL**（我此前手工探的都是正确 URL，两边探的不是同一地址）。
修法一行（补丁未应用，已验证），建议与启动缺陷一起修 | **当前版本 Forge/NeoForge 实例无法安装**（任何机器都一样）；装上后仍会因缺 `-Djava.library.path` 起不来 ⇒ 两处都得修 |
| **R06-RED** | **加载器实例启动崩溃（blocker，第 47 轮；根因与修法已于第 48 轮验证）**：Fabric 实例启动必崩（`Failed to locate library: lwjgl.dll`），根因是加载器 profile 的命令行缺 `-Djava.library.path`（`LauncherService.ResolveJvmArguments` 的 `arguments.jvm` 分支跳过了兜底）。**受控实验**：拿产品自己那条命令行**只插入该参数**（其余一字不改）→ `Backend library: LWJGL` + `Sound engine started` + **10 个图集**、**0 崩溃行**、JVM 存活 150 秒（基线是 208 行日志后崩）| 属**代码缺陷**，不是环境问题。**根因已收敛到一行**（`VersionResolver.cs`：`libraries` 是**追加**、`arguments` 却是**整体替换** ⇒ 父版本的 `-Djava.library.path=${natives_directory}` 丢失；命令行实测父版本库全在、jvm 参数全无）。**三份补丁已备好（未应用）**：A 治根 +48/−1、B 最小兜底 +6、回归测试 +39（新文件）。
**影响面（第 50–53 轮，含证据类型；第 53 轮已更正）**：**受影响 = Fabric（实测崩）/ Forge（安装器内嵌 `version.json`：16 条 jvm 里无库路径）/ NeoForge（同法：15 条里无库路径）**；
**不受影响 = Quilt（已实测：49 mod 的 Quilt 包导入后命令行**含**库路径、12 图集、0 崩溃）与 Vanilla（自带 `${natives_directory}`）**。
Quilt 的机制：其 profile 的 `arguments` 只有 `game`、没有 `jvm` ⇒ 走兜底分支 ⇒ 反而拿到库路径（`run-20261006-1530-quilt-probe/`）。
⚠️ 第 50 轮曾推断"Quilt 极可能同病"，第 53 轮按 profile 原文 + 现有逻辑推演（`fallback-check.cs`：`FALLBACK-CHECK RESULT=PASS`，受影响=3）**推翻了该推断**，见 `run-20261006-1500-loader-blast-radius/`。
另记一个**未解释**的 Forge **安装**失败（HTTP 404，而同一 URL 本机 200；**第 52 轮用记录型代理进一步排除"镜像改写"** —— 代理日志显示启动器连的就是官方 `maven.minecraftforge.net`，但经转发代理仍 404 ⇒ **仍未定因，倾向环境但未证明**）：`run-20261006-1400-forge-probe/`、`run-20261006-1430-forge-404-trace/`。
**新确认缺陷**：代理设置在**启动时**不生效（见 §5 第 13 项）。
**补丁 A 的合并算法已单独验证通过**（`MERGE-CHECK RESULT=PASS`，父版本 `-Djava.library.path=${natives_directory}` 保留、子条目追加在后、`game` 参数不再被清空）。
修完必须回归 **全量 903 测试 + Vanilla 启动 + Fabric 启动**，且因改源码 ⇒ 产物字节变 ⇒ 全量验证链重跑 | 用户决定：**发版前修**（改源码 ⇒ 产物字节变 ⇒ 全量验证链重跑）**或**降级为已知限制写进发布说明（"含加载器的实例暂无法启动"）|
| 3 | R06 真机项（**服务腿与"微软登录无需真人三段"已验**：第 36 轮真实上游 + 第 37 轮内置 client_id/设备码/轮询 PASS，见 `run-20261006-0850-ms-login/`）。剩余：**真人完成设备码授权及登录后各分支（取消/超时/切换/续期/离线/皮肤披风）**（第 45 轮已用**离线账号**把"启动/退出"跑通）、以及"下载后替换自己并重启"（**第 59–60 轮已验通**：检查→下载→SHA256→替换→原进程退出→**重启并由新进程提供服务**）；另外**加载器（Fabric/Forge/NeoForge）实际启动**仍未验（第 47/51/53 轮已定因：命令行缺 `-Djava.library.path`，见 §4 的 R06-RED 行；Quilt 已实测不受影响）；**进入世界/存档加载已验**（第 56 轮：官方服务端造档 + `--quickPlaySingleplayer`，日志出现 `joined the game`）；**多实例并发已验**（第 57 轮：两个实例同时跑、两个 JVM、同实例重复启动 400、退出后守卫解除） | 本机 `java installations=[]`、无真实账号、无游戏实例；替换自己会灭掉被测进程 | 用户提供真实账号/Java/Minecraft 环境 |
| **0（最重要）** | **候选范围决定**：**≥70.8%**（≥9,359 / 13,222 行）的新增行是评估前既有功能/文档工作（AI/新闻/前端/整合包/Mod… + 8 个功能提交），本轮从未复核，且与整改**同文件交织、无法切分** | 属"要不要把这些功能一起发、依据是什么"的**范围决策**，不是技术故障 | 用户决定：(a) 一起发、真机验收记 NOT_RUN 并在发布说明声明范围；或 (b) 先单独验收功能域（工作量大于本会话，且需真实账号/游戏环境）|
| 4 | 提交/推送/远程 CI | 未获授权；**且本地有 7 个提交未推送**（远程 `main`=`c8aa286`，本地 HEAD=`f145d14`，涉及 39 个文件）⇒ 推送会同时公开这 7 个功能提交 | 用户授权提交推送（并确认这批提交可公开）|
| ~~5~~ | ~~actionlint~~ | ✅ **第 32 轮已闭合**（镜像下载 + 双镜像哈希一致 + 官方校验表 + 反向对照；当前两工作流 exit 0）| —— （已复现，不再是 NOT_RUN）|
| 6 | "无权限"的另外两种形态（磁盘满、被安全软件拦截）| 无法在受控环境安全模拟 | 特定故障机/沙箱 |
| 8 | 本轮**新增/改写**的 CI 步骤（证据产物上传、`needs: verify` 互锁、形态断言、独立冷启动、真实更新链、镜像状态落盘；`setup-node@v4` 整步；`setup-dotnet@v4` 由 `dotnet-version` 改为 `global-json-file`）| 从未在真 runner 上执行过。**第 34 轮已把 action 契约静态校验通过**（5 个 action 的 `with` 输入名全部合法、必填齐全、`runs.using=node20`；Node `24.19.0` 与 SDK `10.0.301` 在官方索引里真实存在）| 首次推送触发（需授权）；**四个 action 自身在 v1.0.0 就跑过**（`checkout@v4`/`setup-dotnet@v4`/`upload-artifact@v4`/`action-gh-release@v2`），该仓库旧流水线长期成功（第 33 轮查 runs API、第 34 轮对比 v1.0.0 工作流）|
| 7 | 版本定稿 | 属产品决策，但已是**硬性要求**：tag `v1.0.0` 已被占用（→ f61f44f）、`csproj <Version>1.0.0</Version>`、客户端 `currentVersion` 取**程序集版本**（真实链路实测 `1.0.0`），按 v1.0.0 发会让所有用户"已是最新" | 用户定版本号（须 > 公开 v1.0.0）**并同步改 `csproj` 的 `<Version>`** |

## 5. 需要用户决定的事项（不是技术问题）

**第 0 项（先决条件）——发布范围**：候选里 **≥70.8%** 的新增行（≥9,359 / 13,222）是评估前的既有功能/文档工作，本轮未复核且与整改同文件交织。
先定这一条，下面几条才有意义：一起发（并在发布说明里声明未真机验收的范围）／先单独验收功能域／或别的方案。
依据：`run-20261006-0700-scope-warning/`、**数字更正** `run-20261006-0730-scope-recount/`；发布说明草案已把该决定写成 A/B 两案待勾选：`run-20261006-0710-release-notes/`。

1. **是否修** `/api/versions/remote` 的**退化契约**。第 36 轮实测：该端点**正常工作是好的**（真实上游返回 **917 个版本**、2.4 秒），之前"常态 500 + 界面暂无可下载版本"的描述**过重**；准确问题是"**上游不可达/超时才退化**，退化时只有 500 加一句提示，缺类型化失败与重试入口"。⇒ 是否修仍由你定，但**紧迫度下降**。代价：改 `ui/**` 会让嵌入 UI 指纹、单文件 SHA256 与**全部** UI 证据失效，需整轮重跑。

2. **是否在产品侧修"外置 UI/ 优先"**（第 25 轮实测的升级隐患）：发布说明里要求用户升级后删掉旧 `UI/` 是零风险做法；若希望在代码里兜住（内嵌包优先于 EXE 旁 `UI/`），代价是产物 SHA 变化 + 本会话全部既有证据重跑，并且要重新评估"开发树里直接运行"的便利性。当前**未改**，等你定。

3. **是否处理"陈旧 apply-state"**（第 26 轮实测）：旧版本留下的失败记录会被新版原样继承并在设置页显示（"上次替换失败 … · 旧版本号"），除下一次替换外无人清除。不改的话建议在发布说明里加一句"可忽略该提示"；要改则是界面/接口语义变更（需重跑整轮验证）。当前**未改**。

4. **是否收紧嵌入 glob**（把被 `.gitignore` 忽略的 `ui/assets/*` 排除在 `EmbeddedResource` 之外）。现状：本地产物比 CI/发布产物多嵌一个 47KB 的本地资源（`axolotl.png`，无人引用），导致"本地验的字节 ≠ 要发的字节"。代价：改变产物哈希并牵动既有证据。

5. **版本号**（> v1.0.0）与 **tag/程序集/manifest/资产** 的统一。

6. **是否清理**工作区里的历史遗留（探针脚本、备份、0 字节空文件 `DownloadResumeTests.cs` 等；已全部列入候选计划的"不得进入候选"规则，但未删除）。

7. **是否升级 action 大版本 / 按 SHA 钉死**（第 34 轮实测：现钉 `checkout@v4`/`setup-dotnet@v4`/`setup-node@v4`/`upload-artifact@v4`/`action-gh-release@v2`，上游已有 v7/v6/v7/v7/v3；旧大版本仍可用，v1.0.0 就是用它发布的）。三个选项：保持现状（首次推送天然验证）／升级到最新大版本／按解析到的 SHA 钉死（最稳，需手动维护）。**代价：任何一项都改工作流字节 ⇒ 冻结哈希失效 ⇒ 必须重跑 actionlint + 契约脚本 + 全量验证链。** 依据：`run-20261006-0810-action-contract/`。

8. **可选修（低严重度）**：`release.yml` 的状态字段 `updateJsonUrl` 写的是 `{api}/raw/master/update.json`（`api/v5` 形式），实测**对无 token 的人是 401**；客户端真正用的是 web 形式 `https://gitee.com/{owner}/{repo}/raw/master/update.json`（公开可读）。影响仅限"人去点状态文件里的链接"。一行字符串即可改；与第 7 项一起做最划算（都要重跑验证链）。依据：`run-20261006-0820-asset-fallback/`。

9. **可选修（低严重度）**：图标取图的 `ConnectTimeoutSeconds = 5` 且**不重试**（第 39 轮实测：本机 6 次里 2 次因此返回 504、4 次 1.3–1.8 秒成功）⇒ 短暂抖动会让用户看不到图标（页面其它内容不受影响）。是否放宽超时或加一次重试属产品决定；代价：改 `ContentIconService.cs` 会改产物字节 ⇒ 需重跑全量验证链。依据：`run-20261006-0920-icon-flakiness/`。

10. **是否给普通用户加"安装 Java"入口 / 修正文案**（第 41 轮发现）：`JavaRuntimeService.InstallAsync` **只有整合包导入一个调用点**（`InstancePackService.cs:755-757`），可自动下载的只有 **17/21/25**；界面里与 Java 相关的控件只有路径选择器（`ui/js/launcher.js:666`），而 i18n 写着"使用内置 JRE（默认）"，53 MB 产物里**没有 JRE**。缺 Java 时实测：`prelaunch-check` 报"未找到可用的 Java 运行时，请先在设置中指定 Java 路径"、`POST /launch` 返回 400（**检测与拦截都对**），但用户**无处可装**。选项：① 发布说明写清"需自备 Java 17/21/25"；② 加一个"安装 Java"动作（能力已具备，只是没暴露）；③ 只改文案。**代价**：任何 `ui/**` 改动都会让嵌入 UI 指纹、产物 SHA256 与全部 UI 证据失效 ⇒ 需整轮重跑。依据：`run-20261006-1000-java-entry/`。

11. **可选修（低严重度）**：超过 24 MiB 上传上限时的错误信息说"**请使用流式导入**"，但核对后**没有任何分片/流式导入路由**（`seg[...]` 无 upload/chunk/stream；界面是整包 base64，`ui/js/api.js:872-874`）⇒ 用户拿到的是**死胡同提示**。`.mrpack` 通常很小（本次仅 1.2 MB），但 **CurseForge 的 zip 常把 overrides 打进去**、很容易超限。修法：改文案，或真的实现分片上传（属产品决定）。依据：`run-20261006-1030-modpack-import/` §3。

12. **可选修（低严重度）**：**运行中仍可创建存档备份**（第 46 轮实测 200，其余存档/Mod 操作均被 400 挡住）。`HandleCreateSaveBackupAsync`（`LocalApiService.cs:2374-2382`）没有 `RunningInstanceRegistry` 守卫，而 13 个兄弟操作都有 ⇒ 对正在写盘的存档做快照可能不一致。选项：① 加同一个守卫；② 明确允许并接受风险；③ 在发布说明里提示"运行中备份可能不完整"。**代价**：改 `LocalApiService.cs` 会改产物字节 ⇒ 需重跑全量验证链。依据：`run-20261006-1200-running-guards/`。

13. **可选修（低–中严重度）**：**代理设置在启动时不生效**（第 52 轮 A/B 实测：config.json 预置代理后启动，
    首个网络请求**不走代理**（代理日志 0 行）；`POST /api/config` 保存一次后才走（1 行））。
    代码根因：`ApiHttp` 静态字段初始 `proxyEnabled:false`（`LocalApiService.cs:41`），`LoadConfig()` 不刷新（1051–1060），
    只有 `SaveConfig()` 调 `RefreshApiHttpProxy`（1062–1071）。**用户可见后果**：设置里保存代理后当次会话生效，**重启后失效**（直连）。
    修法：`Start()` 或首次加载配置后调用一次 `RefreshApiHttpProxy(config)`（一行级改动，但要重跑验证链）。
    依据：`run-20261006-1430-forge-404-trace/proxy-apply-test.ps1` 与 `proxy-apply.json`。

14. **是否修"取消导入后残留半成品"（中严重度，第 58 轮实测）**：35 秒时取消一个真实导入 —— 取消**被接受**
    （`{"cancelled":true,"message":"cancelled"}`），但：**工作继续**（目录 51.6 → 312.6 → 364.7 MB）、
    **半成品目录没清**（`instances/Fabulously Optimized 导入` 364.7 MB / 2,805 文件 / 7 个 `.download` 残片）、
    **临时上传目录没清**（`%TEMP%\pineglow-import-…` 1.2 MB）、**客户端拿到的是"连接被关闭"而不是文档里的 409**（224 秒后）。
    代码里两处清理都在（`InstancePackService.cs:349`、`LocalApiService.cs:1497`），但 API 路径的 `_log` 是**空委托**
    ⇒ 连"需要用户手动清理"的提示都无处可去。**"取消先到"（tombstone）实测完全正常**（409 + 0 秒 + 不建实例）。
    建议：① 清理失败要让用户看见；② 清理前释放 `.download` 句柄或改"移入回收区延后清"；③ 原版安装段加密取消检查点；
    ④ 取消返回确定的 409。依据：`run-20261006-1730-import-cancel/`。

15. **是否收紧"清单地址也强制 HTTPS"（加固项，第 59 轮实测）**：`UpdateService.AddCandidate` **同时接受 http 与 https**
    （只排除其它协议），而 `ValidateManifest` **只强制"产物 url 是 HTTPS"**。实测：把 `updateUrl` 指到本机 **HTTP** 清单，
    `check` 直接成功（`manifestSource=http://127.0.0.1:…`，`usedFallback=false`，服务器日志确认被取走）
    ⇒ **中间人可同时篡改清单里的产物地址与 SHA256**，把更新指向任意 HTTPS 主机上的任意文件（产品 `trustNote` 也承认这一点）。
    建议：清单地址同样强制 HTTPS，或固定只信任默认源。依据：`run-20261006-1800-update-chain/`。

16. **是否在本次发版前一并修 Forge/NeoForge 安装器 URL（**一行**，第 61 轮定位）**：`InstallerLoaderService.GetInstallerUrl`
    把 artifact 段拼了两遍（`_groupPath` 里已含），实测 Forge/NeoForge 的安装器 URL 都是 **404**（正确 URL 是 200）。
    ⇒ **当前版本根本无法安装 Forge/NeoForge 实例**。修法一行、已验证（补丁 `run-20261006-1900-forge-url-probe/proposed-fix-installer-url.diff`）。
    **建议：与加载器启动缺陷一起修**（否则即便装上也会因缺 `-Djava.library.path` 而起不来）。

17. **是否在本次发版前一并修"默认实例/账号不落盘导致 id 不稳定"（第 65–66 轮实测，blocker）**：见 §4 的 **R06-RED-3** 行。
    修法一行级（`ConfigStore.LoadAsync()` 造完默认实例/账号后落盘一次），**建议与加载器启动、Forge/NeoForge URL 一起修**——
    三者都要改源码 ⇒ 一次重跑全量验证链最划算。**三个补丁已集中到一处**：`run-20261006-2130-patch-set/PATCH-SET.md`（含应用方式与 9 步验证清单）。
依据：`run-20261006-2100-default-instance-id/`（实例面）、同目录 `verify-default-account-id.ps1`（账号面）。

18. **可选修（低严重度）**：第三方登录失败时**把原始响应体透传进错误文案**（第 70 轮实测：serverUrl 指向非 Yggdrasil 站点时，
    用户看到 `第三方登录失败：<!doctype html>…` 一整段 HTML）。建议对非 JSON 响应做摘要，JSON 的 `errorMessage` 再原样透传（后者现在就是对的）。
    依据：`run-20261006-2330-thirdparty-auth/`。

20. **B2 的第二半（blocker 级，第 77 轮新发现）**：**跑 Forge/NeoForge 安装器前没有准备 `launcher_profiles.json`**。
    在**打过补丁的构建**上导入同一个真实 Forge 包：404 **消失了**（`Forge installer 下载失败：HTTP 404` → `Forge installer 失败，ExitCode=1`）⇒ **B2 的 URL 修复有效**；
    但安装器**仍然失败**。按产品完全相同的 Java/参数手工复现，安装器自己说：`There is no minecraft launcher profile in "<gameDir>", you need to run the launcher first!`；
    **往同一目录写一个 46 字节的最小 `launcher_profiles.json` 后，同一安装器 exit 0、`Successfully installed client into launcher.`**，
    并落盘 `versions/1.20.1-forge-47.4.3` 与 24 个 Forge 库文件。⇒ **即使 URL 修好，Forge/NeoForge 仍装不上**（同一功能的第二个缺陷）。
    修法：`LoaderInstallHelper.RunInstallerAsync` 启动安装器前写最小桩文件（补丁 `proposed-fix-forge-launcher-profile.diff`，**已通过 `git apply --check`**）。
    附带诊断性观察：产品其实**捕获了安装器输出并逐行 `_log`**，但 API 导入路径的 `_log` 是空委托 ⇒ 用户只看到 `ExitCode=1`，看不到那句关键提示（与第 14 项第①条同源）。
    依据：`run-20261006-2800-patched-forge/`。
19. **可选修（低–中严重度，加固）**：**authlib-injector 组件只校验大小、没有 SHA256 固定**（`AuthlibInjectorService.DownloadAgentAsync` 仅要求 `length > 1024`）。
    该 jar 会在启动时经 **`-javaagent`** 注入 JVM（**代码执行面**），而更新链是有 SHA256 校验的。建议钉官方 SHA256。
    （两个下载 URL 实测都可用：官方与 BMCLAPI 镜像均 **200 / 341,970 B**。）依据：同上。

## 6. 本轮更正过的问题（含我自己的错误）

| 来源 | 问题 | 处理 |
| --- | --- | --- |
| 我（第 9 轮）| 把"两份本地产物差 40KB"归因于发布会话参数/PDB | 第 14 轮查明是**被 gitignore 的 `ui/assets/axolotl.png` 被嵌入 glob 带上**；已在进度报告就地更正并标注"第 9 轮那是错的" |
| 我（第 11/16 轮）| 声称 R01 崩溃栈与 R05 误判现场"没有留存" | 第 17 轮在 DeliveryRoot 找到两份原始现场；已更正 |
| 我（第 15 轮）| 用 `git check-ignore` 的**输出**判断是否被忽略，把反选规则误报为"被忽略" | 改用退出码；已记录 |
| 我（第 16 轮）| 新写的表达式检查器把 `dotnet publish` 默认输出目录报成"无产生步骤" | 补规则并写明匹配依据 |
| 仓库（csproj）| 注释称 `CopyOutputSymbolsToPublishDirectory=false` 能清掉原生 PDB | 实测三次发布都带（84MB+20MB）；注释已按实测改写 |
| 仓库（release.yml）| 注释称 sha256 必须**小写**，实际 `-match` 大小写不敏感（且与客户端一致）| 改注释与抛出文案，**不**收紧门槛；并用 `V5-sha-uppercase-accepted` 钉住 |
| 产品（旧实现）| 更新替换脚本用 `timeout /t`，无控制台时永久阻塞 → 更新卡死 | 已修（`ping` 等待 + 有界轮询）并有回归位 |
| 产品（release.yml）| Gitee API 不可达时抛在写状态文件之前 ⇒ 失败却零证据 | 已修（先落盘 + warning 再抛） |

## 7. 证据索引

**本轮复评（SourceRoot: `D:\松光启动器\docs\release-remediation-evidence-2026-10-05\`）**

- 主报告：`REMEDIATION-PROGRESS-2026-10-05.md`（含 §6 对照原始流程的覆盖审计、§6.1 逐条新发现）
- 整轮验证（**11 轮 PASS**，2026-10-05 21:18 起）：`2118` → `2330` → `0010` → `0015` → `0030` → `0050` → `0120` → `0150` → `0220` → `0420` → `0450`（全部 `-final/final`；更早另有 5 轮 `2037/2040/2100/2105/2112`）
- 九页界面运行时：`run-20261005-2350-uipages-1/`、`run-20261005-2350-uipages-2/`（通过）、
  `run-20261005-2340-uipages/`（非通过 + `R06-ui-downloads-timeout-finding.md` 定位）；
  该工具的早期迭代（含假通过/假失败）为 `run-20261005-2130/2238/2250/2255/2302/2308-uipages/`
- 发布门槛干跑：`run-20261006-0110-releasejob-dryrun/`（18 例，含逐条 RED）
- Gitee 镜像失败路径：`run-20261006-0140-gitee-mirror-dryrun/`（14 断言）
- 发布配方与产物差异：`run-20261006-0210-publish-recipe/`
- 候选集合双向核对与触发条件：`run-20261006-0250-candidate-set-audit/`
- 表达式引用静态检查：`run-20261006-0300-expr-check/`
- 工作流引用审计：`run-20261006-0040-wf-audit/`
- R01 无权限两种形态：`run-20261006-0008-r01-noperm/`；R05 机制复现：`run-20261006-0005-r05-red-repro/`
- actionlint 下载实测（失败路径，已被下一行取代）：`run-20261006-0050-actionlint/`
- **actionlint 已复现（镜像下载 + 双镜像哈希一致 + 官方校验表 + 反向对照）**：`run-20261006-0740-actionlint/`

- 替换脚本代码页（437）与 `ApplyUpdate` 覆盖缺口：`run-20261006-0440-apply-codepage/`
- 升级就位（旧配置存活）与测试缝盘点：`run-20261006-0500-config-upgrade-audit/`
- 交付脚本静态解析与"交付 vs 自动执行"盘点：`run-20261006-0520-shipped-scripts-parse/`
- 旧版迁移兼容性（读已发布 v1.0.0 源码）：`run-20261006-0600-v100-migration/`
- 原地升级隐患（外置 `UI/` 与残留 DLL）：`run-20261006-0620-upgrade-in-place/`
- 陈旧 `apply-state.json` 被新版继承：`run-20261006-0640-stale-apply-state/`
- 持久化标识（配置目录/凭据名/更新源）与 v1.0.0 的同一性：`run-20261006-0660-persistence-identity/`
- 实例/数据布局与 v1.0.0 的同一性：`run-20261006-0680-instance-layout/`
- **候选范围警告（约 52% 新增行未经复核且不可切分；数字已由第 31 轮更正为 ≥70.8%）**：`run-20261006-0700-scope-warning/`
- **发布说明草案（含未验收清单与禁止措辞清单）**：`run-20261006-0710-release-notes/`
- **候选范围重算（三段分解；更正上一轮的 48/+11,954/52%）**：`run-20261006-0730-scope-recount/`
- **远程真相 + 真实更新链路 PASS + 7 个未推送提交 + 版本硬要求**：`run-20261006-0750-remote-truth/`
- **action 契约校验（输入名/必填/运行时/版本存在性 + 哪些前所未跑）**：`run-20261006-0810-action-contract/`
- **资产级回退真实验收 PASS + Gitee 主源匿名可下 + 状态字段 401 小缺陷**：`run-20261006-0820-asset-fallback/`
- **R06 外部服务真实端点盘点（版本 917 / 新闻 12 条真实外链 / 源延迟 / 内容搜索双源）+ 两条图片代理未判定**：`run-20261006-0830-r06-services/`
- **微软设备码登录"无需真人"三段 PASS（内置 client_id 有效 / 设备码 / 轮询如实 pending）**：`run-20261006-0850-ms-login/`
- **图片代理根因追查（推翻"SSRF 拒绝图标"的猜测；同 IP 直连对照 1.6s 成功）**：`run-20261006-0910-image-proxy-rootcause/`
- **图标端点复查：6 次 2×504 / 4×200，端点可用，504 属 5 秒连接预算偶发超时**：`run-20261006-0920-icon-flakiness/`
- **原版下载真实验收 PASS（client.jar sha1 与官方一致、assets 468/468 齐全）**：`run-20261006-0940-vanilla-download/`
- **缺 Java 的用户会看到什么 + 没有"安装 Java"入口（发现）**：`run-20261006-1000-java-entry/`
- **真实 JRE 就绪验证 PASS（Adoptium 校验和一致、产品能发现、prelaunch-check 转通过）**：`run-20261006-1010-java-ready/`
- **真实整合包导入 PASS（43 mod 逐文件 SHA1、assets 3052/3052、Java 自动安装）**：`run-20261006-1030-modpack-import/`
- **文件层替换/回滚/备份/恢复 PASS（20 项；含非破坏式恢复与哈希不匹配回滚反证）**：`run-20261006-1100-file-transactions/`
- **原版游戏真的启动并退出 PASS（LWJGL/OpenAL/10 图集就绪、窗口标题 Minecraft 1.18.2、退出结算 305 秒）**：`run-20261006-1130-vanilla-launch/`
- **运行态守卫实测（重复启动/删 Mod/列存档/列备份 400；退出后解除）+ 运行中可创建备份的发现**：`run-20261006-1200-running-guards/`
- **🔴 加载器实例启动崩溃（blocker：缺 `-Djava.library.path`）+ 完整命令行与崩溃日志**：`run-20261006-1230-fabric-launch/`
- **✅ 受控实验：只补该参数后 Fabric 实例进入渲染管线（10 图集、0 崩溃、存活 150 秒）—— 根因确认 + 修法验证**：`run-20261006-1300-fabric-fix-sim/`
- **根因收敛到一行 + 三份可审阅补丁（A 治根 / B 兜底 / 回归测试）**：`run-20261006-1310-loader-fix-patch/`
- **影响面按代码路径分档（Fabric 实测/Quilt 同构/Forge·NeoForge 待定）+ 合并算法独立验证 PASS**：`run-20261006-1330-merge-algo-check/`
- **Forge 同病的 artifact 级判定 + 未解释的 Forge 安装 404 + 镜像回退缺口**：`run-20261006-1400-forge-probe/`
- **记录型代理追查 404（已排除镜像改写）+ 确认"代理设置启动时不生效"**：`run-20261006-1430-forge-404-trace/`
- **⚠️ 影响面更正：Quilt 不受影响；受影响的是 Fabric/Forge/NeoForge（含逐条逻辑推演 PASS）**：`run-20261006-1500-loader-blast-radius/`
- **Quilt 实测：命令行含库路径、12 图集、0 崩溃 ⇒ "不受影响"从推演升级为实测**：`run-20261006-1530-quilt-probe/`
- **✅ Java 探测 与 恢复区 = PASS（第 71 轮）**：`JAVA_HOME` 指向真实 JRE 17 时 `GET /api/java` **发现 1 个且有效**
  （`major=17 vendor=Eclipse Adoptium valid=True`）、`javaPath` 正确回显；恢复区：Mod 移入隔离 → `GET …/recovery` 出现 **1 条**
  `{operationId, reason:"mod-delete", status:"quarantined", expiresAt(30 天)}` → `POST …/recovery/{op}/restore` 返回
  `{status:"restored", overwrite:false, conflicts:0, sha256:…}` 且**文件回到 `mods/`**：`run-20261006-2350-java-recovery/`
- **✅ 第三方（authlib-injector/Yggdrasil）账号的地址校验与失败路径 = PASS（第 70 轮）**：`not-a-url` → **400（37ms）** 明确文案；
  `https://example.com/` → **400（553ms）**（但把整段 HTML 透传进文案，见 §5 第 18 项）；**真实公共 Yggdrasil（LittleSkin）+ 假凭据** → **400（1,085ms）**
  且**服务端 `errorMessage` 如实透传**（`用户名或密码错误。Incorrect username…`）；失败后**账号数不变**、启动器仍健康。
  另：authlib-injector 组件的两个下载 URL 实测 **200 / 341,970 B**（官方与 BMCLAPI 镜像），产品按序兜底 ✅（但缺 SHA256 固定，见 §5 第 19 项）：
  `run-20261006-2330-thirdparty-auth/`
- **✅ 资源包 / 光影 的搜索与安装 = PASS（第 69 轮）**：`category=resourcepack`（`faithful` → 20 条）装出
  **`resourcepacks/Faithful 32x - 1.18.2.zip` 10,868,241 B**；`category=shader`（`complementary`+iris → 10 条）装出
  **`shaderpacks/ComplementaryReimagined_r5.9.3.zip` 553,397 B**；两者任务都 `completed/安装完成`、列表接口与 `state` 计数（各 1）一致：
  `run-20261006-2300-resourcepacks/`（⇒ mod/resourcepack/shader **三个分类都有真机证据**）
- **✅ Mod 二分定位（bisect）状态机 = PASS（第 68 轮）**：植入 8 个假 jar、把 `mod5.jar` 当元凶，按语义逐轮回报 —— **3 轮**（=log₂8）定位到 `mod5.jar`；
  `finished`/`giveup` 后 **`.jar=8 / .jar.disabled=0`（文件全部恢复）**；**过期 step 被拒**（409 `该轮已经推进…`）：`run-20261006-2230-mod-bisect/`
- **✅ 服务器列表 + 延迟探测（ping）= PASS（第 67 轮）**：添加（端口字符串正确解析）/列出/**真实 ping 公网服务器**（`mc.hypixel.net` → `可连接 · 2278 ms` + 真实 MOTD/版本/人数，2 秒）/
  未监听端口（`127.0.0.1:1` → `无法连接`，2 秒干净失败）/**重启后仍在**（持久化）/删除（`{"ok":true}`，2→1）/对已删 id ping 得 **400 `服务器不存在`**：
  `run-20261006-2200-servers/`。另附**配置写入点静态审计**：27 处 `config.X =` 全部落在会调 `SaveConfig` 的路径上（`PatchConfig`/`PatchAiProviders` 由 `HandleUpdateConfigAsync` 统一保存）⇒ **无"改了却不落盘"**
- **🔴 全新安装下"默认游戏"实例不可寻址（blocker，第 65 轮实测）**：无 `config.json` 时每次 `/api/state` 给默认实例**换一个 id**（连续三次都不同），
  针对它的 `/launch`、`/prelaunch-check` 全部 **400 实例不存在**；落盘一次（建离线账号）后 id 稳定、prelaunch 200。
  根因：`ConfigStore.LoadAsync()` 造完默认实例**不落盘**（`GameInstance.Id` 是新 GUID）；界面启动流程只做 GET ⇒ **"全新安装直接点启动"必然失败**：
  `run-20261006-2100-default-instance-id/`
- **✅ 从列表移除 ↔ 挂回 也验清楚了（第 64/65 轮）**：`POST …/delete` 是**只改配置**（`{"ok":true,"removedId":…}`，`versions/1.5.2/1.5.2.jar` 5,564,661 B 完好）；
  `register` 的重复/不存在目录都给 **400** 明确文案；第一版的"FAIL"是被上面的默认实例缺陷造成的**假失败**：`run-20261006-2030-instance-register/`
- **✅ 镜像源模式（BMCLAPI）PASS（第 63 轮）**：`POST /api/sources/select {"key":"bmclapi"}` → `selected=bmclapi` → 装 1.5.2 **completed**（126 秒），
  `client.jar` 5,564,661 B **sha1 与官方逐字一致**、assets **468 个**；代理日志确认流量走了**镜像池**（bmclapi2/sakura/cernet/mcbbs 等 15 个主机）。
  **观察**：`POST /api/config` **不处理 `downloadSource`**（返回 `{"ok":true}` 但什么也没改——界面用的是正确端点 `/api/sources/select`）；
  镜像模式下仍有少量官方主机（`libraries.minecraft.net` 等）⇒ 发布说明**别写"完全不走官方"**：`run-20261006-2000-mirror-mode/`
- **✅ 内容子系统（搜索 5 类 + 真实装 Mod + 任务列表）PASS（第 62 轮）**：Modrinth mod/modpack/资源包/光影搜索分别返回 18/20/20/10 条真实数据，
  CurseForge 搜索 24 条（首个 id `238222`=JEI）；`POST /api/content/install` 真把
  `sodium-fabric-mc1.18.2-0.4.1+build.15.jar`（1,318,645 B）装进实例 `mods/`，任务报 `completed/安装完成`：
  `run-20261006-1930-content-subsystem/`（**未定项**：便携目录里没有 CurseForge Key 却能拿到 CF 数据，与"直连无 key=403"不一致，未定因、不下结论）
- **✅ 更新链全部走通，含"重启后新进程提供服务"（第 59–60 轮）**：HTTP 清单被接受 → `check` 报 `updateAvailable` → 下载（后台任务，轮询到 `completed`）→
  **EXE 被替换且 sha256 与清单逐字相同** → 原进程退出 → **触发重启**（1 秒后有进程写 WebView2 配置）；
  `apply-state.json={"result":"applied","version":"1.0.1"}`。**重启后的进程没活下来**：我选的产物是**已发布 v1.0.0**，
  而它**只交付 EXE 时启动即崩**（`SkiaSharp … DllNotFoundException`）——对照实验证明**候选 EXE 在同样条件下正常启动**，
  且**把候选自解压出的 3 个原生库放到 v1.0.0 旁边它就能启动** ⇒ **独立复现了原始评审的 G07/R01，并确认候选的打包修复有效**：
  `run-20261006-1800-update-chain/`（第 60 轮补完：把候选自解压出的 3 个原生库预置到目标目录后，
  重启起来的 v1.0.0 进程**确实起来并监听了 API 端口**——产品日志 `=== Pineglow Launcher 启动 v1.0.0 ===` + `[LocalApi] 监听 …`，
  脚本侧 `/api/health` 200。真实更新里新版本产物自带原生库，预置只是测试夹具）
- **⚠️ 导入取消的清理不干净（中严重度，第 58 轮实测）**：取消被接受但工作继续、半成品目录与临时上传目录都留下、
  客户端拿到"连接被关闭"而非 409；tombstone 正常：`run-20261006-1730-import-cancel/`
- **✅ R06「多实例并发」由 NOT_RUN → PASS**：两个不同实例同时启动（pid 31644 / 24084，60 秒全程存活），
  重复启动同一实例被 **400**（`该实例已在运行（进程 31644）`），杀掉后守卫解除（再启动 `{"ok":true,"pid":15280}`）——
  且守卫是**按实例**而非全局单例：`run-20261006-1700-multi-instance/`
- **✅ R06「进入世界 / 加载存档」由 NOT_RUN → PASS（不用 GUI）**：官方 1.20.1 服务端生成真实存档（server.jar sha1 与官方一致）
  → 放进实例 `saves/` → 用产品自己的 `CustomGameArgs=--quickPlaySingleplayer QuickWorld` 启动 →
  日志出现 `Starting integrated minecraft server`、`Preparing spawn area`、**`WorldTester joined the game`**、12 图集、0 崩溃；
  退出后 `totalPlaySeconds=153`：`run-20261006-1630-world-entry/`
- **🔴 Forge 安装：B2 已修好、B2b 是真缺陷，但端到端被环境卡住（第 77–78 轮）**：双补丁构建导入仍 `ExitCode=1`（441 秒）；两组对照显示安装器自身 HTTPS 下载报
  `SSLHandshakeException`（**ASCII 与中文路径报错完全相同** ⇒ 不是中文路径问题）。⇒ 两个产品缺陷已定位且补丁就绪，但"Forge 装得上"本机验不了：`run-20261006-2900-patched-forge-fixed/forge-fixed-report.md`
- **🔴 Forge 安装的第二半缺陷（第 77 轮，在打过补丁的构建上发现）**：B2 的 URL 修复**确认有效**（404 消失），但安装器因**缺 `launcher_profiles.json`** 以 ExitCode=1 退出；
  写一个 46 字节最小桩后**同一安装器 exit 0**（`Successfully installed client into launcher.`）⇒ 这是同一功能上的**第二个** blocker 级缺陷（补丁已备）：
  `run-20261006-2800-patched-forge/forge-installer-B2b.md`
- **✅ 补丁构建的回归：Vanilla 与 Quilt 都正常启动（第 76 轮）**：**Vanilla 1.18.2** 命令行 7,412 字符**含库路径**（并含 `-Dminecraft.launcher.brand`），
  `lwjgl=1 sound=1 atlas=10 崩溃=0`（6/6 PASS）；**Quilt**（真实包 `ukupvp`）命令行 18,352 字符含库路径，`loader=3 lwjgl=1 sound=1 atlas=12 崩溃=0`。
  ⇒ 三种启动路径（Vanilla/Fabric/Quilt）在补丁构建上**全部正常**，而候选上 Fabric 必崩：`run-20261006-2700-patched-regressions/`
- **✅ B1 的端到端证据：打过补丁的构建下 Fabric 实例真的跑起来了（第 75 轮）**：同一个真实包（Fabulously Optimized 1.18.2）——
  候选**约 10 秒崩溃**（`Failed to locate library: lwjgl.dll`、208 行日志），**补丁构建**命令行含 `-Djava.library.path`（10,561 字符、主类 `KnotClient`）、
  `Loading Minecraft 1.18.2 with Fabric Loader 0.19.3`、**`Backend library: LWJGL`**、**`Sound engine started`**、**10 个图集**、**0 崩溃**、1,335 行日志、JVM 存活整个观察期（逐项 7/7）：
  `run-20261006-2600-patched-fabric-launch/patched-fabric-report.md`
- **✅ 三个 blocker 的补丁已在"真实打过补丁的构建"里复现验证通过（第 74 轮）**：仓库副本（`%TEMP%`，**不动工作区**）→ `git apply` 四个补丁（**0 警告 0 错误**）→
  **B0**：`/api/state` 连续三次 id **完全相同**、`/launch` 由"实例不存在"变为"实例未关联游戏版本"（id 已正确解析）；
  **B1**：合并后 `arguments.jvm` = `[库路径, -cp, class_path, FabricMcEmu]`（父版本库路径保留）；
  **B2**：URL 单段、GET **200 / 6,068,826 B**；**并且打过补丁的副本 C# 全量测试 = 904/904 全绿**（903 + 新增回归测试）：
  `run-20261006-2500-patched-build/patched-build-report.md`
- **✅ 打过 6 个补丁的候选：全量门槛的 11 个可执行步骤全部 PASS（第 80 轮）**：Node **304/304**、**C# 904/904**（含新增回归测试）、锁定还原、VerifyHost+BisectSmoke、publish、**独立冷启动 19/19**；
  汇总里的"失败项 2"经查是**我手工副本的产物**（未跟踪杂项 `scripts/run-dify-minimax-workflow.ps1` 被 `git add -A` 纳入；快照文件数期望值按副本自身算 512 而实际 363）
  —— 工作区正式跑（第 73 轮）是 GREEN/0。⇒ **补丁不破坏门槛**：`run-20261006-3000-patched-final-gate/final/patched-gate-report.md`
- **✅ 全量验证链复跑：GREEN / 失败 0，且发布产物逐字节一致（第 73 轮）**：Node **304/304**、C# **903/903**、UI 闭包/清单双向一致、
  锁定还原、VerifyHost + BisectSmoke、publish、**独立冷启动 19/19**；重新 publish 出的单文件 EXE = **53,052,722 B / sha256 `5009cf73…`**（与先前完全相同）
  ⇒ **冻结候选未被 40 轮测试碰坏且可复现**。唯一报错是可选清理步骤撞上 `VBCSCompiler` 占用（**非验证失败**）；顺带清掉 `%TEMP%` 里 **12.15 GB** 残留快照（C: 105.9 → **117.1 GB**）：
  `run-20261006-2400-final-refresh/final/final-refresh-report.md`
- **✅ 第 89 轮：整份复验结论已出** ⇒ `D:\松光启动器\docs\FINAL-VERIFICATION-2026-10-07.md`：**门槛 GREEN/0**（Node 305 / C# 917 / 冷启动 19/19）、
  **Vanilla/Fabric/Quilt/Forge/NeoForge 五种启动在"打过两处补丁的构建"上全部 PASS**（NeoForge 由产品自己选到 Java 21、Forge 选 Java 17）；
  但**当前工作区未含这两处补丁** ⇒ **按现状仍不可发布**（Forge/NeoForge 启动 + MC 1.21+ 实例不可用）。补丁：`run-20261007-3800-…/proposed-fix-forge-launch-assembly.diff`、
  `run-20261007-4200-…/proposed-fix-java-version-selection.diff`（两者一起编译 0 警告 0 错误）。
- **🔄 第 83 轮：4 处修复已实际落到工作区（另一会话、用户已授权）；独立复验见** `release-remediation-evidence-2026-10-05/run-20261006-3200-independent-verify/independent-verify-report.md`：当前源码门槛 **GREEN/失败 0**（Node 305、C# **917/917**、冷启动 19/19）、**Fabric 启动 PASS**、**Forge 安装器手工实测 exit 0**（推翻此前"环境阻断"）、Vanilla/Quilt 待重跑；**旧 6 份 proposed-fix 补丁已被取代**。⚠️ 本复评第 80 轮之前的"三个/四个 blocker 待授权"表述按此更新。
- **📌 决策包（一页拍板；**第 75 轮已刷新**：范围 A + **四个 blocker（B0/B1/B2/B2b）** 的补丁集与 9 步清单 + **20 项决定** + 三项授权 + 刷新的"已验证 vs 未验证"）**：`run-20261006-1600-decision-package/DECISION-PACKAGE.md`
- **📦 补丁集（**6 份**，全部 `git apply --check` ✅）**：`run-20261006-2130-patch-set/PATCH-SET.md`
- **发布说明草案已按最新结论更新**（§3 新增加载器/Java/代理三条已知限制；§6 改为"已验证 vs 仍 NOT_RUN"）：`run-20261006-0710-release-notes/release-notes-draft.md`
**原始评估（本 DeliveryRoot: `release-readiness-evidence-2026-10-05/`）**：`exe-only-crash.log`、
`exe-only-summary.json`、`snapshot-run.log`、`snapshot-exitcode.txt`、`actionlint-tool.json`、
`publish-files.json`、`ci-coverage.json`、`resource-coverage.json` 等。

## 8. 自评（按 agent-self-evaluation 五轴；非放行依据）

| 维度 | 分数 | 证据 / 改进 |
| --- | --- | --- |
| 准确性 | 4/5 | 每条状态都指到可复跑证据；但对"原始现场是否存在"的判断错过一次（只翻了一个目录），已更正并写明原因 |
| 完整性 | 4/5 | 原始 G01–G14 与 R01–R06 逐条复评；R06 真实环境部分仍缺 |
| 清晰度 | 4/5 | 区分"已关闭/部分/NOT_RUN/需授权"；两个相近目录名已单列说明 |
| 可执行性 | 4/5 | 未通过项都给了解锁条件；候选提交与发布动作已就绪但不能自授权 |
| 简洁性 | 3/5 | 证据链完整但文档较多（11 轮整轮验证 + 24 份专项/阶段证据，共 49 个证据目录）；如需对外可再压缩成一页结论 |

平均 3.8/5。**本文件不构成放行**；`TechnicalAcceptance` 与 `ReleaseApproval` 的两个 NOT 状态只有补齐
§4 的解锁条件并由用户明确批准后才能改变。