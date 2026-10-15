# 候选提交计划（2026-10-05）——仅计划，**未执行提交**

来源线程：`codex://threads/01a10b15-0f70-7b21-9ec9-c6526072940f`。
基线：`D:\松光启动器`，main，HEAD `f145d14eb26b0b7459a28491e764ac8c573085d3` + 当前脏源码。

> 授权声明：本文件只是"拿到提交授权后那一次提交"的操作清单与验收点。
> 本轮及前几轮**未** `git add`/`commit`/`push`/打 tag，**未**创建或修改线上 `update.json`。

## 1. 候选源码集合的定义

| 组成 | 数量 | 说明 |
| --- | --- | --- |
| tracked 文件的**工作区版本** | 161 | 含 44 个已修改文件；快照验证复制的是工作区内容，不是 HEAD 内容 |
| 白名单未跟踪文件 | 146 | `scripts/source-snapshot-inputs.txt` 逐项列出；**全部**尚未被 git 跟踪 |
| 合计 | **307** | 与快照验证的"快照文件数 307"一致 |

## 2. 必须新增（`git add`）的 146 个文件

- 权威清单：`scripts/source-snapshot-inputs.txt`（类别 A–L）。
- 逐项 SHA256：本目录 `run-20261005-2330-final/final/snapshot-manifest.sha256.txt`（146 行）。
  对比基线（上一整轮，同样 PASS）：`run-20261005-2118-final/final/snapshot-manifest.sha256.txt`（145 行）；
  两者逐项 diff 为：新增 1（`scripts/verify-ui-pages-live.cjs`）、删除 0、内容变化 1（白名单文件自身）。
- 目录分布（前 8）：`dev/PineglowLauncher.Tests` 65、`dev/PineglowLauncher` 22、`ui/tests` 21、
  `scripts/tests` 4、`ui/vendor` 3、`ui/js` 2、`ui/css` 1、其余 `scripts/*` 与 `global.json` 等。

**禁止** `git add -A` / `git add .`：工作区里还有大量不属于候选的东西（见第 5 节）。

## 3. 已修改的 tracked 文件（44 个）

其中与本轮收口直接相关的是：`.github/workflows/build.yml`、`.github/workflows/release.yml`、
`dev/PineglowLauncher/Pineglow.Launcher.Services/{ConfigStore,UpdateService,LocalApiService}.cs`、
`dev/PineglowLauncher/Pineglow.Launcher.csproj`、
`dev/PineglowLauncher/Pineglow.Launcher.Views/Shell/WebShellWindow.axaml.cs`。

本计划初稿写的 43 个已按实测修正为 **44**：差额是本轮较晚落盘（20:54）的
`dev/PineglowLauncher/VerifyHost/{BisectSmoke.cs,Program.cs}`（让 R04 冒烟真跑 + 把"没碰真实凭据"变成断言；
Program.cs 同时把控制台固定为 UTF-8）。核对命令：`git status --porcelain | Where-Object { $_ -match '^ M' }`。

其余为本次评估开始前就存在的脏改动（账号/新闻/AI/资源等），它们**不经本轮复核**，提交时需单独确认是否纳入。

## 4. 生成物注意事项（本轮实际踩过一次）

`dev/PineglowLauncher/packages.lock.json` 与 `dev/PineglowLauncher.Tests/packages.lock.json`
是**生成物但未跟踪**，却在白名单里（CI 的 `--locked-mode` 依赖它们），因此必须纳入提交，且必须是
**非 RID 形态**：

| 文件 | 期望字节数 | 期望 SHA256（前 16） |
| --- | --- | --- |
| dev/PineglowLauncher/packages.lock.json | 8663 | `fbbc9ae67b8e8cf2` |
| dev/PineglowLauncher.Tests/packages.lock.json | 12806 | `244ba2bcd1eb1f5a` |

本机执行一次 `dotnet publish -r win-x64` 就会把前者**就地改写**成 RID 形态（10868 字节，含 `"net10.0/win-x64"` 段与
`Microsoft.NET.ILLink.Tasks`），随后 CI 的 `--locked-mode` 还原必然 NU1004。
本轮已发生一次（快照验证一度 FAIL），修复方式：`dotnet restore dev/PineglowLauncher/Pineglow.Launcher.csproj --force-evaluate`。
目前 `verify-source-snapshot.ps1` 已加**锁定文件形态守卫**，再有污染会在组装阶段直接失败而不是等 CI。

## 4b. 换行符（EOL）不影响提交内容 —— 已实测，但有一个前提

`git ls-files --eol` 实测：44 个已修改 tracked 文件里 **37 个是 `w/lf`**、4 个 `w/crlf`、3 个 `w/-text`（二进制，
含两个图标），而 HEAD 里这些文本文件是 CRLF —— 也就是工作区与 HEAD 的换行风格并不一致
（编辑工具写入 LF）。`git diff` 因此会提示 "LF will be replaced by CRLF"。

**这不影响提交内容**，已用实验证明而不是推断：给同一段文本分别准备 LF 与 CRLF 两份，
用 `git hash-object --path=<同一路径>` 计算提交时会产生的 blob，两者**同为**
`83db48f84ec878fbfb30b46d16630e944e34f205`。原因是本仓库 `core.autocrlf=true`（且无 `.gitattributes`），
`git add` 会把两种换行都归一化成同一个 blob。

**前提/风险**：如果提交时用 `git -c core.autocrlf=false commit`（或在不带该配置的环境里提交），
归一化不发生，这批文件就会出现**纯换行差异的大 diff**（内容没变，但每一行都算改动）。
所以本次候选提交**必须保持 `core.autocrlf=true`**（本机默认值），不要用 `-c core.autocrlf=false` 绕过。

## 5. 明确不得进入候选的东西

- `docs/**`（含本轮全部证据目录）：属于交付/评审材料，是否入库由用户决定；白名单里的 `forbidden` 规则同样排除它们。
- `dev/**/bin`、`dev/**/obj`、`TestResults`、`.testagent/`、`.trae/`、`.test-tmp/`。
- `ui/tests/` 下**凡未列入白名单**的文件（调试脚本、探针、备份与临时产物）：
  `_*.cjs`、`probe-*.cjs`、`phase1-evidence.cjs`、`*run*.cjs`、`run-v4.*`、`*.bak`、`*.orig.bak`、
  `*.append.tmp`、`*.tmp2`、`*.new`、`test-write.txt`、`skin3d-browser.py` 等。
- `scripts/` 下的本地探针/一次性脚本：`_*.py`、`_*.cs`、`_mimeprobe/`、`_assetgen/`、`_uritest/`；
  以及 `scripts/run-dify-minimax-workflow.ps1`（**本地自动化脚本**，只被
  `docs/source-reproducibility-workflow-2026-09-28.md` 引用，不是构建输入）。
- `dev/icon-tests-red.txt`（探针输出）、`dev/PineglowLauncher.Tests/DownloadResumeTests.cs`（**0 字节空文件**，遗留物）。
- 真实 `config.json`、Windows 凭据、存档、游戏文件、任何密钥或令牌。
- 临时/备份可执行文件（例如仓库根目录的历史 `Pineglow-Launcher-win-x64-single-file.exe`，属旧产物）。

### 5.1 这一节是核过数的（第 15 轮，双向核对）

- **正向**：白名单 146 条——**全部存在**、**全部未被 git 跟踪**、**全部未被 `.gitignore` 忽略**
  ⇒ 直接 `git add <路径>` 即可，**不需要 `-f`**，也不会被静默跳过。
  其中 `ui/assets/pine-forest.svg` 依赖 `.gitignore` 里那句 `!ui/assets/pine-forest.svg`（第 37 行）
  才不被 `ui/assets/*`（第 36 行）吃掉——**这句 un-ignore 是必需的**，删了它 R02 的资源就又进不了候选。
- **反向**：工作区里"未跟踪 + 未被忽略 + 不在白名单 + 不在 `docs/`"的文件共 **90 个**，
  逐个分类后**没有任何一个是构建或测试必需输入**（全部是探针、备份、临时产物、工具缓存，
  加上上面点名的那个 0 字节空文件与那个被文档引用的本地脚本）。
- **触发条件**（用 YAML 1.2 语义解析，绕开 PyYAML 把 `on:` 当布尔 `True` 的盲点，见第 15 轮证据）：
  `release.yml` 只有 `push: tags: ['v*']`（**普通推送不会触发发布**）+ `permissions: contents: write`；
  `build.yml` 为 `push: branches: [main]` / `pull_request` / `workflow_dispatch`。
- 提交前可用这条复跑正向核对（应输出 0）：
  `git check-ignore -q -- $(白名单条目)` 逐个判退出码（**注意：`check-ignore` 有输出不等于被忽略，
  要看退出码**；用输出判断会把 `!` 反选规则误报成"被忽略"）。

## 6. 提交前必跑的验收（按顺序）

1. `powershell -NoProfile -ExecutionPolicy Bypass -File scripts/verify-source-snapshot.ps1 -EvidenceDir <新证据目录>`
   → 期望 **最终判定 PASS / 失败项 0**（含锁定文件守卫、资源闭包、清单一致、Node 304、C# 903、publish、独立冷启动）。
   最近一次实测（`run-20261006-0450-final/final/`）：PASS，307 文件、Node 304/304、C# **903/903**、
   单文件 53,052,722 B `5009cf73…`（**十一轮逐字节相同**）、冷启动 **19/19**（第 18 轮起含"不继承开发旋钮"检查）。
   另有：发布门槛干跑 `run-20261006-0110-releasejob-dryrun/`（18 例，含逐条 RED）、
   Gitee 镜像失败路径干跑 `run-20261006-0140-gitee-mirror-dryrun/`（14 项断言）、
   发布配方（带 `/p:Version`）独立冷启动 18/18（当时 18 项）`run-20261006-0210-publish-recipe/`；冷启动门禁环境隔离 `run-20261006-0400-standalone-env/`；替换脚本代码页 437 验证 `run-20261006-0440-apply-codepage/`。
2. `powershell -NoProfile -ExecutionPolicy Bypass -File scripts/verify-update-live-download.ps1 -ExePath <发布的单文件 EXE>`
   → 期望 **PASS**（真实默认源链路；可选 `-AssetUrl <真实 HTTPS 资产>` 走完整三阶段）。
   已实测：默认源链路 PASS（主源 404 → 真实 GitHub Release `manifest.json` 降级成功）；
   真实 HTTPS 资产三阶段（下载 + SHA256、篡改拒绝）亦已实测通过（见进度报告 §2）。
3. `node scripts/run-inventory-tests.cjs --check` → 期望"清单校验通过"。最近一次实测：23 == 23，PASS。
4. actionlint 校验两份工作流：**本机无法执行**（拉取超时），只做了 PyYAML 结构校验；提交后需由 CI 或联网环境补。

## 7. 仍未授权的动作

提交、推送、打 tag、创建 GitHub Release、创建/更新 Gitee `update.json`、以及任何线上写入。
这些都需要用户明确批准；本文件不构成授权。
