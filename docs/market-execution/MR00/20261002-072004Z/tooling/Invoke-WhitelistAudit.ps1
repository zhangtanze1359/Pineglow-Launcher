#Requires -Version 5.1
<#
.SYNOPSIS
    MR00 步骤 2：逐项分类未跟踪文件，审查既有显式输入白名单是否仍然完整。

.DESCRIPTION
    只读分析。输出：
      - 全部已修改(M/A/D/R)文件清单
      - 未跟踪文件按类别计数
      - 白名单覆盖情况：哪些「疑似构建必需的未跟踪文件」不在白名单内（真实缺口）
      - 白名单内哪些条目已变成 tracked 或已不存在（清单陈旧）
#>
[CmdletBinding()]
param(
    [string] $SourceRoot = 'D:\松光启动器'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2.0

# --- 白名单解析（与 verify-source-snapshot.ps1 的规则一致）---
$inputsTxt = Join-Path $SourceRoot 'scripts/source-snapshot-inputs.txt'
$cats = [ordered]@{}
$current = '_'
$cats[$current] = New-Object System.Collections.Generic.List[string]
foreach ($raw in [System.IO.File]::ReadAllLines($inputsTxt)) {
    $line = $raw.Trim()
    if ($line -eq '') { continue }
    if ($line -match '^#\s*-+\s*类别\s*([A-Z])') { $current = $Matches[1]; if (-not $cats.Contains($current)) { $cats[$current] = New-Object System.Collections.Generic.List[string] }; continue }
    if ($line.StartsWith('#')) { continue }
    $cats[$current].Add(($line -replace '\\','/').Trim())
}
$whitelist = @{}
foreach ($k in $cats.Keys) { foreach ($p in $cats[$k]) { $whitelist[$p] = $k } }

# --- git 事实 ---
Push-Location $SourceRoot
try {
    $head = (& git rev-parse HEAD).Trim()
    $branch = (& git branch --show-current).Trim()
    $tracked = @(& git ls-files)
    $statusRaw = @(& git status --porcelain --untracked-files=all)
} finally { Pop-Location }

$trackedSet = @{}
foreach ($t in $tracked) { $trackedSet[($t -replace '\\','/')] = $true }

$modified = New-Object System.Collections.Generic.List[string]
$untracked = New-Object System.Collections.Generic.List[string]
foreach ($line in $statusRaw) {
    if ($line.Length -lt 3) { continue }
    $code = $line.Substring(0, 2)
    $path = $line.Substring(3).Trim() -replace '\\','/'
    if ($code -eq '??') { $untracked.Add($path) }
    else { $modified.Add(('{0}  {1}' -f $code, $path)) }
}

# --- 未跟踪文件分类 ---
function Classify {
    param([string] $P)
    if ($P -like 'docs/*')                                   { return 'docs-evidence' }
    if ($P -like 'dev/PineglowLauncher.Tests/TestResults/*')  { return 'testresults' }
    if ($P -like '.testagent/*' -or $P -like '.trae/*')       { return 'agent-meta' }
    if ($P -like 'scripts/_*' -or $P -like 'scripts/run-dify*') { return 'temp-scripts' }
    if ($P -like 'ui/tests/*') {
        if ($P -like '*.test.cjs') { return 'node-test' }
        return 'node-test-probe'
    }
    if ($P -like 'dev/PineglowLauncher.Tests/*.cs')           { return 'csharp-test' }
    if ($P -like 'dev/PineglowLauncher.Tests/TestAssets/*')   { return 'test-fixture' }
    if ($P -like 'dev/PineglowLauncher/*.csproj' -or $P -like 'dev/PineglowLauncher/packages.lock.json') { return 'proj-lock' }
    if ($P -like 'dev/PineglowLauncher/Pineglow.Launcher.Services/*.cs') { return 'prod-service-cs' }
    if ($P -like 'ui/vendor/*' -or $P -like 'ui/js/*')        { return 'prod-ui' }
    if ($P -eq 'global.json')                                 { return 'sdk-pin' }
    if ($P -like 'scripts/*')                                 { return 'scripts' }
    if ($P -match '\.(bak|tmp\d*|orig)$')                     { return 'junk-ext' }
    if ($P -match 'probe')                                    { return 'probe' }
    if ($P -like 'dev/*' -or $P -like 'ui/*')                 { return 'dev-other' }
    return 'other'
}

$byClass = [ordered]@{}
$whitelistedByClass = [ordered]@{}
foreach ($u in $untracked) {
    $c = Classify $u
    if (-not $byClass.Contains($c)) { $byClass[$c] = 0 }
    $byClass[$c]++
    if ($whitelist.ContainsKey($u)) {
        if (-not $whitelistedByClass.Contains($c)) { $whitelistedByClass[$c] = 0 }
        $whitelistedByClass[$c]++
    }
}

# --- 关键判定：疑似构建必需但不在白名单的未跟踪文件 ---
$buildCritical = New-Object System.Collections.Generic.List[string]
foreach ($u in $untracked) {
    $c = Classify $u
    if ($whitelist.ContainsKey($u)) { continue }
    if ($c -in @('prod-service-cs','prod-ui','node-test','csharp-test','test-fixture','proj-lock','sdk-pin')) {
        $buildCritical.Add(('{0,-16} {1}' -f $c, $u))
    }
}

# --- 白名单陈旧项 ---
$staleTracked = New-Object System.Collections.Generic.List[string]
$staleMissing = New-Object System.Collections.Generic.List[string]
foreach ($p in $whitelist.Keys) {
    if ($trackedSet.ContainsKey($p)) { $staleTracked.Add($p); continue }
    if (-not (Test-Path -LiteralPath (Join-Path $SourceRoot $p))) { $staleMissing.Add($p) }
}

Write-Host ''
Write-Host "HEAD      : $head" -ForegroundColor Cyan
Write-Host "branch    : $branch" -ForegroundColor Cyan
Write-Host "tracked   : $($tracked.Count)" -ForegroundColor Cyan
Write-Host "modified  : $($modified.Count)" -ForegroundColor Cyan
Write-Host "untracked : $($untracked.Count)" -ForegroundColor Cyan
Write-Host "whitelist : $($whitelist.Count)" -ForegroundColor Cyan
Write-Host ''
Write-Host '--- 未跟踪文件分类（总数 / 其中已在白名单）---' -ForegroundColor Cyan
foreach ($k in $byClass.Keys) {
    $w = if ($whitelistedByClass.Contains($k)) { $whitelistedByClass[$k] } else { 0 }
    Write-Host ("  {0,-18} {1,6}   白名单内 {2,4}" -f $k, $byClass[$k], $w)
}
Write-Host ''
Write-Host "--- 疑似构建必需、但不在白名单的未跟踪文件：$($buildCritical.Count) 项 ---" -ForegroundColor Yellow
foreach ($b in $buildCritical) { Write-Host "  $b" -ForegroundColor Yellow }
Write-Host ''
Write-Host "--- 白名单条目已变 tracked（陈旧）：$($staleTracked.Count) ---" -ForegroundColor DarkGray
foreach ($s in $staleTracked) { Write-Host "  $s" }
Write-Host "--- 白名单条目源已不存在：$($staleMissing.Count) ---" -ForegroundColor DarkGray
foreach ($s in $staleMissing) { Write-Host "  $s" }
Write-Host ''
Write-Host '--- 已修改(M/A/D/R)文件清单 ---' -ForegroundColor Cyan
foreach ($m in ($modified | Sort-Object)) { Write-Host "  $m" }

# 落盘供后续引用
$out = [ordered]@{
    head = $head; branch = $branch
    trackedCount = $tracked.Count
    modifiedCount = $modified.Count
    untrackedCount = $untracked.Count
    whitelistCount = $whitelist.Count
    untrackedByClass = $byClass
    whitelistedByClass = $whitelistedByClass
    buildCriticalNotWhitelisted = $buildCritical
    staleWhitelistNowTracked = $staleTracked
    staleWhitelistMissing = $staleMissing
    modified = $modified
}
$script:OutJson = $out
$global:AnalysisOut = $out
