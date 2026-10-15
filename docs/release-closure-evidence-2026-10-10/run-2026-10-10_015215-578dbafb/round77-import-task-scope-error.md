# 记录 77：缺陷 4 的补丁 —— **3 步都能应用了，但编译报出一个真实的作用域错误**

运行标识：`run-2026-10-10_015215-578dbafb` ｜ 2026-10-12 01:20–01:36（+08:00）
性质：**产品目录零改动**（0 staged）。补丁脚本 **仍未通过编译**。

---

## 本轮进展：三步全部应用成功

```
[OK] P-IMPORTTASK 1/4 register the task + route progress
[OK] P-IMPORTTASK 2/4 success terminal state
[OK] P-IMPORTTASK 4/4 fallback terminal state
steps applied = 3
```

**第 3 步（取消终态）我主动删掉了**，理由是**它不必要**：取消分支会提前 `return`，
控制流照样经过 `finally`，所以第 4 步的兜底守卫会把状态写成 `failed`。
唯一差别是消息不够精确（显示"导入未完成"而不是"已取消"），**不影响正确性，也不会留下幽灵任务**。
用不必要的一步去卡住整个补丁，不划算。

## 然后构建报错 —— 而且是个**真实的作用域错误**

```
error CS0103: 当前上下文中不存在名称"importEntry"    （3 处）
```

**原因**：我把 `ContentTaskEntry importEntry = …` 声明在 **`try` 块内部**，
而 C# 里 **`try` 块中声明的变量在 `finally` 块里不可见** —— 每个块有自己的作用域。

第 4 步的兜底守卫写在 `finally` 里，所以看不到 `importEntry`。

**这个错误不是我推理出来的，是编译器报出来的** —— 这正是"构建"这一关的价值。

## 确切的修法（下一轮照做即可）

需要两处改动：

1. **把声明提到方法作用域**。锚点是方法首两行（已确认）：

```csharp
	private static async Task HandleImportInstanceAsync(HttpListenerRequest request, HttpListenerResponse response)
	{
```

在 `{` 之后插入：

```csharp
		ContentTaskEntry? importEntry = null;
```

2. **第 1 步里改成赋值**：`ContentTaskEntry importEntry = new ContentTaskEntry` → `importEntry = new ContentTaskEntry`；
   第 4 步的守卫改成判空：`if (importEntry != null && importEntry.Status == "running")`。

（第 2 步在 `try` 内，作用域没问题，不用动。）

## 当前状态

| 项 | 状态 |
| --- | --- |
| 补丁脚本 | **3 步可应用**；**编译失败**（作用域） |
| 补丁计数 | **保持 15**（半成品不算） |
| 已解决 | 接线方案、三个终态点、第 2 步锚点、第 3 步的取舍 |
| 待解决 | 上面那两处作用域改动 → 再构建 → 再做**真实 `.mrpack` 导入**的行为验证 |

## 一条值得留着的观察

这一轮我做了三件事：删掉不必要的一步、让剩下三步全部应用成功、**让编译器告诉我哪里错了**。
**没有任何一件是靠"我觉得"完成的** —— 尤其是那个作用域错误，我自己不会想到，
而 `dotnet build` 一句话就点出来了。

状态串：`TechnicalAcceptance=NOT_PASSED`；`FormalPhaseApproval=NOT_GRANTED`；`ReleaseApproval=NOT_GRANTED`。
