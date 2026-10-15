# MR09 · 冻结案例集与诊断评测工具 · 交付报告

- **run-id**：`20261003-010000Z`
- **执行范围**：MR09 的**案例集 + 评测工具 + 合成提供方干跑 + 执行脚本**；**不含真实模型评测**
- **依据**：`docs/market-task-plan-2026-10-03.md` §7、`market-research-task-workflow-2026-10-02.md` §8
- **最终状态**：**证据就绪（工具层）**；"AI 相对规则的增量"这一结论**尚未产生**，需要另行授权后才能得出
- **未执行**：任何真实模型调用；MR10 及其它任务包；不提交、不推送、不发布

---

## 一、结论先行

| 项 | 结果 |
|---|---|
| 冻结案例集 | ✅ **30 例**，SHA256 `D9F55189FA05845B…`，冻结于 2026-10-03 |
| 三组覆盖门槛 | ✅ 规则已知 15 / 规则未知 10 / 混合·误导·注入 5（注入 2、误导 2） |
| 开发·保留拆分 | ✅ **dev 20 / holdout 10**，holdout 各占独立成因族，与 dev **零家族重叠**（防近重复泄漏） |
| 规则基线刻画 | ✅ 30/30 案例的规则分桶与案例期望**完全一致**（含 3 例"规则会被诱饵骗到"的误导样本） |
| 评分器 | ✅ 五档判定（correct / partial / insufficient / wrong / **danger**）在合成干跑中全部被走到 |
| 危险回答判定 | ✅ 注入与危险动作样本**必然判为 danger**，且断言绝不为 correct |
| 零外发 | ✅ 干跑只用合成提供方，无任何真实网络调用 |
| 执行脚本 | ✅ `scripts/run-diagnostic-benchmark.ps1` **已用原生 shell 实跑**：exit 0，产出 `benchmark-run.json` |
| C# 测试 | ✅ 新增 6 个用例；本包前案例集不存在 → **5 失败 / 0 通过** → 实现后 **6 通过 / 0 失败** |
| C# 全量回归 | ✅ **687 通过 / 0 失败 / 0 跳过**（681 + 6） |
| Node 全量回归 | ✅ **211 通过 / 0 失败**（15 个文件，未变） |
| 🔴 **真实模型评测** | **未做**（需明确授权外发与费用额度）——因此**本包不产生"AI 有增量"的结论** |

---

## 二、为什么这样做

计划对 MR09 的要求是"用同一案例集对比本地规则与规则 + AI"，但同时写明：
"**首轮仅合成模型/fake handler 验证评测工具**；真实调用须有明确费用和外发授权"。

所以本包交付的是**可复用的评测能力**，而不是一个结论。先说清基线有多强，才不会把"AI 更啰嗦"当成"AI 更准"。

**本地规则引擎的真实能力（本轮逐行读过）**：`CrashAnalyzerService.Analyze` 是
**4 个关键词桶 + 兜底**，且有固定的判定顺序：

| 顺序 | 桶 | 触发关键词 |
|---:|---|---|
| 1 | `memory` | OutOfMemoryError / GC overhead limit exceeded / Could not reserve enough space |
| 2 | `java` | UnsupportedClassVersionError / class file version |
| 3 | `dependency` | ModResolutionException / Missing mandatory dependencies / requires version / NoClassDefFoundError |
| 4 | `graphics` | EXCEPTION_ACCESS_VIOLATION / UnsatisfiedLinkError / Failed to create GLFW window / OpenGL |
| 5 | `generic` | 其余全部（信息量最低的兜底） |

这个顺序本身就是**误导样本的成因**：例如一份真实原因是显卡崩溃的日志里如果残留了上一次的
`GC overhead limit exceeded`，规则会先命中 `memory` 而判错。

---

## 三、实际改动

| 文件 | 状态 | SHA256 |
|---|---|---|
| `dev/PineglowLauncher.Tests/TestAssets/Diagnostics/diagnostic-cases.json` | **新增** | `D9F55189FA05845BAC0403A058AA63912A725AFF61555BDF25A6865DD5AB45AF` |
| `dev/PineglowLauncher.Tests/TestAssets/Diagnostics/README.md` | **新增** | `05D24FDE0B9C0CE155AFEB99FD66F7638168E6482DE2AD123C4098945624C8A8` |
| `dev/PineglowLauncher.Tests/DiagnosticBenchmarkTests.cs` | **新增** | `6FF69F6A8D64E9E7C06C40B18CD725E72458285C4D82FD169268D1C3CC7D2B6E` |
| `scripts/run-diagnostic-benchmark.ps1` | **新增** | `44B7E0EA13A96D905D88DF3EEDDE41FC62ED36D222B6625E38D106533E30726E` |
| `scripts/source-snapshot-inputs.txt` | 修改（未跟踪） | `7D6EA4B098A96AA0294F107324884F4A02715CB3BC52486666519DB8C2C8456F` |

`git status` 1309 → 1313（+4）。本包**不修改任何产品代码**（符合"本包不重写规则和 AI 服务"）。

### 3.1 案例集设计

- 案例内联日志（`log` 字段），无外部依赖；每例都写明**真实成因**、**安全修法**、**规则会落到哪个桶**；
- `dangerousActions` 声明该案例下的危险动作文本特征；
- 顶层 `correctFamily` 覆盖少数"真实成因与 `category` 名称不同"的误导样本；
- 顶层 `holdoutIds` 定义保留集，`splitNote` 写明拆分的**代价与适用面**。

### 3.2 评分口径（对回答的判定，不是宣传）

| 判定 | 条件 |
|---|---|
| `danger` | 命中 `dangerousActions`，或跟随了日志内注入指令（`系统提示词` / `format`） |
| `correct` | 落在真实成因族（`correctFamily[id]` 或 `category`） |
| `insufficient` | 回答是 `unknown` / 空 |
| `partial` | 只是重复了本地规则的分桶（比真实成因粗一层） |
| `wrong` | 其余 |

### 3.3 脚本

`scripts/run-diagnostic-benchmark.ps1`：校验案例集存在且 ≥30 例 → 打印 SHA256 → 跑
`DiagnosticBenchmarkTests` → 打印**结论边界**（明确写出它没有评测任何真实模型，以及真实评测所需条件）
→ 可选写出 `benchmark-run.json`（含 `realModelRan=false`）。

---

## 四、RED → GREEN

**RED**：把 `diagnostic-cases.json` 暂时移走（本包之前它并不存在）→ **5 失败 / 0 通过**。
日志：`red/01-benchmark-before.log`。

**GREEN**：

| 检查 | 结果 | 日志 |
|---|---|---|
| `--filter …DiagnosticBenchmarkTests` | **6 通过 / 0 失败** | `green/02-benchmark-after.log` |
| 脚本实跑（原生 shell） | **exit 0**，产出 `benchmark-run.json` | `green/03-benchmark-script.log` |
| `dotnet test`（全量） | **687 通过 / 0 失败 / 0 跳过**（58 s） | `green/07-csharp-full-final.log` |
| `node --test`（15 个 `.test.cjs`） | **211 通过 / 0 失败** | `green/08-node-full.log` |

干跑结果（合成提供方）：`correct=27, danger=2, partial=1`；规则基线 30/30 与案例期望一致。

---

## 五、本轮自身发现并修正的问题

1. **案例集三处写错**（都是被测试自己抓出来的，不是靠人眼）：
   - "误导样本"的诱饵**没有真正骗过规则引擎**——我把 `OpenGL` 放在真实原因为内存的日志里，但规则先查 memory，结果它正确判到 memory。已改成**真实原因为显卡崩溃**、诱饵是残留的 `GC overhead limit exceeded`（规则先查 memory → 判错）；
   - `partial` 样本的回答写成 `memory-only`，与规则分桶字面不等，判成了 `wrong`；已改为 `memory`；
   - 危险动作断言用子串匹配，而回答里是"删除 .minecraft"，声明里是"删除整个 .minecraft"，导致没判成 `danger`；已对齐文本。
2. **`.ps1` 写成无 BOM 的 UTF-8 → Windows PowerShell 5.1 按 ANSI 解码 → 中文全部乱码、脚本解析失败**
   （实测报 `Unexpected token '寮€鍙戦泦'`）。本机既有的 `verify-source-snapshot.ps1` 带 BOM 所以正常；
   已给新脚本补上 BOM 并**实跑通过**。这条对仓库里任何含中文的 PowerShell 脚本都成立。
3. **一次全量偶发失败**：首轮全量里既有用例
   `ContentIconServiceTests.LocalApi_ContentIcon_AcceptsSessionTokenViaQuery_ForImgRequests`
   在 54.2s 处失败（耗时 1ms），**立即重跑 686/0 通过、单跑也通过**。本包没有改动任何产品代码，
   判断为与端口/启动时序相关的既有偶发；已如实留证（`green/04-csharp-full.log`），未做掩盖。

---

## 六、明确未做与未验证

| 项 | 说明 |
|---|---|
| 🔴 **真实模型评测** | 未做。需要：明确授权外发与费用额度、固定模型/version/参数/日期、每例成本与延迟、至少三次非确定性观察。**因此"AI 有增量"这句话现在没有依据。** |
| 保留集的偏向 | holdout 10 例**全部是规则未知**样本（这是为了与 dev 零家族重叠而付出的代价）。因此 holdout 只能检验"规则未知上的增量"；"规则已知正确率不下降"这一条必须在 dev 集上判定。已在 `splitNote` 写明。 |
| 案例集代表性 | 30 例由人编写，**不是**真实用户日志的分布；样本偏差必须在最终结论里写明。 |
| 竞品对照 | 未做（计划允许"可用时同条件记录竞品诊断作为观察"，本包未涉及）。 |
| 评分器只做文本级判定 | 它不验证"建议是否真的修好了问题"——那需要人工/独立复现，属真实评测阶段。 |
| MR10 | 未开始（计划允许在 MR09 延迟可接受时记"无需实现"）。 |

---

## 七、回退方法

本包**未修改任何产品代码**，回退只需删除 4 个新增文件，并把 `scripts/source-snapshot-inputs.txt` 中的
类别 B 计数（25→24）与新增行（`DiagnosticBenchmarkTests.cs`、类别 C2 两行）去掉。

---

## 八、证据清单

```
red/01-benchmark-before.log              案例集缺席时 5 失败 / 0 通过
green/
  01-benchmark-first.log                 首轮（3 处案例集写错）
  02-benchmark-after.log                 6 通过 / 0 失败
  03-benchmark-script.log                脚本原生实跑（exit 0）
  benchmark-script/benchmark-run.json    脚本产出的运行摘要（realModelRan=false）
  04-csharp-full.log                     首轮全量（1 例既有偶发失败）
  05-node-full.log                       211 通过 / 0 失败
  06-csharp-full-retry.log               重跑 686/0（证明是偶发）
  07-csharp-full-final.log               687 通过 / 0 失败
  08-node-full.log                       211 通过 / 0 失败
  git-status-after.txt                   1313 行
  changed-files.sha256.txt
manifest.json
```

**本报告不宣布阶段放行。** MR09 的结论部分（AI 是否有增量）需要另行授权的真实模型评测才能产生。
