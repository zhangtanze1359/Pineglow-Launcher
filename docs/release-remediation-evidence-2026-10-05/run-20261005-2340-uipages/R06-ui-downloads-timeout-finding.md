# R06 观察项：上游版本列表超时被呈现为「500 + 暂无可下载版本」

日期：2026-10-05（Asia/Hong_Kong）。发现方式：新门槛 `scripts/verify-ui-pages-live.cjs`
（九页界面运行时验收）在**同一份已验收产物**上连续跑三次时的一次非通过。
状态：**观察项 / 未修复**（不是本轮引入，也不是本轮授权范围内的改动）。**不计为本轮发布阻断项**，理由见第 4 节。

## 1. 被测产物（可追溯）

三次运行都用同一份 EXE，其字节与快照验证器的正式发布产物**完全相同**：

| 项 | 值 |
| --- | --- |
| 路径 | `%TEMP%\pineglow-snapshot-8109142107ac4dc49cb61a7f20f73ee5\dev\PineglowLauncher\bin\Release\net10.0\win-x64\publish\Pineglow.Launcher.exe` |
| 大小 | 53,052,722 字节 |
| SHA256 | `5009cf73d0801e83ed68e23829486b2d27d1a313e0db67b5acbdc17bf169383c` |
| 同 SHA 的验收 | `run-20261005-2330-final/final/verify-summary.json`（`finalVerdict=PASS`，独立冷启动 18/18） |

每次运行的目录里都留了 `app-artifact-sha256.txt` 记录这份绑定关系。

## 2. 三次运行的实测对比

命令（每次独立目录、独立目录名）：

```
node scripts/verify-ui-pages-live.cjs --app <上面的 EXE> --out <dir>\shots --json <dir>\ui-pages-live.json --fake-ip-compat --settle-timeout 75
```

| 运行 | 判定 | downloads 页 | `/api/versions/remote` | content 图片 | news 缩略图 |
| --- | --- | --- | --- | --- | --- |
| `run-20261005-2340-uipages` | **FAIL**（8/9） | `external-pending`，等 75,017ms，内容仍是占位 | **500**（30,019ms） | 20/22（其中 1 张上游 504） | 12/12 |
| `run-20261005-2350-uipages-1` | PASS（9/9） | ok，25,988ms | 200（26,584ms） | 21/21 | 12/12 |
| `run-20261005-2350-uipages-2` | PASS（9/9） | ok，17,076ms | 200（14,006ms） | 22/22 | 12/12 |

三次的 `exceptions=0`、`consoleErrors=0`、`uiAssetFailures=0`；`contentIcon/skinImage/cosmetic`
三类非致命项在三次里一致（2–3 条上游 404 "图标不存在"、1 条 "该账号没有皮肤"、1 条 favicon 404）。

关键相关：**远程版本接口的耗时分布是 14.0s / 26.6s / 30.0s（超时）**——它离下面的 30 秒上限很近，
所以"偶尔撞上限"在这个环境下是可复现的，不是一次性噪声。

失败运行的原始证据（未删改）：
`ui-pages-live.json`（`problems`、`productLocalFailures`、`apiFailureBodies`）、
`ui-pages-console.log`、`shots/downloads.png`、`fail-details.txt`。

## 3. 原因（代码级，已定位）

`dev/PineglowLauncher/Pineglow.Launcher.Services/LocalApiService.cs`

- 路由：`/api/versions/remote` → `HandleListRemoteVersionsAsync`（第 726–732 行）。
- 处理体（第 4245–4257 行）：

```csharp
using CancellationTokenSource cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
List<VersionEntry> online = await new MinecraftDownloader(ApiHttp, _ => { }).FetchOnlineVersionsAsync(cts.Token);
```

上游 30 秒内没返回 → CTS 取消 → `OperationCanceledException("The operation was canceled.")` →
被通用错误处理转成 **HTTP 500**，响应体是框架的英文异常消息。

**归属**：`git show HEAD:...LocalApiService.cs` 里第 1911 行起的同一处理体**逐行相同**
（含 `FromSeconds(30)`），因此这条行为**在本次评估之前就存在**，不是本轮 R03 改动造成的。

两个可观察后果：

1. **错误契约不一致**：同一个进程里，上游超时在图标代理上是
   `504 {"error":"上游超时或不可达"}`（可在 `apiFailureBodies` 里看到），
   在远程版本接口上却是 `500 {"error":"The operation was canceled."}`。
   客户端无法用统一规则区分"上游不可达"和"服务端自身出错"。
2. **界面把失败呈现成"没有内容"**：同一次运行里 downloads 页最终文本是
   「… 可用版本 正式版 快照 全部 **暂无可下载版本** 下载任务 📥 暂无下载任务」——
   用户看到的是"没有可下载版本"，而不是"拉取失败，请重试"。
   即：**"取不到" 与 "确实没有" 在界面上不可区分**。

## 4. 为什么不计为发布阻断项（以及为什么本轮不改）

- 影响面是**版本列表这一条辅助信息**：更新检查/下载走的是另一条链，且那条链本轮已经
  把逐源失败原因做成了可判断摘要（见进度报告 §2 的 R03 一节）。
- 它是**既有行为**（HEAD 相同），不是本轮修复引入的回归；把它并入本轮会把
  "关闭评测报告里的 R01–R06" 变成范围外的新特性开发。
- 修复会改动产品行为与界面：界面文案/状态一变，`ui/**` 字节改变 → 嵌入式 UI 指纹、
  单文件 EXE 的 SHA256、`run-20261005-2330-final` 的全部 UI 证据都会失效，必须整轮重跑。
  这是**用户该做的取舍**，不该由我在未授权时替其决定。
- 因此本轮只做"如实记录 + 给出定位"，不伪装成已修。

## 5. 若要修，建议的最小改法（供决策）

1. `HandleListRemoteVersionsAsync`：捕获 `OperationCanceledException`/`TimeoutException`，
   返回**有类型的超时响应**（与图标代理同形状，例如 `504 {"error":"上游超时或不可达"}`），
   不再泄漏框架异常文本；给这个 30s 上限一个显式常量或配置项。
2. 界面（`ui/js/launcher.js` downloads 视图）：把"请求失败"与"列表为空"分开渲染
   （失败时给可重试提示），避免用户把失败读成"没有可下载版本"。
3. 改完必须重跑：`verify-source-snapshot.ps1`（UI 嵌入字节会变）+ 本文档第 2 节的九页验收
   （期望：上游超时那次也应给出**有类型**的失败，而不是 500 与空列表）。

## 6. 未做（明确声明）

- **未**用 `git` 提交、未推送、未发布。
- **未**修改任何产品代码/界面（本文件只是记录）。
- **未**把这次 FAIL 从证据里删除或改名；三次运行的原始 JSON/日志/截图全部保留。
