# 第 30 轮：给死锁缺陷补一个**真正会红的**回归测试（两次自我纠正）

运行标识：`run-2026-10-10_015215-578dbafb`（第 30 轮追加）
时间：2026-10-11 18:08–18:18，Asia/Hong_Kong（+08:00）
本轮性质：**产品目录零改动**（0 staged）。全部在 `%TEMP%` 隔离副本里完成。
产物：`apply-deadlock-test.ps1`（新增 `SyncOverAsyncDeadlockTests.cs`）——**未应用**。

---

## 1. 为什么第 29 轮的验证还不够

第 29 轮我是**在真实运行中**验证死锁修复的（让队列恢复在 UI 线程上跑，应用能起来了）。
那种验证很有说服力，但它**不会留在仓库里**：以后有人再写出「在 UI 线程同步等 async」的代码，
没有任何东西会拦他。所以本轮要把它变成**测试**。

判据只有一条：**这个测试在未修复的树上必须是红的。** 不是红就没有意义。

---

## 2. 第一次尝试：测试"泵送"了上下文，把死锁解开了 → RED 跑成 PASS

第一版写了个 `PumpContext`：一个拥有线程的 `SynchronizationContext`，worker 阻塞等待的同时由主线程**泵送**队列。
想法是"复刻 UI 线程"，实际上**正好相反**：

> 真实 UI 线程在 `GetAwaiter().GetResult()` 里阻塞时**不会**处理自己的消息队列——
> 我那个泵送循环**恰好把 continuation 执行了**，于是死锁被解开。

RED 跑成 PASS（93 ms），说明这个测试**测不出任何东西**。删掉泵送，改成只记账、不执行。

---

## 3. 第二次尝试：`LoadAsync` 走了同步早返回，压根没到那个 await → RED 又跑成 PASS

第二版：`Post` 只 `Interlocked.Increment` 计数，永不执行；断言 worker 在 30 秒内结束、且 `Post` 次数为 0。

RED **又**跑成 PASS（135 ms）。去读 `ConfigStore.LoadAsync()` 才明白：

```csharp
string configPath = ResolveConfigPath();
lock (ConfigGate)
{
    if (!File.Exists(configPath))
    {
        ... WriteConfigFile(configPath, obj);
        return obj;              // <-- 文件不存在时：纯同步早返回
    }
}
try
{
    string json = await File.ReadAllTextAsync(configPath);   // <-- 会捕获上下文的那一行
```

我的测试把 `ConfigPathOverrideForTest` 指向一个**还不存在**的文件 → 走早返回 → **根本没碰到那个 await**。

（顺带解释了为什么真实应用会死锁：真实配置**是存在的**，必然走 await 那条路。）

修法一行：测试构造函数里**先把配置文件建出来**（写入 `{}`）。

---

## 4. 第三次：RED 真的红了

| 运行 | 结果 |
| --- | --- |
| **RED**（未打补丁的树） | **失败，30 秒**：`async 链把 continuation 投回了调用者所在的 SynchronizationContext（Post 次数 = 1）——这正是 UI 线程死锁的成因；每个 await 都必须 ConfigureAwait(false)。` |
| **GREEN**（加上 3 行修复） | **通过，106 ms** |
| GREEN 全量 | **942 通过 / 0 失败**（941 + 新测试） |

测试断言了三件事，任何一条都能独立拦住回归：

1. `context.PostedCount == 0` —— async 链**不得**把 continuation 投回调用者上下文；
2. worker 线程在 30 秒内结束 —— 没有死锁；
3. 加载结果非 null。

**未修复时 Post 次数 = 1、worker 永不结束**；修复后 Post 次数 = 0、106 ms 返回。

---

## 5. 这一轮真正值得记的东西

**"回归测试必须先看到它红"** —— 本轮我连写两版**都是绿的**，而且两版看起来都很合理：

- 第一版"泵送上下文"看起来更像在模拟 UI 线程，实际把 bug 解掉了；
- 第二版逻辑正确，但被被测代码的一条**同步早返回分支**绕开了。

如果我没跑 RED，就会把一个**永远通过**的测试当成"回归保护"提交上去——比没有测试更糟，
因为它会给人"这类问题已经被守住了"的错觉。

（本轮没有新增产品缺陷；新增的是一条**能拦住缺陷 11 回归的测试**。）

---

## 6. 累计状态

| 项 | 状态 |
| --- | --- |
| 已就绪的补丁 | **7 条**：P-UA、P-FABRICMETA、P-PROXYJVM、P-FAKEIP、P-IMPORTLOG、P-DURABLEQUEUE、P-DEADLOCK（+ 本轮这条回归测试） |
| C（发布依赖锁定） | 补丁已就绪，待你决定范围 |
| 仍未修 | 缺陷 1、4、8、10；以及第 29 轮列出的 5 处同类 sync-over-async 写法 |

状态串：`TechnicalAcceptance=NOT_PASSED`（发布整体）；`FormalPhaseApproval=NOT_GRANTED`；`ReleaseApproval=NOT_GRANTED`。
