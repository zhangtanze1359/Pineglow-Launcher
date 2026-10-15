param([switch]$Isolated, [string]$RuntimeVersion = '10.0.0', [int]$DebugPort = 0)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$buildRoot = Join-Path $repoRoot 'dev\PineglowLauncher\bin\Debug\net10.0'
$launcherDll = Join-Path $buildRoot 'Pineglow.Launcher.dll'
if (-not (Test-Path -LiteralPath $launcherDll)) {
 & dotnet build (Join-Path $repoRoot 'dev\PineglowLauncher\Pineglow.Launcher.csproj') -m
 if ($LASTEXITCODE -ne 0) { throw 'Launcher build failed.' }
}
# Local compatibility entry only: no installed runtime or system setting is changed.
# The default runtime selected on this machine failed HttpListener; 10.0.0 was verified separately.
$runRoot = $buildRoot
if ($Isolated) {
 $runRoot = Join-Path $repoRoot 'docs\ui-integration-2026-10-15\preview'
 New-Item -ItemType Directory -Path $runRoot -Force | Out-Null
 Copy-Item -Path (Join-Path $buildRoot '*') -Destination $runRoot -Recurse -Force
 [IO.File]::WriteAllText((Join-Path $runRoot 'portable.flag'), 'new-ui-local-preview')
}
$portProbe = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback,0)
$portProbe.Start(); $apiPort = $portProbe.LocalEndpoint.Port; $portProbe.Stop()
$start = [Diagnostics.ProcessStartInfo]::new('dotnet.exe')
$start.UseShellExecute = $false
$start.CreateNoWindow = $true
$start.WorkingDirectory = $runRoot
$start.ArgumentList.Add('--fx-version'); $start.ArgumentList.Add($RuntimeVersion)
$start.ArgumentList.Add((Join-Path $runRoot 'Pineglow.Launcher.dll'))
$start.Environment['PINEGLOW_API_PORT'] = [string]$apiPort
$start.Environment['PINEGLOW_UI_DIR'] = Join-Path $runRoot 'UI'
$start.Environment['WEBVIEW2_USER_DATA_FOLDER'] = Join-Path $runRoot 'webview2-profile'
if ($DebugPort -gt 0) { $start.Environment['PINEGLOW_WEBVIEW_DEBUG']=[string]$DebugPort }
else { $start.Environment.Remove('PINEGLOW_WEBVIEW_DEBUG') | Out-Null }
$runtimeList = & dotnet --list-runtimes
if (-not ($runtimeList | Where-Object { $_ -like "Microsoft.NETCore.App $RuntimeVersion *" })) {
 throw "The selected local runtime $RuntimeVersion is not installed. Run dotnet --list-runtimes and choose -RuntimeVersion explicitly."
}
$appProcess = [Diagnostics.Process]::Start($start)
if ($Isolated) {
 @{pid=$appProcess.Id;apiPort=$apiPort;runtime=$RuntimeVersion;runRoot=$runRoot} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $runRoot 'owned-preview.json')
}
Write-Output ("新版 UI 已启动；PID={0}；API={1}；Isolated={2}；Runtime={3}" -f $appProcess.Id,$apiPort,$Isolated,$RuntimeVersion)
