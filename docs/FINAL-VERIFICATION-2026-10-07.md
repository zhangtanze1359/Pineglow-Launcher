# 复验结论（2026-10-07）：本地技术复验 PASS，但**当前工作区仍缺两处补丁** ⇒ 按现状不可发布

日期：2026-10-07（Asia/Hong_Kong）。**未提交/未推送/未打 tag/未发布**；工作区未被我改动（`modified=47 / staged=0 / HEAD=f145d14`）。

## 1. 源码身份

| 项 | 值 |
| --- | --- |
| HEAD | `f145d14` |
| 工作区改动 | **47** 个已修改 tracked（含另一会话经用户授权的 **5 处修复**：`ConfigStore` 落盘+原子写、`VersionResolver` 参数合并、`InstallerLoaderService` 安装器 URL、`LoaderInstallHelper` profile 桩，及配套 3 个测试文件）|
| 白名单 | `scripts/source-snapshot-inputs.txt` **149** 项 ⇒ 门槛输入 **310**（161 tracked + 149 白名单）|
| **未包含** | 我在本轮复验中定位并验证的**两处补丁**（Forge/NeoForge 启动装配、Java 版本选择）**尚未应用到工作区** |

## 2. PASS（有真机证据）

| 项 | 证据 |
| --- | --- |
| **全量门槛（当前工作区源码，GUID 隔离快照）** | **模式 GREEN / 失败项 0**：Node **305/305**、C# **917/917**、UI 闭包、非 RID 锁定还原、Release build、VerifyHost、win-x64 publish、**独立冷启动 19/19**（`run-20261006-3200-independent-verify/gate/`）|
| **5 处工作区修复的有效性** | B0：`/api/state` 三次 id 相同、`/launch` 不再"实例不存在"；B1：**Fabric 从"必崩"→进渲染管线**（10 图集/0 崩溃），Vanilla/Quilt 回归 PASS；B2：404 消失、**Forge/NeoForge 导入成功**；B2b：安装器日志 `Injecting profile` → **`Successfully installed client into launcher.`** |
| **三种基础启动（当前源码构建）** | Vanilla 1.18.2 PASS（10 图集/0 崩溃）、Fabric PASS（10 图集/0 崩溃）、Quilt PASS（12 图集/0 崩溃）|
| **Forge 1.20.1（打两处补丁的构建）** | PASS：产品自己选 **Java 17**（该版本要求），命令行 19,999 字符、**12 图集 / 0 崩溃**、JVM 存活整个 153 秒观察期 |
| **NeoForge 21.1（MC 1.21.1，打两处补丁的构建）** | PASS：产品**自己选到 Java 21**（`runtimes/java-21-windows-x64`），命令行 22,154 字符、`--add-opens`×2、占位符 0 残留、**382 行日志 / LWJGL / 15 图集 / 0 崩溃**、JVM 存活 |
| 早前会话已完成的大量真机项 | 原版安装/启动/退出/进世界、多实例并发、文件层事务、运行态守卫、**更新全链**、内容三分类安装、镜像源、整合包导入+取消、服务器+ping、Mod 二分、第三方账号失败路径、Java 探测+恢复区 |

## 3. FAIL —— **仅限"未打两处补丁的当前工作区"**

| 缺陷 | 现象 | 状态 |
| --- | --- | --- |
| **①②③ Forge/NeoForge 启动装配** | ① `LauncherService.cs:197 DistinctPreserveOrder()` 吃掉重复选项 token ⇒ 主类变成 `java.base/java.lang.invoke=…`（0 秒退出）；② `BuildReplacements` 缺 `${library_directory}`/`${classpath_separator}`；③ 类路径放入原版 client jar ⇒ 模块冲突 | **补丁就绪**：`run-20261007-3800-forge-launch-patch/proposed-fix-forge-launch-assembly.diff`（8/8 验证）|
| **④ Java 版本选错** | 启动路径把**加载器版本 id**（`neoforge-21.1.230`/`1.20.1-forge-47.4.3`）交给**字符串版**匹配器 ⇒ **默认 17** ⇒ MC 1.21+ 实例必然失败（`Unsupported major.minor version 65.0`），且产品侧零输出 | **补丁就绪**：`run-20261007-4200-java-version-fix/proposed-fix-java-version-selection.diff`（与上一份一起可编译，7/7 验证）|

⇒ **在把这两处补丁应用到工作区之前，Forge/NeoForge 启动与所有 MC 1.21+ 实例都是坏的。**

## 4. NOT_RUN / BLOCKED

| 项 | 类别 | 原因 |
| --- | --- | --- |
| 真实微软账号**成功登录**及取消/超时/切换/续期/离线/皮肤披风 | **NOT_RUN** | 需真人账号完成设备码授权 |
| AI 诊断 | **NOT_RUN** | 需第三方 Key |
| Gitee 固定 `update.json` 源 | **NOT_RUN** | 需 `GITEE_TOKEN` |
| RID 发布 + 锁定还原 | **NOT_RUN（已知边界）** | `packages.lock.json` 未覆盖 win-x64 RID、缺 ILLink 任务包（实测 NU1004）|
| Forge/NeoForge **进入世界 / 长时间游玩** | **NOT_RUN** | 本轮只验到"启动进渲染管线" |
| MC 1.21+ 的 Fabric/Quilt 实例 | **NOT_RUN** | 只验了 NeoForge（同一 Java 选择逻辑，但未单独实测）|
| 提交/推送/tag/Release/Gitee 写入 | **BLOCKED** | 需 D1/D2/D3 授权（D3 另需 token）|

## 5. 结论（技术复验结果，**不构成发布批准**）

1. **"修好后的源码"**（当前工作区 5 处修复 **+** 我的两处补丁）在本地技术复验中 **PASS**：门槛 GREEN/0、五种启动路径（Vanilla/Fabric/Quilt/Forge/NeoForge）全部进渲染管线。
2. **"当前工作区"（未打两处补丁）** 仍然**不能发布**：Forge/NeoForge 启动与 MC 1.21+ 实例不可用。
3. **剩余任务**：① 授权应用两处补丁 → ② 重跑全量门槛并记录**新的单文件 EXE sha256**（旧 `6ed0c8db…` 将作废）→ ③ 真人/凭据类项（微软账号、AI Key、Gitee token）→ ④ 发布授权（D1/D2/D3）。
4. 未逐行审计的既有产品范围、以及 RID 发布还原边界，**继续明确保留**。
