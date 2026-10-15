# DeepSeek Harness 最终批次独立验收 — 2026-10-03

**结论：暂不放行。** Harness 本次执行已结束，连续目标处于 paused；之前两个存档对象绑定缺陷已通过复验。本轮仍发现两个 P1、两个 P2 功能/测试问题，以及两项证据哈希不一致。不能用实现者的 835 项绿色测试声明替代独立验收。

`TechnicalAcceptance=NOT_PASSED`；`FormalPhaseApproval=NOT_GRANTED`；`ReleaseApproval=NOT_GRANTED`。

## 1. 验收对象与现场

- 源码：`D:\松光启动器`；main，HEAD `f145d14eb26b0b7459a28491e764ac8c573085d3`。验收对象包含当前未提交改动，而非仅 HEAD。
- 本轮文档与证据：`C:\Users\Administrator\.codex\worktrees\ed41\松光启动器\docs`。工作树中的历史交付目录不能误当作 D 盘源码目录。
- Harness session：`session-2e72bd8c-79b8-434c-8c38-d5f3a1190634`；连续目标累计启动 63 轮，目前 paused。最后第 66 turn 于 2026-10-03 12:51:38（UTC+8）结束。读取时无打开中的 turn。
- 最新修复交付：`docs/market-execution/MR03-world-backup-scope-fix/20261003-500000Z/deliverable.md`。只表明该次执行完成；完整 MR00–MR27 路线仍包含准备包、条件任务和未完成任务。
- 独立副本：`C:\Users\Administrator\AppData\Local\Temp\pineglow-final-acceptance-2300818f513541a9aab58f9759193f6a`。
- 快照输入：161 个 tracked 当前文件 + 120 个显式白名单文件，共 281 个，复制时逐项 SHA-256 一致。未纳入的 `DownloadResumeTests.cs` 为 0 字节文件。
- 生产目录前后 1360 条状态记录一致，281 项输入哈希不变；已有 config.json、.bak、.prev 哈希不变，secrets.json 前后均不存在。没有读取真实 Windows 凭据内容。

本轮只在独立副本中构建、测试、加入验收探针，使用 GUID 临时沙盒做文件与 HTTP 故障复现。没有修改 D 盘生产源码、真实存档、真实凭据、系统设置或历史日志，也没有执行修复、提交、发布或恢复 Harness 目标。

## 2. 阻断项

### F-01 / P1：恢复接口可穿过目录联接，向游戏目录之外写文件

定位：`FileRecoveryService.cs:228` 的 `RestoreOne` 仅检查词法路径属于 gameDir（`IsPathInside`，383 行）；隔离入口 130 行有 reparse/ancestor 检查，恢复入口缺少相同保护。移动隔离文件时会跟随目标父目录的 junction。HTTP 入口 `LocalApiService.cs:2737` 调用该恢复逻辑。

独立复现：隔离 `game/mods/a.jar`，移除空 mods 目录，创建 `mods → outside` 的 Windows junction，再恢复。outside 是同一 GUID 沙盒中的兄弟目录，未使用用户目录。服务级探针与真实 HttpListener API 探针均失败；重复运行仍失败。

实际输出：

```text
state=restored, outsideFileExists=True, storedExists=False
API restore => 200: {"ok":true,...,"status":"restored",...}
outsideFileExists=True
```

**影响：** 配置的游戏根目录边界被绕过，接口仍声称恢复成功。本轮证明越界写入，未证明覆盖真实文件或真实数据丢失。

最小修复：在任何创建、删除、移动之前校验目标、父目录链及隔离源的真实边界，拒绝 reparse 路径且保留原隔离文件。对校验到写入之间的竞态明确采取防护或限制；只增加字符串前缀检查不能关闭此项。API 必须返回可读失败并保持可恢复记录。

### F-02 / P1：配置测试的路径替身没有隔离 Windows 凭据操作

定位：`ConfigPreservationTests.cs` 使用 `ConfigStore.ConfigPathOverrideForTest`。`LoadAsync` 的配置归一化会触发 `SaveCore`（`ConfigStore.cs:210`），依次调用 SecretStore 保存账号、MicrosoftRefreshToken、CurseForgeApiKey、ProxyPassword、AiApiKey 等。Windows 上 `SecretStore.Save`（77 行）始终进入 `SaveWindows`；空值走 `CredDeleteW`（123 行），非空值可走 `CredWriteW`（147 行）。便携模式及 JSON 路径替换没有更改凭据目标命名空间。

当前 A15 guard（`ConfigPreservationTests.cs:189` 起）检查 `PlatformPaths.GetConfigDirectory()/secrets.json`。该文件不是 Windows 凭据管理器，因此 guard 通过不能证明凭据未被访问。

**独立取证方式：** 未运行原版这 5 项测试。另建 credential-proof 副本，只在该副本将 SecretStore 的读写入口替换为记录操作意图的内存替身，阻断所有原生凭据 API，且日志不记录值。5 项均通过，但累计产生 20 次 delete-intent：四个上述命名凭据各 5 次，另有 15 次 read-stub。详见 `credential-intents.jsonl`、替身源码及 `06-credential-proof.log`。

**影响：** 原版测试可对真实凭据发起删除/写入，不能在真实用户会话中安全跑完整套件。没有查看真实凭据，不能断言 Harness 之前已经删除了现存凭据。

最小修复：为 SecretStore 的读、写、删提供同一个可注入后端；测试只接触内存或 GUID 隔离后端，原生调用不可达。JSON 路径 seam、portable.flag 或只检查 secrets.json 均不足。先关闭此项，再执行全量测试。

### F-03 / P2：Mod 替换回滚失败，却提示“已回滚到旧版本”

定位：`ModReplacementService.cs:169–171`、186–188 调用 `RollbackAsync` 后无条件拼接成功回滚提示。205–217 行吞掉异常，也未检查 RestoreAsync 返回记录的 partial/failed 状态。

复现使用现有 `BeforeCommitForTest`：旧 JAR 隔离后，在目标位置写入合成只读文件，再抛出提交失败。目标仍为 `foreign-readonly`，恢复记录为 partial，旧版本仍留在恢复区；接口结果 Ok=false，但 Error 声称“已回滚到旧版本”。重复运行仍失败。

```text
status=commit_failed
error=写入新版本失败，已回滚到旧版本：injected commit failure
targetBytes=foreign-readonly, recoveryStatus=partial
```

**影响：** 用户可能依据错误提示继续操作，无法判断还需恢复。本轮未发现旧文件丢失。

最小修复：回滚必须返回结构化结果；失败时返回 needsRecovery、operationId、逐文件错误和真实状态，保留旧文件；只有逐项恢复成功才写“已回滚”。

### F-04 / P2：运行中实例的只读 Mod 列表被启动守卫拦截

定位：`LocalApiService.cs:2520` 的 `HandleListModsAsync` 在 2531 行抛出“请勿重复启动”。这是只读列表处理器。

复现：以当前测试进程 PID 标记合成实例为 running，实际 HTTP GET `/api/instances/{id}/mods` 返回 400，内容是重复启动错误；预期 200。文件保持不变，重复运行仍失败。这里证明 API 运行态行为，未启动真实游戏或验收 GUI。

最小修复：逐路由区分读取、变更和启动。允许 Mod 列表只读查询；保留重复启动、运行中移除与危险文件变更的守卫。禁止全文件搜索替换删除所有守卫。其余类似守卫存在于源码，但本轮只对上述列表接口做了动态证明。

### F-05 / P2：两个 MG 验证包清单与实际文件不符

独立审计先覆盖 55 份运行清单、762 项文件，全部一致；补查顶层 4 个 MG 包后，完整范围为 **59 份清单、784 项记录、0 项自引用、2 项不一致**：

| 清单 | 文件 | 实际结果 |
|---|---|---|
| MG0-verification-package-20261003/manifest.json | drift-notes.md | SHA 不符，实际 4088 字节，记录 3114 字节 |
| MG2-verification-package-20261003/manifest.json | verify-mg2-full.log | SHA 不符，字节数均为 41770 |

这两个文件位于本工作树 `docs/market-execution`。本轮没有改写它们。旧 manifest 自引用问题当前已消除；不能据此扩大为全部证据一致。2 项不一致只证明清单与当前字节不符，不证明篡改动机或内容造假。

最小修复：保留旧清单和原始日志，在新时间戳证据包内写明来源、差异、复跑结果和新清单；独立校验所有层级包，manifest 本身用 detached SHA。项目审计脚本全部 exit 0 却未报告这两项，应补充真实哈希/大小审计并返回非零。

## 3. 已通过的独立检查

| 层级 | 本轮结果 | 退出码 | 原始证据 |
|---|---|---:|---|
| 未修改生产实现的 C# 回归，排除危险配置测试 | 830/830，0 skip | 0 | 01-safe-full.log；safe-full.trx |
| Node，20 个测试入口 | 238/238，0 skip | 0 | 02-node.log；node-inputs.txt |
| Release VerifyHost 构建 | 完成 | 0 | 03-verifyhost-build.log |
| Bisect 真实本地 HTTP + 文件沙盒 | 19/19 检查 | 0 | 04-bisect-smoke.log |
| 原 15 项独立路由探针 + 新 5 项 | 16 pass / 4 fail，共 20 项 | 1 | 09-independent-all-final.log；independent-final-all.trx |
| 上述 4 项失败全部重复 | 0 pass / 4 fail | 1 | 10-all-failures-repeat.log；all-failures-repeat.trx |
| 凭据替身副本的配置测试 | 5/5；20 次删除意图 | 0 | 06-credential-proof.log；credential-instrumented.trx |
| 7 个项目验证/审计脚本 | 7/7 exit 0，MG 使用 SkipTests | 0 | script-results.json；对应脚本日志 |
| 独立全层级证据校验 | 59 manifest / 784 记录，2 mismatch | 审计 JSON 输出问题 | independent-evidence-audit.json |

**数量边界：** 830 项是原生产实现上的安全回归；5 项是改造凭据后端后的取证测试，不能合并宣称“原版 835/835 独立通过”。20 项探针包含同一 junction 缺陷的服务与 API 两种证明，因此 4 个失败对应 3 个运行时根因。重复执行不叠加到唯一测试数量。独立清单审计 Python 当前输出 JSON，不以其进程 exit 0 表示通过；以 failures 数组为准。

原 15 项探针全部通过，之前的 worldA 备份混入 worldB、worldB 恢复 worldA 备份问题已关闭。新增“世界 A-测试”存档删除后恢复，目录名及 level.dat 字节均正确。GroupName 的依据是备份根目录下的相对父目录，最新 deliverable 将其描述为文件名解析，需更正文案。

Bisect 冒烟覆盖 9 项故障处理与 10 项正常流程；失败时返回 409、ok=false、needsRecovery=true、逐项错误，清理后可放弃并恢复。本轮绿色回归也包含更新脚本沙盒测试，但不是实际启动器自更新交接或正式发布验证。

## 4. 重跑方式及证据索引

所有动态复验使用独立副本，不直接在 D 盘运行配置测试。下面是等价重跑命令；原始输出、测试时间和数量见日志及 TRX，不将重建命令字符串当成原始执行器记录。

```powershell
$acceptanceRoot = 'C:\Users\Administrator\.codex\worktrees\ed41\松光启动器\docs\market-acceptance\20261003-final'
$snapshotRoot = 'C:\Users\Administrator\AppData\Local\Temp\pineglow-final-acceptance-2300818f513541a9aab58f9759193f6a'
Set-Location -LiteralPath $snapshotRoot
dotnet test '.\dev\PineglowLauncher.Tests\Pineglow.Launcher.Tests.csproj' -c Release --filter 'FullyQualifiedName!~ConfigPreservationTests' --logger 'trx;LogFileName=safe-full-recheck.trx' --results-directory "$acceptanceRoot\test-results"
$nodeInputs = Get-Content -LiteralPath "$acceptanceRoot\node-inputs.txt"
node --test @nodeInputs
dotnet build '.\dev\PineglowLauncher\VerifyHost\VerifyHost.csproj' -c Release
& '.\dev\PineglowLauncher\VerifyHost\bin\Release\net10.0\VerifyHost.exe' --bisect-smoke
dotnet test '.\dev\PineglowLauncher.Tests\Pineglow.Launcher.Tests.csproj' -c Release --filter 'FullyQualifiedName~IndependentRouteProbes|FullyQualifiedName~FinalAcceptanceProbes'
python "$acceptanceRoot\audit-manifests.py"
```

注意：现存副本后来加入了独立探针。要复现原始 830 数量，需从 281 项快照输入新建副本；否则第一条过滤命令也会包含已加入探针。本轮 credential-proof 有独立后端替身，复跑该证明前必须阅读 instrumentation 脚本和源码，确认原生凭据调用不可达。不要把替身覆盖到生产目录。

关键文件均在 `docs/market-acceptance/20261003-final`：

- `snapshot-manifest.json`：281 项输入与初始哈希。
- `source-readonly-audit.json`、`source-final-check.json`：生产状态/哈希保护。
- `harness-final-session.json`：连续目标状态、最后 turn、原始交付声明。
- `FinalAcceptanceProbes.cs`：本轮 5 项独立探针；原 15 项见上一轮 `20261003-W1-W3/IndependentRouteProbes.cs`。
- `test-summary.json`：TRX 中精确时间、总数、通过/失败/skip；不是累加计数表。
- `credential-intents.jsonl`、`SecretStore.instrumented-copy.cs`、`instrument-credential-copy.py`：凭据证明边界。
- `independent-evidence-audit.json`：每个清单的检查范围及 2 项异常。
- `manifest.json`、`manifest.sha256`：本轮独立证据封存及 detached 校验。

## 5. 放行条件与下一步

建议顺序：**凭据测试隔离 → 恢复路径边界 → 回滚结果真实性 → 只读守卫范围 → 证据重封存 → Codex 独立复验**。保留 Harness paused，不启动新的产品目标。详细实施步骤与独立执行提示词已另存；创建文档不代表授权实施。

需满足：四项失败探针转 GREEN；全部配置测试明确证明真实凭据 API 不可达；原始安全回归、Node、Bisect 冒烟及必要新增测试通过；所有证据包 SHA/大小一致且清单完整；当前版本与被测试版本绑定。执行者只可交付“具备独立复验条件，等待 Codex 独立验收”，不能自行批准阶段。

本轮未验证真实游戏、Avalonia/WebView2 GUI、真实 Windows 凭据读写往返、远程 CI/干净提交、自更新真实进程交接、跨卷/满盘或跨平台。不要将 API 沙盒、BAT 沙盒、测试替身的结果写成这些层级已通过。市场需求验证/用户访谈仍需各自证据。

## 6. 验收输出自检

使用 agent-self-evaluation 对本报告自检；这是文档质量检查，不是产品放行。

| 维度 | 分数 | 证据与改进 |
|---|---:|---|
| 准确性 | 4/5 | 原始 TRX/HTTP/字节与哈希可核对；凭据风险仅证明调用意图，未判断历史凭据损失 |
| 完整性 | 4/5 | 覆盖最新修复及关键事务边界；GUI、真实更新交接没有本轮证据 |
| 清晰度 | 4/5 | 分开原版回归、替身证明及失败探针；完整证据路径较长 |
| 可执行性 | 4/5 | 分包范围、RED/GREEN、停止条件已交付；修复需要后续实施授权 |
| 简洁性 | 4/5 | 最终答复提供短结论，报告保留详细证明；重跑与边界解释仍较多 |

平均 4.0/5。改进优先级：修复后补原版安全全量证据；对关键恢复路径增加真实 GUI 操作；将未来执行命令直接记录为结构化证据。用户应能据原始失败作出暂停与修复决定；最终判断以独立复验为准。
