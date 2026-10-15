param(
  [Parameter(Mandatory)][string]$InstalledExe,      # the build under test = the installed version
  [Parameter(Mandatory)][string]$ManifestServer,    # serve-manifest.cjs
  [Parameter(Mandatory)][string]$AssetUrl,          # public HTTPS artifact
  [Parameter(Mandatory)][long]  $AssetSize,
  [Parameter(Mandatory)][string]$AssetSha256,
  [Parameter(Mandatory)][string]$OutDir,
  [ValidateSet('positive','negative')][string]$Mode = 'positive',
  [string]$PrePlaceNativeLibs = '',   # fixture accommodation for the published 1.0.0 artifact's known R01 defect
  [string]$OfferedVersion = '1.0.2',
  [int]$StartupTimeoutSec = 120,
  [int]$DownloadTimeoutSec = 600
)
# Round-5 probe (own tool, product source untouched).
# Exercises the REAL update chain that round 3 had to leave open: local HTTP manifest (allowed scheme)
# + public HTTPS artifact (the product forces https for the artifact). No upload, no trust-store change.
#   positive: manifest matches the real asset -> download, verify, replace, restart.
#   negative: manifest carries a wrong sha256 -> the chain must refuse and leave the exe untouched.
# Only ever stops processes whose executable path is inside this run's own fixture.
$ErrorActionPreference = 'Stop'
[void](New-Item -ItemType Directory -Path $OutDir -Force)

function Get-FreePort {
  $l = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0); $l.Start()
  $p = ([Net.IPEndPoint]$l.LocalEndpoint).Port; $l.Stop(); return $p
}
function OwnedFixtureProcs([string]$exePath) {
  @(Get-CimInstance Win32_Process -ErrorAction SilentlyContinue |
    Where-Object { $_.ExecutablePath -and ($_.ExecutablePath -ieq $exePath) })
}
function Api([int]$port, [string]$token, [string]$method, [string]$path, $body, [int]$timeoutSec = 60) {
  $hdr = @{ 'X-Pineglow-Token' = $token }
  try {
    if ($null -ne $body) {
      $json = if ($body -is [string]) { $body } else { $body | ConvertTo-Json -Compress }
      $r = Invoke-RestMethod "http://127.0.0.1:$port$path" -Method $method -Headers $hdr -ContentType 'application/json; charset=utf-8' -Body ([Text.Encoding]::UTF8.GetBytes($json)) -TimeoutSec $timeoutSec
    } else {
      $r = Invoke-RestMethod "http://127.0.0.1:$port$path" -Method $method -Headers $hdr -TimeoutSec $timeoutSec
    }
    return [pscustomobject]@{ ok = $true; body = $r; text = ($r | ConvertTo-Json -Compress -Depth 6) }
  } catch {
    $detail = ''
    try { $detail = [string]$_.ErrorDetails.Message } catch { }
    if (-not $detail) { $detail = $_.Exception.Message }
    return [pscustomobject]@{ ok = $false; body = $null; text = $detail }
  }
}

$runRoot = Join-Path ([IO.Path]::GetTempPath()) ('pineglow-chain-' + $Mode + '-' + [guid]::NewGuid().ToString('N'))
$cold = Join-Path $runRoot 'install'
$dataDir = Join-Path $cold 'data'
[void](New-Item -ItemType Directory -Path $dataDir -Force)
$targetExe = Join-Path $cold 'Pineglow.Launcher.exe'
Copy-Item -LiteralPath $InstalledExe -Destination $targetExe -Force
[IO.File]::WriteAllText((Join-Path $cold 'portable.flag'), '')
$shaBefore = (Get-FileHash -LiteralPath $targetExe).Hash
$placed = @()
if ($PrePlaceNativeLibs -and (Test-Path -LiteralPath $PrePlaceNativeLibs)) {
  foreach ($f in 'libSkiaSharp.dll', 'libHarfBuzzSharp.dll', 'av_libglesv2.dll') {
    $p = Join-Path $PrePlaceNativeLibs $f
    if (Test-Path -LiteralPath $p) { Copy-Item -LiteralPath $p -Destination (Join-Path $cold $f) -Force; $placed += $f }
  }
}

# manifest: what the local server hands out
$srvPort = Get-FreePort
$manifestPath = Join-Path $OutDir ("manifest-$Mode.json")
$shaOffered = if ($Mode -eq 'positive') { $AssetSha256 } else { ('a' * 64) }
$manifest = [ordered]@{
  version = $OfferedVersion; url = $AssetUrl; size = $AssetSize; sha256 = $shaOffered
  notes = "round-5 chain probe ($Mode)"; mandatory = $false
}
($manifest | ConvertTo-Json -Depth 4) | Set-Content -LiteralPath $manifestPath -Encoding utf8
$reqLog = Join-Path $OutDir "manifest-requests-$Mode.log"
if (Test-Path -LiteralPath $reqLog) { Remove-Item -LiteralPath $reqLog -Force }

# the installed build reads its manifest source from config.json (http is an allowed scheme)
[IO.File]::WriteAllText((Join-Path $dataDir 'config.json'), ('{"updateUrl":"http://127.0.0.1:' + $srvPort + '/manifest.json"}'), (New-Object Text.UTF8Encoding($false)))

foreach ($item in @(Get-ChildItem Env: | Where-Object { $_.Name -like 'PINEGLOW_*' })) { Remove-Item -LiteralPath ('Env:' + $item.Name) }
$env:DOTNET_BUNDLE_EXTRACT_BASE_DIR = Join-Path $cold 'extract'

$result = [ordered]@{
  probe = 'real-update-chain'; mode = $Mode
  fixture = $cold; apiPort = 0
  installedExeSha256 = $shaBefore
  offeredVersion = $OfferedVersion; offeredSha256 = $shaOffered
  assetUrl = $AssetUrl; assetSize = $AssetSize
  manifestFetches = 0
  currentVersion = ''; checkResult = ''; checkSaysUpdate = $false
  download = ''; downloadState = ''; downloadError = ''
  apply = ''
  exeReplaced = $false; exeShaAfter = ''; exeEqualsOfferedAsset = $false
  applyStateResult = ''; replaceScriptSaved = $false
  restartedHealthy = $false; restartedVersion = ''
  nativeLibsPrePlaced = @()
  ownedProcessesStopped = $false
  timeHK = [TimeZoneInfo]::ConvertTimeBySystemTimeZoneId([datetime]::UtcNow, 'China Standard Time').ToString('o')
}

$srv = $null; $proc = $null
$result.nativeLibsPrePlaced = $placed
try {
  $srv = Start-Process -FilePath 'node' -ArgumentList ('"' + $ManifestServer + '" ' + $srvPort + ' "' + $manifestPath + '" "' + $reqLog + '"') -PassThru -WindowStyle Hidden
  Start-Sleep -Seconds 2

  $port = Get-FreePort
  $result.apiPort = $port
  $env:PINEGLOW_API_PORT = "$port"
  $proc = Start-Process -FilePath $targetExe -WorkingDirectory $cold -PassThru -WindowStyle Hidden `
    -RedirectStandardOutput (Join-Path $OutDir "chain-$Mode-stdout.log") -RedirectStandardError (Join-Path $OutDir "chain-$Mode-stderr.log")

  $token = $null
  $deadline = (Get-Date).AddSeconds($StartupTimeoutSec)
  while ((Get-Date) -lt $deadline -and -not $token) {
    Start-Sleep -Milliseconds 700
    try {
      $h = Invoke-WebRequest "http://127.0.0.1:$port/" -UseBasicParsing -TimeoutSec 5
      if ($h.Content -match 'name="pineglow-session-token"\s+content="([0-9A-Fa-f]+)"') { $token = $Matches[1] }
    } catch { }
  }
  if (-not $token) { throw 'launcher did not become ready' }

  $cur = Api $port $token 'GET' '/api/update' $null
  if ($cur.ok -and $cur.body.PSObject.Properties.Name -contains 'currentVersion') { $result.currentVersion = [string]$cur.body.currentVersion }

  $check = Api $port $token 'GET' '/api/update/check' $null 180
  $result.checkResult = $check.text.Substring(0, [Math]::Min(400, $check.text.Length))
  if ($check.ok -and $check.body.PSObject.Properties.Name -contains 'updateAvailable') { $result.checkSaysUpdate = [bool]$check.body.updateAvailable }
  Start-Sleep -Seconds 2
  $reqs = @(); if (Test-Path -LiteralPath $reqLog) { $reqs = @(Get-Content -LiteralPath $reqLog) }
  $result.manifestFetches = @($reqs | Where-Object { $_ -match 'manifest\.json' }).Count

  $dl = Api $port $token 'POST' '/api/update/download' '{}' $DownloadTimeoutSec
  $result.download = $dl.text.Substring(0, [Math]::Min(300, $dl.text.Length))

  $sw = [Diagnostics.Stopwatch]::StartNew(); $state = ''
  while ($sw.Elapsed.TotalSeconds -lt $DownloadTimeoutSec) {
    Start-Sleep -Seconds 2
    $st = Api $port $token 'GET' '/api/update' $null
    if ($st.ok -and $st.body) {
      if ($st.body.PSObject.Properties.Name -contains 'state') { $state = [string]$st.body.state }
      if ($st.body.PSObject.Properties.Name -contains 'error') { $result.downloadError = [string]$st.body.error }
    }
    if ($state -in @('completed', 'ready', 'failed')) { break }
    if ($state -eq 'idle' -and $sw.Elapsed.TotalSeconds -gt 4) { break }
  }
  $result.downloadState = $state
  $sw.Stop()

  if ($Mode -eq 'positive') {
    $ap = Api $port $token 'POST' '/api/update/apply' '{}' 120
    $result.apply = $ap.text.Substring(0, [Math]::Min(300, $ap.text.Length))
    # wait for the real replacement done by the generated script
    $dl2 = (Get-Date).AddSeconds(120)
    while ((Get-Date) -lt $dl2) {
      Start-Sleep -Seconds 3
      if (Test-Path -LiteralPath $targetExe) {
        $result.exeShaAfter = (Get-FileHash -LiteralPath $targetExe).Hash
        if ($result.exeShaAfter -ne $shaBefore) { $result.exeReplaced = $true; break }
      }
    }
    $result.exeEqualsOfferedAsset = ($result.exeShaAfter -eq $AssetSha256)
    # apply-state.json and the generated script live in the downloaded package's directory (the updates dir)
    $stateFiles = @(Get-ChildItem $cold -Recurse -File -Filter 'apply-state.json' -ErrorAction SilentlyContinue)
    if ($stateFiles.Count -gt 0) {
      $txt = [IO.File]::ReadAllText($stateFiles[0].FullName)
      try { $result.applyStateResult = [string](($txt | ConvertFrom-Json).result) } catch { $result.applyStateResult = $txt }
      Copy-Item -LiteralPath $stateFiles[0].FullName -Destination (Join-Path $OutDir 'apply-state.json') -Force -ErrorAction SilentlyContinue
    }
    $scripts = @(Get-ChildItem $cold -Recurse -File -ErrorAction SilentlyContinue |
                 Where-Object { $_.Name -like 'apply-update*' -or $_.Extension -in @('.cmd', '.bat') })
    foreach ($s in $scripts) {
      Copy-Item -LiteralPath $s.FullName -Destination (Join-Path $OutDir ("replace-script-" + $s.Name + ".txt")) -Force -ErrorAction SilentlyContinue
      $result.replaceScriptSaved = $true
    }
    # the apply script restarts the target itself; wait for the restarted process to answer
    $swR = [Diagnostics.Stopwatch]::StartNew()
    while ($swR.Elapsed.TotalSeconds -lt 90) {
      Start-Sleep -Seconds 3
      try {
        $rr = Invoke-WebRequest "http://127.0.0.1:$port/api/health" -UseBasicParsing -TimeoutSec 5
        if ($rr.StatusCode -eq 200) {
          $result.restartedHealthy = $true
          $hh = (Invoke-WebRequest "http://127.0.0.1:$port/" -UseBasicParsing -TimeoutSec 10).Content
          if ($hh -match 'name="pineglow-session-token"\s+content="([0-9A-Fa-f]+)"') {
            $uu = Api $port $Matches[1] 'GET' '/api/update' $null
            if ($uu.ok -and $uu.body -and ($uu.body.PSObject.Properties.Name -contains 'currentVersion')) { $result.restartedVersion = [string]$uu.body.currentVersion }
          }
          break
        }
      } catch { }
    }
    $swR.Stop()
  }
  else {
    # negative: the chain must refuse; give the state machine a moment, then confirm nothing changed
    Start-Sleep -Seconds 5
    $result.exeShaAfter = (Get-FileHash -LiteralPath $targetExe).Hash
    $result.exeReplaced = ($result.exeShaAfter -ne $shaBefore)
  }
}
finally {
  if ($proc -and -not $proc.HasExited) { Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue }
  $others = OwnedFixtureProcs $targetExe
  foreach ($o in $others) { Stop-Process -Id $o.ProcessId -Force -ErrorAction SilentlyContinue }
  $result.ownedProcessesStopped = (@(OwnedFixtureProcs $targetExe).Count -eq 0)
  if ($srv -and -not $srv.HasExited) { Stop-Process -Id $srv.Id -Force -ErrorAction SilentlyContinue }
  $result | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $OutDir "chain-$Mode.json") -Encoding utf8
  $result | ConvertTo-Json -Depth 6 | Write-Output
}
if ($Mode -eq 'positive') {
  if (-not $result.restartedHealthy) { exit 3 }
  if (-not $result.exeEqualsOfferedAsset) { exit 4 }
  if ($result.applyStateResult -ne 'applied') { exit 5 }
  exit 0
}
if ($result.exeReplaced) { exit 6 }        # negative case must NOT replace
exit 0
