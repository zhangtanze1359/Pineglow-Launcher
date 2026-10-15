# 记录 139：修法 (A) **也失败了** —— 冲突是**互相的**，两条补丁锚在同一段代码上

运行标识：`run-2026-10-10_015215-578dbafb` ｜ 2026-10-13 02:10–02:30（+08:00）
性质：**产品目录零改动**（0 staged）。

---

## 上一轮的修法 (A)：把 single-instance 挪到 startup-log **之前**

**结果：换了一条补丁失败。**

```
[P-SINGLEINSTANCE 3/3] expected exactly 1 match … but found 0     ← 上一轮（它在 startup-log 之后）
[P-STARTUPLOG global handler + protected shell construction] expected exactly 1 match … but found 0   ← 本轮（它在 startup-log 之前）
```

## 所以冲突是**互相的**

| 顺序 | 谁失败 |
| --- | --- |
| SINGLEINSTANCE **在** STARTUPLOG 之后 | **SINGLEINSTANCE 失败** |
| SINGLEINSTANCE **在** STARTUPLOG 之前 | **STARTUPLOG 失败** |

**两种顺序都试过了，两种都失败。** 原因很清楚：

**两条补丁锚在**同一段三行代码**上**：

```csharp
		if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime classicDesktopStyleApplicationLifetime)
		{
			classicDesktopStyleApplicationLifetime.MainWindow = new WebShellWindow();
```

- **STARTUPLOG** 要把 `new WebShellWindow()` 包进 try/catch；
- **SINGLEINSTANCE** 要在这段之前插守卫。

**谁先跑，谁就把对方那个"三行连续"的锚点拆掉。**

**所以 (A)「调顺序」是死路** —— 不是顺序问题，是**两条补丁在同一处落脚**。

## 正确的修法 (B)：**把锚点从"三行块"缩成"一行"**

`classicDesktopStyleApplicationLifetime.MainWindow = new WebShellWindow();` **这一行本身就是唯一的**
（全仓只有一处 `new WebShellWindow()`）。

**如果 STARTUPLOG 的锚点改成这一行**（而不是那三行），那么：
- SINGLEINSTANCE 在前面插多少行都**不影响**它；
- 顺序**不再重要**；
- 而且它**只依赖产品原有代码**，不依赖另一条补丁的形态 —— 比 (A) 更稳。

**这是我要采用的修法**（下一轮做）。

## 代价（必须说清）

**改 `apply-startup-log.ps1` 意味着 P-STARTUPLOG 需要重新验证** ——
它第 45 轮验证过（build 0/0、C# 941/0、冷启动 PASS），**改锚点后那些读数不能直接沿用**。

**但这是必要的**：现在这个补丁**根本进不了完整集合**，
"单独验证通过"没有意义 —— **第 124/128 轮那条教训又出现了一次**：
**"单补丁通过"不等于"能进集合"。**

## 这一轮把两种可能都试完了，所以下一轮不用再猜

| 尝试 | 结果 |
| --- | --- |
| 原顺序（startup-log → single-instance） | ❌ SINGLEINSTANCE 失败 |
| 调换顺序（single-instance → startup-log） | ❌ STARTUPLOG 失败 |
| **改锚点（STARTUPLOG 锚一行）** | **下一轮做** |

**"两种顺序都失败"这个结果本身是有价值的** —— 它**排除了一整类修法**，
把问题定性为"**锚点粒度**"而不是"**排列**"。

## 当前状态

| 项 | 状态 |
| --- | --- |
| 已就绪的补丁 | 20 条（17 个脚本） |
| **二十条合并验证** | **❌ 仍失败** —— 两条补丁锚在同一段代码；(A) 已排除 |
| 下一步 | **改 `apply-startup-log.ps1` 的锚点为单行**，然后重跑 |
| 唯一未做的功能 | 缺陷 13 ③ |
| 待你决定 | ① 提交；② C 的范围；③ 缺陷 12 是否采纳单实例守卫；④ 是否做 13 ③ |

状态串：`TechnicalAcceptance=NOT_PASSED`；`FormalPhaseApproval=NOT_GRANTED`；`ReleaseApproval=NOT_GRANTED`。
