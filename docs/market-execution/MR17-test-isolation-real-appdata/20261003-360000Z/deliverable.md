# 测试隔离缺陷修复 · 有测试把诊断报告写进真实 `%APPDATA%`（缺口登记表 A20）

- **run-id**：`20261003-360000Z`
- **性质**：定位并修掉"测试污染用户真实数据目录"的缺陷
- **依据**：`gap-register.md` A20（第 46 轮排查 A15 时发现）：
  "`%APPDATA%\MCMiniLauncher\diagnostics\inst-A\step3-op-*.json` 等 8+ 个文件 —— 至少一个诊断存储用例未重定向存储根"
- **最终状态**：**证据就绪**（根因定位、已修复、全量验证）

---

## 一、根因：**同一个文件**里两个测试类共用了一个"半重定向"的夹具

`dev/PineglowLauncher.Tests/AiCancelAndRedirectTests.cs` 内含**两个**测试类
（`AiCancelAndRedirectTests` + `AiCancelAndRedirectStep3Tests`），它们共用自己的 `TestHarness`。

该夹具在 `StartAsync` 里：

| 项 | 修复前 | 后果 |
|---|---|---|
| 配置存储 | ✅ `OverrideConfigStoreForTest` | 正常 |
| 真实 config 快照守卫 | ✅ `RealConfigSnapshot` | 正常 |
| **诊断报告存储根** | ❌ **没有重定向** | **所有诊断报告写进真实 `%APPDATA%\MCMiniLauncher\diagnostics`** |

而仓库里**本来就有**这条缝：`LocalApiService.OverrideDiagnosticStoreRootForTest`
（`LocalApiTestHarness` 一直在用）—— 这个自建夹具漏了它。

**证据**（文件名与时间戳互相印证）：

- `locale-test-*.json` —— 由 `AiCancelAndRedirectTests.cs:1138` 的 `"locale-test-" + Guid` 产生；
- `step3-op-*.json` / `phase3c2-op-*.json` —— 由该文件的 step3 用例产生；
- 时间戳覆盖 **04:45 → 10:13**，即**今天每一轮全量测试都在写**。

---

## 二、修复（只改测试）

1. `StartAsync` 里新增：临时目录 + `LocalApiService.OverrideDiagnosticStoreRootForTest(...)`
   （启动失败路径也一并释放）；
2. `DisposeAsync` 里释放重定向并删除临时目录；
3. **新增真实目录守卫**（与该夹具已有的 config 守卫同款）：

```csharp
int realDiagnosticsAfter = CountRealDiagnosticFiles();
if (realDiagnosticsAfter != _realDiagnosticsBefore)
{
    firstError ??= new InvalidOperationException(
        $"真实 %APPDATA% 诊断目录被改动：{_realDiagnosticsBefore} → {realDiagnosticsAfter} 个文件。…");
}
```

**这条守卫的意义**：该类以后**再也不会**悄悄污染用户目录 —— 一旦发生，用例直接失败。

---

## 三、验证（决定性）

| 检查 | 结果 |
|---|---|
| 修复后该类 | **31 通过 / 0 失败** |
| 真实诊断目录文件数（该类跑完） | **259 → 259** ✅ 未新增 |
| **C# 全量** | **817 通过 / 0 失败 / 0 跳过** |
| **真实诊断目录文件数（全量跑完）** | **259 → 259** ✅ **未新增** |
| Node 全量 | **230 通过 / 0 失败**（18 个文件） |

> 修复前，**每一轮全量**都会往真实目录里加文件；现在**整轮全量跑完一个都不加**。

---

## 四、**已积累的垃圾（未清理，等你决定）**

真实目录里已有 **259 个文件**：

| 形态 | 数量 |
|---|---|
| `locale-test-*` | **248** |
| `step3-op-*` / `phase3c2-op-*` | 11 |

时间跨度 **10-03 04:45 → 10:13**（今天）。

**我没有删除它们**：那是对**用户真实数据目录**的删除操作，需要你明确同意。
建议的清理方式（一条命令，只删 `diagnostics\inst-A` 下上述两种命名形态）：

```powershell
# 仅在你同意后执行
$d = "$env:APPDATA\MCMiniLauncher\diagnostics\inst-A"
Get-ChildItem $d -File | Where-Object { $_.Name -like 'locale-test-*' -or $_.Name -like 'step3-op-*' -or $_.Name -like 'phase3c2-op-*' } | Remove-Item -WhatIf
```

---

## 五、一条值得记住的教训

**"全量绿"不等于"全量干净"**：这个泄漏从今天 04:45 起就一直在发生，
而全量测试**每一轮都是 817/0 全绿** —— 因为它只往真实目录**写**，从不**断言**。
本轮是排查 A15（配置/凭据）时顺带看真实目录才发现的。

> 对策已经落地：给这个夹具加了**真实目录守卫**（§二.3）。
> 但同类风险仍可能存在于**其它自建夹具**里 —— 建议下一步做一次"**全仓测试夹具清点**"，
> 逐个确认是否重定向了 config / recovery / bisect / diagnostic 四个根（见 A21 建议）。

---

## 六、实际改动

| 文件 | 改动 |
|---|---|
| `dev/PineglowLauncher.Tests/AiCancelAndRedirectTests.cs` | 夹具重定向诊断根 + 释放清理 + **真实目录守卫**（+`CountRealDiagnosticFiles`） |
| `docs/market-execution/gap-register.md` | A20 关闭；**新增 A21（全仓夹具清点建议）** |
| `docs/market-task-plan-2026-10-03.md` | 相关行补记 |

**产品代码零改动。**

---

## 七、仍未覆盖

| 项 | 说明 |
|---|---|
| **其它自建夹具** | **未清点**（本轮只修了已确证的那一个）。A21 记录了这件事 |
| 残留垃圾清理 | **未执行**（需你同意，见 §四） |
| `locale-test-*` 那个用例**为什么**要写诊断报告 | 未深究（属用例设计问题，不影响隔离修复） |
| 真实 GUI 下的诊断报告落盘 | 未验证 |

---

## 八、证据清单

```
green/AiCancelAndRedirectTests.cs.fixed   修复后参照
green/01-ai-tests-after-fix.log           该类 31 通过 / 0 失败（真实目录 259 → 259）
green/02-csharp-full.log                  全量 817 通过 / 0 失败（真实目录 259 → 259）
green/03-node-full.log                    230 通过 / 0 失败
manifest.json
```

**本报告不宣布阶段放行、不提交、不发布。**
