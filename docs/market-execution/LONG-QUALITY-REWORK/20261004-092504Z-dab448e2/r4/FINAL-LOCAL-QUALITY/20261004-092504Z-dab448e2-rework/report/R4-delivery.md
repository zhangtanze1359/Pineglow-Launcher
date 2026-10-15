# R4 — 终态、封存与交付

## 1. 终态采集

在所有产品写入完成后执行：

```
powershell.exe -NoProfile -ExecutionPolicy Bypass -File <src>\scripts\capture-goal-baseline.ps1 \
  -Stage terminal -SourceRoot "D:\松光启动器" -EvidenceRoot <ev> -OutSubdir "r4\terminal"
```

- HEAD `f145d14eb26b0b7459a28491e764ac8c573085d3`，分支 `main`，工作区仍为 dirty（未提交、未清理、未回退）
- 292 项输入、缺失 0
- 与 R3 基线（`source/r3-input-manifest.json`）逐项比较：**漂移 0 项**（`verdict/changed-files.json`）

## 2. 本轮实际改动

`verdict/changed-files.json`：R0 基线 → R3 基线，**变化 6 项、新增 0 项、删除 0 项**。

| 文件 | 改动性质 |
| --- | --- |
| `ui/js/launcher.js` | `handleImportFiles` 取消终态（LQ-01） |
| `ui/js/i18n.js` | 新增 `import.outcomeUnconfirmed`（zh+en） |
| `ui/tests/goal-core-flow.test.cjs` | IMPORT-03/04 终态断言 + 3 条 INDEPENDENT 反证 |
| `ui/tests/import-cancel.test.cjs` | i18n 键清单 + 静态扫描（意图不得决定终态） |
| `scripts/run-goal-validation.ps1` | LQ-02..06 契约 + 退出码/裸命令名/stderr 计数修复 |
| `scripts/tests/goal-validation.test.cjs` | 10 原有加强 + 18 新增契约用例 |

`D:\松光启动器` 里其余 dirty 项在本轮之前就存在，**不声称为本轮成果**；本轮未新增任何源码侧文件，
因此输入集合数量保持 292，`scripts/source-snapshot-inputs.txt` 无需追加。

## 3. 机器 verdict

`verdict/machine-verdict.json`：

- `gates`：R0 基线/真实 RED、R1 导入终态、R2 运行器契约、R3 GUID 副本回归、R4 终态封存 —— 全部 `PASS`
- `lq`：LQ-01..LQ-06 全部 `CLOSED`
- `implementationReady = true`
- `formalPhaseApproval = NOT_GRANTED`，`releaseApproval = NOT_GRANTED`，
  `technicalAcceptance = AWAITING_INDEPENDENT_REVIEW`
- 结尾固定：**具备独立复验条件，等待 Codex 独立验收**

`verdict/counts.json` 汇总全部计数与绑定 SHA：已验证输入 292、
C# 73/73/0 skip、Node 269、运行器 28、审计器 26、Bisect 19、场景 28/28、
独立运行器反证 7/7 被拒、独立导入反证 3/3、恢复演示 DEMO_PASS。

## 4. 封存

包内内容**全部写完**之后才封存：

1. `manifest.json` 列出包内**每一个**文件的相对 POSIX 路径、字节数与 SHA256
2. 计算 manifest 自身的 SHA256，写入**包外**分离摘要文件 `<runId>.manifest.sha256`
3. **包外**审计 `out-of-package-audit.json`：逐项核对存在/字节数/SHA、检查未登记文件、核对分离摘要
4. 包外**再次逐项复验** `post-seal-reverify.json`

封存后包内**不再有任何写入**。若封存失败，不补写旧包，改用新 runId。

> 分离摘要与两份包外报告位于包目录的**同级**（`r4/` 下），因此包内不包含也无法包含自身的 manifest SHA；
> 这是刻意设计，避免自引用。包内一切引用都使用**包内相对路径**，不依赖可被覆盖的绝对工作目录文件；
> 需要指向包外原件时，包内保留了只读副本并记录其 SHA。

## 5. 逐项 RED/GREEN、命令、计数、SHA

- LQ 逐条关闭台账：`report/LQ-01..LQ-06-closure.md`
- R0 RED：`report/R0-baseline-and-red.md` + `independent/r0-red/`
- R1：`report/R1-import-terminal.md`
- R2：`report/R2-runner-contract.md`
- R3 回归/演示/Resume：`report/R3-regression.md` + `runner/` + `attempts/` + `verdict/recovery-demo.json`

## 6. 停止点

不提交、不推送、不发布、不扩张功能、不自动派发下一目标。
`FormalPhaseApproval` 与 `ReleaseApproval` 始终为 `NOT_GRANTED`。
