# DeepSeek Harness 修复批次独立复验（2026-10-03）

**结论：暂不放行。** 本次 FINAL-REMEDIATION 的旧探针及全量回归均通过，但新增独立边界检查仍复现两个 P1 恢复安全问题，另有回滚 HTTP 契约缺口和审计器假通过问题。R0 与 R3 可以关闭；R1、R4 未关闭；R2 的提示真实性已通过，HTTP 结构化结果仍有缺口。

`TechnicalAcceptance=NOT_PASSED`；`FormalPhaseApproval=NOT_GRANTED`；`ReleaseApproval=NOT_GRANTED`。

## 对象、来源与范围

- 源码事实来源：`D:\松光启动器`，HEAD `f145d14eb26b0b7459a28491e764ac8c573085d3`，当前 dirty 文件。当前聊天工作树为干净旧提交，不能代替源目录。
- 交付包：`C:\Users\Administrator\.codex\worktrees\ed41\松光启动器\docs\market-execution\FINAL-REMEDIATION\20261003-052200Z`。
- 前轮验收、范围定义：ed41 的 `docs/deepseek-harness-final-acceptance-2026-10-03.md`、`deepseek-harness-final-remediation-workflow-2026-10-03.md`、对应 prompt。
- 本轮证据根：当前工作树 `docs/market-acceptance/20261003-remediation-recheck`，下文简称 E。
- 新建副本：`C:\Users\Administrator\AppData\Local\Temp\pineglow-codex-recheck-5a304f4403c544e9be92d9e452fd619d`。
- 最新边界检查证据：`E/boundary-run-20261003T070203Z-6ef2deb1`，简称 B。
- 执行时间：2026-10-03 14:56–15:03（Asia/Hong_Kong）；精确 UTC 以 command JSON 与 TRX 为准。

本轮独立复制 161 项 tracked 当前输入 + 123 项白名单输入 + 2 份前轮探针，共 **286 项**。每项复制后与源 SHA 相同，且 **286/286 与修复方 green-snapshot-manifest 一致**。动态检查使用未改造的生产实现；只在副本测试工程加独立探针，没有给生产方法加 hook 或替换实现。

独立比较前轮 281 项基线：275 项完全一致，6 项改变（SecretStore、FileRecoveryService、ModReplacementService、LocalApiService、ConfigPreservationTests、source-snapshot-inputs）。新增 3 个输入（SecretStoreBackend、SecretStoreBackendTests、FinalAcceptanceRegressionTests）；与修复方清单一致。完整差异见 `E/reviewed-diff.patch`。已读实际服务实现、测试、旧 RED/TRX、当前 GREEN、清单及审计脚本。

## 阻断项与补充缺口

### RA-01 / P1：撤销 Mod 替换仍可穿过记录根以上的 junction

实际源码：`FileRecoveryService.cs:258` / `:567`；API 绑定检查 `LocalApiService.cs:2755`。

正常 Mod 替换把旧包隔离时，record.GameDirectory 是 **game/mods**。新校验只检查该根及根内父链。把其上层 **game** 换成指向 GUID 沙盒兄弟 outside 的 junction，outside/mods 自身仍是普通目录，所以校验放行。HTTP 对记录与实例的绑定仍只是词法检查。

**实际复现，无业务源码改造：** HTTP replace 成功 → 把合成 game 移到 original-game → 新建 outside/mods → 创建 game→outside junction → HTTP restore。结果：

```text
restoreHTTP=200
body={"ok":true,...,"status":"restored",...}
outsideFileExists=True
```

在 B 的两次独立测试中均失败。这不是交付报告已登记的“检查与写入之间竞态”：junction 在请求前已经存在，不需要抢时间。

**影响：** 原 F-01 的越界写入仍可通过真实 API 触发，只是转移到 Mod 替换记录所用的子目录根场景。全部目标为自建 GUID 沙盒；没有触碰真实游戏文件，也未证明真实用户文件被覆盖。

**最小修复方向：** 校验必须锚定可信实例根与完整有效祖先链，不能将不可信记录中的子目录当成完整边界。游戏根、mods 根、恢复根及其有效祖先都需检查；明确目录替换竞态防护。拒绝前不得移动或删除隔离源；保持可恢复状态与 SHA。

### RA-02 / P1：隔离源只绑定全局恢复根，未绑定本实例、本操作

实际源码：`FileRecoveryService.cs:275` 只检查 StoredPath 在 `_recoveryRoot` 内，`:328` 随后移动该文件；API `LocalApiService.cs:2743–2762` 没有验证条目源属于当前实例/操作。

前轮工作流 §4.1 已明确要求“隔离路径必须归属该操作”。修复只拦恢复根之外的文件，恢复根内另一实例的文件仍可被引用。

**实际复现：** 隔离 A 实例的 a.jar 与 B 实例的 b.jar → 在合成 A 的 record.json 中将 StoredPath 和 Sha256 改为 B 条目值 → 请求 A 的恢复 API。A 的目标目录仍合法，记录 GameDirectory 未变。

```text
HTTP=200, ok=true, status=restored
target=http-other-instance-B
BStoredExists=False
```

服务探针与 HTTP 探针均失败，各重复一次仍失败。服务探针还确认 A 原隔离副本仍存在；B 的副本却被消费。

**影响与边界：** 当磁盘恢复记录被错误写入或篡改时，可以消耗另一实例/操作的隔离文件并把错误内容写到 A。证明的是不可信磁盘记录的绑定缺口，不是无需本地文件访问权限的远程攻击，也不代表真实文件已经丢失。

**最小修复方向：** 以请求的 instanceId/operationId 构造唯一可信操作目录，绑定 record 的 ID、操作、游戏目录、条目源与相对路径；拒绝跨实例、跨操作、源目录联接及源哈希不符。调用服务层时也需执行同一约束，不能只靠 HTTP 外层。

### RA-03 / P2：回滚失败字段没有穿过 HTTP 边界

实际源码：`LocalApiService.cs:2878–2881` 将字段拼成字符串后抛异常，通用异常响应仅返回 `{error:...}`。

原 F-03 的“已回滚”错误提示已修好；本轮只读目标故障注入后，文本如实表示未完全回滚，旧文件仍可恢复。然而响应为 HTTP 400，**只有 error 字段**。`needsRecovery`、`operationId`、`recoveryStatus`、失败文件都嵌在文字中，客户端不能按 JSON 字段处理。

B 的 HTTP 契约探针连续两次失败；消息：`needsRecovery only appears inside error prose, not a JSON field`。这是结构化响应缺口，不应重写为原误报仍存在；整体暂不放行已经由 RA-01/02 独立构成。

**修复方向：** 局部返回保持既有 error 兼容的失败 JSON，增加状态、needsRecovery、operationId、recoveryStatus、failedFiles。两种失败（回滚成功/失败）的状态必须准确；保留可再次恢复文件。

### RA-04 / P2：证据审计器会放过新漂移、空目录和错误封存摘要

实际脚本：交付包 `audit-all-manifests.py:40`、`:54`、`:119–120`、`:165`。

本轮自行逐项审计真实证据树：**60 份 manifest、830 项记录、2 处历史 SHA/size 不一致**，仍是 MG0 drift-notes 与 MG2 verify-mg2-full.log；修复包及其 detached SHA 当前一致。新 correction 文档准确登记了旧差异，旧文件未被本轮改写。

问题在机器门槛：脚本读 known-mismatches 时仅保存“路径→reason”，不使用登记的实际 SHA/size。因此该路径以后出现第三种字节也被豁免。以下测试只对新建合成证据树运行原版脚本，历史文件只读：

| 合成输入 | 实际结果 | 预期 |
|---|---|---|
| 存在但没有 manifest 的空根 | manifestCount=0、failures=0、exit=0 | 非零：没有覆盖目标 |
| 已知 MG0 路径，但文件与旧 manifest 均换成未登记字节 | knownMismatches=1、failures=0、exit=0 | 非零：新漂移不能继承豁免 |
| 文件与 manifest 正确，但 manifest.sha256 为 64 个零 | failures=0、exit=0 | 非零：detached SHA 不一致 |

**修复方向：** 允许保留历史差异，但异常豁免必须绑定 manifest 版本、文件旧值与登记的当前值；第三次变化失败。证明目标清单覆盖非空且完整；校验 detached SHA。原始两份坏清单保留为历史，另存新的 correction 封存；“已知差异”不能写成“全部哈希一致”。

## 已通过的独立检查

| 检查 | 本轮实测 | exit | 证据 |
|---|---|---:|---|
| R0 凭据隔离专项 | 13/13，0 skip | 0 | E/01-r0.log、test-results/r0.trx |
| 前轮 20 探针 + 新常驻 12 回归 | 32/32，0 skip | 0 | E/02-existing-probes.log、existing-probes.trx |
| C# 全量 | 875/875，0 skip | 0 | E/03-full-csharp.log、full.trx |
| Node 20 个入口 | 238/238，0 skip | 0 | E/04-node.log、node-inputs.json |
| Release VerifyHost 构建 | 成功 | 0 | E/05-verifyhost-build.log |
| Bisect 真实 loopback HTTP 与合成文件 | 19/19 PASS | 0 | E/06-bisect-smoke.log |
| 新独立 5 探针 | 1 pass / 4 fail，0 skip | 1 | B/07-new-boundaries.log、boundaries.trx |
| 同一 5 探针重复 | 1 pass / 4 fail，0 skip | 1 | B/08-new-boundaries-repeat.log、boundaries-repeat.trx |

**数量口径：** 875 全量在加入本轮新 5 探针之前运行，含之前的 20 探针；32/13 是该全集的子集，不能累加。新 5 探针的 4 个失败对应 RA-01、RA-02（服务/HTTP 各一）、RA-03 三个根因。1 个通过是两条重叠异步流的凭据后端隔离，native delta=0。审计器 3 个合成案例另计，exit 0 是被证明的假通过，不能当作验收 PASS。

R0：已确认测试 ModuleInitializer 将进程默认后端安装为内存，原 5 配置用例的 scope 与正向受理断言保留；两条并发流相互隔离。没有枚举或读取真实 Windows 凭据。

R3：运行中 GET mods 200，危险 Mod 变更/移除实例仍被常驻回归拒绝。运行态用测试进程 PID 合成注册；没有启动真实游戏。

R2：服务层失败结果、回滚成功与失败的文案、解除只读阻断后再次恢复均通过已有回归；HTTP 字段缺口另列 RA-03。

## 现场保护、局限与复跑

`E/source-final-protection.json`：D 盘 git status 前后一致，284 项源输入哈希漂移 0；真实 config.json、.bak、.prev SHA 前后一致，secrets.json 前后均不存在。只新增当前工作树的报告、方案、提示词、证据和副本中的探针。没有修改 D 盘业务代码、旧证据、真实存档、真实凭据或系统配置，没有调用 Harness 执行、提交、发布或推进阶段。

本轮未验证真实 Avalonia/WebView2 GUI、真实 Minecraft、自更新真实进程交接、Windows 原生凭据读写往返、远程 CI/干净提交、跨平台、跨卷/满盘。恢复检查到文件操作之间仍有 TOCTOU：静态可见 Move/删除隔离源发生在写入后复核之前；本轮没有动态注入该竞态，也没有声称其已关闭。

重跑完整批次：先读 `E/run_checks.py`，在新的证据根复制脚本与输入后运行，脚本新建 GUID 白名单副本。后续运行 `E/run_boundary.py` 会新建时间戳证据子目录保留两次失败，不覆写历史 run；副本根从 snapshot-root.txt 读取。每个子命令保存 command JSON（argv、cwd、UTC 起止、exit）及 raw log。执行器脚本本身正常结束不表示验收通过，应检查所有子命令 exit、TRX 和 audit issues。

证据索引：snapshot-manifest、delivery-source-binding、independent-delta、reviewed-diff、test-summary、source-final-protection、B/independent-manifest-audit、B/auditor-*、manifest.json 与 manifest.sha256。探针源码 `E/RemediationBoundaryProbes.cs` 可直接转入授权后的常驻回归。

建议返修顺序：RA-01 → RA-02 → RA-03 → RA-04 → 同版全量与独立复验。详细方案与提示词已另存当前 docs；它们是待执行交接材料，不构成实施授权。

## 输出质量自检

按 agent-self-evaluation 检查：准确性 4/5（原始 TRX/HTTP/SHA 支持结论；TOCTOU 仅静态未动态证明）；完整性 4/5（本修复范围与关键否证覆盖，GUI/原生凭据往返未验证）；清晰度 4/5（区分关闭项、残留项和测试口径，路径较长）；可执行性 4/5（失败源码与分包提示完整，实施需另行授权）；简洁性 4/5（报告保留复跑细节，聊天答复压缩）。平均 4.0/5。优先改进：返修后补同版 GREEN；按需要补 GUI；把封存例外精确绑定到字节。自评分不参与产品放行。
