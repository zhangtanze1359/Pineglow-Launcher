# 选中皮肤一键应用 Implementation Plan

**Goal:** 点击皮肤仅预览；点击独立“使用此皮肤”按钮才为当前 Microsoft 正版账号上传所选 PNG。

**Architecture:** 保留 `/api/accounts/skin` 的 base64 + slim 契约。新增前端 `SG.api.applySkin(accountId, skin)` 将选中贴图读取为 PNG base64 并调用既有接口；mock 仅在隔离内存中更新。后端增加可选 `skinModel` 字段，贯穿官方外观解析、登录、持久化和账号摘要，确保 Alex 不会在刷新后显示宽臂。

**Tech Stack:** 原生 JavaScript / HTML / CSS、.NET 10、xUnit、Python Playwright。

**Spec:** 用户本轮要求“点击皮肤后先是预览皮肤”，通过独立按钮“即可使用皮肤”。现有真实后端只支持 Microsoft 账号；离线/第三方账号按钮禁用并解释原因，不冒充已应用到游戏。

## Global Constraints

- 保留前一轮未提交的官方默认皮肤修复，不提交、推送、发布或改用户配置。
- 自动化使用隔离账号和 HTTP 响应，不对真实账号执行换肤。
- 上传失败不能改当前账号外观；并发点击最多产生一笔上传。
- 框架内记录技术通过与真实服务验证的区别；本任务不自动更新安装版。

## Task 1: 后端模型保持与前端接口

Files: `MinecraftAppearance.cs`、`MinecraftAccount.cs`、`LauncherAccount.cs`、`MicrosoftAuthService.cs`、`AccountService.cs`、`LocalApiService.cs`、`ui/js/api.js`、`MicrosoftAuthServiceTests.cs`。

Contract:

```javascript
// skin: { url: string, model: 'classic'|'slim' }; returns { ok: true, account }
await SG.api.applySkin(accountId, skin);
// existing mutation remains:
await SG.api.uploadSkin(accountId, pngBase64, skin.model === 'slim');
```

```csharp
// Additive persisted/API field. Old configuration uses classic by default.
public string SkinModel { get; set; } = "classic";
// Official ACTIVE texture variant SLIM -> slim, other/missing -> classic.
```

- [x] 测试官方 ACTIVE 皮肤模型优先、旧配置默认值、上传 multipart variant 及模型持久化。
- [x] 运行聚焦测试保留 RED；最小实现；重跑 GREEN。

Run: `dotnet test dev/PineglowLauncher.Tests/Pineglow.Launcher.Tests.csproj -c Release --filter FullyQualifiedName~MicrosoftAuthServiceTests`。

## Task 2: 预览与应用状态

Files: `ui/js/launcher.js`、`ui/js/i18n.js`、`ui/css/figma.css`、`scripts/verify-apply-skin.py`。

```javascript
let previewSkin = accountSkin(active);
let applying = false;
// card click updates previewSkin and the preview; does not call the API.
// apply snapshots active.id + previewSkin, disables controls, awaits applySkin.
// successful account response updates ACCOUNTS, STATE.accounts, sidebar and view.
// failure restores controls, keeps the selected preview and old account skin.
```

- [x] 浏览器 RED：按钮不存在；GREEN 验证点击卡片不调用上传。
- [x] 添加独立按钮、状态与双语说明；无账号/离线/外置账号明确禁用。
- [x] 验证 Alex slim、Steve classic、重复点击、失败重试、重新加载后的模型与头像。
- [x] 跑前一轮默认皮肤回归及 Release 构建；保存截图和原始日志。

Run: `python scripts/verify-apply-skin.py`，`python scripts/verify-default-skins.py`，`dotnet build dev/PineglowLauncher/Pineglow.Launcher.csproj -c Release`。

Stop: 当前功能和隔离验证完成后交付；真实服务/原生 WebView 证据未获得时明确列出，不自动批准发布。
