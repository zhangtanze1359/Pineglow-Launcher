# Forge 安装：B2 已修好；B2b（缺 `launcher_profiles.json`）是**真缺陷**；但**端到端仍被环境卡住**（第 78 轮）

日期：2026-10-06（Asia/Hong_Kong）。本轮**未改动工作区一个字节**。
本轮做了什么：把 B2b 的补丁也应用到 `%TEMP%\pg-patched` 副本（`git apply` ✅、重建 **0 警告 0 错误**），
再跑一次真实 Forge 包导入；然后为定位残余失败做了**两组对照实验**。

## 1. 双补丁构建上的 Forge 导入：**仍然失败**（441 秒，`ExitCode=1`）

| 构建 | Forge 导入结果 |
| --- | --- |
| 候选 | `Forge installer 下载失败：HTTP 404`（~330 秒）|
| 只有 B2（URL）补丁 | `Forge installer 失败，ExitCode=1`（197 秒）|
| **B2 + B2b（`launcher_profiles.json`）补丁** | **`Forge installer 失败，ExitCode=1`（441 秒）** |

时间从 197 → 441 秒说明它**做得更多了**（很可能已经越过 profile 检查、进入下载阶段），但最终仍 ExitCode=1。

## 2. 定位残余失败：两组对照实验（同一个 Java、同一个安装器）

| 对照 | 路径形态 | 结果 |
| --- | --- | --- |
| 1 | 纯 ASCII（上轮**成功**过的那种）| **exit 1**：`javax.net.ssl.SSLHandshakeException: Remote host terminated the handshake` / `Caused by: java.io.EOFException: SSL peer shut down incorrectly` → `There was an error during installation` |
| 2 | **含中文与空格**（与产品实例目录同形态）| **exit 1**：**完全相同的 SSL 报错** |

⇒ **残余失败是"安装器的 HTTPS 下载在本机网络环境下握手失败"**（不是中文路径问题——两种路径报错一模一样，我特意验了这一点）。
上轮那次 ASCII + 桩文件跑出 `Successfully installed client into launcher.`（exit 0）是**网络那一刻能通**；现在**同样条件也会失败**。

## 3. 诚实的结论（分三层，别混为一谈）

1. **B2（安装器 URL 多一段）**：**产品缺陷，补丁有效** —— 报错从"下载 404"变成"运行安装器失败"就是证据。
2. **B2b（跑安装器前没有 `launcher_profiles.json`）**：**产品缺陷**（安装器自己明说 `There is no minecraft launcher profile in "<dir>", you need to run the launcher first!`），
   写一个 46 字节最小桩后**同一次实验里**它越过了这道门（`Successfully installed client into launcher.`、落盘 `versions/1.20.1-forge-47.4.3` + 24 个 Forge 库）。
3. **Forge/NeoForge 的"端到端安装"**：**在本机被环境卡住**（安装器自身的 HTTPS 下载握手失败）⇒
   **我不能声称"B2b 补丁让 Forge 装得上"，也不能声称"补丁没用"**。要判定必须换一个网络环境（或让安装器走可用代理）。

## 4. 影响与建议

- 对**产品**：B2 与 B2b 都是**应当修**的真实缺陷（补丁都已备、`git apply --check` ✅）；修完之后，在**正常网络**上 Forge/NeoForge 的安装路径才是可用的。
- 对**本次发布**：发布说明里 Forge/NeoForge 应写"**当前版本无法安装**"（URL 缺陷 + 缺 profile 桩），
  且**不要**把"已在本机验证过 Forge 安装"写进去——本机验不了（环境）。
- 对**验收记录**：`Forge/NeoForge 安装` 一格从"被 B2 阻断"细化为"**被两个产品缺陷 + 本机环境**共同阻断"，其中**产品缺陷已被定位并给出补丁**。

## 5. 复现

```powershell
# ① 双补丁构建上跑 Forge 导入（预期：仍 ExitCode=1，且本机环境因素参与）
powershell -File ...\run-20261006-2900-patched-forge-fixed\verify-patched-forge-fixed.ps1 `
  -PatchedColdDir %TEMP%\pg-patched\dev\PineglowLauncher\bin\Release\net10.0 -MrpackPath <optimized-fps.mrpack> -JsonPath <证据>\patched-forge-fixed.json
# ② 手工跑安装器（ASCII 路径 / 中文路径各一次，都带最小 launcher_profiles.json）
#    见本目录 installer-env-controls.log（两组对照的原始输出）
```

产物：`patched-forge-fixed.json` + `forge-fixed-step.log`（双补丁构建的导入失败）、`installer-env-controls.log`（两组对照：ASCII 与中文路径**同样的 SSL 握手失败**）。
