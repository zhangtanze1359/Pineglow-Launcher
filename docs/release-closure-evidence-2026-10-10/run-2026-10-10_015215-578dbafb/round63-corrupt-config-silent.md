# 记录 63：新缺陷 **#14 —— 配置读取失败被静默吞掉**

运行标识：`run-2026-10-10_015215-578dbafb` ｜ 2026-10-11 22:36–22:44（+08:00）
性质：**只读审计 + 隔离夹具实测，产品目录零改动**（0 staged）。

---

## 怎么发现的

本轮问了一个此前从未测过、且与发布稳健性直接相关的问题：
**如果 `config.json` 损坏了（比如保存时断电），启动器会怎样？**

做法：把一份真实配置（8,387 字节）**截断到 5,032 字节**（非法 JSON），放进夹具启动。

## 实测结果

| 观测 | 读数 |
| --- | --- |
| API 起来了吗 | **apiUp = True**（**没有崩** ✓） |
| `/api/instances` | **1 个实例** → 说明它**回退到了默认配置** |
| `config.json` 之后的大小 | **仍是 5,032 字节**（**损坏的文件没被改写、也没被改名保存**） |
| 产品日志 | **只有 2 行**（启动 + 监听）——**关于配置损坏一个字都没有** |

## 代码路径（已读，确认）

`ConfigStore.cs:214`：

```csharp
catch
{
    AppConfig obj6 = new AppConfig { GameDirectory = ..., UseAutoMemory = true, UpdateUrl = ... };
    InstanceService.EnsureDefaultInstance(obj6);
    AccountService.EnsureAccounts(obj6);
    return obj6;          // ← 静默返回默认值
}
```

**一个裸 `catch`，没有任何日志**（该文件里搜 `LauncherLog` 在加载路径上零命中）。

## 定性：缺陷 **#14**

| 维度 | 判断 |
| --- | --- |
| 是不是缺陷 | **是**——回退到默认值**是对的**（不崩），但**沉默是错的** |
| 严重度 | **中**：用户的账号 / 实例列表 / 设置**在内存里被悄悄换成默认值**，而日志与界面**都没有任何提示**；损坏的文件原样留在盘上，既没保留也没改名 |
| 触发条件 | `config.json` 非法/截断、不可读、权限错误——任何读配置时的异常 |
| 与已有的关系 | **不影响**已有的 `RotateBackupBeforeWrite`（那只在**保存**时轮转备份）；本次夹具里没有旧备份，所以无处可恢复 |
| 修复方向 | 在 `catch` 里补一条 `LauncherLog.Error`（写明文件路径与异常），并考虑把损坏文件**改名保留**（如 `config.json.corrupt-<时间戳>`）再回退——**让"配置坏了"这件事至少可查、可恢复** |

## 一点说明

这是本会话**第 14 条**已复现缺陷。它不是我"去找"出来的，而是**问了一个此前没问过的问题**
（"最重要的那个文件坏了会怎样"）之后**测出来的**。

状态串：`TechnicalAcceptance=NOT_PASSED`；`FormalPhaseApproval=NOT_GRANTED`；`ReleaseApproval=NOT_GRANTED`。
