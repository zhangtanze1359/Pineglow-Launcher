# v1.0.9 发版回执（最终）

- 执行人：构建与发布工程师
- 完成时间：2026-11-27 12:2x（香港时间 UTC+08）
- 冻结输入：`docs/evidence/2026-11-27-n09-r10/n09-start-download-r10-v109.patch`，23 文件，110,193 字节，SHA-256 `45AA130D825819DD077150BB1A656204A9DB58EF216D9494A064A10F86B233BD`
- 签字：独立复验 PASS（`HANDOFF-2026-11-27-n09-r10-independent.md`）+ 架构复审 PASS（`HANDOFF-2026-11-27-n09-r10-architecture.md`）；用户授权推进到完整发布

## 结论

**v1.0.9 已发布，对用户可见。** GitHub Release 与 Gitee Release 均有发布物，两处下载字节与内容哈希相同；仓库内 `release-notes/v1.0.9.md` 已随 Release 正文发布；Gitee 主更新清单 `update.json` 已指向 1.0.9 并核对一致。tag `v1.0.9` 已推送且可访问。

## 各步与指纹

| 步 | 结果 | 证据 |
|---|---|---|
| ① 合入 | 冻结补丁应用到 `49b371b`，`git apply` 退出 0，23 文件（20 改 + 3 新增）；工作区内容指纹与预演树逐字节相同（`4A51680A…869C60`） | `step2-gates.txt`、合并前对拍 |
| ② 提交 | `47aa97e17bd82d37fbf79984631a065f93d85772`，23 文件 +1404/−280；提交内容指纹 `4A51680A…869C60` | `step2-commit.txt` |
| ③ 版本断言与 tag | 程序集 `1.0.9`、exe `FileVersion 1.0.9.0`；tag `v1.0.9`（annotated，对象 `1b6cc314…`，指向 `47aa97e…`） | `step3-tag.txt` |
| ④ 推送与双源发布 | `main` 推送 `49b371b..47aa97e`；tag 推送成功；GitHub Actions `45932553973` verify+release 均 success；Gitee 附件本机补传成功 | `step4-push.txt` |
| ⑤ 清单核对 | GitHub `manifest.json` 与 Gitee `update.json` 四项一致；双源下载实测哈希一致 | `step5-manifest-check.txt`、`step5-download-hashes.txt` |
| ⑥ 回填 | 基线刷新提交 `f68e7c2`（main @ `47aa97e`，C# 1526 / 前端 522）；发布说明已随 Release 正文；发版回执提交 `f2bdae8` | `step6-backfill.txt`、`step2-gates.txt` |

## 发布物

- **GitHub Release**：`https://github.com/zhangtanze1359/Pineglow-Launcher/releases/tag/v1.0.9`（Release id 460142056，published 2026-11-27T03:46:11Z）
  - `Pineglow-Launcher-win-x64-single-file.exe` 53,495,265 字节，SHA-256 `FE5A20C2D46FF76AAAA140273D0FCEFBCA6A801C4E01D1C18DB59641FF7DDCE6`
  - `manifest.json` 4,296 字节；`LICENSE` 35,823 字节
- **Gitee Release**：`https://gitee.com/ztz1359/pineglow-launcher/releases/tag/v1.0.9`（release id 1374108）
  - 同一 exe 附件，直链 `https://gitee.com/ztz1359/pineglow-launcher/releases/download/v1.0.9/Pineglow-Launcher-win-x64-single-file.exe`，实测 53,495,265 字节、同哈希
- **更新清单（Gitee 主源）**：`https://gitee.com/ztz1359/pineglow-launcher/raw/master/update.json` → version `1.0.9`、size `53495265`、sha256 `fe5a20c2…7ddce6`、`fallbackUrl` 为 GitHub 同一附件
- **提交**：`https://github.com/zhangtanze1359/Pineglow-Launcher/commit/47aa97e17bd82d37fbf79984631a065f93d85772`
- **Actions**：`https://github.com/zhangtanze1359/Pineglow-Launcher/actions/runs/45932553973`

## 发布物与本地候选的差异（已解释）

- 本地候选（未注入构建期 CurseForge Key）：53,429,554 字节，SHA-256 `CB6FA0E0…E6CADC3`。
- CI 发布物：53,495,265 字节，SHA-256 `FE5A20C2…7DDCE6`，`ProductVersion 1.0.9+47aa97e…`。
- 差 65,711 字节，唯一原因是 CI 注入了 CurseForge Key（`AssemblyMetadata`）。证据：CI 日志 `curseforge-key=present / 退出码 0`；平台下载回读 `--print-curseforge-key-status` → `present`；本地无 Key 构建回读 `absent`；本机以假 Key 重建长度回到 53,429,554（不含真实 Key，故与 CI 仍不同）。

## 双源上传实况

- GitHub：工作流 `45932553973` 全绿；verify 内 C# **1526**、前端 **522**、零失败零跳过；`Validate update manifest against final artifact` 输出 `OK: manifest 与最终附件一致`。
- Gitee：CI 内附件上传 2 次 × 900 秒均超时（`curl (28) … 0 bytes received`），脚本按设计降级（manifest 保持 GitHub 直链、不阻断 GitHub 发布），并打出 `::error::[镜像失败]`。随后用仓库自带 `scripts/mirror-gitee.mjs` 在本机补传：下载 GitHub 发布物 → 复用 Gitee release `1374108` → 上传附件成功 → contents API 更新 `update.json`（PUT）。
- 结论：900 秒 ×2 在 CI runner 上仍不够（与 v1.0.8 同一跨国带宽瓶颈）。本机补传稳定可行。建议 v1.0.10 固化为"CI 建 release、境内/本机补传附件"。

## 发布后自动镜像的已知瑕疵

基线回填与回执两次推送触发了 `Mirror to Gitee`（run `45938488765`、`45939623856`）。两次都成功 force push 并成功恢复 `update.json`（`HTTP 201`，`version=1.0.9`），但最后一步 SHA 校验始终报 `远端 master: 1741c8f9… / 7c88f071… 与本地 HEAD 不一致`。该步设了 `continue-on-error: true`，job 结论仍为 success，实际分支推送已完成。此校验逻辑对 Gitee 侧的重写行为不成立，建议 v1.0.10 改为读取 Gitee API 的 master commit 后比对。**不影响用户下载与更新。**

## 末端复核受限

12:20 起本机到 `gitee.com` 的 TLS 握手全部失败（直连与 `127.0.0.1:7890` 代理均同），`github.com` 正常；无法再直读 Gitee。现有证据：12:07 双源下载与清单核对（镜像之前，`update.json` 已由本机补传更新）、12:12 与 12:23 两次镜像恢复 `HTTP 201`。

## 未由我完成

- **v1.0.8 实包 → v1.0.9 在线升级验收**：需真实实包与升级链路，按分工由测试与审查工程师随后执行。
- **Gitee 末端复核**：受本机出网限制，需从可访问 Gitee 的网络路径复读 `update.json` 与附件（已列为待办）。

## 已知问题（已写入 Release 正文）

- 启动响应丢失但游戏实际拉起时，该次退出不提示。
- 架构复审登记的非阻断残余：状态接口分读毫秒级竞态，退出提示可延至下次启动补达（事件不丢档），进 v1.0.10。
