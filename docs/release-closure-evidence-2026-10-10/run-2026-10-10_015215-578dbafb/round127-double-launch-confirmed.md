# 记录 127：**缺陷 12 的"窗口部分"实测确认仍在** —— 而且这次有硬证据

运行标识：`run-2026-10-10_015215-578dbafb` ｜ 2026-10-12 15:00–15:22（+08:00）
性质：**只读 + 隔离夹具实测，产品目录零改动**（0 staged）。

---

## 为什么本轮会测这个

上一轮测 P-LOGSHARE 时，我**显式给两个进程设了不同的 API 端口** ——
那**绕过了缺陷 12 要测的那个场景**（真实双击启动时，两个进程会抢**同一个默认端口 48123**）。

**本轮把那个"绕过"补上**：**不设端口环境变量**，让两个进程都用默认端口。

## 结果：缺陷 12 的窗口部分**确认仍在**

```
A:  [INFO] [LocalApi] 监听 127.0.0.1:48123, ui=…            ← A 正常
B:  [INFO] === Pineglow Launcher 启动 v1.0.0 ===
    [ERROR] [LocalApi] 启动失败: Failed to listen on prefix 'http://127.0.0.1:48123/'
            because it conflicts with an existing registration
              at System.Net.HttpListener.AddPrefixCore(String registeredPrefix)
              at System.Net.HttpListener.AddAllPrefixes()
              at System.Net.HttpListener.Start()
              at Pineglow.Launcher.Services.LocalApiService.Start(Nullable`1 portOverride)
              at Pineglow.Launcher.App.OnFrameworkInitializationCompleted()

B alive = True                       ← B 没退出
杀掉 A 之后：still alive = 1 (pid 27452)
             默认端口不再服务 = False   ← 而 B 也不提供任何服务
```

| 观测 | 结果 |
| --- | --- |
| B 抢不到端口 | **✅ 而且日志里有完整调用栈** |
| **B 没有退出** | **❌ 仍然活着** |
| **杀掉 A 之后 B 还在** | **❌ 僵尸确认** |
| **此时端口没有任何服务** | **❌ 两个进程都没在服务** |

**这正是缺陷 12 描述的那个形态**：第二个窗口起不来服务、又赖着不走，
第一个退出后它变成一个**没有 API 的窗口**。

## 但有一条**比缺陷 12 当初描述的好**

**当初的描述是"静默"** —— 第二个窗口**什么日志都没有**。
**现在它会写下这一整段**（含方法名与调用栈）：

```
[ERROR] [LocalApi] 启动失败: Failed to listen on prefix … 
   at System.Net.HttpListener.AddPrefixCore(...)
   at Pineglow.Launcher.Services.LocalApiService.Start(...)
```

**"静默"这一半已经修掉了**（第 45 轮的日志补丁 + 第 113/114 轮验证过的 P-APILOG 家族）。
**剩下的是"僵尸"那一半。**

## ⚠️ 一处我要说清的边界

**这一行 `[LocalApi] 启动失败:` 不是我那几条日志补丁加的** ——
它来自 `App.cs` 里**早就存在**的 `try/catch`（第 20 轮读过：`:31-39 LocalApiService.Start()` 被 try/catch 包着）。
**所以本轮没有验证 P-STARTUPLOG**（那条仍然只有编译级证据，且本环境不可复现）。
**不要把"日志出现了"当成"我的补丁生效了"。**

## 因此缺陷 12 的状态应当更新

| 部分 | 原表述 | 现在 |
| --- | --- | --- |
| **日志** | "日志部分已修" | **✅ 实测确认已修**（B 的失败有完整栈进日志） |
| **窗口/僵尸** | "窗口部分待你取舍" | **❌ 实测确认仍在**（B 不退出；A 死后端口无人服务） |

**"待你取舍"这个说法现在有了依据**：要么加**单实例守卫**（第二个直接退出），
要么让**抢不到端口的进程自己退出**（而不是留着）。**两条路都不大**，但**属于产品行为取舍，等你定。**

## 当前状态

| 项 | 状态 |
| --- | --- |
| 已就绪的补丁 | 19 条（16 个脚本） |
| 证据等级 | A 15 / B 2 / C 1（P-STARTUPLOG，不可复现）/ D 1 |
| **缺陷 12** | **日志：已修（实测）**；**僵尸：仍在（实测）** —— 需要一个新补丁或你的取舍 |
| 唯一未做的功能 | 缺陷 13 ③（设置页 Java 安装入口） |
| 待你决定 | ① 提交（(a) ~2750 / (a′) 2182 / (b) **204**）；② C 的范围；③ **缺陷 12 的僵尸怎么处理**；④ 是否做 13 ③ |

状态串：`TechnicalAcceptance=NOT_PASSED`；`FormalPhaseApproval=NOT_GRANTED`；`ReleaseApproval=NOT_GRANTED`。
