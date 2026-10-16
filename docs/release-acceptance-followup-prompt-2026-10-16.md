# 可复制给 DeepSeek Harness 的收尾提示词

以下内容需由用户明确交给执行者后才构成实施请求，本文件生成本身不授权代码修改或向其他任务发送消息。

```text
请在 D:\松光启动器 收尾本轮发布验收的 HA-01/HA-02，仅修正式验证器和报告，不改已通过的业务功能，不提交/推送/打标签/发布。

先读 docs/harness-independent-acceptance-2026-10-16-run01/README.md 和 docs/release-acceptance-followup-workflow-2026-10-16.md，并遵循用户指定的 AGENTS/独立验收规则。受验基线 HEAD 为3ab6b0c4fa7bff1bf115391df9414af9436b9adb，开始先核对当前状态并保留全部未提交材料。

已独立通过：Node379/379、C#957/957、非RID锁定还原、全新单EXE冷启动18/18、RP-02真实失败时FAIL JSON与exit1、RP-03完整说明、真实AI保存与持久化/恢复、下载源弹窗、默认更新源GitHub备用清单。旧EXE同SHA移到中性目录也正常。不要再盲改后端/运行时。

HA-01：正式 scripts/verify-ui-pages-live.cjs 统计了所有img。内容库24个img被.mod-icon display:none隐藏、布局0；皮肤页#localSkinTexture是hidden、无src占位。正式脚本因此exit1。只修图片统计的有效可见边界；隐藏占位不能误报，可见破图/真实未完成加载仍要失败，不能加--allow-missing-images绕过。正式VIEWS同时漏了playtime，请补入并验证十页。

验收目录的 verify-ui-pages-visible-images.cjs 是隔离诊断副本，只供参考；它已取得10/10，但不能声称正式脚本通过。为隐藏/无src/祖先隐藏/可见破图/仍加载增加有布局的最小行为验证，保留原脚本RED再做GREEN。

HA-02：在 docs/release-prep-verify-2026-10-16/README.md 追加更正，保留历史原件：ui-live-report.json最终是FAIL；rp02-failpath-report.json其实PASS，不能证明失败路径，本轮Codex的current-workspace.json才是真实FAIL补证；路径差异已证明，CodexSandboxService/bindflt因果未证实，不能断言任意用户都不受影响。空格路径通过argv数组可正常执行，不要未复现就改parseArgs。

允许修改：正式界面验证脚本、对应最小测试/登记、执行报告和新证据目录。产品源码、CSS、后端、项目依赖/运行时、系统设置、真实账户游戏文件只读。禁止reset/clean/restore/stash/stage-all，禁止删除或覆盖旧RED，禁止关闭未知进程。

修改后跑Node清单、资源闭包、正式十页原生WebView，检查真实exit、问题列表、JS异常、截图、脱敏和进程清理。输入不涉及C#时不机械重跑全部957项。用新的中性执行目录记录SourceRoot/ExecutionRoot/EvidenceRoot、HEAD、dirty、源码与EXE哈希，不将副本通过称为远程CI通过。

完成后交付实际变更与原始验证材料，停止在“具备独立复验条件，等待Codex独立验收”。不自动提交、发布或进入真实账户/游戏/升级应用流程。
```
