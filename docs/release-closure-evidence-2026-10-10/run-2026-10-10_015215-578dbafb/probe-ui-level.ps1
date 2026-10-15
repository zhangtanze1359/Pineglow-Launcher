param(
  [Parameter(Mandatory)][string]$FixtureRoot,
  [Parameter(Mandatory)][string]$OutDir,
  [string]$Chrome = 'C:\Program Files\Google\Chrome\Application\chrome.exe',
  [int]$StartupTimeoutSec = 120,
  [int]$SettleSec = 10,
  [string]$ProbeScript = 'cdp-ui-probe.cjs',
  [string[]]$ProbeArgs = @()
)
# Round-18 probe (own tool; product source untouched).
# Drives the SHIPPED ui/ in a real Chromium engine (headless Chrome via CDP) against the
# launcher's own LocalApiService. This is the first UI-level (not API-level) verification.
$ErrorActionPreference = 'Stop'
[void](New-Item -ItemType Directory -Path $OutDir -Force)

function Get-FreePort {
  $l = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0); $l.Start()
  $p = ([Net.IPEndPoint]$l.LocalEndpoint).Port; $l.Stop(); return $p
}
function OwnedProcs([string]$exePath) {
  @(Get-CimInstance Win32_Process -ErrorAction SilentlyContinue | Where-Object { $_.ExecutablePath -and ($_.ExecutablePath -ieq $exePath) })
}

$cold = $FixtureRoot
$targetExe = Join-Path $cold 'Pineglow.Launcher.exe'
$probeScript = Join-Path $PSScriptRoot $ProbeScript
if (-not (Test-Path -LiteralPath $probeScript)) { throw "missing $probeScript" }

$apiPort = Get-FreePort
$dbgPort = Get-FreePort
$profileDir = Join-Path $env:TEMP ('pineglow-chrome-' + [guid]::NewGuid().ToString('N'))

$result = [ordered]@{
  probe = 'ui-level-cdp'; fixture = $cold; exe = $targetExe
  apiPort = $apiPort; debugPort = $dbgPort
  chrome = $Chrome; healthReady = $false
  tokenFound = $false; probeExit = -1
  probeStdout = ''; probeStderr = ''
  ownedProcessesStopped = $false
  timeHK = [TimeZoneInfo]::ConvertTimeBySystemTimeZoneId([datetime]::UtcNow, 'China Standard Time').ToString('o')
}

foreach ($item in @(Get-ChildItem Env: | Where-Object { $_.Name -like 'PINEGLOW_*' })) { Remove-Item -LiteralPath ('Env:' + $item.Name) }
$env:DOTNET_BUNDLE_EXTRACT_BASE_DIR = Join-Path $cold 'extract'

$launcher = $null
$chromeProc = $null
try {
  $env:PINEGLOW_API_PORT = "$apiPort"
  $launcher = Start-Process -FilePath $targetExe -WorkingDirectory $cold -PassThru -WindowStyle Hidden `
    -RedirectStandardOutput (Join-Path $OutDir 'ui-launcher-stdout.log') `
    -RedirectStandardError  (Join-Path $OutDir 'ui-launcher-stderr.log')

  $token = $null
  $deadline = (Get-Date).AddSeconds($StartupTimeoutSec)
  while ((Get-Date) -lt $deadline -and -not $token) {
    Start-Sleep -Milliseconds 700
    try {
      $h = Invoke-WebRequest "http://127.0.0.1:$apiPort/" -UseBasicParsing -TimeoutSec 5
      if ($h.Content -match 'name="pineglow-session-token"\s+content="([0-9A-Fa-f]+)"') { $token = $Matches[1] }
    } catch { }
  }
  if (-not $token) { throw 'launcher did not become ready (no session token in /)' }
  $result.healthReady = $true
  $result.tokenFound = $true

  $chromeArgs = @(
    '--headless=new', '--disable-gpu', '--no-first-run', '--no-default-browser-check',
    '--disable-extensions', '--disable-background-networking', '--mute-audio',
    '--window-size=1440,900',
    "--remote-debugging-port=$dbgPort", "--user-data-dir=$profileDir", 'about:blank'
  )
  $chromeProc = Start-Process -FilePath $Chrome -ArgumentList $chromeArgs -PassThru -WindowStyle Hidden

  Start-Sleep -Seconds 3
  $appUrl = "http://127.0.0.1:$apiPort/"
  $nodeOut = & node $probeScript $appUrl "$dbgPort" $OutDir @ProbeArgs 2>&1 | Out-String
  $result.probeExit = $LASTEXITCODE
  $result.probeStdout = $nodeOut
}
catch {
  $result.error = $_.Exception.Message
}
finally {
  if ($chromeProc -and -not $chromeProc.HasExited) {
    & taskkill.exe /T /F /PID $chromeProc.Id *>$null
    Start-Sleep -Seconds 2
  }
  if ($launcher -and -not $launcher.HasExited) { Stop-Process -Id $launcher.Id -Force -ErrorAction SilentlyContinue }
  foreach ($o in (OwnedProcs $targetExe)) { Stop-Process -Id $o.ProcessId -Force -ErrorAction SilentlyContinue }
  Start-Sleep -Seconds 1
  $result.ownedProcessesStopped = (@(OwnedProcs $targetExe).Count -eq 0)
  Remove-Item -LiteralPath $profileDir -Recurse -Force -ErrorAction SilentlyContinue
  $result | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $OutDir 'ui-level-summary.json') -Encoding utf8
  $result | ConvertTo-Json -Depth 6 | Write-Output
}
if ($result.probeExit -ne 0) { exit 4 }
exit 0
