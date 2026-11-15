# HANDOFF 2026-11-07 · DSH（执行）

> 角色：**批量执行 / 长任务实现**。你是执行者，不是裁判——做完不算完，等 C2 独立验收。
> 前置（2026-11-07 更新）：**开发主线已由用户确定 = 本地 `main`**（`47e939c`），整改线 25 个提交重放到其上。C1 的判定报告用于冲突裁决与「哪些改动已等价存在」的判定，**缺 C1 时只允许先做零冲突部分（合并 `docs/remediation-20261106`），不得自行裁决冲突文件**。
> 远端实况：`remediation/v1.1.0`(27545a8) 与 `docs/remediation-20261106`(efb3b41) **已在 origin**，无需你再推。

## 一、路径边界

**允许写**

- `D:/Projects/_worktrees/pineglow-integration-20261107/`（D1 新建，本轮所有代码工作都在这里）
- `D:/Projects/_archive/worktree-patches/`（D2 的备份与 patch）
- 主仓库 `D:/Projects/松光启动器/docs/handoff/`（只能写合流记录一份）

**只读**

- 主仓库 `D:/Projects/松光启动器`（含工作区脏树：只许读、许打 patch，**不许 checkout/restore/clean 掉**）
- `D:/Projects/_worktrees/pineglow-v110-remediation`、`pineglow-docs-remediation`
- `D:/Projects/_archive/bundles/松光启动器-all.bundle`

**禁止**

- `git push`（两个整改分支已在远端；**新的 `integration/*` 分支本轮不推送**，未经二次授权不许推）、打 tag、发 Release、改远端任何东西
- 改 `.github/workflows/*` 的通过条件（放宽超时、删步骤、加 `continue-on-error`）
- 删测试、改断言以迁就实现；把跳过写成通过
- `git add -A`；先 `git check-ignore` 验证再按文件组提交
- 建 `xxx-2`/`xxx-fix`/`xxx-final` 副本目录
- 动 `_archive/` 以外任何归档，动 `docs/**/*.log`（历史证据）
- 在 `localmain` 上直接 `merge`/`reset`

## 二、D1：合流线落地（P0，本轮唯一必须先完成的事）

### 为什么

本地 `main` 有 13 个 verify 返工提交但缺 Java 收敛；`remediation/v1.1.0` 有 M0/M1/M2 成果但缺那 13 个提交、也缺内部 docs。两条线分叉于 `c8aa286`。详见 `docs/现状基线-2026-11-07.md` 第一、二节。

### 步骤

```bash
cd "D:/Projects/松光启动器"
git worktree add "D:/Projects/_worktrees/pineglow-integration-20261107" -b integration/v1.1.0-20261107 main
cd "D:/Projects/_worktrees/pineglow-integration-20261107"
git merge --no-ff docs/remediation-20261106          # 同基线，预期零冲突
# 再按 C1 结论处理 remediation/v1.1.0：merge 或逐提交 cherry-pick
```

规则：

1. **先合 docs 分支**（与 main 同基线，先把它变成一个干净的成功，再碰有冲突的部分）。这一步不依赖 C1，可以先做。
2. `remediation/v1.1.0` 的处理方式**按 C1 报告执行**，不按自己的手感。若 C1 说需要逐提交重放，就逐提交重放，**每个提交单独能编译**再进下一个。
3. 冲突裁决的默认立场：**Java 收敛取 408 行版本（v1.1.0/origin 侧）；verify 返工与内部 docs 取 main 侧；UI/前端取 v1.1.0 侧（它更新），但必须证明 main 侧新增的 verify 相关钩子没丢**。凡与 C1 冲突，以 C1 为准并在记录里写明分歧。
4. **不许顺手改功能**。合流分支上只允许：解冲突、必要的最小适配、以及让测试全绿的最小修复。新功能一律不在这个分支做。
5. 合流后跑门槛（见第四节），全绿后 `git status` 必须干净。

### 产出

- 分支 `integration/v1.1.0-20261107`（**不推送**）
- `docs/handoff/合流记录-2026-11-07.md`：起始两个 HEAD 哈希 / 每个冲突文件的裁决与理由 / 合并后 HEAD / 门槛命令与 exit code / 未解决项 / 与 C1 结论的分歧

## 三、D2：主工作区清账（M0-1 收尾，`rPJm7y`）

现状：主工作区 10 改 + `docs/csharp-refactoring-2026-11-03/` 未跟踪。其中 5 个文件已确认逐字节等于 v1.1.0 提交内容；`ui/js/launcher.js`、`ui/css/redesign.css` 待定（C1 给结论）。

步骤：

1. **先备份再动**：整目录 `git stash create` 或 `git diff > patch` + `git ls-files -o --exclude-standard` 清单 + 每个文件 `sha256sum`，存到 `_archive/worktree-patches/main-dirty-2026-11-07/`，写 `manifest.json`（路径/大小/sha256）。
2. 逐文件确认「内容已在合流分支的某个提交里」——用 `git hash-object` 与 `git rev-parse integration/...:<path>` 对比，**不要用语义感觉**。已捕获 → 允许 `git checkout --`；未捕获 → 先提交到一个隔离分支 `wip/main-dirty-20261107`，不要丢。
3. `docs/csharp-refactoring-2026-11-03/`（before/after 源码 + 参数对照 json + trx）按用户默认口径**归档**：移到 `_archive/evidence/` 并在归档记录里登记，**不删**。
4. 结束条件：主工作区 `git status --short --untracked-files=all` 为空（或仅剩你明确标注为「待用户定夺」的文件），并附上备份清单哈希。

产出：`docs/handoff/HANDOFF-2026-11-07-dsh.md` 末尾追加「D2 结果」+ `_archive/worktree-patches/main-dirty-2026-11-07/manifest.json`。

## 四、D3：M2-1 剩余两项（`r2cd8n`，等 D1 合流后）

范围只做两项，**不要扩大**：

- **② 退出后自动备份**：实例设置里开关，旧实例默认关闭；游戏退出后执行；备份进现有 `backups/` 体系，复用 `FileRecoveryService`/`UncoveredFileService` 的既有约定，不新造第二套备份布局。
- **③ Mod 批量更新**：逐项提交；某项失败后停止未执行项、保留已成功结果并支持逐项撤销；取消停止后续项。

纪律（来自 11-06 `708073c` 的做法，继续保持）：

- 判定逻辑抽纯函数，IO 与判定分离，否则测不出来。
- 两条失败路径必须各有一条测试：**源未损坏**、**失败可回滚**。
- `scripts/test-inventory.txt` 双向断言同步更新；新测试文件必须登记。
- 前端 i18n 走 `ui/js/i18n.js`，不要在 `launcher.js` 里硬编码中文。

产出：两个提交（各自可独立回滚）+ `docs/handoff/合流记录-2026-11-07.md` 里追加功能证据段（测试数、回滚路径）。

## 五、门槛命令（每个任务收尾都跑）

```bash
export PATH="/c/Windows/System32:/c/Windows:$PATH"   # 11-06 实测：否则 GNU find 抢 find.exe，导致更新脚本测试假失败
cd "D:/Projects/_worktrees/pineglow-integration-20261107"
dotnet restore                 # 11-07 清空过 bin/obj，首次慢是正常的，不许写进性能结论
dotnet test dev/PineglowLauncher.Tests -c Release
node scripts/run-inventory-tests.cjs      # 前端全量 + 清单双向断言
node scripts/check-css-tokens.cjs
node scripts/check-ui-resource-closure.cjs
```

记录每条的 exit code 与测试数。任一项红，先修再继续，**不许带着红进下一个任务**。

## 六、遇到这些就停手

- C1 报告不存在或结论自相矛盾
- 需要推送 / 需要联网改远端
- 需要删任何 `docs/**/*.log`、`scripts/_gen.cjs`、`_migrate.py`、`_batch_resources.cjs`
- 冲突调解需要改产品行为语义（不是合并语义）
- 备份清单生成失败（宁可不动，也不要「先清了再说」）
