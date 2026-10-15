# 选中皮肤一键应用验证记录

日期：2026-10-07，Asia/Hong_Kong。工作目录：`C:\Users\Administrator\.codex\worktrees\8450\松光启动器`。

输入 HEAD：`f145d14eb26b0b7459a28491e764ac8c573085d3`。起点包含上轮默认皮肤 UI 的未提交修改；保留这些修改，未重置、提交、推送或发布。计划见 `skin-apply-plan-2026-10-07.md`。

## 交互与真实支持范围

- 点击卡片只更换预览、名称、模型和选中边框，不写账号。
- 预览下方独立“使用此皮肤”按钮，只有点击该按钮才执行应用。
- 当前实现通过既有 `/api/accounts/skin` 为 Microsoft 正版账号上传所选 PNG；Alex 使用 slim，Steve 使用 classic。
- 正在应用时禁用应用按钮、卡片和文件上传按钮。失败保留原账号外观与所选预览，并允许重试。
- 成功使用服务器返回的账号摘要更新缓存、侧栏头像与当前皮肤；同 URL、同模型的当前皮肤显示“正在使用”。
- 后端 `skinModel` 从官方 ACTIVE 皮肤 variant 获取并保存在账号中，贯穿登录、上传、账号摘要及 JSON 恢复。旧账号缺少该字段时默认 classic。
- 无账号时禁用按钮。离线账号提示需要额外的皮肤模组；第三方账号提示到对应皮肤站应用。没有实现离线游戏皮肤注入或第三方站点的上传接口。
- 文件上传沿用已有直接上传行为；这次分离的是卡片选择预览与应用。
- mock 分支仅更新内存，不能证明真实游戏账号已换肤。

## 要求与证据

| Requirement | Evidence |
| --- | --- |
| “用户点击皮肤后先是预览皮肤” | `test_card_only_previews_until_independent_apply_button`：选中 Alex 后无 POST，原账号 URL 和头像保持 Steve，只有预览及选中边框改变 |
| “再独立做个按钮，用户点击按钮后即可使用皮肤” | `test_apply_alex_posts_exact_png_and_slim_then_refreshes_avatar_and_reload`、`test_apply_steve_uses_classic_model`：仅按钮触发，PNG 字节与本地原版贴图一致，参数分别 slim/classic，成功后头像更新 |
| 应用状态与重复点击 | `test_duplicate_click_submits_once_and_locks_cards`：连续点击只上传一次，执行中卡片禁用 |
| 失败与重试 | `test_failure_preserves_skin_and_preview_then_allows_retry`、`test_http_200_rejection_is_not_reported_as_success`：500 及 200/ok=false 均不显示成功，不改原皮肤 |
| 能力边界 | `test_no_account_cannot_apply`、`test_unsupported_account_types_cannot_apply`、`test_mock_application_updates_only_memory` |
| 官方模型解析 | `GetAppearance_ActiveTextureKeepsModel`，SLIM、CLASSIC、缺 variant 三组 |
| 实际上传接口格式及模型恢复 | `UploadSkin_SelectedModelSentAndSavedThroughAccount`：FakeHandler 捕获 multipart variant，两组；经过 AccountService 和账号 JSON 往返后模型保持 |
| 旧配置兼容 | `LegacyAccountWithoutSkinModel_DefaultsClassic` |

后端 RED：5 项新检查因没有 skinModel 失败；已有 6 项通过。浏览器 RED：原页面缺少 `#btnApplySkin`。均保留原始日志。

最终执行：

```powershell
python scripts/verify-apply-skin.py
# 9/9 PASS
python scripts/verify-default-skins.py
# 8/8 PASS
dotnet test dev/PineglowLauncher.Tests/Pineglow.Launcher.Tests.csproj -c Release --nologo
# 98/98 PASS; failed=0, skipped=0
dotnet build dev/PineglowLauncher/Pineglow.Launcher.csproj -c Release --nologo
# exit=0, errors=0; incremental build warnings=0
node --check ui/js/launcher.js
node --check ui/js/api.js
node --check ui/js/i18n.js
git diff --check
# all exit=0
```

后端首轮编译仍输出项目已有的可空注释等警告；最终增量构建 0 警告不代表修复了这些历史警告。

原始证据：`C:\Users\Administrator\AppData\Local\Temp\pineglow-apply-skin-20261007-8450`，含 `red-browser.txt`、`red-backend.txt`、`backend-round1.txt`、`green-browser-final.txt`、`default-regression.txt`、`full-tests-final.txt`、`build-final.txt`、`source-manifest.json`、`preview-with-apply-button.png`、`applied.png`。临时目录可能被系统清理；测试脚本和本记录保留在项目中。

## 证据边界与停止点

浏览器运行真实 UI/前端接口代码，但用隔离 HTTP 响应代替 LocalApiService 和外部皮肤服务。C# 测试实际调用外观解析、multipart 上传代码及账号模型，HTTP 由 FakeHandler 提供。没有真实 Microsoft 账号 mutation，没有启动游戏，没有原生 WebView2 复验，没有替换已安装程序。

当前范围：功能实现及隔离自动化验证通过。真实账号端到端换肤效果未验证，不将 mock/单测结论扩大为线上服务或发布验收。`FormalPhaseApproval=NOT_GRANTED`。

## 自评与改进

使用 agent-self-evaluation 技能核对交付：准确性 4/5（无真实服务换肤证据）；完整性 4/5（Microsoft 支持已实现，其余账号明确显示能力边界）；清晰度 4/5（预览/应用状态区分，记录较详细）；可操作性 4/5（源代码和测试可用，安装版未更新）；简洁度 4/5（最终答复保留主要信息）。平均 4.0/5。当前源码可交付；后续针对真实账号与安装版的检查必须记录真实环境证据。自检：用户应能看到独立按钮和可验证的应用流程，也应知道哪些账号目前支持。
