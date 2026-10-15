# Harness 修复报告：CV-01 / CV-02 / CV-03（2026-10-13，Asia/Hong_Kong）

- 角色：DeepSeek Harness（实现 + 自检）。终态只能是等待 Codex 独立验收，不自批准、不提交、不发布。
- 产品源：`D:\松光启动器`，HEAD `1829f1c106bb32c7c894eca8f288f1d24e79447b`，分支 `main`。
- 本会话环境：`pwsh` 工具连续两次 `0xC0000142`（进程无法启动），本轮**所有运行时门槛都没有在我这里执行**；
  实现与核对只用了 read/glob/grep/edit/write。凡未执行项一律标 `NOT_RUN`，本报告不声明 GREEN。
- 我未执行任何 Git 写操作（无 add/commit/push/reset/clean/restore/stash），未干预聊天 worktree 或 D 盘独立 baseline。

## 1. 改动文件（仅这 4 个路径）

| 路径 | 类型 | 说明 |
| --- | --- | --- |
| `scripts/verify-source-snapshot.ps1` | 修改 | CV-01：数量口径改为「唯一规范相对路径集合」+ 新增预期集合逐项存在断言 + 必要注释 |
| `.gitattributes` | 新建 | CV-02：只精确约束 `dev/PineglowLauncher.Tests/TestAssets/Diagnostics/diagnostic-cases.json` 为 `text eol=lf`（全文仅 1 条规则，无 `* text=auto`） |
| `dev/PineglowLauncher.Tests/InstancePackTests.cs` | 修改 | CV-03：缺失 `formatVersion` 夹具改用 `JsonNode` 结构化删除属性 |
| `docs/current-version-audit-2026-10-13/harness-fix-report.md` | 新建 | 本报告 |

明确未改：冻结案例内容、`FrozenSha256`、案例断言、其他产品代码、依赖版本、`packages.lock.json`、CI 工作流、
用户配置、历史报告与原始 RED；未更新任何基准哈希来掩盖问题。

## 2. 根因（与任务包一致，并附本会话查到的字节证据）

### CV-01：tracked 与白名单重叠被当成互斥
`scripts/verify-source-snapshot.ps1` 旧实现（修复前 L293）是
`$expectedCount = $Summary.trackedCopied + $Summary.whitelistCopied`。
基线证据 `docs/current-version-audit-2026-10-13/product-baseline-gate/verify-summary.json`
（`repoRoot = D:\松光启动器`，`generatedUtc = 2026-10-12T17:19:12Z`）同时记录：
`trackedCopied = 302`、`whitelistCopied = 163`、`snapshotFileCount = 302`。
快照是「tracked 复制 + 白名单复制（同名覆盖）」，实际只有 302 个唯一文件 ⇒ 163 项白名单**全部已是 tracked**，
唯一集合 = 302，而旧期望 = 465，GREEN 被误判 FAIL。

### CV-02：冻结 JSON 受 `core.autocrlf` 影响
该 JSON 的冻结哈希是 **LF 原始字节**的哈希
`D9F55189FA05845BAC0403A058AA63912A725AFF61555BDF25A6865DD5AB45AF`；CRLF 检出后的字节哈希是
`133E610B1768BBECCE576F97AE19BEBAC60BC2D96AC44A4E55C141F6F275A85F`。同一份 `source-binding-before.json`
（L363–365）把前者记为 `productSha`、后者记为 `worktreeSha`，即：内容一字未改，只是检出换行不同。
证据补充（本会话新查）：
- D 盘本仓库 `.git/config` **没有** `core.autocrlf`（等效 false；`extensions.worktreeConfig=true` 但无 `.git/config.worktree`），
  因此 D 盘工作区该 JSON 目前是 LF：`product-baseline-gate/snapshot-manifest.sha256.txt:146` = `d9f55189…45af`，
  `product-source-integrity-after.json:363–365` 的 `before/after` 均为 `D9F55189…45AF`。
- 会变 CRLF 的是全局 `core.autocrlf=true` 的其它检出（聊天 worktree、以及任何新克隆/新 worktree），
  这正是 `DiagnosticCaseSetInvariantTests`（`FrozenSha256` 门槛）在聊天 worktree 里失败的原因。

### CV-03：夹具的字段删除依赖源码换行
C# 原始字符串字面量原样保留**源文件换行**；该 `.cs` 被 `core.autocrlf=true` 检出后，`MinimalIndex` 里是 CRLF，
于是旧写法 `MinimalIndex.Replace("  \"formatVersion\": 1,\n", string.Empty)` 匹配不到，字段根本没被删掉，
第 157 行 `Assert.DoesNotContain("formatVersion", json)` 在**调用产品解析器之前**就失败——
是夹具构造问题，不是产品行为缺陷。

## 3. 具体变更

### CV-01 `scripts/verify-source-snapshot.ps1`
1. 汇总新增字段：`uniqueExpectedFiles`、`overlapWithTracked`、`expectedMissing`（供 `verify-summary.json` 留证）。
2. 步骤 1：新增 `$expectedSet`（键为 `'/'` 分隔的仓库相对路径，Hashtable 天然去重）。
   tracked 逐项先做存在性检查（工作区缺失 → **结构化失败**，而不是让 `Copy-Item` 抛异常中断整条验证），
   再计入集合；白名单逐项已存在集合内时只累加 `$overlap`，不再抬高期望数；`trackedCopied` 改为实际复制计数。
3. 步骤 2：`$expectedCount = $expectedSet.Count`（旧为两数相加），并打印口径行
   `tracked + 白名单，重叠 N -> 唯一路径 M`。
4. 步骤 2 新增：**预期集合逐项存在于快照内**的显式断言，缺项列出路径并入失败项。
   （数量相等 + 泄漏为 0 本已能推出无缺失，这里把它变成可独立复验的断言与可读证据；去重绝不让缺失假通过。）
5. 未改动的门槛（全部保留）：白名单逐项缺失、白名单 SHA256 与源一致、通用泄漏检测、
   dev/ui/scripts/.github 候选项闭包断言、bin/obj 冷构建前提、PowerShell 脚本 BOM 编码守卫、
   两份 lock 的非 RID 形态守卫、全部步骤 gate（`RequireOutput`/`RequireMatch`/`RequirePositiveCount`）、
   超时与退出码传播、`-SkipCategories` 的 RED 强制非零、`KNOWN-UNLOCKED` 记录方式。
6. 注释：`.DESCRIPTION` 第 4 项、GREEN `.EXAMPLE`、CV-01 处说明，以及文件头「本轮修复（2026-10-13，CV-01）」段。

**预期数值**（当前 HEAD、`.gitattributes` 尚未跟踪故不进快照）：
`trackedCopied=302`、`whitelistCopied=163`、`overlapWithTracked=163`、`uniqueExpectedFiles=302`、
`snapshotFileCount=302`、`expectedMissing=[]`、`unexpectedFiles=[]`。

**必须披露的行为变化**：因为 163 项白名单全部已是 tracked，`-SkipCategories A` 在**新逻辑下不再减少快照文件数**，
数量门槛会通过，RED 的非零来自脚本末尾「RED 模式强制失败」。
（旧逻辑下该场景会因为错误的 465/442 期望而失败——那是重叠 bug 的假象，不是「缺文件」被检出。）
缺文件门槛本身没有削弱，证伪命令见 §5 的 C7；C7 同时验证新增的 tracked 缺失结构化失败分支。

### CV-02 `.gitattributes`
全文只有 1 条约束规则，且是精确路径（无通配、无目录批量、无全局 `* text=auto`）：

```
dev/PineglowLauncher.Tests/TestAssets/Diagnostics/diagnostic-cases.json text eol=lf
```

已用跨行字面量匹配探针确认该文件是 **LF 行尾**（规则行不会带 `\r` 而让 git 误读属性值）。
未修改冻结 JSON 一个字节，未改 `FrozenSha256`，未更新基准。

### CV-03 `dev/PineglowLauncher.Tests/InstancePackTests.cs`
`ParseModrinthIndex_DefaultsMissingFormatVersionToOne` 改为与换行无关的结构化删除：

- `using System.Text.Json.Nodes;`（net10.0 BCL，无需新包，lock 不变）；
- `JsonNode.Parse(MinimalIndex)?.AsObject() ?? throw new InvalidOperationException(...)`；
- `Assert.True(root.Remove("formatVersion"), "夹具必须含 formatVersion…")`（属性真被删除才继续）；
- `string json = root.ToJsonString();` → 保留 `Assert.DoesNotContain("formatVersion", json)`；
- 保留实际产品调用 `InstancePackService.ParseModrinthIndex(json)` 与结果断言 `Assert.Equal(1, manifest.FormatVersion)`。

静态核对：`Nullable=enable` 下 `?.` + `?? throw` 合法；`JsonObject.Remove(string)` 返回 `bool`；
`ToJsonString()` 会把非 ASCII 重新转义为 `\uXXXX`（JSON 语义不变，本用例只断言 `FormatVersion`）；
产品侧缺字段回退为 1（`InstancePackService.cs:378` 与 `:777–784` 的 `ReadInt(..., fallback: 1)`）。

## 4. 逐项验证状态

| 项 | 状态 | 依据 |
| --- | --- | --- |
| CV-01 集合/数量去重逻辑 | 已实现，`NOT_RUN`（未执行） | 代码静态复核；期望数值由基线证据推算（302/163/302） |
| CV-01 缺文件仍失败 | 已保留并强化，`NOT_RUN` | 新增 `expectedMissing` 断言 + tracked 缺失结构化失败；证伪命令 C7 |
| CV-01 泄漏/哈希/闭包/lock/BOM/退出码门槛 | 未改动（人工逐行核对） | 见 §3.5；`NOT_RUN` |
| CV-02 `.gitattributes` 规则 | 已创建；行尾 LF 已探针确认 | 检出后字节哈希 `NOT_RUN`，命令 C4 |
| CV-02 冻结数据/基准未改 | 已确认（未触碰该路径） | 只新建 `.gitattributes` |
| CV-03 夹具构造 | 已修改，`NOT_RUN`（未编译/未跑） | 语义静态核对；LF/CRLF 双跑命令 C5 |
| PowerShell 脚本语法解析 | `NOT_RUN` | 本会话无可用 shell；命令 C1 |
| 脚本编码（BOM） | **未验证，见 §7.1** | 探针显示这些工具会丢弃**文件开头**的 BOM |
| Node 305 / C# 941 / 冷启动 19 | `NOT_RUN` | 必须由 Codex 在新隔离快照执行（命令 C6） |
| 真实 WebView 九页巡检、真实游戏/账号 | `NOT_RUN` | 非本轮范围，且无桌面 shell |

## 5. 验证请求：必须由 Codex 用正常 shell 执行的精确命令

**C0（最高优先，决定 GREEN 能否开跑）脚本 BOM 字节检查**

```powershell
$p='D:\松光启动器\scripts\verify-source-snapshot.ps1'
$b=[System.IO.File]::ReadAllBytes($p)
'first3={0:X2}-{1:X2}-{2:X2}  length={3}' -f $b[0],$b[1],$b[2],$b.Length
# 期望 first3=EF-BB-BF。若 first3=23-52-65（即 "#Re"）说明 BOM 丢失，补回（只加 3 字节，其余内容不动）：
# [System.IO.File]::WriteAllBytes($p,[byte[]]((0xEF,0xBB,0xBF)+$b))
# 然后重跑 C0 确认 first3=EF-BB-BF，再跑 C1/C6。
```

**C1 语法解析（只解析，不执行）**

```powershell
$err=$null
[void][System.Management.Automation.Language.Parser]::ParseFile('D:\松光启动器\scripts\verify-source-snapshot.ps1',[ref]$null,[ref]$err)
if($err){$err|ForEach-Object{$_.Message}}else{'PARSE OK'}
```

**C2 唯一集合静态复算（应为 302 / 163 / 163 / 302）**

```powershell
$root='D:\松光启动器'
$tracked=@(& git -C $root ls-files | Where-Object {$_ -ne ''})
$wl=@(Get-Content -LiteralPath "$root\scripts\source-snapshot-inputs.txt" |
      ForEach-Object {$_.Trim()} |
      Where-Object {$_ -ne '' -and -not $_.StartsWith('#')} |
      ForEach-Object {$_ -replace '\\','/'})
$u=@{}; foreach($f in $tracked){$u[($f -replace '\\','/')]=$true}
$ov=0;  foreach($p in $wl){ if($u.ContainsKey($p)){$ov++} else {$u[$p]=$true} }
'tracked={0} whitelist={1} overlap={2} union={3}' -f $tracked.Count,$wl.Count,$ov,$u.Count
```

**C3 冻结 JSON 的当前工作区字节哈希**

```powershell
(Get-FileHash -LiteralPath 'D:\松光启动器\dev\PineglowLauncher.Tests\TestAssets\Diagnostics\diagnostic-cases.json' -Algorithm SHA256).Hash
# 期望 D9F55189FA05845BAC0403A058AA63912A725AFF61555BDF25A6865DD5AB45AF
```

**C4 `.gitattributes` 生效验证（须在它进入索引/提交之后）**

```powershell
$tmp=Join-Path $env:TEMP ('pineglow-cv02-'+[guid]::NewGuid().ToString('N'))
$rel='dev/PineglowLauncher.Tests/TestAssets/Diagnostics/diagnostic-cases.json'
git clone --no-hardlinks 'D:\松光启动器' $tmp
git -C $tmp config core.autocrlf true
Remove-Item -LiteralPath (Join-Path $tmp ($rel -replace '/','\')) -Force
git -C $tmp checkout -- $rel
git -C $tmp check-attr -a -- $rel          # 期望 text: set / eol: lf
(Get-FileHash -LiteralPath (Join-Path $tmp ($rel -replace '/','\')) -Algorithm SHA256).Hash
# 期望 D9F55189...45AF；无该属性时同一动作会得到 133E610B...85F（可作为对照）
Remove-Item -LiteralPath $tmp -Recurse -Force
```

**C5 CV-03：LF 与 CRLF 两种源码换行都必须通过**

```powershell
$proj='D:\松光启动器\dev\PineglowLauncher.Tests\Pineglow.Launcher.Tests.csproj'
dotnet test $proj -c Release --filter 'FullyQualifiedName~ParseModrinthIndex_DefaultsMissingFormatVersionToOne' --logger 'console;verbosity=normal'
# 要求 1/1 通过，且失败信息里不得再出现 DoesNotContain（即确实走到了 ParseModrinthIndex）
# CRLF 对照：在隔离副本里把该 .cs 转成 CRLF 后重跑同一条命令（不要改 D 盘源文件）：
#   $f='<隔离副本>\dev\PineglowLauncher.Tests\InstancePackTests.cs'
#   [IO.File]::WriteAllText($f, ([IO.File]::ReadAllText($f) -replace "`r?`n","`r`n"))
```

**C6 全量 GREEN 门槛（新隔离快照）**

```powershell
pwsh -NoProfile -File 'D:\松光启动器\scripts\verify-source-snapshot.ps1'
# 期望：exit 0；verify-summary.json 中 finalVerdict=PASS、uniqueExpectedFiles=302、overlapWithTracked=163、
# expectedMissing=[]、unexpectedFiles=[]、whitelistMissing=[]、snapshotFileCount=302、binObjFound=0；
# C# 941/941（失败 0、跳过 0）、Node 305/305（失败 0）、冷启动 19/19；
# publish 锁定探测仍记 KNOWN-UNLOCKED（已知未锁定边界，不阻断、也不等于通过）。
```

**C7 缺文件仍失败（隔离副本，绝不改 D 盘源仓库）**

```powershell
$src='D:\松光启动器'; $copy=Join-Path $env:TEMP ('pineglow-missing-'+[guid]::NewGuid().ToString('N'))
robocopy $src $copy /E /NFL /NDL /NJH /NJS /NP | Out-Null
Remove-Item -LiteralPath (Join-Path $copy 'dev\PineglowLauncher\Pineglow.Launcher.Services\AiEndpointPolicy.cs') -Force
pwsh -NoProfile -File (Join-Path $copy 'scripts\verify-source-snapshot.ps1') -RepoRoot $copy
# 期望：exit != 0，且失败项含「tracked 文件在工作区不存在，无法复制工作区版本」（结构化失败、仍写出汇总）。
# 也可以删除任一白名单项验证「白名单项在源仓库不存在」/「快照内缺失预期文件」两条分支。
Remove-Item -LiteralPath $copy -Recurse -Force
```

**C8 RED 保留（必须非零）**

```powershell
pwsh -NoProfile -File 'D:\松光启动器\scripts\verify-source-snapshot.ps1' -SkipCategories A
# 期望 exit != 0。注意 §3 披露的语义变化：白名单全部已是 tracked，因此该模式不再减少快照文件数。
```

## 6. 外部依赖

- 需要可用的 shell 宿主（本会话 `pwsh` 不可用）；C0–C8 全部未在我这里执行。
- Codex 的独立快照必须继续从 `D:\松光启动器` 工作区**按字节复制**（脚本行为未改）；
  不得用「git 重新检出该 JSON」来充当修复，也不得更新 `FrozenSha256`/文档声明来掩盖 CRLF 差异。
- `.gitattributes` 只有在**索引/提交**中才对 `git checkout`/`worktree add`/`clone` 生效；
  未跟踪状态下的新检出仍然会变 CRLF（C4 因此必须在该文件进入提交后执行）。
- 已存在的 CRLF 检出不会被自动重写（例如聊天 worktree）：需要在**提交后**删除该文件再重新检出
  （`git checkout -- <path>`）。这是 Codex 的动作，我没有执行，也没有触碰该 worktree。

## 7. 遗留问题

1. **脚本 BOM 编码风险（最高优先，必须先验）**：本会话文件工具对「文件首个字符」不可观测，
   且我实测到：把 `U+FEFF` 写在**文件开头**经 write 写入后，用含行首 `U+FEFF` 的 `old_string` 匹配失败；
   而把 `U+FEFF` 写在**第 2 行行首**（以及行中间）时，写入与匹配都成功。
   也就是说这些工具会丢弃「文件开头」的 BOM，而 `scripts/verify-source-snapshot.ps1` 含非 ASCII，
   其自身的编码守卫（步骤 2）要求 .ps1/.psm1 必须带 UTF-8 BOM。
   我已在最后一次 edit 中把 `U+FEFF` 重新放回文件开头，但**无法在本会话自证**。
   请先跑 C0；若不是 `EF-BB-BF`，按 C0 注释里的命令补回 3 字节即可（不改任何其它内容）。
   （`InstancePackTests.cs` 无此问题：Roslyn 对无 BOM 的 UTF-8 源文件按 UTF-8 解析。）
2. `.gitattributes` 目前是未跟踪新文件：CV-02 的检出保证要等它进入提交后才生效，C4 应在提交后执行。
3. CV-01 的 RED 语义变化已在 §3 披露：`-SkipCategories` 不再能构造「缺文件」场景，
   缺文件证伪改用隔离副本（C7）。
4. `.gitattributes` **没有**加入 `scripts/source-snapshot-inputs.txt`（我未被授权改白名单）。
   它未跟踪、也不在 dev/ui/scripts/.github 闭包根下，因此不影响本轮任何门槛；
   若 Codex 认为候选快照应包含它，请自行追加并重跑。
5. RID 锁定发布仍是 `NU1004` 的**已知未锁定边界**（非本轮范围，未改），不得表述为「全链 locked」。
6. 本会话未执行：Node/C# 全量、单文件冷启动、真实 WebView 九页巡检、真实微软登录/进入游戏世界、
   远程同 SHA CI 与发布。UI 设计（OpenDesign Cloud）尚未启动，本轮未做任何 UI 改动。
7. 原始 RED 位于聊天 worktree 的 `docs/current-version-audit-2026-10-13/baseline-gate`
   （D 盘只有 `product-baseline-gate`）；我没有创建、覆盖、改删或提交任何证据目录，
   也没有干预 D 盘正在运行的独立 baseline。

具备独立复验条件，等待 Codex 独立验收。
