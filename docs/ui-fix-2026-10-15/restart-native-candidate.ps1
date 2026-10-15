$ErrorActionPreference='Stop'
$ownedPath=Join-Path $PSScriptRoot 'native-final\owned-process.json'
$owned=Get-Content -Raw -LiteralPath $ownedPath | ConvertFrom-Json
$nativeExpected=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot 'native-final'))
if([IO.Path]::GetFullPath($owned.executionRoot) -ne $nativeExpected){throw 'Unexpected execution root'}
$ownedProcess=Get-CimInstance Win32_Process -Filter ('ProcessId='+$owned.pid)
if($ownedProcess -and $ownedProcess.CommandLine.Contains($nativeExpected)){Stop-Process -Id $owned.pid;Wait-Process -Id $owned.pid -ErrorAction SilentlyContinue}
Copy-Item -Path 'D:\松光启动器\dev\PineglowLauncher\bin\Debug\net10.0\*' -Destination $nativeExpected -Recurse -Force
$info=[Diagnostics.ProcessStartInfo]::new('dotnet.exe');$info.UseShellExecute=$false;$info.CreateNoWindow=$true;$info.WorkingDirectory=$nativeExpected
$info.ArgumentList.Add('--fx-version');$info.ArgumentList.Add('10.0.0');$info.ArgumentList.Add((Join-Path $nativeExpected 'Pineglow.Launcher.dll'))
$info.Environment['PINEGLOW_API_PORT']=[string]$owned.apiPort;$info.Environment['PINEGLOW_WEBVIEW_DEBUG']=[string]$owned.debugPort;$info.Environment['PINEGLOW_UI_DIR']=Join-Path $nativeExpected 'UI';$info.Environment['WEBVIEW2_USER_DATA_FOLDER']=Join-Path $nativeExpected 'webview2-profile'
$newProcess=[Diagnostics.Process]::Start($info);$owned.pid=$newProcess.Id;$owned | ConvertTo-Json | Set-Content -LiteralPath $ownedPath -Encoding utf8
$owned | ConvertTo-Json
