# 记录 65：缺陷 14 的补丁（**第 14 条补丁**）+ 我自己补丁里的一个毛病

运行标识：`run-2026-10-10_015215-578dbafb` ｜ 2026-10-11 22:52–23:02（+08:00）
性质：**产品目录零改动**（0 staged）。补丁在隔离副本验证。
补丁：`apply-config-log.ps1`（1 步）——**未应用**。

---

## 改动（`ConfigStore.cs` 的裸 `catch`）

回退到默认值**保留不动**（那是对的），补上两件此前没有的事：

1. `LauncherLog.Error("[Config] 读取配置失败，已回退到默认配置: " + configPath, ex)` —— **带路径与完整异常**；
2. 把损坏的文件**改名保留**为 `config.json.corrupt-<时间戳>`，便于查看与恢复。

## 验证：**同一场景的前后对照**

| | 修复前（记录 63） | 修复后（本轮） |
| --- | --- | --- |
| 产品日志 | **2 行**，与配置损坏无关 | **`[ERROR] [Config] 读取配置失败…`** + `System.Text.Json.JsonException: '2' is an invalid end of a number. Expected a delimiter. Path: $.Instances[5].WindowWidth \| LineNumber: 132 \| BytePositionInLine: 23.` |
| 损坏文件 | 原样留在盘上，**无副本** | **`config.json.corrupt-20261011-202530`** 已保留 |
| 构建 / 测试 / 发布 | — | **build 0 警告 0 错误 / C# 941 通过 0 失败 / publish exit 0** |

**同一个夹具、同一份截断配置（5,032 / 8,387 字节）**，唯一变量是补丁。

## 我自己补丁里的毛病（如实记录）

产物里出现了**两个**副本：

```
config.json.corrupt-20261011-202530   5032 bytes
config.json.corrupt-20261011-202533   5032 bytes
```

**因为 `LoadAsync` 在一次运行里被调用了不止一次**，而我写的是"每次失败都保留一份"。
后果：配置长期损坏时，这个副本会**随调用次数累积**。

**正确做法**：每个进程只保留一次（或当天已有 `.corrupt-` 就跳过）。
这个毛病**不是这次验证发现的**——是我看到两个文件才意识到的；**验证跑通不等于实现没问题**。

## 意义

| 项 | 变化 |
| --- | --- |
| 已就绪的补丁 | **14 条**（13 + P-CONFIGLOG） |
| 「失败了但不说」一族 | **10 已修**（P-STARTUPLOG）、**14 已修**（本轮）、**15 未修**（自检假阴性 + launch 500 无日志） |

状态串：`TechnicalAcceptance=NOT_PASSED`；`FormalPhaseApproval=NOT_GRANTED`；`ReleaseApproval=NOT_GRANTED`。
