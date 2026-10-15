# Forge 安装：**B2 的 URL 修复确认有效，但暴露出第二个缺陷**（缺 `launcher_profiles.json`）（第 77 轮）

日期：2026-10-06（Asia/Hong_Kong）。本轮**未改动工作区一个字节**。
做法：在**打过补丁的构建**上导入同一个真实 Forge 包（`optimized-fps`，1.20.1 + forge 47.4.3），与候选对照。

## 1. B2（安装器 URL）**确认已修好**——报错变了

| | 候选（第 51/52 轮）| 打过补丁的构建（本轮）|
| --- | --- | --- |
| 导入结果 | **`Forge installer 下载失败：下载失败 HTTP 404：forge-1.20.1-47.4.3-installer.jar`**（~330 秒）| **`Forge installer 失败，ExitCode=1。`**（197 秒）|

⇒ **404 消失了**：安装器**已经下载成功**（否则不会进入"运行安装器"这一步）⇒ **B2 的 URL 修复有效**。
但它**又暴露出下一层问题**：安装器**运行失败**。

## 2. 新缺陷（B2b）：产品运行安装器时**没有准备 `launcher_profiles.json`**

我按产品**完全相同的方式**（同一个自动安装的 Java 17、`java -jar <installer> --installClient <gameDir>`、cwd=gameDir）手工复现，拿到了安装器自己的输出：

```
There is no minecraft launcher profile in "<gameDir>", you need to run the launcher first!
There was an error during installation
exitCode=1
```

**决定性对照**：往同一个 gameDir 里写一个最小的 `launcher_profiles.json`（46 字节：`{"profiles":{},"settings":{},"version":3}`）后，**同一个安装器、同一个 Java、同一台机器**：

```
Successfully installed client into launcher.
exitCode=0
```
并且真的落盘了：`versions/1.20.1-forge-47.4.3`、`versions/1.20.1`、`libraries/net/minecraftforge/…`（**24 个文件**）。

⇒ **根因确定**：现代 Forge/NeoForge 安装器要求目标目录里有 `launcher_profiles.json`（其他启动器都会在跑安装器前写一个最小桩文件）；
产品的 `LoaderInstallHelper.RunInstallerAsync` **只传 `--installClient <gameDir>`，没有准备该文件** ⇒ 安装器直接以 ExitCode=1 退出。
⇒ **即使 URL 修好，Forge/NeoForge 仍然装不上**（这是同一功能上的**第二个**缺陷）。

## 3. 修法（已生成未应用补丁）

在 `RunInstallerAsync` **启动安装器之前**，若 `_gameDirectory` 下没有 `launcher_profiles.json` 就写一个最小桩：
```json
{"profiles":{},"settings":{},"version":3}
```
补丁：`proposed-fix-forge-launcher-profile.diff`（**未应用**，本目录）。
**注意**：这与 B2 的 URL 补丁**互不替代**，两个都要。

## 4. 顺带一条诊断性观察（与决定项 14 同源）

产品其实**捕获了安装器的 stdout/stderr 并逐行 `_log`**（`RunInstallerHelper.RunInstallerAsync` 里 `foreach (string obj in array) _log(obj);`），
但 **API 导入路径的 `_log` 是空委托**（`new InstancePackService(ApiHttp, defaultGameDir, _ => { })`）⇒ 用户只看到 `Forge installer 失败，ExitCode=1`，
**看不到"缺少 launcher profile"这句关键提示**。建议把导入路径的日志接到真正的日志/界面提示（与决定项 14 的第①条同源）。

## 5. 复现

```powershell
# ① 补丁构建导入真实 Forge 包（会走到"ExitCode=1"）
powershell -File ...\run-20261006-2800-patched-forge\verify-patched-forge.ps1 `
  -PatchedColdDir %TEMP%\pg-patched\dev\PineglowLauncher\bin\Release\net10.0 -MrpackPath <optimized-fps.mrpack> -JsonPath <证据>\patched-forge.json
# ② 手工复现安装器（同一 Java、同一参数）——先不带 launcher_profiles.json，再带上
#    见本目录 installer-diag.json / installer-diag-with-profile.json
```

产物：`patched-forge.json`（导入失败的原文）、`forge-step.log`、`installer-diag.json`（**无** profile 时的安装器输出：`There is no minecraft launcher profile …`、exit 1）、
`installer-diag-with-profile.json`（**有** profile 时：`Successfully installed client into launcher.`、exit 0）、`proposed-fix-forge-launcher-profile.diff`。
