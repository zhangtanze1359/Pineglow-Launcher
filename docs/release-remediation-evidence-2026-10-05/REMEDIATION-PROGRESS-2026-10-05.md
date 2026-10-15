# 松光启动器发版前收口 · 进度报告（R01–R06）

日期：2026-10-05，Asia/Hong_Kong。来源线程：`codex://threads/01a10b15-0f70-7b21-9ec9-c6526072940f`。

> **本文件是"过程与证据"记录。两份"结论/执行"级文档在 DeliveryRoot：**
> `C:\Users\Administrator\.codex\worktrees\e423\松光启动器\docs\release-readiness-reassessment-2026-10-06.md`
> （逐条复评原始评估的 G01–G14 与 R01–R06，并列出仍未通过与需要授权的事项）与
> `…\docs\release-readiness-runbook-2026-10-06.md`
> （**发布执行手册**：前置不变量、必跑命令与期望输出、提交/推送/发版顺序、发布后真机项、中止条件、脚本速查）。
> 注意区分两个相近目录：原始证据在 `release-**readiness**-evidence-2026-10-05/`（DeliveryRoot），
> 本文件所在的是 `release-**remediation**-evidence-2026-10-05/`（SourceRoot 的 docs）。
执行基线：SourceRoot `D:\松光启动器`，main，HEAD `f145d14eb26b0b7459a28491e764ac8c573085d3` + 当前脏源码（161 tracked + 146 白名单项）。

## 1. 结论

评测报告里的 6 类阻断项中，**4 类（R01/R02/R04/R05）已关闭**，并且**都配了可复跑的门槛**；
R03 的**客户端侧已关闭**（含真实端点复验），只剩发布侧的写动作；R06 已完成**三段**真机验收，
其余部分需要真实账号/游戏环境与发布授权。因此**本轮仍不能宣布可发布**：`ReleaseApproval` 未获授权。

| 阻断项 | 本轮状态 | 关键证据 |
| --- | --- | --- |
| R02 最新视觉资源漏进候选源码 | **已关闭** | 资源闭包检查 9 条引用 / 缺失 0；门槛自证 7/7 |
| R05 快照验证步骤 PASS 失真 | **已关闭** | 步骤运行器自证 17/17；中文+空格路径、非零退出、超时、零输出、日志缺失、零计数全部按预期失败 |
| R01 发布包不完整（单独 EXE 冷启动崩溃） | **已关闭（核心）** | 单文件附件在全新中文/空格目录冷启动：真实窗口 `松光启动器`、`/api/health` 200、10 项界面资源 200 + MIME 正确、内嵌 UI 解包并与候选源码逐字节一致；19 项检查全通过。本轮审计又补齐了该包流程要求的**许可证随 Release 交付**与**运行环境声明**（此前漏做） |
| R04 CI 不覆盖当前测试、tag 发布无测试互锁 | **已关闭（本地可验证部分，含 VerifyHost 冒烟）** | 清单双向一致（23 个 node 测试）；Node 304/304、C# **903/903**；VerifyHost `--bisect-smoke` 真跑且凭据后端隔离（生产调用 0 次）；release.yml 新增 `verify` 作业并被 `release: needs: verify` 依赖 |
| R03 默认更新清单 404 | **已关闭（含真实端点验证）** | 客户端降级 18 项专项测试 + **真实端点验收**：默认主源 Gitee 实测 404 → 真实 GitHub Release `manifest.json` 备用源降级成功；发布侧新增清单一致性门槛，**且该门槛已用本地干跑 18 例（含逐条 RED）证明会咬** |
| R06 最终附件真机验收 | **部分完成；修复 1 个真实缺陷 + 记录 1 个既有观察项** | 首启/资源/live API 19 项全过；更新替换脚本 **7 项**真实验收（含"无权限"的两种形态）；**九页界面运行时验收**在最终产物上三次运行（2 次 9/9 PASS、1 次因上游版本接口 30s 超时非通过，已定位为既有错误契约问题），异常 0 / console.error 0 / 本机资源失败 0；**真实 HTTPS 下载 + SHA256 + 篡改拒绝**实测通过；过程中发现"替换脚本在无控制台环境下永久卡死"并修复 |

`TechnicalAcceptance=NOT_PASSED`（R06 未闭、R03 的线上部分未验）、`ReleaseApproval=NOT_GRANTED`（未获授权）。本轮未提交、未推送、未发布、未改真实配置。

## 2. 已关闭项的改动与门槛

### R02 · UI 本地资源闭包

- 把此前**既未 tracked 也不在白名单**的两个最新视觉资源精确纳入：`ui/css/pineglow.css`、`ui/assets/pine-forest.svg`（`scripts/source-snapshot-inputs.txt` 类别 L）。
- 新增 `scripts/check-ui-resource-closure.cjs`：从 HTML 入口出发解析 HTML/CSS/JS 本地引用闭包，
  过滤远程/data:/锚点，逐项断言存在；缺资源即 exit 1。`vendor/` 第三方压缩产物不做字符串启发式扫描（避免假阳性）。
- 接线：快照验证脚本步骤 3/7（含 JSON 报告 missingCount 断言）、`build.yml`、`release.yml verify`。
- 门槛自证 `scripts/tests/ui-resource-closure.test.cjs`：7 项，含中文/空格路径与删除 CSS 间接依赖的 RED。
- **第 15 轮补记一个此前没写下来的前提**：`ui/assets/pine-forest.svg` 之所以**真的 add 得进去**，
  靠的是 `.gitignore` 第 37 行的反选规则 `!ui/assets/pine-forest.svg`（第 36 行是 `ui/assets/*`）。
  这条规则是本次修复落地的一部分：删掉它，白名单里虽然仍列着这张图，但 `git add` 会被忽略规则拒绝或静默跳过，
  而**本地构建照旧通过** —— 正好是 R02 原始缺陷的形态（"本地好、候选缺"）。
  已在候选提交计划 §5.1 与 `run-20261006-0250-candidate-set-audit/` 记录。

### R05 · 可信验证运行器

- **留存的原始 RED**（`run-20261005-1735-r02r05/R02-R05/verifier-console.log`）：日志里脚本内的中文
  变成 `鐧藉悕鍗曡嚜妫€` 这类乱码（UTF-8 字节被按 GBK 读的典型形态，说明该 .ps1 当时没有 UTF-8 BOM），
  宿主 Windows PowerShell 5.1 因此解析失败 → `Unexpected token …`、`Missing closing '}' in statement block` …
  最后 `Not all parse errors were reported.` —— **验证器一行证据都没产出**
  （该目录里只有一个控制台日志，没有 verify-summary）。
- **机制复现（本轮补，`run-20261006-0005-r05-red-repro/`）**：原始实现已被重写、现场无法重放，
  所以这里只复现机制并明确标注：用 `Set-Content -Encoding ascii` 把含中文 EvidenceDir 的命令写进 .cmd，
  中文被写成 `D:\?????\...\?? ?? ??\step-output.log`，cmd 报
  `The filename, directory name, or volume label syntax is incorrect.`、**期望日志不存在**。
  **必须如实说明**：这次复现里 cmd **自己返回了 1**，所以"日志没产生却被记成 exit 0 / PASS"那一步，
  问题出在**旧包装器的门槛**（没有把"日志是否存在/是否有输出"当成事实）。
  **而那个误判的原始现场其实是留存的**（第 17 轮才找到）：原始评估的证据目录
  `docs/release-readiness-evidence-2026-10-05/snapshot-run.log` 第 52–95 行有 **8 条
  `exit=0 status=PASS 0.0s`**（0.0 秒、日志名在列却没有产生），整轮直到"单文件发布产物缺失"才失败
  （`snapshot-exitcode.txt`=1）。所以这条不是"不可追回"，是我一开始只在 D 盘源码树里找。
- 因此"误判"不靠现场、改为**正向断言**：`scripts/tests/step-runner.test.ps1` 的 17 项里
  SR-05「零输出不得判为通过」、SR-06「日志缺失门槛失败」、SR-07「零测试必须失败」、
  SR-08「缺少必需证据时门槛失败」逐条断言这些失败形态必须**非零失败**
  （`step0-step-runner-selftest.log`，最近一次 17/17）。
- 新增 `scripts/lib/StepRunner.psm1`：命令文本经 CreateProcessW 以 Unicode 传入（`cmd /d /s /c "<命令>"`），
  工作目录用 `ProcessStartInfo.WorkingDirectory`，日志按字节重定向；退出码、超时、日志缺失、零输出、零计数都是可断言事实。
  超时按**进程树**终止（实测：只杀 cmd.exe 会把 5 秒超时拖成 60 秒）。
- `scripts/verify-source-snapshot.ps1` 的 `Invoke-VerificationStep` 改为「事实采集 + 门槛判定」分离，并新增：
  退出 0 但日志缺失/零输出/零计数/缺必需证据时**判 FAIL**（这正是旧实现把「没跑」记成 PASS 的位置）。
- 新增「脚本编码守卫」：含非 ASCII 却无 UTF-8 BOM 的 .ps1/.psm1 判为快照缺陷（本机宿主是 Windows PowerShell 5.1，会按 ANSI 解析错乱）。
- 门槛自证 `scripts/tests/step-runner.test.ps1`：17 项断言（SR-01…SR-09）。
- 附带发现并修正：门槛证据串必须**与语言环境无关**。第一版用 `Build succeeded` / `Passed!` 做门槛，在本机中文 .NET CLI 下
  把 exit=0 的正确构建误判为「缺证据」——新门槛改用 `已成功生成|Build succeeded`、`(?:Failed|失败): 0`、`(?:Passed|通过): (\d+)`。

### R01 · 单文件发行自足

- 根因：Release 以 `PublishSingleFile=true` 发布，但**原生库默认不进单文件**，UI 一直是 EXE 旁外置目录；
  工作流只上传 EXE + manifest → 新用户冷启动缺 Skia 原生库直接退出。
- 改动：
  - `Pineglow.Launcher.csproj`：`PublishSingleFile` 时启用 `IncludeNativeLibrariesForSelfExtract`；
    UI 以 `EmbeddedResource` 嵌入程序集（14 项）；`PublishSingleFile` 时不再外置 `UI/`；新增嵌入资源非空断言目标。
  - 新增 `Pineglow.Launcher.Services/UiBundle.cs`：按「版本 + 内容指纹」解包内嵌 UI，
    先写临时目录再整体改名；解包不可用（缺文件/文件数不符）会重新解包修复；非单文件宿主不解包。
  - `LocalApiService.ResolveUiRoot()` 增加解包回退（顺序：env → EXE 旁 UI/ → 解包目录 → 配置目录 UI/），
    并暴露 `UiRoot`；`WebShellWindow` 的静态回退改用它。
  - `release.yml`：新增「发布目录必须只剩单个 EXE」断言（清理原生 PDB）、
    以及「只复制该 EXE 到全新目录冷启动」的发布前必过步骤。
- 新增门槛 `scripts/verify-single-file-standalone.ps1`：冷启动目录只放 EXE + portable.flag，
  断言无外置 UI/ 与 DLL、进程存活、真实主窗口、`/api/health`、10 项资源与 MIME、
  解包目录与候选源码逐字节一致（防「新服务配旧界面」）。
- 新增聚焦测试 `dev/PineglowLauncher.Tests/UiBundleTests.cs`（8 项）：嵌入清单/入口闭包/解包字节一致/幂等与自愈/非单文件宿主不受影响。
- **流程步骤 3「排除非必要 PDB，保留许可证与必要资源，声明系统/架构/WebView 条件」——本轮审计时发现没做完，已补**：
  - 正式 Release 此前只挂 `EXE + manifest.json`，**从未交付许可证文本**（GPL-3.0 要求向他人交付二进制时随附）；
    已改为在 `release.yml` 的发布步骤补挂仓库根的 `LICENSE`
    （第三方许可证不走这条路：`ui/vendor/THREE-LICENSE.txt` 随 `ui/**` 一起嵌入程序集，
    目录构建另有 csproj 目标断言它在输出目录存在）。
    结构校验：PyYAML 解析通过、发布步骤 `files` 实测为 `EXE + manifest.json + LICENSE`、`LICENSE` 存在、无制表符缩进。
  - `README.md` 补「运行环境要求（正式附件）」表：Windows x64（本轮验收环境 Windows 11 x64）、
    **WebView2 Runtime 必需**、不需要 .NET 运行时、原生图形库已内嵌，并保留"不可视为正式跨平台发布支持"的限制说明。
  - 二进制未受影响：这两处改动都是文档/工作流，重跑整轮验证后单文件 EXE 仍为
    53,052,722 B / sha256 `5009cf73…`（含此后共八轮整轮验证，该 EXE 始终逐字节相同）。
- **发布配方本身也本地跑过了（第 14 轮，`run-20261006-0210-publish-recipe/`）**：
  `release.yml` 真正用的那条 publish 命令带 `/p:Version=${{ steps.version.outputs.version }}`，
  而此前所有本地验证（包括快照验证器自己的 publish）**都不带**它 —— 也就是"要发布的那条配方"从没跑过。
  本轮按原样执行（测试版本 9.9.9）：产物 53,093,682 B / `79beb39c…`，再由 R01 的原始 RED 复现动作
  `verify-single-file-standalone.ps1` 独立验收 → **18/18 PASS**：真实窗口 `松光启动器`、
  冷启动目录无外置 `UI/`、内嵌 UI 解包到 **`9.9.9-5f6b42a61753`**、解包界面与候选源码逐字节一致。
  **版本键控解包在非 1.0.0 版本下也正确**（目录名带新版本 + 12 位内容指纹，不是复用旧目录）——
  这是此前完全没有的覆盖。
  顺带用实测更正了 csproj 里一句不准确的话：`CopyOutputSymbolsToPublishDirectory=false`
  **并不能**清掉原生 PDB（三次发布全都带 libSkiaSharp.pdb 84MB + libHarfBuzzSharp.pdb 20MB），
  真正让发行物干净的是 `release.yml` 里的显式删除；注释已按实测改写。
- **冷启动门禁的环境隔离加固（第 18 轮，`run-20261006-0400-standalone-env/`）**：
  原始评估对 R01 的要求明确写了"不依赖开发目录/历史 UI/DLL/**`PINEGLOW_UI_DIR`**"，
  而产品解析 UI 根的顺序是 env(`PINEGLOW_UI_DIR`) → EXE 旁 `UI/` → 内嵌解包 → 配置目录，**env 优先级最高**；
  检查脚本过去只设 `PINEGLOW_API_PORT`、**从不清除继承来的 `PINEGLOW_*`**。
  实测（WorkRoot 放在 `%TEMP%`，否则会被已知沙箱现象干扰）：把 `PINEGLOW_UI_DIR` 指向一个**有效的替身 UI** 时，
  修前的门禁会因为"内嵌 UI 根本没解包"（`dirs=` 为空）而**失败** —— 也就是说
  **不存在假通过漏洞**；真实缺陷是**门禁对环境的脆弱性 + 失败归因误导**
  （只报 `health=` 为空或 `dirs=` 为空，完全看不出"你的环境里带了开发旋钮"）。
  已改为启动前清除继承的 `PINEGLOW_*`，并新增第 19 项检查
  `冷启动不继承开发旋钮（PINEGLOW_UI_DIR 等已清除）`（detail 列出实际清掉了什么）。
  新增项当场就抓到本会话泄漏进来的 `PINEGLOW_WEBVIEW_DEBUG=9222` —— 漂移是真实的，不是假想。
  边界：只覆盖这两个变量；另外第一版实验把 WorkRoot 放进工作区导致 `HttpListener` 起不来，
  那几轮**已作废并留档**（不要把沙箱现象读成环境变量导致的失败）。

### R04 遗留项收口 · VerifyHost 冒烟现在真跑，且凭据后端被隔离

独立验收当初指出两点：① 这个端到端冒烟**从未被真正运行过**；② 它只做 portable 沙盒，
**不能证明 Windows 命名凭据没被动过**（portable 隔离的是 JSON 配置，不是凭据管理器）。

本轮把两件事一起解决：

- `VerifyHost/BisectSmoke.cs` 在启动前把 `SecretStore` 换成纯内存后端
  （`InMemorySecretStoreBackend`，产品内的既有类型），收尾时断言
  `SecretStore.ProductionBackendCallCount == 0`——把"没碰真实凭据"从声明变成判定。
- 实测（`run-20261005-2055-verifyhost`）：`[BisectSmoke] PASS`，20 项全过，其中
  `credential-isolation: production credential backend calls must be 0 (actual 0)`；
  同时隔离后端**接住了 34 次操作、其中 4 次是删除意图**——这正说明：
  没有隔离时，这个冒烟会真的对 Windows 凭据管理器发起 34 次调用（含 4 次删除）。
- 该冒烟已作为**常驻门槛**接入快照验证器（步骤 5/7 之后），门槛同时要求
  `[BisectSmoke] PASS` 与 `credential-isolation: production credential backend calls must be 0`
  （标签刻意用纯 ASCII：历史上中文标签在不同控制台编码下曾导致门槛误判）。
- `VerifyHost/Program.cs` 同时把控制台输出固定为 UTF-8，避免被采集的日志变成乱码。

**既有事实（不是本轮造成，也未清理）**：本机真实凭据管理器里存在 4 条与本项目相关的**历史条目**
（`MCMiniLauncher/AccountToken_*` ×2、`AiKey_*`、`ProxyPassword_*`，只记录目标名，不含密钥内容）。
它们可能来自隔离缺陷存在期间的历史测试/真实使用；按现场保护约定**不删除、不修改**，
是否清理由用户决定。证据：`run-20261005-2055-verifyhost/verifyhost/windows-credential-targets-pre-existing.txt`。

### R04 · 测试清单与发布互锁

- 新增 `scripts/test-inventory.txt`（唯一权威清单）+ `scripts/run-inventory-tests.cjs`：
  `--check` 做**双向**断言（清单项必须存在；磁盘上的 `*.test.cjs` 必须都登记），`--list`/`--json` 供 CI 与证据使用。
- `build.yml`：删掉硬编码的 11 文件列表，改为「清单校验 + 按清单运行」（23 个 node 测试文件）。
- **本轮工作流引用审计又补一处**：`build.yml` 的 R02/R04 两份 JSON 报告此前只写进 runner 工作目录，
  随作业一起消失，只能从控制台日志看（而 `release.yml` 的同名证据一直是上传的）。
  已补 `upload-artifact`（`if: always()` —— 门槛失败时才是最想拿到报告的时候）；
  审计脚本复跑 exit 0（证据 `run-20261006-0040-wf-audit/`）。
- `release.yml`：新增 `verify` 作业（锁定还原、UI 闭包、清单校验、Node 全套、C# 全套），`release` 作业 `needs: verify`，
  tag 无法绕过同 SHA 测试。
- 本地 RED/GREEN 已验证：删清单项、加不存在的项都 exit 1。

### R03 · 更新清单入口与降级

- 根因：默认主源（Gitee 仓库固定 `update.json`）实测 404，而旧实现只认这一条地址、取不到就整体失败；
  资产级 `fallbackUrl` 救不了这一层（它只有在**已经拿到清单**之后才有意义）。
- 改动：
  - `ConfigStore.DefaultUpdateMirrorUrls`：新增内置备用源 —— GitHub Release 的固定 asset 直链
    `/releases/latest/download/manifest.json`（release.yml 每次发版都上传，地址不随 tag 变化，无需额外维护仓库文件）。
  - `UpdateService`：`BuildManifestUrlChain`（用户自定义源优先 → 内置主源 → 备用源，按序去重、只接受 http/https）、
    `FetchManifestChainAsync`（逐源尝试，**单次尝试 15s 超时**，主源卡住不吃掉备用源预算）、
    `ValidateManifest`（version / HTTPS url / SHA256 / size 四项校验，坏清单当"这个源坏了"而非等到下载才失败）、
    `DescribeManifestFailure`（404 / 403 / 429 / 超时 / 坏 JSON / 字段非法 / 网络错误分类）、`Redact`（日志与响应去掉 query，避免泄漏 token）、
    `ManifestFetchResult.FailureSummary`（"主源 HTTP 404（清单不存在）；备用源1 HTTP 500"这类可判断摘要）。
  - `LocalApiService`：检查更新与开始下载**走同一条链**，响应新增 `manifestSource` / `usedFallback`，
    失败时给 `error`（分类摘要）+ `manifestSources`（逐源 ok/reason）。界面 `ui/js/launcher.js:3754` 本来就直显 `r.error`，
    因此无需改前端，也不影响既有 21 个前端测试文件。
  - `release.yml`：镜像步骤把结果如实落盘 `dist/mirror-status.json`（缺 token / 附件上传失败 / 返回地址非 HTTPS /
    update.json 写入失败各自写清后果并打 `::warning::`），**不再出现"跳过了却说更新了"**；
    新增「清单与最终附件一致」门槛（version 对 tag、url 为 HTTPS 且指向本次 win-x64 资产、
    sha256 为 64 位十六进制且与实际文件一致、size 与附件一致）。
    **该门槛与产物形态/版本门槛已由本地干跑 18 例（逐条 RED）验证会咬**，见 §2 的干跑小节。
  - **镜像步骤的失败路径已干跑复验**（`run-20261006-0140-gitee-mirror-dryrun/`，14 项断言全过）：
    缺 token / 假 token / API 不可达（故障注入）三种情形下，它都**没有**把 `updateJsonUpdated` 记成真，
    也**没有**把无效地址写进 manifest（三种情形 `manifest.url` 都仍指向 GitHub）。
    **并且因此查出一个真实缺口**：API 不可达时步骤在写状态文件**之前**就 `throw`，而工作流里紧接着的
    `Upload mirror status` 是 `if: always()` + **`if-no-files-found: warn`** —— 于是**失败了却没有任何结构化证据**，
    只剩一句控制台报错（"有没有上传、manifest 有没有被改写、要不要依赖备用源"全都无从查证）。
    已修为「先如实落盘（note 写清三项'没做'与原因）+ 打 `::warning::`，再照旧 `throw`」：
    失败可见性不变（该步骤另有 `continue-on-error: true`，不阻断发布），但证据不再丢失。
- 新增聚焦测试 `dev/PineglowLauncher.Tests/UpdateManifestFallbackTests.cs`（服务层 16 项 + API 层 2 项，共 18 项，
  本地假 HTTP 服务器驱动）：主源成功不降级；404 / 坏 JSON / 字段非法 / 超时四类各自降级成功并给出可读原因；
  全失败时摘要含各源原因；地址链组成与去重；字段校验与脱敏；调用方取消**不**降级；`/api/update/check` 的
  `manifestSource`/`usedFallback`/`manifestSources` 契约。
- 清单协议兼容矩阵（模式 `{version,url,fallbackUrl,size,sha256,notes,mandatory}`，主备两端由同一 release.yml 产出，形状一致）：

| 客户端 | 清单来源 | 本轮变化 | 需要的外部动作 |
| --- | --- | --- | --- |
| ≤ 1.0.0（已发布） | 硬编码 Gitee `raw/master/update.json`，无备用源 | 无法修改旧代码 | **必须**让该 URL 继续返回有效清单（Gitee 仓库 master 建/更 `update.json`），否则旧客户端永久无法自更新 |
| ≥ 本轮修复版 | 用户自定义源 → 内置主源（Gitee）→ 备用源（GitHub Release manifest.json） | 新增降级与逐源原因 | 无需额外动作：GitHub Release 每次发版自动带 `manifest.json` |

**第 24 轮把"旧客户端那一行"从推断升级为源码级核对**（`run-20261006-0600-v100-migration/`）：
仓库里就有 `v1.0.0` tag（公网 Release 的 Actions `head_sha` 也能在本地查到），于是用
`git show v1.0.0:…` 直接读了**已发布客户端**的代码，实测：

- `UpdateManifest.cs` 与工作区**逐字节相同**（归一化换行后 sha256 同为 `C47C569B3CF2625D`，且该文件在工作区未被改）
  ⇒ **协议契约没变**；
- 它解析清单用 `PropertyNameCaseInsensitive = true`，且**没有** `ValidateManifest`
  ⇒ 我们写出的 camelCase 清单它读得懂，且它不会比我们更严；
- 它的默认源就是那个 Gitee `update.json` ⇒ **待授权的写入正是它唯一的入口**；
- 它下载时按 `Url` → `FallbackUrl` 回退 ⇒ 清单里带 `fallbackUrl` 对旧客户端确实有用；
- ⚠️ **但它的替换脚本用的是 `timeout /t 1 /nobreak`、且等待循环无上界**（就是我们已修掉的那个缺陷）
  ⇒ **发布说明必须写明迁移注意**（见 §2 R03 末与发布手册 §5）：
  从 ≤1.0.0 升级时，若启动器是被脚本/父进程（stdin 被重定向）拉起的，更新可能长时间停在"正在应用更新"，
  请手动下载一次新版；资源管理器正常启动的用户不受影响（`timeout` 会立即报错退出、循环空转而仍能完成替换）。

- 待上线动作（**均需发布授权，本轮未执行**）：① 在 Gitee 仓库 master 创建/更新 `update.json`（让旧客户端仍可升级）；
  ② 用真实 HTTPS 复验两条源的真实响应与 `hash/size/version` 一致；③ 若选择放弃 Gitee 这条路，
  需要在 release notes 明确"≤1.0.0 需手动下载升级"。

### R01/R03/R04 · release 作业本地干跑：发布门槛第一次被真正执行（新增）

已有的"真实网络验收"打的都是**客户端**这一侧；而 `release.yml` 的 `release` 作业里那四条纯 PowerShell 门槛
（程序集版本=tag、产物形态、生成 manifest、清单与附件一致）**此前从未被本地执行过**——
只有"提交后 CI 会跑"这句承诺。门槛写错了或者写松了却放行，本地是看不出来的。

新增 `release-job-dryrun.py`（证据 `run-20261006-0110-releasejob-dryrun/`）：用 PyYAML 从
`release.yml` 里按**步骤名**取出真实的 `run:` 正文来执行（不是重写副本），只替换 3 个已知表达式，
**替换后仍残留 `${{` 就拒绝运行该步骤**（宁可报错也不假跑）。

```
RELEASE-JOB-DRYRUN RESULT=PASS cases=18 failed=0
```

覆盖（每条门槛都配 RED）：产物形态——干净放行 / 多余 PDB 与 `UI/` **被清理后放行且实测真的清掉** /
多余文件·多余目录·两个 EXE 各自失败；版本——DLL 与 tag 一致放行、不一致失败；
清单——生成后字段自洽，且 version 不符 / 非 HTTPS / URL 不指向本次资产 / SHA256 不符 / size 不符 / 缺清单**逐条失败**。

**干跑抓到的第一件事是"我的期望错了，门槛是对的"**：我按步骤注释写了"大写 SHA256 必须失败"，
结果门槛放行。查下去是因为 PowerShell 的 `-match` **默认大小写不敏感**，而客户端
`UpdateService` 也用 `Uri.IsHexDigit` + `OrdinalIgnoreCase`，**两端本来就都接受大写**。
所以正确处理是**改注释而不是收紧门槛**（收紧会让发布端比客户端更严，凭空多一种发布失败模式，
而哈希大小写不携带语义）：`release.yml` 的注释与抛出信息已改为"64 位十六进制（大小写不敏感，与客户端一致）"
并写明不要改成 `-cmatch` 的理由，该用例改名 `V5-sha-uppercase-accepted`、期望改为必须放行。

**边界（不得过度解读）**：只跑步骤正文，`actions/*`（checkout / upload-artifact / action-gh-release）一律没跑，
上传与建 Release 未验证；**不等于 actionlint 通过，也不等于 runner 通过**；
本机是 Windows PowerShell 5.1 而 CI 用 `shell: pwsh`（PowerShell 7），**shell 版本差异未覆盖**。

### R03/R06 · 真实网络验收（新增脚本 + 实测结果）

离线测试只能证明"替代环境里的逻辑"，所以补了真实端点验收
`scripts/verify-update-live-download.ps1`（已在 `release.yml` 里作为发布门槛跑第一段）：

| 阶段 | 真实结果 |
| --- | --- |
| A 默认地址链（不写 config，用产品内置默认源） | 主源 Gitee 固定 `update.json` **实测 HTTP 404**；客户端**降级到真实备用源并取到清单**：`manifestSource=https://github.com/zhangtanze1359/Pineglow-Launcher/releases/latest/download/manifest.json`、`usedFallback=true`、`latestVersion=1.0.0` |
| B 真实 HTTPS 下载 + 校验 | 清单由本地夹具提供（http），资产是**真实 Gitee HTTPS** 文件（1618 字节）：下载 `completed`，字节数与 SHA256 与真实文件一致，无 `.part` 残留 |
| C 篡改必须被拒 | 同一真实资产 + 被改写的 SHA256 → `state=failed`，原因 `更新包校验失败`，且不留下任何可用文件或 `.part` |

这一条把 R03 从"逻辑可复验"提升为"**在真实互联网上确实恢复了更新检查**"：
备用源选的 GitHub Release 固定 asset 直链经实测有效，不需要任何新的发布基础设施。

### R06（部分）· 真机验收：首启 + 更新替换 + 九页界面运行时

本轮把三件"必须真跑"的事做成了可复跑验收，并且**在过程中发现了一个真实缺陷、另记录了一个既有观察项**。

**① 首启（正式单文件附件）** —— `scripts/verify-single-file-standalone.ps1`，**19 项**全通过
（第 18 轮起 18→19，见「冷启动门禁的环境隔离加固」）：
全新中文+空格目录只放 EXE + portable.flag；无外置 UI/ 与 DLL；进程存活；真实主窗口 `松光启动器`；
`/api/health` 就绪；10 项界面资源 200 且 MIME 正确；内嵌 UI 解包并与候选源码逐字节一致。

**② 更新替换脚本（新增）** —— `dev/PineglowLauncher.Tests/UpdateApplyScriptPathTests.cs`，**7 项**全通过
（真实执行 `cmd.exe`，只把重启命令换成空操作/写标记文件）：

| 用例 | 覆盖 |
| --- | --- |
| `ApplyScript_WaitsForRunningTargetThenReplaces` | 启动器**正在运行**时必须先等它退出再替换（用一个真实长跑进程占用目标镜像名验证） |
| `ApplyScript_InChineseAndSpacePath_AppliesReplacesAndRecordsApplied` | 中文+空格安装路径：备份 → 替换 → `fc /b` 校验 → `applied`，且**重启那一行真的执行** |
| `ApplyScript_InChineseAndSpacePath_ReplaceFailureRollsBackOldVersion` | 替换失败 → 旧内容原样 + `replace_failed` + 仍然启动旧版本 |
| `ApplyScript_InChineseAndSpacePath_BackupFailureLeavesTargetUntouched` | 备份失败 → 不动目标 + `backup_failed` |
| `ApplyScript_WhenTargetIsNotWritable_KeepsRunnableOldVersion` | **无权限形态一·只读属性**：目标设为只读让 OS 真的拒绝写入 → 不报 `applied`、旧版本原样、有备份、仍然启动旧版本 |
| `ApplyScript_WhenTargetHasExplicitDenyAce_KeepsRunnableOldVersion` | **无权限形态二·显式拒绝 ACE**：`icacls /deny "<用户>:(WD,AD,WA)"`（真·权限，不是文件属性）→ 同样 `replace_failed`、旧版本原样、仍拉起旧版本 |
| `ApplyScript_DoesNotDependOnConsoleOnlyCommands` | 回归位：禁止再用 `timeout /t`，要求等待有上界；**并要求脚本以 `@echo off`+`chcp 65001`（纯 ASCII）开头**——见第 19 轮的代码页验证 |

**"无权限"分支的实测（本轮从 NOT_RUN 转已验，两种形态；证据 `run-20261006-0008-r01-noperm/`）**：

| 形态 | 施加方式 | 实测 |
| --- | --- | --- |
| 一·只读属性 | `FileAttributes.ReadOnly` | `state=replace_failed`、`target=OLD-CONTENT`、`Access is denied.` 出现**两次** |
| 二·显式拒绝 ACE | `icacls "<目标>" /deny "<用户>:(WD,AD,WA)"` | `state=replace_failed`、`target=OLD-CONTENT`、`Access is denied.` |

第二种形态里那个"两次"很关键：**回滚用的 `copy /Y <备份> <目标>` 同样被权限拒绝**，
所以旧版本之所以完好，靠的是"失败的 copy 不改动目标"，而**不是**回滚成功。
也就是说无权限场景下**不能依赖回滚**保证"用户还有可运行启动器"，
可靠的是"失败即不动目标 + 仍然把旧版本拉起来"——这两条正是两个用例断言的内容。

**本轮踩到并记录的夹具坑（否则会把"无权限"验成假象）**：`icacls /deny "<用户>:(W)"` **不是**"只拒绝写"。
`(W)` 映射到 `GENERIC_WRITE`，其中含 `SYNCHRONIZE`，于是**连按读方式打开都会被拒**——
.NET 的 `File.ReadAllText` 直接抛 `UnauthorizedAccessException`。第一版用例就是这样失败的：
脚本还没被判定，**测试宿主自己先读不了目标文件**（那次失败是夹具缺陷，不是产品缺陷）。
换成细粒度 `(WD,AD,WA)` 后才是"读 OK、写被拒"。顺带否掉一个想当然的假设：
`copy /Y` 覆盖不可写目标时**不会**先删除再重建（否则拒绝写拦不住它），它就是失败并保持原文件。

边界：**磁盘满、被安全软件拦截**仍未验证（无法在受控环境里安全模拟），不要按"无权限已全覆盖"表述。

**③ 替换脚本在"非中文 Windows"下能不能用（第 19 轮补，`run-20261006-0440-apply-codepage/`）**：
脚本是 `.bat`、由 `File.WriteAllText` 写成 **UTF-8 无 BOM**、里面内联了含中文的绝对路径，
而 **cmd 按当时的 OEM 代码页解析 .bat**。此前所有验证都在本机 936 下做过——
"英文 Windows / 英文 CI runner（OEM 437）上还能不能用"**从来没人问过**。
答案与机制（实测）：脚本第 1、2 行是纯 ASCII 的 `@echo off` 与 `chcp 65001`，
后者把代码页切成 UTF-8 之后，含中文的行才被正确读取。
把控制台代码页切成 **437** 重跑本类 7 个用例 → **全部通过、状态一致**
（`state=applied` / `state=replace_failed`），所以这条路径对"非中文系统"是稳的。
已把机制钉成回归位：`Assert.StartsWith("@echo off\r\nchcp 65001", script)`——
只靠实验不够，因为删掉那行 `chcp` 在本机 936 下**照样全绿**，缺陷只会在英文机器上炸。

**一处诚实的覆盖缺口**：`UpdateService.ApplyUpdate`（启动器**自己**调用替换那一步，`UpdateService.cs:593`）
**没有任何测试直接覆盖**。原因写清楚：它的 `targetExe = Environment.ProcessPath` 指向**当前进程自己的 exe**，
在测试进程里调用会生成"等这个正在运行的测试宿主退出（上界 300 轮≈5 分钟）→ 覆盖失败回滚 → 再去重启测试宿主"的脚本，
既慢又危险。要安全地测它需要把 target 变成可注入参数，那是**产品重构**，不在本轮授权范围，
也不该为了可测性去动即将发布的核心路径。因此：脚本**语义**（7 项）与**编码**（437 实验 + 回归位）有覆盖，
**调用本身**记为未直接验证。

**④ 升级就位：既有用户的配置会不会被重置（第 21 轮补，`run-20261006-0500-config-upgrade-audit/`）**：
发布评审关心"旧版迁移"，但此前**没有任何一轮**看过这个方向，而本工作区恰好改了配置相关代码
（`AppConfig.cs` +15 行、`ConfigStore.cs` +105 行）。查清的结果是**加法式变更**：
`AppConfig` 只新增两个布尔属性（`DiagnosticKeepEvidence`、`NewsFakeIpCompat`，后者是**安全相关**的
fake-IP 兼容开关、注释明写 fail-closed），无重命名/删除/类型变更 ⇒ 旧配置缺这两个键时保持默认 `false`。
**并且这一维度已被现有测试覆盖**（不是我新加的）：旧形态配置（camelCase + 字段子集 + 缺新键）经
**真实 `LoadAsync`** 加载后逐字段保留、写回仍保留、且**不得触碰真实凭据存储**（`ConfigPreservationTests` 三例）；
新开关的"缺字段/字符串/数字/PATCH 省略"语义逐条钉死（`NewsFakeIpConfigTests` 四例）。
边界：拿不到**已发布 v1.0.0** 的产物或源码，所以是"同类旧形态"模拟，**不是真机升级**。
同轮还盘点了产品里 24 个 `…ForTest` 测试缝：22 个被测试引用，剩下 2 个经查是我搜索范围过窄造成的误报
（一个是 AsyncLocal 后备字段，一个被 `VerifyHost/BisectSmoke.cs` 使用）⇒ **没有"立了没用"的缝**。

**新发现并已修复的真实缺陷（详见 `run-20261005-2014-r06/R06/r06-update-hang-finding.md`）**：
替换脚本用 `timeout /t 1 /nobreak` 做等待间隔，而 `timeout` **依赖控制台**；该脚本由无控制台的 GUI 启动器拉起，
当 stdin 是管道（实测：测试宿主、任何重定向 stdin 的父进程）时 `timeout` 永久阻塞 → **更新原地卡死**：
不替换、不回滚、也不记失败。修复为 `ping -n 2 127.0.0.1 >nul`（不依赖控制台）并给等待循环加上界
（300 轮 ≈ 5 分钟，到点进入备份/替换/校验，失败则回滚，保证"异常时仍有可运行旧版本"）。

**③ 九页界面的运行时验收（新增）** —— `scripts/verify-ui-pages-live.cjs`

这一项补的是一个**此前完全没人管**的缺口：已有两道 UI 门槛都只能证明"资源拿得到"——
`verify-single-file-standalone.ps1` 证明 HTTP 200 且 MIME 正确，`check-ui-resource-closure.cjs`
证明本地静态引用闭包完整——**两者都不能证明界面在真实 WebView2 里真的渲染出来了**。
JS 抛异常、路由切换失败、某页渲染成空白，都可以在"资源全部 200"的情况下发生。
"九页界面人工复验"在本轮之前一直是 NOT_RUN 状态，本项把它变成可复跑判定。

做法：把**真实单文件发布产物**（不是 Debug 产物）放进全新目录启动，用产品自带的
`PINEGLOW_WEBVIEW_DEBUG` CDP 端口（见 `WebShellWindow.OnEnvironmentRequested`）驱动真实浏览器：

1. 逐页点击真实导航项（`.nav-item[data-nav]` 的 `click`，不是直接改 URL）；
2. 断言每页容器存在、激活态正确、并渲染出**非平凡文本**（每页有各自的最小内容量下限）；
3. 逐页截图存证；
4. 收集未捕获异常 / `console.error` / **本机资源的网络失败**，并把外部服务失败单独归类。

实测结果——**绑定到与快照验证同一份产物**（53,052,722 B，sha256 `5009cf73…`，即
`run-20261005-2330-final/final/verify-summary.json` 里的 `publishedExe`）。
同一份 EXE 连跑三次，每份证据目录都留了 `app-artifact-sha256.txt` 记录这层绑定：

| 运行 | 判定 | news 缩略图 | content 图片 | downloads | `/api/versions/remote` |
| --- | --- | --- | --- | --- | --- |
| `run-20261005-2350-uipages-1` | **PASS 9/9** | 12/12 | 21/21 | ok（26.0s） | 200（26.6s） |
| `run-20261005-2350-uipages-2` | **PASS 9/9** | 12/12 | 22/22 | ok（17.1s） | 200（14.0s） |
| `run-20261005-2340-uipages` | **FAIL 8/9** | 12/12 | 20/22 | external-pending（75s） | **500**（30.0s） |

三次共同的硬指标：`exceptions=0`、`consoleErrors=0`、`uiAssetFailures=0`；非致命项一致
（上游 404"图标不存在"2–3 条、"该账号没有皮肤"1 条、favicon 404 1 条）。
`downloads` 落地真实内容（textLen=1525）、`news` 截图 348,481 B 且已人工确认有缩略图、
`content` 页把当时存在的图标全部加载完（21/21、22/22）——这些在通过的那两次里都成立。

**证据与产物的绑定是逐字节验证过的**：更早那批迭代运行（如 `run-20261005-2308-uipages`）用的是一份
**不同的本地发布**（53,093,682 B，sha256 `75572b14…`）。差别不在源码——实测 20:27 之后
**没有任何 launcher 编译输入（C#/csproj/axaml/ico/manifest）被改过**，
且 `Pineglow.Launcher.csproj` 明确写了 `<Compile Remove="VerifyHost\**\*.cs" />`。

**第 14 轮把这个差异查清了，并推翻了第 9 轮的归因**：真正原因是
**`ui/assets/axolotl.png`（47,681 B）**——它被 `.gitignore` 的 `ui/assets/*` 忽略、因此不在候选源码里，
但 csproj 的嵌入 glob 是 `ui\**\*`（**不理会 gitignore**），所以**本地发布会把它嵌进去**；
单文件包是压缩的，压缩后正好差 **40,960 B**，与实测体积差完全吻合。
第 9 轮我写的"差别在发布会话的参数（原生 PDB）"是**错的**：三次对照发布显示
带与不带 `/p:Version` 的**体积完全相同**（53,093,682），PDB 是另一件独立的事（见下面的发布配方小节）。
两个 EXE 渲染出的 **news 页截图逐字节相同**（`abfeeb7aa68a8c79…`、348,481 B，四次运行完全一致），
所以更早那批 9/9 对最终产物同样成立，而上面三次又直接在最终产物上复验了一遍。

三个必须说清楚的边界：

- **首版门槛本身是错的，已修（有三次假通过留证）**：前三版把"页面文本长度 > 20"当作渲染完成，
  于是 `run-20261005-2250`、`run-20261005-2255`、`run-20261005-2302` 三次都判 **PASS** ——
  其中 2255 的 news 页当时正停在 **80 字的"加载失败"中间态**（截图 64,864 B；最终版同一页 348,481 B），
  而且当时**根本没统计图片占位槽位**（`imagesTotal=0`，"缩略图是空的"这件事在报告里看不出来）。
  另一头 `run-20261005-2130` 又把 downloads 页因为导航/轮询预算不足（10s）记成 FAIL（textLen=4），
  而 `run-20261005-2238` 那一版则把本机假 IP 造成的 news 400 误记成 **15 条产品故障**——
  同一个脚本在不同版本里**既假通过、又假失败**，正好说明这类运行时门槛必须自己先被验证。
  修正为：每页独立最小内容量 + 75s settle 预算 + 占位符感知的图片等待
  （`[data-news-art-id]` 槽位 + `<img>`）+ 三分类结果（通过 / 本机资源缺陷 / 外部依赖未落地）。
  这与 R05 是同一类问题：**验证工具自己也会说谎**，所以它自己的判定也必须被单独审过，
  上面那三次假通过就是"没审"的代价。
- **本机假 IP 环境下 news 图片的 400 `dns_private` 不是产品缺陷**：实测该拒绝来自本机 DNS 环境的
  失败关闭策略；加 `--fake-ip-compat` 后同一接口返回 200（4/4，30–43 KB）。
  因此**既不能当成通过**（默认链路下确实拿不到图），**也不能记成产品缺陷**。真实桌面 + 正常 DNS
  下的表现仍需在 R06 剩余项里复验。
- **本轮新增观察项（未修，需用户决策，不计为发布阻断项）**：失败那次的根因不是界面坏了，而是
  `/api/versions/remote` 的处理体有 **30 秒硬超时**（`LocalApiService.HandleListRemoteVersionsAsync`），
  上游超时后抛 `OperationCanceledException` 被通用处理转成 **HTTP 500**（框架英文异常文本），
  界面据此把 downloads 页渲染成"**暂无可下载版本**"——**"取不到"与"确实没有"在界面上不可区分**；
  同一进程里图标代理对同类超时返回的是有类型的 `504 上游超时或不可达`，错误契约不一致。
  该处理体与 `HEAD` **逐行相同**，属既有行为（非本轮引入）。定位、三次运行原始证据与建议改法见
  `run-20261005-2340-uipages/R06-ui-downloads-timeout-finding.md`。
  本轮**没有**改它：改界面会让 `ui/**` 字节变化 → 嵌入 UI 指纹、单文件 SHA256 与整轮 UI 证据全部失效，
  属于该由用户拍板的取舍。

该脚本**刻意不放进快照验证器的必跑步骤**（理由与 `verify-update-live-download.ps1` 相同：
它需要真实桌面会话与真实网络，放进离线确定性验证会让验证变脆），但已纳入白名单类别 L，
以便随快照复制、由人工/交互式验收复跑。

退出码语义是 fail-closed 的：`externalPending > 0`（外部依赖没在预算内落地）或图片没渲出来
都判 **FAIL 而不是 PASS**——`externalPending=0` 是通过的必要条件，不存在"没验到也算过"的写法。

**门槛自身的反向对照（本轮补，`run-20261005-2355-uigate-negctl/`）**：只会说 PASS 的门槛等于没有门槛，
所以对它做了三个负向对照，三条都按预期**非零失败**（命令与原始输出均留证）：

| 对照 | 期望 | 实测 |
| --- | --- | --- |
| `--settle-timeout 5`（预算故意给太小，产物本身合格） | 必须非通过 | **FAIL 8/9，exit 1**（downloads `textLen=4` 未落地即失败） |
| `--app <不存在的路径>` | 用法/环境错误，不得 PASS | **exit 2**，`找不到发布产物: …` |
| `--app <exe> --nope`（未知参数） | 用法错误，不得 PASS | **exit 2**，`未知参数: --nope` |

第一条的细节值得记：预算太小时 downloads 页**还没被激活**（`active=skins`、`textLen=4`），
所以它走的是 `broken` 而不是 `external-pending` 分支；两条分支都非通过
（代码里 `problems` 非空或 `okViews<9` 任一即 `verdict=FAIL`）。

**该门槛的一处已知截图局限（不是产品缺陷）**：`home` 截图里"最新资讯"的四张卡片是空的。
原因是这块内容**数据到位后才就地重绘**（`ui/js/launcher.js` → `refreshNewsSectionInView('home')`，
代码注释即"数据异步到位后，就地刷新当前视图中的新闻区块"），而 `home` 是首屏、
门槛接上 CDP 时它已经"settle"（实测 `settleMs=2`），截图早于新闻请求返回。
新闻页本身已验 **12/12 缩略图**（四张截图逐字节相同）。
**不要把这张首页截图读成"首页坏了"。**

**R06 仍未完成**：真实微软/Authlib/CurseForge 账号、真实 Java 与原版/承诺加载器启动、真实 Minecraft、
整合包导入、Mod/存档恢复、新闻/AI/皮肤真实外链，以及**更新链路的完整端到端**
（`DownloadUpdateAsync` 强制要求资产地址为 HTTPS，本机没有可信 HTTPS 夹具，也不会为测试放宽该检查或改本机信任库）。
"检查 → 下载 → 校验 → 退出交接 → 替换 → 重启 → 同版一致"这条链路仍未整体跑通，缺条件项一律记 NOT_RUN。
本轮已把其中**三段**单独跑实：**九页界面运行时验收**（§③，最终产物上 2/3 次 9/9、另一次原因已定位）、
**真实 HTTPS 下载与校验**、**替换脚本语义**（上表 7 项）。
其中"真实 HTTPS 下载与校验"在第 33 轮被**升级为对真实发布资产**的验证（第 19 轮那次用的是 Gitee `README.md`
**1,618 B 替身**；第 33 轮用 `Pineglow-Launcher-win-x64-single-file.exe` 43,989,943 B，
字节与 SHA256 与真实资产一致、篡改被拒），见 `run-20261006-0750-remote-truth/`。
但"下载完成后由真实应用替换自己并重启成功"这一交接段**仍未验证**。

### 本轮第二个发现：候选源码被本地发布污染（流程类，已修并加守卫）

`dev/PineglowLauncher/packages.lock.json` 是**未跟踪但在白名单里**的生成物。本机为 R01/R03 做单文件发布时，
`dotnet publish -r win-x64` 按 .NET 的已知行为**就地改写**了它：8663 字节（非 RID 形态）→ 10868 字节
（含 `"net10.0/win-x64"` 段与 `Microsoft.NET.ILLink.Tasks`）。后果是快照验证里两份 `--locked-mode` 还原
直接 NU1004、构建与测试全红——**候选源码实际已不可构建**。

修复与防复发：
1. `dotnet restore dev/PineglowLauncher/Pineglow.Launcher.csproj --force-evaluate` 重新生成非 RID 形态（8663 字节）；
2. `scripts/verify-source-snapshot.ps1` 组装阶段新增**锁定文件形态守卫**（与 build.yml/release.yml 同一不变量）：
   命中 RID 段或 ILLink 即硬失败，并提示复原命令。

这一步的价值在于：它证明"本地跑通了"不等于"候选源码是干净的"，也说明为什么必须先跑验证器再谈提交。

## 3. 本轮完整验证结果（GREEN）

`powershell -ExecutionPolicy Bypass -File scripts/verify-source-snapshot.ps1 -EvidenceDir <证据目录>`

| 项 | 结果 |
| --- | --- |
| 快照组装 | 161 tracked + 146 白名单 = 307 文件，额外/泄漏 0，无 bin/obj，编码守卫通过，锁定文件形态守卫通过 |
| R05 步骤运行器自证 | 17/17 通过 |
| R02 资源闭包 | 9 条引用 / 缺失 0；门槛自证 7/7 |
| R04 清单校验 | node 23 个 == 磁盘发现 23 个 |
| Node 全套 | **304 通过 / 0 失败**（23 个文件） |
| **VerifyHost --bisect-smoke** | **PASS，20 项检查全过；凭据隔离断言：生产凭据后端调用 0 次**（隔离后端接住 34 次操作 / 4 次删除意图） |
| C# 全套 | **903 通过 / 0 失败 / 0 跳过**（含 UiBundleTests 8 项、R03 专项 18 项、R06 更新替换 7 项） |
| 锁定还原 | launcher / tests 均 `--locked-mode` exit 0 |
| 单文件 publish | 53,052,722 bytes，sha256=5009cf73…（**十一次整轮验证逐字节相同**：2118 / 2330 / 0010 / 0015 / 0030 / 0050 / 0120 / 0150 / 0220 / 0420 / 0450；本轮只改验收脚本与测试断言，都不参与启动器程序集编译） |
| 独立冷启动 | **19 项**检查全通过（第 18 轮起加了"不继承开发旋钮"一项；此前为 18），窗口 `松光启动器`，端口 48291（每轮端口不同）；该项在验证器自身的运行里也抓到了继承的 `PINEGLOW_WEBVIEW_DEBUG=9222`。**另有一份对"发布配方产物"的独立冷启动：18/18（当时 18 项），解包目录 `9.9.9-5f6b42a61753`**（见发布配方小节） |
| 已知未锁定边界 | RID restore 探测 NU1004（与修复前一致，如实记录） |
| 最终判定 | **PASS（失败项 0）** |

证据目录：R01/R04 见 `run-20261005-1902-r01r04/R01-R04/`，R03 见 `run-20261005-1936-r03/R03/`，
R06 见 `run-20261005-2014-r06/R06/`，VerifyHost 冒烟见 `run-20261005-2055-verifyhost/verifyhost/`，
R01 步骤 6「无权限」分支见 `run-20261006-0008-r01-noperm/`（含原始 `state=replace_failed` 输出），
actionlint 已复现见 `run-20261006-0740-actionlint/`（失败路径保留在 `run-20261006-0050-actionlint/`），
真实更新链路（真实发布资产）与远程/CI 事实见 `run-20261006-0750-remote-truth/`，
action 契约校验（输入名/必填/运行时/版本存在性）见 `run-20261006-0810-action-contract/`，
资产级回退真实验收（含 Gitee 主源匿名下载与反证）见 `run-20261006-0820-asset-fallback/`，
R06 外部服务真实端点盘点（版本/新闻/源/内容搜索）见 `run-20261006-0830-r06-services/`，
图片代理根因追查（更正对外链图的猜测）见 `run-20261006-0910-image-proxy-rootcause/`，
图标端点复查（6 次 2×504/4×200，判定可用）见 `run-20261006-0920-icon-flakiness/`，
原版下载真实验收（client.jar sha1 与官方一致、assets 468/468）见 `run-20261006-0940-vanilla-download/`，
缺 Java 的用户反馈与"没有安装入口"发现见 `run-20261006-1000-java-entry/`，
真实 JRE 就绪验证（Adoptium 校验和一致、prelaunch-check 转通过）见 `run-20261006-1010-java-ready/`，
真实整合包导入（43 mod SHA1、assets 3052/3052、Java 自动安装）见 `run-20261006-1030-modpack-import/`，
文件层替换/回滚/备份/恢复（20 项，含非破坏式恢复与回滚反证）见 `run-20261006-1100-file-transactions/`，
原版游戏真实启动/退出（LWJGL/OpenAL/10 图集、窗口标题、退出结算 305 秒）见 `run-20261006-1130-vanilla-launch/`，
运行态守卫实测（运行中四端点 400、退出后解除、运行中可备份的发现）见 `run-20261006-1200-running-guards/`，
🔴 加载器实例启动崩溃（blocker，缺 -Djava.library.path）见 `run-20261006-1230-fabric-launch/`，
✅ 受控实验（只补该参数即进入渲染管线，根因确认+修法验证）见 `run-20261006-1300-fabric-fix-sim/`，
根因收敛到一行 + 三份可审阅补丁（A 治根/B 兜底/回归测试）见 `run-20261006-1310-loader-fix-patch/`，
影响面分档（Fabric 实测/Quilt 同构/Forge·NeoForge 待定）+ 合并算法独立验证 PASS 见 `run-20261006-1330-merge-algo-check/`，
Forge 同病的 artifact 级判定 + 未解释的安装 404 + 镜像回退缺口见 `run-20261006-1400-forge-probe/`，
记录型代理追查 404（排除镜像改写）+ 确认"代理设置启动时不生效"见 `run-20261006-1430-forge-404-trace/`，
影响面更正（Quilt 不受影响；Fabric/Forge/NeoForge 受影响）见 `run-20261006-1500-loader-blast-radius/`，
Quilt 实测（命令行含库路径、12 图集、0 崩溃）见 `run-20261006-1530-quilt-probe/`，
📌 决策包（一页拍板）见 `run-20261006-1600-decision-package/DECISION-PACKAGE.md`，
✅ R06「进入世界/加载存档」不用 GUI 验通（官方服务端造档 + CustomGameArgs 快速进世界）见 `run-20261006-1630-world-entry/`，
✅ R06「多实例并发」PASS（两个实例同时跑/同实例 400/退出后解除）见 `run-20261006-1700-multi-instance/`，
⚠️ 导入取消清理不干净（半成品 364.7 MB + 临时目录残留 + 客户端拿到连接关闭而非 409；tombstone 正常）见 `run-20261006-1730-import-cancel/`，
✅ 更新链实测通过（HTTP 清单被接受/下载/替换/退出/**重启并由新进程提供服务**）+ 复现已发布 v1.0.0 的 EXE-only 启动缺陷（G07/R01）见 `run-20261006-1800-update-chain/`，
🔴 Forge/NeoForge 安装器 URL 多一段 ⇒ 必然 404（第二个 blocker，一行修法已验证）见 `run-20261006-1900-forge-url-probe/`，
✅ 内容子系统（5 类搜索 + 真实装 Mod + 任务）PASS 见 `run-20261006-1930-content-subsystem/`，
✅ 镜像源模式（BMCLAPI）PASS（装得成、sha1 与官方一致、流量走镜像池）见 `run-20261006-2000-mirror-mode/`，
🔴 全新安装"默认游戏"实例不可寻址（第三个 blocker：默认实例不落盘 ⇒ id 每次都在变）见 `run-20261006-2100-default-instance-id/`，
📦 三个 blocker 的待应用补丁集 + 9 步验证清单见 `run-20261006-2130-patch-set/PATCH-SET.md`，
✅ 服务器列表 + ping（真实公网服务器 2278ms/真实 MOTD、死端口干净失败、重启持久化、删除）PASS 见 `run-20261006-2200-servers/`，
✅ Mod 二分定位状态机 PASS（3 轮定位植入元凶、finished/giveup 后文件全恢复、过期 step 被拒）见 `run-20261006-2230-mod-bisect/`，
✅ 资源包/光影安装 PASS（落进 resourcepacks/shaderpacks、任务 completed、列表与 state 计数一致）见 `run-20261006-2300-resourcepacks/`，
✅ 第三方账号失败路径 PASS（地址校验 400/37ms、真实 Yggdrasil 假凭据 400 且透传服务端错误、不加账号；agent 两个 URL 均 200）见 `run-20261006-2330-thirdparty-auth/`，
✅ Java 探测与恢复区 PASS（JAVA_HOME 里的 JRE 被发现并判有效；隔离→列出→还原全链）见 `run-20261006-2350-java-recovery/`，
📌 决策包已刷新到第 67 轮状态（三 blocker 补丁集 + 19 项决定 + 最新"已验证/未验证"）见 `run-20261006-1600-decision-package/DECISION-PACKAGE.md`，
📌 决策包与补丁集已再刷新到**四个 blocker（B0/B1/B2/B2b）+ 20 项决定 + 6 份补丁（6/6 `git apply --check` ✅）**，
✅ 打过 6 个补丁的候选：全量门槛 11 步全 PASS（Node 304/C# 904/冷启动 19/19；汇总的 2 项"失败"是副本构造产物）见 `run-20261006-3000-patched-final-gate/`，
✅ 全量验证链复跑 GREEN/失败 0 且产物逐字节一致（清掉 12.15 GB 残留快照）见 `run-20261006-2400-final-refresh/final/final-refresh-report.md`，
✅ 三个 blocker 的补丁在"打过补丁的构建"里复现验证通过 + 副本 C# 904/904（不动工作区）见 `run-20261006-2500-patched-build/`，
✅ B1 端到端：补丁构建下 Fabric 实例进渲染管线（10 图集/0 崩溃），候选对照必崩，见 `run-20261006-2600-patched-fabric-launch/`，
✅ 补丁构建回归：Vanilla（10 图集）与 Quilt（12 图集）都正常启动、0 崩溃，见 `run-20261006-2700-patched-regressions/`，
🔴 Forge 安装的第二半缺陷（URL 修好后 404 消失，但安装器缺 launcher_profiles.json ⇒ ExitCode=1；补最小桩后 exit 0）见 `run-20261006-2800-patched-forge/`，
⚠️ Forge 端到端在本机**验不了**（双补丁构建仍 ExitCode=1；两组对照同为安装器 HTTPS 握手失败，ASCII/中文路径一致）见 `run-20261006-2900-patched-forge-fixed/`，
✅ 从列表移除 ↔ 挂回 验清（delete 只改配置、文件完好；重复/不存在目录 400）见 `run-20261006-2030-instance-register/`，
微软设备码登录"无需真人"三段见 `run-20261006-0850-ms-login/`，
九页界面运行时验收见 `run-20261005-2350-uipages-1/`、`run-20261005-2350-uipages-2/`（绑定最终产物、PASS）
与 `run-20261005-2340-uipages/`（同产物上唯一的非通过，含 `R06-ui-downloads-timeout-finding.md` 定位）；
每份目录都有 `ui-pages-live.json` + `ui-pages-console.log` + 9 张截图 + `app-artifact-sha256.txt`。
更早的六次迭代运行 `run-20261005-2130/2238/2250/2255/2302/2308-uipages` 一并保留，
用于复核上面的"假通过/假失败"与"不同发布会话产物"的差异，
最终整轮见 `run-20261006-0450-final/final/`（含 `verify-summary.json`、`standalone-cold-start.json`、
`test-inventory-report.json`、`ui-resource-closure.json`、`step8b-verifyhost-bisect-smoke.log`、
`r03-named-tests.log`、`r06-named-tests.log`、`r06-update-hang-finding.md`、`snapshot-manifest.sha256.txt` 与各步骤原始日志）；
最近十一轮整轮验证依次为 `2118` → `2330` → `0010` → `0015` → `0030` → `0050` → `0120` → `0150` → `0220` → `0420` → `0450`
（**十一轮均 PASS**，单文件 EXE 十一次逐字节相同），十一份 `snapshot-manifest.sha256.txt` 一起构成下面的增量审计基线。
本地干跑/专项证据：发布门槛 `run-20261006-0110-releasejob-dryrun/`、
Gitee 镜像失败路径 `run-20261006-0140-gitee-mirror-dryrun/`、
发布配方与产物差异 `run-20261006-0210-publish-recipe/`、
冷启动门禁环境隔离 `run-20261006-0400-standalone-env/`、
替换脚本代码页（437）验证 `run-20261006-0440-apply-codepage/`、
升级就位（旧配置存活）与测试缝盘点 `run-20261006-0500-config-upgrade-audit/`、
交付脚本静态解析与"交付 vs 自动执行"盘点 `run-20261006-0520-shipped-scripts-parse/`、
复评结论的逐条自称核对 `run-20261006-0540-reassessment-audit/`、
旧版迁移兼容性（读已发布 v1.0.0 源码） `run-20261006-0600-v100-migration/`、
原地升级隐患（外置 UI/ vs 残留 DLL） `run-20261006-0620-upgrade-in-place/`、
陈旧 apply-state 被新版继承 `run-20261006-0640-stale-apply-state/`、
持久化标识与 v1.0.0 的同一性 `run-20261006-0660-persistence-identity/`、
实例/数据布局与 v1.0.0 的同一性 `run-20261006-0680-instance-layout/`、
候选范围警告（约 52% 新增行未经复核；数字已由第 31 轮更正为 ≥70.8%） `run-20261006-0700-scope-warning/`、
候选范围重算（三段分解；更正为 61 项 / +13,222 / ≥70.8%） `run-20261006-0730-scope-recount/`、
发布说明草案（§5.4 必写项落地稿） `run-20261006-0710-release-notes/`、
候选集合双向核对与触发条件 `run-20261006-0250-candidate-set-audit/`、
表达式引用静态检查 `run-20261006-0300-expr-check/`、
工作流引用审计 `run-20261006-0040-wf-audit/`。

**白名单完整性审计（防"本地跑通却改脏了候选"）**：把最早一次与最终一次的
`snapshot-manifest.sha256.txt` 逐项对比——只有 2 个白名单文件内容变化
（`scripts/source-snapshot-inputs.txt`、`scripts/verify-source-snapshot.ps1`，均为我有意编辑），
新增 8 项（本轮新纳入的工具/测试），**无删除**；被本地发布污染过的
`dev/PineglowLauncher/packages.lock.json` 已回到与污染前**逐字节一致**的形态。

**本轮增量审计（2118 → 2330 → 0010 三份清单逐项 diff 实测）**：

- 2118 → 2330：145 → 146 项，新增 **1**（`scripts/verify-ui-pages-live.cjs`）、删除 **0**、内容变化 **1**
  （`scripts/source-snapshot-inputs.txt`，就是上面那次白名单登记）。
- 2330 → 0010：**0 新增 / 0 删除 / 0 变化**——本轮改的是 `README.md` 与 `.github/workflows/release.yml`，
  而这两个**不在这份清单里**（见下）。
- 0220 → 0420：**0 新增 / 0 删除 / 内容变化 1**——正好是第 18 轮改的
  `scripts/verify-single-file-standalone.ps1`（白名单文件，改动被清单如实捕捉）。

**必须说清这份清单的覆盖范围（本轮纠正）**：`snapshot-manifest.sha256.txt` 覆盖的是
**白名单（未跟踪）文件**共 146 行；**tracked 文件的工作区版本不在其中**
（实测：根 `README.md`、`ui/js/launcher.js`、`.github/workflows/release.yml` 都不在清单里，
清单里的两个 `README.md` 是 `dev/PineglowLauncher.Tests/TestAssets/Diagnostics/README.md` 与 `ui/vendor/README.md`）。
因此"0 变化"只能说明**白名单没被动过**，**不等于 tracked 文件没变**；
tracked 侧的改动由 `CANDIDATE-COMMIT-PLAN-2026-10-05.md` 第 3 节单独枚举（44 个，含本轮新增的 README/release.yml）。

## 4. 未运行 / 受限项（不得当成通过）

1. **（第 32 轮已修正）actionlint 已复现** —— 经 `ghproxy.net`/`gh-proxy.com` 下载，双镜像字节数/SHA256 一致、与官方校验表一致、反向对照有效、对当前两工作流 exit 0。
    下面这段是**修正前**的原文（保留以记录"网络阻断"这一误判）：归属要说清：
   原始评估在 2026-10-05 用 actionlint **1.7.7** 校验过两份工作流并 PASS，工具 sha256 与预期一致
   （`docs/release-readiness-evidence-2026-10-05/actionlint-tool.json`，`.exitcode`=0，日志为空=无告警）。
   我这一侧不可得：实测 `curl` 退出码 **0**、2 秒返回、落盘 **9 字节**、内容就是纯文本 **`Not Found`**
   （`github.com` 解析到 `198.18.0.74`，本机 fake-IP 形态的固定应答，不是 GitHub 的 404 页面）。
   证据：`run-20261006-0050-actionlint/actionlint-download-attempt.md` + 那个 9 字节文件。
   我做的替代物只有本地子集（含 PyYAML 结构、触发条件、表达式引用静态检查），
   它们**不覆盖**表达式语法/类型、action 输入 schema、runner 行为——**结构通过不等于 actionlint 通过**。
   另需注意 PyYAML 的盲点：YAML 1.1 把 `on:` 解析成布尔 `True`，**触发条件默认根本没被校验**
   （第 15 轮已用 YAML 1.2 语义单独补上）。
2. **远程 CI 未运行本轮结果**：无提交/推送授权。
    **但该仓库的流水线在真 runner 上是有历史的**（第 33 轮查 runs API）：`Build` 长期 success，
    `Release` 在 3 次失败后于 `f61f44f`（v1.0.0）**发布成功**（含 manifest + EXE 两个附件），
    远程 `main`=`c8aa286`，本地 HEAD=`f145d14`（**7 个提交未推送**）。
    所以仍成立的边界是"**本轮新增/改写的步骤**从未在真 runner 上执行过"。
   本轮做了力所能及的替代：把 `release` 作业的**步骤正文**抽出来本地执行（18 条用例全过，
   含每条门槛的 RED），但它**不覆盖** `actions/*`、runner 环境、跨步骤状态、权限与 secrets，
   也**不覆盖 shell 版本差异**（本机 PowerShell 5.1 vs CI 的 `shell: pwsh`），所以**不能替代 CI**。
3. **前端测试文件仍未 tracked**：`ui/tests/*.test.cjs` 目前只存在于工作区与白名单里；
   CI 要在干净检出上真正跑到它们，候选提交必须按 `scripts/source-snapshot-inputs.txt`（类别 F/J/L）精确纳入。
4. **本机受限观测（沙箱环境，不是产品缺陷）**：本轮把三条现象收敛成同一条规则 ——
   **凡是从工作区 `D:\松光启动器` 里拉起的进程，本会话沙箱把它限制为"只能写工作区内"**：
   - 在 `%TEMP%` 下建目录被拒（`UnauthorizedAccessException`、错误码 5）；把 TEMP 指向工作区内即可通过；
   - 写真实 `%APPDATA%\MCMiniLauncher\updates\...part` 被拒 → 既有用例
     `UpdateRecoveryTests.TruncatedDownload_IsRejectedWithAPreciseReason` 因此只在快照（TEMP 工作根）里通过；
   - 在工作区内启动发布 EXE 时 `HttpListener.SetupV2Config()` 报「句柄无效」，
     同一个 EXE 放到 `%TEMP%` 下启动则 `/api/health` 正常。
   这三条都已如实记录；它们**不能**用来解释或掩盖任何真实缺陷（已区分：本轮真正发现的产品缺陷见 R06 一节）。
   附带观察（既有测试卫生问题，非本轮引入、未修）：`TruncatedDownload_IsRejectedWithAPreciseReason`
   会写真实 `%APPDATA%` 配置目录，与仓库自己的 `RealConfigGuard` 隔离约定不一致。
5. **R06 只完成三段**：正式附件首启、更新替换脚本、九页界面运行时验收（最终产物上 2/3 次 9/9，第 3 次非通过的原因已定位，见 §2 第 ③ 项）。
   真实微软/Authlib/CurseForge 账号、真实 Java 与原版/承诺加载器、真实 Minecraft、整合包导入、
   Mod/存档恢复、新闻/AI/皮肤真实外链、以及九页界面在**正常 DNS 环境**下的图片外链均未执行；
   界面在真实桌面的表现已验，但不等于账号/游戏闭环已验。
6. **R03 的线上写入未做（真实端点已验，失败路径已干跑）**：客户端降级链的**真实端点**已验证——默认主源 Gitee 固定
   `update.json` 实测 HTTP 404，降级到真实 GitHub Release `manifest.json` 成功取到清单；
   真实 Gitee HTTPS 资产的下载 + SHA256 校验、以及被篡改 SHA256 被拒都已实测。
   发布侧**失败路径**也已干跑（缺 token / 假 token / API 不可达，14 项断言；
   并修掉"API 不可达时零证据"的缺口）。
   仍未执行的是**成功路径的写动作**：真的上传附件、真的改写 manifest、真的在 Gitee master 建/更 `update.json`
   （需发布授权与真实令牌）。因此旧客户端（≤1.0.0，无备用源）目前仍升不了级；
   "更新链路已经可用"这句话**只对本轮修复版成立**。
7. **更新链路的 HTTPS 端到端未跑**：`DownloadUpdateAsync` 强制要求资产地址为 HTTPS（MR20 安全不变量），
   本机没有可信 HTTPS 夹具，也不会为测试放宽该检查或改本机信任库；因此
   「检查 → 下载 → 校验 → 退出交接 → 替换 → 重启 → 同版一致」这条完整链路仍未跑通。
   （另：内嵌 UI 方案下新 EXE 会解包到 `<配置目录>/UI/<新版本-指纹>`，旧目录不会被复用，
   该"同版一致"已由冷启动脚本断言，但真实跨版本升级未跑。）
8. **版本定稿未做**：本轮未选定正式版本号（仍是 1.0.0），tag/程序集/manifest/资产的一致性校验要等版本定稿后才有意义。
9. **九页界面验收本身对环境敏感（刻意 fail-closed，不是缺陷）**：本机假 IP 环境下
   `/api/versions/remote` 的实测耗时是 14.0s / 26.6s / 30.0s（超时），所以同一份产物会给出
   "通过"与"非通过"两种结果；非通过那次的因果链已定位并留证（§2 第 ③ 项与
   `run-20261005-2340-uipages/R06-ui-downloads-timeout-finding.md`）。
   这也意味着**这条门槛不适合直接塞进 tag 触发的发布流程**：它需要真实桌面会话与稳定外网，
   否则会把"上游慢"变成"发不出版"。
10. **两处"原始现场"曾经被我判成"没有留存"——第 17 轮查明是**我找错了地方**（只看 D 盘源码树的证据目录，
    没看原始评估的 DeliveryRoot 证据目录）。两处**都在**：
    ① R01 的原始崩溃栈：`docs/release-readiness-evidence-2026-10-05/exe-only-crash.log` ——
    `System.TypeInitializationException: The type initializer for 'SkiaSharp.SKImageInfo' threw an exception.
    ---> System.DllNotFoundException: Dll was not found.`，配 `exe-only-summary.json`（exitCode=-532462766）；
    ② R05 的"步骤被记成 PASS"现场：同目录 `snapshot-run.log` 第 52–95 行有 **8 条 `exit=0 status=PASS 0.0s`**
    （0.0 秒、日志名在列却根本没产生），整轮直到"单文件发布产物缺失"才失败（`snapshot-exitcode.txt` = 1）。
    这两份是原始评估者留下的现场，**不需要我重建**；我第 11 轮补的 cmd 机制复现只是同类的补充。
11. **"无权限"已验两种形态，但仍未全覆盖**：验了**只读文件属性**与**ACL 显式拒绝**
    （§2 第 ② 项，证据 `run-20261006-0008-r01-noperm/`）；
    **磁盘满、被安全软件拦截**无法在受控环境里安全模拟，仍未验证，不要按"无权限已全覆盖"表述。
12. **本地 publish 的产物 ≠ 干净检出的产物（第 14 轮查明原因）**：`ui/assets/axolotl.png`（47,681 B）被
    `.gitignore` 的 `ui/assets/*` 忽略、不在候选源码里，但 csproj 的嵌入 glob 不理会 gitignore，
    于是**本地发布比 CI/发布产物多嵌这一个 PNG**（压缩后单文件包差 40,960 B）。
    它**没有被界面引用**（资源闭包 9 引用/缺失 0、九页验收都在快照产物上通过），所以**不是缺资源缺陷**；
    但**以后任何"看界面 / 看产物"的验证都必须绑定快照产物**，否则验的不是要发出去的那份字节
    （本会话一直这么做，包括九页界面验收与 news 截图逐字节比对）。
    是否收紧嵌入 glob 属需要用户拍板的取舍——改它会改变产物哈希并牵动既有**全部**证据。
13. **候选里交付的 18 个工具/测试脚本没有任何门禁在执行**（第 22 轮盘点，`run-20261006-0520-shipped-scripts-parse/`）：
    17 个历史里程碑工具（`verify-mg0..3`、`measure-*`×4、`audit-*`、`repair-evidence-manifests`、
    `run-diagnostic-benchmark`、`capture-goal-baseline`、`run-goal-validation`、`audit-goal-evidence.py`）
    与 1 个不在清单口径内的 Python 测试（`audit-goal-evidence.test.py`）。
    本轮只证明它们**能被解析**（21 PS + 29 JS + 2 PY 全过），**运行期行为未验证**——
    它们多数依赖更早轮次的证据布局，现在跑只会因为缺输入而失败，那种失败**不构成**对当前候选的判定。
    含义：不要把这些工具的存在当成"验证已被执行"。

## 5. 建议的下一步顺序

1. **照《发布执行手册》走**（DeliveryRoot 的 `release-readiness-runbook-2026-10-06.md`）：
   先跑 §0 三条不变量，再跑 §1 全量验证（期望 PASS / 307 / Node 304 / C# 903 / 冷启动 19/19 / EXE `5009cf73…`），
   然后按 §2 逐项做专项验证；§7 列了 8 条**中止条件**。
2. 候选提交：按 `CANDIDATE-COMMIT-PLAN-2026-10-05.md` 精确纳入（不能 stage-all），随后触发远程 CI 取得同 SHA 证据；
   这一步同时决定 `ui/tests/*.test.cjs` 等前端测试文件是否真的进入仓库（否则干净检出上 CI 仍然跑不到它们）。
2. R03 的线上部分（需发布授权）：在 Gitee 仓库 master 建/更 `update.json`；备用源已由真实端点验证可用。
3. R06 剩余项：在真实桌面环境跑更新端到端（下载后由真实应用替换自己并重启）与游戏/账号闭环；
   再在**正常 DNS 环境**复跑 `node scripts/verify-ui-pages-live.cjs --app <发布附件>` 确认新闻/皮肤
   外链图片（本机假 IP 环境下的 400 `dns_private` 属环境限制，不能当作产品结论）；缺条件的写 NOT_RUN。
4. **决定是否修本轮记录的既有观察项**（§2 第 ③ 项）：`/api/versions/remote` 的 30s 超时
   应返回有类型的失败而不是 500，界面应把"拉取失败"与"确实没有"分开渲染。
   注意代价：改 `ui/**` 会让嵌入 UI 指纹与单文件 SHA256 变化，本轮全部 UI 证据都要重跑。
5. 版本定稿后统一 tag/程序集/运行时/manifest/资产，再等人工放行发布。

## 6. 对照原始流程的覆盖审计（remediation-workflow §0–§7 逐项）

上一版进度报告是"按我做过的事"写的；这一节改成"按原始流程要求的事项"逐条核对，
目的是找出**漏做的项**而不是复述已完成的事。状态：✅ 有证据 / ⚠️ 部分 / ✗ NOT_RUN / 🚫 需授权。

### §0 角色与现场保护

| 条目 | 状态 | 证据 |
| --- | --- | --- |
| 记录 HEAD / 完整 status / diff / 允许文件 SHA / 工具版本 | ✅ | `CANDIDATE-COMMIT-PLAN` §1–3；每轮 `repoHead=f145d14e…`；`snapshot-manifest.sha256.txt`（146 行）|
| 新 GUID 冷副本复验 | ✅ | 每轮快照目录 `pineglow-snapshot-<guid>`；冷启动在 GUID 命名的新中文/空格目录 |
| 真实配置/存档/游戏/凭据与历史失败只读 | ✅ | 未改真实配置；4 条历史凭据只记目标名、未删改 |
| 后端读/写/删全部隔离；portable.flag 不代替凭据隔离 | ✅ | VerifyHost 换内存后端 + 断言生产凭据调用 **0** 次（隔离后端接住 34 次操作 / 4 次删除意图）|
| 禁止 reset/clean/restore/checkout/stash、stage-all、批量删除、覆盖历史、提交/推送/tag/发布 | ✅ | 全程 `staged=0`、HEAD 未动、无提交/推送；未使用上述命令 |
| 只停止本轮明确拥有的进程 | ✅ | 默认步骤超时按进程树只终止自己启动的 PID；UI 门槛 `taskkill /T /F` 只针对自己 spawn 的 PID |

### §1 R02

| 步骤 | 状态 | 证据 |
| --- | --- | --- |
| 1 冻结两资源与引用文件 SHA、核对 tracked 状态 | ✅ | `ui/css/pineglow.css`、`ui/assets/pine-forest.svg` 当时均未跟踪 → 纳入类别 L；SHA 由清单固定 |
| 2 精确追加白名单（不批量纳入 ui/ 或全部未跟踪资产） | ✅ | 只追加 2 个资源 + 2 个工具 + 1 个新工具；三份清单增量 diff |
| 3 HTML/CSS/JS 本地资源闭包，过滤远程/data/锚点 | ✅ | 9 条引用 / 缺失 0（`ui-resource-closure.json`）|
| 4 独立夹具漏资源 RED；完整冷输入 GREEN | ✅ | 门槛自证 7/7，含"删除 CSS 间接依赖 → 必须 RED" |
| 5 副本与正式包资源 SHA 对源一致；HTTP/MIME；GUI 使用最新视觉 | ✅ | 解包 UI 与候选源码逐字节一致；10 项资源 200 + MIME 正确；**本轮人工确认 `home.png` 里渲染出了 `pine-forest.svg` 的像素森林主视觉** |

### §2 R05

| 步骤 | 状态 | 证据 / 说明 |
| --- | --- | --- |
| 1 中文/空格路径复现；保留 cmd 原始报错和步骤误判 | ✅（第 17 轮补齐归属） | 两样**都在原始评估的 DeliveryRoot 证据里**：`snapshot-run.log` 第 52–95 行 8 条 `exit=0 status=PASS 0.0s`（日志未产生却记 PASS）+ `snapshot-exitcode.txt`=1；本轮另补了 cmd 编码机制的独立复现（`run-20261006-0005-r05-red-repro/`）。此前我误报"误判现场未留存"，是只找了 D 盘源码树 |
| 2 Unicode 安全进程参数/输出重定向，退出码立即捕获 | ✅ | `scripts/lib/StepRunner.psm1` |
| 3 日志缺失/零测试/非零退出/超时/产物缺失分别非零失败 | ✅ | SR-01…SR-09，17/17（SR-05/06/07/08 正是这四类）|
| 4 英文/中文/空格路径及故障用例全部验证 | ✅ | SR-03（中文工作目录）、SR-09（中文日志路径）等 |
| 5 保留非 RID locked-mode 与 RID 普通还原的边界描述 | ✅ | `knownUnlockedBoundaries`（NU1004 如实记录）|

### §3 R01

| 步骤 | 状态 | 证据 / 说明 |
| --- | --- | --- |
| 架构选择：A（真正独立单文件/自解包）优先 | ✅（选 A） | B（完整目录 ZIP/安装包）**未做**——A 的兼容性已由"只复制 EXE 冷启动"实测成立 |
| 1 原 Release 参数冷发布并保留 standalone RED | ✅（第 17 轮补齐归属） | 原始 RED **留存于 DeliveryRoot 证据**：`exe-only-crash.log` 的 `DllNotFoundException ... SkiaSharp.SKImageInfo` + `exe-only-summary.json`（exitCode=-532462766）；本轮另有原参数发布产物清单与修复后的 GREEN（18/18）。此前我误报"崩溃栈没有留存文件" |
| 2 统一发行资产与更新/回滚单位，不新服务配旧 UI | ✅ | 解包目录按 `版本-内容指纹`；解包 UI 与源码逐字节一致（冷启动脚本断言）|
| 3 排除非必要 PDB、保留许可证与必要资源、声明系统/架构/WebView 条件 | ✅（本轮补齐） | PDB 早有 artifact-shape 断言；**许可证与运行环境声明本轮才补**（见 §2 R01）|
| 4 正式附件在新中文/空格目录冷启动，不依赖开发树/历史文件/PINEGLOW_UI_DIR | ✅ | 冷启动 19 项（每轮 PASS；第 18 轮起显式清除继承的 `PINEGLOW_*` 并留证）|
| 5 真实窗口、九页必要资源、live API、版本验证 | ✅ | 窗口标题 `松光启动器`；资源 200+MIME；九页运行时 9/9；live API 与版本接口实测 |
| 6 旧版迁移与占用/校验失败/无权限时恢复完整可运行旧版本 | ✅（无权限两种形态；磁盘满/安全软件未覆盖） | 占用（等待运行中目标）、校验失败回滚、备份失败不动目标均已真实验收；**无权限**验了只读属性与 ACL 显式拒绝两种形态 → 均 `replace_failed`、旧版本原样、仍拉起旧版本（`run-20261006-0008-r01-noperm/`）。**"旧版迁移"的另一半——既有用户配置升级后不被重置——第 21 轮也已核过**（`run-20261006-0500-config-upgrade-audit/`：加法式字段变更 + 现有测试覆盖旧形态加载与安全开关的 fail-closed 语义）。磁盘满/被安全软件拦截无法安全模拟，仍未验 |

### §4 R03

| 步骤 | 状态 | 证据 |
| --- | --- | --- |
| 1 冻结默认 Gitee 404，区分清单失败与资产回退 | ✅ | 真实 404 实测；清单失败与 `fallbackUrl` 资产回退分开描述 |
| 2 HTTPS 主/备清单；404/超时/坏 JSON 降级；保留自定义源 | ✅ | 服务层 16 项 + API 2 项；地址链去重；自定义源优先 |
| 3 旧客户端固定 Gitee URL 迁移与协议兼容矩阵 | ✅ | 兼容矩阵表 + 待上线动作单列（旧客户端**必须**继续拿到 update.json）|
| 4 缺 token/镜像失败不得声称已更新或输出无效地址 | ✅ | `mirror-status.json` 如实落盘；非 HTTPS 地址被拒写；**本轮三种失败情形（缺 token / 假 token / API 不可达）干跑复验 14 项断言**，并修掉"API 不可达时零证据"的缺口（`run-20261006-0140-gitee-mirror-dryrun/`）|
| 5 hash/size/version/RID/资产类型对应 R01 最终包 | ✅ | `Validate update manifest against final artifact`（url 必须指向 win-x64 单文件资产）；本轮该门槛连同产物形态/版本门槛一起做了**本地干跑 18 例（每条都配 RED）**，见 `run-20261006-0110-releasejob-dryrun/` |
| 6 先本地 fake 验逻辑；真实 HTTPS 另补；外部写入须授权 | ✅ | 本地假服务器 18 项 + 真实 HTTPS 下载/篡改拒绝实测；**线上写入 🚫 需发布授权** |

### §5 R04

| 步骤 | 状态 | 证据 / 说明 |
| --- | --- | --- |
| 1 核对生产前端文件与 scripts/tests 工具测试 | ✅ | 清单 23 = 21（ui/tests）+ 2（scripts/tests）|
| 2 统一清单、可靠发现；漏必需文件/零文件失败 | ✅ | 双向断言 + 删项/加不存在的项各自 RED |
| 3 C#/Node、资源闭包、冷启动、版本/manifest 形成必需门槛 | ✅ | `release.yml` 的 `verify` 作业 + `release: needs: verify` |
| 4 Release 依赖同 SHA 通过，tag 不能绕过 | ✅ | 同上；且**未为验证触发任何正式发布**（遵守"不得为验证触发正式发布"）|
| 5 actionlint 校验 Actions；本地结构通过不替代 runner | ✅ | **actionlint 已于第 32 轮复现（exit 0、零输出；含反向对照）**，见 `run-20261006-0740-actionlint/`。本地替代物四个：引用审计（`run-20261006-0040-wf-audit/`）、发布门槛干跑（`run-20261006-0110-releasejob-dryrun/`）、触发条件验证（YAML 1.2 语义）、**表达式引用静态检查**（`run-20261006-0300-expr-check/`：`steps.<id>` 顺序/`matrix.<key>`/`env.<name>`/`needs.<job>`/upload 路径来源，0 问题且带 RED 自证）——**都不是 actionlint 的等价物**（表达式语法/类型/action schema/runner 行为仍未覆盖）|
| 6 VerifyHost 冒烟补凭据后端隔离或一次性用户/runner | ✅ | 内存后端 + 生产调用 0 次断言，已作为常驻门槛 |

### §6 R06（十类流程）

| 流程 | 状态 | 说明 |
| --- | --- | --- |
| 首启 | ✅ | 正式附件/新目录、九页资源、live API/版本、无开发依赖（19 项 + 九页 9/9）|
| 账号 | ✗ NOT_RUN | 需真实微软/Authlib 账号；设备码/取消/超时/切换/续期未执行 |
| Java/版本 | ✗ NOT_RUN | 需真实 Java 与原版下载/修复/启动/退出、加载器样例 |
| 下载 | ✗ NOT_RUN | "中断重启、终态进度、取消重试、重复任务"未执行（只有真实 HTTPS 单次下载+校验）|
| 整合包 | ✗ NOT_RUN | 已承诺格式导入/取消/失败清理未执行 |
| Mod/恢复 | ✗ NOT_RUN | running 只读、替换/回滚/记录、junction 边界未执行 |
| 存档 | ✗ NOT_RUN | 实例级备份/恢复、失败不损坏原件未执行 |
| 新闻/AI/皮肤 | ⚠️ 部分 | 新闻 12/12 缩略图、内容图标 22 张真实加载已验；**AI 外发同意/取消、皮肤真外链未验** |
| 更新 | ⚠️ 部分 | 真实 HTTPS 下载+校验、替换脚本语义（含代码页 437 与无权限两种形态）已验证；**"退出→替换→重启"整链未跑**；`ApplyUpdate`（启动器自己调用替换那一步）**无直接测试覆盖**（原因见 §2 R06 ③）|
| 故障 | ⚠️ 部分 | 坏 hash ✓、截断 ✓（既有用例）、占用 ✓、主源失败 ✓；**无权限 ✗**；提示真实性已随 R03 分类摘要核对 |

> §6 明令"不能用 mock/替身/BAT 沙盒替代"真机验收。因此**"替换脚本语义"那 7 项属段内证据，
> 不能当作"更新"流程通过**——本文档在 §2 与上表都已按此口径表述，不混算。

### §7 冻结、独立验收、待发布

| 条目 | 状态 |
| --- | --- |
| 1 选 > v1.0.0 版本并统一 tag/程序集/manifest/资产 | 🚫 需授权（版本定稿未做）|
| 2 精确准备候选提交（不纳入全部证据/临时文件/真实数据/密钥）| ✅ 计划就绪并**双向核过数**（`run-20261006-0250-candidate-set-audit/`）：白名单 146 条全部存在/未跟踪/**未被忽略**（直接 `git add` 即可，不需 `-f`）；反向 90 个未跟踪且未在清单里的文件全部是探针/备份/临时/工具缓存，**没有任何构建或测试必需输入被漏掉**。**🚫 未执行提交** |
| 3 冷 checkout 复验同 SHA、核实远程 CI 与产物绑定 | 🚫 需授权（远程 CI 未跑）|
| 4 封存输入/命令/TRX/GUI/资产 SHA；空证据/漏目标/坏 hash 必须失败 | ⚠️ 本地已封存（每轮证据目录 + 清单）；"**坏 hash 必须失败**"已由干跑 V6 用例实测（改 SHA256 → exit 1）；**远程/独立复验未做** |
| 5 Codex 重跑最强反证（新目录完整附件、旧版升级、失败回滚）| 🚫 由独立验收方执行 |
| 6 技术全满足后交付候选与上线动作，用户批准后才发布 | ✅ 遵守（未放行、未发布）|

### 6.1 本轮审计新发现（写在这里以免被"已完成"的叙述盖掉）

1. **R01 步骤 3 之前没做完**：许可证从未随 Release 交付、运行环境条件未声明 → **本轮已补**。
2. **R05 步骤 1 的"步骤误判"现场：第 17 轮查明它一直都在**（原始评估的 `snapshot-run.log` 里 8 条
   `exit=0 status=PASS 0.0s`），我此前"未留存"的判断是**只找了 D 盘源码树**导致的误报；现同时保留
   原始现场与 SR-05/06/07/08 的正向断言两层证据；
   同时补做了 cmd 机制复现（原始实现已重写，只能算机制复现）。
3. **清单覆盖范围此前被错误表述**：`snapshot-manifest.sha256.txt` 只覆盖白名单文件，
   不含 tracked 工作区版本 → 已在本节与 §3 纠正。
4. **PyYAML 结构校验的盲点**：YAML 1.1 把 `on:` 解析成布尔 `True`，所以结构校验**看不到触发条件**
   （`push.tags` 等）；这进一步说明 §5 步骤 5 的 actionlint **仍是 NOT_RUN、必须补**。
   两个本地替代物（工作流引用审计 `run-20261006-0040-wf-audit/`、发布门槛干跑
   `run-20261006-0110-releasejob-dryrun/`）都只覆盖 actionlint 之外的、对本次改动更相关的子集，
   它们**不是** actionlint 的等价物。
5. **新门槛此前没有反向对照**：只会说 PASS 的门槛等于没有门槛 → 本轮补了三个负向对照（见 §2 第 ③ 项）。
6. **首页截图的空卡片**已定位为截图时机（异步重绘），并已写明"不要读成首页坏了"。
7. **R01 步骤 6 的"无权限"分支本轮补齐**：让 OS 真的拒绝写入（只读属性）→ `replace_failed`、
   旧版本原样、仍拉起旧版本；顺带发现"**回滚 copy 也会被同一权限拒掉**"，
   所以无权限场景可靠的是"失败即不动目标 + 仍拉起旧版本"，**不能依赖回滚**
   （证据 `run-20261006-0008-r01-noperm/`）。ACL/磁盘满/安全软件拦截仍未验。
8. **（第 32 轮更正）"本机网络阻断"是误判**：`api.github.com`、`github.com` 与 `git ls-remote` 实测都通，先前只拿到 9 字节 `Not Found` 是**探测路径不对**；
    actionlint 已经镜像下载并复现（见 `run-20261006-0740-actionlint/`）。下面这段保留原判：
   结论不变（**仍 NOT_RUN**），但至少不再是"拉不下来"这种含糊说法（证据 `run-20261006-0050-actionlint/`）。
9. **"无权限"补齐第二种形态（ACL 显式拒绝）**，并踩到一个值钱的夹具坑：
   `icacls /deny "<用户>:(W)"` **不是**"只拒绝写"——`(W)` 含 `SYNCHRONIZE`，**连读都被拒**
   （第一版用例因此失败：测试宿主自己读不了目标文件）。改用细粒度 `(WD,AD,WA)` 才是"读 OK、写被拒"。
   顺带否掉"`copy /Y` 覆盖是删掉再重建"这个想当然的假设：实测它就是失败并保持原文件。
   **这条同时是对"验证夹具本身要审"的又一例**（与 R05、UI 门槛首版假通过同类）。
10. **工作流引用审计发现 `build.yml` 的产物证据没上传**：R02/R04 的两份 JSON 报告写进 runner 工作目录
    就随作业消失，只能从控制台日志看，而 `release.yml` 的同名证据一直是上传的。
    已按同一模式补上 `upload-artifact`（`if: always()`，失败时才最需要报告），审计脚本复跑 exit 0。
11. **发布门槛第一次被真正执行，第一条就推翻了我的预期**：`release` 作业的四条门槛此前只在 CI 跑过（从没跑过）。
    干跑 18 例后，我原以为"大写 SHA256 必须失败"，实测**放行**——因为 PowerShell `-match` 默认大小写不敏感，
    而客户端 `UpdateService` 也用 `Uri.IsHexDigit` + `OrdinalIgnoreCase`，**两端本来就一致**。
    所以修的是**注释**（原来写着"必须小写"，比代码更严）而不是收紧门槛：
    收紧会让发布端比客户端更严、拒绝一份客户端本可正常校验的清单。
    该行为已用 `V5-sha-uppercase-accepted`（期望放行）钉住。
12. **镜像步骤的失败路径干跑，查出一个"失败了却没留证据"的缺口（已修）**：
    Gitee API 不可达时步骤在写 `mirror-status.json` **之前**就 `throw`，
    而 `Upload mirror status` 是 `if: always()` + `if-no-files-found: warn` ⇒ **静默无证据**。
    修法是先落盘再抛（保留失败可见性）。注意这条**不是**靠读代码发现的，
    而是把步骤正文真的跑起来（第三种情形用故障注入模拟 API 不可达）才暴露的
    —— 与前几轮的教训同一类：**没被执行过的分支，等于没有验证过的分支**。
13. **"要发布的那条 publish 配方"此前从没跑过**：`release.yml` 带 `/p:Version=${{ }}`，
    而本地所有验证（含快照验证器的 publish）都不带它。第 14 轮按原样执行并用独立冷启动验收 → PASS（当时 18 项；第 18 轮起 19 项），
    且**版本键控解包在非 1.0.0 版本下也正确**（解包目录 `9.9.9-5f6b42a61753`，不是复用旧目录）。
14. **两句"看起来像事实"的话被实测推翻，且其中一句是我自己写的**：
    ① csproj 声称 `CopyOutputSymbolsToPublishDirectory=false` 能清掉原生 PDB —— 实测三次发布全都带
    （84MB + 20MB），真正靠的是 release.yml 的显式删除（csproj 注释已更正）；
    ② 第 9 轮我把"两份本地产物差 40KB"归因于发布会话参数/PDB —— 实际是
    **被 gitignore 的 `ui/assets/axolotl.png` 被嵌入 glob 带进了本地产物**（详见 §4 第 12 项），
    原归因已在 §2 就地更正并标注"第 9 轮那是错的"。
15. **候选集合双向核对（第 15 轮）**：白名单 **146 条全部可 add**（存在、未跟踪、**未被忽略**——
    这一点很关键：`git add` 对被忽略文件会拒绝或静默跳过）；反向查了 90 个"未跟踪且不在清单里"的文件，
    **没有一个是构建/测试必需输入**（含一个 0 字节空文件 `DownloadResumeTests.cs` 与一个只被文档引用的本地脚本）。
    同时发现 **R02 的资源能进候选，靠的是 `.gitignore` 第 37 行那句 `!ui/assets/pine-forest.svg`**——
    删掉它 `ui/assets/*` 就会把这张图吃掉，R02 的修复在仓库里会失效（而本地构建照旧通过）。已写进计划 §5.1。
16. **触发条件此前从未被验证**：PyYAML 把 `on:` 解析成布尔 `True`，所以"结构校验通过"压根没看它。
    第 15 轮用 YAML 1.2 语义重解析：`release.yml` **只有** `push: tags: ['v*']`（普通推送不会误触发发布）
    且 `permissions: contents: write`；`build.yml` 为 main/PR/dispatch。
    这条本身不是缺陷，但它是"publish 配方/发布门槛"能被信任的前提之一——之前属于"没人看过"的状态。
17. **表达式引用静态检查（第 16 轮）**：把 actionlint 里可静态化的那一类补上——
    `steps.<id>`（含**顺序**：必须由同一作业里更早的步骤定义）、`matrix.<key>`、`env.<name>`、
    `needs.<job>`、以及 7 条 `upload-artifact` 路径是否由更早步骤产生。结果 **0 问题**，
    13 条 `github.*`/`secrets.*` 如实列为"本地不可验证"；并用**注入错误引用**做 RED 自证（两个注入都被抓到）。
    本轮抓到的两个问题**都在检查器自己身上**：① 把 `dotnet publish` 的默认输出目录误报为"没有产生它的步骤"
    （假阳性，已加规则并在输出里写明是按"默认输出目录"匹配）；② 打印 `✓` 在 GBK 控制台上直接崩
    （`UnicodeEncodeError`，与本仓库 VerifyHost 固定 UTF-8 同类）。
    ——"判据写错会把结论带反"这条，本会话已经出现过五次，这次发生在新写的检查器上。
18. **冷启动门禁的环境加固（第 18 轮）**：原始评估明确要求 R01"不依赖 … `PINEGLOW_UI_DIR`"，
    而产品的 UI 根解析顺序里 **env 优先级最高**，检查脚本却从不清除继承来的 `PINEGLOW_*`。
    实测把 `PINEGLOW_UI_DIR` 指向有效替身 UI 时，**修前的门禁会因"内嵌 UI 根本没解包"直接失败**
    —— 所以**没有假通过漏洞**，真实问题是"门禁脆弱 + 归因误导"（只报 `health=`/`dirs=` 为空，
    看不出是环境带的旋钮）。已改为启动前清除继承的 `PINEGLOW_*` 并新增第 19 项检查；
    该项当场抓到会话里泄漏的 `PINEGLOW_WEBVIEW_DEBUG=9222`。
    **本轮我连续错了两回、都已更正**：① 先假设"会静默误通过"（实测是失败，方向错了）；
    ② 第一版实验把 WorkRoot 放在工作区内、五轮全失败被我当成"环境变量所致"，
    其实是既有沙箱现象（工作区里起的发布 EXE `HttpListener` 起不来），那几轮已作废留档。
    另有老坑复现：编辑工具抹掉 `.ps1` 的 BOM → PS 5.1 按 GBK 解析中文 → 语法失败；
    重新加 BOM 后恢复（这正是快照验证器「脚本编码守卫」要防的，本轮因为手跑绕过了它）。
19. **自更新替换在"非中文 Windows"下能不能用（第 19 轮）**：脚本是 UTF-8 无 BOM 的 `.bat` 且内联中文路径，
    而 cmd 按 OEM 代码页解析 `.bat` —— 此前只在本机 936 下验过，**"英文机器"这个问题从没被问过**。
    实测把代码页切成 **437** 后 7 个用例全绿、状态一致；机制是脚本自己那句 `chcp 65001`（前两行纯 ASCII）。
    已加回归位 `StartsWith("@echo off\r\nchcp 65001")` 把机制钉住（因为删掉它在本机 936 下依旧全绿）。
    **同时查清一处覆盖缺口**：`UpdateService.ApplyUpdate`（启动器自己调用替换）没有直接测试覆盖，
    原因是它的 target 是 `Environment.ProcessPath`（当前进程自己），测它等于让脚本去覆盖正在运行的测试宿主；
    要安全覆盖需产品重构（不在本轮授权内），因此如实记为"未直接验证"。
20. **升级就位（旧配置存活）此前没人看过，第 21 轮核过是"已被覆盖"**：本工作区改了配置相关代码，
    而"装着 v1.0.0 的用户升级后配置会不会被重置"没有任何一轮问过。查清是**加法式**变更
    （只新增两个布尔字段，其中 `NewsFakeIpCompat` 是**安全相关**的 fake-IP 开关、fail-closed），
    且现有测试已覆盖旧形态加载保留、写回保留、凭证零接触，以及新开关的"缺字段/字符串/数字"语义。
    边界：没有已发布 v1.0.0 的产物可测，是**同类旧形态模拟**、不是真机升级。
    同轮盘点 24 个 `…ForTest` 测试缝：22 个被测试引用，另 2 个是我搜索范围过窄的**误报**
    （一个是 AsyncLocal 后备字段，一个被 VerifyHost 使用）——**"判据/范围写错会把结论带反"第 6 次出现**，
    这次发生在我自己刚写的审计里。
21. **交付脚本的静态解析扫描（第 22 轮，`run-20261006-0520-shipped-scripts-parse/`）**：
    白名单把 ~20 个工具脚本一并交付，而验证器只跑其中一部分——**语法坏掉的脚本会原样进候选**，
    而本地全绿时看不出来（BOM 被抹那次就是解析直接失败）。补了一次**只解析不执行**的全量扫描
    （PS Parser / `node --check` / 内建 `compile()`）：**21 PS + 29 JS + 2 PY 全部通过**。
    同轮盘点"交付 vs 自动执行"：52 个脚本类文件中，清单驱动 23（Node 测试）、验证器直连 6、
    人工/CI 两类 2，**另有 21 个无任何自动执行**（其中 3 个是运行时界面资源、已被九页验收覆盖；
    17 个是历史里程碑工具；1 个是 `audit-goal-evidence.test.py`——清单只覆盖 `*.test.cjs`，故它不在口径内）。
    含义：这 18 个工具/测试会**静默腐烂**，本轮只钉住了"语法层不会腐烂"，运行期未验证。
    **这个盘点我连错三次**（文件名匹配漏掉清单驱动的测试 → `Get-Content` 在纯 LF 文件上并行的老坑 →
    清单每行带 `<runner> ` 前缀而我拿裸路径比较），三处都记录在证据文档里。
22. **对"复评结论"本身的核对（第 23 轮，`run-20261006-0540-reassessment-audit/`）**：
    那份复评是你用来决定放不放行的文档，而本会话已查出我自己 8 处错误——所以把它的**每一条可量化声称**
    重新从证据里量了一遍（21 条）。**没有"说出来却没证据"的声称**；查到的是同一类问题：
    **写下的数字被后续轮次追平而过时**——G03「9 轮」→ **11 轮**、G07「共 10 轮」→ **11 轮**、
    §7 的轮次序列、§8 自评「7 份专项证据」→ **24 份（共 49 个证据目录）**、
    R06「content 22 图」→ **20/22、21/21、22/22（各次不同）**。五处已在复评文档里就地改正，
    并把轮数写成"2118 起 11 轮"并列出序列，读者可以自己数（避免再次过时）。

23. **旧版迁移从"推断"变成"读已发布源码核对"（第 24 轮，`run-20261006-0600-v100-migration/`）**：
    此前那张协议兼容矩阵是**靠阅读当前代码推断旧客户端行为**。本轮发现**旧版源码一直就在本地**——
    仓库有 `v1.0.0` tag，公网 Release 的 Actions `head_sha` 也能查到，于是 `git show v1.0.0:…`
    直接读了已发布客户端。结果：清单模型**逐字节相同**、解析大小写不敏感、**没有**严格校验、
    默认源就是那个待写入的 Gitee `update.json`、下载按 `Url → FallbackUrl` 回退
    ⇒ **R03 的写入动作从"看起来对"变成有依据**。
    **同时查到一条必须写进发布说明的迁移注意**：旧客户端的替换脚本用 `timeout /t 1 /nobreak` 且等待**无上界**
    （正是我们第 10 轮实测卡死、已在工作区修掉的写法）——从 ≤1.0.0 升级时，若启动器是被脚本/父进程
    （stdin 被重定向）拉起的，更新可能停在"正在应用更新"；资源管理器正常启动的用户不受影响。
    旧客户端代码无法再改，所以这条**只能靠发布说明**兜住（见发布手册 §5）。

24. **原地升级的两个隐患（第 25 轮，`run-20261006-0620-upgrade-in-place/`）**：
    顺着上轮"读已发布源码"继续问"目录式老安装原地升级会怎样"，实测两条：
    ① **外置 `UI/` 是真隐患**：当前解析顺序里 **EXE 旁 `UI/`（第 236 行）先于内嵌包（第 245 行）**，
    放一个带标记的 `UI/index.html` 在新 EXE 旁启动，`GET /index.html` **返回的正是那个标记** ⇒
    升级后用户继续看旧界面（新服务配旧界面），与 R01 步骤 2 的要求正面冲突。
    发布包不会踩到（附件里没有 `UI/`，且单文件发布不再复制外置 UI），所以这是**升级就位特有**的问题。
    处置两条：**(i)** 发布说明写明"升级后删掉启动器旁的 `UI/`"（零代码改动；诊断看日志 `ui=<根路径>`）；
    **(ii)** 产品侧让内嵌包优先（需授权；产物 SHA 会变、既有证据全部重跑）。
    ② **残留原生 DLL 不是隐患**：把 7 个旧原生 DLL（含 `libSkiaSharp.dll` 11.6MB）放在新单文件 EXE 旁，
    进程正常、`/api/health` 200、**九页界面验收 9/9 PASS**。边界：同世代 DLL；跨世代未验证。

25. **升级就位留下的陈旧状态（第 26 轮，`run-20261006-0640-stale-apply-state/`）**：
    旧版本写在 `<配置目录>/updates/apply-state.json` 的替换结果会跨版本留着，**除了下一次替换没有任何地方清除它**。
    实测（portable 实例 + 真实单文件 EXE + 会话令牌）：`GET /api/update` **原样回传**陈旧记录
    （`{"result":"replace_failed","applied":false,"version":"1.0.0",…}`），接口与界面**都没有做版本比较**；
    界面只在 `applied === false` 时渲染警告横幅（`ui/js/launcher.js:3809`）。
    ⇒ 症状：旧版本上失败过一次替换、之后用别的方式升级到新版的用户，设置页会**一直**提示
    "上次替换失败 … · 1.0.0"。**不是功能故障**（不影响启动/检查/下载/替换），属陈旧提示；
    触发条件也窄（成功记录不显示；第 25 轮那种 `timeout` 卡死走不到写状态）。
    实测局限如实记：记录版本与运行版本都是 1.0.0，**"版本不一致时是否显示"未单独跑出来**，
    依据是"两处代码都没有比较"。处置两条（不改 / 只在版本匹配时显示）已列入复评决策项。
    顺带记录：`ResolveUiRoot()` 第 242–244 行注释声称"保证用的是当前 EXE 自带的界面"，
    但 EXE 旁 `UI/` 仍排在解包之前——注释理由与真实优先级不一致（第 25 轮隐患的另一面）。

26. **持久化标识核对（第 27 轮，`run-20261006-0660-persistence-identity/`）**：
    前几轮查的是"旧版本留下的**文件**会不会干扰新版"，这轮查更致命的一类：**持久化标识有没有变**
    （变了就会静默丢掉用户的设置或登录）。与本地 `v1.0.0` tag 逐项比对：
    配置目录（`%APPDATA%\MCMiniLauncher`、portable 为 `<EXE>/data`）**同一句**；
    凭据目标前缀 `MCMiniLauncher/` **同一常量**、条目名相同且只**新增** `ProxyPassword`；
    内置默认更新源**同一字符串**（另新增 GitHub 备用源）。
    ⇒ **≤1.0.0 的配置、凭据、更新源三条链都没断**。
    按现场保护约定**没有**为了实测而向真实凭据库写入任何条目（约定是"凭据只读"），
    所以这是**源码级同一性证明**而非"旧二进制写、新版读"的端到端演示，已在证据里写明该边界。
    同轮把这五轮（21/24/25/26/27）的迁移结论汇总进复评 §3.1：**可以安全升级**，
    用户侧只需做发布说明里的两件事（删旧 `UI/`、提示旧客户端卡死风险），另有 3 条产品取舍待定。

27. **用户数据布局核对（第 28 轮，`run-20261006-0680-instance-layout/`）**：
    前四轮查完配置/凭据/更新源/清单/替换脚本/界面优先级，这轮补最后一块**用户最在意的东西——实例**。
    `git diff v1.0.0 -- InstanceIsolationService.cs InstanceService.cs GameInstance.cs` **无输出**
    ⇒ 三者与已发布版本**逐字节一致**；目录约定（`<游戏目录>/instances/<名字>`，重名 `" (n)"`）也逐行相同。
    另外精确检索确认**两版都不写注册表**（v1.0.0 里出现的 `CredReadW/CredWriteW` 是凭据管理器 API；
    我第一次把 `Run"` 也塞进模式，命中的是工作流步骤名，已用精确模式重做）。
    顺带列出应用实际写的 <配置目录> 子目录（`bisect/cache/diagnostics/logs/recovery/runtimes/UI/updates`），
    其中**只有 `UI/` 与 `updates/apply-state.json` 是版本敏感的**，都已在前几轮查清。
    ⇒ 升级不会让实例消失、也不留注册表足迹。

28. **候选范围远超整改（第 29 轮，`run-20261006-0700-scope-warning/`）—— 本会话最重要的一条**：
    前面 28 轮都在关 R01–R06，但没人量过"这个候选到底改了多少"。
    **⚠️ 本条数字已被第 31 轮更正**：正确是 **61 项 / +13,222 行**、既有功能 **≥9,359 行（≥70.8%）**，
    原文的 48 文件 / +11,954 / 6,201 行（52%）**分子分母都偏小**（漏了 v1.0.0 之后的 8 个功能提交）。
    以下为原文，数字以 `run-20261006-0730-scope-recount/` 为准：
    实测 `git diff v1.0.0`：**48 个 tracked 文件 / +11,954 行**（另有 146 个白名单新文件），其中 **约 6,201 行（52%）属于
    "评估开始前就存在的功能改动"**（AI 诊断 +930、新闻 +614、`launcher.js` +2531、整合包 +204、Mod 二分 +315…），
    **这些在本轮从未被复核**；而 R06 的 NOT_RUN 项**恰好就是这些功能的真机验收**。
    更关键：**两个域无法切分** —— `LocalApiService.cs` 同时含功能代码（`NewsImageProxyService`、`/skin-image` 令牌）
    与整改代码（`ResolveUiRoot`/R01 内嵌包），`ui/js/launcher.js` 同理。
    ⇒ **发这个候选 = 连同那 ~6,200 行未复核功能一起发**；发布决定因此从"R01–R06 关不关"变成**三个**问题
    （整改关没关 / 既有功能要不要一起发、依据是什么 / R06 的 NOT_RUN 要不要在发布前补）。
    已把这条作为 **§5 第 0 项（先决条件）** 与 §1 的醒目警示写进复评，并在 §4 表里单列。

29. **发布说明草案已成形，但有三处占位卡在决定上（第 30 轮，`run-20261006-0710-release-notes/`）**：
    把手册 §5.4 要求的"发布说明必须写"落成了 `release-notes-draft.md`：已知限制（仅 win-x64 / 需 WebView2）、
    迁移说明**两条 must**（旧客户端 `timeout` 卡住 → 手动下一次；升级后**删掉 EXE 旁的 `UI/` 目录**）、
    配置/凭据/实例不丢、附件与校验表、以及一份**未验收清单**和**禁止措辞清单**。
    卡住的三处：`<版本号>`（须 > v1.0.0）、§1 发布范围（§5 第 0 项未决，已写成 A/B 两案），
    以及 sha256/大小（要等真发版产物）。
    ⇒ **这份稿子把"发布范围"这个决定从抽象问题变成了要填一个勾选框**：定不下来，说明就发不出去。

30. **重算候选范围：既有工作 ≥70.8%，不是 52%（第 31 轮，`run-20261006-0730-scope-recount/`）—— 自查纠错**：
    复核自己第 29 轮的数字时发现**分子分母都错了**：正确是 **61 项 / +13,222 行**（原写 48 / +11,954）。
    用 `git diff v1.0.0` / `v1.0.0 HEAD` / `HEAD` 三段分解 ＋ **原始评估自己留下的**
    `tracked-source-hash-check.json`（161 个 tracked 文件在 2026-10-05 的哈希）逐文件二分：
    ① 已提交部分 40 文件 **+4,135**（v1.0.0 之后的 **8 个提交全是功能/文档**，无一是整改）；
    ② 未提交部分 41 文件 +9,339，其中**哈希未变的 31 个文件 +5,224 行** = 自评估以来**没人动过** ⇒ 纯既有工作；
    ③ 哈希已变的 10 个文件 +4,115（含 `LocalApiService.cs` +2,991，既含功能又含整改，**不可切分**）。
    ⇒ 既有工作 **≥9,359 / 13,222 = ≥70.8%**，本会话与原始整改的改动**至多 ~3,863 行（≤29%，且是上界）**。
    **同时补一条对决定很重要的反证**：这 5,224 行里 **5,095 行（97.5%）所在文件被 93 个已执行测试文件按名引用**
    ⇒ 功能域不是"没测过的代码"，而是"**单元测试绿、真机验收 NOT_RUN**"（R06 的 NOT_RUN 项正是它们的真机验收）。
    已更正三份文档（scope-warning 加更正横幅、复评 §1/§4/§5/§7、本文件第 28 条）。

31. **actionlint 复现了，而且"网络阻断"是我的误判（第 32 轮，`run-20261006-0740-actionlint/`）**：
    通过 `ghproxy.net` / `gh-proxy.com` 两个镜像下载 `actionlint_1.7.7_windows_amd64.zip`，
    **两个镜像字节数（2,229,473）与 SHA256（`7f12f180…`）完全一致**，且与**官方校验表**里
    `windows_amd64.zip` 那一行相同；校验表本身又被**独立锚点**印证（技能文档固定的 linux 包 sha256 `023070a2…` 在同一表里）。
    解压出 `actionlint.exe`（`6d470a52…`，`-version` 报 1.7.7 / go1.23.4 windows/amd64），
    对**当前两份工作流**运行：**exit 0、零输出** ⇒ 与原始评估的 PASS 一致，**NOT_REPRODUCED 消除**。
    关键的一步是**反向对照**（否则"exit 0"可能是静默空跑）：注入"未加引号的 `#` 截断表达式"→ `exit 1`
    `got unexpected EOF while lexing end of string literal`；注入残缺表达式 → `exit 1` `unexpected end of input while parsing variable access`。
    ⇒ 同时更正：`api.github.com`、`github.com`、`git ls-remote` **本机都通**，先前只拿到 9 字节 `Not Found` 是**探测路径不对**。

32. **远程真相：真实更新链路 PASS、7 个提交未推送、版本定稿变成硬要求（第 33 轮，`run-20261006-0750-remote-truth/`）**：
    ① **真实更新链路 PASS**（用已冻结候选 `5009cf73…` 驱动）：阶段 A 主源 Gitee 404 → **真实备用源降级成功**；
    阶段 B **真实发布资产** 43,989,943 B 下载完成、字节与 SHA256 与真实资产一致、无 `.part`；
    阶段 C 篡改清单被拒且无残留。与第 19 轮的区别要诚实：那次**也**是真实网络且 `usedFallback=True`，
    但资产是 Gitee `README.md`（**1,618 B 替身**），且备注被写成 `primary-ok`（**与同文件 `usedFallback=true` 矛盾**）——
    已在旧目录加 `CORRECTION.md`。本轮增量 = **真实发布资产** + **如实标注主源失败**。
    ② **7 个提交未推送**：`git ls-remote` 实测远程 `main`=`c8aa286`，本地 HEAD=`f145d14`（差 7 个提交 / 39 个文件，全是功能与文档）。
    ⇒ 推送不只是发一个候选提交，还会公开这批功能提交；工作流是 `on: push`，推送即触发一次真实 CI（正是 G14 的解锁动作）。
    ③ **版本定稿是硬要求**：候选 EXE `FileVersion=1.0.0.0`、`ProductVersion=1.0.0`（`csproj <Version>1.0.0</Version>`），
    而 `LauncherService.LauncherVersion` 取**程序集版本**（真实链路实测 `currentVersion=1.0.0`），
    `release.yml` 又用 `/p:Version=<tag>` 并**断言程序集版本==tag 版本**，且 **tag `v1.0.0` 已被占用**。
    ⇒ 必须定 **> v1.0.0** 并把 `csproj` 的 `<Version>` 同步改掉；否则所有客户端都会认为"已是最新"。
    ④ **已发布 v1.0.0 的真实清单**逐字段与新版 `release.yml` 生成逻辑一致（`version/url/size/sha256/notes/mandatory`）
    ⇒ 清单协议兼容从"读源码推断"升级为"对比真实发布物"。
    ⑤ **CI 真实历史更正**：该仓库 Build 长期 success、`Release` 在 3 次失败后于 v1.0.0 **发布成功**
    ⇒ "runner 从未跑过"不成立；仍成立的是"**本轮新增/改写的步骤**从未在真 runner 上跑过"。

33. **action 契约校验：actionlint 的盲区补上了，并查清"哪里真的没跑过"（第 34 轮，`run-20261006-0810-action-contract/`）**：
    actionlint **不校验 `with:` 里的输入名**（第 32 轮反向对照里故意写的 `bogus-input` 它没报），
    而本轮新增的 CI 步骤从未在真 runner 上跑过 —— 一个拼错的输入名就会让首次推送变红。
    于是用**每个 action 自己的 `action.yml`** 当权威做了静态校验，**零 `api.github.com` 调用**
    （匿名配额只有 60/小时）：引用→提交用 `git ls-remote`，`action.yml` 用 `raw.githubusercontent.com`，
    Node/SDK 存在性用 `nodejs.org/dist/index.json` 与 dotnetcli release-metadata。
    结果 `ACTION-CONTRACT RESULT=PASS`（15 个步骤 / 5 个 action / 硬失败 0）：
    所有 `with` 键都是声明过的输入、必填齐全、5 个 action 都是 `node20`（无淘汰运行时）；
    **Node `24.19.0` 与 SDK `10.0.301` 在官方索引里真实存在** ⇒ CI 会装到与本机完全一致的运行时。
    历史对照（v1.0.0/HEAD vs 工作区）把口径修正为：**四个 action 自身在 v1.0.0 就跑过**
    （`checkout@v4`/`setup-dotnet@v4`/`upload-artifact@v4`/`action-gh-release@v2`），
    **真正没跑过的是 `setup-node@v4` 整步、`setup-dotnet` 由 `dotnet-version` 改成的 `global-json-file`、
    以及所有新增/改写的步骤正文**。
    同时新增一条产品决定：现钉的都是浮动大版本且已落后（上游已有 checkout v7 / setup-dotnet v6 /
    setup-node v7 / upload-artifact v7 / action-gh-release v3），是否升级或按 SHA 钉死需你定
    （任何改动都会使工作流哈希失效 → 必须重跑 actionlint + 本脚本 + 全量验证链）。

34. **资产级回退真实验收 PASS：R03 客户端侧最后一条没验过的链接补上了（第 35 轮，`run-20261006-0820-asset-fallback/`）**：
    已有的真实网络验收把夹具清单的 `fallbackUrl` 写成**空串**（`scripts/verify-update-live-download.ps1:144`），
    于是"**清单 `url` 坏了、靠 `fallbackUrl` 救回来**"从来没被跑过——而候选发布的清单正是
    `url = Gitee 附件`、`fallbackUrl = GitHub 直链`（`release.yml:324-333`），**主源挂掉时全靠它**。
    新写 `verify-asset-fallback-live.ps1` 做三个对照（每个都全新冷启动）：
    **A** url=真实 Gitee 附件 → 完成、43,989,943 B、SHA256 一致（**顺带证明 Gitee 附件匿名可下**，
    不需要 cookie/referer）；**B** url=真实 404 坏链、fallbackUrl=真实 GitHub 资产 → 完成、字节与 SHA256 一致
    （**资产级回退成立**）；**C** 两条都是坏链 → `failed`，且错误文本**逐条列出试过的两个 URL**
    （正面证明顺序是 `url → fallbackUrl`，同时排除"客户端根本没读 url"这种解释）。
    判定 `PASS`，**19 项断言 0 失败**，退出码 0，用时 100 秒。
    顺带查清 Gitee 侧**已有 v1.0.0 release**（id 783393，附件 43,989,943 B，与 GitHub 资产**字节相同**）
    却**没有 `update.json`** ⇒ 这正是"旧客户端升不了级"的直接原因，也说明候选新增的 update.json 写入步骤是必要的。
    另发现低严重度小缺陷：状态字段 `updateJsonUrl` 写的是 `api/v5` 形式，实测**对无 token 的人是 401**，
    而客户端用的是公开的 web 形式（已列为可选修项，**未改**，因为工作流字节已冻结）。

35. **R06 的"外部服务腿"其实大多是通的——先裸探测，再用候选产物打真实上游（第 36 轮，`run-20261006-0830-r06-services/`）**：
    前几轮我把"新闻/AI/皮肤真外链"记成"本机假 IP 环境限制"。本轮先**裸探测**：
    `piston-meta.mojang.com` / `launchercontent.mojang.com` / `meta.fabricmc.net` / `meta.quiltmc.org` /
    `api.adoptium.net` / `api.curseforge.com` / `login.microsoftonline.com` / `gitee.com` / `github.com` **全部可达**。
    于是启动已验证候选产物、用会话令牌调它自己的只读接口，14 项探测中 **11 项 200 且是真实数据**：
    **`/api/versions/remote` 返回 917 个真实版本**（`26.4-snapshot-2` = 2026-09-29）；**`/api/news` 12 条真实新闻**，
    源 `launchercontent.mojang.com`、`isStale=false`、条目带真实外链且 `linkOk=true`；
    **`/api/sources` 3 个源带真实延迟**（official 724ms 可用、BMCLAPI 189ms 可用、MCBBS 镜像不可用）；
    **内容搜索 Modrinth 24 条 / CurseForge 24 条**（⇒ **内置 CurseForge Key 可用**）；`/api/java` 本机 `installations=[]`。
    **两条图片代理记为"未判定"**（不写成产品缺陷、也不写成通过）：`/api/news-image` 400（9ms）——
    代码里 `unknown_image` 是 **404**，故 400 属策略/DNS 类，结合本机 **fake-IP 198.18.0.x**
    与 R-02 的 fail-closed 默认，最可能是 `dns_private`（环境产物），但**没取到 error kind 正文**；
    `/api/content/icon` 504（5,046ms = `ContentIconService.ConnectTimeoutSeconds`）——而同一 URL 用 PowerShell 直接取是
    **200 / 22,884 B**，差异落在本机 fake-IP + 透明代理与应用连接策略之间，**在不改网络配置的前提下无法判定**。
    另一处**我自己的错**：早前用 `/skin-image` 探测得到 404，真实路由是 `/api/accounts/{id}/skin-image`，
    **需要账号**，无账号时 404 是正常响应——不能算产品失败。
    **更正决策项 1 的前提**：`/api/versions/remote` **正常工作是好的**（917 版本 / 2.4 秒），
    之前"常态 500 + 暂无可下载版本"的描述过重；准确说法是"**上游不可达时才退化**，且退化时缺类型化失败与重试"。
    并据此把 R06 的**服务腿**从 NOT_RUN 里分出来：真正还需要真机的只剩真实账号登录、Java 安装与游戏启动/退出、
    整合包导入、Mod/存档替换回滚、以及"下载后替换自己并重启"。

36. **微软登录：不需要真人的三段已经验过，而且是"全员登录失败"级别的那三段（第 37 轮，`run-20261006-0850-ms-login/`）**：
    账号项一直记 NOT_RUN，但其中三段不需要真人，坏掉的表现却是**所有人都登不上**：
    ① **内置 Client ID 对个人微软账号是否仍可用** —— 直连 `consumers/oauth2/v2.0/devicecode`
    （client_id `60934999-…` + `XboxLive.signin offline_access`）实测 **200**：`verification_uri=https://www.microsoft.com/link`、
    签发 user_code、`expires_in=900`、`interval=5` ⇒ **没有被撤销、账号类型正确、scope 与端点契约正确**；
    ② 候选产物自己的 `POST /api/accounts/microsoft/device` → **200/2.2s**，返回真实 userCode/verificationUri/expiresIn/interval；
    ③ `POST /api/accounts/microsoft/poll`（无人完成）→ **200/1.1s**：`{"ok":false,"status":"pending"}`
    ⇒ **轮询真的在打 Microsoft，且没把 pending 写成成功或失败**（设备码流程最容易假绿的地方）。
    判定 `PASS（真人步骤仍未验）`。**没有输入任何账号密码、没有完成任何授权**；两个待授权设备码 900 秒后自然过期。
    顺带确认：`refresh-skin` **只对外置登录（皮肤站）账号**开放（`LocalApiService.cs:3929-3936`）
    ⇒ 皮肤渲染这条腿确实需要真实第三方账号 + 令牌，不能靠本机补；`textures.minecraft.net` 可达（假哈希 404 符合预期）。
    账号项剩余的真人部分：输入 user_code 完成授权 → 令牌 → XboxLive/XSTS/Minecraft 服务 → 档案与皮肤披风 →
    取消/超时/切换/续期/离线各分支，以及启动游戏时的 clientid/uuid 注入正确性。
37. **本轮两处自我更正（操作层）**：① 第 37 轮收尾时我删了 **17 个空的** `pg-*` 临时目录
（`pg-modwrap-build-*`/`pg-codex-*` 等，属其它会话），**无数据丢失**（全是 0 文件目录），
但违反了我自己上一轮立的"别人的 scratch 一个不动"——已写入 `run-20261006-0850-ms-login/` 报告 §5，
此后只按"我自己证据里记录过的路径"清理；② 探测器脚本 kill 进程后立刻删工作目录会失败
（WebView2 仍占句柄），需加重试——这是脚本收尾问题，不是产品问题。

38. **图片代理根因追查：一条解释清楚，另一条**推翻了我上一轮的猜测**（第 38 轮，`run-20261006-0910-image-proxy-rootcause/`）**：
    上一轮把两条图片端点都写成"未判定（环境可疑）"，并猜是"fake-IP 被 SSRF 防护拒绝"。追代码后发现：
    ① **news-image 的 400 确实属设计**：它有针对 fake-IP 的显式兼容开关且**默认 fail closed**
    （`NewsImageProxyService.cs:62-64`、`AppConfig.NewsFakeIpCompat`），而 `unknown_image` 会是 404、400 只可能是策略/DNS 类 ⇒ **不是缺陷**；
    ② **图标那条我的猜测是错的**：`IsPublicAddress` 的私网清单里**没有 198.18/15**（允许），
    且"IP 被拒"会返回 **502**，而实测是 **504 = `IconStatus.NetworkError`**（`LocalApiService.cs:5218-5219`）。
    决定性对照（同一 URL）：PowerShell **200/22,884 B**；.NET 10 `UseProxy=true` **OK/1,688ms**；
    .NET 10 `UseProxy=false` 直连 **OK/1,592ms** ⇒ **网络路径本身通**，而应用的取图在它自己的 5 秒连接预算内失败。
    ⇒ 修正为：**图标那条根因在本机未判定，但已排除"SSRF 拒绝"与"上游不可用"**，是**必须在真机确认的疑点**
    （真机仍 504 就是产品侧问题）。已在上一轮报告 §3、复评 §3、手册 §6 写入真机检查项。

39. **图标端点复查：我第 38 轮说它是"必须真机确认的疑点"，重复实验证明**端点本身可用**（第 39 轮，`run-20261006-0920-icon-flakiness/`）**：
    同一候选产物、每次全新冷启动，共 **6 次**：`/api/content/icon` **4 次 200**（1,317/1,387/1,402/1,830ms，
    `image/webp` **4,800 B**）、**2 次 504 都恰好 5,046ms**（= `ContentIconService.ConnectTimeoutSeconds = 5`），
    且两次都出现在**最早**；`/api/news-image` **6 次全 400 / 7–9ms**（设计内 fail closed）。
    另外我按产品代码**原样复刻了它的 transport**（`UseProxy=false` + 自定义 `ConnectCallback` 绑定预先解析 IP + 5s 连接超时），
    对上游真实图标 URL（Modrinth 实际给的是 `.webp`）取图 **200 / image/webp / 4,800 B / 1,430ms** ⇒ **实现没错**。
    ⇒ 定性更正：**不是缺陷**；504 是本机 fake-IP + 本地代理组合下**偶发的连接超时**。
    只留一个**可选加固点**（已列为复评 §5 第 9 项）：图标取图是"5 秒连接超时且**不重试**" ⇒ 短暂抖动 = 用户看不到图标
    （页面其它内容不受影响）。真机检查项从"疑点"降级为"顺带看一眼"。

40. **原版下载真实验收 PASS：R06 的"下载"这一半不需要真机，已在真实 Mojang 源上验完（第 40 轮，`run-20261006-0940-vanilla-download/`）**：
    用候选产物自己的 `POST /api/versions/download` 真装了 **1.5.2**，46 秒完成，**10 项断言 0 失败**：
    任务终态 completed；**client.jar 大小 5,564,661 B 与 SHA1 `465378c9dc2f…` 与官方元数据逐字节一致**；
    版本 JSON 落盘；libraries 9 个 jar；`assets/indexes/pre-1.6.json` 落盘；
    **assets 索引里的 468 个唯一对象全部在磁盘上、0 截断、字节精确一致（44.01 MB）**；游戏目录 51.7 MB。
    **同时更正我自己的一个错误门槛**：第一遍我拿"与官方 `assetIndex.totalSize` 差 ±5%"当门槛并 FAIL 了——
    实际是 `totalSize` **把重复条目重复计入**（pre-1.6 索引 749 条 → 去重仅 468 个对象），
    我第一遍还错拿**索引的键**（虚拟路径）去和磁盘 hash 文件名比，得出"749 全缺"的假结论；
    改用 Python 按 `o["hash"]` 核对后真相是"0 缺失、0 截断"。脚本门槛已改成按 hash 逐对象核对（第 40 轮修订）。
    ⇒ R06 的"Java/版本"因此拆开：**下载=已验**；剩余=**Java 运行时安装**与**用 Java 启动/退出游戏**（真机），
    以及**加载器安装样例**（端点可达但安装器路径未实测，不建议在冻结期跑）。

41. **缺 Java 的用户：检测与拦截都对，但**没有"安装 Java"入口**（第 41 轮，`run-20261006-1000-java-entry/`）**：
    代码事实：`JavaRuntimeService.InstallAsync` **只有一个调用点**——整合包导入（`InstancePackService.cs:755-757`）；
    可自动下载的大版本只有 **17/21/25**（`JavaRuntimeService.cs:36-48`）；界面里与 Java 相关的控件只有
    `#btnJrePath`（路径选择器，`ui/js/launcher.js:666`），`/api/java` 是 **GET only**；
    而 i18n 写着"使用内置 JRE（默认）"，53 MB 单文件产物里**没有 JRE**。
    实测缺 Java 时：`/api/java installations=0`；`prelaunch-check` → `ok=False`，错误
    **"未找到可用的 Java 运行时，请先在设置中指定 Java 路径"**；`POST /launch` → **HTTP 400**（被正确拦住）。
    ⇒ 好的部分：检测准、提示明确、启动硬拦；不足：提示让用户"指定"却**不能"安装"**。
    已列为**复评 §5 第 10 项**（发布说明写清 Java 要求 / 加"安装 Java"动作 / 只改文案；任何 `ui/**` 改动都要整轮重跑）。

42. **Java 就绪验证 PASS（第 42 轮，`run-20261006-1010-java-ready/`）—— R06「Java/版本」只剩"真机点一次启动"**：
    按产品目录约定（`<配置>/runtimes/java-<major>-<os>-<arch>/…/bin/java.exe`）放入**真实 Adoptium Temurin JRE 17.0.20.1**：
    元数据可取；下载 **43,780,109 B / 26 秒，SHA256 `bc21a9392310…` 与 Adoptium 声明一致**；
    解压后 `java.exe` 可执行（`openjdk version "17.0.20.1" 2026-08-18`，84ms）；
    **产品自己发现了它**（`/api/java → installations=1, major=17`，路径正确）；
    **同一实例的 `prelaunch-check` 从"缺 Java"变成 `ok=True`、errors 为空**。判定 **PASS（6 项 0 失败）**。
    诚实边界：这**不是**在跑产品自带的 `InstallAsync`（它只有整合包导入一个入口），
    验证的是"来源可用 + 校验和可核 + 目录约定 + 发现与检查逻辑"。
    踩坑记录：`java -version` 写到 stderr，`$ErrorActionPreference='Stop'` 会把它当致命错误（已修）。
    至此 R06「Java/版本」= 版本清单 ✅ + 原版下载 ✅ + Java 就绪 ✅ + **真机启动/退出 ❌** + 加载器样例 ⚠️。

43. **真实整合包导入 PASS：一次补齐 R06 三项（第 43 轮，`run-20261006-1030-modpack-import/`）**：
    用 Modrinth 官方 **Fabulously Optimized v3.15.0**（`.mrpack` 1,245,888 B；`minecraft=1.18.2`、
    `fabric-loader=0.19.3`；清单 **43 个文件 / 27.4 MB** + **60 条 overrides**）走产品自己的
    `POST /api/instances/import`，**15 项断言 0 失败**：实例已登记（`version=fabric-loader-0.19.3-1.18.2`）；
    **43 个 mod 逐文件 SHA1 一致**（missing=0 bad=0）；**overrides 抽查 60 条全在**；
    原版 **client.jar 20,259,661 B 与 SHA1 `2e9a3e3107cc…` 与官方一致**；**assets 3052/3052 个对象齐全、0 截断**；
    加载器目录 `fabric-loader-0.19.3-1.18.2` 已生成；**Java 由产品自己下载安装**到
    `runtimes/java-17-windows-x64/jdk-17.0.20.1+1-jre/`（`java -version` 输出 `17.0.20.1`）；
    libraries 52 jar、mods 46 jar；游戏目录 **556.8 MB**。
    ⇒ 这一趟同时验证了 **整合包导入**、**Java 自动安装**（`JavaRuntimeService.InstallAsync` 的**唯一调用点**，
    这次是产品自己下载+校验+解压的完整路径）、**现代版本安装**（3052 个 assets 对象逐字节核对）。
    两个操作层教训：① 我的主脚本因 `StrictMode` 下空管道没有 `Sum` 属性而中断，**但导入在启动器进程里继续跑完了**——
    ⇒ 改用"事后核对"脚本（端口从启动日志取、令牌从界面 meta 标签现取）完成验收，并把三处同类表达式改成 `DirBytes()`；
    ② **"脚本失败"≠"被测行为失败"**，这次若混淆就会得出完全相反的结论。
    另发现一个低严重度缺陷：超 24 MiB 时提示"**请使用流式导入**"，但**不存在任何分片/流式导入路由**（界面也是整包 base64）
    ⇒ 死胡同提示（已列为复评 §5 第 11 项）。

44. **文件层"替换/回滚/备份/恢复"PASS（第 44 轮，`run-20261006-1100-file-transactions/`）—— 但先纠正了我自己的两个错误预期**：
    用候选产物的 API 对真实文件跑：**A 存档** 备份→改动→恢复；**B Mod** 可恢复删除→恢复；
    **C Mod** 事务化替换。**20 项断言 0 失败**。要点与纠正：
    ① **存档"恢复"是非破坏式导入**（`RestoreBackup` → `ImportSave`：目标已存在则写成 `TestWorld (1)`，
    **绝不覆盖正在用的存档**）——我原先断言"原地被改写"，是**我错**；正确核对是跟着返回的 `path` 走，
    并单独断言"在用的存档未被覆盖"（这正是数据安全属性）。恢复出的 `level.dat`/`region` 与备份前**逐字节一致**；
    恢复不存在的备份 → **400**。
    ② **替换的"校验"是上传字节的 SHA256 与声明是否一致**（完整性），**不是** jar 格式白名单——
    我原先用"假 jar"当反例，被正确接受，是**我错**；改用**哈希不匹配**后：**400 被拒、原文件字节未变、无半成品**。
    ③ 正向替换（哈希匹配）→ 200 `status=replaced`，落盘字节=新包；隔离删除返回
    `{recoverable:true,status:"quarantined",operationId:"2026…"}`，恢复返回 `{status:"restored",overwrite:false,conflicts:0}`。
    三个操作层教训：v1 卡死且**看不到卡在哪**（stdout 结束后才落盘）⇒ v2 改**每步立即写日志 + 30 秒超时**；
    `Set-StrictMode` 下空管道 `Measure-Object` 没有 `Sum`、且访问不存在的属性（我把 `fileName` 写成 `name`）会**直接中断脚本**；
    PS 5.1 传字符串 body 会把中文编成 `??????` ⇒ 改用 UTF-8 字节。脚本中断时**启动器仍在跑**，
    我又用"从界面 meta 标签现取令牌"的事后核对法在活实例上查清了真实字段。

45. **原版游戏真的启动了（第 45 轮，`run-20261006-1130-vanilla-launch/`）—— R06「启动/退出」在真机上证完**：
    本机有真实 GPU（RTX 4060 Laptop / Radeon 780M）、15.2 GB 内存与交互式桌面。完整链路：
    建实例 → 装 **1.18.2**（256 秒，3071 个资源文件）→ 就绪**真实 Java 17**（43,780,109 B / SHA256 与 Adoptium 一致）
    → 离线账号 → **`prelaunch-check ok=True`** → **`POST /launch` → `{"ok":true,"pid":5924}`**。
    游戏日志：`Setting user: AcceptanceTester` → `Backend library: LWJGL version 3.2.2` → `Narrator library … loaded`
    → `Reloading ResourceManager` → `OpenAL initialized` → `Sound engine started` → **10 个 `Created: …-atlas`**；
    进程实测 **窗口标题 `Minecraft 1.18.2`、工作集 2,685 MB**。终止后启动器把 `totalPlaySeconds` 从 0 结算为 **305**、
    `playCount=1` ⇒ **退出检测与游玩统计正确**。日志里唯一的错误是离线账号必然的
    `authlib InvalidCredentialsException: 401` 与 Realms `SignedJWT`——都**没有**阻止进入主菜单。
    庆祝之外的两条实话：① 用的是**离线账号**，所以**真实微软账号链路仍 NOT_RUN**；
    ② 产品**没有 API 停止接口**（只有 launch），本轮退出是外部 kill（用户关窗口是正常路径，但"程序化停止"缺失，可作可选加固）。
    另：脚本又在 `Set-StrictMode` 下被"未 `@()` 包裹的管道 + 标量 `.Count`"打断（游戏仍在跑，人工核对后收尾并修脚本）——
    与第 43/44 轮的 `Measure-Object` 空管道、字段名假设是**同一类**问题，已统一加 `@(...)`。

46. **运行态守卫实测 + 一个真实发现（第 46 轮，`run-20261006-1200-running-guards/`）**：
    真的把游戏跑起来（1.18.2 + 真实 Java 17 + 离线账号），在**运行期间**逐个打带守卫的端点：
    **重复启动 400/18ms、删 Mod 400/4ms（文件仍在）、列存档 400/7ms、列存档备份 400/3ms**；
    杀掉 JVM（pid=4652）后 **5 秒内**恢复：`GET …/saves` 回 200、删 Mod 回 200 并带
    `{recoverable:true,status:"quarantined",operationId:"20261005-215615-…"}` ⇒ **守卫跟着运行态生效与解除**。
    **发现（低严重度）**：`POST …/saves/{world}/backup`（**创建**备份）在游戏运行中仍返回 **200**——
    `HandleCreateSaveBackupAsync`（`LocalApiService.cs:2374-2382`）**没有运行守卫**，而 13 个兄弟操作都有
    ⇒ 对正在写盘的存档做快照可能不一致。已列为复评 §5 第 12 项（加守卫 / 明确允许 / 发布说明提示）。
    **另查清一条此前的误会**：这类 400 的正文**确实带清晰消息**（`{"error":"备份不存在：…"}` 等），
    之前读到"空正文"是 **PowerShell 5.1 的读取通道问题**（`Invoke-RestMethod` 抛异常时响应流已被消费，
    正文在 `$_.ErrorDetails.Message`）——**不是产品缺陷**。
    我这轮还犯了三个测试侧错误：读错错误通道（导致两条假 FAIL）、把删实例的路由写成
    `DELETE /api/instances/{id}`（真实是 `POST …/delete`，我拿到 404 却当成"被拒"）、以及一处字符串插值
    导致**整脚本解析失败**（"什么都没跑"伪装成"跑了没输出"）。后者已用 `Parser::ParseFile` 解析自检兜住。

47. **🔴 发现 blocker：加载器实例启动必崩（第 47 轮，`run-20261006-1230-fabric-launch/`）—— 本会话最重要的坏消息**：
    用 Modrinth 官方 **Fabulously Optimized v3.15.0**（1.18.2 + fabric-loader 0.19.3）导入成功（实例、加载器目录、
    38 个 natives 文件都在，`lwjgl.dll` 的 SHA1 与 sidecar 一致、PE 头正常），`prelaunch-check` ok，
    `POST /launch` 返回 `{"ok":true,"pid":13308}` —— **但约 10 秒后游戏崩溃**：
    `java.lang.ExceptionInInitializerError: java.lang.UnsatisfiedLinkError: Failed to locate library: lwjgl.dll`。
    **定位**：抓活动进程的完整命令行（9801 字符）后与第 45 轮的 Vanilla 对照——
    **Vanilla 有 `-Djava.library.path=…\versions\1.18.2\natives`，加载器实例完全没有**（也没有 `-Dminecraft.launcher.brand/version`）。
    进程序列证明**启动器自己拉起的第一个 JVM 就缺**（后面的 12172/6600/21896 是整合包自带 CrashAssistant 用
    `--args-file` 重新拉起的，同样缺）⇒ **不是模组问题**。
    **根因在 `LauncherService.ResolveJvmArguments`**：加载器（继承型）JSON 的 `arguments.jvm` **只有 Fabric 的一条**
    `-DFabricMcEmu= net.minecraft.client.main.Main `，于是命中 `arguments.jvm` 分支并 `goto` 跳过兜底三行
    （`-Djava.library.path` / `brand` / `version`），**父版本那套 jvm 参数没有被并入**。
    ⇒ **所有带加载器的实例都起不来**（整合包/加载器是承诺功能）；R06「承诺的加载器样例」由 NOT_RUN 变 **RED**。
    也说明"整合包导入 PASS"（第 43 轮）**不能**推出"整合包能玩"——**装得好 ≠ 起得来**；第 45 轮 Vanilla PASS 也不能外推。
    **未改任何源码**；修法两选一（修版本合并 / 走该分支时补 `-Djava.library.path`，并回归 Vanilla+Fabric 两种启动）。
    这一条应由用户决定"发版前修"还是"降级为已知限制并写进发布说明"。

48. **✅ 受控实验：只补一个参数，Fabric 实例就起来了（第 48 轮，`run-20261006-1300-fabric-fix-sim/`）**：
    拿**产品自己生成的那条命令行**（9801 字符，缺库路径），在 `-Dfile.encoding=UTF-8` 后**只插入**
    `-Djava.library.path=<natives>`（10015 字符），其余一字不改、原样执行：
    **`Loading Minecraft 1.18.2 with Fabric Loader 0.19.3` + `Loading 135 mods:` + `Backend library: LWJGL version 3.2.2`
    + `Sound engine started` + 10 个 `Created: …-atlas`、崩溃行 0、日志 2536 行、JVM 存活整个 150 秒**；
    基线对照是"208 行日志后崩（2 行崩溃标记）"。⇒ **第 47 轮的定位被证实**：唯一原因就是缺这个参数，
    补上即可进渲染管线。**修法两选一**（① 修版本合并治根；② 走 `arguments.jvm` 分支时缺则补），
    修完必须回归 **Vanilla + Fabric** 两种启动（脚本已备）。
    另记一个我自己的坑：`Start-Process -ArgumentList <单个字符串>` 会把整串当成一个参数（java 报
    `ClassNotFoundException: -Xmx5632M …`，不是游戏问题）；写 `.cmd` 又撞上 ANSI 编码与中文路径；
    最终用 **`System.Diagnostics.ProcessStartInfo.Arguments` 原样传递**才正确 ⇒ **别让 PowerShell 重新拼接完整命令行**。

49. **把 blocker 变成"审一眼就能拍板"：根因收敛到一行 + 三份补丁（第 49 轮，`run-20261006-1310-loader-fix-patch/`）**：
    **根因精确化**：`VersionResolver.ResolveInternalAsync` 里 `libraries` 是 `Libraries.Add(...)`（**追加**）、
    而 `arguments` 是 `Arguments = value.Clone()`（**整体替换**）⇒ 父版本（1.18.2）那套 `arguments.jvm`
    （含 `-Djava.library.path=${natives_directory}`）被加载器 profile 覆盖掉。
    **命令行侧印证**：第 47 轮那条 9801 字符基线命令行里，父版本库（`oshi-core`/`authlib`/`brigadier`/`jna`/`text2speech`…）
    与 Fabric 库（`fabric-loader`/`sponge-mixin`/`asm`/`intermediary`）**全都在**（⇒ libraries 合并成功），
    但 **`-Djava.library.path` 完全没有**（⇒ arguments 被替换）。⇒ 影响面是所有继承型 profile
    （Fabric 已实测；Quilt/Forge/NeoForge 代码层推断同病、未逐一实测）。
    **三份补丁（均未应用，工作区 `modified=44 / staged=0` 前后不变）**：
    ① `proposed-fix-versionresolver.diff`（**A 治根**，+48/−1：arguments 改为按 Minecraft 继承语义合并，新增 `MergeArguments`）；
    ② `proposed-fix-B-launcherservice.diff`（**B 最小兜底**，+6：走 `arguments.jvm` 分支时缺库路径就补）；
    ③ `proposed-test-versionresolver.diff`（**回归测试**，新文件 +39：断言父版本 `-Djava.library.path=${natives_directory}` 合并后仍在）。
    生成方式：源文件复制到 `%TEMP%`、只在副本上改，再 `git diff --no-index` ⇒ **工作区从未被修改**。
    **回归清单**：全量 903 测试 → Vanilla 启动 → Fabric 启动（命令行须含 `-Djava.library.path`）→
    因改 `dev/**` 源码 ⇒ 产物字节变 ⇒ **全量验证链重跑**（既有 EXE sha256 证据作废）；新增测试文件要进白名单。
    仍未擅自改源码：A/B/不修三条路由用户拍板。

50. **影响面收敛 + 合并算法独立验证（第 50 轮，`run-20261006-1330-merge-algo-check/`）**：
    ① **影响面按"profile 从哪来"分档**（代码级证据，避免夸大）：
    **Fabric**：`FabricService` 从 `meta.fabricmc.net/.../profile/json` 取回后**原样写盘** ⇒ 官方 profile 只有自己那一条 jvm ⇒ **已实测崩**；
    **Quilt**：`QuiltService` 同一套路（`meta.quiltmc.org/.../profile/json`）⇒ **代码层确定同病，未实测**；
    **Forge/NeoForge**：`InstallerLoaderService` **执行官方 installer** 再读它生成的版本 JSON ⇒ 形态可能不同 ⇒ **待定**（判定代价约 8–10 分钟/个）。
    ② **补丁 A 的合并算法单独验证通过**：把 `MergeArguments` 抄进 .NET 10 file-based app，喂真实的父子 JSON，
    得到 `merged.jvm=["-Djava.library.path=${natives_directory}","-cp","${class_path}","-DFabricMcEmu= …"]`、
    `merged.game` 保留 `--width/--height`，`MERGE-CHECK RESULT=PASS`。
    ⇒ 补丁 A 现有三重支撑：根因（第 49 轮）+ **只补该参数即不崩**（第 48 轮受控实验）+ **算法正确**（本轮）。
    边界：算法验证 ≠ 打完补丁的产品行为；Quilt 同病是推断；Forge/NeoForge 未下结论。`dotnet run` 的 obj/bin 已删。

51. **Forge 判定为"同病"（artifact 级）+ 一个未解释的 Forge 安装 404（第 51 轮，`run-20261006-1400-forge-probe/`）**：
    ① **Forge 不需要装就能判定**：直接看官方安装器里嵌的 `version.json`（`forge-1.20.1-47.4.3-installer.jar`，6,068,826 B，HTTP 200）——
    `id=1.20.1-forge-47.4.3`、**`inheritsFrom=1.20.1`**（继承型，与 Fabric 同形态）、
    `arguments.jvm` **16 条**里**没有** `-Djava.library.path`（是 `-DignoreList`/`-DmergeModules`/`-DlibraryDirectory`/`-p`/`--add-modules`…）
    ⇒ 因为 `VersionResolver` 对 `arguments` 是**整体替换**，父版本的库路径会被这 16 条挤掉，而 Forge 自己也没带 ⇒ **Forge 实例同样会在 LWJGL 初始化时崩**。
    影响面因此更新为：**Fabric 实测崩 / Forge artifact 级同病 / Quilt 同构极可能 / NeoForge 未核对**（该 maven 元数据里没有 1.20.1 家族）。
    ② **新观察（未解释）**：真跑 Forge 包导入（`optimized-fps` 1.20.1，`forge=47.4.3`）在 330 秒后失败：
    `Forge installer 下载失败：下载失败 HTTP 404：forge-1.20.1-47.4.3-installer.jar`。
    已实测排除：URL 拼错（错误里的文件名与 `GetInstallerUrl` 形态一致）、官方 URL 不存在（**200 / 6,068,826 B**，`.sha1` 也 200）、
    网络不可达（.NET 10 直连与走代理都 200）、UA 被拦（6 种 UA 全 200）、被镜像改写（`DownloadSource` 默认 official）。
    ⇒ **根因未定**，下一招是用产品自带代理设置把请求导到"会记录 URL 的本地代理"看真实地址。**本轮不下结论。**
    ③ **顺带发现设计缺口（低严重度）**：`LoaderInstallHelper.DownloadFileAsync` 用 `MirrorHelper.Rewrite`（**单地址、无回退**），
    而同类的 `GetCandidates`（**带回退**、版本清单在用）没被它用 ⇒ 选了镜像源且该镜像缺产物时会直接失败而不回退官方。

52. **用记录型代理追 404：排除"镜像改写"，但定因失败；顺带确认一个真缺陷——代理设置**启动时不生效**（第 52 轮，`run-20261006-1430-forge-404-trace/`）**：
    ① **新缺陷（低–中严重度）**：config.json 预置代理后启动，启动器首个网络请求**不走代理**（代理日志 **0** 行）；
    先 `POST /api/config`（保存一次）再请求 → **1** 行 ⇒ **代理设置只在"运行时保存配置"后生效，重启后失效（直连）**。
    代码根因：`ApiHttp` 静态字段初始 `proxyEnabled:false`（`LocalApiService.cs:41`）、`LoadConfig()` 不刷新（1051–1060）、
    只有 `SaveConfig()` 调 `RefreshApiHttpProxy`（1062–1071）。已列为复评 §5 第 13 项。
    ② **Forge 404 追查**：用产品自带代理设置把请求导到本机**记录型转发代理**（`logging-proxy.cjs`，CONNECT 隧道），
    代理日志显示启动器连的是**官方主机** `maven.minecraftforge.net:443`（**没有**镜像主机）⇒ **推翻"被 MirrorHelper 改写导致 404"**；
    但**经转发代理仍 404**（用时 316 秒，同一错误），而同一 URL 从本机 PowerShell（走系统代理）与 .NET（直连）都是 **200 / 6,068,826 B**，
    6 种 UA 全 200。⇒ **主机对、地址可用、无镜像改写、UA 无关**，但**路径不可见**（CONNECT 不显示路径），
    **404 究竟来自真实 CDN 还是本机 fake-IP/透明代理链，本轮无法判定**——倾向环境，**但未证明，不写成结论**。
    可选定因手段（未做）：改源码加一行日志 / MITM 看路径（要动本机信任库，本会话一直拒绝）/ 换网络环境复跑。
    ③ 附带：记录型代理可复用（HTTP 转发 + CONNECT 隧道 + 主机日志），是排查"到底请求了哪个地址"的干净手段。

53. **⚠️ 更正第 50 轮的推断：Quilt **不受**这个缺陷影响（第 53 轮，`run-20261006-1500-loader-blast-radius/`）**：
    把四个加载器的 **profile 原文**逐个取回来：Fabric（`jvm` 1 条，无库路径）、**Quilt（`arguments` 只有 `game`、没有 `jvm`）**、
    Forge（16 条，无库路径）、NeoForge（本轮补测 `21.1.254`：15 条，无库路径）、Vanilla（自带 `-Djava.library.path=${natives_directory}`）。
    再按**产品现有逻辑**（`LauncherService.ResolveJvmArguments` 179–197）逐条推演（`fallback-check.cs`）：
    **Fabric/Forge/NeoForge 走 `jvm` 分支且缺库路径 ⇒ 受影响（会崩）；Quilt 因为没有 `jvm` 数组而走兜底分支 ⇒ 拿到库路径 ⇒ 不受影响；
    Vanilla 自带库路径 ⇒ 不受影响**（`FALLBACK-CHECK RESULT=PASS`，受影响=3）。
    ⇒ 我第 50 轮写的"Quilt 极可能同病"是**错的**，已更正复评 §4 与手册 §6.5。
    另一个好消息：**补丁 A 对这两个"不受影响"的加载器没有副作用**——无 `inheritsFrom` 时 `MergeArguments(null, child)` 原样返回；
    Quilt 场景合并后 = 父的 `jvm` + 子的 `game`（仍有库路径，且 `game` 更完整，符合官方继承语义）。即"修 3 个、不动 2 个、顺带更正确"。
    边界：Quilt"不受影响"是按 profile 原文 + 逻辑推演得出，**未真的导入 Quilt 包跑一遍**（要 6–10 分钟；若写进发布说明可补跑钉死）。

54. **Quilt 实测：**不受影响**（第 54 轮，`run-20261006-1530-quilt-probe/`）—— 把第 53 轮的推演钉成实测**：
    用 Modrinth 真实 Quilt 包 `ukupvp`（`quilt-loader=0.21.0` + `1.20.1`，49 个文件 / 43.2 MB）导入（331 秒）后启动：
    ① 产品写出的 Quilt 版本 JSON：`inheritsFrom=1.20.1`、`arguments` 键=**`[game]`**、**`有 jvm=False`**、自带库路径=False；
    ② **启动器命令行 17,601 字符、含 `-Djava.library.path`**（走兜底分支）；
    ③ `launch → {"ok":true,"pid":35640}`，观测 122 秒：Quilt 相关行 **121**、`Backend library: LWJGL` 1、
    `Sound engine started` 1、**纹理图集 12**、**崩溃 0**、日志 **2,193 行**、JVM 全程存活。
    判定 **"Quilt 正常进入渲染管线且命令行含库路径（推演成立：不受该缺陷影响）"**。
    ⇒ **加载器影响面最终确定（每格都有依据）**：Fabric **实测崩** / Forge、NeoForge **artifact 级同病** /
    **Quilt 实测不受影响** / Vanilla 不受影响。发布说明可以据此写准。
    另记：上一版脚本在 `$vd.arguments.jvm` 处被 `Set-StrictMode` 打断（Quilt 没有 `jvm` 属性——**这恰是要证明的事实**），
    已改为统一 `PSObject.Properties` 判定；新脚本复用已导入实例，3 分钟跑完。同类脚本坑这是第 4 次，教训一致。

55. **出决策包 + 按最新结论更新发布说明草案（第 55 轮，`run-20261006-1600-decision-package/DECISION-PACKAGE.md`）**：
    本轮不再堆证据，而是把**所有待你拍板的事压成一页**：A 发布范围（A1 一起发 / A2 先验收功能域 / A3 只发整改部分）；
    B 唯一技术阻断项（加载器崩溃：B1 治根 +48/−1 + 测试 / B2 最小兜底 +6 / B3 不修并点名 Fabric/Forge/NeoForge），
    每格影响面都标了证据类型（Fabric 实测崩、Forge/NeoForge artifact 级、Quilt 实测不受影响、Vanilla 不受影响）；
    C 13 项产品决定逐条给建议与代价；D 三项授权（D1 提交推送 / D2 版本定稿与发布物 / D3 Gitee update.json）附**我会跑的确切动作**；
    E 一屏"已验证 vs 未验证"；F 建议的下一步顺序。并同步更新发布说明草案：
    §3 新增第 6/7/8 条已知限制（**加载器实例无法启动**、**需自备 Java（无内置 JRE，可自动下载仅 17/21/25）**、
    **代理设置需保存一次才生效**），§6 由"全部 NOT_RUN"改为"**已在真机验证**（原版下载安装、真实 Java、完整启动退出、
    整合包导入、文件层事务、运行态守卫、Quilt 49 mod 启动）**vs 仍为 NOT_RUN**（更新端到端、真实微软账号、Forge/NeoForge 安装、
    进入世界、CurseForge/MCM、自替换重启、UI 400 文案）"。另把复评 §5 的编号重排为 1–13 顺序（此前 13 排在 12 前）。

56. **✅ R06「进入世界 / 加载存档」不用 GUI 就验通了（第 56 轮，`run-20261006-1630-world-entry/`）**：
    这一格此前一直记 NOT_RUN（"需要 GUI 交互"）。本轮改成可复现的真机验收：
    ① 本机无系统 Java ⇒ 下 Adoptium **JRE 17**（43.8 MB，`openjdk 17.0.20.1`）；
    ② 用**官方 1.20.1 server.jar**（47,791,053 B，**sha1 `84194a2f…` 与 Mojang 官方一致**）跑一次服务器生成真实存档
    （flat/peaceful，日志 `Preparing level "QuickWorld"` → `Preparing spawn area: 83%` → **`Done (2.067s)!`**；
    落盘 `level.dat` + **4 个 region/*.mca**，共 2,186,941 B）；
    ③ 产品装 1.20.1（异步任务，`assets` 落盘 **620.6 MB**）→ 世界放进 `saves/QuickWorld`；
    ④ 实例 `customGameArgs = --quickPlaySingleplayer QuickWorld`、`javaPath` 指向该 JRE（**实例级 javaPath 实测生效**）；
    ⑤ `prelaunch-check ok=true` → `launch {"ok":true,"pid":29820}` → 日志：
    **`Starting integrated minecraft server` ×1**、**`Preparing spawn area` ×2**、**`WorldTester joined the game` ×1**、
    `Backend library: LWJGL` ×1、**12 个纹理图集**、**崩溃 0**、JVM 存活 150 秒；退出后 **`totalPlaySeconds=153`**。
    ⇒ 判定 **PASS**：世界真的被加载并进入了。**不含**"点选存档/切世界"这类界面交互。
    另记我自己的两个 bug（产品行为正确）：**`POST /api/versions/download` 是异步排队**，不轮询到终态就停启动器会把下载掐掉
    （随后启动报 `未找到版本 JSON`）；**`GET /api/downloads` 返回 `{"tasks":[…]}` 对象而非数组**，按数组取 id 会空转到超时。
    还有个小坑：`$jvmPid=0` 时 `Get-Process -Id 0` 返回 **Idle 进程** ⇒ 假的"存活=True"，已改 `pid -gt 0`。

57. **✅ R06「多实例并发」PASS（第 57 轮，`run-20261006-1700-multi-instance/`）**：这一格此前记 NOT_RUN。
    先读代码：`RunningInstanceRegistry` 是**按 instanceId 键**的 `ConcurrentDictionary` ⇒ 语义上不同实例可同时跑；
    再真机测（装 1.18.2，异步任务轮询到 `completed` 用时 250s，两个实例共享 gameDir、同一 JRE 17）：
    启动 `conc-a` → `{"ok":true,"pid":31644}`；启动 `conc-b` → `{"ok":true,"pid":24084}`；
    **与 gameDir 匹配的 java 进程 = 2 个（31644、24084），60 秒观察全程 2/2 存活**；
    **重复启动 `conc-a` → HTTP 400**：`该实例已在运行（进程 31644），请勿重复启动。`；
    杀掉两个 JVM 后再启动 `conc-a` → `{"ok":true,"pid":15280}` ⇒ **守卫解除**。
    ⇒ 判定 **PASS**，并确认守卫是**按实例**而非全局单例（解释了第 46 轮"运行中守卫"不会误伤另一个实例）。
    边界：未压测更多实例，未验多实例共享同一 `saves/`/`mods/` 的并发写一致性，未做内存上限测试。
    另：本轮脚本按第 56 轮的教训写对了——`POST /api/versions/download` **异步排队**要轮询到终态，
    且 `GET /api/downloads` 返回的是 **`{"tasks":[…]}` 对象**。

58. **⚠️ 导入"取消"实测：取消被接受，但半成品与临时目录都没清掉（中严重度，第 58 轮，`run-20261006-1730-import-cancel/`）**：
    用真实包 `Fabulously.Optimized-v3.15.0.mrpack`，`operationId` 由客户端生成（与 UI 一致），分两阶段：
    **A 导入中取消**：35 秒时 `POST /api/instances/import/cancel` → **被接受**（`{"ok":true,"cancelled":true,"pendingCancel":false,"message":"cancelled"}`），
    但随后：① **工作继续**（目录 51.6 MB → 312.6 MB → **364.7 MB**，HTTP 调用早已结束仍在涨，直到我杀掉启动器才停）；
    ② **半成品目录没清**：`instances/Fabulously Optimized 导入` = 364.7 MB / 2,805 文件，含 assets/libraries/versions，`mods` 0 个，**7 个 `.download` 残片**；
    ③ **临时上传目录没清**：`%TEMP%\pineglow-import-ac849e51…`（1.2 MB）仍在；
    ④ **客户端拿到的是"连接被关闭"而不是文档里的 409**（导入调用 224 秒后以 `status=0 / 基础连接已经关闭` 结束）；
    ⑤ 实例**没有进配置**（取消前后 `/api/state` 实例数都是 1）——这点是对的。
    **B 取消先到（tombstone）**：先取消（无在途操作）→ `{"cancelled":false,"pendingCancel":true,"message":"recorded_for_later"}`；
    随后用同一 id 导入 → **0 秒返回 409** `{"ok":false,"cancelled":true,"error":"导入已取消，未生成实例。"}` ✅ **完全正确**。
    **代码对照**：两处清理都在（`InstancePackService.cs:349` 的 `Directory.Delete`、`LocalApiService.cs:1497` 的 tempDir 删除），
    但 API 路径的 `_log` 是**空委托**（`new InstancePackService(ApiHttp, defaultGameDir, _ => { })`）⇒
    连"导入失败后未能删除半成品目录（需要用户手动清理）"这句话都**无处可去**。
    **未区分**：删除是"跑了但被 `.download` 占用挡住而失败"还是"异常路径没走到"（未插桩）——但**结果确定**：都留下了且无任何提示。
    已列为复评 §5 第 14 项（是否修 + 四条建议）。另记我自己的两个脚本坑：`/api/state` **没有** `gameDirectory`（StrictMode 直接中断，本会话第 5 次同类）、
    `config.json` 是**懒写**的（启动后立刻读会 FileNotFoundException）。

59. **✅ 更新链实测通过 + 独立复现已发布 v1.0.0 的启动缺陷（第 59 轮，`run-20261006-1800-update-chain/`）**：
    本地用 **HTTP** 清单（`config.json` 的 `updateUrl` 指过去）、产物用公开 HTTPS（清单校验强制），走完整链：
    `GET /api/update/check` → `{"updateAvailable":true,"latestVersion":"1.0.1","manifestSource":"http://127.0.0.1:…","usedFallback":false}`
    （HTTP 服务器日志确认清单**真被取走**）；`POST /api/update/download` 是**后台任务**（`downloading` → 轮询 → **`completed`**，
    `path=…\updates\Pineglow-Launcher-1.0.1-win-x64-single-file.exe`）；`POST /api/update/apply` → `{"ok":true,"message":"更新已启动，重启后生效。"}`；
    **EXE 被替换**（`sha256 5009cf73…` → **`6130db19…`，与清单逐字相同**）、**原进程退出**、
    `updates\apply-state.json = {"result":"applied","version":"1.0.1"}`、**触发重启**（1 秒后有进程写 WebView2 配置）。
    **但重启起来的进程崩了**：`SkiaSharp.SKImageInfo` 类型初始化 → **`DllNotFoundException`**。对照实验：
    **候选 EXE 在同条件（全新空目录只放 EXE）正常启动**；**已发布 v1.0.0 资产**（43,989,943 B，sha256 `6130db19…`）**崩**；
    把候选自解压出的 `libSkiaSharp.dll`(11.4 MB)/`libHarfBuzzSharp.dll`/`av_libglesv2.dll`（约 18.5 MB 未压缩，与两者体积差 +9.1 MB 吻合）
    放到 v1.0.0 旁边 → **它就能启动**。⇒ **独立复现了原始评审的 G07/R01（"只交付 EXE 漏必要依赖"），并实测确认候选的
    `<IncludeNativeLibrariesForSelfExtract>true</IncludeNativeLibrariesForSelfExtract>` 修复有效**。
    顺带发现**加固项**：`UpdateService.AddCandidate` 同时接受 http/https，而 `ValidateManifest` 只强制"产物 url 是 HTTPS"
    ⇒ 清单可走 HTTP，中间人可同时改产物地址与 SHA256（已列为复评 §5 第 15 项）。
    仍未验：**重启后新进程正常提供服务**（缺一个可公开获取、能启动的新版本产物）、Gitee 固定 `update.json` 源。
    另记我自己的坑（同类第 7 次）：后台任务接口必须**轮询到终态**（第一版紧跟着 apply 得到"更新包未就绪"——产品行为正确）。

60. **✅ 更新链最后一格补齐：重启后的新进程确实提供服务（第 60 轮，`run-20261006-1800-update-chain/update-chain-restart-verified.md`）**：
    承接第 59 轮（`apply` 后重启被触发，但重启起来的**已发布 v1.0.0** 因自身缺原生库而崩）。本轮把候选自解压出的
    `libSkiaSharp.dll`(11.4 MB)/`libHarfBuzzSharp.dll`/`av_libglesv2.dll` **预置到目标目录**，让"能启动的产物"跑起来：
    `apply` → `{"ok":true,"message":"更新已启动，重启后生效。"}`；**EXE 被替换**（`sha256 5009cf73…` → `6130db19…`，与清单逐字相同）；
    原进程退出；**`重启检测: 健康=True`**（同端口 `/api/health` 200）；其它同名进程 **1 个**；`apply-state.json={"result":"applied","version":"1.0.1"}`；
    **产品日志**：`09:14:50 正在应用更新 v1.0.1 → …` → **`09:14:52 === Pineglow Launcher 启动 v1.0.0 ===`** → **`[LocalApi] 监听 127.0.0.1:64714`**。
    ⇒ 更新链 **检查 → 下载 → SHA256 → 替换自己 → 原进程退出 → 重启并由新进程提供服务** 全部实测通过。
    **预置原生库的性质**：真实更新里新版本产物自带原生库（候选的 `IncludeNativeLibrariesForSelfExtract=true` 就是修这个），
    预置只是让测试用的旧产物能启动，属**测试夹具**。
    另记一个**我自己的判定偏差**：我手动再启动被替换后的 v1.0.0 做交叉确认，脚本报"没起来"，但产品日志显示它起来了
    （`09:16:45 === 启动 v1.0.0 ===` + `监听 127.0.0.1:62150`）——原因是我的判定要求先抓到界面里的会话令牌，
    而 **v1.0.0 的 UI 解析与候选不同**（v1.0.0 是 `ui=…\data\UI`，候选是 `ui=…\data\UI\1.0.0-41346c7082cb`，**版本键控解包正是 R02 的整改点**），
    令牌抓不到就误判。教训：**判定探针要选与产品版本无关的**（`/api/health`）。

61. **🔴 找到第二个 blocker：Forge/NeoForge 的安装器 URL 多了一段 ⇒ 必然 404（第 61 轮，`run-20261006-1900-forge-url-probe/`）**：
    做法：`dotnet build`（1 秒，只写 gitignored 的 bin/obj）后写探针**引用构建产物**，直接调产品自己的类型，并**反射**拿私有的 `GetInstallerUrl`：
    ```
    GetInstallerUrl("1.20.1-47.4.3") = https://maven.minecraftforge.net/net/minecraftforge/forge/forge/1.20.1-47.4.3/forge-…-installer.jar
    ```
    ⇒ **`forge` 出现两次**。对照 HEAD：正确 URL **200**（6,068,826 B）、产品拼法 **404**；NeoForge 同理（正确 200 / 6,972,013 B、产品拼法 404）。
    **根因**：`InstallerLoaderService.GetInstallerUrl`（`…:157`）拼了 `/{_groupPath}/{_artifactId}/…`，而两个子类传的 `_groupPath`
    **已经包含 artifact 段**（`net/minecraftforge/forge`、`net/neoforged/neoforge`）；metadata URL 是单独传的（拼法正确）⇒
    **只有"下载安装器"这一步坏**，所以现象是"导入跑到一半 404"。**修法一行**（去掉多余的 `/{_artifactId}`），
    已验证：按修法拼出的 URL 是 200，且**产品自己的 `DownloadFileAsync` 在正确 URL 上实测成功**（`RESULT=PASS 下载成功 6068826 B`）。
    **更正我第 51/52 轮的判断**：那个 404 **不是环境问题**——我每次手工探的都是**正确 URL**，而产品请求的是**重复段 URL**，
    两边探的不是同一地址，所以怎么查都解释不通（这是我的排查盲点）。**影响**：当前版本 **Forge/NeoForge 实例根本无法安装**（与网络无关）；
    且即便修好 URL 装上，它们**仍会因命令行缺 `-Djava.library.path` 起不来** ⇒ **两处都得修**。

62. **✅ 内容子系统实测 PASS（第 62 轮，`run-20261006-1930-content-subsystem/`）**：本会话此前只验过 `/api/content/icon`，本轮补上搜索与安装：
    ① **五类搜索全部返回真实数据**：Modrinth mod `sodium`（1.18.2/fabric）**18** 条（首个 `AANobbMI`）、整合包 `fabulously` **20** 条（`1KVo5zza`）、
    资源包 `faithful` **20** 条、光影 `complementary`（iris）**10** 条、**CurseForge** `jei` **24** 条（首个 `238222`=JEI 的 CF id）；
    ② **真实装 Mod**：`POST /api/content/install {category:mod, projectId:AANobbMI}` → `{"ok":true,"status":"queued"}` →
    轮询 `/api/content/tasks` → `{"status":"completed","progress":1,"message":"安装完成","title":"Sodium 0.4.1"}` →
    实例 `mods/` 落盘 **`sodium-fabric-mc1.18.2-0.4.1+build.15.jar`（1,318,645 B）**（版本匹配正确）。
    ③ **留作开放问题**：便携目录里**没有** `secrets.json`（也就没有 CurseForge Key），CF 搜索却返回真实数据；
    而 PowerShell 直连 `api.curseforge.com`（无 key）是 **403** ⇒ 二者不一致，**未定因、不下结论**（对验收的影响：只声明"本机上能用"）。
    另记我自己的坑（第 8 次）：`PSObject.Properties.Name -contains 'projectId'` **大小写敏感**，接口给的是 `ProjectId` ⇒ 第一版取不到 id、
    安装被跳过而误判 FAIL（**不是产品问题**）；以及"等任务完成"我写成"等任务列表变空"，而已完成任务仍留在列表里 ⇒ 白等 150 秒（产品行为正确）。

63. **✅ 镜像源模式（BMCLAPI）实测 PASS（第 63 轮，`run-20261006-2000-mirror-mode/`）**：设置里的"下载源"选项此前从未端到端验过。
    ① **我第一版用错了端点**：`POST /api/config {"downloadSource":"bmclapi"}` 返回 `{"ok":true}`，但 `/api/sources` 仍是 `selected=official`
    ⇒ 源码里只有 `HandleSelectSourceAsync`（`POST /api/sources/select {"key":…}`）会写 `config.DownloadSource`。
    **顺带观察（低严重度）**：`/api/config` 对**未知字段静默成功**（我用 `{"downloadSource":"official"}` 复测，`selected` 仍为 bmclapi）
    ⇒ 第三方脚本可能"以为改成功了"（界面走的是正确端点）。
    ② **正确端点下镜像模式 PASS**：`selected=bmclapi` → 装 1.5.2 **completed（126 秒；官方源对照 48 秒）**，
    `client.jar` 5,564,661 B、**sha1 `465378c9…` 与官方逐字一致**、assets **468 个**（与官方源那轮完全一致）；
    代理日志 15 个主机，镜像池 `bmclapi2.bangbang93.com`(8)、`bmclapi2/5/6.sakura.sld.tw`(13)、`bmclapi.qytzz.cn`(4)、
    `mirrors.cernet.edu.cn`(6)、`download.mcbbs.net`(2)、`oba*.hejuqingci.qingcilove.top`(8)、`open-bmcl-api.lunar.cc`(2)、`unnys3.sakura.sld.tw`(5)。
    **观察**：镜像模式下**仍有少量官方主机**（`libraries.minecraft.net` 6、`piston-meta.mojang.com` 1、`launchercontent.mojang.com` 1）
    ⇒ 发布说明**不要**写成"完全不走官方"。
    ③ 我自己的另一个坑：`Log ('判定: ' + $(if (...) { '…' } else { "…" + (...) + '）"' }))` 这种嵌套被 PowerShell 5.1 解析器判为
    "字符串缺少终止符"（且错误定位在**后续行**，具有误导性）⇒ 改成"先算变量再 Log"；v1 已删、v2（错端点）/v3（正确）留档。

64. **✅ "从列表移除" ↔ "把目录挂回" 验清（第 64 轮，`run-20261006-2030-instance-register/`）**：
    `POST /api/instances/{id}/delete`（源码注释写明"只改配置"）实测返回 `{"ok":true,"removedId":…,"removedName":"默认游戏",…}`，
    且 `versions/1.5.2/1.5.2.jar` 仍是 **5,564,661 B**（文件完好）✅；`POST /api/instances/register` 对**重复目录**与**不存在的目录**
    都返回 **400** 且文案明确 ✅。**第一版测试报的两个 FAIL 是假失败**——被第 65 轮那条默认实例缺陷 + "移除后自动重建默认实例"造成的
    （重建的默认实例占着同一目录，所以 register 的 400 是**正确的去重**）。教训：**测试期望要建立在"配置已落盘、实例集合稳定"的前提上**。

65. **🔴 第三个 blocker：全新安装下"默认游戏"实例不可寻址（第 65 轮，`run-20261006-2100-default-instance-id/`）**：
    在验 64 的过程中撞出来的。**最小复现**（全新便携目录、无 config.json）：连续三次 `/api/state` 给出的默认实例 id
    **三次都不同**（`cc81e482…` / `29b415ab…` / `a4c48f41…`）；用第一次的 id 调 `/launch` 与 `/prelaunch-check` 都是
    **400 `实例不存在：cc81e482…`**；一旦执行**会落盘**的操作（`POST /api/accounts/offline`）→ 再取两次 id **稳定**（`5ba1f970…`），
    `prelaunch-check` 返回 **200**（带"未关联版本/未找到 Java"的正常提示）。
    **根因**：`ConfigStore.LoadAsync()` 在"配置文件不存在"时 `new AppConfig` + `EnsureDefaultInstance` + `EnsureAccounts` 后**直接 return（从不落盘）**；
    在"文件存在但实例列表为空"时也 `EnsureDefaultInstance`，**只有迁移过（flag）才 SaveAsync**；而 `GameInstance.Id` 是**对象级新 GUID**
    ⇒ 每次 `Load()`（= 每次 API 调用）都造出**id 不同**的默认实例且不持久化。
    **影响**：全新安装用户界面上的"默认游戏"**不可寻址**（启动/预检/存档/Mod/删除全 400）；界面启动流程只做 GET（`loadAccounts`/`goTo`→`loadState`），
    所以"什么都不改、直接点启动"这条最自然的路径**必然踩中**；移除最后一个实例后同样触发。
    **修法一行级**：造完默认实例/账号后**落盘一次**（两条路径都要）。已列为复评 §5 第 17 项，**建议与另两个 blocker 一起修**（都要改源码 ⇒ 一次重跑全量验证链最划算）。

66. **📦 三个 blocker 的补丁集 + 验证清单；并补测"默认账号同病"（第 66 轮，`run-20261006-2130-patch-set/PATCH-SET.md`）**：
    ① 把三个 blocker 的补丁**集中到一处**（本目录 5 份 `.diff`，含副本）：**① 加载器 `arguments` 合并**（+48/−1）、
    **② 对应回归测试**（+39 新文件）、**③ 最小兜底版**（+6，与 ① 二选一）、**④ Forge/NeoForge 安装器 URL**（−1/+1）、
    **⑤ 本轮新出：`ConfigStore` 默认实例/账号落盘**（+6，两处：文件不存在路径 `SaveAsync`、文件存在路径把"实例列表为空"计入 `flag`）。
    每份都**干跑过 `git apply --check`：5/5 全部 ✅ 可应用**（本轮顺手修掉了三处会让补丁不可用的头部问题：diff 里的人类注释与 BOM、临时路径、新增文件缺 `new file mode`）、**9 步验证清单**（903 测试 → Vanilla/Fabric/Quilt 启动 → Forge 包不再 404 →
    默认实例/账号 id 稳定 → 全量验证链 → 重新记录新 sha256），并写明**改源码 ⇒ 产物字节变 ⇒ 旧 sha256 证据作废**。
    ② **补测"默认账号同病"**：连续两次 `/api/state` 的默认账号 id 不同（`dd89f67a…` / `36c780ea…`）；
    `DELETE /api/accounts/{id}` 先撞"至少保留一个账号"守卫（400）⇒ **id 不稳定已证明、后果同源**（同一 `LoadAsync()` 不落盘）。
    ③ 诚实边界：⑤ 的补丁**没跑过构建/测试**（按约定不改工作区源码），应用后必须由清单第 1/6/7 步确认；
    ① 与 ③ 是二选一，不要同时应用。

（第 66 轮附记）**补丁集已可交付**：`run-20261006-2130-patch-set/` 里 5 份 `.diff` 全部通过 `git apply --check`（5/5 ✅）。
生成过程中修掉三处会让补丁**不可用**的头部问题：① diff 文件里混入人类注释行 + UTF-8 BOM（git 判 "corrupt patch"）；
② 头部仍指向 `%TEMP%` 的临时副本路径（git 判 "invalid path"）；③ 新增文件缺 `new file mode 100644`（git 把 `--- /dev/null` 当"修改 /dev/null"）。
⇒ 教训：**给别人用的补丁文件必须是"纯 diff"**（无注释、无 BOM、路径仓库相对、新增文件带 mode 行），而且**必须干跑 `git apply --check`** 才算交付。

67. **✅ 服务器列表 + 延迟探测（ping）PASS；配置写入点静态审计无"改了不落盘"（第 67 轮，`run-20261006-2200-servers/`）**：
    ① 这条功能此前**完全没覆盖**。实测：`GET /api/servers` 初始 `{"servers":[]}`；`POST /api/servers {name,host,port:"25565"}` 正确把端口解析成 25565；
    **真实 ping 公网服务器** `mc.hypixel.net` → **`"statusText":"可连接 · 2278 ms"`** + 真实 MOTD（`Hypixel Network [1.8/26.3] … SKYBLOCK 0.27.1 | HALLOWEEN EVENT`）+ 版本名/人数，
    **2 秒返回**；未监听端口 `127.0.0.1:1` → **`"无法连接"`**、2 秒内干净失败；**重启启动器后两台都还在**（连缓存状态一起）⇒ 持久化正常；
    `POST /api/servers/{id}/delete` → `{"ok":true}`、列表 2→1；对**已删除**的 id 再 ping → **400 `服务器不存在：…`**。逐项 7/7 **PASS**。
    ② 顺带做了一次**静态审计**（起因是第 65/66 轮那条"改了内存不落盘"的 blocker）：`LocalApiService.cs` 里 **27 处 `config.X =`** 赋值，
    按所在方法检查是否调用 `SaveConfig(` —— 21 处"方法内没有"全部集中在 `PatchConfig`(18)/`PatchAiProviders`(3)，
    而调用者 `HandleUpdateConfigAsync` 是 `PatchConfig(config, root); SaveConfig(config);` 两步走 ⇒ **没有任何"改了却不落盘"的配置写入点**（全文件 20 个 `SaveConfig` 调用）。
    ③ 顺带澄清第 63 轮的观察：`POST /api/config` 忽略 `downloadSource` **不是**持久化问题，而是 `PatchConfig` **没覆盖该字段**（正式入口是 `/api/sources/select`）。

68. **✅ Mod 二分定位（bisect）状态机 PASS（第 68 轮，`run-20261006-2230-mod-bisect/`）**：这条功能此前完全没覆盖。
    在实例 `mods/` 放 **8 个假 jar**、把 **`mod5.jar`** 当元凶，按源码语义逐轮回报（`crashed=true` ⇒ 元凶在仍启用的另一半；`crashed=false` ⇒ 在被禁用的这半）：
    `start` → step=1、禁用 `mod1-4`（8 里禁 4）；第 1 轮 crashed=true → step=2 禁用 `mod5,6`；第 2 轮 crashed=false → step=3 禁用 `mod5`；
    第 3 轮 crashed=false → **`finished=true, culprit=mod5.jar`**（= 植入的元凶）。**3 轮收敛 = log₂8**，与二分语义一致。
    结束后 **`mods/` 下 `.jar=8 / .jar.disabled=0`**（文件全部恢复）；**`giveup`** 中途放弃同样恢复到 8/0；
    **过期 step**（送 100 而实际是 1）→ **409 `该轮已经推进，请用最新一轮的状态重新提交。`**（源码注释说这是防双击提交把答案记错轮次，实测有效）。逐项 **5/5 PASS**。
    另记我自己的三个坑（都在断言/脚本侧，产品行为正确）：① 变量跨轮复用导致"start 时禁用了半区"断言取到最后一次（空）的值 ⇒ 假 FAIL，
    改成直接看**响应体**；② `String.Replace` **替换所有出现**，想"只在 start 后插一行"结果循环里也被插；③ 多行锚点用了 `\r\n` 而文件是 LF ⇒ 锚点不匹配（本会话第 N 次踩行尾）。

69. **✅ 资源包 / 光影 的搜索与安装 PASS（第 69 轮，`run-20261006-2300-resourcepacks/`）**：第 62 轮只验了 `category=mod`；本轮补另外两个分类（**装进不同目录**，容易出错）：
    ① `resourcepack`：`q=faithful` 命中 20 条，取 `w0TnApzs`（Faithful 32x）→ 安装任务 `completed/安装完成`（9 秒）→
    **`resourcepacks/Faithful 32x - 1.18.2.zip` 10,868,241 B**；`GET /api/instances/{id}/resourcepacks` 返回 `packs:[…]`；`state.resourcePacksCount=1`。
    ② `shader`：`q=complementary&loader=iris` 命中 10 条，取 `HVnmMxH1`（Complementary Shaders - Reimagined）→ 任务 `completed/安装完成`（3 秒）→
    **`shaderpacks/ComplementaryReimagined_r5.9.3.zip` 553,397 B**；`GET …/shaderpacks` 返回 `packs:[…]`；`state.shaderPacksCount=1`。
    逐项 **7/7 PASS** ⇒ **mod / resourcepack / shader 三个内容分类都有真机证据**。
    另记我自己的三个坑（脚本侧，产品行为正确）：① 用了 `$pid` 当变量名 —— **`$PID` 是只读自动变量**，赋值直接中断脚本；
    ② 随后的"批量改名" `Replace('$pid = ','')` **把两处初始化赋值整行删掉** ⇒ 变量未初始化、`StrictMode` 再次中断
    （教训：**不要对脚本做全局字符串手术**，要按精确行替换并**立刻回读那几行**）；③ 轮询这次按 `status` 判终态（第 62 轮的坑已避开）。

70. **✅ 第三方（authlib-injector / Yggdrasil）账号的地址校验与失败路径 PASS（第 70 轮，`run-20261006-2330-thirdparty-auth/`）**：
    这是本会话唯一没碰过的账号类型。本轮不追求"登录成功"（需真实第三方账号），把能验的验干净：
    ① `serverUrl="not-a-url"` → **400（37 ms）** `认证服务地址必须是以 http:// 或 https:// 开头的完整地址。`；
    ② `serverUrl="https://example.com/"`（非 Yggdrasil）→ **400（553 ms）**，但**把整段 HTML 透传进错误文案**（见决定项 18）；
    ③ `serverUrl="https://littleskin.cn/api/yggdrasil"`（**真实公共 Yggdrasil**）+ 假凭据 → **400（1,085 ms）**，且**服务端 `errorMessage` 如实透传**
    （`用户名或密码错误。Incorrect username…`）⇒ 真实服务器的语义没被吞掉、也没挂起。失败后**账号数不变**（1→1）、`/api/health` 仍正常。逐项 **5/5 PASS**。
    ④ **authlib-injector 组件**（启动时经 `-javaagent:"…jar"=<认证服务地址>` 注入 JVM）：官方与 BMCLAPI 两个下载 URL 实测**都是 200 / 341,970 B**，
    产品按序兜底、要求 `length > 1024` 才算成功 ⇒ **没有 Forge 安装器那类 URL 缺陷**；但**只校验大小、没有 SHA256 固定**（见决定项 19，属加固）。
    ⑤ 未验（诚实边界）：真实第三方账号**成功登录**、皮肤/披风拉取、启动时 `-javaagent` **实际注入生效**（需已登录的第三方账号）。

71. **✅ Java 探测（`/api/java`）与恢复区（`/api/instances/{id}/recovery`）PASS（第 71 轮，`run-20261006-2350-java-recovery/`）**：这两个端点此前没单独验过。
    ① **Java 探测**：把启动器的 `JAVA_HOME` 指向真实 Adoptium JRE 17 ⇒ `GET /api/java` **发现 1 个安装、1 个有效**，
    路径正是 `…\jre17\bin\java.exe`，`major=17  vendor=Eclipse Adoptium  valid=True`；`POST /api/config {javaPath:…}` 后该端点**正确回显**。
    ② **恢复区**：初始 `{"retentionDays":30,"recoveries":[]}`；把 `RecoveryProbe.jar` 移入恢复区 → 返回
    `{"ok":true,"operationId":"20261006-032652-13f79f2c","recoverable":true,"status":"quarantined",…}` 且**文件从 `mods/` 消失**；
    `GET …/recovery` → **1 条** `{operationId, reason:"mod-delete", status:"quarantined", createdAt, expiresAt:"2026-11-05…"}`；
    `POST …/recovery/{operationId}/restore` → `{"ok":true,"status":"restored","overwrite":false,"conflicts":0,"files":[{relativePath:"mods\\RecoveryProbe.jar",state:"restored",sha256:"40AFF2E9…"}]}`，
    **文件回到 `mods/`**；还原后记录仍保留（作为历史，状态已变），与"保留期 30 天"一致。逐项 **6/6 PASS**。
    另记我自己的两个脚本坑：一处括号没闭合（解析失败）；`[byte[]](1..256)` 越界（byte 上限 255）⇒ 改 `[byte[]](0..255)`。

72. **📌 决策包刷新到最新状态；并把复评 §5 的决定项重排为 1–19 顺序（第 67 轮）**：
    ① 决策包（`run-20261006-1600-decision-package/DECISION-PACKAGE.md`）原为第 55 轮版本，此后新增了大量已验证项与决定项，本轮**整体刷新**：
    A 发布范围（A1/A2/A3）；**B 三个 blocker**（B0 默认实例/账号不落盘、B1 加载器启动缺库路径、B2 Forge/NeoForge 安装器 URL 多一段）
    每条都带"现象/影响 + 修法 + 验证状态"，并指向 `run-20261006-2130-patch-set/PATCH-SET.md`（5/5 `git apply --check`）与 **9 步验证清单**；
    C **19 项决定**逐条给建议与代价（并标出"我建议至少要做"的 10 项）；D 三项授权（D1/D2/D3）附确切动作；
    E **刷新的"已验证 vs 未验证"**（把更新全链、内容三分类、镜像源、服务器+ping、Mod 二分、第三方账号失败路径、Java/恢复区、
    以及"复现已发布 v1.0.0 的 EXE-only 启动缺陷（G07/R01）"都列进已验证；NOT_RUN 只剩真人/凭据/发布物相关 6 类）；F 下一步顺序。
    ② 顺带修正复评 §5 的编号顺序（此前 18、19 排在 16、17 之前），现在是 **1–19 连续**（自检：19 项、无重复、无缺号）。

73. **✅ 全量验证链复跑：GREEN / 失败 0，且发布产物逐字节一致（第 73 轮，`run-20261006-2400-final-refresh/final/final-refresh-report.md`）**：
    动机：本会话跑了 ~40 轮探针与真机测试，需要一次"最终门槛复跑"证明候选**没被测试碰坏**且**仍可复现**。
    结果：**模式 GREEN / 失败项 0** —— step0 自检、step1a/1b/1c（UI 闭包 + 闭包自检 + 清单双向一致 23/23）、
    **step1 Node 正式测试 pass 304 / fail 0**、step2/3 锁定还原、step6 build、**step7 C# 正式测试 失败 0 / 通过 903 / 总计 903**、
    step8 VerifyHost + step8b `[BisectSmoke] PASS`、step10 publish、**step11 独立冷启动 19/19**（窗口 `松光启动器`、`/api/health` 就绪、GUI 真起来了）。
    并且**重新 publish 出的单文件 EXE 逐字节一致**：`53052722 bytes  sha256=5009cf73d0801e83ed68e23829486b2d27d1a313e0db67b5acbdc17bf169383c`（与先前 11 轮验证的候选相同）。
    ① 唯一报错是**可选**的 `-CleanupSnapshot`：删隔离快照时 `VerifyHost` 目录被 `VBCSCompiler` 占用（`being used by another process`）——
    **不是验证失败**（该步骤在 7/7 判定之后）。建议脚本在清理前先停编译服务。
    ② 顺带发现并清理了 `%TEMP%` 下 **15 个残留 `pineglow-snapshot-*` 目录（12,151 MB）**：C: 可用 **105.9 → 117.1 GB**。
    ③ console 里同样如实记录了已知边界：`publish win-x64 (锁定还原探测)` 因 `packages.lock.json` 未覆盖 win-x64 RID 且缺 ILLink 任务包（实测 NU1004）
    ⇒ **RID 发布不在锁定还原范围内**（与早先结论一致）。

74. **✅ 三个 blocker 的补丁：在"真实打过补丁的构建"里全部复现验证通过（第 74 轮，`run-20261006-2500-patched-build/patched-build-report.md`）**：
    **纪律**：把仓库复制到 `%TEMP%\pg-patched`（排除 `.git/bin/obj/node_modules/docs`），在副本里 `git init` → `git apply` 四个补丁 →
    `dotnet build -c Release`；**工作区全程未被触碰**（`modified=44 / staged=0` 前后一致）。
    ① **补丁与构建**：四个补丁（versionresolver / test / installer-url / configstore）全部 `apply ok`；**build exit=0，0 警告 0 错误**（5 秒）。
    ② **复现 4/4 全部成立**：**B0** 连续三次 `/api/state` 的默认实例 id **完全相同**（`3793e1d006f2437089c7406af2df6e70`），
    且 `/launch` 由修复前的 **400 `实例不存在`** 变为 **400 `实例未关联游戏版本，请先在设置中安装版本`** ⇒ **id 已被正确解析**、只剩业务门槛；
    **B1** `VersionResolver.ResolveAsync` 的合并结果 = `["-Djava.library.path=${natives_directory}","-cp","${class_path}","-DFabricMcEmu= net.minecraft.client.main.Main "]`
    ⇒ 父版本库路径**保留**、子版本条目**追加**（`mainClass=…KnotClient`、`libraries=2` 也对）；**B2** `GetInstallerUrl("1.20.1-47.4.3")` 已是**单段** URL
    （`/forge/forge/` 不再出现），GET **200 / 6,068,826 B**。
    ③ **更强的一步**：在打过补丁的副本里跑 **C# 全量测试 = 失败 0 / 通过 904 / 总计 904**（= 903 原有 + **1 个新增回归测试**）
    ⇒ 补丁**能编译、不破坏现有测试、新回归测试确实在跑**。
    ④ **剩余（9 步清单）**：打过补丁的 Node 304、Vanilla/Fabric/Quilt 三种真实启动、全量验证链 + 新 sha256 —— 这些要在**你批准修之后**于工作区正式做。
    `%TEMP%\pg-patched` 本轮保留，供下一轮做"打过补丁的 Fabric 真实启动"。

75. **✅ B1 的端到端证据：打过补丁的构建下 Fabric 实例真的跑起来了（第 75 轮，`run-20261006-2600-patched-fabric-launch/patched-fabric-report.md`）**：
    用第 74 轮那个"打过补丁并构建成功"的副本（`%TEMP%\pg-patched`），跑**同一个真实包**（Fabulously Optimized 1.18.2 + fabric-loader 0.19.3），与第 47 轮候选对照：
    ① 导入 ok（**159 秒**）；实例 `loaderType=Fabric`、`version=fabric-loader-0.19.3-1.18.2`、`natives` **38 个文件**；`prelaunch ok=True`；
    `launch → {"ok":true,"pid":29176}`。
    ② **启动器命令行 10,561 字符，含 `-Djava.library.path`**（指向实例的 `versions\fabric-loader-0.19.3-1.18.2\natives`）、主类 `…knot.KnotClient`。
    ③ 游戏日志：**`Loading Minecraft 1.18.2 with Fabric Loader 0.19.3`**、`Loading 135 mods:`、**`Backend library: LWJGL version 3.2.2 build 10`**、
    **`Sound engine started`**、**10 个 `Created: …-atlas`**、**0 崩溃**、**1,335 行**、JVM 存活整个 150 秒观察期。逐项 **7/7 PASS**。
    ④ **对照**：候选在同样条件下**约 10 秒崩溃**（`UnsatisfiedLinkError: Failed to locate library: lwjgl.dll`、208 行日志）
    ⇒ **B1 从"代码级验证"升级为"端到端验证"**（候选必崩 → 补丁构建进渲染管线）。
    ⑤ 9 步清单剩余（**批准后**在工作区正式做）：打过补丁的 Node 304、**Vanilla / Quilt 启动回归**、Forge 包不再 404、全量验证链 + 新 sha256。

76. **✅ 补丁构建的回归：Vanilla 与 Quilt 都正常启动（第 76 轮，`run-20261006-2700-patched-regressions/patched-regressions-report.md`）**：
    目的：证明 B1 的"arguments 合并"修复**没弄坏另外两条路径**（第 75 轮已证 Fabric 从"必崩"变为"进渲染管线"）。
    ① **Vanilla 1.18.2**：`prelaunch ok=True`、`launch → {"ok":true,"pid":15056}`；命令行 **7,412 字符含 `-Djava.library.path`**
    （指向 `…\versions\1.18.2\natives`）**且含 `-Dminecraft.launcher.brand`**；标记 `lwjgl=1 sound=1 atlas=10 崩溃=0`，JVM 存活整个观察期 ⇒ **6/6 PASS**。
    ② **Quilt**（真实包 `ukupvp`，quilt-loader 0.21.0）：导入 ok（203 秒）、`loader=Quilt`、`prelaunch ok=True`、`launch → pid 15140`；
    命令行 **18,352 字符含库路径**；标记 `loader=3 lwjgl=1 sound=1 atlas=12 崩溃=0 行数=709` ⇒ PASS。
    ⇒ **三种启动路径（Vanilla / Fabric / Quilt）在打过补丁的构建上全部正常**，与第 53 轮"Fabric 被修好、Quilt/Vanilla 不受影响"的推演一致。
    ③ 两个**我自己造成的**假失败（产品行为都正确）：**删掉补丁副本的 `data/`** 会连带删掉"整合包导入时自动装的 Java" ⇒
    `prelaunch` 正确报"未找到可用的 Java 运行时，请先在设置中指定 Java 路径"并拒绝启动；**装完版本不会自动把"默认游戏"实例绑到该版本**
    （默认实例 `minecraftVersion` 仍为空串）⇒ 测试要**显式 `POST /api/instances {name,version}`**（与第 45 轮候选验收一致）。
    ④ 9 步清单剩余（批准后在工作区正式做）：打过补丁的 Node 304、**Forge 包导入不再 404**、全量验证链 + 新 sha256。

77. **🔴 Forge 安装的"第二半"缺陷（第 77 轮，`run-20261006-2800-patched-forge/forge-installer-B2b.md`）——在打过补丁的构建上发现**：
    在**打过补丁的构建**上导入同一个真实 Forge 包（`optimized-fps`，1.20.1 + forge 47.4.3）：导入 197 秒后失败，报
    **`Forge installer 失败，ExitCode=1。`** —— 与候选的 `Forge installer 下载失败：HTTP 404` **不是同一个错** ⇒
    **B2 的 URL 修复确认有效**（安装器已经下载成功，才会走到"运行安装器"这一步）。
    ① **新缺陷（blocker 级）**：按产品**完全相同**的 Java（它自动装的 Adoptium 17.0.20.1）与参数
    （`java -jar <installer> --installClient <gameDir>`、cwd=gameDir）手工复现，安装器自己输出：
    `There is no minecraft launcher profile in "<gameDir>", you need to run the launcher first!` → `There was an error during installation` → **exit 1**。
    **决定性对照**：往同一目录写一个 **46 字节**的最小 `launcher_profiles.json`（`{"profiles":{},"settings":{},"version":3}`）后，
    **同一安装器、同一 Java、同一机器**：**`Successfully installed client into launcher.` / exit 0**，并落盘
    `versions/1.20.1-forge-47.4.3`、`versions/1.20.1`、`libraries/net/minecraftforge/…`（**24 个文件**）。
    ⇒ **根因确定**：现代 Forge/NeoForge 安装器要求目标目录里有 `launcher_profiles.json`；产品的 `LoaderInstallHelper.RunInstallerAsync`
    只传 `--installClient <gameDir>`，**没有准备该文件** ⇒ 即使 URL 修好，**Forge/NeoForge 仍装不上**（同一功能的第二个缺陷）。
    **修法**：启动安装器前写最小桩（补丁 `proposed-fix-forge-launcher-profile.diff`，**已通过 `git apply --check`**，已并入补丁集）。
    ② **诊断性观察**：产品其实**捕获了安装器的 stdout/stderr 并逐行 `_log`**，但 **API 导入路径的 `_log` 是空委托**
    ⇒ 用户只看到 `Forge installer 失败，ExitCode=1`，**看不到那句关键提示**（与决定项 14 的第①条同源）。

78. **⚠️ Forge 端到端：本机验不了；但两个产品缺陷都已定位（第 78 轮，`run-20261006-2900-patched-forge-fixed/forge-fixed-report.md`）**：
    把 **B2b 补丁**（`launcher_profiles.json` 桩）也应用到 `%TEMP%\pg-patched` 副本（`git apply` ✅、重建 **0 警告 0 错误**）后重跑真实 Forge 包导入：
    **仍 `Forge installer 失败，ExitCode=1`**（**441 秒**，比只有 B2 时的 197 秒做得更多 ⇒ 很可能已越过 profile 检查、进入下载阶段）。
    ① **两组对照**（同一 Java、同一安装器，都带最小 `launcher_profiles.json`）：**纯 ASCII 路径** 与 **含中文+空格的路径**（与产品实例目录同形态）
    **报错完全相同**：`javax.net.ssl.SSLHandshakeException: Remote host terminated the handshake` / `Caused by: java.io.EOFException: SSL peer shut down incorrectly`
    → `There was an error during installation`、**exit 1**。⇒ **残余失败是"安装器自身的 HTTPS 下载在本机网络环境下握手失败"**，
    **不是中文路径问题**（我特意用两种路径对照，排除了这个常见猜测）。
    ② **结论分三层（不要混为一谈）**：**B2（URL 多一段）= 产品缺陷，补丁有效**（报错从"下载 404"变成"运行安装器失败"）；
    **B2b（缺 launcher_profiles.json）= 产品缺陷**（安装器自己说 `There is no minecraft launcher profile in "<dir>", you need to run the launcher first!`；
    写 46 字节最小桩后**同一次实验**里它越过了这道门：`Successfully installed client into launcher.`、落盘 `versions/1.20.1-forge-47.4.3` + 24 个 Forge 库）；
    **"Forge 端到端装得上"在本机无法验证**（环境）⇒ **既不声称"补丁让 Forge 装得上"，也不声称"补丁没用"**。
    ③ 发布口径：Forge/NeoForge 写"**当前版本无法安装**"（两个产品缺陷），并且**不要**写"已在本机验证过 Forge 安装"。
    ④ 顺带记一个我自己的假设与排除：我一度怀疑"中文路径导致安装器失败"（本会话 R05 就与中文/空格路径有关），
    用 ASCII/中文两组对照**排除了**它——两条路径的失败原因一模一样。

79. **📌 决策包与补丁集刷新到"四个 blocker / 20 项决定 / 6 份补丁"（第 75 轮）**：
    第 72–74 轮新发现的 **B2b（跑 Forge/NeoForge 安装器前没有 `launcher_profiles.json`）** 需要进入"一页拍板"，本轮把它们刷新：
    ① `DECISION-PACKAGE.md`：B 段由"三个技术阻断项"改为"**四个**（B0/B1/B2/B2b）"，B 表新增 **B2b 行**（现象：安装器直接报
    `There is no minecraft launcher profile in "<dir>"` ⇒ ExitCode=1；修法：启动前写 46 字节最小桩；验证：手工对照写桩后 exit 0，
    但**端到端受本机环境限制**——安装器自身 HTTPS 下载 `SSLHandshakeException`，ASCII 与中文路径报错相同、已排除中文路径）；
    C 段由 19 项改为 **20 项**（新增第 20 项 = B2b）；E 段的 NOT_RUN 里补上"Forge/NeoForge 端到端安装（需换网络环境）"；补丁集标注 **6/6**。
    ② `PATCH-SET.md`：补丁表新增第 **⑥** 行（`proposed-fix-forge-launcher-profile.diff`，+7/−0），
    并把第 5 步（Forge 导入）的期望更新为"**不再 404**（已验）**且**安装器要能跑到 `Successfully installed client into launcher.`（需正常网络）"；
    5/5 → **6/6 `git apply --check` ✅**（本轮逐份复跑确认）。
    ③ 复评 §7 索引同步标注"第 75 轮已刷新：四个 blocker + 20 项决定 + 6 份补丁"。

80. **✅ 打过 6 个补丁的候选：全量门槛的 11 个可执行步骤全部 PASS（第 80 轮，`run-20261006-3000-patched-final-gate/final/patched-gate-report.md`）**：
    动机：回答"**如果批准这些修复，全量门槛还过得去吗**"。做法：把"复制仓库 + 应用 6 个补丁 + 构建"的副本（`%TEMP%\pg-patched`）
    变成验证器可接受的仓库（`git add -A` + 一次提交），然后 `verify-source-snapshot.ps1 -RepoRoot <副本>` 跑整条门槛。
    ① **结果**：step0 自检、step1a/1b/1c（UI 闭包/闭包自检/清单双向一致）、**step1 Node `pass 304 / fail 0`**、step2/3 锁定还原、step6 build、
    **step7 C# `失败 0 / 通过 904 / 总计 904`**（903 原有 + **新增回归测试**）、step8 VerifyHost + `[BisectSmoke] PASS`、step10 publish、
    **step11 独立冷启动 19/19** —— **11 步全 PASS** ⇒ **补丁不破坏门槛**。
    ② 汇总里的"**失败项: 2**"经查**都是我手工副本的产物，不是补丁问题**：**(a)** `[FAIL] 存在含非 ASCII 但无 UTF-8 BOM 的 PowerShell 脚本 1 个：
    `scripts\run-dify-minimax-workflow.ps1`` —— 该脚本在**工作区里是未跟踪的历史遗留**（决定项 6 那一类），
    我为了让验证器接受副本做了 `git add -A`，把未跟踪杂项也纳入了快照；**工作区正式跑（第 73 轮）没有这条 FAIL**。
    **(b)** `快照文件数量不符: 期望 512 实际 363` —— 期望值 = 副本自身 tracked（366）+ 白名单（146），而实际 363：
    因为副本**不是真仓库**（复制时排除了 `docs/`、又带进若干未跟踪杂项）⇒ 文件集合不同。判据：同一脚本在工作区上跑是 `GREEN / 失败项 0`。
    ③ 顺带确认：工作区确实存在**未跟踪的历史遗留**（例：上面那个含非 ASCII 且**无 BOM** 的 PS 脚本，PS 5.1 会解析错乱），
    但它**不在候选白名单**里 ⇒ 不影响候选本身；这正是决定项 6"是否清理工作区历史遗留"的一个实例。

81. **📄 交付"一页版答卷"并刷新复评 §1 结论（第 81 轮）**：
    ① 新增 `docs/FINAL-ANSWER-2026-10-06.md`：直接回答原问题"**能不能作为新版本发布**"——**不能**，并把"原因变了两次"讲清
    （原始 R01–R06 → 前半段"要环境与授权" → **现在"还要写代码"：4 个产品缺陷 B0/B1/B2/B2b**）；
    给出**四类任务**（① 修 4 个缺陷，补丁 6/6 就绪且已在补丁构建上验证；② 20 项产品决定，含建议至少做的 11 项；
    ③ 发布范围三选一；④ 三项授权）、**必须真人/凭据的 R06 尾巴**（真实微软账号、Gitee token、AI Key、Forge 端到端需换网络）、
    **已经做完的**（原始 6 类阻断 + R06 大量真机项 + 全量门槛 GREEN）与**证据入口（6 个按序看的文件 + 104 个证据目录）**。
    ② 复评 **§1 结论整体刷新**：由"剩下的不是写代码"改为"**这一次确实还有代码要写**"，并新增 **B0–B2b 四行缺陷表**（影响 / 修法 / 验证状态），
    同时保留"候选范围 ≥70.8% 未复核"这条决定性前提（并把引用它的那句过时表述改成"别把 B0–B2b 当成全部问题"）。
    ③ 复评 §1 顶部加了指向答卷的指引。

82. **📄 交付"批准后一键执行脚本" + 证据索引 + 交付物自评（第 82 轮）**：
    ① **一键批准脚本** `run-20261006-3100-approval-runbook/apply-and-verify.ps1`（**默认 `-DryRun`**）：
    把 6 份补丁按**正确组合**应用（**B1 二选一**：`-B1Variant Root` 治根＋回归测试 / `Minimal` 最小兜底；B0/B2/B2b 始终应用），
    并打印 **9 步验证清单**（含每步期望值）。**已干跑验证**：5 份（Root 组合）`git apply --check` **全 ✅**、9 步清单正常输出、
    **工作区未被改动**（`modified=44 / staged=0 / HEAD=f145d14`）。⇒ 你批准后我跑一条命令，而不是临时拼命令。
    ② **证据索引** `release-remediation-evidence-2026-10-05/INDEX.md`：104 个目录的**唯一入口**——按"你想确认什么"
    指向 1–2 个目录（4 个 blocker 的复现与补丁验证、门槛与回归、R06 各功能域、范围与流程依据），其余标注为原始存档。
    ③ **交付物自评**（agent-self-evaluation 五轴）：Accuracy **5**（每条结论可追到原始 JSON；自己的假 FAIL 也写在交付物里）、
    Completeness **4**（候选 ≥70.8% 未逐行复核这个真实缺口）、Clarity **4**（104 目录负担，本轮以 INDEX 缓解）、
    Actionability **5**（决策包逐项建议 + 一键脚本已干跑）、Conciseness **3**（进展 1647 行 / 104 目录体量过大）
    ⇒ **总分 4.2/5**；三条改进：证据可导航性（已当场修）、按 A2 关掉未复核缺口（需你决定）、真正落地 4 个缺陷（需你授权）。

83. **🔄 状态重大变化：4 处修复已由另一会话（用户明确授权后）实际落到工作区；本轮做独立复验（第 83 轮）**：
    ① **工作区已变**：`modified` 44 → **47**；`ConfigStore.cs`（+143：进程内锁 + 原子写 `tmp→备份轮转→File.Move` + 首次加载落盘 + 已有配置缺实例时落盘 + `DefaultUpdateMirrorUrls` 备用清单源 + `ProxyPassword`）、
    `VersionResolver.cs`（+24：`MergeArguments` 父→子拼接，保留库路径与规则对象）、`InstallerLoaderService.cs`（±1：去掉重复 artifactId）、
    `LoaderInstallHelper.cs`（+22：`FileMode.CreateNew` 写最小 `launcher_profiles.json`，保留既有文件字节）；
    新增 3 个 C# 测试文件（`ConfigDefaultIdentityTests` / `VersionResolverInheritanceTests` / `LoaderInstallerContractTests`）并纳入白名单（**149 项**）；
    另有 `scripts/run-goal-validation.ps1` + `scripts/tests/` 与 3 份 docs（`harness-blocker-fix-report` / `-remediation-workflow` / `minimax-harness-continuation-prompt`）。
    ⚠️ **因此我此前准备的 6 份 `proposed-fix-*.diff` 已被取代**（不要再应用；仅作历史留证）。
    ② **独立门槛复跑（GUID 隔离快照，当前源码 310 输入）**：**模式 GREEN / 失败项 0** —— Node **305**、C# **917/917**、publish、**独立冷启动 19/19**；
    新单文件 EXE 53,052,722 B / **sha256 `6ed0c8db…b926f0`**（与修复前 `5009cf73…` 不同）；RID 锁定还原 NU1004 边界**保留**。
    ⇒ 实施方报告的数字被**独立复现**。
    ③ **真实启动回归**：**Fabric PASS**（真实包；命令行 11,002 字符含 `-Djava.library.path`；`LWJGL + 声音 + 10 图集 + 0 崩溃`、1,335 行）；
    **Vanilla 本轮 FAIL 但属我的用例顺序**（全新便携目录没有 Java ⇒ `prelaunch` 正确拒绝"未找到可用的 Java 运行时"）→ 待重跑；
    **Quilt NOT_RUN（导入进行中，实例已建、assets 447 MB）**。
    ④ **重要更正（推翻我上一轮的"环境阻断"结论）**：用产品同款 Java 17 手工跑官方 Forge 安装器（带最小 profile 桩）——**不带代理 exit 0（99 秒）**，
    落盘 `versions/1.20.1` + **`versions/1.20.1-forge-47.4.3`** + 全部依赖库；带代理（我猜的 7890，实际本机是 **7897**）才 `Connection refused`。
    ⇒ 第 78 轮那次 `SSLHandshakeException` 是**当时网络瞬时失败**，不是稳定环境阻断 ⇒ **B2+B2b 的产品级端到端（真实 Forge 包导入）现在可测**（待下一轮）。
    ⑤ **环境边界（独立诊断）**：在 `D:\松光启动器` 直接 `dotnet test` 大面积失败（**467 失败 / 450 通过 / 917**），
    失败信息全是 `UnauthorizedAccessException: Access to the path '…\Temp\pineglow-*' is denied`（**447 条不同路径**，均为测试自建临时根）；
    最小对照：同一路径模式从 pwsh / `cmd` 子进程 / `dotnet exec` 探针（同为 `Administrator`、同 TEMP）创建**全部成功**，
    只有 **xUnit 测试宿主**在普通 `Directory.CreateDirectory` 上被拒 ⇒ 是**测试宿主进程的沙箱/权限边界**，不是业务 RED；
    在 `%TEMP%` 的隔离快照里跑则 **917/917**。**未调整任何系统 ACL**。

84. **🔴 当前源码分阶段真实验收：Quilt PASS / Vanilla PASS / Forge **装得上但启动必失败**（新发现两个装配缺陷，第 84 轮）**：
    ① **Quilt PASS**：真实包导入 **ok / 201 秒**（上一轮那次">13 分钟不返回"**未复现**——纯净复跑正常返回；上一轮很可能是**我的客户端超时/并发带宽**所致，两次记录都保留）；
    启动：命令行 **15,419 字符含 `-Djava.library.path`**、`lwjgl=1 sound=1 atlas=12 崩溃=0 行数=708`。
    ② **Vanilla 1.18.2 PASS**：安装 169 秒 → 启动：命令行 **5,849 字符含库路径**、`lwjgl=1 sound=1 atlas=10 崩溃=0`。
    ③ **Forge 导入 PASS（269 秒）** —— 实例 `loaderType=Forge`、`versions/1.20.1-forge-47.4.3` 落盘、
    `installer.log` 34,095 行结尾 **`Injecting profile` → `Successfully installed client into launcher.`**
    ⇒ **B2（安装器 URL）+ B2b（`launcher_profiles.json` 桩）在产品级端到端成立** ✅
    ④ **但 Forge 启动 FAIL（新缺陷，代码位置已定位）**：`launch` 返回 `{"ok":true,"pid":…}`，JVM **0 秒 exit 1**、游戏日志都没生成。两个独立缺陷：
       **① `LauncherService.cs:197` 的 `list.DistinctPreserveOrder()` 去重**，把 Forge JSON 里**重复出现的选项 token**（`--add-opens`×2、`--add-exports`×2）
       吃掉成各 1 个，于是 JVM 把第二个 spec 当主类：`错误: 找不到或无法加载主类 java.base.java.lang.invoke=cpw.mods.securejarhandler`（手工复跑原始输出已存档）；
       **② Forge 专有占位符未替换**：生成的命令行里 **`${library_directory}` 出现 9 次、`${classpath_separator}` 7 次**（字面量），
       而 vanilla 占位符都替换了 ⇒ 模块路径指向不存在的位置 ⇒ 手工补回 ① 后失败变为
       `InaccessibleObjectException: … module java.base does not "opens java.lang.invoke" to unnamed module`（`SecureJar.from` / `BootstrapLauncher.main`）。
       我把两个都手工修正后：进程活到 10 秒、**游戏日志首次生成**并出现 **`GL info: NVIDIA GeForce RTX 4060 Laptop GPU … GL version 4.6.0`**，
       但随后仍在 `ModuleLayerHandler.buildLayer` 失败 —— ⚠️ **我用的 8.3 短路径含中文段且控制台乱码，该替换本身不可靠，因此不据此宣称"修好"**。
    ⑤ 证据：`run-20261006-3400-phases/`（`phases-report.md`、三个阶段 JSON/日志、`forge-launch-cmdline.txt` 19,503 字符、`forge-manual-run.json`、`forge-fixed*-cmdline-run.json`）。

85. **✅ Forge/NeoForge **启动**：三个装配缺陷全部定位，**组合修复后实测跑起来**（第 85 轮，`run-20261007-3600-forge-launch-fix/forge-launch-fix-report.md`）**：
    在**捕获到的产品命令行**上手工应用三处修正（**未改产品源码**）后，Forge 实例**存活整个 200 秒观察期、stderr 0 行、游戏日志 193 行、LWJGL=1、声音=1、图集=12、崩溃=0**，
    日志尾部还有 `[Forge Version Check/INFO] [forge] Found status: OUTDATED Current: 47.4.3` ⇒ 游戏完全跑起来了。三个缺陷：
    ① **`LauncherService.cs:197` 的 `list.DistinctPreserveOrder()`**（`EnumerableExtensions` 用 `HashSet`）把 Forge JSON 里**重复出现的选项 token** 去重：
    `--add-opens` 2→1、`--add-exports` 2→1 ⇒ JVM 把第二个 spec 当主类（`找不到或无法加载主类 java.base.java.lang.invoke=cpw.mods.securejarhandler`，0 秒 exit 1）。
    ② **`BuildReplacements`（333–359 行）没有 Forge 占位符**：产品命令行里 `${library_directory}` **9 次**、`${classpath_separator}` **7 次**都是字面量
    （`version_name` 有 ✓）⇒ 模块路径无效 ⇒ 补回 ① 后失败变为 `InaccessibleObjectException: … does not "opens java.lang.invoke" to unnamed module`。
    ③ **类路径里放了原版 client jar**：`-cp` 94 条里含 `versions\1.20.1\1.20.1.jar`、**不含**安装器产出的 `forge-1.20.1-47.4.3-client.jar`（该文件存在，4,840,443 B）
    ⇒ ModLauncher 报 `java.lang.module.ResolutionException: Modules _1._20._1 and minecraft export package net.minecraft.util.profiling.jfr.event to module immediatelyfast`。
    **修复配方（已验证）**：保留重复选项 token + 替换 Forge 占位符（含空格/中文路径**必须加引号**）+ 不把原版 client jar 放进 `-cp`。
    诚实边界：这是**"缺陷定位 + 配方验证"**，不是"产品已修好"；③的产品级修法（用 `forge-<ver>-client.jar` 还是干脆不放原版 jar）需要产品决策；NeoForge 未单独实测。

86. **✅ Forge 启动三处缺陷：补丁已备，并在"打过补丁的构建"上**8/8 验证通过**（第 86 轮，`run-20261007-3800-forge-launch-patch/forge-launch-patch-report.md`）**：
    ① 补丁 `proposed-fix-forge-launch-assembly.diff`（**5 hunk / +72−3 / 只改 `LauncherService.cs` / `git apply --check` ✅**）：
    **①** `DistinctPreserveOrder()` → `DeduplicateJvmArguments()`（新增 `RepeatableJvmOptions` 白名单：可重复选项**本身与其取值一律保留**，其余照旧去重、顺序不变）；
    **②** `BuildReplacements` 补 `library_directory` 与 `classpath_separator`（`version_name` 本就有）；**③** 新增 `SuppliesOwnClient(version)`
    （版本 JSON 的 `-DignoreList=` 含 `client-extra` 即 Forge/NeoForge）时**不把父版本 jar 加进 `-cp`**，`BuildClasspath` 的 `clientJar` 改可空。
    ② **验证（副本构建，工作区未改）**：复制仓库 → `git apply` → `dotnet build`（0 警告 0 错误）→ apphost 覆盖进已有冷目录 → **由产品自己 `/launch`**：
    **Forge 标记 行数=193 lwjgl=1 声音=1 图集=12 崩溃=0 java存活=1（整个 204 秒）**；**Vanilla 回归**：命令行 5,808 字符含库路径、lwjgl=1 图集=10 崩溃=0；
    逐项 **8/8 全 True**，判定 **PASS**。
    ③ **我第一次验证失败了，原因是我自己的补丁有 bug**：初版写成 `bool keep = previousWasRepeatable || seen.Add(item);` —— 第二次出现的 `--add-opens` 已在 `seen` 里 ⇒ 仍被丢掉 ⇒ Forge 照旧 0 秒退出。
    更危险的是那一轮断言读到的**游戏日志是上一轮手工验证留下的旧内容**（时间戳 `00:51:48`），差点把旧 PASS 当新 PASS。
    **两条教训**：去重必须显式保留"可重复选项本身"；断言游戏标记必须**核对日志时间戳/mtime**。
    ④ 边界：补丁**未应用到工作区**（属新的源码改动，需授权）；**NeoForge 未单独实测**；③ 的取舍（不加父版本 jar vs 改用 `forge-<ver>-client.jar`）只验证了前者。

87. **🔴 NeoForge：导入 PASS、启动 FAIL —— 根因是 **Java 版本选错**（新缺陷 ④）（第 87 轮，`run-20261007-4000-neoforge/neoforge-report.md`）**：
    真实包 `optimized-fps` / **NeoForge 21.1.230** / MC **1.21.1**，被测为**打过三处启动补丁的构建**。
    ① **导入 PASS（262 秒）**：实例 `loaderType=NeoForge`、`versions/neoforge-21.1.230` 落盘 ⇒ **B2（安装器 URL）+ B2b（profile 桩）对 NeoForge 同样成立**。
    ② **启动 FAIL**：`launch` 返回 `{"ok":true,"pid":…}` 但 JVM 秒退、**游戏日志都没生成**、**产品侧零输出**。
    ③ **根因定位（用自写的 java 包装器 `%TEMP%\pgwrap` 抓产品真实 61 个参数后逐元素重放）**：
    `Error occurred during initialization of boot layer / java.lang.module.FindException: … / Caused by: InvalidModuleDescriptorException: Unsupported major.minor version 65.0`
    —— **65.0 = Java 21 的 class 版本**，而产品用的是它自己装的 **Java 17** ⇒ **NeoForge 21.1（MC 1.21.1）需要 Java 21**。
    **对照**：下载 Temurin **21.0.12.1**，用**同一套参数、同一实例**运行 ⇒ **游戏跑起来**（**327 行日志 / LWJGL=1 / 声音=1 / 图集=15 / 崩溃=0**，NeoForge 版本检查在跑）。
    ④ **新缺陷 ④（blocker 级）**：**Java 版本选择/自动安装不匹配** —— MC **1.21+** 实例在只有 Java 17 的机器上必然失败且无提示（影响面比单个加载器更广）。
    建议：按 MC 版本解析所需 Java 主版本（1.21+→21、1.20.5–1.20.6→21、1.18–1.20.4→17），不足则自动安装/提示，并在启动前拒绝不匹配运行时。
    ⑤ 顺带：**产品对子进程输出完全不可见**（真实报错只在我用包装器时才能看到）——与既有"API 路径 `_log` 空委托"同源；
    以及**上一轮三处补丁对 NeoForge 是安全的**（参数里 `--add-opens`×2、`--add-exports`×2、占位符 0 残留、类路径无原版 jar；把原版 jar 加回再试仍然失败 ⇒ 不是补丁造成的）。

88. **✅ 缺陷 ④ 根因细化 + 补丁 v3（与 Forge 装配补丁一起可编译）（第 88 轮，`run-20261007-4200-java-version-fix/java-version-fix-report.md`）**：
    ① **根因（代码级）**：启动路径 `LocalApiService.ResolveJavaPath(config, instance, instance.MinecraftVersion)` →
    `JavaLocator.MatchForMinecraftVersion(found, minecraftVersion)`（**不传游戏目录**）→ 内部走字符串版 `JavaVersionMatcher.GetRequiredJavaMajor(string)`；
    而 `instance.MinecraftVersion` 是**加载器版本 id**（`neoforge-21.1.230` / `1.20.1-forge-47.4.3`），解析不出主版本 ⇒ **默认 17**。
    实测探针：`1.21.1→21` ✅，但 `neoforge-21.1.230→17`、`1.20.1-forge-47.4.3→17`、`fabric-loader-…→17`、`quilt-loader-…→17` ❌
    ⇒ **MC 1.21+ 的加载器实例在只有 Java 17 的机器上必然启动失败**。产品**已有**元数据版重载（读版本 JSON 的 `javaVersion.majorVersion` 并可沿 `inheritsFrom` 上溯），**只是启动路径没用它**。
    ② **补丁 v3** `proposed-fix-java-version-selection.diff`（3.0 KB，`git apply --check` ✅）：把**游戏目录**传进匹配与所需主版本解析；**不再静默用不匹配的 Java**；
    先按导入路径同一机制**自动安装**所需主版本；都不行时抛可读错误（`该版本需要 Java 21，但本机只找到 Java 17（…）…`）。
    **两份补丁一起应用并编译：0 警告 0 错误** ✅（在 `%TEMP%` 副本里，工作区未动）。
    ③ **我自己的两个 harness bug**：**(a) 又踩"旧日志陷阱"** —— 第一次验证读到上一轮手工验证留下的 `latest.log`（时间戳 01:33），把 3 项报成 PASS，
    而实际那轮 NeoForge **根本没起来**（java=0、命令行长度 0）⇒ 已修正脚本（启动前删日志 + 只采信 `mtime > 启动时刻` 的日志，否则标记记 0 并警告）；
    **(b) 补丁生成索引错位** —— v2 的两处编辑都用原始文本偏移量 ⇒ 第二处编辑后第一处失效 ⇒ 生成语法坏行（`…Java 路径");vaGameDir);`）⇒ 构建 CS1002/CS1513 ⇒ 改为每次编辑后重新定位锚点。
    ④ 状态：NeoForge **导入 PASS**、**启动仍 FAIL**（v2 那次运行因语法错误未生效；v3 已可编译，**端到端复验待下一轮**）；Forge 1.20.1 回归 **PASS**（命令行 19,999 字符、LWJGL、12 图集、0 崩溃、JVM 存活整个观察期）。

89. **✅ 端到端复验通过（7/7）+ 交付整份复验结论（第 89 轮，`docs/FINAL-VERIFICATION-2026-10-07.md`）**：
    ① 用**打过两份补丁**（Forge 启动装配 + Java 版本选择）的构建跑两个真实实例：
    **NeoForge 21.1（MC 1.21.1）PASS** —— 产品**自己选到 `runtimes/java-21-windows-x64`**、命令行 22,154 字符、`--add-opens`×2、占位符 0 残留、
    **382 行日志 / LWJGL / 15 图集 / 0 崩溃**、JVM 存活整个观察期；**Forge 1.20.1 回归 PASS** —— 产品自己选 **Java 17**（该版本要求）、237 行日志 / 12 图集 / 0 崩溃、JVM 存活。
    逐项 **7/7 True**，判定 **PASS**（本轮 harness 已带"旧日志"防护：启动前删日志 + 只采信 `mtime > 启动时刻` 的日志）。
    ② **整份复验结论**（`docs/FINAL-VERIFICATION-2026-10-07.md`）：源码身份（HEAD `f145d14` / 47 改动 / 149 白名单 / 310 输入）、
    **PASS**（门槛 GREEN/0：Node 305、C# 917、冷启动 19/19；5 处工作区修复有效性；Vanilla/Fabric/Quilt 三种启动；Forge/NeoForge 在打补丁构建上的启动）、
    **FAIL（仅限未打两处补丁的当前工作区）**（①②③ Forge/NeoForge 启动装配、④ Java 版本选错 ⇒ MC 1.21+ 实例不可用）、
    **NOT_RUN**（真实微软账号、AI Key、Gitee token、RID 锁定发布 NU1004、Forge/NeoForge 进世界与长时游玩、MC 1.21+ 的 Fabric/Quilt）、
    **BLOCKED**（提交/推送/tag/Release/Gitee 写入需 D1–D3 授权）。
    ③ **结论**："修好后的源码"本地技术复验 **PASS**；**当前工作区未含两处补丁 ⇒ 按现状仍不可发布**。剩余：授权应用两处补丁 → 重跑门槛 + 记录新 sha256 → 真人/凭据项 → 发布授权。

90. **✅ 限定修复：两处源码阻断 RED → GREEN（第 90 轮，`run-20261007-4400-limited-fix/limited-fix-report.md`）**：
    依据 `harness-blocker-diagnosis-2026-10-07.md` / `-remediation-workflow-2026-10-07.md` / `minimax-harness-blocker-execution-prompt-2026-10-07.md`，
    **本轮授权范围**：`LauncherService.cs`、`LocalApiService.cs`、聚焦测试、`source-snapshot-inputs.txt`（已记录修复前 SHA256 到 `allowed-scope-sha256.json`）。
    ① **先写隔离回归 + 真实 RED**：新增 `LoaderLaunchAssemblyTests.cs`、`JavaSelectionMetadataTests.cs`（已加入白名单**类别 N**）；
    在**未修复源码的隔离副本**里跑出 **失败 5 / 通过 4 / 总计 9**，5 个失败分别对应缺陷 ①②③④（另 4 项是守卫：元数据读 21、17 不匹配 21、未知主版本不视为兼容、Fabric 必须保留版本 jar）。
    ② **最小修复（只改两个授权文件，4 处）**：① `ResolveJvmArguments` 收尾**删除** `DistinctPreserveOrder()`（不对语义 token 任意去重，未采纳 `RepeatableJvmOptions` 白名单方案）；
    ② `BuildReplacements` 补 `${library_directory}`/`${classpath_separator}`；③ 新增 `SuppliesOwnClient(gameDirectory, version)` **按实际 profile**（libraries 里确有 `client-*-extra.jar`/`*-client.jar`）判定，
    自带 client 时不把父版本原版 jar 放进 `-cp`（`BuildClasspath` 的 `clientJar` 改可空）；④ `ResolveJavaPath` 把**游戏目录**传进匹配与所需主版本解析（走 `javaVersion.majorVersion`、可沿 `inheritsFrom` 上溯），
    "任意 Java"必须主版本已知且兼容才使用，否则抛可读错误；**启动路径不做自动下载**（避免门槛测试引入网络依赖，自动安装仍在整合包导入路径，错误信息明确指向设置/导入）。
    ③ **GREEN**：聚焦 **9/9**；**全量 C# 926/926**（917 旧 + 9 新，**无回归**）；工作区构建 **0 警告 0 错误**。
    期间修正了我自己测试的两处断言错误（`JsonUtil.BuildReplacements` 的 key 形如 `${key}`；"跳过原版 jar"的判定在 `BuildCommandAsync` 而非 `BuildClasspath`）。
    ④ **尚未完成（下一轮）**：新 GUID 完整快照门槛 + 记录**新 EXE SHA256**；真实启动验证（Forge1.20.1→17、NeoForge1.21.1→21、Vanilla/Fabric/Quilt 回归、**补 1.21+ Fabric/Quilt 的 Java 选择**）与"进入世界/长时游玩"分别留证；终态 **"具备独立复验条件，等待 Codex 独立验收"**。

91. **✅ 最终门槛 GREEN + 真实产品启动 14/14（第 91 轮，`run-20261007-4800-final-gate-and-launch/`）**：
    ① **最终门槛**（修复后源码，新 GUID 快照）：**模式 GREEN / 失败项 0**；白名单 **151**；文件数量 **312/312**；Node **305**；**C# 926/926**（含 9 项新聚焦测试）；publish；**独立冷启动 19/19**；
    **新单文件 EXE = 53,052,722 B，sha256 `3859e1390ab8b24ec1e0779439f163cb559653f449a566f015b0339ee4e5005d`**（旧哈希作废）；RID NU1004 边界保留。
    ② **真实产品启动（用该 EXE 启动已有实例）：14/14 全 True** ——
    **Forge 1.20.1**：产品选 **java-17** ✅、命令行 19,999、`--add-opens=2`/`--add-exports=2` ✅、占位符 **0** ✅、**371 行新日志 / LWJGL + 声音 + 12 图集 / 0 崩溃 / JVM 存活**；
    **NeoForge 21.1（MC 1.21.1）**：产品选 **java-21** ✅（元数据路径生效）、命令行 22,154、token 2/2、占位符 0、**483 行新日志 / LWJGL + 声音 + 15 图集 / 0 崩溃 / JVM 存活**。
    ③ **保留的一次失败与修正**：第一次"只看 JSON libraries 有无 client jar"的判定导致 Forge 只写 12 行、NeoForge 崩溃 ——
    因为两者的补丁 client **不在** libraries 列表里，而是安装器写到 `libraries/net/minecraft/client/client-*-extra.jar` 与 `net/minecraftforge|neoforged/**/*-client.jar`；
    改成"**profile 声明（-DignoreList 含 client-extra）+ 磁盘上确有替换 client jar**"两条证据后，聚焦 9/9、门槛 GREEN、真实启动 14/14。
    ④ **仍未做**：Vanilla/Fabric/Quilt 回归 + 补 **1.21+ Fabric/Quilt 的 Java 选择**；**进入世界 / 长时游玩**分别留证；终态报告"具备独立复验条件，等待 Codex 独立验收"。

92. **✅ 六用例真实启动全部 PASS + 终态报告（第 92 轮，`docs/FINAL-ACCEPTANCE-2026-10-07.md`）**：
    ① **Vanilla/Fabric/Quilt 回归 + 1.21+ Fabric 的 Java 选择**（同一冷目录、修复后源码的 EXE）：
    **Vanilla 1.18.2** java-17、命令行 5,808、占位符 0、32 行 / LWJGL+声音+**10 图集** / 0 崩溃 / JVM 存活；
    **Fabric 1.18.2**（真实包，导入 576s）java-17、8,645、0、1,335 行 / **10 图集** / 0 崩溃 / 存活；
    **Quilt 1.20.1**（真实包）java-17、14,993、0、710 行 / **12 图集** / 0 崩溃 / 存活；
    **Fabric 1.21.1**（真实包，导入 688s）**java-21** ✅、15,772、0、405 行 / **14 图集** / 0 崩溃 / 存活。
    ② 加上第 91 轮的 Forge 1.20.1（java-17 / 12 图集）与 NeoForge 21.1（java-21 / 15 图集）⇒ **六个真实启动用例全部 PASS**，且 **Java 选择双向正确**（1.18/1.20→17，1.21 的 NeoForge 与 Fabric→21）。
    ③ **终态报告** `docs/FINAL-ACCEPTANCE-2026-10-07.md`：源码身份（HEAD `f145d14`、47 modified、白名单 151、312 输入、两个生产文件修复后 SHA256、**EXE sha256 `3859e139…`**）、
    RED→GREEN（聚焦 5→9；全量 917→**926**）、门槛 GREEN/0、六用例表、**保留的一次真实失败与修正**（第一版 client 判定只看 JSON libraries ⇒ Forge 12 行、NeoForge 崩溃；改为"声明 + 磁盘确有替换 jar"后全过）、
    边界（**进入世界/长时游玩 NOT_RUN**、1.21+ Quilt NOT_RUN、RID NU1004、账号/AI Key/Gitee/发布授权分别 BLOCKED/NOT_RUN、既有范围未逐行审计保留声明）。
    ④ **终态：具备独立复验条件，等待 Codex 独立验收**（不构成发布批准）。
    ⑤ 小记：本轮回归脚本在最后收尾（写 JSON）阶段卡住，**结论从 step 日志逐条提取**（四个用例的命令行长度/java/占位符/标记齐全），已停掉卡住的脚本与游戏进程；未伪造 JSON。

93. **✅ 独立验收 P2 返工：客户端识别边界 RED → GREEN，六类真实启动全过（第 93 轮，`docs/FINAL-ACCEPTANCE-REWORK-2026-10-07.md`）**：
    ① **RED（先构造反例，真实 `BuildCommandAsync`，隔离副本）**：4 项失败 / 6 项通过（exit 1）——
    无关 `unrelated-tool-extra.jar`、旧版 `client-1.20.1-…-extra.jar`、只有资源（0 `.class`）的 extra jar 都会把原版 jar 移出 `-cp`；
    声明的 `*-client.jar` 无类文件时静默生成错误 classpath（应明确报错）。
    ② **最小修复（只改 `LauncherService.cs`）**：`SuppliesOwnClient`/`IsClientJarName` → `ResolveClientSupply`：声明（`-DignoreList` 含 `client-extra`）+ **绑定当前版本/加载器的产物坐标**
    （`<mc>-forge-<ver>` → `net/minecraftforge/forge/<mc>-<ver>/forge-<mc>-<ver>-client.jar`；`neoforge-<ver>` → `net/neoforged/neoforge/<ver>/neoforge-<ver>-client.jar`）+ **产物必须确实含 `.class`**；
    不再递归扫描任意目录、不再按 `*-extra.jar` 后缀肯定。契约：Provided⇒移出原版 jar；Missing⇒**合法回退保留**并写日志；Unusable（存在但无类）⇒**装配前明确拒绝**；不静默、不自动下载、不改用户配置。
    实测真实产物：`forge-1.20.1-47.4.3-client.jar` 1438/1438 个 `.class`、`neoforge-21.1.230-client.jar` 1601/1601 个 `.class`；而 `client-*-extra.jar` 为 13517/15250 条目、**0 个 `.class`**（与独立验收证据一致）。
    ③ **GREEN**：聚焦 **14/14**（0 失败 0 跳过）、全量 C# **931/931**、构建 0 警告 0 错误；统一门槛（新 GUID 快照）**GREEN/失败 0**：白名单 151、输入 **312/312**、Node **305**、C# **931**、**单文件 EXE 冷启动 19/19**；
    **新 EXE sha256 `342e140f4a0544e222a0c68f879e73ff0fa71f30967ca5990e1ea372d60302b1`**；RID locked restore NU1004 如实保留。
    ④ **六类真实启动全过**（同 EXE、自有离线副本、40 秒观察）：Forge 1.20.1→**java17**/193 行/12 图集；NeoForge 21.1→**java21**/327 行/15 图集；Vanilla 1.18.2 ×2→**java17**/32、31 行/10 图集；
    Fabric 1.21.1→**java21**/374 行/14 图集；Quilt 1.20.1→**java17**/634 行/12 图集；全部占位符 0、崩溃 0、观察期存活；脚本显式 **exit 0**。
    ⑤ **边界**：只验证"进入渲染初始化 + 观察期存活"，**未**验进入世界/长时游玩/视觉验收、全新在线安装、Quilt 1.21+；账号/AI Key/Gitee/发布授权仍 BLOCKED/NOT_RUN。
    ⑥ **终态：具备独立复验条件，等待 Codex 独立验收**（不构成发布批准）。

94. **✅ 用独立验收自己的探针复跑：P2 阻断由 `failures: 2` 变为 `failures: 0`（第 94 轮）**：
    把独立验收证据里的 `BoundaryProbe.cs`（**未改动**）编译并链接到**返工后**的产品 DLL，用同样两个反例复跑：
    `failures: 0`，两个反例 `retainedBefore=true / retainedAfter=true / pass=true`，且 `beforeClasspath` 与 `afterClasspath` **完全相同**，
    **探针 exitCode = 0**（独立验收当时为 `failures: 2`、exit 1）⇒ 无关/旧版文件不再改变原版客户端 jar 的保留状态。
    证据：`run-20261007-5200-client-boundary-rework/boundary-recheck-results.json`；报告已追加到 `docs/FINAL-ACCEPTANCE-REWORK-2026-10-07.md` §9。

95. **⚠️ 额外尝试：Quilt 1.21+ 的 Java 选择**未取得证据**（环境 TLS 失败，非产品缺陷）（第 95 轮，`run-20261007-5400-quilt-121/`）**：
    用返工后的 EXE 导入真实 **Quilt 1.21.1** 包（Modrinth `thunder` 1.2.3+1.21.1）：`/api/instances/import` 返回 **HTTP 500**（255 s）；
    **受控复跑抓响应体**得到**可读 JSON**：`{"error":"The SSL connection could not be established, see inner exception."}`（156 s）——
    即**本机网络当时无法建立 TLS**（与更早 Forge 安装器那次 `SSLHandshakeException` 同类，环境/瞬时故障），**不是产品在 Quilt 1.21.1 上的缺陷**；
    失败后**未留半成品实例目录**（清理正常），产品日志无新增行（API 路径日志空委托，既有诊断性缺口）。
    ⇒ **"Quilt 1.21+ 的 Java 选择"仍为 NOT_RUN**，需网络可用时重试；**不写成通过、也不写成产品缺陷**。
    更正我自己的第一印象：第一次只看到 "500" 时曾怀疑是未处理异常，抓到响应体后确认**产品返回的是结构化错误**。

96. **⚠️ Quilt 1.21+ 重试仍失败（同一 500；环境 TLS 阻断）（第 96 轮，`run-20261007-5400-quilt-121/`）**：
    网络预检通过（Modrinth API 200、Quilt maven 200）后再试：**HTTP 500，用时 669 s**（第一次 255 s、受控复跑 156 s）。
    三次都在**导入阶段**失败；两次已确证的响应体都是**本机 TLS 建连失败**的结构化错误（`{"error":"The SSL connection could not be established, see inner exception."}`）。
    **本次重试的响应体未捕获**（用的是分阶段脚本），因此**不断言**与受控复跑同因，只记录"同一 500、耗时更长"。
    ⇒ **"Quilt 1.21+ 的 Java 选择"继续 NOT_RUN**；不写成通过、也不写成产品缺陷。报告已追加到 `quilt121-report.md`。