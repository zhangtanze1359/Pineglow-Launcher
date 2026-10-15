# 打过 **6 个补丁**的候选：全量门槛的**每一步都 PASS**（2 项"失败"经查是我手工副本的产物）（第 80 轮）

日期：2026-10-06（Asia/Hong_Kong）。本轮**未改动工作区一个字节**。
动机：回答"如果批准这些修复，**全量门槛还过得去吗**？"——直接在**打过 6 个补丁的构建**上跑一遍 `verify-source-snapshot.ps1`。

## 1. 结果：11 个可执行步骤**全部 PASS**

| 步骤 | 结果 |
| --- | --- |
| step0 步骤运行器自检 | **PASS** |
| step1a/1b/1c UI 闭包 + 闭包自检 + 清单双向一致 | **PASS** |
| **step1 Node 正式测试** | **PASS — `pass 304 / fail 0`** |
| step2/3 锁定还原（launcher / tests）| **PASS** |
| step6 Release build | **PASS** |
| **step7 C# 正式测试** | **PASS — `失败 0 / 通过 904 / 总计 904`**（903 原有 + **新增回归测试**）|
| step8 VerifyHost build + step8b 二分冒烟 | **PASS**，`[BisectSmoke] PASS` |
| step10 win-x64 单文件 publish | **PASS** |
| **step11 独立冷启动** | **PASS — 19 项全通过**（窗口 `松光启动器`、`/api/health` 就绪）|

⇒ **补丁没有破坏门槛的任何一步**，而且新增的回归测试确实被计入（904）。

## 2. 汇总里的"失败项: 2"是什么（**都是我这个手工副本的产物，不是补丁问题**）

| 报告项 | 真相 |
| --- | --- |
| `[FAIL] 存在含非 ASCII 但无 UTF-8 BOM 的 PowerShell 脚本 1 个：scripts\run-dify-minimax-workflow.ps1` | 这个脚本在**工作区里是未跟踪的历史遗留**（决定项 6 那一类）。为了让验证器接受副本，我做了 `git add -A`，**把工作区的未跟踪杂项也一起纳入了** ⇒ 它才出现在快照里。**工作区正式跑（第 73 轮）里没有这条 FAIL**。|
| `快照文件数量不符: 期望 512 实际 363` | 期望值 = 副本自己的 tracked（366）+ 白名单（146）= 512；实际 363 —— 因为**副本不是真仓库**：我复制时排除了 `docs/`，又带进了若干未跟踪杂项 ⇒ 文件集合与真仓库不同。**与补丁无关**。|

**判据**：同一脚本在**工作区**上跑（第 73 轮）是 `模式 GREEN / 失败项 0`；本轮所有"步骤级"判定也全是 PASS ⇒ **这 2 项是我构造副本的方式造成的**。

## 3. 这一轮顺带确认的三件事

1. **6 个补丁一起上也编译得过**（0 警告 0 错误）并且 **C# 904/904**（含新回归测试）。
2. **门槛的可执行部分全绿**（Node 304、锁定还原、publish、冷启动 19/19）。
3. 工作区里确实存在**未跟踪的历史遗留**（例如 `scripts/run-dify-minimax-workflow.ps1`：含非 ASCII 且**无 BOM**，PS 5.1 会解析错乱）
   —— 这正是决定项 6"是否清理工作区历史遗留"的实例；**它不在候选白名单里**，所以不影响候选本身。

## 4. 复现

```powershell
# 前提：%TEMP%\pg-patched 是"复制仓库 + 应用 6 个补丁 + 构建"的副本（见第 74 轮脚本）
cd %TEMP%\pg-patched
git add -A; git -c user.email=x@y -c user.name=x commit -q -m "6-patch candidate"
powershell -File D:\松光启动器\scripts\verify-source-snapshot.ps1 `
  -RepoRoot %TEMP%\pg-patched -EvidenceDir <证据>\final -CleanupSnapshot
# 期望：11 步全 PASS、C# 904/904、冷启动 19/19；汇总里会有 2 项"副本产物"式的失败（见 §2）
```

产物：`run-console.log`（逐步判定 + 两条失败的原文）、`snapshot-manifest.sha256.txt`、`step1-node-tests.log`、`step7-dotnet-test-release.log`、
`step8b-verifyhost-bisect-smoke.log`、`step10-publish.log`、`step11-standalone-cold-start.log`、`standalone-cold-start.json`。
