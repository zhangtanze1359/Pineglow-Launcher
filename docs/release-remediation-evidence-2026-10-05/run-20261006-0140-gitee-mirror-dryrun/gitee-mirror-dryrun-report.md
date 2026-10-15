# 「Mirror to Gitee (optional)」步骤失败路径干跑 —— 顺带查出一个真实的证据缺口（已修）

日期：2026-10-06（Asia/Hong_Kong）。脚本：`gitee-mirror-dryrun.py`；
原始输出：`dryrun-console.log`；结构化结果：`gitee-mirror-dryrun.json`。

## 1. 为什么挑这条步骤

`release.yml` 的镜像步骤有 100 多行 PowerShell（建 Gitee release → curl 上传附件 → 改写 manifest →
用 contents API 写固定 `update.json`），而且**从来没有被执行过**（需要 `GITEE_TOKEN`）。
R03 的要求恰好就落在这里：「**缺 token / 镜像失败不得声称固定 update.json 已更新或输出无效地址**」。
只读代码不算验证，所以把步骤正文抽出来，在**没有任何有效凭据**的前提下真的跑起来。

做法与发布门槛干跑一致：按步骤名取真实 `run:` 正文 → 替换 3 个已知表达式（残留 `${{` 就拒绝运行）
→ 落成 UTF-8 BOM 的 `.ps1` → 在 `dist/` 形状里执行，`GITEE_TOKEN` 由脚本控制。
**未使用任何真实令牌**：G1 为空串，G2/G3 是明显的假串（`invalid-token-for-test-only`）。
本机 `gitee.com` 走 fake-IP DNS，请求到不了真实服务。

## 2. 三个用例与结果

| 用例 | 情形 | exit | 是否留下 `mirror-status.json` | 记录内容要点 |
| --- | --- | --- | --- | --- |
| G1 | `GITEE_TOKEN` 为空 | 0 | **有** | `tokenPresent=false`、`attachmentUploaded=false`、`updateJsonUpdated=false`、`manifestUrlSource=github`，note 说明缺 token |
| G2 | token 为假串、接口返回不可用 | 0 | **有** | `attachmentUploaded=false`、`updateJsonUpdated=false`、`manifestUrlSource=github`，note「返回的下载地址无效，拒绝写进 manifest」|
| G3 | **故障注入**：把 API 基址换成 `http://127.0.0.1:9/...`（连不上）| **1** | **修前：无 → 修后：有** | note「Gitee API 不可达或鉴权失败：未上传附件、未改写 manifest、未更新固定 update.json；客户端将依赖备用源。原因：无法连接到远程服务器」|

十四个断言在修复后**全部通过**（`GITEE-MIRROR-DRYRUN RESULT=PASS failed=0`），其中关键几条：

- G1/G2/G3 都**没有**把 `updateJsonUpdated` 记成真，也**没有**把无效地址写进 manifest；
- 三个用例的 `manifest.url` **都仍指向 GitHub**（发布出去的东西不会是坏地址）；
- G3 失败时**必须留下**可读的状态记录（这条就是下面那个缺口）。

## 3. 查出的真实缺口（已修）

**G3 第一次跑出来是 `exit=1` 且 `mirror-status: null`** —— 也就是：
Gitee API 不可达时，步骤在写状态文件**之前**就 `throw` 了，于是 `mirror-status.json` **根本不会产生**。

为什么这算缺口（而不是"失败就该没证据"）：工作流里紧接着的
`Upload mirror status` 是 `if: always()` 且 **`if-no-files-found: warn`** ——
文件不存在时它**不会报错**，只是静默跳过。于是运营者能看到的只有一句控制台报错，
**没有结构化记录说明"有没有上传、manifest 有没有被改写、客户端要不要依赖备用源"**，
而这恰恰是 R03 花力气要保证的那件事（"跳过了却说更新了"的反面：**失败了却什么都没留下**）。

**修法（保留语义，只补证据）**：在 API 失败分支里，**先** `Save-MirrorStatus`（note 写清三项"没做"与原因）
并打 `::warning::`，**再**照旧 `throw`。这样"这一步算失败"的可见性不变
（步骤本身有 `continue-on-error: true`，不阻断发布），但证据不再丢失。修后 G3 复跑：
状态文件产生、note 准确、exit 仍为 1。

## 4. 边界（不得过度解读）

- 用**故障注入**模拟 API 不可达（只改了 API 基址一处 URL，分支逻辑未动）；
  这不是"真跑过一次 Gitee 镜像"，**真实上传附件与写 `update.json` 仍未验证**（需发布授权与真实令牌）。
- 只跑步骤正文；`actions/*` 没跑；**不等于 CI 通过**。
- 本机是 Windows PowerShell 5.1，CI 用 `shell: pwsh`（PowerShell 7）：shell 版本差异未覆盖。
- G2 的实际走法（sinkhole 返回了一个可被 `Invoke-RestMethod` 接受、但缺 `browser_download_url` 的响应）
  是本机网络环境的产物，不要把它当成对真实 Gitee 行为的描述。
