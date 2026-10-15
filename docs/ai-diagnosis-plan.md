# AI 崩溃诊断 · 实现计划（AiDiagnoseService）

> 反映 2026-08-16 状态。本文件是「AI 崩溃诊断」功能的**完整执行计划**，是对 [roadmap.md](roadmap.md) 第五节 M1~M4 方向的落地细化。
> 计划随仓库版本管理，属知识库正文。实现完成后按维护规约同步更新本文档与 api-reference / features / roadmap。

## Summary

实现「AI 崩溃诊断」功能。在现有纯 Web UI 架构上，新增 `AiDiagnoseService` 服务，接通 C# 服务层 → API 路由 → 前端，复用现有 `CrashAnalyzerService`（规则引擎）与 `ModHealthService`，AI 作为**增强解释**串行输出，AI 不可用时优雅降级为纯规则结果。

**范围**：仅做"崩溃 AI 诊断"单场景，不实现多模型切换 / 会话历史 / `/` 命令 / RAG（roadmap 已明确不做）。

## Current State Analysis（现状，已核实）

### 后端
- `CrashAnalyzerService.Analyze(gameDirectory, exitCode)` 返回 `CrashAnalysis(Summary, Suggestions, SourceFile)`，读取 latest.log 或 crash-reports/*.txt 尾部（私有 `ReadTail`，512KB 上限）。**无公共读取 tail 的方法**。
- `ModHealthService.Analyze(instance, mods)` 返回 `ModHealthReport{EnabledCount,DisabledCount,OnlineManagedCount,Issues}`。
- `LocalApiService`：HttpListener 监听 `127.0.0.1:48123`，`HandleApiAsync` 手工路由（`seg` 数组逐段匹配）。已有静态 `ApiHttp`（代理感知，`RefreshApiHttpProxy` 重建）。写响应用 `WriteJsonAsync`，读 body 用 `ReadBodyAsync`，取字符串字段用 `JsonUtil.GetString`。`LoadConfig()`/`SaveConfig()` 用 `ConfigGate` 锁。
- `AppConfig`：密钥字段用 `[JsonIgnore]` 不掉盘；`CurseForgeApiKey` 先例。密钥经 `SecretStore`（Windows 凭据管理器 / 非 Win 加密文件）持久化，在 `ConfigStore.LoadAsync` 用 `SecretStore.Load("CurseForgeApiKey")` 读回、在 `SaveAsync` 用 `SecretStore.Save("CurseForgeApiKey", ...)` 写入。
- `/api/state` 返回 config 子集（不含密钥，仅布尔 `curseForgeApiKeyConfigured`）。`/api/config` POST 走 `PatchConfig` 逐字段更新。

### 前端
- `ui/js/api.js`：`SG.api` 对象，`live`（fetch 真实 API）与 `mock`（内存 `SG.mock`）**双通道需同步实现**。工具 `_req`/`_post`。实例启动走 `POST /api/instances/{id}/launch`（fire-and-forget，当前无崩溃检测）。
- `ui/js/launcher.js`：`openInstanceMenu(inst)` 弹窗（实例操作菜单，含"管理 Mod"与"删除"），`openModal(html)` 通用弹窗。设置页 `renderSettings`/`bindSettings`，保存用 `SG.api.updateConfig(payload)`。`esc()` 转义、`toast()` 提示。
- `ui/js/i18n.js`：`zh`/`en` 两个字典，`t(key)` 取词。
- 实例配置保存：`updateConfig({ ... })` 已泛化，新增字段只需在 payload 里加 key + 后端 `PatchConfig` 认领。

## Proposed Changes（决策完整）

### 后端

#### 1. `AppConfig.cs`（Models）新增 4 字段
在类内（`CurseForgeApiKey` 附近）追加：
```csharp
/// <summary>AI 诊断服务地址（OpenAI 兼容 chat/completions 基址，默认 DeepSeek）</summary>
public string AiProviderUrl { get; set; } = "https://api.deepseek.com/v1";

/// <summary>AI 诊断使用的大模型名</summary>
public string AiModel { get; set; } = "deepseek-chat";

[JsonIgnore]
public string AiApiKey { get; set; } = string.Empty;

/// <summary>AI 诊断功能开关</summary>
public bool AiDiagnosisEnabled { get; set; }
```
> `AiApiKey` 用 `[JsonIgnore]` 不掉盘，与 `CurseForgeApiKey` 同先例。

#### 2. `ConfigStore.cs` 持久化 AiApiKey
- `LoadAsync`：仿 CurseForge 分支，`config.AiApiKey = SecretStore.Load("AiApiKey");`（放现有 `try` 内）。
- `SaveAsync`：新增 `SecretStore.Save("AiApiKey", config.AiApiKey);`。

#### 3. `CrashAnalyzerService.cs` 抽公共读取方法（M1）
新增 public 方法，供 `AiDiagnoseService` 复用：
```csharp
public static string ReadNewestDiagnosticTail(string gameDirectory)
    => ReadTail(FindNewestDiagnosticFile(gameDirectory) ?? string.Empty, 524288);
```
> 复用现有私有 `FindNewestDiagnosticFile`/`ReadTail`，不改动 `Analyze` 行为。

#### 4. 新增 `AiDiagnoseService.cs`（Services）
实现 OpenAI 兼容 chat/completions 调用，核心职责：组装 prompt + 调用模型 + 优雅降级。

```csharp
public static class AiDiagnoseService
{
    public static string ComposePrompt(string tail, CrashAnalysis rule, ModHealthReport mods);

    /// <summary>调用模型，失败返回 null（调用方降级）；http 由调用方传入代理感知实例。</summary>
    public static async Task<string?> DiagnoseAsync(
        HttpClient http, string baseUrl, string apiKey, string model,
        string tail, CrashAnalysis rule, ModHealthReport mods, CancellationToken ct);
}
```
要点：
- `ComposePrompt`：拼接「崩溃日志尾部（截断到 ~2000 字符）+ 规则引擎预判（summary+suggestions）+ Mod 健康报告问题列表」，中文内联指令要求模型用自然语言解释崩溃原因、给出修复步骤、指出嫌疑 Mod。
- `DiagnoseAsync`：`POST {baseUrl}/chat/completions`，`Content-Type: application/json`，头 `Authorization: Bearer {apiKey}`；body `{ model, messages:[{role:user,content}], temperature:0.3 }`；`HttpClient.Timeout` 设 ~60s；解析 `choices[0].message.content`。任何异常（网络/非 200/JSON 解析）返回 `null`，不抛到上层。

#### 5. `LocalApiService.cs`
- **路由**：在 `HandleApiAsync` 的实例分支（`seg.Length >= 4 && seg[1]=="instances"` 内，`launch` 分支附近）新增：
```csharp
if (action.Equals("diagnose", StringComparison.OrdinalIgnoreCase) && method == "POST")
{
    await HandleDiagnoseInstanceAsync(request, response, seg[2]);
    return;
}
```
- **handler**：新增 `HandleDiagnoseInstanceAsync`：
  1. `LoadConfig()` + `RequireInstance(config, instanceId)` + `ResolveGameDirectory`。
  2. 读 tail：`CrashAnalyzerService.ReadNewestDiagnosticTail(gameDir)`；规则：`CrashAnalyzerService.Analyze(gameDir, exitCode)`（exitCode 取 body 可选字段，默认 0）。
  3. Mod 健康：`ModHealthService.Analyze(instance, await new ModService(gameDir).GetModsAsync(instance))`。
  4. AI：仅当 `config.AiDiagnosisEnabled && !string.IsNullOrEmpty(config.AiApiKey) && !string.IsNullOrWhiteSpace(config.AiProviderUrl)` 时，用 `ApiHttp`（代理感知）调 `AiDiagnoseService.DiagnoseAsync(...)`；失败 result 为 null。
  5. **始终返回 200**，体：
```csharp
new {
    ok = true,
    hasTail = !string.IsNullOrEmpty(tail),
    rule = new { summary = rule.Summary, suggestions = rule.Suggestions },
    mods = new { mods.EnabledCount, mods.DisabledCount, mods.OnlineManagedCount, issues = mods.Issues.Select(...) },
    ai = aiText,            // string?  null 表示未启用或失败
    aiError = aiFailedMsg,  // string?  仅 AI 失败时非空
    configured = config.AiDiagnosisEnabled && !string.IsNullOrEmpty(config.AiApiKey)
}
```
  6. 异常统一由外层 catch 兜底（500），但 AI 相关失败不触发 500（上面已降级）。
- **`PatchConfig`**：新增认领 `aiProviderUrl`、`aiModel`、`aiApiKey`（`JsonUtil.GetString`）、`aiDiagnosisEnabled`（bool）。
- **`/api/state` config 输出**：新增 `aiDiagnosisEnabled`、`aiProviderUrlConfigured = !string.IsNullOrEmpty(config.AiProviderUrl)`、`aiKeyConfigured = !string.IsNullOrEmpty(config.AiApiKey)`（不返回密钥明文）。

### 前端

#### 6. `ui/js/api.js`
- `live` 新增 `diagnoseInstance(instanceId)`：
```js
async diagnoseInstance(instanceId) {
    return await _post('/api/instances/' + encodeURIComponent(instanceId) + '/diagnose', {});
}
```
- `mock` 新增同形状 `diagnoseInstance`：返回 `SG.mock.diagnose`（见下），`_mock(...)` 包一层延迟。
- `SG.mock` 新增 `diagnose` 假数据：`{ ok:true, hasTail:true, rule:{summary:'…', suggestions:[…]}, mods:{…}, ai:'…（模拟 AI 解释）', aiError:null, configured:true }`。
- `SG.mock.settings` 新增 `aiDiagnosisEnabled:false, aiProviderUrlConfigured:true, aiKeyConfigured:false`。

#### 7. `ui/js/launcher.js`
- `openInstanceMenu(inst)`：在"管理 Mod"按钮旁新增 `AI 诊断` 按钮（`id="mDiagnose"`，`class="modal-btn ghost"`），onclick → `openAiDiagnose(inst)`。
- 新增 `async function openAiDiagnose(inst)`：
  - `openModal` 结构：标题 + `.page-sub`（版本/加载器）+ 内容区 `#diagContent`（初始 loading）+ 关闭按钮。
  - 调 `SG.api.diagnoseInstance(inst.id)`。
  - 渲染：规则结果（summary + suggestions 列表）+ 分隔 + AI 结果。AI 结果用轻量 markdown 渲染（见下）。若 `ai===null` 且 `configured===false` → 提示"未配置 AI（设置中开启），当前为规则分析"；若 `ai===null` 且 `aiError` → 提示 AI 失败已降级。
  - 隐私提示：底部小字"崩溃日志含用户名/绝对路径，仅发送至所选模型"。
- 新增轻量 **markdown 渲染函数**（仅子集）：`renderMarkdown(text)` 支持 `\n` 段落、`**加粗**`、`## / ### 标题`、`- 列表`、`` `行内代码` ``、`1. 有序列表`；其余按纯文本+换行。返回 HTML，内容先 `esc()` 再回归（简单实现，非完整解析器）。
- 设置页 `renderSettings`：新增"AI 诊断" section（`settings-section`），含启用 toggle + 服务地址输入 + 模型输入 + API Key 输入（`type=password`，留空表示不改）。`bindSettings` 新增按钮：保存时 `SG.api.updateConfig({ aiDiagnosisEnabled, aiProviderUrl, aiModel, aiApiKey })`，其中 `aiApiKey` 仅当输入框非空才提交（避免覆盖已存密钥）。

#### 8. `ui/js/i18n.js`
`zh` 与 `en` 各新增键（前缀 `diag.` / `set.`）：
- `'diag.title'`/`'diag.analyzing'`/`'diag.ruleTitle'`/`'diag.aiTitle'`/`'diag.notConfigured'`/`'diag.aiFailed'`/`'diag.noTail'`/`'diag.privacy'`/`'diag.button'`
- `'set.aiSection'`/`'set.aiEnableName'`/`'set.aiEnableDesc'`/`'set.aiUrlName'`/`'set.aiModelName'`/`'set.aiKeyName'`/`'set.aiSaved'`/`'set.aiSaveFailed'`

### 文档（维护规约要求同步）
#### 9. `docs/api-reference.md`
新增 `POST /api/instances/{id}/diagnose` 端点说明（请求/响应/降级语义），并把工程.md 的端点数核对为 50（如需）。

#### 10. `docs/features.md`
`installations` 视图补充"AI 崩溃诊断（弹窗）"；能力清单新增 AI 诊断（需配置 Key，默认 DeepSeek）。

#### 11. `docs/roadmap.md`
把第五节 M1~M4 标注为「已实现/进行中」，更新现状基准（`CrashAnalyzerService` 现已有 API 端点与前端）。

## Assumptions & Decisions

- **决策**：诊断入口放在实例操作菜单（`openInstanceMenu`）的"AI 诊断"按钮，不新增独立视图（最小改动）。
- **决策**：AI 协议用 OpenAI 兼容 `chat/completions`，默认 DeepSeek（`https://api.deepseek.com/v1` + `deepseek-chat`），与 roadmap 一致。
- **决策**：API Key 走 `SecretStore`（`AiApiKey` 键），`[JsonIgnore]` 不掉 config.json，与 CurseForge 先例一致。
- **决策**：AI 失败 / 未配置 → 返回 200 + `ai:null` + `aiError`，前端降级展示规则结果，**不抛 500**。
- **决策**：markdown 只做最小子集渲染，不引入第三方库（`index.html` 无外部依赖）。
- **决策**：`exitCode` 前端不感知，默认传 0；规则引擎在有 tail 匹配时不受影响，AI 看原始 tail 为主。
- **假设**：AI 调用需穿代理时复用 `LocalApiService.ApiHttp`（已代理感知），不对 AI 单独建代理。
- **不做**：日志页 AI 入口、多模型切换、会话历史、RAG、`/` 命令（roadmap 已明确）。

## Verification

1. 后端编译通过：`dotnet build dev/PineglowLauncher/Pineglow.Launcher.csproj`。
2. 无 AI Key 时 `curl -X POST http://127.0.0.1:48123/api/instances/{id}/diagnose` 返回 200 + `{ok:true, rule:{...}, ai:null, configured:false}`。
3. 配置了 Key 且网络可达时，`ai` 返回模型文本；构造失败情景（错 Key/断网）返回 `ai:null` + `aiError`，仍 200。
4. `config.json` 中**不出现** `AiApiKey`（明文）；`SecretStore` 中可读回。
5. 前端 mock 模式（`file://` 打开 `ui/index.html`）点"AI 诊断"正常展示规则 + 模拟 AI 结果；live 模式接通真实端点。
6. 设置页 AI 三项可保存并回显（`/api/state` 返回 `aiDiagnosisEnabled`/`aiKeyConfigured`）。
7. 文档 3 处同步更新完成。