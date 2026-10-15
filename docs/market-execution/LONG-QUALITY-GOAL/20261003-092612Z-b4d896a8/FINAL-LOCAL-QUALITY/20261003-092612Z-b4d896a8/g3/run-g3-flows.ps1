<#
run-g3-flows.ps1 - LONG-QUALITY-GOAL / G3 part 2: authorised per-flow cross-layer regression.

Adds the ordinary service-level classes that carry real HTTP + disk assertions for the six user
flows, in the same GUID execution copy (built by run-g3-authorized.ps1):

  InstanceRemovalTests      -> INSTANCE flow (list removal keeps every game file byte identical)
  ImportCancellationTests   -> IMPORT flow (operationId binding, no half-built instance)
  DiagnosticReportTests     -> DIAGNOSIS flow (local-only report, history, export honesty)
  BisectRecoveryTests       -> BISECT flow (pending record, resume restore, conflict refusal)
  UpdateRecoveryTests       -> UPDATE flow (apply script backup/replace/rollback + result record)
  RecoveryVersionDisplayEndToEndTests -> RECOVERY flow over the real HTTP surface

Deliberately NOT selected: ImportWithALiveTokenButCancelledMidway_StillCleansUp (a mid-flight
cancellation race probe). The goal does not execute or delegate the platform-terminated
counter-proof range; the method stays in the repository untouched and is recorded as NOT_RUN.

ASCII-only source so Windows PowerShell 5.1 parses it identically.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$ExecutionRoot,
    [Parameter(Mandatory = $true)][string]$OutDir
)

$ErrorActionPreference = 'Continue'
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$resultsDir = Join-Path $OutDir 'test-results'
New-Item -ItemType Directory -Force -Path $resultsDir | Out-Null

$project = 'dev/PineglowLauncher.Tests/Pineglow.Launcher.Tests.csproj'
$filter = '(FullyQualifiedName~InstanceRemovalTests|FullyQualifiedName~ImportCancellationTests|FullyQualifiedName~DiagnosticReportTests|FullyQualifiedName~BisectRecoveryTests|FullyQualifiedName~UpdateRecoveryTests|FullyQualifiedName~RecoveryVersionDisplayEndToEndTests)&FullyQualifiedName!~ImportWithALiveTokenButCancelledMidway'

$log = Join-Path $OutDir 'g3-flows.log'
$started = Get-Date
Push-Location $ExecutionRoot
try {
    & dotnet test $project -c Release --no-build --filter $filter `
        --logger 'trx;LogFileName=flows.trx' --results-directory $resultsDir 2>&1 |
        Out-File -FilePath $log -Encoding utf8
    $code = $LASTEXITCODE
}
finally { Pop-Location }

$entry = [ordered]@{
    schemaVersion = 1
    generatedUtc  = (Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ')
    executionRoot = $ExecutionRoot
    resultsDir    = $resultsDir
    id            = 'g3-flows'
    command       = ('dotnet test {0} -c Release --no-build --filter "{1}"' -f $project, $filter)
    startedUtc    = $started.ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ')
    exitCode      = $code
    expected      = 'exit 0, non-zero tests, zero skipped'
    rawEvidence   = 'g3/g3-flows.log'
    excluded      = @('ImportWithALiveTokenButCancelledMidway_StillCleansUp (NOT_RUN: mid-flight cancellation race probe, outside this goal)')
}
$enc = New-Object System.Text.UTF8Encoding($false)
[System.IO.File]::WriteAllText((Join-Path $OutDir 'g3-flows-results.json'),
    (($entry | ConvertTo-Json -Depth 5) + "`n"), $enc)
Write-Host ("[g3-flows] exit={0}" -f $code)
exit ([int]($code -ne 0))
