$ErrorActionPreference = 'Stop'
Set-Location -LiteralPath 'D:\松光启动器'
$evidenceRoot = Join-Path (Get-Location).Path 'docs\ui-integration-2026-10-15'
$env:TEMP = Join-Path $evidenceRoot 'temp'
$env:TMP = $env:TEMP
$testBin = Join-Path (Get-Location).Path 'dev\PineglowLauncher.Tests\bin\Debug\net10.0'
# This marker belongs to generated test output. It redirects default update/cache paths away from real user data.
New-Item -ItemType Directory -Path $testBin -Force | Out-Null
[IO.File]::WriteAllText((Join-Path $testBin 'portable.flag'), 'ui-integration-tests')
& dotnet test dev/PineglowLauncher.Tests/Pineglow.Launcher.Tests.csproj --no-restore -m --logger 'trx;LogFileName=host-regression.trx' --results-directory docs/ui-integration-2026-10-15/tests *> (Join-Path $evidenceRoot 'dotnet-host-regression.log')
$result = $LASTEXITCODE
[IO.File]::WriteAllText((Join-Path $evidenceRoot 'dotnet-host-exit.txt'), [string]$result)
exit $result
