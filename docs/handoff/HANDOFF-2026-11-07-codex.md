# HANDOFF 2026-11-07 · Codex（只读裁判）

> 角色：**只读分析 + 独立验收**。你是裁判，不是执行者。本轮不改产品代码、不建分支、不提交、不推送。
> 模型建议：reasoning high。语言：中文。

## 一、路径边界（缺一条就不开工）

**允许写（仅这两处）**

- `D:/Projects/松光启动器/docs/evidence/2026-11-07-c1/`（C1 产出）
- `D:/Projects/松光启动器/docs/evidence/2026-11-07-r5/`（C2 产出）

**允许读（全部）**

- `D:/Projects/松光启动器`（主仓库，含 `.git`）
- `D:/Projects/_worktrees/pineglow-v110-remediation`（`remediation/v1.1.0`）
- `D:/Projects/_worktrees/pineglow-docs-remediation`（`docs/remediation-20261106`）
- `D:/Projects/_archive/bundles/`（只读比对）

**禁止**

- 任何 `git commit` / `merge` / `checkout -b` / `push` / `tag` / `reset` / `stash drop` / `gc`
- 修改 `dev/`、`ui/`、`scripts/`、`.github/`、`release-notes/` 下任何文件
- 删任何目录或文件（包括 bin/obj、日志、证据）
- 访问网络（除非做 U6 的远端核实，且只读、只记录命令与输出）

## 二、C1：基线判定与合并方案（现在就能做，P0）

### 要回答的问题（每条都要证据，不要推断）

1. **本地 `main`(47e939c) 的 13 个提交 vs `origin/main`(30f7b56, = tag v1.0.3) 的 7 个提交**：逐文件比对，哪些改动是同一件事的两次提交（10-10 误删后重建导致 hash 不同）、哪些只在一边有。已知反例：`JavaLocator.cs` 在 main 是 349 行无收敛版，在 origin/main 与 v1.1.0 是 408 行有收敛版 —— 请把这一结论扩展到全部文件，给出「只 main 有 / 只 origin 有 / 内容等价 / 真冲突」四分类清单。
2. **`remediation/v1.1.0`(25 提交) 应重放到哪条线**：给出结论与理由，并且必须说明重放时预计的冲突文件清单（预测即可，附依据）。
3. **main 脏树里两个不确定文件**：`ui/js/launcher.js`、`ui/css/redesign.css`（工作区 hash 与 HEAD、与 v1.1.0 都不同）的内容是否已被 v1.1.0 的提交（`d4e18d2`/`8ecd563`/`e72f4cf`/`708073c`/`27545a8`）语义等价捕获。其余 5 个脏文件已确认逐字节等于 v1.1.0 提交内容，不必重复验证。
4. **M0-1 备份实物**：报告称「备份 4.467GB / 7931 文件哈希全一致」但未写路径。请在 `D:\` 根、`C:\Users\Administrator\Documents\Codex\**\outputs\`、`_archive/` 下检索是否存在该备份及其哈希清单；找不到就明确写「不存在或不可定位」，不要替执行方圆场。
5. **内部 docs 的处置**：`main` 有 538 份 .md，`origin/main` 只保留发布面子集。给出「开发线保 docs、发布线剥离 docs」这一做法是否与现有 `release.yml` / `.gitignore` 一致（读文件说话），或是否应该改。

### 建议命令（只读）

```bash
cd "D:/Projects/松光启动器"
git rev-list --left-right --count main...origin/main
git log --oneline main..origin/main ; git log --oneline origin/main..main
git diff --stat main origin/main
git diff --name-status main origin/main | sort | uniq -c | sort -rn | head -40
git rev-list --left-right --count main...remediation/v1.1.0
git log --oneline main..remediation/v1.1.0
git diff --stat main..remediation/v1.1.0
git show remediation/v1.1.0:dev/PineglowLauncher/Pineglow.Launcher.Services/JavaLocator.cs | wc -l
git hash-object ui/js/launcher.js ; git rev-parse HEAD:ui/js/launcher.js ; git rev-parse remediation/v1.1.0:ui/js/launcher.js
git bundle verify "D:/Projects/_archive/bundles/松光启动器-all.bundle"
```

（本机 `terminal` 走 git-bash：原生 git 不认 `/d/...`，用 `D:/...`。）

### 产出

`docs/evidence/2026-11-07-c1/判定报告.md`，结构固定：

1. 一句话结论：开发主线应选哪条、整改线重放到哪、是否需要真冲突解
2. 四分类文件清单（表格：路径 / 分类 / 判定依据）
3. 合并冲突预测清单（文件 + 冲突原因 + 建议取哪一侧）
4. U4 判定（`launcher.js` / `redesign.css`：已捕获 / 部分捕获 / 未捕获，附证据）
5. M0-1 备份检索结果（存在→路径+哈希；不存在→写「不可定位」）
6. 未验证项（凡是你没能确认的，逐条列出并写为什么）

### 停止条件

- 任何需要写仓库的步骤 → 停，写进报告「需执行方处理」
- 需要联网（远端核实）且网络不可用 → 记录失败原文，标 BLOCKED，不要重试超过 2 次

## 三、C2：R5 独立验收（等 D1 完成后）

输入版本：合流分支 `integration/v1.1.0-20261107` 的 HEAD（**你自己 `git rev-parse HEAD` 取，不许抄执行方给的哈希**）。

必须亲自重跑、逐条记录 exit code 与测试数：

```bash
cd "D:/Projects/_worktrees/pineglow-integration-20261107"   # 目录由 D1 创建
export PATH="/c/Windows/System32:/c/Windows:$PATH"          # 避免 GNU find 抢占 find.exe（11-06 实测陷阱）
dotnet restore
dotnet test dev/PineglowLauncher.Tests -c Release --logger "trx;LogFileName=r5.trx"
node scripts/run-inventory-tests.cjs      # 前端全量 + 清单双向断言
node scripts/check-css-tokens.cjs ; echo $?
node scripts/check-ui-resource-closure.cjs ; echo $?
# 路由结构守卫：ApiRouteInventory / RouteReachability / LocalApiServiceSplit 三项
# Java 一次性测量：按 docs/m0-3-release-blocker-closure-2026-11-06.md 的方法复测，记录耗时
```

### 判定规则

- 测试数必须与 `scripts/test-inventory.txt` 双向断言一致；不一致就是 FAIL，不许写成「大致一致」。
- 只要有一项 FAIL，整轮结论写 `FAIL_REWORK_REQUIRED`，并列返工项编号（R5-01 起）。
- 全部通过也只能写 `PASS_SCOPED` + `FormalPhaseApproval=NOT_GRANTED`（技术通过 ≠ 发布批准）。
- 冷启动证据：11-07 清理已删 `publish`/`runtime` 产物，若无法重建单文件 EXE，就明确写 `NOT_RUN`，**不要用旧日志冒充新证据**。

### 产出

`docs/evidence/2026-11-07-r5/验收报告.md`：结论 / 输入版本（commit hash + 是否 dirty）/ 每项命令原文 + exit code + 测试数 / 证据文件相对路径 / 返工清单 / 未验证项。

## 四、给你的两条提醒（来自历史教训）

- 这个项目从 10-03 起的返工几乎都源于「结论写在证据前面」：回滚误报已回滚、验证器误报 PASS、报告漏记 FAIL。你每写一句「通过」，先问自己有没有对应的原始输出。
- 不要采信执行方的自评文档（`self-evaluation.md`、`m0-consolidated-report`），它们是**待验证材料**，不是证据。
