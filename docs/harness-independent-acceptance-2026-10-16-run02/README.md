# HA-01 / HA-02 第二轮独立验收

日期：2026-10-16，Asia/Hong_Kong。SourceRoot：`D:/松光启动器`；受验 HEAD：`f9b109d9ac3b665a926acbad3bdd96c8d57ac77e`，分支 main。开始时工作区干净；本轮不修正式脚本/测试或产品，不提交、推送、打标签、发布。

## 结论

`TechnicalAcceptance=FAIL_REWORK_REQUIRED`。HA-02 的主要报告更正已完成；HA-01 的正式十页正常路径通过，但本轮正式 Node 回归有一个失败，且两个浏览器反例证明新的图片判定/滚动实现尚未满足约定。暂不放行该收尾包。

没有发现需要依据本轮证据再修改后端启动器实现的理由。问题集中于正式测试预期、验证器的可视边界、滚动行为及其行为测试。

## 实际输入与执行范围

- 新提交 `42b822c` 更正原报告并调整更新验收辅助脚本、UTF-8 BOM；`f9b109d` 修复正式界面验证器并新增 Python 行为验证。
- `git diff 3ab6b0c..HEAD -- dev ui .github release-notes` 无差异，产品、依赖、C# 测试、发布工作流和版本说明未变。本轮不重复上轮已独立执行的957项C#；不称为本轮C#通过。
- 关键输入/时间/哈希见 `context.json`；收尾状态和哈希核对见 `final-verification.json`。正式 Node、行为验证、原生巡检直接使用当前仓库源码，未使用上一轮校正副本。
- 原生候选为执行报告保留的 v1.0.1 单文件 EXE，SHA256/size见 `candidate.json`；实际数据/解包/进程运行在各自新建的中性目录，未访问真实账户或游戏配置。
- 反例 ExecutionRoot 为本轮Python脚本启动的本机临时HTTP夹具+真实Edge布局。直接从正式源码抽取 `IMG_VISIBLE_FN`、`imgProbeExpr`、最终 `imagesPending` 表达式和滚动表达式，未复制另一套判定、改产品DOM或修改产品源来制造RED。

## 结果

| 项目 | 独立结果 | 证据 |
| --- | --- | --- |
| 全部Node清单测试 | **378/379通过，1失败，0跳过，exit1** | `node-tests.log`、`check-exits.json` |
| 清单双向一致 | Node28+C#1，exit0 | `inventory.json` |
| UI资源闭包 | 0缺失，exit0 | `closure.json` |
| 执行者新增的真实布局行为脚本 | PASS、exit0，但预期中包含错误的aria-hidden归类，未实际证明滚动生效 | `executor-behavior-rerun.json`、同名log |
| **正式**原生WebView巡检 | **10/10 PASS、exit0**，0未捕获JS异常/console.error/UI资源/未知本机接口失败；未使用allow-missing-images | `official-ui.json`、同名log/exit、`ui-shots/` |
| 可见破图反例 | 两个84×84可见破图被忽略，计数仅1张健康图片，最终图片失败谓词false | `boundary-probes.json`、`visible-broken-omitted.png` |
| 内层滚动懒加载反例 | 正式滚动后内层scrollTop仍0、图片请求0次；真正滚入该元素后请求成功并loaded1 | 同上、`lazy-after-official-scroll.png`、`lazy-after-real-element-scroll.png` |
| 受控加载中→成功对照 | 加载中时图片失败谓词true，释放响应后false | 同上controls |
| 更新辅助脚本，阶段A/B/C | **16/16 PASS、exit0**：默认备用清单、真实HTTPS下载大小/SHA一致、篡改拒绝且无残留 | `live-update-abc.json`、同名log/exit |
| 版本说明+BOM | pwsh读取的介绍与Markdown完全一致，UTF-8 BOM存在 | `release-notes-check.json` |

真实更新阶段B/C使用本地夹具清单声明1.0.2，下载的是已发布v1.0.0的真实HTTPS资产（43,989,943字节）。这验证下载与校验边界，不代表1.0.2存在或完成了升级。未调用apply、未替换候选EXE。辅助脚本仅在本轮副本加入后台启动的WindowStyle Hidden，保留原判定、源/校验和退出码，未改正式文件。

## 返工发现

### H2-01 / P1：新增第十页后，正式回归预期仍停留在九页

位置：`scripts/tests/ui-live-cdp.test.cjs:301`。

`mod.VIEWS`现有10项，但旧断言仍要求length为9。全清单运行在这个用例失败：`CV-07: 验收主流程只在本文件被直接运行时执行（require 时不得启动真实 EXE）`，actual10/expected9。当前执行报告的379/379来自旧输入，不能当作最新提交的结果。此失败会阻断运行该清单的发布测试门槛。

最小方向：保留新增playtime与require.main守卫，更新页面覆盖契约，明确验证十个预期页面身份/不重复/包含playtime；不要删除该测试、跳过清单或退回九页来变绿。聚焦与全清单重跑。

### H2-02 / P2：把可访问性元数据、MIME提示误当成视觉隐藏条件

位置：`scripts/verify-ui-pages-live.cjs:540-541`；错误预期还写入 `scripts/ha01-image-visibility-behavior.py:118`。

正式谓词对 `aria-hidden=true` 和 `type=image/svg+xml`直接返回false。独立Edge反例中，两个HTML img的布局盒均84×84、complete=true、naturalWidth=0，并未视觉隐藏；一个带aria-hidden，一个带type属性。正式统计却返回total1/loaded1/broken0，最终图片失败谓词为false。

`aria-hidden`不等同于CSS不可见；HTML img的type属性也不能把它变成SVG `<image>`节点。当前querySelectorAll('img')已限制选择器，不应靠type文本排除可见HTML图片。反例证明的是图片统计/判定漏报，不将局部谓词结果冒充整份原生巡检的总判定。

最小方向：只依据实际渲染/布局与来源判断；按真实节点类型处理SVG，保留对真正hidden/display:none祖先/无src占位的排除。把aria-hidden可见破图、HTML SVG图片的成功/失败纳入有布局验证，修正旧“aria-hidden必不可见”的错误预期。

### H2-03 / P2：懒加载滚动未作用于实际滚动容器，也没有给各步渲染机会

位置：`scripts/verify-ui-pages-live.cjs:589-593`。

实现同步循环调用window.scrollTo并立即归零；产品实际滚动区域是 `.content-panel`（`ui/css/figma.css:149-155`的overflow-y:auto），不是外层window。独立夹具按内层滚动容器布置一张真正HTTP懒加载图片：正式表达式运行前后nestedTop=0/windowY=0，图片top=12055，HTTP请求仍0次、loaded0。执行实际scroll_into_view后，图像进入视口，正常加载，loaded变1。因此不是图片资源或网络失败。

现有行为脚本只断言offscreen数量不为0，没有执行并断言正式滚动使元素进入视口、发起请求并加载；“会触发分支”不能证明“滚动有效”。即使滚window的页面，连续同步滚动后立即复位也可能不给浏览器足够渲染/懒加载调度机会。

最小方向：对目标图片找到真实滚动祖先或逐项滚入视口，按步等待布局/加载，记录并恢复原滚动位置；不要固定滚window。用独立HTTP图片和内层overflow容器验证请求/加载前后变化，保留破图仍失败和真实pending不提前PASS。

## HA-02 与边界

主要更正已存在：不再认定沙箱服务因果已证实；承认旧九页总判定FAIL；说明旧rp02-failpath归档实际PASS并关联真实健康失败报告；撤销含空格路径必有脚本bug的说法。原证据应保持可追溯，旧文件删除前版本可在Git历史查到。本轮没有重写其原始结果。

未做真实账号/皮肤、Minecraft会话、更新应用/回滚、远程同SHA CI或发布。这些不计为本轮新产品失败。本轮不因正常十页PASS或辅助脚本PASS忽略全清单失败与反例。

返工流程和可复制提示词分别见本目录 `remediation-workflow-2026-10-16.md`、`execution-prompt-2026-10-16.md`。本轮停止在验收交付；`FormalPhaseApproval=NOT_GRANTED`。
