# MR05 补测之二 · 替换端点接受版本元数据（缺口登记表 A22）

- **run-id**：`20261003-420000Z`
- **性质**：把上一轮发现的 A22 修掉，使 A14 的「旧版本 → 新版本」展示**真正完整**
- **依据**：`MR05-recovery-version-display/20261003-410000Z` §四：
  "`/mods/{file}/replace` **不接受版本元数据** …… API 驱动的替换产生的记录，`newVersion` **永远是 null**"
- **最终状态**：**证据就绪**（RED 成立；A14 用例改为**全程走 API**）

---

## 一、缺口与修法

**修复前**：`HandleReplaceModAsync` 只解析 `newFileBase64` / `expectedSha256`
→ `ReplaceAsync(..., version: null)` → `if (version != null)` 不成立 → **不写新侧车**
→ 恢复区里 `newVersion` 永远为 null。

**修复后**：接受**可选**的 `version` 对象并透传：

```csharp
ModVersion? version = null;
if (body.RootElement.TryGetProperty("version", out JsonElement versionEl) && versionEl.ValueKind == JsonValueKind.Object)
{
    string versionSource = JsonUtil.GetString(versionEl, "source")?.Trim() ?? string.Empty;
    version = new ModVersion
    {
        Source = versionSource.Equals("curseforge", StringComparison.OrdinalIgnoreCase) ? ModSource.CurseForge : ModSource.Modrinth,
        ProjectId = ..., VersionId = ..., VersionNumber = ..., FileName = ..., DisplayName = ...
    };
    // 连版本号与版本 ID 都没有 → 没有可用信息，宁可不写侧车
    if (string.IsNullOrWhiteSpace(version.VersionNumber) && string.IsNullOrWhiteSpace(version.VersionId)) version = null;
}

ModReplacementResult result = await replacement.ReplaceAsync(
    instance, modService, fileName, stagedPath,
    string.IsNullOrWhiteSpace(expectedSha256) ? null : expectedSha256,
    version); // A22：把版本元数据透传下去（写新侧车）
```

**`source` 的解析沿用仓库既有惯例**（`LocalApiService.cs:4996` 同款：`== "curseforge" ? CurseForge : Modrinth`）✓，
不引入新的解析方式。

**向后兼容**：`version` 是**可选**的 —— 不传的调用方行为与修复前完全一致（不写侧车）✓。

---

## 二、用例加强：A14 现在**全程走 API**

上一轮因为 A22，测试不得不**手工补写**现网侧车才能验证展示逻辑。本轮改成：

```csharp
(int status, string body) = await h.SendAsync(HttpMethod.Post, fx.Api("/mods/a.jar/replace"),
    new { newFileBase64 = Base64(NewJar), version = new { source = "Modrinth", projectId = "proj-1",
          versionId = "ver-2", versionNumber = "2.0.0", fileName = "demo-2.0.0.jar", displayName = "Demo" } });

// A22：新侧车必须**真的**被写出来（这是"新版本"的来源）
Assert.True(File.Exists(liveSidecar), "替换端点应写出新侧车（A22）");
Assert.Contains("2.0.0", File.ReadAllText(liveSidecar));
```

随后恢复列表实测：**`oldVersion=1.0.0  newVersion=2.0.0`** ✅ ——
**端到端**（API 传版本 → 写侧车 → 列表读回 → 前端展示）全部打通，**不再有任何手工补写**。

---

## 三、RED

| RED | 改什么 | 结果 |
|---|---|---|
| **1** | 把透传改回 `null`（不再写新侧车） | **1 失败**，消息精确：**"替换端点应写出新侧车（A22）"** ✓ |

RED 后 `LocalApiService.cs` **逐字节还原**（与 `green/LocalApiService.cs.fixed` 一致 ✓）。

---

## 四、验证

| 检查 | 结果 |
|---|---|
| `ModReplacementTests` | **11 通过 / 0 失败** |
| C# 全量 | **819 通过 / 0 失败 / 0 跳过** |
| Node 全量 | **234 通过 / 0 失败** |
| RED | 1 失败（消息精确）✓ |
| 还原 | 逐字节一致 ✓ |

---

## 五、本轮遵守的两条教训

1. **触碰 mtime**：`Copy-Item` 会保留源文件时间戳 → 增量构建可能被跳过、测试跑旧二进制。
   本轮 RED 还原后**显式 `LastWriteTime = Get-Date`** 再重建 ✓（上一轮正是因此拿到过一次假结果）。
2. **用 `edit` 工具改源码**，不用 PowerShell 做手术 ✓（本轮两处改动都是 `edit`/单条字面替换，未再出现引号事故）。

---

## 六、实际改动

| 文件 | 改动 |
|---|---|
| `LocalApiService.cs` | `HandleReplaceModAsync` 解析可选 `version` 并透传给 `ReplaceAsync` |
| `dev/PineglowLauncher.Tests/ModReplacementTests.cs` | A14 用例改为**全程走 API** + 断言新侧车被写出 |
| `docs/market-execution/gap-register.md` | **A22 关闭** |
| `docs/market-task-plan-2026-10-03.md` | MR05 行补记 |

---

## 七、仍未覆盖

| 项 | 说明 |
|---|---|
| **真实 Mod 更新流程** | 未跑（需真实源）；本包验证的是端点契约与端到端读写 |
| **真实 GUI 渲染** | 未验证（无桌面会话） |
| `source` 之外的元数据校验 | 未做（例如 `versionId` 与实际内容是否一致） |
| 其它调用方 | 前端目前**没有**在替换时传 `version`（该字段先由 API 侧提供；前端接线属另一件事） |

> 最后一条值得注意：**后端已支持**，但**前端尚未传**。界面上的"新版本"要真正出现，
> 还需要前端在调用 replace 时带上 version（或改由安装流程负责写侧车）。
> 本包**没有**改前端调用点 —— 如实记为剩余项。

---

## 八、证据清单

```
green/LocalApiService.cs.fixed         修复后参照
green/ModReplacementTests.cs.fixed     用例参照
green/01-csharp-full.log               819 通过 / 0 失败
green/02-node-full.log                 234 通过 / 0 失败
red/01-red-no-version-passthrough.log  RED：不透传 → "替换端点应写出新侧车（A22）"失败
manifest.json
```

**本报告不宣布阶段放行、不提交、不发布。**
