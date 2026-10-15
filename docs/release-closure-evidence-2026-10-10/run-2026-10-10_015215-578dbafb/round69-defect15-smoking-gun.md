# 记录 69：缺陷 15 ② 定案 —— **它确实看到了那个不存在的路径，检查却没触发**

运行标识：`run-2026-10-10_015215-578dbafb` ｜ 2026-10-11 23:32–23:42（+08:00）
性质：**只读审计 + 隔离夹具实测，产品目录零改动**（0 staged）。

---

## 做法：用它自己的回显来定案

`HandlePrelaunchCheckAsync` 的响应里**本来就带 `gameDir`**（`LocalApiService.cs:1636-1643`）：

```csharp
await WriteJsonAsync(response, 200, new
{
    ok = errors.Count == 0,
    errors,
    warnings,
    maxMemoryMb,
    gameDir
});
```

所以只要跑一次、把原始 JSON 打出来，就能知道**它到底在看哪个路径** —— 不需要猜。

## 原始响应（逐字）

```json
{"ok":true,"errors":[],"warnings":[],"maxMemoryMb":4608,"gameDir":"C:\\does-not-exist\\ghost-instance"}
```

配套读数：

| 检查 | 读数 |
| --- | --- |
| 启动前，盘上的配置里那个实例的目录 | **`C:\does-not-exist\ghost-instance`**（`ghost = True`，id `6478bce1…`） |
| 该路径存在吗 | **否** |
| `/api/instances` 里该实例的 `gameDirectory` | **就是那个幽灵路径** |
| **`prelaunch-check` 回显的 `gameDir`** | **就是那个幽灵路径** |
| **`warnings`** | **`[]`（空）** |

## 因此：**它看到了，但没报警**

`HandlePrelaunchCheckAsync` 里**原本就有**这一段（第 1596–1600 行）：

```csharp
// 游戏目录存在性
string gameDir = ResolveGameDirectory(config, instance);
if (!Directory.Exists(gameDir))
{
    warnings.Add("游戏目录不存在（首次启动将自动创建）：" + gameDir);
}
```

**`gameDir` 是不存在的路径（由响应回显证实），`Directory.Exists` 必然为 false，
可 `warnings` 是空的。**

这不是夹具问题（第 56 轮四项已排除），也不是"看错了实例"（回显就是那个路径）——
**是这一段检查没有按它字面写的那样生效。**

## 定案与仍未定的部分

| 项 | 状态 |
| --- | --- |
| 现象 | **确认**：`prelaunch-check` 对"实例目录不存在"报 `ok=true`、`warnings=[]` |
| 它看到的是哪个路径 | **确认**：就是那个不存在的路径（响应回显） |
| 夹具是否可疑 | **已排除**（第 56 轮四项） |
| **为什么这段检查没触发** | **仍未确定** —— 需要看编译后的实际代码路径（例如是否有更早的分支/异常吞掉、或这段在某条件下被跳过） |

**仍然不写成"已修"**。但现在的表述比前两轮强得多：**不是"我没看见它报警"，而是"它自己回显了那个坏路径，同时说没问题"** ——
这已经足够作为一条**可复现、有自证据**的缺陷记录下来。

## 一句话

**缺陷 15 的自检假阴性，现在有它自己的输出作为证据。**

状态串：`TechnicalAcceptance=NOT_PASSED`；`FormalPhaseApproval=NOT_GRANTED`；`ReleaseApproval=NOT_GRANTED`。
