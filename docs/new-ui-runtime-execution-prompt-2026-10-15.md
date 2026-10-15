# 可复制后续执行提示词

```text
请在 D:\松光启动器 继续完成新版 UI 的运行时兼容与完整 C# 回归闭环。

先读 docs/ui-integration-2026-10-15/README.md、docs/new-ui-runtime-remediation-workflow-2026-10-15.md、最终源码哈希和所有原始失败日志。新 UI 已经接入，不要重做设计，不要把原型模拟数据库搬进产品。

先核对 git status --short --untracked-files=all、HEAD、适用规则及现有变更。所有既有改动、未跟踪材料、RED 和中止日志均需保留。不要 reset/restore/checkout/clean/stash/stage-all，不提交、推送、发布，不修改真实账号配置或系统网络/安全设置。

需要定位两个具体问题：
1. 本机 .NET 10.0.9 引用产品依赖时 HttpListener.Start 抛 Win32 6，而显式 10.0.0 的相同探针成功。逐层缩小依赖/初始化差异，不能直接宣布是官方运行时缺陷，也不能仅为变绿永久固定旧补丁。
2. 指定 DotNetHostPath 和 runtimeconfig 后 C# 全量完成 507 项就长期无进展，现有 TRX 是主动停止后的中止结果。加入每用例进度和有界等待，定位剩余活动用例，不能把 507 通过当成 944 全部通过。

允许读取、复现、运行隔离测试与必要的最小修复。生产允许路径限于证明必要的启动/运行时兼容代码、相关测试和项目依赖配置；ui/css/redesign.css、ui/js/playtime.js、其余新版 UI 默认只读，确有回归须先用有效 RED 定位。证据写入新的日期化 docs 子目录，不覆盖本轮失败证据。

验证必须包含：JS 语法、测试清单双向校验、静态资源闭包、完整 Node 与 C# 回归、最新构建独立冷启动真实本地 API、十页 WebView2 及一个隔离游戏正常退出后的会话采集。不要动真实游戏目录，不用 mock/健康 200 代替界面和业务证明。只停止本轮明确拥有的测试/预览进程。

完成后分层报告静态、自动化、真实 WebView2、外部账户/游戏、干净 CI/发布的证据范围。FormalPhaseApproval=NOT_GRANTED，停止于可供独立复验的本地结果。
```
