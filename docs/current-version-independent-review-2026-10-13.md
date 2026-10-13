# 当前版本缺陷修复与独立验收结论

日期：2026-10-13，Asia/Hong_Kong。SourceRoot：D:\松光启动器。

**本轮发现的八项缺陷已修复，独立技术验收通过，范围为 PASS_SCOPED_CV01_CV08。** 已取得验收通过后直接本地提交全部已审查改动的用户授权；实际提交号以本仓库 git log -1 为准。没有推送、部署或正式发布授权。

OpenDesign Cloud 完成启动、下载恢复、失败提示的局部界面设计，完整四文件原型保存在 docs/opendesign-ui-artifact-2026-10-13。Harness 完成首批和部分UI实现；其剩余短任务反复TIMEOUT后，用户明确授权由Codex定向返修，并由未参与实施的独立子代理验收。设计样例没有成为生产数据。

| 修复项 | 当前行为 |
|---|---|
| CV-01 快照重复计数 | tracked与白名单按唯一集合计数，缺失/泄漏/哈希门槛保留 |
| CV-02 冻结数据检出换行 | 精确LF属性保持原冻结SHA，不改案例或基准 |
| CV-03 CRLF测试夹具 | 按JSON结构真正移除缺字段，不依赖源码换行 |
| CV-04 下载错误链 | 真实API严格模式传播失败，错误不伪装空列表，保留本地版本及任务 |
| CV-05 新闻验证时间 | 默认/无效时间显示尚未验证，有效时间保留 |
| CV-06 错误状态UI | 可读主说明、真实重试、原生技术详情、文本转义与窄窗口样式 |
| CV-07 验证器 | CDP正常拒绝、完整中文JSON错误分类、token/Cookie/短认证头脱敏 |
| CV-08 启动检查保护 | 检查异常/null/明确失败不启动，真实重试，警告确认及防连点 |

实际验收：聚焦42/42、独立生产调用链反例7/7、全量Node347/347、C#941/941、单文件冷启动19/19、真实原生WebView受控HTTP流程13/13，测试零跳过。最终隔离输入305项，SourceRoot哈希漂移0。非RID锁定还原、Release、VerifyHost、普通单文件publish通过，使用已核对完整性的本地NuGet源，没有修改全局配置/源lock/安全检查。

默认联网九页巡检的原始结果仍为 **FAIL / exit1，6/9成功**；内容、下载和新闻三页落地外部依赖错误终态，无待定loading。未捕获JS异常、console.error、UI资源失败、未知本机接口失败均0。它没有被改记为9PASS。本轮八项关闭标准已由生产调用链和真实WebView受控失败/恢复验证，但在线资源、安装、账号和游戏端到端成功未获证明。

默认在线NuGet源还原未完成；win-x64 RID锁定publish仍为已知NU1004，普通publish通过。联网证书撤销/信任链、微软/第三方登录、AI服务、进入游戏世界、远程同SHA CI及发布均没有本轮放行。FormalPhaseApproval=NOT_GRANTED；ReleaseApproval=NOT_GRANTED。

最终单文件产物53,060,914字节，SHA256 aa1f65471e9f9c2bbeb20c5a86d593bffaac2b4f823620fbb0ef0f2248259a1d；真实WebView两次复制产物的哈希与之相同。普通RIDpublish仅改动隔离快照lock为RID形状，D盘源lock保持不变。

详细结果：[最终验收](current-version-audit-2026-10-13/final-acceptance.md)、[独立子代理报告](current-version-audit-2026-10-13/independent-agent-review.md)、[完整快照汇总](current-version-audit-2026-10-13/final-independent-snapshot-cache/verify-summary.json)、[真实WebView报告](current-version-audit-2026-10-13/final-webview-states/report.json)、[默认九页原始结果](current-version-audit-2026-10-13/final-nine-page-ui.json)。

历史RED、Harness原始会话、网络等待和工具限制日志均保留在本轮audit目录。原先隔离缺文件探针的删除命令被工具policy明确拒绝，未执行或绕过；只读生产收集逻辑四边界和完整快照通过，物理删除方法仍LIMITED。

提交仅按commit-scope.json列出的已审查路径进行；不用stage-all，原有2611项历史未跟踪材料保持原位。未经脱敏的旧GUI日志、Harness思考日志、工具二进制和包不进入提交。
