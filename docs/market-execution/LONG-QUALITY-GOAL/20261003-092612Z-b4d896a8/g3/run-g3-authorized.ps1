<#
run-g3-authorized.ps1 - LONG-QUALITY-GOAL / G3: run the authorised ordinary regression inside the
GUID execution copy and record the REAL exit code of every stage.

Scope rules honoured here:
  * every stage runs inside -ExecutionRoot (the GUID copy), never in D:\松光启动器;
  * TRX/results are written to -OutDir\test-results (outside the product source tree);
  * only the pre-authorised ordinary sets are executed - the full C# assembly is NEVER invoked
    (it contains the recovery-safety counter-proof range that the platform terminated);
  * a stage that fails does not stop the others: each real exit code is recorded.
ASCII-only source so Windows PowerShell 5.1 parses it identically.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$ExecutionRoot,
    [Parameter(Mandatory = $true)][string]$OutDir
)

$ErrorActionPreference = 'Continue'
if (-not (Test-Path -LiteralPath $ExecutionRoot)) { throw "execution root missing: $ExecutionRoot" }
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$resultsDir = Join-Path $OutDir 'test-results'
New-Item -ItemType Directory -Force -Path $resultsDir | Out-Null

$project = 'dev/PineglowLauncher.Tests/Pineglow.Launcher.Tests.csproj'
$verifyHost = 'dev/PineglowLauncher/VerifyHost/VerifyHost.csproj'
$stages = New-Object System.Collections.ArrayList

function Invoke-Stage {
    param(
        [string]$Id,
        [string]$File,
        [string[]]$Argv,
        [string]$Expect
    )
    $log = Join-Path $OutDir ("{0}.log" -f $Id)
    $started = Get-Date
    Push-Location $ExecutionRoot
    try {
        & $File @Argv 2>&1 | Out-File -FilePath $log -Encoding utf8
        $code = $LASTEXITCODE
    }
    finally { Pop-Location }
    $entry = [ordered]@{
        id         = $Id
        file       = $File
        argv       = ($Argv -join ' ')
        cwd        = $ExecutionRoot
        startedUtc = $started.ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ')
        endedUtc   = (Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ')
        exitCode   = $code
        expected   = $Expect
        rawEvidence = ("g3/{0}.log" -f $Id)
    }
    [void]$stages.Add($entry)
    Write-Host ("[{0}] exit={1}" -f $Id, $code)
    return $code
}

# 1) build the test assembly once; later stages reuse it with --no-build
Invoke-Stage -Id 'g3-build-tests' -File 'dotnet' -Argv @('build', $project, '-c', 'Release') -Expect 'exit 0' | Out-Null

# 2) credential/config isolation speciality (R2 authorised ordinary set)
Invoke-Stage -Id 'g3-secrets' -File 'dotnet' -Argv @('test', $project, '-c', 'Release', '--no-build',
    '--filter', 'FullyQualifiedName~ConfigPreservationTests|FullyQualifiedName~SecretStoreBackendTests',
    '--logger', 'trx;LogFileName=secrets.trx', '--results-directory', $resultsDir) -Expect 'exit 0, non-zero tests, no skip' | Out-Null

# 3) ordinary structured-failure / restore HTTP set
Invoke-Stage -Id 'g3-ordinary-r2' -File 'dotnet' -Argv @('test', $project, '-c', 'Release', '--no-build',
    '--filter', 'FullyQualifiedName~RollbackFailure_HttpExposesStructuredRecoveryFields|FullyQualifiedName~RollbackSuccess_HttpReportsNoRecoveryNeeded|FullyQualifiedName~MetadataFailure_HttpExposesStructuredFields|FullyQualifiedName~ModReplaceUndo_NormalRestore_StillWorksWithTrustedRoot|FullyQualifiedName~SecretBackend_ConcurrentAsyncFlowsStayIsolated',
    '--logger', 'trx;LogFileName=ordinary-r2.trx', '--results-directory', $resultsDir) -Expect 'exit 0, non-zero tests, no skip' | Out-Null

# 4) ordinary recovery (quarantine / restore / conflict) disk set
Invoke-Stage -Id 'g3-recovery-normal' -File 'dotnet' -Argv @('test', $project, '-c', 'Release', '--no-build',
    '--filter', 'FullyQualifiedName~DeleteMod_QuarantinesJarAndSidecar_KeepsByteIdenticalCopies|FullyQualifiedName~DeleteMod_DisabledMod_QuarantinesDotDisabledName|FullyQualifiedName~Restore_PutsJarAndSidecarBackByteIdentical|FullyQualifiedName~Restore_Twice_IsIdempotent_AndKeepsFileContent|FullyQualifiedName~Restore_WithDifferentFileAtOriginalPath_ReportsConflictAndDoesNotOverwrite|FullyQualifiedName~DeleteMod_LeavesEveryOtherFileUntouched',
    '--logger', 'trx;LogFileName=recovery-normal.trx', '--results-directory', $resultsDir) -Expect 'exit 0, non-zero tests, no skip' | Out-Null

# 5) every node test entry that ships in ui/tests (includes this goal's behaviour suite)
$nodeInputs = @(Get-ChildItem (Join-Path $ExecutionRoot 'ui/tests') -Filter '*.test.cjs' |
    Select-Object -ExpandProperty FullName)
$nodeInputs | Set-Content (Join-Path $OutDir 'node-inputs.txt') -Encoding utf8
Invoke-Stage -Id 'g3-node-all' -File 'node' -Argv (@('--test') + $nodeInputs) -Expect 'exit 0, non-zero tests, no skip' | Out-Null

# 6) VerifyHost release build + the ordinary bisect/file sandbox smoke
Invoke-Stage -Id 'g3-verifyhost-build' -File 'dotnet' -Argv @('build', $verifyHost, '-c', 'Release') -Expect 'exit 0' | Out-Null
$verifyExe = Join-Path $ExecutionRoot 'dev/PineglowLauncher/VerifyHost/bin/Release/net10.0/VerifyHost.exe'
if (Test-Path -LiteralPath $verifyExe) {
    Invoke-Stage -Id 'g3-bisect-smoke' -File $verifyExe -Argv @('--bisect-smoke') -Expect 'exit 0, ordinary loopback HTTP + file sandbox only' | Out-Null
}
else {
    $stages.Add([ordered]@{ id = 'g3-bisect-smoke'; file = $verifyExe; argv = '--bisect-smoke';
        exitCode = -1; expected = 'exit 0'; rawEvidence = 'n/a'; note = 'VerifyHost.exe not found after build' })
    Write-Host '[g3-bisect-smoke] skipped: VerifyHost.exe missing'
}

$summary = [ordered]@{
    schemaVersion = 1
    generatedUtc  = (Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ')
    executionRoot = $ExecutionRoot
    resultsDir    = $resultsDir
    stages        = $stages
}
$enc = New-Object System.Text.UTF8Encoding($false)
[System.IO.File]::WriteAllText((Join-Path $OutDir 'g3-results.json'),
    (($summary | ConvertTo-Json -Depth 6) + "`n"), $enc)

$failed = @($stages | Where-Object { $_.exitCode -ne 0 })
Write-Host ("G3 stages: {0}; failed: {1}" -f $stages.Count, $failed.Count)
exit ([int]($failed.Count -gt 0))
