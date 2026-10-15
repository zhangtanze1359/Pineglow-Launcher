# 可复制给 DeepSeek Harness / MiniMax 的限定修复提示

以下内容供用户决定采用后发到对应执行会话；本文件的生成没有自动发出消息或授权执行。

请继续 D:\松光启动器 项目，仅修复并复验当前 goal-0388dcd6-b7b0-4b59-9bdd-4513efbc0648 的两项本地源码阻断：Forge/NeoForge JVM参数/占位符/类路径装配，以及Java自动选择未读取实例版本元数据。请先读 docs/harness-blocker-diagnosis-2026-10-07.md 和 docs/harness-blocker-remediation-workflow-2026-10-07.md。

若此提示由用户发送，授权本轮限定修改 LauncherService.cs、LocalApiService.cs、对应聚焦测试及 source-snapshot-inputs.txt，并保存新的证据与报告。保护已有47项tracked修改、昨天五项修复与所有未跟踪资产。不得reset/restore/checkout/clean/stash、批量暂存、提交/推送/tag/发布；不改缓存goal/压缩会话/真实配置/凭据。

现成补丁在 run-20261007-3800-forge-launch-patch 与 run-20261007-4200-java-version-fix。先审查，不盲用：保持重复JVM选项与值，补齐占位符，避免自带client的profile与原版client模块冲突；Java选择走实例目录和元数据，未知主版本不能静默视为兼容。不要只用临时修正命令行证明产品已修好。

先写隔离回归并记录实际RED，再最小修复与GREEN；覆盖Forge/NeoForge、Vanilla/Fabric/Quilt回归、元数据继承、Java21缺失/未知及中文空格路径。新测试文件必须列入精确快照输入。使用新GUID候选快照跑完整门槛，核对实际执行数、skip、退出码和产物，再记录新EXE哈希。

用新构建重跑Forge1.20.1/NeoForge1.21.1以及Vanilla/Fabric/Quilt真实启动，并补1.21+ Fabric/Quilt的Java选择。保留所有失败；只进渲染管线不能声称已进入世界或长时游玩。不要把旧临时补丁构建7/7代替当前源码的独立验证。

账号、AI Key、Gitee token和正式发布授权缺失时，把对应项目分别列BLOCKED/NOT_RUN；这些不是本轮本地修复的前置条件。完成限定修复和交付后停止，不无限等待或重复申请同一范围授权。终态为“具备独立复验条件，等待 Codex 独立验收”，不自动宣布可发布。
