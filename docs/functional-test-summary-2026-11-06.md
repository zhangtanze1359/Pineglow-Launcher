# 功能测试汇总报告（2026-11-06）

> **结论：本地全部自动化功能验证通过（无失败项），但"全部功能验证通过"这一前提应限定为「可在无交互、无真实账号、无真实游戏环境的前提下验证的功能」。**
> 有 6 类功能本机无法验证（见第四节），其中 3 类是产品本身的既有缺口，不是本次改动引入。
> 授权推送**已执行并成功**，两个分支已上远端，推送结果见第五节。

> **测试对象**：`remediation/v1.1.0` HEAD `a994b98`，单文件发布产物 `Pineglow-Launcher-win-x64-single-file.exe`（51,619,122 B，sha256 `8cf2fc63…80e8`）。
> **环境**：Windows 10.0.26200 / .NET 10.0.301 / Node 22.22.2 / 无 WSL、无 Docker。
> **证据目录**：`C:/Pineglow-Backup/ui-verify-20261106/`（截图 10 张 + 5 份 JSON）。

## 一、自动化门槛（全部通过）

| 门槛 | 结果 | 证据 |
|---|---|---|
| C# 单元/集成测试 | **982 / 982 通过，0 跳过** | 详见第一节补充 |
| 前端 Node 测试（29 个文件） | **385 / 385 通过，0 跳过** | `run-inventory-tests.cjs` |
| 测试清单双向一致性 | **通过**（清单 29 + C# 1，磁盘发现 29） | `run-inventory-tests.cjs --check` |
| CSS token 纪律 | **通过**：49 token，字面色值 0、未定义引用 0、死回退 0 | `check-css-tokens.cjs` exit 0 |
| UI 资源闭包 | **通过** | `check-ui-resource-closure.cjs` exit 0 |
| 单文件独立冷启动 | **PASS**，18 项检查全过 | `standalone-cold-start.json` |
| 真实 WebView2 逐页渲染 | **PASS**，10/10 视图 | `report.json` |
| 跨域 API 冒烟 | **PASS**，31/31 | `api-smoke.json` |
| 真实更新链路（默认源降级） | **PASS** | `update-live.json` |
| 诊断评测（合成提供方，零外发） | **PASS**，6/6 用例 | `benchmark-run.json` |

### 1.1 C# 982 项：一次偶发失败及归因

首轮全量跑出 1 项失败：

```
UpdateManifestApiTests.CheckUpdate_AllSourcesFail_ReportsPerSourceReasons
  System.InvalidOperationException : LocalApiService 启动失败（已重试 6 次换端口）
  期望 52711，实际 -1
```

**归因：端口 TOCTOU 偶发，非本次改动引起。**

- 该用例单跑 **3/3 通过**；随后两次全量复跑均 **982/982 通过**。
- 机制在测试夹具注释里已写明：`AcquirePortAsync` 先绑定 `TcpListener` 再释放，随后 `HttpListener` 才绑定，中间的窗口里端口可能被别的 socket 抢走；夹具已重试 6 次换端口，这次 6 次都被抢（本机同时有多个进程占随机高端口）。
- **这不是产品缺陷**，但确实是一个**测试稳定性缺陷**：重试 6 次仍可能不够，且失败信息只说"启动失败"不指向真实原因。**建议后续单独修**（提高重试次数或改为固定端口段 + 严格串行），本次未动，避免与功能验证混在一个提交里。

## 二、真实界面逐页验收（10/10）

产物放进全新中文+空格目录冷启动，通过产品自带 WebView2 远程调试端口连 CDP，逐页真实点击导航（`.nav-item[data-nav]` 的 click），断言容器存在、激活态正确、渲染出非平凡文本，并逐页截图。

| 视图 | 结果 | 状态 | 内容长度 | 说明 |
|---|---|---|---|---|
| home | OK | content | 397 | 实例卡片、游玩统计、下载队列、最新资讯均渲染 |
| installations | OK | content | 91 | 实例列表 + 筛选 + 新建入口 |
| worlds | 成功空态 | empty | 27 | 「还没有存档，进入游戏创建一个世界吧」——请求成功且列表为空，属终态 |
| servers | 成功空态 | empty | 34 | 「还没有添加服务器」——同上 |
| playtime | OK | content | 322 | 时长树状图 + 日历，本月/本周/近7天/近30天/自定义齐全 |
| content | OK | content | 3621 | 搜索真实命中 Modrinth，卡片含版本标签与安装按钮 |
| skins | OK | content | 200 | 3D 预览区显示「无法获取皮肤贴图，请选择本地 PNG 预览」（离线账号无皮肤，属预期降级） |
| downloads | 成功空态 | empty | 1501 | 版本列表 5 条真实数据（26.3 / 26.2 / 26.1.2 / 26.1.1 / 26.1），任务区空 |
| news | OK | content | 1838 | 4 条官方新闻，12 个图片槽位全部落地 |
| settings | OK | content | 568 | 内存/Java/下载源/代理/快捷键/日志/更新/AI 诊断分区齐全 |

**零异常**：`exceptions=0`、`consoleErrors=0`、`productLocalFailures=0`、`uiAssetFailures=0`、`landedErrorStates=0`。

截图逐张人工复核，未发现布局错乱、文字重叠、破图或错误提示。10 张截图在 `shots/` 下。

## 三、功能域逐项验证（31 项 API 冒烟 + 冷启动 18 项）

### 3.1 安全边界（5/5）

| 项 | 结果 |
|---|---|
| index.html 注入会话令牌 | 通过，长度 64 |
| 无令牌访问 `/api/state` | **401** |
| 错误令牌 | **401** |
| 非白名单 Origin（`http://evil.example.com`） | **403** |
| CORS 不写 `*` | 通过（无 Origin 头时 ACAO 不出现） |

### 3.2 各功能域只读接口（15/15 全 200）

系统状态、实例列表、账号、本地版本、更新检查、更新状态、配置回读、Java 列表、统计、下载来源、服务器列表、新闻、下载任务、内容任务、世界列表。

### 3.3 写操作闭环（3/3）

- 创建实例 → 返回 id `62ec3112…` → 读详情 200 → **删除后返回 `removedId` 与 `removedName`**，闭环完整。
- 临时实例在全新目录中创建与删除，**未触碰任何真实游戏目录**。

### 3.4 错误语义（2/2）

- 启动不存在的实例返回 **400 +「实例不存在：__no_such_instance__」**，而非路由兜底 404。
- 确认响应体不含「未找到接口」——这条是 2026-10-03 那 10 条恒 404 事故的直接防线。

### 3.5 启动前自检（1/1）

`GET /api/instances/{id}/prelaunch-check` 返回结构化结果 `{"ok":false,"errors":["实例未关联游戏版本，请先在设置中…"]}`——真实读磁盘并给出可操作的原因，不是空壳。

### 3.6 静态托管与路径安全（3/3）

- CSS 以 `text/css` 正确 MIME 提供。
- 未知路径返回 404，不误投文件。
- `/../app.manifest` 被拦（403），未泄露内容。

### 3.7 单文件冷启动（18/18）

全新中文+空格目录、只放一个 EXE + `portable.flag`：目录内无外置 `UI/`、无 `.dll`；`PINEGLOW_UI_DIR` 等开发旋钮已清除；进程存活；`/api/health` 就绪；10 类关键资源 MIME 与非空正确；内嵌 UI 解包到 `data/UI/1.0.3-42a771a3017f`；**服务出来的字节与候选源码逐字节一致**（防"新服务配旧界面"）。

### 3.8 真实更新链路

默认主源 `https://gitee.com/ztz1359/pineglow-launcher/raw/master/update.json` 返回 **HTTP 404**（该文件在 Gitee 仓库根不存在，我用 Gitee API 独立核实过：根目录只有 `.github` / `dev` / `scripts` / `ui` 与三个文件）。备用源 `github.com/.../releases/latest/download/manifest.json` 本轮 **15s 超时**（本机到 GitHub 通道不稳）。

产品行为**符合设计**：逐源给出真实原因，未伪装成功。判定 PASS。

## 四、本机无法验证的 6 类（如实列出，不当作通过）

| 功能 | 状态 | 原因 |
|---|---|---|
| 真实启动游戏（拉起 Java、进主界面） | **未验证** | 本机无 Minecraft 客户端与可用 Java 运行时；且启动会真实占用磁盘/网络 |
| 微软账号登录（设备码全流程） | **未验证** | 需真实 Microsoft 账号与人工在 microsoft.com/link 输码 |
| Authlib 外置登录 | **未验证** | 需真实认证服务器 |
| AI 崩溃诊断真实调用 | **未验证** | 需真实 AI Key 与费用授权（计划明列为 BLOCKED）；本轮只跑了合成提供方的评测，证明评分器与规则引擎，**不代表真实模型效果** |
| CurseForge 真实搜索/下载 | **未验证** | 需真实 Key 与配额（本机无） |
| 长时游玩统计（跨午夜切分） | **未验证** | 需真实游玩数小时；`playtime` 页只验证了空态渲染与区间口径的单元测试 |
| Ubuntu / macOS runner 复测 | **未验证** | 本机 WSL 与 Docker 被安全策略禁用，**无法本地跑 Linux 验证** |

**已发现的产品级问题（非本次改动引入，如实记录）**：

1. **默认更新源 404**：`ConfigStore.DefaultUpdateUrl` 指向 Gitee 的 `update.json`，但该文件从未被创建（Gitee 仓库根目录确实没有）。原因是历史发布流程里 `GITEE_TOKEN` 未配置，脚本跳过上传与 `update.json` 写入。**后果**：所有用户的默认更新检查都只能靠 GitHub 备用源，而备用源在部分网络下超时 → **更新检查对相当一部分用户实际不可用**。这不是 M1 引入的，但影响面比 M1 任何一项都大，建议单独立项。
2. **删除实例只有 POST**：`POST /api/instances/{id}/delete` 有路由，`DELETE /api/instances/{id}` 没有。前端 `api.js:883` 用的也是 POST，所以**功能正常**，但 REST 语义不统一。列为观察，不改。
3. **无 SPA 深链回退**：访问不存在的路径返回 404 而非回退 `index.html`。当前 UI 是单页 hash 路由（`#home`），不依赖深链回退，**不构成功能缺陷**；但若将来改成 history 路由就会踩到。

## 五、授权推送结果

用户已授权执行推送。结果：

| 分支 | 提交 | 结果 |
|---|---|---|
| `remediation/v1.1.0` | `a994b98`（11 个提交） | **推送成功**，远端已建分支 |
| `docs/remediation-20261106` | `e595af9`（18 个提交） | **推送成功**，远端已建分支 |

验证方式：`git fetch origin --prune` 后比对哈希，两个分支本地与远端**完全一致**。

```
local  v110: a994b98fe80e7ec67bc865df6674de4ddab12121
remote v110: a994b98fe80e7ec67bc865df6674de4ddab12121
local  docs: e595af98998a076d1f1457c3d372cf48a55ca244
remote docs: e595af98998a076d1f1457c3d372cf48a55ca244
```

**推送过程有网络波折**（如实记录）：本机走 `127.0.0.1:59085` 代理，GitHub 的 `CONNECT` 通道间歇性返回 **502**，前 6 次尝试全部失败（含 `schannel: server closed abruptly`、`Empty reply from server`、`CONNECT tunnel failed, response 502`），第 7 次成功。**没有使用任何绕过手段，就是重试**。

### 5.1 推送后的 CI 状态（未触发，需注意）

**本次推送没有触发任何 CI 运行**。`build.yml` 的 `on:` 只有 `push: branches: [main]` + `pull_request` + `workflow_dispatch`——推送到特性分支**不会**触发 Build。

已核实：`actions/runs?branch=remediation/v1.1.0` 返回 **0 条**，`open PRs` 也是 **0 个**。

**所以 M0-4 的 Ubuntu runner 复测仍然没有做**。要真正解掉这个阻塞，需要以下之一：
- 开一个从 `remediation/v1.1.0` 到 `main` 的 PR（会触发 `pull_request` 的 Build），或
- 在 Actions 页面手动 `workflow_dispatch` 选这个分支。

这两步都会在仓库产生对外可见的状态变更（PR 或 CI 运行记录），**我没有擅自执行**，等你确认。
