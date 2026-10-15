# 记录 66：修掉我自己补丁里的那个毛病（副本累积）—— P-CONFIGLOG v2

运行标识：`run-2026-10-10_015215-578dbafb` ｜ 2026-10-11 23:02–23:10（+08:00）
性质：**产品目录零改动**（0 staged）。补丁在隔离副本验证。

---

## 上一轮留下的毛病

第 53 轮我给缺陷 14 写的补丁里，保留损坏配置的条件是"**每次加载失败都保留一份**"，
而 `LoadAsync` 一次运行会被调用多次 → **一次运行产生了两个副本**。我在记录 65 里把它如实记了下来。

**本轮把它修掉，而不是留着。**

## 改法

不再用进程内标志，改成**看盘上有没有**：

```csharp
string cfgDir = Path.GetDirectoryName(configPath) ?? ".";
bool alreadyKept = Directory.EnumerateFiles(cfgDir, Path.GetFileName(configPath) + ".corrupt-*").Any();
if (File.Exists(configPath) && !alreadyKept)
```

理由：**"已经保留过"这件事，盘上就能看出来**，不需要额外状态。
而且它对**跨运行**也成立——第二次启动时若已有副本，不会再堆一份。

## 复验（同一场景、同一份截断配置）

| 项 | v1（第 53 轮） | **v2（本轮）** |
| --- | --- | --- |
| `.corrupt-` 副本数 | **2** | **1** ✓ |
| 日志里的错误 | 有（含 `LineNumber: 132`） | **仍有** ✓ |
| `dotnet build` | 0/0 | **0 警告 0 错误** |
| 发布 | exit 0 | **exit 0** |

日志原文（v2）：

```
[ERROR] [Config] 读取配置失败，已回退到默认配置: ...\data\config.json
        | System.Text.Json.JsonException: '2' is an invalid end of a number. Expected a delimiter.
          Path: $.Instances[5].WindowWidth | LineNumber: 132 | BytePositionInLine: 23.
```

## 一次自查失败（如实记）

第一版修法我本想加一个 `private static bool _corruptConfigPreserved;` 字段，锚点写成
`private static readonly object ConfigGate = new object();` —— **那个声明在 `ConfigStore.cs` 里，不在补丁脚本里**，
所以"1 of 2"的编辑只成功了一半。我**打印了每处编辑的结果**才发现（`edits applied = 1 of 2`），
随后换成上面这个不需要类级字段的做法。

**这与第 49 轮那个教训是同一件事**：改东西要**看结果**，不能假设替换成功。

状态串：`TechnicalAcceptance=NOT_PASSED`；`FormalPhaseApproval=NOT_GRANTED`；`ReleaseApproval=NOT_GRANTED`。
