# 可复制给 DeepSeek Harness 的 H2-03 收尾提示词

以下内容需用户明确交给执行者后才构成实施请求；本轮未向其他任务发送消息。

```text
请在D:\松光启动器仅收尾独立审查R3-01/R3-02。先读docs/harness-independent-acceptance-2026-10-17-run03/README.md、cdp-wiring-v2.json和remediation-workflow-2026-10-17.md，遵循用户指定AGENTS/独立验收规则。基线HEAD为73ad9c4a6def0d967127e8a3a86fe95c96372a94，先核对当前dirty，保留旧证据，不reset/clean/restore/stash/stage-all。

H2-01和H2-02已独立通过，Node380/380、正式十页10/10。不要回退十页契约或可见破图修复。问题仍在H2-03的正式CDP接线和恢复。

R3-01：evaluate只接收expression，Runtime.evaluate设置returnByValue:true。listOffscreenExpr返回DOM元素后变成[{}]；evaluate(scrollOneExpr,target)丢弃第二参数，scrollOneExpr是未调用的箭头函数，结果{}而非滚动结果。独立反例中scrollTop0、HTTP图片请求0、loaded0；在页面内显式查询元素并调用同一LAZY_SCROLL_FN后loaded1。当前Python测试用Playwright活ElementHandle直接调用，绕过了有故障的正式CDP路径，不能作为主流程通过依据。

请在页面内完成目标查询和调用，或用真实CDP对象句柄调用，保留逐项等待与结果核对。不要只给evaluate增加JSON参数，因为DOM身份前一步已丢失。联测必须走正式包装器、目标选择和实际调用点，并验证进入视口/HTTP请求/加载结果，不抄一份测试专用算法。

R3-02：LAZY_RESTORE_FN将所有容器与window清零，没有保存原值。反例从scrollTop137开始，恢复后实际0。请首次触碰时保存原始位置、在finally恢复被触碰对象、清理状态，覆盖非零纵横/窗口位置和异常恢复；原用例从0回0不能证明恢复正确。

允许修改正式界面验证器、其直接相关测试、报告和新证据。产品UI/后端/依赖/运行时/.github/真实用户数据只读，不改系统服务、驱动、防火墙、TLS或执行策略。

先以本轮反例做有效RED，最小修改后做真实CDP联测、正常/失败恢复、破图与pending边界，再跑全部Node、资源闭包和正式十页原生WebView。未触及产品/C#时不机械重跑957项C#或更新下载。保留真实退出码/数量/skip、所有旧RED和新GREEN、输入/EXE哈希、布局/请求/滚动对照和截图。

停止在“具备独立复验条件，等待Codex独立验收”，不提交、推送、打tag、发布或自批准。
```
