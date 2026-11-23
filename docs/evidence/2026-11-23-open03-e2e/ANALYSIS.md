# 实包验证 · 阶段二修正版：用 HTTPS 本地服务（产品强制 HTTPS）

## 为什么第一次失败

第一次跑完后目标成了 v1.0.5 而不是 v1.0.6。根因**不是产品缺陷，是我的验证设计违反了产品的安全约束**：

`UpdateService.ValidateManifest`（`UpdateService.cs:333-336`）明确拒绝非 HTTPS 资产地址：

```csharp
if (!Uri.TryCreate(manifest.Url, UriKind.Absolute, out Uri? assetUri) || assetUri.Scheme != Uri.UriSchemeHttps)
{
    throw new InvalidDataException("清单的 url 不是有效 HTTPS 地址");
}
```

我的本地清单用 `http://127.0.0.1:49123/...`，被判定为坏清单 → 自动降级到内置 GitHub 源 →
拿到真实的 v1.0.5 → 「升级」成它自己。

**这一轮附带证明了产品的降级链确实工作**（`usedFallback: true`、`manifestSource` 指向 GitHub）。

## 第二次尝试的障碍

改用本地 HTTPS 需要证书。但产品用默认 `HttpClientHandler`（`OutboundHttp.cs:159-164`），**不关闭证书校验**。
自签名证书会被拒绝。若要绕过，只有三条路：

1. 把自签名证书装进系统信任根 —— **会改动本机安全配置，不做**
2. 改产品代码跳过校验 —— **违反「不改产品代码」**
3. **改用真实的 HTTPS 源，但让它指向我构建的 v1.0.6 包** —— 需要把包传到公网，等于发版，**未获授权**

三条都不可行或不授权。**因此「本地闭环验证跨版本升级」这条路在设计上走不通**，除非牺牲安全性或发版。

## 所以本轮能做到的和不能做到的

**能证明（第一次演练已实测）**：

| 环节 | 证据 |
| --- | --- |
| 真实 v1.0.5 单文件 exe 可启动 | `PID=24780`，`/api/health` 返回 `{"ok":true,"pid":24780,"port":48123}` |
| 真实 HTTP API 可用 | 从 `index.html` 提取到 64 位 session token |
| 检查更新走真实网络 | `GET /api/update/check` 拿到 GitHub 真实 v1.0.5 清单（`usedFallback:true`） |
| 下载真实 51 MB 包 | `updates/` 下出现 `Pineglow-Launcher-1.0.5-win-x64-single-file.exe`（51,684,833 bytes） |
| SHA256 与大小校验通过 | 下载包被接受，未报校验失败 |
| **替换脚本真实执行** | 目标 exe 从 51,619,122 → 51,684,833 |
| **备份保住原始版本** | `Pineglow.Launcher.exe.bak-1.0.5` = 51,619,122 bytes（原始 v1.0.5） |
| 状态写为 applied | `apply-state.json` = `{"result":"applied","version":"1.0.5"}` |
| 自动重启执行 | `Pineglow.Launcher.exe.WebView2/EBWebView` 目录在替换后生成 |
| **非 HTTPS 清单被正确拒绝** | 降级到 GitHub 源，未使用我的 http:// 清单 |
| **多源选优/降级链工作** | `usedFallback: true`，`manifestSource` 指向 GitHub |

**不能证明**：

| 项 | 原因 |
| --- | --- |
| **v1.0.5 → v1.0.6 的跨版本升级** | 需要 HTTPS 源指向 v1.0.6 包；本地 HTTPS 被证书校验挡住，公网发布需授权 |
| 从 Gitee 主源升级 | 主源返回 404（历史上已知），实际走 GitHub 镜像 |
| CI runner 上的同一套 | 本地 ≠ GitHub runner |

## 结论

**「从 v1.0.5 升级到 v1.0.6」这条链路，在遵守产品安全约束且不发版的前提下，无法在本地闭环验证。**

但本轮**意外验证到了更基础也更容易出问题的一层**：真实二进制能启动、真实 HTTP API 可用、真实下载与校验通过、
**替换脚本能真实替换自己的可执行文件、备份保住旧版、并自动重启**。这正是 OPEN-04 七轮复核里始终只由夹具模拟、
从未由真实二进制验证的部分。

**它证明的是「自更新机制本身工作」，不是「v1.0.6 这个具体版本能被正确升级到」。**
