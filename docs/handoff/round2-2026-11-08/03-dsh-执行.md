# 03 · DSH（执行）· 第三步

**前提：第 2 步的 Codex 报告已存在。** 贴进 DSH 时请先选中 `松光启动器` 工作区再新建会话（不要用默认工作区）。

---

## 投递块（复制以下全部）

```
任务：松光启动器 —— M2-3 收尾（验证器统计口径）+ M2-1②（退出后自动备份）

第 0 步（先做，做错后面全废）：确认工作目录，把原始输出贴进过程记录
  cd "D:/Projects/_worktrees/pineglow-integration-20261107"
  pwd
  git rev-parse --show-toplevel
  git rev-parse HEAD          # 期望 5e8ae56 或其后继；不是就先报告，不要 reset
  如果 pwd 不是 D:/Projects/_worktrees/pineglow-integration-20261107，立刻停下报告——
  在默认工作区里相对路径会把文件写到错位置（2026-11-07 发生过一次，D:\ 根多了个空目录）。

必读
  D:/Projects/松光启动器/docs/handoff/合流记录-2026-11-07.md   （尤其 §4.11 平台断言口径、§4.13 CI）
  D:/Projects/松光启动器/docs/evidence/2026-11-08-m22/清账表.md （Codex 的判定，M2-3 相关条目按它的结论做）
  D:/Projects/松光启动器/docs/decision-gui-testing-2026-11-06.md（GUI/WebView 测试取舍）

任务 A：M2-3 收尾 —— 九页验证器统计口径
  背景：历史上界面验证器把某一页的图片数分母算错，导致 PASS 假通过；另有「九页」与「十页」口径不一致。
  要求：
   1) 先写一条**会失败**的测试固定住现状（证明问题真实存在），再修；
   2) 修正后给出「修前/修后 同一页面同一输入」的对照表（图片数、参与统计的页数、分母）；
   3) 与截图/证据无关的判据一律不用；拿不到某个页面的真实渲染就写「未覆盖」，不要凑数。
  验收标准：该测试在修复前红、修复后绿；对照表能解释每一个数字从哪来。

任务 B：M2-1② 退出后自动备份
  要求：
   1) 实例设置里增加开关，**旧实例默认关闭**（不得改变现有用户行为）；
   2) 游戏进程退出后执行一次备份，进现有 backups/ 体系，复用 FileRecoveryService/UncoveredFileService 的既有约定，
      **不要新造第二套备份布局**；
   3) 判定逻辑与 IO 分离（纯函数可单测），必须有这三条失败路径测试：磁盘不足、目标被占用、备份中断；
     每条都断言「源存档完好」；
   4) 前端文案走 ui/js/i18n.js，不在 launcher.js 里硬编码中文。

纪律
  - 每个任务是独立提交，可单独回滚。
  - 新增测试必须登记进 scripts/test-inventory.txt，双向断言（run-inventory-tests.cjs --check）必须通过。
  - **不许删测试、不许改已有断言迁就实现、不许把跳过写成通过**（xUnit 2.9.3 没有动态 skip，用断言真实行为）。
  - 提交身份用环境变量 GIT_AUTHOR_NAME/EMAIL 与 GIT_COMMITTER_NAME/EMAIL = 「松光启动器 <launcher@pineglow.local>」，不要改 git config。
  - 不 push、不打 tag、不改 .github/**。这两件都由编排方单独做。

收尾门槛（逐条记 exit code 与测试数）
  export PATH="/c/Windows/System32:/c/Windows:$PATH"
  dotnet restore dev/PineglowLauncher.Tests
  dotnet test dev/PineglowLauncher.Tests -c Release
  node scripts/run-inventory-tests.cjs
  node scripts/check-css-tokens.cjs
  node scripts/check-ui-resource-closure.cjs
  注意：仓库没有 .sln，裸 dotnet restore 会 MSB1003，用带项目路径的写法；
  首次编译慢是因为 11-07 清过 bin/obj，不许写进性能结论。

产出
  - 两个提交（A、B 各一）
  - 在 D:/Projects/松光启动器/docs/handoff/合流记录-2026-11-07.md 追加一节「R6 执行（M2-3 / M2-1②）」：
    每个任务的改法、修前/修后对照、门槛数字、未解决项
  - 结束时回报：HEAD、提交数、门槛 exit code 与测试数、你没做成的事
```

---

## 技能与规则（DSH 侧的现实情况，先说清楚）

DSH **没有磁盘上的技能目录**（`~/.dsh/skills` 不存在，界面里的「技能中心」不是可寻址路径）。
所以它的「技能」只有两种，投递块里已经写进去了：

1. **项目规则文件**：`D:/Projects/松光启动器/AGENTS.md` —— 必读，里面有构建命令、1379/481 基线、7 条硬约束、环境陷阱
2. **任务书本身**：`03` 这个文件里的投递块，逐条验收标准与禁止项

要它做到的事都写在投递块里了，包括：第 0 步 `pwd` 取证、每任务独立提交、三条失败路径测试、清单双向断言。如果它自己提到「技能中心」，让它说明用的是哪一个、做了什么——**不要让它凭技能名就跳步**。

## 你要做的

1. 先确认 `docs/evidence/2026-11-08-m22/清账表.md` 存在（没有就别开这一步，DSH 会停）
2. **左侧先点 `松光启动器` 工作区，再新建会话**，然后贴投递块
3. 看到它第 0 步回报的 `pwd` 是 `D:/Projects/_worktrees/pineglow-integration-20261107` 才算正常开始
4. 跑完回来告诉我「第 3 步做完」，我读提交和记录

## 它可能会停下的地方（正常，别催）

- 任务 A 里某个页面的渲染拿不到 → 它会写「未覆盖」，这是对的
- 任务 B 需要改实例配置模型 → 如果它说要动 `InstanceConfig` 的兼容性，让它停下来问我
- 任何要改 `.github/**` 的想法 → 一律拒绝
