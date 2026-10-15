# 第 3 轮复验补充记录（B 的升级链与回退证据）

运行标识：`run-2026-10-10_015215-578dbafb`（同一运行目录，第 3 轮追加）
时间：2026-10-10 02:32–02:50，Asia/Hong_Kong（+08:00）
授权范围：沿用「授权修 A+B」；B 的允许路径在 `release-readiness-remediation-restored-2026-10-10.md` 中明确包含
`UpdateService.cs` 的迁移路径与 `UpdateRecoveryTests.cs`。本轮除该测试文件外未改产品源码。

## 1. 先做的脚本审查（结论：不能直接运行旧链脚本）

`docs/release-remediation-evidence-2026-10-05/run-20261006-1800-update-chain/verify-update-chain.ps1` 审查结果：

| 检查项 | 结果 |
| --- | --- |
| 写真实用户路径 | 否，全部在 `%TEMP%` 夹具 + 证据目录（配置靠 `portable.flag` 隔离） |
| 清库/迁移/付费接口 | 无 |
| 停止非本轮进程 | **有风险**：第 155 行按进程名枚举 `Pineglow.Launcher`，第 180 行把这些进程**全部强杀** |
| 其它 | 需要联网（清单校验强制产物为公开 HTTPS）；会遗留 `PINEGLOW_API_PORT` 到当前会话环境 |

按你的规则「不按进程名批量杀掉用户应用」与「已有脚本须先审查是否写真实用户路径、清库或停止非本轮 PID；只有隔离后才运行」，
**本轮没有运行该脚本**，改为自建隔离探针 + 补测产品测试。

## 2. 链条两半各自的证据边界（先说清楚再给结果）

「下载 → 替换自己 → 重启」这条链在本机被**证书信任**一分为二：

- **下载半段需要真实 HTTPS 产物地址**（`UpdateService.cs:279` 强制 `assetUri.Scheme == https`）。
  本机 `CurrentUser\My` 里有一张 `CN=localhost` 开发证书，但它**不在** `CurrentUser\Root`，即不受信任；
  要让本地 HTTPS 被接受就必须把证书装进信任库——那是系统安全设置变更，且不在本次授权范围内，
  关闭 TLS 校验更被规则明确禁止。因此**下载半段未做真实 HTTPS 复验**，只沿用既有假 handler 单测
  （截断被拒、跨平台资产被拒）。
- **替换/校验/回滚/重启半段**改为两条互补证据：产品测试（真实执行生成的脚本）+ 真实 EXE 的升级探针。

## 3. 补测：重启命令被真实执行（产品测试，永久回归位）

既有用例把重启命令固定替换成 `rem noop-restart`，类注释本身就把「真实重启（测试用空操作代替启动命令）」列为不覆盖项，
所以「任何失败路径都会启动旧版本」此前只有字符串断言、没有执行证据。本轮新增两条用例并**真实执行**生成的脚本：

| 用例 | 断言 | 结果 |
| --- | --- | --- |
| `ApplyScript_OnSuccess_ActuallyRunsTheRestartCommand` | 成功路径：`applied`、目标=新内容、备份=旧内容、**重启标记文件存在** | PASS |
| `ApplyScript_OnRollback_StillRunsTheRestartCommand` | 回滚路径：`replace_failed`、目标内容回到旧值、**重启标记文件存在** | PASS |

RED 实证（在隔离副本里删掉回滚路径那一行重启命令后重跑）：

```
[FAIL] ApplyScript_OnRollback_StillRunsTheRestartCommand   [restart-rollback] state=replace_failed markerExists=False target=OLD-CONTENT
        [restart-success] state=applied markerExists=True
失败! - 失败: 1，通过: 1      （退出码 1）
```

即：新用例能精确抓住「回滚后不再拉起旧版本」这一种回归，成功路径用例不受影响——不是空断言。
`dev/PineglowLauncher.Tests/UpdateRecoveryTests.cs`：`6C42FFC6…`（改动后）。

## 4. 真实 EXE 升级探针（自有工具，`probe-upgrade-config.ps1`）

夹具 = 目录式安装现场：**旧 EXE（未修复件 `3BA60553…`）+ 残留外置旧 `UI/`（带标记）+ 已存在的 `data/config.json`（带标记值）**。

| 阶段 | 观察 | 结果 |
| --- | --- | --- |
| 升级前（旧 EXE） | 健康检查就绪；`GET /` **服务的是外置旧界面**（`servedStaleMarker=true`） | RED 基线的真实现场 |
| 替换步骤（与已验证脚本同序：备份 → 覆盖 → `fc /b` 逐字节校验） | `fc` 退出码 0、逐字节一致；目标 = 新 EXE；备份 = 旧 EXE（哈希相等） | PASS |
| 升级后（新 EXE，同目录、旧 `UI/` 仍在） | 健康检查就绪；**不再服务旧标记**；服务内容归一化后与当前源码 `ui/index.html` **哈希相等** | PASS |
| 配置 | `data/config.json` 升级后仍存在、**内容逐字节未变**、标记值仍在 | PASS |
| 进程 | 两个阶段都只停止探针自己启动的进程（`processStopped=true`） | PASS |

探针退出码 0；原始结果 `b-upgrade-config/upgrade-config.json`、`b-upgrade-config-console.log`。

**这一条与 3 节的证据是互补的，不能互相替代**：3 节证明「产品生成的脚本在被真实执行时，备份/替换/校验/回滚/两条重启路径都成立」；
4 节证明「一个真实的目录式旧安装，按同一序列换掉 EXE 后确实进入新界面且配置不丢」。
仍未直接合成的是「由生成的 .bat 自己去替换那个 53 MB 真实 EXE」——它依赖 2 节所说的 HTTPS 下载半段。

## 5. 本轮产品文件改动

| 文件 | 变化 | 说明 |
| --- | --- | --- |
| `dev/PineglowLauncher.Tests/UpdateRecoveryTests.cs` | **未跟踪的白名单输入**（类别 B，C# 测试源），新增 2 个用例 + 抽出 `RunApplyScriptCore` | 在 B 的授权路径内；文件本来就在白名单里，因此无需改白名单，闭包断言不受影响 |

`dev/PineglowLauncher/Pineglow.Launcher.Services/UpdateService.cs` **未改**（它在 `git status` 里显示为 ` M` 是恢复现场的既有差异，不是本轮改动；本轮只在隔离副本里对它做过 RED 注入，已还原）。
A 的四个文件哈希与第 1 轮记录一致；0 staged。

改后完整流水线（第 4 次，`green-snapshot-3/`）：判定 **PASS**、退出码 0；
快照 139 + 163 = 302 文件、泄漏 0、闭包缺失 0；**C# 941/941**（939 + 本轮 2 条）、Node 305/305、冷启动 19/19；
发布 EXE SHA 仍为 `ca8f94c1a6173f23f38f3d2a3c949affc16d5ab67752b662c7806831f350d93e`——四次流水线产物逐字节相同（测试工程不参与启动器产物，属预期一致）。


## 6. 仍未完成

- 下载半段的真实 HTTPS 复验（受证书信任限制，需授权安装受信任证书或提供可用的 HTTPS 产物地址）。
- 真实发布端点、真实安装目录替换（产品测试注释里同样列为「需另行授权」）。
- C（RID 锁定方案）、D（游戏内长时场景）、E（真实账号/皮肤/AI）、F（候选提交与同 SHA CI）。
- 目录构建形态的完整端到端（本轮只覆盖了「外置 UI 优先」这一条判定）。

## 7. 状态

A：两轮流水线 PASS + 闭包断言 RED 实证——具备独立复验条件。
B：界面来源、目录构建、资源同版、重复启动、脚本回滚/重启（含 RED）、真实 EXE 升级与配置保留均已留证；
**仅剩依赖真实 HTTPS 下载的那一段未复验**。
`TechnicalAcceptance=NOT_PASSED`（发布整体）；`FormalPhaseApproval=NOT_GRANTED`；`ReleaseApproval=NOT_GRANTED`。
