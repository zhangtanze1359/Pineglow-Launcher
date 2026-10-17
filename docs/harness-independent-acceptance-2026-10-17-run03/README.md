# Code Review 与第三轮独立复验

日期：2026-10-17，Asia/Hong_Kong。SourceRoot：`D:/松光启动器`，受验 HEAD `73ad9c4a6def0d967127e8a3a86fe95c96372a94`，分支main，开始时工作区干净。本轮只审查、运行隔离验证、交付材料，未修正式实现/测试、未提交/推送/发布。

## 结论

`TechnicalAcceptance=FAIL_REWORK_REQUIRED`。H2-01（十页契约）和H2-02（aria-hidden/type可见破图）已修复并通过复验；H2-03的辅助滚动函数在直接调用时有效，但正式CDP调用没有执行它，且恢复逻辑只清零。因此H2-03未闭环，尚不能放行本轮收尾包。

用户显式选择了Code Review。本会话可发现的该插件工具只有PR/合并请求CI诊断，没有适用于本地提交的审查调用；本轮未提供PR，也未凭空创建PR或调用无关CI。实际完成的是本地差异代码审查和真实CDP/浏览器独立复验，不声称插件后端或另一个执行者给出审查结果。

## 输入与验证

审查 `f9b109d..73ad9c4` 的四个脚本/测试改动及执行报告。产品源码、依赖/C#、UI、版本说明和发布工作流未变；不重复上轮C#、构建或真实更新A/B/C，不将历史结果算作本轮重跑。

关键输入哈希见 `context.json`，执行结束后再次核对见 `final-verification.json`。原生巡检使用保留候选v1.0.1，SHA256 `72eae380db265a97c16938fd6086441e0409fd192f6b5cce1c01c65a33a32534`、53,089,586字节，数据运行在正式脚本新建的中性目录。执行目录和候选信息分别在 `official-ui.json`、`candidate.json`。

| 检查 | 本轮实际结果 | 证据 |
| --- | --- | --- |
| 正式Node全部清单 | **380/380，0失败、0跳过、exit0**；原379+新增页面集合契约1项 | `node-tests.log`、`node-check-exits.json` |
| 清单、资源闭包 | 通过、exit0 | `inventory.json`、`closure.json` |
| 正式十页原生WebView | **10/10 PASS、exit0**，0异常/console.error/UI资源/未知本机接口失败 | `official-ui.json`、同名log/exit、`ui-shots/` |
| 执行者可见性行为验证 | PASS、exit0，两个可见破图反例已保留在计数内 | `visibility.json`、同名log |
| 执行者滚动辅助函数验证 | PASS、exit0；Playwright活元素句柄直接调用辅助函数 | `lazy-helper.json`、同名log |
| **正式CDP包装器与调用点联测** | **失败反例确认**：targets变`[{}]`，返回`{}`，滚动0、请求0、loaded0 | `cdp-wiring-v2.json`、同名log、`cdp-v2-wrapper-no-scroll.png` |
| 同浏览器控制实验 | 显式在页面内调用同一辅助函数后，scrolled=true、loaded1 | 同上、`cdp-v2-explicit-invocation-control.png` |
| 非零初始滚动恢复 | **失败反例确认**：原始137，恢复成0 | `cdp-wiring-v2.json`第二项 |

本轮CDP反例直接使用当前正式文件的evaluate包装器（原样抽取）、listOffscreenExpr、scrollOneExpr、runLazyRestoreExpr以及导出的函数。通过真实Edge CDPSession发送Runtime.evaluate，使用本轮临时HTTP服务器提供距视口12000像素的真实loading=lazy图片。没有修改产品DOM、正式文件、脚本判定或运行时来制造失败。

## 审查发现

### R3-01 / P2：正式CDP路径丢失元素身份，滚动表达式没有被调用

位置：`scripts/verify-ui-pages-live.cjs:688-693`，结合`:583-586`。

`evaluate`只接收一个expression，调用Runtime.evaluate时使用returnByValue=true。listOffscreenExpr返回DOM元素数组，跨CDP按值转换后只得到`[{}]`。随后`evaluate(scrollOneExpr, target)`的第二参数被丢弃；scrollOneExpr本身又只是`(img)=>...`函数表达式。Runtime.evaluate只创建/返回函数对象，未执行它，按值结果为`{}`，没有scrolled/containerCount。

独立复现：正式路径调用前后内层scrollTop=0，图片top=12055，HTTP请求0→0，loaded0。使用同一浏览器、同一辅助函数，但把真实DOM查询和调用写在页面表达式里后，scrolled=true、HTTP图片加载完成、loaded1。证明根因在正式调用接线而非网络或scrollIntoView本身。

新增`ha01-lazy-scroll-behavior.py`传入Playwright活ElementHandle，绕过了正式CDP值序列化和包装器，所以它的PASS不能证明主流程有效。正常十页路径没有触发有效的视口外图片分支，因此也不能反驳该反例。

最小修复方向：在页面上下文中完成元素查询与函数调用，通过稳定索引/标识传递可序列化数据；或明确保留CDP objectId并用Runtime.callFunctionOn调用。不能只给evaluate增加一个普通JSON参数，因为元素身份在前一步已经丢失。为真实包装器、目标选择、调用与最终统计增加同路径联测。

### R3-02 / P2：恢复函数将所有滚动位置清零，没有恢复原状态

位置：`scripts/verify-ui-pages-live.cjs:156-160`。

LAZY_RESTORE_FN把所有发现的容器scrollTop/scrollLeft设为0，并执行window.scrollTo(0,0)。没有保存调用前的位置，无法满足本轮明确要求的“记录并恢复原滚动位置”。独立对照从panel.scrollTop=137开始，滚动后执行正式恢复表达式，实际变成0，期望仍为137。

现有新增行为用例只从0开始并断言回0，因此掩盖了该问题。最小修复：在首次触碰各滚动容器/窗口前保存位置，在finally中恢复实际初值、只恢复本轮触碰的容器并清理状态；加入非零纵横滚动初值的联测。

## 证据解释与边界

最初CDP探针假定函数按值求值返回undefined，实际当前Chromium返回空对象；其整体标为INCONCLUSIVE。原脚本与JSON/log均保留。v2仅修正探针对返回形态的预期，未改正式源码或实验动作，复现了两个反例。以v2作为确认依据。

本轮没有重验真账号/游戏、升级替换/回滚、同SHA远程CI或发布。所列发现限定于验证脚本；不据此要求改启动器后端。所有临时进程和服务器已关闭，检查结果见final-verification。

返工流程和可复制提示词分别见本目录 `remediation-workflow-2026-10-17.md`、`execution-prompt-2026-10-17.md`。`FormalPhaseApproval=NOT_GRANTED`。
