# 第 21 轮：补丁的**行为验证**（含一次 A/B 直接证据），以及两处自我更正

运行标识：`run-2026-10-10_015215-578dbafb`（第 21 轮追加）
时间：2026-10-11 15:18–16:45，Asia/Hong_Kong（+08:00）
本轮性质：**产品目录零改动**（0 staged）。全部实验在 `%TEMP%` 隔离副本与夹具里完成。

---

## 1. 结论摘要

| 项 | 结果 |
| --- | --- |
| 补丁集扩到 **6 条修复 + 6 个回归用例**，8 个文件、+75/−22 | `proposed-fixes.patch`（18,314 字节，17 hunk，**零附带改动**） |
| 隔离副本全量测试 | **基线 941 通过 → 打补丁 947 通过 / 0 失败**；`dotnet build` **0 警告 0 错误** |
| **P-UA 的行为 A/B（直接证据）** | **未打补丁：`hasUserAgent=False`；打补丁：`hasUserAgent=True, userAgent='PineglowLauncher/1.0.0'`** |
| 端到端镜像安装（P-UA 的现场验证） | **未能在 40 分钟预算内跑完** —— 见第 3 节，**不作结论** |
| 缺陷 3 的描述 | **我原来的写法不准确，本轮更正**（见第 4 节） |

---

## 2. P-UA 的行为证据：直接对 `CreateApiHttp` 做 A/B

「编译通过 + 测试绿」只能证明补丁无害，**不能证明它真的修好了那个 403**。所以本轮做了一次直接的行为比对：

用一个小探针（`AssemblyLoadContext` + 反射）分别加载**未打补丁**的构建产物与**打过补丁**的构建产物，调用同一个私有方法
`LocalApiService.CreateApiHttp(...)`，然后读它产出的 `HttpClient` 的请求头：

```
=== 未打补丁的构建（baseline copy） ===
dll=Pineglow.Launcher.dll hasUserAgent=False userAgent=''
exit=1

=== 打过补丁的构建（P-UA applied） ===
dll=Pineglow.Launcher.dll hasUserAgent=True userAgent='PineglowLauncher/1.0.0'
exit=0
```

把它和第 12 轮的测量接起来，因果链就闭合了：

| 环节 | 证据 |
| --- | --- |
| 该目录下**不带 UA** 请求镜像 maven（302 后的学术镜像） | **403**（第 12 轮，同族 .NET 10 客户端，2/2） |
| 该目录下带 **`PineglowLauncher/1.0.0`** | **200，1,984,980 字节**（第 12 轮，2/2） |
| 未打补丁的产品客户端 | **不发 UA**（本轮，反射实测） |
| 打过补丁的产品客户端 | **发 `PineglowLauncher/1.0.0`**（本轮，反射实测） |

即：**补丁让产品客户端从"必然 403"变成"发一个已被实测接受的 UA"**。这比"跑一次完整安装"更直接地锁定了因果。

---

## 3. 端到端的镜像安装：**没跑完，因此不作结论**

我另外做了一次现场验证：用**打过补丁的 EXE**、把下载源切到 `bmclapi`，走整合包导入装 Fabric 0.19.5
（这正是第 9 轮报 `Fabric 库 2/8 下载失败：HTTP 403` 的那条路）。

结果：**2400 秒（40 分钟）探针预算耗尽，仍停在资源（assets）阶段**，没有走到加载器那一步。

```
importOk=false  importSeconds=2400  importResponse="请求被中止: 操作超时。"
磁盘终态：4,669 个文件 / 510.4 MB；versions 只有 1.21.11；libraries\net\fabricmc 不存在
```

**为什么不作结论**：这段时间里我**同时在跑一套完整测试套件**，两者抢网络与磁盘（那套测试平时 68 秒，当时跑了 34 分钟还没结束）。
所以这次超时**既不能证明补丁有效，也不能证明补丁无效**——它是**方法上的污染**，不是产品的反例。
我没有把它写成任何一侧的证据。

---

## 4. 一处自我更正：缺陷 3 的原文写错了

我在报告里把缺陷 3 写成「**安装器 stdout/stderr 被捕获后丢弃**」。本轮读代码后确认**这个说法不准确**：

`LoaderInstallHelper.RunInstallerAsync`（`:181-195`）**确实**把安装器的 stdout 与 stderr 逐行读出来并**转发给日志委托**：

```csharp
Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync();
Task<string> stderrTask = process.StandardError.ReadToEndAsync();
await process.WaitForExitAsync();
...
foreach (string obj in array) { _log(obj); }     // stdout 逐行
foreach (string obj2 in array) { _log(obj2); }   // stderr 逐行
```

**丢在最后一步——调用方给的是空委托**：

| 调用点 | 传进去的日志委托 | 结果 |
| --- | --- | --- |
| `LocalApiService.cs:1489`（`/api/instances/import`，用户上传整合包） | `_ => { }` | **全部丢弃** |
| `LocalApiService.cs:5529`（从 Modrinth 搜索安装整合包） | `msg => UpdateContentTask(entry, msg)` | 正常显示给用户 |

**正确的说法**：`/api/instances/import` 这条路径把进度与日志全部丢掉，而同一个文件里另一条整合包安装路径是正常接线的。
修复方向也因此不同——不是"补日志"，而是"**在 1489 那一行接上日志**"（本轮的 `P-IMPORTLOG` 就是把两处 `_ => { }` 换成写进 `LauncherLog`）。

### 4.1 现场佐证

第 9 节那次 40 分钟的导入期间，产品的日志文件 `data\logs\launcher-20261011.log` 是：

```
2026-10-11 15:19:01.029 [INFO] === Pineglow Launcher 启动 v1.0.0 ===
2026-10-11 15:19:01.094 [INFO] [LocalApi] 监听 127.0.0.1:50065, ui=...\data\UI\1.0.0-41346c7082cb
```

**只有两行**。一次 40 分钟的整合包导入，对一个打开日志的用户来说是**完全空白**的。

---

## 5. 一处探针失误（记录用，不影响结论方向）

我建"打过补丁的夹具"时，用 PowerShell `Add-Member` 重置了 `minecraftDir` / `instances` / `selectedInstanceId`
—— 但真实的配置字段是 **`GameDirectory`（大写 G）**，而 `Instances` 也没被覆盖。
结果：那个夹具的 `GameDirectory` 仍然指向**旧的 d6 夹具**，所以第 3 节那次导入把新实例建在了 d6 里。

副作用有两面：

- **坏的一面**：我的"隔离夹具"其实没有隔离，污染了 d6 夹具（多了一个未完成的实例目录）。
- **好的一面（意外）**：这反而把变量收得更干净——**同一个游戏目录、同一个镜像源，只有 EXE 不同**
  （第 9 轮是出厂 EXE → 403；本轮是打过补丁的 EXE → 未跑完）。

以后建夹具应当**逐字段核对关键路径**，而不是"重置我记得的那几个名字"。

---

## 6. 一处测试自纠

我新加的 `DualStackFakeIp_IsRejectedWhenCompatIsOff` 一开始断言 `dns_mixed`，跑出来**红**。
原因不是产品错，而是**我断言的错误码过时了**：补丁生效后两个地址**都**被认作基准段，
于是走"全部都是基准段"的分支，错误码变成 **`dns_private`**（未打补丁时才是 `dns_mixed`）。
两者**都是拒绝**，安全性质不变；改的是错误码。已更正断言并在注释里写清原因。
更正后：**947 通过 / 0 失败**。

（这条值得单独记：**修完安全策略后，错误码的语义也会变**；只断言"抛错"会漏掉这种变化，断言具体错误码才能钉住行为——而这次正是它先红了。）

---

## 7. 本轮补丁集最终形态

| 补丁 | 缺陷 | 文件 | 增/删 |
| --- | --- | --- | --- |
| **P-UA** | 缺陷 6（镜像库下载 403） | `LocalApiService.cs` | +27/−4（含 P-PROXYJVM 与 P-IMPORTLOG 的同文件改动） |
| **P-FABRICMETA** | 缺陷 5（Fabric 元数据前缀） | `MirrorHelper.cs` | +4/−1 |
| **P-PROXYJVM** | 缺陷 2（代理没给安装器 JVM） | `LoaderInstallHelper.cs` / `InstallerLoaderService.cs` / `ForgeService.cs` / `NeoForgeService.cs` / `InstancePackService.cs` | +12/−1, +2/−2, +3/−2, +3/−2, +7/−3 |
| **P-FAKEIP** | 缺陷 9（fake-IP 开关双栈失效） | `NewsImageProxyService.cs` | +17/−7 |
| **P-IMPORTLOG** | 缺陷 3（导入路径日志被丢） | `LocalApiService.cs` | 见上 |
| 回归用例 ×6 | — | `MirrorFabricMetaMappingTests.cs`（2）、`NewsFakeIpDualStackTests.cs`（4） | 新增 |

**仍然没有应用到产品目录。** 等你的决定。

状态串：`TechnicalAcceptance=NOT_PASSED`（发布整体）；`FormalPhaseApproval=NOT_GRANTED`；`ReleaseApproval=NOT_GRANTED`。
