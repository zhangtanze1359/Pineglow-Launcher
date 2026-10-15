# 轮次记录索引（138 份文件，编号 2–139；编号 1、4、19 无独立文件）

> 这份索引存在的理由：第 28 轮自我评估给「清晰度」打了 **3 分**，理由是
> 「38 份轮次记录里读者无法分辨哪几份重要」。本文件修的就是这一条。

---

## ⚠️ 先说明一个容易踩的坑：这里有**两套"第 N 轮"**

| 计数器 | 含义 | 现在到哪 |
| --- | --- | --- |
| **文件名里的 `roundNN`** | **本审计自己的记录序号**（我每写一份就 +1） | **42** |
| **正文标题里的「第 N 轮」** | 早期用的是记录序号，**后来改用了目标轮次**（goal round，你那边看到的那个） | 标题里最大是 41，而目标轮次是 29 |

**两者已经分叉**：早期一个目标轮次会产出多份记录（如 `round2`/`round3` 是同一轮的补充），
所以记录数（42）一直多于目标轮次数（29）。
**按文件名找最可靠**；正文标题里的"第 N 轮"在 `round42` 之前指的是记录序号。

（`round42-self-evaluation.md` 的标题写的是「第 28 轮」，那是**目标轮次**——这是我在这一份里
第一次改用目标轮次，反而造成了不一致。以文件名为准。）

---

## 先读这 4 份（如果只看 4 份）

| [`APPLY-ORDER.md`](APPLY-ORDER.md) | **9 个补丁脚本的应用顺序**（已逐个校验存在 / UTF-8 BOM / 语法），含用法与唯一顺序约束 |

| 文件 | 为什么先读它 |
| --- | --- |
| [`round13-source-not-in-git.md`](round13-source-not-in-git.md) | **交付阻断的量化**：源码根本不在 git 里。而且这份记录**保留了一次自我更正**（初稿两条推断被实测推翻），比结论本身更能说明证据等级 |
| [`round41-all-twelve.md`](round41-all-twelve.md) | **12 条补丁一起应用的合并验证**（最新一次）；含"产物哈希相同 = README-only 补丁零影响"的产物级证明 |
| [`round10-mirror-403-rootcause.md`](round10-mirror-403-rootcause.md) | 一个典型缺陷的**完整归因链**（镜像 403 → User-Agent），可作为"证据长什么样"的样本 |
| [`round37-no-java-observed.md`](round37-no-java-observed.md) | 一个缺陷**从推断升级为端到端观测**的过程（无 Java 机器上的首启死路） |

---

## 全部记录（按文件名排序）

| 文件 | 这一轮做了什么 |
| --- | --- |
| `round2-verification-addendum.md` | 第 2 轮复验补充（A/B 收口证据） |
| `round3-verification-addendum.md` | 第 3 轮复验补充（B 的升级链与回退证据） |
| `round5-update-chain-verification.md` | 真实更新链（下载 → 校验 → 替换 → 重启）复验 |
| `round6-install-launch-verification.md` | D 的真实安装与启动证据（portable 夹具） |
| `round7-loader-matrix-findings.md` | 加载器可达性与 Modrinth 导入路径实测 |
| `round8-loader-matrix-and-proxy.md` | 加载器矩阵收口与代理补救验证 |
| `round9-mirror-and-fabric.md` | 下载源（镜像）与 Fabric 安装实测 |
| `round10-mirror-403-rootcause.md` | 镜像 403 的根因（User-Agent）——用产品同代 .NET 建最小客户端复现 |
| `round11-resume-and-queue-findings.md` | README 承诺「断点续传 + 可恢复任务队列」的实测 |
| `round12-mirror-403-ua-rootcause.md` | 镜像库下载 403 根因**完全确定**（闭合第 9 轮遗留） |
| `round13-source-not-in-git.md` | **交付阻断量化**：源码不在 git；并在原生 HEAD 检出里实跑 CI 步骤推翻了自己的推断 |
| `round14-fabric-retry-environment.md` | Fabric 复测——这次不是产品错误，是**探针超时**（区分产品缺陷与环境失败） |
| `round15-release-gates-and-rid-lock.md` | 跑通两条从未执行过的发布门槛；C 的三个选项全部验证完 |
| `round16-readme-claims-audit.md` | README 功能清单逐条对码（15 条断言）——新增「文档过度承诺」 |
| `round17-fabric-verified.md` | Fabric 端到端验证通过 → **四种加载器全部闭环** |
| `round18-ui-level-verification.md` | **界面级**验证（第一次）：真实 Chromium 引擎驱动出厂前端 |
| `round20-proposed-fixes-validated.md` | 三条缺陷变成**已验证的补丁**；发现 C 的方案清单漏了一个文件 |
| `round21-patch-behaviour-and-corrections.md` | 补丁的**行为验证**（含 A/B 直接证据）+ 两处自我更正 |
| `round22-in-situ-verification.md` | 两条修复的**现场验证**（都有对照组） |
| `round23-c-rid-lock-validated.md` | C 从「方案」变成已验证的补丁，且**发布产物逐字节不变** |
| `round24-combined-and-importlog.md` | 两套补丁**合并验证**；用现场测量改掉自己补丁里的一个毛病 |
| `round25-patched-launch-verified.md` | **打过补丁的构建**也走通「启动到标题界面」 |
| `round26-patched-pipeline.md` | **打过补丁的源码树**跑通整条候选流水线 |
| `round27-webview2-firstrun-audit.md` | 首启风险「没有 WebView2 会怎样」——审计 + 一次**失败**的复现尝试 |
| `round28-durable-queue-and-deadlock.md` | 实现 README 最后一条未兑现承诺；顺手撞出**真实产品缺陷**（UI 线程死锁） |
| `round29-deadlock-fix-verified.md` | 死锁地雷拆掉（**3 行**），用"重放原本会挂死的动作"验证 |
| `round30-deadlock-regression-test.md` | 给死锁补**真正会红的**回归测试（连写两版都是绿的，第三次才红） |
| `round31-all-patches-verified.md` | **把全部补丁一起应用**（8 条）——从"各自验证"变成"一次性动作已验证" |
| `round32-double-launch.md` | 双击启动两次：无单实例守卫，第二个实例**完全没有日志** |
| `round33-logshare-fix.md` | 让第二个实例说出它为什么没有 API（**1 行**）+ 把推断变成观测 |
| `round34-all-nine-verified.md` | 九条补丁合并通过；补上最后一个未在补丁版本上跑过的发布门槛 |
| `round35-runsync.md` | 「同步等 async」这一类**一次收干净**（RunSync 机制 + 10 个调用点）+ 更正自己一处误判 |
| `round36-ten-patches-and-missing-jre.md` | 十补丁合并复验 + 新发现：**「内置 JRE」并不存在** |
| `round37-no-java-observed.md` | 缺陷 13 **端到端实测**（无 Java 机器上的首启死路） |
| `round38-java-label-fix.md` | 把「内置 JRE」那句谎话改成实话 |
| `round39-all-eleven.md` | 十一条补丁合并复验（第 3 次重跑） |
| `round40-optifine-doc.md` | README 不再把 OptiFine 说成受支持的加载器 |
| `round41-all-twelve.md` | 十二条补丁合并复验；产物与十一条**逐字节相同** |
| `round42-self-evaluation.md` | **对本审计自身的 5 轴自我评估**（总分 3.0，查出报告里三处误导数字） |
| `round43-resume-on-patched.md` | 断点续传在**补丁版**上字节级验证通过（此前只验过出厂版）；且是被三条补丁碰过的区域 |
| `round44-ui-level-on-patched.md` | 补丁版的**界面级**验证；与出厂版逐项一致（证明 P-JAVALABEL 没造成界面回归），并把 httpErrors=5 归因为夹具而非产品 |
| `round45-correcting-my-own-attribution.md` | **更正记录 44 的错误归因**：`httpErrors=5` 的"令牌过期→401"解释被实测推翻，成因**仍未查明**；并记下"把猜测写成结论"这个模式 |
| `round46-http-errors-closed.md` | **`httpErrors=5` 结案**（1 条 favicon 404 + 4 条设计内 fail-closed），并撤回建立在假前提上的待办 20；记下"用推断补上没查的那一步"这个反复出现的模式 |
| `round47-what-163-really-is.md` | 查明 **163 正好是候选流水线白名单的全部条目**（0 条在核心目录外、0 条引用 `docs/`）——决策 1(b) 由此有证据 |
| `round48-own-pipeline-FAIL.md` | ⚠️ 曾报「产品自己的流水线 FAIL」——**记录 49 结案：是我的夹具没带 `.gitignore`** |
| `round48-own-pipeline-FAIL.md` | ~~⚠️ 未解决~~ **已结案，见记录 49**：产品自身的流水线在十二补丁树上 **FAIL**（8 步 PASS，step9 `publish-locked-probe` 失败）；假设未验证，下一轮首要项 |
| `round49-my-own-harness-error.md` | **结案记录 48 的 FAIL**：两条真失败都源于我给副本 `git init` 时没带 `.gitignore`；撤回假警报，并记下"假设必须显式标注"这条闸门 |
| `round50-own-pipeline-13-of-14.md` | 产品自身流水线复跑：夹具修好两处（算术对上 `143+163=306`），**14 步里 13 步 PASS**；唯一未过项（候选闭包缺 2 个 AppIcon）疑为夹具产物，未验证 |
| `round51-own-pipeline-PASS.md` | ✅ **产品自己的候选流水线 PASS**（失败项 0、C# 949/0、冷启动 19 项） |
| `round51-own-pipeline-PASS.md` | ✅ **产品自己的候选流水线在十二补丁树上 PASS**（失败项 0、C# 949/0、冷启动 19 项）；四轮夹具迭代收敛，每步都有可核对读数 |
| `round52-report-consistency-sweep.md` | 报告一致性复查：又找出**三处**把「204」当成全仓规模的表述（同一句话的其它副本）；记下"改了某个数字要全文搜一遍" |
| `round53-reproducibility-confirmed.md` | **可复现性确认**：产品自身流水线与我的构建**产出逐字节相同的 EXE**（`21EB0C5E…`） |
| `round54-defect4-narrowed.md` | 缺陷 4 的表述**过宽**已收窄：`内容中心` 导入**有**进度界面，只有**本地文件导入**没有 |
| `round55-defect1-confirmed.md` | 缺陷 1 的表述**经查成立**（版本下载接口全程无加载器处理）——一个反例：核查不总是找出错误 |
| `round56-defect10-confirmed.md` | 缺陷 10 的代码级断言**逐条成立**（0 处全局异常处理；`App.cs:44` 无保护而 `:31` 有） |
| `round57-defect10-patch.md` | **第 13 条补丁**（P-STARTUPLOG）：全局兜底 + 窗口壳构造保护；无补丁缺陷从 3 条降到 **2 条** |
| `round58-all-thirteen.md` | **十三条补丁一起应用全部通过**（build 0/0、C# 949/0、Node 305/0、锁定发布、冷启动 PASS） |
| `round59-quilt-launch.md` | **Quilt** 在十三条补丁版上走通「启动到标题界面」——补丁版加载器启动覆盖 **2/4** |
| `round60-four-loaders-on-patched.md` | **四种加载器全部在十三条补丁版上走通启动**（4/4）；明确区分"启动"已导入实例 vs "安装"仍未覆盖 |
| `round61-integrity-check.md` | 收尾完整性检查：脚本 10/10 可用、索引 57 链接全解析；**在决策单里查出两处过时表述**（含一处"改过但没改上"） |
| `round62-patch-count-verified.md` | 核对「13 条补丁」= **12 条 `P-` + C**，成立；记下"反复重复的断言值得抽查一次" |
| `round63-corrupt-config-silent.md` | **新缺陷 #14**：`config.json` 损坏时被静默换成默认值（裸 catch、无日志、损坏文件不保留）——实测截断配置后启动 |
| `round64-missing-instance-dir.md` | **新缺陷 #15**：实例目录缺失时 `prelaunch-check` 报 ok、`launch` 500 且无日志；并与 #10/#14 归为**同一族（失败了但不说）** |
| `round65-config-log-patch.md` | **第 14 条补丁**（P-CONFIGLOG）：配置损坏不再静默；含同一夹具的前后对照，**并记下我自己补丁里的一个毛病**（副本按每次失败保留） |
| `round66-config-log-v2.md` | 修掉我自己补丁里的毛病：损坏配置副本 **2 → 1**；含一次"替换只成功一半"的自查失败 |
| `round67-api-log-half-verified.md` | 缺陷 15 的补丁：**① 500 落盘日志验证通过**（完整栈）；**② 自检查实例目录没生效**，如实记录未查因 |
| `round68-defect15-fixture-ruled-out.md` | 把缺陷 15 ② 的**夹具排除了**（属性名/解析函数/写回/唯一性四项全排除）——观察成立，**成因仍未定**，下一步是回显它实际看到的 `gameDir` |
| `round69-defect15-smoking-gun.md` | **缺陷 15 ② 定案**：响应回显 `gameDir` 就是那个坏路径，而 `warnings=[]` —— **它看到了却没报警**（自证据） |
| `round70-defect15-part2-withdrawn.md` | **缺陷 15 ② 撤回**：产品在我检查前**已创建**那个目录，测试前提自己失效；含"问题出在我这边"第四次的经验总结与痕迹清理 |
| `round71-trim-api-log-patch.md` | 删掉 P-APILOG 里**已经没用的那一半**（针对已撤回的缺陷）；含又一次"改东西要看结果"（双引号里 `$report` 被展开成空） |
| `round72-all-fifteen.md` | **十五条补丁一起应用全部通过**（build 0/0、C# 949/0、Node 305/0、锁定发布、冷启动 PASS） |
| `round73-defect4-recon.md` | 缺陷 4 的**改造勘察（第 62 轮已补全）**：方法定义 `:1419-1538`、进度回调锚点 `:1455` 全部定位；**修法不需改 UI**，下一轮可直接写补丁 |
| `round74-defect4-wiring-decided.md` | 缺陷 4 接线方案已定（`CreateImportLogger()` 是 `Action<string>`）；**并说明为什么不能只做一半**（不写终态会留下幽灵导入） |
| `round75-import-task-half.md` | 缺陷 4 的补丁**写到一半卡在重复锚点**（自检拒绝改错地方）；含本轮另外两次失败，以及"半成品不计入 15 条"的说明 |
| `round76-import-task-step3-blocked.md` | 缺陷 4 补丁：**1、2 步通了**；第 3 步卡在空白（先 2 处、改后 0 处）；含"别隔着 PowerShell 做字符串手术"的教训 |
| `round77-import-task-scope-error.md` | 缺陷 4：**三步全部应用成功**，但编译报出真实作用域错误（`try` 变量在 `finally` 不可见）；含确切修法 |
| `round78-import-task-compiles.md` | **第 16 条补丁**（P-IMPORTTASK）：应用成功、编译 0/0、测试 947/0；**行为未验**（需真实导入），并说明这一数字尚未并入合并验证 |
| `round79-import-task-behaviour.md` | P-IMPORTTASK **行为验证**：自造最小 `.mrpack` 导入 → `/api/content/tasks` 由 `[]` 变为带**实时进度**的任务；**终态未观测** |
| `round80-import-progress-all-phases.md` | P-IMPORTTASK 的进度**贯穿导入所有阶段**（消息流 + 日志到 4550/4591）；**终态未观测，卡在网络不卡在补丁** |
| `round81-all-sixteen.md` | **十六条补丁一起应用全部通过**（build 0/0、C# 949/0、Node 305/0、锁定发布、冷启动 PASS） |
| `round82-defect1-recon.md` | 缺陷 1 的改造勘察：**边界摸清，规模从"最大"修正为"中等偏上"**（三步：公开 `InstallLoaderAsync` + 新路由 + UI 按钮） |
| `round83-commit-scope-remeasured.md` | 复测提交规模：**核心区 204 没变**；"全部提交"从 2467 涨到 **2605**（+138 **全部来自我自己的证据文件**） |
| `round84-three-options.md` | 决策 1 拆成**三个**选项（2606 / **2180** / 204）；发现**非我产出的未跟踪文件有 1,976 个**（恢复遗留文档） |
| `round85-decision-sheet-audit.md` | **决策单数字复查**：又找出 **8 处过时**（派生文档天然会漂）；记下"派生件要跟着源头一起复查" |
| `round86-index-audit.md` | **索引复查**：2 份记录（`round48` FAIL / `round51` PASS）**根本没进索引**，标题计数也把"最大编号 85"当成了"文件数 82"；含我自己检查器的 bug |
| `round87-decision-sheet-structure.md` | 决策单**结构**复查（渲染通过）；但查出选项 (a) 的"构成"栏**少了 163 个核心源码文件**（算式不成立），已按实测重算 |
| `round88-defect1-anchors-complete.md` | 缺陷 1 的**锚点全部取到**（`InstallLoaderAsync:475` 签名、`ModLoaderInfo.TryParse` 字符串→枚举入口），并给出**可照抄的路由处理器**；下一轮可直接写补丁 |
| `round89-loader-entry-patch.md` | **第 17 条补丁**（P-LOADERENTRY）：缺陷 1 的**后端**入口（build 0/0、测试 947/0）；**至此无补丁的缺陷 0 条**；UI 入口未含 |
| `round90-all-seventeen.md` | **十七条补丁一起应用全部通过**（build 0/0、C# 949/0、Node 305/0、锁定发布、冷启动 PASS） |
| `round91-loader-entry-behaviour.md` | P-LOADERENTRY 的**路由与校验行为验证**（3×400 + 对照 404，无需网络）；真实安装与 UI 入口未含 |
| `round92-java-auto-patch.md` | **第 18 条补丁**（P-JAVAAUTO）：Java 自动安装接到启动路径（build 0/0、测试 942/0）；**行为未验**；有顺序依赖 |
| `round93-all-eighteen.md` | **十八条补丁一起应用全部通过**（含 P-JAVAAUTO 的顺序依赖首次在合并验证里被走到） |
| `round94-javaauto-coverage-gap.md` | 启动回归**未验成**（夹具 Java 失效）；但撞出 **P-JAVAAUTO 的覆盖缺口**：`ResolveJavaPath` 前两分支不做存在性检查，失效的配置路径走不到自动安装 |
| `round95-javaauto-gap-closed.md` | 补上 P-JAVAAUTO 的**覆盖缺口**（配置路径加 `File.Exists`）；现在两个入口都覆盖；含一处标签不一致的小瑕疵 |
| `round96-launch-regression-pass.md` | **启动路径回归验成**（18 条补丁 + Java 存在的夹具 → 到标题界面）；顺带发现 5 个由导入路径真装出来的 JRE |
| `round97-javaauto-verified.md` | **Java 自动安装端到端验证通过**：无 Java → `[Java] 未找到 Java 21，正在自动下载…` → 46.7 MB → 装好 → **launch ok=True** |
| `round98-loader-install-partial.md` | P-LOADERENTRY 的真实安装：**路由走到、安装在跑**（日志到 3550/4591），**420 秒未完成**；顺带记下"给原版实例装加载器会重下全部资源"这一产品事实 |
| `round99-loader-install-verified.md` | **缺陷 1 端到端验证通过**：原版实例 → 一次 POST → **40 秒**后成为 Fabric 实例、字段回写、`prelaunch ok=True` |
| `round100-decision-sheet-audit-2.md` | 决策单**又**漂了 8 处（第二次复查）；并更正我自己的**增长速率估计**（实测约每轮 +3～4，不是 +1）；指出 (a′) 与 (b) 十五轮没变过 |
| `round101-option-aprime-verified.md` | **(a′) 已验证安全**：全仓 0 处引用证据目录、白名单 0 处引用 `docs/`；一行 `.gitignore` 即可 —— 从"看起来合理"变成"已验证" |
| `round102-ui-entry-recon.md` | 缺陷 1 的 **UI 入口勘察**：UI 里根本没有"加载器"概念，需**三件套**（api 调用 + 显示当前加载器 + 按钮）；判定**属 UI 取舍，等用户定** |
| `round103-ui-already-shows-loader.md` | **更正记录 102**：UI **早就会显示加载器**（`api.js:248` 映射 `loaderType→loader`），入口是**两件套**不是三件套；含"我的观测装置比现实窄"的第六个样本 |
| `round104-ui-entry-anchors.md` | UI 入口锚点补全：**跨 3 个文件**（含我上轮没想到的 `i18n.js`）；给出可直接照抄的改动清单 |
| `round105-loader-ui-patch.md` | **第 19 条补丁**（P-LOADERUI，跨 3 文件）：**缺陷 1 至此完整**；含我自己写坏的一次（未转义撇号 → Node 170 失败，被测试抓住） |
| `round106-all-nineteen.md` | **十九条补丁一起应用全部通过**（含两条顺序依赖；`node --check` 三文件 exit 0）；并**更正"产物字节数一直没变"这个旧说法**（那是压缩后的巧合） |
| `round107-loader-ui-runtime.md` | P-LOADERUI 运行时检查：**按钮/接线/i18n 已进产物**、界面渲染与出厂一致；**但探针只加载首页，安装管理视图未被渲染** → 交互仍未验 |
| `round108-installations-view-verified.md` | **视觉验证成功**：安装管理视图 0 异常、**截图确认按钮在每一行**；并暴露我自己的瑕疵 —— **按钮文案是占位符 `L`**（静态门禁抓不到这类问题） |
| `round109-loader-ui-label-fixed.md` | 修掉占位符 L（改用已加好的文案键）；记下"**所有静态门禁全绿也没发现它，是截图发现的**"；并说明补丁内容变了、合并验证待重跑 |
| `round110-all-nineteen-v2.md` | **第 100 轮**：十九条（含修正后的文案）**合并验证全部通过**；并汇总这一百轮里我犯过的错（观测装置 6 / 派生文档 4 / 写坏代码 3 / 推断当结论 1） |
| `round111-click-loader-verified.md` | **P-LOADERUI 点击交互验证通过**（`ok=True`、7 行都有按钮、文案"安装加载器"、弹窗四项俱全、0 异常）—— **最后一处验证缺口关上** |
| `round112-defect13-part3-recon.md` | 缺陷 13 ③ 勘察：java 路由只有只读一条、无 POST 能装；四件套已列明；**并给出价值判断**（缺的是便利不是能力） |
| `round113-apply-order-never-verified.md` | **发现交接文档的顺序从未被验证**：`APPLY-ORDER.md` 的 B 顺序与已验证的 A 顺序不同（集合一致）；两条依赖在 B 里都满足，但"B 也能跑通"只是判断；已在文档里加警示 |
| `round114-apply-order-B-verified.md` | **按 `APPLY-ORDER.md` 的顺序跑通**，且**产物哈希与另一顺序逐字节相同**（`DCB8FD1F…`）→ 交接件可信、无隐含依赖 |
| `round115-decision-sheet-conclusion-fixed.md` | 修正决策单**开头结论**：那句"若干缺陷还没修"**已不成立**（15 条缺陷全部有补丁，缺的是"决定要不要应用"）；并补上补丁版三项端到端验证。**记下"结论漂比数字漂更危险"** |
| `round116-artifact-number-checker.md` | 把人工复查做成**脚本**（`check-artifact-numbers.ps1`）：第一次跑抓出 **5 处真漂移**；**且它自己有 2 条正则是错的（假阳性）** —— 记下"检查器报错时先确认它自己是对的" |
| `round117-drift-is-structural.md` | 脚本化检查暴露**结构性问题**：审计自己往被审计的仓库里写东西，所以自述数字**必然滞后** —— 前五轮把漂移当"疏忽"只诊断对了一半；决策单已改为**指向检查器作为权威来源** |
| `round118-numbers-snapshot-mechanism.md` | 给"结构性漂移"配**机制**：新增 [`产物数字-最新.txt`](../../产物数字-最新.txt)（脚本输出原样落盘，带生成时间与退出码），决策单改为**指向快照** —— 会变的数从此只有一个来源 |
| `round119-attempted-change-not-done.md` | 一次**没做成的改动**（PowerShell 解析失败，文件零改动）+ **重新判断"它本来该不该做"**（结论：不该 —— 会损失精度，而漂移已被妥善报出） |
| `round120-steady-state-restored.md` | 回到稳态（数字从实测重算，检查器 0 漂移）；并发现 **(a′) 行有与 (a) 同一个毛病**（构成栏漏了 163 个核心源码）—— 第 87 轮只修了报出来的那一行 |
| `round121-same-class-check-applied.md` | **把上一轮教训用出去**：检查所有"构成式"→ 又抓到 2 处同类错误（(a) 行差 1、报告漏改的 2426/2467）；我自己修的过程中**又弄坏一处**（粘贴了旧行尾巴），是回读抓到的；教训升级为"**修一类 = 给这一类加检查**" |
| `round122-composition-check-in-script.md` | 把构成式验算**真正写进脚本**（上轮只在临时命令里）；并用**反例对照**（把 (b) 总数改成 999）证明它**会响** —— 记下"说了加了 ≠ 加了""它通过 ≠ 它有效" |
| `round123-evidence-levels.md` | **给 19 条补丁标出证据等级**：A 行为 12 / B 测试 2 / **C 只编译 4** / D 文档 1；那 4 条同属"失败了但不说"族，其中 **3 条可补验**、1 条本环境不可复现 |
| `round124-configlog-verified-apilog-not.md` | **P-CONFIGLOG 行为验证通过**（C→A）并暴露"坏配置不自动恢复 + 每次读都记 ERROR"；**P-APILOG 没验成** —— 我构造的是 400 不是 500，**前提错了** |
| `round125-apilog-verified.md` | **P-APILOG 行为验证通过**（畸形 JSON → 真 500 → 日志带 9 行调用栈）；顺带测清错误分层（`InvalidOperationException`→400、其它→500）；**只剩 P-LOGSHARE 可补验** |
| `round126-logshare-verified.md` | **P-LOGSHARE 行为验证通过**（两进程同目录同日志、0 IOException，闭合缺陷 12 的日志部分）→ **19 条里 18 条有运行时证据**；唯一剩下的 P-STARTUPLOG **不可复现** |
| `round127-double-launch-confirmed.md` | **缺陷 12 的窗口部分实测确认仍在**（默认端口双启：B 不退出、A 死后端口无人服务）；日志部分确认已修；**并说明那行日志来自 App.cs 既有的 catch，不是我的补丁 —— 所以本轮没验证 P-STARTUPLOG** |
| `round128-zombie-mechanism-inconclusive.md` | 僵尸机制**没查清** —— "数 msedgewebview2 子进程"的测法不成立（WebView2 进程共享），**第 7 次"观测装置比现实窄"**；但"多出一个活着不服务不退出的进程"确认 |
| `round129-zombie-truth-established.md` | **换了不依赖那个前提的测法（TCP 连接表）**，僵尸真相查清：B **0 个子进程、0 条连接** → **没有第二个窗口、没有借用 API**；**缺陷 12 的两条描述应更正**；三条处理路线的依据齐了 |
| `round130-code-contradicts-reading.md` | **我上一轮结论过头了**：读 `App.cs` 发现 catch 无 return、`:44` 无条件创建窗口 → **读数与代码冲突且未调和**；**第八次同类问题，形态是"忘了代码也是证据"** |
| `round131-webview2-contention-root-cause.md` | **判别实验**：两个实例各给可用端口，B 端口正常仍 **0 渲染进程** → 根因是 **WebView2 用户数据目录争用**（夹具只有一个 EBWebView）；**推荐修法改为单实例守卫**（原"抢不到端口就退出"不会触发） |
| `round132-single-instance-patch.md` | **第 20 条补丁**（P-SINGLEINSTANCE，缺陷 12 的僵尸）：单实例守卫，**位置放在 `new WebShellWindow()` 之前**（按实测根因选）；build 0/0、测试 941/0；**行为待验** |
| `round133-single-instance-test-invalid.md` | 单实例守卫**验证没做成**：表面像成功，**但日志里没有守卫那一行** → B 是**崩溃退出**（`0xE0434352`）而非被拦；夹具坏了（d6 夹具已清掉 + 构建产物静默复制）。**"看到期望的现象" ≠ "看到期望的痕迹"** |
| `round134-guard-unreachable-without-logshare.md` | **查明 B 为什么崩**：死在 `LauncherLog.Init()`（`:21`），**守卫在 `:54` 根本到不了** —— 因为这棵树没打 **P-LOGSHARE**（日志不许第二个进程打开 → `0xE0434352`）。**该守卫必须与 P-LOGSHARE 一起打才能验**；并记下"本机有一个不属于我的 Launcher 实例，我没杀它" |
| `round135-single-instance-verified.md` | **P-SINGLEINSTANCE 行为验证通过**（判定条件命中、僵尸消除）；**但退出码仍是 `0xE0434352`** —— 我调 `Shutdown()` 时生命周期未初始化完，**实现待改**；本轮同时用上了前两轮两条教训 |
| `round136-first-fix-ineffective.md` | **第一个修法无效**：先 `base()` 再 `Shutdown()`，退出码**一点没变** → 假设 1 已排除；列出假设清单与下一个实验（`Environment.Exit(0)`，但须同时看日志行是否还在） |
| `round137-exit0-verified.md` | **`Environment.Exit(0)` 成了**：退出码 **0**、**日志行仍在**（预判的日志丢失未发生 → `LauncherLog` 每次写就落盘）；**P-SINGLEINSTANCE 完全验证通过**，缺陷 12 的僵尸至此修好 |
| `round138-integration-conflict.md` | **二十条合并验证抓到集成冲突**：P-SINGLEINSTANCE 的锚点被 P-STARTUPLOG 破坏（`new WebShellWindow()` 被包进 try/catch）→ 0 处匹配；**单补丁测试发现不了**；记下"文本前置"这一新形态的依赖 |
| `round139-conflict-is-mutual.md` | **修法 (A) 也失败** —— 冲突**互相**（两种顺序各失败一条）；根因是两条补丁**锚在同一段三行代码**；**修法 (B)：把 STARTUPLOG 的锚点缩成单行**（全仓唯一），顺序即无关 |
| `round140-fixB-real-shape.md` | **修法 (B) 的真实形状**：不是"缩小锚点"而是**拆成两步**（`$new` 里两件事插入位置不同）；并指出**直接照上轮说法改会静默丢掉全局兜底**（编译/测试/冷启动都发现不了） |
| `round141-all-twenty-pass.md` | **二十条补丁终于全部通过** —— 把 P-STARTUPLOG 拆成两个单行锚点解决了互相冲突；build 0/0、C# 949/0、Node 305/0、锁定发布、冷启动 PASS；**并验证了诊断本身**（根因是锚点粒度而非顺序） |

---

## 没有对应文件的目标轮次

第 1、4、19 轮**没有独立记录文件**——它们的内容并入了相邻记录或直接写进了
`release-readiness-distance-report.md`。这不是遗漏，但按文件名检索时找不到它们。
