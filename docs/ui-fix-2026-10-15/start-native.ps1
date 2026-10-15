param([string]$RunName='native')
$ErrorActionPreference='Stop'
$repoRoot='D:\松光启动器'
if($RunName -notin @('native','native-final')) { throw 'Only task-owned run directories are allowed.' }
$nativeRoot=Join-Path $PSScriptRoot $RunName
if(Test-Path -LiteralPath (Join-Path $nativeRoot 'owned-process.json')) { throw 'Native evidence already exists; choose a fresh run directory.' }
New-Item -ItemType Directory -Path $nativeRoot -Force | Out-Null
Copy-Item -Path (Join-Path $repoRoot 'dev\PineglowLauncher\bin\Debug\net10.0\*') -Destination $nativeRoot -Recurse
[IO.File]::WriteAllText((Join-Path $nativeRoot 'portable.flag'),'ui-layout-fix-verification')
New-Item -ItemType Directory -Path (Join-Path $nativeRoot 'data') -Force | Out-Null
& python (Join-Path $PSScriptRoot 'seed-native.py') $RunName
if($LASTEXITCODE -ne 0){throw 'Fixture seed failed'}
$port1=[Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback,0);$port1.Start();$apiPort=$port1.LocalEndpoint.Port;$port1.Stop()
$port2=[Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback,0);$port2.Start();$debugPort=$port2.LocalEndpoint.Port;$port2.Stop()
$startInfo=[Diagnostics.ProcessStartInfo]::new('dotnet.exe')
$startInfo.UseShellExecute=$false;$startInfo.CreateNoWindow=$true;$startInfo.WorkingDirectory=$nativeRoot
$startInfo.ArgumentList.Add('--fx-version');$startInfo.ArgumentList.Add('10.0.0');$startInfo.ArgumentList.Add((Join-Path $nativeRoot 'Pineglow.Launcher.dll'))
$startInfo.Environment['PINEGLOW_API_PORT']=[string]$apiPort
$startInfo.Environment['PINEGLOW_WEBVIEW_DEBUG']=[string]$debugPort
$startInfo.Environment['PINEGLOW_UI_DIR']=Join-Path $nativeRoot 'UI'
$startInfo.Environment['WEBVIEW2_USER_DATA_FOLDER']=Join-Path $nativeRoot 'webview2-profile'
$nativeProcess=[Diagnostics.Process]::Start($startInfo)
$owned=@{pid=$nativeProcess.Id;apiPort=$apiPort;debugPort=$debugPort;runtime='10.0.0';sourceRoot=$repoRoot;executionRoot=$nativeRoot}
$owned | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $nativeRoot 'owned-process.json') -Encoding utf8
$owned | ConvertTo-Json
