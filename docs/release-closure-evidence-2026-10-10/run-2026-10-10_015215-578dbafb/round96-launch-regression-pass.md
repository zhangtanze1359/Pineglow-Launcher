# 记录 96：启动路径的回归**验成了** —— 而且顺带证实了自动安装确实能装出 JRE

运行标识：`run-2026-10-10_015215-578dbafb` ｜ 2026-10-12 06:18–06:34（+08:00）
性质：**只读 + 隔离夹具实测，产品目录零改动**（0 staged）。

---

## 上一轮为什么没验成，这一轮怎么修的

记录 94 的夹具里 `JavaPath` 指向一个**已被清掉的** JRE，所以那次跑的是"配置路径失效"那条分支。
本轮**先在 `%TEMP%` 里找出一个真实存在的 JRE**，再把它写进夹具 —— 于是这次跑的是"**Java 找得到**"那条分支。

## 结果：PASS

```
fixture JavaPath = ...\data\runtimes\java-21-windows-x64\jdk-21.0.12.1+1-jre\bin\javaw.exe  exists=True
apiUp = True
prelaunch ok=True errors=0
launch ok=True pid=1672
reachedTitle=True  marker='Sound engine detected' → 'Sound engine started'
leftover = 0
```

**用十八条补丁的构建（`674CC84D…`）、Java 存在的夹具，实例启动到了标题界面。**

**这关掉了 P-JAVAAUTO 的回归风险**：它改的 `ResolveJavaPath` 在"Java 找得到"这条最常用的分支上**没有被改坏**。

## 顺带一个意外的好证据

我找 JRE 时，`%TEMP%` 下有**五个**同路径的 JRE：

```
pineglow-importlog-*/data/runtimes/java-21-windows-x64/jdk-21.0.12.1+1-jre/bin/javaw.exe
pineglow-importlog2-*/data/runtimes/java-21-windows-x64/...
pineglow-itb2-*/data/runtimes/java-21-windows-x64/...
pineglow-itb3-*/data/runtimes/java-21-windows-x64/...
pineglow-patched-fixture-*/data/runtimes/java-21-windows-x64/...
```

注意路径形状：**`<配置目录>/runtimes/java-21-windows-x64/...`** ——
这正是 `JavaLocator` 会去找的"内置 JRE"位置（`JavaLocator.cs:210`），
也正是 `JavaRuntimeService.InstallAsync` 的安装目标。

**这些 JRE 是早前轮次里、由整合包导入路径真的下载安装出来的**（不是手工放的）。
所以：**`JavaRuntimeService.InstallAsync` 在这个环境里确实能装出一个可用的 JRE** ——
这为 P-JAVAAUTO 的"启动路径自动安装"提供了**间接但实在**的支持：
它调用的**是同一个服务、同一个目标目录**。

**仍然是推理，不是观测**（启动路径上的自动安装本身还没跑过），但比"机制相同"那句强了一档 ——
现在有"**同一服务在同一环境下确实产出过可用的 JRE**"这个物证。

## 当前验证版图（补丁版）

| 门槛 | 状态 |
| --- | --- |
| 十八条合并验证（build / C# 949/0 / Node 305/0 / 锁定发布 / 冷启动） | **PASS** |
| **启动到标题界面**（Fabric，Java 存在的夹具，18 条补丁） | **✅ 本轮补上** |
| 四种加载器的启动（13 条补丁时） | PASS |
| 断点续传 / 界面级 / 更新链门槛 | PASS |
| **启动路径的 Java 自动安装** | **未观测**（但同一服务产出过可用 JRE） |

状态串：`TechnicalAcceptance=NOT_PASSED`；`FormalPhaseApproval=NOT_GRANTED`；`ReleaseApproval=NOT_GRANTED`。
