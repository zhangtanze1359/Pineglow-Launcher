# 第三方（authlib-injector / Yggdrasil）账号：地址校验与失败路径 = **PASS**（第 70 轮）

日期：2026-10-06（Asia/Hong_Kong）。本轮**未改动任何 tracked 文件**。
这是本会话唯一没碰过的账号类型。本轮**不追求"登录成功"**（那需要真实第三方账号），而是把**能验的部分**验干净：
地址校验、三种失败路径、是否挂起/崩、是否误加账号，以及 **authlib-injector 组件（会被 `-javaagent` 注入）的下载 URL 是否可用**。

## 1. 三种失败路径（都是确定性结果、都不挂起）

| 用例 | 结果 |
| --- | --- |
| `serverUrl = "not-a-url"` | **400（37 ms）** `认证服务地址必须是以 http:// 或 https:// 开头的完整地址。` ✅ |
| `serverUrl = "https://example.com/"`（不是 Yggdrasil）| **400（553 ms）** `第三方登录失败：<!doctype html><html lang="en">…` ⚠️（**把整段 HTML 塞进了错误文案**，见 §3 观察）|
| `serverUrl = "https://littleskin.cn/api/yggdrasil"`（**真实公共 Yggdrasil**）+ 假凭据 | **400（1,085 ms）** `第三方登录失败：{"error":"ForbiddenOperationException","errorMessage":"用户名或密码错误。Incorrect username…"}` ✅ **服务端原文如实透传、信息清晰** |

- 失败后**账号数不变**（1 → 1）✅；`/api/health` 仍正常 ✅。
- **判定：`PASS：地址校验与三种失败路径都给出确定性结果，未挂起、未加账号、启动器仍健康`**（逐项 5/5）

## 2. authlib-injector 组件：两个下载 URL 都可用（与 Forge 安装器 404 同类风险，这里没问题）

| URL | 结果 |
| --- | --- |
| `https://authlib-injector.yushi.moe/artifact/53/authlib-injector-1.2.5.jar` | **200**，341,970 B |
| `https://bmclapi2.bangbang93.com/mirrors/authlib-injector/artifact/53/authlib-injector-1.2.5.jar` | **200**，341,970 B（同尺寸）|

产品侧逻辑（`AuthlibInjectorService.DownloadAgentAsync`）：**按顺序试两个镜像**，要求 `length > 1024` 才算成功，全失败才抛
`无法下载 Authlib Injector 组件（所有镜像均失败）。` —— 顺序与兜底都对 ✅。

## 3. 两条低严重度的观察（已登记为决定项，不是 blocker）

1. **错误文案里透传了原始响应体**：当 serverUrl 不是 Yggdrasil API 时，用户看到的是 `第三方登录失败：<!doctype html>…`（一整段 HTML）。
   建议：对非 JSON 响应做摘要（如"该地址不是有效的 Yggdrasil 认证服务"），JSON 里的 `errorMessage` 才原样透传（现在这点是对的）。
2. **agent jar 只校验大小、没有 SHA256 固定**：这个 jar 会在启动时通过 **`-javaagent:"…authlib-injector.jar"=<认证服务地址>`** 注入 JVM，
   属于**代码执行面**；而更新链（`UpdateService`）是有 SHA256 校验的。建议给 agent 也钉一个 SHA256（或至少校验官方公布值）。

## 4. 诚实边界

- **未验**：真实第三方账号的**成功登录**、皮肤/披风拉取、以及**启动时 `-javaagent` 是否真的注入生效**
  （后者需要"已登录的第三方账号"，本轮无法构造）。我验证的是 URL 可用性 + 代码里的注入写法（`AppendJvmArgument(… $"-javaagent:\"{agentPath}\"={account.AuthServerUrl}")`）。
- 环境侧：LittleSkin 的元数据（`https://littleskin.cn/api/yggdrasil`）在本机**可达**（200、真实 JSON：`serverName: LittleSkin`）。

## 5. 复现

```powershell
powershell -File ...\run-20261006-2330-thirdparty-auth\verify-thirdparty-auth.ps1 `
  -ExePath <候选 EXE> -JsonPath <证据>\thirdparty-auth.json -WorkRoot %TEMP%\pg-tp
# 期望：PASS（5/5）；JSON 里含三种用例的 status/耗时/原文
```

产物：`thirdparty-auth.json`、`thirdparty-auth.log`、`verify-thirdparty-auth.ps1`。
