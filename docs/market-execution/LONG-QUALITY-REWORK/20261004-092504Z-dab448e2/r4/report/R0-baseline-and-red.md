# R0 — 现场冻结与真实 RED（LONG-QUALITY-REWORK）

本文件描述 R0 阶段的输入、环境、命令与**在未修改产品源码上**取得的真实 RED 证据。
所有路径均以本证据根为基准的相对路径；绝对工作路径只在“原始位置”一节列出，且不作为交付依赖。

## 1. 现场

| 项 | 值 |
| --- | --- |
| SourceRoot | `D:\松光启动器`（保持 dirty，未回退/未清理/未提交） |
| HEAD | `f145d14eb26b0b7459a28491e764ac8c573085d3` |
| 分支 | `main` |
| 输入集合 | 292（tracked 161 / whitelist 125 / goal-tooling 6），缺失 0 |
| 证据根 | `docs\market-execution\LONG-QUALITY-REWORK\20261004-092504Z-dab448e2` |
| R0 执行副本 | `%TEMP%\dsr-rework-r0-685bb8d5`（全新 GUID 路径，292/292 复制后逐项核 SHA，0 不一致） |

R0 基线封存于 `source/r0-input-manifest.json`，副本核对结果封存于 `source/copy-verification.json`。

## 2. 环境

| 工具 | 版本 / 结果 |
| --- | --- |
| Windows PowerShell | 5.1.26100.9549 |
| Node.js | v24.19.0 |
| .NET SDK | 10.0.301 |
| Python | 3.12.10 |

**PSModulePath 校正：本轮不需要。** 从 Node 子进程调用 `powershell.exe` 执行 `Get-FileHash`，
在“继承父进程 PSModulePath”和“显式指定 5.1 模块目录”两种情况下都返回 `OK` + 正确 SHA。
因此本轮没有修改机器全局配置，也没有把上一轮的环境故障当成本轮产品缺陷。

## 3. 真实 RED（未修产品）

### 3.1 LQ-01 —— 导入取消终态

用 Codex 独立验收保留的反证测试（断言字符串未改动，仅把 `ROOT` 重新绑定到本轮 GUID 副本）：

```
node --test --test-reporter=tap --test-name-pattern INDEPENDENT \
  independent/r0-red/import-terminal-disproof.test.cjs
```

结果：**执行 3 项，1 项对照通过，2 项缺陷失败，exit 1**。
终态提示序列（原始日志）：

- `INDEPENDENT IMPORT-04`：`["取消失败：unknown-operation","导入已取消"]`
- `INDEPENDENT IMPORT-03`：`["已记录取消；若该导入随后才被受理，会自动中止。","导入已取消"]`

即：**点击取消的“意图”被当成了“服务端确认取消”**。日志：`independent/r0-red/import-terminal-disproof.log`。

### 3.2 LQ-02..LQ-06 —— 运行器契约

用 Codex 独立验收保留的 7 个反证夹具（期望字符串未改动）：

```
node independent/r0-red/runner-disproof.cjs
```

结果：**7 个 case 全部 exit 0 / verdict PASS**（`independent/r0-red/runner-disproof-results.json`）：

| case | 期望 | 实际（未修） |
| --- | --- | --- |
| actual-input-changed-manifest-unchanged | 拒绝输入漂移 | exit 0 / PASS / **reused=true** |
| stale-trx | 拒绝子命令未更新的旧 TRX | exit 0 / PASS |
| inconsistent-counters | 拒绝 0 执行/矛盾计数 | exit 0 / PASS |
| produces-list-changed | 必需产物变化后拒绝复用 | exit 0 / PASS / **reused=true** |
| resume-changed-manifest-same-run-id | 新 runId 且保留旧记录 | exit 0 / PASS / 旧记录**未**保留 |
| empty-stages | 拒绝零 stage | exit 0 / PASS / stageCount=0 |
| only-unknown | 拒绝未知 Only | exit 0 / PASS / stageCount=0 |

### 3.3 源码侧 RED（新增断言先失败）

把 3 条 INDEPENDENT 断言提取进源码测试，并把 IMPORT-03/04 的断言移到 `await running` **之后**，
在未修 `launcher.js` 上运行：

```
node --test --test-reporter=tap ui/tests/goal-core-flow.test.cjs
→ tests 31 / pass 27 / fail 4 / skipped 0, exit 1
```

失败的 4 项：`IMPORT-03`、`IMPORT-04`（加强后的终态断言）、`INDEPENDENT IMPORT-04`、`INDEPENDENT IMPORT-03`。
`INDEPENDENT IMPORT-01`（operationId 对照）通过，证明夹具本身可用。

## 4. R0 结论

六项缺陷全部在**未修改的产品源码**上复现，RED 不是人为制造的。R0 未修改任何产品代码。
