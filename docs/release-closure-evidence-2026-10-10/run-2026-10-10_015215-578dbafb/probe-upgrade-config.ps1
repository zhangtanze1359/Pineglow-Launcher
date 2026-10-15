param(
  [Parameter(Mandatory)][string]$OldExe,
  [Parameter(Mandatory)][string]$NewExe,
  [Parameter(Mandatory)][string]$OutDir,
  [Parameter(Mandatory)][string]$SourceRoot,
  [string]$StaleMarker = 'PG_PREUPGRADE_STALE_UI',
  [string]$ConfigUrlMarker = 'http://127.0.0.1:9/CONFIG-PRESERVED-MARKER.json'
)
# Round-3 probe (own tool, product source untouched).
# Purpose: cover the two things the update-chain work still lacked, with REAL processes:
#   (1) a real EXE is replaced the way the verified apply script does it (backup -> copy -> byte compare),
#   (2) after the EXE swap the restarted launcher serves the NEW ui and still reads the SAME config.
# The apply script's own execution semantics (backup/replace/verify/rollback/restart on both paths)
# are proven separately by UpdateRecoveryTests; this probe covers the real-binary upgrade outcome.
$ErrorActionPreference = 'Stop'
[void](New-Item -ItemType Directory -Path $OutDir -Force)

function Get-FreePort {
  $l = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0); $l.Start()
  $p = ([Net.IPEndPoint]$l.LocalEndpoint).Port; $l.Stop(); return $p
}
function Wait-Health([int]$Port, [int]$Tries = 60) {
  for ($i = 0; $i -lt $Tries; $i++) {
    try { $r = Invoke-WebRequest "http://127.0.0.1:$Port/api/health" -TimeoutSec 2 -UseBasicParsing; if ($r.StatusCode -eq 200) { return $true } } catch { }
    Start-Sleep -Milliseconds 500
  }
  return $false
}

$root = Join-Path ([IO.Path]::GetTempPath()) ('pineglow-upgcfg-' + [guid]::NewGuid().ToString('N'))
[void](New-Item -ItemType Directory -Path $root -Force)
[void](New-Item -ItemType Directory -Path (Join-Path $root 'UI') -Force)
[void](New-Item -ItemType Directory -Path (Join-Path $root 'data') -Force)
[IO.File]::WriteAllText((Join-Path $root 'portable.flag'), '')
$target = Join-Path $root 'Pineglow.Launcher.exe'
Copy-Item -LiteralPath $OldExe -Destination $target -Force
$oldSha = (Get-FileHash -LiteralPath $target).Hash
$newSha = (Get-FileHash -LiteralPath $NewExe).Hash

# a directory-style install: stale external UI + an existing user config
[IO.File]::WriteAllText((Join-Path $root 'UI/index.html'), '<!-- ' + $StaleMarker + ' --><!doctype html><title>stale</title>')
[IO.File]::WriteAllText((Join-Path $root 'data/config.json'), ('{"updateUrl":"' + $ConfigUrlMarker + '"}'), (New-Object Text.UTF8Encoding($false)))
$configBefore = [IO.File]::ReadAllText((Join-Path $root 'data/config.json'))

foreach ($item in @(Get-ChildItem Env: | Where-Object { $_.Name -like 'PINEGLOW_*' })) { Remove-Item -LiteralPath ('Env:' + $item.Name) }
$env:DOTNET_BUNDLE_EXTRACT_BASE_DIR = Join-Path $root 'extract'

$result = [ordered]@{
  probe                 = 'real-exe-upgrade-ui-and-config'
  fixtureRoot           = $root
  oldExeSha256          = $oldSha
  newExeSha256          = $newSha
  staleExternalUi       = $true
  preUpgrade            = [ordered]@{}
  postUpgrade           = [ordered]@{}
  backupHoldsOldExe     = $false
  replacedExeEqualsNew  = $false
  ownedProcessStopped   = $false
  timeHK                = [TimeZoneInfo]::ConvertTimeBySystemTimeZoneId([datetime]::UtcNow, 'China Standard Time').ToString('o')
}

# ---- phase 1: pre-upgrade install (old exe + stale UI + existing config)
$p1 = $null
$port = Get-FreePort
$env:PINEGLOW_API_PORT = "$port"
try {
  $p1 = Start-Process -FilePath $target -WorkingDirectory $root -PassThru -WindowStyle Hidden `
        -RedirectStandardOutput (Join-Path $OutDir 'pre-stdout.log') -RedirectStandardError (Join-Path $OutDir 'pre-stderr.log')
  $result.preUpgrade.healthReady = Wait-Health $port
  if ($result.preUpgrade.healthReady) {
    $resp = Invoke-WebRequest "http://127.0.0.1:$port/" -TimeoutSec 10 -UseBasicParsing
    $result.preUpgrade.servedStaleMarker = ([Text.Encoding]::UTF8.GetString($resp.RawContentStream.ToArray())).Contains($StaleMarker)
  }
}
finally {
  if ($p1 -and -not $p1.HasExited) { Stop-Process -Id $p1.Id -Force; $p1.WaitForExit(5000) | Out-Null }
}
$result.preUpgrade.processStopped = ($p1 -and $p1.HasExited)

# ---- phase 2: replace the exe exactly the way the verified apply script does it
$backup = "$target.bak-1.0.1"
Copy-Item -LiteralPath $target -Destination $backup -Force
Copy-Item -LiteralPath $NewExe -Destination $target -Force
$fcExit = 0
& cmd /c "fc /b `"$NewExe`" `"$target`" 2>nul >nul"
$fcExit = $LASTEXITCODE
$result.replaceStep = [ordered]@{
  sequence      = 'copy target->backup, copy new->target, fc /b new vs target (same order as BuildApplyScript)'
  fcExitCode    = $fcExit
  byteIdentical = ($fcExit -eq 0)
  replacedSha   = (Get-FileHash -LiteralPath $target).Hash
}
$result.replacedExeEqualsNew = ($result.replaceStep.replacedSha -eq $newSha)
$result.backupHoldsOldExe = ((Get-FileHash -LiteralPath $backup).Hash -eq $oldSha)

# ---- phase 3: restart the upgraded install
$p2 = $null
$port2 = Get-FreePort
$env:PINEGLOW_API_PORT = "$port2"
try {
  $p2 = Start-Process -FilePath $target -WorkingDirectory $root -PassThru -WindowStyle Hidden `
        -RedirectStandardOutput (Join-Path $OutDir 'post-stdout.log') -RedirectStandardError (Join-Path $OutDir 'post-stderr.log')
  $result.postUpgrade.healthReady = Wait-Health $port2
  if ($result.postUpgrade.healthReady) {
    $resp2 = Invoke-WebRequest "http://127.0.0.1:$port2/" -TimeoutSec 10 -UseBasicParsing
    $body = [Text.Encoding]::UTF8.GetString($resp2.RawContentStream.ToArray())
    $result.postUpgrade.servedStaleMarker = $body.Contains($StaleMarker)
    $normalized = $body -replace '(name="pineglow-session-token" content=")[^"]*"', '$1__PINEGLOW_SESSION_TOKEN__"'
    $result.postUpgrade.servedIndexTokenNormalizedSha256 = ([BitConverter]::ToString([Security.Cryptography.SHA256]::Create().ComputeHash([Text.Encoding]::UTF8.GetBytes($normalized))) -replace '-', '')
    $expected = (Get-FileHash -LiteralPath (Join-Path $SourceRoot 'ui/index.html')).Hash
    $result.postUpgrade.expectedSourceIndexSha256 = $expected
    $result.postUpgrade.servedMatchesNewSource = ($result.postUpgrade.servedIndexTokenNormalizedSha256 -eq $expected)
  }
}
finally {
  if ($p2 -and -not $p2.HasExited) { Stop-Process -Id $p2.Id -Force; $p2.WaitForExit(5000) | Out-Null }
}
$result.postUpgrade.processStopped = ($p2 -and $p2.HasExited)

# ---- config survival
$cfgPath = Join-Path $root 'data/config.json'
$result.config = [ordered]@{
  path            = $cfgPath
  existsAfter     = (Test-Path -LiteralPath $cfgPath)
  unchangedAfter  = ((Test-Path -LiteralPath $cfgPath) -and ([IO.File]::ReadAllText($cfgPath) -ceq $configBefore))
  markerPresent   = ((Test-Path -LiteralPath $cfgPath) -and ([IO.File]::ReadAllText($cfgPath).Contains($ConfigUrlMarker)))
}
$result.ownedProcessStopped = [bool]($result.preUpgrade.processStopped -and $result.postUpgrade.processStopped)
$result | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $OutDir 'upgrade-config.json') -Encoding utf8
$result | ConvertTo-Json -Depth 6 | Write-Output

if (-not $result.preUpgrade.healthReady) { exit 2 }
if (-not $result.postUpgrade.healthReady) { exit 3 }
if (-not $result.replacedExeEqualsNew) { exit 4 }
if (-not $result.backupHoldsOldExe) { exit 5 }
if ($result.postUpgrade.servedStaleMarker) { exit 6 }
if (-not $result.config.markerPresent) { exit 7 }
exit 0
