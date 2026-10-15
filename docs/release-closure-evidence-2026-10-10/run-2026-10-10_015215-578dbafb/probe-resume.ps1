param(
  [Parameter(Mandatory)][string]$ExePath,
  [Parameter(Mandatory)][string]$OutDir,
  [string]$VersionId = '1.21.11',
  [int]$InterruptAfterSeconds = 25,
  [int]$MinBytesBeforeInterrupt = 20MB,
  [int]$CompleteTimeoutSec = 2400,
  [int]$StartupTimeoutSec = 120
)
# Round-11 probe (own tool, product source untouched).
# Verifies the README claim "断点续传 + 可恢复任务队列" headlessly:
#   1) start a real install, let it download for a while,
#   2) kill ONLY this fixture's launcher process (simulated crash),
#   3) inspect what survived on disk (partial *.download files),
#   4) restart and re-issue the install, sampling the SAME partial file to see whether it
#      resumes (size never drops back to 0) or restarts from scratch,
#   5) wait for completion and verify the installed tree.
$ErrorActionPreference = 'Stop'
[void](New-Item -ItemType Directory -Path $OutDir -Force)

function Get-FreePort {
  $l = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0); $l.Start()
  $p = ([Net.IPEndPoint]$l.LocalEndpoint).Port; $l.Stop(); return $p
}
function OwnedProcs([string]$exePath) {
  @(Get-CimInstance Win32_Process -ErrorAction SilentlyContinue | Where-Object { $_.ExecutablePath -and ($_.ExecutablePath -ieq $exePath) })
}
function Api([int]$port, [string]$token, [string]$method, [string]$path, $body, [int]$timeoutSec = 60) {
  $hdr = @{ 'X-Pineglow-Token' = $token }
  try {
    if ($null -ne $body) {
      $json = if ($body -is [string]) { $body } else { $body | ConvertTo-Json -Compress }
      $r = Invoke-RestMethod "http://127.0.0.1:$port$path" -Method $method -Headers $hdr -ContentType 'application/json; charset=utf-8' -Body ([Text.Encoding]::UTF8.GetBytes($json)) -TimeoutSec $timeoutSec
    } else { $r = Invoke-RestMethod "http://127.0.0.1:$port$path" -Method $method -Headers $hdr -TimeoutSec $timeoutSec }
    return [pscustomobject]@{ ok = $true; body = $r; text = ($r | ConvertTo-Json -Compress -Depth 8) }
  } catch {
    $d = ''; try { $d = [string]$_.ErrorDetails.Message } catch { }
    if (-not $d) { $d = $_.Exception.Message }
    return [pscustomobject]@{ ok = $false; body = $null; text = $d }
  }
}
function Start-Launcher([string]$exe, [string]$wd, [string]$outDir, [string]$tag) {
  foreach ($item in @(Get-ChildItem Env: | Where-Object { $_.Name -like 'PINEGLOW_*' })) { Remove-Item -LiteralPath ('Env:' + $item.Name) }
  $port = Get-FreePort
  $env:PINEGLOW_API_PORT = "$port"
  $p = Start-Process -FilePath $exe -WorkingDirectory $wd -PassThru -WindowStyle Hidden `
       -RedirectStandardOutput (Join-Path $outDir "$tag-stdout.log") -RedirectStandardError (Join-Path $outDir "$tag-stderr.log")
  $token = $null
  $deadline = (Get-Date).AddSeconds($StartupTimeoutSec)
  while ((Get-Date) -lt $deadline -and -not $token) {
    Start-Sleep -Milliseconds 700
    try {
      $h = Invoke-WebRequest "http://127.0.0.1:$port/" -UseBasicParsing -TimeoutSec 5
      if ($h.Content -match 'name="pineglow-session-token"\s+content="([0-9A-Fa-f]+)"') { $token = $Matches[1] }
    } catch { }
  }
  return [pscustomobject]@{ proc = $p; port = $port; token = $token }
}
function TreeBytes([string]$root) {
  if (-not (Test-Path -LiteralPath $root)) { return 0 }
  $s = (Get-ChildItem $root -Recurse -File -Force -ErrorAction SilentlyContinue | Measure-Object Length -Sum).Sum
  if (-not $s) { return 0 }
  return [int64]$s
}
function Partials([string]$root) {
  @(Get-ChildItem $root -Recurse -File -Force -Filter '*.download' -ErrorAction SilentlyContinue |
    Sort-Object Length -Descending)
}

$root = Join-Path ([IO.Path]::GetTempPath()) ('pineglow-resume-' + [guid]::NewGuid().ToString('N'))
$cold = Join-Path $root 'install'
[void](New-Item -ItemType Directory -Path $cold -Force)
$targetExe = Join-Path $cold 'Pineglow.Launcher.exe'
Copy-Item -LiteralPath $ExePath -Destination $targetExe -Force
[IO.File]::WriteAllText((Join-Path $cold 'portable.flag'), '')
$env:DOTNET_BUNDLE_EXTRACT_BASE_DIR = Join-Path $cold 'extract'
$mc = Join-Path $cold 'data\.minecraft'

$result = [ordered]@{
  probe = 'install-interrupt-and-resume'; version = $VersionId; fixture = $cold
  installedExeSha256 = (Get-FileHash -LiteralPath $targetExe).Hash
  phase1 = [ordered]@{}; afterKill = [ordered]@{}; queueAfterRestart = ''; phase2 = [ordered]@{}
  resumedFromPartial = $false; partialSizeBefore = 0; partialFirstSampleAfter = 0
  finalStatus = ''; finalSeconds = 0; finalTreeBytes = 0
  finalVersionJson = 0; finalVersionJars = 0; finalLibraryJars = 0; finalAssetObjects = 0; leftoverDownloads = 0
  ownedProcessesStopped = $false
  timeHK = [TimeZoneInfo]::ConvertTimeBySystemTimeZoneId([datetime]::UtcNow, 'China Standard Time').ToString('o')
}

$s1 = $null; $s2 = $null
try {
  # ---- phase 1: start a real install and let it run
  $s1 = Start-Launcher $targetExe $cold $OutDir 'phase1'
  if (-not $s1.token) { throw 'launcher did not become ready (phase 1)' }
  $sw1 = [Diagnostics.Stopwatch]::StartNew()
  $dl1 = Api $s1.port $s1.token 'POST' '/api/versions/download' @{ versionId = $VersionId } 120
  $result.phase1.downloadCall = $dl1.text.Substring(0, [Math]::Min(200, $dl1.text.Length))
  $state = ''
  # wait the FULL window (an early byte-threshold break lands in the small-file phase, where no
  # *.download temp survives long enough to observe; the bulk assets phase needs more time)
  while ($sw1.Elapsed.TotalSeconds -lt $InterruptAfterSeconds) { Start-Sleep -Seconds 3 }
  $sw1.Stop()
  $result.phase1.elapsedSeconds = [int]$sw1.Elapsed.TotalSeconds
  $result.phase1.treeBytes = TreeBytes $mc
  $pl1 = Partials $mc
  $result.phase1.partialCount = @($pl1).Count
  $result.phase1.partialTop = @($pl1 | Select-Object -First 3 | ForEach-Object { [ordered]@{ name = $_.Name; bytes = $_.Length } })
  $largest = @($pl1 | Select-Object -First 1)
  $largestPath = if ($largest.Count -gt 0) { $largest[0].FullName } else { '' }
  $result.phase1.largestPartialPath = $largestPath

  # ---- kill ONLY this fixture's launcher
  foreach ($o in (OwnedProcs $targetExe)) { Stop-Process -Id $o.ProcessId -Force -ErrorAction SilentlyContinue }
  Start-Sleep -Seconds 3
  $result.afterKill.killedProcesses = @(OwnedProcs $targetExe).Count
  $result.afterKill.treeBytes = TreeBytes $mc
  $pl2 = Partials $mc
  $result.afterKill.partialCount = @($pl2).Count
  $result.phase1.processStopped = (@(OwnedProcs $targetExe).Count -eq 0)

  # ---- restart: is the task queue still there?
  $s2 = Start-Launcher $targetExe $cold $OutDir 'phase2'
  if (-not $s2.token) { throw 'launcher did not become ready (phase 2)' }
  $q = Api $s2.port $s2.token 'GET' '/api/downloads' $null 60
  $result.queueAfterRestart = $q.text.Substring(0, [Math]::Min(400, $q.text.Length))

  # ---- phase 2: re-issue the SAME install, watch the largest partial file
  $before = 0
  if ($largestPath -and (Test-Path -LiteralPath $largestPath)) { $before = (Get-Item -LiteralPath $largestPath).Length }
  $result.partialSizeBefore = [int64]$before
  $dl2 = Api $s2.port $s2.token 'POST' '/api/versions/download' @{ versionId = $VersionId } 120
  $result.phase2.downloadCall = $dl2.text.Substring(0, [Math]::Min(200, $dl2.text.Length))
  $samples = @()
  $firstSample = -1
  for ($i = 0; $i -lt 6; $i++) {
    Start-Sleep -Seconds 2
    if ($largestPath -and (Test-Path -LiteralPath $largestPath)) {
      $len = (Get-Item -LiteralPath $largestPath).Length
      $samples += $len
      if ($firstSample -lt 0) { $firstSample = $len }
    } else { $samples += -1; if ($firstSample -lt 0) { $firstSample = -1 } }
  }
  $result.phase2.samples = $samples
  $result.partialFirstSampleAfter = [int64]$firstSample
  # resumed if the file never dropped below where it was (a from-scratch retry truncates it to 0)
  $result.resumedFromPartial = [bool](($before -gt 0) -and ($firstSample -ge $before))

  $sw2 = [Diagnostics.Stopwatch]::StartNew(); $state2 = ''
  while ($sw2.Elapsed.TotalSeconds -lt $CompleteTimeoutSec) {
    Start-Sleep -Seconds 5
    $st = Api $s2.port $s2.token 'GET' '/api/downloads' $null 60
    if ($st.ok -and $st.body) {
      $tasks = $null
      foreach ($prop in @('downloads', 'tasks', 'items')) { if ($st.body.PSObject.Properties.Name -contains $prop) { $tasks = $st.body.$prop; break } }
      if ($null -eq $tasks -and $st.body -is [array]) { $tasks = $st.body }
      $mine = @($tasks | Where-Object { $_.versionId -eq $VersionId } | Select-Object -First 1)
      if ($mine.Count -gt 0) {
        $state2 = [string]$mine[0].status
        if ($state2 -in @('completed', 'failed', 'cancelled', 'error')) { break }
      }
    }
    if ((TreeBytes $mc) -gt 0 -and $state2 -eq '' -and $sw2.Elapsed.TotalSeconds -gt 30) { }
  }
  $sw2.Stop()
  $result.finalStatus = $state2
  $result.finalSeconds = [int]$sw2.Elapsed.TotalSeconds
  $result.finalTreeBytes = TreeBytes $mc
  $result.finalVersionJson = @(Get-ChildItem (Join-Path $mc "versions\$VersionId") -Filter '*.json' -File -ErrorAction SilentlyContinue).Count
  $result.finalVersionJars = @(Get-ChildItem (Join-Path $mc "versions\$VersionId") -Filter '*.jar' -File -ErrorAction SilentlyContinue).Count
  $result.finalLibraryJars = @(Get-ChildItem (Join-Path $mc 'libraries') -Recurse -Filter '*.jar' -File -ErrorAction SilentlyContinue).Count
  $result.finalAssetObjects = @(Get-ChildItem (Join-Path $mc 'assets\objects') -Recurse -File -ErrorAction SilentlyContinue).Count
  $result.leftoverDownloads = @(Partials $mc).Count
}
finally {
  foreach ($s in @($s1, $s2)) { if ($s -and $s.proc -and -not $s.proc.HasExited) { Stop-Process -Id $s.proc.Id -Force -ErrorAction SilentlyContinue } }
  foreach ($o in (OwnedProcs $targetExe)) { Stop-Process -Id $o.ProcessId -Force -ErrorAction SilentlyContinue }
  $result.ownedProcessesStopped = (@(OwnedProcs $targetExe).Count -eq 0)
  $result | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $OutDir 'resume.json') -Encoding utf8
  $result | ConvertTo-Json -Depth 6 | Write-Output
}
if (-not $result.phase1.processStopped) { exit 2 }
exit 0
