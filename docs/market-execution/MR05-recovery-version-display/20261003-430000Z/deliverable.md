# MR05 补测之三 · 生产路径端到端验证（A14）—— **并更正上一轮的一处误判**

- **run-id**：`20261003-430000Z`
- **性质**：验证 A14 在**真实生产路径**上生效；**并撤回上一轮一条错误的"剩余项"**
- **依据**：`MR05-replace-version-metadata/20261003-420000Z` §七 的"剩余"：
  "**前端调用点尚未传 version** …… 界面上的新版本要真正出现还需要前端接线"
- **最终状态**：**证据就绪**（生产路径端到端通过；RED 成立；**上述"剩余项"不成立，已更正**）

---

## 一、**更正**：那条"剩余项"是错的

第 53 轮我把"前端 replace 调用点尚未传 version"记为剩余项。**实测三条事实推翻它**：

| 事实 | 证据 |
|---|---|
| 前端的 Mod 操作**只有** `getMods` / `toggleMod` / `deleteMod` | `api.js:528/539/543` |
| **前端根本没有"更新 Mod"入口** | `ui/js` 里所有 `replace` 命中都是 `String.replace` 或恢复区文案 |
| **生产里的更新走内容服务**，且**本来就传了真实版本** | `ModContentService.cs:140-141`：`ReplaceAsync(instance, modService, installed.FileName, text2, **null, version**)` |

**结论**：新侧车在生产路径上**本来就会被写**，A14 的「旧版本 → 新版本」**在真实产品里是生效的** ✓。
我上一轮把"HTTP 端点没有 UI 调用方"误读成了"前端缺少接线" ✗。

**A22 的价值因此需要重新表述**（不缩水、但要说准）：
`/mods/{file}/replace` 端点**没有 UI 调用方**，它是**测试/自动化缝**；
A22 让这条缝与**生产路径**保持一致（同形状的版本元数据）—— 仍然是正确的加固，但**不是**界面展示的前置条件。

---

## 二、本轮补的用例：**生产路径端到端**

新增 `RecoveryVersionDisplayEndToEndTests`，直接跑生产流程（不发外网）：

1. 造一个带**真实形状侧车**的旧版本（`versionNumber=1.0`）；
2. `ModContentService.DownloadAndInstallAsync(instance, v2.0, modService)`
   —— 与 `ModContentService.cs:140` **同一调用形状**，恢复区根用**夹具那一份**（否则 API 列表看不到）；
3. `GET /api/instances/{id}/recovery` → 断言 **`oldVersion=1.0`、`newVersion=2.0`**。

实测输出：

```
[A14-e2e] 更新结果路径 = …\mods\example-1.0.jar
[A14-e2e] 生产路径更新后：oldVersion=1.0  newVersion=2.0
```

> 这比"调 HTTP 端点"更接近真实使用 —— 它验证的是**产品实际走的代码路径**。

---

## 三、RED

| RED | 改什么 | 结果 |
|---|---|---|
| **1** | 把生产调用里的 `version` 改成 `null`（模拟"更新不记版本"） | **1 失败**，输出精确：`Expected: "2.0"  Actual: null` ✓ |

还原后 `ModContentService.cs` **逐字节一致**（并**触碰 mtime** 以免增量构建跳过 ✓）。

---

## 四、验证

| 检查 | 结果 |
|---|---|
| 新用例 | **1 通过 / 0 失败** |
| C# 全量 | **820 通过 / 0 失败 / 0 跳过**（819 + 1） |
| Node 全量 | **234 通过 / 0 失败** |
| RED | 1 失败（消息精确）✓ |
| 还原 | 逐字节一致 ✓ |

---

## 五、实际改动

| 文件 | 改动 |
|---|---|
| `dev/PineglowLauncher.Tests/RecoveryVersionDisplayEndToEndTests.cs` | **新增**（生产路径端到端用例） |
| `scripts/source-snapshot-inputs.txt` | 登记新测试（类别 B +1） |
| `docs/market-execution/gap-register.md` | **更正 A22 行的"剩余"表述**（撤回"前端接线"） |
| `docs/market-task-plan-2026-10-03.md` | MR05 行补记 |

**产品代码零改动**（本轮只加测试与更正记录）。

---

## 六、仍未覆盖

| 项 | 说明 |
|---|---|
| **真实 GUI 渲染** | 未验证（无桌面会话）；本轮验证的是**接口返回值**与**生产代码路径** |
| 前端恢复区那一行的 **JS 行为用例** | 仍未加（只有代码改动 + i18n；A13 的静态守卫不覆盖这一行） |
| 真实 Mod 更新 | 未跑（需真实源）；本用例用假下载器供字节 |
| `versionId` 与内容一致性校验 | 未做 |

---

## 七、证据清单

```
green/RecoveryVersionDisplayEndToEndTests.cs.fixed  新用例参照
green/ModContentService.cs.fixed                    RED 用参照
green/01-e2e.log                                    生产路径端到端通过（1.0 → 2.0）
green/02-csharp-full.log                            820 通过 / 0 失败
green/03-node-full.log                              234 通过 / 0 失败
red/01-red-production-no-version.log                RED：Expected "2.0" Actual null
manifest.json
```

**本报告不宣布阶段放行、不提交、不发布。**
