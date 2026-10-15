# 升级就位（旧配置能否存活）审计 + 测试缝盘点（第 21 轮）

日期：2026-10-06（Asia/Hong_Kong）。本轮**没有新增测试**——结果是"这一维度已被现有测试覆盖"，
以及一次我自己的误报。**没有改任何 tracked 文件。**

## 1. 为什么要问"旧配置能不能活下来"

发布评审关注"旧版迁移"，但此前**没有任何一轮**看过：一个装着 v1.0.0 的用户升级到本轮构建后，
**他的 `config.json` 会不会被重置或读出默认值**。而本工作区恰好动了配置相关代码：

```
dev/PineglowLauncher/Pineglow.Launcher.Models/AppConfig.cs   | 15 +++++++++
dev/PineglowLauncher/Pineglow.Launcher.Services/ConfigStore.cs | 105 ++++++++++++++++++++-
```

## 2. 结论：是**加法式**变更，且这一维度已被现有测试覆盖

`AppConfig.cs` 相对 HEAD 只**新增**两个布尔属性（无重命名、无删除、无类型变更）：

| 新字段 | 默认 | 为什么敏感 |
| --- | --- | --- |
| `DiagnosticKeepEvidence` | `false` | 诊断是否保留脱敏证据摘录 |
| `NewsFakeIpCompat`（`[JsonPropertyName("newsFakeIpCompat")]`）| `false` | **安全相关**：它允许新闻图片代理访问 `198.18/15` fake-IP 地址，注释明写"默认 false（fail closed）" |

旧配置缺这两个字段 → 反序列化保持默认 `false` ⇒ **不会因为升级而打开这个开关**。

**现有测试的引用（都是既有测试，不是我新加的）**：

| 覆盖点 | 证据 |
| --- | --- |
| **旧形态**配置文件（camelCase、字段只有子集、缺新字段）经**真实 `LoadAsync`** 加载后，每个非凭据字段原样保留 | `ConfigPreservationTests.LoadAsync_PreservesEveryNonSecretField_FromAnExistingConfig`（手写 JSON 子集 + `ConfigPathOverrideForTest` 重定向，见其第 70–86 行）|
| 加载后若发生写回，既有字段仍保留 | `ConfigPreservationTests.LoadAsync_IfItWritesBack_StillPreservesExistingFields` |
| **不得创建/修改真实凭据存储** | `ConfigPreservationTests.LoadAsync_MustNotCreateOrModifyTheRealCredentialStore`（构造时先换 `InMemorySecretStoreBackend`）|
| 新开关：序列化必带该字段且默认 `false` | `NewsFakeIpConfigTests`（`JsonValueKind.False` 断言）|
| 新开关：PATCH `true`/`false`/`{}`（省略）分别"应用/应用/保留旧值" | `NewsFakeIpConfigTests.PatchConfig_AppliesOrPreserves`（Theory，含 `"{}"` → 保留 `true`）|
| 新开关：`"yes"`（字符串）**不得**隐式改变 | `NewsFakeIpConfigTests.PatchConfig_NonBooleanValue_DoesNotChangeExisting` |
| 新开关：`1`（数字）**不得**被当成 `true` | `NewsFakeIpConfigTests.PatchConfig_NumberValue_DoesNotChangeExisting` |
| 新开关：PATCH 只改别的字段时不得被重置 | `NewsFakeIpConfigTests.PatchConfig_OnlyOtherFields_DoesNotReset` |

这些用例都在快照验证器的 C# 全量里跑过（**903 通过 / 0 失败**），因此这条起点是"已验证"，不是"待验证"。

**边界（如实写）**：我拿不到**已发布的 v1.0.0** 构建产物或它的源码，所以无法用"真实旧文件"做端到端；
现有测试用的是**同类旧形态**（字段子集 + camelCase + 缺新键）。二者形状等级相同，
但这仍然是**模拟**而不是"真机升级"，结论不能外推成"已用真实旧配置验证过"。

## 3. 顺带盘点：产品里的 `…ForTest` 测试缝有没有"立了没用"的

动机：仓库自己用"测试缝 + 缺口登记表"的方式记录可测性，**一个没人用的缝通常意味着一个忘写的测试**。
共找到 **24 个缝**：

- **22 个被测试引用**（`BeforeCommitForTest` 10 处、`OverrideConfigStoreForTest` 12 处、`RotateBackupBeforeWriteForTest` 9 处……）
- 剩下 2 个在 `SecretStore.cs`，初查"0 引用"，但**都是我搜索方式不精确造成的误报，不是缺口**：

| 成员 | 真相 |
| --- | --- |
| `BackendOverrideForTest` | 它是 `AsyncLocal<ISecretStoreBackend?>` 的**存储字段**，由 `CurrentBackend` / `IsBackendOverriddenForTest` / `OverrideBackendForTest` / `BackendOverrideScope` 使用；测试用的是公开包装 `OverrideBackendForTest`（10 处引用）|
| `ResetProductionBackendCallCountForTest` | **被 VerifyHost 使用**：`dev/PineglowLauncher/VerifyHost/BisectSmoke.cs:59` —— 正是验证器里那条"生产凭据后端调用必须为 0"的断言 |

⇒ **没有"立了没用"的缝**，也就**没有藏在缝后面的缺口测试**。

## 4. 我这一轮的错误（记录在第 N 次同类教训里）

第一次统计时我把搜索范围限定在 `dev/PineglowLauncher.Tests`，于是把一个**在 VerifyHost 里被使用**的缝判成"未被任何测试使用"；
另一个则把**后备字段**当成了缝本身。**"判据/范围写错会把结论带反"**——本会话已是第六次出现，
这次发生在"审计测试缝"这件小事上。两处都已在上面更正，没有写进任何结论。

## 5. 未做

- **未**新增测试：既有覆盖已足够，再加一份就是重复而不是补缺。
- **未**改动产品/测试代码。
- **未**在真实旧版机器上做升级（那属于 R06 真机项，见发布手册 §6）。
