# AI 功能演进记录（原 .trae/documents 三份规划整理）

> 本文由 .trae/documents/ 下三份规划文档整理而来，2026-08-17 建立。
> 三份文档按时间顺序是同一个 AI 功能的三阶段演进；其结论已落地为 `AiDiagnoseService`（单套配置基线）与"多提供商预设"改造（当前形态）。
> 本文是这些规划的唯一正式归档：写代码前先看本文与 [ai-diagnosis-plan.md](ai-diagnosis-plan.md)，.trae/documents 下的原始规划留作过程留痕，不再单独引用。

## 一、三份原始文档与关系

| 原始文件（.trae/documents/） | 定位 | 对应现行文档 |
|---|---|---|
| 创建项目知识库计划.md | 建 `docs/` 知识库的计划（2026-08-16），已完成 | 本知识库（docs/README.md 入口） |
| 推进AiDiagnoseService实现计划.md | AI 诊断第一版：单套配置（URL/模型/Key）实现计划 | ai-diagnosis-plan.md + 本文 §2 | 
| AI提供商预设自定义配置.md | 演进：升级为多提供商预设 + 可自由编辑 | 本文 §3（已实现） |

演进脉络：**建知识库 → 单套 AI 配置的诊断功能 → 多提供商预设改造**。代码实现以后两份为准；第一份是知识库本身，其产物直接就是 `docs/`。

## 二、基线功能（推进AiDiagnoseService 计划，已实现）

设计目标：崩溃后把「规则引擎预判 + Mod 健康检查 + 日志尾部」交给大模型做自然语言解释，AI 不可用时优雅降级为纯规则结果。

### 服务层
- 新增 `AiDiagnoseService`（Services）：
  - `ComposePrompt(tail, rule, mods)`：拼接崩溃日志尾部（截断至 ~2000 字符）+ `CrashAnalysis`（summary+suggestions）+ `ModHealthReport` 问题列表，中文内联指令要求解释原因、给修复步骤、指出嫌疑 Mod。
  - `DiagnoseAsync(http, baseUrl, apiKey, model, ...)`：`POST {baseUrl}/chat/completions`，`Authorization: Bearer {apiKey}`（**Key 为空时不带头**），body `{model, messages:[{role:user,content}], temperature:0.3}`，60s 超时；任何失败返回 `null`，不抛到上层。
- `CrashAnalyzerService` 抽出公共 `ReadNewestDiagnosticTail`（复用私有 `FindNewestDiagnosticFile`/`ReadTail`，512KB 上限），不改动 `Analyze` 行为。
- 复用 `ModHealthService.Analyze` 产出 Mod 健康报告。
- `AppConfig` 增 `AiDiagnosisEnabled` 开关；`ConfigStore` 持久化 Key（SecretStore，`AiApiKey` 键，`[JsonIgnore]` 不入 config.json）。

### API
- 新增 `POST /api/instances/{id}/diagnose`。**始终返回 200**：
  - `ok` / `hasTail` / `rule{summary,suggestions}` / `mods{...}` / `ai`（string?，null=未启用或失败）/ `aiError` / `configured`。
  - 仅当 `AiDiagnosisEnabled && AiApiKey 非空 && AiProviderUrl 非空` 才调 AI；AI 失败不触发 500。
- `PatchConfig` 认领 `aiProviderUrl`/`aiModel`/`aiApiKey`/`aiDiagnosisEnabled`；`/api/state` 输出 `aiDiagnosisEnabled`/`aiProviderUrlConfigured`/`aiKeyConfigured`（不返回密钥明文）。

### 前端
- `api.js`：`live.diagnoseInstance(instanceId)` + `mock` 同形状（`SG.mock.diagnose`）；`SG.mock.settings` 增 `aiDiagnosisEnabled`/`aiProviderUrlConfigured`/`aiKeyConfigured`。
- `launcher.js`：实例操作菜单（`openInstanceMenu`）加"AI 诊断"按钮 → `openAiDiagnose` 弹窗（规则结果 + 分隔 + AI 结果），AI 结果用轻量 `renderMarkdown`（仅 换行/`**`/`##`/`-`/`` ` ``/`1.` 子集，先 `esc()` 再回归，不引第三方库）；底部隐私提示"崩溃日志仅发送至所选模型"；设置页新增 AI 诊断 section（启用 toggle + 地址/模型/Key 输入，Key 留空表示不改）。
- `i18n.js`：`diag.*` / `set.ai*` 键，zh/en 双语对齐。

### 设计决策（基线版）
- 入口放实例操作菜单，不新增独立视图（最小改动）。
- 协议 OpenAI 兼容 `chat/completions`，默认 DeepSeek（`https://api.deepseek.com/v1` + `deepseek-chat`）。
- AI 失败/未配置 → 200 + `ai:null` + `aiError`，前端降级展示规则结果，不抛 500。
- `exitCode` 前端不感知，默认传 0；AI 以原始 tail 为主。
- AI 调用复用 `LocalApiService.ApiHttp`（代理感知），不单独建代理。

## 三、多提供商预设改造（AI提供商预设自定义配置 计划，已实现）

### 目标与形态
把「全局单套 AI 配置（URL/模型/Key）」升级为「多提供商预设 + 可自由编辑」：每套 profile 独立存名称/BaseUrl/模型/Key，可新增/编辑/删除/设为当前；内置常用服务商模板，选择后仍可手动改。不改变 AI 调用协议与诊断流程。

### 数据模型（Models）
```csharp
public sealed class AiProviderProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N"); // GUID hex，满足 SecretStore.ValidateName
    public string Name { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = string.Empty;  // OpenAI 兼容基址
    public string Model { get; set; } = string.Empty;
    [JsonIgnore] public string ApiKey { get; set; } = string.Empty; // 不入 config.json，SecretStore 键 AiKey_{id}
}
```
`AppConfig`：删 `AiProviderUrl`/`AiModel`/`AiApiKey`，保留 `AiDiagnosisEnabled`，新增 `AiProviders`（列表）+ `SelectedAiProviderId`。

### 持久化（ConfigStore）
- 密钥键 `AiKey_{id}`（GUID hex 满足 `ValidateName` 仅字母数字/`-`/`_`）。
- `LoadAsync`：遍历 `AiProviders` 逐个从 SecretStore 读回 Key；并做**一次性迁移**——旧 config.json 的 `AiProviderUrl`/`AiModel` + SecretStore `AiApiKey` 非空时，新建默认 DeepSeek profile 并选中，触发 Save 落盘。
- `SaveAsync`：遍历逐个 `SecretStore.Save(AiKey(p.Id), p.ApiKey)`；清理旧 `AiApiKey` 凭据。

### API 语义
- `/api/state` config 输出 `aiProviders[]`（每项 `id/name/baseUrl/model/keyConfigured`，无 key 明文）+ `selectedAiProviderId`，替代旧 `aiProviderUrlConfigured`/`aiKeyConfigured`。
- `PatchConfig` 对 `aiProviders` 采用**整体替换 + 按 id 合并密钥**：请求带完整列表（含既有 id）；未带 `apiKey` 的项保留 SecretStore 已有值；请求缺失的旧 id 视为删除并清理密钥。
- 诊断/`configured` 判断基于 `SelectedAiProviderId` 指向的 profile。**Ollama 免 Key**：`configured` 改为 `!string.IsNullOrWhiteSpace(BaseUrl)`（不再要求 Key 非空），空 Key 请求不带 `Authorization` 头。

### 前端
- 内置预设为**前端常量**（纯填表模板，后端只存用户实际保存的 profile）：
  ```js
  const AI_PRESETS = {
    deepseek: { name:'DeepSeek', baseUrl:'https://api.deepseek.com/v1', model:'deepseek-chat' },
    openai:   { name:'OpenAI',   baseUrl:'https://api.openai.com/v1',   model:'gpt-4o-mini' },
    qwen:     { name:'通义千问', baseUrl:'https://dashscope.aliyuncs.com/compatible-mode/v1', model:'qwen-plus' },
    zhipu:    { name:'智谱',     baseUrl:'https://open.bigmodel.cn/api/paas/v4', model:'glm-4-flash' },
    ollama:   { name:'Ollama',   baseUrl:'http://localhost:11434/v1',   model:'llama3.1' },
  };
  ```
- 设置区：「管理 AI 提供商」按钮 → 管理弹窗（列表 + 新增/编辑/删除/设为当前）；编辑/新增弹窗有预设下拉（选择后自动填充，可手改），Key 输入 `type=password`（新增必填？Ollama 可留空；编辑留空=保持不变）。
- 保存 payload：`{ aiProviders: [...], selectedAiProviderId, aiDiagnosisEnabled }`；`saveAiProviders` 提交后刷新设置区。

### 设计决策（多提供商版）
- 内置预设是前端常量，后端只存用户实际保存结果。
- 每个 profile 的 Key 独立存 SecretStore（`AiKey_{id}`），config.json 永不出现 Key。
- 旧单套配置首次加载一次性迁移为默认 DeepSeek profile，并清理旧 `AiApiKey`。
- 诊断判断基于选中 profile；无选中或不可调用时优雅降级（与基线一致）。
- Ollama 免 Key：`configured` 只验 BaseUrl，空 Key 不带头，AI 失败仍返回 `aiError` 降级。

## 四、实现核对（2026-08-17）

以下核对基于工作区当前未提交代码，结论：**三份计划的结论均已实现**，与本文 §2/§3 描述一致。

| 项目 | 状态 |
|---|---|
| `AiProviderProfile`（Models） | ✅ 与 §3 一致 |
| `AppConfig` 四字段（AiProviders/SelectedAiProviderId/AiDiagnosisEnabled） | ✅ 已落地 |
| `ConfigStore` per-profile 密钥 + 一次性迁移 + 旧 `AiApiKey` 清理 | ✅ 已实现（`AiKey(id)` 辅助、迁移分支、Save 清理） |
| `AiDiagnoseService`（含空 Key 不带头） | ✅ 已实现 |
| `POST /api/instances/{id}/diagnose` + 200 降级语义 | ✅ 已实现 |
| 前端 `AI_PRESETS` 五预设 + 管理/编辑弹窗 | ✅ 已实现
| 嫌疑 Mod 结构化输出 + 一键禁用（`suspectMods` + JSON 尾巴约定） | ✅ 已实现（2026-08-17） |
| 前端 live/mock 双通道 + 诊断弹窗 + `renderMarkdown` | ✅ 已实现 |
| i18n `diag.*`/`set.ai*` 双语 | ✅ zh/en 完整对齐（315/316 键无缺口） |
| 文档同步（features/api-reference/roadmap） | ✅ 已同步（含本文） |

> 说明：与 .trae 原始计划相比有两处**有意偏差**（按代码实况为准）：① 设置区独立显示"当前提供商"行，管理弹窗为列表+编辑视图，而非计划的"先下拉选预设再配置"形态；② i18n 保留了 `set.aiUrlName`/`set.aiModelName`/`set.aiKeyName` 等通用键（编辑弹窗仍使用），仅移除了不再使用的 `set.aiConfigure`/`set.aiDefaultUrl`。

## 五、验证清单（回归用）

1. 后端编译：`dotnet build dev/PineglowLauncher/Pineglow.Launcher.csproj`。
2. 未配置 Key：`curl -X POST http://127.0.0.1:48123/api/instances/{id}/diagnose` → 200 + `{ok:true, rule:{...}, ai:null, configured:false}`。
3. 配置 Key 且网络可达 → `ai` 返回模型文本；错 Key/断网 → `ai:null` + `aiError`，仍 200。
4. `config.json` 不出现任何 ApiKey 明文；SecretStore 可读回 `AiKey_{id}`。
5. 前端 mock 模式（`file://` 打开 ui/index.html）"AI 诊断"正常展示规则 + 模拟 AI；live 模式接通真实端点。
6. 设置页可新增/编辑/删除/设为当前，预设下拉自动填充；`/api/state` 返回 `aiProviders[]` + `selectedAiProviderId`。
7. 旧单套配置（若在）首次启动迁移为默认 DeepSeek profile。

