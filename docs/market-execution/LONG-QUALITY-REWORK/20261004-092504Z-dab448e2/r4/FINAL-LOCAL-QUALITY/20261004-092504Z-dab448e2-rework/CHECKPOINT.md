# LONG-QUALITY-REWORK 执行 checkpoint

证据根：`C:\Users\Administrator\.codex\worktrees\34ca\松光启动器\docs\market-execution\LONG-QUALITY-REWORK\20261004-092504Z-dab448e2`

## 固定边界（与返工提示词一致）

- SourceRoot = `D:\松光启动器`，保持 dirty，未 reset/restore/checkout/clean/stash/stage-all，未提交/推送/发布。
- PlanRoot = `C:\Users\Administrator\.codex\worktrees\34ca\松光启动器\docs`。
- 新证据根 = PlanRoot\`market-execution\LONG-QUALITY-REWORK\20261004-092504Z-dab448e2`。
- 旧 LONG-QUALITY-GOAL 最终包与 `docs\market-acceptance\20261004-long-quality-independent` **严格只读**。
  本轮从未在旧目录内重跑会写输出的脚本；所有复跑都在新证据目录的副本中进行。
- 构建与测试只在本轮 GUID 执行副本内进行，从未在 `D:\松光启动器` 内构建或写测试结果。
- 审计器 `scripts/audit-goal-evidence.py` 与 26 项测试、C# 服务与测试、`api.js`、CSS/index/设计资源、
  全部历史证据：**只读未改**。

## 本轮的三个 GUID 执行副本（都在 %TEMP%，全新路径）

| 副本 | 用途 | 结果 |
| --- | --- | --- |
| `dsr-rework-r0-685bb8d5` | R0 未修源码上的真实 RED | 292/292 复制核对，0 不一致 |
| `dsr-rework-r3-905e0b60` | 第一版 R3（**已被取代**） | 见下 |
| `dsr-rework-r3b-3c837208` | 第二版 R3（**已被取代**） | 见下 |
| `dsr-rework-r3c-10ed6d4c` | **最终 R3**，级联回归与全部结论来源 | 292/292，级联 PASS 10/10 |

## 为何存在被取代的副本（诚实记录）

1. `r3\g0` 之后，运行器又修了“裸命令名必须走 PATH 解析”。
2. `r3\g1` 之后，运行器又修了“子进程退出码捕获”（`Start-Process -PassThru` 的 `ExitCode` 丢失）
   以及“期望文本可来自 stderr”。
3. 每次源码变化后旧副本立即漂移。**运行器自己**以 `INPUT_SIZE_MISMATCH` 拒绝了漂移副本
   （清单文件字节未变），这正是 LQ-02 新增的行为。
4. 旧副本**没有被就地改写**：每次都新建基线与新 GUID 副本。历史见 `r3\BASELINE-HISTORY.md`。

## 独立仪器的两处接口级重绑（期望字符串未改动）

`r3\independent-reproof\` 中的两个仪器复制自 Codex 独立验收目录，只做重绑，不改断言：

1. `rebind-inputs.cjs` / `rebind-round.cjs`：把输入清单与 `ROOT` 重新绑定到本轮 GUID 副本，
   并从本轮 `input-manifest.json` 重建 `independent-inputs.json`（292 项，0 不一致）。
2. `runner-disproof.cjs` 的**合成子命令**改为写入 `{attemptDir}` 指向的本次 attempt 目录，
   且其合成 TRX 补上与自身计数器一致的结果行。
   原因：新契约要求“每个 attempt 独立目录”“计数器与结果行同一总体”，否则**对照调用**（第一次
   非 Resume 运行）会因与探针无关的原因失败，使“拒绝”不可判读。
   **所有 `expected` 字符串保持原样。**

## 本轮新增的证据侧脚本（全部位于证据根，不在源码树）

| 路径 | 用途 |
| --- | --- |
| `r0\red\rebind-inputs.cjs`、`rebind-root.cjs` | 把复制的仪器重绑到本轮副本并逐项核 SHA |
| `r3\independent-reproof\rebind-round.cjs` | 从本轮清单重建输入列表 + 合成子命令适配新接口 |
| `r3\build-scenario-matrix.cjs` | 生成 28 项场景矩阵，绑定测试文件 SHA 与原始 TAP 日志 SHA |
| `r3\demo\recovery-demo.cjs` | 失败→恢复、输入变更→新 runId 的机器可读演示 |
| `r4\collect-facts.cjs` | 汇总机器 verdict / counts / changed-files |
| `r4\build-package.cjs`、`r4\seal.cjs` | 构建交付包、manifest、分离摘要、包外审计与逐项复验 |

**没有新增任何源码侧脚本**，因此 `scripts/source-snapshot-inputs.txt` 无需追加，输入集合保持 292。

## 关键命令

```powershell
# R0 / R3 基线与 GUID 副本
capture-goal-baseline.ps1 -Stage baseline -SourceRoot "D:\松光启动器" -EvidenceRoot <ev> -OutSubdir <r0|r3\g2> -Materialize -ExecutionRoot <new GUID temp>

# 运行器级联（最终）
powershell.exe -NoProfile -ExecutionPolicy Bypass -File <exec>\scripts\run-goal-validation.ps1 -InputManifest <ev>\r3\g2\input-manifest.json -RunDir <ev>\r3\run-final -StagesFile <ev>\r3\goal-stages.json -ExecutionRoot <exec> -EvidenceRoot <ev>

# 真实 3 根覆盖审计（审计器只读）
python <exec>\scripts\audit-goal-evidence.py --expected <旧封存包>\...\g0\expected-evidence-final.json --out <ev>\r3\audit-real.json

# 终态采集
capture-goal-baseline.ps1 -Stage terminal -SourceRoot "D:\松光启动器" -EvidenceRoot <ev> -OutSubdir "r4\terminal"
```

## 范围与停止点

未扩张到真实更新安装、凭据、游戏、外部模型、受限恢复安全反证、CI/发布。
`FormalPhaseApproval` / `ReleaseApproval` 始终 `NOT_GRANTED`。
结尾固定：**具备独立复验条件，等待 Codex 独立验收**。
