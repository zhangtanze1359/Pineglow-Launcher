param(
  [Parameter(Mandatory)][string]$FixtureRoot,
  [Parameter(Mandatory)][string]$OutDir,
  [Parameter(Mandatory)][string]$JavaExe,
  [Parameter(Mandatory)][array]$Entries,   # @(@{loader='fabric';mc='1.21.11';dep='fabric-loader';ver='0.19.5'}, ...)
  [string]$Proxy = '',                      # 'host:port' -> written into the product's own proxy settings
  [string]$JavaToolOptions = '',            # inherited by every child JVM (this is how the loader INSTALLER gets a proxy)
  [int]$ImportTimeoutSec = 1200,
  [int]$LaunchObserveSec = 180,
  [int]$StartupTimeoutSec = 120
)
# Round-7 probe (own tool, product source untouched).
# Modpack import is the ONLY route in the shipped product that installs a loader, so this probe drives it
# with a minimal synthetic .mrpack (files = [] -> no mods to download) whose dependencies declare the loader.
# For each loader it records: import result, instance loader fields, prelaunch check, launch, and whether the
# client reached the title screen.
$ErrorActionPreference = 'Stop'
[void](New-Item -ItemType Directory -Path $OutDir -Force)
Add-Type -AssemblyName System.IO.Compression.FileSystem

function Get-FreePort {
  $l = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0); $l.Start()
  $p = ([Net.IPEndPoint]$l.LocalEndpoint).Port; $l.Stop(); return $p
}
function Read-Shared([string]$path) {
  $fs = [IO.File]::Open($path, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::ReadWrite)
  try { $sr = New-Object IO.StreamReader($fs); return $sr.ReadToEnd() } finally { $fs.Dispose() }
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
function New-MinimalMrpack([string]$dir, [string]$name, [string]$mc, [string]$depKey, [string]$depVer) {
  $index = [ordered]@{
    formatVersion = 1; game = 'minecraft'; versionId = '1.0.0'; name = $name
    summary = 'd7 loader reachability probe (empty file list)'
    files = @()
    dependencies = [ordered]@{ minecraft = $mc; $depKey = $depVer }
  }
  $packDir = Join-Path $dir ('pack-' + [guid]::NewGuid().ToString('N'))
  [void](New-Item -ItemType Directory -Path $packDir -Force)
  ($index | ConvertTo-Json -Depth 6) | Set-Content -LiteralPath (Join-Path $packDir 'modrinth.index.json') -Encoding utf8
  $zip = Join-Path $dir ((($name -replace '[^A-Za-z0-9]+', '-')) + '.mrpack')
  if (Test-Path -LiteralPath $zip) { Remove-Item -LiteralPath $zip -Force }
  [IO.Compression.ZipFile]::CreateFromDirectory($packDir, $zip)
  return $zip
}

$cold = $FixtureRoot
$targetExe = Join-Path $cold 'Pineglow.Launcher.exe'
$cfgPath = Join-Path $cold 'data\config.json'
foreach ($item in @(Get-ChildItem Env: | Where-Object { $_.Name -like 'PINEGLOW_*' })) { Remove-Item -LiteralPath ('Env:' + $item.Name) }
$env:DOTNET_BUNDLE_EXTRACT_BASE_DIR = Join-Path $cold 'extract'

# two independently switchable knobs, because the launcher's own HTTP client and the loader INSTALLER
# subprocess are separate JVMs/processes with separate proxy behaviour:
#   -Proxy           -> product setting (affects ApiHttp: metadata, mod downloads)
#   -JavaToolOptions -> inherited by child JVMs (affects the Forge/NeoForge installer)
if ($Proxy) {
  $parts = $Proxy.Split(':')
  $cfgObj = Get-Content -LiteralPath $cfgPath -Raw -Encoding UTF8 | ConvertFrom-Json
  $cfgObj | Add-Member -NotePropertyName proxyEnabled -NotePropertyValue $true -Force
  $cfgObj | Add-Member -NotePropertyName proxyHost -NotePropertyValue $parts[0] -Force
  $cfgObj | Add-Member -NotePropertyName proxyPort -NotePropertyValue ([int]$parts[1]) -Force
  ($cfgObj | ConvertTo-Json -Depth 8) | Set-Content -LiteralPath $cfgPath -Encoding utf8
}
if ($JavaToolOptions) { $env:JAVA_TOOL_OPTIONS = $JavaToolOptions }

$result = [ordered]@{
  probe = 'loader-matrix-via-modpack-import'; fixture = $cold
  proxyKnob = $Proxy; javaToolOptionsKnob = $JavaToolOptions
  apiPort = 0; healthReady = $false; rows = @()
  timeHK = [TimeZoneInfo]::ConvertTimeBySystemTimeZoneId([datetime]::UtcNow, 'China Standard Time').ToString('o')
}

$proc = $null
try {
  $port = Get-FreePort
  $result.apiPort = $port
  $env:PINEGLOW_API_PORT = "$port"
  $proc = Start-Process -FilePath $targetExe -WorkingDirectory $cold -PassThru -WindowStyle Hidden `
    -RedirectStandardOutput (Join-Path $OutDir 'matrix-stdout.log') -RedirectStandardError (Join-Path $OutDir 'matrix-stderr.log')
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

  $rows = New-Object System.Collections.Generic.List[object]
  foreach ($e in $Entries) {
    $row = [ordered]@{
      loader = $e.loader; mc = $e.mc; dep = $e.dep; depVersion = $e.ver
      importOk = $false; instanceId = ''; importSeconds = 0; importResponse = ''
      instanceLoader = ''; instanceVersion = ''; loaderProfileOnDisk = $false
      prelaunchOk = $false; prelaunchVerdict = ''; launchResponse = ''
      javaStarted = $false; reachedTitle = $false; gameLogMarker = ''; gameLogPath = ''; error = ''
    }
    try {
      $zip = New-MinimalMrpack -dir $OutDir -name ("d7-" + $e.loader) -mc $e.mc -depKey $e.dep -depVer $e.ver
      $b64 = [Convert]::ToBase64String([IO.File]::ReadAllBytes($zip))
      $swI = [Diagnostics.Stopwatch]::StartNew()
      $imp = Api $port $token 'POST' '/api/instances/import' @{ fileName = [IO.Path]::GetFileName($zip); fileBase64 = $b64 } $ImportTimeoutSec
      $swI.Stop()
      $row.importSeconds = [int]$swI.Elapsed.TotalSeconds
      $row.importResponse = $imp.text.Substring(0, [Math]::Min(400, $imp.text.Length))
      $row.importOk = $imp.ok
      if ($imp.ok -and $imp.body -and ($imp.body.PSObject.Properties.Name -contains 'instanceId')) { $row.instanceId = [string]$imp.body.instanceId }

      if ($row.instanceId) {
        $det = Api $port $token 'GET' "/api/instances/$($row.instanceId)" $null 60
        if ($det.ok -and $det.body) {
          $inst = $det.body
          if ($inst.PSObject.Properties.Name -contains 'instance') { $inst = $inst.instance }
          foreach ($p in @('loader', 'loaderType')) { if ($inst.PSObject.Properties.Name -contains $p) { $row.instanceLoader = [string]$inst.$p } }
          foreach ($p in @('version', 'minecraftVersion')) { if ($inst.PSObject.Properties.Name -contains $p) { $row.instanceVersion = [string]$inst.$p } }
        }
        $pc = Api $port $token 'GET' "/api/instances/$($row.instanceId)/prelaunch-check" $null 240
        $row.prelaunchVerdict = $pc.text.Substring(0, [Math]::Min(600, $pc.text.Length))
        if ($pc.ok -and $pc.body -and ($pc.body.PSObject.Properties.Name -contains 'ok')) { $row.prelaunchOk = [bool]$pc.body.ok }

        $mcDir = Join-Path $cold 'data\.minecraft'
        $mark = @(Get-ChildItem (Join-Path $mcDir 'versions') -Directory -ErrorAction SilentlyContinue | Where-Object { $_.Name -like "*$($e.loader)*" })
        $row.loaderProfileOnDisk = ($mark.Count -gt 0)

        # NOTE: an imported instance gets its OWN game directory (<mc>/instances/<name>), so the game log is
        # NOT <mc>/logs/latest.log. Search the whole fixture and keep the newest log as the reference point.
        $findLog = { Get-ChildItem $cold -Recurse -File -Filter 'latest.log' -ErrorAction SilentlyContinue | Sort-Object LastWriteTime | Select-Object -Last 1 }
        $logBefore = & $findLog
        $beforeWrite = if ($logBefore) { $logBefore.LastWriteTimeUtc } else { [datetime]::MinValue }
        $lb = Api $port $token 'POST' "/api/instances/$($row.instanceId)/launch" @{} 240
        $row.launchResponse = $lb.text.Substring(0, [Math]::Min(300, $lb.text.Length))
        $swL = [Diagnostics.Stopwatch]::StartNew()
        while ($swL.Elapsed.TotalSeconds -lt $LaunchObserveSec) {
          Start-Sleep -Seconds 5
          $jp = @(Get-CimInstance Win32_Process -ErrorAction SilentlyContinue | Where-Object { $_.ExecutablePath -and ($_.ExecutablePath -ieq $JavaExe) })
          if ($jp.Count -gt 0) { $row.javaStarted = $true }
          $logNow = & $findLog
          if ($logNow -and $logNow.LastWriteTimeUtc -gt $beforeWrite) {
            try {
              $txt = Read-Shared $logNow.FullName
              foreach ($m in @('Sound engine started', 'Reloading ResourceManager', 'LWJGL Version', 'OpenAL initialized')) {
                if ($txt -match [regex]::Escape($m)) { $row.reachedTitle = $true; $row.gameLogMarker = $m; $row.gameLogPath = $logNow.FullName; break }
              }
            } catch { }
          }
          if ($row.reachedTitle) { break }
          if ($row.javaStarted -and -not (Get-CimInstance Win32_Process -ErrorAction SilentlyContinue | Where-Object { $_.ExecutablePath -and ($_.ExecutablePath -ieq $JavaExe) })) { break }
        }
        $swL.Stop()
        $logNow2 = & $findLog
        if ($logNow2) { $row.gameLogPath = $logNow2.FullName; Copy-Item -LiteralPath $logNow2.FullName -Destination (Join-Path $OutDir ("latest-$($e.loader).log")) -Force -ErrorAction SilentlyContinue }
      }
    }
    catch { $row.error = $_.Exception.Message }
    finally {
      foreach ($j in @(Get-CimInstance Win32_Process -ErrorAction SilentlyContinue | Where-Object { $_.ExecutablePath -and ($_.ExecutablePath -ieq $JavaExe) })) {
        Stop-Process -Id $j.ProcessId -Force -ErrorAction SilentlyContinue
      }
      Start-Sleep -Seconds 3
    }
    $rows.Add([pscustomobject]$row)
    $result.rows = $rows.ToArray()
    $result | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $OutDir 'loader-matrix.json') -Encoding utf8
  }
}
finally {
  if ($proc -and -not $proc.HasExited) { Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue }
  foreach ($o in @(Get-CimInstance Win32_Process -ErrorAction SilentlyContinue | Where-Object { $_.ExecutablePath -and ($_.ExecutablePath -ieq $targetExe) })) { Stop-Process -Id $o.ProcessId -Force -ErrorAction SilentlyContinue }
  foreach ($j in @(Get-CimInstance Win32_Process -ErrorAction SilentlyContinue | Where-Object { $_.ExecutablePath -and ($_.ExecutablePath -ieq $JavaExe) })) { Stop-Process -Id $j.ProcessId -Force -ErrorAction SilentlyContinue }
  $result.rows = @($rows.ToArray())
  $result | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $OutDir 'loader-matrix.json') -Encoding utf8
  $result | ConvertTo-Json -Depth 6 | Write-Output
}
if (-not $result.healthReady) { exit 2 }
exit 0
