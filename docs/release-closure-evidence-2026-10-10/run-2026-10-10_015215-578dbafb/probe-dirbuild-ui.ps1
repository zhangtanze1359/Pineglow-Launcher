param(
  [Parameter(Mandatory)][string]$ExePath,
  [Parameter(Mandatory)][string]$OutDir,
  [string]$Marker = 'PG_DIRBUILD_EXTERNAL_UI'
)
# Round-2 probe (own tool, product source untouched).
# Purpose: a NON-single-file deployment (dev tree / directory package) must STILL prefer the
# external UI/ directory next to the exe. The R02 fix only reorders the single-file case;
# this probe proves the directory workflow did not regress.
$ErrorActionPreference = 'Stop'
[void](New-Item -ItemType Directory -Path $OutDir -Force)

$exeDir = Split-Path -Parent $ExePath
$uiDir = Join-Path $exeDir 'UI'
[void](New-Item -ItemType Directory -Path $uiDir -Force)
[IO.File]::WriteAllText((Join-Path $exeDir 'portable.flag'), '')
$indexPath = Join-Path $uiDir 'index.html'
$original = $null
if (Test-Path -LiteralPath $indexPath) { $original = [IO.File]::ReadAllBytes($indexPath) }
$body = if ($original) { [Text.Encoding]::UTF8.GetString($original) } else { '<!doctype html><title>external</title>' }
[IO.File]::WriteAllBytes($indexPath, [Text.Encoding]::UTF8.GetBytes('<!-- ' + $Marker + ' -->' + $body))
$markerHash = (Get-FileHash -LiteralPath $indexPath).Hash

$listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0)
$listener.Start(); $port = ([Net.IPEndPoint]$listener.LocalEndpoint).Port; $listener.Stop()
foreach ($item in @(Get-ChildItem Env: | Where-Object { $_.Name -like 'PINEGLOW_*' })) { Remove-Item -LiteralPath ('Env:' + $item.Name) }
$env:PINEGLOW_API_PORT = [string]$port

$result = [ordered]@{
  probe               = 'dirbuild-external-ui-preference'
  exePath             = $ExePath
  artifactSha256      = (Get-FileHash -LiteralPath $ExePath).Hash
  externalUiSha256    = $markerHash
  apiPort             = $port
  healthReady         = $false
  externalMarkerServed = $false
  servedIndexSha256   = $null
  servedEqualsExternalUi = $false
  ownedProcessStopped = $false
  timeHK              = [TimeZoneInfo]::ConvertTimeBySystemTimeZoneId([datetime]::UtcNow, 'China Standard Time').ToString('o')
}

$p = $null
try {
  $p = Start-Process -FilePath $ExePath -WorkingDirectory $exeDir -PassThru -WindowStyle Hidden `
        -RedirectStandardOutput (Join-Path $OutDir 'dirbuild-stdout.log') -RedirectStandardError (Join-Path $OutDir 'dirbuild-stderr.log')
  for ($i = 0; $i -lt 60; $i++) {
    if ($p.HasExited) { break }
    try {
      $h = Invoke-WebRequest "http://127.0.0.1:$port/api/health" -TimeoutSec 2 -UseBasicParsing
      if ($h.StatusCode -eq 200) { $result.healthReady = $true; break }
    } catch { }
    Start-Sleep -Milliseconds 500
  }
  if ($result.healthReady) {
    $resp = Invoke-WebRequest "http://127.0.0.1:$port/" -TimeoutSec 10 -UseBasicParsing
    $bytes = $resp.RawContentStream.ToArray()
    $result.externalMarkerServed = ([Text.Encoding]::UTF8.GetString($bytes)).Contains($Marker)
    $result.servedIndexSha256 = ([BitConverter]::ToString([Security.Cryptography.SHA256]::Create().ComputeHash($bytes)) -replace '-', '')
    $result.servedEqualsExternalUi = ($result.servedIndexSha256 -eq $markerHash)
  }
}
finally {
  if ($p -and -not $p.HasExited) { Stop-Process -Id $p.Id -Force; $p.WaitForExit(5000) | Out-Null }
  $result.ownedProcessStopped = ($p -and $p.HasExited)
  if ($original) { [IO.File]::WriteAllBytes($indexPath, $original) }
  $result | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $OutDir 'dirbuild-external-ui.json') -Encoding utf8
  $result | ConvertTo-Json -Depth 5 | Write-Output
}
if (-not $result.healthReady) { exit 2 }
if (-not $result.externalMarkerServed) { exit 1 }
exit 0
