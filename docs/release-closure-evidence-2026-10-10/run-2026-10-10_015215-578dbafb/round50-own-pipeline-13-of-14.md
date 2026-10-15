# 记录 50：产品自身流水线的复跑 —— 夹具修好两处，**仍剩 1 项失败**（未解决）

运行标识：`run-2026-10-10_015215-578dbafb`
时间：2026-10-11 20:48–21:00，Asia/Hong_Kong（+08:00）
本轮性质：**只读 + 隔离副本，产品目录零改动**（0 staged）。

目标（记录 49 定下）：按正确方式重跑产品自身的候选流水线，把"十二条补丁一起应用"的结论
从"我选的步骤通过"升级为"产品自己的门槛也通过"。

---

## 1. 三轮夹具迭代，失败项 3 → 2 → 1

| 轮 | 夹具做法 | 失败项 | 失败内容 |
| --- | --- | --- | --- |
| 48 | `git init` + `add -A`（**无 `.gitignore`**） | **2 + 1 已知边界** | `快照文件数量不符 期望475/实际312`；`快照内存在 bin/obj` |
| 50 | 加上 `.gitignore` | **2** | `bin/obj` 污染消失；仍 `期望469/实际306` |
| **51** | 再把**白名单 163 项保持未跟踪**（`git rm --cached`） | **1** | `候选项闭包不完整，缺 2 项: dev/PineglowLauncher/Assets/AppIcon.ico, AppIcon.png` |

**算术对上了**：`ls-files = 143`，`143 + 163 = 306` = 快照文件数 = **出厂版历史上的 302 同一量级**。
`bin/obj tracked = 0`，白名单 163 项都在盘上且未跟踪——**副本的仓库状态已与真实仓库同构**。

**其余全部 PASS**（这一轮跑了完整的 15 步）：

```
step0 step-runner-selftest            PASS
step1a ui-resource-closure            PASS
step1b ui-closure-selftest            PASS
step1c test-inventory                 PASS
step1  node-tests                     PASS
step2  restore-launcher-locked        PASS
step3  restore-tests-locked           PASS
step6  build-launcher-release         PASS
step7  dotnet-test-release            PASS   ← 失败: 0，通过: 949
step8  build-verifyhost               PASS
step8b verifyhost-bisect-smoke        PASS
step9  publish-locked-probe           KNOWN-UNLOCKED（脚本明示"不阻断判定"）
step10 publish                        PASS
step11 standalone-cold-start          PASS   ← 19 项全通过
```

## 2. 唯一剩下的失败

```
候选项闭包不完整，缺 2 项:
  dev/PineglowLauncher/Assets/AppIcon.ico
  dev/PineglowLauncher/Assets/AppIcon.png
```

我查了这四项事实：

| 检查 | 结果 |
| --- | --- |
| 这两个文件被 `.gitignore` 忽略吗 | **否**（`git check-ignore` 无输出） |
| 在真实仓库里被跟踪吗 | **是**（`git ls-files --error-unmatch` 两者都命中） |
| 在白名单里吗 | **否** |
| 盘上存在吗 | 是（172,927 字节 / 1,953,440 字节） |

## 3. 我的判断（**标注为判断，不是结论**）

**倾向于"仍是夹具产物"，理由有一条硬的**：出厂版流水线**历史上是在真实仓库上 PASS 的**
（`snapshotFileCount = 302`、闭包缺口 0），而那两次 AppIcon 在真实仓库里**是被跟踪的**。
我这套 `git init` + `add -A` + `rm --cached` 造出来的跟踪集合，**无法复现"某些文件被跟踪、
却不在白名单里"这个真实仓库才有的状态**——闭包检查要的正是这个状态。

**但我没有验证它**，所以不写成结论。要查清需要：把真实仓库的 `.git` 索引复制进副本
（而不是自己 `git init`），或直接对 `AppIcon.*` 做 `git add -f` 后重跑。

## 4. 本轮真正的收获

**"产品自己的门槛也通过"这个升级还没做到**，但夹具的三处错误已经全部定位，
其中两处已修复且**有算术证据**（`143 + 163 = 306`）。剩下的一处有了明确的下一步。

**而且这轮的结果本身有价值**：在十二补丁树上，产品自己流水线的 **14 步里有 13 步 PASS**
（含 Node、C# 949/0、VerifyHost 凭据隔离、发布、19 项冷启动），唯一未过的与补丁无关
（是候选闭包的文件跟踪状态）。

状态串：`TechnicalAcceptance=NOT_PASSED`（发布整体）；`FormalPhaseApproval=NOT_GRANTED`；`ReleaseApproval=NOT_GRANTED`。
