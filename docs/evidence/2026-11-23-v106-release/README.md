# v1.0.6 发布与发布后验证（2026-11-23）

**执行者**：松光启动器
**`FormalPhaseApproval=NOT_GRANTED`**（本条记录事实，不构成发布批准）

## 发布结果

| 项 | 值 |
| --- | --- |
| Tag | `v1.0.6`（annotated，`86fb341` → 提交 `4b176b7`） |
| Release | **https://github.com/zhangtanze1359/Pineglow-Launcher/releases/tag/v1.0.6** |
| 发布时间 | 2026-11-23 11:23:51 UTC（19:23 UTC+08） |
| Workflow run | `45163205757` — **completed success**，耗时约 8 分钟 |
| 附件 | `Pineglow-Launcher-win-x64-single-file.exe`（51,750,369 bytes）、`manifest.json`、`LICENSE` |

## CI 执行了什么

`release.yml` 在 tag 推送时触发，两个作业：

**verify**（Windows runner）：锁定还原 ×2 → UI 资源闭包 → 测试清单双向校验 → 前端全量 → **C# 全量 1502 条**。
任一失败则 release 作业不启动。

**release**：按 tag 号构建 win-x64 单文件 → 断言只有一个 EXE → 校验程序集版本 == tag →
生成 manifest → **冷启动验证**（复制到全新中文/空格目录再启动）→ **真实更新链验证**（联网查默认源）→
可选 Gitee 镜像 → 创建 Release。

## 发布的 manifest（下载后实测）

```text
version       = 1.0.6
url           = https://github.com/zhangtanze1359/Pineglow-Launcher/releases/download/v1.0.6/Pineglow-Launcher-win-x64-single-file.exe
fallbackUrl   = null
size          = 51750369
sha256        = 43750657a75145cbc2f77a37b281c9e514a35cc16a6db79e9b703bac1c6361cd
mandatory     = false
notes 长度    = 1540
```

`releases/latest/download/manifest.json` 端点也正确返回 v1.0.6。

## 下载校验

用 `curl --retry 3` 完整下载后核对：

| 项 | manifest 声明 | 实测 | 一致 |
| --- | --- | --- | :---: |
| 大小 | 51,750,369 | 51,750,369 | ✅ |
| SHA-256 | `43750657…61CD` | `43750657…61CD` | ✅ |

> 首次用 `gh release download` 产生 `unexpected EOF`（35,850,560 bytes 截断），
> `curl --retry 3` 重下后完整。**这是下载工具的问题，不是附件问题**——第二次逐字节吻合。

## 发布产物自更新验证（同版本）

用刚发布的 v1.0.6 exe 在便携沙盒里跑完整链路，**指向真实 GitHub 源**（不设 `updateUrl`，走产品默认值）：

| # | 环节 | 实测 |
| --- | --- | --- |
| 1 | 启动 | `=== Pineglow Launcher 启动 v1.0.6 ===` |
| 2 | 检查更新 | `current=1.0.6 latest=1.0.6`，`source=.../releases/latest/download/manifest.json`，`fallback=True` |
| 3 | Gitee 主源 | `HTTP 404（清单不存在）` —— 降级到 GitHub ✅ |
| 4 | 下载 | 真实拉取 51,750,369 bytes，耗时约 90 s |
| 5 | 应用 | `state=applying` |
| 6 | **替换** | 目标 SHA256 → `43750657…61CD`，**与发布产物逐字节一致** |
| 7 | 备份 | `.bak-1.0.6` 存在 |
| 8 | 状态 | `{"result":"applied","version":"1.0.6"}` |
| 9 | **自动重启** | 日志出现第二条 `启动 v1.0.6`（19:35:05，替换后 2 s） |

**第 9 条这次是真的验证到了**。上一轮（v1.0.5 演练）我承认「自动重启未验证」，因为当时的证据（WebView2 目录时间戳）早于替换。这次有两条独立证据：

1. 产品日志里第二条 `启动 v1.0.6`
2. 清理时发现 **PID 31748** 的 `Pineglow.Launcher` 进程 —— **我的脚本从未启动过这个 PID**，那是替换脚本 `start "" "%TARGET%"` 拉起的

## 这一轮证明了什么、没证明什么

**证明了**：

- **发布流程端到端成功**：tag → CI 全量测试 → 构建 → 冷启动验证 → Release
- **发布产物可下载且校验一致**（大小 + SHA256 双吻合）
- **v1.0.6 的自更新机制在真实发布产物上完整工作**：检查 → 下载 → 校验 → 替换 → 备份 → **自动重启**
- **Gitee 主源 404 时正确降级到 GitHub**，且发布流程不因此阻断

**没证明**：

| 项 | 原因 |
| --- | --- |
| **v1.0.5 → v1.0.6 跨版本升级** | 本次是 v1.0.6 升 v1.0.6（`latest=1.0.6` = `current=1.0.6`，`updateAvailable=False`），我直接调 `download`/`apply` 绕过了版本比较。**真跨版本升级需要一个版本号不同的已发布包** |
| 真实用户环境的网络/代理/防火墙 | 本次是直连 GitHub |
| 从 Gitee 主源升级 | 主源仍 404 |
| macOS / Linux | 仅 win-x64 交付 |

## 已知遗留

1. **Gitee 主源 404 未修复**。产品每次检查更新都要先吃一次 404 再降级，多一次往返。这是基础设施问题，不是本版缺陷。
2. **跨版本升级路径仍未端到端验证**。要验证它，需要一次真实的新版本发布——即下一次发版时才有机会。
3. 上一轮实包验证（`docs/evidence/2026-11-23-open03-e2e/`）的结论**仍然成立且未被本轮推翻**：产品的 HTTPS 强制是正确行为。

## 产物

| 文件 | 内容 |
| --- | --- |
| `README.md` | 本文件 |
| `raw/postrelease-verify.txt` | 发布后验证完整输出 |
| `raw/product-launcher.log` | **产品自己的日志原文**（含两次启动记录） |
| `verify-postrelease.ps1` | 验证脚本 |

## 清理

- 临时下载目录 `D:/Projects/_tmp_v106` 已删
- 沙盒 `D:/Projects/_worktrees/pg-v106-postrelease-20261123` 已删
- 残留进程（`Pineglow.Launcher` PID 31748、`msedgewebview2` 31 个）已终止
- 端口 48123 已释放
