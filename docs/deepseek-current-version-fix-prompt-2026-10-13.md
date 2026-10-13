# DeepSeek Harness 当前版本修复任务包

你是用户指定的实现者，Codex 负责需求、架构与独立验收。用户已授权完整修复当前版本缺陷，但本包只覆盖 CV-01 至 CV-03；后续 UI 设计由 OpenDesign Cloud 提供，不自行重设计。

SourceRoot 与 cwd：`D:\松光启动器`。HEAD：`1829f1c106bb32c7c894eca8f288f1d24e79447b`。先读取 `docs/current-version-remediation-workflow-2026-10-13.md`。已有未跟踪 docs 均为用户资产，不删除、不覆盖、不提交。

允许修改：
- `scripts/verify-source-snapshot.ps1` 的路径集合/数量去重逻辑与必要注释；保留全部缺失、哈希、泄漏、锁定、测试和退出码门槛。
- 新建 `.gitattributes`，只精确约束 `dev/PineglowLauncher.Tests/TestAssets/Diagnostics/diagnostic-cases.json` 为 `text eol=lf`。
- `dev/PineglowLauncher.Tests/InstancePackTests.cs` 的缺失 formatVersion 测试夹具构造，保留实际缺字段、产品调用和结果断言。
- 新建 `docs/current-version-audit-2026-10-13/harness-fix-report.md`、本轮自有的验证请求/证据。

禁止改冻结案例内容、FrozenSha256、案例断言、任何其他产品代码、依赖、lock、CI 工作流、用户配置、历史报告或原始失败。禁止 Git 暂存/提交/推送/发布、reset/clean/restore/stash、goal 无限执行、其他会话或进程控制。

根因已经独立核实：
1. trackedCopied=302，whitelistCopied=163，实际唯一输入=302，直接相加=465 导致误 FAIL。以唯一规范相对路径集合计数，正确处理重叠；不得让缺失/泄漏假通过。
2. Git core.autocrlf=true 会把冻结 JSON 检出为 CRLF；LF 原始字节的正确冻结哈希为 D9F55189FA05845BAC0403A058AA63912A725AFF61555BDF25A6865DD5AB45AF。禁止更新基准掩盖问题。
3. MinimalIndex.Replace 中硬编码 LF 在 CRLF 源码编译后不删字段。用结构化 JsonNode 删除属性或与换行无关的精确替换，确保字段实际缺失并调用 ParseModrinthIndex。

最小实施顺序：read 当前文件 -> 解释最小方案 -> edit -> 重新 read 核对变更 -> 写报告。你的 pwsh 工具已两次 0xC0000142，read/glob 正常。可以使用 read/edit/write 完成实现；不要无区别反复尝试坏 shell。将必须执行的精确验证命令写进报告，由 Codex 用正常 shell 独立运行。没有实际执行的检查标 NOT_RUN，不声明 GREEN。

原始 RED 在聊天工作树 `docs/current-version-audit-2026-10-13/baseline-gate`；D 盘独立 baseline 正在另一个冻结快照运行，请不要干预。修复需保留 RED 并新增 GREEN。每次写之前重新读取，避免 FS_STALE_VERSION。

报告包含：改动文件、根因、具体变更、逐项验证/未验证、外部依赖、遗留问题。结尾必须为：具备独立复验条件，等待 Codex 独立验收。
