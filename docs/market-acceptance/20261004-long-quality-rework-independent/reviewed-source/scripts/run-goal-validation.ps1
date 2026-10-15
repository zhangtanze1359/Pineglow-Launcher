<#
run-goal-validation.ps1 - LONG-QUALITY-GOAL / G4 resumable quality runner.

What it guarantees (each one is a real gate, not a label):

  LQ-02  the input manifest is not trusted as a file. Every entry it lists is re-checked against
         the actual execution root named by the manifest (existence + integer size + SHA256), on
         the first run AND on every resume. A changed input file with an unchanged manifest file
         is rejected. Empty / duplicated / malformed manifests are rejected.
  LQ-03  every attempt runs in its own freshly created attempt directory, so a TRX can only be
         the one this child just wrote. The TRX counters must be present, non-negative and
         mutually consistent (total = executed + notExecuted, executed = passed + failed) and the
         UnitTestResult rows must agree with them. executed = 0 is never a pass.
  LQ-04  produces is part of the stage fingerprint, and a stage is only reused when every declared
         required output exists with the recorded SHA256. Adding a required output that does not
         exist can never be REUSED.
  LQ-05  a run is bound to (runId, input-manifest SHA, verified input-set SHA, stages-file SHA,
         execution root). An incompatible resume refuses before touching anything and keeps the
         previous records, logs, TRX and artifacts. Attempt evidence is never overwritten.
  LQ-06  an empty stage list, an unknown -Only selection, a duplicated stage id or a missing
         required stage can never produce an overall PASS. A legal -Only subset yields an explicit
         PARTIAL verdict that is not an overall completion.

Also guaranteed: a child process exit code is captured immediately after the wait and is never
overwritten by an Out-File/tee; a non-zero exit fails the stage; a timeout or a process still
running after the wall-clock budget fails the stage; a log countPattern below minCount fails and
a forbidPattern match fails; the raw stdout/stderr of every attempt is hashed and kept.

Deliberately NOT done here: sealing. Sealing (terminal capture -> manifest -> detached digest ->
out-of-package audit) is a separate step and must only run once the bound input set stops changing.

Exit codes: 0 = PASS, 1 = FAIL (a stage failed), 2 = REFUSED (invalid or incompatible invocation),
3 = PARTIAL (a legal -Only subset ran; not an overall completion).

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
$script:Utf8Read = New-Object System.Text.UTF8Encoding($false)

# ---------------------------------------------------------------- primitives

function Write-Json([string]$Path, $Object) {
    $dir = Split-Path -Parent $Path
    if ($dir -and -not (Test-Path -LiteralPath $dir)) { New-Item -ItemType Directory -Force -Path $dir | Out-Null }
    [System.IO.File]::WriteAllText($Path, (($Object | ConvertTo-Json -Depth 14) + "`n"), $script:Utf8NoBom)
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

function Read-JsonFile([string]$Path) {
    # UTF-8 explicitly: PowerShell 5.1 Get-Content without -Encoding UTF8 corrupts non-ASCII
    # paths and then fails to parse the manifest.
    $text = [System.IO.File]::ReadAllText($Path, $script:Utf8Read)
    if ($text.Length -gt 0 -and $text[0] -eq [char]0xFEFF) { $text = $text.Substring(1) }
    return ($text | ConvertFrom-Json)
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

function Get-CanonicalStageText($Stage, [string]$InputManifestSha, [string]$InputSetSha, [string]$RunDir, [string]$ExecutionRoot) {
    # Length-prefixed canonical form: `tag:length:value`. Values are newline-escaped so a value
    # that contains a separator can never be confused with a field boundary (no string-joining
    # ambiguity). Arrays keep their declared order and their full element list.
    #
    # {attemptDir} is deliberately left UNEXPANDED here: an attempt number is not part of the stage
    # contract, so a resume must still recognise the same stage. The run directory and execution
    # root ARE part of the fingerprint, so the same stage in another run can never be mistaken
    # for this one.
    $sb = New-Object System.Text.StringBuilder
    function Add-Field([System.Text.StringBuilder]$sb, [string]$tag, [string]$value) {
        $v = [string]$value
        $v = $v.Replace("`r", '\r').Replace("`n", '\n')
        [void]$sb.Append($tag).Append(':').Append($v.Length).Append(':').Append($v).Append("`n")
    }
    Add-Field $sb 'v' '2'
    Add-Field $sb 'id' ([string]$Stage.id)
    Add-Field $sb 'file' ([string]$Stage.file)
    Add-Field $sb 'cwd' ([string]$Stage.cwd)
    Add-Field $sb 'inputManifestSha256' $InputManifestSha
    Add-Field $sb 'inputSetSha256' $InputSetSha
    Add-Field $sb 'runDir' $RunDir
    Add-Field $sb 'executionRoot' $ExecutionRoot
    $av = @($Stage.argv)
    Add-Field $sb 'argv.count' ([string]$av.Count)
    for ($i = 0; $i -lt $av.Count; $i++) { Add-Field $sb ("argv.$i") ([string]$av[$i]) }
    $pv = @($Stage.produces)
    Add-Field $sb 'produces.count' ([string]$pv.Count)
    for ($i = 0; $i -lt $pv.Count; $i++) { Add-Field $sb ("produces.$i") ([string]$pv[$i]) }
    $expectJson = if ($null -eq $Stage.expect) { 'null' } else { ($Stage.expect | ConvertTo-Json -Depth 8 -Compress) }
    Add-Field $sb 'expect' $expectJson
    return $sb.ToString()
}

function Get-StageFingerprint($Stage, [string]$InputManifestSha, [string]$InputSetSha, [string]$RunDir, [string]$ExecutionRoot) {
    return Get-Sha256OfText (Get-CanonicalStageText $Stage $InputManifestSha $InputSetSha $RunDir $ExecutionRoot)
}

# ---------------------------------------------------------------- input binding (LQ-02)

function Test-InputSet([string]$ManifestPath) {
    # Returns @{ ok; reasons; inputRoot; setSha; entries; count; manifestSha }
    $reasons = New-Object System.Collections.ArrayList
    $result = @{ ok = $false; reasons = $reasons; inputRoot = ''; setSha = ''; entries = @(); count = 0; manifestSha = '' }
    if (-not (Test-Path -LiteralPath $ManifestPath -PathType Leaf)) {
        [void]$reasons.Add('INPUT_MANIFEST_MISSING')
        return $result
    }
    $result.manifestSha = Get-Sha256 $ManifestPath
    $doc = $null
    try { $doc = Read-JsonFile $ManifestPath } catch { [void]$reasons.Add('INPUT_MANIFEST_UNREADABLE'); return $result }
    if ($null -eq $doc) { [void]$reasons.Add('INPUT_MANIFEST_UNREADABLE'); return $result }

    $root = ''
    if ($doc.PSObject.Properties['executionRoot'] -and -not [string]::IsNullOrWhiteSpace([string]$doc.executionRoot)) { $root = [string]$doc.executionRoot }
    elseif ($doc.PSObject.Properties['sourceRoot'] -and -not [string]::IsNullOrWhiteSpace([string]$doc.sourceRoot)) { $root = [string]$doc.sourceRoot }
    if ([string]::IsNullOrWhiteSpace($root)) { [void]$reasons.Add('INPUT_ROOT_MISSING'); return $result }
    if (-not (Test-Path -LiteralPath $root -PathType Container)) { [void]$reasons.Add('INPUT_ROOT_NOT_FOUND'); return $result }
    $result.inputRoot = (Resolve-Path -LiteralPath $root).Path

    $inputs = @($doc.inputs)
    if ($inputs.Count -eq 0) { [void]$reasons.Add('INPUT_SET_EMPTY'); return $result }

    $seen = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
    $lines = New-Object 'System.Collections.Generic.List[string]'
    $entries = New-Object System.Collections.ArrayList
    foreach ($e in $inputs) {
        $rel = [string]$e.path
        if ([string]::IsNullOrWhiteSpace($rel)) { [void]$reasons.Add('INPUT_PATH_EMPTY'); continue }
        if ([System.IO.Path]::IsPathRooted($rel)) { [void]$reasons.Add('INPUT_PATH_ROOTED'); continue }
        $norm = $rel -replace '\\', '/'
        # strip only a literal leading './' -- TrimStart('./') would also eat the dot of '.gitignore'
        while ($norm.StartsWith('./')) { $norm = $norm.Substring(2) }
        if ($norm -match '(^|/)\.\.(/|$)') { [void]$reasons.Add('INPUT_PATH_INVALID'); continue }
        if (-not $seen.Add($norm)) { [void]$reasons.Add('INPUT_DUPLICATE') ; continue }

        $sizeText = [string]$e.sizeBytes
        $size = -1
        if (-not [int]::TryParse($sizeText, [ref]$size) -or $size -lt 0) { [void]$reasons.Add('INPUT_SIZE_INVALID'); continue }
        $sha = ([string]$e.sha256).ToUpperInvariant()
        if ($sha -notmatch '^[0-9A-F]{64}$') { [void]$reasons.Add('INPUT_SHA_INVALID'); continue }

        $abs = Join-Path $result.inputRoot ($norm -replace '/', '\')
        if (-not (Test-Path -LiteralPath $abs -PathType Leaf)) { [void]$reasons.Add('INPUT_FILE_MISSING'); continue }
        $fi = Get-Item -LiteralPath $abs
        if ($fi.Length -ne $size) { [void]$reasons.Add('INPUT_SIZE_MISMATCH'); continue }
        $actualSha = Get-Sha256 $abs
        if ($actualSha -ne $sha) { [void]$reasons.Add('INPUT_SHA_MISMATCH'); continue }
        [void]$lines.Add(('{0}`t{1}`t{2}' -f $norm, $size, $sha))
        [void]$entries.Add([ordered]@{ path = $norm; sizeBytes = $size; sha256 = $sha })
    }

    $sorted = New-Object 'System.Collections.Generic.List[string]'
    foreach ($l in $lines) { [void]$sorted.Add($l) }
    $sorted.Sort([System.StringComparer]::Ordinal)
    $result.setSha = Get-Sha256OfText ($sorted -join "`n")
    $result.entries = $entries
    $result.count = $entries.Count
    $result.ok = ($reasons.Count -eq 0)
    return $result
}

# ---------------------------------------------------------------- TRX (LQ-03)

function Read-Trx([string]$Path) {
    # Returns $null when the file is not a usable TRX, otherwise the counters plus the
    # UnitTestResult row census used for the consistency gate.
    $xml = $null
    try {
        $xml = New-Object System.Xml.XmlDocument
        $xml.Load($Path)
    }
    catch { return $null }
    $node = $xml.SelectSingleNode('//*[local-name()="Counters"]')
    if ($null -eq $node) { return $null }
    $values = [ordered]@{}
    foreach ($name in @('total', 'executed', 'passed', 'failed', 'notExecuted')) {
        $attr = $node.GetAttribute($name)
        if ([string]::IsNullOrWhiteSpace($attr)) { return $null }
        $v = -1
        if (-not [int]::TryParse($attr, [ref]$v)) { return $null }
        if ($v -lt 0) { return $null }
        $values[$name] = $v
    }
    $rows = $xml.SelectNodes('//*[local-name()="UnitTestResult"]')
    $passedRows = 0; $failedRows = 0; $notExecutedRows = 0; $otherRows = 0
    foreach ($r in $rows) {
        $o = [string]$r.GetAttribute('outcome')
        if ($o -eq 'Passed') { $passedRows++ }
        elseif ($o -eq 'Failed') { $failedRows++ }
        elseif ($o -eq 'NotExecuted') { $notExecutedRows++ }
        else { $otherRows++ }
    }
    return [ordered]@{
        total            = $values['total']
        executed         = $values['executed']
        passed           = $values['passed']
        failed           = $values['failed']
        notExecuted      = $values['notExecuted']
        resultRows       = $rows.Count
        passedRows       = $passedRows
        failedRows       = $failedRows
        notExecutedRows  = $notExecutedRows
        otherRows        = $otherRows
    }
}

# ---------------------------------------------------------------- stage execution

function Resolve-StagePath([string]$StageDir, [string]$Rel) {
    if ([string]::IsNullOrWhiteSpace($Rel)) { return $null }
    if ([System.IO.Path]::IsPathRooted($Rel)) { return $Rel }
    return (Join-Path $StageDir $Rel)
}

function New-AttemptDir([string]$RunDir, [string]$Id) {
    $attemptsRoot = Join-Path $RunDir 'attempts'
    $stageRoot = Join-Path $attemptsRoot $Id
    $seq = 1
    if (Test-Path -LiteralPath $stageRoot) {
        $seq = @(Get-ChildItem -LiteralPath $stageRoot -Directory -ErrorAction SilentlyContinue).Count + 1
    }
    $dir = Join-Path $stageRoot ([string]$seq)
    if (Test-Path -LiteralPath $dir) { throw ("attempt directory already exists: " + $dir) }
    New-Item -ItemType Directory -Force -Path $dir | Out-Null
    return [ordered]@{ dir = $dir; relative = ('attempts/{0}/{1}' -f $Id, $seq); seq = $seq }
}

function Read-StageLog([string]$StdoutPath, [string]$StderrPath, [string]$Which) {
    # Some children report on stderr (python unittest verbose output, for example). The stage can
    # say where its expected text lives: stdout (default), stderr, or both.
    $paths = if ($Which -eq 'stderr') { @($StderrPath) } elseif ($Which -eq 'both') { @($StdoutPath, $StderrPath) } else { @($StdoutPath) }
    $sb = New-Object System.Text.StringBuilder
    foreach ($p in $paths) {
        if (Test-Path -LiteralPath $p) { [void]$sb.AppendLine([System.IO.File]::ReadAllText($p, $script:Utf8Read)) }
    }
    return $sb.ToString()
}

function Invoke-CheckedStage($Stage, [string]$StageDir, [string]$InputManifestSha, [string]$InputSetSha, [string]$EffectiveRunId, [string]$AttemptRelative, [string]$Fingerprint) {
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
    # A relative path WITH directories is resolved against the stage working directory; a bare
    # command name (node, dotnet, python) is left alone so the normal PATH lookup still applies.
    if ($cwd -and -not [System.IO.Path]::IsPathRooted($file) -and ($file -match '[\\/]')) {
        $candidate = Join-Path $cwd $file
        if (Test-Path -LiteralPath $candidate -PathType Leaf) { $file = $candidate }
    }

    # LQ-03: the attempt directory is brand new, so a TRX found afterwards can only be this
    # child's output. Record any pre-existing evidence anyway instead of deleting it.
    $trxRelative = ''
    $trxPath = ''
    $trxPreexisting = $false
    if ($Stage.expect -and $Stage.expect.trx) {
        $trxRelative = ([string]$Stage.expect.trx).Replace('{runId}', $EffectiveRunId)
        $trxPath = Resolve-StagePath $StageDir $trxRelative
        if (Test-Path -LiteralPath $trxPath -PathType Leaf) { $trxPreexisting = $true }
    }

    $startUtc = (Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ')
    $exitCode = $null
    $timedOut = $false
    $stillRunning = $false

    if ($reasons.Count -eq 0) {
        # Start-Process joins ArgumentList with spaces and does not quote; quote the elements that
        # need it so a path or filter with spaces still arrives as one argument.
        $quoted = @($argv | ForEach-Object { if ($_ -match '[\s"]') { '"' + ($_ -replace '"', '\"') + '"' } else { $_ } })
        $argLine = (@($quoted) -join ' ').Trim()

        # The raw .NET process API is used instead of Start-Process: on Windows PowerShell 5.1 a
        # Start-Process -PassThru object loses ExitCode as soon as HasExited/WaitForExit is touched
        # (reproduced for node, dotnet and python), which would silently turn a real exit code into
        # "unavailable". Here the handle is owned by this script, so the exit code is captured
        # immediately after the wait and the timeout/kill path stays available.
        $psi = New-Object System.Diagnostics.ProcessStartInfo
        $psi.FileName = $file
        $psi.WorkingDirectory = $cwd
        $psi.UseShellExecute = $false
        $psi.CreateNoWindow = $true
        $psi.RedirectStandardOutput = $true
        $psi.RedirectStandardError = $true
        $psi.StandardOutputEncoding = (New-Object System.Text.UTF8Encoding($false))
        $psi.StandardErrorEncoding = (New-Object System.Text.UTF8Encoding($false))
        if ($argLine.Length -gt 0) { $psi.Arguments = $argLine }

        $proc = New-Object System.Diagnostics.Process
        $proc.StartInfo = $psi
        $outTask = $null
        $errTask = $null
        $outText = ''
        $errText = ''
        try {
            [void]$proc.Start()
            $outTask = $proc.StandardOutput.ReadToEndAsync()
            $errTask = $proc.StandardError.ReadToEndAsync()
            $exited = $false
            try { $exited = $proc.WaitForExit($TimeoutSeconds * 1000) } catch { $exited = $false }
            if (-not $exited) {
                $timedOut = $true
                try { $proc.Kill() } catch { }
                try { [void]$proc.WaitForExit(10000) } catch { }
                if (-not $proc.HasExited) { $stillRunning = $true }
                [void]$reasons.Add('TIMEOUT')
            }
            else {
                $exitCode = $proc.ExitCode
                if ($null -eq $exitCode) { [void]$reasons.Add('EXIT_CODE_UNAVAILABLE') }
                elseif ($exitCode -ne 0) { [void]$reasons.Add('EXIT_NONZERO') }
            }
        }
        catch {
            [void]$reasons.Add('START_FAILED')
            $errText = 'START_FAILED: ' + $_.Exception.Message
        }
        foreach ($t in @($outTask, $errTask)) {
            if ($null -ne $t) { try { [void]$t.Wait(15000) } catch { } }
        }
        if ($null -ne $outTask) { try { $outText = [string]$outTask.Result } catch { } }
        if ($null -ne $errTask) { try { $errText = [string]$errTask.Result } catch { } }
        [System.IO.File]::WriteAllText($stdoutPath, $outText, $script:Utf8NoBom)
        [System.IO.File]::WriteAllText($stderrPath, $errText, $script:Utf8NoBom)
    }

    $endUtc = (Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ')

    # ---- expectation checks -------------------------------------------------
    $expect = $Stage.expect
    $counters = $null
    if ($trxRelative -ne '') {
        if ($trxPreexisting) { [void]$reasons.Add('TRX_PREEXISTING') }
        if (-not (Test-Path -LiteralPath $trxPath -PathType Leaf)) {
            [void]$reasons.Add('TRX_MISSING')
        }
        else {
            $counters = Read-Trx $trxPath
            if ($null -eq $counters) { [void]$reasons.Add('TRX_INVALID') }
            else {
                $minTests = if ($expect.minTests) { [int]$expect.minTests } else { 1 }
                $maxSkipped = if ($null -ne $expect.maxSkipped) { [int]$expect.maxSkipped } else { 0 }
                # counters must describe the same population as the result rows
                if ($counters.resultRows -le 0) { [void]$reasons.Add('TRX_NO_RESULT_ROWS') }
                if (($counters.executed -ne ($counters.passed + $counters.failed)) -or
                    ($counters.total -ne ($counters.executed + $counters.notExecuted)) -or
                    ($counters.resultRows -ne $counters.total) -or
                    ($counters.passedRows -ne $counters.passed) -or
                    ($counters.failedRows -ne $counters.failed) -or
                    ($counters.notExecutedRows -ne $counters.notExecuted)) {
                    [void]$reasons.Add('TRX_COUNTERS_INCONSISTENT')
                }
                if ($counters.executed -le 0) { [void]$reasons.Add('ZERO_TESTS') }
                if ($counters.executed -lt $minTests -or $counters.passed -lt $minTests) { [void]$reasons.Add('TESTS_BELOW_MIN') }
                if ($counters.failed -gt 0) { [void]$reasons.Add('TESTS_FAILED') }
                if ($counters.notExecuted -gt $maxSkipped) { [void]$reasons.Add('REQUIRED_SKIP') }
            }
        }
    }
    if ($expect -and $expect.countPattern) {
        $countWhich = if ($expect.countIn) { [string]$expect.countIn } else { 'stdout' }
        $logText = Read-StageLog $stdoutPath $stderrPath $countWhich
        $m = [regex]::Matches($logText, [string]$expect.countPattern)
        $count = 0
        foreach ($hit in $m) {
            if ($hit.Groups.Count -gt 1) { $count += [int]$hit.Groups[1].Value } else { $count += 1 }
        }
        $minCount = if ($expect.minCount) { [int]$expect.minCount } else { 1 }
        if ($count -lt $minCount) { [void]$reasons.Add('COUNT_BELOW_MIN') }
    }
    if ($expect -and $expect.forbidPattern) {
        $forbidWhich = if ($expect.forbidIn) { [string]$expect.forbidIn } else { 'stdout' }
        $logText = Read-StageLog $stdoutPath $stderrPath $forbidWhich
        if ([regex]::IsMatch($logText, [string]$expect.forbidPattern)) { [void]$reasons.Add('FORBIDDEN_PATTERN') }
    }

    # ---- artifact binding ---------------------------------------------------
    $artifacts = New-Object System.Collections.ArrayList
    foreach ($rel in @($Stage.produces)) {
        if ([string]::IsNullOrWhiteSpace([string]$rel)) { continue }
        $p = Resolve-StagePath $StageDir ([string]$rel)
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
        schemaVersion       = 2
        id                  = $id
        runId               = $EffectiveRunId
        attemptDir          = $AttemptRelative
        fingerprint         = $Fingerprint
        inputManifestSha256 = $InputManifestSha
        inputSetSha256      = $InputSetSha
        command             = ((@($file) + $argv) -join ' ')
        workingDirectory    = $cwd
        startUtc            = $startUtc
        endUtc              = $endUtc
        exitCode            = $exitCode
        timedOut            = $timedOut
        stillRunning        = $stillRunning
        counters            = $counters
        trx                 = $trxRelative
        artifacts           = $artifacts
        rawEvidence         = $rawEvidence
        reasons             = @($reasons)
        status              = $status
        reused              = $false
    }
    return $record
}

function Test-Reusable($Previous, [string]$Fingerprint, $Stage, [string]$InputManifestSha, [string]$InputSetSha, [string]$RunDir) {
    if ($null -eq $Previous) { return $false }
    if ([string]$Previous.status -ne 'COMPLETED') { return $false }
    if ([string]$Previous.fingerprint -ne $Fingerprint) { return $false }
    if ([string]$Previous.inputManifestSha256 -ne $InputManifestSha) { return $false }
    if ([string]$Previous.inputSetSha256 -ne $InputSetSha) { return $false }
    if ([string]::IsNullOrWhiteSpace([string]$Previous.attemptDir)) { return $false }
    $prevAttempt = Join-Path $RunDir ([string]$Previous.attemptDir)
    if (-not (Test-Path -LiteralPath $prevAttempt -PathType Container)) { return $false }

    # LQ-04: every output the stage declares as required must be recorded, present and unchanged.
    foreach ($rel in @($Stage.produces)) {
        if ([string]::IsNullOrWhiteSpace([string]$rel)) { continue }
        $match = @($Previous.artifacts | Where-Object { [string]$_.path -eq [string]$rel })
        if ($match.Count -ne 1) { return $false }
        if ($null -eq $match[0].sha256) { return $false }
        $p = Resolve-StagePath $prevAttempt ([string]$rel)
        if (-not (Test-Path -LiteralPath $p -PathType Leaf)) { return $false }
        if ((Get-Sha256 $p) -ne [string]$match[0].sha256) { return $false }
    }
    foreach ($a in @($Previous.artifacts)) {
        if ($null -eq $a.sha256) { return $false }
        $p = Resolve-StagePath $prevAttempt ([string]$a.path)
        if (-not (Test-Path -LiteralPath $p -PathType Leaf)) { return $false }
        if ((Get-Sha256 $p) -ne [string]$a.sha256) { return $false }
    }
    foreach ($e in @($Previous.rawEvidence)) {
        $p = Join-Path (Join-Path $prevAttempt 'logs') ([string]$e.path)
        if (-not (Test-Path -LiteralPath $p -PathType Leaf)) { return $false }
        if ((Get-Sha256 $p) -ne [string]$e.sha256) { return $false }
    }
    return $true
}

function Get-UniquePath([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path)) { return $Path }
    $dir = Split-Path -Parent $Path
    $leaf = [System.IO.Path]::GetFileNameWithoutExtension($Path)
    $ext = [System.IO.Path]::GetExtension($Path)
    $n = 2
    while ($true) {
        $candidate = Join-Path $dir ("{0}.{1}{2}" -f $leaf, $n, $ext)
        if (-not (Test-Path -LiteralPath $candidate)) { return $candidate }
        $n++
    }
}

# ---------------------------------------------------------------- main

$InputManifest = (Resolve-Path -LiteralPath $InputManifest).Path
$StagesFile = (Resolve-Path -LiteralPath $StagesFile).Path
if (-not (Test-Path -LiteralPath $RunDir)) { New-Item -ItemType Directory -Force -Path $RunDir | Out-Null }
$RunDir = (Resolve-Path -LiteralPath $RunDir).Path
$stageStateDir = Join-Path $RunDir 'stages'
New-Item -ItemType Directory -Force -Path $stageStateDir | Out-Null

$stamp = (Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ')

function Refuse($Reasons, [string]$Stage) {
    $list = @($Reasons)
    $payload = [ordered]@{
        schemaVersion = 2
        runner        = 'run-goal-validation.ps1'
        runDir        = $RunDir
        stageFilter   = $Stage
        generatedUtc  = $stamp
        refused       = $true
        reasons       = $list
        verdict       = 'REFUSED'
    }
    $refusalPath = Get-UniquePath (Join-Path $RunDir ("refusal-{0}.json" -f ((Get-Date).ToUniversalTime().ToString('yyyyMMdd-HHmmssZ'))))
    Write-Json $refusalPath $payload
    $verdictPath = Join-Path $RunDir 'runner-verdict.json'
    if (-not (Test-Path -LiteralPath $verdictPath)) {
        # never overwrite an existing verdict: a refusal must not erase previous results
        $payload['stageCount'] = 0
        $payload['failedCount'] = 0
        $payload['stages'] = @()
        Write-Json $verdictPath $payload
    }
    Write-Host ("runner REFUSED: {0} -> {1}" -f ($list -join ','), $refusalPath)
    exit 2
}

# ---- LQ-02: verify the manifest AND the actual input bytes it points at
$inputCheck = Test-InputSet $InputManifest
$inputSha = $inputCheck.manifestSha
if (-not $inputCheck.ok) {
    $why = @($inputCheck.reasons) + @('INPUT_SET_FAILED')
    Write-Host ("input set verification failed: {0}" -f (@($inputCheck.reasons) -join ','))
    Refuse $why ''
}
$inputSetSha = $inputCheck.setSha

# ---- stage definitions (LQ-06)
$definitions = $null
try { $definitions = Read-JsonFile $StagesFile } catch { Refuse @('STAGES_FILE_UNREADABLE') '' }
if ($null -eq $definitions) { Refuse @('STAGES_FILE_UNREADABLE') '' }
$rawStages = @($definitions.stages)
if ($rawStages.Count -eq 0) { Refuse @('NO_STAGES') '' }

$ids = New-Object System.Collections.ArrayList
$seenIds = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::Ordinal)
$dup = New-Object System.Collections.ArrayList
foreach ($s in $rawStages) {
    $sid = [string]$s.id
    if ([string]::IsNullOrWhiteSpace($sid)) { Refuse @('STAGE_ID_EMPTY') '' }
    if (-not $seenIds.Add($sid)) { [void]$dup.Add($sid) }
    [void]$ids.Add($sid)
}
if ($dup.Count -gt 0) { Refuse @('DUPLICATE_STAGE_ID') '' }

$requiredIds = @()
if ($definitions.PSObject.Properties['requiredStages']) { $requiredIds = @($definitions.requiredStages | ForEach-Object { [string]$_ }) }
$missingRequired = @($requiredIds | Where-Object { -not $seenIds.Contains($_) })
if ($missingRequired.Count -gt 0) { Refuse @('REQUIRED_STAGE_MISSING') '' }

if ($Only -and -not $seenIds.Contains($Only)) { Refuse @('ONLY_NOT_FOUND') '' }

if (-not $RunId) { $RunId = if ($definitions.runId) { [string]$definitions.runId } else { (Get-Date).ToUniversalTime().ToString('yyyyMMdd-HHmmss') + 'Z' } }

$stagesFileSha = Get-Sha256 $StagesFile
$binding = [ordered]@{
    schemaVersion        = 1
    runId                = $RunId
    inputManifest        = $InputManifest
    inputManifestSha256  = $inputSha
    verifiedInputSetSha256 = $inputSetSha
    verifiedInputCount   = $inputCheck.count
    verifiedInputRoot    = $inputCheck.inputRoot
    stagesFile           = $StagesFile
    stagesFileSha256     = $stagesFileSha
    executionRoot        = $ExecutionRoot
    evidenceRoot         = $EvidenceRoot
}

# ---- LQ-05: a run directory belongs to exactly one verified input+definition binding
$bindingPath = Join-Path $RunDir 'run-binding.json'
$existingStageRecords = @(Get-ChildItem -LiteralPath $stageStateDir -File -Filter '*.json' -ErrorAction SilentlyContinue)
if (Test-Path -LiteralPath $bindingPath) {
    $previousBinding = $null
    try { $previousBinding = Read-JsonFile $bindingPath } catch { Refuse @('RUN_BINDING_UNREADABLE') '' }
    $diff = New-Object System.Collections.ArrayList
    foreach ($k in @('runId', 'inputManifestSha256', 'verifiedInputSetSha256', 'stagesFileSha256')) {
        if ([string]$previousBinding.$k -ne [string]$binding[$k]) { [void]$diff.Add(('{0}: {1} -> {2}' -f $k, [string]$previousBinding.$k, [string]$binding[$k])) }
    }
    if ($diff.Count -gt 0) {
        Write-Host ("incompatible resume: {0}" -f ($diff -join '; '))
        Write-Host 'this run directory keeps its existing records; use a new -RunId and a new -RunDir'
        Refuse @('INCOMPATIBLE_RESUME_SAME_RUN_DIR') ''
    }
}
elseif ($existingStageRecords.Count -gt 0) {
    Refuse @('RUN_BINDING_MISSING') ''
}
else {
    Write-Json $bindingPath $binding
}

$allStages = New-Object System.Collections.ArrayList
$baseVars = @{ runId = $RunId; runDir = $RunDir; executionRoot = $ExecutionRoot; evidenceRoot = $EvidenceRoot }

$summary = New-Object System.Collections.ArrayList
foreach ($rawStage in $rawStages) {
    $stageId = [string]$rawStage.id
    if ($Only -and $Only -ne $stageId) { continue }

    # Two expansions of the same definition: the execution form knows the attempt directory, the
    # fingerprint form does not ({attemptDir} stays literal) so a resume can recognise the stage.
    $stageFp = Expand-Stage $rawStage $baseVars
    $fingerprint = Get-StageFingerprint $stageFp $inputSha $inputSetSha $RunDir $ExecutionRoot
    $id = [string]$rawStage.id
    [void]$allStages.Add($id)

    $statePath = Join-Path $stageStateDir ("{0}.json" -f $id)
    $previous = $null
    if (Test-Path -LiteralPath $statePath) {
        try { $previous = Read-JsonFile $statePath } catch { $previous = $null }
    }

    if ($Resume -and (Test-Reusable $previous $fingerprint $stageFp $inputSha $inputSetSha $RunDir)) {
        $reuse = [ordered]@{
            schemaVersion       = 2
            id                  = $id
            runId               = [string]$previous.runId
            attemptDir          = [string]$previous.attemptDir
            fingerprint         = [string]$previous.fingerprint
            inputManifestSha256 = [string]$previous.inputManifestSha256
            inputSetSha256      = [string]$previous.inputSetSha256
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

    # only a real re-run consumes a new attempt directory: a REUSED stage must not leave an
    # empty attempt behind (that would also inflate later attempt numbers)
    $attempt = New-AttemptDir $RunDir $stageId
    $varsRun = @{}
    foreach ($k in $baseVars.Keys) { $varsRun[$k] = $baseVars[$k] }
    $varsRun['attemptDir'] = $attempt.dir
    $stageRun = Expand-Stage $rawStage $varsRun

    if ($null -ne $previous) {
        # keep the superseded record; never overwrite an attempt's record or evidence
        if ([string]$previous.status -eq 'COMPLETED') {
            $archive = Get-UniquePath (Join-Path $stageStateDir ("{0}.{1}.json" -f $id, [string]$previous.runId))
        }
        else {
            $archive = Get-UniquePath (Join-Path $stageStateDir ("{0}.incomplete.{1}.json" -f $id, [string]$previous.runId))
        }
        Copy-Item -LiteralPath $statePath -Destination $archive
    }

    $record = Invoke-CheckedStage $stageRun $attempt.dir $inputSha $inputSetSha $RunId $attempt.relative $fingerprint
    $record['status'] = if ($record.reasons.Count -eq 0) { 'COMPLETED' } else { 'FAILED' }
    Write-Json $statePath $record
    [void]$summary.Add($record)
    $reasonText = if ($record.reasons.Count -gt 0) { ' ' + (@($record.reasons) -join ',') } else { '' }
    Write-Host ("[{0}] {1} exit={2}{3}" -f $id, $record['status'], $record.exitCode, $reasonText)
}

$failed = @($summary | Where-Object { $_.status -eq 'FAILED' })
$notCompletedRequired = New-Object System.Collections.ArrayList
foreach ($rid in $requiredIds) {
    $s = @($summary | Where-Object { $_.id -eq $rid })
    if ($s.Count -eq 0) { [void]$notCompletedRequired.Add($rid); continue }
    if ([string]$s[0].status -ne 'COMPLETED' -and [string]$s[0].status -ne 'REUSED') { [void]$notCompletedRequired.Add($rid) }
}

$isPartial = ($Only -ne '' -and $allStages.Count -lt $ids.Count)
$verdictName = if ($failed.Count -gt 0) { 'FAIL' } elseif ($isPartial) { 'PARTIAL' } elseif ($notCompletedRequired.Count -gt 0) { 'FAIL' } else { 'PASS' }
$exitCode = if ($verdictName -eq 'PASS') { 0 } elseif ($verdictName -eq 'PARTIAL') { 3 } else { 1 }

$verdict = [ordered]@{
    schemaVersion       = 2
    runner              = 'run-goal-validation.ps1'
    runId               = $RunId
    inputManifest       = $InputManifest
    inputManifestSha256 = $inputSha
    verifiedInputSetSha256 = $inputSetSha
    verifiedInputCount  = $inputCheck.count
    verifiedInputRoot   = $inputCheck.inputRoot
    stagesFile          = $StagesFile
    stagesFileSha256    = $stagesFileSha
    generatedUtc        = (Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ')
    resume              = [bool]$Resume
    only                = $Only
    partial             = $isPartial
    requiredStages      = $requiredIds
    requiredNotCompleted = $notCompletedRequired
    partialNotRun       = $(if ($isPartial) { @($requiredIds | Where-Object { -not ($allStages -contains $_) }) } else { @() })
    stageCount          = $summary.Count
    failedCount         = $failed.Count
    stages              = $summary
    verdict             = $verdictName
}
$verdictPath = Join-Path $RunDir 'runner-verdict.json'
if (Test-Path -LiteralPath $verdictPath) {
    # LQ-05: a previous run summary is history, never silently replaced evidence.
    $archiveDir = Join-Path $RunDir 'verdicts'
    New-Item -ItemType Directory -Force -Path $archiveDir | Out-Null
    $archive = Get-UniquePath (Join-Path $archiveDir ('runner-verdict.{0}.json' -f ($stamp -replace '[:]', '')))
    Copy-Item -LiteralPath $verdictPath -Destination $archive
}
Write-Json $verdictPath $verdict
Write-Host ("runner verdict: {0} (stages {1}, failed {2}, partial {3})" -f $verdict.verdict, $summary.Count, $failed.Count, $isPartial)
exit $exitCode
