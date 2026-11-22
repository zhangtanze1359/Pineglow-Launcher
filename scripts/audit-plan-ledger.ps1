# 计划台账一致性审计（可复跑）
#
# 为什么需要：台账是"计划 ↔ 证据"的唯一索引。它一旦与磁盘不一致，
# 复验方就找不到证据、或以为某个包还没做（其实做了）。这类不一致不会自己暴露，
# 必须被机器检查。
#
# 检查四项：
#   1. §8 状态表里的状态词是否都在计划允许的七个词之内；
#   2. 每个声称"证据就绪"的包，能否在证据根下定位到对应的 run 目录；
#   3. 是否存在"有证据目录但台账没声称"的孤儿目录；
#   4. 台账里引用的每个 run-id 是否真实存在。
#
# 用法（本机只有 Windows PowerShell 5.1，没有 pwsh）：
#   powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\audit-plan-ledger.ps1
#
# 证据树默认取 `-RepoRoot` 下的 `docs/market-execution`（OPEN-05：旧默认指向已不存在的历史交付根
# `C:\Users\Administrator\.codex\worktrees\ed41\...`）。显式 `-EvidenceRoot` 优先，
# 且**必须存在**：显式给错就当场失败，不静默回退（否则「审计了另一棵树」会被误读成「审计了给定的树」）。
#
# 三档入口判定（r2 补强：用参数绑定状态区分「省略」与「显式给了空值」）：
#   1. 省略 `-EvidenceRoot` → 用 `-RepoRoot` 下的 `docs/market-execution`；必须存在，否则 `[DEFAULT_ROOT_MISSING]`。
#   2. 显式空字符串/全空白 → **参数错误** `[EVIDENCE_ROOT_BLANK]`（不是"用默认"）。
#   3. 显式非空值 → 必须是已存在目录；不存在 `[EVIDENCE_ROOT_MISSING]`，
#      存在但是普通文件 `[EVIDENCE_ROOT_NOT_DIRECTORY]`，都在审计之前拒绝、都不回退。

param(
    [string]$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path,
    # OPEN-05：默认值只用于类型稳定；「是否显式指定」一律看 $PSBoundParameters。
    [string]$EvidenceRoot = '',
    [string]$PlanPath = ''
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($PlanPath)) { $PlanPath = Join-Path $RepoRoot 'docs\market-task-plan-2026-10-03.md' }

$defaultEvidenceRoot = Join-Path $RepoRoot 'docs\market-execution'
$evidenceRootExplicitlyBound = $PSBoundParameters.ContainsKey('EvidenceRoot')

if (-not $evidenceRootExplicitlyBound) {
    # ① 省略参数：这是唯一允许落到默认根的情形。
    $evidenceRootSource = '默认（-RepoRoot\docs\market-execution）'
    $EvidenceRoot = $defaultEvidenceRoot
    if (-not (Test-Path -LiteralPath $EvidenceRoot -PathType Container)) {
        throw ("[DEFAULT_ROOT_MISSING] 找不到证据树：默认根目录不存在 {0}（默认根取自 -RepoRoot = {1}；请用 -EvidenceRoot 指定实际证据树）" -f $EvidenceRoot, $RepoRoot)
    }
}
elseif ([string]::IsNullOrWhiteSpace($EvidenceRoot)) {
    # ② 显式给了空/空白：参数错误。把它当"省略"会让空白值静默变成另一棵树。
    throw ("[EVIDENCE_ROOT_BLANK] 参数错误：-EvidenceRoot 被显式指定，但值是空字符串或全空白（[{0}]）。省略该参数才会用默认根 {1}；要指定证据树请给出真实目录。" -f $EvidenceRoot, $defaultEvidenceRoot)
}
else {
    # ③ 显式给了非空值：必须是已存在的目录，审计之前就拒绝。
    $evidenceRootSource = '显式 -EvidenceRoot'
    if (-not (Test-Path -LiteralPath $EvidenceRoot -PathType Container)) {
        if (Test-Path -LiteralPath $EvidenceRoot) {
            throw ("[EVIDENCE_ROOT_NOT_DIRECTORY] 显式指定的证据树不是目录：{0}（该路径存在，但是普通文件或其他非容器对象；-EvidenceRoot 必须是目录）" -f $EvidenceRoot)
        }
        throw ("[EVIDENCE_ROOT_MISSING] 显式指定的证据树不存在：{0}（显式路径不回退到默认根 {1}；要用默认值请省略 -EvidenceRoot）" -f $EvidenceRoot, $defaultEvidenceRoot)
    }
}

function Write-Head([string]$text) {
    Write-Host ''
    Write-Host "== $text" -ForegroundColor Cyan
}

$plan = Get-Content -LiteralPath $PlanPath -Encoding UTF8
$dirs = Get-ChildItem -LiteralPath $EvidenceRoot -Directory
$failures = 0
Write-Host ("  证据树 = {0}" -f $EvidenceRoot)
Write-Host ("  来源   = {0}" -f $evidenceRootSource)

# §8 状态表区间：从表头"| ID | 任务 | 优先级 |"开始，到出现空行或下一个二级标题为止
$start = -1
for ($i = 0; $i -lt $plan.Count; $i++) {
    if ($plan[$i] -match '^\|\s*ID\s*\|\s*任务\s*\|') { $start = $i; break }
}
if ($start -lt 0) { throw '找不到 §8 状态表表头' }
$end = $plan.Count - 1
for ($i = $start + 2; $i -lt $plan.Count; $i++) {
    if ($plan[$i] -notmatch '^\|') { $end = $i - 1; break }
}
Write-Head ("台账审计：状态表行区间 {0}-{1}" -f ($start + 1), ($end + 1))

$rows = @()
for ($i = $start + 2; $i -le $end; $i++) {
    if ($plan[$i] -match '^\|\s*\*{0,2}(MR\d{2}|W\d)\*{0,2}\s*\|') {
        $cols = $plan[$i].Split('|')
        $rows += [pscustomobject]@{ Line = $i + 1; Id = $cols[1].Trim().Trim('*'); Status = $cols[$cols.Count - 2].Trim() }
    }
}
Write-Host ("  解析到 {0} 个包行" -f $rows.Count)

Write-Head '检查 1：状态词是否合法'
$allowed = '未授权|已授权待执行|执行中|证据就绪|独立复验通过|暂不放行|用户已批准'
foreach ($r in $rows) {
    if ($r.Status -notmatch $allowed) {
        Write-Host ("  [非法状态词] 行{0} {1} → {2}" -f $r.Line, $r.Id, $r.Status.Substring(0, [Math]::Min(70, $r.Status.Length))) -ForegroundColor Red
        $failures++
    }
}
if ($failures -eq 0) { Write-Host '  全部合法' }

Write-Head '检查 2：声称"证据就绪"的包能否定位到证据目录'
# W1/W2/W3 共用同一个合并 run 目录
$dirMap = @{ 'W1' = 'W1-W3-route-closure'; 'W2' = 'W1-W3-route-closure'; 'W3' = 'W1-W3-route-closure' }
$before = $failures
foreach ($r in $rows) {
    if ($r.Status -notmatch '证据就绪') { continue }
    $want = if ($dirMap.ContainsKey($r.Id)) { $dirMap[$r.Id] } else { $r.Id }
    $hit = $dirs | Where-Object { $_.Name -like "$want*" }
    if (-not $hit) {
        Write-Host ("  [缺证据目录] {0}（期望 {1}*）" -f $r.Id, $want) -ForegroundColor Red
        $failures++
    }
}
if ($failures -eq $before) { Write-Host '  全部可定位' }

Write-Head '检查 3：孤儿证据目录（有目录但台账未声称）'
$claimed = @()
foreach ($r in $rows) { if ($r.Status -match '证据就绪') { $claimed += $r.Id } }
$claimed += @('MG0', 'MG1', 'MG2', 'MG3', 'gap-register', 'MG-audit-evidence-tree', 'MG-audit-claimed-tests')   # 跨里程碑产物不在包行里（新增时必须在这里登记）
$before = $failures
foreach ($d in $dirs) {
    $matched = $false
    foreach ($c in $claimed) {
        $prefix = if ($dirMap.ContainsKey($c)) { $dirMap[$c] } else { $c }
        if ($d.Name -like "$prefix*") { $matched = $true; break }
    }
    if (-not $matched) {
        Write-Host ("  [孤儿证据目录] {0}" -f $d.Name) -ForegroundColor Red
        Write-Host '    提示：跨里程碑产物（复验包/登记表等）不在包行里，必须在上面的 claimed 列表显式登记。' -ForegroundColor DarkYellow
        $failures++
    }
}
if ($failures -eq $before) { Write-Host '  无孤儿' }

Write-Head '检查 4：台账引用的 run-id 是否真实存在'
$runIds = @()
foreach ($d in $dirs) { $runIds += (Get-ChildItem -LiteralPath $d.FullName -Directory | ForEach-Object Name) }
$before = $failures
foreach ($m in [regex]::Matches(($plan -join "`n"), '`(2026\d{6}-\d{6}Z)`')) {
    $rid = $m.Groups[1].Value
    if ($runIds -notcontains $rid) {
        Write-Host ("  [引用了不存在的 run] {0}" -f $rid) -ForegroundColor Red
        $failures++
    }
}
if ($failures -eq $before) { Write-Host ("  引用的 run-id 全部存在（共 {0} 个 run 目录）" -f $runIds.Count) }

Write-Head '结论'
if ($failures -eq 0) {
    Write-Host '台账与磁盘证据一致：状态词合法、证据可定位、无孤儿目录、run-id 真实。' -ForegroundColor Green
    exit 0
}
Write-Host ("发现 {0} 处不一致；台账是复验的索引，必须先修台账再谈复验。" -f $failures) -ForegroundColor Yellow
exit 1
