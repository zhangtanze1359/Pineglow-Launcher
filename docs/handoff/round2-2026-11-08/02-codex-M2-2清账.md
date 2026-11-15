# 02 · Codex（只读判定）· 第二步

贴进 **Codex 桌面**（Codex 模式）。它是只读裁判，允许写**一个**报告文件，别的都不许动。

---

## 投递块（复制以下全部）

```
任务：松光启动器 —— M2-2「BLOCKED / NOT_RUN 逐条清账」判定（只读）

你是裁判，不是执行者。除了下面指定的报告文件，不许写、改、删任何仓库文件；不 commit、不 push、不打 tag、不 checkout 分支。

第 0 步：确认工作目录与版本（把原始输出贴进报告）
  cd "D:/Projects/_worktrees/pineglow-integration-20261107"
  git rev-parse HEAD
  git status --porcelain --untracked-files=all | wc -l
  （期望 HEAD=5e8ae56 或其后继；工作区 0 行。若不是，先停下来报告，不要 reset。）

必读材料
  D:/Projects/松光启动器/docs/handoff/合流记录-2026-11-07.md        （合流过程、R5 返工、§4.11/§4.13）
  D:/Projects/松光启动器/docs/evidence/2026-11-07-c1/判定报告.md     （基线四分类与冲突预测）
  D:/Projects/松光启动器/docs/evidence/2026-11-07-r5/验收报告-R5b.md （上一轮验收的遗留项）
  D:/Projects/松光启动器/docs/现状基线-2026-11-07.md                 （未验证项 U1–U7）

要清账的条目（逐条给结论，不许笼统）
 1. 真实微软账号登录（Minecraft 账号全链）
 2. AI Key / AI 诊断真实调用
 3. Gitee 镜像 token 与镜像流程
 4. 进入世界长时游玩（游戏内真实运行时行为）
 5. 在线 NuGet 还原（LIMITED）与 RID NU1004
 6. 真实发版替换重启（单文件 EXE 自替换）
 7. M0-1 声称的 4.467GB / 7931 文件备份（当前判定：不可定位）
 8. 更新替换脚本的并发时序抖动（曾出现一次 956/957，未改断言）
 9. JSON 端点的手工路由残留（M1-1 后的 Route 表与文档端点表口径）
 10. design-system.md 与 development-guide.md 的 token 规则互相冲突
 11. TOCTOU 竞态（PathSafetyGuard 已如实标注「竞态未关闭」）
 12. 两条 POSIX 安全缺口：PathsEqual/IsPathInside 恒用 OrdinalIgnoreCase；无 trusted root 时缺重解析点链检查
 13. macOS testhost 无法加载 Pineglow.Launcher.dll（故 macOS 不跑测试）
 14. 冷启动逐字节比对（11-07 清理删了 publish 产物后是否还能复现）

每条的结论必须落在三者之一，并附证据（命令/文件/行号/日志路径）：
 A 已验证 —— 指出验证方式与结果
 B 明确不验证 —— 写清为什么（平台不支持 / 需要真实账号 / 成本与收益不成比例），并说明这个标签会一直留着
 C 转后续事项 —— 给出建议的事项标题与完成定义（不要自己建事项，交给 WorkBuddy）

硬约束：
 - 只读。禁止改产品代码或测试、禁止改 .github/**、禁止 git 写操作（commit/merge/cherry-pick/reset/stash/push/tag）。
 - 不引用 docs/api-reference.md 里过时的 CORS 描述（该文件虽已在分支上修正，仍以代码为准）。
 - 不要把「测试全绿」当作某条 BLOCKED 已被验证的证据。
 - 拿不到证据就写「不可验证」并说明卡在哪，不要推断。

唯一允许写入的文件：
  D:/Projects/松光启动器/docs/evidence/2026-11-08-m22/清账表.md

报告结构：
 ① 一句话结论（A/B/C 各几条）
 ② 清账表（表头：编号 / 条目 / 结论 A|B|C / 证据 / 若不验证的理由或后续事项定义）
 ③ 与上一轮验收（R5b）不一致之处
 ④ 未验证项（连清账本身都没做成的，写明为什么）
 ⑤ 下一步建议（按性价比排序，最多 3 条）
```

---

## 技能与规则（开工前先确认已加载）

Codex 侧有两层规则会自动生效，开工前先确认它们存在：

1. `C:/Users/Administrator/.codex/AGENTS.md` —— 全局工作与验收约定（结论先行、事实/推断/未验证分开、技术通过不自动授权）
2. `D:/Projects/松光启动器/AGENTS.md` —— **项目级规则**（构建命令、1379/481 基线、硬约束、平台缺口、环境陷阱）

这个任务该用的 Codex 技能（在 `C:/Users/Administrator/.codex/skills/` 下，按需加载）：

| 技能 | 用途 |
|---|---|
| `csharp-expert` | 判断 C# 侧代码语义（如 `PathSafetyGuard` 的 Win32 依赖）时用 |
| `run-tests` | 任何要跑 `dotnet test` 的场合先用它，避免自创命令 |
| `frontend-testing-debugging` | 涉及前端 481 项清单口径时用 |

**不要加载 `csharp-refactoring`**：那是改写用的技能，本轮是只读判定，加载了容易越界去改代码。

报告末尾加一行「本轮使用的技能/规则文件」，列出你实际读了哪些。

## 你要做的

1. 贴进去，等它写完 `docs/evidence/2026-11-08-m22/清账表.md`
2. 回来只说一句「第 2 步做完」，**不用转述内容**——我直接读文件
3. 如果它把报告写到了别处或只输出在对话里，让我知道，我按它输出补文件（不采信对话里的「我已完成」）

## 判断它有没有偷懒的三条

- 表里 14 条是否都在（缺一条就是漏做）
- 每条是否有**具体证据**（命令/行号/日志路径），而不是「已确认支持」
- B 类（明确不验证）是否有理由，而不是一句「环境不具备」
