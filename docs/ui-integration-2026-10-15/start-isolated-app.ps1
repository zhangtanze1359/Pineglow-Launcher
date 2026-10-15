$ErrorActionPreference='Stop'
Set-Location -LiteralPath 'D:\松光启动器'
$runRoot = Join-Path (Get-Location).Path 'docs\ui-integration-2026-10-15\runtime'
New-Item -ItemType Directory -Path $runRoot -Force | Out-Null
Copy-Item -Path 'dev\PineglowLauncher\bin\Debug\net10.0\*' -Destination $runRoot -Recurse -Force
[IO.File]::WriteAllText((Join-Path $runRoot 'portable.flag'),'ui-integration-verification')
$dataRoot=Join-Path $runRoot 'data'
New-Item -ItemType Directory -Path $dataRoot -Force | Out-Null
if (-not (Test-Path -LiteralPath (Join-Path $dataRoot 'config.json'))) {
 [IO.File]::WriteAllText((Join-Path $dataRoot 'config.json'),'{"Instances":[],"Accounts":[],"MinimizeOnLaunch":false,"AutoCloseOnExit":false,"GameDirectory":"D:\\松光启动器\\docs\\ui-integration-2026-10-15\\runtime\\game"}')
}
$env:PINEGLOW_API_PORT='49372'
$env:PINEGLOW_WEBVIEW_DEBUG='49373'
$env:PINEGLOW_UI_DIR=Join-Path (Get-Location).Path 'ui'
$env:TEMP=Join-Path (Get-Location).Path 'docs\ui-integration-2026-10-15\temp'
$env:TMP=$env:TEMP
$appProcess=Start-Process -FilePath (Join-Path $runRoot 'Pineglow.Launcher.exe') -WorkingDirectory $runRoot -WindowStyle Hidden -PassThru
[IO.File]::WriteAllText((Join-Path $runRoot 'owned-process.json'),(@{pid=$appProcess.Id;apiPort=49372;debugPort=49373;sourceRoot=(Get-Location).Path}|ConvertTo-Json))
Write-Output ('Isolated app PID='+$appProcess.Id)
