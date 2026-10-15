# LONG-QUALITY-GOAL 执行报告 · 第 1 轮（2026-10-03）

**目标（登记于本 Harness 目标模式）：** 在保护松光启动器现有 dirty 工作区和全部历史证据的前提下，完成 Windows 本机离线质量闭环：修复证据根/包覆盖与封存顺序，冻结六条普通用户流程的 27 项行为场景，补齐由有效 RED 证明的 UI 状态缺口，建立退出码/测试数量/输入 SHA 可靠的可恢复运行器，最终以新 GUID 源码副本交付完整、机器可审计的候选验收包。只推进 G0–G4。

**本轮结论：G0 DONE、G1 DONE、G2 IN_PROGRESS（5/27 场景，RED→GREEN 已验证）、G3/G4 NOT_STARTED。**
不存在"已完成整个目标"的说法：`ImplementationReady=false`、`IndependentAcceptance=NOT_GRANTED`、`FormalPhaseApproval=NOT_GRANTED`、`ReleaseApproval=NOT_GRANTED`。

## 0. 固定事实与本轮路径

| 项 | 值 |
| --- | --- |
| SourceRoot | `D:\松光启动器`（HEAD `f145d14eb26b0b7459a28491e764ac8c573085d3`，branch `main`，dirty） |
| PlanRoot | `C:\Users\Administrator\.codex\worktrees\34ca\松光启动器\docs` |
| EvidenceRoot | `PlanRoot\market-execution\LONG-QUALITY-GOAL\20261003-092612Z-b4d896a8` |
| ExecutionRoot（G0 副本） | `%TEMP%\pineglow-goal-e47a42e7b21846b89be8d4e0e0766704` |
| 计划终态包 | `<EvidenceRoot>\FINAL-LOCAL-QUALITY\20261003-092612Z-b4d896a8`（G4 生成） |
| runId | `20261003-092612Z-b4d896a8` |
| 工具链 | dotnet 10.0.301 / node v24.19.0 / Python 3.12.10 / PowerShell 5.1 |
| 香港时间 | 本轮执行 2026-10-03 17:28–17:52（UTC 09:28–09:52） |

## 1. G0：冻结基线

命令（cwd=`D:\松光启动器`）：

```powershell
& .\scripts\capture-goal-baseline.ps1 -Stage baseline -EvidenceRoot <ER> `
    -SourceRoot 'D:\松光启动器' -ExecutionRoot <new GUID temp> -Materialize
```

| 期望 | 实际 | 状态 |
| --- | --- | --- |
| 真实退出码 0 | **exit 0** | PASS |
| 输入清单逐项 SHA/大小 | 286 项（tracked 161 + 白名单 125），missing=0，合计 5,907,176 B | PASS |
| 与 R2 独立输入集合一致 | **同样是 286 项**（R2 记录 289 项含 3 项交付根探针） | PASS |
| 新 GUID 副本逐项字节一致 | copied=286，SHA 不一致 **0** | PASS |
| 场景清单冻结 | `g0/requirements.json`：6 组 27 项，含真实生产入口、夹具、可观察预期、既有覆盖、缺口、testId | PASS |
| 期望根/包冻结 | `g0/expected-evidence.json`（历史 2 根）＋`g0/expected-evidence-final.json`（+新根与新包，声明为 pending） | PASS |
| checkpoint | `g0/checkpoint.json`（G0 BASELINED → 自动进入 G1） | PASS |

原始证据：`g0/input-manifest.json`（SHA `A02411E6…2F28`）、`g0/copy-verification.json`、`g0/source-status-baseline.txt`（1366 行 porcelain -uall）、`g0/toolchain.json`、`g0/git-head.txt`。

**基线期间的源改动：0**（本脚本不执行任何 git 写操作）。

## 2. G1：严格证据门槛与封存顺序修复

### 2.1 新审计器（不覆盖历史审计器）

`D:\松光启动器\scripts\audit-goal-evidence.py`（SHA `18AFA9C9…ABFE`）。方向被反转：**期望是冻结文件**，逐 required 根、逐 declared 包按身份校验；磁盘上发现但未声明的 manifest 一律失败。

覆盖的 21+ 类负例：必需根缺失/为空、根无包清单、包缺失、包内 manifest 数≠1、runId 与目录不符、未声明包、未封存同级包、空 files、缺 size、缺 sha、非 64 位 sha、重复条目、缺文件、包内漏登记、未知 SHA 漂移、已登记例外的第三种字节、错误 detached、新包缺 detached、自引用、声明 packageCount 不符、封存后重写、例外对应 manifest 后再变化、未消费的登记例外、manifest 不可读（干净拒绝而非崩溃）。正例：完整严格包 exit 0；legacy-policy 无 size 的历史包 exit 0。

### 2.2 有效 RED：旧审计器的 5 处假通过在本轮新合成目录中复现

```powershell
python -X utf8 <ER>\g1\red-old-auditor-coverage.py <ER>\g1
```

（脚本 SHA、真实退出码、stdout/stderr 与新旧审计器 SHA 全部写入 `g1/red-old-auditor-coverage.json`（SHA `102D5084…8BFB`）与 `.log`。）

| 合成形态 | 旧审计器（R2 `audit-all-manifests-r2.py`） | 新审计器 |
| --- | --- | --- |
| 必需根缺失 | exit **0**（假通过，RED） | exit 1 · `ROOT_MISSING` |
| 必需根为空 | exit **0**（假通过，RED） | exit 1 · `ROOT_EMPTY` |
| 未封存同级运行包 | exit **0**（假通过，RED） | exit 1 · `UNSEALED_SIBLING` |
| 新包空 manifest | exit **0**（假通过，RED） | exit 1 · `MANIFEST_EMPTY_FILES` |
| 条目缺 size | exit **0**（假通过，RED） | exit 1 · `ENTRY_MISSING_SIZE` |

`allRedReproduced=true`（5/5）。

### 2.3 合成契约测试（26 项，0 失败）

```powershell
python -X utf8 scripts/tests/audit-goal-evidence.test.py   # exit 0
```

`Ran 26 tests … OK`，唯一计数 26、0 skip。原始日志：`g1/audit-goal-evidence.test.log`。

### 2.4 真实树覆盖：61 份 manifest / 874 条记录

| 运行 | 命令 | 真实 exit | 结果 |
| --- | --- | --- | --- |
| 原始（不加例外） | `--expected g1/expected-evidence-history.json --out g1/audit-history-raw.json` | **1** | 4 项失败：MG0 `drift-notes.md` SHA+size、MG2 `verify-mg2-full.log` SHA、**R2 `source-after.json` SHA** |
| 字节绑定后 | `--expected g0/expected-evidence.json --out g1/audit-history-bound.json` | **0** | 61 包 / 874 条记录全部核对；`knownDrift=3`（逐条列出，绝不静默通过） |
| 终态预检 | `--expected g0/expected-evidence-final.json --out g1/audit-final-preflight.json` | **1** | 新包的 ROOT_EMPTY + PACKAGE_MISSING —— 未生成就绝不通过 |

**独立复现了 Codex 的结论：** R2 manifest 记录 `source-after.json`=`7CA0937E…F161`，磁盘实际 `0FDB0194…899B2`（同为 4192 B）。原始 exit 1 结果**保留**为 `audit-history-raw.json`（SHA `1DB810E1…C381`）。

### 2.5 新 correction（不改历史字节）

`correction/r2-source-after-postseal-drift.md`：顺序根因来自 Harness 原始 `tool/call seq=1351`（`seal-evidence.ps1` → 审计 → `capture-baseline.ps1 -Tag after` 重写已封存文件）；该调用顺序的只读副本仍留在 `34ca\docs\market-acceptance\20261003-r2-independent\harness-sealing-call-order.json`。旧 manifest、旧日志、旧 detached、旧 RED、R2 包内全部文件字节未变；**原始字节是否可恢复记为 UNKNOWN**，不伪造"原始版本"。新封存契约固定为：**终态采集 → 内容完成 → 封存 → 包外审计 → 之后禁止写包内**；四类违反均有合成否证测试（test_19/test_13/test_20/test_15）。

## 3. G2：六流程行为验证（本轮 5/27，RED→GREEN 已验证）

新增 `ui/tests/goal-core-flow.test.cjs`（SHA `53457987…06F2`）：在 vm 中加载**真实** `i18n.js` + `launcher.js`，注入可控 `SG.api` 与受控 DOM，直接调用真实生产入口（不是静态扫描）。DOM 桩与 `launcher-behavior.test.cjs` 同源，仅补了真实 DOM 的 `Element.contains` 语义。

**有效 RED：**

```powershell
node --test ui/tests/goal-core-flow.test.cjs     # exit 1 · 5 tests / 1 pass / 4 fail
```

| 场景 | RED 表现（真实原因） |
| --- | --- |
| RECOVERY-01 | 查询失败只有一行文本，**没有重试入口** |
| BISECT-03 | pending 查询失败被 `catch(e){/* 忽略 */}` 吞掉，仍显示"正常开始" |
| DIAGNOSIS-03 | 历史读取失败只有一行文本，**没有重试入口** |
| DIAGNOSIS-04 | 非空历史渲染时 `ReferenceError: versionLine is not defined`（模板里引用了未声明标识符） |

**最小修复（只改白名单对象）：** `launcher.js` 三处失败分支给出真实原因 + 显式重试动作（`#rRetry` / `#bPendingRetry` / `#dhRetry`，重试走同一条生产路径，不新增业务入口），并删掉越界的 `versionLine`；`i18n.js` 中英各 5 个新键；`figma.css` 仅加禁用/忙态与重试按钮对齐。

**GREEN：**

```powershell
node --test ui/tests/goal-core-flow.test.cjs     # exit 0 · 5/5 pass
node --test <ui/tests 全部 21 个入口>             # exit 0 · 243 tests / 243 pass / 0 fail / 0 skip
```

对照 R2 独立基线（20 入口 / 238 项）：**+1 入口 / +5 项，既有测试一条未改、未弱化、未 skip**。原始日志：`g2/node-goal-core-flow-RED.log`、`g2/node-goal-core-flow.log`、`g2/node-all-tests.log`。

剩余 22 项（INSTANCE-01..04、RECOVERY-02..06、BISECT-01/02/04/05、IMPORT-01..04、DIAGNOSIS-01/02、UPDATE-01..04）在 `g0/requirements.json` 中已逐项冻结，下一步按同样方法补行为证据。

## 4. 源保护

```powershell
python -X utf8 <ER>\source-drift-check.py <ER> <ER>\g2\source-drift-after-g2.json --label after-g2
# exit 0
```

| 项 | 值 |
| --- | --- |
| 冻结输入 | 286 |
| 未变 | 282 |
| 白名单内变化 | **4**：`scripts/source-snapshot-inputs.txt`（只追加类别 J）、`ui/js/launcher.js`、`ui/js/i18n.js`、`ui/css/figma.css` |
| **范围外变化** | **0** |
| 缺失 | 0 |
| git 写操作 | 0（无 commit / push / stage / reset / clean / restore / checkout / stash） |

## 5. 未运行 / 未验证（不得因本目标完成而变成 PASS）

- **RA-01/RA-02 恢复安全反证：NOT_RUN**（前次被平台终止的范围；未执行、未委派、未换执行者补做；原测试保留、未改 skip、未弱化断言）。
- 真实 Avalonia/WebView2 GUI、Minecraft、Windows 原生凭据往返、自更新真实进程交接、跨卷/满盘、非 Windows 运行、远程 CI：**UNVERIFIED**。

## 6. 下一轮计划（仍在本目标 G0–G4 内）

1. G2 补齐剩余 22 项场景（行为断言为主，已有等价覆盖的按 `requirements.json` 映射复用，不重复造测试）。
2. G3：在**新 GUID 副本**中跑本目标获准的常规 C#/Node/API 集合与磁盘一致性，记录实际数量（基线 24/238/19）与 exit code，不调用含受限场景的全量集合。
3. G4：`run-goal-validation.ps1` + 其合成测试（≥6 项：非零传播、缺 TRX、0 测试、required skip、中断不复用、输入变化拒绝复用），一次失败→恢复演示，终态采集→封存→包外审计→封存后哈希无变化，产出 `machine-verdict.json`、`scenario-matrix.json`、`changed-files.json`、`final-report.md`、`manifest.json`、`manifest.sha256`。

## 7. 本轮产物 SHA256（完整值见下；审计报告内含 `auditorSha256`，证明报告由哪一版工具产出）

| 产物 | SHA256 | 字节 |
| --- | --- | --- |
| `g0/input-manifest.json` | `A02411E6AE18B427018D1BC1751E6C06675C52BAF702A00335517343AF242F28` | 107241 |
| `g0/requirements.json` | `8D46EA54CF19225F90C6DC940489F53AB90DAADED8DF55EDA63CF8FDF3831EB2` | 17495 |
| `g0/expected-evidence.json` | `644C0444E4BD57DE4DD104E75BCD65C317DDC28B94579D0821851AF30C15505B` | 22277 |
| `g0/expected-evidence-final.json` | `29B8E240F29FF2E01FA7CAACB5638F8036B6B01B75EEBBF983A5FB8040384EC0` | 23757 |
| `g0/historical-exceptions.json` | `C89A41335A39A05344F0665FCB13BAF7BE4C0A35876EE5CF2511A32B81BA0334` | 3738 |
| `g1/audit-history-raw.json`（exit 1，保留 RED） | `8D45DC68C8E10C8D885940CA63BAE68994BEE2B7D774899FFA9DE5AA6C2A5A0E` | 2538 |
| `g1/audit-history-bound.json`（exit 0，knownDrift=3） | `F810B3EF1E998052F69322768791F393738B5C20124A25A65ECC882898543AD8` | 4440 |
| `g1/audit-final-preflight.json`（exit 1，诚实预检） | `F0B9A0EFE9141A1FE41CC64F93B1CDCFBFADAB02B92C70945B47489E7DC7C2BF` | 5325 |
| `g1/red-old-auditor-coverage.json`（5/5 RED 复现） | `F2061B5DFCF07D27C1A15BCB08B40608A9C5155138608C607D172798114DEF43` | 5016 |
| `g1/audit-goal-evidence.test.log`（26 tests OK） | `01A1054E26802AC8AE3E1E7FEF59BD819DC75580B48D1101A39D4FBC0D2064CF` | 7482 |
| `g1/command-results.json`（全部真实 exit code） | `F5F119C5C2B8FE80F2C47E02A4C1CF5179A3727C10F07A2D26F2418AB55C5D69` | 3259 |
| `g2/node-goal-core-flow-RED.log` | `4F3FB7D2DB9D930CAC8C27312A5818AAE3FB2DF10B70F0F7BF1DE67AD97AEDEB` | 5804 |
| `g2/node-goal-core-flow.log`（5/5 GREEN） | `0F07F6E823513C492D0251AA133CC86CC3F1066C1D021B80C0F9E1FF5FB12C08` | 1018 |
| `g2/node-all-tests.log`（243/243，0 skip） | `A86B2A5C4BDB4D8FA954E688BB3A6E2F838799B2348A6D4D268D3BC08705405B` | 38442 |
| `g2/source-drift-after-g2.json`（范围外漂移 0） | `1391F6FC29FA14E2E73C4E476D9C569294BF964FDA69ED0E58BB7F1D3386AE22` | 2056 |
| `scripts/audit-goal-evidence.py` | `F4121D33933F22C3A32BB556F36FF8F7E83A622C6CAEFF594533A2BE2673099E` | 29149 |
| `scripts/tests/audit-goal-evidence.test.py` | `85AC69812F1F55FDF3D27CC476C133DDDFF0B4DB2F9D869CC25B8D0241585E36` | 22259 |
| `scripts/capture-goal-baseline.ps1` | `D708DE1EB777E42EEA85C1676FCDC2A2ECA8E2CE1D26D68283D7C95C8615D92E` | 13188 |
| `ui/tests/goal-core-flow.test.cjs` | `534579874EC70C1C6A99672309C0A1BE21E1B5F538E0C05CD0485FB1AAE306F2` | 23508 |
| `ui/js/launcher.js` | `06620D4CB3B601CAE170EA8BEC5FBD4B6D7F7BC92433C1E9C8A188FE8BC3A94E` | 202354 |
| `ui/js/i18n.js` | `8EC48352F488F50D790B1573A420AC17707E5C53C2A836AB1D519BA35BC02AFF` | 60050 |
| `ui/css/figma.css` | `7DD6B77DCE06920A58B89CE57DB7FCA6A33C0E64404B6117A7C78CDDDEA62F2E` | 50949 |

git 完整性（本轮结束时）：`HEAD=f145d14eb26b0b7459a28491e764ac8c573085d3`（未变）、`git stash list` 为空、无 commit/push/stage/reset/clean/restore/checkout；工作区变化仅为本目标白名单对象 +1 个新增测试入口。

---

**本轮终止状态：** `ImplementationReady=false`（G2/G3/G4 未完成）· `IndependentAcceptance=NOT_GRANTED` · `FormalPhaseApproval=NOT_GRANTED` · `ReleaseApproval=NOT_GRANTED`。
本机闭环尚未满足；下一轮继续，最终停在 **具备独立复验条件，等待 Codex 独立验收**。
