# OPEN-08 授权证明（脱敏存档）

## 原图

编排方于 2026-11-24 提供了一张 CurseForge 官方邮件的截图。
原图含**完整 API Key**，因此**不存入本仓库**，仅在本文件记录其结论。

## 从原图读取到的事实

| 项 | 内容 |
| --- | --- |
| 发件人 | `Amichai Marmor <api@curseforge.com>`，由 `dana.feldman@overwolf.com` 代发 |
| 收件人 | `1690536468@qq.com`（邮件正文称呼 **Hi Zhang Tanze**） |
| 日期 | 2026 年 7 月 15 日 22:24 |
| 主题 | **Your CurseForge API Application - Approved** |
| 授权正文 | *"We're pleased to inform you that we've approved your application **Pineglow Launcher** for the 3rd Party CurseForge API."* |
| 随邮件发放 | **personal API Key**，形如 `$2a$10$G3lI…NWWjOCrG`（60 字符） |

## 与仓库中那把 Key 的关系

把原图的 Key 与 `v1.0.6` tag 中 `CurseForgeKeyDefaults.cs` 的 `Embedded` 常量比对：

| 项 | 值 |
| --- | --- |
| 长度 | 均 60 |
| 前缀 | 均 `$2a$10$G3lI` |
| 后缀 | 均 `NWWjOCrG` |

**结论：同一把 Key。** 它是 CurseForge 官方按「Pineglow Launcher」这个第三方应用审批后发放给持有人 Zhang Tanze 的。

## 这补上了验收项 1 的哪一半

| 验收项 1 要求 | 状态 |
| --- | --- |
| **持有人身份** | ✅ 邮件收件人及其称呼 |
| **授权来源** | ✅ CurseForge 官方（overwolf.com 代发） |
| **授权事项** | ✅ 明确写明 "Pineglow Launcher" 的 3rd Party API |
| **当前有效性** | ✅ 见下方「实际调用验证」 |

## 实际调用验证（2026-11-24 实测）

仅凭邮件不能证明 Key **现在仍有效**，所以另外跑了一次真实调用：

[`raw/curseforge-live.txt`](raw/curseforge-live.txt)

| 步骤 | 结果 |
| --- | --- |
| `GET /mods/search` | **HTTP 200**，返回 3 条 |
| `GET /mods/238222/files` | **HTTP 200**（JEI） |
| `GET /mods/238222/files/9773637/download-url` | **HTTP 200**，`edge.forgecdn.net` |
| 实际下载 jar | **HTTP 200**，7,103,608 bytes，SHA-256 `0614F0A6A6EF9D7E0A313B1D71561A561E8AF4936E557710034507CFC356908F` |

**这证明 Key 当前有效，且 CurseForge 下载功能可用**——验收项 1 的后半段要求。

## 安全处理

1. **原截图未入仓库**，本文件只记录结论。
2. 所有验证脚本从**临时纯文本文件**读 Key，跑完立即删除；**不经 shell 环境变量**
   （实测 Git Bash 会把 `$2a$10$...` 当变量展开，60 字符破坏成 2 字符，导致 403）。
3. 落盘日志经 `scrub()` 处理，**已实测不含完整 Key 形态**。
4. 印证：[`verify-curseforge-live.py`](verify-curseforge-live.py) 里 `scrub()` 的实现。

## 与「不轮换」决定的关系

编排方 2026-11-24 判定：**Key 未泄露，不轮换。**

本文件记录的授权事实不改变该决定。需要重申的**剩余风险**：

- 该 Key 仍存在于 Git 历史的 **82 个快照**（复核方口径）与已发布的 **v1.0.6 tag + Release 附件**中。
- 因此「未泄露」的准确含义是**「未观察到未经授权使用的证据」**，不是「无法被提取」。
- 若日后要使其失效，只能由持有人到 CurseForge 后台吊销/轮换——**历史重写做不到这一点**。
