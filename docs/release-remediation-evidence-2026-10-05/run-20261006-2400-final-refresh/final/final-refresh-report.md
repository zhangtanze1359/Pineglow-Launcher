# 全量验证链**复跑**：GREEN / 0 失败，且发布产物**逐字节一致**（第 73 轮）

日期：2026-10-06（Asia/Hong_Kong）。本轮**未改动任何 tracked 文件**。
动机：本会话已经跑了 ~40 轮探针与真机测试，需要一次"**最终门槛复跑**"来证明候选**没有被这些测试碰坏**，并且**仍然可复现**。

## 1. 结果：`模式 GREEN / 失败项 0`

| 步骤 | 结果 |
| --- | --- |
| step0 步骤运行器自检 | **PASS** |
| step1a/1b/1c UI 资源闭包 + 闭包自检 + 测试清单 | **PASS**（清单双向一致：node 测试 23/23）|
| **step1 Node 正式测试** | **PASS** — `pass 304 / fail 0`（27.2 秒）|
| step2/3 锁定还原（launcher / tests）| **PASS**（`--locked-mode`，硬失败）|
| step6 Release build | **PASS** |
| **step7 C# 正式测试** | **PASS** — `失败 0 / 通过 903 / 总计 903`（72.2 秒）|
| step8 VerifyHost build + step8b 二分冒烟 | **PASS**，`[BisectSmoke] PASS` |
| step10 win-x64 单文件 publish | **PASS** |
| **step11 独立冷启动（R01 那道门槛）** | **PASS** — **19 项全通过**（窗口 `松光启动器`、`/api/health` 就绪、GUI 真起来了）|

**发布产物逐字节一致**（本轮重新 publish 出来的）：
```
…\publish\Pineglow.Launcher.exe  53052722 bytes
sha256=5009cf73d0801e83ed68e23829486b2d27d1a313e0db67b5acbdc17bf169383c
```
⇒ 与先前 11 轮验证的候选**完全相同**。**"冻结候选没被 40 轮测试碰坏、且可复现"** 这条现在是本轮直接证过的。

## 2. 唯一的报错：可选清理步骤（**不是**验证失败）

`-CleanupSnapshot` 在删隔离快照时撞上 `VerifyHost` 目录被占用：
```
Remove-Item : Cannot remove item …\pineglow-snapshot-…\dev\PineglowLauncher\VerifyHost:
The process cannot access the file … because it is being used by another process.
```
成因：`VBCSCompiler`（Roslyn 编译服务）仍持有该目录。**验证判定不受影响**（该步骤在 7/7 判定之后执行）。

## 3. 顺带发现的磁盘卫生问题：**12.15 GB 残留快照**

`%TEMP%` 下积了 **15 个** `pineglow-snapshot-*` 目录（历史各次全量跑留下的：6×1394 MB、1548 MB、1105 MB、581 MB、551 MB 等）。
杀掉 `VBCSCompiler` 后全部清理成功，**共释放 12,151 MB**：C: 可用 **105.9 GB → 117.1 GB**。
（建议：`verify-source-snapshot.ps1` 在退出前**先停编译服务再清理**，或在下次启动时清掉陈旧的 `pineglow-snapshot-*`。）

## 4. 已知边界（本轮 console 里同样被如实记录）

`[已知边界·未通过] publish win-x64 (锁定还原探测)`：`packages.lock.json` 未覆盖 win-x64 RID 且缺 ILLink 任务包（实测 NU1004）
⇒ **RID 发布不在锁定还原范围内**（与本会话早先的结论一致；发布流程靠 `release.yml` 的独立断言兜）。

## 5. 复现

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\verify-source-snapshot.ps1 `
  -RepoRoot D:\松光启动器 -EvidenceDir <证据>\final-refresh\final -CleanupSnapshot
# 期望：模式 GREEN、失败项 0、单文件产物 sha256=5009cf73…
```

产物：`run-console.log`（逐步判定）、`snapshot-manifest.sha256.txt`（146 行候选清单）、
`step1-node-tests.log`、`step7-dotnet-test-release.log`、`step8b-verifyhost-bisect-smoke.log`、
`step10-publish.log`、`step11-standalone-cold-start.log`、`standalone-cold-start.json`。
