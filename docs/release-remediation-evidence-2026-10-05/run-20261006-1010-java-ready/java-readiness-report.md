# 真实 JRE 就绪验证：**PASS**（6 项 0 失败）—— Java 这一段到"启动游戏"之前已全部打通（第 42 轮）

日期：2026-10-06（Asia/Hong_Kong）。本轮**未改动任何 tracked 文件**。
工具：`verify-java-ready.ps1`（本目录，可复跑）。目的：证明**Java 运行时的来源、布局约定与产品发现逻辑**都成立，
把 R06「Java/版本」里除"真的启动游戏"以外的最后一段补齐。

## 1. 做法（诚实说明它与"跑产品自带安装"的区别）

产品自带的 `JavaRuntimeService.InstallAsync` **只有整合包导入一个调用点**（见
`run-20261006-1000-java-entry/java-entry-finding.md`），普通流程里没有入口。
所以本轮**按它的目录约定**（`<配置目录>/runtimes/java-<大版本>-<os>-<arch>/…/bin/java.exe`，
`JavaRuntimeService.cs:84-125`、`149-166`）用**同一来源、同一校验和**放入一份真实 JRE，
再让产品自己去发现它。

> 边界：这**不是**在跑 `InstallAsync` 本身（下载+校验+解压那三步）；它验证的是
> "**来源可用 + 校验和可核 + 布局约定正确 + 产品的发现与检查逻辑正确**"。

## 2. 实测结果（Adoptium Temurin JRE 17.0.20.1）

| # | 检查 | 结果 |
| --- | --- | --- |
| 1 | Adoptium 元数据可取 | ✅ `OpenJDK17U-jre_x64_windows_hotspot_17.0.20.1_1.zip` |
| 2 | 下载完成且 **SHA256 与 Adoptium 声明一致** | ✅ 43,780,109 B，26 秒，`bc21a9392310…` 两侧一致 |
| 3 | 解压后存在 `java.exe`（产品也是递归查找） | ✅ `…\runtimes\java-17-windows-x64\jdk-17.0.20.1+1-jre\bin\java.exe` |
| 4 | **真实 JRE 可执行** | ✅ `openjdk version "17.0.20.1" 2026-08-18`（84 ms）|
| 5 | **产品发现 `runtimes/` 下的 JRE** | ✅ `/api/java` → `installations=1  major=17`，路径正确 |
| 6 | **Java 就绪后 `prelaunch-check` 不再报 Java 缺失** | ✅ `ok=True`，`errors=`（空）|

对比上一轮（`run-20261006-1000-java-entry/`）：同一实例在**没有 Java** 时 `ok=False`、
报"未找到可用的 Java 运行时…"且 `POST /launch` 被 400 挡住；装上真实 JRE 后**同一检查变成通过**。

## 3. 这条证据把 R06「Java/版本」拆到了最后一步

| 环节 | 状态 |
| --- | --- |
| 版本清单（917 个真实版本）| ✅ 第 36 轮 |
| **原版下载 + 完整性**（client.jar sha1、assets 逐对象）| ✅ 第 40 轮（10 项 0 失败）|
| **Java 运行时**：来源、校验和、布局、产品发现、prelaunch-check | ✅ **本轮** |
| **用 Java 真正启动游戏并退出** | ❌ 仍需真机（且要有桌面/显卡；本机是虚拟桌面）|
| 加载器安装样例（Fabric/Quilt/Forge/NeoForge）| ⚠️ 端点可达；安装器路径未实测（冻结期不建议跑）|

⇒ 现在"启动游戏"缺的**只剩"在有桌面与显卡的真机上点一次启动"**：
版本已能装、Java 已能就绪、prelaunch-check 已通。

## 4. 顺带留下的一个可选验证（下一轮可用）

本轮的 JRE 证明了 Java 17 路径可用。若要验证**现代版本端到端到启动前**，
可以装 `1.20.1`（client 28 MB + 113 库 + **assets 402 MB**，约 10 分钟）并保留 Java 17，
然后跑 `prelaunch-check` 的完整通过路径与（可选）一次真实启动。本轮**没做**，因为
"启动游戏"明确属真机项，且 400 MB 级下载对当前结论没有增量。

## 5. 复现命令

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File `
  docs\release-remediation-evidence-2026-10-05\run-20261006-1010-java-ready\verify-java-ready.ps1 `
  -ExePath %TEMP%\pineglow-keep\candidate-5009cf73d0801e83.exe `
  -WorkRoot %TEMP%\pg-java-ready `
  -JsonPath docs\release-remediation-evidence-2026-10-05\run-20261006-1010-java-ready\java-ready.json
# 期望：真实 JRE 就绪验证判定: PASS（6 项 0 失败，约 1 分钟，含 43MB 下载）
```

产物：`java-ready.json`（元数据/校验和/实测值/6 项断言）、`java-ready.log`。

## 6. 本轮我踩到的一个坑（记下来）

`java -version` 把版本信息写到 **stderr**，而脚本里 `$ErrorActionPreference='Stop'` 会把
`NativeCommandError` 当成致命错误终止脚本（第一次跑就是这样"失败"的，其实 JRE 已经装好且能跑）。
已在脚本里临时把 `$ErrorActionPreference` 设为 `Continue` 再捕获。
