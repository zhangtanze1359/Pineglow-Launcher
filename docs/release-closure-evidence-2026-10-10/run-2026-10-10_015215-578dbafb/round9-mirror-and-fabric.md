# 第 9 轮：下载源（镜像）与 Fabric 安装的实测

运行标识：`run-2026-10-10_015215-578dbafb`（第 9 轮追加）
时间：2026-10-10 04:14–04:32，Asia/Hong_Kong（+08:00）
本轮性质：**只做验证与取证，产品目录零改动**（A/B/C 相关文件哈希与前几轮一致，0 staged）。
（本轮在隔离夹具里把 `downloadSource` 临时切到 `bmclapi` 做了一次实验，实验后已改回 `official`；产品目录未动。）

## 1. Fabric 在两种下载源下都装不上，但失败点不同

| 配置 | 结果 |
| --- | --- |
| 官方源（第 7、8 轮，共 3 次） | 失败：2 次 `Fabric Meta API ... SSL`、1 次 `无法连接 Minecraft 版本下载源` |
| **镜像源 `bmclapi`（本轮）** | 失败，但**走得更远**：`Fabric 库 2/8 下载失败：下载失败 HTTP 403：fabric-loader-0.19.5.jar`（536 秒） |

镜像那次失败说明：这一次 **Fabric 元数据取到了**（没有再报 Meta API 错误），失败发生在其后的**库下载**阶段。
原始证据：`d-fabric-mirror/loader-matrix.json`、`d-fabric-mirror/matrix-console.log`。

## 2. 镜像重写规则：Fabric 元数据映射是错的（有 HTTP 证据）

`MirrorHelper.RewriteCore` 对 Fabric/Quilt 的重写规则（源码原文）：

| 源 | 重写成 | 实测 |
| --- | --- | --- |
| `libraries.minecraft.net` | `<base>/libraries` | — |
| `maven.minecraftforge.net` | `<base>/maven` | — |
| `maven.neoforged.net` | `<base>/maven` | — |
| `maven.fabricmc.net` | `<base>/maven` | 302 → `cmcc.mirrors.ustc.edu.cn/bmclapi/...` →（跟随跳转）**200** |
| **`meta.fabricmc.net`** | **`<base>` + 原路径** | **404**（应为 `<base>/fabric-meta` + 路径 → **200**） |
| `meta.quiltmc.org` | `<base>/quilt-meta` | **200**（正确） |

即：**Fabric 元数据的镜像前缀写错了**——BMCLAPI 的 Fabric 元数据在 `/fabric-meta/v2/...`，而产品把
`meta.fabricmc.net/v2/...` 直接改写成 `<base>/v2/...`，实测 404；Quilt 的同名规则带了 `/quilt-meta` 前缀、200 正确。
两组 URL 的状态码都由 `curl` 直接验证（见上文表格）。

**但要谨慎表述**：本轮镜像那次安装并没有走到这条错误映射上（元数据取到了），原因是
`FabricService` / `QuiltService` 使用**硬编码的官方地址**、不调用 `MirrorHelper`；
真正会重写的只有 `LoaderInstallHelper.DownloadFileAsync`（库下载）与 `MinecraftDownloader`（Mojang 元数据）。
所以这条错误映射目前至少是**潜在错误**（取决于调用方），一旦有路径走到它就会 404。

## 3. 镜像下的库下载 403：可复现，但未完全归因

产品在镜像下请求的是 `https://bmclapi2.bangbang93.com/maven/net/fabricmc/fabric-loader/0.19.5/fabric-loader-0.19.5.jar`，得到 **403**。

而同一条 URL 用 curl 验证：**302 → `cmcc.mirrors.ustc.edu.cn/bmclapi/...` → 200**（1,984,980 字节，与官方源逐字节同大小）。
即「镜像 maven 路径本身是通的」，403 出现在产品的请求上。

可能原因（**未证实**）：产品客户端未跟随跳转／跳转目标对请求头（如缺失 User-Agent）返回 403。
我尝试用 curl 复现该 403（空 UA、去 UA、直接访问 USTC 目标），但**该次命令的输出被二进制内容污染**（见第 5 节），
没有取得可信读数，**因此不作结论**。下一步需要产品侧把下载失败时的 URL/状态码/响应头记录下来（属产品改动，需授权）。

## 4. 与本轮之前结论的关系

- 第 7 轮结论「Forge/NeoForge 失败原因是安装器子进程没有代理」仍然成立，并已由第 8 轮的双开关实验证实。
- 本轮新增的是**下载源（镜像）维度**的问题：Fabric 在两种源下都失败；镜像下的失败点从「元数据」变成「库下载 403」。
- 镜像选择（`/api/sources`）确实生效：镜像延迟测量走 `<base>/version_manifest_v2.json`，是 Mojang 形状的探针，
  Fabric/Quilt 元数据不在其中——这也是「镜像覆盖范围」的一个可改进点。

## 5. 本轮我自己的两次工具失误（都影响过结果）

1. `curl` 调用有一次漏了 `-o NUL`，把 1.9 MB 的 jar 二进制打进了终端输出，并污染了紧随其后的 UA 对比读数 → **该组 UA 数据作废**，未写入结论。
2. 更早一次用 `Invoke-WebRequest -Headers @{Range=...}` 测状态码，8 个目标全部返回「ERR」（连已知 200 的官方地址也是），属**harness 不可用**，已改用 `curl` 重测（第 2 节的表格来自可靠的 curl 读数）。

## 6. 状态与待决事项

D 的剩余：Fabric（未验证，两个不同失败点）、进世界、退出/重启、30 分钟长时运行、安装中断/重试。
累计待你决定的产品问题新增一条：**下载源（镜像）对 Fabric 元数据的映射错误 + 镜像下库下载 403 的归因需要产品侧可观测性**。

`TechnicalAcceptance=NOT_PASSED`（发布整体）；`FormalPhaseApproval=NOT_GRANTED`；`ReleaseApproval=NOT_GRANTED`。
