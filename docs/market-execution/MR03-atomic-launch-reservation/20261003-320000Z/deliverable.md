# MR03 补测之三 · 启动守卫的**严格原子性**（A7 剩余项收尾）

- **run-id**：`20261003-320000Z`
- **性质**：把"检查是否在运行"与"登记启动中占位"合并为**一次原子预约**，消除上一轮记录的窄窗口
- **依据**：`MR03-launch-concurrency-guard/20261003-310000Z/deliverable.md` §八：
  "并发守卫的**原子性** | 本守卫是「检查后再派发」，两次请求之间仍有极窄窗口（同进程内 HTTP 处理是并发的）。
  **未做**更严格的原子预约（那需要把「检查+登记」合并为一次原子操作）"
- **最终状态**：**证据就绪**（本类 7 个用例；全量 811/0；**RED 未复现，如实记录**）

---

## 一、实现

### 1. 注册表：原子预约

```csharp
public static bool TryBeginLaunch(string instanceId, out int existingPid)
{
    existingPid = 0;
    if (IsRunning(instanceId, out existingPid)) return false;   // 先自愈（陈旧条目会被清掉）

    // TryAdd 是原子的：只有「当前没有条目」才写入成功
    if (!Running.TryAdd(instanceId, new Entry(0, DateTimeOffset.Now, null, Launching: true)))
    {
        IsRunning(instanceId, out existingPid);
        return false;
    }
    return true;
}

public static void AbortLaunch(string instanceId)   // 只删「预约中」的条目
```

`Entry` 增加 `Launching` 标志；`IsRunning` **把"预约中"也视为占用**（否则第二次启动会挤进来）。

### 2. 处理器：预约壳 + 核心（`finally` 无条件释放）

```csharp
if (!RunningInstanceRegistry.TryBeginLaunch(instanceId, out int alreadyRunningPid))
{
    throw new InvalidOperationException(alreadyRunningPid > 0
        ? $"该实例已在运行（进程 {alreadyRunningPid}），请勿重复启动。"
        : "该实例正在启动中，请勿重复启动。");
}

try { await LaunchBodyAsync(response, instanceId); }
finally { RunningInstanceRegistry.AbortLaunch(instanceId); }
```

**关键设计**：`finally` 里**无条件**调用 `AbortLaunch` 就够 —— 因为启动成功后 `MarkRunning` 已把条目
**升级**为 `Launching=false`，此时 `AbortLaunch` 是 **no-op**；而**任何失败路径**（抛异常/提前返回）都会释放预约。
这样就不需要额外的 `upgraded` 标志，减少了出错面（用例 ⑥ 专门验证这个性质）。

---

## 二、7 个用例（本类累计）

| # | 用例 | 断言 |
|---|---|---|
| ① | 已在运行 → 拒绝 | 400 + "已在运行（进程 N）" |
| ② | 陈旧条目（死 PID）→ 不拦 | 守卫自愈，不误挡 |
| ③ | 从未运行 → 不拦 | 防过度拦截 |
| ④ | **预约互斥** | 首次成功；预约中 `IsRunning` 为真；第二次 `TryBeginLaunch` 被拒（`existingPid=0`）；释放后可重新预约 |
| ⑤ | **32 路并发** | 恰好 **1** 个赢家（实测 `赢家数 = 1`） |
| ⑥ | **升级后 `AbortLaunch` 是 no-op** | `MarkRunningPid` 后调 `AbortLaunch`，条目仍在、PID 不变 → 这就是外壳 `finally` 可无条件调用的依据 |
| ⑦ | **启动失败必须释放预约** | 启动在预约之后失败 → `IsRunning` 为 false → 第二次启动**不被**当成"已在运行/正在启动中" |

⑦ 是最重要的一条：**预约泄漏会让该实例被永久挡住，那比"漏挡"更糟**。

---

## 三、RED：**没复现，如实记录**

| RED | 改什么 | 结果 |
|---|---|---|
| **1** | 把 `TryAdd` 换成"`ContainsKey` 检查后再赋值"（**非原子**） | **0 失败** ✗ —— 32 并发下**仍然只有 1 个赢家** |

**为什么没变红**：`ConcurrentDictionary` 的"检查→赋值"竞争窗口只有纳秒级，而 32 个 `Task.Run` 的启动本身是错开的，
几乎不可能挤进那个窗口。**因此：用例 ⑤ 尚未被证明能抓住非原子实现** —— 它证明了"当前实现恰好一个赢家"，
但**不能**证明"换成非原子实现一定会失败"。

> 要让这条 RED 真正成立，需要一个可控的竞争点（例如在检查与赋值之间插入同步屏障的测试缝），
> 或把并发数/重复次数提到能稳定复现的量级。**本包没做**，如实记为未达成。

---

## 四、本轮自找并修掉的三个问题（都是**流程**问题）

### 4.1 两次补丁尝试失败 —— **不该用 PowerShell 做字符串手术**

| 尝试 | 失败原因 |
|---|---|
| v1（索引运算） | 删多了：把 `AppConfig config = …` / `GameInstance instance = …` 两行**一起吃掉了** → 35 个编译错误 |
| v2 / v3（逐字匹配多行中文） | 多行含中文/反引号的字面量**匹配不上**，两次都在"守卫块未找到"处中止 |

**改用 `edit` 工具（先 `read` 拿到真实文本，再做字面替换）→ 一次成功。**
**规则**：改代码用 `edit`；PowerShell 只用来**跑**命令，不要用来做多行源码手术。

### 4.2 **同一个错误在两轮内犯了两次**：RED 的还原源用了"修复前"的备份

- 第 42 轮：还原后 `含守卫 = False`（修复被撤掉）；
- 本轮：还原后 `含 TryAdd = False`（`TryBeginLaunch` 被撤掉），而 `LocalApiService` **仍在调用它** → 构建必坏。

**规则（写死）**：对**修复**做 RED 时，必须在**修复之后**另存一份参照（本包已存
`green/RunningInstanceRegistry.cs.fixed`、`green/LocalApiService.cs.fixed`），RED 之后**还原到修复后状态**，
而不是还原到本轮开始前。

### 4.3 一个"看起来通过"的陷阱：构建失败时的旧二进制

v1 补丁删多了之后，我**先检查了构建退出码**（35 个错误）才没有继续跑测试 —— 否则会拿旧二进制得出"一切正常"。

---

## 五、验证

| 检查 | 结果 |
|---|---|
| 本类用例 | **7 通过 / 0 失败** |
| C# 全量 | **811 通过 / 0 失败 / 0 跳过**（807 + 4） |
| 并发用例实测输出 | `[A7c] 32 个并发 TryBeginLaunch，赢家数 = 1` |
| 生产文件 | 含 `TryBeginLaunch` / `AbortLaunch` / `LaunchBodyAsync`；已存修复后参照 |

---

## 六、实际改动

| 文件 | 改动 |
|---|---|
| `RunningInstanceRegistry.cs` | `Entry` 加 `Launching`；新增 `TryBeginLaunch` / `AbortLaunch`；`IsRunning` 把预约视为占用 |
| `LocalApiService.cs` | `HandleLaunchAsync` 改为**预约壳**（`try/finally` 无条件释放）；原方法体改名 `LaunchBodyAsync` |
| `dev/PineglowLauncher.Tests/InstanceLaunchGuardTests.cs` | **新增 4 个用例**（累计 7 个） |
| `scripts/tmp-patch-a7c*.ps1` | 三次补丁尝试的脚本（**失败过程留档**，见 §四.1） |

---

## 七、仍未覆盖

| 项 | 说明 |
|---|---|
| **RED 未复现** | 见 §三：非原子实现也能通过并发用例 → 该用例的"牙齿"未被证明 |
| **跨进程**的并发启动 | 只在本进程内验证；两个启动器实例同时启动同一实例仍无协调（进程内注册表的固有限制） |
| 运行态持久化 | 仍是进程内内存态（MR03b 已列） |
| 真实启动 Minecraft | 未验证（需真实游戏/Java） |
| 删除守卫与预约的交互 | 预约中删除会被 `IsRunning` 挡住（行为正确），但**未单独写用例** |

---

## 八、证据清单

```
green/RunningInstanceRegistry.cs.fixed   修复后参照（RED 用；本轮教训）
green/LocalApiService.cs.fixed           修复后参照
green/01-after-atomic-wiring.log         补丁尝试中的中间状态（3 通过）
green/02-atomic-wired.log                处理器接好后 3 通过
green/03-atomic-tests.log                加 3 条原子性用例后 6 通过
green/04-atomic-with-race.log            加并发用例后 7 通过
green/05-atomic-restored.log             注册表重应用后 7 通过
green/06-csharp-full.log                 811 通过 / 0 失败
red/RunningInstanceRegistry.cs.before        （**修复前**状态，只用于构造 RED —— 见 §4.2）
red/01-red-nonatomic.log                 RED：非原子实现 → **0 失败**（未复现，见 §三）
manifest.json
```

**本报告不宣布阶段放行、不提交、不发布。**
