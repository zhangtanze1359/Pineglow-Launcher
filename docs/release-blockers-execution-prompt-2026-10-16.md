# 可复制执行提示词

以下内容供用户在决定开展运行时修复后使用；生成本文件不等于已经向执行者发送或授予实施权限。

```text
请修复 D:\松光启动器 的发布冷启动阻断，按证据调查、最小实现、自检后交付独立复验材料。不要提交、推送、打标签、上传 Release 或修改系统安全设置。

先读：
1. docs/release-preparation-2026-10-16/README.md、standalone-cold-start-report.json、first-launch-exception.txt、second-launch-exception.txt。
2. docs/release-blockers-remediation-workflow-2026-10-16.md。
3. docs/feature-audit-2026-10-15/run-fix-20261015-01/README.md，以及当前 git diff。
4. 用户指定的 2026-10-03 Codex 执行与独立验收规则和适用 AGENTS.md。

基线 HEAD 是 1356efa70cfd91f6289ee6e0dc6af5160b444ab3。工作区包含已完成但未提交的 FA-01 至 FA-05 修复，必须保留。先记录当前状态、源码输入和哈希；不要 reset/restore/checkout/clean/stash/stage-all，不删除旧日志或未知文件。

已确认 RED：本轮 1.0.1 win-x64 自包含单文件 EXE（内置 .NET 10.0.9）在两个全新 portable 中文目录中，HttpListener.Start/SetupV2Config 抛 HttpListenerException(6) 句柄无效；进程/窗口存在但 API 不就绪。先查清运行时、产品依赖初始化、操作系统之间的因果，不把旧运行时探针成功直接当成正式修复，不用 mock、禁用真实 API 或跳过健康门槛掩盖。

允许范围：根因证明必要的启动/兼容代码和对应回归；scripts/verify-single-file-standalone.ps1 的失败报告路径；release-notes/v1.0.1.md 的本轮完整说明；必要测试登记和本轮证据文档。依赖/服务器架构变更若超出最小启动修复，先提交具体方案与影响，暂停相关实现直到用户授权。不要改真实配置、凭据、账户/游戏数据、皮肤或无关业务功能。

另一个已确认问题：原冷启动脚本 $served 只在 API 健康成功分支初始化，失败时 StrictMode 使 JSON 无法落盘。修正后既要真实失败时 exit 1+FAIL JSON，也要正常成功路径通过，不能只是消除异常。证据目录两份副本仅供诊断，正式脚本尚未修复。

发布介绍草稿已在 docs/release-preparation-2026-10-16/v1.0.1-notes-draft.md，修复完成后核对版本并同步正式介绍；不上传当前失败 EXE，不把它的哈希用于新版 manifest。

自检：聚焦真实 HttpListener 回归；有界诊断后完整 C#；按 scripts/test-inventory.txt 全部 Node（当前基线379/379，28文件）；UI资源闭包和清单双向一致；新产物只带EXE在全新目录冷启动，真实API+关键UI资源与源码一致；原生WebView正常页操作、失败恢复、退出重启；对应更新替换/回滚门槛。区分单元/mock/真实服务/原生WebView/外部账号与游戏/远程CI的证据，未验证就明确说明。

仅启动和停止本轮可识别的隔离进程，使用独立端口、portable.flag、隔离TEMP，后台辅助进程隐藏窗口。不关闭用户用途不明的现有进程，不修改防火墙、TLS、URL ACL 或全局运行时。

保存所有原始RED和GREEN、命令退出码、测试数与skip、源码/二进制哈希及对应路径到新运行目录；不要把部分通过或测试中止称为全量通过。最终逐项报告问题根因、变更、实际验证与未验证，停止在“具备独立复验条件，等待 Codex 独立验收”。不得自行正式放行或发布。
```
