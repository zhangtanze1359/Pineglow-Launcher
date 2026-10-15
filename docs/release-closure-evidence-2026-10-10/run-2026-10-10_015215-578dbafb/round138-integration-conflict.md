# 记录 138：**二十条合并验证抓到一处集成冲突** —— P-SINGLEINSTANCE 与 P-STARTUPLOG 抢同一段代码

运行标识：`run-2026-10-10_015215-578dbafb` ｜ 2026-10-13 01:52–02:10（+08:00）
性质：**产品目录零改动**（0 staged）。全部在 `%TEMP%` 隔离副本里完成。

---

## 结果：**合并验证失败**

```
[P-SINGLEINSTANCE 3/3 the guard] expected exactly 1 match in …\App.cs but found 0
```

**第 16 个脚本（P-SINGLEINSTANCE）的 3/3 步匹配不到锚点，脚本按设计抛错退出。**

## 原因：**两条补丁改同一段代码，而顺序让后者失效**

P-SINGLEINSTANCE 的 3/3 锚点是这三行**连续**代码：

```csharp
		if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime classicDesktopStyleApplicationLifetime)
		{
			classicDesktopStyleApplicationLifetime.MainWindow = new WebShellWindow();
```

**但 P-STARTUPLOG 在本顺序里排第 10 位（早于它）**，而它**恰好把 `new WebShellWindow()` 包进了 try/catch**
（第 45 轮：那条补丁就是"给窗口壳构造包 try/catch"）。

**于是那三行不再连续 → 锚点 0 处匹配。**

## 为什么单补丁验证没发现它

| 验证方式 | 当时打了什么 | 锚点 |
| --- | --- | --- |
| 第 135 / 137 轮 | **只打 P-LOGSHARE + P-SINGLEINSTANCE** | **匹配** ✓ |
| **本轮（全 20 条）** | 还打了 P-STARTUPLOG（更早） | **0 处匹配** ✗ |

**这正是合并验证存在的意义** —— 单补丁测试里那个锚点是好的，
**只有在"前序补丁已经改过那段代码"的完整集合里才暴露出来。**

**而且这与我此前的一条判断有关**：第 124 轮我写「**"逐个补丁单独验证"对有前置依赖的补丁不成立**」——
当时说的是**运行时前置**（日志层）；**本轮发现还有"文本前置"**：**锚点所在的代码可能被前序补丁改掉。**

## 修法（很明确，两种）

| 方案 | 做法 | 评价 |
| --- | --- | --- |
| **(A) 调顺序** | 把 `apply-single-instance.ps1` 挪到 `apply-startup-log.ps1` **之前** | **最小** —— 但要确认 P-STARTUPLOG 的锚点在 SINGLEINSTANCE 改过之后仍匹配 |
| (B) 改锚点 | 让 SINGLEINSTANCE 的锚点适配"已被 try/catch 包过"的形态 | 更稳，但锚点会依赖另一条补丁的**具体形态** |

**我倾向 (A)**：顺序依赖本来就是这个补丁集的一部分（已经有两处：java-auto 依赖 runsync、loader-ui 依赖 java-label），
再加一处是**同一种处理方式**，而且**不需要让一条补丁去适配另一条的产物**。

**但 (A) 必须验证**：P-STARTUPLOG 的锚点是否会被 SINGLEINSTANCE 的守卫改动破坏。
**这是下一轮第一件要试的事** —— 而且**只需要跑一次合并验证就能同时回答**。

## 本轮没做什么（如实）

**没有改顺序、没有重跑。** 失败被脚本自己的断言挡住了（这正是"每步断言恰好 1 处匹配"的价值 ——
**它没有静默地少打一步**）。**下一轮试 (A)**。

## 当前状态

| 项 | 状态 |
| --- | --- |
| 已就绪的补丁 | 20 条（17 个脚本） |
| 证据等级 | A 17 / B 2 / C 1 / D 1（单补丁口径） |
| **二十条合并验证** | **❌ 失败** —— **P-SINGLEINSTANCE 与 P-STARTUPLOG 的顺序冲突** |
| 已知的顺序依赖 | java-auto→runsync、loader-ui→java-label、**single-instance↔startup-log（本轮新增，待修）** |
| 唯一未做的功能 | 缺陷 13 ③ |
| 待你决定 | ① 提交；② C 的范围；③ 缺陷 12 是否采纳单实例守卫；④ 是否做 13 ③ |

状态串：`TechnicalAcceptance=NOT_PASSED`；`FormalPhaseApproval=NOT_GRANTED`；`ReleaseApproval=NOT_GRANTED`。
