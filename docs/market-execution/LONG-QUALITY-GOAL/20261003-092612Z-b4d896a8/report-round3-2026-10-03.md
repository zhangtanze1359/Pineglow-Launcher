# LONG-QUALITY-GOAL 执行报告 · 第 3 轮（2026-10-03）— G0–G4 全部完成并封存

**本轮结论：G4 DONE；G0–G4 全部完成；最终包已封存、包外审计通过、封存后复验通过。**
`ImplementationReady=true`（仅指本机闭环交付）· `IndependentAcceptance=NOT_GRANTED` · `FormalPhaseApproval=NOT_GRANTED` · `ReleaseApproval=NOT_GRANTED`。
终止语：**具备独立复验条件，等待 Codex 独立验收。**

## 1. 最终封存包

| 项 | 值 |
| --- | --- |
| 包路径 | `<EvidenceRoot>\FINAL-LOCAL-QUALITY\20261003-092612Z-b4d896a8` |
| 文件数 | **148**（manifest 登记数与实际数一致） |
| `manifest.json` SHA256 | `7B09EA7568BD114B9A8A1F13864DCAAF34CA81CDE777A55ACB2A94C8990E3B99` |
| detached `manifest.sha256` | 与 manifest 自身 SHA256 **一致** |
| 包内 `machine-verdict.json` | `implementationReady=true`，G0–G4=DONE，四行机械状态与终止语齐全 |
| 包内 `changed-files.json` | 本目标改动 4 项与**外部写者**改动 3 项**分开登记**，`gitWriteOperations=0` |

**封存顺序（F-R2-01 的修复被真正执行，而不是写在文档里）：**

组装证据 → **终态采集**（`seal/source-after-manifest.json`，292 项输入）→ machine-verdict / changed-files / final-report → `manifest.json` → `manifest.sha256` → **包外**严格审计 → **封存后逐文件复验**。manifest 写出后包内**零写入**。

## 2. 封存前：冻结 stage 集在全新 GUID 副本上运行

副本：`%TEMP%\pineglow-goal-seal-39dfd2b90cbb4689b7fbad1c8ab10ba9`（292 项输入，复制核对不一致 0）。

| stage | 真实 exit | 实测计数 |
| --- | --- | --- |
| g4-build-tests | 0 | 0 error |
| g4-secrets | 0 | 13/13，0 skip |
| g4-ordinary-r2 | 0 | 5/5，0 skip |
| g4-recovery-normal | 0 | 6/6，0 skip |
| g4-flows | 0 | 49/49，0 skip |
| g4-node-all | 0 | 266/266，0 skip |
| g4-verifyhost-build | 0 | 0 error |
| g4-bisect-smoke | 0 | 19 PASS / 0 FAIL |

第一次运行时 `g4-node-all` **真实失败**（exit 1：把目录当入口模块 → `MODULE_NOT_FOUND`，并触发 `COUNT_BELOW_MIN`、`FORBIDDEN_PATTERN`）。修正 stage 定义为 node 自身展开的通配符后，用 `-Resume` 重跑：**7 个 stage 被复用、只重跑失败的那一个**，最终 8/8 PASS——运行器的复用规则在真实 stage 集上得到实证，而不是只靠合成用例。

## 3. 包外审计与封存后复验

```powershell
python -X utf8 scripts/audit-goal-evidence.py --expected g0/expected-evidence-final.json `
    --out final-audit-outside-package-20261003-092612Z-b4d896a8.json      # exit 0
python -X utf8 post-seal-verify.py FINAL-LOCAL-QUALITY/<runId> post-seal-verify-final.json
python -X utf8 scripts/run-goal-validation.ps1 -StagesFile g4/goal-seal-stages.json -Resume   # exit 0
```

| 检查 | 结果 |
| --- | --- |
| 包外严格审计 | **exit 0**：3 个根、**62 个包**、**1022 条记录**全部核对，failures=0，`knownDrift=3`（三条历史漂移逐条列出，绝不静默通过） |
| 封存后复验（独立脚本） | **PASS**：登记 148 / 核对 148、problems=0、未登记文件 0、detached 一致、count 一致 |
| 运行器审计 stage | **exit 0**，runner verdict PASS |
| 封存后再写包内 | **0 次** |

## 4. 本轮踩到并修掉的一个真实工具缺陷（记录，不隐藏）

`seal-final-package.ps1` 最初在写完"终态采集"后**卡死**：宿主进程 100% CPU、工作集涨到 ~8.5GB，`changed-files.json` 永不落盘。分步标记把范围收敛到一行：

```powershell
$j = $changed | ConvertTo-Json -Depth 12      # Windows PowerShell 5.1：无限膨胀/卡死
```

最小复现（含中文、换行、数组、嵌套 PSCustomObject）在 0ms 内完成，说明触发条件比"形状"更隐蔽；我**没有继续深挖 5.1 内部实现**，而是把封存器的 JSON 写出改成**自带的确定性序列化器**（`Esc-JsonString` + `To-JsonText`，非 ASCII 一律 `\uXXXX` 转义），随后封存 **2.6 秒**完成，且每个封存 JSON 都用真正的 JSON 解析器重新解析验证（`manifest.json`、`machine-verdict.json`、`changed-files.json`、`seal-capture.json`、`seal/source-after-manifest.json`、`seal/toolchain.json` 全部 OK）。

## 5. 源保护与并发漂移

- 冻结 286 项输入：未变 279、本目标白名单内变化 4（`scripts/source-snapshot-inputs.txt`、`ui/js/launcher.js`、`ui/js/i18n.js`、`ui/css/figma.css`）、**外部写者变化 3**（`ui/index.html`、`.gitignore`、`docs/design-system.md`）、缺失 0。
- 外部变化全程**未回退、未冒领**，逐条写进 `changed-files.json` 与 `g3/concurrent-drift-ui-index-html.json`。
- 本目标 **0 次 git 写操作**：`HEAD=f145d14eb26b0b7459a28491e764ac8c573085d3` 未变、stash 空、无 commit/push/stage/reset/clean/restore/checkout。

## 6. 未运行 / 未验证（不因本目标完成而变成 PASS）

RA-01/RA-02 恢复安全反证 **NOT_RUN**（平台终止范围，未执行、未委派、未换执行者）；`ImportWithALiveTokenButCancelledMidway_StillCleansUp` NOT_RUN；真实 Avalonia/WebView2 GUI、Minecraft、Windows 原生凭据往返、真实自更新进程交接、跨卷/满盘、非 Windows、远程 CI：**UNVERIFIED**。

## 7. 交付物索引

- 封存包：`FINAL-LOCAL-QUALITY/20261003-092612Z-b4d896a8/`（`manifest.json` + `manifest.sha256` + `final-report.md` + `machine-verdict.json` + `changed-files.json` + `g0..g4` 全部证据）
- 包外验证：`final-audit-outside-package-20261003-092612Z-b4d896a8.json`、`post-seal-verification-20261003-092612Z-b4d896a8.json`、`post-seal-verify-final.json`
- 运行器与审计器：`scripts/run-goal-validation.ps1`、`scripts/audit-goal-evidence.py`、`scripts/capture-goal-baseline.ps1` + 各自测试
- 报告：`report-round1-2026-10-03.md`、`report-round2-2026-10-03.md`、本文件

**说明：** 本报告写于封存**之后**，因此它不在包内（包内已封存，任何后续写入都会破坏封存）。其 SHA256 记入 checkpoint。

---

**终止状态：** G0 DONE · G1 DONE · G2 DONE · G3 DONE · G4 DONE。
`ImplementationReady=true`（本机闭环交付） · `IndependentAcceptance=NOT_GRANTED` · `FormalPhaseApproval=NOT_GRANTED` · `ReleaseApproval=NOT_GRANTED`。
**具备独立复验条件，等待 Codex 独立验收。**
