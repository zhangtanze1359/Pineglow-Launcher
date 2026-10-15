# 记录 140：修法 (B) 的**真实形状** —— 不是"缩小锚点"，而是**把这条补丁拆成两步**

运行标识：`run-2026-10-10_015215-578dbafb` ｜ 2026-10-13 02:30–02:50（+08:00）
性质：**只读审计，产品目录零改动**（0 staged）。

---

## 我读了 `apply-startup-log.ps1` 全文（82 行），看清了它的结构

**一个 `$old` / `$new`，锚点正是那三行**：

```
44: $old = @'
45: 		if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime classicDesktopStyleApplicationLifetime)
46: 		{
47: 			classicDesktopStyleApplicationLifetime.MainWindow = new WebShellWindow();
48: '@
```

**而 `$new`（49–78 行）里其实做了两件不同位置的事**：

| 位置 | 做什么 |
| --- | --- |
| **在 `if` 之前** | 插入 `AppDomain.UnhandledException` 与 `TaskScheduler.UnobservedTaskException` 两个全局兜底 |
| **在 `if` 之内** | 把 `MainWindow = new WebShellWindow();` 包进 try/catch |

## 所以上一轮我写的 (B)「把锚点缩成一行」**是不够的**

**如果锚点只剩 `MainWindow = …` 那一行**，那么替换也只能替换那一行 ——
**而两个全局兜底是要插到 `if` 之前去的，那个位置就没了。**

**也就是说：(B) 不是"改锚点"，而是"把这条补丁拆成两步"** —— 每步各锚一行：

| 步 | 锚点（单行，全仓唯一） | 插入什么 |
| --- | --- | --- |
| **1/2** | `if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime classicDesktopStyleApplicationLifetime)` | **在它之前**插入两个全局兜底 |
| **2/2** | `classicDesktopStyleApplicationLifetime.MainWindow = new WebShellWindow();` | **把它包进** try/catch |

**两步的锚点都是单行、都是唯一的**，而且：
- SINGLEINSTANCE 在 `if` 之前插的那几行**不影响**第 1 步（它锚的是 `if` 那一行本身）；
- 第 2 步锚的是 `MainWindow = …` 那一行，**前面插多少行都不影响**。

**于是两条补丁互不干扰，顺序也不再重要。**

## 代价（必须说清）

| 项 | 影响 |
| --- | --- |
| `apply-startup-log.ps1` 要重写（一个 `$old/$new` → 两个） | 中等 |
| **P-STARTUPLOG 需重新验证** | 第 45 轮那次（build 0/0、C# 941/0、冷启动 PASS）**不能直接沿用** |
| 它仍然**没有运行时证据** | 而且**本机装了 WebView2，那个场景复现不了** —— 这一点不因本轮而改变 |

**但这是必须做的**：现在这条补丁**根本进不了完整集合**，
"单独验证通过"没有意义 —— **第 124/128/129 轮那条教训已经出现三次了**：
**"单补丁通过"不等于"能进集合"。**

## 这一轮的形态

**我上一轮给了一个"看起来很简单"的修法 (B)，本轮读代码发现它不成立** ——
因为 `$new` 里那两件事**插入位置不同**，而"缩小锚点"这个动作**只能保住其中一个位置**。

**这是"先读代码再改"的价值**：如果直接按上一轮的说法去改，
我会写出一个**丢了全局兜底**的补丁 —— 而且**编译能过、测试能过、冷启动能过**，
因为丢掉的正是"只在崩溃时才生效"的那部分（P-STARTUPLOG 本来就只有编译级证据，
**没有任何读数会告诉我它丢了**）。

**所以这条尤其危险**：**它的缺失是静默的。**

## 下一轮要做的

1. 把 `apply-startup-log.ps1` 重写成**两个单行锚点的步骤**；
2. **重跑二十条合并验证**（这一次两条补丁应当互不干扰）；
3. 重跑后，P-STARTUPLOG 的证据等级**仍是 C**（本机不可复现），但**"能进集合"这一条会从"未知"变成"已验证"**。

## 当前状态

| 项 | 状态 |
| --- | --- |
| 已就绪的补丁 | 20 条（17 个脚本） |
| **二十条合并验证** | **❌ 仍失败** —— 根因已定性为**锚点粒度**；(A) 已排除；**(B) 的真实形状本轮查明** |
| 下一步 | 重写 `apply-startup-log.ps1` 为两步单行锚点，再重跑 |
| 唯一未做的功能 | 缺陷 13 ③ |
| 待你决定 | ① 提交；② C 的范围；③ 缺陷 12 是否采纳单实例守卫；④ 是否做 13 ③ |

状态串：`TechnicalAcceptance=NOT_PASSED`；`FormalPhaseApproval=NOT_GRANTED`；`ReleaseApproval=NOT_GRANTED`。
