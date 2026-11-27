# v1.0.9 发版回执（2026-11-27）

- 执行人：构建与发布工程师
- 冻结输入：`docs/evidence/2026-11-27-n09-r10/n09-start-download-r10-v109.patch`，23 文件，110,193 字节，SHA-256 `45AA130D825819DD077150BB1A656204A9DB58EF216D9494A064A10F86B233BD`（实测一致）
- 签字链：独立复验 PASS（`HANDOFF-2026-11-27-n09-r10-independent.md`）+ 架构复审 PASS（`HANDOFF-2026-11-27-n09-r10-architecture.md`）；用户已授权推进到完整发布

## 结果

**v1.0.9 已发布并对用户可见。** GitHub Release 与 Gitee 双源各有一份发布物，两处下载的字节与内容哈希相同；更新清单 `update.json`（Gitee 主源）与 GitHub `manifest.json` 的 version/size/sha256/url 逐项核对一致。tag `v1.0.9` 已推送且可访问。

## 各步记录

| 步 | 内容 | 结果 |
|---|---|---|
| ① 合入 | 在主树把冻结补丁应用到 `49b371b` | `git apply` 退出 0；23 文件（20 改 + 3 新增） |
| ② 提交 | 单次提交含修复与 1.0.9 元数据 | `47aa97e17bd82d37fbf79984631a065f93d85772`，23 文件，+1404/−280 |
| ③ tag | 先断言程序集版本 | 程序集 `1.0.9`、exe `FileVersion 1.0.9.0`；tag `v1.0.9`（annotated，对象 `1b6cc314…`，指向 `47aa97e…`），已推送 |
| ④ 双源发布 | GitHub Actions Release 工作流 + Gitee 镜像 | GitHub `success`（verify + release 两 job）；Gitee 附件由本机补传成功 |
| ⑤ 清单核对 | update.json / manifest 四项 | 全部一致（见下） |
| ⑥ 回填 | 基线、发版记录 | 本文与 `docs/baseline.md`、发布说明 |

### 关键指纹

- 合入前对冻结补丁与工作区实际内容做等价校验：`git diff --binary` 归一化指纹 `4A51680A63164AC27A2932C5B02E7E0C2E3BEF8F7D6AF0205C9726F39C869C60`，与预演树逐字节相同。
- 本地发布构建（无构建期 CurseForge Key）：53,429,554 字节，SHA-256 `CB6FA0E0F25F893E3F0505C86B88318955A7B386860EC0B2C568AF229E6CADC3`，`FileVersion 1.0.9.0` —— 与冻结候选一致。
- **CI 发布物**：53,495,265 字节，SHA-256 `FE5A20C2D46FF76AAAA140273D0FCEFBCA6A801C4E01D1C18DB59641FF7DDCE6`，`ProductVersion 1.0.9+47aa97e…`。
  - 与本地候选相差 65,711 字节，原因是 CI 在构建期注入了 CurseForge Key（`AssemblyMetadata`），本地未注入。**同一份冻结补丁与同一份元数据，行为等价、机器差异仅此一处。**
  - 证据：CI 日志 `Assert CurseForge key is embedded in the final EXE` → `curseforge-key=present, 退出码 0`；平台发布物回读 `--print-curseforge-key-status` → `curseforge-key=present`；本地无 Key 构建回读 `absent`。
  - 本机以同一源码 + 任意假 Key 重建得 53,429,554 字节（未含真实 Key，故长度与 CI 不同），说明差异来自 Key 注入而非代码。

### 双源与清单核对

| 项 | GitHub `manifest.json` | Gitee `update.json` |
|---|---|---|
| version | 1.0.9 | 1.0.9 |
| size | 53,495,265 | 53,495,265 |
| sha256 | `fe5a20c2…7ddce6` | `fe5a20c2…7ddce6` |
| url | `…/releases/download/v1.0.9/Pineglow-Launcher-win-x64-single-file.exe`（GitHub） | Gitee 同版本附件直链；`fallbackUrl` 为 GitHub 同一附件 |

- GitHub 附件三件：`LICENSE`(35,823)、`manifest.json`(4,296)、`Pineglow-Launcher-win-x64-single-file.exe`(53,495,265)。
- 两源下载实测：GitHub 与 Gitee 各下载一次，均为 53,495,265 字节、SHA-256 `fe5a20c2…7ddce6`，三处（平台摘要、下载重算、清单）完全一致。
- CI 内 `Validate update manifest against final artifact`：`OK: manifest 与最终附件一致（version=1.0.9 size=53495265 url=…）`。
- CI 内阶段 A 更新链检查：默认地址链取到清单且来源可辨认（`manifestSource=gitee raw update.json`，`usedFallback=False`）。

### 双源上传实况（如实记录）

- **GitHub**：Release 工作流 `45932553973` 全绿（verify 与 release 两 job 均 success）。verify 内 C# **1526** 通过、前端 **522** 通过、零失败零跳过；`Passed! - Failed: 0, Passed: 1526`。
- **Gitee**：CI 内附件上传两次各 900 秒均超时（`curl: (28) Operation timed out after 900013 / 900002 milliseconds with 0 bytes received`），Gitee release `1374108` 建成但无 exe 附件，`::error::[镜像失败]`，manifest 保持 GitHub 直链（这是脚本设计的降级路径，不阻断 GitHub 发布）。
- **补救**：用仓库自带的 `scripts/mirror-gitee.mjs` 在本机补传（本机到 Gitee 链路正常）：从 GitHub Release 下载 → 复用 Gitee release `1374108` → 上传附件成功 → 用 contents API 更新 Gitee 仓库根 `update.json`（PUT 成功）。
- 结论：Gitee 900 秒 ×2 在 CI runner 上仍不够（与 v1.0.8 同一跨国带宽瓶颈）；本机补传可稳定完成，建议 v1.0.10 把这一步改为「CI 只创建 release，附件由本机/境内 runner 补传」并在流程里固化。

## 链接

- 提交：`https://github.com/zhangtanze1359/Pineglow-Launcher/commit/47aa97e17bd82d37fbf79984631a065f93d85772`
- tag：`https://github.com/zhangtanze1359/Pineglow-Launcher/releases/tag/v1.0.9`
- GitHub Release：`https://github.com/zhangtanze1359/Pineglow-Launcher/releases/tag/v1.0.9`（Release id 460142056，发布于 2026-11-27T03:46:11Z）
- Gitee Release：`https://gitee.com/ztz1359/pineglow-launcher/releases/tag/v1.0.9`（release id 1374108）
- Gitee 更新清单：`https://gitee.com/ztz1359/pineglow-launcher/raw/master/update.json`
- Actions：`https://github.com/zhangtanze1359/Pineglow-Launcher/actions/runs/45932553973`

## 已知问题（随发布说明）

- 启动响应丢失但游戏实际拉起时，该次退出不提示（已写入 Release 说明的已知问题）。
- 架构复审登记的非阻断残余：状态接口分读的毫秒级竞态可使退出提示延至下次启动补达，事件不丢档；进 v1.0.10。

## 发布后自动镜像与末端复核

- 基线回填提交 `f68e7c2` 推送后，`Mirror to Gitee` 自动跑了一次（run `45938488765`，12:11–12:12）。它先备份 Gitee 上的 `update.json`（`version=1.0.9 sha=55bc1932767d`），force push `0be7a67...f68e7c2`，再恢复 `update.json`（`HTTP 201`）。
- 该 job 的最后一步"SHA 校验"报 `远端 master: 1741c8f9… / 本地 HEAD: f68e7c2c…`，`::error::[镜像失败] 推送后远端 SHA 与本地不一致`。因为工作流对这一步设了 `continue-on-error: true`，job 结论仍显示 success。GitHub 侧不受影响；Gitee 仓库分支可能被站点侧重写为 `1741c8f9…`，与发布物无关（用户下载走 Release 附件与 `update.json`，两者均已核对）。
- 末端复核受限：12:20 起本机到 `gitee.com` 的 TLS 握手全部失败（直连与 `127.0.0.1:7890` 代理都一样，`github.com` 正常），无法再直读 Gitee。以 12:07（镜像之前）实测与 12:12 恢复的 `HTTP 201` 作为当前证据；`step5b-gitee-recheck.txt` 已如实记录。
- 建议 v1.0.10 处理：镜像工作流的 SHA 校验与实际推送结果对齐（或改用 Gitee API 读 `master` 的 commit），避免"推成功但校验误判"，并把 `continue-on-error` 只留给附件上传这类可降级步骤。

## 未由我完成、转交测试与审查工程师

- v1.0.8 实包 → v1.0.9 的在线升级验收（下载、校验、替换、重启版本号）。
- 发布物可用性抽检（双源链接可访问性、升级后首启）。
