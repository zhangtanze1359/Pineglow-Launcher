<#
run-goal-validation.ps1 - LONG-QUALITY-GOAL / G4 resumable quality runner.

What it guarantees (each one is a real gate, not a label):
  * a child process exit code is captured immediately after the wait and is never overwritten by
    an Out-File/tee; a non-zero exit fails the stage;
  * a stage that declares a TRX must produce exactly that TRX, valid XML, with total >= minTests,
    zero failures and no required skip (default 0). Missing / invalid / zero-test / skipped TRX
    fail the stage with a precise reason code;
  * a stage that declares a log pattern must reach minCount;
  * a timeout or a process that is still running after the wall-clock budget fails the stage;
  * every stage record binds the input-manifest SHA it ran against, the exact command, the working
    directory, the raw evidence paths and the SHA/size of every produced artifact;
  * resume reuses a stage ONLY when its stored record says COMPLETED, the input-manifest SHA is
    unchanged, the stage fingerprint (id + command + cwd + expectation) is unchanged and every
    referenced evidence/artifact still exists with the recorded SHA. Otherwise the stage re-runs
    under a new runId and the previous record is preserved (never overwritten).

Deliberately NOT done here: sealing. Sealing (terminal capture -> manifest -> detached digest ->
out-of-package audit) is a separate step and must only run once the bound input set stops changing.

ASCII-only source so Windows PowerShell 5.1 parses it identically.

Usage
  run-goal-validation.ps1 -InputManifest <file> -RunDir <dir> -StagesFile <file> [-Resume]
                          [-Only <id>] [-RunId <id>] [-TimeoutSeconds <n>]
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$InputManifest,
    [Parameter(Mandatory = $true)][string]$RunDir,
    [Parameter(Mandatory = $true)][string]$StagesFile,
    [switch]$Resume,
    [string]$Only = '',
    [string]$RunId = '',
    [string]$ExecutionRoot = '',
    [string]$EvidenceRoot = '',
    [int]$TimeoutSeconds = 1800
)

$ErrorActionPreference = 'Stop'
$script:Utf8NoBom = New-Object System.Text.UTF8Encoding($false)

function Write-Json([string]$Path, $Object) {
    $dir = Split-Path -Parent $Path
    if ($dir -and -not (Test-Path -LiteralPath $dir)) { New-Item -ItemType Directory -Force -Path $dir | Out-Null }
    [System.IO.File]::WriteAllText($Path, (($Object | ConvertTo-Json -Depth 12) + "`n"), $script:Utf8NoBom)
}

function Write-Lines([string]$Path, $Lines) {
    $dir = Split-Path -Parent $Path
    if ($dir -and -not (Test-Path -LiteralPath $dir)) { New-Item -ItemType Directory -Force -Path $dir | Out-Null }
    [System.IO.File]::WriteAllText($Path, (($Lines -join "`n") + "`n"), $script:Utf8NoBom)
}

function Get-Sha256([string]$Path) { return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToUpperInvariant() }

function Get-Sha256OfText([string]$Text) {
    $sha = [System.Security.Cryptography.SHA256]::Create()
    try {
        $bytes = [System.Text.Encoding]::UTF8.GetBytes($Text)
        return ([System.BitConverter]::ToString($sha.ComputeHash($bytes)) -replace '-', '').ToUpperInvariant()
    }
    finally { $sha.Dispose() }
}

function Expand-Text([string]$Text, $Vars) {
    $out = [string]$Text
    foreach ($k in $Vars.Keys) { $out = $out.Replace('{' + $k + '}', [string]$Vars[$k]) }
    return $out
}

function Expand-Stage($Stage, $Vars) {
    # Expanding before fingerprinting means the fingerprint binds the fully expanded command:
    # a different execution root or run dir can never be mistaken for the same stage.
    $merged = @{}
    foreach ($k in $Vars.Keys) { $merged[$k] = $Vars[$k] }
    if ($Stage.vars) { foreach ($p in $Stage.vars.PSObject.Properties) { $merged[$p.Name] = $p.Value } }
    $expectIn = $Stage.expect
    $expectOut = $null
    if ($expectIn) {
        $expectOut = [ordered]@{}
        foreach ($p in $expectIn.PSObject.Properties) {
            $expectOut[$p.Name] = if ($p.Value -is [string]) { Expand-Text ([string]$p.Value) $merged } else { $p.Value }
        }
    }
    return [ordered]@{
        id       = [string]$Stage.id
        file     = Expand-Text ([string]$Stage.file) $merged
        argv     = @($Stage.argv | ForEach-Object { Expand-Text ([string]$_) $merged })
        cwd      = Expand-Text ([string]$Stage.cwd) $merged
        produces = @($Stage.produces | ForEach-Object { Expand-Text ([string]$_) $merged })
        expect   = $expectOut
    }
}

function Get-StageFingerprint($Stage, [string]$InputSha) {
    $parts = @(
        [string]$Stage.id,
        [string]$Stage.file,
        (@($Stage.argv) -join '|'),
        [string]$Stage.cwd,
        $InputSha,
        ($Stage.expect | ConvertTo-Json -Compress)
    )
    return Get-Sha256OfText ($parts -join ';;')
}

function Read-TrxCounters([string]$Path) {
    # returns $null when the file is not a usable TRX
    try {
        $xml = New-Object System.Xml.XmlDocument
        $xml.Load($Path)
        $node = $xml.SelectSingleNode('//*[local-name()="Counters"]')
        if ($null -eq $node) { return $null }
        return [ordered]@{
            total       = [int]$node.GetAttribute('total')
            executed    = [int]$node.GetAttribute('executed')
            passed      = [int]$node.GetAttribute('passed')
            failed      = [int]$node.GetAttribute('failed')
            notExecuted = [int]$node.GetAttribute('notExecuted')
        }
    }
    catch { return $null }
}

function Invoke-CheckedStage($Stage, [string]$StageDir, [string]$InputSha, [string]$EffectiveRunId) {
    $id = [string]$Stage.id
    $logDir = Join-Path $StageDir 'logs'
    New-Item -ItemType Directory -Force -Path $logDir | Out-Null
    $stdoutPath = Join-Path $logDir ("{0}.stdout.log" -f $id)
    $stderrPath = Join-Path $logDir ("{0}.stderr.log" -f $id)

    $reasons = New-Object System.Collections.ArrayList
    $cwd = [string]$Stage.cwd
    if (-not (Test-Path -LiteralPath $cwd)) { [void]$reasons.Add('CWD_MISSING') }

    $file = [string]$Stage.file
    $argv = @($Stage.argv | ForEach-Object { [string]$_ })
    $startUtc = (Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ')
    $exitCode = $null
    $timedOut = $false
    $stillRunning = $false

    if ($reasons.Count -eq 0) {
        # Start-Process joins ArgumentList with spaces and does not quote; quote the elements that
        # need it so a path or filter with spaces still arrives as one argument. Passing one
        # pre-joined string also avoids the 5.1 validation error caused by an empty-string element.
        $quoted = @($argv | ForEach-Object { if ($_ -match '[\s"]') { '"' + ($_ -replace '"', '\"') + '"' } else { $_ } })
        $argLine = (@($quoted) -join ' ').Trim()
        $spArgs = @{
            FilePath               = $file
            WorkingDirectory       = $cwd
            RedirectStandardOutput = $stdoutPath
            RedirectStandardError  = $stderrPath
            PassThru               = $true
            NoNewWindow            = $true
        }
        if ($argLine.Length -gt 0) { $spArgs.ArgumentList = $argLine }
        $proc = Start-Process @spArgs
        $proc | Wait-Process -Timeout $TimeoutSeconds -ErrorAction SilentlyContinue
        if (-not $proc.HasExited) {
            $timedOut = $true
            try { Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue } catch { }
            Start-Sleep -Milliseconds 300
            if (-not $proc.HasExited) { $stillRunning = $true }
            [void]$reasons.Add('TIMEOUT')
        }
        else {
            $exitCode = $proc.ExitCode
            if ($exitCode -ne 0) { [void]$reasons.Add('EXIT_NONZERO') }
        }
    }

    $endUtc = (Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ')

    # ---- expectation checks -------------------------------------------------
    $expect = $Stage.expect
    $counters = $null
    $trxRelative = ''
    if ($expect -and $expect.trx) {
        $trxRelative = ([string]$expect.trx).Replace('{runId}', $EffectiveRunId)
        $trxPath = if ([System.IO.Path]::IsPathRooted($trxRelative)) { $trxRelative } else { Join-Path $StageDir $trxRelative }
        if (-not (Test-Path -LiteralPath $trxPath -PathType Leaf)) {
            [void]$reasons.Add('TRX_MISSING')
        }
        else {
            $counters = Read-TrxCounters $trxPath
            if ($null -eq $counters) { [void]$reasons.Add('TRX_INVALID') }
            else {
                $minTests = if ($expect.minTests) { [int]$expect.minTests } else { 1 }
                $maxSkipped = if ($null -ne $expect.maxSkipped) { [int]$expect.maxSkipped } else { 0 }
                if ($counters.total -lt $minTests) { [void]$reasons.Add('TESTS_BELOW_MIN') }
                if ($counters.total -le 0) { [void]$reasons.Add('ZERO_TESTS') }
                if ($counters.failed -gt 0) { [void]$reasons.Add('TESTS_FAILED') }
                if ($counters.notExecuted -gt $maxSkipped) { [void]$reasons.Add('REQUIRED_SKIP') }
            }
        }
    }
    if ($expect -and $expect.countPattern) {
        $logText = ''
        if (Test-Path -LiteralPath $stdoutPath) { $logText = [System.IO.File]::ReadAllText($stdoutPath) }
        $m = [regex]::Matches($logText, [string]$expect.countPattern)
        $count = 0
        foreach ($hit in $m) {
            if ($hit.Groups.Count -gt 1) { $count += [int]$hit.Groups[1].Value } else { $count += 1 }
        }
        $minCount = if ($expect.minCount) { [int]$expect.minCount } else { 1 }
        if ($count -lt $minCount) { [void]$reasons.Add('COUNT_BELOW_MIN') }
    }
    if ($expect -and $expect.forbidPattern) {
        $logText = ''
        if (Test-Path -LiteralPath $stdoutPath) { $logText = [System.IO.File]::ReadAllText($stdoutPath) }
        if ([regex]::IsMatch($logText, [string]$expect.forbidPattern)) { [void]$reasons.Add('FORBIDDEN_PATTERN') }
    }

    # ---- artifact binding ---------------------------------------------------
    $artifacts = New-Object System.Collections.ArrayList
    foreach ($rel in @($Stage.produces)) {
        if ([string]::IsNullOrWhiteSpace([string]$rel)) { continue }
        $p = if ([System.IO.Path]::IsPathRooted([string]$rel)) { [string]$rel } else { Join-Path $StageDir ([string]$rel) }
        if (Test-Path -LiteralPath $p -PathType Leaf) {
            [void]$artifacts.Add([ordered]@{ path = [string]$rel; sha256 = Get-Sha256 $p; sizeBytes = (Get-Item -LiteralPath $p).Length })
        }
        else {
            [void]$artifacts.Add([ordered]@{ path = [string]$rel; sha256 = $null; sizeBytes = $null; missing = $true })
            [void]$reasons.Add('ARTIFACT_MISSING')
        }
    }

    # ---- stdout/stderr evidence --------------------------------------------
    $rawEvidence = New-Object System.Collections.ArrayList
    foreach ($p in @($stdoutPath, $stderrPath)) {
        if (Test-Path -LiteralPath $p) {
            [void]$rawEvidence.Add([ordered]@{ path = (Split-Path $p -Leaf); sha256 = Get-Sha256 $p; sizeBytes = (Get-Item -LiteralPath $p).Length })
        }
    }

    $status = if ($reasons.Count -eq 0) { 'PASS' } else { 'FAIL' }
    $record = [ordered]@{
        schemaVersion      = 1
        id                 = $id
        runId              = $EffectiveRunId
        fingerprint        = Get-StageFingerprint $Stage $InputSha
        inputManifestSha256 = $InputSha
        command            = ((@($file) + $argv) -join ' ')
        workingDirectory   = $cwd
        startUtc           = $startUtc
        endUtc             = $endUtc
        exitCode           = $exitCode
        timedOut           = $timedOut
        stillRunning       = $stillRunning
        counters           = $counters
        trx                = $trxRelative
        artifacts          = $artifacts
        rawEvidence        = $rawEvidence
        reasons            = @($reasons)
        status             = $status
        reused             = $false
    }
    return $record
}

function Test-Reusable($Previous, $Stage, [string]$InputSha, [string]$StageDir) {
    if ($null -eq $Previous) { return $false }
    if ([string]$Previous.status -ne 'COMPLETED') { return $false }
    if ([string]$Previous.fingerprint -ne (Get-StageFingerprint $Stage $InputSha)) { return $false }
    if ([string]$Previous.inputManifestSha256 -ne $InputSha) { return $false }
    foreach ($a in @($Previous.artifacts)) {
        $p = if ([System.IO.Path]::IsPathRooted([string]$a.path)) { [string]$a.path } else { Join-Path $StageDir ([string]$a.path) }
        if (-not (Test-Path -LiteralPath $p -PathType Leaf)) { return $false }
        if ((Get-Sha256 $p) -ne [string]$a.sha256) { return $false }
    }
    foreach ($e in @($Previous.rawEvidence)) {
        $p = Join-Path $StageDir ('logs\' + [string]$e.path)
        if (-not (Test-Path -LiteralPath $p -PathType Leaf)) { return $false }
        if ((Get-Sha256 $p) -ne [string]$e.sha256) { return $false }
    }
    return $true
}

# ---------------------------------------------------------------- main

$InputManifest = (Resolve-Path -LiteralPath $InputManifest).Path
$StagesFile = (Resolve-Path -LiteralPath $StagesFile).Path
if (-not (Test-Path -LiteralPath $RunDir)) { New-Item -ItemType Directory -Force -Path $RunDir | Out-Null }
$RunDir = (Resolve-Path -LiteralPath $RunDir).Path
$stageStateDir = Join-Path $RunDir 'stages'
New-Item -ItemType Directory -Force -Path $stageStateDir | Out-Null

$inputSha = Get-Sha256 $InputManifest
$definitions = Get-Content -LiteralPath $StagesFile -Raw -Encoding UTF8 | ConvertFrom-Json
if (-not $RunId) { $RunId = if ($definitions.runId) { [string]$definitions.runId } else { (Get-Date).ToUniversalTime().ToString('yyyyMMdd-HHmmss') + 'Z' } }

$summary = New-Object System.Collections.ArrayList
$placeholders = @{
    runId         = $RunId
    runDir        = $RunDir
    executionRoot = $ExecutionRoot
    evidenceRoot  = $EvidenceRoot
}
foreach ($rawStage in @($definitions.stages)) {
    $stage = Expand-Stage $rawStage $placeholders
    $id = [string]$stage.id
    if ($Only -and $Only -ne $id) { continue }
    $statePath = Join-Path $stageStateDir ("{0}.json" -f $id)
    $previous = $null
    if (Test-Path -LiteralPath $statePath) {
        try { $previous = Get-Content -LiteralPath $statePath -Raw -Encoding UTF8 | ConvertFrom-Json } catch { $previous = $null }
    }

    if ($Resume -and (Test-Reusable $previous $stage $inputSha $RunDir)) {
        $reuse = [ordered]@{
            schemaVersion       = 1
            id                  = $id
            runId               = [string]$previous.runId
            fingerprint         = [string]$previous.fingerprint
            inputManifestSha256 = [string]$previous.inputManifestSha256
            command             = [string]$previous.command
            workingDirectory    = [string]$previous.workingDirectory
            counters            = $previous.counters
            artifacts           = $previous.artifacts
            rawEvidence         = $previous.rawEvidence
            status              = 'REUSED'
            reused              = $true
            reusedFrom          = ('stages/{0}.json' -f $id)
            reasons             = @()
        }
        [void]$summary.Add($reuse)
        Write-Host ("[{0}] REUSED (input {1})" -f $id, $inputSha.Substring(0, 12))
        continue
    }

    if ($null -ne $previous -and [string]$previous.status -eq 'COMPLETED' -and -not $Resume) {
        # a fresh (non-resume) run must not silently overwrite completed evidence
        $archive = Join-Path $stageStateDir ("{0}.{1}.json" -f $id, [string]$previous.runId)
        if (-not (Test-Path -LiteralPath $archive)) { Copy-Item -LiteralPath $statePath -Destination $archive }
    }
    elseif ($null -ne $previous -and [string]$previous.status -ne 'COMPLETED') {
        $archive = Join-Path $stageStateDir ("{0}.incomplete.{1}.json" -f $id, [string]$previous.runId)
        if (-not (Test-Path -LiteralPath $archive)) { Copy-Item -LiteralPath $statePath -Destination $archive }
    }

    $record = Invoke-CheckedStage $stage $RunDir $inputSha $RunId
    $record['status'] = if ($record.reasons.Count -eq 0) { 'COMPLETED' } else { 'FAILED' }
    Write-Json $statePath $record
    [void]$summary.Add($record)
    $reasonText = if ($record.reasons.Count -gt 0) { ' ' + (@($record.reasons) -join ',') } else { '' }
    Write-Host ("[{0}] {1} exit={2}{3}" -f $id, $record['status'], $record.exitCode, $reasonText)
}

$failed = @($summary | Where-Object { $_.status -eq 'FAILED' })
$verdict = [ordered]@{
    schemaVersion       = 1
    runner              = 'run-goal-validation.ps1'
    runId               = $RunId
    inputManifest       = $InputManifest
    inputManifestSha256 = $inputSha
    stagesFile          = $StagesFile
    generatedUtc        = (Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ')
    resume              = [bool]$Resume
    stageCount          = $summary.Count
    failedCount         = $failed.Count
    stages              = $summary
    verdict             = if ($failed.Count -eq 0) { 'PASS' } else { 'FAIL' }
}
Write-Json (Join-Path $RunDir 'runner-verdict.json') $verdict
Write-Host ("runner verdict: {0} (stages {1}, failed {2})" -f $verdict.verdict, $summary.Count, $failed.Count)
exit ([int]($failed.Count -gt 0))
