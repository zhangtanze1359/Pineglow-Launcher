# DSH 对 Codex 复验结论的独立复核（2026-10-03）

目的：**不采信转述**，用自己的工具链、自己的 GUID 隔离副本，独立重跑 Codex 在
`deepseek-harness-remediation-reacceptance-2026-10-03.md` 里给出的边界探针与审计器否证。

结论：**Codex 的复验结论成立，4 项阻断（RA-01～RA-04）全部独立复现。**

## 一、边界探针（Codex 的 `RemediationBoundaryProbes.cs` 原样使用）

- 副本：`C:\Users\Administrator\AppData\Local\Temp\pineglow-dsh-repro-6b1f0c9e2a4d47b8a3e5c70d91f4b2a6`
  （由 `make-snapshot.ps1` 从 `D:\松光启动器` 构建，282 项输入含该探针；未改造生产实现）
- 命令：`dotnet test … --filter FullyQualifiedName~RemediationBoundaryProbes`
- 结果：**失败 4 / 通过 1，exit 1**（`dsh-repro-boundary.log`、`test-results/`）

| 探针 | 我的实测 | 对应 |
|---|---|---|
| `ModReplaceUndo_MustRejectJunctionAboveRecordedModsRoot` | `restoreHTTP=200`、`ok=true`、`status=restored`、`outsideFileExists=True` | RA-01 |
| `StoredPath_MustBelongToSameInstanceAndOperation`（服务层） | `status=restored`、`target=other-instance-B`、`BStoredExists=False`、`AStoredExists=True` | RA-02 |
| `HttpForgedStoredPath_MustNotConsumeOtherInstanceFile`（HTTP） | `HTTP=200`、`ok=true`、`BStoredExists=False`、`target=http-other-instance-B` | RA-02 |
| `RollbackFailure_HttpMustExposeRecoveryFields` | `HTTP=400`，响应体只有 `{error:"…needsRecovery=true…"}`（字段在中文散文里，不是 JSON 字段） | RA-03 |
| `SecretBackend_ConcurrentAsyncFlowsStayIsolated` | **通过**（两条重叠异步流互不串后端，native delta=0） | 已关闭项复核 |

## 二、证据审计器否证（对我上一轮的 `audit-all-manifests.py`）

对**合成**证据树运行我自己的审计脚本（历史文件只读，未改写任何真实证据）：

| 合成输入 | 实测 | 应有 |
|---|---|---|
| `A-empty-root`：存在但没有 manifest 的根 | `manifestCount=0, failures=0, exit=0` | 非零（没有覆盖目标清单） |
| `B-bad-detached`：内容正确但 `manifest.sha256` 为 64 个 0 | `failures=0, exit=0` | 非零（detached SHA 不一致） |
| `C-known-third-bytes`：已知路径换成**未登记**的第三种字节 | `knownMismatches=1, failures=0, exit=0` | 非零（豁免只按路径、没绑定已登记 SHA/size） |

结果 JSON 见 `auditor-A-empty-root.json`、`auditor-B-bad-detached.json`、`auditor-C-known-third-bytes.json`；
合成根路径记于 `auditor-fixture-root.txt`（位于 TEMP）。三项与 Codex 报告完全一致。

## 三、源目录保护

- `D:\松光启动器`：HEAD 未变 `f145d14…`；工作区状态行 **1363**；与前轮 281 项基线比对 **275 同 / 6 变**（就是上轮获准的 6 个文件）。
- 本次**没有修改 D 盘任何文件**，只新增本目录（复核证据）与 TEMP 隔离副本。

## 四、复核结论

上一轮我交付的 R1（F-01）只把校验锚定在**记录里的根**（`mod-replace` 时是 `game\mods`），
没有锚定**可信实例根与完整祖先链**；R4 的审计器豁免只按路径、且不校验 detached SHA。
两项都是真实缺口，不是"验收误判"。返修范围见 Codex 的
`deepseek-harness-remediation-rework-workflow-2026-10-03.md`（S1–S4）。
