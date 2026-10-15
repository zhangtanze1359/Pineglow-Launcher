# 可复制的 Harness 恢复执行提示词

日期：2026-10-11。此提示词尚未发送到执行者，不表示当前已授权实施。供用户决定开展恢复后复制使用。

```text
请在 D:\松光启动器 完成 DeepSeek Harness 执行恢复的有限闭环。

先读 docs/deepseek-harness-execution-diagnosis-2026-10-11.md、
docs/deepseek-harness-execution-recovery-workflow-2026-10-11.md 和
docs/harness-execution-diagnosis-evidence-2026-10-11/summary.json。
原会话：session-98527af6-2b06-4bf8-9c00-e4bc727dd861。
原运行目录：docs/release-closure-evidence-2026-10-10/run-2026-10-10_015215-578dbafb。

本次授权正常界面内有界的会话恢复验证、必要的短会话诊断对照、
有效 profile 只读检查、以及新证据目录中的隔离探针副本校正和复测。
不授权产品代码变更、原始会话/日志/探针覆盖、profile 配置变更、
批量结束用户进程、创建或重启长期目标、提交、推送或发布。

1. 固定 HEAD、git status、原始记录和关键文件哈希，保护既有脏工作区。
2. 注意原会话已经成功 compact；压缩后没有续跑证据，goal 已 clear。
   先做一次无工具、无文件改动的短响应测试，核对真正收到 assistant/message。
   若失败，最多再做一个同路由/同等级的短新会话对照并保存错误，不循环恢复。
   没有服务端正文不能断言唯一根因；有 request ID 则保存，密钥不得输出。
3. 只读核对 compaction-basic 合并后的有效策略。
   文件已有 opencode-go/deepseek-v4.1-flash 的0.30/0.08，不重复追加。
   解释有效配置与文件是否一致，无法核实时写未验证。
4. 保留原 probe-range-resume.ps1 和 exit4记录，在新独立副本中重新读取后编辑。
   保留UTF-8 BOM；保留 jarMatchesOriginal 检查；exit4检查改用 messagesSeen
   中的“继续下载”。解析通过后在隔离数据目录实际重跑，不改旧结果成PASS。
   仅清理本轮拥有的实例和临时资产，记录进程所有权和清理结果。
5. 报告模型可响应情况、配置有效性、探针结果、变更清单及当前未完成项。
   JAR校验不能替代整个安装、HTTP206抓包或跨重启队列恢复。
   不把其他执行者的 A+B 自检报告当作 Codex 独立验收，也不扩大原任务授权。

停止点：完成上述恢复诊断和隔离工具复验，留下具体可复验检查点，
等待独立验收；不自动恢复长期项目目标或开始相邻产品修改。
所有实际命令、退出码、失败和未验证项保存到新日期化证据目录。
FormalPhaseApproval=NOT_GRANTED；ReleaseApproval=NOT_GRANTED。
```
