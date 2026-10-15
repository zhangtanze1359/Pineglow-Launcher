# 第 2 轮复验补充记录（A/B 收口证据）

运行标识：`run-2026-10-10_015215-578dbafb`（同一运行目录，第 2 轮追加）
时间：2026-10-10 02:20–02:31，Asia/Hong_Kong（+08:00）
本轮性质：**只做验证与取证，没有再改任何产品源码**（4 个受改文件哈希与上一轮记录一致）。

## 1. 本轮新增的五项检查

| # | 检查 | 结果 | 证据 |
| --- | --- | --- | --- |
| 1 | 新闭包断言的 **RED 实证**（跳过类别 O，复现恢复后的 290 项现场） | 断言按预期失败并结构化退出 | `red-closure-console.log`、`red-closure/verify-summary.json`、`red-closure-exit.json` |
| 2 | 目录构建（非单文件）仍优先 EXE 旁外置 `UI/` | PASS，标记被服务 | `b-dirbuild/dirbuild-external-ui.json`、`b-dirbuild-console.log` |
| 3 | 服务的页面与 JS/CSS 字节属于**同版 EXE** | PASS，6/6 逐字节一致 | `b-assets/assets-version.json`、`b-assets-version-console.log` |
| 4 | 重复启动与解包复用 | PASS，两次启动同一解包键目录 | `b-repeat-launch/repeat-launch-summary.json`、`b-repeat-launch/same-fixture-repeat.json` |
| 5 | 第二次完整候选流水线（独立快照 + 完整控制台留档） | **PASS**，产物 SHA 与首轮**完全相同** | `green-snapshot-2-console.log`、`green-snapshot-2/` |

## 2. 闭包断言的 RED 实证（本轮最关键的一条）

命令：`verify-source-snapshot.ps1 -SkipCategories O`（等价于恢复后「白名单不含类别 O」的现场）。

```
[OK]   白名单文件已复制: 151 个（缺失 0）
文件数量: 期望 290 / 实际 290
[OK]   文件数量一致
[OK]   通用泄漏检测通过：额外文件 0
[FAIL] 候选项闭包不完整：12 个产品输入未进入快照（已检查 298 个）
 - 候选项闭包不完整，缺 12 项: dev/PineglowLauncher/Pineglow.Launcher.Models/AiProviderProfile.cs, …
最终判定: FAIL        （退出码 1；verify-summary.json 正常写出：closureMissing=12）
```

这正是 2026-10-10 01:33 那次失败现场的**同一组数字**（139 tracked + 151 白名单 = 290 文件），
当时「数量一致」和「泄漏检测」两条**都通过了**，脚本随后在硬编码抽查读 `AiDiagnoseService.cs` 时抛异常中止、连汇总都没写出来。
现在同一条路径会在步骤 2 就给出**结构化失败**，列出全部 12 个缺失输入，并按既有约定以非零退出码结束。
这条实证说明闭包断言不是「加了个可能永远通过的检查」，而是确实能抓住当初静默漏掉的东西。

## 3. 目录构建（开发/目录包）未回归

`probe-dirbuild-ui.ps1`（本轮自有探针）在非单文件构建产物旁写入带唯一标记的 `UI/index.html` 后启动：

```
healthReady=True  externalMarkerServed=True  ownedProcessStopped=True  （退出码 0）
```

即 R02 的次序调整只作用于单文件发行；开发树/目录包依旧使用 EXE 旁的外置 UI，既有开发流程不变。
说明：该探针的次要断言 `servedEqualsExternalUi=false` 属**探针口径问题**而非产品缺陷——
服务端会把 `pineglow-session-token` 占位符替换为本次运行的会话令牌（当前版本 `index.html` 含该 meta 标签），
所以原始字节哈希必然不同；「标记被服务」才是判定外置 UI 被选中的有效断言。

## 4. 服务的资源确实属于同版 EXE

`probe-assets-version.ps1` 在「单文件 EXE + 残留旧外置 `UI/`」布局下逐个抓取并比对：

| 资源 | HTTP | 与源码字节一致 |
| --- | --- | --- |
| `index.html` | 200 | 是（比较前归一化会话令牌） |
| `js/api.js` | 200 | 是 |
| `js/i18n.js` | 200 | 是 |
| `js/launcher.js` | 200 | 是 |
| `css/pineglow.css` | 200 | 是 |
| `css/figma.css` | 200 | 是 |

夹具里外置 `UI/` 只放了一个旧 `index.html`，而 `/js/*`、`/css/*` 全部 200 且与当前源码逐字节一致——
说明整个 UI 根目录都已切到内嵌包，不再是外置旧目录（旧行为下这些路径会落在外置目录上而 404）。

## 5. 重复启动与解包复用

- 同一安装目录连续启动两次：两次都 `healthReady=True`、退出码 0；`data/UI/` 下**只有一个**解包键目录
  `1.0.0-41346c7082cb`（版本 + 内容指纹），即第二次启动复用既有解包，不重复展开。
- 两次抓取的 `index.html` 原始哈希不同（`8872D5…` / `13AD0D…`），因为会话令牌是**每进程随机**的；
  归一化后与源码一致（上一轮 `probe-ui-source-v2` 两次启动给出同一个归一化哈希 `39933134…`）。
- 解包损坏后的自愈由既有单测 `ExtractTo_IsIdempotent_AndRepairsDamagedExtraction` 覆盖。

## 6. 可重复构建

第二轮完整流水线（独立空缓存快照）与第一轮结果逐项一致：

| 指标 | 第一轮 | 第二轮 |
| --- | --- | --- |
| 最终判定 | PASS | PASS |
| 快照文件 | 302（139 + 163） | 302（139 + 163） |
| 泄漏 / 闭包缺失 | 0 / 0 | 0 / 0 |
| C# 测试 | 939/939 | 939/939 |
| 冷启动 | 19/19 | 19/19 |
| 发布 EXE SHA-256 | `ca8f94c1a6173f23f38f3d2a3c949affc16d5ab67752b662c7806831f350d93e` | **同上，逐字节相同** |

两轮独立组装得到同一个单文件 EXE，这对「候选输入闭包已固定」是较强的旁证。

## 7. 本轮修正的自身问题

1. **控制台留档为空**：上一轮用 `2>&1` 捕获脚本输出，但脚本用 `Write-Host`（信息流），因此
   `green-snapshot-console.log` 为 0 字节、`p1a-snapshot-console.log` 仅 3 字节。日志本身没丢——
   `green-snapshot/` 下 19 个逐步原始日志与 23 KB 的 `verify-summary.json` 才是权威证据。
   本轮改用 `*>&1` 重新跑了一遍完整流水线并留档（`green-snapshot-2-console.log`，13,280 字节）。
2. **探针参数拼接错误**：首轮用 `@($hashtable)` 展开参数导致参数绑定失败，当时打印的退出码 0 是**假值**。
   已改为 `@hashtable` 正确展开后重跑，两个探针均以真实退出码 0 通过。

## 8. B 仍未完成的证据项

`release-readiness-remediation-restored-2026-10-10.md` 给 B 的 GREEN 还包含
「隔离的**下载 → 替换自己 → 重启**链路，以及下载中断、不能替换、校验不一致条件下的回滚、原 EXE 与配置存活、明示失败」。
本轮**未执行**该链路。仓库里存在 2026-10-06 的旧链脚本（`docs/release-remediation-evidence-2026-10-05/run-20261006-1800-update-chain/verify-update-chain.ps1`），
按既有规则需先审查它是否写真实用户路径、清库或停止非本轮 PID，确认隔离后才可运行。

其余未验证项与上一轮相同（真实 WebView 全量操作、在线安装、进世界与长时游玩、真实账号/皮肤/AI、目录构建形态的完整端到端）。

## 9. 状态

A：流水线两轮 PASS，闭包断言有 RED 实证——**具备独立复验条件**。
B：单文件优先级、目录构建保持、资源同版、重复启动均已端到端复验；**升级自替换链路仍缺证据**。
C：未处理（需锁定方案决策）。D/E/F：未开始。
`TechnicalAcceptance=NOT_PASSED`（发布整体）；`FormalPhaseApproval=NOT_GRANTED`；`ReleaseApproval=NOT_GRANTED`。
