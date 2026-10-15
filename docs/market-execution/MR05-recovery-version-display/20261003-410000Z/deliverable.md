# MR05 补测 · 恢复区展示「旧版本 → 新版本」（缺口登记表 A14）

- **run-id**：`20261003-410000Z`
- **性质**：补上"侧车的版本/变更展示"；过程中**查出一个新的真实缺口**
- **依据**：`MR05-mod-replacement-transaction/20261003-*` 报告 §六：
  "侧车的'版本/变更'展示 | 恢复区展示了原因、文件与错误；**没有**做'新旧版本号对比'的界面"
- **最终状态**：**证据就绪**（2 个 C# 用例 + RED；**新增缺口 A22**）

---

## 一、数据来源（先把事实查清）

| 事实 | 位置 |
|---|---|
| 侧车 = `<mod>.mcmini.json`，字段 `source/projectId/versionId/**versionNumber**/installedAt` | `ModInstallMetadata` + `ModService.WriteOnlineMetadata` |
| 替换操作的隔离 reason = **`mod-replace`**，且隔离基准是 **mods 目录**（不是游戏目录） | `ModReplacementService.cs:141` |
| 恢复记录本身**没有**版本字段 | `FileRecoveryRecord` |

**所以**：旧版本读**被隔离的侧车**（恢复区 `files/` 下那份），新版本读**现网侧车** ✓。

---

## 二、实现

### 2.1 后端：`GET /api/instances/{id}/recovery` 增加两个字段

```csharp
oldVersion = ReadSidecarVersion(r, recoveryService, gameDir, instanceId, quarantined: true),
newVersion = ReadSidecarVersion(r, recoveryService, gameDir, instanceId, quarantined: false),
```

`ReadSidecarVersion` 要点：
- **路径基准按 reason 区分**：`mod-replace` → `<gameDir>/mods`，其它 → `<gameDir>`（记录里的 `RelativePath` 是相对那个基准的）；
- 读不到 / 解析失败 / 无侧车 → **一律返回 null**：**展示信息绝不能把列表接口弄挂**。

### 2.2 前端：恢复区面板展示

`launcher.js` 的 `openRecoveryPanel` 里，替换类记录多一行：

```js
const versionLine = (isReplace && (op.oldVersion || op.newVersion))
    ? `<div class="rec-versions">${esc(t('recovery.versions', { from: ..., to: ... }))}</div>`
    : '';
```

i18n 中英各加两键：`recovery.versions`（`版本：{from} → {to}` / `Version: {from} → {to}`）、`recovery.unknownVersion`。

---

## 三、用例与 RED

| 用例 | 断言 |
|---|---|
| `RecoveryList_ForAReplace_ExposesOldAndNewVersion` | 真实形状的旧侧车（`versionNumber=1.0.0`）+ 替换 + 现网侧车（`2.0.0`）→ 列表返回 **`oldVersion=1.0.0`、`newVersion=2.0.0`** ✅ 实测输出一致 |
| `RecoveryList_ForAReplaceWithoutSidecar_ReportsNullVersions_AndStillLists` | **没有侧车**时两者皆 null，且**列表接口仍正常**（防"展示信息弄挂接口"） |

| RED | 改什么 | 结果 |
|---|---|---|
| **1** | 把响应字段 `oldVersion` 改名 | **1 失败** ✓（用例的 `TryGetProperty("oldVersion")` 落空） |

> **诚实记录**：第一次 RED 尝试（把 `newVersion` 直接置 `null`）**编译失败**，属于无效 RED；换成"改字段名"才成立。

---

## 四、**新发现（A22）**：`/mods/{file}/replace` **不接受版本元数据**

实测：该端点只解析 `newFileBase64` 与 `expectedSha256`，**没有 `version`**
→ 调用 `ReplaceAsync(..., version: null)` → `if (version != null)` 不成立 → **不写新侧车** ✗。

**后果**：**API 驱动的替换**产生的记录，`newVersion` **永远是 null**（只有安装/更新流程写过侧车时才有值）。
界面上的"旧版本 → 新版本"因此只能显示一半。

**修法建议（未实施）**：在替换 body 里接受可选的 `version`（`source/projectId/versionId/versionNumber`…），
透传给 `ReplaceAsync` —— 与安装流程的 `WriteOnlineMetadata` 对齐。**属产品改动，已登记为 A22。**

本包的做法：测试里**按安装流程的同一形状补写**现网侧车，从而把**展示逻辑**验证完整 ✓。

---

## 五、验证

| 检查 | 结果 |
|---|---|
| `ModReplacementTests` | **11 通过 / 0 失败** |
| C# 全量 | **819 通过 / 0 失败 / 0 跳过**（817 + 2） |
| Node 全量 | **234 通过 / 0 失败** |
| RED | 1 失败（字段改名）✓ |

---

## 六、实际改动

| 文件 | 改动 |
|---|---|
| `LocalApiService.cs` | 恢复列表加 `oldVersion`/`newVersion` + `ReadSidecarVersion` 辅助 |
| `ui/js/launcher.js` | 恢复区面板展示"版本：旧 → 新" |
| `ui/js/i18n.js` | 中英各加 `recovery.versions` / `recovery.unknownVersion` |
| `dev/PineglowLauncher.Tests/ModReplacementTests.cs` | 新增 2 个 A14 用例 |
| `docs/market-execution/gap-register.md` | A14 关闭；**新增 A22** |
| `docs/market-task-plan-2026-10-03.md` | MR05 行补记 |

---

## 七、仍未覆盖

| 项 | 说明 |
|---|---|
| **`newVersion` 在 API 驱动替换下的可用性** | 受 A22 限制（需先修端点） |
| **真实 GUI 下的展示** | 未验证（无桌面会话）；本轮验证的是**接口返回**与**渲染代码路径**（后者未做 JS 行为用例） |
| 前端渲染的 JS 行为用例 | **未加**（本轮只改渲染代码 + i18n；A13 那类静态守卫未覆盖这一行） |
| 真实 Mod 更新流程 | 未跑（需真实源） |

---

## 八、证据清单

```
green/LocalApiService.cs.fixed         后端修复后参照
green/ModReplacementTests.cs.fixed     用例参照
green/launcher.js.fixed                前端展示参照
green/i18n.js.fixed                    i18n 参照
green/02-a14-tests.log                 11 通过 / 0 失败（oldVersion=1.0.0 newVersion=2.0.0）
green/05-csharp-full-final.log         819 通过 / 0 失败
green/04-node-full.log                 234 通过 / 0 失败
red/01-red-no-new-version.log          第一次 RED（编译失败 → 无效，如实留档）
red/02-red-field-renamed.log           有效 RED：字段改名 → 1 失败
manifest.json
```

**本报告不宣布阶段放行、不提交、不发布。**
