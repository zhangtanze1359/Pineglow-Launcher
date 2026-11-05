# M0-3 发布阻塞根因闭环

> 日期：2026-11-06 | 基线：`30f7b56e`（v1.0.3） | 状态：**根因已闭环，本地复测通过；runner 复测待触发**

## 一、原阻塞是否真实存在

| 期 | 现象 | 证据 |
|---|---|---|
| v1.0.1（`8845574f`） | 发布工作流 41586294011 Windows .NET 测试 953/957；`Step3_04/05/06` 等 fake handler 5 秒超时；`LocalDiagnose_EndToEndLatency` p95 7188.2ms | `docs/release-diagnosis-2026-11-03/发布阻断修复流程.md` |
| 诊断 | `JavaLocator.FindAsync` 实测 **124990.9ms**、7038.1ms、7105.0ms | 诊断分支 run 41596085291 的 `LocalDiagnose_StageBreakdown_IsMeasured` |

根因：`JavaLocator.EnumerateCandidates` 对 `Program Files` 与 `Program Files (x86)` 两个根做 `*jdk*` 与 `*jre*` 的 **`SearchOption.AllDirectories`** 递归（本地 `47e939c` 的 `JavaLocator.cs:246` 仍是此写法）。两个全盘递归、无深度上限、无数量上限。

## 二、修复是否已落地

**已落地，且已随 v1.0.3 发布。** 修复提交 `e89023d`（`fix: bound Java discovery and cancel version subprocesses`），位于远端 main。

修复后的实现（`30f7b56e` 的 `JavaLocator.cs:318-347`）：

```csharp
private static IEnumerable<string> BoundedDirectories(string root, CancellationToken cancellationToken)
{
    var pending = new Queue<(string Path, int Depth)>();
    pending.Enqueue((root, 0));
    int count = 0;
    var options = new EnumerationOptions { IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint };
    while (pending.Count > 0 && count < 4096) { ... if (current.Depth < 3) pending.Enqueue((path, current.Depth + 1)); ... }
}
```

| 修复项 | 具体做法 |
|---|---|
| 递归深度 | `current.Depth < 3` → 最多 4 层 |
| 目录数量 | 每个根最多 **4096** 个目录 |
| 跳转点 | `AttributesToSkip = ReparsePoint`，不跟随 junction/symlink |
| 不可访问 | `IgnoreInaccessible = true` |
| 版本进程 | 每个 2 秒上限（`timeout.CancelAfter(TimeSpan.FromSeconds(2))`），stdout/stderr 并发读，取消/超时清理进程树 |
| 诊断侧 | 配置了 Java 先验证；补充版本信息只给 **1 秒**预算（`LocalApiService.cs:1937-1944`），未取到就保留「信息缺失」状态 |
| 扫描策略 | `FindAsync` 改为**惰性取首个有效**（`FindFromCandidatesAsync`），不再等全部候选验证完；显式选择 Java 时仍走完整 `ScanAsync` |

## 三、本地实测（隔离分支 `remediation/v1.1.0`，Windows 10.0.26200 / .NET 10.0.301）

用仓库自带的 `LocalDiagnose_StageBreakdown_IsMeasured`（会打印 MR10 指标行）：

| 阶段 | p50 | p95 | 说明 |
|---|---|---|---|
| `diagnose.stage.modScan` | 4.627 ms | 38.983 ms | 枚举 60 个 Mod + 读 manifest |
| `diagnose.stage.ruleEngine` | 2.828 ms | 6.184 ms | 读 1.2 MB 日志尾 + 规则引擎 |
| `diagnose.stage.sanitize` | 25.806 ms | 59.633 ms | 长日志脱敏 + 证据挑选 |
| `diagnose.stage.javaFind` | **123.034 ms** | **149.298 ms** | `JavaLocator.FindAsync`，3 次采样 149.3 / 120.2 / 123.0 ms |

`JavaDiscoveryTimingTests` 分离测量：

| 项 | 实测 |
|---|---|
| 候选枚举（`enumeration_ms`） | **321.3 ms**，候选数 3 |
| 单候选版本验证 | 129.3 / 120.5 / 106.7 ms，3 个全部 `valid=True` |

对照修复前：**124990.9 ms → 321.3 ms**，改善约 **389 倍**。Java 查找不再是诊断的主要成本（脱敏 25.8ms 才是本地模式的最大项）。

## 四、测试门槛复核

| 门槛 | 结果 | 备注 |
|---|---|---|
| Java 与诊断聚焦子集 | **109 / 109 通过**（22.9s） | 含 `SlowVersionProcess_IsBounded_AndHasExitedOnReturn`（原 15s 超时失败，现通过）、`BothOutputPipes_AreDrainedConcurrently`、`FallbackSearch_FindsNormalInstallation_WithoutDescendingUnrelatedDeepTrees` |
| Windows 全量 .NET | **967 / 967 通过**，0 跳过（1m15s） | 与远端 `docs/release-fix-2026-11-03/validation.md` 记载的 967 一致 |
| 前端清单 | 374 项，371 通过 / 3 失败 | 3 个失败已在 M0-1 报告定位（夹具缺口，非产品缺陷） |

### 一个必须记录的本地环境陷阱

第一次跑全量时 `UpdateApplyScriptPathTests.ApplyScript_WaitsForRunningTargetThenReplaces` 失败（`replace_failed`，waited=0.5s）。排查结论：**不是产品缺陷，是 PATH 顺序**。

生成脚本用 `tasklist /FI "IMAGENAME eq ..." | find /I "..."`。在 Git Bash 环境下 `PATH` 把 `/usr/bin` 排在前面，`find` 解析到 **GNU find** 而非 `C:\Windows\System32\find.exe`，于是 `tasklist` 的参数被 GNU find 当作路径，返回非零、`goto ready` 提前跳出等待循环，进程仍占用目标文件，替换失败。

把 `C:\Windows\System32` 放到 PATH 最前重跑：**967/967 全通过**，该用例单跑 6 秒通过（正确等待进程退出）。

**这不是产品 bug**，但如果 CI 用 Git Bash 跑测试会踩到同样问题——**GitHub Actions 的 Windows runner 默认 shell 是 pwsh，不会踩**。记录在此以免后人误判为回归。

## 五、禁止项自查

| 禁止项 | 是否违反 |
|---|---|
| 调高超时阈值 | **否**。2 秒版本检测上限是**新增**约束（原来没有上限），fake handler 的 5 秒阈值未动 |
| 删除测试 | **否**。967 → 967，测试数不变 |
| 放宽工作流 | **否**。`build.yml` / `release.yml` 未改动 |
| 改写已公开的 v1.0.1 tag | **否**。v1.0.1/v1.0.2 tag 未动，新发布用 v1.0.3 |

## 六、未完成：runner 复测

`docs/release-diagnosis-2026-11-03/发布阻断修复流程.md` 第 4 步要求「在 Windows GitHub runner 上复测阶段耗时与完整发布门槛」。

**当前状态**：`main` 上最近三次 Build 都是 **failure**，但失败原因是 **Ubuntu 的 dotnet test**（见 `m0-4-ubuntu-test-diagnosis-2026-11-06.md`），**Windows 与 macOS 作业均为 success**。

而 `Release v1.0.3` 工作流（run 41600153222）**全 17 步 success**，包含：
- `Run C# tests` success
- `Standalone cold start (single-file EXE)` success
- `Live update-chain check (default sources)` success
- `Validate update manifest against final artifact` success
- `Create GitHub Release` success

所以：**发布门槛本身已在 Windows runner 上实跑并通过**（v1.0.3 就是这样发出去的）。`main` 的红色是 Ubuntu 作业，不阻断 Windows 发布门槛。

**但这不等于 CI 全绿**，如实记为：`main` branch 当前状态为 failing（Ubuntu only），需 M0-4 处理。
