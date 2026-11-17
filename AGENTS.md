# AGENTS.md · 松光启动器（Pineglow Launcher）

> 任何 agent 在本仓库开工前先读本文件。它取代「凭印象猜规矩」。
> 项目根：`D:/Projects/松光启动器`。并行工作只在 `D:/Projects/_worktrees/`。

## 是什么

Avalonia 12 只做 WebView2 宿主；界面是 `ui/` 下的纯 Web 前端，通过 `LocalApiService`（HttpListener，127.0.0.1:48123）与 C# 服务层通信。.NET 10（SDK 10.0.301，见 `global.json`）。**交付平台只有 win-x64**（单文件自包含）。

## 目录

| 路径 | 作用 |
|---|---|
| `dev/PineglowLauncher/` | 主程序（Avalonia 宿主 + Services/Views） |
| `dev/PineglowLauncher.Tests/` | xUnit 测试（唯一测试项目） |
| `ui/` | 前端（`js/launcher.js` 单文件、三套 CSS 叠加） |
| `scripts/` | 门槛脚本与数据血缘脚本（**`_gen.cjs`/`_migrate.py`/`_batch_resources.cjs` 禁止删除**） |
| `.github/workflows/build.yml` | 唯一构建门槛（三平台矩阵） |
| `docs/` | 知识库；`docs/handoff/` 放交接与合流记录、`docs/evidence/` 放验收证据、`docs/open-items.md` 是**唯一**的遗留事项清单 |

## 命令（照抄，别自创）

```bash
node scripts/gates.cjs            # 四道门一次跑完：C# 全量 / 前端全量+清单 / CSS token / 资源闭包
node scripts/gates.cjs --only inventory,closure   # 只跑其中几条
node scripts/gates.cjs --baseline                 # 跑完顺手刷新 docs/baseline.md
node scripts/check-release-consistency.cjs        # 版本号 ↔ release-notes ↔ tag
```

`gates.cjs` 里已经固化了两个手工时代的坑，别在自己的命令里重犯：
dotnet 门槛需要把 `C:\Windows\System32;C:\Windows` 前置于 PATH（否则 GNU `find.exe` 抢占，更新脚本测试假失败）；
传 MSBuild 属性只用 `-p:`，**不用 `/p:`**（Git Bash/MSYS 会把 `/p:` 改写成路径）。

**基线数字只认 `docs/baseline.md`**（由 `gates.cjs --baseline` 生成，别手改）。数字对不上先怀疑自己跑错目录或少了环境变量。

但基线自己也会过期——所以 `gates.cjs` 每次跑完都会比对基线记的 commit 与当前 HEAD，不一致就喊出来。看到下面这段就是基线旧了，不是环境坏了：

```
⚠ docs/baseline.md 已过期：它记的是 <旧 sha>，当前 HEAD 是 <新 sha>。
```

当前开着的缺口统一登记在 **`docs/open-items.md`**；开工前先读它，避免重复发现同一个问题。

### 新增 `LocalApiService*.cs` 必须登记 `LocalApiServiceSource.OrderedFiles`

拆分守卫 `LocalApiServiceSplitTests` 会直接判红。登记方式与文件布局见 `docs/engineering.md` §二「LocalApiService 实现逻辑深析」。

### ⚠️ 主工作区里 `dotnet test` 会假失败（2026-11-15 实测，不是代码问题）

在 `D:\Projects\松光启动器` **这一份工作副本**里跑 `dotnet test`，`vstest → testhost` 子进程写
`%TEMP%` 会被拒：`UnauthorizedAccessException`（81）+ `HttpListenerException`（55）+
`LocalApiService 启动失败（已重试 6 次换端口）`（89），结果是 **309 / 1380 失败**，
**没有任何业务断言失败**。

已实测排除：`%TEMP%` 临时目录堆积、TEMP 路径、cwd、中文路径名、陈旧 bin/obj、文件内容
（与副本哈希一致）、DACL（`icacls /reset /T` 12224 对象无效）、属主（`takeown /r` 无效）、
目录联接、`subst` 盘符、计划任务裸跑、以及「从主仓库目录启动的进程本身」
（普通 .NET 程序在主仓库 cwd 往 `%TEMP%` 写文件是正常的）。

同一 commit 在任何**副本**里跑都是全绿（`git worktree` / `cp -a` / `robocopy /COPY:DAT`），
所以这是绑在这份工作副本上的**外部规则**（疑为机器上安全产品的目录规则），与仓库内容无关。

**规范**：验收、复验、要写进证据的测试数字，一律在 `D:/Projects/_worktrees/` 下的工作树里跑；
主工作区只做源码改动与提交，不要在主工作区得出「多少条失败」的结论。

## 平台支持与已知缺口

- 只支持 win-x64。README 与发布说明不得写成「三平台可用」；「CI 三格绿」≠「三平台能跑」。
- macOS 不跑测试：Avalonia WinExe 在 testhost 下加载 `Pineglow.Launcher.dll` 会 `FileNotFoundException`（宿主依赖问题，不是测试逻辑）。
- 两处 POSIX 缺口（仅影响非交付平台，已记录）：`PathSafetyGuard.PathsEqual` / `FileRecoveryService.IsPathInside` 恒用 `OrdinalIgnoreCase`；无 `trustedGameDirectory` 时缺跨平台重解析点链检查。

## 硬约束（违反一次，这轮结果作废）

1. **不推送、不打 tag、不发 Release**。推送由编排方单独授权执行。
2. **不改 `.github/workflows/*` 的通过条件**（不加 `continue-on-error`、不放宽超时、不删步骤）。
3. **不删测试、不改断言迁就实现、不把跳过写成通过**。xUnit 2.9.3 没有动态 skip；环境不具备时断言真实行为，不要 `return`。
4. **不用 `git add -A`**。先 `git check-ignore`，再按文件组显式 add。
5. **不建 `xxx-2`/`xxx-fix`/`xxx-final` 副本目录**。并行只在 `_worktrees/`，结束 `git worktree remove`。
6. 不动 `_archive/` 以外归档、不动 `docs/**/*.log`（每轮验收原始日志，删了等于销毁证据链）。
7. 提交身份用环境变量（本机 `git config` 可能没有 user.name）：
   `GIT_AUTHOR_NAME/EMAIL = GIT_COMMITTER_NAME/EMAIL = 松光启动器 <launcher@pineglow.local>`，**不要改全局配置**。

## 验收规则（这个项目反复踩过的）

- **执行者 ≠ 裁判**：改完由另一个人（Codex 或编排方）重跑关键命令。`FormalPhaseApproval` 默认 `NOT_GRANTED`，技术通过不等于发布批准。
- **证据优先于结论**：每个「完成」都要能指回可复核的原始输出（exit code / 测试数 / 哈希 / 日志路径）。自评文档只是待验材料。
- **不要引用 `docs/api-reference.md` 的 CORS 描述**做安全判断，以代码为准（该文件历史上与代码相反）。
- 测试数必须与 `scripts/test-inventory.txt` 双向断言一致；「大致一致」= FAIL。
- 首次编译慢是 11-07 清过 `bin/obj` 的后果，不许写进性能结论。

## 环境陷阱（真实发生过）

- `git-bash` 的 PATH 把 `/usr/bin` 放前面时，`.bat` 脚本里的 `find` 会解析到 GNU find，导致更新脚本测试假失败 → 测试给子进程设置干净 PATH。
- 中文路径下 `fs.cpSync` 会另建乱码目录且**不报错**（祖先目录含非 ASCII 时目标 0 文件）→ 先 `mkdirSync` 再逐文件复制。
- 原生 `git`/`node`/`dotnet` 不认 `/d/...` 路径，用 `D:/...` 形式。
- 桌面版 agent 的「工作区」就是会话 cwd：新建会话前先选中项目工作区，否则相对路径会写到别处。
