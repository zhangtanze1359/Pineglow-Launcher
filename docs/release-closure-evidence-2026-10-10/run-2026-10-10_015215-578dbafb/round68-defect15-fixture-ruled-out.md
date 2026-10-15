# 记录 68：把缺陷 15 ② 的**夹具**排除了 —— 观察成立，成因仍未定

运行标识：`run-2026-10-10_015215-578dbafb` ｜ 2026-10-11 23:22–23:32（+08:00）
性质：**只读审计，产品目录零改动**（0 staged）。

---

## 上一轮留下的问题

记录 67 里，我给 `prelaunch-check` 加的"实例自己的目录是否存在"检查**没生效**（仍报 `ok=True`）。
当时的可能原因我列了两条：**属性名不对**，或者**夹具没写进去**。本轮逐条排除。

## 排除结果

| 待排除项 | 检查 | 结论 |
| --- | --- | --- |
| 属性名是不是 `GameDirectory`？ | `GameInstance.cs` 里有 `public string GameDirectory { get; set; } = string.Empty;` | **是，名字对** |
| `ResolveGameDirectory` 用的是它吗？ | `LocalApiService.cs:1108`：`string dir = instance?.GameDirectory;`（为空才退回 `config.GameDirectory`） | **是** |
| 夹具真的写进去了吗？ | 用同样方式写一份、**再读回来**：`read back GameDirectory = 'C:\does-not-exist\ghost-instance'`，**`survived = True`** | **写进去了** |
| 我选中的实例是不是唯一的？ | 配置里 `d7-fabric*` **只有 1 个**（id `6478bce1e50048ab85339cb800fbe80a`），且**与记录 67 里 launch 报错的那个 id 完全一致** | **选择无歧义** |

**四条全部排除。** 也就是说：`prelaunch-check` 确实是在**那个目录不存在的实例**上被调用的。

## 因此观察成立，而且比我原先以为的更严重

`HandlePrelaunchCheckAsync` 里**原本就有**这一项：

```csharp
string gameDir = ResolveGameDirectory(config, instance);
if (!Directory.Exists(gameDir))
{
    warnings.Add("游戏目录不存在（首次启动将自动创建）：" + gameDir);
}
```

**这一项也没生效**（`warnings=0`）。所以不是"我加的检查写错了"，而是
**两条独立的检查都没有触发** —— 这指向一个更结构性的原因（例如该处理器实际用的实例/目录与我所想不同）。

## 我没有解决的（明确标注）

**成因仍未确定。** 我排除了夹具、属性名、选择歧义这三类解释，但**没有查出真正的机制**。

**下一步很具体**：在 `prelaunch-check` 的响应里临时加一个字段，把它**实际看到的 `gameDir` 与 `instance.GameDirectory`** 原样回显出来——
一次运行就能看出它到底在看哪个路径。**本轮上下文预算用尽，未做。**

**不写成"已修"，也不写成"确认是产品缺陷"** —— 现有证据只支持"**观察成立、夹具已排除**"。

状态串：`TechnicalAcceptance=NOT_PASSED`；`FormalPhaseApproval=NOT_GRANTED`；`ReleaseApproval=NOT_GRANTED`。
