# OPEN-02 执行证据 · 2026-11-22

执行者记录。v1、v2、v3 分别被 Codex 独立复验判 FAIL；v4 修复后本地复跑全绿，**第四轮独立复验 PASS**。`FormalPhaseApproval=NOT_GRANTED`，发布审批另行。

## 结论

- 基线 `274675f` 上真实 `GET /api/state` 已复现迁移缺口：既有 10 组输入 **2 通过 / 8 失败**；v1 测试 12 组输入 **2 通过 / 10 失败**。失败均为业务断言，不是编译、依赖还原、监听或权限失败。
- **第 1 轮阻断（v1）**：`ConfigStore.LoadAsync` 在任一凭据读取失败时会清空内存中的凭据；迁移若走全量 `Save`，`SaveCore` 会把空值写回 `SecretStore`（空值即删除），误删 CurseForge 等无关凭据。
- **v2 修复**：迁移改走 `IConfigStore.SaveConfigOnly` / `ConfigStore.SaveConfigOnlyAsync`，只写 `config.json`，完全不调用 `SecretStore`；新增生产存储回归与“迁移只走 config-only”的计数断言。
- **第 2 轮阻断（v2）**：旧 CurseForge 密钥仅存在于 `config.json`、转存 SecretStore 暂时失败时，v2 的整包重写会把该旧字段从主配置抹掉（只剩 `.bak`），后端恢复后也无法自动重试转存。
- **v3 修复**：`LoadAsync` 在转存失败时置位 `AppConfig.SecretMigrationPending`；`SaveConfigOnlyAsync` 在标志置位时跳过整包重写，保留旧字段，等下一次 `LoadAsync` 重试转存成功后再持久化下载源。
- **第 3 轮阻断（v3）**：标志的置位条件覆盖了整个迁移保存异常。缺少账户列表时 `EnsureAccounts` 生成的账户补全也会触发全量保存；该保存因无关的 AI 凭据删除失败后，标志被误置位，把本可独立执行的下载源迁移挡在磁盘之外。
- **v4 修复**：只统计**旧 JSON 明文凭据**（`MicrosoftRefreshToken`、`CurseForgeApiKey`、旧 AI 配置）是否真的进入待转存状态，只有这些条件成立且保存失败时才置位 `SecretMigrationPending`；账户补全等无关保存失败不再阻止下载源落盘。新增直接存储层与真实 HTTP 回归各一条。
- v4 本地结果：定向 **17/17 通过**；配置/状态相关回归 **79/79 通过**；测试清单校验通过；四道仓库门槛全绿：C# **1502 通过 / 0 失败 / 0 跳过**，前端 **508 通过 / 0 失败 / 0 跳过**，CSS token 49 个，资源闭包通过。
- **第 4 轮 PASS（Codex）**：前三轮阻断均在独立探针中复核通过；账户补全故障下 `SecretMigrationPending=False`，首次 `/api/state` 即落盘 `bmclapi`；定向 17/17、回归 79/79、C# 1502、前端 508，输入与五个生产文件哈希一致。
- 测试总数 1502 = v3 的 1500 + 2 条账户补全回归，与新增用例数一致。
- `docs/baseline.md` 绑定的是 `dcdf056`（1453 通过），与本次 `274675f` 不同；门槛脚本已主动告警，本轮数字不以该基线为准。

## 环境与输入

- 执行工作树：`D:/Projects/_worktrees/pineglow-open02-20261122`
- HEAD：`274675f0aaf686833239811adc39684d5b02743c`（与主工作区一致）
- 工具：dotnet SDK `10.0.301`，Node `v24.19.0`，Windows 11（win32）
- dotnet 门槛运行时将 `C:\Windows\System32;C:\Windows` 前置于 PATH；`TEMP`/`TMP` 指向工作树内 `.cache/tmp`，测试临时目录可归属。
- v4 输入哈希：

| 文件 | SHA-256 |
| --- | --- |
| `22-candidate-final-v4.patch` | `15220EC7C5DB2999550C437D59393301DA9D3B874E86A01C34C6DACEF9CC2CC8` |
| `22-test-final-v4.cs` | `0B8E2B9DFDF5A9F63CD7243A0601A0B984E16CFE52514C2D491459387A607F82` |
| `AppConfig.cs` | `EF7AC244AEDB8D495570EF105B58135902285B505CD11E30B296A239F3BA9ED8` |
| `ConfigStore.cs` | `2B8375FF2746FC3A0CFB8607E6D69317687A09710A92774BAE69EACC4FBEA9E5` |
| `IConfigStore.cs` | `1F05159C63EF67CE0035F3C084F190C9BF528E225542684098319588EF6C5B72` |
| `LocalApiService.Configuration.cs` | `897AD35EF9B9504DBD5FB3D9096F558647F25D7FAF013DBBFFA38C2BEDBFF683` |
| `LocalApiService.Instances.cs` | `C78892DDE83FA38BE993B10C89968A3A971AC3AB76681B97A5074848D1253916` |

## 证据文件

| 文件 | 内容 | 结果 |
| --- | --- | --- |
| `00-preflight.txt` | pwd、Git 根、HEAD、status、输入哈希、工具版本 | 记录 |
| `01-candidate-before-review.patch` | 现场旧补丁（`MigrateDownloadSourceOnce` 方案），已由最终候选取代 | 对照 |
| `01-test-before-review.cs` | 现场 10 组输入测试原件 | 对照 |
| `02-red-baseline.txt` | 现场测试 vs 干净 `274675f` | 10 总数 / 2 通过 / 8 失败，exit 1 |
| `03-candidate-final.patch` | v1 候选生产补丁（+20/−2） | Codex 第 1 轮 FAIL |
| `03-test-final.cs` | v1 测试（12 用例） | 已被取代 |
| `04-red-final-tests-baseline.txt` | v1 测试 vs 干净 `274675f` | 12 总数 / 2 通过 / 10 失败，exit 1 |
| `05-green-targeted.txt` | v1 定向 | 12/12，exit 0 |
| `06-regression.txt` | v1 回归子集 | 79/79，exit 0 |
| `07-inventory-check.txt` | 清单双向校验 | 35 个 node + 1 个 C# 项目，exit 0 |
| `08-gates.txt` | v1 四道门槛 | 全 PASS（C# 1497），exit 0 |
| `09-final-diff-stat.txt` | v1 diff 统计、行数、哈希 | 记录 |
| `10-final-status.txt` | v1 收尾状态 | 记录 |
| `11-fix-targeted.txt` | v2 定向 | 13/13，exit 0 |
| `12-fix-regression.txt` | v2 回归子集 | 79/79，exit 0 |
| `13-fix-gates.txt` | v2 四道门槛 | 全 PASS（C# 1498），exit 0 |
| `14-candidate-final-v2.patch` | v2 补丁（4 文件，+52/−6） | Codex 第 2 轮 FAIL |
| `14-test-final-v2.cs` | v2 测试（13 用例） | 已被取代 |
| `15-fix2-targeted.txt` | v3 定向 | 15/15，exit 0 |
| `16-fix2-regression.txt` | v3 回归子集 | 79/79，exit 0 |
| `17-fix2-gates.txt` | v3 四道门槛 | 全 PASS（C# 1500），exit 0 |
| `18-candidate-final-v3.patch` | v3 补丁（5 文件，+67/−6） | Codex 第 3 轮 FAIL |
| `18-test-final-v3.cs` | v3 测试（15 用例） | 已被取代 |
| `19-fix3-targeted.txt` | v4 定向 | 17/17，exit 0 |
| `20-fix3-regression.txt` | v4 回归子集 | 79/79，exit 0 |
| `21-fix3-gates.txt` | v4 四道门槛 | 全 PASS（C# 1502），exit 0 |
| `22-candidate-final-v4.patch` | v4 最终生产补丁（5 文件，+76/−6） | **Codex 第 4 轮 PASS** |
| `22-test-final-v4.cs` | v4 最终测试（17 用例） | **Codex 第 4 轮 PASS** |
| `verify-codex-fail/` | Codex 第 1 轮 FAIL 交接、探针与命令（原样接收） | FAIL |
| `verify-codex-fail-2/` | Codex 第 2 轮 FAIL 交接、探针与命令（原样接收） | FAIL |
| `verify-codex-fail-3/` | Codex 第 3 轮 FAIL 交接、探针与命令（原样接收） | FAIL |
| `verify-codex-pass/` | Codex 第 4 轮 PASS 交接、三轮探针与命令（原样接收） | PASS |

## 复跑命令

Git Bash（与执行时一致）：

```bash
cd D:/Projects/_worktrees/pineglow-open02-20261122
export PATH="/c/Windows/System32:/c/Windows:$PATH"
export TEMP="D:/Projects/_worktrees/pineglow-open02-20261122/.cache/tmp"
export TMP="$TEMP"
mkdir -p "$TEMP"

# 定向（v4 为 17 条）
dotnet test dev/PineglowLauncher.Tests/Pineglow.Launcher.Tests.csproj -c Release \
  --filter "FullyQualifiedName~DownloadSourceHttpMigrationTests"

# 回归子集
dotnet test dev/PineglowLauncher.Tests/Pineglow.Launcher.Tests.csproj -c Release \
  --filter "FullyQualifiedName~DownloadSourceMigrationTests|FullyQualifiedName~ConfigBackupRotationTests|FullyQualifiedName~ConfigPreservationTests|FullyQualifiedName~ConfigDefaultIdentityTests|FullyQualifiedName~NewsFakeIpConfigTests|FullyQualifiedName~HostDependenciesTests|FullyQualifiedName~LocalApiServiceSplitTests|FullyQualifiedName~RouteReachabilityTests|FullyQualifiedName~ApiRouteInventoryTests"

# 清单与四道门槛
node scripts/run-inventory-tests.cjs --check
node scripts/gates.cjs
```

## 补丁审查要点

1. **现场旧补丁的问题**：迁移放在 `HandleGetStateAsync` 的 `MigrateDownloadSourceOnce` 中，`LoadConfig` 与 `SaveConfig` 分两次获取 `ConfigGate`，并发 `/api/state` 可能重复写回；并发配置保存可能用旧快照覆盖迁移结果；写回无异常处理，磁盘失败会让原本只读的 `/api/state` 变成 500；且只覆盖 `/api/state` 一个入口。
2. **v1 接线**：迁移进入 `LoadConfig` 的 `ConfigGate` 临界区，读—规范化—写回原子完成；写回失败被捕获并记录 WARN；`/api/state` 回传 `Normalize`。
3. **第 1 轮阻断**：`ConfigStore.LoadAsync` 的凭据读取是“一个失败、全部清空”；v1 的迁移写回走全量 `Store.Save` → `SaveCore`，会先把内存里的空凭据写回 `SecretStore`（空值 = 删除），再写 JSON。一次 AI 凭据读异常即可删除无关的 CurseForge 凭据。
4. **v2 修复**：`IConfigStore` 新增 `SaveConfigOnly`（默认回退 `Save`）；`ProductionConfigStore` 覆盖为 `ConfigStore.SaveConfigOnlyAsync`，只做 `ApplySelectedToLegacy` + `WriteConfigFile`，**不调用 `SecretStore`**。
5. **第 2 轮阻断**：旧 JSON 明文 `CurseForgeApiKey` 在 `LoadAsync` 中通过 `SaveAsync` 转存；转存失败被 catch 后，主配置仍保留旧字段。v2 的整包重写会因 `[JsonIgnore]` 把该字段从主配置移除（只剩 `.bak`），后续无法自动重试。
6. **v3 修复**：`LoadAsync` 在转存失败时置位 `AppConfig.SecretMigrationPending`（`[JsonIgnore]` 运行期标志）；`SaveConfigOnlyAsync` 在置位时跳过整包重写。
7. **第 3 轮阻断**：标志的置位条件过宽——缺少账户列表时 `EnsureAccounts` 的账户补全也会进入全量保存，该保存因无关的 `AiApiKey` 删除失败后，标志被误置位，下载源迁移被误挡在磁盘之外。
8. **v4 修复**：新增局部 `legacySecretMigration`，只在三类**旧 JSON 明文凭据**进入待转存状态时置位：旧 AI 配置迁移、`MicrosoftRefreshToken` 恢复、`CurseForgeApiKey` 恢复。只有它成立且保存失败时，才设置 `SecretMigrationPending`；账户补全等无关失败不再阻止迁移落盘。新增 `ProductionStore_AccountOnlySaveFailure_DoesNotBlockMigration` 与 `State_AccountOnlySaveFailure_StillPersistsDownloadSource`。
9. **回归测试总览**：`ProductionStore_ConfigOnlyWrite_PreservesUnrelatedCredential`（第 1 轮，含全量 `Save` 删除反证）、`ProductionStore_LegacyTransferFailure_KeepsConfigForRetry`（第 2 轮，存储层）、`State_LegacyCredentialTransferFailure_PreservesConfigAndRetries`（第 2 轮，HTTP）、两条账户补全回归（第 3 轮）。
10. **第 4 轮独立复验**：Codex 在独立工作树复跑 RED、17/17、79/79、清单、四道门槛（C# 1502）与三轮探针，均通过；源码复核确认只有三类旧配置迁移进入保存异常分支时才设置待转存标志。PASS 原件存于 `verify-codex-pass/`。
11. **语义差异（有意保留）**：发布线用 `OrdinalIgnoreCase` 比较，大小写不同不写回；本候选用 `Ordinal`，`Official` / `BMCLAPI` 也会落盘为小写。依据是 `MirrorHelper.Normalize` 的既有契约“其余原样小写返回”。
12. **未覆盖 / 边界**：真实 Avalonia/WebView2 宿主、发布包冷启动、原生 Windows 凭据后端未在本轮运行；`ConfigStore.LoadAsync` 自身“读失败后若因其它迁移触发全量保存”的既有路径未重构（不属于 OPEN-02 的迁移写回路径，已在 v2/v3/v4 中绕开）。`.bak`/`.prev` 轮转由 `WriteConfigFile` 覆盖，但未单独做真机走查。
