# 冷启动门禁的环境隔离加固（第 18 轮）——含两次自我纠错

日期：2026-10-06（Asia/Hong_Kong）。改动对象：`scripts/verify-single-file-standalone.ps1`。
证据：本目录的 `R*.json`、原始修前脚本副本 `verify-single-file-standalone.PREFIX-COPY.ps1`。

## 1. 起因：怀疑"自足性"验收可能通过得莫名其妙

原始评估对 R01 的要求明确写着"**不依赖开发目录、历史 UI/DLL、PINEGLOW_UI_DIR**"。
而本产品的 UI 根解析顺序是：**env(`PINEGLOW_UI_DIR`) → EXE 旁 `UI/` → 内嵌解包目录 → 配置目录**——
env 优先级最高。检查脚本过去只在启动前设了 `PINEGLOW_API_PORT`，**从不清除继承来的 `PINEGLOW_*`**。
本机会话里确实残留着 `PINEGLOW_WEBVIEW_DEBUG=9222`，证明环境会泄漏进被测进程。
所以担心的是：只要机器上恰好设了 `PINEGLOW_UI_DIR`，这个"自足性"验收就会变成"从开发树取 UI"。

## 2. 两次自我纠错（记录，因为它们改变了结论）

**纠错一（结论方向）**：我最初的假设是"会**静默误通过**"。实测**不是**——
环境变量确实劫持了 UI 解析（见 §3 的 `dirs=` 为空），但**原有的"内嵌 UI 已解包"检查就已经把它判失败**。
所以**不存在假通过漏洞**；真实缺陷是**门禁对环境的脆弱性 + 失败归因误导**
（报出来的是 `/api/health 就绪 health=` 或 `dirs=`，完全看不出"你的环境里有个开发旋钮"）。

**纠错二（这次实验本身）**：第一版实验把 `-WorkRoot` 放在**工作区内**
（`D:\松光启动器\.tmp-standalone`），三组运行全部失败。我一度把它当成"环境变量导致的失败"——
**错了**。那是既有的沙箱现象：**从工作区里拉起的发布 EXE 会因 `HttpListener` 初始化失败而起不来**
（`/api/health` 一直为空），与 `PINEGLOW_UI_DIR` 无关。把 WorkRoot 换到 `%TEMP%` 后才有意义
（`R1/R2/R3/R4/R5` 五份 JSON 因此**作废**，保留但不作为结论依据；有效的是 `R6/R7/R8`）。

## 3. 有效的对照实验（WorkRoot 在 `%TEMP%`）

| 条件 | 修前（`PREFIX-COPY`） | 修后 |
| --- | --- | --- |
| `PINEGLOW_UI_DIR` = 一个**有效的替身 UI** | **FAIL**：`程序集内嵌 UI 已解包到 data/UI/<版本-指纹> dirs=`（**根本没解包** ⇒ 变量确实劫持了 UI 解析）| **PASS（19/19）** |
| 环境干净 | 历史记录 18/18 PASS | **PASS（19/19）** |

修后那两轮的 `scrubbed=` 明细（写进 JSON 证据）：

```
R7: scrubbed=PINEGLOW_UI_DIR=C:\Users\Administrator\AppData\Local\Temp\decoy-ui; PINEGLOW_WEBVIEW_DEBUG=9222
R8: scrubbed=PINEGLOW_WEBVIEW_DEBUG=9222
```

注意 R8：**即使我不设任何变量，门禁也从会话里继承到了 `PINEGLOW_WEBVIEW_DEBUG`**——
这正是"环境会泄漏"的实证，也说明加固不只是防御假想情况。

## 4. 改动内容

`scripts/verify-single-file-standalone.ps1` 启动前新增：

1. **清除从父进程继承的 `PINEGLOW_*`**（只保留本脚本自己要设的 `PINEGLOW_API_PORT`、
   `DOTNET_BUNDLE_EXTRACT_BASE_DIR`），并把清除清单打印出来；
2. **新增一条检查**（第 19 项）：`冷启动不继承开发旋钮（PINEGLOW_UI_DIR 等已清除）`，
   断言 `PINEGLOW_UI_DIR` 为空，detail 里列出实际清掉了什么。

于是检查数由 **18 → 19**。这条检查的价值不在"多一项"，而在于：失败时能直接回答
"是不是环境里带了开发旋钮"，而不是让人去猜 `health=` 为空是产品问题还是环境问题。

## 5. 顺带撞到的老坑（值得单独记）

改完 `.ps1` 后直接跑，三组全部失败且**没有任何可读输出**。原因是
**编辑工具把文件的 UTF-8 BOM 抹掉了**（首字节变成 `#Re`），Windows PowerShell 5.1 于是按 GBK
解析中文，报 `Unexpected token '鏃?'`（`'无'` 被读坏），**解析失败发生在我的新增行上**。
用 `[IO.File]::WriteAllText($path,$text,UTF8Encoding($true))` 重新加回 BOM 后恢复正常。
——这正是快照验证器里那条"**脚本编码守卫**"要防的东西；它是第 11 轮加的，这轮它本可以替我拦住这个坑
（因为我是在工作区里手跑，没走验证器，所以先被自己绊了一次）。

**同时排除一个假警报**：白名单里有两个 `.ps1` 没有 BOM（`scripts/capture-goal-baseline.ps1`、
`scripts/run-goal-validation.ps1`），但实测它们**非 ASCII 字符数 = 0**，
所以不需要 BOM，守卫不会（也不该）报错。

## 6. 边界

- 只验证了 `PINEGLOW_UI_DIR` 与 `PINEGLOW_WEBVIEW_DEBUG` 这两个继承变量；其他环境变量（如代理、区域设置）
  不在本轮范围内。
- 替身 UI 用"快照 ui/ 的副本 + 一个 marker 文件"，因此与内嵌包**内容不同**，
  能区分"用了替身"与"用了内嵌包"。
- 本轮未改动产品代码；改的是验收脚本本身。
