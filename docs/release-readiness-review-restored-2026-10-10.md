# 恢复版本距正式发布的独立评估

日期：2026-10-10，Asia/Hong_Kong。检查目录：`D:\松光启动器`。

**结论：目前暂不可作为正式成品发布。源码、自动化及单文件首启基础已可用；至少两项可复现阻断仍在，另有发布可重复性与真实场景证据缺口。上次两个客户端 JAR 反例已在本轮解除。**

`TechnicalAcceptance=NOT_PASSED`（发布整体）；`FormalPhaseApproval=NOT_GRANTED`；`ReleaseApproval=NOT_GRANTED`。
本轮仅检查、隔离验证与文档交付；没有修复产品代码、提交、推送、打 tag、发布、上传或修改线上清单。

## 1. 版本与现场

- SourceRoot：`D:\松光启动器`，用户本轮明确指定。
- Git HEAD：`c8aa286f65ad13433129fe0a9e86e2d3b0e9020b`（远程恢复的 2026-08-11 历史）；工作区是恢复的 10 月代码，不是 8 月产品回退。
- 历史 `f145d14` 等未推送对象未恢复，不能称 Git 历史已完整恢复。找回旧对象不是产品发布必须依赖的条件；可以保留恢复资产后另行建立经确认的新候选版本。
- 已跟踪改动 41 项；本轮开始与结束均 0 staged；未跟踪材料见 `release-audit-2026-10-10/git-status-before.txt`。不以旧 HEAD 上的 clean checkout 代替当前候选。
- 按恢复交付清单逐项核对 312 文件，312/312 与预期 SHA-256 一致；复制到新 GUID 隔离目录，从无 bin/obj 的输入开始验证。它是冻结的恢复快照，不是已提交干净源码或远程 CI。
- `LauncherService.cs` 为 `4A3F4168F19F9E420E26FCD2EDBE8753795EAF8CBA75B6E3060D1C0C9A7B3B78`，`LoaderLaunchAssemblyTests.cs` 为 `D341930A502CCB1E1033359AFEBF36A6AFB04C546FD3095DC152EEC68A846340`，与较晚返工版本一致。
- 312 文件前后哈希变化 0；真实用户 config.json/.bak/.prev 存在状态与哈希不变。测试和迁移探针使用独立目录，探针拥有的启动器已停止；没有停止既有应用或 Java 进程。
- 恢复材料明确把 `8450` 工作树的较晚皮肤功能留在单独副本。当前 D 盘没有 `btnApplySkin`；同名皮肤验收文档属于另一工作树，不能当作主版本已包含该功能。是否纳入此版属于范围选择。

## 2. 本轮直接验证

SDK 10.0.301，Node v24.19.0；开始约 01:33，结束约 01:39，+08:00。每步精确 UTC 时间、cwd、命令与退出码在 `release-audit-2026-10-10/run.json`，转换 +08:00 即香港时间。

| 检查 | 结果 | 原始证据（相对本 docs） | 能证明的范围 |
| --- | --- | --- | --- |
| 恢复输入及前后稳定性 | PASS，312/312，变化 0 | `release-audit-2026-10-10/input-hashes-before.json`、`input-hashes-after.json` | 当前恢复输入与来源清单一致 |
| 测试清单双向一致 | PASS，23 Node 文件、1 C# 项目 | `release-audit-2026-10-10/test-inventory.json` | 无清单与磁盘漂移 |
| UI 资源闭包 | PASS，9 引用、缺失 0 | `release-audit-2026-10-10/ui-closure.json` | 静态入口闭包 |
| Node 全量 | PASS，305/305，0 fail/skip/cancel，exit 0 | `release-audit-2026-10-10/node-all.log` | 现有前端与验证工具用例 |
| 非 RID locked restore | PASS，exit 0 | `release-audit-2026-10-10/restore-locked.log` | 当前非 RID 依赖锁定 |
| 启动装配/Java 聚焦 | PASS，14/14，0 fail/skip，exit 0 | `release-audit-2026-10-10/focused.log`、`focused.trx` | 指定启动装配边界 |
| 上轮原始反例探针 | PASS，两个反例均 before/after 保留原版 jar，exit 0 | `release-audit-2026-10-10/original-boundary.json`、`original-boundary-run.json` | 无关 extra、旧 client extra 不再错误移除当前原版 jar；原探针代码未改 |
| C# 全量 | PASS，931/931，0 fail/skip，exit 0 | `release-audit-2026-10-10/csharp-all.log`、`all.trx` | 现有 C# 用例；聚焦 14 已包含在 931 中，不能相加 |
| VerifyHost build / 二分真实 HTTP 冒烟 | PASS，exit 0；生产凭据后端调用 0 | `release-audit-2026-10-10/verifyhost.log`、`bisect-smoke.log` | 文件故障注入、定位/放弃/恢复及隔离凭据 |
| RID locked restore | FAIL，NU1004，exit 1 | `release-audit-2026-10-10/rid-locked.log` | lock 未覆盖 win-x64 与 ILLink 任务包 |
| 按 Release 主要参数单文件打包 | PASS，exit 0 | `release-audit-2026-10-10/publish.log`、`run.json` | 当前冻结输入可以生成 Windows x64 自包含单文件 |
| 单文件独立冷启动 | PASS，19/19，exit 0 | `release-audit-2026-10-10/standalone-cold.json`、`standalone-cold.log` | 中文/空格目录，仅 EXE，窗口标题、API、UI 资源、内嵌字节对应当前源码 |
| 原有源码快照流程 | FAIL，exit 1 | `release-audit-2026-10-10/existing-snapshot-console.log`、`existing-snapshot-exit.json` | 现有正式候选组装路径不再完整 |
| 保留旧 UI 的 EXE 升级路径 | FAIL，exit 1，legacyUiServed=true | `release-audit-2026-10-10/legacy-ui.json`、`legacy-ui.log` | 新 EXE 在旧 UI 目录旁继续服务旧界面；是隔离安装布局复现，未运行真实下载及自替换全链 |
| 线上默认更新入口 | 主 Gitee 404；备用 GitHub 200、manifest.version=1.0.0 | `release-audit-2026-10-10/update-endpoints.json`、`github-manifest-check.json`、`github-live-manifest.json` | 本轮只读 HTTP 和 JSON 验证；未下载资产、未更新远程清单 |

本轮 EXE 版本用 `1.0.1` 验证非默认版本资源解包，**只是本地验收版本，不是选定发布版本或已发布 1.0.1**。
产物 53,052,722 bytes；SHA-256 `3BA605531690D832900280DD29CCEAF1C0102EC71171F8498AFFF234DC47F96F`。路径见 `run.json.artifact.path`。
普通 publish 同时输出约 105 MB 两份原生 PDB；正式工作流有清理逻辑。本轮只取 EXE 进行冷启动，不把整个临时 publish 目录当成整理后的交付包。
普通 RID publish 会改写**隔离快照**的 lock，原 D 盘 lock 未变。非 RID lock 和 RID publish 不能混称全链已锁定。

## 3. 两项已复现阻断

### P1 / S01：恢复后候选输入收集遗漏

位置：`scripts/verify-source-snapshot.ps1` 的 tracked+白名单收集与 tracked 抽查；`scripts/source-snapshot-inputs.txt`。

当前 Git 已跟踪 139 项，白名单 151 项，去重后 290 项。相对已验证的 312 项恢复清单漏 22 项：11 项源码/测试/VerifyHost 和 11 项项目文档。漏生产文件包含 `AiDiagnoseService.cs`、`MinecraftNewsService.cs`、`ModBisectService.cs`、`AiProviderProfile.cs`。
实际运行现有脚本，它按 290 项数量检查通过后，在打开漏掉的 `AiDiagnoseService.cs` 时异常退出，未到测试或发布步骤。

原因：旧白名单把较晚本地提交已跟踪文件交给 git ls-files 收集；恢复的 Git 历史更早，这些文件现在变成未跟踪，不能再自动收进候选。
影响：主目录构建成功仍可能交付缺源码；不能凭恢复测试 PASS 宣称现有发布可重复性流程通过。完整漏项在 `run.json.omittedByExistingSnapshot`。
方向：重建**明确候选输入闭包**，确保必需源码/测试/工具全部纳入，文件缺失给结构化失败并保留日志。不要把 docs 证据目录或所有 untracked 一股脑收进去，也不要批量暂存。

### P1 / S02：旧目录升级继续加载旧界面

位置：`dev/PineglowLauncher/Pineglow.Launcher.Services/LocalApiService.cs:228`，尤其 236–240；`UpdateService.cs` 的仅 EXE 替换策略。

`ResolveUiRoot` 优先选 EXE 旁 `UI/`，然后才选内嵌解包目录。用本轮新 EXE 放入独立 portable 目录，保留由旧 HEAD 的真实 `ui/index.html` 加唯一取证标记构成的外置 UI；清除开发 UI 环境变量，启动后 GET `/` 返回旧标记，`legacyUiServed=true`。探针退出 1，自有进程已停止。
影响：目录式老版本原地更新后，后台与前端版本可能不一致。全新安装 19/19 冷启动不会覆盖此问题。
方向：针对正式单文件模式保证当前内嵌 UI 优先，或做有备份和恢复的旧 UI 迁移；保留开发/目录构建有意使用外置 UI 的路径。不能直接删用户旧目录解决。

## 4. 距离发布还差的工作包

| 工作包 | 当前状态 | 关闭标准 | 优先级 |
| --- | --- | --- | --- |
| A 恢复后的候选输入与版本闭包 | S01 FAIL；源码完整但候选流程漏项 | 完整输入在空缓存独立副本构建/测试/发布通过；候选与范围可审阅 | P1，先做 |
| B 升级一致性及回退 | S02 FAIL；下载→替换→重启全链未在本轮执行 | 旧目录/旧 UI/中文空格路径升级用新版 UI，配置不丢；失败可回滚；完整链路留证 | P1 |
| C 发布依赖可重复性 | 非 RID PASS，RID NU1004 | 设计一致的非 RID/RID lock 方案并复验；或明确记录接受未锁定发布风险，不能假报 locked PASS | P2；发布前收口或决策 |
| D 当前最终包核心使用场景 | 首启/API PASS；完整原生 UI、全新在线安装、世界与长时游玩未复验 | 当前包完成原版及承诺加载器的安装→Java→启动→进世界→退出/重启；队列恢复与关键文件事务 | P1 证据缺口 |
| E 真实账号、皮肤、AI等本版承诺 | 真实成功链路未验证；8450 皮肤改动未合入 | 微软成功登录及续期/取消/失败；需要承诺的第三方账号与 AI 用实际用户条件验；不纳入的功能如实限定 | 按发布范围 |
| F 候选归档、同版 CI 与正式更新交付 | 未提交/同 SHA CI；Gitee 404；GitHub 仍 1.0.0 | 有授权的候选提交与同 SHA CI；最终附件/manifest/版本/许可证一致；升级通道可达；发布批准 | 最后完成 |

**判断：已到发布收口阶段，仍需约 6 个工作包；其中 A/B 是已证实的问题，D/E/F 主要是证据、范围与发布流程，C 是依赖管理边界。**
不采用“931 个测试通过所以完成 99%”的百分比；目前没有完整固定的功能分母，测试数量也不代表用户路径覆盖率。

**工期仅为规划估计**：一名熟悉仓库的执行者、网络正常且测试账号及时可用，收口 Windows 预览候选约 2–4 个工作日，完整正式成品约 5–10 个工作日。预览候选也必须关闭 A/B 并完成最低核心路径，不能直接把本轮产物当可对外发布版本。新发现问题、在线安装网络故障、凭据等待、皮肤功能整合、CI/发布授权等待可能延长；本轮不是实际排期或承诺。
原 Git 对象和全部历史截图的进一步恢复可以单独继续，不要求先找回全部历史文件才能准备新候选。

## 5. 证据边界与停止点

- 10 月 7 日返工报告声明六项正常游戏启动，但对应返工目录当前只恢复了 `verify-six-launches.ps1`，未找到该目录列出的 six-launches.json/六份新日志等原始结果。报告文字不是本轮完整复验。
- 本轮没做当前包六类真实游戏启动、进入世界/长时运行、全新在线安装、完整原生 WebView 点击巡检、Quilt 1.21+、微软真实成功授权、AI第三方调用、完整升级自替换/故障回滚、干净候选提交/远程 CI。
- 真实主窗口标题与资源 API 是本轮事实；这不等于原生界面全部交互已验收。
- RID NU1004 不表示普通 publish 不能生成产物；Gitee 404 不表示备用源也不可用，且公开备用清单仍是旧版。
- 旧客户端供应链的两个反例在本轮 PASS，不能继续把历史 FAIL 当当前未修缺陷；也不能推广为所有加载器/损坏 jar 情形均正确。
- 本轮用户只授权检查。发布批准缺失与技术缺陷分别记录；不把缺失批准解释成所有技术测试失败。

详细执行顺序见 `release-readiness-remediation-restored-2026-10-10.md`；复制提示词见 `minimax-release-readiness-restored-prompt-2026-10-10.md`。当前检查完成，停止在材料交付。
