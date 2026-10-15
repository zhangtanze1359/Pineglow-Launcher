# MR20 补测 · 凭据持久化**契约**（缺口登记表 A19 的可验证部分）

- **run-id**：`20261003-490000Z`
- **性质**：**不碰凭据代码**也能把"该写哪些凭据"钉住
- **依据**：`MR20-config-preservation/20261003-350000Z` §4.1（A19）：
  "回写走 `SaveAsync`/`SaveCore` → 写真实凭据存储；`SecretStore` **无测试缝**"
- **最终状态**：**证据就绪**（4 个契约用例；RED 成立）

---

## 一、为什么用"源码级契约"而不是加测试缝

给 `SecretStore` 加路径缝会**触及凭据代码** ✗ —— 我此前把它登记为"需要确认的改动"。
本轮换一条**无需授权**的路：`SaveCore` 的**写入契约**是**源码可判**的 ✓，
于是像 A6/A13 那样把契约钉住（静态守卫）✓。

**代价（如实记录）**：这**不能**证明"写入 → 读回"的真实往返 ✗（那仍需要凭据存储的测试缝）。

---

## 二、被钉住的契约：`SaveCore` 的 **7 处**写入

| # | 表达式 | 语义 |
|---|---|---|
| 1 | `SecretStore.Save(SecretStore.AccountToken(item.Id), value)` | 每账号 token（MS 用 refresh、第三方用 access） |
| 2 | `SecretStore.Save(SecretStore.MicrosoftAccountToken(item.Id), string.Empty)` | **旧键清空**（迁移到 per-account 键） |
| 3 | `SecretStore.Save("MicrosoftRefreshToken", string.Empty)` | **旧全局键清空** |
| 4 | `SecretStore.Save("CurseForgeApiKey", config.CurseForgeApiKey)` | 从配置写入 |
| 5 | `SecretStore.Save(SecretStore.ProxyPassword, config.ProxyPassword)` | 从配置写入 |
| 6 | `SecretStore.Save("AiApiKey", string.Empty)` | **旧全局 AI 键清空** |
| 7 | `SecretStore.Save(AiKey(aiProvider.Id), aiProvider.ApiKey)` | 每供应商键写入 |

**为什么"清空旧键"必须保留**：这是"凭据从全局键迁移到 per-account/per-provider 键"的**收尾动作**；
少清一个，用户机器上就会**同时**留着旧键与新键（旧键可能被别处读走 → 行为不一致）✓。

---

## 三、4 个用例

| # | 用例 | 断言 |
|---|---|---|
| ① | `SaveCore_WritesEveryExpectedSecret` | **恰好 7 处**，且逐项与上表**表达式精确一致**（只取 `SaveCore` 方法体，不把别处算进来） |
| ② | `SaveCore_KeepsClearingTheThreeLegacyGlobalKeys` | 被清空的键**恰好**是那三个（MS 账号旧键 / 全局 refresh / 全局 AI） |
| ③ | `LiteralKeysInConfigStore_MatchSecretStoreConstants` | `ConfigStore` 用的**字面量**与 `SecretStore` 的**常量**一致（含反射读常量值） |
| ④ | `SecretStoreKeyConstants_KeepTheirOnDiskNames` | 三个键名常量与**落盘格式**一致（改名会让用户已存凭据成**孤儿**） |

实测输出（节选）：

```
[A19] SaveCore 里的 SecretStore.Save 调用：（7 条，逐条打印）
[A19] 被清空的旧键：SecretStore.MicrosoftAccountToken(item.Id) | "MicrosoftRefreshToken" | "AiApiKey"
[A19] "MicrosoftRefreshToken" == MicrosoftRefreshToken
[A19] "CurseForgeApiKey" == CurseForgeApiKey
[A19] 三个键名常量与落盘格式一致
```

**③ 抓的是什么**：`ConfigStore` 用字面量、`SecretStore` 用常量 —— 常量一旦改名而字面量没跟上，
凭据就会写到一个**没人读的键**上（静默失效）✓。

---

## 四、RED

| RED | 改什么 | 结果 |
|---|---|---|
| **1** | 删掉"清空旧全局 AI 键"那一处 | **3 失败** ✓（用例 ①②③ 同时抓到） |

RED 后 `ConfigStore.cs` **逐字节还原**（并触碰 mtime ✓）。

---

## 五、验证

| 检查 | 结果 |
|---|---|
| 本组用例 | **4 通过 / 0 失败** |
| C# 全量 | **832 通过 / 0 失败 / 0 跳过**（828 + 4） |
| Node 全量 | **238 通过 / 0 失败**（20 个文件） |
| RED | 3 失败 ✓ |
| 还原 | 逐字节一致 ✓ |

---

## 六、实际改动

| 文件 | 改动 |
|---|---|
| `dev/PineglowLauncher.Tests/SecretPersistenceContractTests.cs` | **新增**（4 个契约用例） |
| `scripts/source-snapshot-inputs.txt` | 登记新测试（类别 B +1） |
| `docs/market-execution/gap-register.md` | A19 更新为"契约已钉住；往返仍未测" |
| `docs/market-task-plan-2026-10-03.md` | MR20 行补记 |

**产品代码零改动**（本轮只加测试）。

---

## 七、仍未覆盖（A19 的实质）

| 项 | 说明 |
|---|---|
| **写入 → 读回的真实往返** | **仍未测** —— 需要 `SecretStore` 的路径测试缝（**触及凭据代码，仍需你确认**） |
| `secrets.json` 的**实际落盘内容/权限** | 未验证（`Save` 里有 `SetUnixFileMode` 分支，仅非 Windows 生效） |
| `LoadAsync` 回写路径的端到端 | 未测（同因） |

> **口径**：本轮把 A19 从"完全未验证"推进到"**契约已钉住、往返仍缺**" ✓ ——
> **没有**把它说成已解决 ✓。

---

## 八、证据清单

```
green/SecretPersistenceContractTests.cs.fixed  用例参照
green/ConfigStore.cs.fixed                     RED 用参照
green/01-contract.log                          4 通过 / 0 失败（含 7 处写入逐条打印）
green/02-csharp-full.log                       832 通过 / 0 失败
green/03-node-full.log                         238 通过 / 0 失败
red/01-red-missing-legacy-clear.log            RED：删掉旧键清空 → 3 失败
manifest.json
```

**本报告不宣布阶段放行、不提交、不发布。**
