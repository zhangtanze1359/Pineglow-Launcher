# 第 11 轮：README 承诺「断点续传 + 可恢复任务队列」的实测

运行标识：`run-2026-10-10_015215-578dbafb`（第 11 轮追加）
被测版本：发布候选 EXE `CA8F94C1A6173F23F38F3D2A3C949AFFC16D5AB67752B662C7806831F350D93E`
（与 A 阶段 4 次流水线 PASS 发布的 EXE 逐字节相同）
本轮性质：**只做验证与取证，产品目录零改动**（0 staged；A/B 相关文件哈希与前几轮一致）。
被测承诺出处：`README.md:10` ——「**版本**：官方版本安装 / 校验 / 修复，**断点续传 + 可恢复任务队列**」。

本轮把这一句拆成**两个可独立证伪的子承诺**分别验证：

| 子承诺 | 结论 | 证据强度 |
| --- | --- | --- |
| ① 断点续传（单文件字节级续传） | **成立** | 确定性复现（注入截断临时文件 → Range 续传 → SHA-1 与原件一致） |
| ② 可恢复任务队列（任务跨进程存活） | **不成立** | 代码级零引用 + 硬杀进程后 `GET /api/downloads` 返回空表 |

---

## 1. 子承诺 ① 断点续传：成立（确定性证据）

### 1.1 代码事实（`MinecraftDownloader.DownloadFileAsync`，只读）

实现确实是**真正的字节级续传**，不是「重下整文件」：

- 下载写入 `<目标文件>.download` 临时文件；落盘前用 **SHA-1 校验**，匹配才提升（promote）为正式文件；
- 已存在临时文件时按已有字节数设置 `request.Headers.Range = new RangeHeaderValue(existingBytes, null)`；
- 按响应是不是 `206 PartialContent` 决定追加还是重写（`append = existingBytes > 0 && PartialContent`）；
- 处理 `416`（Range 不满足）等边界；
- 续传时向消息流发 `继续下载：<文件名>（已完成 <大小>）`。

### 1.2 确定性实测（`d-range-resume2`）

为避免「靠时序猜有没有命中续传」，探针**主动构造**了唯一的续传场景：

1. 记下正式客户端的 SHA-1：`ba2df812c2d12e0219c489c4cd9a5e1f0760f5bd`；
2. 删掉正式 jar，写入一个**截断的** `1.21.11.jar.download`（1,048,576 字节 = 1 MiB，取自真实 jar 前缀）；
3. 向产品 API 重新下发安装；
4. 抓取 `GET /api/downloads` 的消息流。

实测消息流（`d-range-resume2/range-resume.json` 的 `messagesSeen`，原文）：

```
正在获取 Minecraft 1.21.11 元数据...
下载：1.21.11.jar
继续下载：1.21.11.jar（已完成 1.0 MB）      <-- 命中续传路径
正在下载游戏资源：3000/4591
```

结果：

| 指标 | 值 |
| --- | --- |
| 续传后 jar SHA-1 | `ba2df812c2d12e0219c489c4cd9a5e1f0760f5bd` |
| 与截断前原文件是否一致 | **是**（`jarMatchesOriginal: true`） |
| 残留 `.download` 临时文件 | 无（`leftoverTemp: false`） |
| 本次耗时 | 12 秒 |

即：**「已完成 1.0 MB」被识别、只补传剩余部分、结果文件逐字节正确、临时文件清理干净**。子承诺 ① 成立。

### 1.3 硬杀进程后的续传行为（`d-resume2`，弱证据，仅作旁证）

对 1.21.11 做一次完整安装，在 112 秒时**强制杀掉**进程：

| 阶段 | 读数 |
| --- | --- |
| 杀掉前已落盘 | 226,147,451 字节（215.7 MB） |
| 杀后已落盘 | 226,248,199 字节 |
| 杀时可见的 `.download` 残片 | 0 个 |
| 重启后 `GET /api/downloads` | `{"tasks":[]}` |
| 重新下发后完成 | 213 秒，总计 552,196,543 字节 |
| 终态 | 1 个版本 json / 1 个版本 jar / 75 个库 jar / 4591 个资源对象 / 0 个残留 `.download` |

**诚实标注两点局限**：

- 杀进程时**看不到** `.download` 残片（`partialCount: 0`），所以这一次**没有直接观测到续传**；原因是资源阶段都是小文件，临时文件存在时间极短，抓不到。真正的续传证据是上面 1.2 的注入式复现，不是这一次。
- 因此本节的正确读法是：**已经落盘的 215.7 MB 在重启后仍在**，重新下发不必从零开始——这是「文件级续传」的实用价值；但**任务本身没有存活**（见第 2 节）。

---

## 2. 子承诺 ② 可恢复任务队列：不成立

### 2.1 「可恢复队列」的模型确实存在——但没有任何代码用它

产品里有一套看起来正是为这个承诺准备的数据模型：

| 位置 | 内容 |
| --- | --- |
| `dev/.../Pineglow.Launcher.Models/AppConfig.cs:41` | `public List<VersionDownloadQueueItem> DownloadQueue { get; set; } = new List<VersionDownloadQueueItem>();` |
| `dev/.../Pineglow.Launcher.Models/VersionDownloadQueueItem.cs` | 任务项（含 `State`、进度文案等） |
| `dev/.../Pineglow.Launcher.Models/VersionDownloadQueueState.cs` | 枚举 `Waiting / Downloading / Paused / Completed / Failed` |

对 `dev`、`ui`、`scripts` 全量检索这三个符号（排除 `bin`/`obj`/`node_modules`），命中的**全部**是上表这三处声明本身：

```
AppConfig.cs:41                        声明
VersionDownloadQueueItem.cs:3          类型定义
VersionDownloadQueueItem.cs:7,17,18,19 类型内部自引用
VersionDownloadQueueState.cs:3         枚举定义
```

**结论：`AppConfig.DownloadQueue` 是无生产者、无消费者、无 API、无 UI 的死数据。** 它被序列化进 `config.json`，永远是空数组。

### 2.2 真正在用的队列是**进程内内存表**

| 位置 | 内容 |
| --- | --- |
| `LocalApiService.cs:143` | `private static readonly List<DownloadTaskEntry> DownloadTasks = new List<DownloadTaskEntry>();` |
| `LocalApiService.cs:4329` | 下发下载时 `DownloadTasks.Add(entry)` |
| `LocalApiService.cs:4602-4620` | `HandleListDownloadsAsync` 只把这张内存表快照成 `{ tasks: [...] }` |
| `LocalApiService.cs:779-781` | 路由 `GET /api/downloads` → 上面的 handler |
| `ui/js/api.js:659-660` | 前端 `getDownloads()` 调 `/api/downloads`，下载中心据此渲染 |

`DownloadTasks` 是 `static readonly List`，**只活在进程生命周期内**：没有落盘、没有启动时读回、与 2.1 那套持久化模型之间没有任何连线。

### 2.3 实测后果

`d-resume2` 里硬杀进程再重启后，产品自己返回：

```json
{"tasks":[]}
```

即：**重启后任务队列为空**——已经在跑或排队中的下载任务，重启后既不显示、也不自动恢复、也没有暂停/失败记录。用户能看到的只有「文件还在磁盘上」这一件事（子承诺 ① 的功劳），任务身份、进度、状态全部丢失。

### 2.4 准确表述

README 这一句应当拆成两半来评价：

- 「**断点续传**」——**名副其实**，且实现质量不低（Range + 206 + SHA-1 提升 + 416 处理）。
- 「**可恢复任务队列**」——**名不副实**。模型层的名字（`DownloadQueue` / `Waiting/Paused/Failed`）会让人以为已实现，但**零引用**；实际队列是内存表。

影响面（对「距离发布」的意义）：**中等偏低**。它不是崩溃或数据损坏类缺陷——用户的文件不会丢，重发一次下载就能接着传。它伤害的是**产品承诺的可信度**与重启后的可预期性（用户重启看不到刚才的任务，容易以为丢了进度而重复点）。修复路径也清楚：把 `DownloadTasks` 的增删改同步进 `AppConfig.DownloadQueue`，启动时读回并按 `State` 决定是否继续；两者本来就是为彼此准备的。

---

## 3. 本轮我自己的工具失误（已修正）

1. `probe-range-resume.ps1` 的收尾断言仍在检查 `resumeLogLines`（来自一个产品日志文件），而**本轮的证据通道已改成 API 消息流 `messagesSeen`**，导致第 11 轮那次运行以 `exit 4` 结束——**退出码是断言过时，不是被测对象失败**（`messagesSeen` 里的续传消息如上，客观存在）。已把断言改为检查 `messagesSeen` 中的「继续下载」，并复跑取到 `exit 0`（见 `d-range-resume3`）。
2. 同一次编辑把该 `.ps1` 的 **BOM 弄丢了**（本仓库规则：非 ASCII 的 `.ps1/.psm1` 必须带 BOM），PowerShell 5.1 按 GBK 读会报 6 个解析错误。已补回 BOM，`Parser::ParseFile` 复验 0 错误。
3. 用 `powershell.exe -File ... -Entries @(@{...})` 传数组参数**静默失效**（`-File` 只接受字符串参数）：探针收到全 `null`，生成了一个没有依赖项的 `d7-.mrpack`，被产品正确拒收（`整合包 dependencies 缺少 minecraft 版本。`）。该次运行 `d-fabric-retry2` **判定为无效、作废**；已改为在当前进程内直接调用探针（`d-fabric-retry3`）。

---

## 4. 累计状态（第 11 轮结束时）

- D 的已验证部分：原版 1.21.11 安装 + Java 自动安装 + 启动到标题界面；三种加载器（Quilt 0.30.1 / Forge 61.2.1 / NeoForge 21.11.45）经整合包导入装成并启动到标题界面；**续传（本轮）**。
- D 的未验证部分：**Fabric**（两个不同失败点，本轮正在复测）、**进世界**、**退出/重启**、**30 分钟长时运行**（后三项需要桌面/GUI 自动化，本环境不可达）。
- 累计待你决定的产品问题（新增第 7 条）：
  1. 加载器安装入口只有整合包导入一条路（`OptiFineService` 不存在）；
  2. 代理没有传给加载器安装器 JVM（A/B 已证实）；
  3. 安装器 stdout/stderr 被捕获后丢弃，用户只看到 `ExitCode=1`；
  4. 整合包导入没有进度/日志；
  5. Fabric 元数据的镜像重写规则写错（`meta.fabricmc.net` → 404，应为 `/fabric-meta`）；
  6. 镜像下经 302 跳转的库下载 403（疑与 User-Agent 有关）；
  7. **【本轮新增】`AppConfig.DownloadQueue` 等一套「可恢复任务队列」模型零引用，实际队列是进程内内存表 → README 的「可恢复任务队列」未实现。**

状态串：`TechnicalAcceptance=NOT_PASSED`（发布整体）；`FormalPhaseApproval=NOT_GRANTED`；`ReleaseApproval=NOT_GRANTED`。
