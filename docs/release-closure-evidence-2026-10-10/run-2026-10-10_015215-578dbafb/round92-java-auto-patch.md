# 记录 92：缺陷 13 ② 的补丁（**第 18 条**）—— 把「Java 自动安装」接到启动路径

运行标识：`run-2026-10-10_015215-578dbafb` ｜ 2026-10-12 05:02–05:24（+08:00）
性质：**产品目录零改动**（0 staged）。补丁在隔离副本验证。
补丁：`apply-java-auto.ps1`（3 步）——**未应用**。

---

## 补的是什么

README 把「**Java：自动检测与自动安装**」列为一级功能。
但记录 63/70 实测：**自动安装只挂在整合包导入上**（`JavaRuntimeService.InstallAsync` 全仓只有一个调用点，
`InstancePackService.cs:757`）。于是在"装个原版 → 点开始游戏"这条**最常见的路径**上，
没 Java 的用户只会拿到：

> 未找到可用的 Java 运行时（该版本需要 Java 21），**请先在设置中指定 Java 路径**

而设置页**没有安装入口**（缺陷 13 ③ 仍未做）。

## 三步改动

| 步 | 内容 |
| --- | --- |
| 1/3 | 新增 `InstallRequiredJavaOrThrow(requiredMajor, fallbackMessage)`：调用既有的 `JavaRuntimeService.InstallAsync`（**复用导入路径用的同一个服务**），成功返回 `JavaPath` |
| 2/3 | "找到了 Java 但版本不对" → **改为自动安装**（与导入路径的行为一致） |
| 3/3 | "一个 Java 都没有" → **改为自动安装** |

**关键设计：装不上时仍抛出原来那句可操作的提示**（并把失败原因附上）：
`"…请先在设置中指定 Java 路径（自动下载 Java 也失败了：<原因>）"`。
这样**没网的机器不会只看到一条下载错误**，而是仍然拿到"去设置里指定路径"这个可执行的动作。

## 验证

```
[OK] P-JAVAAUTO 1/3 the helper
[OK] P-JAVAAUTO 2/3 wrong version -> auto-install
[OK] P-JAVAAUTO 3/3 no Java -> auto-install
steps applied = 3

buildExit=0   0 警告 0 错误
testExit=0    失败: 0，通过: 942
```

（942 = 941 原有 + `apply-runsync.ps1` 带进来的 1 个 RunSync 回归用例。）

**顺序依赖**：这个补丁用了 `RunSync`，所以**必须在 `apply-runsync.ps1` 之后应用** ——
已写进脚本头部注释，并已记入 `APPLY-ORDER.md`。

## ⚠️ 验证边界

| 断言 | 状态 |
| --- | --- |
| 补丁能应用、能编译、全量测试通过 | **✅ 已验证** |
| **无 Java 的机器上点开始游戏会自动装 Java 并启动成功** | **⏳ 未验证** |

后者需要**一次真实的 JRE 下载**（几十 MB + 网络）。**在验之前它只能算编译级验证通过。**

## 补丁计数

| | |
| --- | --- |
| 之前 | 17 条 |
| **现在** | **18 条** |

**尚未并入合并验证**（第 80 轮那次是 17 条），下一轮应重跑。

状态串：`TechnicalAcceptance=NOT_PASSED`；`FormalPhaseApproval=NOT_GRANTED`；`ReleaseApproval=NOT_GRANTED`。
