# 可复制的限定返工提示词 — 2026-10-07

以下供用户授权后复制给 MiniMax/DeepSeek Harness。本文件不是自动执行授权。

```text
在 D:\松光启动器 执行客户端识别边界的限定返工。先读：
docs/harness-independent-acceptance-review-2026-10-07.md
docs/harness-independent-acceptance-remediation-workflow-2026-10-07.md
以及用户提供的三份 Codex 执行/独立验收规则和适用 AGENTS.md。

当前独立验收 NOT_PASSED，正式阶段/发布批准 NOT_GRANTED。不得提交、推送、打 tag、发布、reset/restore/checkout/clean/stash、批量暂存、改用户原配置、覆盖历史证据、清理原缓存或修改会话 goal。先核对实际 HEAD、脏工作区与输入哈希，出现漂移先报告。HEAD 基线 f145d14eb26b0b7459a28491e764ac8c573085d3。

只修 LauncherService.cs 的当前替代客户端识别及必要的明确拒绝路径，并补现有 LoaderLaunchAssemblyTests.cs；确需新测试文件才追加快照白名单。不改已通过的 Java 选择、下载器、UI、账号或其他模块。

已证实问题：SuppliesOwnClient 第455–459行递归扫描整个相关 libraries 目录，IsClientJarName 第466–468行把任意 *-extra.jar/*-client.jar 当作当前替代客户端。实际公共 BuildCommandAsync 在当前1.21.1 profile缺少当前替代客户端时，添加不被profile引用的 unrelated-tool-extra.jar 或旧版1.20.1 client-extra.jar，就错误移除父版客户端jar。

原独立证据目录：
C:\Users\Administrator\.codex\worktrees\e423\松光启动器\docs\harness-independent-acceptance-evidence-2026-10-07
查看 BoundaryProbe.cs、boundary-results.json、boundary.log。必须保留这份原 RED。另有真实Forge1.20.1 client-extra.jar为13517个ZIP条目、0个.class的证据；文件名/存在不能独自证明类供应链可用。

先在新GUID隔离副本用真实 BuildCommandAsync 将两项反例做成RED，另覆盖当前产物缺失和仅资源extra jar。再从当前版本/加载器安装元数据准确证明实际客户端供应链，不允许靠无关缓存文件静默去掉原版jar。当前供应链缺失时，合法回退保留原版jar，或装配前返回明确缺失错误；禁止生成错误-classpath或自动下载修复。

GREEN须保留正常Forge/NeoForge行为、重复JVM选项、占位符、Fabric原版jar和Java选择回归。新快照重跑聚焦测试、全部Node/C#、统一gate、单文件EXE19项冷启动；RID locked restore NU1004保留为未通过边界。使用新的独立构建在自有离线副本重验Forge1.20.1、NeoForge1.21.1、Vanilla1.18.2、Fabric1.18.2、Quilt1.20.1、Fabric1.21.1，记录真实Java/命令行/新鲜日志/渲染初始化/观察期存活。不得把35秒结果写成进入世界或长时游玩验证。

启动取证脚本成功显式exit0，失败非零；可选配置字段先检查。只清理经路径确认属于自有GUID根的进程及子进程。保留原RED、新GREEN、TRX执行数/失败/跳过、源码前后SHA、发布EXE SHA、原始退出码和全部限制。完成后停止在：具备独立复验条件，等待 Codex 独立验收。不要自动推进下一阶段或宣称可发布。
```
