# OPEN-07 销项记录 · Gitee 更新主源修复（2026-11-24）

## 结论

**OPEN-07 已修复并通过产品链路验证。** 主源 `https://gitee.com/ztz1359/pineglow-launcher/raw/master/update.json`
从 **HTTP 404** 变为 **HTTP 200**，产品经默认链成功取到清单且**未走备用源**。

## 根因：不是「没做」，是 token 早已失效

之前的判断是「主源 404，可能是没配 token / 没做镜像」。**实际原因更具体**：

| 证据 | 内容 |
| --- | --- |
| CI artifact `mirror-status.json`（v1.0.6 发布） | `"tokenPresent": true`、`"attachmentUploaded": false`、`"updateJsonUpdated": false` |
| Gitee release 列表 | **只有 v1.0.0**（`id=783393`）—— v1.0.4/1.0.5/1.0.6 全都没创建成功 |
| 旧 token 身份测试 | 无法创建 release |

**`tokenPresent: true` 但什么都做不成 → token 存在但无效**（过期或权限不足）。

而 `release.yml` 的镜像步骤标了 **`continue-on-error: true`**，所以工作流**每次都显示 success**。
这条链路坏了至少三个月，直到有人主动去看 artifact 才暴露。

## 修复动作

| # | 动作 | 结果 |
| --- | --- | --- |
| 1 | 编排方重新生成 Gitee 私人令牌（带 `projects` 权限），写入 `secrets.GITEE_TOKEN` | ✅ `gh secret list` 显示 `2026-11-24T02:51:13Z` |
| 2 | 用新 token 创建 Gitee release `v1.0.6` | ✅ `id=1360866` |
| 3 | 上传附件（51,750,369 bytes）到 Gitee release | ✅ HTTP 201，**46 秒** |
| 4 | 写 `update.json` 到 Gitee 仓库根（主源 Gitee、备用 GitHub） | ✅ HTTP 201 |
| 5 | 补齐历史版本 release（v1.0.4 / v1.0.5） | ⏳ **未做**，见「遗留」 |

### 新 `update.json` 内容

```json
{
  "version": "1.0.6",
  "url": "https://gitee.com/ztz1359/pineglow-launcher/releases/download/v1.0.6/Pineglow-Launcher-win-x64-single-file.exe",
  "fallbackUrl": "https://github.com/zhangtanze1359/Pineglow-Launcher/releases/download/v1.0.6/Pineglow-Launcher-win-x64-single-file.exe",
  "size": 51750369,
  "sha256": "43750657a75145cbc2f77a37b281c9e514a35cc16a6db79e9b703bac1c6361cd",
  "notes": "<v1.0.6 发布说明，1540 字>",
  "mandatory": false
}
```

## 验证：产品链路（销项判据）

用**已发布的真实 v1.0.6 产物**（SHA-256 `43750657A75145CBC2F77A37B281C9E514A35CC16A6DB79E9B703BAC1C6361CD`），
**不设 `updateUrl`**，走产品内置默认链：

| 字段 | 值 |
| --- | --- |
| `currentVersion` | 1.0.6 |
| `latestVersion` | 1.0.6 |
| `manifestSource` | **`https://gitee.com/.../update.json`** |
| `usedFallback` | **`False`** |

产品日志：`检查更新：current=1.0.6 latest=1.0.6 available=False source=https://gitee.com/... fallback=False`

原始输出：[`raw/product-gitee-verify.txt`](raw/product-gitee-verify.txt)

> **判据说明**：不用 `manifestSources[].ok`。该字段是**失败时**才填充的（成功时为空数组），
> 用它判 PASS 会永远失败——本轮实测踩过这个坑，见脚本内注释。

## `release.yml` 的三个附带修复

| 缺陷 | 修法 |
| --- | --- |
| `curl -F "access_token=$token"` **让 token 出现在进程命令行**（可被 ps / 日志捕获） | 改为写临时 curl 配置文件（`-K`），用完立即删除；鉴权改用 `Authorization` 头 |
| `--max-time 120` 对 51 MB 跨国上传余量偏小 | 提高到 **300 s**（实测 46 s，但留余量） |
| `continue-on-error` 让失败**静默三个月** | 保留不阻断发布，但所有失败注解从 `::warning::` 升级为 **`::error::`**（Actions 里标红） |

## 遗留（如实记录）

1. **v1.0.4 / v1.0.5 的 Gitee release 仍未补**。`update.json` 只指向最新版 v1.0.6，功能上不需要旧版 release。
   但若用户要用 Gitee 直链手动下载旧版，会 404。**是否需要补，由编排方决定。**
2. **Gitee 仓库的 `master` 分支停在 `c8aa286f65`（2026-08-11）**。`update.json` 是通过 contents API 直接提交的，
   所以能生效；但 Gitee 仓库的**源码**与 GitHub 长期不同步。本项不涉及源码镜像，不影响更新链。
3. **新 token 的权限范围未逐项核验**。实测能：读用户信息、创建 release、上传附件、写仓库文件。
   **能做的都做了，没做的没试过**。
4. **本轮是手工执行一次镜像**，`release.yml` 的自动化下次发布时会重新跑。若 token 再次失效，
   `::error::` 注解会让它显眼，不再沉默。

## 明确未做的事

- **未打新 tag、未发新 Release**。修复通过直接操作 Gitee + 更新 `update.json` 完成。
- **未修改 Gitee 仓库源码**（只有一个 `update.json` 通过 API 提交）。
- 未推送相关改动到 Gitee 的代码分支。
