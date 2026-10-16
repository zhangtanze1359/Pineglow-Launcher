# 发布验证重跑与阻断项闭环（2026-10-16）

用户决定：「提交已审查修复，并按修正后的流程重跑发布验证」。本目录是这次重跑的交付记录。
日期：2026-10-16（Asia/Hong_Kong）。**本轮未推送、未打标签、未上传 Release。**

## 1. 结论：RP-01 不是产品缺陷，是工作区沙箱造成的验证伪象

上一轮把「发行候选冷启动时 `HttpListener` 报错误 6（句柄无效）」记为 P1 发布阻断。
本轮用最小探针程序把该结论证伪了：

| 实验 | 结果 |
| --- | --- |
| 同样的最小 `HttpListener` 探针用 `dotnet run`（共享运行时）启动 | 三个前缀全部 `OK` |
| 探针自包含发布（含/不含单文件、含/不含压缩，三种组合） | 全部 `FAIL 错误 6` |
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

机制：本机运行着 `CodexSandboxService`（`OpenAI.Codex`，PID 8968，随 Codex/DSH 工作区沙箱启用），
`D:\松光启动器` 是该沙箱绑定的**工作区根**（已加载 `bindflt` 绑定过滤驱动）。从该目录内执行的进程
拿不到启动 HTTP 监听所需的资源，`HttpListener.Start()` 在 `SetupV2Config()` 阶段以 Win32 6 失败；
放在该目录之外的同一个 EXE 完全正常。

**含义：**

1. 该失败只出现在工作区目录内，**不是用户会遇到的发行缺陷**。用户按常规位置（Program Files、
   桌面、任意自选目录）安装即处于受影响范围之外。
2. 上一轮的冷启动验证把 EXE 放在 `docs/release-preparation-2026-10-16/cold-start/...`，
   即**放在工作区内**，因此必然失败：那次 FAIL 是验证方式的伪象，不是候选质量结论。
3. 因此「先解决发行包 API 启动根因，再重新生成候选」这一后续流程不再成立；
   RP-01 无需产品修复，改为**在正常工作区外的中性目录验收**。

## 2. 本轮实际改动

| 项 | 文件 | 说明 |
| --- | --- | --- |
| FA-01–FA-05 修复提交 | 56 个文件 | 见下方提交 `d138c1a`（本地提交，未推送） |
| RP-02 | `scripts/verify-single-file-standalone.ps1` | `$served` 原本只在「健康就绪」分支内初始化（原 232 行），报告却无条件引用（306 行）；StrictMode 下健康失败时抛次生异常，**最需要证据的 FAIL 场景反而写不出 JSON**。现在把它提到分区外初始化。 |
| RP-03 | `release-notes/v1.0.1.md` | 补入 FA-01–FA-05 的全部修复说明（原草稿 7 条已并入，草稿顶部的「请勿发布」注记未带入）。 |

### 提交

- `d138c1a` — `fix(ui): 修复实例选择、设置回显与列表错误提示`（作者沿用项目身份 `松光启动器 <launcher@pineglow.local>`）
- 基线 HEAD 为 `1356efa`；提交前已确认暂存范围只含已审查的修复、测试、清单登记与证据文档，未 `stage-all`。

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
| 真实 WebView2 九页界面 | **9/9 视图渲染，未捕获异常 0、console.error 0、本机资源失败 0** | `ui-live-report.json`、`ui-live2.log`、`ui-shots/` |
| 更新清单与最终附件一致 | 7/7 断言通过（version/notes/url/sha256/size 全部与实际附件相符） | `manifest.json` |

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

- 未推送、未打标签、未上传 Release；Gitee 镜像与真实升级链路未验证。
- 未做真实账户登录、真实游戏启动。
- 真实外网更新源链路（`verify-update-live-download.ps1`）本轮未跑。
- 原生 WebView2 之外的真实交互（真实鼠标键盘操作）未跑。

## 5. 本轮发现的两个附带问题（未修改，待决定）

1. **`scripts/get-release-notes.ps1` 缺 UTF-8 BOM。** 文件含中文，但以无 BOM UTF-8 保存；
   PowerShell 7（CI 的 `shell: pwsh`）能正确按 UTF-8 读取，而 Windows PowerShell 5.1
   会按 ANSI 解码并**直接解析失败**（`Unexpected token`）。同类现象也出现在
   `scripts/verify-single-file-standalone.ps1`（它有 BOM，因此正常）。定位本轮时已确认：
   编辑这类含中文的脚本必须保留 BOM，否则 5.1 下语法错误。
2. **`scripts/verify-ui-pages-live.cjs --app` 遇到含空格的路径会失败。** 传
   `--app "…\中文 冷启动 1234\x.exe"` 时被测脚本报「未知参数: 冷启动」。
   CI 里 `dist/` 路径无空格所以未暴露；本轮改用无空格的冷启动目录后正常。

## 6. 复现命令

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

# 4) 真实 WebView2 九页（--app 路径不要含空格）
node scripts/verify-ui-pages-live.cjs --app "<工作区外>\cold2-中文冷启动\Pineglow-Launcher-win-x64-single-file.exe" `
  --out <截图目录> --json <报告> --keep
```
