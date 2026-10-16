# 发布验证重跑与阻断项闭环（2026-10-16）

用户决定：「提交已审查修复，并按修正后的流程重跑发布验证」。本目录是这次重跑的交付记录。
日期：2026-10-16（Asia/Hong_Kong）。**本轮未推送、未打标签、未上传 Release。**

## 1. 结论：RP-01 是本机「路径相关」的运行环境差异，不是产品缺陷（机制未证实）

> **2026-10-16 更正（独立验收 HA-02）。** 本节初版把「本机运行着 `CodexSandboxService` /
> 已加载 `bindflt` / `D:\松光启动器` 是沙箱工作区根」直接写成**根因**，并进一步断言
> 「不是用户会遇到的发行缺陷」。这两句都**超出证据范围**，已改写为下面的限定表述。
> 本机没有做任何停用服务、改安全设置或卸载绑定过滤驱动的操作。

上一轮把「发行候选冷启动时 `HttpListener` 报错误 6（句柄无效）」记为 P1 发布阻断。
本轮用最小探针程序把「它是产品缺陷」这个结论证伪了——**同等强度的证据只支持「本机路径相关」**：

| 实验 | 结果 |
| --- | --- |
| 同样的最小 `HttpListener` 探针用 `dotnet run`（共享运行时）启动 | 三个前缀全部 `OK` |
| 探针自包含发布（含/不含单文件、含/不含压缩，三种组合） | 全部 `FAIL 错误 6` |（见下方注） |
| **同一个 EXE 放在 `D:\pgmatrix-droot` 或桌面** | **`OK`** |
| **同一个 EXE 放在 `D:\松光启动器\...` 任意子目录** | **`FAIL 错误 6`** |
| 同一 EXE 改 `-WorkingDirectory`（exe 在工作区内、cwd 在工作区外） | 仍 `FAIL` → 取决于 **exe 自身路径** |
| exe 在工作区外、cwd 在工作区内 | `OK` → 工作目录无关 |
| 从 `D:\pgjunction`（junction 指向工作区内）启动 | `FAIL` → 跟**解析后**的路径走，不是路径拼写 |
| 中文路径（`D:\松光启动器测试`、`C:\...\Temp\松光启动器测试`） | `OK` → **不是**非 ASCII 路径问题 |
| 把失败目录的 ACL 复制到正常目录（含 `Everyone → Deny DeleteSubdirectoriesAndFiles`） | 仍 `OK` → **不是** ACL 问题 |
| 路径长度 12 → 106 字符 | 全部 `OK` → **不是**路径长度问题 |
| 真实候选 EXE 在桌面上启动 | `/api/health` **200**，正常监听 |
| 真实候选 EXE 在 `D:\松光启动器` 内启动 | `/api/health` 不可达，`LocalApiService.cs:193` 抛错 |

> 注：自包含发布那一行是分类对照（当时的探针在两种发布形态下都失败），
> 它说明失败与「单文件/压缩」开关无关，**不是**一条独立的机制证据。

**可确认的：** 失败与 **exe 自身的绝对路径是否位于 `D:\松光启动器` 树内**强相关，且稳定可复现
（交替运行、换端口、换目录名、换发布形态都不改变结论）；同一个二进制放在该树之外即正常。

**未确认的：** 具体是什么机制造成这个路径相关性。本机同时存在 `CodexSandboxService`
（`OpenAI.Codex`）与已加载的 `bindflt` 绑定过滤驱动，且 `D:\松光启动器` 是工作区根——
时间与空间上吻合，但**没有做因果实验**（未逐个停用/排除），因此
「是它导致的」与「只影响本机」都**不能作为结论**。

**含义（限定版）：**

1. 在**本机**，该失败只出现在工作区目录内；放在中性目录（桌面、驱动器根、临时目录）的同一个
   二进制正常。**没有证据表明任意用户机器/安装路径都不受影响**——受影响范围未查明。
2. 上一轮的冷启动验证把 EXE 放在 `docs/release-preparation-2026-10-16/cold-start/...`，
   即放在工作区内，因此失败：那次 FAIL **不能**作为候选质量结论。
3. 因此「先解决发行包 API 启动根因，再重新生成候选」这一后续流程不再成立；
   改为**在工作区之外的中性目录验收**，并在验收记录里保留上述未确认边界。

## 2. 本轮实际改动

| 项 | 文件 | 说明 |
| --- | --- | --- |
| FA-01–FA-05 修复提交 | 56 个文件 | 见下方提交 `d138c1a`（本地提交，未推送） |
| RP-02 | `scripts/verify-single-file-standalone.ps1` | `$served` 原本只在「健康就绪」分支内初始化（原 232 行），报告却无条件引用（306 行）；StrictMode 下健康失败时抛次生异常，**最需要证据的 FAIL 场景反而写不出 JSON**。现在把它提到分区外初始化。 |
| RP-03 | `release-notes/v1.0.1.md` | 补入 FA-01–FA-05 的全部修复说明（原草稿 7 条已并入，草稿顶部的「请勿发布」注记未带入）。 |

### 提交

- `d138c1a` — `fix(ui): 修复实例选择、设置回显与列表错误提示`（作者沿用项目身份 `松光启动器 <launcher@pineglow.local>`）
- 基线 HEAD 为 `1356efa`；提交前已确认暂存范围只含已审查的修复、测试、清单登记与证据文档，未 `stage-all`。

### 2.1 RP-02 证据更正（原归档证据与声称不符）

> **2026-10-16 更正（独立验收 HA-02）。** 提交 `65fe285` 的说明与本文初版都声称
> 「用 HEAD 版本与修复版本做 A/B、修复版本正常写出 verdict=FAIL 报告」，
> 但当时归档进来的文件是 **`rp02-failpath-report.json`（健康成功、`verdict=PASS`、`servedAssets` 非空）**，
> 它**不能**证明健康失败这条分支被执行过 —— 那次运行里真实 API 其实是启动成功的
> （工作区外的 `%TEMP%` 冷启动目录）。

实际发生的是：先跑了一次（工作区外，健康成功 → PASS），
随后才用「`-WorkRoot` 指向工作区内」构造出真实的健康失败并得到 FAIL 报告，
但当时只把**第一份**证据复制进了本目录，第二份留在了 `.testagent/` 并被当作临时文件清理。
这是证据归档错误，不是实现问题。

现在归档的是**真实健康失败**的报告：`rp02-health-failure-report.json`（原样摘录）：

- 生成方式：中性目录 + 官方脚本 + 当前候选 EXE，仅把 `-WorkRoot` 指向工作区内 → API 真实失败。
- `verdict = FAIL`、`health = 空`、`servedAssets = 0 项`、`failures = 1`（`/api/health 就绪 :: health=`）。
- `stderr` 干净：**没有** `The variable '$served' cannot be retrieved` 这类次生异常
  （对照：未修复的 HEAD 版本在同一场景下抛该异常、JSON 不落盘）。
- 独立验收（`../harness-independent-acceptance-2026-10-16-run01/`）也在工作区内独立复现出 FAIL 分支，
  并据此认可 RP-02 的实现；本目录的旧 `rp02-failpath-*` 文件已移除，避免继续误引。

## 3. 验证结果

中性验证根目录：`%TEMP%\pg-verify-20261016\`（在 `D:\松光启动器` 之外）。

| 门槛 | 结果 | 证据 |
| --- | --- | --- |
| 候选发布（`/p:Version=1.0.1`，自包含单文件 win-x64） | 成功，程序集版本 `1.0.1`，内嵌 17 项 UI 资源 | `publish.log` |
| 候选 SHA256 / 大小 | `72eae380db265a97c16938fd6086441e0409fd192f6b5cce1c01c65a33a32534` / 53,089,586 B | 同上 |
| **独立冷启动（全新中文路径、仅 EXE + portable.flag）** | **PASS — 18/18 检查，0 失败**；10 项界面资源全部按正确 MIME 送达，解包 UI 与候选源码逐字节一致 | `standalone-cold-start.json`、`cold-start.log` |
| 正式清单 Node 测试 | **379/379 通过，0 失败 0 跳过**（28 个测试文件） | 提交前的 `run-inventory-tests.cjs` 运行 |
| 测试清单双向一致 | 通过（Node 28 + C# 1，磁盘一致） | `test-inventory.json`、`inv-check.log` |
| UI 资源闭包 | 12 个文件、14 条本地引用、0 缺失 | `ui-resource-closure.json`、`closure.log` |
| 全量 C# 测试（957 项） | **957/957 通过，0 失败 0 跳过** | `csharp-tests-outside2.log` |
| 真实 WebView2 界面（正式验证器，修复后） | **PASS — 10/10 视图，exit 0**；未捕获异常 0、console.error 0、本机资源失败 0。修复前的 FAIL 报告按原样保留 | `ui-live-fixed.json`、`ui-live-fixed.log` |
| HA-01 行为验证（真实浏览器布局） | **PASS** — 16 个节点分类全部符合预期，含可见破图仍报错、可见未完成不提前通过 | `ha01-behavior-report.json`、`ha01-pass.log`、`ha01-exit-code-check.txt` |
| 更新清单与最终附件一致 | 7/7 断言通过（version/notes/url/sha256/size 全部与实际附件相符） | `manifest.json` |
| **真实更新链路（阶段 A/B/C，真实外网）** | **PASS** — 见下方第 3.1 节 | `live-update-abc-final2.json`、`live-update-abc-final2.log` |

### 3.2 九页验证器的真实总判定是 FAIL（不要只摘取「9/9」）

> **2026-10-16 更正（独立验收 HA-02）。** 本表格初版把这一行写成
> 「9/9 视图渲染，未捕获异常 0…」，属于**只摘取通过项**：归档的 `ui-live-report.json`
> 里 `verdict` 字段实际是 **FAIL**，`problems` 非空。现按原样更正。

归档报告里的 4 项 problems（原样）：

- `视图 content: 图片未在预算内渲染（img 0/24，待填槽位 0，等 20444ms）`
- `视图 skins: 图片未在预算内渲染（img 0/1，待填槽位 0，等 20431ms）`
- 另两项来自第一次（含空格路径）运行的同类统计。

独立验收的进一步结论：被计入的图片节点本就不该算作「可见图片加载失败」——
内容库那 24 张图由 `ui/css/redesign.css` 的 `.mod-icon { display:none; }` 隐藏、布局宽高为 0；
skins 页唯一被计入的 `#localSkinTexture` 是 `hidden`、无 `src`、宽高 0 的本地文件预览占位。
也就是说这是**验证器统计口径缺陷（HA-01）**，不是界面真的没渲染出来。
同时正式 `VIEWS` 只有 9 项，漏了实际存在的 `playtime` 页（`ui/index.html:61`）。

**HA-01 已修复（本轮后续）：**

1. `scripts/verify-ui-pages-live.cjs` 新增可见性判定 `IMG_VISIBLE_FN`：只统计
   仍在文档内、**有来源**（`src`/`srcset`）、非 `hidden`/`aria-hidden`、
   自身及全部祖先既非 `display:none` 也非 `visibility:hidden`、且布局盒非零的
   `HTMLImageElement`。无来源的占位、隐藏节点、SVG `<image>` 一律不参与统计。
2. `pending` 的定义同时修正：原为 `loaded < total`，会把「永久 0/24」一直等到超时；
   现在只要所有**被统计**的图片都出了结果（成功或明确失败）、且没有待填槽位就结束等待。
   新增 `broken` 判定（`complete && naturalWidth===0`，并用 `getAttribute('src')` 兜底
   `currentSrc`——实测相对地址 404 时 `currentSrc` 为空串），**可见破图仍然计入并 FAIL**。
3. `VIEWS` 补入 `playtime`（`data-nav="playtime"` 确实存在），正式验证器改为十页。
4. 懒加载：被统计节点里若有滚出视口者，先滚动覆盖可视区域再复测一次，
   避免把「还没进视口」当成「没渲染」；滚动探测失败会记入 problems 而不是静默通过。

**GREEN 证据（正式脚本、真实 WebView2、当前候选 EXE）：**

- `ui-live-fixed.json` / `ui-live-fixed.log`：`UI-PAGES RESULT=PASS views=10/10`，
  退出码 0，`exceptions=0 consoleErrors=0 uiAssetFailures=0 productLocalFailures=0`，
  `playtime` 页 `OK state=content textLen=325`。
- 修复前同一命令（九页）为 `FAIL`、4 项 problems —— 该原始 FAIL 报告 `ui-live-report.json` 按原样保留，未篡改。

**行为验证（RED→GREEN 的最小行为覆盖，真实浏览器布局，非 DOM 桩）：**

`scripts/ha01-image-visibility-behavior.py` 直接从正式脚本源码抽出 `IMG_VISIBLE_FN`
（不是复制一份），在真实 Edge（Playwright `channel=msedge`）里对 16 个节点求值：

| 覆盖项 | 结果 |
| --- | --- |
| A 不应误报：无 `src`、`hidden`、`aria-hidden`、隐藏祖先（`display:none` / `visibility:hidden`）、真实 `.mod-icon`、显式 0 尺寸、SVG `<image>`、已移出文档（`isConnected=false`） | 9/9 全部 `visible=false` |
| B 可见破图仍必须报错 | `broken=2`（可见失败图片被计入） |
| C 可见未完成不能提前算作完成 | `loaded=5 + broken=2 < total=8`，仍计 pending |
| D 视口外节点保留统计并触发滚动分支 | `offscreen=4`，`below-viewport`/`scrolled-out` 均在统计内 |
| 区分力对照 | 修复前口径 `raw img=15 loaded=11`，修复后 `计数=8`：确认用例能区分两种口径 |

判定 PASS，退出码 0；浏览器不可用时以 **exit 3 明确跳过**（`ha01-skip.log`），
正式脚本路径缺失时 exit 2，见 `ha01-exit-code-check.txt`。

**语义边界（实测记录，避免后人误判）：** 父容器 0×0 **不会**自动让 `<img>` 被排除——
若 `img` 自带 `width`/`height` 属性，其自身布局盒仍是 20×20，按可见统计是正确行为；
只有显式 `width:0;height:0`（或 `display:none`）才会被排除。测试里 `zero-box` /
`zero-box-sized` 两个节点就是用来钉住这条边界的。

### 3.1 真实更新链路验收（本轮新增）

`scripts/verify-update-live-download.ps1` 带 `-AssetUrl`（GitHub 上真实的 v1.0.0 附件）对**本轮候选**跑完三个阶段：

| 阶段 | 结果 | 说明 |
| --- | --- | --- |
| A 真实默认源 | OK | 主源 Gitee 固定 `update.json` 返回 **HTTP 404**（该文件尚未发布），按设计降级到 GitHub 备用源并取到清单 v1.0.0；`currentVersion=1.0.1` → `updateAvailable=false`（不产生假更新提示） |
| B 真实 HTTPS 下载 + 校验 | OK | 夹具清单 1.0.2、真实资产地址为 GitHub HTTPS：**43,989,943 字节与 SHA256 `6130db19…` 与真实资产完全一致**，无 `.part` 残留 |
| C 篡改必须被拒 | OK | 只把清单里的 `sha256` 改成全 0：下载以精确原因 `更新包下载失败：校验失败：<url>` 失败，且未留下任何可用文件或 `.part` 残留 |

顺带确认一条**发布前必须知道的事实**：Gitee 的固定源 `https://gitee.com/ztz1359/pineglow-launcher/raw/master/update.json`
目前是 **404**。也就是说主源现在还没有任何清单；release 工作流里的 Gitee 镜像步骤需要 `GITEE_TOKEN`
才会创建它。在它存在之前，客户端的检查更新只能靠 GitHub 备用源（本轮实测该降级路径可用）。

### 必须记录的环境事实：工作区内跑不出绿色

同一份源码、同一台机器：

- 在 `D:\松光启动器` 内跑全量 C# 测试：**失败 492 / 通过 465 / 共 957**。
- 在 `D:\pg-testrepo`（同机、工作区外、源码副本）跑同一命令：**失败 13 / 通过 944**；
  补上我漏拷的 `.github/` 后：**失败 0 / 通过 957**。

工作区内的 492 项失败由沙箱环境造成（12 项 `HttpListenerException: 句柄无效` +
`ContentIconServiceTests` 在 Temp 建目录被拒 `UnauthorizedAccessException`），
**不是产品缺陷**；工作区外的 13 项是我拷贝时漏了 `.github/workflows`（`WorkflowInvariantTests`
需要读工作流文件），补齐即全绿。原始日志两份都保留，便于复核这个差异。

因此后续任何「全量测试 / 冷启动 / 浏览器」门槛都应**在工作区之外的副本或中性目录执行**，
否则会重复产生上一轮那样的假阻断。

## 4. 尚未完成 / 未验证

- 未推送、未打标签、未上传 Release；Gitee 镜像未执行（缺 `GITEE_TOKEN`，且主源 `update.json` 仍为 404）。
- 未做真实账户登录、真实游戏启动。
- 未做「真实升级安装」——本轮只验到下载与校验，**刻意不调用** `/api/update/apply`（那会替换被测产物）。
- 原生 WebView2 之外的真实鼠标键盘交互未跑。

## 5. 本轮修掉的两个验收脚本缺陷（重要）

### 5.1 `verify-update-live-download.ps1`：夹具版本硬编码，验收同版本候选时自相矛盾

阶段 B/C 的夹具清单把「新版本」**硬编码为 `1.0.1`**，而断言的语义前提是「夹具必须比候选新」。
于是验收 1.0.1 候选时 `1.0.1 > 1.0.1` 不成立 → 阶段 B 第一条断言必然失败。
（脚本写在候选为 1.0.0 的时期，那时常量确实比候选新。）

- 改为**新增 `-FixtureVersion` 参数**，默认从产物自身读取版本再进一位 patch。
- 必须用 `FileVersionInfo`：单文件发布产物**没有可读的程序集清单**，
  `AssemblyName::GetAssemblyName` 会抛「该模块应包含一个程序集清单」。
  实测单文件 EXE 的 `FileVersion=1.0.1.0`（`ProductVersion=1.0.1+<commit>`），故默认算出 **1.0.2**。
- 断言语义同步修正为「latest == 夹具版本」且「夹具版本确实高于 currentVersion」（真做数值序比较）。
- CI 不受影响（`release.yml` 的 Live update-chain check 不传 `-AssetUrl` → 脚本跳过阶段 B/C），
  但本地深度验收这条路径此前必然报 FAIL。

### 5.2 同脚本：阶段 A 的「逐源失败」分支会抛异常，阶段 B/C 被整体跳过

阶段 A 明确把「逐源失败」当作**允许的通过形态**（`$perSourceFailure`），但紧随其后的断言
直接拼 `"$($checkA.manifestSource)"`，而该属性在逐源失败分支**不存在**；
`Set-StrictMode -Version 2.0` 下抛 `PropertyNotFound`，脚本在断言中途终止 ——
**阶段 B/C 根本没跑**，最终只留下一句属性缺失的报错。本轮实测触发条件：
GitHub 瞬时 DNS 解析失败（`不知道这样的主机。 (github.com:443)`）。

- 现在按分支取证据：取到清单用 `manifestSource/usedFallback`，逐源失败时显示「(逐源失败，无清单)」，
  两者都不再解引用不存在的属性；`giteeNote` 分支同样改用已守卫的变量。
- 用三种响应形态（备用源成功 / 逐源失败 / 用户自定义源）在 StrictMode 下做了合成回归：
  修复前「逐源失败」这一支抛异常，修复后三支全部通过。

## 6. 编码坑：含中文的 `.ps1` 必须带 UTF-8 BOM

`scripts/get-release-notes.ps1` 含中文却以**无 BOM** 保存：PowerShell 7（CI 的 `shell: pwsh`）能正确按
UTF-8 读取，但 Windows PowerShell 5.1 按 ANSI 解码 → **3 处语法错误、脚本完全不可用**，
而 release 工作流正是用它生成 `manifest.notes`。

本轮一并处理：

- `scripts/get-release-notes.ps1` 补 BOM（现在 PS 5.1 下可解析，实测生成 18 行说明）。
- `scripts/verify-update-live-download.ps1` 本轮编辑时补 BOM（否则 PS 5.1 下同样解析失败）。
- 全量扫描 `scripts/**/*.ps1`：含非 ASCII 且缺 BOM 的只有上述两个；
  `scripts/start-new-ui.ps1` 虽无 BOM 但 PS 5.1 解析通过，未改动。
- `release-notes/v1.0.1.md` 保持**无 BOM** 是正确的：它是被 `ReadAllText(..., UTF8)` 读取的数据文件。

**证据边界（据独立验收的核对补充）：** 我的复现是在**允许脚本执行的策略**下取得的——
用 `-ExecutionPolicy Bypass` 调用 `powershell.exe`，并用
`[Parser]::ParseFile` 直接对文件做语法解析（不执行脚本）。独立验收方在同一台机器上
**没有**取得同样的复现，因为本机要求脚本签名，其探针被拒绝执行；按其流程约定，
它没有绕过签名要求。因此这一条应记为「**在允许执行/可解析的环境下已复现**」，
而不是「任何环境下都必然失败」；它**也不是**当前 `pwsh` 发布流程的已证明阻断
（CI 用 PowerShell 7，能正确按 UTF-8 读取无 BOM 文件）。补 BOM 属于低风险的一致性修复。

## 7. 更正：`--app` 含空格路径**不是**脚本缺陷

本节此前记录「`verify-ui-pages-live.cjs --app` 遇到含空格路径会失败」。**该结论是错的，已撤销。**

复核过程：同一个含空格路径分三种方式喂给 `node`，并打印 `process.argv`：

- `Start-Process -ArgumentList` 用**数组**传参 → `argv` 里路径完整。
- 直接用 `&` 调用 → `argv` 里路径完整。
- 直接调用该脚本 `--app "<含空格路径>" --help` → 正常解析（打印用法，未报未知参数）。

真实原因是我自己的调用方式：最初把整条命令拼成**一个字符串**交给
`Start-Process -ArgumentList`，PowerShell 会按空格把它拆成多个参数，
于是路径被截断、`冷启动` 变成「未知参数」。脚本本身没有这个问题，
`release.yml` 的 `dist/` 调用方式也不受影响。

独立验收方用 Python `subprocess` 参数数组直接传入含「中文 冷启动」的路径，
正式 Node 验证器正常跑完九页，**未复现**该现象——与上述结论一致。

教训（写进证据：避免下次又误判）：向 `Start-Process -ArgumentList` 传参必须用**数组**；
或使用 `&` 调用。相关地，本轮另一次「未知参数: 冷启动」来自一个未加 `&` 调用运算符的脚本名，
同属调用方式问题，与产品脚本无关。

## 7.5 独立验收与本次更正台账

本轮工作期间，同一工作区出现了独立验收记录
`../harness-independent-acceptance-2026-10-16-run01/`（受验 HEAD `3ab6b0c`）。
它对我这份报告的核对结论是 `ReleaseEvidence=NEEDS_VERIFIER_AND_REPORT_CORRECTION`，
技术面为 `PASS_SCOPED_RP02_RP03_COLDSTART_API_REGRESSION`。它指出并已在本文件更正的问题：

| 编号 | 原表述（错） | 更正 |
| --- | --- | --- |
| HA-02a | 「9/9 视图渲染」当作通过 | 归档报告 `verdict=FAIL`、4 项 problems；已改为按原样记录（第 3.2 节） |
| HA-02b | RP-02 的归档 failpath 证据 | 该文件实际是健康成功 PASS；已替换为真实健康失败的 FAIL 报告（第 2.1 节） |
| HA-02c | 把沙箱服务/`bindflt` 写成根因，并断言「不是用户会遇到的缺陷」 | 降级为「本机路径相关环境差异，机制未证实，影响范围不明」（第 1 节） |
| HA-01 | 未提及 | 正式验证器存在统计口径缺陷（误计隐藏/无 src 图片）且漏 `playtime` 页 —— **已修复**，见第 3.2 节 |
| — | 「`--app` 含空格路径是脚本缺陷」 | 已撤销，属我的调用方式（第 7 节） |
| — | PS 5.1 BOM 兼容性表述过宽 | 限定为「在允许执行/可解析的环境下已复现」，非 `pwsh` 发布阻断（第 6 节） |

独立验收**独立确认**了：清单 Node 379/379、全量 C# 957/957（含 TRX counters 核对）、
资源闭包、同 SHA 旧候选与当前候选在中性目录均 18/18、冻结副本重新发布 + 冷启动 18/18、
阶段 A 真实更新源链、版本介绍与 manifest 一致。

**本轮之后仍未闭环：** HA-01 已按后续流程第 2–4 项修复并通过行为验证（第 3.2 节）；
其余未验证边界仍见第 4 节（真实账户/游戏、真实升级安装、Gitee 镜像）。
发布批准与技术检查分开，仍需人工决定。

## 8. 复现命令
```powershell
# 1) 候选发布（输出到工作区外的中性目录）
dotnet publish dev/PineglowLauncher/Pineglow.Launcher.csproj -c Release -r win-x64 --self-contained `
  /p:PublishSingleFile=true /p:EnableCompressionInSingleFile=true `
  /p:DebugType=None /p:DebugSymbols=false /p:Version=1.0.1 /p:PublishDir=<工作区外>\publish\

# 2) 独立冷启动（脚本可以留在仓库里，但 EXE 必须在工作区外）
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/verify-single-file-standalone.ps1 `
  -ExePath "<工作区外>\cold2-中文冷启动\Pineglow-Launcher-win-x64-single-file.exe" `
  -ExpectedUiRoot "D:\松光启动器\ui" -JsonPath "<工作区外>\standalone-cold-start.json" `
  -KeepWorkDir -SkipWindowTitleCheck

# 3) 全量 C#（必须在工作区外执行，否则 492 项环境失败）
dotnet test dev/PineglowLauncher.Tests/Pineglow.Launcher.Tests.csproj -c Release

# 4) 真实 WebView2 九页（路径含空格也可以；用 & 调用或数组传参，别把整条命令拼成一个字符串）
& node scripts/verify-ui-pages-live.cjs --app "<工作区外>\cold2-中文冷启动\Pineglow-Launcher-win-x64-single-file.exe" `
  --out <截图目录> --json <报告> --keep

# 5) 真实更新链路 A/B/C（真实外网；-AssetUrl 传一个真实 HTTPS 附件才会跑 B/C）
& scripts/verify-update-live-download.ps1 -ExePath "<工作区外>\cold2-中文冷启动\Pineglow-Launcher-win-x64-single-file.exe" `
  -AssetUrl "<GitHub Release 上的真实附件地址>" -JsonPath "<报告>" -KeepWorkDir
```
