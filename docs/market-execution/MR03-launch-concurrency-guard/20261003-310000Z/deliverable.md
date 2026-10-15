# MR03 补测之二 · 同一实例的并发启动守卫（缺口登记表 A7 的剩余项）

- **run-id**：`20261003-310000Z`
- **性质**：补上 MR03b 自报缺失的守卫；**并修掉一个既有测试基础设施的端口竞争**
- **依据**：`MR03-instance-running-guard/20261003-060000Z/manifest.json` 的 `knownGaps`：
  "同一实例重复启动**没有并发守卫**（注册表以最后一次为准）"
- **最终状态**：**证据就绪**（3 个用例；1 个 RED 成立；**产品代码 +1 处守卫**；**测试基础设施修 1 处 flake**）

---

## 一、缺口是真实的：启动路由**从不检查运行态**

| 事实 | 位置 |
|---|---|
| `HandleLaunchAsync` 只 `MarkRunning(instanceId, process)` | `LocalApiService.cs:1677` |
| 全文件唯一的 `IsRunning` 检查在**删除**守卫里 | `LocalApiService.cs:1243` |
| 于是同一实例可以**起两个游戏进程**同时写同一份存档 | 真实的损坏风险 |
| 且 `MarkRunning` "以最后一次为准" → 删除守卫此后只跟踪**最新**的 PID | 数据安全 |

---

## 二、守卫（放在最前：任何网络调用/进程派发之前）

```csharp
AppConfig config = LoadConfig();
GameInstance instance = RequireInstance(config, instanceId);

// MR03 收尾之二（A7）：同一实例的**并发启动守卫**。
if (RunningInstanceRegistry.IsRunning(instanceId, out int alreadyRunningPid))
{
    throw new InvalidOperationException($"该实例已在运行（进程 {alreadyRunningPid}），请勿重复启动。");
}
```

**位置刻意靠前**：在 `MicrosoftAuthService` 刷新凭据、解析版本 JSON、派发进程**之前**就拒绝 ——
既省掉昂贵的网络往返，也避免"拒绝时已经改动了什么"。

---

## 三、3 个用例（只验守卫，不依赖真实游戏）

| # | 用例 | 断言 |
|---|---|---|
| ① | `Launch_WhenInstanceAlreadyRunning_IsRejectedWithReadableError` | 用**存活 PID** 标记运行中 → **400** + 含"已在运行" + 含该 PID |
| ② | `Launch_AfterTheProcessExited_IsNotBlockedByTheGuard` | 用"真实但已死"的 PID（`cmd /c exit`）→ 守卫**不得**再拦（注册表自愈） |
| ③ | `Launch_WhenNeverStarted_IsNotBlockedByTheGuard` | 从未运行 → 守卫**不得**拦截（防过度拦截） |

实测输出：

```
[A7b] 已在运行时 launch -> 400 error=该实例已在运行（进程 45068），请勿重复启动。
[A7b] 陈旧条目下 launch -> 500 error=未找到版本 JSON：…\versions\…      ← 守卫没拦，走到下一阶段才失败
[A7b] 未运行时 launch   -> 500 error=未找到版本 JSON：…\versions\…      ← 同上
```

②③ 的失败原因**不是**"已在运行"，正说明守卫**没有过度拦截** —— 这正是这两条用例的价值。

---

## 四、RED

| RED | 改什么 | 结果 |
|---|---|---|
| **1** | 移除刚加的守卫 | **1 失败**：用例 ① 拿到 `500 error=未找到版本 JSON`（守卫不在时请求会一路走到版本解析）而不是 `400 已在运行` ✓ |

---

## 五、本轮自找并修掉的两个问题

### 5.1 我的 RED 备份取错了点 → "还原"把**修复本身**撤掉了

我用了**加守卫之前**的备份做 RED 的还原源，于是还原后 `含守卫 = False`（我自己检查了这一点才发现）。
**修法**：重新应用守卫，并另存一份**修复后**的备份（`green/LocalApiService.cs.fixed`）作为 RED 的正确参照。

> **规则**：对"修复"做 RED 时，备份必须是**修复后**的状态；否则 RED 之后的"还原"会把修复抹掉。
> 这条与"改完必须显式 build 并检查退出码"同级 —— 都是**证据流程自身**的坑。

### 5.2 一个**既有的测试基础设施 flake**被放大后暴露（并已修掉）

加完新用例后全量第一次报 **6 个失败**，全是 bisect 用例，错误是：

```
System.Net.HttpListenerException : 另一个程序正在使用此文件，进程无法访问。
  at System.Net.HttpListener.AddPrefixCore(...)   ← LocalApiService.Start 第 178 行
  at LocalApiTestHarness.StartAsync(...)
```

**定性（不是猜）**：
- 单独跑 bisect 两类：**17/17 通过**；
- 全量重跑：**2 个**（从 6 降到 2）→ **间歇性**；
- 根因在 `LocalApiTestHarness.AcquirePortAsync`：它**先绑定再释放** `TcpListener`，随后 `HttpListener` 才绑定 ——
  **典型的 TOCTOU**；再加上端口是**确定性种子序列**（`52000 + (seed + i*41) % 9000`），
  用例变多后碰撞概率上升。

**修法**（只改测试夹具）：启动失败时**换端口重试**（最多 6 次；`AcquirePortAsync` 每次调用都会推进种子）。

**验证**：修完后**连续 3 次全量全部 807 通过 / 0 失败**（修复前为 6 → 2 → …）。

> 注意：**这不是我的守卫引起的**（bisect 路径不启动游戏），但**我的新用例增加了服务启动次数**、
> 把它从"偶发"放大到"每次都能看到"。把它修掉，后续所有验证都会更可信。

---

## 六、验证

| 检查 | 结果 |
|---|---|
| 本组用例 | **3 通过 / 0 失败** |
| C# 全量（**连跑 3 次**） | **807 通过 / 0 失败 / 0 跳过** ×3 |
| Node 全量 | **230 通过 / 0 失败**（18 个文件） |
| 生产文件 | 含守卫；与 `green/LocalApiService.cs.fixed` 一致 |

---

## 七、实际改动

| 文件 | 改动 |
|---|---|
| `dev/PineglowLauncher/Pineglow.Launcher.Services/LocalApiService.cs` | **新增并发启动守卫**（+10 行） |
| `dev/PineglowLauncher.Tests/LocalApiTestHarness.cs` | **修复端口 TOCTOU**：启动失败换端口重试 6 次 |
| `dev/PineglowLauncher.Tests/InstanceLaunchGuardTests.cs` | **新增**（3 个用例） |
| `scripts/source-snapshot-inputs.txt` | 登记新测试（类别 B 42→43） |
| `docs/market-execution/gap-register.md` | A7 标记为**已关闭** |
| `docs/market-task-plan-2026-10-03.md` | MR03 行补记 |

---

## 八、仍未覆盖

| 项 | 说明 |
|---|---|
| **真实启动 Minecraft** | 未验证（需真实游戏与 Java）。本类只验**守卫**：已在运行→拒绝、已退出→不拦、未运行→不拦 |
| 运行态持久化 | 仍是**进程内内存态**（MR03b 已列）：启动器重启后不知道游戏还在跑 |
| 并发守卫的**原子性** | 本守卫是"检查后再派发"，理论上两次请求之间仍有极窄窗口（同进程内 HTTP 处理是并发的）。**未做**更严格的原子预约（那需要把"检查+登记"合并为一次原子操作） |
| 非 Windows | 自愈依赖 `Process.GetProcessById`，仅在 Windows 验证（MR03b 已列） |

> 关于原子性：当前实现已经把窗口从"整个启动过程"缩小到"检查→派发之间"，
> 是**明显的改进**但**不是**严格互斥。若要严格，需要引入 per-instance 的启动预约（`TryBeginLaunch`）。
> 本包**没有**做这一步 —— 如实记录为剩余项。

---

## 九、证据清单

```
green/LocalApiService.cs.fixed            修复后的正确参照（RED 用）
green/LocalApiTestHarness.cs.before       夹具修复前备份
green/01-launch-guard-first.log           3 通过
green/02-launch-guard-final.log           RED 后（此时守卫被误撤 → 2 通过 / 1 失败）
green/03-launch-guard-restored.log        重新应用守卫后 3 通过
green/04-csharp-full.log                  第一次全量（6 个 bisect flake 失败）
green/06-bisect-alone.log                 bisect 单独跑 17/17（证明是间歇性）
green/07-csharp-full-retry.log            全量重跑（2 个失败）
green/08-csharp-full-run{1,2,3}.log       夹具修复后连跑 3 次：807/0 ×3
green/09-node-full.log                    230 通过 / 0 失败
red/LocalApiService.cs.before             （注意：这是**修复前**状态，只用于构造 RED）
red/01-red-no-launch-guard.log            RED：移除守卫 → 1 失败
manifest.json
```

**本报告不宣布阶段放行、不提交、不发布。**
