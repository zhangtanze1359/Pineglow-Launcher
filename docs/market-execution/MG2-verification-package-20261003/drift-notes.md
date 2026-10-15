# MG2 证据漂移说明

复验脚本的漂移检查在**首次运行**时报告 `无法解释的漂移 = 0`；在**生成本复验包之后**再跑，
报告出 **2 条漂移**。两条都是真实的，也必须解释清楚，否则证据不可用于复验。

> **本包没有通过"改脚本让它不报"来消除漂移** —— 那会让检查失去意义。

---

## 漂移 1：`scripts/source-snapshot-inputs.txt`

| 项 | 值 |
|---|---|
| 记录来源 | `MR12-curseforge-rate-limit/20261003-043000Z/green/changed-files.sha256.txt` |
| 记录值 | `75018421D77A0D2869F7924C103C3BD1C342E7C2593C0687E83899D9163A5087` |
| 当前值 | `DED291F3198F9DDE0981A47836E9235D71A76DC58F7A2879935A5BDC34E58FD6` |

**原因（不是产品变化，是这份"清单文件"被反复编辑）**：生成 MG2 复验包时，我把两个复现性脚本补登记进白名单"类别 G"：

```
# ---- 类别 G：本轮新增的可复现性工具（2）----   →  （4）   →  （5）
scripts/run-diagnostic-benchmark.ps1           ← 新增（MR09 交付的评测脚本，此前漏登记）
scripts/verify-mg2.ps1                         ← 新增（MG2 复验脚本）
scripts/verify-mg1.ps1                         ← 再次新增（MG1 复验脚本）
```

**这条漂移是"共享索引被多个包反复编辑"的固有现象**，不是实现缺陷：
`source-snapshot-inputs.txt` 是快照构建的**输入清单**，每新增一个可复现性工具都要登记一次，
因此它永远会与"上一个记录它的包"不一致。**产品源码（类别 A/B/F）没有任何变化。**

**顺带发现的流程问题**：`run-diagnostic-benchmark.ps1`（MR09 交付）当时**漏登记**进白名单，
说明"新增可复现性工具要同步登记"这一步在 MR09 那轮被漏掉了。本包已补。

---

## 漂移 2：`dev/PineglowLauncher.Tests/CurseForgeRateLimitTests.cs`

| 项 | 值 |
|---|---|
| 记录来源 | `MR12-curseforge-rate-limit/20261003-043000Z/green/changed-files.sha256.txt` |
| 记录值 | `30E8D12725523381A129A4BAC461333E098AD2C1CBDD7A4619A9D043C0ECD9F7` |
| 当前值 | 见本包 `manifest.json` 的 `driftExplained[1].current` |

**原因（复验过程中发现的真实缺陷，已修）**：MR12 的并发用例里，假 handler 用
`Calls++` 统计调用次数 —— **非线程安全**。单跑该用例时看不出来，**全量跑时实测丢过一次计数**
（`Expected: 6, Actual: 5`）。已改为 `Interlocked.Increment` + `Volatile.Read`，
峰值并发也改成 CAS 循环。

**这属于"证据生成后又被修正"**：MR12 的记录哈希对应的是有缺陷的版本。处置方式：

1. **没有改写 MR12 的封存证据**（`changed-files.sha256.txt` 事后修改会让整条证据链失效）；
2. 缺陷、修法与影响登记在本文件；
3. 修的是**测试代码**，不是产品代码 —— MR12 的限流实现本身没有因此改变。

---

## 一并记录：复验过程中修掉的第三个缺陷（不在漂移清单里）

`dev/PineglowLauncher.Tests/ContentIconServiceTests.cs` 里有一处端口派生：

```csharp
int port = 49777 + (Environment.CurrentManagedThreadId % 100);
```

**线程池里 id 相差 100 的两个线程会派生出同一个端口**，并行测试时必然相撞。
实测在全量跑时复现：`HttpListenerException: 另一个程序正在使用此文件`（`LocalApiService.Start` 抛）。

- 这个文件**不在**任何包的 `changed-files.sha256.txt` 里，所以它不产生漂移条目；
  它在本仓库里是 **`??` 未跟踪**状态（与本工作区大量测试文件一样），
  因此修改它**不会**让 `git status` 的行数变化 —— 这也解释了为什么改完它计数仍是 1320。
- 已改为让操作系统分配空闲端口（`TcpListener(Loopback, 0)`），与
  `AiCancelAndRedirectTests` / `NewsLinkHandlerTests` / `NewsFakeIpConfigTests` 的做法一致；
- 该偶发在 MR09 那轮也出现过一次（当时判为"端口/启动时序相关"，本次拿到了确定根因并修掉）。

---

## 处置汇总

| 漂移/缺陷 | 属于 | 是否改了产品代码 | 处置 |
|---|---|---|---|
| `source-snapshot-inputs.txt` | 本复验包 | 否 | 补登记脚本；登记哈希 |
| `CurseForgeRateLimitTests.cs` | 复验时发现 | 否（测试代码） | 修 Interlocked 竞争；登记哈希 |
| `ContentIconServiceTests.cs` | 复验时发现 | 否（测试代码） | 修端口派生碰撞；**不在漂移清单**（未被任何包记录，且为 `??` 未跟踪） |

**复验方复跑时应看到 `无法解释的漂移 = 2`，并在这里读到原因 —— 这是预期行为，不是失败。**
