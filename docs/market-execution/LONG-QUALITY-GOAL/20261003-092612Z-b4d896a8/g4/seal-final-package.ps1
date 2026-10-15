<#
seal-final-package.ps1 - LONG-QUALITY-GOAL / G4 final seal (runs from the run package, not from scripts/).

Order enforced here (this is the fix for F-R2-01, where the terminal capture ran AFTER the seal and
rewrote an already sealed file):

    assemble evidence  ->  TERMINAL capture  ->  machine verdict / changed files / final report
                       ->  manifest.json  ->  manifest.sha256 (detached)
                       ->  out-of-package strict audit  ->  post-seal re-hash of every package file

After manifest.json exists nothing inside the package is written again: every later output goes to
<EvidenceRoot> (outside the package). The script refuses to run if the package directory exists, so a
re-seal always needs a new runId.

ASCII-only source so Windows PowerShell 5.1 parses it identically.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$EvidenceRoot,
    [Parameter(Mandatory = $true)][string]$RunId,
    [Parameter(Mandatory = $true)][string]$SourceRoot,
    [Parameter(Mandatory = $true)][string]$CaptureScript,
    [Parameter(Mandatory = $true)][string]$Auditor,
    [Parameter(Mandatory = $true)][string]$ExpectedEvidence,
    [string]$RunnerVerdict = '',
    [string]$SealedLocal = ''
)
$ErrorActionPreference = 'Stop'
$enc = New-Object System.Text.UTF8Encoding($false)
# ---------------------------------------------------------------------------
# Explicit JSON writer.
# Windows PowerShell 5.1's ConvertTo-Json was observed to spin at 100% CPU and grow to several GB
# on the verdict/changed-files object of this seal (reproduced three times, including after the
# object had been converted to plain PSCustomObject). Instead of debugging that, this seal writes
# JSON with a small deterministic serializer that only ever touches the values it is handed.
# Non-ASCII characters are emitted as \uXXXX escapes, so the output is pure ASCII and cannot be
# mangled by the console code page; every artefact is re-validated with a real JSON parser later.
# ---------------------------------------------------------------------------
function Esc-JsonString([string]$s) {
    $sb = New-Object System.Text.StringBuilder
    [void]$sb.Append('"')
    foreach ($ch in $s.ToCharArray()) {
        $code = [int]$ch
        if ($ch -eq '"') { [void]$sb.Append('\"') }
        elseif ($ch -eq '\') { [void]$sb.Append('\\') }
        elseif ($code -eq 8) { [void]$sb.Append('\b') }
        elseif ($code -eq 12) { [void]$sb.Append('\f') }
        elseif ($code -eq 10) { [void]$sb.Append('\n') }
        elseif ($code -eq 13) { [void]$sb.Append('\r') }
        elseif ($code -eq 9) { [void]$sb.Append('\t') }
        elseif ($code -lt 32 -or $code -gt 126) { [void]$sb.Append(('\u{0:x4}' -f $code)) }
        else { [void]$sb.Append($ch) }
    }
    [void]$sb.Append('"')
    return $sb.ToString()
}
function To-JsonText($v, [int]$indent) {
    $pad = ' ' * $indent
    $pad2 = ' ' * ($indent + 2)
    if ($null -eq $v) { return 'null' }
    if ($v -is [bool]) { if ($v) { return 'true' } else { return 'false' } }
    if ($v -is [string]) { return (Esc-JsonString $v) }
    if ($v -is [int] -or $v -is [long] -or $v -is [int16] -or $v -is [byte]) { return ([string]$v) }
    if ($v -is [double] -or $v -is [single] -or $v -is [decimal]) {
        return ($v.ToString([System.Globalization.CultureInfo]::InvariantCulture))
    }
    if ($v -is [System.Collections.IDictionary]) {
        $keys = @($v.Keys)
        if ($keys.Count -eq 0) { return '{}' }
        $parts = @()
        foreach ($k in $keys) { $parts += ($pad2 + (Esc-JsonString ([string]$k)) + ': ' + (To-JsonText $v[$k] ($indent + 2))) }
        return '{' + "`n" + ($parts -join (",`n")) + "`n" + $pad + '}'
    }
    if ($v -is [System.Collections.IEnumerable]) {
        $items = @()
        foreach ($i in $v) { $items += ($pad2 + (To-JsonText $i ($indent + 2))) }
        if ($items.Count -eq 0) { return '[]' }
        return '[' + "`n" + ($items -join (",`n")) + "`n" + $pad + ']'
    }
    $props = @($v.PSObject.Properties | Where-Object { $_.MemberType -eq 'NoteProperty' -or $_.MemberType -eq 'Property' })
    if ($props.Count -gt 0) {
        $parts = @()
        foreach ($p in $props) { $parts += ($pad2 + (Esc-JsonString $p.Name) + ': ' + (To-JsonText $p.Value ($indent + 2))) }
        return '{' + "`n" + ($parts -join (",`n")) + "`n" + $pad + '}'
    }
    return (Esc-JsonString ([string]$v))
}
function Write-Json([string]$Path, $Object) {
    $dir = Split-Path -Parent $Path
    if ($dir -and -not (Test-Path -LiteralPath $dir)) { New-Item -ItemType Directory -Force -Path $dir | Out-Null }
    [System.IO.File]::WriteAllText($Path, ((To-JsonText $Object 0) + "`n"), $enc)
}
function Get-Sha256([string]$Path) { return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToUpperInvariant() }

$EvidenceRoot = (Resolve-Path -LiteralPath $EvidenceRoot).Path
$pkgRoot = Join-Path $EvidenceRoot 'FINAL-LOCAL-QUALITY'
$pkg = Join-Path $pkgRoot $RunId
if (Test-Path -LiteralPath $pkg) { throw "package already exists, use a new runId: $pkg" }
$progressLog = Join-Path $EvidenceRoot ('seal-progress-{0}.log' -f $RunId)
$errorLog = Join-Path $EvidenceRoot ('seal-error-{0}.txt' -f $RunId)
$script:step = 'start'
function Step([string]$Name) {
    $script:step = $Name
    $line = ('{0}  {1}' -f (Get-Date).ToString('HH:mm:ss'), $Name)
    [System.IO.File]::AppendAllText($progressLog, $line + "`n", $enc)
    Write-Host $line
}
Step 'start'
try {
    # enumerate the evidence BEFORE the package directory exists, so the scan can never see the
    # package it is about to create
    $evidenceFiles = @(Get-ChildItem -LiteralPath $EvidenceRoot -Recurse -File |
        Where-Object { -not $_.FullName.StartsWith($pkgRoot, [StringComparison]::OrdinalIgnoreCase) })
    Step ('enumerated {0} evidence files' -f $evidenceFiles.Count)
    New-Item -ItemType Directory -Force -Path $pkg | Out-Null
    if (-not $SealedLocal) { $SealedLocal = (Get-Date).ToString('yyyy-MM-ddTHH:mm:sszzz') }
    $sealedUtc = (Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ')

    # ---- 1. assemble the evidence that this goal produced ----------------------
    $copied = New-Object System.Collections.ArrayList
    foreach ($f in $evidenceFiles) {
        $rel = $f.FullName.Substring($EvidenceRoot.Length + 1)
        $dst = Join-Path $pkg $rel
        $dstDir = Split-Path -Parent $dst
        if (-not (Test-Path -LiteralPath $dstDir)) { New-Item -ItemType Directory -Force -Path $dstDir | Out-Null }
        Copy-Item -LiteralPath $f.FullName -Destination $dst -Force
        [void]$copied.Add($rel)
    }
    Step ('assembled {0} evidence files' -f $copied.Count)

Step 'terminal capture starting'
$captureJson = Join-Path $EvidenceRoot ('seal-capture-{0}.json' -f $RunId)
$capture = [ordered]@{ schemaVersion = 1; runId = $RunId; evidenceFiles = $copied.Count; terminalCapture = $null }

# ---- 2. TERMINAL capture (must happen before the seal) ---------------------
$sealSub = 'seal'
$captureOut = Join-Path $pkg $sealSub
& $CaptureScript -Stage terminal -OutSubdir (Join-Path 'FINAL-LOCAL-QUALITY' (Join-Path $RunId $sealSub)) `
    -EvidenceRoot $EvidenceRoot -SourceRoot $SourceRoot | Out-Null
$captureCode = $LASTEXITCODE
$capture['terminalCapture'] = [ordered]@{
    exitCode   = $captureCode
    outputDir  = $captureOut
    manifest   = (Join-Path $captureOut 'source-after-manifest.json')
    statusFile = (Join-Path $captureOut 'source-status-terminal.txt')
}
if ($captureCode -ne 0) { throw "terminal capture failed with exit $captureCode" }
Step 'terminal capture done'

Step 'writing verdict/changed/report'
# ---- 3. machine verdict, changed files, final report ----------------------
$verdictPath = Join-Path $pkg 'machine-verdict.json'
$changedPath = Join-Path $pkg 'changed-files.json'
$reportPath = Join-Path $pkg 'final-report.md'
$capturePath = Join-Path $pkg 'seal-capture.json'

# changed files: allowed drift vs unexplained (external) drift, never merged together
$drift = Get-Content -LiteralPath (Join-Path $EvidenceRoot 'g4\source-drift-after-g4.json') -Raw -Encoding UTF8 | ConvertFrom-Json
$changed = [ordered]@{
    schemaVersion = 1
    runId         = $RunId
    sealedUtc     = $sealedUtc
    sourceRoot    = $SourceRoot
    head          = (Get-Content -LiteralPath (Join-Path $pkg 'g0\git-head.txt') -Raw -Encoding UTF8)
    frozenInputs  = $drift.counts.frozen
    unchanged     = $drift.counts.unchanged
    changedByThisGoal = @($drift.allowedDrift | ForEach-Object { [ordered]@{ path = $_.path; frozenSha256 = $_.frozenSha256; actualSha256 = $_.actualSha256; owner = 'this-goal' } })
    changedByExternalWriter = @($drift.unexpectedDrift | ForEach-Object { [ordered]@{ path = $_.path; frozenSha256 = $_.frozenSha256; actualSha256 = $_.actualSha256; owner = 'external-writer (not this goal)'; recordedIn = 'g3/concurrent-drift-ui-index-html.json' } })
    newFilesNotInFrozenInputs = @('ui/css/pineglow.css', 'ui/assets/pine-forest.svg', 'scripts/audit-goal-evidence.py', 'scripts/capture-goal-baseline.ps1', 'scripts/run-goal-validation.ps1', 'scripts/tests/audit-goal-evidence.test.py', 'scripts/tests/goal-validation.test.cjs', 'ui/tests/goal-core-flow.test.cjs')
    gitWriteOperations = 0
    note = 'This goal performed no git write operation: no commit, push, stage, reset, clean, restore, checkout or stash.'
}
Write-Json $changedPath $changed

$runnerInfo = $null
if ($RunnerVerdict -and (Test-Path -LiteralPath $RunnerVerdict)) {
    $rv = Get-Content -LiteralPath $RunnerVerdict -Raw -Encoding UTF8 | ConvertFrom-Json
    $runnerInfo = [ordered]@{
        path            = $RunnerVerdict
        verdict         = [string]$rv.verdict
        stageCount      = [int]$rv.stageCount
        failedCount     = [int]$rv.failedCount
        inputManifestSha256 = [string]$rv.inputManifestSha256
        stages          = @($rv.stages | ForEach-Object { [ordered]@{ id = $_.id; status = $_.status; exitCode = $_.exitCode; reused = $_.reused; reasons = @($_.reasons) } })
    }
}
$implementationReady = ($null -ne $runnerInfo) -and ([string]$runnerInfo.verdict -eq 'PASS')

$verdict = [ordered]@{
    schemaVersion          = 1
    runId                  = $RunId
    sealedLocal            = $SealedLocal
    sealedUtc              = $sealedUtc
    scope                  = 'windows-local-offline-quality'
    implementationReady    = $implementationReady
    independentAcceptance  = 'NOT_GRANTED'
    formalPhaseApproval    = 'NOT_GRANTED'
    releaseApproval        = 'NOT_GRANTED'
    runnerVerdict          = $runnerInfo
    sealChecks             = 'recorded OUTSIDE this package: final-audit-outside-package-<runId>.json and post-seal-verification-<runId>.json (the package is never written after manifest.json)'
    gates                  = [ordered]@{
        G0 = 'DONE'
        G1 = 'DONE'
        G2 = 'DONE'
        G3 = 'DONE'
        G4 = 'DONE'
    }
    evidence = [ordered]@{
        g0Inputs            = 286
        g1SyntheticTests    = 26
        g1OldAuditorRed     = '5/5 false passes reproduced'
        g1RealTree          = '61 packages / 874 entries / 3 registered drifts'
        g2Scenarios         = '27/27'
        g2NodeTests         = 28
        g3CsharpTests       = 73
        g3NodeTests         = 266
        g3BisectSmoke       = '19/19'
        g4RunnerTests       = 10
        g4FailureToResume   = 'PASS'
    }
    notVerified = @(
        'real Avalonia/WebView2 GUI',
        'Minecraft launch and in-game behaviour',
        'native Windows credential round-trip',
        'real self-update process handover',
        'cross-volume / full-disk behaviour',
        'non-Windows platforms',
        'remote CI'
    )
    notRun = @(
        'RA-01/RA-02 recovery-safety counter-proofs (platform-terminated range; not executed, not delegated)',
        'ImportCancellationTests.ImportWithALiveTokenButCancelledMidway_StillCleansUp'
    )
    concurrentDrift = 'g3/concurrent-drift-ui-index-html.json (files changed by a writer outside this goal; not reverted, not attributed to this goal)'
    terminationPhrase = [System.Text.Encoding]::UTF8.GetString([System.Convert]::FromBase64String('5YW35aSH54us56uL5aSN6aqM5p2h5Lu277yM562J5b6FIENvZGV4IOeLrOeri+mqjOaUtuOAgg=='))
}
Write-Json $verdictPath $verdict

Write-Json $capturePath $capture
Write-Json (Join-Path $EvidenceRoot ('seal-capture-{0}.json' -f $RunId)) $capture

$reportLines = @(
    '# LONG-QUALITY-GOAL final package (' + $RunId + ')',
    '',
    'Sealed local time: ' + $SealedLocal + '  (UTC ' + $sealedUtc + ')',
    'Sealed from: ' + $SourceRoot,
    '',
    'This package is the sealed deliverable of the bounded Windows local offline quality goal.',
    'It is machine-audited from OUTSIDE the package; nothing inside was written after manifest.json.',
    '',
    '## Gates',
    '',
    '| gate | result | artefact |',
    '| --- | --- | --- |',
    '| G0 frozen baseline | 286 inputs, copy verified byte-identical | g0/input-manifest.json, g0/copy-verification.json |',
    '| G1 strict evidence threshold | 26/26 synthetic tests; 5/5 old-auditor false passes reproduced; 61 packages / 874 entries; 3 byte-bound drifts | g1/, correction/ |',
    '| G2 27 ordinary scenarios | 27/27 behaviour PASS; 28/28 tests; full node suite 266/266, 0 skipped | g2/scenario-matrix.json |',
    '| G3 API + disk closed loop | 73/73 C# (24 R2 baseline + 49 per-flow); 266/266 node; VerifyHost build 0 errors; bisect smoke 19/19 | g3/g3-summary.json, g3/test-results/*.trx |',
    '| G4 runner | 10/10 contract tests; failure -> resume demonstration PASS | g4/runner-synthetic-tests.log, g4/demo-summary.json |',
    '',
    '## Mechanical state',
    '',
    '- ImplementationReady = ' + ([string]$implementationReady).ToUpperInvariant(),
    '  (true only when the pre-seal runner run over this package''s own stages reports PASS)',
    '- IndependentAcceptance = NOT_GRANTED',
    '- FormalPhaseApproval = NOT_GRANTED',
    '- ReleaseApproval = NOT_GRANTED',
    '- The out-of-package strict audit and the post-seal re-hash are recorded OUTSIDE this package;',
    '  nothing inside was written after manifest.json.',
    '',
    '## Not verified / not run',
    '',
    'See machine-verdict.json: real GUI/WebView2, Minecraft, native credentials, real update handover,',
    'cross-platform, remote CI, and the platform-terminated recovery-safety counter-proof range.',
    '',
    '## Concurrent drift',
    '',
    'g3/concurrent-drift-ui-index-html.json and changed-files.json record the files a writer outside',
    'this goal changed in the shared working tree. They were not reverted and are not attributed to',
    'this goal.',
    '',
    '## Termination phrase',
    '',
    [System.Text.Encoding]::UTF8.GetString([System.Convert]::FromBase64String('5YW35aSH54us56uL5aSN6aqM5p2h5Lu277yM562J5b6FIENvZGV4IOeLrOeri+mqjOaUtuOAgg==')),
    ''
)
[System.IO.File]::WriteAllText($reportPath, ($reportLines -join "`n"), $enc)
Step 'verdict/changed/report written'

Step 'manifest build starting'
# ---- 4. manifest + detached digest ----------------------------------------
$entries = New-Object System.Collections.ArrayList
$totalBytes = 0
foreach ($f in (Get-ChildItem -LiteralPath $pkg -Recurse -File | Sort-Object FullName)) {
    $rel = $f.FullName.Substring($pkg.Length + 1).Replace('\', '/')
    if ($rel -eq 'manifest.json' -or $rel -eq 'manifest.sha256') { continue }
    $totalBytes += $f.Length
    [void]$entries.Add([ordered]@{ path = $rel; sizeBytes = $f.Length; sha256 = Get-Sha256 $f.FullName })
}
$manifest = [ordered]@{
    schemaVersion = 1
    runId         = $RunId
    title         = 'LONG-QUALITY-GOAL windows-local-offline-quality final package'
    generatedUtc  = $sealedUtc
    sealedLocal   = $SealedLocal
    root          = $pkg
    sourceRoot    = $SourceRoot
    count         = $entries.Count
    totalBytes    = $totalBytes
    files         = $entries
}
Write-Json (Join-Path $pkg 'manifest.json') $manifest
$manifestSha = Get-Sha256 (Join-Path $pkg 'manifest.json')
[System.IO.File]::WriteAllText((Join-Path $pkg 'manifest.sha256'), ($manifestSha + '  manifest.json' + "`n"), $enc)
Step ('sealed manifest.json sha256={0} files={1}' -f $manifestSha, $entries.Count)

Step 'out-of-package audit starting'
# ---- 5. out-of-package strict audit + post-seal re-hash --------------------
$auditOut = Join-Path $EvidenceRoot ('final-audit-outside-package-{0}.json' -f $RunId)
Push-Location $SourceRoot
try {
    & python -X utf8 $Auditor --expected $ExpectedEvidence --out $auditOut 2>&1 | Write-Host
    $auditCode = $LASTEXITCODE
}
finally { Pop-Location }

$postSeal = New-Object System.Collections.ArrayList
$recheck = [ordered]@{ schemaVersion = 1; runId = $RunId; checkedUtc = (Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ');
    files = 0; changed = @(); missing = @(); manifestSha256 = $manifestSha;
    detachedSha256 = ((Get-Content -LiteralPath (Join-Path $pkg 'manifest.sha256') -Raw -Encoding UTF8).Trim().Split(' ')[0]).ToUpperInvariant() }
$live = Get-Content -LiteralPath (Join-Path $pkg 'manifest.json') -Raw -Encoding UTF8 | ConvertFrom-Json
foreach ($e in $live.files) {
    $p = Join-Path $pkg ($e.path -replace '/', '\')
    if (-not (Test-Path -LiteralPath $p -PathType Leaf)) { [void]$postSeal.Add(@{ path = $e.path; problem = 'missing' }); continue }
    $recheck.files = $recheck.files + 1
    $now = Get-Sha256 $p
    if ($now -ne $e.sha256) { [void]$postSeal.Add(@{ path = $e.path; problem = 'sha-mismatch'; recorded = $e.sha256; actual = $now }) }
}
$recheck.changed = @($postSeal)
Write-Json (Join-Path $EvidenceRoot ('post-seal-verification-{0}.json' -f $RunId)) $recheck

Step 'post-seal verification done'
$ok = ($auditCode -eq 0) -and ($postSeal.Count -eq 0) -and ($recheck.manifestSha256 -eq $recheck.detachedSha256) -and $implementationReady
Write-Host ("out-of-package audit exit={0}; post-seal files={1} problems={2}; detached match={3}; implementationReady={4}" -f `
    $auditCode, $recheck.files, $postSeal.Count, ($recheck.manifestSha256 -eq $recheck.detachedSha256), $implementationReady)
exit ([int](-not $ok))
}
catch {
    $msg = ('step={0}
{1}' -f $script:step, $_.Exception.ToString())
    [System.IO.File]::WriteAllText($errorLog, $msg + "`n", $enc)
    Write-Host ('SEAL FAILED at ' + $script:step)
    Write-Host $msg
    exit 1
}
