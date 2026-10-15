# 远程真相：真实端点可达 + 真实更新链路 PASS + 7 个未推送提交（第 32–33 轮）

日期：2026-10-06（Asia/Hong_Kong）。本轮**未改动任何 tracked 文件、未推送、未打 tag、未发布**。
方法：直连 `api.github.com` / `github.com` / `gitee.com` 的真实 HTTP 交互 + `git ls-remote` + 已验证的候选 EXE
（`5009cf73d0801e83…`，53,052,722 B）驱动真实更新链路。原始事实落在本目录的 json/log。

## 0. 为什么值得单独记一轮：**我上一轮的"网络阻断"结论是错的**

第 21–29 轮我把 "actionlint 拉不下来 / GitHub 不通" 当作环境事实写进文档（`run-20261006-0050-actionlint/`，
9 字节 `Not Found`）。本轮实测：

| 端点 | 结果 |
| --- | --- |
| `https://api.github.com/...`（release / runs / commits）| **200**（直连，无需镜像）|
| `https://github.com/.../releases/download/v1.0.0/manifest.json` | **200**，324 B |
| `git ls-remote origin` | **exit 0**，能读到 `HEAD`/`main`/`tags` |
| `https://gitee.com/ztz1359/pineglow-launcher` | **200**（仓库页 181,750 B）|
| `https://gitee.com/.../raw/master/update.json` | **404**（与原始评估一致）|
| `https://ghproxy.net/https://github.com/rhysd/actionlint/...` | 200（能下 zip）|
| `https://ghproxy.net/https://api.github.com/...` | 403（镜像不放 API，改直连即可）|

⇒ 正确说法是"**我之前的探测路径不对**"，不是"网络阻断"。这条更正同时使两项此前判为 **NOT_RUN/无法闭合**
的验收变成**已通过**（见 §3、§4）。

## 1. 已发布 v1.0.0 的真实清单（原文）

```json
{
  "version": "1.0.0",
  "url": "https://github.com/zhangtanze1359/Pineglow-Launcher/releases/download/v1.0.0/Pineglow-Launcher-win-x64-single-file.exe",
  "size": 43989943,
  "sha256": "6130db19bbcc8b4d6b606bc1825d824a99a19c72ef2ef7af3ac874515953a999",
  "notes": "Pineglow Launcher 1.0.0",
  "mandatory": false
}
```

与**新版** `release.yml` 生成逻辑逐字段一致（`version/url/size/sha256/notes/mandatory`，`notes` 也是
`Pineglow Launcher <version>`）⇒ **清单协议兼容从"读源码推断"升级为"对比真实发布物"**。
（`v100-manifest.json` 为原件副本。）

## 2. Gitee 默认源：仓库在，文件不在

- 仓库页 `gitee.com/ztz1359/pineglow-launcher` → **200**（存在）；
- 固定源 `raw/master/update.json` → **404**（不存在）。

⇒ 旧客户端（v1.0.0 只认这一个地址、**没有备用源**）现在**无法检查更新**；这条以前是"发布说明要写"的
建议，现在是**实测事实**。第一次由新流程发版时 `release.yml` 会写入这个文件（需 `GITEE_TOKEN`）。

## 3. 真实更新链路验收：**PASS**（阶段 A/B/C，全部真实端点）

用已验证的候选 EXE 驱动，`scripts/verify-update-live-download.ps1`，判定 `PASS`，15 项断言 0 失败，用时 62 秒：

| 阶段 | 断言 | 实测 |
| --- | --- | --- |
| A（真实默认源）| 默认地址链取到清单或逐源说明原因 | 主源 Gitee **失败**（404）→ **按设计降级**到 GitHub 备用源，`usedFallback=true`，取到 v1.0.0 清单 |
| A | 来源可辨认 | `manifestSource=https://github.com/zhangtanze1359/Pineglow-Launcher/releases/latest/download/manifest.json` |
| B（真实 HTTPS 下载）| 下载完成、字节数一致、SHA256 一致、无 `.part` 残留 | `got=43989943 want=43989943`；`got=6130db19bbcc8b4d want=6130db19bbcc8b4d`；无残留 |
| C（篡改必须被拒）| 清单 SHA256 被改写 → 失败且不留可用文件 | `state=failed`，`error=更新包下载失败：校验失败：<资产 URL>`，`leftover=`（空）|

### 3.1 与第 19 轮那次真实网络运行的关系（**我先把这次的分量说准**）

第 19 轮（`run-20261005-2028-live/live/`，客户端构建 `75572b14…`）**已经**跑过真实网络，而且当时
`usedFallback=True` 也成立——所以"降级"**不是**这次才第一次被真实端点证明。两点差别要说清：

| 维度 | 第 19 轮（build `75572b14…`）| 本轮（build `5009cf73…`，已冻结候选）|
| --- | --- | --- |
| 资产 | Gitee 的 `README.md`（**1,618 B 替身**）| **真实发布资产** `Pineglow-Launcher-win-x64-single-file.exe`（43,989,943 B）|
| 清单 | 本地 http 夹具（`manifest-good.json`）| 阶段 A 用**真实 GitHub 备用源清单**；阶段 B/C 用本地夹具（脚本设计如此）|
| 主源 | 真实 Gitee 主源失败 → 降级（`usedFallback=True`）| 同左，但现在**备注如实写 `primary-failed`** |
| 备注字段 | `stageANote="primary-ok"` ← **与同一文件里的 `usedFallback=true` 自相矛盾** | `primary-failed（已按设计降级到备用源: …）` |

⇒ 本轮真正的增量是：① 用**真实发布资产**（不只是替身）证明下载/字节/SHA256/篡改拒绝；
② 把"主源失败"这件事**如实标注**。

### 3.2 顺带查出的旧证据缺陷：`primary-ok` 是错的

`run-20261005-2028-live/live/live-update-acceptance.json` 与 `live-update-stageA.json` 里，
`stageANote` 写着 `primary-ok`，而**同一份 JSON 的 `results.stageA.usedFallback` 是 `true`、
`manifestSource` 指向 GitHub 备用源**——即主源当时**也失败了**，备注字段是错的。
当前脚本已修（`$giteeNote` 会按 `manifestSource`/失败原因写成 `primary-failed（…）`），
但**旧目录里的错值仍在**：只看那一个字段会得出"主源可用"的反结论。
已在该目录加 `CORRECTION.md` 标注。（这是本会话第 N 次"字段/口径不一致直接翻转结论"的同型问题。）

> 一个诚实的边界：脚本**刻意不调用** `/api/update/apply`（那会把启动器替换成被测资产），
> 所以"下载"是真、"替换自己并重启"仍未验（= R06 剩余项，需要真实发布一次）。

## 4. 另一个先前判为 NOT_RUN 的项：actionlint **已本地复现**

- 通过 `ghproxy.net` / `gh-proxy.com` 两个镜像下载 `actionlint_1.7.7_windows_amd64.zip`，
  **两个镜像字节数（2,229,473）与 SHA256（`7f12f180…`）完全一致**；
- 该 SHA256 与**官方校验表** `actionlint_1.7.7_checksums.txt` 中 `windows_amd64.zip` 一行相同；
  校验表本身又被**独立锚点**印证（技能文档里固定的 `linux_amd64.tar.gz` = `023070a2…` 出现在同一表里）；
- 解压后 exe `6d470a52…`，`-version` 报 `1.7.7`、`go1.23.4 windows/amd64`；
- 对当前两份工作流运行：**exit 0、零输出**（= 与原始评估的 PASS 一致，NOT_REPRODUCED 消除）；
- **反向对照**（证明工具确实在读文件，不是静默空跑）：
  - 注入"未加引号的 `#` 截断表达式"→ `exit 1`：`got unexpected EOF while lexing end of string literal`；
  - 注入残缺表达式 → `exit 1`：`unexpected end of input while parsing variable access`。

证据：`run-20261006-0740-actionlint/`（`actionlint-result.json`、原始日志、反向对照日志）。
边界：不覆盖 runner 行为、shell 逻辑（`-shellcheck=`/`-pyflakes=` 关闭）、action 的 input 名
（反向对照里故意写的 `bogus-input` **未**被报错）。

## 5. CI 真实历史：旧流水线在真 runner 上跑过，新步骤没跑过

`api.github.com/.../actions/runs?per_page=12`：

```
Build    push  success    c8aa286  2026-08-10T16:36:17Z
Release  push  success    f61f44f  2026-08-10T16:17:28Z   ← v1.0.0 发布成功
Build    push  success    f61f44f  / 2f251f1 / 236e4ab / 988dac5 / e4329c6 / 39b5256（均 success）
Release  push  failure    e4329c6 / 988dac5 / 236e4ab（v1.0.0 之前的 3 次失败）
Release  push  cancelled  2f251f1
```

⇒ 更正我之前"`actions/*`、runner 环境从未在真实 CI 上跑过"的说法：**该仓库的 Build/Release 在真 runner 上
长期成功**，`release.yml` 也**成功发布过 v1.0.0**（含 manifest + EXE 两个附件）。
但本轮新增/改写的步骤（证据产物上传、`needs: verify` 互锁、形态断言、独立冷启动、真实更新链、镜像状态落盘）
**从未在真 runner 上执行过**——这才是仍然成立的边界。

## 6. 新事实：**7 个提交还没推送**

`git ls-remote origin` → `refs/heads/main = c8aa286`；本地 `HEAD = f145d14`。未推送的 7 个提交（涉及 39 个文件）：

```
f145d14 feat: 微软登录改设备码流（microsoft/device + poll 轮询）+ CurseForge 验证
0616638 chore: 忽略 VerifyHost 构建产物与调试备份（_orig_*.js）
f342d6f docs: 建立项目知识库（架构/API/功能/工程） + AI 诊断计划
259d53e feat: 接入真实 Minecraft 新闻源 + 资源库真实图标
258b172 feat: Mod 二分定位（Bisect）+ VerifyHost 冒烟自检 + CI 集成
40de7ca feat: AI 崩溃诊断 — 多提供商配置 + 规则引擎增强解释
45d8132 test: 补 AI 诊断/崩溃规则/Mod 计数/Mod 二分测试 + 测试质量加固
```

⇒ 对提交计划的影响：**推送不只发一个候选提交**，还会把这 7 个功能提交一起推上去（它们与 §0 范围的
"已提交功能工作"是同一批：+4,135 行）。工作流是 `on: push`（非 tag）就会跑 Build，所以推送即触发一次真实 CI——
这正好是 G14（远程 CI）的解锁动作，但也意味着**推送前必须确认这 7 个提交是允许公开的**。

## 7. 版本定稿是**硬性**要求，不只是"建议 > 1.0.0"

- 候选 EXE 的 `FileVersion=1.0.0.0`、`ProductVersion=1.0.0`（`csproj <Version>1.0.0</Version>`）；
- `LauncherService.LauncherVersion` = **程序集版本**（`Assembly.GetName().Version.ToString(3)`），
  更新检查接口返回的 `currentVersion` 就是它（真实链路实测 `currentVersion=1.0.0`）；
- `release.yml` 用 `/p:Version=<tag 版本>` 发布，并**断言程序集版本 == tag 版本**；
- 而 **tag `v1.0.0` 已被占用**（指向 f61f44f，已发布）。

⇒ 若按 v1.0.0 发：tag 冲突且客户端会认为"已是最新"，用户永远收不到升级提示；
必须定一个 **> 1.0.0** 的版本，并把 `csproj` 的 `<Version>` 同步改掉（否则本机构建/证据里的版本号
与发布产物不一致，会污染"同版一致"这类断言）。

## 8. 本轮**没有**证明的（边界）

1. 候选自己替换自己并重启（R06 剩余项，需真实发布一次；脚本刻意不调 apply）；
2. Gitee 的**写入**（`update.json` 仍需发布授权 + `GITEE_TOKEN`）；
3. 新工作流步骤在真 runner 上的行为；
4. `actions/*` 版本可用性、runner 镜像内容；
5. 真实账号 / Java / 游戏 / 整合包 / Mod / 存档等 R06 真机项（依旧 NOT_RUN）。

## 9. 复现命令

```powershell
# 1) 真实更新链路（需要一份候选发布产物）
powershell -NoProfile -File scripts\verify-update-live-download.ps1 `
  -ExePath <候选 EXE> `
  -AssetUrl https://github.com/zhangtanze1359/Pineglow-Launcher/releases/download/v1.0.0/Pineglow-Launcher-win-x64-single-file.exe `
  -WorkRoot "$env:TEMP\pg-update-real" -JsonPath <证据目录>\update-live-real.json

# 2) 远程/CI 事实
git ls-remote origin
Invoke-WebRequest https://api.github.com/repos/zhangtanze1359/Pineglow-Launcher/actions/runs?per_page=12 -UseBasicParsing

# 3) 已发布清单 vs Gitee 固定源
Invoke-WebRequest https://github.com/zhangtanze1359/Pineglow-Launcher/releases/download/v1.0.0/manifest.json -UseBasicParsing
Invoke-WebRequest https://gitee.com/ztz1359/pineglow-launcher/raw/master/update.json -UseBasicParsing   # 404

# 4) actionlint（见 run-20261006-0740-actionlint/）
actionlint -shellcheck= -pyflakes= -color .github/workflows/build.yml .github/workflows/release.yml
```

## 10. 环境发现：`%TEMP%` 里有 ~50 GB 历史 scratch（**只清了我自己的**）

顺手盘点 `%TEMP%` 里 `pg-*` / `pineglow-*` 目录：

| 事实 | 数值 |
| --- | --- |
| 匹配目录总数 | **1,517** |
| 占用合计 | **~50.5 GB**（清理前） |
| 其中 `pineglow-snapshot-*` | 26 个 × ~1,838 MB ≈ **33 GB**（每次整轮验证会复制一份完整源码 + bin/obj）|
| 证据里**我自己**记录过的、且仍存在的 | **15 个 / 22.5 GB** |
| 清理后释放 | **21.84 GB**（C: 可用 84.5 GB → **106.3 GB**）|
| 残留（**不是我的**，未动） | ~1,459 个目录，名字含 `pineglow-final-acceptance`、`pineglow-goal-seal`、`pineglow-codex-r2`、`pg-codex-c5a-*`、`pineglow-boundary-*`、`pineglow-step3-test-*`、`pineglow-newsimg-*`(420 个) 等 |

清理规则（可复核、不误删）：只删**本轮证据文件里明确记录过**的 `%TEMP%\<名字>`，
删前逐个校验路径解析后仍在 `%TEMP%` 之内、且删除后确实消失；**别人的 scratch 一个没动**
（来源不确定，可能被其它会话引用）。

- 保留了一份已验证产物副本：`%TEMP%\pineglow-keep\candidate-5009cf73d0801e83.exe`
  （53,052,722 B，sha256 与冻结值一致），供后续复跑 UI 九页 / 真实更新链，不必重新发布。
- 与"磁盘满"那条 NOT_RUN 的关系：**当前 C: 余量充足**，所以"磁盘满导致失败"仍属无法安全模拟的形态；
  但这次盘点说明**这个环境很容易积累几十 GB scratch**，发版前应看一眼余量（已写进手册 §7 的语境）。