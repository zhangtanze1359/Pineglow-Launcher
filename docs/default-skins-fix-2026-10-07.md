# 默认皮肤显示修复记录

日期：2026-10-07，Asia/Hong_Kong。

## 范围与输入

- 工作目录：`C:\Users\Administrator\.codex\worktrees\8450\松光启动器`。
- 基线 HEAD：`f145d14eb26b0b7459a28491e764ac8c573085d3`，开始时工作区干净。
- 用户目标：默认显示 Minecraft 官方的 Steve、Alex，修复当前错误外观。
- 当前交付是本工作树中的 UI 源码及 Release 构建，不是安装版更新。
- 未修改 C# 服务、用户账号配置或游戏目录。卡片点击沿用预览行为，不会上传或变更游戏账号皮肤。

## 根因与修复

原 UI 在无皮肤 URL 时绘制手工 SVG；mock 的“经典 Alex”只有色块。贴图预览还将左右肢体镜像处理，缺少细臂和现代皮肤外层。

从本机 Minecraft Java Edition 1.21.5 原版客户端提取宽臂 Steve、细臂 Alex 的 PNG。客户端 SHA-1 与其本地版本清单一致：`b88808bbb3da8d9f453694b5d8f74a3396f1a533`。官方来源地址和两张 PNG 的 SHA-256 见 `ui/skins/README.md`。官方站点下载的 TLS 失败保留在本轮工具输出中；此次来源验证使用本机客户端与本地清单，没有重新联网校验清单。

- 无账号或无自定义皮肤时用本地 Steve；皮肤库提供 Steve、Alex 两张默认卡片。
- mock 默认 Alex 使用真实官方贴图，替换配色占位。
- 宽臂为 4 像素、细臂为 3 像素；64×64 皮肤读取独立左右肢体及头发、袖子、外套等外层。
- 64×32 旧皮肤按原始比例裁剪并镜像左肢体。
- 账号头像使用相同贴图；自定义贴图保持优先，加载失败时回退到对应模型的本地默认贴图。
- UI 的账号映射保留 `model`/`variant` 元数据；现有 C# 服务只提供 `SkinUrl` 时仍按经典模型预览。本任务没有扩展后端模型元数据。
- 默认模型固定供启动器预览使用；未实现现代 Minecraft 按 UUID 选择全部九种默认角色的逻辑。

## 验证结果

| 检查 | 结果 | 证据 |
| --- | --- | --- |
| 原缺陷 RED | FAIL，发现 SVG 占位且没有官方贴图 | `red.txt`，首轮 5 项检查失败 2、错误 3；独立聚焦复现也报 `0 != 1` |
| 无皮肤默认与卡片切换 | PASS | `test_no_skin_uses_official_steve_and_offers_alex` |
| 无账号默认 | PASS | `test_no_account_still_displays_steve` |
| mock Alex 与头像 | PASS | `test_mock_alex_has_texture_and_matches_sidebar` |
| 自定义 URL 与细臂模型 | PASS | `test_custom_skin_keeps_own_url_and_slim_model` |
| 贴图失败回退 | PASS | `test_missing_custom_texture_falls_back_to_official_skin` |
| 官方贴图逐像素对照 | PASS，分别比较两种模型的 300 个以上不透明像素中心 | `test_front_pixels_match_official_atlas_for_both_models` |
| 64×32 兼容 | PASS | `test_legacy_64_by_32_skin_keeps_scale_and_mirrors_left_limbs` |
| HTTP 前端映射 | PASS，拦截提供隔离 API 数据，运行真实 `api.js` | `test_http_account_mapping_preserves_variant_and_defaults` |
| JS 语法与 diff | PASS | 三份 JS 的 `node --check`；`git diff --check` |
| Release 构建 | PASS，退出码 0，137 条警告、0 个错误 | `dotnet build dev/PineglowLauncher/Pineglow.Launcher.csproj -c Release --nologo`；`build.txt` |
| 资源打包 | PASS | 输出 `UI/skins` 中两张 PNG 的 SHA-256 与源文件一致 |

浏览器最终命令：`python scripts/verify-default-skins.py`，执行 8 项，全部通过，无跳过。测试依赖 Python Playwright、Chromium 和 Pillow，不启动真实账号服务，不改用户数据。

中间增加旧皮肤检查时，出现一次 CSS 序列化断言错误：Chromium 把 `560px auto` 规范化为 `560px`。这是测试预期错误，已校正断言；保留原始 `green.txt`，最终结果另存 `green-final.txt`。

本轮原始证据目录：`C:\Users\Administrator\AppData\Local\Temp\pineglow-default-skins-20261007-8450`。内含 `red.txt`、`green.txt`、`green-final.txt`、`build.txt`、`source-manifest.json`、`steve.png`、`alex.png`。该目录是临时目录，系统清理可能删除证据；回归脚本及来源说明保留在项目中，可重新运行。

## 结论与边界

当前默认皮肤 UI 修复的浏览器检查及资源构建验证通过。HTTP 检查使用隔离响应；未验证真实 Microsoft/第三方皮肤服务、原生 WebView2 或游戏内皮肤。没有替换已安装程序，没有提交、推送、发布。`FormalPhaseApproval=NOT_GRANTED`。

## 交付自评

按 agent-self-evaluation 技能对本轮限定范围进行自评，不作为独立验收或发布批准。

| 维度 | 分数 | 依据 |
| --- | --- | --- |
| 准确性 | 4/5 | 官方客户端贴图有本地清单哈希及渲染像素证据；未重新在线验证清单 |
| 完整性 | 4/5 | 8 项浏览器行为及构建通过；原生宿主和安装版更新未验证 |
| 清晰度 | 4/5 | 记录来源、命令和证据边界；详细记录比日常使用说明长 |
| 可操作性 | 4/5 | 源码、可重跑脚本与构建产物可用；用户正在运行的旧安装版尚未更新 |
| 简洁度 | 4/5 | 本文件保留追溯细节；最终答复仅保留关键变化与验证结果 |

平均 4.0/5。当前工作树的修复可以交付；安装版更新和原生宿主复验属于后续交付环境工作。自检：用户要求显示正确的 Steve/Alex，源码和浏览器预览已满足；不能据此声称旧安装版已经更新。
