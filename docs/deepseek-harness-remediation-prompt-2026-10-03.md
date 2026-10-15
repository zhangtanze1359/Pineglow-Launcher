# 可复制给 DeepSeek Harness / 执行 AI 的修复提示词

本提示词是 2026-10-03 验收后的交接产物。只有用户明确要求你执行本提示词，才开始实施；文档存在不构成自动实施授权。本次 Codex 尚未调用任何执行工具。

---

你负责松光启动器的一个有边界修复批次。先读以下文件并遵循其约束：

1. `C:\Users\Administrator\Documents\Codex\2026-09-25\gen-2\outputs\项目协作规范-2026-09-25.md`
2. `C:\Users\Administrator\.codex\worktrees\ed41\松光启动器\docs\deepseek-harness-acceptance-2026-10-03.md`
3. `C:\Users\Administrator\.codex\worktrees\ed41\松光启动器\docs\deepseek-harness-remediation-workflow-2026-10-03.md`

**范围：只执行 R0、R1、R2；R3 目录打开行为不在本批。** SourceRoot=`D:\松光启动器`；DeliveryRoot=`C:\Users\Administrator\.codex\worktrees\ed41\松光启动器`。建立新的隔离源码快照，在 `DeliveryRoot\docs\market-execution\save-backup-contract\<UTC-run-id>` 写新证据。不得把 ed41 的旧生产源码作为事实来源。

W1 修复已独立技术通过，不要推倒重做。现有独立回归为 C# 628 / 628、Node 184 / 184、路由 16 / 16、Bisect smoke 10 / 10。独立 15 项探针中 13 通过 / 2 失败：

- 只备份 worldA，GET worldB/backups 却返回 worldA 备份。
- POST worldB/backups/{worldA备份名}/restore 返回 200，并生成 worldA 副本。

这两个已复现问题的根因是 LocalApiService 的列表 handler 返回实例全量备份，恢复 handler 在全量集合中只按 FileName 选择，均忽略 folderName。handler 在 W1 改前已存在，不要写成 W1 新引入或数据覆盖；当前恢复会创建新副本。

另外，旧 run `20261002-182304Z` 的 manifest files 中包括它自身的旧 SHA/6475 B，实际 manifest 为 8825 B。其他 25 项均匹配。保留旧目录全部字节，在新 run 追加 corrected-manifest 与 detached SHA；不在 manifest files 中包含自身。

源码基线：HEAD=`f145d14eb26b0b7459a28491e764ac8c573085d3`；验收时 D 盘 main、git status 1290 项。`LocalApiService.cs` SHA=`F9F662A85922C08AFF7715B1F3C0179729FD1FC46401474D2EC816108DD3FF77`。开始前再次读取状态和哈希，变动则识别归属，不能覆盖别人修改。

**允许修改的精确文件：**

```text
D:\松光启动器\dev\PineglowLauncher\Pineglow.Launcher.Services\LocalApiService.cs
D:\松光启动器\dev\PineglowLauncher\Pineglow.Launcher.Services\SaveBackupService.cs
D:\松光启动器\dev\PineglowLauncher.Tests\SaveBackupApiContractTests.cs
D:\松光启动器\scripts\source-snapshot-inputs.txt
D:\松光启动器\docs\api-reference.md
D:\松光启动器\docs\market-task-plan-2026-10-03.md
```

第二个服务文件仅限对象归属相关辅助；LocalApiService 仅改备份列表与恢复 handler。保留路由精确长度、会话令牌、ZIP 安全和新副本恢复策略。不改 shared LocalApiTestHarness 或原 16 项测试，复用它们。源目录的应用落点须符合本次用户给你的明确执行授权；默认先在新快照实现，复核基线后按最小补丁交付。

新增证据与交付仅可写新 run 目录及其文档，不可覆盖旧证据。禁止修改其他生产源码、UI、AI、依赖、packages.lock.json、Mod 删除或 Bisect 行为。禁止 reset/restore/checkout/clean/stash/add-A，禁止提交、推送、发布、publish、部署、MR03–MR07/MG1 推进、付费模型调用和外部发消息。

按顺序执行：

1. 保存允许文件原件、SHA、git 状态。精确 tracked+白名单构造冷快照；验证未跟踪非空 C# 输入没有遗漏，禁止整目录打包未知文件。
2. 复核旧 25 项证据，追加纠正 manifest+detached SHA，校验失败须非零，不把旧 manifest 自身再放进清单。
3. 在新 `SaveBackupApiContractTests.cs` 写真实 HTTP RED，复用内存配置、动态本地 listener、自建合成目录和真实配置 SHA 护栏。参照 `DeliveryRoot\docs\market-acceptance\20261003-W1-W3\IndependentRouteProbes.cs` 的两个失败用例。原始响应与 TRX 必须证明问题存在。
4. 以实例及存档归属约束备份列表和恢复选择。错对象返回受控 4xx，无任何写盘；不可只使用文件名或路径前缀。测试净化名称冲突、中文空格、备份不存在以及原存档被删除后从正确备份恢复。保持恢复成新副本，校验 level.dat 原字节、原位修改字节与另一个世界都保持。
5. 逐条运行正式契约测试、原 RouteReachabilityTests、C# 全套、Node 正式 11 文件、VerifyHost 构建及 --bisect-smoke。明确记录 C# 新实际总数，不预填数字。旧 628 项不得删减或跳过，Node 必须184，路由必须16，smoke必须10。
6. 复跑完整独立 15 项探针，必须全绿。所有测试只在隔离副本和合成世界中执行，禁止操作用户真实存档。
7. 白名单补入新增正式测试；源码应用前再核对原文件 SHA，冲突时停止。同步 API 的按存档隔离与新副本恢复语义，以及台账证据状态。
8. 写 deliverable.md、最终 diff、逐命令 JSON（cwd、命令、开始/结束UTC、exit、测试数）、真实配置护栏、manifest、detached SHA、未验证项、原件回退说明。旧 RED 保留，GREEN 必须对应最终字节。

关键命令在新快照根逐条运行，任何 GREEN 命令非零就停止：

```powershell
dotnet test dev/PineglowLauncher.Tests/Pineglow.Launcher.Tests.csproj -c Release --filter FullyQualifiedName~SaveBackupApiContractTests
dotnet test dev/PineglowLauncher.Tests/Pineglow.Launcher.Tests.csproj -c Release --filter FullyQualifiedName~RouteReachabilityTests
dotnet test dev/PineglowLauncher.Tests/Pineglow.Launcher.Tests.csproj -c Release
dotnet build dev/PineglowLauncher/VerifyHost/VerifyHost.csproj -c Release
dotnet dev/PineglowLauncher/VerifyHost/bin/Release/net10.0/VerifyHost.dll --bisect-smoke --port 49138
```

Node 用正式文件数组运行 `node --test` 并保留枚举名单；RED 期望 exit=1 是有效证据，必须显式区别于 GREEN。不要因输出重定向或中间脚本错误而只检查最后一条命令的退出码。

达到条件后最终只报告：**“具备独立复验条件，等待 Codex 独立验收”**。列出允许文件实际改动、RED→GREEN、完整回归、最终 SHA、未验证 GUI / Explorer / 游戏 / CI / RID 发布。不要自称独立验收已通过，不要要求或执行 MG1/发布。
