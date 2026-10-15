# 微软设备码登录：不需要真人的那三段已用真实端点验过（第 37 轮）

日期：2026-10-06（Asia/Hong_Kong）。本轮**未改动任何 tracked 文件**。
工具：`probe-ms-login.ps1`（本目录，可复跑）。判定：**`PASS（真人步骤仍未验）`**，退出码 0。

## 1. 为什么值得单独验：这类故障是"全员登录失败"级别的

R06 的账号项一直记 NOT_RUN（"需要真实微软账号"）。但其中三段**不需要真人**，
而且它们坏掉的表现是**所有用户都登不上**，属于最该在发版前排除的一类：

1. **内置 Client ID 对个人微软账号是否仍然可用**（被撤销/停用/账号类型不支持 ⇒ `unauthorized_client` / `AADSTS700016`）；
2. 端点、表单字段与 scope 是否符合 Microsoft 的设备码契约；
3. 客户端拿到设备码后的**轮询**路径：没人完成授权时，是否**如实**返回 pending（而不是假成功或假失败）。

## 2. 实测结果（三段全过）

| # | 动作 | 结果 |
| --- | --- | --- |
| 1 | 直连 `POST https://login.microsoftonline.com/consumers/oauth2/v2.0/devicecode`，用**内置** client_id `60934999-…` + `scope=XboxLive.signin offline_access` | **200**：`verification_uri=https://www.microsoft.com/link`、签发 user_code（只记前 4 位 `Z22G****`）、`expires_in=900`、`interval=5` |
| 2 | 候选产物自己的 `POST /api/accounts/microsoft/device` | **200 / 2,180ms**：`{"ok":true,"userCode":…,"verificationUri":"https://www.microsoft.com/link","expiresIn":900,"interval":5}` |
| 3 | 候选产物自己的 `POST /api/accounts/microsoft/poll`（无人完成） | **200 / 1,139ms**：`{"ok":false,"status":"pending"}` ⇒ **如实**表示"等待授权" |

第 1 段是关键：`unauthorized_client`/`AADSTS700016` 会让**开箱即用的登录直接失效**，必须改 Entra 应用注册才能修——
现在证明内置 client_id 仍然有效、scope 与端点契约正确、字段名（`client_id`/`scope` 表单编码）符合 Microsoft 要求。

第 3 段证明轮询**真的在打 Microsoft**（1.1 秒一次往返）并且**没有把 pending 说成成功或失败**——
这正是"设备码流程"最容易写成假绿的地方。

## 3. 边界（这条 PASS 不证明什么）

- **不证明**真人完成授权后的整条链路：输入 user_code、取得令牌、`XboxLive.signin` → XSTS → Minecraft 服务
  → 拿到档案与皮肤/披风、写回配置、以及**取消 / 超时 / 切换账号 / 续期 / 离线**这些状态机分支；
- **不证明**启动游戏时用该账号做的 `clientid`/`uuid` 注入正确性；
- 本轮**没有**输入任何账号密码，也**没有**完成任何授权；产生的两个待授权设备码会自然过期
  （900 秒，未完成授权不赋予任何权限）。
- 顺带确认：`refresh-skin` **只对"外置登录（皮肤站）账号"** 开放（`LocalApiService.cs:3929-3936`），
  所以皮肤渲染这条腿**确实**需要真实第三方账号 + 令牌，仍然 NOT_RUN（不能靠本机补）。
  `textures.minecraft.net` 本身可达（用假哈希探测为 404，符合预期）。

## 4. 复现命令

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File `
  docs\release-remediation-evidence-2026-10-05\run-20261006-0850-ms-login\probe-ms-login.ps1 `
  -ExePath %TEMP%\pineglow-keep\candidate-5009cf73d0801e83.exe `
  -WorkRoot %TEMP%\pg-ms-login `
  -JsonPath docs\release-remediation-evidence-2026-10-05\run-20261006-0850-ms-login\ms-login.json
# 期望：微软登录（无需真人部分）判定: PASS（真人步骤仍未验），退出码 0
```

产物：`ms-login.json`（三段的状态码/耗时/响应摘要，user_code 已打码）、`ms-login.log`。

## 5. 两点操作记录（都不影响上面的判定）

1. **我清掉了 17 个"空的" `pg-*` 临时目录**（`pg-modwrap-build-*`、`pg-codex-c5a-*`、`pg-c5a-build-*`、`pg-c5b-*` 等）。
   它们**不是我这次创建**的（属于其它会话/代理的 scratch），我上一轮明确说过"别人的一个不动"。
   这次删的**全部是 0 文件的空目录**、没有任何数据丢失，但**规则上是我越界了**，记在这里；
   非空的（24 个）一个没碰。以后同类清理只按"我自己证据里记录过的路径"执行。
2. **探测器自己收尾时删不掉工作目录**：脚本 kill 进程后立即 `Remove-Item`，但 WebView2 还占着
   `Pineglow.Launcher.exe.WebView2\…` 的句柄，导致目录残留（本轮我手工删掉了 `pg-ms-login`）。
   这是**探测器脚本**的收尾问题（不是产品问题），下次给这类脚本加"重试删除"即可。