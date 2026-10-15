param(
  [Parameter(Mandatory)][string]$ExePath,
  [Parameter(Mandatory)][string]$OutDir,
  [string]$VersionId = '1.21.4',
  [ValidateSet('install','full')][string]$Mode = 'install',
  [string]$DownloadSource = '',   # '' = leave product default; otherwise POST /api/sources/select before installing
  [int]$DownloadTimeoutSec = 2400,
  [int]$StartupTimeoutSec = 120
)
# Round-6 probe (own tool, product source untouched).
# Drives the REAL install path of the launcher through its own local API in an isolated portable fixture:
#   java detection -> remote version list -> POST /api/versions/download -> poll /api/downloads
#   -> confirm the version is installed and the files are on disk.
# In 'full' mode it also creates an instance, runs the prelaunch check and asks for a launch.
# Only ever stops processes whose executable path is inside this run's own fixture.
$ErrorActionPreference = 'Stop'
[void](New-Item -ItemType Directory -Path $OutDir -Force)

function Get-FreePort {
  $l = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0); $l.Start()
  $p = ([Net.IPEndPoint]$l.LocalEndpoint).Port; $l.Stop(); return $p
}
function OwnedFixtureProcs([string]$exePath) {
  @(Get-CimInstance Win32_Process -ErrorAction SilentlyContinue | Where-Object { $_.ExecutablePath -and ($_.ExecutablePath -ieq $exePath) })
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
    return [pscustomobject]@{ ok = $true; body = $r; text = ($r | ConvertTo-Json -Compress -Depth 8) }
  } catch {
    $detail = ''
    try { $detail = [string]$_.ErrorDetails.Message } catch { }
    if (-not $detail) { $detail = $_.Exception.Message }
    return [pscustomobject]@{ ok = $false; body = $null; text = $detail }
  }
}

$root = Join-Path ([IO.Path]::GetTempPath()) ('pineglow-d6-' + $Mode + '-' + [guid]::NewGuid().ToString('N'))
$cold = Join-Path $root 'install'
[void](New-Item -ItemType Directory -Path $cold -Force)
$targetExe = Join-Path $cold 'Pineglow.Launcher.exe'
Copy-Item -LiteralPath $ExePath -Destination $targetExe -Force
[IO.File]::WriteAllText((Join-Path $cold 'portable.flag'), '')
foreach ($item in @(Get-ChildItem Env: | Where-Object { $_.Name -like 'PINEGLOW_*' })) { Remove-Item -LiteralPath ('Env:' + $item.Name) }
$env:DOTNET_BUNDLE_EXTRACT_BASE_DIR = Join-Path $cold 'extract'

$result = [ordered]@{
  probe = 'real-install-and-launch'; mode = $Mode; requestedVersion = $VersionId
  fixture = $cold; installedExeSha256 = (Get-FileHash -LiteralPath $targetExe).Hash
  apiPort = 0; healthReady = $false
  javaBefore = ''; remoteVersionsListed = 0; remoteVersionPresent = $false
  downloadTaskId = ''; downloadStatus = ''; downloadMessage = ''; downloadSeconds = 0
  installedVersions = @(); versionOnDisk = $false
  versionJsonFiles = 0; versionJars = 0; libraryJars = 0; assetObjects = 0
  instanceId = ''; prelaunchVerdict = ''; launchResponse = ''
  launchObserved = $false; launchLogTail = ''
  ownedProcessesStopped = $false
  timeHK = [TimeZoneInfo]::ConvertTimeBySystemTimeZoneId([datetime]::UtcNow, 'China Standard Time').ToString('o')
}

$proc = $null
try {
  $port = Get-FreePort
  $result.apiPort = $port
  $env:PINEGLOW_API_PORT = "$port"
  $proc = Start-Process -FilePath $targetExe -WorkingDirectory $cold -PassThru -WindowStyle Hidden `
    -RedirectStandardOutput (Join-Path $OutDir "install-$Mode-stdout.log") -RedirectStandardError (Join-Path $OutDir "install-$Mode-stderr.log")

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
  $result.healthReady = $true

  $java = Api $port $token 'GET' '/api/java' $null 60
  $result.javaBefore = $java.text.Substring(0, [Math]::Min(500, $java.text.Length))

  if ($DownloadSource) {
    $sel = Api $port $token 'POST' '/api/sources/select' @{ key = $DownloadSource } 60
    $result.sourcesSelect = $sel.text.Substring(0, [Math]::Min(300, $sel.text.Length))
    $chk = Api $port $token 'GET' '/api/sources' $null 90
    $result.sourcesAfterSelect = $chk.text.Substring(0, [Math]::Min(400, $chk.text.Length))
  }

  $remote = Api $port $token 'GET' '/api/versions/remote' $null 120
  if ($remote.ok -and $remote.body) {
    $arr = $null
    foreach ($prop in @('versions', 'items', 'releases')) { if ($remote.body.PSObject.Properties.Name -contains $prop) { $arr = $remote.body.$prop; break } }
    if ($null -eq $arr -and $remote.body -is [array]) { $arr = $remote.body }
    if ($arr) {
      $result.remoteVersionsListed = @($arr).Count
      $result.remoteVersionPresent = [bool](@($arr) | Where-Object { ("" + $_) -match [regex]::Escape($VersionId) -or ($_.PSObject.Properties.Name -contains 'id' -and $_.id -eq $VersionId) })
    }
  }

  $sw = [Diagnostics.Stopwatch]::StartNew()
  $dl = Api $port $token 'POST' '/api/versions/download' @{ versionId = $VersionId } 120
  if ($dl.ok -and $dl.body -and ($dl.body.PSObject.Properties.Name -contains 'id')) { $result.downloadTaskId = [string]$dl.body.id }
  $status = ''
  while ($sw.Elapsed.TotalSeconds -lt $DownloadTimeoutSec) {
    Start-Sleep -Seconds 5
    $list = Api $port $token 'GET' '/api/downloads' $null 60
    if ($list.ok -and $list.body) {
      $tasks = $null
      foreach ($prop in @('downloads', 'tasks', 'items')) { if ($list.body.PSObject.Properties.Name -contains $prop) { $tasks = $list.body.$prop; break } }
      if ($null -eq $tasks -and $list.body -is [array]) { $tasks = $list.body }
      $mine = @($tasks | Where-Object { $_.versionId -eq $VersionId })
      if ($mine.Count -gt 0) {
        $status = [string]$mine[0].status
        $result.downloadMessage = [string]$mine[0].message
        if ($status -in @('completed', 'failed', 'cancelled', 'error')) { $result.downloadStatus = $status; break }
      }
    }
  }
  $sw.Stop()
  $result.downloadStatus = $status
  $result.downloadSeconds = [int]$sw.Elapsed.TotalSeconds

  $inst = Api $port $token 'GET' '/api/versions' $null 60
  $result.installedVersions = @($inst.text.Substring(0, [Math]::Min(600, $inst.text.Length)))

  $mc = Join-Path $cold 'data\.minecraft'
  $result.versionOnDisk = (Test-Path -LiteralPath (Join-Path $mc "versions\$VersionId"))
  $result.versionJsonFiles = @(Get-ChildItem (Join-Path $mc "versions\$VersionId") -Filter '*.json' -File -ErrorAction SilentlyContinue).Count
  $result.versionJars = @(Get-ChildItem (Join-Path $mc "versions\$VersionId") -Filter '*.jar' -File -ErrorAction SilentlyContinue).Count
  $result.libraryJars = @(Get-ChildItem (Join-Path $mc 'libraries') -Recurse -Filter '*.jar' -File -ErrorAction SilentlyContinue).Count
  $result.assetObjects = @(Get-ChildItem (Join-Path $mc 'assets\objects') -Recurse -File -ErrorAction SilentlyContinue).Count

  if ($Mode -eq 'full') {
    $ci = Api $port $token 'POST' '/api/instances' @{ name = "d6-$VersionId"; version = $VersionId } 120
    if ($ci.ok -and $ci.body -and ($ci.body.PSObject.Properties.Name -contains 'instance')) { $result.instanceId = [string]$ci.body.instance.id }
    if ($result.instanceId) {
      $pc = Api $port $token 'GET' "/api/instances/$($result.instanceId)/prelaunch-check" $null 180
      $result.prelaunchVerdict = $pc.text.Substring(0, [Math]::Min(700, $pc.text.Length))
      $lb = Api $port $token 'POST' "/api/instances/$($result.instanceId)/launch" @{} 180
      $result.launchResponse = $lb.text.Substring(0, [Math]::Min(500, $lb.text.Length))
      $swL = [Diagnostics.Stopwatch]::StartNew()
      while ($swL.Elapsed.TotalSeconds -lt 90) {
        Start-Sleep -Seconds 5
        $javaProcs = @(Get-CimInstance Win32_Process -ErrorAction SilentlyContinue | Where-Object { $_.Name -ieq 'java.exe' -or $_.Name -ieq 'javaw.exe' })
        if ($javaProcs.Count -gt 0) { $result.launchObserved = $true; break }
      }
      $swL.Stop()
    }
  }

  $logDir = Join-Path $cold 'data\logs'
  $lastLog = Get-ChildItem $logDir -Filter '*.log' -ErrorAction SilentlyContinue | Sort-Object LastWriteTime | Select-Object -Last 1
  if ($lastLog) { $result.launchLogTail = (Get-Content -LiteralPath $lastLog.FullName -Encoding UTF8 | Select-Object -Last 14) -join "`n" }
}
finally {
  if ($proc -and -not $proc.HasExited) { Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue }
  foreach ($o in (OwnedFixtureProcs $targetExe)) { Stop-Process -Id $o.ProcessId -Force -ErrorAction SilentlyContinue }
  $result.ownedProcessesStopped = (@(OwnedFixtureProcs $targetExe).Count -eq 0)
  $result | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $OutDir "install-$Mode.json") -Encoding utf8
  $result | ConvertTo-Json -Depth 6 | Write-Output
}
if (-not $result.healthReady) { exit 2 }
if ($result.downloadStatus -ne 'completed') { exit 3 }
exit 0
