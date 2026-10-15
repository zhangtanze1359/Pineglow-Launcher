# 资产级回退真实验收：PASS（第 35 轮）—— R03 客户端侧最后一条没被证明的链接

日期：2026-10-06（Asia/Hong_Kong）。本轮**未改动任何 tracked 文件**。
工具：`verify-asset-fallback-live.ps1`（本目录，可复跑）。结论：**`资产级回退真实验收判定: PASS`**，
19 项断言 0 失败，用时 100 秒，退出码 0。

## 1. 为什么还缺这一条

已有的真实网络验收（`scripts/verify-update-live-download.ps1`）证明了**清单源级**降级
（Gitee 固定 `update.json` 404 → GitHub 备用清单）与真实下载/哈希/篡改拒绝。
但它的夹具把清单里的 `fallbackUrl` 写成**空串**（`scripts/verify-update-live-download.ps1:144`），
所以下面这条路径**从未被任何测试跑过**：

> 清单里的 `url`（主资产）坏了 → 靠 `fallbackUrl`（备用资产）救回来。

而候选发布的清单恰恰是 **`url` = Gitee 附件、`fallbackUrl` = GitHub 直链**
（`release.yml:324-333` 重写 manifest）——**主源挂掉时用户能不能下载，全靠这一条**。

## 2. 设计：三个对照 + 一个反证

每个对照都用**全新冷启动**（独立 `data/config.json`），互不影响：

| 用例 | 清单里的 `url` | 清单里的 `fallbackUrl` | 期望 | 实测 |
| --- | --- | --- | --- | --- |
| **A 主源可用** | 真实 **Gitee 附件**（匿名）| 空 | 完成、字节与 SHA256 一致 | ✅ `completed`，43,989,943 B，`6130db19…` |
| **B 资产级回退** | **真实 404 坏链** | 真实 **GitHub 资产** | 完成、字节与 SHA256 一致 | ✅ `completed`，43,989,943 B，`6130db19…` |
| **C 反证** | 真实 404 坏链 | **同一个坏链** | **必须失败** | ✅ `failed`，错误里**逐条列出两个 URL** |

C 的存在是为了排除"B 的成功其实是因为客户端根本没读 `url`"这种解释。C 的错误文本是：

```
更新包下载失败：https://gitee.com/.../does-not-exist.exe - 404 (Not Found).；
               https://gitee.com/.../does-not-exist.exe - 404 (Not Found).
```

——**两个 URL 都被试过并按序失败**，这同时正面证明了顺序是 `url → fallbackUrl`。

### 前置断言（避免"用假坏链证明假结论"）

- 坏链实测 `status=404`（同域真实 404，不是拼错域名）；
- **Gitee 附件匿名可下**：200，43,989,943 B，SHA256 `6130db19…` —— 不需要 cookie/referer，
  说明"Gitee 作为主下载源"对匿名用户**确实可用**（evaluate 设计成立）；
- **GitHub 资产**同为 43,989,943 B、SHA256 `6130db19…` —— **两个源字节完全相同**（互为镜像的完整性）。

## 3. 这条证据把 R03 的客户端侧链补完了

| 环节 | 之前的证据 | 现在 |
| --- | --- | --- |
| 清单**源**级降级（Gitee 404 → GitHub 备用清单）| ✅ 真实端点（`run-20261006-0750-remote-truth/`）| 不变 |
| 清单**资产**级回退（`url` 坏 → `fallbackUrl`）| ❌ 从未跑过（夹具写空）| ✅ **本轮真实端点 PASS** |
| 真实 HTTPS 下载 + 字节/SHA256 | ✅ 真实发布资产（同上）| 不变，且在 A/B 两个源上都成立 |
| 篡改必须被拒 | ✅（同上）| 不变 |
| **主源（Gitee）本身可匿名下载** | ⚠️ 只验过备用源 GitHub | ✅ **本轮补上（A 用例）** |
| 真实应用替换自己并重启 | ❌ 仍未验（需真实发布一次）| 不变 |

## 4. 另一条顺带查清的事实：Gitee 侧**已有 v1.0.0 发布**

`GET https://gitee.com/api/v5/repos/ztz1359/pineglow-launcher/releases` 返回 1 条：

```
id=783393  tag=v1.0.0  target_commitish=236e4ab  created=2026-08-10T23:34:41+08:00
附件：Pineglow-Launcher-win-x64-single-file.exe  43989943 B
      https://gitee.com/ztz1359/pineglow-launcher/releases/download/v1.0.0/Pineglow-Launcher-win-x64-single-file.exe
```

⇒ ① 镜像步骤里 `$uploaded.browser_download_url` 的 URL 形式**已被真实发布物证明**；
② **Gitee 上有 release 附件，却没有 `update.json`** —— 这正是"旧客户端升不了级"的直接原因，
也说明候选新增的 `update.json` 写入步骤是**必要**的（不是锦上添花）。

### 4.1 顺手发现的小缺陷（低严重度，未改）

`release.yml` 把状态字段写成 `$status.updateJsonUrl = "$api/raw/master/update.json"`
（即 `https://gitee.com/api/v5/repos/.../raw/master/update.json`）。实测：

| URL 形式 | 现状 |
| --- | --- |
| 客户端默认地址（web 形式）`https://gitee.com/{owner}/{repo}/raw/master/update.json` | 文件不存在 ⇒ 404（写成功后应 200）|
| 状态字段里的 api 形式 `https://gitee.com/api/v5/repos/{owner}/{repo}/raw/master/update.json` | **401（需要 token）** |
| 对照：web 形式 `README.md` | 200，1,618 B（与 API contents 里的 size 一致）|

⇒ 状态文件里那个 `updateJsonUrl` 对**没有 token 的人**是打不开的（401），而客户端真正用的是 web 形式。
影响仅限"人去点状态文件里的链接"，不影响功能；一行字符串即可改。因为工作流字节已冻结，**未改**，
列为可选修项（与 §5 的 action 大版本一起做最划算）。

## 5. 边界（不证明什么）

1. **不证明**"真实应用替换自己并重启"（脚本刻意不调 `/api/update/apply`）；
2. **不证明** Gitee 的**写入**（需要 `GITEE_TOKEN` 与发布授权）；A 用例只证明"读+下载"可用；
3. 不证明 v1.1.0 的清单/资产（还不存在）；本轮的资产是 v1.0.0 的真实发布物；
4. 不证明打包/清单生成逻辑（那由 `release.yml` 与干跑覆盖）。

## 6. 复现命令

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File `
  docs\release-remediation-evidence-2026-10-05\run-20261006-0820-asset-fallback\verify-asset-fallback-live.ps1 `
  -ExePath %TEMP%\pineglow-keep\candidate-5009cf73d0801e83.exe `
  -WorkingAssetUrl https://github.com/zhangtanze1359/Pineglow-Launcher/releases/download/v1.0.0/Pineglow-Launcher-win-x64-single-file.exe `
  -PrimaryAssetUrl https://gitee.com/ztz1359/pineglow-launcher/releases/download/v1.0.0/Pineglow-Launcher-win-x64-single-file.exe `
  -BrokenAssetUrl  https://gitee.com/ztz1359/pineglow-launcher/releases/download/v1.0.0/does-not-exist.exe `
  -ExpectedSha256 6130db19bbcc8b4d6b606bc1825d824a99a19c72ef2ef7af3ac874515953a999 -ExpectedSize 43989943 `
  -WorkRoot %TEMP%\pg-asset-fallback -JsonPath <证据>\asset-fallback-real.json
# 期望：资产级回退真实验收判定: PASS（19 项 0 失败，退出码 0）
```

产物：`asset-fallback-real.json`（19 项断言 + 三个用例的 check/status 原文）、`asset-fallback-real.log`。
