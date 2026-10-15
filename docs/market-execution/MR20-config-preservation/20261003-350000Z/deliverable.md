# MR20 补测 · 旧配置/凭据不被重置（缺口登记表 A15）

- **run-id**：`20261003-350000Z`
- **性质**：补"旧配置不被重置"的用例；**过程中发现两个更重要的东西**（见 §四）
- **依据**：`gap-register.md` A15："旧配置/凭据不被重置、主备切换留证 | 补用例"
- **最终状态**：**证据就绪**（5 个用例；1 个 RED 成立；**新增两条缺口 A19/A20**）

---

## 一、做法：给 `ConfigStore` 加**配置路径测试缝**

`ConfigStore` 的读写都走 `PlatformPaths.GetConfigPath()`，而那条路径由"可执行文件旁的 `portable.flag`"
这个**全局**开关决定 —— 在测试进程里动它会波及并行用例。

本轮新增（仅测试用，生产恒为 `null`）：

```csharp
internal static string? ConfigPathOverrideForTest { get; set; }
private static string ResolveConfigPath() =>
    string.IsNullOrWhiteSpace(ConfigPathOverrideForTest) ? PlatformPaths.GetConfigPath() : ConfigPathOverrideForTest!;
```

`LoadAsync` / `SaveCore` 都改用它 → 于是可以对**真实的 `LoadAsync` 归一化逻辑**做验证，
而完全不碰真实 `%APPDATA%`。

---

## 二、5 个用例

| # | 用例 | 断言 |
|---|---|---|
| ① | `LoadAsync_PreservesEveryNonSecretField_FromAnExistingConfig` | `GameDirectory` / `UpdateUrl` / `DownloadSource` / `PlayerName` / `JavaPath` / `MaxMemoryMb` / 窗口尺寸 / `SelectedInstanceId` / 2 个实例 —— **全部原样保留** |
| ② | `LoadAsync_FillsUpdateUrl_OnlyWhenItIsBlank` | 只有**空** `UpdateUrl` 才被补成 `DefaultUpdateUrl`（这是被允许的覆盖之一） |
| ③ | `LoadAsync_DoesNotInjectADefaultInstance_WhenInstancesExist` | 已有实例时**不再注入**默认实例，选中项不变 |
| ④ | `LoadAsync_IfItWritesBack_StillPreservesExistingFields` | **即使发生回写**，内存与盘上的既有字段都保留 |
| ⑤ | `LoadAsync_MustNotCreateOrModifyTheRealCredentialStore` | **安全守卫**：跑完 `LoadAsync` 后，真实 `secrets.json` **不得被创建、也不得被改动** |

**刻意不做的事（写进用例注释）**：不调用 `SaveAsync`；不断言凭据字段（那些值来自真实凭据存储，
断言它们会让用例依赖**本机凭据**）。

---

## 三、RED → GREEN

| | ① | ② | ③ | ④ | ⑤ |
|---|---|---|---|---|---|
| **RED**（把 `UpdateUrl` 改成无条件覆盖） | ❌ | ✅ | ✅ | ❌ | ✅ |
| **GREEN** | ✅ | ✅ | ✅ | ✅ | ✅ |

RED 后 `ConfigStore.cs` **逐字节还原**（与 `green/ConfigStore.cs.fixed` 一致，本轮起遵守该规则）。
全量：**817 通过 / 0 失败 / 0 跳过**。

---

## 四、**两个比原任务更重要的发现**

### 4.1 **A19（新）**：`LoadAsync` 会**回写**，而回写路径会写**真实凭据存储**

我原本假设"没有旧版凭据迁移 → 不回写"。**实测推翻了这个假设**：读取前后 config.json 的 SHA 变了
（`D6D00E01…` → `B34E0A74…`）。

原因：`AccountService.EnsureAccounts` 在"**没有账号**"时会补一个默认账号，
使迁移标志为真 → 走到 `await SaveAsync(config)`（`ConfigStore.cs:175-179`）→ `SaveCore`
→ **`SecretStore.Save(...)` 若干次**，而 `SecretStore` 的路径是**真实 `%APPDATA%`**、**没有测试缝**。

**这次没有造成损害**，已核实：

| 检查 | 结果 |
|---|---|
| `%APPDATA%\MCMiniLauncher\secrets.json` | **不存在**（我的测试里所有凭据值恰好都是空串，`Save` 不落盘） |
| `%APPDATA%\MCMiniLauncher\config.json` | 修改时间仍是 **09-07 00:33**（**未被触碰**） |

**但风险是真实的**：只要某个测试的配置里有**非空**的 `CurseForgeApiKey` / 账号 token，
这条路径就会**把真实凭据写进用户的凭据存储**。因此用例⑤被写成**显式守卫**（存在性与 mtime 双检查）。

**修法建议**（未实施）：给 `SecretStore` 加一个路径测试缝（与 `ConfigStore` 同款），
这样 `SaveAsync`（含 `.bak`/`.prev` 轮转）也能在测试里安全验证 —— 那正是 A15 的另一半"**主备切换留证**"。
**本包没有实施**：它触及凭据处理代码，属于需要你确认的改动。

### 4.2 **A20（新）**：**有测试把文件写进了真实 `%APPDATA%`**

排查凭据问题时发现，真实目录里有**测试产物**：

```
%APPDATA%\MCMiniLauncher\diagnostics\inst-A\step3-op-registry-zero-12345.json     10-03 09:55:46
%APPDATA%\MCMiniLauncher\diagnostics\inst-A\phase3c2-op-fake-seam-12345.json      10-03 09:56:17
…（同目录共 8+ 个，均为 09:55–09:56，来自某次全量测试运行）
```

文件名形态（`inst-A` + `step3-op-*` / `phase3c2-op-*`）指向**诊断报告存储**的用例，
说明**至少有一个测试没有重定向诊断存储根**（`LocalApiTestHarness` 明明提供了
`OverrideDiagnosticStoreRootForTest`）。**这是测试隔离缺口**：它在用户真实数据目录里留下垃圾。

**本包只报告、未修**：需要先定位是哪个用例（本轮预算用在了 A15 主体与上面这条排查上）。

---

## 五、顺带记录：`ConfigStore.SaveAsync` 写的是 **PascalCase**

`ConfigStore.Options` 只设了 `WriteIndented` + `PropertyNameCaseInsensitive`，
**没有** `CamelCase` 命名策略 → 保存出的 config.json 是 **PascalCase**，而读入是大小写不敏感的。
我的用例④最初用 `GetProperty("updateUrl")` 取值就因此失败 —— 已改为**大小写不敏感查找**，
并在注释里写明这个事实。

---

## 六、验证

| 检查 | 结果 |
|---|---|
| 本组用例 | **5 通过 / 0 失败** |
| C# 全量 | **817 通过 / 0 失败 / 0 跳过**（812 + 5） |
| RED | 2 失败（两个保留用例）✓ |
| 还原 | 与 `green/ConfigStore.cs.fixed` **逐字节一致** |
| **真实凭据/配置** | `secrets.json` 不存在；`config.json` 仍为 09-07 → **未被触碰** |

---

## 七、实际改动

| 文件 | 改动 |
|---|---|
| `ConfigStore.cs` | +`ConfigPathOverrideForTest` 测试缝 +`ResolveConfigPath()`；`LoadAsync`/`SaveCore` 改用它（**生产行为不变**） |
| `dev/PineglowLauncher.Tests/ConfigPreservationTests.cs` | **新增**（5 个用例） |
| `scripts/source-snapshot-inputs.txt` | 登记新测试（类别 B 43→44） |
| `docs/market-execution/gap-register.md` | A15 **部分关闭**；**新增 A19 / A20** |
| `docs/market-task-plan-2026-10-03.md` | MR20 行补记 |

---

## 八、仍未覆盖

| 项 | 说明 |
|---|---|
| **主备切换留证（`.bak`/`.prev` 轮转）** | **未测** —— 必须经 `SaveAsync`，那会写真实凭据（见 §4.1）。需要先给 `SecretStore` 加缝 |
| **凭据字段本身** | 刻意不断言（会依赖本机凭据） |
| **A20 的定位与修复** | 未做 |
| 真实 GUI 下的配置迁移 | 未验证 |

---

## 九、证据清单

```
green/ConfigStore.cs.fixed                修复后参照（RED 用）
green/ConfigPreservationTests.cs.fixed    修复后参照
green/01-preservation.log                 首次 3 通过 / 1 失败（暴露"会回写"）
green/02-preservation-fixed.log           4 通过 / 1 失败（暴露 PascalCase）
green/03-preservation-final.log           5 通过 / 0 失败
green/04-preservation-restored.log        RED 还原后本组通过
green/05-csharp-full.log                  817 通过 / 0 失败
red/01-red-updateurl-overwritten.log      RED：无条件覆盖 UpdateUrl → 2 失败
manifest.json
```

**本报告不宣布阶段放行、不提交、不发布。**
