# 第 23 轮：C 从「方案」变成**已验证的补丁**（而且发布产物逐字节不变）

运行标识：`run-2026-10-10_015215-578dbafb`（第 23 轮追加）
时间：2026-10-11 16:55–17:30，Asia/Hong_Kong（+08:00）
本轮性质：**产品目录零改动**（0 staged）。全部在 `%TEMP%` 隔离副本里完成。
补丁：`round23-c-rid-lock/c-rid-lock.patch`（44,992 字节，8 个 hunk）——**未应用**。

---

## 1. 为什么要做

C 是九个缺陷里**唯一被标为「发布阻断」**的那个：正式发布的 `win-x64` 恰好是唯一没有依赖锁定的路径。
它的方案在第 10/15 轮验证过**机制**，但用户还得先决定范围、再有人去写。
本轮把选项 2（三个 RID 全锁）**直接做成可应用的补丁并验证**——这样剩下的只有「要不要应用」。

---

## 2. 补丁内容

| # | 改动 | 说明 |
| --- | --- | --- |
| 1–3 | **新增** `dev/PineglowLauncher/packages.win-x64.lock.json`、`packages.linux-x64.lock.json`、`packages.osx-x64.lock.json` | 由 `dotnet restore -r <rid> -p:PublishSingleFile=true -p:NuGetLockFilePath=packages.<rid>.lock.json --force-evaluate` 生成 |
| 4 | `.github/workflows/release.yml` 的发布行追加 `/p:RestoreLockedMode=true /p:NuGetLockFilePath=packages.win-x64.lock.json` | 正式发布的那个 RID |
| 5 | `.github/workflows/build.yml` 的矩阵发布行追加 `/p:RestoreLockedMode=true /p:NuGetLockFilePath=packages.${{ matrix.rid }}.lock.json` | 三个 RID 全覆盖 |
| 6 | `build.yml` 的守卫**新增反向断言** | 每个 RID 的 lock 必须**存在**、且**确实含 `"net10.0/<rid>"` 段**——否则「缺 lock」会退化成「看起来锁定了」 |
| 7–8 | `WorkflowInvariantTests.cs` 两条不变量**改写**（见第 4 节） | 它们原本是用来**捍卫旧取舍**的，采用 C 就必须显式推翻并换成新要求 |

**共享的 `packages.lock.json` 一个字都没动。**

---

## 3. 验证结果（全部实测）

| 检查 | 结果 |
| --- | --- |
| 基线测试（改动前） | **941 通过 / 0 失败** |
| 生成三个 RID lock | 三个都 `exit=0` |
| `dotnet build -c Release` | **exit 0，0 警告 0 错误** |
| 全量测试（改动后） | **941 通过 / 0 失败** |
| 共享 `packages.lock.json` | **改动前后哈希一致** `FBBC9AE67B8E8CF2E3AA63C48242BC08AC713BE47830D337914CC39C85EE0672` |

### 3.1 三个 lock 文件的形态

| 文件 | 字节 | 段 | 含 ILLink |
| --- | --- | --- | --- |
| `packages.lock.json`（共享，**未变**） | 8,663 | 仅 `net10.0` | 否 |
| `packages.win-x64.lock.json` | 10,868 | `net10.0` + `net10.0/win-x64` | 是 |
| `packages.linux-x64.lock.json` | 10,870 | `net10.0` + `net10.0/linux-x64` | 是 |
| `packages.osx-x64.lock.json` | 10,868 | `net10.0` + `net10.0/osx-x64` | 是 |

### 3.2 最关键的一条：**锁定发布会产出同一个二进制**

用 C 之后 release.yml 的完整发布命令（带 `RestoreLockedMode` 与 RID lock）在隔离副本里实跑：

```
lockedPublishExit  = 0
产物 SHA-256       = CA8F94C1A6173F23F38F3D2A3C949AFFC16D5AB67752B662C7806831F350D93E
产物字节数          = 53,052,722
与已验收产物逐字节相同 = True
发布后共享 lock      = FBBC9AE67B8E8CF2…（仍未变）
```

**也就是说：C 补上的是「依赖图被锁定」这个保证，而不是换一个产物。**
这一点很重要——它意味着采用 C **不需要重跑**之前全部的功能验收（安装、启动、四种加载器、界面、续传……），因为被验的就是同一个二进制。

---

## 4. 那两条不变量为什么要改写（不是"测试碍事"）

`WorkflowInvariantTests.cs` 把**旧取舍**写成了硬不变量，第 20 轮已实测：把 C 的改动加上去，这组用例 **13 项里 2 项变红**。本轮把它们改成**新取舍下的更精确要求**：

| 旧不变量 | 新不变量 |
| --- | --- |
| `RidPublish_MustNotUseRestoreLockedMode`：「RID 发布**不得**使用 `RestoreLockedMode`（实测必然 NU1004）」 | `RidPublish_MustUseRestoreLockedModeWithItsOwnLockFile`：「RID 发布**必须**带 `RestoreLockedMode`，**并且必须**用 `-p:NuGetLockFilePath` 指向**自己的** RID lock」 |
| `Workflows_DoNotReferenceRidSpecificLockFiles`：「工作流**不得**引用 `packages.<rid>.lock.json`（本项目只有一份 lock）」 | `Workflows_OnlyReferenceRidLockFilesThatExist`：「引用了某个 RID lock，就**必须**真的存在那份文件」（另保留：不得引用 win-arm64） |

为什么是"改写"而不是"删掉"：旧禁令的**根据**是「只有一份 lock，RID 锁定会破坏非 RID 的 `--locked-mode`」。
改成每 RID 一份 lock 之后，那个根据**消失了**，但**新的风险出现了**——「指向一个不存在的 lock」会让锁定静默退化。
所以新不变量守的是**新风险**，而不是简单地放行。
（`BuildLockFileTests.cs` **不需要改**：它只看共享的那两份 lock，而它们形态不变——这一点与第 10 轮方案里的说法不同，本轮核实后更正。）

---

## 5. 如实标注的限制

1. **lock 会过期**：依赖或发布属性变化后必须用 `--force-evaluate` 重新生成并提交，否则 CI 会红。这是"锁定"的本意，但发布流程说明里要写明再生成命令。
2. **RID lock 属性敏感**：用 `-p:PublishSingleFile=true` 生成的 lock，不能用于不带该属性的 RID 还原。所以**锁定还原与发布必须传同一组 `-p:`**。本补丁的两处命令都带了 `PublishSingleFile=true`。
3. **没有跑远程 CI**：本轮是本地隔离副本验证（含真实的 `dotnet publish`）。同 SHA 的远程 CI 属 F 范围，仍需要授权与推送。
4. **另外两个 RID 只验到"能锁定、能发布"**：`linux-x64` / `osx-x64` 的运行时行为仍属未验证（README 也已声明不提供正式跨平台支持）。

---

## 6. 结果

**C 不再是"待决策 + 待实施"，而是"待决策"**：三个选项现在各有代价——

- **选项 2（本补丁）**：应用即可。代价：多两个 lock 文件进仓库，依赖变化时要重新生成。
- **选项 1**：本补丁的子集（只保留 win-x64 那份 lock，`build.yml` 的矩阵行需要加条件判断——反而更绕）。
- **选项 3**：不改，接受「发布产物无锁定保证」并记录为风险。

状态串：`TechnicalAcceptance=NOT_PASSED`（发布整体）；`FormalPhaseApproval=NOT_GRANTED`；`ReleaseApproval=NOT_GRANTED`。
