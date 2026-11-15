# 04 · Codex（验收）· 第四步

**前提：第 3 步 DSH 已交付两个提交。** 贴进 Codex 桌面（Codex 模式），只读。

---

## 投递块（复制以下全部）

```
任务：松光启动器 —— R6 独立验收（只读，针对 DSH 本轮两个提交）

你是裁判。不许改产品代码、不许 commit/push/tag、不许 checkout 分支。
DSH 的自评（合流记录里「R6 执行」一节）只是待验材料，不是证据。

第 0 步（自己取，不许采信别人给的数字）
  cd "D:/Projects/_worktrees/pineglow-integration-20261107"
  git rev-parse HEAD
  git status --porcelain --untracked-files=all | wc -l
  git log --oneline 5e8ae56..HEAD

要验的三件事

一、门槛数字（逐条记录 exit code 与测试总数/通过/失败/跳过）
  export PATH="/c/Windows/System32:/c/Windows:$PATH"
  dotnet restore dev/PineglowLauncher.Tests
  dotnet test dev/PineglowLauncher.Tests -c Release
  node scripts/run-inventory-tests.cjs
  node scripts/check-css-tokens.cjs
  node scripts/check-ui-resource-closure.cjs
  对比基线：本轮之前的 HEAD 5e8ae56 是 1379/1379、前端 481/481、0 跳过。
  新测试必须让总数上升且 0 跳过；若总数没变而「新增了测试」，就是没跑或被删。

二、任务 A（验证器统计口径）
  - 找到那条「修复前应红」的测试，检查它断言的是不是真问题（分母/页数），而不是为了变绿写的同义反复；
  - 复算它对照片：随机挑一页，用原始日志/图表数据自己算一遍图片数与分母，看是否与表格一致；
  - 若它只改了阈值或删了断言 → 直接判 FAIL。

三、任务 B（退出后自动备份）
  - 旧实例默认关闭：找到缺省值所在代码，确认默认 false，且**没有**顺带改已有配置的语义；
  - 三条失败路径测试是否存在：磁盘不足 / 目标被占用 / 备份中断；每条是否断言「源存档完好」；
  - 是否复用现有 backups/ 布局（不许第二套目录结构）；
  - 前端文案是否走 i18n.js。

四、越界检查
  - `.github/**` 相对 5e8ae56 是否零改动
  - 是否有 push（`git ls-remote --heads origin` 里 integration 分支的 sha 是否仍是 5e8ae56 —— 若是新 sha 说明偷偷推了）
  - 有没有把构建产物（bin/obj/publish/*.dll）纳入提交

结论只能二选一：
  PASS_SCOPED（技术通过，列出遗留项 R6-01 起）
  FAIL_REWORK_REQUIRED（列返工项，每条给最小修复建议）
末尾必须写 `FormalPhaseApproval=NOT_GRANTED`。

唯一允许写入：
  D:/Projects/松光启动器/docs/evidence/2026-11-08-r6/验收报告.md

报告结构：结论 / 输入版本（HEAD + 是否 dirty）/ 命令与 exit code 表 / 三件事逐条判定（通过|不通过|证据）/ 与 DSH 自评不一致之处 / 返工或遗留清单 / 未验证项。
```

---

## 技能与规则（开工前先确认）

1. `C:/Users/Administrator/.codex/AGENTS.md`（全局约定）
2. `D:/Projects/松光启动器/AGENTS.md`（项目规则，含「执行者≠裁判」「证据优先于结论」两条本项目铁律）

该用的技能：

| 技能 | 用途 |
|---|---|
| `run-tests` | 重跑门槛时用，避免自创命令导致数字不可比 |
| `csharp-expert` | 判断「旧实例默认关闭」这类配置语义时用 |
| `test-gap-analysis` | 判断新增测试是否真覆盖失败路径（而不是同义反复）时用 |

**不要加载 `csharp-refactoring` / `simplify-code`**：验收时顺手改代码是最常见的越界方式。

报告末尾加一行「本轮使用的技能/规则文件」。

## 你要做的

1. 贴进去，等它写完 `docs/evidence/2026-11-08-r6/验收报告.md`
2. 回来告诉我「第 4 步做完」+ 结论是 `PASS_SCOPED` 还是 `FAIL_REWORK_REQUIRED`
3. 如果是 FAIL，我据此出第 5 步（返工 prompt）；如果是 PASS_SCOPED，我把遗留项转成台账条目

## 补一句现实提醒

Codex 的沙箱在 Windows 上跑不了 `dotnet`（读不到 `%APPDATA%\NuGet\NuGet.Config`），
如果它报 restore/test 卡在权限，那属于**环境受阻**、不是产品问题——
把它的原话发我，我在宿主机重跑那两条命令补证据（今天已经这么配合过三轮）。
