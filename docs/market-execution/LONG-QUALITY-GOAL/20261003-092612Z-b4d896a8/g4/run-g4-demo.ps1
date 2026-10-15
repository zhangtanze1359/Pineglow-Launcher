<#
run-g4-demo.ps1 - LONG-QUALITY-GOAL / G4 "failure -> resume" demonstration with owned synthetic stages.

Pass 1  : two synthetic stages, both owned by this goal. demo-ok produces a valid TRX; demo-fail
          exits 2. The runner must exit non-zero and BOTH records must stay on disk.
Pass 2  : -Resume with a NEW runId. demo-ok is unchanged, so it must be REUSED (its artifact and
          raw evidence still match). demo-fail has a fixed command, so its fingerprint differs and
          it must re-run - the previous failed record is archived, never overwritten.

The demo touches nothing outside its own directory and never stops or kills a user process.
ASCII-only source so Windows PowerShell 5.1 parses it identically.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$OutDir,
    [Parameter(Mandatory = $true)][string]$Runner
)
$ErrorActionPreference = 'Stop'
$enc = New-Object System.Text.UTF8Encoding($false)
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$demo = Join-Path $OutDir 'demo'
New-Item -ItemType Directory -Force -Path $demo | Out-Null
$runDir = Join-Path $demo 'run'
$synth = Join-Path $demo 'synth-stage.cjs'
$inputManifest = Join-Path $demo 'input-manifest.json'

$synthSource = @'
'use strict';
const fs = require('node:fs');
const path = require('node:path');
const a = process.argv.slice(2);
const trx = a[0];
const exitCode = Number(a[1] || 0);
const total = Number(a[2] || 0);
const passed = Number(a[3] || 0);
const failed = Number(a[4] || 0);
const skipped = Number(a[5] || 0);
if (trx && trx !== '-') {
	const p = path.isAbsolute(trx) ? trx : path.resolve(process.cwd(), trx);
	fs.mkdirSync(path.dirname(p), { recursive: true });
	fs.writeFileSync(p, '<?xml version="1.0" encoding="UTF-8"?>\n' +
		'<TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">' +
		'<ResultSummary><Counters total="' + total + '" executed="' + (total - skipped) +
		'" passed="' + passed + '" failed="' + failed + '" notExecuted="' + skipped +
		'" /></ResultSummary></TestRun>');
}
console.log('synthetic stage done: exit=' + exitCode + ' total=' + total);
process.exit(exitCode);
'@
[System.IO.File]::WriteAllText($synth, $synthSource, $enc)
[System.IO.File]::WriteAllText($inputManifest, "{`n  `"schemaVersion`": 1,`n  `"inputs`": []`n}`n", $enc)

$node = (Get-Command node).Source

function New-Stages([string]$Path, [string]$FailExit, [string]$FailTotal, [string]$FailPassed) {
    $stages = [ordered]@{
        schemaVersion = 1
        runId         = 'demo'
        stages        = @(
            [ordered]@{ id = 'demo-ok'; file = $node
                argv = @($synth, 'results/demo-ok.trx', '0', '4', '4', '0', '0')
                cwd = $runDir; produces = @('results/demo-ok.trx')
                expect = [ordered]@{ trx = 'results/demo-ok.trx'; minTests = 1; maxSkipped = 0 } },
            [ordered]@{ id = 'demo-fail'; file = $node
                argv = @($synth, 'results/demo-fail.trx', $FailExit, $FailTotal, $FailPassed, '0', '0')
                cwd = $runDir; produces = @('results/demo-fail.trx')
                expect = [ordered]@{ trx = 'results/demo-fail.trx'; minTests = 1; maxSkipped = 0 } }
        )
    }
    [System.IO.File]::WriteAllText($Path, (($stages | ConvertTo-Json -Depth 8) + "`n"), $enc)
}

$stagesPass1 = Join-Path $demo 'stages-pass1.json'
$stagesPass2 = Join-Path $demo 'stages-pass2.json'
New-Stages $stagesPass1 '2' '2' '1'
New-Stages $stagesPass2 '0' '4' '4'

function Invoke-Pass([string]$Label, [string]$StagesFile, [string]$RunId, [switch]$Resume) {
    $log = Join-Path $OutDir ("{0}.log" -f $Label)
    $args = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $Runner,
        '-InputManifest', $inputManifest, '-RunDir', $runDir, '-StagesFile', $StagesFile, '-RunId', $RunId)
    if ($Resume) { $args += '-Resume' }
    & powershell.exe @args 2>&1 | Out-File -FilePath $log -Encoding utf8
    $code = $LASTEXITCODE
    $verdictPath = Join-Path $runDir 'runner-verdict.json'
    if (Test-Path -LiteralPath $verdictPath) {
        Copy-Item -LiteralPath $verdictPath -Destination (Join-Path $OutDir ("{0}-verdict.json" -f $Label)) -Force
    }
    return $code
}

$pass1 = Invoke-Pass -Label 'demo-pass1' -StagesFile $stagesPass1 -RunId 'demo-pass1'
$pass2 = Invoke-Pass -Label 'demo-pass2' -StagesFile $stagesPass2 -RunId 'demo-pass2' -Resume

$v2 = Get-Content -LiteralPath (Join-Path $OutDir 'demo-pass2-verdict.json') -Raw -Encoding UTF8 | ConvertFrom-Json
$summary = [ordered]@{
    schemaVersion = 1
    generatedUtc  = (Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ')
    runner        = $Runner
    demoDir       = $demo
    pass1         = [ordered]@{ expected = 'runner exits non-zero and both stage records stay on disk'; exitCode = $pass1 }
    pass2         = [ordered]@{ expected = 'runner exits 0, demo-ok reused, demo-fail re-run, old record archived'; exitCode = $pass2 }
    pass2Stages   = @($v2.stages | ForEach-Object { [ordered]@{ id = $_.id; status = $_.status; reused = $_.reused; exitCode = $_.exitCode; reasons = @($_.reasons) } })
    archivedRecords = @(Get-ChildItem -Path (Join-Path $runDir 'stages') -Filter '*.json' |
        ForEach-Object { $_.Name })
}
[System.IO.File]::WriteAllText((Join-Path $OutDir 'demo-summary.json'), (($summary | ConvertTo-Json -Depth 8) + "`n"), $enc)
Write-Host ("pass1 exit={0} pass2 exit={1}" -f $pass1, $pass2)
exit ([int]($pass1 -eq 0 -or $pass2 -ne 0))
