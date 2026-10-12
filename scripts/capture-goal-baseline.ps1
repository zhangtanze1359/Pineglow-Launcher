<#
capture-goal-baseline.ps1  (LONG-QUALITY-GOAL, G0/G4)

Purpose
  Stage=baseline : freeze the SourceRoot input set (every tracked file at its working-tree
                   version + every untracked path listed in scripts/source-snapshot-inputs.txt),
                   hash each entry (SHA256 + size) and write input-manifest.json.
                   With -Materialize the same set is copied into a brand-new ExecutionRoot
                   (GUID temp copy) and each copy is re-hashed and compared.
  Stage=terminal : capture the terminal snapshot (HEAD, full git status, input SHA).
                   This MUST run before the final seal; after sealing only out-of-package
                   paths may be written.

Outputs (new files only; never overwrites historical evidence)
  <EvidenceRoot>\g0\             Stage=baseline
  <EvidenceRoot>\terminal\       Stage=terminal
    input-manifest.json | source-after-manifest.json | source-status-*.txt | git-head.txt
    toolchain.json | copy-verification.json | capture-summary-*.txt

Exit codes: 0 = success, 1 = failure (missing input / SHA mismatch / copy failure / write failure).
This script performs no git write operation: no commit, no stage, no restore, no clean.
ASCII-only source so Windows PowerShell 5.1 and PowerShell 7 parse it identically.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][ValidateSet('baseline', 'terminal')][string]$Stage,
    [Parameter(Mandatory = $true)][string]$EvidenceRoot,
    [string]$SourceRoot = '',
    [string]$ExecutionRoot = '',
    [switch]$Materialize,
    [string]$InputListName = 'source-snapshot-inputs.txt',
    [string]$OutSubdir = ''
)

$ErrorActionPreference = 'Stop'
$script:Failures = New-Object System.Collections.ArrayList

function Add-Failure([string]$reason, $detail) {
    [void]$script:Failures.Add([ordered]@{ reason = $reason; detail = $detail })
    Write-Host "FAIL: $reason :: $detail" -ForegroundColor Red
}

function Write-JsonFile([string]$Path, $Object) {
    $dir = Split-Path -Parent $Path
    if ($dir -and -not (Test-Path -LiteralPath $dir)) { New-Item -ItemType Directory -Force -Path $dir | Out-Null }
    $json = $Object | ConvertTo-Json -Depth 12
    $enc = New-Object System.Text.UTF8Encoding($false)
    [System.IO.File]::WriteAllText($Path, $json + "`n", $enc)
}

function Write-TextFile([string]$Path, [string]$Text) {
    $dir = Split-Path -Parent $Path
    if ($dir -and -not (Test-Path -LiteralPath $dir)) { New-Item -ItemType Directory -Force -Path $dir | Out-Null }
    $enc = New-Object System.Text.UTF8Encoding($false)
    [System.IO.File]::WriteAllText($Path, $Text, $enc)
}

function Get-Sha256([string]$Path) {
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToUpperInvariant()
}

function Get-RelPath([string]$Path) {
    return ($Path -replace '\\', '/')
}

function Read-InputWhitelist([string]$Path) {
    $list = New-Object System.Collections.ArrayList
    if (-not (Test-Path -LiteralPath $Path)) { return $list }
    foreach ($raw in [System.IO.File]::ReadAllLines($Path)) {
        $line = $raw.Trim()
        if ($line.Length -eq 0) { continue }
        if ($line.StartsWith('#')) { continue }
        [void]$list.Add((Get-RelPath $line))
    }
    return $list
}

function Get-TrackedFiles([string]$Root) {
    $out = & git -C $Root -c core.quotepath=false ls-files 2>&1
    if ($LASTEXITCODE -ne 0) { throw ("git ls-files failed: " + ($out -join ' ')) }
    return @($out | Where-Object { $_ -and $_.ToString().Trim().Length -gt 0 } | ForEach-Object { Get-RelPath $_.ToString().Trim() })
}

function Test-GoalTooling([string]$Rel) {
    if ($Rel -eq 'scripts/audit-goal-evidence.py') { return $true }
    if ($Rel -eq 'scripts/capture-goal-baseline.ps1') { return $true }
    if ($Rel -eq 'scripts/run-goal-validation.ps1') { return $true }
    if ($Rel -eq 'scripts/tests/audit-goal-evidence.test.py') { return $true }
    if ($Rel -eq 'scripts/tests/goal-validation.test.cjs') { return $true }
    if ($Rel -eq 'ui/tests/goal-core-flow.test.cjs') { return $true }
    if ($Rel -eq 'dev/PineglowLauncher.Tests/GoalOrdinaryFlowTests.cs') { return $true }
    return $false
}

function Get-Toolchain() {
    $t = [ordered]@{}
    try { $t.dotnet = ((& dotnet --version 2>&1 | Select-Object -First 1) -join '') } catch { $t.dotnet = 'unavailable' }
    try { $t.node = ((& node --version 2>&1 | Select-Object -First 1) -join '') } catch { $t.node = 'unavailable' }
    try { $t.python = ((& python --version 2>&1 | Select-Object -First 1) -join '') } catch { $t.python = 'unavailable' }
    try { $t.pythonExe = (Get-Command python -ErrorAction Stop).Source } catch { $t.pythonExe = 'unavailable' }
    $t.powershell = $PSVersionTable.PSVersion.ToString()
    $t.os = [System.Environment]::OSVersion.VersionString
    $t.machine = $env:COMPUTERNAME
    return $t
}

# ---------------------------------------------------------------- main

if ([string]::IsNullOrWhiteSpace($SourceRoot)) { throw '-SourceRoot is required (pass the absolute product source root).' }
$SourceRoot = (Resolve-Path -LiteralPath $SourceRoot).Path
$EvidenceRoot = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($EvidenceRoot)
if (-not (Test-Path -LiteralPath $EvidenceRoot)) { New-Item -ItemType Directory -Force -Path $EvidenceRoot | Out-Null }
$stageDir = Join-Path $EvidenceRoot $(if ($OutSubdir) { $OutSubdir } else { if ($Stage -eq 'baseline') { 'g0' } else { 'terminal' } })
New-Item -ItemType Directory -Force -Path $stageDir | Out-Null

$nowLocal = (Get-Date).ToString('yyyy-MM-ddTHH:mm:sszzz')
$nowUtc = (Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ')

# git state (read-only)
$headText = ((& git -C $SourceRoot rev-parse HEAD 2>&1 | Select-Object -First 1) -join '')
$branchText = ((& git -C $SourceRoot rev-parse --abbrev-ref HEAD 2>&1 | Select-Object -First 1) -join '')
$statusLines = @(& git -C $SourceRoot status --porcelain=v1 -uall 2>&1)
$statusPath = Join-Path $stageDir ("source-status-{0}.txt" -f $Stage)
Write-TextFile $statusPath (($statusLines -join "`n") + "`n")
Write-TextFile (Join-Path $stageDir 'git-head.txt') ("HEAD=$headText`nBRANCH=$branchText`n")

# input set
$tracked = Get-TrackedFiles $SourceRoot
$whitelistPath = Join-Path $SourceRoot ('scripts\' + $InputListName)
$whitelist = Read-InputWhitelist $whitelistPath
$trackedSet = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
foreach ($t in $tracked) { [void]$trackedSet.Add($t) }

$inputs = New-Object System.Collections.ArrayList
$seen = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
foreach ($rel in ($tracked | Sort-Object)) {
    if ($seen.Add($rel)) { [void]$inputs.Add([ordered]@{ path = $rel; origin = 'tracked' }) }
}
$whitelistDuplicates = New-Object System.Collections.ArrayList
foreach ($rel in $whitelist) {
    if ($trackedSet.Contains($rel)) { [void]$whitelistDuplicates.Add($rel); continue }
    if ($seen.Add($rel)) {
        $origin = if (Test-GoalTooling $rel) { 'goal-tooling' } else { 'whitelist' }
        [void]$inputs.Add([ordered]@{ path = $rel; origin = $origin })
    }
}
if ($whitelistDuplicates.Count -gt 0) {
    Write-JsonFile (Join-Path $stageDir 'whitelist-duplicates.json') ([ordered]@{
            note = 'whitelist rows that are already tracked (deduplicated, not a failure)'
            paths = @($whitelistDuplicates)
        })
}

$entries = New-Object System.Collections.ArrayList
$missing = New-Object System.Collections.ArrayList
$totalBytes = 0
foreach ($item in $inputs) {
    $abs = Join-Path $SourceRoot ($item.path -replace '/', '\')
    if (-not (Test-Path -LiteralPath $abs -PathType Leaf)) {
        [void]$missing.Add($item.path)
        [void]$entries.Add([ordered]@{ path = $item.path; origin = $item.origin; sha256 = $null; sizeBytes = $null; missing = $true })
        continue
    }
    $fi = Get-Item -LiteralPath $abs
    $sha = Get-Sha256 $abs
    $totalBytes += $fi.Length
    [void]$entries.Add([ordered]@{ path = $item.path; origin = $item.origin; sha256 = $sha; sizeBytes = $fi.Length; missing = $false })
}
if ($missing.Count -gt 0) { Add-Failure 'inputs missing on disk' @($missing) }

$originCounts = [ordered]@{ tracked = 0; whitelist = 0; 'goal-tooling' = 0 }
foreach ($e in $entries) {
    if ($originCounts.Contains($e.origin)) { $originCounts[$e.origin] = $originCounts[$e.origin] + 1 }
    else { $originCounts[$e.origin] = 1 }
}

$manifestName = if ($Stage -eq 'baseline') { 'input-manifest.json' } else { 'source-after-manifest.json' }
$manifestPath = Join-Path $stageDir $manifestName
$manifest = [ordered]@{
    schemaVersion  = 1
    scope          = 'windows-local-offline-quality'
    stage          = $Stage
    generatedLocal = $nowLocal
    generatedUtc   = $nowUtc
    sourceRoot     = $SourceRoot
    executionRoot  = $ExecutionRoot
    head           = $headText
    branch         = $branchText
    inputList      = 'scripts/' + $InputListName
    counts         = [ordered]@{
        total            = $entries.Count
        tracked          = $originCounts['tracked']
        whitelist        = $originCounts['whitelist']
        goalTooling      = $originCounts['goal-tooling']
        missing          = $missing.Count
        whitelistSkipped = $whitelistDuplicates.Count
    }
    totalBytes     = $totalBytes
    inputs         = $entries
}
Write-JsonFile $manifestPath $manifest

$summaryLines = New-Object System.Collections.ArrayList
[void]$summaryLines.Add("stage=$Stage")
[void]$summaryLines.Add("generatedLocal=$nowLocal")
[void]$summaryLines.Add("head=$headText branch=$branchText")
[void]$summaryLines.Add("inputs=$($entries.Count) tracked=$($originCounts['tracked']) whitelist=$($originCounts['whitelist']) goalTooling=$($originCounts['goal-tooling']) missing=$($missing.Count)")
[void]$summaryLines.Add("totalBytes=$totalBytes")
[void]$summaryLines.Add("gitStatusLines=$($statusLines.Count) -> $statusPath")
[void]$summaryLines.Add("manifest=$manifestPath")
$summaryPath = Join-Path $stageDir ("capture-summary-{0}.txt" -f $Stage)
Write-TextFile $summaryPath (($summaryLines -join "`n") + "`n")
Write-JsonFile (Join-Path $stageDir 'toolchain.json') (Get-Toolchain)

# optional materialization of the execution copy
if ($Materialize) {
    if ([string]::IsNullOrWhiteSpace($ExecutionRoot)) {
        Add-Failure 'Materialize requires -ExecutionRoot' ''
    }
    else {
        $full = [System.IO.Path]::GetFullPath($ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($ExecutionRoot)).TrimEnd('\')
        $tempRoot = [System.IO.Path]::GetFullPath($env:TEMP).TrimEnd('\')
        if (-not $full.StartsWith($tempRoot + '\', [StringComparison]::OrdinalIgnoreCase)) {
            Add-Failure 'ExecutionRoot is outside TEMP' $full
        }
        elseif (Test-Path -LiteralPath $full) {
            Add-Failure 'ExecutionRoot already exists (must be a new GUID path)' $full
        }
        else {
            $copyEntries = New-Object System.Collections.ArrayList
            foreach ($e in $entries) {
                if ($e.missing) { continue }
                $src = Join-Path $SourceRoot ($e.path -replace '/', '\')
                $dst = Join-Path $full ($e.path -replace '/', '\')
                $dstDir = Split-Path -Parent $dst
                if (-not (Test-Path -LiteralPath $dstDir)) { New-Item -ItemType Directory -Force -Path $dstDir | Out-Null }
                Copy-Item -LiteralPath $src -Destination $dst -Force
                $sha = Get-Sha256 $dst
                $ok = ($sha -eq $e.sha256)
                if (-not $ok) { Add-Failure 'copied file SHA mismatch' $e.path }
                [void]$copyEntries.Add([ordered]@{ path = $e.path; expectedSha256 = $e.sha256; actualSha256 = $sha; ok = $ok })
            }
            $verify = [ordered]@{
                schemaVersion = 1
                generatedUtc  = $nowUtc
                sourceRoot    = $SourceRoot
                executionRoot = $full
                copied        = $copyEntries.Count
                mismatched    = @($copyEntries | Where-Object { -not $_.ok }).Count
                entries       = $copyEntries
            }
            Write-JsonFile (Join-Path $stageDir 'copy-verification.json') $verify
            Write-JsonFile (Join-Path $stageDir 'execution-root.json') ([ordered]@{
                    schemaVersion = 1
                    sourceRoot    = $SourceRoot
                    executionRoot = $full
                    createdUtc    = $nowUtc
                    copied        = $copyEntries.Count
                })
            [void]$summaryLines.Add("materializedExecutionRoot=$full copied=$($copyEntries.Count)")
            Write-TextFile $summaryPath (($summaryLines -join "`n") + "`n")
        }
    }
}

if ($script:Failures.Count -gt 0) {
    Write-JsonFile (Join-Path $stageDir 'capture-failures.json') ([ordered]@{ stage = $Stage; failures = $script:Failures })
    Write-Host ("capture-goal-baseline {0}: {1} failure(s)" -f $Stage, $script:Failures.Count) -ForegroundColor Red
    exit 1
}
Write-Host ("capture-goal-baseline {0}: OK inputs={1} -> {2}" -f $Stage, $entries.Count, $stageDir) -ForegroundColor Green
exit 0
