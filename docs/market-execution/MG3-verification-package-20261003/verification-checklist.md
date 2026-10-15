# MG3 复验清单（逐条可执行）

> 用法：逐行照做。每行给出**要验什么**、**怎么验**、**期望看到什么**、**证据在哪**。
> 标 ⚠️ 的是"脚本验不了、必须人读代码/日志判断"的项；标 🔴 的是**执行者已明确标为未验证**的项。

---

## 零、前置

```powershell
cd D:\松光启动器
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\verify-mg3.ps1
```

期望：`C# exit=0  Node exit=0`、`本里程碑内无法解释的漂移=0`、MR21 显示 `productCodeChanged = False`。

---

## 一、逐包复验

### MR17 核心流程无障碍与小窗口体验

| # | 验什么 | 命令 / 位置 | 期望 |
|---|---|---|---|
| 17.1 | 契约用例 | `node --test ui/tests/a11y-keyboard.test.cjs` | 9 通过 / 0 失败 |
| 17.2 | RED 口径 | `MR17…/red/01-a11y-before.log` | 9 失败 / 0 通过 |
| 17.3 | ⚠️ **断言是否够紧** | 用例 `焦点必须可见` | 必须断言**行首的全局** `:focus-visible`（scoped 规则不算兜底） |
| 17.4 | ⚠️ 是否动了既有 CSS 规则 | `MR17…/deliverable.md` §三 | 应当只有**追加块**，既有规则未改（便于整块回退） |
| 17.5 | ⚠️ **48 个假失败的处置** | `MR17…/deliverable.md` §五 | 只补测试替身，**未给产品代码加特例**；确认两处产品侧改动语义等价 |
| 17.6 | 🔴 真实键盘 / 屏幕阅读器 / 缩放 / 对比度 | —— | **未验证**，需桌面会话与人工 |

### MR19 可复现构建、RID 边界与 CI

| # | 验什么 | 命令 / 位置 | 期望 |
|---|---|---|---|
| 19.1 | 不变量用例 | `dotnet test --filter "FullyQualifiedName~BuildLockFileTests"` | 3 通过 / 0 失败 |
| 19.2 | ⚠️ **RID 锁定冻结是否有证据** | `MR19…/deliverable.md` §2.2 | 必须能看到"RID 形态与非 RID 锁定还原**互斥**"的实验（含测试项目也失败） |
| 19.3 | 锁文件是否回到提交态 | `MR19…/green/launcher-packages.lock.json.before` 与当前文件 | 8663 bytes、SHA 与备份一致 |
| 19.4 | 工作流是否合法 | `actionlint -shellcheck= -pyflakes= .github/workflows/*.yml` | exit 0（本包用 1.7.7 校验过） |
| 19.5 | ⚠️ 可复现性的**边界** | `MR19…/deliverable.md` §2.3 | 只声称**托管程序集**逐字节可复现；单文件 exe **未测** |
| 19.6 | 🔴 真实 GitHub Actions 运行 | —— | **未验证**（本机无 runner） |

### MR20 Windows 自更新的验证与恢复

| # | 验什么 | 命令 / 位置 | 期望 |
|---|---|---|---|
| 20.1 | 聚焦用例 | `dotnet test --filter "FullyQualifiedName~UpdateRecoveryTests"` | 19 通过 / 0 失败 |
| 20.2 | RED 口径 | `MR20…/red/01-update-recovery-before.log` | 6 失败 / 13 通过（并读报告 §四解释为什么有 13 个通过） |
| 20.3 | ⚠️ **回滚是真的** | 用例 `ApplyScript_HappyPath_…`、`…WhenReplaceFails_RollsBackToTheOldContent` | 脚本被**真实执行**；替换失败后目标内容回到旧值 |
| 20.4 | ⚠️ 危险路径仍可运行 | 用例 `…WhenBackupFails_DoesNothingAndRecordsBackupFailed` | `backup_failed` 且目标未被动过 |
| 20.5 | ⚠️ **errorlevel 脆弱点** | `MR20…/deliverable.md` §五 | 三处检查必须已改为 `set "RC=%errorlevel%"` 形式 |
| 20.6 | UI 是否展示失败 | `node --test ui/tests/update-recovery.test.cjs` | 5 通过；界面只在 `applied===false` 时告警 |
| 20.7 | 🔴 真实安装目录替换 / 真实重启 / Unix 分支 | —— | **未验证**（卡片明确"另需授权"） |

### MR21 安装启动与性能基线（**只测量**）

| # | 验什么 | 命令 / 位置 | 期望 |
|---|---|---|---|
| 21.1 | ⚠️ **产品代码是否真的没动** | `MR21…/manifest.json` 的 `productCodeChanged` | `False`；`git diff` 中 MR21 只新增测试与脚本 |
| 21.2 | 一键复现测量 | `powershell -File .\scripts\measure-mr21.ps1 -Runs 5 -Iterations 20` | exit 0；覆盖 `computed/perf-baseline.json` |
| 21.3 | ⚠️ 原始数据可复查 | `MR21…/raw/run-0N/raw-metrics.jsonl` | 含**逐次样本**；可人工重算 p50/p95 并与 computed 对照 |
| 21.4 | ⚠️ **不得出现"性能提升"** | `MR21…/deliverable.md` | 全文只有测量与基线；候选修复明确标注**未实施** |
| 21.5 | 候选修复的**变化风险** | 同上 §3.4 | 必须写明"选中行集合可能不同"，不能只看性能数字 |
| 21.6 | 🔴 真实 GUI / WebView2 渲染 / 账号流程 / WebView2 缺失路径 | —— | **未测量** |

---

## 二、跨包检查（容易漏的）

| # | 验什么 | 怎么验 | 期望 |
|---|---|---|---|
| C1 | 证据布局是否一致 | 各 run 下应有 `green/changed-files.sha256.txt` | 四个包齐全（本包生成时补过 MR17/MR21） |
| C2 | 是否有人"顺手改"了封存证据 | 比对各包 `deliverable.md` 的哈希与 `changed-files.sha256.txt` | 一致 |
| C3 | 白名单计数与条目数 | `scripts/source-snapshot-inputs.txt` 的类别声明 vs 实际行数 | 每个类别都相等（八个类别） |
| C4 | 含中文的 `.ps1` 是否带 BOM | 读前三字节是否为 `EF BB BF` | 必须带（否则 PowerShell 5.1 按 ANSI 解码会乱码） |

---

## 三、复验结论模板（照抄，不要省略"未通过"项）

```
自动验证：C# 752/0、Node 230/0、本里程碑内漂移 0（脚本 exit 0）
独立复验：<逐条填 §一、§二 的结果；每个 ⚠️ 都要写是否抽查了代码/日志>
未通过项：<逐条列；没有就写"无">
未验证项：<照抄各包 deliverable.md 的"明确未做与未验证"，尤其 MR17 的真实键盘/屏幕阅读器、
          MR19 的真实 CI、MR20 的真实安装目录替换、MR21 的全部 GUI 项>
用户批准：<未给出 / 已给出（附原话）>
```

**注意**：MG3 的放行条件是"**可提交内测候选供用户批准分发**"。
本里程碑有四类关键未验证项（上表 🔴），因此**当前只能写"证据就绪"**，
不能写"可提交内测候选"——那需要真实键盘/屏幕阅读器、真实 CI、真实替换与真实 GUI 观测补上。
