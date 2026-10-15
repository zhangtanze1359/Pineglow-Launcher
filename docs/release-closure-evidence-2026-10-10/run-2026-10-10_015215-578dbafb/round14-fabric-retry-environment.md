# 第 14 轮：Fabric 复测 —— 这次没有产品错误，是**探针超时**

运行标识：`run-2026-10-10_015215-578dbafb`（第 14 轮追加）
时间：2026-10-11 14:15–14:35，Asia/Hong_Kong（+08:00）
本轮性质：**只做验证与取证，产品目录零改动**（0 staged；探针与夹具都在 `%TEMP%`）。
证据：`d-fabric-retry3/`（有效）与 `d-fabric-retry2/`（无效，见第 4 节）。

---

## 1. 结论

**这一次 Fabric 导入没有报出任何产品错误**：既没有第 7/8 轮的 `Fabric Meta API ... SSL`，也没有第 9 轮镜像下的 403。
它在探针给的 **1200 秒预算用尽**时被中止，终止信息是 HTTP 客户端侧的「请求被中止: 操作超时。」。
终止时它**仍在资源（assets）阶段**，还没走到加载器安装。

所以 Fabric 依然是**未验证**，但**阻断原因的分类变了**：
第 7–9 轮记的是「产品错误（元数据 SSL／库下载 403）」，本轮是「**本机网络太慢，20 分钟不够**」。
按第 12 轮已经确证的环境事实（单个 200 KB 的 JSON 要 9–20 秒，且经常直接连接失败），这个解释是自洽的，**不应记为产品缺陷**。

---

## 2. 实测读数（`d-fabric-retry3/loader-matrix.json`）

| 字段 | 值 |
| --- | --- |
| 探针 | `loader-matrix-via-modpack-import`（走整合包导入，产品唯一的加载器安装入口） |
| 加载器 | fabric / MC 1.21.11 / 依赖 `fabric-loader` **0.19.5** |
| `healthReady` | true（启动器正常起来了） |
| `importOk` | **false** |
| `importSeconds` | **1200**（= 探针 `ImportTimeoutSec`，即预算用尽） |
| `importResponse` | `请求被中止: 操作超时。` |
| `instanceLoader` / `loaderProfileOnDisk` | 空 / false |
| `launchResponse` / `reachedTitle` | 未执行 |
| 工作树残留进程 | 无（探针 `finally` 正常收尾） |

导入超时那一刻磁盘上的状态（`...\instances\d7-fabric 导入\`）：

| 项 | 值 |
| --- | --- |
| 文件总数 / 体积 | 4,669 个 / **525.6 MB** |
| `versions/` | 只有 `1.21.11`（原版）——**没有 Fabric 加载器 profile** |
| `libraries/` 里的 jar | **75 个，全部是原版库**，没有任何 `net/fabricmc/...` |
| `assets/` | 4,590 个正式对象 + **2 个仍在传输的 `.download` 临时文件** |
| 首次/末次写入时间 | 14:15:31 → **14:35:27**（探针 14:35:23 超时，写盘随即停止） |

**读法**：原版那一整套（版本 json + 75 库 + 4591 资源）基本下完了，卡在资源阶段的最后几个对象上；**加载器阶段根本没开始**。
这与「产品坏了」是不同的失败：没有错误码、没有拒绝、没有校验失败——**它只是慢**。

---

## 3. 与第 7/8/9 轮的三次失败对比

| 轮次 | 下载源 | 观察到的失败 | 分类 |
| --- | --- | --- | --- |
| 7、8（共 3 次） | 官方 | 2 次 `Fabric Meta API ... SSL`、1 次「无法连接 Minecraft 版本下载源」 | 当时记为产品侧，**第 12 轮已改判为环境待定** |
| 9 | 镜像 `bmclapi` | 走得最远：`Fabric 库 2/8 下载失败：HTTP 403`（536 秒） | 第 12 轮已定位为 UA 缺陷 |
| **14（本轮）** | 官方（夹具里 `proxyEnabled=true` → 走 127.0.0.1:7897） | **元数据成功、零产品错误，1200 秒预算耗尽** | **环境（网络过慢）** |

即：同一台机器上，Fabric 元数据这次是**通的**——第 7/8 轮的 SSL 失败确实不是稳定复现的产品缺陷。

---

## 4. 本轮被作废的那次运行（工具失误，保留记录）

第一次调用探针时我用了：

```
powershell.exe -NoProfile -ExecutionPolicy Bypass -File probe-loader-matrix.ps1 ... -Entries @(@{loader='fabric';mc='1.21.11';dep='fabric-loader';ver='0.19.5'})
```

**`-File` 只接受字符串参数**，数组/哈希表会**静默失效**：探针收到的 `Entries` 是 `$null`，
于是生成了一个**没有依赖项**的 `d7-.mrpack`（文件名里的 loader 就是空的），被产品正确拒收：

```json
{"error":"整合包 dependencies 缺少 minecraft 版本。"}
```

→ 该次运行（`d-fabric-retry2`，4 秒结束）**判定无效并作废**，不作任何结论。
改正后改为在当前 PowerShell 进程内直接调用探针（`& $probe -Entries $entries`），才有 `d-fabric-retry3` 的有效读数。

（同类失误本会话已出现过一次：`ProcessStartInfo.ArgumentList` 在 .NET Framework 下不存在，导致 Forge 安装器无参数空跑。）

另一处失误：统计「哪些库属于 Fabric」时我对**完整路径**做了 `-match 'fabric'`，而实例目录名本身就叫 `d7-fabric 导入`，于是 75 个原版库全部被误判为 Fabric 库。按相对路径重新统计后才是第 2 节的正确数字。

---

## 5. 要让 Fabric 真正验证通过，需要什么

- 探针预算：把 `ImportTimeoutSec` 从 1200 提高（例如 3600），或
- 复用已有原版实例：Fabric 安装不必每次重下 500 MB 的**原版**资源——本轮 20 分钟几乎全花在原版 assets 上。夹具里已有多个装好原版的实例，可让探针在「原版已完成」的夹具上只跑加载器阶段（省掉 assets 阶段 = 数量级的加速）。
- 环境：等本机到 Mojang/镜像的网络恢复到正常水平（本轮实测：直连 Mojang 6 次里 2 次直接失败，成功也要 10–20 秒）。

以上都属**验证方法**的调整，不涉及产品改动。

---

## 6. 累计状态

- D 的已验证：原版 1.21.11 安装 + Java 自动安装 + 启动到标题界面；Quilt 0.30.1 / Forge 61.2.1 / NeoForge 21.11.45 导入并启动到标题界面；断点续传（注入式确定性复现）。
- D 的未验证：**Fabric**（本轮由「产品错误」改判为「环境过慢」）、进世界、退出/重启、30 分钟长时运行（后三项本环境不可达）。

状态串：`TechnicalAcceptance=NOT_PASSED`（发布整体）；`FormalPhaseApproval=NOT_GRANTED`；`ReleaseApproval=NOT_GRANTED`。
