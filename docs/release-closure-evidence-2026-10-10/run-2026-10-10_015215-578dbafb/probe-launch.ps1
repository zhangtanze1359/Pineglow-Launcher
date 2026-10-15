param(
  [Parameter(Mandatory)][string]$ExePath,
  [Parameter(Mandatory)][string]$FixtureRoot,
  [Parameter(Mandatory)][string]$JavaExe,
  [Parameter(Mandatory)][string]$OutDir,
  [string]$VersionId = '1.21.11',
  [string]$PlayerName = 'd6tester',
  [int]$LaunchObserveSec = 150,
  [int]$StartupTimeoutSec = 120
)
# Round-6 probe (own tool, product source untouched).
# Reuses the fixture produced by probe-install-launch.ps1 (game already installed) and drives the
# remaining real path: java path -> offline account -> instance -> prelaunch check -> launch.
# The JRE is the fixture's own (downloaded from the same Adoptium API the product uses) and lives in %TEMP%.
# Only ever stops the launcher process it started plus java processes whose executable path is that JRE.
$ErrorActionPreference = 'Stop'
[void](New-Item -ItemType Directory -Path $OutDir -Force)

function Get-FreePort {
  $l = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0); $l.Start()
  $p = ([Net.IPEndPoint]$l.LocalEndpoint).Port; $l.Stop(); return $p
}
function Read-Shared([string]$path) {
  # the running game holds latest.log open; read it with a share-all handle
  $fs = [IO.File]::Open($path, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::ReadWrite)
  try {
    $sr = New-Object IO.StreamReader($fs)
    return $sr.ReadToEnd()
  } finally { $fs.Dispose() }
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

$cold = $FixtureRoot
$targetExe = Join-Path $cold 'Pineglow.Launcher.exe'
if (-not (Test-Path -LiteralPath $targetExe)) { Copy-Item -LiteralPath $ExePath -Destination $targetExe -Force }
$cfgPath = Join-Path $cold 'data\config.json'
$cfg = Get-Content -LiteralPath $cfgPath -Raw -Encoding UTF8 | ConvertFrom-Json
$cfg | Add-Member -NotePropertyName javaPath -NotePropertyValue $JavaExe -Force
($cfg | ConvertTo-Json -Depth 8) | Set-Content -LiteralPath $cfgPath -Encoding utf8

foreach ($item in @(Get-ChildItem Env: | Where-Object { $_.Name -like 'PINEGLOW_*' })) { Remove-Item -LiteralPath ('Env:' + $item.Name) }
$env:DOTNET_BUNDLE_EXTRACT_BASE_DIR = Join-Path $cold 'extract'

$result = [ordered]@{
  probe = 'real-launch'; fixture = $cold; javaConfigured = $JavaExe; requestedVersion = $VersionId
  apiPort = 0; healthReady = $false
  javaReport = ''; offlineAccount = ''; instanceId = ''
  prelaunchVerdict = ''; prelaunchOk = $false
  launchResponse = ''; javaProcessStarted = $false; javaProcessCmdline = ''
  gameLogPath = ''; gameLogTail = ''; launcherLogTail = ''
  gameReachedMainMenu = $false
  ownedProcessesStopped = $false
  timeHK = [TimeZoneInfo]::ConvertTimeBySystemTimeZoneId([datetime]::UtcNow, 'China Standard Time').ToString('o')
}

$proc = $null
try {
  $port = Get-FreePort
  $result.apiPort = $port
  $env:PINEGLOW_API_PORT = "$port"
  $proc = Start-Process -FilePath $targetExe -WorkingDirectory $cold -PassThru -WindowStyle Hidden `
    -RedirectStandardOutput (Join-Path $OutDir 'launch-stdout.log') -RedirectStandardError (Join-Path $OutDir 'launch-stderr.log')

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

  $java = Api $port $token 'GET' '/api/java' $null 90
  $result.javaReport = $java.text.Substring(0, [Math]::Min(500, $java.text.Length))

  $acct = Api $port $token 'POST' '/api/accounts/offline' @{ userName = $PlayerName } 90
  $result.offlineAccount = $acct.text.Substring(0, [Math]::Min(400, $acct.text.Length))

  $ci = Api $port $token 'POST' '/api/instances' @{ name = "d6-launch-$VersionId"; version = $VersionId } 120
  if ($ci.ok -and $ci.body -and ($ci.body.PSObject.Properties.Name -contains 'instance')) { $result.instanceId = [string]$ci.body.instance.id }
  else { $result.launchResponse = 'instance create failed: ' + $ci.text }

  if ($result.instanceId) {
    $pc = Api $port $token 'GET' "/api/instances/$($result.instanceId)/prelaunch-check" $null 240
    $result.prelaunchVerdict = $pc.text.Substring(0, [Math]::Min(800, $pc.text.Length))
    if ($pc.ok -and $pc.body -and ($pc.body.PSObject.Properties.Name -contains 'ok')) { $result.prelaunchOk = [bool]$pc.body.ok }

    $lb = Api $port $token 'POST' "/api/instances/$($result.instanceId)/launch" @{} 240
    $result.launchResponse = $lb.text.Substring(0, [Math]::Min(700, $lb.text.Length))

    $sw = [Diagnostics.Stopwatch]::StartNew()
    while ($sw.Elapsed.TotalSeconds -lt $LaunchObserveSec) {
      Start-Sleep -Seconds 5
      $jp = @(Get-CimInstance Win32_Process -ErrorAction SilentlyContinue |
              Where-Object { $_.ExecutablePath -and ($_.ExecutablePath -ieq $JavaExe) })
      if ($jp.Count -gt 0 -and -not $result.javaProcessStarted) {
        $result.javaProcessStarted = $true
        $result.javaProcessCmdline = ("" + $jp[0].CommandLine).Substring(0, [Math]::Min(600, ("" + $jp[0].CommandLine).Length))
      }
      $latest = Get-ChildItem $cold -Recurse -File -Filter 'latest.log' -ErrorAction SilentlyContinue | Sort-Object LastWriteTime | Select-Object -Last 1
      if ($latest) {
        $result.gameLogPath = $latest.FullName
        try {
          $txt = Read-Shared $latest.FullName
          if ($txt -match 'Sound engine started|Created: .*minecraft:textures|OpenAL initialized|LWJGL Version') { $result.gameReachedMainMenu = $true; break }
        } catch { }
      }
      if ($result.javaProcessStarted -and -not (Get-CimInstance Win32_Process -ErrorAction SilentlyContinue | Where-Object { $_.ExecutablePath -and ($_.ExecutablePath -ieq $JavaExe) })) { break }
    }
    $sw.Stop()
  }

  if ($result.gameLogPath -and (Test-Path -LiteralPath $result.gameLogPath)) {
    try { $result.gameLogTail = ((Read-Shared $result.gameLogPath) -split "`r?`n" | Select-Object -Last 30) -join "`n" } catch { }
  }
  $logDir = Join-Path $cold 'data\logs'
  $lastLog = Get-ChildItem $logDir -Filter '*.log' -ErrorAction SilentlyContinue | Sort-Object LastWriteTime | Select-Object -Last 1
  if ($lastLog) { $result.launcherLogTail = ((Read-Shared $lastLog.FullName) -split "`r?`n" | Select-Object -Last 22) -join "`n" }
}
finally {
  if ($proc -and -not $proc.HasExited) { Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue }
  $javaLeft = @(Get-CimInstance Win32_Process -ErrorAction SilentlyContinue | Where-Object { $_.ExecutablePath -and ($_.ExecutablePath -ieq $JavaExe) })
  foreach ($j in $javaLeft) { Stop-Process -Id $j.ProcessId -Force -ErrorAction SilentlyContinue }
  $stillLauncher = @(Get-CimInstance Win32_Process -ErrorAction SilentlyContinue | Where-Object { $_.ExecutablePath -and ($_.ExecutablePath -ieq $targetExe) })
  foreach ($o in $stillLauncher) { Stop-Process -Id $o.ProcessId -Force -ErrorAction SilentlyContinue }
  $result.ownedProcessesStopped = (@(Get-CimInstance Win32_Process -ErrorAction SilentlyContinue |
    Where-Object { $_.ExecutablePath -and (($_.ExecutablePath -ieq $targetExe) -or ($_.ExecutablePath -ieq $JavaExe)) }).Count -eq 0)
  $result | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $OutDir 'launch.json') -Encoding utf8
  $result | ConvertTo-Json -Depth 6 | Write-Output
}
if (-not $result.healthReady) { exit 2 }
if (-not $result.javaProcessStarted) { exit 3 }
exit 0
