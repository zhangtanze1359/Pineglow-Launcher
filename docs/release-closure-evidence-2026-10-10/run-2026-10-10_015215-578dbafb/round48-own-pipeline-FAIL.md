# 记录 48：**产品自己的候选流水线在十二补丁树上判定 FAIL** —— 未解决，留作下一轮首要项

运行标识：`run-2026-10-10_015215-578dbafb`
时间：2026-10-11 20:30–20:40，Asia/Hong_Kong（+08:00）
本轮性质：**只读 + 隔离副本，产品目录零改动**（0 staged）。

---

## 1. 做了什么

把 `verify-source-snapshot.ps1`（产品**自己的**候选流水线，13 步 + 发布 + 19 项冷启动）
跑在**十二条补丁**的源码树上。这是第 26 轮之后第一次重跑——中间又加了 6 个补丁。

（第一次跑失败是因为流水线要读 `git ls-files`，而 `%TEMP%` 副本不是 git 仓库；
给副本 `git init` + 提交后即可跑通——`ls-files` 312 条。）

## 2. 结果：8 步 PASS，1 步失败，**最终判定 FAIL**

| 步骤 | 状态 |
| --- | --- |
| step2 restore-launcher-locked | PASS |
| step3 restore-tests-locked | PASS |
| step6 build-launcher-release | PASS |
| **step7 dotnet-test-release** | **PASS**（`已通过! - 失败: 0，通过: 949`） |
| step8 build-verifyhost | PASS |
| step8b verifyhost-bisect-smoke | PASS |
| **step9 publish-locked-probe** | **exit=1  status=KNOWN-UNLOCKED** |
| step10 publish | PASS |
| step11 standalone-cold-start | PASS |
| **最终判定** | **FAIL** |

**唯一失败的是 step9**，其状态被标为 `KNOWN-UNLOCKED`。

## 3. 我的**未经验证**的假设（明确标注为假设）

step9 叫 `publish-locked-probe`，而 **C 补丁改的正是"发布是否锁定"这件事**
（`RestoreLockedMode` + RID lock + 工作流改造）。
所以**最可能**的解释是：**step9 编码的是 C 之前对"未锁定发布"的预期行为，C 应用后那个预期不再成立**。

**但我没有验证它。** 需要读 `verify-source-snapshot.ps1` 里 step9 的定义与最终判定逻辑，
才能确定这是"流水线需要跟着 C 更新"还是"C 引入了真实问题"。

**本会话已经连续三轮因为"用合理的推断补上没查的那一步"而出错**（记录 44/45/46），
所以这里**只写"未解决"，不写结论**。

## 4. 为什么这条很重要

在这之前，"十二条补丁一起应用"的结论建立在**我自己的**流水线（build + 测试 + 锁定发布 + 冷启动）上，
那些**全部通过**。而产品**自己的**门槛现在报 FAIL——**这两者不一致，必须查清**。

在查清之前，**报告里"全部补丁一起应用已验证"这句话应当降级**：它验证的是我选的步骤，不是产品自己的判定。

## 5. 状态

| 项 | 状态 |
| --- | --- |
| 十二补丁树 + 产品自身流水线 | **FAIL（1 步：step9 publish-locked-probe，KNOWN-UNLOCKED）** |
| 其余 8 步 | PASS（含 **C# 949/0**、发布、**冷启动**） |
| 与我的流水线结论的关系 | **不一致，未解释** |
| 下一轮首要项 | 读 `verify-source-snapshot.ps1` 的 step9 定义与判定逻辑，确定是"C 要求流水线同步更新"还是"C 有真实问题" |

状态串：`TechnicalAcceptance=NOT_PASSED`（发布整体）；`FormalPhaseApproval=NOT_GRANTED`；`ReleaseApproval=NOT_GRANTED`。
