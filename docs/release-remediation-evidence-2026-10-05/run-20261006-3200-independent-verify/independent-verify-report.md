# 独立复验（当前源码，已应用 4 处修复）：门槛 GREEN / Fabric 启动 PASS / Forge 安装器实测 **exit 0**（第 83 轮，进行中）

日期：2026-10-06（Asia/Hong_Kong）。复验对象：**当前工作区源码**（`D:\松光启动器`，HEAD `f145d14` + 未提交的修复改动）。
纪律：**未提交/未推送/未打 tag/未发布**；未 reset/restore/checkout/clean/stash；未批量暂存；未改历史证据。

## 1. 源码身份（独立记录）

| 项 | 值 |
| --- | --- |
| HEAD | `f145d14`（`feat: 微软登录改设备码流…`）|
| 已修改 tracked | **47**（含 4 处修复：`ConfigStore.cs` +143、`VersionResolver.cs` +24、`InstallerLoaderService.cs` ±1、`LoaderInstallHelper.cs` +22）|
| 白名单 | `scripts/source-snapshot-inputs.txt` **149 项** |
| 快照输入 | **310**（161 tracked + 149 白名单），门槛自检「期望 310 / 实际 310」一致 ✅ |
| 新单文件 EXE | 53,052,722 B，**sha256 `6ed0c8db5afb3f1372181b85be3a459d3cba621f616075e1900361defab926f0`**（与修复前的 `5009cf73…` 不同，符合预期）|

## 2. 门槛复跑（独立执行，GUID 隔离快照）：**模式 GREEN / 失败项 0**

| 步骤 | 我的独立结果 |
| --- | --- |
| step0 运行器自检 | **PASS** |
| step1a/1b/1c UI 闭包 + 清单 | **PASS** |
| **step1 Node** | **PASS — `pass 305`** |
| step2/3 锁定还原 | **PASS** |
| step6 Release build | **PASS** |
| **step7 C#** | **PASS — `失败 0 / 通过 917 / 总计 917`** |
| step8 VerifyHost + 8b 二分冒烟 | **PASS** |
| step10 win-x64 publish | **PASS** |
| **step11 独立冷启动** | **PASS — 19 项全通过** |
| 已知边界 | `publish win-x64 (锁定还原探测)` **NU1004**（RID 不在锁定还原范围）——**保留，未掩盖** |

⇒ 与实施方报告的数字（Node 305 / C# 917 / 冷启动 19/19）**独立一致**。
证据：`gate/run-console.log`、`gate/step7-dotnet-test-release.log`、`gate/step1-node-tests.log`、`gate/standalone-cold-start.json`。

## 3. 真实启动回归（当前源码 EXE，全新便携目录）

| 用例 | 结果 |
| --- | --- |
| **Fabric**（真实包 Fabulously Optimized 1.18.2）| **PASS**：导入 ok、`loaderType=Fabric`、命令行 **11,002 字符含 `-Djava.library.path`**、日志 `with Fabric Loader`、**LWJGL + 声音 + 10 图集 + 0 崩溃**、1,335 行、JVM 存活整个观察期 |
| **Vanilla 1.18.2** | **本轮 FAIL（我的用例顺序问题，不是产品缺陷）**：全新便携目录**没有 Java 运行时** ⇒ `prelaunch ok=False：未找到可用的 Java 运行时，请先在设置中指定 Java 路径`、启动被正确拒绝。**待重跑**（同目录在整合包导入后已有 Java）|
| **Quilt**（真实包 ukupvp）| **进行中**：实例 `ukus-pvp-modpack 导入` 已创建、assets 已下 447 MB，导入调用仍在阻塞（本轮未取到终态）|

证据：`launches.log`、`launch-regress-step.log`、`fabric-cmdline.txt`、`fabric-game.log`。

## 4. **重要更正：Forge 安装器在本机其实能跑通**（此前"环境阻断"结论需要修正）

用产品同款 Java 17 + 官方安装器手工对照（都带最小 `launcher_profiles.json`）：

| 对照 | 结果 |
| --- | --- |
| **不带代理** | **exit 0（99 秒）**，落盘 `versions/1.20.1`、**`versions/1.20.1-forge-47.4.3`** 与全部依赖库 ✅ |
| 带代理（我猜的 `127.0.0.1:7890`）| exit 1：`java.net.ConnectException: Connection refused`（本机 WinINET 代理实际是 **`127.0.0.1:7897`**，我猜错了端口）|

⇒ 上一轮（第 78 轮）观察到的 `SSLHandshakeException` 是**当时网络瞬时失败**，**不是稳定的环境阻断**。
⇒ 因此 **B2 + B2b 的产品级端到端（真实 Forge 包导入）现在可测**，本轮尚未执行（待下一轮补）。
证据：`forge-tls-control.log`（两组对照原始输出）。

## 5. 环境边界（独立诊断，供交接）

**在 `D:\松光启动器` 直接跑 `dotnet test` 会大面积失败**（我这次：**467 失败 / 450 通过 / 917**），失败原因**不是业务 RED**：
- 失败信息高度集中：`System.UnauthorizedAccessException: Access to the path 'C:\Users\Administrator\AppData\Local\Temp\pineglow-*' is denied.`（**447 条不同路径**，全部是测试自建临时根）。
- 最小复现（我做的对照）：**同一路径模式**从 pwsh、`cmd` 子进程、`dotnet exec` 探针（身份 `PC-20260705RBJQ\Administrator`、TEMP 相同）创建**全部成功**；
  但 **xUnit 测试宿主**在 `ConfigBackupRotationTests..ctor` 第 32 行的**普通 `Directory.CreateDirectory`** 上被拒。
- 结论：**这是测试宿主进程的权限/沙箱边界**（`%TEMP%` 的 ACL 里有 `CodexSandboxUsers` 组与一个裸 SID，来自沙箱写入），**不是产品代码问题**；
  在 `%TEMP%` 下的 GUID 隔离快照里跑则 **917/917 全绿**（§2）。
- 我**没有**调整任何系统 ACL 或安全设置（按交接要求）。

## 6. 本轮状态汇总（诚实口径）

| 项 | 状态 |
| --- | --- |
| 当前源码门槛（Node/C#/publish/冷启动）| **PASS**（独立复现，GREEN/0）|
| Fabric 真实启动 | **PASS** |
| Vanilla 真实启动 | **FAIL（我的用例顺序）→ 待重跑** |
| Quilt 真实启动 | **NOT_RUN（进行中）** |
| Forge 安装器（手工、产品同款 Java）| **PASS（exit 0）** |
| Forge **产品级**导入 | **NOT_RUN（下一轮）** |
| 真人 Microsoft 登录 / AI Key / Gitee token / 正式发布授权 | **BLOCKED**（外部条件，未伪造通过）|
| RID 锁定还原边界 | **已知未通过（NU1004），保留** |
| 既有产品范围逐行审计 | **未完成（未复核比例本轮未重新测量）** |

---

## 7. 追加：Quilt 导入在本轮实测中**响应挂起**（文件已落盘，HTTP 未返回）

- 时间线：`23:46:39` 发出 `POST /api/instances/import`（真实 Quilt 包）→ 至 `00:00` 仍未返回（**>13 分钟**）。
- 但**工作其实已经做完**：`GET /api/state` 返回 **4 个实例**，其中 `ukus-pvp-modpack 导入` 的 `loaderType=Quilt`、`minecraftVersion=quilt-loader-0.21.0-1.20.1`；
  `GET /api/downloads` 显示该导入的下载任务 **`[completed] 安装完成`**；实例目录 **749.2 MB**，`versions/1.20.1` 与 `versions/quilt-loader-0.21.0-1.20.1` **都在**，`runtimes` 已装 1 个。
- 同时：启动器进程 **CPU 不再增长**（20 秒内 49.8s → 49.8s，增量 0），内存 241 MB、103 线程 ⇒ **空闲等待**（不是 CPU 忙等），
  而**其他 API 仍然正常响应**（`/api/state`、`/api/downloads` 都秒回）⇒ **服务端活着，只有这个导入请求的响应一直不返回**。
- 口径：这是**本轮新观察到的现象**，**尚未定位根因**（可能是我这次并发跑了 Forge 安装器对照、占用带宽导致某个下载阶段无超时等待；
  也可能是导入收尾阶段的 I/O 无超时）。**不把它写成"产品缺陷"也不写成"已通过"**；下一轮先复跑一次纯净 Quilt 导入以判断是否可复现。
- 保留：`launch-regress-step.log`（无 `[quilt] 导入 ok=` 行）、实例目录与 `versions` 列表、CPU 增量观察、`/api/state` 与 `/api/downloads` 的响应内容。