# 时长分布层级树交付

已将用户截图中的矩形分块改为连接线层级树：**实例 → 日期 → 单次游玩**。只改这块时长分布区域及其中文/英文文案，保留原范围、日历、摘要、空态和错误态。

实例/日期使用原生details，可鼠标或键盘展开；提供展开全部/收起全部。每次记录显示时间段与时长，聚焦显示完整读出，点击记录继续按日期筛选。颜色只保留实例标记点，原矩形色块不再渲染。跨日会话仍按实际日期分片，汇总按同一会话去重。

- SourceRoot：`D:\松光启动器`
- ExecutionRoot：`D:\松光启动器\docs\ui-fix-2026-10-15\native-final`（用户此前打开的隔离预览）
- EvidenceRoot：本目录
- HEAD：`ea6d1cbf145bd4ae479d9f4774dba468351a91a8`，原脏工作区保留，无提交/推送/发布。
- 改动：`ui/js/playtime.js`、`ui/css/redesign.css`、`ui/js/i18n.js`、`ui/tests/playtime.test.cjs`。

验证：

| 检查 | 结果 | 证据 |
|---|---|---|
| 分层函数与原统计算法 | 7/7 | `tree-red.txt`、`tree-green.txt` |
| UI Node全套 | 304/304，0失败、0跳过 | `node-final.txt` |
| 构建 | 成功，0警告、0错误 | `build-final.txt` |
| 原生WebView2 | 19/19，960/1280/1600三尺寸，0未捕获JS异常 | `native-report.json`、`native-final-log.txt` |
| 长名称/大量数据 | 200条受控会话，无横向溢出；测试后撤回响应替换并恢复实际隔离数据 | `native-report.json` |

| Requirement | Evidence |
|---|---|
| 相同名称的不同实例不合并、日期倒序、叶子来自真实记录 | `playtime: hierarchy separates instance identities and groups actual sessions by descending date` |
| 跨午夜秒数守恒、同一会话仅计一次 | `playtime: tree conserves cross-midnight duration and counts the original session once` |
| 展开/收起、键盘、点击筛日期、非法范围保留上次结果、双语 | `native-report.json`中的具体检查名称 |

截图：`before.png`为原矩形图；`tree-1280.png`、`tree-960.png`、`tree-1600.png`为新树形展示；`tree-final-open.png`为已刷新后留给用户检查的页面。实际记录来自本轮之前创建的隔离复验数据，不是用户真实游玩历史。原生沿用已验证的.NET 10.0.0运行时和显式UI目录；本轮没有重启或修改真实游戏实例。

最终保留用户打开的预览进程，不关闭窗口；页面定位到游戏时长，测试临时API替换已撤回。当前运行目录的三份UI文件与源文件及构建输出逐项校验相同。原生宿主DLL未热替换，图表代码由显式UI目录载入；新构建也已包含这次UI变更。

FormalPhaseApproval=NOT_GRANTED（本地实现验证完成，不代表发布）。
