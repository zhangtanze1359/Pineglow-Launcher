# DeepSeek Harness 最新阻断修复计划

目标：修复 session-466887a4-24c0-4da2-b92e-a6d4801f1be2 等待授权的四项产品缺陷，并独立复验。产品执行根为 D:\松光启动器；报告与证据根为本 worktree。技术栈为 .NET 10、xUnit、PowerShell、Node。

架构：在配置初始化处只原子写 JSON，避免调用凭据保存；在版本解析器合并父子参数；在 Maven 地址生成处去掉重复 artifact 段；安装器启动前创建最小 profile，已有文件保持不变。保持此前 R01–R06 的修改。

规格依据：当前 Harness cache 的 blockedReason，以及 D:\松光启动器\docs\release-remediation-evidence-2026-10-05\run-20261006-2130-patch-set。补丁只是待审查输入。

- [x] 保存原源码、工作区状态和阻断状态，禁止覆盖会话历史。
- [x] 编写隔离回归测试，运行修复前 RED 并保留失败。
- [x] 修复 ConfigStore、VersionResolver、InstallerLoaderService、LoaderInstallHelper；补齐精确快照输入。
- [x] 运行聚焦 GREEN；运行新的隔离快照全套门槛，核实执行数和退出码。额外修复 run-goal-validation 的 Get-FileHash 依赖。
- [x] 交付修复报告、剩余任务及执行提示。正式提交、推送、发版、真人账号与 AI 密钥验收仍需对应条件。

授权：用户 2026-10-06 明确要求“查看该项目 deepseek harness 执行受到的最新阻断，并修复”。本计划不授权正式发布，也不将自动化 PASS 视作完整产品验收。
